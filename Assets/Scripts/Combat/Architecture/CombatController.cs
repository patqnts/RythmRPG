using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Core;
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
        [Tooltip("The encounter intro (intro music + enemy intro animation) plays out in full: no projectile appears before the song's loop starts. Off: the first attack may start during the intro.")]
        [SerializeField] private bool startCombatWithLoop = true;
        [Tooltip("Off (default): the enemy's first wind-up animation may begin in the last moments of the intro so its first projectile appears right as the loop starts. On: the wind-up also waits for the loop (the first projectile comes a bit later).")]
        [SerializeField] private bool windUpAfterIntro;
        [Tooltip("Pause after the enemy's attack ends before the player's turn starts (zoom in, ability icons). " +
                 "Gives the last hits, judgements and damage numbers time to land. 0 = straight away.")]
        [SerializeField, Min(0f)] private float playerTurnDelay = 0.6f;

        [Header("Battle result")]
        [Tooltip("Show the result screen (grade, score, stats) when a battle ends.")]
        [SerializeField] private bool showResultScreen = true;
        [Tooltip("Empty = a CombatResultScreen in the scene, then the result style's prefab, then the generated template.")]
        [SerializeField] private CombatResultScreen resultScreen;
        [Tooltip("Empty = Resources/Combat/UI/CombatResultGrading.")]
        [SerializeField] private CombatResultGrading resultGrading;

        private CombatTurnStateMachine stateMachine;
        private EnemyAttackSequenceDefinition nextSequence;
        private CombatResourceRules resourceRules;
        private float manaCarry;
        private CombatEncounterContext encounter;
        private RhythmPatternRunner runner;
        private AbilityRuntimeInstance selectedAbility;
        private int selectedLane;
        private Coroutine stateRoutine;
        // Set by a restart from the result screen: the player is already in place, so the next battle start skips
        // the walk-in and only replays the music intro and the enemy's intro.
        private bool restartInPlace;
        private CombatStatsTracker stats;

        /// <summary>The report of the last finished battle (null until one ends).</summary>
        public CombatReport LastReport { get; private set; }
        /// <summary>Raised when a battle ends, before the result screen opens.</summary>
        public event Action<CombatReport> ResultReady;
        /// <summary>Current hit combo this battle (for a combo counter).</summary>
        public int CurrentCombo => stats?.Combo ?? 0;
        /// <summary>Raised right after a judgement updates the combo (new combo count, 0 = it just broke).</summary>
        public event Action<int> ComboChanged;

        public CombatState CurrentState => stateMachine?.CurrentState ?? CombatState.BattleStart;
        public bool IsBattleActive { get; private set; }
        public event Action<CombatState> StateChanged;
        public event Action<CombatEncounterContext> BattleStarted;
        public event Action<CombatState> BattleEnded;

        // The resume countdown ("3, 2, 1") only runs while a battle is on.
        private void OnEnable() => GamePause.AddCountdownCondition(IsInBattle);
        private void OnDisable() => GamePause.RemoveCountdownCondition(IsInBattle);
        private bool IsInBattle() => IsBattleActive;

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
            // Every lane exists from the start (buttons, targets, ability slots); each chart then shows only its own.
            inputRouter.SetActiveLaneCount(GameInput.LaneCount);
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
            stats?.Stop();
            stats = new CombatStatsTracker(resultGrading);
            stats.Begin(encounter.Player, encounter.Enemy);
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
            vfxController?.ClearCastEffects();
            vfxController.SetAbilitySlotsVisible(false, false);
            stats?.Stop();
            if (resultScreen != null && resultScreen.IsOpen) resultScreen.Close();
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
            uiController ??= FindAnyObjectByType<CombatUIController>(FindObjectsInactive.Include);
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
            judgementSystem.OnJudgementResolved -= HandleStatsJudgement;
            judgementSystem.OnJudgementResolved += HandleStatsJudgement;
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
            // Encounter intro = intro music + enemy intro animation. The song's Intro starts as soon as the player
            // has moved into place and hands over to the Loop on its last sample. The enemy turn starts right after
            // the intro animation, but its first projectile is planned for the Loop (see RunEnemyStep), so the intro
            // always plays out in full and the attack lands as the Loop begins.
            nextSequence = encounter.Enemy.SelectAttackSequence(UnityEngine.Random.value);
            CombatSong song = nextSequence != null ? nextSequence.Song : null;
            CombatMusicDirector.Preload(song); // load the audio while the player walks, not when a section starts
            if (restartInPlace)
            {
                restartInPlace = false;
                musicDirector?.BeginEncounter(song);
                lanePresentation?.RevealHitLine();
                if (lanePresentation != null && lanePresentation.HitLineRevealSeconds > 0f)
                    yield return new WaitForSeconds(lanePresentation.HitLineRevealSeconds);
            }
            else
            {
                yield return encounterCoordinator.Prepare(encounter, () => musicDirector?.BeginEncounter(song));
            }
            yield return PlayEnemyBattleIntro();
            Transition(CombatState.EnemyTurnStart);
        }

        private IEnumerator EnemyTurnStartRoutine()
        {
            // Defensive: guarantees the hit line is back even if the player skipped ability selection
            // (DebugSkipPlayerTurn) without ever reaching PlayerAbilityExecuting.
            lanePresentation?.RevealHitLine();
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
            // The sequence's song keeps looping across steps and turns; only a different song cross-fades in
            // (on the next bar, loop only, no intro).
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
            // Player death: the End section starts now (on the next bar), not after the turn bookkeeping.
            if (encounter.Player.IsDefeated) musicDirector?.PlayEnd(false);
            Transition(CombatState.EnemyTurnEnd);
        }

        private IEnumerator RunEnemyStep(RhythmChart chart, string animationName, float anticipationDuration,
            float duration, AttackStepEndPolicy policy)
        {
            UseLanesOf(chart);
            // Plan first, then wind up: the chart's beat 0 has to land on the song's grid, so instead of
            // "wind-up, then wait for the next bar, then the notes' travel lead", pick the first grid-valid start whose
            // first projectile is at least one wind-up away (and not before the loop), and play the wind-up exactly
            // that long before the first projectile. The grid wait now overlaps the wind-up instead of adding to it.
            double anticipation = Math.Max(0f, anticipationDuration);
            double earliestSpawn = GameAudioClock.Now + anticipation;
            if (startCombatWithLoop && musicDirector != null && musicDirector.IsPlayingIntro)
            {
                double loopStart = musicDirector.LoopStartDsp;
                earliestSpawn = Math.Max(earliestSpawn, windUpAfterIntro ? loopStart + anticipation : loopStart);
            }
            double zeroDsp = runner.PlanStart(chart, earliestSpawn, out double firstSpawnDsp);
            double windUpDsp = firstSpawnDsp - anticipation;
            while (GameAudioClock.Now < windUpDsp && !encounter.Player.IsDefeated) yield return null;
            encounter.Enemy.PlayAnimation(animationName);

            bool completed = false;
            void OnCompleted(PatternRunResult result)
            {
                if (result.Mode == PatternRunMode.EnemyDefense) completed = true;
            }
            runner.PatternCompleted += OnCompleted;
            runner.Run(chart, new PatternRunContext(PatternRunMode.EnemyDefense, encounter.Player,
                encounter.Enemy.transform, duration, policy), zeroDsp);
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
            // A beat of breathing room after the enemy's attack (game time: it holds still while paused).
            if (playerTurnDelay > 0f) yield return new WaitForSeconds(playerTurnDelay);
            stats?.RecordTurn();
            abilitySlots.TickCooldowns();
            // Ability selection: player-turn stem (or low-pass). Casting brings the main loop back.
            musicDirector?.SetPlayerTurn(true);
            // The hit line is only meaningful while notes are on it; hide it while the player is just
            // choosing an ability (PlayerTurnStart + PlayerAbilitySelection), reveal it again once a chart
            // actually starts (see PlayerAbilityRoutine / EnemyTurnStartRoutine).
            lanePresentation?.HideHitLine();
            if (resourceRules != null && resourceRules.ManaPerPlayerTurn > 0) encounter.Player.GainMana(resourceRules.ManaPerPlayerTurn);
            // Choosing an ability: one lane per ability slot (the ability icons and their keys).
            UseLaneCount(abilitySlots.HighestLaneId > 0 ? abilitySlots.HighestLaneId : GameInput.LaneCount);
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
            stats?.RecordAbility(ability);
            abilitySlots.EndSelection();
            Transition(CombatState.PlayerAbilityExecuting);
        }

        private IEnumerator PlayerAbilityRoutine()
        {
            // The player finished choosing; back to the main loop and reveal the hit line before the chart starts.
            musicDirector?.SetPlayerTurn(false);
            lanePresentation?.RevealHitLine();

            // Plan the ability's pattern first, so the wisp pops at centre stage exactly when its first projectile
            // appears (on the song's grid), and the projectiles burst out of the pop.
            AbilityDefinition definition = selectedAbility?.Definition;
            RhythmChart chart = definition != null ? definition.RhythmPattern : null;
            UseLanesOf(chart);
            double earliestPop = GameAudioClock.Now + vfxController.MinimumCastSeconds(definition);
            double popDsp = earliestPop;
            double? plannedZero = null;
            if (chart != null) plannedZero = runner.PlanStart(chart, earliestPop, out popDsp);
            yield return vfxController.PlayAbilitySelected(selectedLane, selectedAbility, popDsp);
            vfxController.SetAbilitySlotsVisible(false, true);

            bool completed = false;
            void OnCompleted(AbilityRuntimeInstance _, RhythmPerformanceResult __) => completed = true;
            abilitySystem.ExecutionCompleted += OnCompleted;
            runner.NoteSpawned -= HandleAbilityNoteSpawned;
            runner.NoteSpawned += HandleAbilityNoteSpawned;
            abilitySystem.Execute(selectedAbility, encounter.Player, encounter.Enemy, runner,
                vfxController.AbilityPatternSpawnOrigin, vfxController.GetCenterLaneViewTransform(), selectedLane, plannedZero);
            while (!completed) yield return null;
            runner.NoteSpawned -= HandleAbilityNoteSpawned;
            abilitySystem.ExecutionCompleted -= OnCompleted;
            Transition(CombatState.PlayerTurnEnd);
        }

        // ---------- Lanes ----------

        private static readonly HashSet<RhythmChart> warnedLaneCharts = new();

        /// <summary>Shows the chart's lanes (1-4, from its lanes in the Rhythm Composer) on their keys.</summary>
        private void UseLanesOf(RhythmChart chart)
        {
            if (chart == null)
            {
                UseLaneCount(GameInput.LaneCount);
                return;
            }
            if (chart.Lanes.Count > RhythmChart.MaxLanes && warnedLaneCharts.Add(chart))
                Debug.LogWarning($"[Combat] Chart '{chart.name}' has {chart.Lanes.Count} lanes; only lanes 1-{RhythmChart.MaxLanes} " +
                                 "can be played. Open it in the Rhythm Composer (it offers to fit it) or run Tools > Rhythm > " +
                                 "Fit All Charts To 4 Lanes.", chart);
            UseLaneCount(chart.GameplayLaneCount);
        }

        private void UseLaneCount(int count)
        {
            if (inputRouter == null) return;
            inputRouter.SetActiveLaneCount(count);
            lanePresentation?.EnsurePresentation(inputRouter);
        }

        // Each projectile of the player's ability sparks out of centre stage as it is thrown.
        private void HandleAbilityNoteSpawned(Note note)
        {
            if (note == null || runner == null || runner.CurrentMode != PatternRunMode.PlayerAbility
                || CurrentState != CombatState.PlayerAbilityExecuting) return;
            vfxController?.PlayNoteSpark(note.transform.position, selectedAbility?.Definition);
        }

        private IEnumerator PlayerTurnEndRoutine()
        {
            vfxController.SetAbilitySlotsVisible(false, true);
            yield return null;
            if (encounter.Enemy.IsDefeated)
            {
                // Enemy death: the End section starts with the death animation.
                musicDirector?.PlayEnd(true);
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
            bool victory = terminalState == CombatState.Victory;
            runner?.CancelCurrentPattern(false);
            // Usually already triggered on the death itself; PlayEnd does nothing if the ending is scheduled.
            // The End clip plays out on its own (through the result screen); no End clip = fade out.
            musicDirector?.PlayEnd(victory);
            vfxController.SetAbilitySlotsVisible(false, true);
            vfxController.ClearCastEffects();
            yield return GamePause.WaitUnpausedRealtime(vfxController.TerminalDelay);

            // Result: built before a defeat restores health, shown before the encounter is restored.
            if (stats != null)
            {
                EnemyCombatant enemy = encounter.Enemy;
                string enemyName = enemy == null ? string.Empty
                    : enemy.Definition != null ? enemy.Definition.DisplayName : enemy.name;
                LastReport = stats.Finish(victory, enemyName);
                stats.Stop();
                ResultReady?.Invoke(LastReport);
                if (showResultScreen)
                {
                    CombatResultScreen screen = ResolveResultScreen();
                    if (screen != null)
                    {
                        yield return screen.Show(LastReport);
                        if (screen.Choice == CombatResultChoice.Restart)
                        {
                            // Start the next frame, outside this state routine (starting a battle replaces it).
                            StartCoroutine(RestartNextFrame());
                            yield break;
                        }
                    }
                }
            }

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

        private IEnumerator RestartNextFrame()
        {
            yield return null;
            RestartBattle();
        }

        /// <summary>
        /// Starts the same fight over (result screen's Restart): both sides back to their battle-start health and mana,
        /// cooldowns reset, the enemy shown again, then a fresh battle in place (no walk-in; intro music and enemy intro
        /// play again). The encounter's original world position is kept, so the final exit still returns there.
        /// </summary>
        public void RestartBattle()
        {
            if (!IsBattleActive || encounter.Player == null || encounter.Enemy == null) return;
            StopStateRoutine();
            runner?.CancelCurrentPattern(false);
            abilitySlots.EndSelection();
            vfxController?.ClearCastEffects();
            if (resultScreen != null && resultScreen.IsOpen) resultScreen.Close();

            encounter.Player.RestoreBattleStart();
            encounter.Enemy.RestoreBattleStart();
            abilitySlots.ResetRuntime();
            Animator animator = encounter.Enemy.Animator;
            if (animator != null)
            {
                // Back out of the death animation to the default state; the battle intro sets the battle pose again.
                animator.Rebind();
                animator.Update(0f);
            }

            CombatEncounterContext context = encounter;
            IsBattleActive = false;
            restartInPlace = true;
            BeginBattle(context);
        }

        private void HandleModifierJudgement(RhythmJudgementResult result) => modifierSystem.OnJudgementResolved(result, runner);

        // Enemy turn: damage weighted by judgement (see CombatResourceRules), unless a ward covers the lane.
        private int ResolveDefenseDamage(Note note, RhythmJudgementResult result)
        {
            if (DebugInvulnerable) return 0;
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
            stats?.RecordGuard();
        }

        private void HandleStatsJudgement(RhythmJudgementResult result)
        {
            if (!IsBattleActive || stats == null) return;
            stats.RecordJudgement(result.Judgement, runner != null ? runner.CurrentMode : PatternRunMode.EnemyDefense);
            ComboChanged?.Invoke(stats.Combo);
        }

        private CombatResultScreen ResolveResultScreen()
        {
            if (resultScreen != null) return resultScreen;
            resultScreen = FindAnyObjectByType<CombatResultScreen>(FindObjectsInactive.Include);
            if (resultScreen != null) return resultScreen;
            CombatResultStyle style = CombatResultStyle.LoadOrDefault();
            resultScreen = style.ScreenPrefab != null ? Instantiate(style.ScreenPrefab) : CombatResultScreen.CreateTemplate(style);
            return resultScreen;
        }

#if UNITY_EDITOR
        /// <summary>Editor tool: assigns a result screen built in the scene.</summary>
        public void EditorAssignResultScreen(CombatResultScreen screen) => resultScreen = screen;
#endif

        private void Transition(CombatState next)
        {
            stateRoutine = null;
            if (!stateMachine.TryTransition(next))
                Debug.LogError($"Invalid combat transition: {CurrentState} -> {next}", this);
        }

        // ---------- Dev tools (driven by CombatDebugTools) ----------

        public CombatEncounterContext Encounter => encounter;
        public AbilitySlotController AbilitySlots => abilitySlots;

        /// <summary>Dev tool: enemy notes deal no damage while true.</summary>
        public bool DebugInvulnerable { get; set; }

        /// <summary>Dev tool: ends the enemy's attack now (remaining notes vanish, no damage) and moves to the player turn.</summary>
        public bool DebugSkipEnemyTurn()
        {
            if (!IsBattleActive) return false;
            if (CurrentState == CombatState.EnemyTurnStart)
            {
                StopStateRoutine();
                Transition(CombatState.EnemyTurnExecuting);
            }
            if (CurrentState != CombatState.EnemyTurnExecuting) return false;
            StopStateRoutine();
            runner?.CancelCurrentPattern(false);
            Transition(CombatState.EnemyTurnEnd);
            return true;
        }

        /// <summary>Dev tool: the player passes the turn without using an ability.</summary>
        public bool DebugSkipPlayerTurn()
        {
            if (!IsBattleActive || CurrentState != CombatState.PlayerAbilitySelection) return false;
            StopStateRoutine();
            abilitySlots.EndSelection();
            Transition(CombatState.PlayerTurnEnd);
            return true;
        }

        /// <summary>
        /// Dev tool: kills the enemy (victory) or the player (defeat) and plays the normal ending.
        /// While an ability is running the kill is applied and the battle ends when the ability finishes.
        /// </summary>
        public bool DebugEndBattle(bool victory)
        {
            if (!IsBattleActive || CurrentState == CombatState.BattleStart
                || CurrentState == CombatState.Victory || CurrentState == CombatState.Defeat) return false;
            if (CurrentState == CombatState.PlayerAbilityExecuting)
            {
                if (!victory) return false;
                encounter.Enemy.ApplyDamage(encounter.Enemy.CurrentHealth);
                return true;
            }
            StopStateRoutine();
            runner?.CancelCurrentPattern(false);
            abilitySlots.EndSelection();
            if (victory) encounter.Enemy.ApplyDamage(encounter.Enemy.CurrentHealth);
            else encounter.Player.ApplyDamage(encounter.Player.CurrentHealth);
            stateRoutine = StartCoroutine(DebugFinishRoutine(victory));
            return true;
        }

        private IEnumerator DebugFinishRoutine(bool victory)
        {
            musicDirector?.PlayEnd(victory);
            if (victory) yield return PlayEnemyDefeat();
            Transition(victory ? CombatState.Victory : CombatState.Defeat);
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
