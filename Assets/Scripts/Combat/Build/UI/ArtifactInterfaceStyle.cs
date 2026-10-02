using RythmRPG.UI.Title;
using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Shared art direction for the artifact's inventory and reward projections.</summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Artifact Interface Style", fileName = "ArtifactInterfaceStyle")]
    public sealed class ArtifactInterfaceStyle : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/ArtifactInterfaceStyle";
        [Header("Typography")]
        public TMP_FontAsset headingFont;
        public TMP_FontAsset bodyFont;
        [Tooltip("Readability multiplier for UI that uses Body Font. Monogram SDF has a compact visual em, so it needs a larger point size on screen.")]
        [Range(1f, 2f)] public float bodyFontScale = 1.35f;
        [Header("Iridescent light")]
        public Color cyan = new(0.48f, 0.94f, 1f, 1f);
        public Color lilac = new(0.79f, 0.65f, 1f, 1f);
        public Color pearl = new(0.94f, 0.98f, 1f, 1f);
        public Color glass = new(.07f, .07f, .07f, .42f);
        [Header("Title-menu disintegration (shader references keep the effect in builds)")]
        public Shader textShader;
        public Shader imageShader;
        public Shader livingOutlineShader;
        public TitleDisintegrateSettings disintegration = new()
        {
            directionAngle = -90f, directionBias = 0.85f, noiseScale = 12.8f,
            emberWidth = 0.034f, charWidth = 0.035f, flakes = false,
            emberColor = new Color(0.5f, 0.92f, 1f), emberHotColor = Color.white,
            ashColor = new Color(0.72f, 0.59f, 0.93f)
        };
        [Min(0f)] public float revealSeconds = 0.55f;
        [Min(0f)] public float dismissSeconds = 0.42f;
        [Tooltip("The outer panel dissolves and masks every child together, using the menu logo effect.")]
        public bool animateDisintegration = true;

        [Header("Slime response on hover / selection")]
        public bool slimeResponse = true;
        [Tooltip("Time for the noisy border ripple to settle. Uses unscaled UI time.")]
        [Min(.05f)] public float wobbleSeconds = .7f;
        [Tooltip("How far the outline's dissolve noise can ripple. Text and icons stay still.")]
        [Range(0f, 40f)] public float outlineRipplePixels = 18f;
        [Range(1f, 8f)] public float wobbleCycles = 3f;
        [Header("Selected item edge disintegration")]
        [Tooltip("How far inward from the item's outer sides the shared dissolve may reach, in UI pixels. The center stays intact. Zero disables disintegration.")]
        [InspectorName("Edge Depth Pixels")]
        [Min(0f)] public float pokeEdgeDepthPixels = 24f;
        [Tooltip("Time to break the edges apart after hover or selection, before they restore. Uses unscaled UI time.")]
        [InspectorName("Disintegrate Seconds")]
        [Min(0f)] public float pokeDisintegrateSeconds = .12f;
        [Tooltip("Time for the dissolved edges to restore after reaching maximum disintegration.")]
        [InspectorName("Restore Seconds")]
        [Min(0f)] public float pokeGlitchSeconds = .55f;
        [Tooltip("How strongly the outer region breaks apart. Background, outline and any contents near the sides share one mask; the center is protected.")]
        [InspectorName("Disintegration Strength")]
        [Range(0f, 1f)] public float pokeGlitchStrength = .7f;
        [Tooltip("Pixel step of the selected item's living edge.")]
        [Range(1f, 8f)] public float pokePixelSize = 2f;

        [Header("Selected item edge colors and bands")]
        [InspectorName("Ember Width")]
        [Range(0f, .25f)] public float pokeEmberWidth = .034f;
        [InspectorName("Char Width")]
        [Range(0f, .25f)] public float pokeCharWidth = .035f;
        [InspectorName("Ash Width")]
        [Range(0f, .25f)] public float pokeAshWidth = .02f;
        [InspectorName("Ember Color"), ColorUsage(true, true)]
        public Color pokeEmberColor = new(.5f, .92f, 1f);
        [InspectorName("Hot Ember Color"), ColorUsage(true, true)]
        public Color pokeEmberHotColor = Color.white;
        [InspectorName("Char Color")]
        public Color pokeCharColor = new(.015f, .02f, .025f);
        [InspectorName("Ash Color")]
        public Color pokeAshColor = new(.72f, .59f, .93f);

        [Header("Selected item edge noise")]
        [Tooltip("Amount of noise in both the eroding edge and wobble. Zero produces a smooth, uniform edge; one preserves the original noise.")]
        [InspectorName("Noise Amount")]
        [Range(0f, 2f)] public float pokeNoiseAmount = 1f;
        [Tooltip("Size/frequency of the noisy pattern. Higher values give smaller, more frequent details.")]
        [InspectorName("Noise Scale")]
        [Min(.1f)] public float pokeNoiseScale = 5f;
        [Tooltip("Change this to choose a different edge noise pattern.")]
        [InspectorName("Noise Seed")]
        [Min(0)] public int pokeNoiseSeed = 7;
        [Tooltip("Choose a new shared pattern each time an item is hovered or selected. The seed stays fixed throughout that disintegrate/restore pulse. Off uses Noise Seed.")]
        [InspectorName("Randomize Seed Each Poke")]
        public bool pokeRandomizeSeed;

        internal void ApplyEdgeAppearance(Material target, int? noiseSeed = null)
        {
            target.SetFloat("_EmberWidth", pokeEmberWidth);
            target.SetFloat("_CharWidth", pokeCharWidth);
            target.SetFloat("_AshWidth", pokeAshWidth);
            target.SetColor("_EmberColor", pokeEmberColor);
            target.SetColor("_EmberHotColor", pokeEmberHotColor);
            target.SetColor("_CharColor", pokeCharColor);
            target.SetColor("_AshColor", pokeAshColor);
            target.SetFloat("_NoiseAmount", pokeNoiseAmount);
            target.SetFloat("_DissolveScale", pokeNoiseScale);
            target.SetFloat("_NoiseSeed", noiseSeed ?? pokeNoiseSeed);
        }

        private static ArtifactInterfaceStyle fallback;
        public static ArtifactInterfaceStyle Load()
        {
            var style = Resources.Load<ArtifactInterfaceStyle>(ResourcePath);
            if (style != null) return style;
            if (fallback == null)
            {
                fallback = CreateInstance<ArtifactInterfaceStyle>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }

        public TitleLogoLook ProjectionLook() => new()
        {
            ink = Color.white, useVertexColor = 1f, fillWholeText = 0f,
            burstRadius = 0f, burstBreakup = 0f, burstSoftness = 0.01f,
            accentShadow = Color.white, accentMid = Color.white, accentHighlight = Color.white,
            grungeStrength = 0f, grungeTextureStrength = 0f, coreIntensity = 0f,
            swirlStrength = 0f, pulseAmount = 0f, shineStrength = 0.12f,
            shineColor = pearl, shineSpeed = 0.16f, shineWidth = 0.055f,
            outlineWidth = 0f, outlineColor = Color.clear, edgeGlow = false
        };

        public float BodyFontSize(float baseSize) => bodyFont != null
            ? Mathf.Max(1f, baseSize * Mathf.Max(1f, bodyFontScale))
            : baseSize;
    }
}
