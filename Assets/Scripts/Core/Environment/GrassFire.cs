using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Sets <see cref="GrassField"/> grass on fire (or puts it out) in a shape that matches the effect:
    /// <list type="bullet">
    /// <item><b>Sphere</b>: a torch, a fireball, a campfire.</item>
    /// <item><b>Cone</b>: a flamethrower, a fire breath. It starts at this object and points along its forward
    /// (blue) axis, so put it on the nozzle.</item>
    /// <item><b>Box</b> / <b>Capsule</b>: a wall of fire, a burning trail, a beam.</item>
    /// <item><b>Particles</b>: burns exactly where the particles of a ParticleSystem (your flamethrower effect)
    /// come down onto the grass, so the fire follows the visual wherever it goes.</item>
    /// </list>
    /// With <see cref="Action.Extinguish"/> it puts fire out instead (a water hose, rain, a splash). The fire then
    /// spreads by itself, pushed by the field's wind, and the grass grows back later.
    /// </summary>
    [AddComponentMenu("Rythm RPG/Environment/Grass Fire")]
    public sealed class GrassFire : MonoBehaviour
    {
        public enum Mode { WhileEnabled, OnEnable, Manual }
        public enum Action { Ignite, Extinguish }
        public enum Shape { Sphere, Cone, Box, Capsule, Particles }

        [SerializeField] private Action action = Action.Ignite;
        [Tooltip("While Enabled: keeps acting every Interval. On Enable: once when switched on. Manual: call Apply().")]
        [SerializeField] private Mode mode = Mode.WhileEnabled;
        [Tooltip("Seconds between two applications in While Enabled mode.")]
        [SerializeField, Min(0f)] private float interval = 0.15f;

        [Header("Shape")]
        [SerializeField] private Shape shape = Shape.Sphere;
        [Tooltip("Offset of the shape from this object's pivot (in its local space), e.g. to the nozzle's tip.")]
        [SerializeField] private Vector3 offset;
        [Tooltip("On: burns everything under the shape as seen from above (what the player reads on screen). " +
                 "Off: only grass the shape really touches in 3D.")]
        [SerializeField] private bool footprint = true;
        [Tooltip("Sphere / Capsule radius, and the size of each particle's touch in Particles mode.")]
        [SerializeField, Min(0.02f)] private float radius = 0.5f;
        [Tooltip("Cone: how far the flame reaches. Capsule: its length along forward.")]
        [SerializeField, Min(0f)] private float length = 3.5f;
        [Tooltip("Cone: full opening angle in degrees.")]
        [SerializeField, Range(1f, 160f)] private float coneAngle = 28f;
        [Tooltip("Cone: width at the nozzle.")]
        [SerializeField, Min(0f)] private float coneStartRadius = 0.15f;
        [Tooltip("Box: size along this object's right / up / forward, centred on the pivot + offset.")]
        [SerializeField] private Vector3 boxSize = new(1f, 1f, 3f);

        [Header("Particles mode")]
        [Tooltip("The particle system to follow (empty = the first one on this object or its children).")]
        [SerializeField] private ParticleSystem particles;
        [Tooltip("A particle burns grass only within this height above the grass (it has come down to the ground).")]
        [SerializeField, Min(0f)] private float particleReach = 0.6f;
        [Tooltip("Ignore particles younger than this share of their life (e.g. still inside the nozzle's flash).")]
        [SerializeField, Range(0f, 1f)] private float minParticleAge = 0.1f;
        [Tooltip("Most particles checked per application (spread across frames), to keep big effects cheap.")]
        [SerializeField, Range(4, 256)] private int particlesPerCheck = 48;

        [Header("Sphere mode")]
        [Tooltip("Higher than this above the grass the sphere has no effect (a fireball flying over).")]
        [SerializeField, Min(0f)] private float maxHeightAboveGrass = 1f;

        private float nextTime;
        private ParticleSystem.Particle[] particleBuffer;
        private int particleCursor;

        public float Radius { get => radius; set => radius = Mathf.Max(0.02f, value); }
        public float Length { get => length; set => length = Mathf.Max(0f, value); }
        public float ConeAngle { get => coneAngle; set => coneAngle = Mathf.Clamp(value, 1f, 160f); }
        public Shape AreaShape { get => shape; set => shape = value; }
        public Vector3 Position => transform.TransformPoint(offset);

        /// <summary>The shape as it is right now (not used in Particles mode).</summary>
        public GrassArea Area
        {
            get
            {
                Vector3 position = Position;
                Vector3 forward = transform.forward;
                switch (shape)
                {
                    case Shape.Cone: return GrassArea.Cone(position, forward, length, coneAngle, coneStartRadius, footprint);
                    case Shape.Box: return GrassArea.Box(position, transform.rotation, boxSize, footprint);
                    case Shape.Capsule: return GrassArea.Capsule(position, position + forward * length, radius, footprint);
                    default: return GrassArea.Sphere(position, radius, footprint);
                }
            }
        }

        private void OnEnable()
        {
            nextTime = 0f;
            if (mode == Mode.OnEnable) Apply();
        }

        private void Update()
        {
            if (mode != Mode.WhileEnabled || Time.time < nextTime) return;
            nextTime = Time.time + interval;
            Apply();
        }

        /// <summary>Ignites (or extinguishes) the grass in the shape now. Returns how many tufts were affected.</summary>
        public int Apply()
        {
            if (shape == Shape.Particles) return ApplyParticles();
            if (shape == Shape.Sphere && Grass.HeightAboveGrass(Position) > maxHeightAboveGrass) return 0;
            GrassArea area = Area;
            return action == Action.Ignite ? Grass.Ignite(area) : Grass.Extinguish(area);
        }

        // Every live particle close enough to the grass burns (or puts out) a small circle under it.
        private int ApplyParticles()
        {
            if (particles == null) particles = GetComponentInChildren<ParticleSystem>();
            if (particles == null) return 0;
            ParticleSystem.MainModule main = particles.main;
            int capacity = Mathf.Max(1, main.maxParticles);
            if (particleBuffer == null || particleBuffer.Length < capacity) particleBuffer = new ParticleSystem.Particle[capacity];
            int count = particles.GetParticles(particleBuffer);
            if (count == 0) return 0;

            Transform space = null;
            if (main.simulationSpace == ParticleSystemSimulationSpace.Local) space = particles.transform;
            else if (main.simulationSpace == ParticleSystemSimulationSpace.Custom) space = main.customSimulationSpace;

            // Look at an evenly spread subset, shifting it every time so all particles get their turn.
            int step = Mathf.Max(1, Mathf.CeilToInt(count / (float)particlesPerCheck));
            particleCursor = (particleCursor + 1) % step;
            int affected = 0;
            for (int i = particleCursor; i < count; i += step)
            {
                ParticleSystem.Particle particle = particleBuffer[i];
                float life = Mathf.Max(0.0001f, particle.startLifetime);
                if (1f - particle.remainingLifetime / life < minParticleAge) continue;
                Vector3 position = space != null ? space.TransformPoint(particle.position) : particle.position;
                if (Grass.HeightAboveGrass(position) > particleReach) continue;
                affected += action == Action.Ignite ? Grass.Ignite(position, radius) : Grass.Extinguish(position, radius);
            }
            return affected;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = action == Action.Ignite ? new Color(1f, 0.5f, 0.1f, 0.9f) : new Color(0.3f, 0.6f, 1f, 0.9f);
            if (shape != Shape.Particles)
            {
                Area.DrawGizmo();
                return;
            }
            // Particles mode: show the touch size at the effect's origin.
            ParticleSystem system = particles != null ? particles : GetComponentInChildren<ParticleSystem>();
            Gizmos.DrawWireSphere(system != null ? system.transform.position : Position, radius);
        }
    }
}
