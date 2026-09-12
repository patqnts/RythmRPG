using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class AbilityExecutor : MonoBehaviour
    {
        private Dictionary<AbilityType, IAbilityExecutor> executors;

        private void Awake() => RefreshExecutors();

        public void RefreshExecutors()
        {
            executors = GetComponents<MonoBehaviour>().OfType<IAbilityExecutor>()
                .GroupBy(executor => executor.SupportedType)
                .ToDictionary(group => group.Key, group => group.First());
        }

        public bool Execute(AbilityExecutionContext context, RhythmPerformanceResult performance)
        {
            AbilityDefinition definition = context.Ability?.Definition;
            if (definition == null) return false;
            if (executors == null) RefreshExecutors();
            return executors.TryGetValue(definition.AbilityType, out IAbilityExecutor executor)
                && executor.Execute(context, performance);
        }
    }
}
