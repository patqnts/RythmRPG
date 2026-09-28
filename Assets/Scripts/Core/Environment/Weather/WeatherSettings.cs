using System;
using UnityEngine;
using UnityEngine.Events;

namespace RythmRPG.Core
{
    [Serializable]
    public sealed class RainSettings
    {
        public bool enabled = true;
        [Tooltip("Drops at full rain. They fill a box around the view, anchored to the world.")]
        [Range(100, 6000)] public int maxDrops = 1400;
        public Color color = new(0.78f, 0.85f, 0.98f, 0.6f);
        [Tooltip("Fall speed (world units per second).")]
        [Min(1f)] public float fallSpeed = 16f;
        [Tooltip("Streak length range (world units; 1 unit = 32 pixels).")]
        public Vector2 streakLength = new(0.3f, 0.65f);
        [Tooltip("Small pixel splashes where drops hit the ground.")]
        public bool splashes = true;
        [Min(0.02f)] public float splashSeconds = 0.16f;
        [Tooltip("How much the wind slants the rain.")]
        [Range(0f, 2f)] public float windSlant = 1f;
        [Tooltip("Size (X, Z) of the rain box around the view centre, and its height.")]
        public Vector3 area = new(22f, 10f, 18f);
    }

    [Serializable]
    public sealed class SnowSettings
    {
        public bool enabled = true;
        [Range(100, 6000)] public int maxFlakes = 1500;
        public Color color = new(0.97f, 0.98f, 1f, 1f);
        [Min(0.1f)] public float fallSpeed = 1.3f;
        [Tooltip("Side-to-side sway (world units).")]
        [Range(0f, 2f)] public float sway = 0.35f;
        [Min(0f)] public float swaySpeed = 1.3f;
        [Tooltip("Share of 2x2 pixel flakes (the rest are 1 pixel).")]
        [Range(0f, 1f)] public float bigFlakes = 0.25f;
        [Tooltip("How long a flake lies on the ground before fading away.")]
        [Min(0f)] public float settleSeconds = 0.8f;
        [Tooltip("How strongly wind pushes the flakes.")]
        [Range(0f, 2f)] public float windPush = 0.6f;
        public Vector3 area = new(22f, 8f, 18f);
    }

    [Serializable]
    public sealed class HeatSettings
    {
        [Tooltip("Largest shimmer offset, in whole pixels.")]
        [Range(1, 6)] public int maxOffsetPixels = 2;
        [Tooltip("Size of the shimmer waves (world units).")]
        [Min(0.05f)] public float scale = 0.7f;
        [Tooltip("How fast the shimmer rises.")]
        [Min(0f)] public float speed = 1.6f;
        [Tooltip("Global heat is strongest this high above the ground and fades above it.")]
        [Min(0.1f)] public float groundBand = 3f;
    }

    [Serializable]
    public sealed class LightningSettings
    {
        public Color flashColor = new(0.86f, 0.9f, 1f, 1f);
        [Range(0f, 2f)] public float flashBrightness = 0.55f;
        [Tooltip("Optional: this light is also flashed (usually the sun).")]
        public Light flashLight;
        [Min(0f)] public float flashLightBoost = 2.5f;
        [Tooltip("Optional: plays one of the thunder clips after the flash.")]
        public AudioSource thunderSource;
        public AudioClip[] thunderClips;
        [Tooltip("Seconds between flash and thunder (random in range).")]
        public Vector2 thunderDelay = new(0.4f, 2.5f);
        public UnityEvent onLightning = new();
    }

    [Serializable]
    public sealed class SkySettings
    {
        [Tooltip("Sun light to dim under clouds (empty = RenderSettings.sun).")]
        public Light sun;
        [Tooltip("How much the sun dims in heavy rain, snow or fog.")]
        [Range(0f, 1f)] public float cloudDimming = 0.45f;
        [Header("Ambient sound (optional, looping sources)")]
        public AudioSource rainLoop;
        [Range(0f, 1f)] public float rainVolume = 1f;
        public AudioSource windLoop;
        [Range(0f, 1f)] public float windVolume = 0.7f;
    }
}
