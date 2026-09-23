using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/VFX Theme", fileName = "CombatVFXTheme")]
    public sealed class CombatVFXTheme : ScriptableObject
    {
        [SerializeField, Min(0.05f)] private float selectionHoldDuration = 0.75f;
        [SerializeField, Min(0f)] private float terminalStateDelay = 1f;
        [SerializeField, Min(0f)] private float hitFlashDuration = 0.12f;
        [SerializeField, Min(0f)] private float hitShakeStrength = 0.12f;

        [Header("Ability cast defaults (used when an ability's VFX profile leaves a slot empty)")]
        [SerializeField] private GameObject defaultChargePrefab;
        [SerializeField] private GameObject defaultChargeReleasePrefab;
        [SerializeField] private GameObject defaultWispPrefab;
        [SerializeField] private GameObject defaultPopPrefab;
        [SerializeField] private GameObject defaultNoteSparkPrefab;

        [Header("Centre stage (where the wisp pops and the player's pattern comes from)")]
        [Tooltip("0 = at the player, 1 = at the enemy.")]
        [SerializeField, Range(0f, 1f)] private float centerStageBias = 0.5f;
        [Tooltip("Height above the note lanes' plane, in world units.")]
        [SerializeField] private float centerStageHeight = 0f;

        [Header("Ability selection: icons above the player")]
        [Tooltip("On: the ability icons float in a row above the player's head (world-space canvas). Off: above the lane buttons.")]
        [SerializeField] private bool abilityIconsAbovePlayer = true;
        [Tooltip("Icon size as a fraction of the player sprite's height.")]
        [SerializeField, Range(0.1f, 1.5f)] private float iconSize = 0.4f;
        [Tooltip("Distance between icon centres, in icon widths.")]
        [SerializeField, Range(1f, 3f)] private float iconSpacing = 1.35f;
        [Tooltip("Gap between the top of the player's head and the icons, as a fraction of the sprite's height.")]
        [SerializeField, Range(0f, 1.5f)] private float iconsHeightAboveHead = 0.12f;
        [Tooltip("Show each lane's key (A, S, D...) under its icon.")]
        [SerializeField] private bool showKeysUnderIcons = true;
        [Tooltip("Bend of the row: the outermost icons sit this many icon heights lower (+, an arch) or higher (-, a smile) " +
                 "than the middle one. 0 = straight.")]
        [SerializeField, Range(-1.5f, 1.5f)] private float rowCurvature = 0.25f;
        [Tooltip("Circular frame, backdrop and hold-progress ring of the icons.")]
        [SerializeField] private AbilityIconFrameStyle iconFrame = new();

        [Header("Ability selection: holding an ability")]
        [Tooltip("The held icon is drawn down into the player while the hold fills, and swallowed when it is picked.")]
        [SerializeField] private bool pullIntoCharacter = true;
        [Tooltip("How far toward the player the icon gets during the hold (0-1 of the way); picking it finishes the pull.")]
        [SerializeField, Range(0f, 1f)] private float pullDistance = 0.55f;
        [Tooltip("1 = even pull; higher = resists at first, then gives way near the end of the hold.")]
        [SerializeField, Range(0.5f, 4f)] private float pullCurve = 2f;
        [Tooltip("Icon scale once fully pulled into the player.")]
        [SerializeField, Range(0.05f, 1f)] private float pullScale = 0.35f;
        [Tooltip("Seconds for the picked icon to be swallowed by the player.")]
        [SerializeField, Min(0.01f)] private float absorbSeconds = 0.15f;
        [Tooltip("Opacity of the other icons while one is held.")]
        [SerializeField, Range(0f, 1f)] private float otherIconsAlphaWhileHolding = 0.45f;
        [Tooltip("Show the held ability's name above the row.")]
        [SerializeField] private bool showAbilityName = true;
        [SerializeField, Range(0.3f, 3f)] private float abilityNameSize = 1f;
        [Tooltip("How long the name stays after the ability is picked.")]
        [SerializeField, Min(0f)] private float abilityNameHoldSeconds = 0.6f;

        [Header("Ability selection: camera (Cinemachine)")]
        [Tooltip("Zoom in on the player while choosing an ability, then blend back to the combat view.")]
        [SerializeField] private bool zoomOnPlayerTurn = true;
        [Tooltip("Zoomed view size relative to the combat view (0.5 = twice as close).")]
        [SerializeField, Range(0.2f, 1f)] private float zoomScale = 0.55f;
        [Tooltip("Where the player's body sits on screen while zoomed (0-1; 0.5, 0.5 = centre). Lower Y leaves room for the icons.")]
        [SerializeField] private Vector2 zoomScreenPosition = new(0.5f, 0.38f);
        [SerializeField, Min(0f)] private float zoomInSeconds = 0.45f;
        [Tooltip("Blend back to the combat view after an ability is picked. Keep it shorter than the cast (wisp travel).")]
        [SerializeField, Min(0f)] private float zoomOutSeconds = 0.35f;

        [Header("Ability selection: charge shake (while holding)")]
        [Tooltip("Camera shake (world units) when the hold starts.")]
        [SerializeField, Min(0f)] private float chargeShakeStart = 0.004f;
        [Tooltip("Camera shake (world units) when the hold is complete.")]
        [SerializeField, Min(0f)] private float chargeShakeEnd = 0.045f;
        [Tooltip("1 = grows evenly; higher = stays subtle, then ramps up hard near the end.")]
        [SerializeField, Range(0.5f, 4f)] private float chargeShakeCurve = 2f;
        [Tooltip("Shake speed. Higher = a faster rumble.")]
        [SerializeField, Min(1f)] private float chargeShakeFrequency = 28f;
        [Tooltip("One strong kick when the ability is picked.")]
        [SerializeField, Min(0f)] private float selectShake = 0.08f;
        [SerializeField, Min(0f)] private float selectShakeSeconds = 0.25f;

        public float SelectionHoldDuration => selectionHoldDuration;
        public bool AbilityIconsAbovePlayer => abilityIconsAbovePlayer;
        public float IconSize => iconSize;
        public float IconSpacing => iconSpacing;
        public float IconsHeightAboveHead => iconsHeightAboveHead;
        public bool ShowKeysUnderIcons => showKeysUnderIcons;
        public float RowCurvature => rowCurvature;
        public AbilityIconFrameStyle IconFrame => iconFrame;
        public bool PullIntoCharacter => pullIntoCharacter;
        public float PullDistance => pullDistance;
        public float PullCurve => pullCurve;
        public float PullScale => pullScale;
        public float AbsorbSeconds => absorbSeconds;
        public float OtherIconsAlphaWhileHolding => otherIconsAlphaWhileHolding;
        public bool ShowAbilityName => showAbilityName;
        public float AbilityNameSize => abilityNameSize;
        public float AbilityNameHoldSeconds => abilityNameHoldSeconds;
        public bool ZoomOnPlayerTurn => zoomOnPlayerTurn;
        public float ZoomScale => zoomScale;
        public Vector2 ZoomScreenPosition => zoomScreenPosition;
        public float ZoomInSeconds => zoomInSeconds;
        public float ZoomOutSeconds => zoomOutSeconds;
        public float ChargeShakeStart => chargeShakeStart;
        public float ChargeShakeEnd => chargeShakeEnd;
        public float ChargeShakeCurve => chargeShakeCurve;
        public float ChargeShakeFrequency => chargeShakeFrequency;
        public float SelectShake => selectShake;
        public float SelectShakeSeconds => selectShakeSeconds;
        public float TerminalStateDelay => terminalStateDelay;
        public float HitFlashDuration => hitFlashDuration;
        public float HitShakeStrength => hitShakeStrength;
        public GameObject DefaultChargePrefab => defaultChargePrefab;
        public GameObject DefaultChargeReleasePrefab => defaultChargeReleasePrefab;
        public GameObject DefaultWispPrefab => defaultWispPrefab;
        public GameObject DefaultPopPrefab => defaultPopPrefab;
        public GameObject DefaultNoteSparkPrefab => defaultNoteSparkPrefab;
        public float CenterStageBias => centerStageBias;
        public float CenterStageHeight => centerStageHeight;
    }
}
