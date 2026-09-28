using System;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Static access to the scene's weather (driven by the active <see cref="WeatherController"/>).
    /// Everything reads 0 / clear when there is no controller.
    /// <code>
    /// Weather.Set("Storm", 10f);          // fade to the Storm preset over 10 s
    /// if (Weather.Current.rain > 0.5f) ...
    /// Weather.Lightning += OnLightning;    // flash + thunder moments
    /// </code>
    /// Shaders can read the globals <c>_WeatherAmounts</c> (rain, snowfall, 0, 0),
    /// <c>_WeatherAmounts2</c> (heat, 0, 0, lightning flash) and <c>_WeatherWind</c>
    /// (wind x, wind z, wind speed, weather time).
    /// </summary>
    public static class Weather
    {
        internal static readonly int AmountsId = Shader.PropertyToID("_WeatherAmounts");
        internal static readonly int Amounts2Id = Shader.PropertyToID("_WeatherAmounts2");
        internal static readonly int WindId = Shader.PropertyToID("_WeatherWind");

        /// <summary>The controller driving the weather, or null.</summary>
        public static WeatherController Controller { get; internal set; }

        /// <summary>The weather right now at the player (after transitions and zones).</summary>
        public static WeatherState Current { get; internal set; } = WeatherState.Clear;

        /// <summary>Lightning flash brightness right now (0..1).</summary>
        public static float Flash { get; internal set; }

        /// <summary>The name of the preset being shown (or faded to).</summary>
        public static string CurrentName => Controller != null ? Controller.CurrentName : "Clear";

        /// <summary>The player position used for zones (or the view's centre on the ground).</summary>
        public static Vector3 Focus { get; internal set; }

        /// <summary>The ground point at the centre of the view.</summary>
        public static Vector3 ViewFocus { get; internal set; }

        /// <summary>World height of the ground the weather lands on.</summary>
        public static float GroundHeight { get; internal set; }

        /// <summary>Raised when lightning strikes (flash start), with the time until the thunder sound.</summary>
        public static event Action<float> Lightning;

        /// <summary>True while it rains or snows at the player (above a small threshold).</summary>
        public static bool IsPrecipitating => Current.rain > 0.05f || Current.snowfall > 0.05f;

        /// <summary>Fades to a preset by name (see the controller's profile list).</summary>
        public static bool Set(string presetName, float seconds = -1f) =>
            Controller != null && Controller.SetWeather(presetName, seconds);

        /// <summary>Fades to any weather.</summary>
        public static void Set(WeatherState state, float seconds = -1f, string displayName = "Custom")
        {
            if (Controller != null) Controller.SetWeather(state, seconds, displayName);
        }

        internal static void RaiseLightning(float thunderDelay) => Lightning?.Invoke(thunderDelay);

        internal static void ClearGlobals()
        {
            Current = WeatherState.Clear;
            Flash = 0f;
            Shader.SetGlobalVector(AmountsId, Vector4.zero);
            Shader.SetGlobalVector(Amounts2Id, Vector4.zero);
            Shader.SetGlobalVector(WindId, Vector4.zero);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Controller = null;
            Lightning = null;
            Current = WeatherState.Clear;
            Flash = 0f;
        }
    }
}
