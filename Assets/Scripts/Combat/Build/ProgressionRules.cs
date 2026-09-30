using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Run progression: growth rewards (max health / max mana), healing between battles, and how enemies scale with run
    /// depth (victories so far). Lives inside <see cref="BuildBalanceRules"/>.
    ///
    /// Design intent: enemies get harder mainly through denser patterns (author denser attack sequences and gate them
    /// with the sequence's Min Run Depth); per-note damage grows slowly. Players grow by choosing growth cards, sized so
    /// that max health keeps pace with per-note damage: a Miss keeps costing about the same share of your health.
    /// </summary>
    [Serializable]
    public sealed class ProgressionRules
    {
        [Header("Growth rewards (an extra card on every victory)")]
        [Tooltip("Every victory offer gets one growth card next to the usual options.")]
        public bool growthCardEveryOffer = true;
        [Tooltip("Vitality card: max health added.")]
        [Min(0)] public int healthGrowth = 100;
        [Tooltip("Focus card: max mana added.")]
        [Min(0)] public int manaGrowth = 15;
        [Tooltip("Balanced card: max health and max mana added.")]
        [Min(0)] public int balancedHealthGrowth = 60;
        [Min(0)] public int balancedManaGrowth = 8;
        [Tooltip("How often each growth card is offered (relative weights).")]
        [Min(0f)] public float healthCardWeight = 2f;
        [Min(0f)] public float manaCardWeight = 1f;
        [Min(0f)] public float balancedCardWeight = 1f;

        [Header("Between battles")]
        [Tooltip("Share of max health restored after each victory (1 = full heal).")]
        [Range(0f, 1f)] public float healAfterVictory = 1f;

        [Header("Enemy scaling by run depth (victories)")]
        [Tooltip("Enemy note damage +this per victory (0.05 = +5%). Keep it at or below the growth pace.")]
        [Min(0f)] public float noteDamagePerDepth = 0.05f;
        [Min(1f)] public float maxNoteDamageScale = 2f;
        [Tooltip("Enemy max health +this per victory (0.1 = +10%), so growing player damage stays meaningful.")]
        [Min(0f)] public float enemyHealthPerDepth = 0.1f;
        [Min(1f)] public float maxEnemyHealthScale = 3f;

        public float NoteDamageScale(int depth) => Mathf.Clamp(1f + Mathf.Max(0, depth) * noteDamagePerDepth, 1f, maxNoteDamageScale);
        public float EnemyHealthScale(int depth) => Mathf.Clamp(1f + Mathf.Max(0, depth) * enemyHealthPerDepth, 1f, maxEnemyHealthScale);
    }

    /// <summary>The growth cards (content ids of <see cref="RewardKind.Growth"/> options).</summary>
    public static class GrowthRewards
    {
        public const string Health = "growth-health";
        public const string Mana = "growth-mana";
        public const string Balanced = "growth-balanced";

        public static void Amounts(string id, ProgressionRules rules, out int health, out int mana)
        {
            health = 0;
            mana = 0;
            switch (id)
            {
                case Health: health = rules.healthGrowth; break;
                case Mana: mana = rules.manaGrowth; break;
                case Balanced:
                    health = rules.balancedHealthGrowth;
                    mana = rules.balancedManaGrowth;
                    break;
            }
        }

        public static string Title(string id) => id switch
        {
            Health => "Vitality",
            Mana => "Focus",
            Balanced => "Resolve",
            _ => "Growth"
        };

        public static string Summary(string id, ProgressionRules rules)
        {
            Amounts(id, rules, out int health, out int mana);
            if (health > 0 && mana > 0) return $"+{health} max HP and +{mana} max MP for the rest of the run.";
            return health > 0 ? $"+{health} max HP for the rest of the run." : $"+{mana} max MP for the rest of the run.";
        }
    }
}
