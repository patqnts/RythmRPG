using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Generated circle sprites for runtime UI: a filled disc and rings of any thickness (cached).</summary>
    public static class UiCircleSprites
    {
        private const int Size = 128;
        private static Sprite disc;
        private static readonly Dictionary<int, Sprite> rings = new();

        public static Sprite Disc => disc != null ? disc : disc = Make(0f, "UI Circle Disc");

        /// <summary>Ring whose band is <paramref name="thickness"/> of the radius (0.01 - 1).</summary>
        public static Sprite Ring(float thickness)
        {
            int key = Mathf.Clamp(Mathf.RoundToInt(thickness * 100f), 1, 100);
            if (rings.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Sprite ring = Make(key / 100f, $"UI Circle Ring {key}");
            rings[key] = ring;
            return ring;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            disc = null;
            rings.Clear();
        }

        private static Sprite Make(float band, string name)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[Size * Size];
            float center = (Size - 1) * 0.5f;
            float outer = Size * 0.5f - 1f;
            float inner = band <= 0f ? -1f : outer - Math.Max(1f, outer * band);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                float a = Mathf.Clamp01(outer - d + 0.5f);
                if (inner >= 0f) a *= Mathf.Clamp01(d - inner + 0.5f);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>Look of the circular frame around an ability icon (set on the Combat VFX Theme).</summary>
    [Serializable]
    public sealed class AbilityIconFrameStyle
    {
        [Tooltip("Circular frame + backdrop around each icon; the icon is clipped to a circle inside it.")]
        public bool Enabled = true;
        public Color FrameColor = new(1f, 0.86f, 0.5f, 1f);
        [Tooltip("Frame band thickness as a fraction of the icon's radius.")]
        [Range(0.02f, 0.5f)] public float FrameThickness = 0.1f;
        [Tooltip("Disc behind the icon (alpha 0 = none).")]
        public Color BackdropColor = new(0.07f, 0.08f, 0.13f, 0.85f);
        [Tooltip("Icon size inside the frame (fraction of the frame's size).")]
        [Range(0.3f, 1f)] public float IconInset = 0.74f;

        [Header("Hold progress")]
        [Tooltip("A ring that fills around the icon while it is held.")]
        public bool ShowHoldProgress = true;
        [Range(0.02f, 0.5f)] public float ProgressThickness = 0.12f;
        [Tooltip("Use the ability's accent colour (its VFX profile) for the progress ring and the frame's charge glow.")]
        public bool UseAbilityAccent = true;
        public Color ProgressColor = new(1f, 0.9f, 0.55f, 1f);
    }
}
