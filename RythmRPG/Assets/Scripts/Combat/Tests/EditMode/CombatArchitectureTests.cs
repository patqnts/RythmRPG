using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class CombatArchitectureTests
    {
        [Test]
        public void StateMachine_EnforcesTurnOrder()
        {
            CombatTurnStateMachine machine = CreateStartedStateMachine();

            Assert.That(machine.TryTransition(CombatState.PlayerTurnStart), Is.False);
            Assert.That(machine.TryTransition(CombatState.EnemyTurnStart), Is.True);
            Assert.That(machine.TryTransition(CombatState.EnemyTurnExecuting), Is.True);
            Assert.That(machine.TryTransition(CombatState.EnemyTurnEnd), Is.True);
            Assert.That(machine.TryTransition(CombatState.PlayerTurnStart), Is.True);
            Assert.That(machine.TryTransition(CombatState.PlayerAbilitySelection), Is.True);
            Assert.That(machine.TryTransition(CombatState.PlayerAbilityExecuting), Is.True);
            Assert.That(machine.TryTransition(CombatState.PlayerTurnEnd), Is.True);
            Assert.That(machine.TryTransition(CombatState.EnemyTurnStart), Is.True);
        }

        [Test]
        public void PerformanceCalculator_UsesWeightedAverageAcrossExpectedNotes()
        {
            RhythmJudgementResult[] judgements =
            {
                Result(HitJudgement.Miss), Result(HitJudgement.Bad),
                Result(HitJudgement.Good), Result(HitJudgement.Perfect)
            };

            RhythmPerformanceResult performance = RhythmPerformanceCalculator.Calculate(4, judgements, null);

            Assert.That(performance.AverageWeight, Is.EqualTo(0.575f).Within(0.0001f));
            Assert.That(performance.MissCount, Is.EqualTo(1));
            Assert.That(RhythmPerformanceCalculator.CalculatePower(100, performance), Is.EqualTo(58));
        }

        [Test]
        public void PerformanceCalculator_TreatsUnresolvedNotesAsMisses()
        {
            RhythmPerformanceResult performance = RhythmPerformanceCalculator.Calculate(3,
                new[] { Result(HitJudgement.Perfect) }, null);

            Assert.That(performance.AverageWeight, Is.EqualTo(1f / 3f).Within(0.0001f));
            Assert.That(performance.MissCount, Is.EqualTo(2));
        }

        [Test]
        public void Combatants_ClampDamageAndRestoreDefeatSnapshot()
        {
            GameObject playerObject = new("Player Test");
            GameObject enemyObject = new("Enemy Test");
            try
            {
                PlayerCombatant player = playerObject.AddComponent<PlayerCombatant>();
                EnemyCombatant enemy = enemyObject.AddComponent<EnemyCombatant>();
                player.CaptureBattleStart();
                enemy.CaptureBattleStart();

                player.ApplyDamage(int.MaxValue);
                enemy.ApplyDamage(int.MaxValue);
                Assert.That(player.IsDefeated, Is.True);
                Assert.That(enemy.IsDefeated, Is.True);

                player.RestoreBattleStart();
                enemy.RestoreBattleStart();
                Assert.That(player.CurrentHealth, Is.EqualTo(player.MaxHealth));
                Assert.That(enemy.CurrentHealth, Is.EqualTo(enemy.MaxHealth));
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void DefaultCombatAssets_AreLoadable()
        {
            Assert.That(Resources.Load<JudgementConfig>("Combat/Judgement/JudgementConfig"), Is.Not.Null);
            Assert.That(Resources.Load<AbilityDefinition>("Combat/Abilities/BasicAttack"), Is.Not.Null);
            Assert.That(Resources.Load<EnemyDefinition>("Combat/Enemies/Enemy200"), Is.Not.Null);
            Assert.That(Resources.Load<EnemyDefinition>("Combat/Enemies/Enemy500"), Is.Not.Null);
            Assert.That(Resources.Load<EnemyDefinition>("Combat/Enemies/Enemy1000"), Is.Not.Null);
        }

        [Test]
        public void AbilitySlotView_CreatesMissingRenderersSafely()
        {
            GameObject slotObject = new("Slot Test");
            GameObject staleIcon = new("Ability Icon");
            staleIcon.transform.SetParent(slotObject.transform, false);
            try
            {
                AbilitySlotView view = slotObject.AddComponent<AbilitySlotView>();

                Assert.DoesNotThrow(() => view.Configure(null));
                Assert.That(staleIcon.GetComponent<SpriteRenderer>(), Is.Not.Null);
                Assert.That(slotObject.GetComponentInChildren<LineRenderer>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(slotObject);
            }
        }

        [Test]
        public void EnemyHitReaction_DoesNotRestoreCombatantRootPosition()
        {
            GameObject enemyObject = new("Enemy Root Test");
            enemyObject.transform.localPosition = new Vector3(3f, 4f, 0f);
            try
            {
                enemyObject.AddComponent<SpriteRenderer>();
                EnemyHitReactionView reaction = enemyObject.AddComponent<EnemyHitReactionView>();

                reaction.Play(0.01f, 1f);
                reaction.StopAndRestore();

                Assert.That(enemyObject.transform.localPosition, Is.EqualTo(new Vector3(3f, 4f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
            }
        }

        private static CombatTurnStateMachine CreateStartedStateMachine()
        {
            CombatTurnStateMachine machine = new();
            foreach (CombatState state in (CombatState[])System.Enum.GetValues(typeof(CombatState)))
                machine.Register(new TestState(state));
            machine.Start();
            return machine;
        }

        private static RhythmJudgementResult Result(HitJudgement judgement) =>
            new("note", 1, judgement, 0f, Vector3.zero, NoteResolutionSource.PlayerInput);

        private sealed class TestState : ICombatState
        {
            public CombatState State { get; }
            public TestState(CombatState state) => State = state;
            public void Enter() { }
            public void Exit() { }
            public void Tick() { }
        }
    }
}
