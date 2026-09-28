using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    public enum WeatherZoneShape
    {
        Box,
        Sphere
    }

    /// <summary>
    /// Changes the weather while the player (the controller's focus) is inside: a house or cave (no rain, snow, wind
    /// or fog banks), heat near lava, thick mist in a swamp. Only the ticked channels are changed; outside the zone
    /// the weather blends back over <see cref="blendDistance"/>. Zones apply in priority order (higher last).
    /// The shape uses this object's position, rotation and scale (Box = unit cube, Sphere = radius 0.5).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Rythm RPG/Environment/Weather Zone")]
    public sealed class WeatherZone : MonoBehaviour
    {
        [SerializeField] private WeatherZoneShape shape = WeatherZoneShape.Box;
        [Tooltip("Distance (world units) outside the shape over which the zone fades out.")]
        [SerializeField, Min(0f)] private float blendDistance = 2f;
        [SerializeField] private int priority;
        [Tooltip("Which parts of the weather this zone changes. Sky = rain, snowfall, fog banks, wind and lightning " +
                 "(use it for interiors).")]
        [SerializeField] private WeatherChannels channels = WeatherChannels.Sky;
        [Tooltip("The values used for the ticked channels.")]
        [SerializeField] private WeatherState weather = WeatherState.Clear;

        private static readonly List<WeatherZone> zones = new();
        public static IReadOnlyList<WeatherZone> All => zones;

        public int Priority => priority;
        public WeatherChannels Channels { get => channels; set => channels = value; }
        public WeatherState Weather { get => weather; set => weather = value; }

        private void OnEnable() { if (!zones.Contains(this)) zones.Add(this); }
        private void OnDisable() => zones.Remove(this);

        /// <summary>1 inside, fading to 0 at <see cref="blendDistance"/> outside.</summary>
        public float WeightAt(Vector3 point)
        {
            float distance = WeatherShapes.DistanceOutside(transform, shape, point);
            if (distance <= 0f) return 1f;
            if (blendDistance <= 0.0001f) return 0f;
            float t = 1f - Mathf.Clamp01(distance / blendDistance);
            return t * t * (3f - 2f * t);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => zones.Clear();

        private void OnDrawGizmos() => WeatherShapes.DrawGizmo(transform, shape, new Color(0.5f, 0.8f, 1f, 0.5f), blendDistance);
    }

    /// <summary>Shape helpers shared by the weather volumes.</summary>
    public static class WeatherShapes
    {
        /// <summary>World distance from <paramref name="point"/> to the shape (0 inside).</summary>
        public static float DistanceOutside(Transform t, WeatherZoneShape shape, Vector3 point)
        {
            Vector3 local = t.InverseTransformPoint(point);
            Vector3 scale = t.lossyScale;
            if (shape == WeatherZoneShape.Sphere)
            {
                float radius = 0.5f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                return Mathf.Max(0f, Vector3.Distance(point, t.position) - radius);
            }
            Vector3 q = new(
                Mathf.Max(0f, Mathf.Abs(local.x) - 0.5f) * Mathf.Abs(scale.x),
                Mathf.Max(0f, Mathf.Abs(local.y) - 0.5f) * Mathf.Abs(scale.y),
                Mathf.Max(0f, Mathf.Abs(local.z) - 0.5f) * Mathf.Abs(scale.z));
            return q.magnitude;
        }

        public static void DrawGizmo(Transform t, WeatherZoneShape shape, Color color, float blend)
        {
            Gizmos.color = color;
            if (shape == WeatherZoneShape.Sphere)
            {
                Vector3 s = t.lossyScale;
                float radius = 0.5f * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                Gizmos.DrawWireSphere(t.position, radius);
                if (blend > 0f)
                {
                    Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.35f);
                    Gizmos.DrawWireSphere(t.position, radius + blend);
                }
                return;
            }
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = t.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = previous;
        }

        /// <summary>
        /// Packs a box/sphere volume for the weather shaders: world-to-local rows (xyz = rotation * 1/scale,
        /// w = translation) so the shader can test the unit shape in local space.
        /// </summary>
        public static void Pack(Transform t, out Vector4 row0, out Vector4 row1, out Vector4 row2)
        {
            Matrix4x4 m = t.worldToLocalMatrix;
            row0 = m.GetRow(0);
            row1 = m.GetRow(1);
            row2 = m.GetRow(2);
        }
    }
}
