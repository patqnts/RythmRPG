using UnityEngine;
using UnityEngine.Events;

namespace RythmRPG.Core
{
    /// <summary>
    /// Flies a <see cref="LightShadowProjector"/> shadow across the level: a dragon (or airship, or giant bird)
    /// passing overhead. The shadow travels from <see cref="StartPoint"/> to <see cref="EndPoint"/> (offsets from
    /// this object, so moving the object moves the whole path), turns so the image's top leads, and changes size,
    /// blur and darkness with altitude: high = larger, softer and fainter; low = small, crisp and dark. Optional
    /// wing flap pulses the width.
    /// <para>
    /// The object itself never moves: the flight is applied to the projector as a runtime override, so every
    /// setting you change on the projector (image, colour, strength, size, softness...) still applies during the
    /// flight. Outside a flight (and in Edit mode) the shadow sits at the object, exactly as the projector is set.
    /// </para>
    /// <para>Use the projector's DragonShadow preset (Straight Down or Sun direction). Call <see cref="Play"/> from a
    /// trigger or cutscene, or tick Play On Enable.</para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(LightShadowProjector))]
    [AddComponentMenu("Rythm RPG/Environment/Shadow Flyover")]
    public sealed class ShadowFlyover : MonoBehaviour
    {
        public enum LoopMode { Once, Loop, PingPong }

        [Header("Path (offsets from this object, world axes)")]
        [SerializeField] private Vector3 startPoint = new(-14f, 0f, -5f);
        [SerializeField] private Vector3 endPoint = new(14f, 0f, 7f);
        [Tooltip("Sideways swerve of the path at its middle (world units, to the right of travel).")]
        [SerializeField] private float curve = 2f;

        [Header("Timing")]
        [SerializeField, Min(0.05f)] private float duration = 3.5f;
        [SerializeField, Min(0f)] private float startDelay;
        [Tooltip("Progress along the path over time (0..1 both axes). Default: steady.")]
        [SerializeField] private AnimationCurve progress = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private LoopMode loop = LoopMode.Loop;
        [Tooltip("Pause between loops (seconds).")]
        [SerializeField, Min(0f)] private float loopGap = 2f;
        [SerializeField] private bool playOnEnable = true;
        [Tooltip("Play mode: hide the shadow while not flying. Off = it waits at this object between flights.")]
        [SerializeField] private bool hideWhenIdle = true;

        [Header("Heading")]
        [Tooltip("Turn so the top of the image points the way it flies.")]
        [SerializeField] private bool faceTravelDirection = true;

        [Header("Altitude (0 = low, 1 = high)")]
        [Tooltip("Altitude over the flight (x = 0..1 of the flight).")]
        [SerializeField] private AnimationCurve altitude = new(
            new Keyframe(0f, 0.7f), new Keyframe(0.5f, 0.25f), new Keyframe(1f, 0.8f));
        [Tooltip("Size multiplier when high.")]
        [SerializeField, Range(0.5f, 3f)] private float highSize = 1.35f;
        [Tooltip("Off (default) = the shadow keeps exactly the edge you set on the projector (crisp stays crisp).\n" +
                 "On = it gets blurrier the higher it flies (adds High Softness).")]
        [SerializeField] private bool blurWithAltitude;
        [Tooltip("Extra blur when high (world units). Only used when Blur With Altitude is on.")]
        [SerializeField, Range(0f, 2f)] private float highSoftness = 0.45f;
        [Tooltip("Darkness multiplier when high.")]
        [SerializeField, Range(0f, 1f)] private float highStrength = 0.45f;
        [Tooltip("Fade in / out at the ends of the flight (seconds).")]
        [SerializeField, Min(0f)] private float edgeFadeTime = 0.35f;

        [Header("Wing flap (use 0 with a flipbook)")]
        [SerializeField, Range(0f, 0.5f)] private float flapAmount = 0.12f;
        [SerializeField, Min(0f)] private float flapsPerSecond = 1.4f;

        [Header("Events")]
        [SerializeField] private UnityEvent started = new();
        [SerializeField] private UnityEvent finished = new();

        // 0 = points were world positions (first version), 1 = offsets from this object.
        [SerializeField, HideInInspector] private int pathVersion;

        private LightShadowProjector projector;
        private float clock = -1f; // < 0 = idle
        private bool reverse;
        private float gapLeft;
        private bool previewing;

        /// <summary>Start of the path, as an offset from this object.</summary>
        public Vector3 StartPoint { get => startPoint; set { startPoint = value; pathVersion = 1; } }
        /// <summary>End of the path, as an offset from this object.</summary>
        public Vector3 EndPoint { get => endPoint; set { endPoint = value; pathVersion = 1; } }
        public Vector3 WorldStart => transform.position + startPoint;
        public Vector3 WorldEnd => transform.position + endPoint;
        public float Curve { get => curve; set => curve = value; }
        public float Duration { get => duration; set => duration = Mathf.Max(0.05f, value); }
        public bool IsPlaying => clock >= 0f;
        public bool IsPreviewing => previewing;
        public UnityEvent Started => started;
        public UnityEvent Finished => finished;

        private LightShadowProjector Projector
        {
            get
            {
                if (projector == null) projector = GetComponent<LightShadowProjector>();
                return projector;
            }
        }

        private void Reset()
        {
            pathVersion = 1;
        }

        private void OnEnable()
        {
            MigratePath();
            if (Application.isPlaying && playOnEnable) Play();
            else ApplyIdle();
        }

        private void OnDisable()
        {
            previewing = false;
            clock = -1f;
            if (Projector == null) return;
            Projector.ClearRuntimeOverrides();
            Projector.Refresh();
        }

        /// <summary>Starts (or restarts) the flight from the start point.</summary>
        public void Play()
        {
            previewing = false;
            reverse = false;
            gapLeft = 0f;
            clock = 0f;
            started.Invoke();
            if (startDelay <= 0f) ApplyFlight(0f, true);
            else ApplyIdle();
        }

        /// <summary>Stops the flight (the shadow hides if Hide When Idle).</summary>
        public void Stop()
        {
            clock = -1f;
            ApplyIdle();
        }

        /// <summary>Edit-mode preview: shows the shadow at a point of the flight (t = 0..1) without fading.</summary>
        public void Preview(float t)
        {
            previewing = true;
            ApplyFlight(t, false);
        }

        /// <summary>Ends an Edit-mode preview: the shadow goes back to the object.</summary>
        public void EndPreview()
        {
            if (!previewing) return;
            previewing = false;
            ApplyIdle();
        }

        /// <summary>Applies the pose, size, blur and darkness for a point of the flight (t = 0..1).</summary>
        public void Evaluate(float t) => ApplyFlight(t, true);

        private void ApplyFlight(float t, bool fadeEnds)
        {
            LightShadowProjector p = Projector;
            if (p == null) return;
            t = Mathf.Clamp01(t);
            float u = Mathf.Clamp01(progress.Evaluate(t));
            float along = reverse ? 1f - u : u;

            Vector3 position = PathPoint(along);
            Vector3 ahead = PathPoint(Mathf.Clamp01(along + (reverse ? -0.01f : 0.01f)));
            Vector3 travel = ahead - position;
            if (travel.sqrMagnitude < 1e-8f) travel = reverse ? WorldStart - WorldEnd : WorldEnd - WorldStart;
            Vector3 heading = faceTravelDirection ? travel : transform.forward;
            p.SetPoseOverride(position, heading);

            float height = Mathf.Clamp01(altitude.Evaluate(u));
            float flap = flapAmount > 0f && flapsPerSecond > 0f
                ? 1f + flapAmount * Mathf.Sin(t * duration * flapsPerSecond * Mathf.PI * 2f)
                : 1f;
            float scale = Mathf.Lerp(1f, highSize, height);
            p.SizeMultiplier = new Vector2(scale * flap, scale);
            // Any extra softness switches the projector from crisp point sampling to a blur, so only when asked.
            p.ExtraSoftness = blurWithAltitude ? highSoftness * height : 0f;

            float fade = 1f;
            if (fadeEnds && edgeFadeTime > 0f)
            {
                float seconds = t * duration;
                fade = Mathf.Clamp01(seconds / edgeFadeTime) * Mathf.Clamp01((duration - seconds) / edgeFadeTime);
            }
            p.StrengthMultiplier = Mathf.Lerp(1f, highStrength, height) * fade;
            p.Refresh();
        }

        /// <summary>World position on the path (along = 0..1 from start to end).</summary>
        public Vector3 PathPoint(float along)
        {
            Vector3 a = WorldStart, b = WorldEnd;
            Vector3 straight = Vector3.Lerp(a, b, along);
            Vector3 dir = b - a;
            Vector3 right = Vector3.Cross(Vector3.up, new Vector3(dir.x, 0f, dir.z)).normalized;
            return straight + right * (curve * Mathf.Sin(along * Mathf.PI));
        }

        private void Update()
        {
            if (!Application.isPlaying || clock < 0f) return;

            if (gapLeft > 0f)
            {
                gapLeft -= Time.deltaTime;
                if (gapLeft > 0f) return;
                clock = 0f;
                started.Invoke();
            }

            clock += Time.deltaTime;
            float t = (clock - startDelay) / duration;
            if (t < 0f)
            {
                ApplyIdle();
                return;
            }
            if (t < 1f)
            {
                ApplyFlight(t, true);
                return;
            }

            ApplyFlight(1f, true);
            finished.Invoke();
            switch (loop)
            {
                case LoopMode.Loop:
                    gapLeft = Mathf.Max(0.0001f, loopGap);
                    clock = 0f;
                    ApplyIdle();
                    break;
                case LoopMode.PingPong:
                    reverse = !reverse;
                    gapLeft = Mathf.Max(0.0001f, loopGap);
                    clock = 0f;
                    ApplyIdle();
                    break;
                default:
                    clock = -1f;
                    ApplyIdle();
                    break;
            }
        }

        private void ApplyIdle()
        {
            LightShadowProjector p = Projector;
            if (p == null) return;
            p.ClearRuntimeOverrides();
            // Edit mode: the shadow shows at the object exactly as the projector is set, so it can be edited.
            if (hideWhenIdle && Application.isPlaying) p.StrengthMultiplier = 0f;
            p.Refresh();
        }

        // The first version stored world positions; convert them to offsets from this object once.
        private void MigratePath()
        {
            if (pathVersion >= 1) return;
            startPoint -= transform.position;
            endPoint -= transform.position;
            pathVersion = 1;
        }

        private void OnValidate()
        {
            if (progress == null || progress.length == 0) progress = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            if (altitude == null || altitude.length == 0) altitude = AnimationCurve.Constant(0f, 1f, 0.5f);
            MigratePath();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.6f, 1f, 0.9f);
            Vector3 previous = PathPoint(0f);
            for (int i = 1; i <= 24; i++)
            {
                Vector3 p = PathPoint(i / 24f);
                Gizmos.DrawLine(previous, p);
                previous = p;
            }
            Gizmos.DrawWireSphere(WorldStart, 0.25f);
            Gizmos.DrawSphere(WorldEnd, 0.18f);
        }
    }
}
