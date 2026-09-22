using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class RhythmAbilitySystem : MonoBehaviour
    {
        [SerializeField] private AbilityExecutor executor;
        [SerializeField] private CombatVFXController vfxController;
        [SerializeField] private CharacterAttackPerformer attackPerformer;
        private RhythmPatternRunner runner;
        private AbilityExecutionContext context;
        private Transform abilityImpactOrigin;
        private int laneId = -1;

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
            RhythmPatternRunner patternRunner, Transform spawnOrigin = null, Transform impactOrigin = null, int selectedLane = -1)
        {
            laneId = selectedLane;
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

            AbilityDefinition definition = context.Ability?.Definition;
            CharacterAttackSequence sequence = definition?.AttackSequence;
            var effectContext = new AbilityEffectContext
            {
                Player = context.Player,
                Enemy = context.Enemy,
                Ability = definition,
                Performance = performance,
                LaneId = laneId,
                Modifiers = GetComponent<CombatModifierSystem>(),
                Rules = CombatResourceRules.Load(),
                ShowText = vfxController != null ? new Action<string, Vector3, Color>(vfxController.ShowFloatingText) : null
            };
            // The ability's effects land as the attack's hits land: split by hit weight (multi-hit, damage over
            // time), once-effects on their chosen hit. Whatever is left lands when the sequence ends.
            var resolution = new AbilityResolution(effectContext, definition != null ? definition.Effects : null,
                sequence != null ? sequence.TotalHitWeight : 0f);
            bool announced = false;
            void Hit(float weight)
            {
                resolution.Hit(weight);
                if (announced) return;
                announced = true;
                ImpactResolved?.Invoke(context, performance);
            }

            if (sequence != null)
            {
                if (attackPerformer == null) attackPerformer = GetComponent<CharacterAttackPerformer>();
                if (attackPerformer == null) attackPerformer = gameObject.AddComponent<CharacterAttackPerformer>();
                Color accent = vfxProfile != null ? vfxProfile.AccentColor : Color.white;
                yield return attackPerformer.Perform(sequence, context.Player, context.Enemy, accent, Hit);
            }
            else if (vfxController != null && definition != null
                     && (definition.AbilityType == AbilityType.BasicAttack || definition.AbilityType == AbilityType.SpecialAttack))
            {
                yield return vfxController.PlayAbilityImpact(context.Ability, abilityImpactOrigin, context.Enemy != null ? context.Enemy.transform : null);
            }

            resolution.Finish();
            if (!announced) ImpactResolved?.Invoke(context, performance);
            if (vfxProfile != null && vfxProfile.ImpactSettleDuration > 0f)
                yield return new WaitForSeconds(vfxProfile.ImpactSettleDuration);

            ExecutionCompleted?.Invoke(context.Ability, performance);
        }
    }
}
