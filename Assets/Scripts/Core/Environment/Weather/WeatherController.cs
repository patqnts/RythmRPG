using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    /// <summary>
    /// The scene's sky weather: pick a preset (Clear, Rain, Storm, Snow, Blizzard, Heat Wave or your own) and it fades
    /// there smoothly. It draws rain and snowfall, heat shimmer and the storm tint (through the Weather renderer
    /// feature), lightning and ambient sound. <see cref="WeatherZone"/>s change it locally (interiors, lava) and
    /// <see cref="HeatHaze"/>s add local shimmer. Snow on the ground, mist and fog are placed separately with
    /// <see cref="TerrainWeather"/>.
    /// <para>One per scene. Add it with GameObject > Rythm RPG > Weather. Heat shimmer, tint and the lightning
    /// flash need the Weather renderer feature (Tools > Rythm RPG > Rendering > Install Weather Renderer Feature);
    /// rain and snowfall draw without it.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9000)]
    [AddComponentMenu("Rythm RPG/Environment/Weather Controller")]
    public sealed class WeatherController : MonoBehaviour
    {
        public enum GroundHeightSource
        {
            /// <summary>The oblique camera's ground height (what the grass and shadows use), else this object's Y.</summary>
            Auto,
            /// <summary>This object's Y position.</summary>
            ThisObject
        }

        [Header("Weather")]
        [Tooltip("Preset shown when the scene starts.")]
        [SerializeField] private string startWeather = "Clear";
        [Tooltip("Default fade time (seconds) when changing weather.")]
        [SerializeField, Min(0f)] private float transitionSeconds = 8f;
        [SerializeField] private List<WeatherProfile> profiles = new(WeatherProfile.Defaults());

        [Header("Auto cycle")]
        [Tooltip("Change weather by itself now and then (picks by each preset's Cycle Weight).")]
        [SerializeField] private bool autoCycle;
        [Tooltip("Seconds each weather lasts (random in range).")]
        [SerializeField] private Vector2 cycleSeconds = new(120f, 300f);

        [Header("Placement")]
        [Tooltip("Whose position decides the zones (empty = the object tagged Player, else the view centre).")]
        [SerializeField] private Transform focus;
        [SerializeField] private GroundHeightSource groundHeight = GroundHeightSource.Auto;
        [Tooltip("Adds a Grass Interactor to every CharacterController (they part mist, leave snow trails and " +
                 "make ripples in water).")]
        [SerializeField] private bool autoAddInteractors = true;
        [Tooltip("Hide all weather while the world is hidden (space combat).")]
        [SerializeField] private bool hideWithWorld = true;
        [Tooltip("Show the start weather in Edit mode.")]
        [SerializeField] private bool previewInEditor = true;

        [SerializeField] private RainSettings rain = new();
        [SerializeField] private SnowSettings snow = new();
        [SerializeField] private HeatSettings heat = new();
        [SerializeField] private LightningSettings lightning = new();
        [SerializeField] private SkySettings sky = new();

        private WeatherState from = WeatherState.Clear;
        private WeatherState to = WeatherState.Clear;
        private float transitionStart;
        private float transitionLength;
        private string currentName = "Clear";
        private float nextCycle;
        // Bumped when the built-in presets change so old saved lists (with Cloudy, Fog...) are replaced once.
        private const int CurrentPresetVersion = 2;
        [SerializeField, HideInInspector] private int presetVersion;
        private float nextLightning;
        private float flashStart = -10f;
        private float thunderAt = -1f;
        private float sunBaseIntensity = -1f;
        private Light dimmedSun;
        private float flashLightBase = -1f;
        private float nextScan;
        private bool initialised;
        private Transform taggedPlayer;
        private float nextPlayerSearch;
        private int lastSubmitFrame = -1;
        private static readonly List<WeatherZone> zoneBuffer = new();

        public RainSettings Rain => rain;
        public SnowSettings Snow => snow;
        public HeatSettings Heat => heat;
        public LightningSettings LightningFx => lightning;
        public IReadOnlyList<WeatherProfile> Profiles => profiles;
        public string CurrentName => currentName;
        public bool HideWithWorld => hideWithWorld;

        /// <summary>True when the weather should not be drawn right now (world hidden for space combat).</summary>
        public bool Hidden => hideWithWorld && Application.isPlaying && SceneVisibility.WorldHidden;

        /// <summary>The weather before zones (the preset / transition).</summary>
        public WeatherState Base => WeatherState.Lerp(from, to, TransitionT);

        private float TransitionT
        {
            get
            {
                if (transitionLength <= 0.0001f) return 1f;
                float t = Mathf.Clamp01((Now - transitionStart) / transitionLength);
                return t * t * (3f - 2f * t);
            }
        }

        private static float Now => Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartupAsDouble;

        // ------------------------------------------------------------------ public API

        /// <summary>Fades to the preset called <paramref name="presetName"/> (case-insensitive).</summary>
        public bool SetWeather(string presetName, float seconds = -1f)
        {
            WeatherProfile profile = Find(presetName);
            if (profile == null)
            {
                Debug.LogWarning($"[Weather] No preset called '{presetName}'.", this);
                return false;
            }
            SetWeather(profile.state, seconds, profile.name);
            return true;
        }

        /// <summary>Fades to <paramref name="state"/> over <paramref name="seconds"/> (negative = default time).</summary>
        public void SetWeather(WeatherState state, float seconds = -1f, string displayName = "Custom")
        {
            from = Base;
            to = state;
            transitionStart = Now;
            transitionLength = seconds < 0f ? transitionSeconds : seconds;
            currentName = displayName;
            if (autoCycle) ScheduleCycle();
        }

        /// <summary>Jumps straight to a preset (scene loads, cutscenes).</summary>
        public bool SetWeatherImmediate(string presetName)
        {
            WeatherProfile profile = Find(presetName);
            if (profile == null) return false;
            from = to = profile.state;
            transitionLength = 0f;
            currentName = profile.name;
            return true;
        }

        public WeatherProfile Find(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return null;
            foreach (WeatherProfile p in profiles)
                if (p != null && string.Equals(p.name, presetName, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        /// <summary>Strikes lightning now (flash + thunder), whatever the weather.</summary>
        public void StrikeLightning()
        {
            flashStart = Now;
            float delay = Random.Range(Mathf.Min(lightning.thunderDelay.x, lightning.thunderDelay.y),
                                       Mathf.Max(lightning.thunderDelay.x, lightning.thunderDelay.y));
            thunderAt = Now + delay;
            lightning.onLightning?.Invoke();
            Weather.RaiseLightning(delay);
        }

        /// <summary>Restores the built-in presets (the list in the Inspector is replaced).</summary>
        [ContextMenu("Reset Presets To Defaults")]
        public void ResetPresets()
        {
            profiles = new List<WeatherProfile>(WeatherProfile.Defaults());
            presetVersion = CurrentPresetVersion;
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (Weather.Controller != null && Weather.Controller != this && Weather.Controller.isActiveAndEnabled)
                Debug.LogWarning("[Weather] More than one Weather Controller is active; the newest one is used.", this);
            Weather.Controller = this;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            initialised = false;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RestoreLights();
            if (Weather.Controller == this)
            {
                Weather.Controller = null;
                Weather.ClearGlobals();
            }
            WeatherPrecipitation.Release();
        }

        private void OnValidate()
        {
            if (profiles == null || profiles.Count == 0 || presetVersion < CurrentPresetVersion)
            {
                profiles = new List<WeatherProfile>(WeatherProfile.Defaults());
                presetVersion = CurrentPresetVersion;
            }
            initialised = false; // Edit mode: show the (possibly renamed) start weather again.
        }

        private void Initialise()
        {
            initialised = true;
            if (profiles == null || profiles.Count == 0 || presetVersion < CurrentPresetVersion)
            {
                profiles = new List<WeatherProfile>(WeatherProfile.Defaults());
                presetVersion = CurrentPresetVersion;
            }
            WeatherProfile start = Find(startWeather) ?? (profiles.Count > 0 ? profiles[0] : null);
            WeatherState state = start != null ? start.state : WeatherState.Clear;
            from = to = state;
            transitionLength = 0f;
            currentName = start != null ? start.name : "Clear";
            nextLightning = Now + 3f;
            if (autoCycle) ScheduleCycle();
        }

        private void ScheduleCycle()
        {
            nextCycle = Now + Random.Range(Mathf.Min(cycleSeconds.x, cycleSeconds.y), Mathf.Max(cycleSeconds.x, cycleSeconds.y));
        }

        private void LateUpdate()
        {
            if (!initialised) Initialise();
            bool playing = Application.isPlaying;
            Camera view = Camera.main;

            // Ground and focus.
            float groundY = transform.position.y;
            if (groundHeight == GroundHeightSource.Auto && view != null && ObliqueProjection.TryGet(view, out ObliqueProjection oblique))
                groundY = oblique.GroundHeight;
            Weather.GroundHeight = groundY;
            Vector3 viewFocus = view != null ? ViewGroundPoint(view, groundY) : transform.position;
            Weather.ViewFocus = viewFocus;
            Weather.Focus = ResolveFocus(viewFocus);

            if (playing && autoCycle && Now >= nextCycle) PickCycleWeather();

            // Preset / transition, then zones in priority order.
            WeatherState state = playing || previewInEditor ? Base : WeatherState.Clear;
            state = ApplyZones(state, Weather.Focus);

            UpdateLightning(state, playing);
            if (Hidden)
            {
                state.rain = state.snowfall = state.heat = 0f;
                state.tint.a = 0f;
                Weather.Flash = 0f;
            }

            Weather.Current = state;
            UploadGlobals(state);

            if (playing)
            {
                UpdateSun(state);
                UpdateAudio(state);
                ScanInteractors();
            }
        }

        private WeatherState ApplyZones(WeatherState state, Vector3 point)
        {
            IReadOnlyList<WeatherZone> zones = WeatherZone.All;
            if (zones.Count == 0) return state;
            zoneBuffer.Clear();
            foreach (WeatherZone z in zones)
                if (z != null) zoneBuffer.Add(z);
            // Insertion sort by priority (stable, and there are only a few zones).
            for (int i = 1; i < zoneBuffer.Count; i++)
            {
                WeatherZone z = zoneBuffer[i];
                int j = i - 1;
                while (j >= 0 && zoneBuffer[j].Priority > z.Priority)
                {
                    zoneBuffer[j + 1] = zoneBuffer[j];
                    j--;
                }
                zoneBuffer[j + 1] = z;
            }
            foreach (WeatherZone z in zoneBuffer)
            {
                float w = z.WeightAt(point);
                if (w > 0f) state = WeatherState.Override(state, z.Weather, z.Channels, w);
            }
            zoneBuffer.Clear();
            return state;
        }

        private void PickCycleWeather()
        {
            float total = 0f;
            foreach (WeatherProfile p in profiles)
                if (p != null && p.name != currentName) total += Mathf.Max(0f, p.cycleWeight);
            if (total <= 0f)
            {
                ScheduleCycle();
                return;
            }
            float pick = Random.value * total;
            foreach (WeatherProfile p in profiles)
            {
                if (p == null || p.name == currentName) continue;
                pick -= Mathf.Max(0f, p.cycleWeight);
                if (pick <= 0f)
                {
                    SetWeather(p.state, transitionSeconds, p.name);
                    return;
                }
            }
            ScheduleCycle();
        }

        private void UpdateLightning(WeatherState state, bool playing)
        {
            float now = Now;
            if (playing && state.lightning > 0.01f && now >= nextLightning)
            {
                StrikeLightning();
                float interval = Mathf.Lerp(25f, 3f, state.lightning);
                nextLightning = now + interval * Random.Range(0.5f, 1.5f);
            }
            else if (state.lightning <= 0.01f)
            {
                nextLightning = Mathf.Max(nextLightning, now + 2f);
            }

            // Double flicker: a bright hit, then a weaker second flash.
            float t = now - flashStart;
            float flash = 0f;
            if (t >= 0f && t < 0.6f)
            {
                flash = Mathf.Max(Pulse(t, 0f, 0.07f), 0.65f * Pulse(t, 0.16f, 0.12f));
                flash = Mathf.Max(flash, 0.3f * Pulse(t, 0.34f, 0.2f));
            }
            Weather.Flash = flash * lightning.flashBrightness;

            if (lightning.flashLight != null)
            {
                if (flashLightBase < 0f) flashLightBase = lightning.flashLight.intensity;
                // The sun may also be dimmed by clouds (UpdateSun); the flash adds on top.
                if (lightning.flashLight != dimmedSun)
                    lightning.flashLight.intensity = flashLightBase * (1f + flash * lightning.flashLightBoost);
            }

            if (thunderAt > 0f && now >= thunderAt)
            {
                thunderAt = -1f;
                AudioSource source = lightning.thunderSource;
                if (source != null && lightning.thunderClips != null && lightning.thunderClips.Length > 0)
                {
                    AudioClip clip = lightning.thunderClips[Random.Range(0, lightning.thunderClips.Length)];
                    if (clip != null) source.PlayOneShot(clip, Random.Range(0.75f, 1f));
                }
            }
        }

        private static float Pulse(float t, float start, float length)
        {
            float x = (t - start) / length;
            if (x < 0f || x > 1f) return 0f;
            return x < 0.2f ? x / 0.2f : 1f - (x - 0.2f) / 0.8f;
        }

        private void UpdateSun(WeatherState state)
        {
            Light sun = sky.sun != null ? sky.sun : RenderSettings.sun;
            if (sun != dimmedSun)
            {
                RestoreSun();
                dimmedSun = sun;
                sunBaseIntensity = sun != null ? sun.intensity : -1f;
            }
            if (sun == null || sunBaseIntensity < 0f) return;
            float cloud = Mathf.Max(state.rain, state.snowfall);
            float intensity = sunBaseIntensity * (1f - sky.cloudDimming * cloud);
            if (lightning.flashLight == sun) intensity *= 1f + Weather.Flash / Mathf.Max(0.01f, lightning.flashBrightness) * lightning.flashLightBoost;
            sun.intensity = intensity;
        }

        private void RestoreSun()
        {
            if (dimmedSun != null && sunBaseIntensity >= 0f) dimmedSun.intensity = sunBaseIntensity;
            dimmedSun = null;
            sunBaseIntensity = -1f;
        }

        private void RestoreLights()
        {
            RestoreSun();
            if (lightning.flashLight != null && flashLightBase >= 0f) lightning.flashLight.intensity = flashLightBase;
            flashLightBase = -1f;
        }

        private void UpdateAudio(WeatherState state)
        {
            if (sky.rainLoop != null) SetLoop(sky.rainLoop, state.rain * sky.rainVolume);
            if (sky.windLoop != null) SetLoop(sky.windLoop, Mathf.Clamp01(state.windSpeed / 8f) * sky.windVolume);
        }

        private static void SetLoop(AudioSource source, float volume)
        {
            source.volume = volume;
            if (volume > 0.001f && !source.isPlaying) { source.loop = true; source.Play(); }
            else if (volume <= 0.001f && source.isPlaying) source.Stop();
        }

        private void ScanInteractors()
        {
            if (!autoAddInteractors || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 2f;
            foreach (CharacterController controller in FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude))
                if (controller != null && controller.GetComponent<GrassInteractor>() == null)
                    controller.gameObject.AddComponent<GrassInteractor>();
        }

        private Vector3 ResolveFocus(Vector3 fallback)
        {
            if (focus != null) return focus.position;
            if (!Application.isPlaying) return fallback;
            if (taggedPlayer == null && Time.unscaledTime >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.unscaledTime + 2f;
                GameObject player = null;
                try { player = GameObject.FindGameObjectWithTag("Player"); }
                catch (UnityException) { /* tag not defined */ }
                taggedPlayer = player != null ? player.transform : null;
            }
            return taggedPlayer != null ? taggedPlayer.position : fallback;
        }

        private static Vector3 ViewGroundPoint(Camera camera, float groundY)
        {
            Ray ray = ObliqueProjection.ViewportPointToRay(camera, new Vector3(0.5f, 0.5f, 0f));
            Plane plane = new(Vector3.up, new Vector3(0f, groundY, 0f));
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance)
                : new Vector3(camera.transform.position.x, groundY, camera.transform.position.z);
        }

        private void UploadGlobals(WeatherState state)
        {
            Vector2 wind = state.Wind;
            Shader.SetGlobalVector(Weather.AmountsId, new Vector4(state.rain, state.snowfall, 0f, 0f));
            Shader.SetGlobalVector(Weather.Amounts2Id, new Vector4(state.heat, 0f, 0f, Weather.Flash));
            Shader.SetGlobalVector(Weather.WindId, new Vector4(wind.x, wind.y, state.windSpeed, Now));
        }

        // Rain and snowfall are drawn here (no renderer feature needed), like the GPU grass.
        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (Time.renderedFrameCount == lastSubmitFrame) return;
            lastSubmitFrame = Time.renderedFrameCount;
            if (!isActiveAndEnabled || Hidden) return;
            if (!Application.isPlaying && !previewInEditor) return;
            WeatherPrecipitation.Draw(this, Weather.Current, gameObject.layer);
        }

        [ContextMenu("Strike Lightning")]
        private void StrikeFromMenu() => StrikeLightning();
    }
}
