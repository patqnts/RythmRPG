using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RythmRPG.Core
{
    /// <summary>Shared settings for the stylised, sprite-based ground reflections.</summary>
    [Serializable]
    public sealed class GroundReflectionSettings
    {
        public bool enabled = true;

        [ColorUsage(false, true)]
        public Color tint = new(0.36f, 0.46f, 0.58f, 1f);

        [Range(0f, 1f)] public float strength = 0.3f;
        [Min(0.1f)] public float maxLength = 2.75f;
        [Range(0f, 1f)] public float dither = 0.28f;
        [Tooltip("Only upward-facing parts of selected receiver meshes whose world normal reaches this value receive reflections.")]
        [Range(0f, 1f)] public float minimumFloorNormalY = 0.55f;

        [Tooltip("How often newly spawned opt-in mesh and sprite renderers are discovered.")]
        [Min(0.1f)] public float rescanInterval = 0.75f;

        public RenderPassEvent maskInjectionPoint = RenderPassEvent.AfterRenderingOpaques;

        public void Sanitize()
        {
            strength = Mathf.Clamp01(strength);
            maxLength = Mathf.Max(0.1f, maxLength);
            dither = Mathf.Clamp01(dither);
            minimumFloorNormalY = Mathf.Clamp01(minimumFloorNormalY);
            rescanInterval = Mathf.Max(0.1f, rescanInterval);
            if (maskInjectionPoint >= RenderPassEvent.BeforeRenderingTransparents)
                maskInjectionPoint = RenderPassEvent.AfterRenderingOpaques;
        }
    }

    /// <summary>
    /// Creates and maintains mirrored SpriteRenderers in Play mode. The renderer feature writes stencil only on visible
    /// upward-facing Pixel Mesh surfaces, and the reflection material draws these proxies through that stencil.
    /// </summary>
    public static class GroundReflectionRuntime
    {
        internal const string MaterialResource = "Rendering/GroundReflection";
        private static GroundReflectionSettings settings = new();
        private static GroundReflectionManager manager;

        public static GroundReflectionSettings Settings => settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureManager();
        }

        public static void Configure(GroundReflectionSettings value)
        {
            if (value == null) return;
            value.Sanitize();
            settings = value;
            if (Application.isPlaying) EnsureManager();
            manager?.ApplySettings();
        }

        private static void EnsureManager()
        {
            if (!Application.isPlaying || manager != null) return;
            GameObject host = new("Ground Reflections");
            UnityEngine.Object.DontDestroyOnLoad(host);
            manager = host.AddComponent<GroundReflectionManager>();
        }
    }
}
