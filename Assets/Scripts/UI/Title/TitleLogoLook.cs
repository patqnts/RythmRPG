using System;
using UnityEngine;

namespace RythmRPG.UI.Title
{
    /// <summary>
    /// Every color / style knob of the title logo finish. Serialized on <see cref="TitleLogoText"/>,
    /// pushed to the material each frame, so it can be edited live, animated, or swapped via presets.
    /// </summary>
    [Serializable]
    public sealed class TitleLogoLook
    {
        [Header("Ink (letter base color)")]
        public Color ink = new Color(0.02f, 0.018f, 0.016f, 1f);
        [Tooltip("0 = ignore the TMP vertex color. 1 = tint the whole finish by it (rich text <color> works).")]
        [Range(0, 1)] public float useVertexColor = 0f;

        [Header("Accent Ramp")]
        [Tooltip("Build Shadow / Mid / Highlight automatically from the single Accent color.")]
        public bool deriveRampFromAccent = false;
        public Color accent = new Color(0.95f, 0.62f, 0.15f, 1f);
        public Color accentShadow = new Color(0.22f, 0.09f, 0.02f, 1f);
        public Color accentMid = new Color(0.86f, 0.52f, 0.1f, 1f);
        public Color accentHighlight = new Color(1f, 0.9f, 0.45f, 1f);
        [Tooltip("0 = brightness radiates from the focus. 1 = top-bright vertical gradient (subtitle look).")]
        [Range(0, 1)] public float verticalGradient = 0.2f;

        [Header("Burst (where the accent shows)")]
        [Tooltip("1 = the accent covers every letter (no ink).")]
        [Range(0, 1)] public float fillWholeText = 0f;
        [Tooltip("Radius around the focus, in text heights.")]
        [Range(0, 3)] public float burstRadius = 0.85f;
        [Range(0.01f, 1)] public float burstSoftness = 0.5f;
        [Tooltip("How torn / grungy the ink-to-accent boundary is.")]
        [Range(0, 1)] public float burstBreakup = 0.6f;

        [Header("Core Glow")]
        [ColorUsage(false, true)] public Color coreColor = new Color(1f, 0.95f, 0.7f, 1f);
        [Range(0, 1)] public float coreSize = 0.09f;
        [Range(0, 4)] public float coreIntensity = 1.1f;

        [Header("Grunge")]
        [Range(1, 80)] public float grungeScale = 16f;
        [Range(0, 1)] public float grungeStrength = 0.75f;
        [Range(0.5f, 4)] public float grungeContrast = 1.8f;
        [Range(0, 1)] public float grungeFlow = 0.02f;
        [Tooltip("Optional grunge texture (red channel), multiplied over the accent.")]
        public Texture2D grungeTexture;
        [Range(0, 1)] public float grungeTextureStrength = 0f;
        public float grungeTextureTiling = 2f;

        [Header("Halo Swirl")]
        [ColorUsage(false, true)] public Color swirlColor = new Color(1f, 0.97f, 0.88f, 1f);
        [Range(0, 2)] public float swirlStrength = 1.2f;
        [Range(0, 1)] public float swirlRadius = 0.3f;
        [Range(1, 6)] public float swirlTurns = 2.5f;
        [Range(0.002f, 0.06f)] public float swirlLineWidth = 0.02f;
        [Range(0, 2)] public float swirlTwist = 1f;
        [Range(0.1f, 1)] public float swirlTilt = 0.5f;
        [Range(-3, 3)] public float swirlSpinSpeed = 0.35f;

        [Header("Pulse")]
        [Range(0, 1)] public float pulseAmount = 0.12f;
        [Range(0, 4)] public float pulseSpeed = 0.5f;

        [Header("Shine Sweep")]
        public Color shineColor = new Color(1f, 0.98f, 0.85f, 1f);
        [Range(0, 2)] public float shineStrength = 0f;
        [Range(0, 2)] public float shineSpeed = 0.22f;
        [Range(0.01f, 0.5f)] public float shineWidth = 0.08f;
        [Range(-180, 180)] public float shineAngle = -25f;

        [Header("Pixelate")]
        [Tooltip("Snap grunge, burst, swirl and dissolve to a pixel grid (the glyph edge stays SDF-sharp).")]
        public bool pixelate = false;
        [Range(8, 256)] public float pixelsPerTextHeight = 48f;

        [Header("SDF Edge")]
        [Range(-1, 1)] public float faceDilate = 0f;
        public Color outlineColor = new Color(0f, 0f, 0f, 1f);
        [Range(0, 1)] public float outlineWidth = 0f;
        [Range(0, 1)] public float outlineSoftness = 0f;
        public bool edgeGlow = false;
        [ColorUsage(true, true)] public Color glowColor = new Color(1f, 0.6f, 0.2f, 0.35f);
        [Range(-1, 1)] public float glowOffset = 0f;
        [Range(0, 1)] public float glowWidth = 0.3f;
        [Range(0.1f, 4)] public float glowFalloff = 1.5f;

        /// <summary>Shadow / mid / highlight derived from one color (used when deriveRampFromAccent is on).</summary>
        public static void DeriveRamp(Color accent, out Color shadow, out Color mid, out Color highlight)
        {
            Color.RGBToHSV(accent, out float h, out float s, out float v);
            shadow = Color.HSVToRGB(Mathf.Repeat(h - 0.02f, 1f), Mathf.Clamp01(s * 1.1f), v * 0.24f);
            mid = accent;
            highlight = Color.Lerp(Color.HSVToRGB(Mathf.Repeat(h + 0.03f, 1f), s * 0.55f, 1f), Color.white, 0.25f);
            shadow.a = mid.a = highlight.a = 1f;
        }

        public TitleLogoLook Clone() => (TitleLogoLook)MemberwiseClone();

        internal void ApplyTo(Material m)
        {
            Color shadow = accentShadow, mid = accentMid, high = accentHighlight;
            if (deriveRampFromAccent) DeriveRamp(accent, out shadow, out mid, out high);

            m.SetColor(Ids.FaceColor, ink);
            m.SetFloat(Ids.VertexTint, useVertexColor);
            m.SetFloat(Ids.GoldFill, fillWholeText);
            m.SetFloat(Ids.BurstRadius, burstRadius);
            m.SetFloat(Ids.BurstSoftness, burstSoftness);
            m.SetFloat(Ids.BurstBreakup, burstBreakup);
            m.SetColor(Ids.GoldShadow, shadow);
            m.SetColor(Ids.GoldColor, mid);
            m.SetColor(Ids.GoldHighlight, high);
            m.SetFloat(Ids.GradientMix, verticalGradient);
            m.SetColor(Ids.CoreColor, coreColor);
            m.SetFloat(Ids.CoreSize, coreSize);
            m.SetFloat(Ids.CoreIntensity, coreIntensity);

            m.SetFloat(Ids.GrungeScale, grungeScale);
            m.SetFloat(Ids.GrungeStrength, grungeStrength);
            m.SetFloat(Ids.GrungeContrast, grungeContrast);
            m.SetFloat(Ids.GrungeFlow, grungeFlow);
            m.SetTexture(Ids.GrungeTex, grungeTexture ? grungeTexture : Texture2D.whiteTexture);
            m.SetFloat(Ids.GrungeTexStrength, grungeTexture ? grungeTextureStrength : 0f);
            m.SetFloat(Ids.GrungeTexTiling, grungeTextureTiling);

            m.SetColor(Ids.RingColor, swirlColor);
            m.SetFloat(Ids.RingStrength, swirlStrength);
            m.SetFloat(Ids.RingRadius, swirlRadius);
            m.SetFloat(Ids.RingCount, swirlTurns);
            m.SetFloat(Ids.RingWidth, swirlLineWidth);
            m.SetFloat(Ids.RingSwirl, swirlTwist);
            m.SetFloat(Ids.RingTilt, swirlTilt);
            m.SetFloat(Ids.RingSpeed, swirlSpinSpeed);

            m.SetFloat(Ids.PulseAmount, pulseAmount);
            m.SetFloat(Ids.PulseSpeed, pulseSpeed);

            m.SetColor(Ids.ShineColor, shineColor);
            m.SetFloat(Ids.ShineStrength, shineStrength);
            m.SetFloat(Ids.ShineSpeed, shineSpeed);
            m.SetFloat(Ids.ShineWidth, shineWidth);
            m.SetFloat(Ids.ShineAngle, shineAngle);

            m.SetFloat(Ids.Pixelate, pixelate ? 1f : 0f);
            m.SetFloat(Ids.PixelDensity, pixelsPerTextHeight);

            m.SetFloat(Ids.FaceDilate, faceDilate);
            m.SetColor(Ids.OutlineColor, outlineColor);
            m.SetFloat(Ids.OutlineWidth, outlineWidth);
            m.SetFloat(Ids.OutlineSoftness, outlineSoftness);
            m.SetFloat(Ids.EnableGlow, edgeGlow ? 1f : 0f);
            if (edgeGlow) m.EnableKeyword("GLOW_ON"); else m.DisableKeyword("GLOW_ON");
            m.SetColor(Ids.GlowColor, glowColor);
            m.SetFloat(Ids.GlowOffset, glowOffset);
            m.SetFloat(Ids.GlowOuter, glowWidth);
            m.SetFloat(Ids.GlowPower, glowFalloff);
        }

        // ------------------------------------------------------------------ presets

        /// <summary>Black ink letters, grungy gold burst on one glyph, hot core and a halo swirl.</summary>
        public static TitleLogoLook InkGoldBurst() => new TitleLogoLook();

        /// <summary>All-gold, top-bright gradient with light grunge. Good for the subtitle line.</summary>
        public static TitleLogoLook GoldGradient() => new TitleLogoLook
        {
            fillWholeText = 1f,
            verticalGradient = 0.85f,
            accentShadow = new Color(0.62f, 0.3f, 0.04f, 1f),
            accentMid = new Color(0.93f, 0.62f, 0.13f, 1f),
            accentHighlight = new Color(1f, 0.9f, 0.42f, 1f),
            grungeStrength = 0.25f,
            coreIntensity = 0f,
            swirlStrength = 0f,
            pulseAmount = 0f,
        };

        /// <summary>Ink letters with a crimson-to-orange burst; hotter and angrier.</summary>
        public static TitleLogoLook CrimsonBurst() => new TitleLogoLook
        {
            accentShadow = new Color(0.18f, 0.02f, 0.03f, 1f),
            accentMid = new Color(0.78f, 0.1f, 0.08f, 1f),
            accentHighlight = new Color(1f, 0.62f, 0.28f, 1f),
            coreColor = new Color(1f, 0.75f, 0.5f, 1f),
            swirlColor = new Color(1f, 0.85f, 0.75f, 1f),
            burstRadius = 1f,
        };

        /// <summary>
        /// Gold burst tuned for a UI Image logo, where 1 unit = the whole image height (padding included),
        /// so the burst, core and swirl are smaller and the grunge finer than on text.
        /// </summary>
        public static TitleLogoLook SpriteDefault() => new TitleLogoLook
        {
            burstRadius = 0.33f,
            grungeScale = 24f,
            coreSize = 0.06f,
            coreIntensity = 1f,
            swirlRadius = 0.12f,
            swirlLineWidth = 0.008f,
            swirlStrength = 0.9f,
        };

        /// <summary><see cref="SpriteDefault"/> snapped to the sprite's own pixels, for pixel-art images.</summary>
        public static TitleLogoLook PixelSprite()
        {
            TitleLogoLook look = SpriteDefault();
            look.pixelate = true;
            look.grungeScale = 14f;
            look.burstSoftness = 0.3f;
            look.swirlLineWidth = 0.02f;
            look.swirlTurns = 1.5f;
            return look;
        }

        /// <summary>Gold burst snapped to a chunky pixel grid, for pixel fonts.</summary>
        public static TitleLogoLook PixelGold() => new TitleLogoLook
        {
            pixelate = true,
            pixelsPerTextHeight = 32f,
            grungeScale = 10f,
            burstSoftness = 0.3f,
            swirlLineWidth = 0.03f,
            swirlTurns = 1.5f,
        };

        internal static class Ids
        {
            public static readonly int FaceColor = Shader.PropertyToID("_FaceColor");
            public static readonly int VertexTint = Shader.PropertyToID("_VertexTint");
            public static readonly int GoldFill = Shader.PropertyToID("_GoldFill");
            public static readonly int BurstRadius = Shader.PropertyToID("_BurstRadius");
            public static readonly int BurstSoftness = Shader.PropertyToID("_BurstSoftness");
            public static readonly int BurstBreakup = Shader.PropertyToID("_BurstBreakup");
            public static readonly int GoldShadow = Shader.PropertyToID("_GoldShadow");
            public static readonly int GoldColor = Shader.PropertyToID("_GoldColor");
            public static readonly int GoldHighlight = Shader.PropertyToID("_GoldHighlight");
            public static readonly int GradientMix = Shader.PropertyToID("_GradientMix");
            public static readonly int CoreColor = Shader.PropertyToID("_CoreColor");
            public static readonly int CoreSize = Shader.PropertyToID("_CoreSize");
            public static readonly int CoreIntensity = Shader.PropertyToID("_CoreIntensity");
            public static readonly int GrungeScale = Shader.PropertyToID("_GrungeScale");
            public static readonly int GrungeStrength = Shader.PropertyToID("_GrungeStrength");
            public static readonly int GrungeContrast = Shader.PropertyToID("_GrungeContrast");
            public static readonly int GrungeFlow = Shader.PropertyToID("_GrungeFlow");
            public static readonly int GrungeTex = Shader.PropertyToID("_GrungeTex");
            public static readonly int GrungeTexStrength = Shader.PropertyToID("_GrungeTexStrength");
            public static readonly int GrungeTexTiling = Shader.PropertyToID("_GrungeTexTiling");
            public static readonly int RingColor = Shader.PropertyToID("_RingColor");
            public static readonly int RingStrength = Shader.PropertyToID("_RingStrength");
            public static readonly int RingRadius = Shader.PropertyToID("_RingRadius");
            public static readonly int RingCount = Shader.PropertyToID("_RingCount");
            public static readonly int RingWidth = Shader.PropertyToID("_RingWidth");
            public static readonly int RingSwirl = Shader.PropertyToID("_RingSwirl");
            public static readonly int RingTilt = Shader.PropertyToID("_RingTilt");
            public static readonly int RingSpeed = Shader.PropertyToID("_RingSpeed");
            public static readonly int PulseAmount = Shader.PropertyToID("_PulseAmount");
            public static readonly int PulseSpeed = Shader.PropertyToID("_PulseSpeed");
            public static readonly int ShineColor = Shader.PropertyToID("_ShineColor");
            public static readonly int ShineStrength = Shader.PropertyToID("_ShineStrength");
            public static readonly int ShineSpeed = Shader.PropertyToID("_ShineSpeed");
            public static readonly int ShineWidth = Shader.PropertyToID("_ShineWidth");
            public static readonly int ShineAngle = Shader.PropertyToID("_ShineAngle");
            public static readonly int Pixelate = Shader.PropertyToID("_Pixelate");
            public static readonly int PixelDensity = Shader.PropertyToID("_PixelDensity");
            public static readonly int FaceDilate = Shader.PropertyToID("_FaceDilate");
            public static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
            public static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
            public static readonly int OutlineSoftness = Shader.PropertyToID("_OutlineSoftness");
            public static readonly int EnableGlow = Shader.PropertyToID("_EnableGlow");
            public static readonly int GlowColor = Shader.PropertyToID("_GlowColor");
            public static readonly int GlowOffset = Shader.PropertyToID("_GlowOffset");
            public static readonly int GlowOuter = Shader.PropertyToID("_GlowOuter");
            public static readonly int GlowPower = Shader.PropertyToID("_GlowPower");

            public static readonly int Disintegrate = Shader.PropertyToID("_Disintegrate");
            public static readonly int DisintegrateAngle = Shader.PropertyToID("_DisintegrateAngle");
            public static readonly int DirectionBias = Shader.PropertyToID("_DirectionBias");
            public static readonly int DissolveScale = Shader.PropertyToID("_DissolveScale");
            public static readonly int EmberWidth = Shader.PropertyToID("_EmberWidth");
            public static readonly int CharWidth = Shader.PropertyToID("_CharWidth");
            public static readonly int EmberColor = Shader.PropertyToID("_EmberColor");
            public static readonly int EmberHotColor = Shader.PropertyToID("_EmberHotColor");
            public static readonly int AshColor = Shader.PropertyToID("_AshColor");
            public static readonly int NoiseSeed = Shader.PropertyToID("_NoiseSeed");

            public static readonly int EffectAspect = Shader.PropertyToID("_EffectAspect");
            public static readonly int FocusPoint = Shader.PropertyToID("_FocusPoint");
            public static readonly int IsFlake = Shader.PropertyToID("_IsFlake");

            public static readonly int MainTex = Shader.PropertyToID("_MainTex");
            public static readonly int GradientScale = Shader.PropertyToID("_GradientScale");
            public static readonly int TextureWidth = Shader.PropertyToID("_TextureWidth");
            public static readonly int TextureHeight = Shader.PropertyToID("_TextureHeight");
            public static readonly int WeightNormal = Shader.PropertyToID("_WeightNormal");
            public static readonly int WeightBold = Shader.PropertyToID("_WeightBold");
        }
    }

    /// <summary>How the logo breaks apart. Shared by the shader dissolve and the CPU flakes.</summary>
    [Serializable]
    public sealed class TitleDisintegrateSettings
    {
        [Tooltip("Where the dissolve starts. 0 = left edge first (sweeps right), 90 = bottom first, 180 = right first.")]
        [Range(-180, 180)] public float directionAngle = 0f;
        [Tooltip("0 = pure noise (random holes). 1 = clean sweep along the direction.")]
        [Range(0, 1)] public float directionBias = 0.55f;
        [Range(1, 80)] public float noiseScale = 18f;
        public int seed = 7;
        [Range(0, 0.3f)] public float emberWidth = 0.07f;
        [Tooltip("Darkened, charred band ahead of the ember edge.")]
        [Range(0, 0.3f)] public float charWidth = 0.08f;
        [ColorUsage(false, true)] public Color emberColor = new Color(1f, 0.45f, 0.08f, 1f);
        [ColorUsage(false, true)] public Color emberHotColor = new Color(1f, 0.92f, 0.6f, 1f);
        public Color ashColor = new Color(0.16f, 0.14f, 0.13f, 1f);

        [Header("Flakes")]
        public bool flakes = true;
        [Tooltip("Flake grid density (flakes across one logo height). With Pixelate + Match Pixel Grid, each flake is a whole number of pixels, as close to this as possible.")]
        [Range(6, 120)] public float flakesPerTextHeight = 30f;
        public bool matchPixelGrid = true;
        [Range(0, 1)] public float flakeChance = 0.8f;
        [Tooltip("How long a flake lives, as a fraction of the whole disintegrate animation.")]
        [Range(0.05f, 1)] public float flakeLifetime = 0.35f;
        [Tooltip("Distance a flake travels over its life, in text heights (x right, y up).")]
        public Vector2 wind = new Vector2(1.6f, 0.6f);
        [Range(0, 90)] public float windSpreadDegrees = 25f;
        [Range(0, 1)] public float turbulence = 0.25f;
        [Tooltip("Turns over the flake's life (random direction). Off automatically when Pixelate is on.")]
        [Range(0, 4)] public float spin = 1.2f;
        [Range(0, 1)] public float shrink = 0.5f;
        [Range(64, 12000)] public int maxFlakes = 4000;

        public TitleDisintegrateSettings Clone() => (TitleDisintegrateSettings)MemberwiseClone();

        internal void ApplyTo(Material m, float dissolveProgress)
        {
            m.SetFloat(TitleLogoLook.Ids.Disintegrate, dissolveProgress);
            m.SetFloat(TitleLogoLook.Ids.DisintegrateAngle, directionAngle);
            m.SetFloat(TitleLogoLook.Ids.DirectionBias, directionBias);
            m.SetFloat(TitleLogoLook.Ids.DissolveScale, noiseScale);
            m.SetFloat(TitleLogoLook.Ids.EmberWidth, emberWidth);
            m.SetFloat(TitleLogoLook.Ids.CharWidth, charWidth);
            m.SetColor(TitleLogoLook.Ids.EmberColor, emberColor);
            m.SetColor(TitleLogoLook.Ids.EmberHotColor, emberHotColor);
            m.SetColor(TitleLogoLook.Ids.AshColor, ashColor);
            m.SetFloat(TitleLogoLook.Ids.NoiseSeed, Mathf.Max(0, seed));
        }
    }

    /// <summary>Sprite-only settings of <see cref="TitleLogoImage"/>.</summary>
    [Serializable]
    public sealed class TitleSpriteSettings
    {
        [Tooltip("0 = keep the sprite's own colors outside the burst. 1 = flat Ink color (silhouette, like the text).")]
        [Range(0, 1)] public float inkReplace = 0f;
        [Tooltip("How much of the sprite's light/dark shading shows through the accent. 0 for flat silhouette logos.")]
        [Range(0, 1)] public float keepShading = 0.35f;
        [Tooltip("With Pixelate on, snap to the sprite's own pixels (ignores Look > Pixels Per Text Height).")]
        public bool matchSpritePixels = true;
        [Tooltip("Pixel outline around the sprite, in texels (needs transparent padding around the art).")]
        [Range(0, 4)] public float outlineTexels = 0f;
        public Color outlineColor = new Color(0f, 0f, 0f, 1f);
        [Tooltip("Alpha above which a chunk counts as art when spawning flakes (needs Read/Write on the texture).")]
        [Range(0.01f, 1)] public float flakeAlphaThreshold = 0.1f;

        public TitleSpriteSettings Clone() => (TitleSpriteSettings)MemberwiseClone();

        internal void ApplyTo(Material m)
        {
            m.SetFloat(Ids.InkMix, inkReplace);
            m.SetFloat(Ids.DetailKeep, keepShading);
            m.SetFloat(Ids.OutlineWidth, outlineTexels);
            m.SetColor(Ids.OutlineColor, outlineColor);
        }

        static class Ids
        {
            public static readonly int InkMix = Shader.PropertyToID("_InkMix");
            public static readonly int DetailKeep = Shader.PropertyToID("_DetailKeep");
            public static readonly int OutlineWidth = Shader.PropertyToID("_SpriteOutlineWidth");
            public static readonly int OutlineColor = Shader.PropertyToID("_SpriteOutlineColor");
        }
    }
}
