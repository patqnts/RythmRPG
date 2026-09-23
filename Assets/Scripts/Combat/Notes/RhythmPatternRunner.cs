using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Rhythm;
using RythmRPG.Rhythm.Sequences;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    public readonly struct PatternRunContext
    {
        public readonly PatternRunMode Mode;
        public readonly PlayerCombatant Player;
        public readonly Transform SpawnOrigin;
        public readonly float DurationOverride;
        public readonly AttackStepEndPolicy EndPolicy;

        public PatternRunContext(PatternRunMode mode, PlayerCombatant player, Transform spawnOrigin = null,
            float durationOverride = 0f, AttackStepEndPolicy endPolicy = AttackStepEndPolicy.WaitForResolvedNotes)
        {
            Mode = mode;
            Player = player;
            SpawnOrigin = spawnOrigin;
            DurationOverride = durationOverride;
            EndPolicy = endPolicy;
        }
    }

    public readonly struct PatternRunResult
    {
        public readonly PatternRunMode Mode;
        public readonly RhythmPerformanceResult Performance;
        public readonly bool WasCancelled;

        public PatternRunResult(PatternRunMode mode, RhythmPerformanceResult performance, bool wasCancelled)
        {
            Mode = mode;
            Performance = performance;
            WasCancelled = wasCancelled;
        }
    }

    public sealed class RhythmPatternRunner : MonoBehaviour
    {
        [Header("Pattern Presentation")]
        [FormerlySerializedAs("rhythmChart")]
        [SerializeField] private RhythmChart fallbackChart;
        [SerializeField] private Transform projectileObjectHolder;
        [SerializeField] private AudioSource chartAudioSource;
        [SerializeField] private bool playChartAudio;
        [SerializeField] private JudgementConfig judgementConfig;
        [Tooltip("Used by sequence attacks (Ping-Pong) when the running chart has no Pong note definition with a prefab.")]
        [SerializeField] private GameObject sequenceNotePrefab;

        private readonly List<Note> activeNotes = new();
        private readonly List<RhythmJudgementResult> results = new();
        private Coroutine runCoroutine;
        private PatternRunContext currentContext;
        private int expectedNoteCount;
        private bool cancelled;
        private KeyButton[] cachedKeys;
        private CombatLanePresentation3D lanePresentation;
        private MusicClock clock;
        private readonly BeatScheduler scheduler = new();
        private readonly SequenceAttackPool sequencePool = new();
        private RhythmChart currentChart;
        private const double EndGraceSeconds = 1d;
        private CombatMusicDirector musicDirector;

        /// <summary>Damage the player takes when an enemy note resolves (set by the combat controller).</summary>
        public Func<Note, RhythmJudgementResult, int> DefenseDamageResolver { get; set; }
        /// <summary>Mode of the running (or last) pattern.</summary>
        public PatternRunMode CurrentMode => currentContext.Mode;
        // Ping-Pong: the shot deflected during the current ResolveNote call, and shots kept alive between volleys.
        private PongNote deflectedThisResolve;
        private readonly HashSet<PongNote> rallyShots = new();

        public bool IsRunning => runCoroutine != null;
        public bool HorizontalGameplay => lanePresentation != null && lanePresentation.HorizontalGameplay;
        public RhythmChart FallbackChart => fallbackChart;
        /// <summary>Seconds. Past this a note that was not pressed is a Miss.</summary>
        public float BadWindow => judgementConfig != null ? judgementConfig.BadWindow : 0.125f;
        /// <summary>Seconds. Presses closer than this reach a note; between Bad and this they are a Miss.</summary>
        public float PressWindow => judgementConfig != null ? judgementConfig.MissWindow : 0.18f;
        public IReadOnlyList<Note> ActiveNotes => activeNotes;

        /// <summary>Song clock of the running chart (AudioSettings.dspTime based); null when nothing has run yet.</summary>
        public MusicClock Clock => clock;
        /// <summary>Fires beat callbacks while a chart runs (metronome pulses, enemy animation, WaitForBeat users).</summary>
        public BeatScheduler Scheduler => scheduler;
        /// <summary>Running sequence attacks. The pattern run stays open (and the enemy turn with it) while blocking ones are active.</summary>
        public SequenceAttackPool Sequences => sequencePool;
        /// <summary>Seconds into the running chart (0 when idle). Negative during the lead-in before the first note spawns.</summary>
        public double ChartSeconds => IsRunning && clock != null ? clock.Seconds : 0d;
        public double ChartBeat => IsRunning && clock != null ? clock.Beat : 0d;
        public Transform ProjectileObjectHolder
        {
            get
            {
                ResolveProjectileHolder();
                return projectileObjectHolder;
            }
        }
        public event Action<RhythmChart, PatternRunContext> PatternStarted;
        public event Action<Note> NoteSpawned;
        public event Action<RhythmJudgementResult> NoteResolved;
        public event Action<PatternRunResult> PatternCompleted;

        private void Awake()
        {
            judgementConfig ??= Resources.Load<JudgementConfig>("Combat/Judgement/JudgementConfig");
            ResolveProjectileHolder();
        }

        public void ConfigurePresentation(CombatLanePresentation3D presentation)
        {
            lanePresentation = presentation;
        }

        /// <summary>
        /// Combat music. While its song plays, every chart starts on the song's next bar line: the chart's beat 0
        /// (chart time = its audio offset) lands on a downbeat, so notes follow the song's beat whatever moment of the
        /// looping song the chart starts at. Charts should be authored at the song's BPM.
        /// </summary>
        public void ConfigureMusic(CombatMusicDirector director)
        {
            musicDirector = director;
        }

        public void ConfigureInput(LaneInputRouter inputRouter)
        {
            cachedKeys = inputRouter != null ? inputRouter.GetViews() : null;
        }

        private void Update()
        {
            if (!IsRunning) return;
            if (clock != null) scheduler.Advance(clock.Beat);
            foreach (Note note in activeNotes.ToArray())
            {
                if (note == null)
                {
                    activeNotes.Remove(note);
                    continue;
                }
                KeyButton key = note.keys?.FirstOrDefault(candidate => candidate != null && candidate.keyIdentity == note.GetNoteIdentity());
                if (note.ShouldAutoMissByPosition
                    && key != null
                    && note.HasPassedMissWindow(key, BadWindow))
                {
                    note.ForceMiss(key);
                }
            }
        }

        public Coroutine Run(RhythmChart chart, PatternRunContext context) => Run(chart, context, null);

        /// <summary>
        /// Runs a chart whose start was planned with <see cref="PlanStart"/>: chart time 0 happens at
        /// <paramref name="plannedZeroDsp"/> (null = align now, as <see cref="Run(RhythmChart, PatternRunContext)"/>).
        /// </summary>
        public Coroutine Run(RhythmChart chart, PatternRunContext context, double? plannedZeroDsp)
        {
            CancelCurrentPattern(false);
            currentContext = context;
            cancelled = false;
            runCoroutine = StartCoroutine(RunRoutine(chart, plannedZeroDsp));
            return runCoroutine;
        }

        /// <summary>
        /// Plans when <paramref name="chart"/> would start so that its first projectile appears no earlier than
        /// <paramref name="earliestFirstSpawnDsp"/> and its beat 0 lands on the song's grid (see the song's Chart Sync).
        /// Returns the dsp time of chart time 0; <paramref name="firstSpawnDsp"/> is when the first projectile appears.
        /// Lets the enemy's wind-up animation be timed to the throw instead of adding to the wait.
        /// </summary>
        public double PlanStart(RhythmChart chart, double earliestFirstSpawnDsp, out double firstSpawnDsp)
        {
            double now = AudioSettings.dspTime;
            if (chart == null)
            {
                firstSpawnDsp = Math.Max(now, earliestFirstSpawnDsp);
                return firstSpawnDsp;
            }

            double beatZeroChartTime = chart.AudioOffsetSeconds;
            double firstSpawn = FirstSpawnChartTime(chart);
            double lead = Math.Max(0d, beatZeroChartTime - firstSpawn); // time from the first spawn to beat 0
            double earliestBeatZero = Math.Max(earliestFirstSpawnDsp + lead,
                now + Math.Max(CombatMusicDirector.MinLeadSeconds, lead));
            double beatZeroDsp = earliestBeatZero;
            if (musicDirector != null && musicDirector.HasSong)
            {
                beatZeroDsp = musicDirector.NextBarDsp(earliestBeatZero);
                WarnIfTempoDiffers(chart, musicDirector.CurrentSong);
            }

            double zeroDsp = beatZeroDsp - beatZeroChartTime;
            firstSpawnDsp = zeroDsp + firstSpawn;
            return zeroDsp;
        }

        private static double FirstSpawnChartTime(RhythmChart chart)
        {
            double firstSpawn = double.MaxValue;
            foreach (RhythmNoteData note in chart.Notes)
                if (note != null) firstSpawn = Math.Min(firstSpawn, note.SpawnTime);
            foreach (SequenceActivationData sequence in chart.Sequences)
                if (sequence != null) firstSpawn = Math.Min(firstSpawn, sequence.StartTime);
            return firstSpawn == double.MaxValue ? chart.AudioOffsetSeconds : firstSpawn;
        }

        public void HandleLanePressed(int laneId)
        {
            if (!IsRunning) return;
            KeyButton key = GetKeys().FirstOrDefault(candidate => candidate.keyIdentity == laneId);
            if (key == null) return;
            Note best = activeNotes.Where(note => note != null && note.CanReceiveHit(key))
                .OrderBy(note => note.GetTimingError(key)).FirstOrDefault();
            if (best == null) return;
            float distance = best.GetTimingError(key);
            HitJudgement judgement = Evaluate(distance);
            judgement = best.AdjustJudgement(judgement, distance);
            RhythmJudgementResult result = new(best.RuntimeNoteId, laneId, judgement, distance,
                best.GetJudgementWorldPosition(), NoteResolutionSource.PlayerInput);
            if (judgement == HitJudgement.Miss)
            {
                // Pressed in the Miss range (too early / too late): the note is used up as a Miss.
                bool missOnPress = judgementConfig == null || judgementConfig.PressInMissRangeCountsAsMiss;
                if (best.ShouldResolveMissOnPlayerInput || missOnPress) best.ForceResolve(result);
                return;
            }
            best.TryHitFromKey(key, result);
        }

        public void HandleLaneReleased(int laneId)
        {
            KeyButton key = GetKeys().FirstOrDefault(candidate => candidate.keyIdentity == laneId);
            if (key == null) return;
            foreach (Note note in activeNotes.Where(note => note != null && note.IsUsingKey(key)).ToArray())
                note.OnKeyReleased(key);
        }

        public void ResolveNote(Note note, RhythmJudgementResult result)
        {
            if (note == null || !activeNotes.Remove(note)) return;
            results.Add(result);
            NoteResolved?.Invoke(result);
            bool success = result.Judgement != HitJudgement.Miss;
            // A deflected Ping-Pong shot is not destroyed: the same object flies back to the enemy (see PongNote).
            deflectedThisResolve = success && note is PongNote pong && pong.IsSequenceShot ? pong : null;
            if (deflectedThisResolve != null) deflectedThisResolve.BeginDeflect();
            sequencePool.NotifyNoteResolved(note.RuntimeNoteId, success, ChartSeconds);
            deflectedThisResolve = null;
            if (currentContext.Mode == PatternRunMode.EnemyDefense && currentContext.Player != null)
            {
                // Damage is weighted by judgement (Miss / Bad hurt, Good / Perfect do not by default) and may be
                // cancelled by wards; the combat controller supplies the rule. Without one: legacy "Miss = full damage".
                int amount = DefenseDamageResolver != null
                    ? DefenseDamageResolver(note, result)
                    : note.ShouldDamagePlayerOnResolve(result) ? note.damage : 0;
                if (amount > 0)
                {
                    currentContext.Player.ApplyDamage(amount);
                    if (currentContext.Player.IsDefeated) CancelCurrentPattern(true);
                }
            }
        }

        public void ResolveCollateral(IEnumerable<Note> notes)
        {
            foreach (Note note in notes?.Where(note => note != null && activeNotes.Contains(note)).ToArray() ?? Array.Empty<Note>())
            {
                RhythmJudgementResult result = new(note.RuntimeNoteId, note.GetNoteIdentity(), HitJudgement.Perfect, 0f,
                    note.GetJudgementWorldPosition(), NoteResolutionSource.Modifier);
                note.TryHitFromKey(GetKeys().FirstOrDefault(key => key.keyIdentity == note.GetNoteIdentity()), result);
            }
        }

        public void CancelCurrentPattern(bool reportCompletion = true)
        {
            if (runCoroutine != null)
            {
                StopCoroutine(runCoroutine);
                runCoroutine = null;
            }
            cancelled = true;
            sequencePool.CancelAll(SequenceCancelReason.EncounterInterrupted);
            StopAudio();
            ClearRemaining(false);
            DestroyWaitingRallyShots();
            if (reportCompletion) Complete(true);
        }

        public void ClearRemaining(bool asMisses)
        {
            foreach (Note note in activeNotes.ToArray())
            {
                if (note == null) continue;
                if (asMisses)
                {
                    RhythmJudgementResult miss = new(note.RuntimeNoteId, note.GetNoteIdentity(), HitJudgement.Miss,
                        BadWindow, note.GetJudgementWorldPosition(), NoteResolutionSource.SystemClear);
                    note.ForceResolve(miss);
                }
                else
                {
                    activeNotes.Remove(note);
                    note.ClearWithoutResult();
                }
            }
        }

        private IEnumerator RunRoutine(RhythmChart chart, double? plannedZeroDsp)
        {
            results.Clear();
            currentChart = chart;
            sequencePool.CancelAll(SequenceCancelReason.Superseded);
            expectedNoteCount = chart?.Notes.Count(note => note != null) ?? 0;
            PatternStarted?.Invoke(chart, currentContext);
            if (chart == null)
            {
                Complete(false);
                yield break;
            }

            List<RhythmNoteData> notes = chart.GetNotesBySpawnTime().ToList();
            double playbackStart = RhythmTimingUtility.GetPlaybackStartTime(chart);
            double zeroDspTime = AudioSettings.dspTime - playbackStart;
            bool encounterMusic = musicDirector != null && musicDirector.HasSong;
            if (plannedZeroDsp.HasValue) zeroDspTime = plannedZeroDsp.Value;
            else if (encounterMusic) zeroDspTime = PlanStart(chart, AudioSettings.dspTime, out _);
            // Smoothed audio clock: notes glide instead of stepping once per audio buffer, same timeline as dspTime.
            clock = new MusicClock(() => SmoothedDspTime.Now, chart.CreateTempoMap());
            clock.StartAt(zeroDspTime);
            scheduler.Reposition(0d);
            int nextIndex = 0;
            List<SequenceActivationData> sequences = chart.Sequences.Where(sequence => sequence != null)
                .OrderBy(sequence => sequence.StartTime).ToList();
            int nextSequence = 0;
            TempoMap tempo = chart.CreateTempoMap();
            if (!encounterMusic) ScheduleAudio(chart, zeroDspTime);
            // A chart ends when its last note is resolved. Audio length and the chart's editor-only Composition Duration
            // do not keep it running. The small grace lets the last note still be hit before the end policy clears it.
            double contentEnd = chart.ContentEndTime;
            double configuredEnd = currentContext.DurationOverride > 0f ? currentContext.DurationOverride : contentEnd + EndGraceSeconds;

            while (!cancelled)
            {
                double chartTime = AudioSettings.dspTime - zeroDspTime;
                while (nextIndex < notes.Count && notes[nextIndex].SpawnTime <= chartTime)
                    Spawn(chart, notes[nextIndex++]);

                while (nextSequence < sequences.Count && sequences[nextSequence].StartTime <= chartTime)
                {
                    ISequenceAttack attack = CreateSequence(sequences[nextSequence++], chart, tempo);
                    if (attack != null) sequencePool.Start(attack, chartTime);
                }
                sequencePool.Tick(chartTime);

                if (chartTime >= configuredEnd)
                {
                    if (currentContext.EndPolicy == AttackStepEndPolicy.ClearAsMisses)
                    {
                        sequencePool.CancelAll(SequenceCancelReason.ChartEnded);
                        ClearRemaining(true);
                    }
                    else if (currentContext.EndPolicy == AttackStepEndPolicy.ClearWithoutPenalty)
                    {
                        sequencePool.CancelAll(SequenceCancelReason.ChartEnded);
                        ClearRemaining(false);
                    }
                    if (currentContext.EndPolicy != AttackStepEndPolicy.WaitForResolvedNotes
                        || (activeNotes.Count == 0 && !sequencePool.HasBlocking)) break;
                }
                if (nextIndex >= notes.Count && activeNotes.Count == 0 && !sequencePool.HasBlocking
                    && nextSequence >= sequences.Count && chartTime >= contentEnd) break;
                // Failsafe. A blocking sequence has its own MaxAge, so allow for it before cutting the run short.
                double failsafe = Math.Max(contentEnd, configuredEnd) + 8d + (sequencePool.HasBlocking ? 60d : 0d);
                if (chartTime > failsafe)
                {
                    sequencePool.CancelAll(SequenceCancelReason.MaxAge);
                    ClearRemaining(true);
                    break;
                }
                yield return null;
            }
            Complete(cancelled);
        }

        private bool Spawn(RhythmChart chart, RhythmNoteData data, GameObject fallbackPrefab = null)
        {
            RhythmLaneData lane = chart.FindLane(data.LaneId);
            GameObject prefab = chart.ResolvePrefab(data);
            if (prefab == null) prefab = fallbackPrefab;
            if (lane == null || prefab == null) return false;
            Transform origin = currentContext.SpawnOrigin != null ? currentContext.SpawnOrigin : transform;
            ResolveProjectileHolder();
            Vector3 spawnPosition = lanePresentation != null
                ? lanePresentation.ProjectToGameplayPlane(origin.position)
                : origin.position;
            Quaternion rotation = lanePresentation != null && lanePresentation.HorizontalGameplay
                ? prefab.transform.rotation
                : projectileObjectHolder != null ? projectileObjectHolder.rotation : Quaternion.identity;
            GameObject instance = Instantiate(prefab, spawnPosition, rotation, projectileObjectHolder);
            RhythmNoteVisualLayer visualLayer = instance.GetComponent<RhythmNoteVisualLayer>();
            if (visualLayer == null) visualLayer = instance.AddComponent<RhythmNoteVisualLayer>();
            visualLayer.Configure();
            if (lanePresentation != null && lanePresentation.HorizontalGameplay)
                visualLayer.FaceSpritesToCamera(lanePresentation.RenderCamera);
            Note note = instance.GetComponent<Note>();
            if (note == null)
            {
                Destroy(instance);
                return false;
            }
            note.Initialize(new RhythmNoteSpawnContext(this, data, data.Id, lane.KeyIdentity,
                Mathf.Max(0.01f, data.Speed), Mathf.Max(0, data.Damage), GetKeys()));
            ApplyInitializeMovement(note, lanePresentation != null && lanePresentation.HorizontalGameplay
                ? NoteInitializeMovementType.None
                : data.InitializeMovementType);
            if (note is HoldNoteObject hold) hold.length = data.LegacyHoldLength;
            if (note is HoldLaserNote holdLaser) holdLaser.length = data.LegacyHoldLength;
            activeNotes.Add(note);
            instance.SetActive(true);
            NoteSpawned?.Invoke(note);
            return true;
        }

        // ---- sequence attacks ----

        private ISequenceAttack CreateSequence(SequenceActivationData data, RhythmChart chart, TempoMap tempo)
        {
            switch (data.Kind)
            {
                case SequenceKind.PingPong:
                {
                    var settings = new PingPongSettings
                    {
                        Volleys = data.Volleys,
                        InitialTravelSeconds = data.InitialTravelSeconds,
                        SpeedUpFactor = data.SpeedUpFactor,
                        MinTravelSeconds = data.MinTravelSeconds,
                        ReturnSeconds = data.ReturnSeconds,
                        LaneIds = ResolveSequenceLanes(data, chart)
                    };
                    Func<double, double> align = null;
                    if (data.AlignToBeat) align = seconds => tempo.BeatToSeconds(Math.Ceiling(tempo.SecondsToBeat(seconds) - 1e-6d));
                    var policy = new SequencePolicy { BlocksTurnEnd = true, MaxAgeSeconds = data.MaxAgeSeconds };
                    return new PingPongAttack(data.Id, settings,
                        new RunnerPingPongHost(this, data.Damage, (float)Math.Max(0.05d, data.ReturnSeconds)), policy, align);
                }
                default:
                    Debug.LogWarning("Unknown sequence kind " + data.Kind + " in chart " + chart.name);
                    return null;
            }
        }

        private static string[] ResolveSequenceLanes(SequenceActivationData data, RhythmChart chart)
        {
            List<string> ids = data.LaneIds.Where(id => !string.IsNullOrEmpty(id) && chart.FindLane(id) != null).ToList();
            if (ids.Count == 0 && chart.Lanes.Count > 0) ids.Add(chart.Lanes[chart.Lanes.Count / 2].Id);
            return ids.ToArray();
        }

        private bool SpawnSequenceNote(string noteId, string laneId, double hitTime, double travelSeconds, int damage,
            float returnSeconds, PongNote reuse, out PongNote shot)
        {
            shot = null;
            if (currentChart == null) return false;
            RhythmNoteData data = new(laneId, hitTime, RhythmNoteType.Pong);
            data.AssignId(noteId);
            data.TravelTime = travelSeconds;
            data.Damage = damage;
            RhythmNoteDefinition definition = currentChart.FindDefinition(RhythmNoteType.Pong);
            if (definition != null) data.Speed = definition.DefaultSpeed;
            RhythmLaneData lane = currentChart.FindLane(laneId);
            Vector3 spawnPosition = ResolveSpawnPosition();

            // Same object for the whole rally: re-fire the shot that was just deflected back to the enemy.
            if (reuse != null && lane != null)
            {
                rallyShots.Remove(reuse);
                reuse.Rearm(new RhythmNoteSpawnContext(this, data, data.Id, lane.KeyIdentity,
                    Mathf.Max(0.01f, data.Speed), Mathf.Max(0, data.Damage), GetKeys()), spawnPosition);
                reuse.ReturnSeconds = returnSeconds;
                reuse.ReturnDestination = spawnPosition;
                activeNotes.Add(reuse);
                expectedNoteCount++;
                NoteSpawned?.Invoke(reuse);
                shot = reuse;
                return true;
            }

            if (!Spawn(currentChart, data, sequenceNotePrefab))
            {
                Debug.LogWarning("Ping-Pong could not spawn a note: give the chart a Pong note definition with a prefab, or assign Sequence Note Prefab on the runner.");
                return false;
            }
            expectedNoteCount++;
            shot = activeNotes.Count > 0 ? activeNotes[activeNotes.Count - 1] as PongNote : null;
            if (shot == null)
            {
                Debug.LogWarning("Ping-Pong note prefab has no PongNote component; each volley will spawn a new object.");
                return true;
            }
            shot.IsSequenceShot = true;
            shot.ReturnSeconds = returnSeconds;
            shot.ReturnDestination = spawnPosition;
            return true;
        }

        private Vector3 ResolveSpawnPosition()
        {
            Transform origin = currentContext.SpawnOrigin != null ? currentContext.SpawnOrigin : transform;
            return lanePresentation != null ? lanePresentation.ProjectToGameplayPlane(origin.position) : origin.position;
        }

        // The deflected shot flies back and waits for the next volley; picked up by the Ping-Pong host.
        private PongNote TakeDeflectedShotForNextVolley()
        {
            PongNote shot = deflectedThisResolve;
            if (shot == null) return null;
            shot.KeepForNextVolley();
            rallyShots.Add(shot);
            return shot;
        }

        private void DestroyWaitingRallyShots()
        {
            foreach (PongNote shot in rallyShots)
                if (shot != null && shot.IsWaitingForNextVolley) Destroy(shot.gameObject);
            rallyShots.Clear();
        }

        private sealed class RunnerPingPongHost : IPingPongHost
        {
            private readonly RhythmPatternRunner runner;
            private readonly int damage;
            private readonly float returnSeconds;
            private PongNote ball;

            public RunnerPingPongHost(RhythmPatternRunner runner, int damage, float returnSeconds)
            {
                this.runner = runner;
                this.damage = damage;
                this.returnSeconds = returnSeconds;
            }

            public bool SpawnIncoming(string noteId, string laneId, double hitTime, double travelSeconds)
            {
                PongNote reuse = ball;
                ball = null;
                return runner.SpawnSequenceNote(noteId, laneId, hitTime, travelSeconds, damage, returnSeconds, reuse, out _);
            }

            // The deflected shot itself flies back (PongNote.DestroyObject); keep it for the next volley.
            public void ReturnShot(string laneId, double fromTime, double toTime)
            {
                ball = runner.TakeDeflectedShotForNextVolley();
            }
        }

        private void Complete(bool wasCancelled)
        {
            runCoroutine = null;
            sequencePool.CancelAll(SequenceCancelReason.ChartEnded);
            DestroyWaitingRallyShots();
            if (clock != null) clock.Stop();
            StopAudio();
            RhythmPerformanceResult performance = RhythmPerformanceCalculator.Calculate(expectedNoteCount, results, null);
            PatternCompleted?.Invoke(new PatternRunResult(currentContext.Mode, performance, wasCancelled));
        }

        private HitJudgement Evaluate(float distance)
        {
            // distance = timing error in seconds.
            if (judgementConfig != null) return judgementConfig.Evaluate(distance);
            if (distance <= 0.04f) return HitJudgement.Perfect;
            if (distance <= 0.08f) return HitJudgement.Good;
            if (distance <= 0.125f) return HitJudgement.Bad;
            return HitJudgement.Miss;
        }

        private KeyButton[] GetKeys()
        {
            if (cachedKeys == null || cachedKeys.Length == 0)
                cachedKeys = FindObjectsByType<KeyButton>(FindObjectsInactive.Include);
            return cachedKeys;
        }

        private static readonly HashSet<RhythmChart> tempoWarned = new();

        private static void WarnIfTempoDiffers(RhythmChart chart, CombatSong song)
        {
            if (song == null || chart == null) return;
            bool differs = Math.Abs(chart.Bpm - song.Bpm) > 0.01f || chart.BeatsPerMeasure != song.BeatsPerMeasure;
            if (!differs || !tempoWarned.Add(chart)) return;
            Debug.LogWarning($"[Rhythm] Chart '{chart.name}' is authored at {chart.Bpm} BPM ({chart.BeatsPerMeasure}/bar) but the " +
                $"song '{song.name}' is {song.Bpm} BPM ({song.BeatsPerMeasure}/bar). Its notes will drift off the beat: open it in " +
                "the composer, set its Song and save.", chart);
        }

        private void ScheduleAudio(RhythmChart chart, double zeroDspTime)
        {
            if (!playChartAudio || chartAudioSource == null || chart.AudioClip == null) return;
            chartAudioSource.clip = chart.AudioClip;
            chartAudioSource.PlayScheduled(Math.Max(AudioSettings.dspTime, zeroDspTime));
        }

        private void StopAudio()
        {
            if (chartAudioSource != null) chartAudioSource.Stop();
        }

        private static void ApplyInitializeMovement(Note note, NoteInitializeMovementType type)
        {
            NoteInitializeMovement movement = note.GetComponent<NoteInitializeMovement>();
            if (movement == null && type != NoteInitializeMovementType.None)
                movement = note.gameObject.AddComponent<NoteInitializeMovement>();
            if (movement != null) movement.SetMovementType(type);
        }

        private void ResolveProjectileHolder()
        {
            if (projectileObjectHolder != null) return;
            projectileObjectHolder = FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(candidate => candidate.name == "ProjectileHolder");
            if (projectileObjectHolder != null) return;

            GameObject holder = new("ProjectileHolder");
            projectileObjectHolder = holder.transform;
        }
    }
}
