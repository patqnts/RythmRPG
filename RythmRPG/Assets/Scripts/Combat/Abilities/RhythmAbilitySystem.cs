using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class RhythmAbilitySystem : MonoBehaviour
    {
        [SerializeField] private AbilityExecutor executor;
        [SerializeField] private CombatVFXController vfxController;
        private RhythmPatternRunner runner;
        private AbilityExecutionContext context;
        private Transform abilityImpactOrigin;

        public event Action<AbilityRuntimeInstance> ExecutionStarted;
        public event Action<AbilityRuntimeInstance, RhythmPerformanceResult> ExecutionCompleted;
        public event Action<AbilityExecutionContext, RhythmPerformanceResult> ImpactAnticipationStarted;
        public event Action<AbilityExecutionContext, RhythmPerformanceResult> ImpactResolved;

        private void Awake()
        {
            if (executor == null) executor = GetComponent<AbilityExecutor>();
            if (vfxController == null) vfxController = GetComponent<CombatVFXController>();
        }

        public void Execute(AbilityRuntimeInstance ability, PlayerCombatant player, EnemyCombatant enemy,
            RhythmPatternRunner patternRunner, Transform spawnOrigin = null, Transform impactOrigin = null)
        {
            if (executor == null) executor = GetComponent<AbilityExecutor>();
            if (vfxController == null) vfxController = GetComponent<CombatVFXController>();
            runner = patternRunner;
            context = new AbilityExecutionContext(player, enemy, ability);
            abilityImpactOrigin = impactOrigin != null ? impactOrigin : spawnOrigin != null ? spawnOrigin : player != null ? player.transform : null;
            if (runner == null || ability?.Definition?.RhythmPattern == null)
            {
                Finish(new RhythmPerformanceResult(0, 0, 0, 0f, Array.Empty<RhythmJudgementResult>()));
                return;
            }
            runner.PatternCompleted += HandlePatternCompleted;
            ExecutionStarted?.Invoke(ability);
            runner.Run(ability.Definition.RhythmPattern,
                new PatternRunContext(PatternRunMode.PlayerAbility, player, spawnOrigin != null ? spawnOrigin : player.transform));
        }

        private void HandlePatternCompleted(PatternRunResult result)
        {
            if (result.Mode != PatternRunMode.PlayerAbility) return;
            runner.PatternCompleted -= HandlePatternCompleted;
            AbilityOutcomeProfile profile = context.Ability.Definition.OutcomeProfile;
            RhythmPerformanceResult performance = RhythmPerformanceCalculator.Calculate(
                result.Performance.ExpectedNoteCount, result.Performance.Judgements, profile);
            Finish(performance);
        }

        private void Finish(RhythmPerformanceResult performance)
        {
            StartCoroutine(FinishRoutine(performance));
        }

        private System.Collections.IEnumerator FinishRoutine(RhythmPerformanceResult performance)
        {
            if (vfxController == null) vfxController = GetComponent<CombatVFXController>();
            AbilityVFXProfile vfxProfile = context.Ability?.Definition?.VFXProfile;
            ImpactAnticipationStarted?.Invoke(context, performance);
            if (vfxProfile != null && vfxProfile.ImpactAnticipationDuration > 0f)
                yield return new WaitForSeconds(vfxProfile.ImpactAnticipationDuration);

            if (vfxController != null)
                yield return vfxController.PlayAbilityImpact(context.Ability, abilityImpactOrigin, context.Enemy != null ? context.Enemy.transform : null);

            executor?.Execute(context, performance);
            ImpactResolved?.Invoke(context, performance);
            if (vfxProfile != null && vfxProfile.ImpactSettleDuration > 0f)
                yield return new WaitForSeconds(vfxProfile.ImpactSettleDuration);

            ExecutionCompleted?.Invoke(context.Ability, performance);
        }
    }
}
