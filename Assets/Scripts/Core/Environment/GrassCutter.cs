using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Cuts <see cref="GrassField"/> grass. Put it on a sword hitbox, a lawnmower, a spinning blade, a dash trail.
    /// The cutter decides how high it cuts (<see cref="HeightMode"/>); tufts already shorter than the cut are left
    /// alone, and the severed tops are thrown off in the direction the cutter moves.
    /// <list type="bullet">
    /// <item>While Enabled: cuts along its path every frame (it sweeps, so fast moves leave no gaps).</item>
    /// <item>On Enable: one cut each time the object is switched on (a hitbox enabled for a swing).</item>
    /// <item>Manual: call <see cref="Cut"/> (for example from an animation event or a UnityEvent).</item>
    /// </list>
    /// </summary>
    [AddComponentMenu("Rythm RPG/Environment/Grass Cutter")]
    public sealed class GrassCutter : MonoBehaviour
    {
        public enum Mode { WhileEnabled, OnEnable, Manual }

        public enum HeightMode
        {
            /// <summary>Cuts where the cutter is: its world height (plus offset). A low swing cuts low.</summary>
            AtCutter,
            /// <summary>A fixed height above the ground (Height, world units).</summary>
            AboveGround,
            /// <summary>A fixed fraction of every tuft (Height, 0 = ground, 1 = tip).</summary>
            FractionOfTuft,
            /// <summary>The Grass Field's own Stubble Height.</summary>
            FieldDefault
        }

        [SerializeField] private Mode mode = Mode.WhileEnabled;
        [Tooltip("World radius of the cut.")]
        [SerializeField, Min(0.05f)] private float radius = 0.6f;
        [Tooltip("Offset of the cut from this object's pivot (e.g. to the blade's edge).")]
        [SerializeField] private Vector3 offset;

        [Header("Cut height")]
        [Tooltip("At Cutter: the cut goes through the grass at this object's height (the blade's height). " +
                 "Above Ground / Fraction Of Tuft: a fixed height. Field Default: the field's Stubble Height.")]
        [SerializeField] private HeightMode heightMode = HeightMode.AtCutter;
        [Tooltip("Above Ground: world units above the ground. Fraction Of Tuft: 0..1. At Cutter: added to the cutter's height.")]
        [SerializeField] private float height = 0.12f;

        [Tooltip("Higher than this above the grass it does not cut at all (flying, jumping).")]
        [SerializeField, Min(0f)] private float maxHeightAboveGrass = 1.5f;

        private Vector3 lastPosition;
        private bool hasLast;

        public float Radius { get => radius; set => radius = Mathf.Max(0.05f, value); }
        public Vector3 Position => transform.position + transform.rotation * offset;
        public HeightMode CutHeightMode { get => heightMode; set => heightMode = value; }
        public float Height { get => height; set => height = value; }

        /// <summary>Tufts cut by this cutter so far.</summary>
        public int TotalCut { get; private set; }

        /// <summary>The cut height the cutter uses right now.</summary>
        public GrassCutHeight CurrentCutHeight
        {
            get
            {
                switch (heightMode)
                {
                    case HeightMode.AtCutter: return GrassCutHeight.AtWorldHeight(Position.y + height);
                    case HeightMode.AboveGround: return GrassCutHeight.AboveGround(height);
                    case HeightMode.FractionOfTuft: return GrassCutHeight.AtFraction(height);
                    default: return GrassCutHeight.FieldDefault;
                }
            }
        }

        private void OnEnable()
        {
            hasLast = false;
            if (mode == Mode.OnEnable) Cut();
        }

        private void LateUpdate()
        {
            if (mode != Mode.WhileEnabled) return;
            Vector3 position = Position;
            if (!CloseToGrass(position))
            {
                hasLast = false;
                return;
            }
            Vector3 from = hasLast ? lastPosition : position;
            TotalCut += Grass.CutAlong(from, position, radius, CurrentCutHeight, position - from);
            lastPosition = position;
            hasLast = true;
        }

        /// <summary>Cuts the grass around the cutter now. Returns how many tufts were cut.</summary>
        public int Cut() => Cut(Vector3.zero);

        /// <summary>Cuts now, throwing the severed tops along <paramref name="direction"/> (e.g. the swing direction).</summary>
        public int Cut(Vector3 direction)
        {
            Vector3 position = Position;
            if (!CloseToGrass(position)) return 0;
            int count = Grass.Cut(position, radius, CurrentCutHeight, direction);
            TotalCut += count;
            return count;
        }

        private bool CloseToGrass(Vector3 position) => Grass.HeightAboveGrass(position) <= maxHeightAboveGrass;

        private void OnDrawGizmosSelected()
        {
            Vector3 position = Position;
            Gizmos.color = new Color(0.9f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(position, radius);
            if (heightMode != HeightMode.AtCutter) return;
            // The cut plane.
            float y = position.y + height;
            Gizmos.DrawLine(new Vector3(position.x - radius, y, position.z), new Vector3(position.x + radius, y, position.z));
            Gizmos.DrawLine(new Vector3(position.x, y, position.z - radius), new Vector3(position.x, y, position.z + radius));
        }
    }
}
