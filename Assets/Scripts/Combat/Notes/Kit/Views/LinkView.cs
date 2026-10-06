using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Draws something between two anchors: a chain from a mimic's mouth to its tongue-note, a tether, a beam, a rope.
    /// It can grow toward its end while the note approaches, sag, wiggle, and retract when the note resolves. Drawn as a
    /// line (LineRenderer, tiled texture for chains) or as repeated segment prefabs (chain links).
    /// </summary>
    [Serializable]
    public sealed class LinkView : NoteView
    {
        public enum Style
        {
            /// <summary>A LineRenderer (set a tiling material for chains / ropes).</summary>
            Line,
            /// <summary>Copies of a segment prefab (chain links) along the way.</summary>
            Segments
        }

        public enum Reach
        {
            /// <summary>Always all the way to the end anchor.</summary>
            Full,
            /// <summary>Grows with the approach: from the start at spawn to the end on the beat.</summary>
            GrowWithApproach,
            /// <summary>Grows over Grow Seconds after it appears.</summary>
            GrowOverSeconds
        }

        [Header("Ends")]
        [SerializeField] private NoteAnchor from = NoteAnchor.Source;
        [Tooltip("Socket on the Source / Target, e.g. Mouth.")]
        [SerializeField] private string fromSocket = "Mouth";
        [Tooltip("Lane space: X side, Y up, Z forward toward the hit line.")]
        [SerializeField] private Vector3 fromOffset;
        [SerializeField] private NoteAnchor to = NoteAnchor.Note;
        [SerializeField] private string toSocket = string.Empty;
        [SerializeField] private Vector3 toOffset;

        [Header("Timing")]
        [Tooltip("When it appears: a moment plus seconds.")]
        [SerializeField] private NoteCue showAt = new(NoteMoment.Spawned);
        [SerializeField] private Reach reach = Reach.Full;
        [SerializeField, Min(0.01f)] private float growSeconds = 0.25f;
        [Tooltip("How reach (0-1) maps to length (0-1).")]
        [SerializeField] private AnimationCurve growCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("When it pulls back. None = only when the note goes away.")]
        [SerializeField] private NoteCue retractAt = new(NoteMoment.Resolved);
        [SerializeField, Min(0f)] private float retractSeconds = 0.2f;

        [Header("Shape")]
        [SerializeField, Range(2, 64)] private int points = 16;
        [Tooltip("Droop in the middle, as a share of the length.")]
        [SerializeField] private float sag;
        [Tooltip("Sideways wiggle (world units).")]
        [SerializeField] private float waveAmplitude;
        [Tooltip("Wiggles along the whole length.")]
        [SerializeField] private float waveFrequency = 2f;
        [Tooltip("Wiggle speed (cycles per second).")]
        [SerializeField] private float waveSpeed = 1f;

        [Header("Look")]
        [SerializeField] private Style style = Style.Line;
        [SerializeField] private Material material;
        [SerializeField, Min(0.001f)] private float width = 0.08f;
        [SerializeField] private Gradient colors = new();
        [Tooltip("Multiply the colours by the projectile's colour.")]
        [SerializeField] private bool tintWithNoteColor;
        [SerializeField] private LineTextureMode textureMode = LineTextureMode.Tile;
        [Tooltip("Tile mode: texture repeats per world unit.")]
        [SerializeField, Min(0.01f)] private float tilesPerUnit = 4f;
        [SerializeField] private int sortingOrder = 450;
        [SerializeField] private GameObject segmentPrefab;
        [SerializeField, Min(0.01f)] private float segmentSpacing = 0.2f;
        [Tooltip("Turn each segment along the link (its right axis).")]
        [SerializeField] private bool alignSegments = true;

        [NonSerialized] private GameObject root;
        [NonSerialized] private LineRenderer line;
        [NonSerialized] private List<Transform> segments;
        [NonSerialized] private Vector3[] buffer;
        [NonSerialized] private bool visible;
        [NonSerialized] private float shownAt;
        [NonSerialized] private float retractStarted = -1f;
        [NonSerialized] private float retractFrom;
        [NonSerialized] private float currentReach;

        private static Material defaultMaterial;

        public NoteAnchor From { get => from; set => from = value; }
        public string FromSocket { get => fromSocket; set => fromSocket = value ?? string.Empty; }
        public NoteAnchor To { get => to; set => to = value; }
        public Reach ReachMode { get => reach; set => reach = value; }
        public Style DrawStyle { get => style; set => style = value; }
        public Material LineMaterial { get => material; set => material = value; }
        public GameObject SegmentPrefab { get => segmentPrefab; set => segmentPrefab = value; }
        public NoteCue ShowAt { get => showAt ??= new NoteCue(NoteMoment.Spawned); set => showAt = value; }
        public NoteCue RetractAt { get => retractAt ??= new NoteCue(NoteMoment.Resolved); set => retractAt = value; }
        public float GrowSeconds { get => growSeconds; set => growSeconds = Mathf.Max(0.01f, value); }
        public float RetractSeconds { get => retractSeconds; set => retractSeconds = Mathf.Max(0f, value); }

        public override float Linger => retractSeconds;

        public override void Begin(NoteViewContext context)
        {
            visible = false;
            retractStarted = -1f;
            currentReach = 0f;
            context.Cue(ShowAt, () =>
            {
                if (!visible) Show(context);
            });
            if (RetractAt.moment != NoteMoment.None) context.Cue(RetractAt, StartRetract);
        }

        private void StartRetract()
        {
            if (!visible || retractStarted >= 0f) return;
            retractStarted = Time.time;
            retractFrom = currentReach;
        }

        public override void Tick(NoteViewContext context)
        {
            if (!visible) return;
            float amount = LinkMath.Reach(reach, context.Approach, Time.time - shownAt, growSeconds, growCurve);
            if (retractStarted >= 0f)
            {
                amount = LinkMath.Retract(retractFrom, Time.time - retractStarted, retractSeconds);
                if (amount <= 0f)
                {
                    Hide();
                    return;
                }
            }
            currentReach = amount;
            Draw(context, amount);
        }

        public override void End(NoteViewContext context) => Hide();

        private void Show(NoteViewContext context)
        {
            visible = true;
            shownAt = Time.time;
            root = new GameObject("Note Link");
            if (style == Style.Line)
            {
                line = root.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.widthMultiplier = width;
                line.textureMode = textureMode;
                line.numCapVertices = 0;
                line.sortingOrder = sortingOrder;
                line.sharedMaterial = material != null ? material : DefaultMaterial();
                Gradient gradient = colors ?? new Gradient();
                line.colorGradient = tintWithNoteColor ? Tint(gradient, context.Color) : gradient;
            }
            else
            {
                segments ??= new List<Transform>();
                segments.Clear();
            }
        }

        private void Hide()
        {
            visible = false;
            if (root != null) Object.Destroy(root);
            root = null;
            line = null;
            segments?.Clear();
        }

        private void Draw(NoteViewContext context, float amount)
        {
            Vector3 a = context.Position(from, fromSocket, fromOffset);
            Vector3 b = context.Position(to, toSocket, toOffset);
            int count = Mathf.Max(2, points);
            if (buffer == null || buffer.Length != count) buffer = new Vector3[count];
            Vector3 end = Vector3.LerpUnclamped(a, b, amount);
            NoteSpawnOffset.LaneFrame(end - a, out Vector3 side, out _, out _);
            float length = Vector3.Distance(a, end);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                buffer[i] = LinkMath.Point(a, end, t, sag * length, side, waveAmplitude, waveFrequency, Time.time * waveSpeed);
            }

            if (style == Style.Line && line != null)
            {
                line.positionCount = count;
                line.SetPositions(buffer);
                if (textureMode == LineTextureMode.Tile) line.textureScale = new Vector2(tilesPerUnit, 1f);
                return;
            }
            DrawSegments(length);
        }

        private void DrawSegments(float length)
        {
            if (segmentPrefab == null || root == null) return;
            int needed = Mathf.Clamp(Mathf.FloorToInt(length / segmentSpacing) + 1, 0, 256);
            while (segments.Count < needed)
            {
                GameObject segment = Object.Instantiate(segmentPrefab, root.transform);
                segments.Add(segment.transform);
            }
            float travelled = 0f;
            int index = 0;
            for (int i = 1; i < buffer.Length && index < needed; i++)
            {
                Vector3 p0 = buffer[i - 1];
                Vector3 p1 = buffer[i];
                float piece = Vector3.Distance(p0, p1);
                while (index < needed && travelled + piece >= index * segmentSpacing)
                {
                    float along = piece <= 0.0001f ? 0f : (index * segmentSpacing - travelled) / piece;
                    Transform segment = segments[index];
                    segment.gameObject.SetActive(true);
                    segment.position = Vector3.Lerp(p0, p1, along);
                    if (alignSegments && piece > 0.0001f) segment.right = p1 - p0;
                    index++;
                }
                travelled += piece;
            }
            for (int i = index; i < segments.Count; i++)
                if (segments[i] != null) segments[i].gameObject.SetActive(false);
        }

        private static Gradient Tint(Gradient source, Color tint)
        {
            var result = new Gradient();
            GradientColorKey[] keys = source.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color *= tint;
            result.SetKeys(keys, source.alphaKeys);
            return result;
        }

        private static Material DefaultMaterial()
        {
            if (defaultMaterial != null) return defaultMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) defaultMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            return defaultMaterial;
        }

        public override IEnumerable<string> Validate(CombatNote note)
        {
            if (style == Style.Segments && segmentPrefab == null) yield return "Link View: Segments style needs a Segment Prefab.";
            if (from == to && fromSocket == toSocket && fromOffset == toOffset) yield return "Link View: both ends are the same point.";
        }

        public override string Title => "Link " + from + (string.IsNullOrEmpty(fromSocket) ? "" : "/" + fromSocket) + " > " + to;

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            if (RetractAt.moment != NoteMoment.None)
                items.Add(NoteTimelineItem.Span("link", ShowAt, RetractAt, NoteTimelineColors.Link));
            else
                items.Add(NoteTimelineItem.Marker("link", ShowAt, NoteTimelineColors.Link));
            if (reach == Reach.GrowOverSeconds)
                items.Add(NoteTimelineItem.Timed("grow", ShowAt, growSeconds, value => GrowSeconds = value, NoteTimelineColors.Link));
            else if (reach == Reach.GrowWithApproach)
                items.Add(NoteTimelineItem.Info("grow with approach", NoteMoment.Spawned, NoteMoment.ReachedBeat, NoteTimelineColors.Link));
            if (RetractAt.moment != NoteMoment.None && retractSeconds > 0f)
                items.Add(NoteTimelineItem.Timed("retract", RetractAt, retractSeconds, value => RetractSeconds = value, NoteTimelineColors.Link));
        }
    }

    /// <summary>The link's maths (kept separate so it can be tested).</summary>
    public static class LinkMath
    {
        /// <summary>How far along (0-1) the link reaches.</summary>
        public static float Reach(LinkView.Reach mode, float approach, float secondsShown, float growSeconds, AnimationCurve curve)
        {
            float t = mode switch
            {
                LinkView.Reach.GrowWithApproach => Mathf.Clamp01(approach),
                LinkView.Reach.GrowOverSeconds => Mathf.Clamp01(secondsShown / Mathf.Max(0.01f, growSeconds)),
                _ => 1f
            };
            if (mode == LinkView.Reach.Full || curve == null || curve.length == 0) return t;
            return Mathf.Clamp01(curve.Evaluate(t));
        }

        /// <summary>Retracting: from <paramref name="from"/> down to 0 over <paramref name="seconds"/>, accelerating.</summary>
        public static float Retract(float from, float elapsed, float seconds)
        {
            if (seconds <= 0f) return 0f;
            float t = Mathf.Clamp01(elapsed / seconds);
            return Mathf.Max(0f, from * (1f - t * t));
        }

        /// <summary>A point on the link: straight from a to b, drooping by <paramref name="sagAmount"/> in the middle and
        /// wiggling sideways (zero at both ends).</summary>
        public static Vector3 Point(Vector3 a, Vector3 b, float t, float sagAmount, Vector3 side, float waveAmplitude,
            float waveFrequency, float phase)
        {
            Vector3 p = Vector3.LerpUnclamped(a, b, t);
            float bell = 4f * t * (1f - t);
            p += Vector3.down * (sagAmount * bell);
            if (waveAmplitude != 0f)
                p += side * (Mathf.Sin((t * waveFrequency + phase) * Mathf.PI * 2f) * waveAmplitude * bell);
            return p;
        }
    }
}
