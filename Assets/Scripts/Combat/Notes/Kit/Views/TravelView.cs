using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Flies an object from one point to another between two cues: a sword thrown from the enemy's hand that lands on the
    /// lane on the beat, a bomb lobbed in an arc, a dagger... The object is a child of the note (or spawned from a prefab).
    /// It becomes the note's <see cref="NoteAnchor.Projectile"/>, so other views can follow it (e.g. an Actor Move View
    /// with Destination = Projectile and Move Seconds 0 teleports the attacker to it: a warp strike).
    /// </summary>
    [Serializable]
    public sealed class TravelView : NoteView
    {
        public enum Facing
        {
            /// <summary>Leave its rotation alone.</summary>
            Keep,
            /// <summary>Point along the flight on screen (the art points right).</summary>
            AlongPath,
            /// <summary>Spin on screen while it flies.</summary>
            Spin
        }

        [Tooltip("The flying object: a child of the note (e.g. a sword sprite). Empty = spawn Prefab instead.")]
        [SerializeField] private Transform visual;
        [SerializeField] private GameObject prefab;

        [Header("From / To")]
        [SerializeField] private NoteAnchor from = NoteAnchor.Source;
        [Tooltip("Socket on the anchor (Hand, Mouth...). Empty = the anchor itself.")]
        [SerializeField] private string fromSocket = string.Empty;
        [Tooltip("Lane space: X side, Y up, Z forward toward the hit line.")]
        [SerializeField] private Vector3 fromOffset = new(0f, 0.6f, 0.25f);
        [SerializeField] private NoteAnchor to = NoteAnchor.HitPoint;
        [SerializeField] private string toSocket = string.Empty;
        [SerializeField] private Vector3 toOffset = new(0f, 0.45f, 0f);

        [Header("Timing")]
        [Tooltip("When it leaves (e.g. the throw animation's release frame).")]
        [SerializeField] private NoteCue departAt = new(NoteMoment.Spawned, 0.2f);
        [Tooltip("When it gets there. Spawn / Beat / Hold End cues follow the chart, so it lands exactly on the beat.")]
        [SerializeField] private NoteCue arriveAt = new(NoteMoment.ReachedBeat);
        [Tooltip("Flight time when Depart or Arrive is not a planned moment (Pressed, Hit...).")]
        [SerializeField, Min(0.01f)] private float fallbackSeconds = 0.4f;
        [SerializeField] private AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Arc height in world units (0 = straight line).")]
        [SerializeField, Min(0f)] private float arcHeight = 0.35f;

        [Header("Look")]
        [SerializeField] private Facing facing = Facing.AlongPath;
        [Tooltip("Degrees added to the facing (if the art does not point right).")]
        [SerializeField] private float angleOffset;
        [SerializeField] private float spinDegreesPerSecond = 1080f;
        [Tooltip("Hidden until it leaves (it is in the thrower's hand).")]
        [SerializeField] private bool hideBeforeDepart = true;
        [Tooltip("When it disappears (e.g. the attacker warps to it). None = it stays until the note goes.")]
        [SerializeField] private NoteCue hideAt = new(NoteMoment.ReachedBeat, -0.1f);

        [NonSerialized] private Transform mover;
        [NonSerialized] private GameObject spawned;
        [NonSerialized] private bool chartTimed;
        [NonSerialized] private double departChart;
        [NonSerialized] private double arriveChart;
        [NonSerialized] private float departReal = -1f;
        [NonSerialized] private bool departed;
        [NonSerialized] private bool hidden;
        [NonSerialized] private Vector3 lastPosition;
        [NonSerialized] private Vector3 lastDirection;
        [NonSerialized] private Quaternion restRotation;

        public Transform Visual { get => visual; set => visual = value; }
        public GameObject Prefab { get => prefab; set => prefab = value; }
        public NoteAnchor From { get => from; set => from = value; }
        public string FromSocket { get => fromSocket; set => fromSocket = value ?? string.Empty; }
        public Vector3 FromOffset { get => fromOffset; set => fromOffset = value; }
        public NoteAnchor To { get => to; set => to = value; }
        public string ToSocket { get => toSocket; set => toSocket = value ?? string.Empty; }
        public Vector3 ToOffset { get => toOffset; set => toOffset = value; }
        public NoteCue DepartAt { get => departAt ??= new NoteCue(NoteMoment.Spawned, 0.2f); set => departAt = value; }
        public NoteCue ArriveAt { get => arriveAt ??= new NoteCue(NoteMoment.ReachedBeat); set => arriveAt = value; }
        public NoteCue HideAt { get => hideAt ??= new NoteCue(NoteMoment.None); set => hideAt = value; }
        public float ArcHeight { get => arcHeight; set => arcHeight = Mathf.Max(0f, value); }
        public Facing FacingMode { get => facing; set => facing = value; }
        public bool HideBeforeDepart { get => hideBeforeDepart; set => hideBeforeDepart = value; }
        public float FallbackSeconds { get => fallbackSeconds; set => fallbackSeconds = Mathf.Max(0.01f, value); }

        public override void Begin(NoteViewContext context)
        {
            spawned = null;
            mover = visual;
            if (mover == null && prefab != null)
            {
                spawned = Object.Instantiate(prefab, context.Position(from, fromSocket, fromOffset), prefab.transform.rotation, context.EffectParent);
                context.FaceCamera(spawned);
                mover = spawned.transform;
            }
            if (mover == null) return;
            if (context.Projectile == null) context.Projectile = mover;
            restRotation = mover.rotation;
            departed = false;
            hidden = false;
            departReal = -1f;
            lastDirection = Vector3.zero;
            chartTimed = DepartAt.IsScheduled && ArriveAt.IsScheduled;
            departChart = NoteCueScheduler.BaseTime(DepartAt.moment, context.SpawnTime, context.HitTime, context.EndTime) + DepartAt.EffectiveOffset;
            arriveChart = NoteCueScheduler.BaseTime(ArriveAt.moment, context.SpawnTime, context.HitTime, context.EndTime) + ArriveAt.EffectiveOffset;
            if (!chartTimed) context.Cue(DepartAt, () => departReal = Time.time);
            if (HideAt.moment != NoteMoment.None)
                context.Cue(HideAt, () =>
                {
                    hidden = true;
                    Show(false);
                });
            mover.position = context.Position(from, fromSocket, fromOffset);
            lastPosition = mover.position;
            Show(!hideBeforeDepart);
        }

        public override void Tick(NoteViewContext context)
        {
            if (mover == null) return;
            Vector3 start = context.Position(from, fromSocket, fromOffset);
            Vector3 end = context.Position(to, toSocket, toOffset);
            float t;
            if (chartTimed)
            {
                if (context.Now < departChart)
                {
                    Hold(start);
                    return;
                }
                t = ActorMath.MoveProgress(context.Now, departChart, arriveChart);
            }
            else
            {
                if (departReal < 0f)
                {
                    Hold(start);
                    return;
                }
                t = ActorMath.MoveProgress(Time.time, departReal, departReal + fallbackSeconds);
            }

            if (!departed)
            {
                departed = true;
                if (!hidden) Show(true);
                foreach (TrailRenderer trail in mover.GetComponentsInChildren<TrailRenderer>(true)) trail.Clear();
            }
            float eased = curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
            Vector3 position = Vector3.LerpUnclamped(start, end, eased) + context.LaneOffset(new Vector3(0f, ActorMath.Hop(eased, arcHeight), 0f));
            Vector3 moved = position - lastPosition;
            if (moved.sqrMagnitude > 0.0000001f) lastDirection = moved;
            else if (lastDirection == Vector3.zero) lastDirection = end - start;
            mover.position = position;
            lastPosition = position;
            Face(context, t);
        }

        public override void End(NoteViewContext context)
        {
            if (spawned != null) Object.Destroy(spawned);
            spawned = null;
        }

        // Before it leaves: in the thrower's hand.
        private void Hold(Vector3 start)
        {
            mover.position = start;
            lastPosition = start;
        }

        private void Face(NoteViewContext context, float t)
        {
            Camera camera = context.Camera;
            // Notes and spawned effects draw camera-facing copies of their sprites (Rhythm Note Visual Layer), which take
            // only the roll (world Z) from the original: give it the on-screen angle as a plain Z rotation then.
            bool faced = mover.GetComponentInParent<RhythmNoteVisualLayer>() != null;
            switch (facing)
            {
                case Facing.AlongPath:
                    if (lastDirection == Vector3.zero) return;
                    if (camera != null)
                    {
                        Transform view = camera.transform;
                        float angle = Mathf.Atan2(Vector3.Dot(lastDirection, view.up), Vector3.Dot(lastDirection, view.right)) * Mathf.Rad2Deg;
                        Quaternion roll = Quaternion.Euler(0f, 0f, angle + angleOffset);
                        mover.rotation = faced ? roll : view.rotation * roll;
                    }
                    else mover.right = lastDirection;
                    break;
                case Facing.Spin:
                    Quaternion spin = Quaternion.Euler(0f, 0f, angleOffset - spinDegreesPerSecond * Time.time);
                    mover.rotation = faced ? spin : (camera != null ? camera.transform.rotation : restRotation) * spin;
                    break;
            }
        }

        private void Show(bool shown)
        {
            if (mover == null) return;
            foreach (Renderer renderer in mover.GetComponentsInChildren<Renderer>(true)) renderer.enabled = shown;
            if (!shown)
                foreach (TrailRenderer trail in mover.GetComponentsInChildren<TrailRenderer>(true)) trail.emitting = false;
            else
                foreach (TrailRenderer trail in mover.GetComponentsInChildren<TrailRenderer>(true)) trail.emitting = true;
        }

        public override IEnumerable<string> Validate(CombatNote note)
        {
            if (visual == null && prefab == null) yield return "Travel View: nothing to fly (set Visual to a child of the note, or a Prefab).";
            else if (visual != null && note != null && !visual.IsChildOf(note.transform)) yield return "Travel View: Visual must be part of the note prefab.";
            if (visual != null && note != null && visual == note.transform) yield return "Travel View: Visual must be a child, not the note itself.";
        }

        public override string Title => "Travel: " + (visual != null ? visual.name : prefab != null ? prefab.name : "nothing");

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            string label = $"fly {from} > {to}";
            if (DepartAt.IsScheduled && ArriveAt.IsScheduled) items.Add(NoteTimelineItem.Span(label, DepartAt, ArriveAt, NoteTimelineColors.Effect));
            else items.Add(NoteTimelineItem.Timed(label, DepartAt, fallbackSeconds, value => FallbackSeconds = value, NoteTimelineColors.Effect));
            if (HideAt.moment != NoteMoment.None) items.Add(NoteTimelineItem.Marker("hide", HideAt, NoteTimelineColors.Effect));
        }
    }
}
