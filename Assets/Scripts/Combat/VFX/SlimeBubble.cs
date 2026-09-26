using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// The ability wisp as a slimy energy bubble (think Sam's energy bubble in Eastward). Its body is a cluster of
    /// overlapping pixel-metaball particles in local space, so the merged pixel outline is always a little lumpy and
    /// alive; this component makes it bouncy:
    /// <list type="bullet">
    /// <item>stretches along its screen-space velocity (and thins across it, keeping its volume), on a spring, so it
    /// overshoots and jiggles when it starts, turns and stops;</item>
    /// <item>breathes with a small idle wobble while it hovers;</item>
    /// <item>keeps a pixel shine on its upper-left;</item>
    /// <item>when the wisp pops (the <see cref="AbilityWisp"/> detaches it), the bubble vanishes at once and only its
    /// drips finish, so the pop effect takes over cleanly.</item>
    /// </list>
    /// Built by Tools > Rythm RPG > Combat > Create Slime Bubble Cast Effects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlimeBubble : MonoBehaviour, ICastEffectTint
    {
        [Tooltip("Child holding the bubble's local-space particle systems; it is turned toward the motion and stretched.")]
        [SerializeField] private Transform body;
        [Tooltip("Pixel highlight, kept on the upper-left of the bubble.")]
        [SerializeField] private SpriteRenderer shine;
        [Tooltip("Particle systems that are the bubble itself (cleared at once when it pops).")]
        [SerializeField] private ParticleSystem[] bubbleSystems = new ParticleSystem[0];
        [Tooltip("Particle systems that trail behind (drips); they are stopped and left to finish when it pops.")]
        [SerializeField] private ParticleSystem[] trailSystems = new ParticleSystem[0];

        [Header("Stretch (bouncy)")]
        [Tooltip("Extra length per world unit per second of speed on screen.")]
        [SerializeField, Min(0f)] private float stretchPerSpeed = 0.11f;
        [SerializeField, Range(1f, 3f)] private float maxStretch = 1.85f;
        [Tooltip("Spring stiffness of the stretch. Higher = snappier.")]
        [SerializeField, Min(1f)] private float stiffness = 240f;
        [Tooltip("Spring damping. Lower = more jiggle after it stops.")]
        [SerializeField, Min(0f)] private float damping = 11f;
        [Tooltip("How fast it turns toward its motion (per second).")]
        [SerializeField, Min(0f)] private float turnSpeed = 18f;

        [Header("Idle wobble")]
        [SerializeField, Range(0f, 0.3f)] private float wobble = 0.06f;
        [SerializeField, Min(0f)] private float wobbleSpeed = 7.5f;

        [Header("Colour")]
        [Tooltip("How much the ability's accent colour replaces the authored colours (0 = keep the prefab's colours).")]
        [SerializeField, Range(0f, 1f)] private float tintAmount = 0.6f;

        [Header("Shine")]
        [Tooltip("Shine position relative to the bubble, in bubble radii (x right, y up on screen).")]
        [SerializeField] private Vector2 shineOffset = new(-0.32f, 0.34f);
        [SerializeField, Min(0.01f)] private float radius = 0.25f;

        private Vector3 lastPosition;
        private Vector2 velocity;
        private float stretch = 1f;
        private float stretchSpeed;
        private float angle;
        private float time;
        private bool popped;
        private bool started;
        private Color[] authoredBubble;
        private Color[] authoredTrail;
        private Vector3 baseScale = Vector3.one;

        private void Awake()
        {
            if (body == null) body = transform;
            baseScale = body.localScale;
            authoredBubble = SlimeVfx.CaptureStartColors(bubbleSystems);
            authoredTrail = SlimeVfx.CaptureStartColors(trailSystems);
        }

        public void ApplyTint(Color accent)
        {
            if (authoredBubble == null) Awake();
            SlimeVfx.Tint(bubbleSystems, authoredBubble, accent, tintAmount);
            SlimeVfx.Tint(trailSystems, authoredTrail, accent, tintAmount);
        }

        private void OnEnable()
        {
            started = false;
            popped = false;
        }

        // AbilityWisp.Pop detaches the prefab from the wisp before destroying it: that is the pop.
        private void OnTransformParentChanged()
        {
            if (!started || popped || transform.parent != null) return;
            Pop();
        }

        /// <summary>The bubble bursts: its body disappears at once, the drips finish.</summary>
        public void Pop()
        {
            popped = true;
            foreach (ParticleSystem system in bubbleSystems)
                if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (ParticleSystem system in trailSystems)
                if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (shine != null) shine.enabled = false;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Camera view = SlimeVfx.ViewCamera();
            Quaternion facing = SlimeVfx.Facing(view);
            Vector3 right = facing * Vector3.right;
            Vector3 up = facing * Vector3.up;

            Vector3 position = transform.position;
            if (!started)
            {
                started = true;
                lastPosition = position;
            }
            Vector3 delta = (position - lastPosition) / dt;
            lastPosition = position;
            var screenVelocity = new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, up));
            velocity = Vector2.Lerp(velocity, screenVelocity, 1f - Mathf.Exp(-dt * 20f));
            float speed = velocity.magnitude;

            // Spring toward the speed-based stretch: overshoots on start/stop, so it squashes and jiggles.
            float target = 1f + Mathf.Min(maxStretch - 1f, speed * stretchPerSpeed);
            stretchSpeed += (target - stretch) * stiffness * dt;
            stretchSpeed *= Mathf.Exp(-damping * dt);
            stretch = Mathf.Clamp(stretch + stretchSpeed * dt, 0.55f, maxStretch + 0.4f);

            if (speed > 0.25f)
            {
                float wanted = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                angle = Mathf.LerpAngle(angle, wanted, 1f - Mathf.Exp(-dt * turnSpeed));
            }

            time += dt;
            float breathe = 1f + wobble * Mathf.Sin(time * wobbleSpeed);
            float across = 1f / Mathf.Sqrt(Mathf.Max(0.2f, stretch));
            if (body != transform)
            {
                body.rotation = facing * Quaternion.Euler(0f, 0f, angle);
                body.localScale = new Vector3(baseScale.x * stretch * breathe, baseScale.y * across / breathe, baseScale.z);
            }

            if (shine != null && !popped)
            {
                Vector3 offset = right * (shineOffset.x * radius) + up * (shineOffset.y * radius * across);
                shine.transform.SetPositionAndRotation(position + offset, facing);
            }
        }
    }
}
