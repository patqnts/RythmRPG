using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Put on a note / projectile prefab to move where it spawns, e.g. a laser that would start behind the enemy's
    /// sprite. The offset is added to the normal spawn point (the enemy for enemy attacks, centre stage for the player's
    /// ability charts) before the note starts its travel. Edit it live with the Combat Preview window (Tools > Rythm RPG >
    /// Combat > Combat Preview), which also shows a draggable handle in the Scene view.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Combat/Note Spawn Offset")]
    public sealed class NoteSpawnOffset : MonoBehaviour
    {
        public enum OffsetSpace
        {
            /// <summary>X = sideways, Y = up, Z = forward along the lane toward the hit line.</summary>
            Lane,
            /// <summary>World X / Y / Z.</summary>
            World
        }

        [Tooltip("Lane: X = sideways, Y = up, Z = forward along the lane toward the hit line (so it works whichever " +
                 "way the lanes face). World: world axes.")]
        [SerializeField] private OffsetSpace space = OffsetSpace.Lane;
        [Tooltip("Added to the spawn point. Lane space: +Z brings the note forward, out from behind the enemy.")]
        [SerializeField] private Vector3 offset;
        [Tooltip("Apply when the enemy attacks with this note.")]
        [SerializeField] private bool enemyAttacks = true;
        [Tooltip("Apply when this note is used in a player ability chart (spawning from centre stage).")]
        [SerializeField] private bool playerAbilityCharts = true;

        public OffsetSpace Space { get => space; set => space = value; }
        public Vector3 Offset { get => offset; set => offset = value; }

        public bool AppliesTo(PatternRunMode mode) =>
            mode == PatternRunMode.EnemyDefense ? enemyAttacks : playerAbilityCharts;

        /// <summary>The offset in world space for a lane whose notes travel along <paramref name="laneForward"/>.</summary>
        public Vector3 ToWorld(Vector3 laneForward)
        {
            if (space == OffsetSpace.World) return offset;
            LaneFrame(laneForward, out Vector3 side, out Vector3 up, out Vector3 forward);
            return side * offset.x + up * offset.y + forward * offset.z;
        }

        /// <summary>A world-space offset expressed in this component's space (for dragging a handle).</summary>
        public Vector3 FromWorld(Vector3 worldOffset, Vector3 laneForward)
        {
            if (space == OffsetSpace.World) return worldOffset;
            LaneFrame(laneForward, out Vector3 side, out Vector3 up, out Vector3 forward);
            return new Vector3(Vector3.Dot(worldOffset, side), Vector3.Dot(worldOffset, up), Vector3.Dot(worldOffset, forward));
        }

        /// <summary>
        /// Axes of lane space: forward = the lane's travel direction, up = world up (or toward the camera for lanes that
        /// run vertically), side = up x forward.
        /// </summary>
        public static void LaneFrame(Vector3 laneForward, out Vector3 side, out Vector3 up, out Vector3 forward)
        {
            forward = laneForward.sqrMagnitude > 0.0001f ? laneForward.normalized : Vector3.forward;
            up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.9f ? Vector3.back : Vector3.up;
            side = Vector3.Cross(up, forward).normalized;
            up = Vector3.Cross(forward, side).normalized;
        }

        /// <summary>Spawn point of <paramref name="prefab"/>: <paramref name="spawnPoint"/> plus its offset, if it has one for this mode.</summary>
        public static Vector3 Apply(GameObject prefab, Vector3 spawnPoint, Vector3 laneForward, PatternRunMode mode)
        {
            NoteSpawnOffset settings = prefab != null ? prefab.GetComponent<NoteSpawnOffset>() : null;
            return settings != null && settings.AppliesTo(mode) ? spawnPoint + settings.ToWorld(laneForward) : spawnPoint;
        }
    }
}
