using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class AbilityRuntimeInstance
    {
        public AbilityDefinition Definition { get; }
        public int RemainingCooldown { get; private set; }

        public AbilityRuntimeInstance(AbilityDefinition definition) => Definition = definition;

        public bool CanUse(PlayerCombatant player) => Definition != null
            && player != null
            && !player.IsDefeated
            && RemainingCooldown == 0
            && player.CurrentMana >= Definition.ManaCost
            && Definition.RhythmPattern != null;

        public bool Commit(PlayerCombatant player)
        {
            if (!CanUse(player) || !player.SpendMana(Definition.ManaCost)) return false;
            RemainingCooldown = Definition.Cooldown;
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
