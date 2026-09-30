using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Tuning for elemental marks, reactions and board effects (zaps, walls, stagger). Lives inside
    /// <see cref="BuildBalanceRules"/> so one asset holds every guard rail.
    /// </summary>
    [Serializable]
    public sealed class ElementalRules
    {
        [Header("Marks")]
        [Tooltip("Most Burn stacks on the enemy (passives can raise it).")]
        [Min(1)] public int burnMaxStacks = 5;
        [Tooltip("Enemy turns Burn lasts (refreshed when stacks are added).")]
        [Min(1)] public int burnTurns = 3;
        [Tooltip("Soaked: enemy notes deal this much less damage.")]
        [Range(0f, 0.9f)] public float soakedDamageReduction = 0.2f;
        [Min(1)] public int soakedTurns = 2;
        [Tooltip("Static stacks needed for a Lightning hit to discharge them (passives can raise it).")]
        [Min(1)] public int staticMaxStacks = 5;
        [Min(1)] public int staticTurns = 3;
        [Tooltip("Cracked: the enemy takes this much more melee damage.")]
        [Min(0f)] public float crackedMeleeBonus = 0.2f;
        [Min(1)] public int crackedTurns = 2;
        [Tooltip("Wind hits add this many stacks to Burn and Static.")]
        [Min(0)] public int windStacks = 1;
        [Tooltip("Wind hits extend every mark by this many turns (once per mark per cast).")]
        [Min(0)] public int windExtendTurns = 1;

        [Header("Reactions")]
        [Tooltip("Steam burst = remaining Burn tick damage x this.")]
        [Min(0f)] public float steamMultiplier = 2f;
        [Tooltip("Steam burst is at least this fraction of the hit that triggered it.")]
        [Min(0f)] public float steamMinimumOfHit = 0.3f;
        [Tooltip("Overload: Static discharges at this multiple.")]
        [Min(1f)] public float overloadMultiplier = 2f;
        [Tooltip("Wildfire: Burn stacks are multiplied by this (up to the stack cap).")]
        [Min(1f)] public float wildfireMultiplier = 2f;
        [Tooltip("Conduct: extra notes each zap chains to while the enemy is Soaked.")]
        [Min(0)] public int conductExtraZaps = 1;
        [Tooltip("Conduct: each Chain Spark arc fires a second arc at this fraction of its damage.")]
        [Range(0f, 1f)] public float conductArcScale = 0.5f;
        [Tooltip("Magnetize: shield gained = discharge damage x this.")]
        [Min(0f)] public float magnetizeShieldScale = 1f;

        [Header("Board")]
        [Tooltip("Zaps reach notes at most this many seconds before they hit the line.")]
        [Min(0.1f)] public float zapWindowSeconds = 1f;
        [Tooltip("Most notes zaps may clear in one enemy turn, from all sources (enemy profiles can lower it).")]
        [Min(1)] public int maxZapsPerEnemyTurn = 8;
        [Tooltip("A walled note breaks this many seconds before it would reach the hit line.")]
        [Min(0f)] public float wallLeadSeconds = 0.12f;

        [Header("Stagger")]
        [Tooltip("A staggered attack with a single step keeps its notes but they deal this fraction of damage.")]
        [Range(0f, 1f)] public float singleStepStaggerDamage = 0.5f;
        [Tooltip("An enemy staggered this enemy turn can't be staggered again before its next turn is over.")]
        public bool noBackToBackStagger = true;
    }
}
