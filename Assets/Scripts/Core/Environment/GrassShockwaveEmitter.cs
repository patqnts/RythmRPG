using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Sends a shockwave through <see cref="GrassField"/> grass: a wave front that pushes the grass flat and lets it
    /// spring back behind it, optionally cutting (at a height you choose), igniting or blowing out fire as it passes.
    /// <list type="bullet">
    /// <item><b>Ring</b>: grows in every direction (a ground slam, an explosion, a landing).</item>
    /// <item><b>Cone</b>: a slice of the ring along this object's forward axis (a blast, a roar, a wide swing).</item>
    /// <item><b>Line</b>: a straight wall moving along forward (a charge, a gust, a beam front).</item>
    /// </list>
    /// The cut / fire effects use the same shape. Call <see cref="Emit()"/> from code, an animation event or a
    /// UnityEvent (for example on the beat), or let it emit on enable / on a timer.
    /// </summary>
    [AddComponentMenu("Rythm RPG/Environment/Grass Shockwave Emitter")]
    public sealed class GrassShockwaveEmitter : MonoBehaviour
    {
        public enum CutHeightMode
        {
            /// <summary>The Grass Field's Stubble Height.</summary>
            FieldDefault,
            /// <summary>Cut Height = world units above the ground.</summary>
            AboveGround,
            /// <summary>Cut Height = a fraction of every tuft (0 = ground, 1 = tip).</summary>
            FractionOfTuft,
            /// <summary>At this object's world height, plus Cut Height.</summary>
            AtEmitter
        }

        [Header("Shape")]
        [SerializeField] private GrassShockwaveShape shape = GrassShockwaveShape.Ring;
        [Tooltip("How far the wave travels before fading out (world units). For a ring or cone this is its radius.")]
        [SerializeField, Min(0.1f)] private float maxRadius = 5f;
        [Tooltip("Cone: full opening angle in degrees.")]
        [SerializeField, Range(5f, 360f)] private float coneAngle = 70f;
        [Tooltip("Line: how long the wall is, side to side (world units).")]
        [SerializeField, Min(0.1f)] private float lineLength = 4f;
        [Tooltip("Offset of the start from this object's pivot (in its local space).")]
        [SerializeField] private Vector3 offset;

        [Header("Motion")]
        [Tooltip("How fast the wave travels (units per second).")]
        [SerializeField, Min(0.1f)] private float speed = 14f;
        [Tooltip("Push at the start (1 = like a footstep, 2 = flattening).")]
        [SerializeField, Range(0f, 2f)] private float strength = 1.3f;
        [Tooltip("Thickness of the wave front (units).")]
        [SerializeField, Min(0.05f)] private float width = 0.9f;

        [Header("Effects")]
        [Tooltip("Also cut, ignite or extinguish the grass as the wave passes.")]
        [SerializeField] private GrassShockwaveEffect effects = GrassShockwaveEffect.None;
        [Tooltip("How far those effects reach (less than 0 = half of Max Radius).")]
        [SerializeField] private float effectRadius = -1f;
        [Tooltip("Where the wave cuts the grass (when Effects has Cut).")]
        [SerializeField] private CutHeightMode cutHeightMode = CutHeightMode.FieldDefault;
        [Tooltip("Above Ground: units above the ground. Fraction Of Tuft: 0..1. At Emitter: added to this object's height.")]
        [SerializeField] private float cutHeight = 0.1f;

        [Header("Triggering")]
        [SerializeField] private bool emitOnEnable;
        [Tooltip("Emit again every this many seconds (0 = no repeat).")]
        [SerializeField, Min(0f)] private float repeatInterval;

        private float nextTime;

        public Vector3 Position => transform.TransformPoint(offset);
        public GrassShockwaveShape Shape { get => shape; set => shape = value; }
        public GrassShockwaveEffect Effects { get => effects; set => effects = value; }
        public float CutHeight { get => cutHeight; set => cutHeight = value; }
        public CutHeightMode HeightMode { get => cutHeightMode; set => cutHeightMode = value; }

        /// <summary>The shockwave this emitter sends right now (edit it and pass it to Grass.Shockwave for variations).</summary>
        public GrassShockwave Wave
        {
            get
            {
                Vector3 position = Position;
                GrassShockwave wave = GrassShockwave.Ring(position, maxRadius);
                wave.shape = shape;
                wave.direction = transform.forward;
                wave.angle = coneAngle;
                wave.lineLength = lineLength;
                wave.speed = speed;
                wave.strength = strength;
                wave.width = width;
                wave.effects = effects;
                wave.effectDistance = effectRadius;
                switch (cutHeightMode)
                {
                    case CutHeightMode.AboveGround: wave.cutHeight = GrassCutHeight.AboveGround(cutHeight); break;
                    case CutHeightMode.FractionOfTuft: wave.cutHeight = GrassCutHeight.AtFraction(cutHeight); break;
                    case CutHeightMode.AtEmitter: wave.cutHeight = GrassCutHeight.AtWorldHeight(position.y + cutHeight); break;
                    default: wave.cutHeight = GrassCutHeight.FieldDefault; break;
                }
                return wave;
            }
        }

        private void OnEnable()
        {
            nextTime = Time.time + repeatInterval;
            if (emitOnEnable) Emit();
        }

        private void Update()
        {
            if (repeatInterval <= 0f || Time.time < nextTime) return;
            nextTime = Time.time + repeatInterval;
            Emit();
        }

        /// <summary>Sends one shockwave from here.</summary>
        public void Emit() => Grass.Shockwave(Wave);

        /// <summary>Sends one shockwave from here with a different strength (e.g. scaled by a hit's power).</summary>
        public void Emit(float strengthOverride)
        {
            GrassShockwave wave = Wave;
            wave.strength = strengthOverride;
            Grass.Shockwave(wave);
        }

        private void OnDrawGizmosSelected()
        {
            Wave.DrawGizmo(new Color(0.6f, 0.9f, 1f, 0.8f), new Color(1f, 0.6f, 0.3f, 0.8f));
        }
    }
}
