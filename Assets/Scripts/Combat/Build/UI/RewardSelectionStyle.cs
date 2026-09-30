using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Look and feel of the reward selection screen (cards after a victory). Loaded from
    /// Resources/Combat/UI/RewardSelectionStyle; built-in defaults are used when the asset is missing. Font and outline
    /// fall back to the battle result style so both screens match.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Reward Selection Style", fileName = "RewardSelectionStyle")]
    public sealed class RewardSelectionStyle : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/RewardSelectionStyle";

        [Header("Custom screen (optional, replaces the generated template)")]
        [SerializeField] private RewardSelectionScreen screenPrefab;

        [Header("Text")]
        [SerializeField] private string title = "CHOOSE A REWARD";
        [Tooltip("{0} = build name. Empty = no subtitle.")]
        [SerializeField] private string subtitleFormat = "{0}";
        [SerializeField] private string replaceTitle = "REPLACE WHICH ABILITY?";
        [SerializeField] private string replaceSubtitle = "The replaced ability keeps its upgrades in reserve.";
        [SerializeField] private string reserveLabel = "KEEP IT IN RESERVE";
        [SerializeField] private string claimedStamp = "ACQUIRED";
        [SerializeField] private string newAbilityLabel = "NEW ABILITY";
        [SerializeField] private string upgradeLabel = "UPGRADE";
        [SerializeField] private string passiveLabel = "PASSIVE";
        [Tooltip("{0} = confirm key, {1} = skip key.")]
        [SerializeField] private string cardPrompt = "[ARROWS] CHOOSE      [{0}] TAKE      [{1}] SKIP";
        [SerializeField] private string cardPromptNoSkip = "[ARROWS] CHOOSE      [{0}] TAKE";
        [Tooltip("{0} = confirm key, {1} = back key.")]
        [SerializeField] private string replacePrompt = "[ARROWS] CHOOSE      [{0}] CONFIRM      [{1}] BACK";

        [Header("Colors")]
        [SerializeField] private Color dimColor = new(0.02f, 0.02f, 0.05f, 0.86f);
        [SerializeField] private Color cardColor = new(0.07f, 0.06f, 0.12f, 0.97f);
        [SerializeField] private Color frameColor = new(0.55f, 0.53f, 0.6f, 1f);
        [SerializeField] private Color titleColor = new(1f, 0.85f, 0.3f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color mutedColor = new(0.74f, 0.76f, 0.84f);
        [SerializeField] private Color disabledColor = new(0.45f, 0.45f, 0.5f, 0.8f);
        [SerializeField] private Color abilityColor = new(0.45f, 0.8f, 1f);
        [SerializeField] private Color upgradeColor = new(0.5f, 1f, 0.6f);
        [SerializeField] private Color passiveColor = new(1f, 0.72f, 0.35f);
        [SerializeField] private Color warningColor = new(1f, 0.45f, 0.45f);

        [Header("Font (empty = the battle result style's font)")]
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField] private bool useResultOutline = true;
        [SerializeField] private Color outlineColor = new(0f, 0f, 0f, 0f);

        [Header("Sizes (1920x1080 canvas)")]
        [SerializeField] private Vector2 cardSize = new(470f, 640f);
        [SerializeField, Min(0f)] private float cardSpacing = 56f;
        [SerializeField, Min(8)] private int titleSize = 72;
        [SerializeField, Min(8)] private int subtitleSize = 28;
        [SerializeField, Min(8)] private int kindSize = 24;
        [SerializeField, Min(8)] private int cardTitleSize = 40;
        [SerializeField, Min(8)] private int summarySize = 25;
        [SerializeField, Min(8)] private int detailSize = 21;
        [SerializeField, Min(8)] private int promptSize = 26;
        [SerializeField, Min(8)] private int stampSize = 56;

        [Header("Motion (seconds / pixels)")]
        [SerializeField, Min(0f)] private float fadeInSeconds = 0.25f;
        [SerializeField, Min(0f)] private float cardsDelay = 0.15f;
        [SerializeField, Min(0f)] private float cardStagger = 0.1f;
        [SerializeField, Min(0.01f)] private float cardInSeconds = 0.35f;
        [SerializeField] private float cardInDistance = 140f;
        [SerializeField] private float selectedLift = 26f;
        [SerializeField, Min(1f)] private float selectedScale = 1.06f;
        [SerializeField, Min(0.01f)] private float selectSmoothing = 0.08f;
        [SerializeField, Range(0f, 1f)] private float unselectedDim = 0.55f;
        [SerializeField, Min(0f)] private float claimHoldSeconds = 0.9f;
        [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.3f;

        [Header("Input")]
        [Tooltip("Ignore input for this long after opening (keeps a held rhythm key from picking a card).")]
        [SerializeField, Min(0f)] private float inputDelay = 0.6f;
        [SerializeField] private KeyCode[] confirmKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space };
        [SerializeField] private KeyCode[] previousKeys = { KeyCode.LeftArrow, KeyCode.UpArrow };
        [SerializeField] private KeyCode[] nextKeys = { KeyCode.RightArrow, KeyCode.DownArrow };
        [SerializeField] private KeyCode[] backKeys = { KeyCode.Backspace };
        [Tooltip("Allow leaving without a reward (the offer stays unclaimed; it can still be claimed from the F11 panel).")]
        [SerializeField] private bool allowSkip = true;
        [Tooltip("Number keys 1-4 pick a card / slot directly.")]
        [SerializeField] private bool numberKeys = true;

        [Header("Sounds (optional)")]
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip moveSound;
        [SerializeField] private AudioClip confirmSound;
        [SerializeField] private AudioClip claimSound;
        [SerializeField] private AudioClip deniedSound;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        private static RewardSelectionStyle fallback;

        public RewardSelectionScreen ScreenPrefab => screenPrefab;
        public string Title => title;
        public string SubtitleFormat => subtitleFormat;
        public string ReplaceTitle => replaceTitle;
        public string ReplaceSubtitle => replaceSubtitle;
        public string ReserveLabel => reserveLabel;
        public string ClaimedStamp => claimedStamp;
        public Color DimColor => dimColor;
        public Color CardColor => cardColor;
        public Color FrameColor => frameColor;
        public Color TitleColor => titleColor;
        public Color TextColor => textColor;
        public Color MutedColor => mutedColor;
        public Color DisabledColor => disabledColor;
        public Color WarningColor => warningColor;
        public Vector2 CardSize => cardSize;
        public float CardSpacing => cardSpacing;
        public int TitleSize => titleSize;
        public int SubtitleSize => subtitleSize;
        public int KindSize => kindSize;
        public int CardTitleSize => cardTitleSize;
        public int SummarySize => summarySize;
        public int DetailSize => detailSize;
        public int PromptSize => promptSize;
        public int StampSize => stampSize;
        public float FadeInSeconds => fadeInSeconds;
        public float CardsDelay => cardsDelay;
        public float CardStagger => cardStagger;
        public float CardInSeconds => cardInSeconds;
        public float CardInDistance => cardInDistance;
        public float SelectedLift => selectedLift;
        public float SelectedScale => selectedScale;
        public float SelectSmoothing => selectSmoothing;
        public float UnselectedDim => unselectedDim;
        public float ClaimHoldSeconds => claimHoldSeconds;
        public float FadeOutSeconds => fadeOutSeconds;
        public float InputDelay => inputDelay;
        public KeyCode[] ConfirmKeys => confirmKeys;
        public KeyCode[] PreviousKeys => previousKeys;
        public KeyCode[] NextKeys => nextKeys;
        public KeyCode[] BackKeys => backKeys;
        public bool AllowSkip => allowSkip;
        public bool NumberKeys => numberKeys;
        public AudioClip OpenSound => openSound;
        public AudioClip MoveSound => moveSound;
        public AudioClip ConfirmSound => confirmSound;
        public AudioClip ClaimSound => claimSound;
        public AudioClip DeniedSound => deniedSound;
        public float Volume => volume;

        public TMP_FontAsset FontAsset => fontAsset != null ? fontAsset : CombatResultStyle.LoadOrDefault().FontAsset;
        public Color OutlineColor => useResultOutline ? CombatResultStyle.LoadOrDefault().OutlineColor : outlineColor;

        public Color KindColor(RewardKind kind) => kind switch
        {
            RewardKind.NewAbility => abilityColor,
            RewardKind.AbilityUpgrade => upgradeColor,
            _ => passiveColor
        };

        public string KindLabel(RewardKind kind) => kind switch
        {
            RewardKind.NewAbility => newAbilityLabel,
            RewardKind.AbilityUpgrade => upgradeLabel,
            _ => passiveLabel
        };

        public string CardPrompt => allowSkip
            ? Format(cardPrompt, confirmKeys, backKeys)
            : Format(cardPromptNoSkip, confirmKeys, backKeys);

        public string ReplacePrompt => Format(replacePrompt, confirmKeys, backKeys);

        private static string Format(string format, KeyCode[] first, KeyCode[] second)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            string a = first != null && first.Length > 0 ? CombatResultStyle.KeyName(first[0]) : "?";
            string b = second != null && second.Length > 0 ? CombatResultStyle.KeyName(second[0]) : "?";
            return string.Format(format, a, b);
        }

        public static RewardSelectionStyle LoadOrDefault()
        {
            RewardSelectionStyle style = Resources.Load<RewardSelectionStyle>(ResourcePath);
            if (style != null) return style;
            if (fallback == null)
            {
                fallback = CreateInstance<RewardSelectionStyle>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
