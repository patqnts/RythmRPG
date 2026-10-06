using System;
using System.Collections.Generic;
using PrimeTween;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// The note / projectile component of the Note Kit. One component for every kind of note: its <see cref="Kind"/>
    /// (Tap, Hold, Stationary, Stationary Hold, Mash, Pong) is data and decides timing and judgement; its looks are a
    /// stack of <see cref="NoteView"/>s (animator, spawned effects, a chain or beam between two points, the enemy itself
    /// moving and striking...) plus any custom script implementing <see cref="INoteViewListener"/>. Create and edit them
    /// with Tools > Rythm RPG > Combat > Note Designer, which also converts the old note prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Combat/Combat Note")]
    public sealed class CombatNote : Note, ILaneAnticipation, IRallyShot
    {
        [Serializable]
        public sealed class StationarySettings
        {
            [Tooltip("Snap to the lane's X when it appears (the Lane Position Offset is added after).")]
            public bool alignToLane = true;
            public Vector3 lanePositionOffset;
            [Tooltip("Presses after the beat are graded too (by the same windows), until the Bad window has passed.")]
            public bool allowLatePress = true;
            [Tooltip("Show the charge on the lane's key marker (outline for Stationary, fill for Stationary Hold).")]
            public bool keyMarkerCharge = true;
        }

        [Serializable]
        public sealed class HoldSettings
        {
            [Tooltip("Stationary Hold: letting go early counts as Bad and hurts like a miss would.")]
            public bool damageOnEarlyRelease = true;
            [Tooltip("Hold: once pressed, the note stays on the hit line while it is held.")]
            public bool pinToHitLine = true;
            [Tooltip("Holding effect, completion / head-burst effects and the key marker's fill colour.")]
            public HoldFxSettings fx = new();
        }

        [Serializable]
        public sealed class MashSettings
        {
            [Tooltip("Presses needed when the chart does not set them.")]
            [Min(1)] public int fallbackPresses = MashRules.DefaultRequiredPresses;
            [Tooltip("Clear within this share of the travel time for Perfect.")]
            [Range(0f, 1f)] public float perfectProgress = MashRules.DefaultPerfectProgress;
            [Tooltip("Clear within this share for Good; later but before the line is Bad.")]
            [Range(0f, 1f)] public float goodProgress = MashRules.DefaultGoodProgress;
        }

        [Tooltip("How the note moves, is judged and resolves. The chart note type should match (Note Designer shows it).")]
        [SerializeField] private NoteKind kind = NoteKind.Tap;
        [SerializeField] private StationarySettings stationary = new();
        [SerializeField] private HoldSettings hold = new();
        [SerializeField] private MashSettings mash = new();
        [Tooltip("Seconds the note stays after it resolves (hit / miss animations). Views that need longer extend it.")]
        [SerializeField, Min(0f)] private float lingerSeconds = 0.25f;
        [Tooltip("The note's looks. They stack: each reacts to the note's moments (spawned, beat, pressed, hold, hit, miss...).")]
        [SerializeReference, SubclassSelector] private List<NoteView> views = new();

        private readonly NoteViewContext context = new();
        private readonly NoteCueScheduler cues = new();
        private readonly List<INoteViewListener> listeners = new();
        private readonly HoldFeedback holdFeedback = new();
        private RhythmPatternRunner noteRunner;
        private float spawnRealTime;
        private bool viewsBegun;
        private bool reachedBeat;
        private bool reachedHoldEnd;

        // Travel (moving kinds): positioned from the audio clock while a chart runs.
        private bool travelStarted;
        private int travelLane;
        private bool clockDriven;
        private bool restartFromNow;
        private Transform clockSpace;
        private Vector3 clockStartLocal;
        private Vector3 clockKeyLocal;
        private Vector3 clockTargetLocal;
        private float clockDuration;
        private double clockStartSeconds;
        private bool hasLane;
        private Transform laneSpace;
        private Vector3 laneLineLocal;
        private Vector3 laneUpLocal;

        // Hold
        private bool holding;
        private bool pinned;
        private bool earlyReleased;
        private double pressedAt;
        private float pressedRealTime;

        // Mash
        private int presses;
        private int requiredPresses = MashRules.DefaultRequiredPresses;
        private bool landed;
        private bool finished;

        // Pong
        private bool deflecting;
        private bool keepForNextVolley;

        public NoteKind Kind { get => kind; set => kind = value; }
        public StationarySettings Stationary => stationary ??= new StationarySettings();
        public HoldSettings Hold => hold ??= new HoldSettings();
        public MashSettings Mash => mash ??= new MashSettings();
        public float LingerSeconds { get => lingerSeconds; set => lingerSeconds = Mathf.Max(0f, value); }
        public List<NoteView> Views => views ??= new List<NoteView>();
        public NoteViewContext Context => context;
        /// <summary>The chart note type this note should be authored as.</summary>
        public RhythmNoteType ChartNoteType => NoteKinds.ChartType(kind);
        public bool ClearableByEffects => NoteKinds.ClearableByEffects(kind);
        public bool IsMash => kind == NoteKind.Mash;

        /// <summary>Chart seconds now: the audio clock while a chart runs, otherwise time since spawn on the chart's scale.</summary>
        public double NowSeconds => UsesChartClock ? ChartSeconds : context.SpawnTime + (Time.time - spawnRealTime);
        public bool IsHolding => holding;
        public bool IsPinned => pinned;
        public bool EarlyReleased => earlyReleased;
        public int Presses => presses;
        public int RequiredPresses => requiredPresses;
        /// <summary>World units per second along the lane (moving kinds).</summary>
        public float TravelWorldSpeed => TravelSpeed;
        /// <summary>Hold notes: the hold's length in world units along the lane (its tail).</summary>
        public float HoldLengthWorld => Mathf.Max(0f, (float)(context.EndTime - context.HitTime)) * TravelSpeed;
        /// <summary>Direction up the lane (from the hit line toward the spawn), for tails.</summary>
        public bool TryGetLaneUp(out Vector3 up)
        {
            up = default;
            if (!hasLane) return false;
            up = FromMovementLocal(laneUpLocal, laneSpace) - FromMovementLocal(laneLineLocal, laneSpace);
            return up.sqrMagnitude > 0.000001f;
        }
        public Vector3 MarkerPosition
        {
            get
            {
                RhythmLaneTarget target = GetLaneTarget();
                return target != null ? target.transform.position : transform.position;
            }
        }

        // ---------- spawn ----------

        public override bool ShouldAutoMissByPosition => kind == NoteKind.Tap || kind == NoteKind.Pong || kind == NoteKind.Hold;
        public override bool ShouldResolveMissOnPlayerInput => NoteKinds.IsStationary(kind);
        protected override bool ResolveImmediatelyOnPress => kind == NoteKind.Tap || kind == NoteKind.Pong || kind == NoteKind.Stationary;

        public override void Initialize(RhythmNoteSpawnContext spawn)
        {
            base.Initialize(spawn);
            noteRunner = spawn.Runner;
            keys = spawn.Keys;
            spawnRealTime = Time.time;
            ResetRuntime();
            isMoving = NoteKinds.Moves(kind);
            int fromData = spawn.NoteData != null ? spawn.NoteData.MashRequiredPresses : 0;
            requiredPresses = kind == NoteKind.Mash && fromData > 0 ? fromData : Mathf.Max(1, Mash.fallbackPresses);
            if (NoteKinds.IsStationary(kind)) AlignToLane();
            BuildContext();
            if (NoteKinds.IsStationary(kind) && Stationary.keyMarkerCharge) LaneAnticipation.Register(this);
            BeginViews();
        }

        private void ResetRuntime()
        {
            reachedBeat = false;
            reachedHoldEnd = false;
            travelStarted = false;
            clockDriven = false;
            restartFromNow = false;
            hasLane = false;
            holding = false;
            pinned = false;
            earlyReleased = false;
            presses = 0;
            landed = false;
            finished = false;
            context.Resolved = false;
            context.Outcome = NoteOutcome.None;
            context.Judgement = HitJudgement.Miss;
        }

        private void BuildContext()
        {
            context.Note = this;
            context.Projectile = null; // a Travel view sets it in Begin
            context.Scheduler = cues;
            PatternRunContext run = noteRunner != null ? noteRunner.CurrentContext : default;
            context.Mode = run.Mode;
            RhythmLaneTarget target = GetLaneTarget();
            context.HitPoint = target != null ? target.transform : null;
            // The runner lives on the enemy of this encounter (enemy attacks spawn from it; ability charts use it too).
            EnemyCombatant enemy = noteRunner != null ? noteRunner.GetComponentInParent<EnemyCombatant>() : null;
            Transform enemyTransform = enemy != null ? enemy.transform : run.SpawnOrigin;
            Transform player = run.Player != null ? run.Player.transform : null;
            bool enemyAttack = run.Mode == PatternRunMode.EnemyDefense;
            context.Source = enemyAttack ? enemyTransform : player;
            context.Target = enemyAttack ? player : enemyTransform;
            context.SpawnPoint = transform.position;
            context.LaneForward = noteRunner != null ? noteRunner.LaneForward(GetNoteIdentity()) : Vector3.forward;
            context.Camera = HoldFeedback.ViewCamera();
            double travel = Data != null ? Math.Max(0d, Data.TravelTime) : 2.5d;
            context.SpawnTime = Data != null ? Data.SpawnTime : 0d;
            context.HitTime = Data != null ? Data.HitTime : context.SpawnTime + travel;
            context.EndTime = Data != null ? Data.EndTime : context.HitTime;
            context.Color = ProjectileColor.Resolve(this, TailRenderers(), new Color(1f, 1f, 1f, 0f), Color.white);
        }

        private void BeginViews()
        {
            listeners.Clear();
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is INoteViewListener listener && !ReferenceEquals(behaviour, this)) listeners.Add(listener);
            viewsBegun = true;
            cues.Clear();
            foreach (NoteView view in Views)
            {
                if (view == null || !view.Enabled) continue;
                try { view.Begin(context); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            Emit(NoteMoment.Spawned);
            TickCues();
        }

        private void TickCues() =>
            cues.Tick(NowSeconds, context.SpawnTime, context.HitTime, context.EndTime, Time.time);

        private void EndViews()
        {
            if (!viewsBegun) return;
            viewsBegun = false;
            foreach (NoteView view in Views)
            {
                if (view == null || !view.Enabled) continue;
                try { view.End(context); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }

        /// <summary>Tells every view and listener about a moment.</summary>
        public void Emit(NoteMoment moment)
        {
            if (!viewsBegun || moment == NoteMoment.None) return;
            cues.OnMoment(moment, Time.time);
            foreach (NoteView view in Views)
            {
                if (view == null || !view.Enabled) continue;
                try { view.Moment(context, moment); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                INoteViewListener listener = listeners[i];
                if (listener == null || (listener is UnityEngine.Object o && o == null))
                {
                    listeners.RemoveAt(i);
                    continue;
                }
                try { listener.OnNoteMoment(context, moment); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }

        /// <summary>Lets a spawned effect's custom scripts follow this note (Effect View does this).</summary>
        public void AddListener(INoteViewListener listener)
        {
            if (listener != null && !listeners.Contains(listener)) listeners.Add(listener);
        }

        public void RemoveListener(INoteViewListener listener) => listeners.Remove(listener);

        private void TickViews()
        {
            if (!viewsBegun) return;
            foreach (NoteView view in Views)
            {
                if (view == null || !view.Enabled) continue;
                try { view.Tick(context); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                INoteViewListener listener = listeners[i];
                if (listener == null || (listener is UnityEngine.Object o && o == null))
                {
                    listeners.RemoveAt(i);
                    continue;
                }
                try { listener.OnNoteTick(context); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }

        // ---------- every frame ----------

        private void Update()
        {
            if (!viewsBegun) return;
            if (!IsResolved && !deflecting)
            {
                switch (kind)
                {
                    case NoteKind.Tap:
                    case NoteKind.Pong:
                        Travel();
                        break;
                    case NoteKind.Hold:
                        if (holding) TickMovingHold();
                        else Travel();
                        break;
                    case NoteKind.Stationary:
                        TickStationary();
                        break;
                    case NoteKind.StationaryHold:
                        TickStationary();
                        if (holding) TickStationaryHold();
                        break;
                    case NoteKind.Mash:
                        TickMash();
                        break;
                }
            }

            if (!reachedBeat && NowSeconds >= context.HitTime)
            {
                reachedBeat = true;
                Emit(NoteMoment.ReachedBeat);
            }
            if (!reachedHoldEnd && NowSeconds >= Math.Max(context.HitTime, context.EndTime))
            {
                reachedHoldEnd = true;
                Emit(NoteMoment.HoldEnd);
            }
            TickCues();
            TickViews();
        }

        // ---------- travel (Tap, Hold, Mash, Pong) ----------

        private void Travel()
        {
            if (!isMoving) return;
            if (ShouldWaitForInitializeMovement()) return;
            EnsureTravel();
            if (clockDriven && UsesChartClock)
            {
                float elapsed = Mathf.Max(0f, (float)(ChartSeconds - clockStartSeconds));
                transform.position = EvaluateLaneTravel(clockSpace, clockStartLocal, clockKeyLocal, clockTargetLocal, elapsed, clockDuration);
            }
        }

        // Reaches the hit line exactly Travel Time after it starts, measured along the lane (see Note.TryPlanLaneTravel).
        private void EnsureTravel()
        {
            int lane = GetNoteIdentity();
            if (travelStarted && travelLane == lane) return;
            float travelTime = Mathf.Max(0.01f, (float)(Data?.TravelTime ?? 2.5d));
            if (!TryPlanLaneTravel(lane, travelTime, 3f, out Transform space, out Vector3 startLocal, out Vector3 keyLocal,
                    out Vector3 targetLocal, out float laneSpeed, out float totalDuration)) return;

            StopMovementTweens();
            travelLane = lane;
            travelStarted = true;
            SetTravelSpeed(laneSpeed);
            RememberLane(space, startLocal, keyLocal);
            if (UsesChartClock)
            {
                clockDriven = true;
                clockSpace = space;
                clockStartLocal = startLocal;
                clockKeyLocal = keyLocal;
                clockTargetLocal = targetLocal;
                clockDuration = Mathf.Max(0.01f, totalDuration);
                // First start: due at SpawnTime, so a late spawn catches up. After an intro movement: from now.
                clockStartSeconds = restartFromNow ? ChartSeconds : Data.SpawnTime;
                restartFromNow = false;
                return;
            }
            TweenLaneTravel(space, startLocal, keyLocal, targetLocal, totalDuration);
        }

        protected override void OnInitializeMovementComplete()
        {
            restartFromNow = true;
            travelStarted = false;
            clockDriven = false;
            StopMovementTweens();
            if (isMoving) EnsureTravel();
        }

        private void RememberLane(Transform space, Vector3 startLocal, Vector3 keyLocal)
        {
            Vector3 laneStart = startLocal;
            laneStart.x = keyLocal.x;
            if ((laneStart - keyLocal).sqrMagnitude <= 0.000001f) laneStart = startLocal;
            if ((laneStart - keyLocal).sqrMagnitude <= 0.000001f) return;
            laneSpace = space;
            laneLineLocal = keyLocal;
            laneUpLocal = laneStart;
            hasLane = true;
        }

        // ---------- judgement ----------

        public override Vector3 GetJudgementWorldPosition() =>
            NoteKinds.IsStationary(kind) || kind == NoteKind.Mash || holding ? MarkerPosition : transform.position;

        public override float GetTimingError(KeyButton keyButton)
        {
            if (NoteKinds.IsStationary(kind)) return Mathf.Abs((float)(context.HitTime - NowSeconds));
            if (kind == NoteKind.Mash)
            {
                RhythmLaneTarget target = GetLaneTarget();
                return target != null ? DistanceToSeconds(target.GetTimingDistance(transform.position)) : 0f;
            }
            return base.GetTimingError(keyButton);
        }

        public override HitJudgement AdjustJudgement(HitJudgement judgement, float timingError)
        {
            if (kind == NoteKind.Mash) return MashPressable ? HitJudgement.Perfect : HitJudgement.Miss;
            if (!NoteKinds.IsStationary(kind)) return judgement;
            if (PassedLatePressWindow) return HitJudgement.Miss;
            GetStationaryWindows(out float perfect, out float good, out float bad);
            if (timingError <= perfect) return HitJudgement.Perfect;
            if (timingError <= good) return HitJudgement.Good;
            if (timingError <= bad) return HitJudgement.Bad;
            return HitJudgement.Miss;
        }

        public override bool CanReceiveHit(KeyButton keyButton)
        {
            if (deflecting) return false;
            if (kind == NoteKind.Mash)
                return isActiveAndEnabled && !IsResolved && MashPressable && keyButton != null && keyButton.GetInteractable()
                       && keyButton.keyIdentity == GetNoteIdentity();
            return base.CanReceiveHit(keyButton);
        }

        protected override bool IsWithinPressWindow(KeyButton keyButton) =>
            NoteKinds.IsStationary(kind) ? NowSeconds <= context.HitTime + LatePressWindow : base.IsWithinPressWindow(keyButton);

        public override bool IsUsingKey(KeyButton keyButton)
        {
            if (kind == NoteKind.Mash) return false;
            if (NoteKinds.IsHold(kind)) return holding && base.IsUsingKey(keyButton);
            return base.IsUsingKey(keyButton);
        }

        public override bool ShouldDamagePlayerOnResolve(RhythmJudgementResult result) =>
            base.ShouldDamagePlayerOnResolve(result)
            || (kind == NoteKind.StationaryHold && Hold.damageOnEarlyRelease && earlyReleased && result.Judgement == HitJudgement.Bad);

        protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
        {
            switch (kind)
            {
                case NoteKind.Hold:
                    BeginMovingHold();
                    return;
                case NoteKind.StationaryHold:
                    BeginStationaryHold();
                    return;
                case NoteKind.Mash:
                    MashPress();
                    return;
                default:
                    Emit(NoteMoment.Pressed);
                    base.OnHit(keyButton, result);
                    return;
            }
        }

        public override void OnKeyReleased(KeyButton keyButton)
        {
            if (!holding || IsResolved) return;
            holding = false;
            earlyReleased = true;
            holdFeedback.End(false, MarkerPosition);
            Emit(NoteMoment.HoldReleased);
            if (kind == NoteKind.StationaryHold)
            {
                RhythmJudgementResult press = GetPressJudgement();
                ForceResolve(new RhythmJudgementResult(press.NoteId, press.LaneId, HitJudgement.Bad, press.Distance,
                    press.WorldPosition, NoteResolutionSource.PlayerInput));
            }
            else
            {
                ForceMiss(keyButton, NoteResolutionSource.PlayerInput);
            }
        }

        // ---------- stationary ----------

        private void AlignToLane()
        {
            if (!Stationary.alignToLane) return;
            RhythmLaneTarget target = GetLaneTarget();
            Transform lane = target != null ? target.transform : GetIdentityButton()?.transform;
            if (lane == null) return;
            Vector3 position = transform.position;
            position.x = lane.position.x;
            transform.position = position + Stationary.lanePositionOffset;
        }

        private void GetStationaryWindows(out float perfect, out float good, out float bad)
        {
            perfect = Mathf.Max(0f, Data != null ? Data.StationaryPerfectWindow : 0.15f);
            good = Mathf.Max(0f, Data != null ? Data.StationaryGoodWindow : 0.45f);
            bad = Mathf.Max(0f, Data != null ? Data.StationaryBadWindow : 0.9f);
        }

        /// <summary>How long after the beat a press still counts.</summary>
        private float LatePressWindow
        {
            get
            {
                GetStationaryWindows(out _, out _, out float bad);
                float window = Stationary.allowLatePress ? bad : 0f;
                if (kind == NoteKind.StationaryHold)
                    window = Mathf.Min(window, Mathf.Max(0f, (float)(context.EndTime - context.HitTime)) * 0.5f);
                return window;
            }
        }

        private bool PassedLatePressWindow => NowSeconds > context.HitTime + LatePressWindow;

        private void TickStationary()
        {
            if (!hitAccepted && PassedLatePressWindow) ForceMiss(GetIdentityButton());
        }

        // ---------- holds ----------

        private void BeginMovingHold()
        {
            holding = true;
            pressedAt = NowSeconds;
            pressedRealTime = Time.time;
            isMoving = false;
            StopMovementTweens();
            RhythmLaneTarget target = GetLaneTarget();
            Color color = ProjectileColor.Resolve(this, TailRenderers(), Hold.fx.markerFillColor, new Color(1f, 0.85f, 0.35f, 1f));
            context.Color = color;
            if (Hold.pinToHitLine && target != null)
            {
                pinned = true;
                KeepPinned();
            }
            holdFeedback.Begin(this, GetNoteIdentity(), target != null ? target.transform : null, MarkerPosition, Hold.fx, color);
            Emit(NoteMoment.Pressed);
            Emit(NoteMoment.HoldStarted);
        }

        private void TickMovingHold()
        {
            if (activeKeyButton != null && !activeKeyButton.GetInteractable()) return;
            KeepPinned();
            bool done = UsesChartClock
                ? ChartSeconds >= context.EndTime
                : Time.time - pressedRealTime >= Mathf.Max(0.01f, (float)(context.EndTime - pressedAt));
            if (!done) return;
            holding = false;
            holdFeedback.End(true, MarkerPosition);
            Emit(NoteMoment.HoldCompleted);
            CompleteHeldHit();
        }

        // The note sits on the lane target (re-applied every frame: the lane targets follow the camera).
        private void KeepPinned()
        {
            if (!pinned) return;
            RhythmLaneTarget target = GetLaneTarget();
            if (target != null) transform.position = target.transform.position;
        }

        private void BeginStationaryHold()
        {
            holding = true;
            pressedAt = NowSeconds;
            pressedRealTime = Time.time;
            RhythmLaneTarget target = GetLaneTarget();
            Color color = ProjectileColor.Resolve(this, null, Hold.fx.markerFillColor, new Color(0.55f, 0.85f, 1f, 1f));
            context.Color = color;
            holdFeedback.Begin(this, GetNoteIdentity(), target != null ? target.transform : null, MarkerPosition, Hold.fx, color);
            Emit(NoteMoment.Pressed);
            Emit(NoteMoment.HoldStarted);
        }

        private void TickStationaryHold()
        {
            bool done = UsesChartClock
                ? ChartSeconds >= context.EndTime
                : Time.time - pressedRealTime >= Mathf.Max(0.01f, (float)(context.EndTime - pressedAt));
            if (!done) return;
            holding = false;
            holdFeedback.End(true, MarkerPosition);
            Emit(NoteMoment.HoldCompleted);
            RhythmJudgementResult press = GetPressJudgement();
            // A completed hold is Perfect (pressed Perfect) or Good.
            HitJudgement judgement = press.Judgement == HitJudgement.Perfect ? HitJudgement.Perfect : HitJudgement.Good;
            ForceResolve(new RhythmJudgementResult(press.NoteId, press.LaneId, judgement, press.Distance, press.WorldPosition, press.Source));
        }

        private IEnumerable<Renderer> TailRenderers()
        {
            foreach (HoldNoteTailVisual tail in GetComponentsInChildren<HoldNoteTailVisual>(true))
                if (tail != null)
                    foreach (Renderer part in tail.GetComponentsInChildren<Renderer>(true)) yield return part;
        }

        // ---------- mash ----------

        private double SecondsIntoWindow => NowSeconds - context.HitTime;
        private bool MashPressable => !finished && SecondsIntoWindow <= MashRules.LineGraceSeconds;

        private void TickMash()
        {
            if (finished) return;
            if (!landed)
            {
                if (SecondsIntoWindow < 0d)
                {
                    Travel();
                    return;
                }
                landed = true;
                isMoving = false;
                StopMovementTweens();
                RhythmLaneTarget target = GetLaneTarget();
                if (target != null) transform.position = target.transform.position;
            }
            if (SecondsIntoWindow >= MashRules.LineGraceSeconds) MashFail();
        }

        private void MashPress()
        {
            if (finished || IsResolved) return;
            presses++;
            Emit(NoteMoment.Pressed);
            if (presses >= requiredPresses) MashClear();
        }

        private void MashClear()
        {
            finished = true;
            double travel = Data != null ? Data.TravelTime : 2.5d;
            MashGrade grade = MashRules.GradeClear(-SecondsIntoWindow, travel, Mash.perfectProgress, Mash.goodProgress);
            HitJudgement judgement = grade switch
            {
                MashGrade.Perfect => HitJudgement.Perfect,
                MashGrade.Good => HitJudgement.Good,
                MashGrade.Bad => HitJudgement.Bad,
                _ => HitJudgement.Miss
            };
            ForceResolve(new RhythmJudgementResult(RuntimeNoteId, GetNoteIdentity(), judgement, 0f,
                GetJudgementWorldPosition(), NoteResolutionSource.PlayerInput));
        }

        private void MashFail()
        {
            if (finished || IsResolved) return;
            finished = true;
            NoteResolutionSource source = presses > 0 ? NoteResolutionSource.PlayerInput : NoteResolutionSource.Timeout;
            ForceResolve(new RhythmJudgementResult(RuntimeNoteId, GetNoteIdentity(), HitJudgement.Miss, 0f,
                GetJudgementWorldPosition(), source));
        }

        // ---------- end ----------

        protected override void OnResolving(RhythmJudgementResult result)
        {
            if (holding)
            {
                holding = false;
                holdFeedback.End(false, MarkerPosition);
            }
            LaneAnticipation.Unregister(this);
            MarkResolved(NoteKinds.Outcome(result), result.Judgement);
        }

        private void MarkResolved(NoteOutcome outcome, HitJudgement judgement)
        {
            if (context.Resolved) return;
            context.Resolved = true;
            context.Outcome = outcome;
            context.Judgement = judgement;
            context.ResolvedAt = Time.time;
            Emit(NoteKinds.OutcomeMoment(outcome));
            Emit(NoteMoment.Resolved);
        }

        /// <summary>Seconds the note stays after resolving: its own linger or the longest a view needs.</summary>
        public float Linger
        {
            get
            {
                float linger = lingerSeconds;
                foreach (NoteView view in Views)
                    if (view != null && view.Enabled) linger = Mathf.Max(linger, view.Linger);
                // Cues after the ending ("Hit +0.3s") and planned ones still ahead ("Beat -0.1s" after an early hit)
                // get to play. Not when the note was cleared away (pattern cancelled, battle over).
                linger = Mathf.Max(linger, cues.LongestDelayAfter(NoteMoment.Hit, NoteMoment.Missed, NoteMoment.Resolved,
                    NoteMoment.HoldCompleted, NoteMoment.HoldReleased) + 0.05f);
                if (context.Outcome != NoteOutcome.Cleared)
                    linger = Mathf.Max(linger, cues.SecondsUntilLastScheduled(NowSeconds, context.SpawnTime, context.HitTime, context.EndTime));
                return linger;
            }
        }

        public override void DestroyObject()
        {
            canBePressed = false;
            isMoving = false;
            // Cleared without a result (pattern cleared, battle over): still an ending for the views.
            MarkResolved(NoteOutcome.Cleared, HitJudgement.Miss);
            holding = false;
            holdFeedback.End(false, MarkerPosition);
            LaneAnticipation.Unregister(this);
            NoteInitializeMovement intro = GetComponent<NoteInitializeMovement>();
            if (intro != null) intro.Stop();
            if (deflecting)
            {
                FlyBack();
                return;
            }
            StopMovementTweens();
            Destroy(gameObject, Linger);
        }

        private void OnDestroy()
        {
            holdFeedback.End(false, transform.position);
            LaneHold.End(this);
            LaneAnticipation.Unregister(this);
            EndViews();
        }

        // ---------- key marker charge (stationary) ----------

        int ILaneAnticipation.AnticipationLaneId => GetNoteIdentity();
        LaneAnticipationKind ILaneAnticipation.AnticipationKind =>
            kind == NoteKind.StationaryHold ? LaneAnticipationKind.ChargeFill : LaneAnticipationKind.ApproachOutline;
        bool ILaneAnticipation.IsAnticipating => NoteKinds.IsStationary(kind) && !IsResolved && !hitAccepted && !context.Resolved;
        float ILaneAnticipation.AnticipationProgress => context.Approach;

        // ---------- Ping-Pong rally (Pong kind) ----------

        Note IRallyShot.Note => this;
        public bool IsSequenceShot { get; set; }
        public float ReturnSeconds { get; set; } = 0.5f;
        public Vector3 ReturnDestination { get; set; }
        public bool IsWaitingForNextVolley => deflecting && keepForNextVolley;
        public void BeginDeflect() => deflecting = true;
        public void KeepForNextVolley() => keepForNextVolley = true;

        private void FlyBack()
        {
            travelStarted = false;
            clockDriven = false;
            StopMovementTweens();
            Tween.Position(transform, ReturnDestination, Mathf.Max(0.05f, ReturnSeconds), Ease.OutQuad)
                .OnComplete(this, shot => shot.OnReturned());
        }

        private void OnReturned()
        {
            if (!keepForNextVolley) Destroy(gameObject);
        }

        /// <summary>Fires this same object again as the next incoming volley.</summary>
        public void Rearm(RhythmNoteSpawnContext spawn, Vector3 spawnPosition)
        {
            Tween.StopAll(transform);
            deflecting = false;
            keepForNextVolley = false;
            transform.position = spawnPosition;
            EndViews();
            Initialize(spawn);
        }
    }
}
