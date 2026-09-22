using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    /// <summary>Look of one resource bar (colors, optional sprites, title).</summary>
    [Serializable]
    public sealed class ResourceBarStyle
    {
        public string title = string.Empty;
        public Color fill = new(0.36f, 0.86f, 0.42f);
        [Tooltip("Fill color the bar pulses toward when the value is at or below Low Threshold.")]
        public Color lowFill = new(1f, 0.28f, 0.28f);
        [Range(0f, 1f)] public float lowThreshold = 0.25f;
        [Tooltip("Color of the chunk that drains away after a loss.")]
        public Color loseTrail = new(1f, 0.93f, 0.62f);
        [Tooltip("Color of the chunk that fills in after a gain.")]
        public Color gainTrail = new(0.75f, 1f, 0.8f);
        public Color background = new(0.07f, 0.06f, 0.11f, 0.92f);
        public Color frame = new(0.93f, 0.9f, 0.82f, 1f);
        [Tooltip("Optional sprites (9-sliced if they have borders). Empty = flat pixel rectangles.")]
        public Sprite fillSprite;
        public Sprite backgroundSprite;
        public Sprite frameSprite;
        [Min(0f)] public float frameThickness = 3f;
        public bool showNumbers = true;
    }

    /// <summary>Where a bar sits on the 1920x1080 reference canvas.</summary>
    [Serializable]
    public sealed class HudBarLayout
    {
        [Tooltip("Screen corner/edge the bar hangs from (0,0 bottom-left .. 1,1 top-right). Also used as the pivot.")]
        public Vector2 anchor = Vector2.zero;
        public Vector2 position = new(40f, 40f);
        public Vector2 size = new(380f, 26f);
        [Tooltip("Direction the bar slides in from when combat starts (in pixels).")]
        public Vector2 introOffset = new(-60f, 0f);
    }

    /// <summary>How bars animate when their value changes.</summary>
    [Serializable]
    public sealed class ResourceBarMotion
    {
        [Tooltip("Seconds for the fill to drop to a lower value.")]
        [Min(0f)] public float lossSeconds = 0.12f;
        [Tooltip("Seconds for the fill to rise to a higher value.")]
        [Min(0f)] public float gainSeconds = 0.45f;
        [Tooltip("The lost chunk stays visible this long before it drains.")]
        [Min(0f)] public float trailDelay = 0.35f;
        [Min(0f)] public float trailSeconds = 0.45f;
        [Tooltip("Seconds for the number to count to the new value.")]
        [Min(0f)] public float countSeconds = 0.35f;
        [Tooltip("Shake on loss, in canvas pixels; scaled by how big the loss was (full at 20% of the bar).")]
        [Min(0f)] public float shakePixels = 7f;
        [Min(0f)] public float shakeSeconds = 0.25f;
        [Min(0f)] public float flashSeconds = 0.2f;
        [Tooltip("Scale pulse on gain (0.06 = 6% bigger).")]
        [Min(0f)] public float gainPulse = 0.06f;
        [Min(0f)] public float pulseSeconds = 0.25f;
        [Tooltip("Pulses per second of the low-value warning.")]
        [Min(0f)] public float lowPulseSpeed = 3f;

        public ResourceBarMotion Clone() => (ResourceBarMotion)MemberwiseClone();
    }

    /// <summary>
    /// Everything about the combat HUD that a designer may want to change: bar colors, layout, motion, font, and
    /// optional prefabs that replace the generated template bars. Loaded from Resources/Combat/UI/CombatHudStyle.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/HUD Style", fileName = "CombatHudStyle")]
    public sealed class CombatHudStyle : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/CombatHudStyle";

        [Header("Custom bar prefabs (optional, replace the generated template)")]
        [SerializeField] private ResourceBarView playerHealthPrefab;
        [SerializeField] private ResourceBarView playerManaPrefab;
        [SerializeField] private ResourceBarView enemyHealthPrefab;

        [Header("Bars")]
        [SerializeField] private ResourceBarStyle playerHealth = new() { title = "HP" };
        [SerializeField] private ResourceBarStyle playerMana = new()
        {
            title = "MP",
            fill = new Color(0.3f, 0.62f, 1f),
            lowFill = new Color(0.3f, 0.62f, 1f),
            lowThreshold = 0f,
            loseTrail = new Color(0.8f, 0.92f, 1f),
            gainTrail = new Color(0.72f, 0.9f, 1f)
        };
        [SerializeField] private ResourceBarStyle enemyHealth = new()
        {
            fill = new Color(0.9f, 0.24f, 0.3f),
            lowFill = new Color(1f, 0.55f, 0.2f),
            lowThreshold = 0.25f
        };

        [Header("Layout (1920x1080 reference)")]
        [SerializeField] private HudBarLayout playerHealthLayout = new()
        {
            anchor = new Vector2(0f, 0f), position = new Vector2(40f, 92f), size = new Vector2(400f, 28f),
            introOffset = new Vector2(-80f, 0f)
        };
        [SerializeField] private HudBarLayout playerManaLayout = new()
        {
            anchor = new Vector2(0f, 0f), position = new Vector2(40f, 44f), size = new Vector2(320f, 20f),
            introOffset = new Vector2(-80f, 0f)
        };
        [SerializeField] private HudBarLayout enemyHealthLayout = new()
        {
            anchor = new Vector2(0.5f, 1f), position = new Vector2(0f, -44f), size = new Vector2(560f, 24f),
            introOffset = new Vector2(0f, 70f)
        };

        [Header("Motion")]
        [SerializeField] private ResourceBarMotion motion = new();
        [Tooltip("Seconds for the HUD to slide in at battle start and out at battle end.")]
        [SerializeField, Min(0f)] private float introSeconds = 0.4f;

        [Header("Text (TextMeshPro)")]
        [Tooltip("TextMeshPro font for every HUD text. Empty = generated from Legacy Font below, else TMP's default font.")]
        [SerializeField] private TMP_FontAsset fontAsset;
        [Tooltip("Old uGUI font, kept only so existing assets still pick their font. Converted to a TMP font automatically.")]
        [FormerlySerializedAs("font")]
        [SerializeField] private Font legacyFont;
        [SerializeField, Min(6)] private int fontSize = 20;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color textOutline = new(0f, 0f, 0f, 0.85f);

        public ResourceBarView PlayerHealthPrefab => playerHealthPrefab;
        public ResourceBarView PlayerManaPrefab => playerManaPrefab;
        public ResourceBarView EnemyHealthPrefab => enemyHealthPrefab;
        public ResourceBarStyle PlayerHealth => playerHealth;
        public ResourceBarStyle PlayerMana => playerMana;
        public ResourceBarStyle EnemyHealth => enemyHealth;
        public HudBarLayout PlayerHealthLayout => playerHealthLayout;
        public HudBarLayout PlayerManaLayout => playerManaLayout;
        public HudBarLayout EnemyHealthLayout => enemyHealthLayout;
        public ResourceBarMotion Motion => motion;
        public float IntroSeconds => introSeconds;
        public TMP_FontAsset FontAsset => CombatText.ResolveFont(fontAsset, legacyFont);
        public Font LegacyFont => legacyFont;
        public int FontSize => fontSize;
        public Color TextColor => textColor;
        public Color TextOutline => textOutline;

        private static CombatHudStyle fallback;

        public static CombatHudStyle LoadOrDefault()
        {
            CombatHudStyle style = Resources.Load<CombatHudStyle>(ResourcePath);
            if (style != null) return style;
            if (fallback == null)
            {
                fallback = CreateInstance<CombatHudStyle>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
