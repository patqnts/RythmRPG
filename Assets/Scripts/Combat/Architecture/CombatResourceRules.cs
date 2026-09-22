using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Balance knobs for player health and mana, weighted by judgement. Loaded from
    /// Resources/Combat/Balance/CombatResourceRules (defaults are used when the asset is missing).
    /// See the "Combat balance" project doc for the reasoning behind the numbers.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Combat Resource Rules", fileName = "CombatResourceRules")]
    public sealed class CombatResourceRules : ScriptableObject
    {
        public const string ResourcePath = "Combat/Balance/CombatResourceRules";

        [Header("Damage taken on the enemy turn (x the note's Damage)")]
        [SerializeField, Min(0f)] private float missDamage = 1f;
        [SerializeField, Min(0f)] private float badDamage = 0.5f;
        [SerializeField, Min(0f)] private float goodDamage;
        [SerializeField, Min(0f)] private float perfectDamage;

        [Header("Mana gained per note hit")]
        [SerializeField, Min(0f)] private float perfectMana = 1f;
        [SerializeField, Min(0f)] private float goodMana = 0.5f;
        [SerializeField, Min(0f)] private float badMana;
        [Tooltip("Multiplier for mana gained while playing your own ability charts (the enemy turn is the main source).")]
        [SerializeField, Range(0f, 2f)] private float abilityChartManaScale = 0.5f;
        [Tooltip("Mana restored at the start of each player turn, regardless of play.")]
        [SerializeField, Min(0)] private int manaPerPlayerTurn = 5;

        [Header("Defensive abilities")]
        [Tooltip("Below this ability performance (0-1 average judgement weight) a defensive effect fails.")]
        [SerializeField, Range(0f, 1f)] private float defensiveMinimumPerformance = 0.25f;

        private static CombatResourceRules fallback;

        public int ManaPerPlayerTurn => manaPerPlayerTurn;
        public float DefensiveMinimumPerformance => defensiveMinimumPerformance;

        public static CombatResourceRules Load()
        {
            CombatResourceRules rules = Resources.Load<CombatResourceRules>(ResourcePath);
            if (rules != null) return rules;
            if (fallback == null) fallback = CreateInstance<CombatResourceRules>();
            return fallback;
        }

        public float DamageMultiplier(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => perfectDamage,
            HitJudgement.Good => goodDamage,
            HitJudgement.Bad => badDamage,
            _ => missDamage
        };

        public int DefenseDamage(int noteDamage, HitJudgement judgement) =>
            Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, noteDamage) * DamageMultiplier(judgement)));

        public float ManaGain(HitJudgement judgement, PatternRunMode mode)
        {
            float gain = judgement switch
            {
                HitJudgement.Perfect => perfectMana,
                HitJudgement.Good => goodMana,
                HitJudgement.Bad => badMana,
                _ => 0f
            };
            return mode == PatternRunMode.PlayerAbility ? gain * abilityChartManaScale : gain;
        }
    }
}
