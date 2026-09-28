using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    /// <summary>
    /// Submits rain and snowfall (Hidden/RythmRPG/WeatherPrecipitation) for the <see cref="WeatherController"/>.
    /// One instanced quad per drop; positions and timing are computed on the GPU from the instance ID.
    /// </summary>
    internal static class WeatherPrecipitation
    {
        private const string ShaderPath = "Rendering/Weather/WeatherPrecipitation";

        private static readonly int ModeId = Shader.PropertyToID("_PrecipMode");
        private static readonly int FocusId = Shader.PropertyToID("_PrecipFocus");
        private static readonly int AreaId = Shader.PropertyToID("_PrecipArea");
        private static readonly int MotionId = Shader.PropertyToID("_PrecipMotion");
        private static readonly int WindId = Shader.PropertyToID("_PrecipWind");
        private static readonly int ExtraId = Shader.PropertyToID("_PrecipExtra");
        private static readonly int ColorId = Shader.PropertyToID("_PrecipColor");

        private static Material material;
        private static Mesh quad;
        private static MaterialPropertyBlock rainBlock;
        private static MaterialPropertyBlock snowBlock;
        private static bool warned;
        private static readonly Vector3[] upDirection = { Vector3.up };
        private static readonly Color[] ambientSample = new Color[1];

        public static void Draw(WeatherController controller, WeatherState state, int layer)
        {
            RainSettings rain = controller.Rain;
            SnowSettings snow = controller.Snow;
            bool drawRain = rain.enabled && state.rain > 0.005f;
            bool drawSnow = snow.enabled && state.snowfall > 0.005f;
            if (!drawRain && !drawSnow) return;
            if (!EnsureResources()) return;

            Vector3 focus = Weather.ViewFocus;
            float ground = Weather.GroundHeight;
            float time = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600.0);
            Vector2 wind = state.Wind;
            Color light = SceneLight();

            var rp = new RenderParams(material)
            {
                layer = layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };

            if (drawRain)
            {
                int count = Mathf.Clamp(Mathf.RoundToInt(rain.maxDrops * state.rain), 1, rain.maxDrops);
                Vector3 area = Sanitise(rain.area);
                Vector2 slant = wind * rain.windSlant;
                Color c = rain.color;
                Color lit = new(c.r * Mathf.Lerp(0.5f, 1f, light.r), c.g * Mathf.Lerp(0.5f, 1f, light.g),
                                c.b * Mathf.Lerp(0.5f, 1f, light.b), c.a);
                rainBlock.SetVector(ModeId, new Vector4(0f, time, count, ground));
                rainBlock.SetVector(FocusId, focus);
                rainBlock.SetVector(AreaId, area);
                rainBlock.SetVector(MotionId, new Vector4(rain.fallSpeed, rain.splashSeconds,
                    Mathf.Min(rain.streakLength.x, rain.streakLength.y), Mathf.Max(rain.streakLength.x, rain.streakLength.y)));
                rainBlock.SetVector(WindId, new Vector4(slant.x, slant.y, 0f, 0f));
                rainBlock.SetVector(ExtraId, new Vector4(rain.splashes ? 1f : 0f, 0f, 0f, 0f));
                rainBlock.SetColor(ColorId, lit);
                rp.matProps = rainBlock;
                rp.worldBounds = new Bounds(focus + Vector3.up * area.y * 0.5f, area + Vector3.one * 4f);
                Graphics.RenderMeshPrimitives(rp, quad, 0, count);
            }

            if (drawSnow)
            {
                int count = Mathf.Clamp(Mathf.RoundToInt(snow.maxFlakes * state.snowfall), 1, snow.maxFlakes);
                Vector3 area = Sanitise(snow.area);
                Vector2 push = wind * snow.windPush;
                Color c = snow.color;
                Color lit = new(c.r * light.r, c.g * light.g, c.b * light.b, c.a);
                snowBlock.SetVector(ModeId, new Vector4(1f, time, count, ground));
                snowBlock.SetVector(FocusId, focus);
                snowBlock.SetVector(AreaId, area);
                snowBlock.SetVector(MotionId, new Vector4(snow.fallSpeed, snow.settleSeconds, 0f, 0f));
                snowBlock.SetVector(WindId, new Vector4(push.x, push.y, snow.sway, snow.swaySpeed));
                snowBlock.SetVector(ExtraId, new Vector4(snow.bigFlakes, 0f, 0f, 0f));
                snowBlock.SetColor(ColorId, lit);
                rp.matProps = snowBlock;
                rp.worldBounds = new Bounds(focus + Vector3.up * area.y * 0.5f, area + Vector3.one * 4f);
                Graphics.RenderMeshPrimitives(rp, quad, 0, count);
            }
        }

        private static Vector3 Sanitise(Vector3 area) =>
            new(Mathf.Max(1f, area.x), Mathf.Max(0.5f, area.y), Mathf.Max(1f, area.z));

        // Rough light on falling snow: ambient from above plus the sun, so snow is not glowing at night.
        private static Color SceneLight()
        {
            Color ambient;
            if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat)
            {
                ambient = RenderSettings.ambientLight;
            }
            else
            {
                RenderSettings.ambientProbe.Evaluate(upDirection, ambientSample);
                ambient = ambientSample[0];
            }
            Color light = ambient;
            Light sun = RenderSettings.sun;
            if (sun != null && sun.isActiveAndEnabled)
            {
                float up = Mathf.Clamp01(Vector3.Dot(Vector3.up, -sun.transform.forward));
                light += sun.color * sun.intensity * Mathf.Lerp(0.35f, 1f, up) * 0.8f;
            }
            float max = Mathf.Max(light.r, Mathf.Max(light.g, light.b));
            if (max > 1f) light /= max;
            light.r = Mathf.Max(light.r, 0.2f);
            light.g = Mathf.Max(light.g, 0.2f);
            light.b = Mathf.Max(light.b, 0.25f);
            light.a = 1f;
            return light;
        }

        private static bool EnsureResources()
        {
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null)
                {
                    if (!warned) Debug.LogWarning("[Weather] Missing Resources/" + ShaderPath + ".shader.");
                    warned = true;
                    return false;
                }
                material = new Material(shader)
                {
                    name = "Weather Precipitation (runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                    enableInstancing = true
                };
            }
            if (quad == null)
            {
                quad = new Mesh { name = "Weather Quad", hideFlags = HideFlags.HideAndDontSave };
                quad.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) });
                quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
                quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
                // Huge bounds: the vertex shader places the quad anywhere (culling uses RenderParams.worldBounds).
                quad.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            }
            rainBlock ??= new MaterialPropertyBlock();
            snowBlock ??= new MaterialPropertyBlock();
            return true;
        }

        public static void Release()
        {
            if (material != null) Object.DestroyImmediate(material);
            if (quad != null) Object.DestroyImmediate(quad);
            material = null;
            quad = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => warned = false;
    }
}
