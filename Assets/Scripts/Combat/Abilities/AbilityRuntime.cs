using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>What was frozen when an ability was committed: the quote shown to the player and the mana actually paid.</summary>
    public sealed class AbilityCommit
    {
        public int CommitNumber;
        public AbilityQuote Quote;
        public int PaidCost;
        public int Cooldown;
    }

    public sealed class AbilityRuntimeInstance
    {
        private int commitCounter;

        public AbilityDefinition Definition { get; }
        /// <summary>The run-owned instance this slot runtime was built from (null for legacy / scene loadouts).</summary>
        public AbilityInstance BuildInstance { get; }
        /// <summary>Resolves the effective cost / cooldown / effects (build upgrades and passives). Null = the definition as authored.</summary>
        public Func<AbilityRuntimeInstance, AbilityQuote> QuoteProvider { get; set; }
        public int RemainingCooldown { get; private set; }
        public AbilityCommit LastCommit { get; private set; }

        public AbilityRuntimeInstance(AbilityDefinition definition) => Definition = definition;

        public AbilityRuntimeInstance(AbilityInstance instance, Func<AbilityRuntimeInstance, AbilityQuote> quoteProvider)
        {
            BuildInstance = instance;
            Definition = instance?.Definition;
            QuoteProvider = quoteProvider;
        }

        /// <summary>The effective ability right now: the same numbers are shown in previews and paid on commit.</summary>
        public AbilityQuote Quote() => QuoteProvider != null ? QuoteProvider(this) : AbilityQuote.FromDefinition(Definition);

        public int EffectiveManaCost => Quote().ManaCost;

        public bool CanUse(PlayerCombatant player) => Definition != null
            && player != null
            && !player.IsDefeated
            && RemainingCooldown == 0
            && player.CurrentMana >= EffectiveManaCost
            && Definition.RhythmPattern != null;

        /// <summary>Pays the effective cost once and starts the effective cooldown. The frozen values are kept in <see cref="LastCommit"/>.</summary>
        public bool Commit(PlayerCombatant player)
        {
            if (!CanUse(player)) return false;
            AbilityQuote quote = Quote();
            if (!player.SpendMana(quote.ManaCost)) return false;
            RemainingCooldown = quote.Cooldown;
            LastCommit = new AbilityCommit { CommitNumber = ++commitCounter, Quote = quote, PaidCost = quote.ManaCost, Cooldown = quote.Cooldown };
            return true;
        }

        public void TickPlayerTurn()
        {
            if (RemainingCooldown > 0) RemainingCooldown--;
        }

        public void Reset() => RemainingCooldown = 0;
    }

    public readonly struct AbilityExecutionContext
    {
        public readonly PlayerCombatant Player;
        public readonly EnemyCombatant Enemy;
        public readonly AbilityRuntimeInstance Ability;

        public AbilityExecutionContext(PlayerCombatant player, EnemyCombatant enemy, AbilityRuntimeInstance ability)
        {
            Player = player;
            Enemy = enemy;
            Ability = ability;
        }
    }

    public interface IAbilityExecutor
    {
        AbilityType SupportedType { get; }
        bool Execute(AbilityExecutionContext context, RhythmPerformanceResult performance);
    }

    [Serializable]
    public sealed class AbilitySlotAssignment
    {
        [SerializeField] private int laneId = 1;
        [SerializeField] private AbilityDefinition ability;

        public int LaneId => laneId;
        public AbilityDefinition Ability => ability;
    }
}
