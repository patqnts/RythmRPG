using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Look and feel of the combo streak (the big combo count on the centre-right of the screen while the player
    /// chains hits). Loaded from Resources/Combat/UI/ComboStreakStyle; missing = the defaults below.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Combo Streak Style", fileName = "ComboStreakStyle")]
    public sealed class ComboStreakStyle : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/ComboStreakStyle";

        [Header("Layout (1920x1080 reference)")]
        [Tooltip("Screen point the streak hangs from (0,0 bottom-left .. 1,1 top-right). Also used as the pivot. Default: centre-right.")]
        public Vector2 anchor = new(1f, 0.5f);
        public Vector2 position = new(-90f, 60f);
        public Vector2 size = new(520f, 260f);

        [Header("Text (TextMeshPro)")]
        [Tooltip("Empty = the HUD style's font.")]
        public TMP_FontAsset font;
        [Tooltip("Shown under the number, e.g. \"COMBO\".")]
        public string label = "COMBO";
        [Min(6)] public float numberFontSize = 168f;
        [Min(6)] public float labelFontSize = 46f;
        public Color numberColor = Color.white;
        public Color labelColor = new(1f, 1f, 1f, 0.9f);
        public Color textOutline = new(0f, 0f, 0f, 0.9f);
        [Range(0f, 0.5f)] public float outlineWidth = 0.26f;

        [Header("Heat (color climbs with the streak)")]
        [Min(2)] public int warmThreshold = 10;
        public Color warmColor = new(1f, 0.82f, 0.2f, 1f);
        [Min(3)] public int hotThreshold = 25;
        public Color hotColor = new(1f, 0.42f, 0.18f, 1f);
        [Tooltip("Every Nth combo gets a bigger punch and a shake.")]
        [Min(0)] public int milestoneEvery = 10;

        [Header("Visibility")]
        [Tooltip("Combo needs to reach at least this before the streak appears (1-2 hits looks noisy).")]
        [Min(1)] public int minComboToShow = 2;
        [Tooltip("Slides in from this far to the right the first time it appears.")]
        public float slideInDistance = 70f;
        [Min(0.01f)] public float appearSeconds = 0.14f;

        [Header("Hit (every combo increase) - keep these short for a snappy feel")]
        [Tooltip("The number jumps to this scale instantly and snaps back.")]
        [Min(1f)] public float hitScale = 1.32f;
        [Min(1f)] public float milestoneScale = 1.6f;
        [Min(0.01f)] public float hitSeconds = 0.11f;
        [Tooltip("Alternating tilt kick on each hit (degrees).")]
        public float hitTilt = 7f;
        [Tooltip("The number flashes this color, then settles to its heat color.")]
        public Color flashColor = Color.white;
        [Min(0.01f)] public float flashSeconds = 0.12f;
        [Min(0f)] public float milestoneShake = 14f;

        [Header("Break")]
        public Color breakColor = new(1f, 0.25f, 0.3f, 1f);
        [Min(0.01f)] public float hideSeconds = 0.2f;
        [Tooltip("Drops this far while fading out when the combo breaks.")]
        public float breakDrop = 45f;

        public Color ColorFor(int combo)
        {
            if (combo >= hotThreshold) return hotColor;
            if (combo >= warmThreshold) return warmColor;
            return numberColor;
        }

        public bool IsMilestone(int combo) => milestoneEvery > 0 && combo > 0 && combo % milestoneEvery == 0;

        private static ComboStreakStyle fallback;

        public static ComboStreakStyle LoadOrDefault()
        {
            ComboStreakStyle style = Resources.Load<ComboStreakStyle>(ResourcePath);
            if (style != null) return style;
            if (fallback == null)
            {
                fallback = CreateInstance<ComboStreakStyle>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
