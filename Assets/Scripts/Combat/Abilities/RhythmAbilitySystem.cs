using System;
using System.Collections.Generic;
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
            RhythmPatternRunner patternRunner, Transform spawnOrigin = null, Transform impactOrigin = null, int selectedLane = -1,
            double? plannedZeroDsp = null)
        {
            laneId = selectedLane;
            if (executor == null) executor = GetComponent<AbilityExecutor>();
            if (vfxController == null) vfxController = GetComponent<CombatVFXController>();
            runner = patternRunner;
            context = new AbilityExecutionContext(player, enemy, ability);
            abilityImpactOrigin = impactOrigin != null ? impactOrigin : spawnOrigin != null ? spawnOrigin : player != null ? player.transform : null;
            if (runner == null || ability?.Definition?.RhythmPattern == null)
            {
                // Zero-opportunity chart: never counts as a completed / flawless execution.
                Finish(new RhythmPerformanceResult(0, 0, 0, 0f, Array.Empty<RhythmJudgementResult>()), false);
                return;
            }
            runner.PatternCompleted += HandlePatternCompleted;
            ExecutionStarted?.Invoke(ability);
            // plannedZeroDsp: the start planned before the cast animation (RhythmPatternRunner.PlanStart), so the first
            // projectile appears right as the ability's wisp pops.
            runner.Run(ability.Definition.RhythmPattern,
                new PatternRunContext(PatternRunMode.PlayerAbility, player, spawnOrigin != null ? spawnOrigin : player.transform),
                plannedZeroDsp);
        }

        private void HandlePatternCompleted(PatternRunResult result)
        {
            if (result.Mode != PatternRunMode.PlayerAbility) return;
            runner.PatternCompleted -= HandlePatternCompleted;
            AbilityOutcomeProfile profile = context.Ability.Definition.OutcomeProfile;
            RhythmPerformanceResult performance = RhythmPerformanceCalculator.Calculate(
                result.Performance.ExpectedNoteCount, result.Performance.Judgements, profile);
            Finish(performance, !result.WasCancelled);
        }

        private void Finish(RhythmPerformanceResult performance, bool completed)
        {
            StartCoroutine(FinishRoutine(performance, completed));
        }

        private System.Collections.IEnumerator FinishRoutine(RhythmPerformanceResult performance, bool completed)
        {
            if (vfxController == null) vfxController = GetComponent<CombatVFXController>();
            if (vfxController == null) vfxController = FindAnyObjectByType<CombatVFXController>();
            AbilityVFXProfile vfxProfile = context.Ability?.Definition?.VFXProfile;
            AbilityDefinition definition = context.Ability?.Definition;
            CharacterAttackSequence sequence = definition?.AttackSequence;
            // The character is the hit line during the chart; for its attack animation it takes its own form again
            // (the line and markers pull back into it), and becomes the line again afterwards.
            bool steppedOut = sequence != null && vfxController != null;
            if (steppedOut) vfxController.StepOutOfHitLine();
            ImpactAnticipationStarted?.Invoke(context, performance);
            if (vfxProfile != null && vfxProfile.ImpactAnticipationDuration > 0f)
                yield return new WaitForSeconds(vfxProfile.ImpactAnticipationDuration);
            if (steppedOut)
            {
                float waitUntil = Time.time + 2f; // failsafe
                while (vfxController.CharacterMorphing && Time.time < waitUntil) yield return null;
            }

            // Build: the cast snapshot made at commitment (frozen cost, reserved bonuses). Performance is final now, so
            // conditional pre-outcome bonuses resolve before any total is frozen.
            CombatBuildRuntime build = GetComponent<CombatBuildRuntime>();
            if (build != null && !build.IsActive) build = null;
            CastSnapshot cast = build != null && build.CurrentCast != null && build.CurrentCast.Ability == context.Ability
                ? build.CurrentCast : null;
            if (cast != null) build.PreOutcome(cast, performance, completed);

            var effectContext = new AbilityEffectContext
            {
                Player = context.Player,
                Enemy = context.Enemy,
                Ability = definition,
                Performance = performance,
                LaneId = laneId,
                Modifiers = GetComponent<CombatModifierSystem>(),
                Rules = CombatResourceRules.Load(),
                ShowText = vfxController != null ? new Action<string, Vector3, Color>(vfxController.ShowFloatingText) : null,
                Build = build,
                Cast = cast
            };
            // The ability's effects land as the attack's hits land: split by hit weight (multi-hit, damage over
            // time), once-effects on their chosen hit. Whatever is left lands when the sequence ends. Totals are
            // frozen when the resolution is created. The committed quote includes dedicated-upgrade effects.
            IReadOnlyList<AbilityEffect> effects = cast?.Quote != null ? (IReadOnlyList<AbilityEffect>)cast.Quote.Effects
                : definition != null ? definition.Effects : null;
            var resolution = new AbilityResolution(effectContext, effects, sequence != null ? sequence.TotalHitWeight : 0f);
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
            if (cast != null) build.CastResolved(cast);
            // Back into the hit line (not after the finishing blow: the character stays for the victory).
            if (steppedOut && (context.Enemy == null || !context.Enemy.IsDefeated)) vfxController.StepBackIntoHitLine();
            if (vfxProfile != null && vfxProfile.ImpactSettleDuration > 0f)
                yield return new WaitForSeconds(vfxProfile.ImpactSettleDuration);

            ExecutionCompleted?.Invoke(context.Ability, performance);
        }
    }
}
