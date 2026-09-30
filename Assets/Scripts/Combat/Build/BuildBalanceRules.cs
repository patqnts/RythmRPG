using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// System-level bounds for the build system: modifier caps, shield capacity, counter budget, reward offer size.
    /// Content (ability numbers, passive values) lives on the definitions; these are the guard rails around them.
    /// Loaded from Resources/Combat/Balance/BuildBalanceRules (defaults are used when the asset is missing).
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Build Balance Rules", fileName = "BuildBalanceRules")]
    public sealed class BuildBalanceRules : ScriptableObject
    {
        public const string ResourcePath = "Combat/Balance/BuildBalanceRules";

        [Header("Modifier groups (percent bonuses add inside a group, groups multiply)")]
        [Tooltip("Highest total bonus one group can give (2 = +200%).")]
        [SerializeField, Min(0f)] private float groupBonusCap = 2f;
        [Tooltip("Lowest total one group can give (-0.9 = -90%).")]
        [SerializeField, Range(-1f, 0f)] private float groupPenaltyFloor = -0.9f;
        [Tooltip("Highest final multiplier after all groups (before enemy affinity).")]
        [SerializeField, Min(1f)] private float totalMultiplierCap = 4f;

        [Header("Enemy affinity")]
        [Tooltip("Affinity multipliers are clamped to this range so no build is locked out.")]
        [SerializeField, Min(0f)] private float minAffinity = 0.25f;
        [SerializeField, Min(1f)] private float maxAffinity = 2f;

        [Header("Protection")]
        [Tooltip("Shield capacity cap as a fraction of max health (all shield sources share it).")]
        [SerializeField, Range(0.05f, 1f)] private float shieldCapFraction = 0.3f;

        [Header("Counterpower")]
        [Tooltip("Most counter charges that can be stored.")]
        [SerializeField, Min(1)] private int maxCounterCharges = 5;
        [Tooltip("Stored charges fade after this many player turns without gaining a new one.")]
        [SerializeField, Min(1)] private int counterExpiryPlayerTurns = 2;

        [Header("Secondary effects")]
        [Tooltip("Most secondary (passive-generated) transactions one root cause (cast / enemy note) may create.")]
        [SerializeField, Min(1)] private int maxSecondaryPerRoot = 4;

        [Header("Rewards")]
        [SerializeField, Range(1, 6)] private int optionsPerOffer = 3;

        [Header("Elements (marks, reactions, zaps, walls, stagger)")]
        [SerializeField] private ElementalRules elements = new();

        private static BuildBalanceRules fallback;

        public float GroupBonusCap => groupBonusCap;
        public float GroupPenaltyFloor => groupPenaltyFloor;
        public float TotalMultiplierCap => totalMultiplierCap;
        public float MinAffinity => minAffinity;
        public float MaxAffinity => maxAffinity;
        public float ShieldCapFraction => shieldCapFraction;
        public int MaxCounterCharges => maxCounterCharges;
        public int CounterExpiryPlayerTurns => counterExpiryPlayerTurns;
        public int MaxSecondaryPerRoot => maxSecondaryPerRoot;
        public int OptionsPerOffer => optionsPerOffer;
        public ElementalRules Elements => elements ??= new ElementalRules();

        public static BuildBalanceRules Load()
        {
            BuildBalanceRules rules = Resources.Load<BuildBalanceRules>(ResourcePath);
            if (rules != null) return rules;
            if (fallback == null) fallback = CreateInstance<BuildBalanceRules>();
            return fallback;
        }
    }
}
