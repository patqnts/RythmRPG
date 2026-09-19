using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// World-space gameplay anchor for a rhythm lane.  Visual input buttons are deliberately
    /// separate so notes can use SpriteRenderers, meshes, particles, or any mixture of them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RhythmLaneTarget : MonoBehaviour
    {
        [SerializeField, Min(1)] private int laneId = 1;
        [SerializeField] private Vector3 localTravelDirection = Vector3.down;

        public int LaneId => laneId;
        public Vector3 WorldTravelDirection
        {
            get
            {
                Vector3 direction = transform.TransformDirection(localTravelDirection);
                return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.down;
            }
        }

        public void Configure(int id, Vector3 travelDirection)
        {
            laneId = Mathf.Max(1, id);
            localTravelDirection = transform.InverseTransformDirection(
                travelDirection.sqrMagnitude > 0.0001f ? travelDirection.normalized : Vector3.down);
        }

        /// <summary>Absolute distance from the shared judgement plane, measured along note travel.</summary>
        public float GetTimingDistance(Vector3 worldPosition) =>
            Mathf.Abs(Vector3.Dot(worldPosition - transform.position, WorldTravelDirection));

        /// <summary>Positive after the note has crossed the judgement plane.</summary>
        public float GetSignedProgressPastLine(Vector3 worldPosition) =>
            Vector3.Dot(worldPosition - transform.position, WorldTravelDirection);
    }
}
