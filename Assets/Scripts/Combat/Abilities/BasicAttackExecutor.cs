using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class BasicAttackExecutor : MonoBehaviour, IAbilityExecutor
    {
        public AbilityType SupportedType => AbilityType.BasicAttack;

        public bool Execute(AbilityExecutionContext context, RhythmPerformanceResult performance)
        {
            if (context.Ability?.Definition == null || context.Enemy == null) return false;
            int damage = RhythmPerformanceCalculator.CalculatePower(context.Ability.Definition.BasePower, performance);
            context.Enemy.ApplyDamage(damage);
            return true;
        }
    }
}
