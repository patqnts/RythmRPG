using System;
using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>How one icon tile looks: an optional frame sprite (9-sliced when it has borders), a background and the icon.</summary>
    [Serializable]
    public sealed class IconTileLook
    {
        [Tooltip("Tile size in pixels (1920x1080 canvas).")]
        public Vector2 size = new(64f, 64f);
        [Tooltip("Frame sprite. 9-sliced when the sprite has borders. Empty = a plain frame drawn in Frame Color.")]
        public Sprite frameSprite;
        public Color frameColor = new(0.55f, 0.53f, 0.6f, 1f);
        [Tooltip("Frame width in pixels. With a frame sprite this is how far the background / icon sit inside it.")]
        [Min(0f)] public float frameThickness = 4f;
        [Tooltip("Draw the frame above the icon (for ornate frames with a transparent centre). Off = frame behind.")]
        public bool frameOnTop;
        [Tooltip("Background sprite behind the icon (empty = solid colour).")]
        public Sprite backgroundSprite;
        public Color backgroundColor = new(0.07f, 0.06f, 0.12f, 0.95f);
        [Tooltip("Extra gap between the frame and the icon, in pixels.")]
        [Min(0f)] public float iconInset = 4f;
        [Tooltip("Keep the icon sprite's aspect ratio.")]
        public bool preserveAspect = true;
        [Tooltip("Font size of the fallback letter (no icon sprite).")]
        [Min(4)] public int glyphSize = 34;
    }

    /// <summary>
    /// Look of the build HUD: the passive icon column on the left, the in-effect icons above the player's health bar /
    /// under the enemy's, and the loadout panel (Tab / Select). Loaded from Resources/Combat/UI/BuildHudStyle; built-in
    /// defaults are used when the asset is missing. Create one with Create > Rythm RPG > Combat > Build > Build HUD Style.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Build HUD Style", fileName = "BuildHudStyle")]
    public sealed class BuildHudStyle : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/BuildHudStyle";

        [Header("Passive column (left side, during battle)")]
        [SerializeField] private bool showPassiveColumn = true;
        [Tooltip("Screen point the column hangs from (0,0 bottom-left .. 1,1 top-right). Also its pivot.")]
        [SerializeField] private Vector2 passiveAnchor = new(0f, 0.5f);
        [SerializeField] private Vector2 passivePosition = new(28f, 70f);
        [Tooltip("A longer column is scaled down to fit this height.")]
        [SerializeField, Min(60f)] private float passiveMaxHeight = 760f;
        [Tooltip("Direction the column slides in from when combat starts.")]
        [SerializeField] private Vector2 passiveIntroOffset = new(-120f, 0f);
        [SerializeField, Min(0f)] private float passiveSpacing = 10f;
        [SerializeField] private IconTileLook passiveTile = new() { size = new Vector2(68f, 68f), frameThickness = 4f };
        [Tooltip("Show the level (I, II, III) in the tile's corner.")]
        [SerializeField] private bool showPassiveLevel = true;
        [SerializeField, Min(4)] private int passiveLevelSize = 20;
        [SerializeField] private Color passiveLevelColor = new(1f, 0.85f, 0.3f);
        [Tooltip("Owned passives with no compatible ability equipped are drawn at this opacity.")]
        [SerializeField, Range(0f, 1f)] private float inactiveAlpha = 0.35f;
        [Tooltip("Show the passive's name next to its tile.")]
        [SerializeField] private bool showPassiveNames;
        [SerializeField, Min(4)] private int passiveNameSize = 20;
        [Tooltip("Frame colour when a passive triggers (fades back).")]
        [SerializeField] private Color triggerFlashColor = new(1f, 0.95f, 0.55f, 1f);
        [SerializeField, Min(0f)] private float triggerFlashSeconds = 0.45f;
        [SerializeField, Min(1f)] private float triggerPunchScale = 1.18f;

        [Header("Category colours (frame tint + fallback letter tile)")]
        [SerializeField] private bool tintFrameByCategory = true;
        [SerializeField] private Color damageColor = new(1f, 0.5f, 0.4f);
        [SerializeField] private Color meleeColor = new(1f, 0.68f, 0.35f);
        [SerializeField] private Color magicColor = new(0.55f, 0.65f, 1f);
        [SerializeField] private Color elementalColor = new(1f, 0.45f, 0.75f);
        [SerializeField] private Color supportColor = new(0.5f, 1f, 0.6f);
        [SerializeField] private Color rhythmColor = new(1f, 0.9f, 0.35f);
        [SerializeField] private Color sequenceColor = new(0.45f, 0.95f, 1f);
        [SerializeField] private Color conversionColor = new(0.8f, 0.6f, 1f);
        [SerializeField] private Color survivabilityColor = new(0.7f, 0.85f, 0.95f);

        [Header("In-effect icons (buffs above the player's HP bar, statuses under the enemy's)")]
        [SerializeField] private bool showEffectIcons = true;
        [Tooltip("Offset of the player's row from the health bar's top-left corner (clears the bar's title).")]
        [SerializeField] private Vector2 playerEffectsOffset = new(0f, 38f);
        [SerializeField] private bool showEnemyEffects = true;
        [Tooltip("Offset of the enemy's row from its health bar's bottom-left corner.")]
        [SerializeField] private Vector2 enemyEffectsOffset = new(0f, -10f);
        [SerializeField, Min(0f)] private float effectSpacing = 6f;
        [SerializeField] private IconTileLook effectTile = new()
        {
            size = new Vector2(46f, 46f), frameThickness = 3f, iconInset = 3f, glyphSize = 24,
            frameColor = new Color(0.55f, 0.85f, 1f, 1f)
        };
        [SerializeField] private Color buffFrameColor = new(0.55f, 0.85f, 1f, 1f);
        [SerializeField] private Color debuffFrameColor = new(1f, 0.5f, 0.35f, 1f);
        [SerializeField] private Color counterFrameColor = new(1f, 0.85f, 0.3f, 1f);
        [Tooltip("Show counter charges as an icon (count = charges).")]
        [SerializeField] private bool showCounterCharges = true;
        [SerializeField] private Sprite counterIcon;
        [SerializeField, Min(4)] private int effectCountSize = 22;
        [SerializeField] private Color effectCountColor = Color.white;
        [Tooltip("Turn count colour on its last turn.")]
        [SerializeField] private Color expiringCountColor = new(1f, 0.45f, 0.4f);
        [SerializeField, Min(0f)] private float effectPopSeconds = 0.2f;

        [Header("Loadout panel (Tab / Select)")]
        [Tooltip("Create the loadout panel automatically in every scene (off = only a panel placed in a scene works).")]
        [SerializeField] private bool enableLoadoutPanel = true;
        [SerializeField] private string panelTitle = "RESONANCE ARCHIVE";
        [SerializeField] private string battleSubtitle = "";
        [SerializeField] private string editSubtitle = "";
        [SerializeField] private string noBuildText = "No abilities have been attuned yet.";
        [Tooltip("{0} = confirm key, {1} = close key.")]
        [SerializeField] private string editPrompt = "[{0}] Swap    [X] Unequip    [{1}] Close";
        [SerializeField] private string viewPrompt = "[{1}] CLOSE";
        [SerializeField] private Color dimColor = new(0.015f, 0.025f, 0.055f, 0.48f);
        [SerializeField] private Color panelColor = new(0.022f, 0.046f, 0.09f, 0.86f);
        [SerializeField] private Color panelFrameColor = new(0.48f, 0.94f, 1f, 0.7f);
        [SerializeField] private Color titleColor = new(0.94f, 0.98f, 1f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color mutedColor = new(0.65f, 0.76f, 0.85f);
        [SerializeField] private Color highlightColor = new(0.48f, 0.94f, 1f);
        [SerializeField] private Color pickedColor = new(0.79f, 0.65f, 1f);
        [SerializeField] private Color warningColor = new(1f, 0.45f, 0.45f);
        [SerializeField] private Vector2 panelSize = new(1740f, 880f);
        [SerializeField] private IconTileLook panelTile = new() { size = new Vector2(58f, 58f), frameThickness = 2f,
            frameColor = new Color(1f, 1f, 1f, .6f), backgroundColor = new Color(.035f, .10f, .11f, .38f) };
        [SerializeField, Min(8)] private int panelTitleSize = 64;
        [SerializeField, Min(8)] private int headerSize = 36;
        [SerializeField, Min(8)] private int rowTitleSize = 36;
        [SerializeField, Min(8)] private int rowDetailSize = 30;
        [SerializeField, Min(8)] private int promptSize = 30;
        [SerializeField, Min(0f)] private float fadeSeconds = 0.55f;
        [Tooltip("Keys besides the Loadout action (Tab / Select). Gamepad: South = pick / place, West = to reserve, East = close.")]
        [SerializeField] private KeyCode[] confirmKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space };
        [SerializeField] private KeyCode[] closeKeys = { KeyCode.Backspace };
        [SerializeField] private KeyCode[] reserveKeys = { KeyCode.X, KeyCode.Delete };

        [Header("Font (empty = the battle result style's font)")]
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField] private bool useResultOutline = true;
        [SerializeField] private Color outlineColor = new(0f, 0f, 0f, 0f);

        [Header("Sounds (optional)")]
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip moveSound;
        [SerializeField] private AudioClip confirmSound;
        [SerializeField] private AudioClip deniedSound;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        private static BuildHudStyle fallback;

        public bool ShowPassiveColumn => showPassiveColumn;
        public Vector2 PassiveAnchor => passiveAnchor;
        public Vector2 PassivePosition => passivePosition;
        public float PassiveMaxHeight => passiveMaxHeight;
        public Vector2 PassiveIntroOffset => passiveIntroOffset;
        public float PassiveSpacing => passiveSpacing;
        public IconTileLook PassiveTile => passiveTile ??= new IconTileLook();
        public bool ShowPassiveLevel => showPassiveLevel;
        public int PassiveLevelSize => passiveLevelSize;
        public Color PassiveLevelColor => passiveLevelColor;
        public float InactiveAlpha => inactiveAlpha;
        public bool ShowPassiveNames => showPassiveNames;
        public int PassiveNameSize => passiveNameSize;
        public Color TriggerFlashColor => triggerFlashColor;
        public float TriggerFlashSeconds => triggerFlashSeconds;
        public float TriggerPunchScale => triggerPunchScale;
        public bool TintFrameByCategory => tintFrameByCategory;

        public bool ShowEffectIcons => showEffectIcons;
        public Vector2 PlayerEffectsOffset => playerEffectsOffset;
        public bool ShowEnemyEffects => showEnemyEffects;
        public Vector2 EnemyEffectsOffset => enemyEffectsOffset;
        public float EffectSpacing => effectSpacing;
        public IconTileLook EffectTile => effectTile ??= new IconTileLook();
        public Color BuffFrameColor => buffFrameColor;
        public Color DebuffFrameColor => debuffFrameColor;
        public Color CounterFrameColor => counterFrameColor;
        public bool ShowCounterCharges => showCounterCharges;
        public Sprite CounterIcon => counterIcon;
        public int EffectCountSize => effectCountSize;
        public Color EffectCountColor => effectCountColor;
        public Color ExpiringCountColor => expiringCountColor;
        public float EffectPopSeconds => effectPopSeconds;

        public bool EnableLoadoutPanel => enableLoadoutPanel;
        public string PanelTitle => panelTitle;
        public string BattleSubtitle => battleSubtitle;
        public string EditSubtitle => editSubtitle;
        public string NoBuildText => noBuildText;
        public Color DimColor => dimColor;
        public Color PanelColor => panelColor;
        public Color PanelFrameColor => panelFrameColor;
        public Color TitleColor => titleColor;
        public Color TextColor => textColor;
        public Color MutedColor => mutedColor;
        public Color HighlightColor => highlightColor;
        public Color PickedColor => pickedColor;
        public Color WarningColor => warningColor;
        public Vector2 PanelSize => panelSize;
        public IconTileLook PanelTile => panelTile ??= new IconTileLook();
        public int PanelTitleSize => panelTitleSize;
        public int HeaderSize => headerSize;
        public int RowTitleSize => rowTitleSize;
        public int RowDetailSize => rowDetailSize;
        public int PromptSize => promptSize;
        public float FadeSeconds => fadeSeconds;
        public KeyCode[] ConfirmKeys => confirmKeys;
        public KeyCode[] CloseKeys => closeKeys;
        public KeyCode[] ReserveKeys => reserveKeys;

        public AudioClip OpenSound => openSound;
        public AudioClip MoveSound => moveSound;
        public AudioClip ConfirmSound => confirmSound;
        public AudioClip DeniedSound => deniedSound;
        public float Volume => volume;

        public TMP_FontAsset FontAsset => fontAsset != null ? fontAsset : CombatResultStyle.LoadOrDefault().FontAsset;
        public Color OutlineColor => useResultOutline ? CombatResultStyle.LoadOrDefault().OutlineColor : outlineColor;

        public string EditPrompt(string closeKey) => FormatPrompt(editPrompt, closeKey);
        public string ViewPrompt(string closeKey) => FormatPrompt(viewPrompt, closeKey);

        private string FormatPrompt(string format, string closeKey)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            string confirm = confirmKeys != null && confirmKeys.Length > 0 ? CombatResultStyle.KeyName(confirmKeys[0]) : "?";
            return string.Format(format, confirm, string.IsNullOrEmpty(closeKey) ? "TAB" : closeKey);
        }

        public Color CategoryColor(PassiveCategory category) => category switch
        {
            PassiveCategory.DamageEnhancement => damageColor,
            PassiveCategory.MeleeEnhancement => meleeColor,
            PassiveCategory.MagicEfficiency => magicColor,
            PassiveCategory.ElementalEnhancement => elementalColor,
            PassiveCategory.SupportEnhancement => supportColor,
            PassiveCategory.RhythmConditioned => rhythmColor,
            PassiveCategory.ActionSequence => sequenceColor,
            PassiveCategory.Conversion => conversionColor,
            _ => survivabilityColor
        };

        /// <summary>Frame colour of a passive's tile (category tint or the tile's own frame colour).</summary>
        public Color PassiveFrameColor(PassiveDefinition passive, IconTileLook look) =>
            tintFrameByCategory && passive != null ? CategoryColor(passive.Category) : look.frameColor;

        public static string Roman(int level) => level switch
        {
            1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X",
            _ => level.ToString()
        };

        public static BuildHudStyle LoadOrDefault()
        {
            BuildHudStyle style = Resources.Load<BuildHudStyle>(ResourcePath);
            if (style != null) return style;
            if (fallback == null)
            {
                fallback = CreateInstance<BuildHudStyle>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
