using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>What the gauge's multiplier number shows while the pattern plays.</summary>
    public enum PowerGaugeReadout
    {
        [Tooltip("Power banked so far: starts at x0.00 and charges up with every hit. The number the ability ends on " +
                 "is exactly its multiplier.")]
        Banked,
        [Tooltip("Accuracy so far: starts at the best multiplier and drops on mistakes.")]
        RunningAccuracy
    }

    public enum PowerGaugeOrientation
    {
        [Tooltip("Fills bottom to top; the multiplier rides the top of the fill, on the bar's right.")]
        Vertical,
        [Tooltip("Fills left to right; the multiplier sits on the bar's right.")]
        Horizontal
    }

    /// <summary>A multiplier band of the power gauge (colour, and the word stamped when a pattern ends in it).</summary>
    [Serializable]
    public sealed class PowerGaugeTier
    {
        [Tooltip("The tier applies from this multiplier up.")]
        public float minMultiplier;
        public Color color = Color.white;
        [Tooltip("Stamped above the gauge when the pattern ends in this tier. Empty = no stamp.")]
        public string label = string.Empty;
    }

    /// <summary>
    /// Look and feel of the power gauge shown while the player plays an ability's rhythm pattern. Loaded from
    /// Resources/Combat/UI/PowerGaugeStyle; missing = the defaults below.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Power Gauge Style", fileName = "PowerGaugeStyle")]
    public sealed class PowerGaugeStyle : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/PowerGaugeStyle";

        [Header("Layout (1920x1080 reference)")]
        public PowerGaugeOrientation orientation = PowerGaugeOrientation.Vertical;
        [Tooltip("Screen point the gauge hangs from (0,0 bottom-left .. 1,1 top-right). Also used as the pivot. " +
                 "Default: left side, middle of the screen.")]
        public Vector2 anchor = new(0f, 0.5f);
        public Vector2 position = new(64f, 30f);
        [Tooltip("Vertical: stand just left of the leftmost lane, bottom on the hit line, following it as the camera " +
                 "moves; the multiplier and stamp go on the bar's left so they never cover notes. Off = Anchor / Position.")]
        public bool besideFirstLane = true;
        [Tooltip("Pixels between the bar and the left edge of the first lane.")]
        public float laneGap = 18f;
        [Tooltip("Pixels the bar's bottom sits above (+) or below (-) the hit line.")]
        public float laneHeightOffset = 0f;
        [Tooltip("Size of the bar itself (width x height). Vertical: tall and thin; Horizontal: wide and flat " +
                 "(swapped automatically if it does not match the orientation). The title sits above the bar and the " +
                 "multiplier to its right.")]
        public Vector2 barSize = new(28f, 380f);

        [Header("Readout")]
        public PowerGaugeReadout readout = PowerGaugeReadout.Banked;
        [Tooltip("A faint bar behind the fill showing the most power still reachable. It drops on every mistake.")]
        public bool showPotential = true;
        [Tooltip("Thin marks on the bar where each tier starts.")]
        public bool showTierTicks = true;

        [Header("Text (TextMeshPro)")]
        [Tooltip("Empty = the HUD style's font.")]
        public TMP_FontAsset font;
        public string title = "POWER";
        [Min(6)] public float titleFontSize = 22f;
        [Tooltip("string.Format pattern for the multiplier, e.g. x{0:0.00} or {0:P0}.")]
        public string multiplierFormat = "x{0:0.00}";
        [Min(6)] public float multiplierFontSize = 46f;
        [Min(6)] public float rankFontSize = 40f;
        public Color titleColor = new(1f, 1f, 1f, 0.9f);
        public Color textOutline = new(0f, 0f, 0f, 0.9f);
        [Range(0f, 0.5f)] public float outlineWidth = 0.24f;

        [Header("Bar")]
        public Color frame = new(0.93f, 0.9f, 0.82f, 1f);
        public Color background = new(0.07f, 0.06f, 0.11f, 0.92f);
        public Color potential = new(1f, 1f, 1f, 0.16f);
        public Color tick = new(1f, 1f, 1f, 0.45f);
        [Min(0f)] public float frameThickness = 3f;
        [Tooltip("Optional sprites (9-sliced if they have borders). Empty = flat pixel rectangles.")]
        public Sprite fillSprite;
        public Sprite backgroundSprite;
        public Sprite frameSprite;

        [Header("Tiers (fill and number colour; lowest first)")]
        public List<PowerGaugeTier> tiers = new()
        {
            new PowerGaugeTier { minMultiplier = 0f, color = new Color(0.62f, 0.66f, 0.82f), label = "WEAK" },
            new PowerGaugeTier { minMultiplier = 0.5f, color = new Color(0.35f, 0.82f, 1f), label = "GOOD" },
            new PowerGaugeTier { minMultiplier = 0.8f, color = new Color(0.45f, 1f, 0.5f), label = "GREAT" },
            new PowerGaugeTier { minMultiplier = 1f, color = new Color(1f, 0.82f, 0.25f), label = "FULL POWER" }
        };
        [Tooltip("Stamp when every note was Perfect. Empty = use the tier's label.")]
        public string flawlessLabel = "PERFECT!";
        public Color flawlessColor = new(1f, 0.95f, 0.55f, 1f);

        [Header("Per-note flash (the bar flashes the judgement's colour)")]
        public Color perfectFlash = new(1f, 0.92f, 0.45f, 1f);
        public Color goodFlash = new(0.55f, 1f, 0.6f, 1f);
        public Color badFlash = new(1f, 0.62f, 0.3f, 1f);
        public Color missFlash = new(1f, 0.25f, 0.3f, 1f);
        [Range(0f, 1f)] public float flashAlpha = 0.75f;
        [Min(0.01f)] public float flashSeconds = 0.16f;

        [Header("Motion")]
        [Min(0.01f)] public float appearSeconds = 0.18f;
        [Tooltip("Slides in from this far off (from the left when vertical, from above when horizontal).")]
        public float slideInDistance = 60f;
        [Min(0.01f)] public float fillSeconds = 0.12f;
        [Tooltip("The potential bar waits this long after a mistake, then drains (so the lost chunk reads).")]
        [Min(0f)] public float potentialDelay = 0.2f;
        [Min(0.01f)] public float potentialSeconds = 0.35f;
        [Tooltip("The multiplier jumps to this scale on each hit and snaps back.")]
        [Min(1f)] public float hitPunch = 1.18f;
        [Min(0.01f)] public float hitSeconds = 0.12f;
        [Tooltip("Shake on a Miss, in canvas pixels.")]
        [Min(0f)] public float missShake = 7f;
        [Tooltip("Punch of the multiplier and the stamp when the pattern ends.")]
        [Min(1f)] public float finishPunch = 1.5f;
        [Min(0.01f)] public float finishSeconds = 0.3f;
        [Tooltip("Seconds the gauge stays after the ability's hit lands.")]
        [Min(0f)] public float holdAfterImpact = 0.45f;
        [Min(0.01f)] public float hideSeconds = 0.25f;

        public bool IsVertical => orientation == PowerGaugeOrientation.Vertical;

        /// <summary>Vertical beside the first lane: labels go on the bar's left.</summary>
        public bool LabelsOnLeft => IsVertical && besideFirstLane;

        /// <summary>The bar size, oriented to match <see cref="orientation"/>.</summary>
        public Vector2 OrientedBarSize
        {
            get
            {
                Vector2 size = new(Mathf.Max(4f, barSize.x), Mathf.Max(4f, barSize.y));
                bool tall = size.y >= size.x;
                return tall == IsVertical ? size : new Vector2(size.y, size.x);
            }
        }

        /// <summary>Where the gauge slides in from (and back out to).</summary>
        public Vector2 SlideOffset => IsVertical ? new Vector2(-slideInDistance, 0f) : new Vector2(0f, slideInDistance);

        /// <summary>The tier that <paramref name="multiplier"/> falls in (the highest one whose minimum it reaches).</summary>
        public PowerGaugeTier TierFor(float multiplier)
        {
            PowerGaugeTier best = null;
            if (tiers != null)
                foreach (PowerGaugeTier tier in tiers)
                {
                    if (tier == null || multiplier + 0.0001f < tier.minMultiplier) continue;
                    if (best == null || tier.minMultiplier >= best.minMultiplier) best = tier;
                }
            if (best != null) return best;
            return tiers != null && tiers.Count > 0 && tiers[0] != null ? tiers[0] : new PowerGaugeTier();
        }

        public Color FlashFor(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => perfectFlash,
            HitJudgement.Good => goodFlash,
            HitJudgement.Bad => badFlash,
            _ => missFlash
        };

        private static PowerGaugeStyle fallback;

        public static PowerGaugeStyle LoadOrDefault()
        {
            PowerGaugeStyle style = Resources.Load<PowerGaugeStyle>(ResourcePath);
            if (style != null) return style;
            if (fallback == null)
            {
                fallback = CreateInstance<PowerGaugeStyle>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
