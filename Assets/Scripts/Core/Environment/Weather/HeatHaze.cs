using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Local heat shimmer: the air above a campfire, lava pool, forge or desert road wobbles in whole pixels.
    /// Drawn by the Weather renderer feature (up to 8 hazes nearest the view), on top of the weather's global Heat.
    /// The shape uses this object's transform (Box = unit cube, Sphere = radius 0.5); put it where the hot air is,
    /// usually above the heat source.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Rythm RPG/Environment/Heat Haze")]
    public sealed class HeatHaze : MonoBehaviour
    {
        public const int MaxDrawn = 8;

        [SerializeField] private WeatherZoneShape shape = WeatherZoneShape.Sphere;
        [Tooltip("How strong the shimmer is (1 = up to the controller's max pixel offset).")]
        [SerializeField, Range(0f, 2f)] private float strength = 1f;
        [Tooltip("Soft edge, as a fraction of the shape.")]
        [SerializeField, Range(0.01f, 1f)] private float edgeSoftness = 0.5f;
        [Tooltip("Fade the shimmer toward the top of the shape (hot air cools as it rises).")]
        [SerializeField] private bool fadeUpward = true;

        private static readonly List<HeatHaze> hazes = new();
        public static IReadOnlyList<HeatHaze> All => hazes;

        public WeatherZoneShape Shape => shape;
        public float Strength { get => strength; set => strength = Mathf.Max(0f, value); }
        public float EdgeSoftness => edgeSoftness;
        public bool FadeUpward => fadeUpward;

        private void OnEnable() { if (!hazes.Contains(this)) hazes.Add(this); }
        private void OnDisable() => hazes.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => hazes.Clear();

        private void OnDrawGizmos() => WeatherShapes.DrawGizmo(transform, shape, new Color(1f, 0.55f, 0.2f, 0.6f), 0f);
    }
}
