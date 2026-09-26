using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Shared particle systems for grass clippings, embers and smoke: pixel-sized squares emitted one by one by the
    /// grass fields (no prefabs needed). The quads stand upright facing the camera's yaw, so they stay square under
    /// the <see cref="ObliqueProjection"/>, and they are hidden with the world during space combat.
    /// <para>
    /// <see cref="EmitCustom"/> plays your own particle systems (a GrassField's Burning / Ignite / Burn Out / Cut
    /// effects): each gets one private copy, with emission switched off in all its layers, and particles are emitted
    /// into every layer at the requested point, so the effect keeps its own look, flipbooks and sub-emitters.
    /// </para>
    /// </summary>
    public static class GrassEffects
    {
        public enum Kind { Clipping, Ember, Smoke }

        /// <summary>Most particles emitted per frame (all fields together).</summary>
        public const int BudgetPerFrame = 160;

        private static readonly ParticleSystem[] systems = new ParticleSystem[3];
        private static GameObject root;
        private static Material defaultMaterial;
        private static Material currentMaterial;
        private static int frame = -1;
        private static int budget;
        private static bool missingShaderReported;
        private static bool listening;
        private static GameObject customRoot;
        private static readonly Dictionary<ParticleSystem, ParticleSystem[]> customCopies = new Dictionary<ParticleSystem, ParticleSystem[]>();
        private static readonly Dictionary<ParticleSystem, float[]> customWeights = new Dictionary<ParticleSystem, float[]>();

        /// <summary>Emits one particle. Returns false when this frame's budget is used up.</summary>
        public static bool Emit(Kind kind, Vector3 position, Vector3 velocity, float size, float lifetime, Color color,
            Material material = null)
        {
            if (!Application.isPlaying || SceneVisibility.WorldHidden) return false;
            BeginFrame();
            if (budget <= 0) return false;
            ParticleSystem system = Ensure(kind, material);
            if (system == null) return false;
            budget--;
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = color,
                applyShapeToPosition = false
            };
            system.Emit(emit, 1);
            return true;
        }

        /// <summary>
        /// Emits <paramref name="count"/> particles of your particle system <paramref name="source"/> (a prefab or a
        /// scene object; it is not modified) at <paramref name="position"/>. Returns false when over budget.
        /// </summary>
        public static bool EmitCustom(ParticleSystem source, Vector3 position, int count)
        {
            if (source == null || count <= 0 || !Application.isPlaying || SceneVisibility.WorldHidden) return false;
            BeginFrame();
            if (budget <= 0) return false;
            ParticleSystem[] layers = CustomCopy(source);
            if (layers == null) return false;
            budget -= count;
            float[] weights = customWeights[source];
            for (int l = 0; l < layers.Length; l++)
            {
                ParticleSystem layer = layers[l];
                if (layer == null) continue;
                // Each layer keeps the prefab's own emission ratio to the first layer (e.g. fewer embers than flames).
                int layerCount = Mathf.FloorToInt(count * weights[l] + Random.value);
                if (layerCount <= 0) continue;
                bool world = layer.main.simulationSpace == ParticleSystemSimulationSpace.World;
                var emit = new ParticleSystem.EmitParams
                {
                    position = world ? position : layer.transform.InverseTransformPoint(position),
                    applyShapeToPosition = true
                };
                layer.Emit(emit, layerCount);
            }
            return true;
        }

        private static void BeginFrame()
        {
            if (Time.frameCount == frame) return;
            frame = Time.frameCount;
            budget = BudgetPerFrame;
            if (root != null) Orient();
        }

        private static ParticleSystem[] CustomCopy(ParticleSystem source)
        {
            if (customCopies.TryGetValue(source, out ParticleSystem[] layers) && layers.Length > 0 && layers[0] != null)
                return layers;
            if (customRoot == null)
            {
                customRoot = new GameObject("Grass FX (custom)") { hideFlags = HideFlags.DontSave };
                customRoot.SetActive(!SceneVisibility.WorldHidden);
                Listen();
            }
            ParticleSystem copy = Object.Instantiate(source, customRoot.transform);
            copy.name = source.name + " (grass)";
            copy.transform.localPosition = Vector3.zero;
            copy.gameObject.SetActive(true);
            layers = copy.GetComponentsInChildren<ParticleSystem>(true);
            copy.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var weights = new float[layers.Length];
            float baseRate = EmissionRate(layers[0]);
            for (int l = 0; l < layers.Length; l++)
                weights[l] = baseRate > 0.0001f ? Mathf.Clamp(EmissionRate(layers[l]) / baseRate, 0f, 8f) : 1f;
            customWeights[source] = weights;
            foreach (ParticleSystem layer in layers)
            {
                // Only our Emit calls spawn particles; the copy keeps running so they live out their lifetime.
                ParticleSystem.EmissionModule emission = layer.emission;
                emission.enabled = false;
                ParticleSystem.MainModule main = layer.main;
                main.loop = true;
                main.playOnAwake = false;
                main.maxParticles = Mathf.Max(main.maxParticles, 4000); // one copy serves every burning tuft
            }
            copy.Play(true);
            customCopies[source] = layers;
            return layers;
        }

        private static float EmissionRate(ParticleSystem system)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            if (!emission.enabled) return 0f;
            ParticleSystem.MinMaxCurve rate = emission.rateOverTime;
            return rate.mode == ParticleSystemCurveMode.TwoConstants ? (rate.constantMin + rate.constantMax) * 0.5f
                : rate.mode == ParticleSystemCurveMode.Constant ? rate.constant : rate.curveMultiplier;
        }

        private static void Listen()
        {
            if (listening) return;
            // Hide particles already in flight when combat hides the world.
            SceneVisibility.Changed += hidden =>
            {
                if (root != null) root.SetActive(!hidden);
                if (customRoot != null) customRoot.SetActive(!hidden);
            };
            listening = true;
        }

        private static void Orient()
        {
            root.SetActive(!SceneVisibility.WorldHidden);
            Camera camera = Camera.main;
            if (camera == null) return;
            // Upright, facing the camera's yaw (ObliqueProjection draws that 1:1); the camera's own rotation otherwise.
            root.transform.rotation = ObliqueProjection.BillboardRotation(camera);
        }

        private static ParticleSystem Ensure(Kind kind, Material material)
        {
            Material wanted = material != null ? material : DefaultMaterial();
            if (wanted == null) return null;
            if (root == null)
            {
                root = new GameObject("Grass FX") { hideFlags = HideFlags.DontSave };
                for (int i = 0; i < systems.Length; i++) systems[i] = null;
                Orient();
                Listen();
            }
            int index = (int)kind;
            if (systems[index] == null) systems[index] = Create(kind, wanted);
            if (wanted != currentMaterial)
            {
                currentMaterial = wanted;
                foreach (ParticleSystem system in systems)
                    if (system != null) system.GetComponent<ParticleSystemRenderer>().sharedMaterial = wanted;
            }
            return systems[index];
        }

        private static ParticleSystem Create(Kind kind, Material material)
        {
            var go = new GameObject("Grass " + kind) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = kind == Kind.Smoke ? 600 : 1200;
            main.startSpeed = 0f;
            main.gravityModifier = kind == Kind.Clipping ? 0.6f : kind == Kind.Ember ? -0.03f : -0.01f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            // Shrink away instead of fading, so the particles stay opaque pixel squares.
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve curve = kind == Kind.Smoke
                ? new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.25f))
                : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            if (kind != Kind.Clipping)
            {
                // Air drag, so embers and smoke drift rather than fly off.
                ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 10f;
                limit.drag = kind == Kind.Smoke ? 0.8f : 0.5f;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.Local; // faces the root's upright rotation
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;

            system.Play();
            return system;
        }

        private static Material DefaultMaterial()
        {
            if (defaultMaterial != null) return defaultMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                if (!missingShaderReported)
                    Debug.LogWarning("[Grass] No particle shader found for grass effects; assign an Effect Material on the Grass Field.");
                missingShaderReported = true;
                return null;
            }
            defaultMaterial = new Material(shader) { name = "Grass FX (runtime)", hideFlags = HideFlags.HideAndDontSave };
            return defaultMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            root = null;
            for (int i = 0; i < systems.Length; i++) systems[i] = null;
            currentMaterial = null;
            frame = -1;
            listening = false;
            customRoot = null;
            customCopies.Clear();
            customWeights.Clear();
        }
    }
}
