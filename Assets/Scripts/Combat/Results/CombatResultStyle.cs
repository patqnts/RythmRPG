using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    /// <summary>One line on the result screen: which stat, its label and (optionally) its color.</summary>
    [Serializable]
    public sealed class ResultRowDefinition
    {
        public CombatStat stat = CombatStat.Perfect;
        public string label = "PERFECT";
        [Tooltip("Label color. Alpha 0 = the style's default label color.")]
        public Color color = new(1f, 1f, 1f, 0f);
    }

    /// <summary>The TextMeshPro material the rank letter uses for one grade label (SS, S, A...).</summary>
    [Serializable]
    public sealed class RankTextLook
    {
        public string grade = "S";
        [Tooltip("A material made for the Rank Font (e.g. Assets/Shader/Combat/RankText/Materials).")]
        public Material material;
    }

    /// <summary>
    /// Look, wording, layout rows, timing and sounds of the battle result screen. Loaded from
    /// Resources/Combat/UI/CombatResultStyle; missing = the defaults below. For full layout control, build the screen
    /// with Tools > Rythm RPG > Combat > Create Result Screen In Scene, edit it, save it as a prefab and assign it
    /// to Screen Prefab (or to the CombatController's Result Screen slot).
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Result Style", fileName = "CombatResultStyle")]
    public sealed class CombatResultStyle : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/CombatResultStyle";

        [Header("Custom screen (optional, replaces the generated template)")]
        [SerializeField] private CombatResultScreen screenPrefab;

        [Header("Text")]
        [SerializeField] private string victoryTitle = "VICTORY";
        [SerializeField] private string defeatTitle = "DEFEAT";
        [Tooltip("{0} = enemy name. Empty = no subtitle.")]
        [SerializeField] private string subtitleFormat = "vs {0}";
        [SerializeField] private string gradeCaption = "RANK";
        [SerializeField] private string continuePrompt = "PRESS ANY KEY TO CONTINUE";
        [SerializeField] private string fullComboBadge = "FULL COMBO";
        [SerializeField] private string allPerfectBadge = "ALL PERFECT";
        [SerializeField] private string noDamageBadge = "NO DAMAGE";

        [Header("Rows")]
        [SerializeField] private List<ResultRowDefinition> mainRows = new()
        {
            new ResultRowDefinition { stat = CombatStat.Perfect, label = "PERFECT", color = new Color(1f, 0.84f, 0.2f) },
            new ResultRowDefinition { stat = CombatStat.Good, label = "GOOD", color = new Color(0.2f, 1f, 0.75f) },
            new ResultRowDefinition { stat = CombatStat.Bad, label = "BAD", color = new Color(1f, 0.5f, 0.12f) },
            new ResultRowDefinition { stat = CombatStat.Miss, label = "MISS", color = new Color(1f, 0.2f, 0.35f) },
            new ResultRowDefinition { stat = CombatStat.MaxCombo, label = "MAX COMBO" },
            new ResultRowDefinition { stat = CombatStat.Accuracy, label = "ACCURACY" },
            new ResultRowDefinition { stat = CombatStat.Score, label = "SCORE" }
        };
        [SerializeField] private List<ResultRowDefinition> detailRows = new()
        {
            new ResultRowDefinition { stat = CombatStat.DamageDealt, label = "DAMAGE DEALT" },
            new ResultRowDefinition { stat = CombatStat.DamageTaken, label = "DAMAGE TAKEN" },
            new ResultRowDefinition { stat = CombatStat.Healed, label = "HEALED" },
            new ResultRowDefinition { stat = CombatStat.Turns, label = "TURNS" },
            new ResultRowDefinition { stat = CombatStat.BattleTime, label = "TIME" },
            new ResultRowDefinition { stat = CombatStat.MostUsedAbility, label = "FAVORITE" }
        };

        [Header("Colors")]
        [SerializeField] private Color dimColor = new(0.02f, 0.02f, 0.05f, 0.82f);
        [SerializeField] private Color panelColor = new(0.07f, 0.06f, 0.12f, 0.96f);
        [SerializeField] private Color panelFrameColor = new(0.93f, 0.9f, 0.82f, 1f);
        [SerializeField] private Color victoryColor = new(1f, 0.85f, 0.3f);
        [SerializeField] private Color defeatColor = new(1f, 0.3f, 0.35f);
        [SerializeField] private Color labelColor = new(0.8f, 0.8f, 0.88f);
        [SerializeField] private Color valueColor = Color.white;
        [SerializeField] private Color badgeColor = new(1f, 0.9f, 0.4f);
        [SerializeField] private Color promptColor = new(1f, 1f, 1f, 0.85f);
        [SerializeField] private Color outlineColor = new(0f, 0f, 0f, 0.9f);

        [Header("Font (TextMeshPro)")]
        [Tooltip("TextMeshPro font for every result-screen text except the rank letter. Empty = generated from Legacy Font, else TMP's default font.")]
        [SerializeField] private TMP_FontAsset fontAsset;
        [Tooltip("Old uGUI font, kept only so existing assets still pick their font. Converted to a TMP font automatically.")]
        [FormerlySerializedAs("font")]
        [SerializeField] private Font legacyFont;

        [Header("Rank letter (TextMeshPro)")]
        [Tooltip("Font of the rank letter. Must be the font the rank materials were made for (Combat Rank SDF).")]
        [SerializeField] private TMP_FontAsset rankFont;
        [Tooltip("Material per grade label. Unlisted grades use the rank font's own material.")]
        [SerializeField] private List<RankTextLook> rankMaterials = new()
        {
            new RankTextLook { grade = "SS" }, new RankTextLook { grade = "S" }, new RankTextLook { grade = "A" },
            new RankTextLook { grade = "B" }, new RankTextLook { grade = "C" }, new RankTextLook { grade = "D" }
        };
        [Tooltip("Paragraph texture mapping: both letters of SS share one continuous finish (recommended by the rank materials).")]
        [SerializeField] private bool rankParagraphMapping = true;
        [SerializeField, Min(8)] private int titleSize = 96;
        [SerializeField, Min(8)] private int subtitleSize = 30;
        [SerializeField, Min(8)] private int rowSize = 34;
        [SerializeField, Min(8)] private int detailSize = 26;
        [SerializeField, Min(8)] private int gradeSize = 250;
        [SerializeField, Min(8)] private int badgeSize = 28;
        [SerializeField, Min(8)] private int promptSize = 26;

        [Header("Timing (seconds)")]
        [SerializeField, Min(0f)] private float fadeInSeconds = 0.3f;
        [SerializeField, Min(0f)] private float titleDelay = 0.15f;
        [SerializeField, Min(0.01f)] private float titleSeconds = 0.4f;
        [SerializeField, Min(0f)] private float rowsDelay = 0.6f;
        [SerializeField, Min(0f)] private float rowStagger = 0.09f;
        [SerializeField, Min(0.01f)] private float rowSlideSeconds = 0.25f;
        [SerializeField, Min(0.01f)] private float countSeconds = 0.5f;
        [Tooltip("Rows slide in from this far to the left (pixels).")]
        [SerializeField] private float rowSlideDistance = 70f;
        [SerializeField, Min(0f)] private float gradeDelay = 0.35f;
        [SerializeField, Min(0.01f)] private float gradeStampSeconds = 0.32f;
        [Tooltip("The grade starts this many times its size and slams down.")]
        [SerializeField, Min(1f)] private float gradeStartScale = 3f;
        [SerializeField] private AnimationCurve gradeStampCurve = new(
            new Keyframe(0f, 0f), new Keyframe(0.7f, 1.08f), new Keyframe(1f, 1f));
        [SerializeField, Min(0f)] private float stampShakePixels = 14f;
        [SerializeField, Min(0f)] private float stampShakeSeconds = 0.3f;
        [SerializeField, Min(0f)] private float gradePulse = 0.035f;
        [SerializeField, Min(0.05f)] private float gradePulsePeriod = 1.4f;
        [SerializeField, Min(0f)] private float badgeStagger = 0.14f;
        [SerializeField, Min(0.01f)] private float badgeSeconds = 0.28f;
        [SerializeField, Min(0f)] private float promptDelay = 0.4f;
        [SerializeField, Min(0.05f)] private float promptBlinkPeriod = 1.1f;
        [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.3f;

        [Header("Input")]
        [Tooltip("Ignore input for this long after the screen opens (so battle keys don't skip it).")]
        [SerializeField, Min(0f)] private float inputDelay = 0.5f;
        [Tooltip("The first press skips the animation, the next one closes the screen.")]
        [SerializeField] private bool pressToSkip = true;
        [Tooltip("Close automatically after this many seconds once finished (0 = wait for a key).")]
        [SerializeField, Min(0f)] private float autoCloseSeconds;

        [Header("Sounds (optional)")]
        [SerializeField] private AudioClip victoryJingle;
        [SerializeField] private AudioClip defeatJingle;
        [SerializeField] private AudioClip rowTick;
        [SerializeField] private AudioClip gradeSlam;
        [SerializeField] private AudioClip badgePop;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        public CombatResultScreen ScreenPrefab => screenPrefab;
        public string VictoryTitle => victoryTitle;
        public string DefeatTitle => defeatTitle;
        public string SubtitleFormat => subtitleFormat;
        public string GradeCaption => gradeCaption;
        public string ContinuePrompt => continuePrompt;
        public string FullComboBadge => fullComboBadge;
        public string AllPerfectBadge => allPerfectBadge;
        public string NoDamageBadge => noDamageBadge;
        public IReadOnlyList<ResultRowDefinition> MainRows => mainRows;
        public IReadOnlyList<ResultRowDefinition> DetailRows => detailRows;
        public Color DimColor => dimColor;
        public Color PanelColor => panelColor;
        public Color PanelFrameColor => panelFrameColor;
        public Color VictoryColor => victoryColor;
        public Color DefeatColor => defeatColor;
        public Color LabelColor => labelColor;
        public Color ValueColor => valueColor;
        public Color BadgeColor => badgeColor;
        public Color PromptColor => promptColor;
        public Color OutlineColor => outlineColor;
        public TMP_FontAsset FontAsset => CombatText.ResolveFont(fontAsset, legacyFont);
        public Font LegacyFont => legacyFont;
        public bool RankParagraphMapping => rankParagraphMapping;

        /// <summary>The rank letter's font (null = none set: the letter keeps the result font and no rank material).</summary>
        public TMP_FontAsset RankFont
        {
            get
            {
#if UNITY_EDITOR
                if (rankFont == null) rankFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultRankFontPath);
#endif
                return rankFont;
            }
        }

        /// <summary>The material for a grade label, or null to keep the font's own material.</summary>
        public Material GetRankMaterial(string grade)
        {
            if (rankMaterials == null || string.IsNullOrEmpty(grade)) return null;
            foreach (RankTextLook look in rankMaterials)
            {
                if (look == null || !string.Equals(look.grade, grade, StringComparison.OrdinalIgnoreCase)) continue;
#if UNITY_EDITOR
                if (look.material == null && DefaultRankMaterials.TryGetValue(look.grade.ToUpperInvariant(), out string path))
                    look.material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
#endif
                return look.material;
            }
            return null;
        }

        public const string DefaultRankFontPath = "Assets/Shader/Combat/RankText/Combat Rank SDF.asset";
        public static readonly IReadOnlyDictionary<string, string> DefaultRankMaterials = new Dictionary<string, string>
        {
            { "SS", "Assets/Shader/Combat/RankText/Materials/Combat Rank SDF - SS Iridescent.mat" },
            { "S", "Assets/Shader/Combat/RankText/Materials/Combat Rank SDF - S Gold.mat" },
            { "A", "Assets/Shader/Combat/RankText/Materials/Combat Rank SDF - A Emerald.mat" },
            { "B", "Assets/Shader/Combat/RankText/Materials/Combat Rank SDF - B Sapphire.mat" },
            { "C", "Assets/Shader/Combat/RankText/Materials/Combat Rank SDF - C Violet.mat" },
            { "D", "Assets/Shader/Combat/RankText/Materials/Combat Rank SDF - D Crimson.mat" }
        };
        public int TitleSize => titleSize;
        public int SubtitleSize => subtitleSize;
        public int RowSize => rowSize;
        public int DetailSize => detailSize;
        public int GradeSize => gradeSize;
        public int BadgeSize => badgeSize;
        public int PromptSize => promptSize;
        public float FadeInSeconds => fadeInSeconds;
        public float TitleDelay => titleDelay;
        public float TitleSeconds => titleSeconds;
        public float RowsDelay => rowsDelay;
        public float RowStagger => rowStagger;
        public float RowSlideSeconds => rowSlideSeconds;
        public float CountSeconds => countSeconds;
        public float RowSlideDistance => rowSlideDistance;
        public float GradeDelay => gradeDelay;
        public float GradeStampSeconds => gradeStampSeconds;
        public float GradeStartScale => gradeStartScale;
        public float StampShakePixels => stampShakePixels;
        public float StampShakeSeconds => stampShakeSeconds;
        public float GradePulse => gradePulse;
        public float GradePulsePeriod => gradePulsePeriod;
        public float BadgeStagger => badgeStagger;
        public float BadgeSeconds => badgeSeconds;
        public float PromptDelay => promptDelay;
        public float PromptBlinkPeriod => promptBlinkPeriod;
        public float FadeOutSeconds => fadeOutSeconds;
        public float InputDelay => inputDelay;
        public bool PressToSkip => pressToSkip;
        public float AutoCloseSeconds => autoCloseSeconds;
        public AudioClip VictoryJingle => victoryJingle;
        public AudioClip DefeatJingle => defeatJingle;
        public AudioClip RowTick => rowTick;
        public AudioClip GradeSlam => gradeSlam;
        public AudioClip BadgePop => badgePop;
        public float Volume => volume;

        public float EvaluateStamp(float t) =>
            gradeStampCurve == null || gradeStampCurve.length == 0 ? t : gradeStampCurve.Evaluate(Mathf.Clamp01(t));

        private static CombatResultStyle fallback;

        public static CombatResultStyle LoadOrDefault()
        {
            CombatResultStyle style = Resources.Load<CombatResultStyle>(ResourcePath);
            if (style != null) return style;
            if (fallback == null)
            {
                fallback = CreateInstance<CombatResultStyle>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
