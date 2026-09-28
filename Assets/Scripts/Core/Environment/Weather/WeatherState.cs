using System;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>Which parts of the weather a <see cref="WeatherZone"/> overrides.</summary>
    [Flags]
    public enum WeatherChannels
    {
        None = 0,
        Rain = 1 << 0,
        Snowfall = 1 << 1,
        Heat = 1 << 6,
        Wind = 1 << 7,
        Lightning = 1 << 8,
        Tint = 1 << 9,
        /// <summary>Indoors: no rain, snowfall, wind or lightning.</summary>
        Sky = Rain | Snowfall | Wind | Lightning,
        All = ~0
    }

    /// <summary>One weather look: rain, snowfall, heat shimmer, wind, lightning and an optional colour tint.</summary>
    [Serializable]
    public struct WeatherState
    {
        [Header("Precipitation")]
        [Range(0f, 1f)] public float rain;
        [Range(0f, 1f)] public float snowfall;

        [Header("Heat")]
        [Tooltip("Heat-wave shimmer over the whole view.")]
        [Range(0f, 1f)] public float heat;

        [Header("Wind and storm")]
        [Tooltip("Wind direction in degrees on the ground (0 = +X, 90 = +Z).")]
        [Range(0f, 360f)] public float windDirection;
        [Tooltip("Wind speed (world units per second). Slants rain, blows snow and drifts Terrain Weather fog.")]
        [Range(0f, 12f)] public float windSpeed;
        [Tooltip("How often lightning strikes (0 = never, 1 = every few seconds).")]
        [Range(0f, 1f)] public float lightning;

        [Header("Colour")]
        [Tooltip("Colour multiplied over the whole view (storm gloom, golden heat). Alpha = amount; 0 = off.")]
        public Color tint;

        public Vector2 Wind
        {
            get
            {
                float r = windDirection * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * windSpeed;
            }
        }

        public static WeatherState Clear => new()
        {
            windDirection = 20f, windSpeed = 0.6f,
            tint = new Color(1f, 1f, 1f, 0f)
        };

        public static WeatherState Lerp(WeatherState a, WeatherState b, float t)
        {
            t = Mathf.Clamp01(t);
            // Wind blends as a vector so it never spins the long way round.
            Vector2 wind = Vector2.Lerp(a.Wind, b.Wind, t);
            return new WeatherState
            {
                rain = Mathf.Lerp(a.rain, b.rain, t),
                snowfall = Mathf.Lerp(a.snowfall, b.snowfall, t),
                heat = Mathf.Lerp(a.heat, b.heat, t),
                lightning = Mathf.Lerp(a.lightning, b.lightning, t),
                tint = Color.Lerp(a.tint, b.tint, t),
                windSpeed = wind.magnitude,
                windDirection = wind.sqrMagnitude > 0.0001f
                    ? Mathf.Repeat(Mathf.Atan2(wind.y, wind.x) * Mathf.Rad2Deg, 360f)
                    : Mathf.LerpAngle(a.windDirection, b.windDirection, t)
            };
        }

        /// <summary>Takes the channels in <paramref name="mask"/> from <paramref name="b"/>, by weight <paramref name="t"/>.</summary>
        public static WeatherState Override(WeatherState a, WeatherState b, WeatherChannels mask, float t)
        {
            WeatherState full = Lerp(a, b, t);
            WeatherState r = a;
            if ((mask & WeatherChannels.Rain) != 0) r.rain = full.rain;
            if ((mask & WeatherChannels.Snowfall) != 0) r.snowfall = full.snowfall;
            if ((mask & WeatherChannels.Heat) != 0) r.heat = full.heat;
            if ((mask & WeatherChannels.Wind) != 0) { r.windSpeed = full.windSpeed; r.windDirection = full.windDirection; }
            if ((mask & WeatherChannels.Lightning) != 0) r.lightning = full.lightning;
            if ((mask & WeatherChannels.Tint) != 0) r.tint = full.tint;
            return r;
        }
    }

    /// <summary>A named weather preset (see <see cref="WeatherController"/>).</summary>
    [Serializable]
    public sealed class WeatherProfile
    {
        public string name = "Clear";
        public WeatherState state = WeatherState.Clear;
        [Tooltip("Relative chance of being picked by Auto Cycle (0 = never).")]
        [Min(0f)] public float cycleWeight = 1f;

        public WeatherProfile() { }

        public WeatherProfile(string name, WeatherState state, float cycleWeight = 1f)
        {
            this.name = name;
            this.state = state;
            this.cycleWeight = cycleWeight;
        }

        /// <summary>The built-in presets: Clear, Rain, Storm, Snow, Blizzard, Heat Wave.</summary>
        public static WeatherProfile[] Defaults()
        {
            WeatherState clear = WeatherState.Clear;

            WeatherState rain = clear;
            rain.rain = 0.55f;
            rain.windSpeed = 2f;
            rain.windDirection = 200f;
            rain.tint = new Color(0.8f, 0.84f, 0.92f, 0.3f);

            WeatherState storm = rain;
            storm.rain = 1f;
            storm.windSpeed = 6f;
            storm.lightning = 0.55f;
            storm.tint = new Color(0.62f, 0.67f, 0.8f, 0.5f);

            WeatherState snow = clear;
            snow.snowfall = 0.5f;
            snow.windSpeed = 1f;
            snow.windDirection = 160f;

            WeatherState blizzard = snow;
            blizzard.snowfall = 1f;
            blizzard.windSpeed = 8f;
            blizzard.tint = new Color(0.86f, 0.9f, 1f, 0.3f);

            WeatherState heat = clear;
            heat.heat = 0.7f;
            heat.windSpeed = 0.4f;

            return new[]
            {
                new WeatherProfile("Clear", clear, 3f),
                new WeatherProfile("Rain", rain, 1.5f),
                new WeatherProfile("Storm", storm, 0.5f),
                new WeatherProfile("Snow", snow, 0f),
                new WeatherProfile("Blizzard", blizzard, 0f),
                new WeatherProfile("Heat Wave", heat, 0f)
            };
        }
    }
}
