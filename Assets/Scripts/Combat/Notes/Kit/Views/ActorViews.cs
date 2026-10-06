using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Moves the attacker (or defender) during the note: e.g. a melee enemy steps up to the lane while the note approaches,
    /// arriving just before the beat so its strike animation (Animator View on Source) lands on it, then walks back.
    /// When several notes want the same combatant, the newest takes over; its home is restored by the last one.
    /// </summary>
    [Serializable]
    public sealed class ActorMoveView : NoteView
    {
        public enum Who { Source, Target }

        [SerializeField] private Who actor = Who.Source;
        [SerializeField] private NoteAnchor destination = NoteAnchor.HitPoint;
        [SerializeField] private string destinationSocket = string.Empty;
        [Tooltip("Lane space from the destination: Z below 0 stops short of the hit line, X stands beside the lane.")]
        [SerializeField] private Vector3 offset = new(0f, 0f, -0.6f);
        [Tooltip("Keep the actor's own height (walk on the ground).")]
        [SerializeField] private bool keepHeight = true;
        [Tooltip("When the move starts; it takes Move Seconds. Default: 0.45 s before the beat, so it arrives 0.1 s before it and the strike (Animator View on Source) lands on the beat.")]
        [SerializeField] private NoteCue moveAt = new(NoteMoment.ReachedBeat, -0.45f);
        [Tooltip("Seconds the move takes (0 = jump there).")]
        [SerializeField, Min(0f)] private float moveSeconds = 0.35f;
        [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Hop this high on the way there (0 = straight). With Move Seconds 0 it is a teleport.")]
        [SerializeField, Min(0f)] private float moveJumpHeight;

        [Header("Return")]
        [Tooltip("When it walks back home.")]
        [SerializeField] private NoteCue returnAt = new(NoteMoment.Resolved, 0.15f);
        [SerializeField, Min(0.01f)] private float returnSeconds = 0.3f;
        [Tooltip("Jump back in an arc this high (0 = walk straight back).")]
        [SerializeField, Min(0f)] private float returnJumpHeight;

        [NonSerialized] private Transform who;
        [NonSerialized] private Vector3 home;
        [NonSerialized] private bool claimed;
        [NonSerialized] private bool returning;
        [NonSerialized] private bool chartTimed;
        [NonSerialized] private double moveStartChart;
        [NonSerialized] private float moveStartReal = -1f;

        public Who Actor { get => actor; set => actor = value; }
        public Vector3 Offset { get => offset; set => offset = value; }
        public NoteAnchor Destination { get => destination; set => destination = value; }
        public NoteCue MoveAt { get => moveAt ??= new NoteCue(NoteMoment.ReachedBeat, -0.45f); set => moveAt = value; }
        public float MoveSeconds { get => moveSeconds; set => moveSeconds = Mathf.Max(0f, value); }
        public NoteCue ReturnAt { get => returnAt ??= new NoteCue(NoteMoment.Resolved, 0.15f); set => returnAt = value; }
        public float ReturnSeconds { get => returnSeconds; set => returnSeconds = Mathf.Max(0.01f, value); }
        public bool KeepHeight { get => keepHeight; set => keepHeight = value; }
        public float MoveJumpHeight { get => moveJumpHeight; set => moveJumpHeight = Mathf.Max(0f, value); }
        public float ReturnJumpHeight { get => returnJumpHeight; set => returnJumpHeight = Mathf.Max(0f, value); }

        public override void Begin(NoteViewContext context)
        {
            who = actor == Who.Source ? context.Source : context.Target;
            claimed = who != null;
            returning = false;
            if (!claimed) return;
            Tween.StopAll(who); // a previous note's walk back
            home = ActorLock.Acquire(who, this);
            // Planned moments (spawn, beat, hold end) follow the chart clock; others start when they happen.
            chartTimed = MoveAt.IsScheduled;
            moveStartReal = -1f;
            moveStartChart = NoteCueScheduler.BaseTime(MoveAt.moment, context.SpawnTime, context.HitTime, context.EndTime)
                             + MoveAt.EffectiveOffset;
            if (!chartTimed) context.Cue(MoveAt, () => moveStartReal = Time.time);
            if (ReturnAt.moment != NoteMoment.None) context.Cue(ReturnAt, () => ReturnHome(0f));
        }

        public override void Tick(NoteViewContext context)
        {
            if (!claimed || returning || who == null || !ActorLock.Owns(who, this)) return;
            Vector3 target = context.Position(destination, destinationSocket, offset);
            if (keepHeight) target.y = home.y;
            float t;
            if (chartTimed) t = ActorMath.MoveProgress(context.Now, moveStartChart, moveStartChart + moveSeconds);
            else if (moveStartReal >= 0f) t = ActorMath.MoveProgress(Time.time, moveStartReal, moveStartReal + moveSeconds);
            else return;
            float eased = moveCurve != null && moveCurve.length > 0 ? moveCurve.Evaluate(t) : t;
            who.position = Vector3.LerpUnclamped(home, target, eased) + Vector3.up * ActorMath.Hop(t, moveJumpHeight);
        }

        public override void End(NoteViewContext context) => ReturnHome(0f);

        public override string Title => "Move " + actor;

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            items.Add(NoteTimelineItem.Timed("move to " + destination, MoveAt, moveSeconds, value => MoveSeconds = value, NoteTimelineColors.Move));
            if (ReturnAt.moment != NoteMoment.None)
                items.Add(NoteTimelineItem.Timed("back home", ReturnAt, returnSeconds, value => ReturnSeconds = value, NoteTimelineColors.Move));
        }

        private void ReturnHome(float delay)
        {
            if (!claimed || returning || who == null) return;
            returning = true;
            if (!ActorLock.Owns(who, this)) return; // a newer note has it
            Transform mover = who;
            object owner = this;
            if (returnJumpHeight > 0f)
            {
                // A jump: straight across, up and down in a parabola.
                Vector3 from = mover.position, to = home;
                float height = returnJumpHeight;
                Tween.Custom(mover, 0f, 1f, returnSeconds,
                        (m, p) => m.position = Vector3.LerpUnclamped(from, to, p) + Vector3.up * ActorMath.Hop(p, height), Ease.Linear)
                    .OnComplete(mover, _ => ActorLock.Release(mover, owner));
                return;
            }
            Tween.Position(mover, new TweenSettings<Vector3>(mover.position, home, new TweenSettings(returnSeconds, Ease.OutQuad, startDelay: delay)))
                .OnComplete(mover, _ => ActorLock.Release(mover, owner));
        }
    }

    public static class ActorMath
    {
        /// <summary>0 before <paramref name="start"/>, 1 at <paramref name="arrive"/> and after.</summary>
        public static float MoveProgress(double now, double start, double arrive)
        {
            if (arrive <= start) return now >= arrive ? 1f : 0f;
            return Mathf.Clamp01((float)((now - start) / (arrive - start)));
        }

        /// <summary>Height of a hop or throw arc at progress t (0-1): 0 at both ends, <paramref name="height"/> halfway.</summary>
        public static float Hop(float t, float height) => height <= 0f ? 0f : 4f * height * t * (1f - t);
    }

    /// <summary>
    /// Shows child objects per phase: the approach (with a fill that grows to the beat), the hold, the hit and the miss.
    /// This is how the old stationary notes' Anticipation / Hit Window / Hold / End children work.
    /// </summary>
    [Serializable]
    public sealed class PhaseObjectsView : NoteView
    {
        [SerializeField] private GameObject approach;
        [Tooltip("Scaled from Fill From to Fill To as the note approaches the beat.")]
        [SerializeField] private Transform approachFill;
        [SerializeField] private Vector3 fillFrom = Vector3.one * 0.15f;
        [SerializeField] private Vector3 fillTo = Vector3.one;
        [SerializeField] private GameObject hold;
        [SerializeField] private GameObject hit;
        [Tooltip("Empty = the Hit object.")]
        [SerializeField] private GameObject miss;

        public GameObject Approach { get => approach; set => approach = value; }
        public Transform ApproachFill { get => approachFill; set => approachFill = value; }
        public Vector3 FillFrom { get => fillFrom; set => fillFrom = value; }
        public Vector3 FillTo { get => fillTo; set => fillTo = value; }
        public GameObject HoldObject { get => hold; set => hold = value; }
        public GameObject HitObject { get => hit; set => hit = value; }
        public GameObject MissObject { get => miss; set => miss = value; }

        public override void Begin(NoteViewContext context) => Show(approach: true);

        public override string Title => "Phase Objects";

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            if (approach != null) items.Add(NoteTimelineItem.Info(approach.name, NoteMoment.Spawned, NoteMoment.ReachedBeat, NoteTimelineColors.Info));
            if (hold != null) items.Add(NoteTimelineItem.Info(hold.name, NoteMoment.HoldStarted, NoteMoment.HoldEnd, NoteTimelineColors.Info));
            if (hit != null) items.Add(NoteTimelineItem.Info(hit.name, NoteMoment.Hit, NoteMoment.Hit, NoteTimelineColors.Info));
        }

        public override void Tick(NoteViewContext context)
        {
            if (approachFill != null && approach != null && approach.activeSelf)
                approachFill.localScale = Vector3.LerpUnclamped(fillFrom, fillTo, context.Approach);
        }

        public override void Moment(NoteViewContext context, NoteMoment moment)
        {
            switch (moment)
            {
                case NoteMoment.HoldStarted: Show(holding: true); break;
                case NoteMoment.Hit: Show(hitShown: true); break;
                case NoteMoment.Missed: Show(missShown: true); break;
                case NoteMoment.Cleared: Show(); break;
            }
        }

        private void Show(bool approach = false, bool holding = false, bool hitShown = false, bool missShown = false)
        {
            Set(this.approach, approach);
            if (approachFill != null && approachFill.gameObject != this.approach) approachFill.gameObject.SetActive(approach);
            Set(hold, holding);
            GameObject missObject = miss != null ? miss : hit;
            if (missObject == hit)
            {
                Set(hit, hitShown || missShown);
                return;
            }
            Set(hit, hitShown);
            Set(missObject, missShown);
        }

        private static void Set(GameObject part, bool active)
        {
            if (part != null && part.activeSelf != active) part.SetActive(active);
        }
    }

    /// <summary>
    /// Moving hold notes: the tail (a HoldNoteTailVisual child: sprite, particles or custom) grows as the note travels,
    /// shrinks toward the hit line while held, and retracts when let go. The head can burst on the press.
    /// </summary>
    [Serializable]
    public sealed class HoldTailView : NoteView
    {
        [Tooltip("Which HoldNoteTailVisual child to use when there are several.")]
        [SerializeField] private HoldNoteTailMode tailMode = HoldNoteTailMode.Sprite;
        [Tooltip("When pressed, the head bursts on the key marker and disappears; the tail stays.")]
        [SerializeField] private bool breakHeadOnPress = true;
        [Tooltip("Seconds the tail takes to retract when let go early or missed (0 = vanish).")]
        [SerializeField, Range(0f, 0.25f)] private float collapseSeconds = 0.12f;

        [NonSerialized] private HoldNoteTailVisual tail;
        [NonSerialized] private float length;
        [NonSerialized] private float speed;
        [NonSerialized] private float revealed;
        [NonSerialized] private float shown;
        [NonSerialized] private bool ending;
        [NonSerialized] private bool hidden;
        [NonSerialized] private float collapseFrom;
        [NonSerialized] private float collapseAt;

        public HoldNoteTailMode TailMode { get => tailMode; set => tailMode = value; }
        public bool BreakHeadOnPress { get => breakHeadOnPress; set => breakHeadOnPress = value; }
        public float CollapseSeconds { get => collapseSeconds; set => collapseSeconds = Mathf.Clamp(value, 0f, 0.25f); }

        public override float Linger => collapseSeconds;

        public override string Title => "Hold Tail";

        public override void DescribeTimeline(List<NoteTimelineItem> items) =>
            items.Add(NoteTimelineItem.Info("tail", NoteMoment.Spawned, NoteMoment.HoldEnd, NoteTimelineColors.Info));

        public override void Begin(NoteViewContext context)
        {
            HoldNoteTailVisual[] tails = context.Note.GetComponentsInChildren<HoldNoteTailVisual>(true);
            tail = null;
            foreach (HoldNoteTailVisual candidate in tails)
                if (candidate != null && candidate.TailMode == tailMode) { tail = candidate; break; }
            if (tail == null && tails.Length > 0) tail = tails[0];
            foreach (HoldNoteTailVisual other in tails)
            {
                if (other == null) continue;
                if (other == tail) other.gameObject.SetActive(true);
                else
                {
                    other.Hide();
                    other.gameObject.SetActive(false);
                }
            }
            length = speed = revealed = shown = 0f;
            ending = hidden = false;
            if (tail != null) Set(0f);
        }

        public override void Tick(NoteViewContext context)
        {
            if (tail == null || hidden) return;
            CombatNote note = context.Note;
            if (note.TryGetLaneUp(out Vector3 up)) tail.SetLaneDirection(up);
            if (ending)
            {
                float t = collapseSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - collapseAt) / collapseSeconds);
                if (t >= 1f) Hide();
                else Set(collapseFrom * (1f - t * t));
                return;
            }

            float travelSpeed = note.TravelWorldSpeed;
            float holdLength = note.HoldLengthWorld;
            if (holdLength > 0.001f && (Mathf.Abs(holdLength - length) > 0.01f || Mathf.Abs(travelSpeed - speed) > 0.01f))
            {
                length = holdLength;
                speed = travelSpeed;
                tail.Initialize(length, speed);
            }
            if (length <= 0f) return;

            if (note.IsHolding && note.IsPinned)
            {
                // Pinned to the key marker: the tail's end keeps travelling and reaches the marker at the hold's end.
                Set(Mathf.Min(length, Mathf.Max(0f, (float)(context.EndTime - context.Now)) * speed));
                return;
            }
            if (!note.IsHolding)
            {
                revealed = Mathf.Min(length, revealed + speed * Time.deltaTime);
                Set(revealed);
            }
        }

        public override void Moment(NoteViewContext context, NoteMoment moment)
        {
            switch (moment)
            {
                case NoteMoment.HoldStarted:
                    if (breakHeadOnPress) BreakHead(context);
                    break;
                case NoteMoment.HoldCompleted:
                    EndTail(false);
                    break;
                case NoteMoment.HoldReleased:
                case NoteMoment.Missed:
                case NoteMoment.Cleared:
                    EndTail(true);
                    break;
            }
        }

        private void EndTail(bool collapse)
        {
            if (ending || tail == null) return;
            ending = true;
            collapseFrom = shown;
            collapseAt = Time.time;
            if (!collapse || collapseSeconds <= 0f || collapseFrom <= 0.01f) Hide();
        }

        private void Hide()
        {
            hidden = true;
            if (tail != null) tail.Hide();
        }

        private void Set(float remaining)
        {
            shown = remaining;
            tail.SetRemainingLength(remaining, length <= 0f ? 0f : Mathf.Clamp01(remaining / length));
        }

        // The head bursts on the key marker and disappears; the tail stays.
        private void BreakHead(NoteViewContext context)
        {
            var tailParts = new HashSet<Renderer>();
            foreach (HoldNoteTailVisual visual in context.Note.GetComponentsInChildren<HoldNoteTailVisual>(true))
                foreach (Renderer part in visual.GetComponentsInChildren<Renderer>(true)) tailParts.Add(part);
            foreach (Renderer part in context.Note.GetComponentsInChildren<Renderer>(true))
                if (part != null && !tailParts.Contains(part)) part.enabled = false;
            foreach (ParticleSystem system in context.Note.GetComponentsInChildren<ParticleSystem>(true))
            {
                Renderer systemRenderer = system.GetComponent<Renderer>();
                if (systemRenderer == null || !tailParts.Contains(systemRenderer)) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            HoldFxSettings fx = context.Note.Hold.fx;
            HoldFeedback.OneShot(fx != null ? fx.headHitEffectPrefab : null, context.Note.MarkerPosition, context.Color, 0.45f, 10, fx);
        }

        public override IEnumerable<string> Validate(CombatNote note)
        {
            if (note.Kind != NoteKind.Hold) yield return "Hold Tail View only does something on Hold notes.";
            if (note.GetComponentInChildren<HoldNoteTailVisual>(true) == null)
                yield return "Hold Tail View: add a tail child (Hold Note Sprite Tail Visual or Particle Tail Visual).";
        }
    }

    /// <summary>Mash notes: a meter that fills with the presses, a pulse on each press and an optional tint.</summary>
    [Serializable]
    public sealed class MashMeterView : NoteView
    {
        [Tooltip("Scaled on Y from 0 to 1 as presses add up.")]
        [SerializeField] private Transform meter;
        [Tooltip("What pulses on a press. Empty = the note.")]
        [SerializeField] private Transform pulseTarget;
        [SerializeField, Min(0f)] private float pulseScale = 0.25f;
        [SerializeField, Min(0.01f)] private float pulseDecay = 6f;
        [Tooltip("Sprites are tinted this colour. Alpha 0 = no tint.")]
        [SerializeField] private Color tint = new(1f, 0.85f, 0.2f, 1f);

        [NonSerialized] private float pulse;
        [NonSerialized] private Vector3 baseScale;
        [NonSerialized] private Transform pulsing;

        public Transform Meter { get => meter; set => meter = value; }
        public float PulseScale { get => pulseScale; set => pulseScale = value; }
        public float PulseDecay { get => pulseDecay; set => pulseDecay = value; }
        public Color Tint { get => tint; set => tint = value; }

        public override string Title => "Mash Meter";

        public override void DescribeTimeline(List<NoteTimelineItem> items) =>
            items.Add(NoteTimelineItem.Info("meter", NoteMoment.Spawned, NoteMoment.ReachedBeat, NoteTimelineColors.Info));

        public override void Begin(NoteViewContext context)
        {
            pulsing = pulseTarget != null ? pulseTarget : context.Note.transform;
            baseScale = pulsing.localScale;
            pulse = 0f;
            if (tint.a > 0f)
                foreach (SpriteRenderer sprite in context.Note.GetComponentsInChildren<SpriteRenderer>(true)) sprite.color = tint;
            SetMeter(0f);
        }

        public override void Moment(NoteViewContext context, NoteMoment moment)
        {
            if (moment == NoteMoment.Pressed)
            {
                pulse = 1f;
                SetMeter(context.MashProgress);
            }
            else if (moment == NoteMoment.Resolved && pulsing != null) pulsing.localScale = baseScale;
        }

        public override void Tick(NoteViewContext context)
        {
            if (pulse <= 0f || pulsing == null || context.Resolved) return;
            pulse = Mathf.Max(0f, pulse - pulseDecay * Time.deltaTime);
            pulsing.localScale = baseScale * (1f + pulseScale * pulse);
        }

        private void SetMeter(float value)
        {
            if (meter == null) return;
            Vector3 scale = meter.localScale;
            scale.y = Mathf.Clamp01(value);
            meter.localScale = scale;
        }
    }

    /// <summary>A sound and / or a camera shake on a moment (the strike landing, the hit, the miss...).</summary>
    [Serializable]
    public sealed class FeedbackView : NoteView
    {
        [Tooltip("When: a moment plus seconds (e.g. Beat for the strike landing, Hit, Missed).")]
        [SerializeField] private NoteCue at = new(NoteMoment.Hit);
        [SerializeField] private AudioClip sound;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Min(0f)] private float shake;
        [SerializeField, Min(0.01f)] private float shakeSeconds = 0.15f;

        public NoteCue At { get => at ??= new NoteCue(NoteMoment.Hit); set => at = value; }
        public AudioClip Sound { get => sound; set => sound = value; }
        public float Shake { get => shake; set => shake = Mathf.Max(0f, value); }

        public override void Begin(NoteViewContext context) => context.Cue(At, () => Play(context));

        public override string Title => sound != null ? "Feedback: " + sound.name : "Feedback";

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            string label = sound != null ? sound.name : shake > 0f ? "shake" : "feedback";
            if (shake > 0f)
                items.Add(NoteTimelineItem.Timed(label, At, shakeSeconds, value => shakeSeconds = Mathf.Max(0.01f, value), NoteTimelineColors.Feedback));
            else
                items.Add(NoteTimelineItem.Marker(label, At, NoteTimelineColors.Feedback));
        }

        private void Play(NoteViewContext context)
        {
            if (sound != null)
            {
                Vector3 at = context.Camera != null ? context.Camera.transform.position : context.Note.transform.position;
                AudioSource.PlayClipAtPoint(sound, at, volume);
            }
            if (shake > 0f) CombatCameraShaker.Shake(context.Camera, shake, shakeSeconds);
        }
    }
}
