using UnityEngine;

namespace RythmRPG.Combat
{
    public static class HorizontalCombatGeometry
    {
        public static float ResolveGameplayHeight(float playerGroundHeight, float enemyGroundHeight,
            float clearance) => Mathf.Max(playerGroundHeight, enemyGroundHeight) + Mathf.Max(0f, clearance);

        public static Vector3 AtHeight(Vector3 position, float height)
        {
            position.y = height;
            return position;
        }

        public static Vector3 PositionBehindLine(Vector3 lineCenter, Vector3 frontDirection,
            float distance, float characterHeight)
        {
            Vector3 direction = Vector3.ProjectOnPlane(frontDirection, Vector3.up);
            if (direction.sqrMagnitude <= 0.0001f) direction = Vector3.forward;
            Vector3 position = lineCenter - direction.normalized * Mathf.Max(0f, distance);
            position.y = characterHeight;
            return position;
        }

    }
}
