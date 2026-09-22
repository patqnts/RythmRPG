using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class CombatController : MonoBehaviour
    {
        [SerializeField] private CombatEncounterCoordinator encounterCoordinator;
        [SerializeField] private LaneInputRouter inputRouter;
        [SerializeField] private AbilitySlotController abilitySlots;
        [SerializeField] private RhythmAbilitySystem abilitySystem;
        [SerializeField] private AbilityExecutor abilityExecutor;
        [SerializeField] private RhythmJudgementSystem judgementSystem;
        [SerializeField] private CombatModifierSystem modifierSystem;
        [SerializeField] private CombatVFXController vfxController;
        [SerializeField] private CombatUIController uiController;
        [SerializeField] private CombatLanePresentation3D lanePresentation;
        [SerializeField] private RhythmChart defaultEnemyPattern;
        [SerializeField] private CombatMusicDirector musicDirector;

        private CombatTurnStateMachine stateMachine;
        private EnemyAttackSequenceDefinition nextSequence;
        private CombatResourceRules resourceRules;
        private float manaCarry;
        private CombatEncounterContext encounter;
        private RhythmPatternRunner runner;
        private AbilityRuntimeInstance selectedAbility;
        private int selectedLane;
        private Coroutine stateRoutine;

        public CombatState CurrentState => stateMachine?.CurrentState ?? CombatState.BattleStart;
        public bool IsBattleActive { get; private set; }
        public event Action<CombatState> StateChanged;
        public event Action<CombatEncounterContext> BattleStarted;
        public event Action<CombatState> BattleEnded;

        private void Awake()
        {
            EnsureServices();
            lanePresentation.SetPresentationVisible(false);
        }

        private void Update() => stateMachine?.Tick();

        public void BeginBattle(CombatEncounterContext context)
        {
            if (IsBattleActive || context.Player == null || context.Enemy == null) return;
            EnsureServices();
            runner = context.Enemy.PatternRunner ?? context.Enemy.gameObject.AddComponent<RhythmPatternRunner>();
            runner.enabled = true;
            lanePresentation.ConfigureEncounter(context, runner.ProjectileObjectHolder);
            lanePresentation.EnsurePresentation(inputRouter);
            lanePresentation.SetPresentationVisible(true);
            // Sequence: the player walks to its combat spot first; the coordinator reveals the hit line afterwards.
            lanePresentation.HideHitLine();
            encounter = context;
            encounter.Player.CaptureBattleStart();
            encounter.Enemy.CaptureBattleStart();
            runner.ConfigurePresentation(lanePresentation);
            runner.ConfigureMusic(musicDirector);
            resourceRules = CombatResourceRules.Load();
            runner.DefenseDamageResolver = ResolveDefenseDamage;
            modifierSystem.Clear();
            manaCarry = 0f;
            BindRuntime();
            ConfigureStateMachine();
            IsBattleActive = true;
            uiController?.Bind(this, encounter.Player, encounter.Enemy);
            vfxController.Bind(abilitySlots, inputRouter, judgementSystem, encounter.Enemy, encounter.Player);
            abilitySlots.SetHoldDuration(vfxController.SelectionHoldDuration);
            BattleStarted?.Invoke(encounter);
            stateMachine.Start();
        }

        public void CancelBattle()
        {
            if (!IsBattleActive) return;
            StopStateRoutine();
            runner?.CancelCurrentPattern(false);
            musicDirector?.Stop();
            abilitySlots.EndSelection();
            vfxController.SetAbilitySlotsVisible(false, false);
            encounterCoordinator.Restore(encounter, false);
            lanePresentation?.SetPresentationVisible(false);
            IsBattleActive = false;
            BattleEnded?.Invoke(CurrentState);
        }

        private void EnsureServices()
        {
            encounterCoordinator ??= GetComponent<CombatEncounterCoordinator>() ?? gameObject.AddComponent<CombatEncounterCoordinator>();
            inputRouter ??= GetComponent<LaneInputRouter>() ?? gameObject.AddComponent<LaneInputRouter>();
            abilitySlots ??= GetComponent<AbilitySlotController>() ?? gameObject.AddComponent<AbilitySlotController>();
            modifierSystem ??= GetComponent<CombatModifierSystem>() ?? gameObject.AddComponent<CombatModifierSystem>();
            judgementSystem ??= GetComponent<RhythmJudgementSystem>() ?? gameObject.AddComponent<RhythmJudgementSystem>();
            if (GetComponent<BasicAttackExecutor>() == null) gameObject.AddComponent<BasicAttackExecutor>();
            abilityExecutor ??= GetComponent<AbilityExecutor>() ?? gameObject.AddComponent<AbilityExecutor>();
            abilitySystem ??= GetComponent<RhythmAbilitySystem>() ?? gameObject.AddComponent<RhythmAbilitySystem>();
            vfxController ??= GetComponent<CombatVFXController>() ?? gameObject.AddComponent<CombatVFXController>();
            lanePresentation ??= GetComponent<CombatLanePresentation3D>() ?? gameObject.AddComponent<CombatLanePresentation3D>();
            musicDirector ??= GetComponent<CombatMusicDirector>() ?? gameObject.AddComponent<CombatMusicDirector>();
            uiController ??= FindFirstObjectByType<CombatUIController>(FindObjectsInactive.Include);
            if (uiController == null)
            {
                GameObject uiObject = new("Runtime Combat UI");
                uiController = uiObject.AddComponent<CombatUIController>();
            }
        }

        private void BindRuntime()
        {
            inputRouter.RhythmLanePressed -= runner.HandleLanePressed;
            inputRouter.RhythmLanePressed += runner.HandleLanePressed;
            inputRouter.RhythmLaneReleased -= runner.HandleLaneReleased;
            inputRouter.RhythmLaneReleased += runner.HandleLaneReleased;
            judgementSystem.Bind(runner);
            runner.ConfigureInput(inputRouter);
            judgementSystem.OnJudgementResolved -= HandleModifierJudgement;
            judgementSystem.OnJudgementResolved += HandleModifierJudgement;
            judgementSystem.OnJudgementResolved -= HandleManaJudgement;
            judgementSystem.OnJudgementResolved += HandleManaJudgement;
            modifierSystem.DamageBlocked -= HandleDamageBlocked;
            modifierSystem.DamageBlocked += HandleDamageBlocked;
            abilitySlots.Initialize(inputRouter, encounter.Player);
            abilitySlots.AbilitySelected -= HandleAbilitySelected;
            abilitySlots.AbilitySelected += HandleAbilitySelected;
        }

        private void ConfigureStateMachine()
        {
            stateMachine = new CombatTurnStateMachine();
            foreach (CombatState state in Enum.GetValues(typeof(CombatState)))
                stateMachine.Register(new CallbackCombatState(state, () => EnterState(state), null, null));
            stateMachine.StateChanged += (_, next) =>
            {
                inputRouter.SetState(next);
                StateChanged?.Invoke(next);
            };
        }

        private void EnterState(CombatState state)
        {
            StopStateRoutine();
            stateRoutine = state switch
            {
                CombatState.BattleStart => StartCoroutine(BattleStartRoutine()),
                CombatState.EnemyTurnStart => StartCoroutine(EnemyTurnStartRoutine()),
                CombatState.EnemyTurnExecuting => StartCoroutine(EnemyTurnRoutine()),
                CombatState.EnemyTurnEnd => StartCoroutine(EnemyTurnEndRoutine()),
                CombatState.PlayerTurnStart => StartCoroutine(PlayerTurnStartRoutine()),
                CombatState.PlayerAbilitySelection => StartCoroutine(PlayerSelectionRoutine()),
                CombatState.PlayerAbilityExecuting => StartCoroutine(PlayerAbilityRoutine()),
                CombatState.PlayerTurnEnd => StartCoroutine(PlayerTurnEndRoutine()),
                CombatState.Victory => StartCoroutine(TerminalRoutine(CombatState.Victory)),
                CombatState.Defeat => StartCoroutine(TerminalRoutine(CombatState.Defeat)),
                _ => null
            };
        }

        private IEnumerator BattleStartRoutine()
        {
            // Pick the first attack now so its song already plays during the intro.
            nextSequence = encounter.Enemy.SelectAttackSequence(UnityEngine.Random.value);
            if (nextSequence != null) musicDirector?.PlaySong(nextSequence.Song);
            yield return encounterCoordinator.Prepare(encounter);
            yield return PlayEnemyBattleIntro();
            Transition(CombatState.EnemyTurnStart);
        }

        private IEnumerator EnemyTurnStartRoutine()
        {
            vfxController.SetAbilitySlotsVisible(false, true);
            musicDirector?.SetPlayerTurn(false);
            modifierSystem.OnEnemyTurnStarted();
            yield return null;
            Transition(CombatState.EnemyTurnExecuting);
        }

        private IEnumerator EnemyTurnRoutine()
        {
            EnemyAttackSequenceDefinition sequence = nextSequence ?? encounter.Enemy.SelectAttackSequence(UnityEngine.Random.value);
            nextSequence = null;
            // The sequence's song keeps looping across steps and turns; only a different song cross-fades in.
            if (sequence != null) musicDirector?.PlaySong(sequence.Song);
            IReadOnlyList<EnemyAttackStepDefinition> steps = sequence?.Steps;
            if (steps == null || steps.Count == 0)
            {
                RhythmChart fallback = runner.FallbackChart ?? defaultEnemyPattern
                    ?? Resources.Load<RhythmChart>("Combat/Patterns/EnemyBasicPattern");
                yield return RunEnemyStep(fallback, string.Empty, 0f, 0f, AttackStepEndPolicy.WaitForResolvedNotes);
            }
            else
            {
                foreach (EnemyAttackStepDefinition step in steps.Where(step => step != null))
                {
                    yield return RunEnemyStep(step.RhythmPattern, step.AnimationName,
                        step.AnticipationDuration, step.DurationOverride, step.EndPolicy);
                    if (encounter.Player.IsDefeated) break;
                }
            }
            Transition(CombatState.EnemyTurnEnd);
        }

        private IEnumerator RunEnemyStep(RhythmChart chart, string animationName, float anticipationDuration,
            float duration, AttackStepEndPolicy policy)
        {
            encounter.Enemy.PlayAnimation(animationName);
            if (anticipationDuration > 0f) yield return new WaitForSeconds(anticipationDuration);
            bool completed = false;
            void OnCompleted(PatternRunResult result)
            {
                if (result.Mode == PatternRunMode.EnemyDefense) completed = true;
            }
            runner.PatternCompleted += OnCompleted;
            runner.Run(chart, new PatternRunContext(PatternRunMode.EnemyDefense, encounter.Player,
                encounter.Enemy.transform, duration, policy));
            while (!completed && !encounter.Player.IsDefeated) yield return null;
            runner.PatternCompleted -= OnCompleted;
        }

        private IEnumerator EnemyTurnEndRoutine()
        {
            modifierSystem.OnEnemyTurnEnded();
            yield return null;
            Transition(encounter.Player.IsDefeated ? CombatState.Defeat : CombatState.PlayerTurnStart);
        }

        private IEnumerator PlayerTurnStartRoutine()
        {
            abilitySlots.TickCooldowns();
            musicDirector?.SetPlayerTurn(true);
            if (resourceRules != null && resourceRules.ManaPerPlayerTurn > 0) encounter.Player.GainMana(resourceRules.ManaPerPlayerTurn);
            vfxController.SetAbilitySlotsVisible(true, true);
            yield return null;
            Transition(CombatState.PlayerAbilitySelection);
        }

        private IEnumerator PlayerSelectionRoutine()
        {
            abilitySlots.BeginSelection();
            while (CurrentState == CombatState.PlayerAbilitySelection) yield return null;
        }

        private void HandleAbilitySelected(int laneId, AbilityRuntimeInstance ability)
        {
            if (CurrentState != CombatState.PlayerAbilitySelection) return;
            selectedLane = laneId;
            selectedAbility = ability;
            abilitySlots.EndSelection();
            Transition(CombatState.PlayerAbilityExecuting);
        }

        private IEnumerator PlayerAbilityRoutine()
        {
            yield return vfxController.PlayAbilitySelected(selectedLane, selectedAbility);
            vfxController.SetAbilitySlotsVisible(false, true);
            bool completed = false;
            void OnCompleted(AbilityRuntimeInstance _, RhythmPerformanceResult __) => completed = true;
            abilitySystem.ExecutionCompleted += OnCompleted;
            abilitySystem.Execute(selectedAbility, encounter.Player, encounter.Enemy, runner,
                vfxController.AbilityPatternSpawnOrigin, vfxController.GetCenterLaneViewTransform(), selectedLane);
            while (!completed) yield return null;
            abilitySystem.ExecutionCompleted -= OnCompleted;
            Transition(CombatState.PlayerTurnEnd);
        }

        private IEnumerator PlayerTurnEndRoutine()
        {
            vfxController.SetAbilitySlotsVisible(false, true);
            yield return null;
            if (encounter.Enemy.IsDefeated)
            {
                yield return PlayEnemyDefeat();
                Transition(CombatState.Victory);
            }
            else
            {
                Transition(CombatState.EnemyTurnStart);
            }
        }

        private IEnumerator PlayEnemyDefeat()
        {
            vfxController.PrepareEnemyDefeat();
            EnemyDefinition definition = encounter.Enemy.Definition;
            string stateName = definition != null ? definition.DeathAnimationName : "Death";
            float duration = definition != null ? definition.DeathAnimationFallbackDuration : 1f;
            Animator animator = encounter.Enemy.Animator;
            int hash = Animator.StringToHash(stateName);
            if (animator != null && !string.IsNullOrWhiteSpace(stateName) && animator.HasState(0, hash))
            {
                animator.Play(hash, 0, 0f);
                yield return null;
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                duration = Mathf.Max(duration, info.length);
            }
            if (duration > 0f) yield return new WaitForSeconds(duration);
            encounter.Enemy.HideAfterDefeat();
        }

        private IEnumerator PlayEnemyBattleIntro()
        {
            EnemyDefinition definition = encounter.Enemy.Definition;
            Animator animator = encounter.Enemy.Animator;
            if (definition == null || animator == null) yield break;

            SetAnimatorBoolIfPresent(animator, definition.BattleAnimatorBoolParameter, true);

            string stateName = definition.IntroAnimationName;
            float duration = definition.IntroAnimationFallbackDuration;
            int hash = Animator.StringToHash(stateName);
            if (!string.IsNullOrWhiteSpace(stateName) && animator.HasState(0, hash))
            {
                animator.Play(hash, 0, 0f);
                yield return null;
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                duration = Mathf.Max(duration, info.length);
            }

            if (duration > 0f) yield return new WaitForSeconds(duration);
        }

        private IEnumerator TerminalRoutine(CombatState terminalState)
        {
            runner?.CancelCurrentPattern(false);
            musicDirector?.Stop();
            vfxController.SetAbilitySlotsVisible(false, true);
            yield return new WaitForSecondsRealtime(vfxController.TerminalDelay);
            bool victory = terminalState == CombatState.Victory;
            if (!victory)
            {
                encounter.Player.RestoreBattleStart();
                encounter.Enemy.RestoreBattleStart();
            }
            encounterCoordinator.Restore(encounter, victory);
            lanePresentation?.SetPresentationVisible(false);
            IsBattleActive = false;
            BattleEnded?.Invoke(terminalState);
        }

        private void HandleModifierJudgement(RhythmJudgementResult result) => modifierSystem.OnJudgementResolved(result, runner);

        // Enemy turn: damage weighted by judgement (see CombatResourceRules), unless a ward covers the lane.
        private int ResolveDefenseDamage(Note note, RhythmJudgementResult result)
        {
            int amount = (resourceRules ?? CombatResourceRules.Load()).DefenseDamage(note.damage, result.Judgement);
            if (amount > 0 && modifierSystem.TryBlockDamage(result)) return 0;
            return amount;
        }

        // Mana builds up from good play: Perfect / Good hits (player input only), weighted by judgement.
        private void HandleManaJudgement(RhythmJudgementResult result)
        {
            if (!IsBattleActive || encounter.Player == null || result.Source != NoteResolutionSource.PlayerInput) return;
            CombatResourceRules rules = resourceRules ?? CombatResourceRules.Load();
            manaCarry += rules.ManaGain(result.Judgement, runner != null ? runner.CurrentMode : PatternRunMode.EnemyDefense);
            int whole = Mathf.FloorToInt(manaCarry);
            if (whole <= 0) return;
            manaCarry -= whole;
            encounter.Player.GainMana(whole);
        }

        private void HandleDamageBlocked(RhythmJudgementResult result)
        {
            vfxController?.ShowFloatingText("GUARD", result.WorldPosition, new Color(0.6f, 0.8f, 1f));
        }

        private void Transition(CombatState next)
        {
            stateRoutine = null;
            if (!stateMachine.TryTransition(next))
                Debug.LogError($"Invalid combat transition: {CurrentState} -> {next}", this);
        }

        private void StopStateRoutine()
        {
            if (stateRoutine == null) return;
            StopCoroutine(stateRoutine);
            stateRoutine = null;
        }

        private static void SetAnimatorBoolIfPresent(Animator animator, string parameterName, bool value)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName)) return;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Bool || parameter.name != parameterName) continue;
                animator.SetBool(parameterName, value);
                return;
            }
        }

        private sealed class CallbackCombatState : ICombatState
        {
            private readonly Action enter;
            private readonly Action exit;
            private readonly Action tick;
            public CombatState State { get; }
            public CallbackCombatState(CombatState state, Action enter, Action exit, Action tick)
            { State = state; this.enter = enter; this.exit = exit; this.tick = tick; }
            public void Enter() => enter?.Invoke();
            public void Exit() => exit?.Invoke();
            public void Tick() => tick?.Invoke();
        }
    }
}
