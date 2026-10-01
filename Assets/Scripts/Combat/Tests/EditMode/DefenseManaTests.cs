using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class DefenseManaTests
    {
        private GameObject root;
        private PlayerCombatant player;
        private CombatController controller;
        private RhythmPatternRunner runner;
        private CombatResourceRules rules;
        private CombatTurnStateMachine states;

        [SetUp]
        public void SetUp()
        {
            // Keep the controller inactive: exercise resource handling without creating scene presentation services.
            root = new GameObject("Defence Mana Test");
            root.SetActive(false);
            player = root.AddComponent<PlayerCombatant>();
            player.ResetToMaximum();
            player.SpendMana(player.CurrentMana - 10);
            var enemy = root.AddComponent<EnemyCombatant>();
            runner = root.AddComponent<RhythmPatternRunner>();
            controller = root.AddComponent<CombatController>();
            rules = ScriptableObject.CreateInstance<CombatResourceRules>();
            states = new CombatTurnStateMachine();
            states.Start();
            states.TryTransition(CombatState.EnemyTurnStart);
            states.TryTransition(CombatState.EnemyTurnExecuting);
            SetField(controller, "stateMachine", states);
            SetField(controller, "resourceRules", rules);
            SetField(controller, "encounter", new CombatEncounterContext(player, enemy));
            SetField(controller, "runner", runner);
            SetProperty(controller, "IsBattleActive", true);
            SetMode(PatternRunMode.EnemyDefense);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(rules);
        }

        [TestCase(ManaSource.TurnAccuracy)]
        [TestCase(ManaSource.PerHit)]
        public void EveryBadDefenceHit_LosesOneManaImmediately(ManaSource source)
        {
            SetField(rules, "manaSource", source);
            SetField(rules, "badMana", 10f); // Legacy recovery must not cancel a Bad defence penalty.
            for (int i = 1; i <= 3; i++)
            {
                Resolve(HitJudgement.Bad, NoteResolutionSource.PlayerInput, "bad" + i);
                Assert.That(player.CurrentMana, Is.EqualTo(10 - i));
            }
            if (source == ManaSource.TurnAccuracy)
            {
                Assert.That(controller.TurnAccuracy, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(controller.PendingMana, Is.EqualTo(5));
                Invoke("PayTurnAccuracyMana");
                Assert.That(player.CurrentMana, Is.EqualTo(12), "Recovery remains a separate end-of-turn payout.");
                Assert.That(controller.PendingMana, Is.Zero);
            }
        }

        [TestCase(HitJudgement.Perfect)]
        [TestCase(HitJudgement.Good)]
        [TestCase(HitJudgement.Miss)]
        public void OtherJudgements_DoNotSpendMana(HitJudgement judgement)
        {
            Resolve(judgement);
            Assert.That(player.CurrentMana, Is.EqualTo(10));
        }

        [Test]
        public void PerHitRecovery_StillAccumulatesPerfectAndGoodHits()
        {
            SetField(rules, "manaSource", ManaSource.PerHit);
            Resolve(HitJudgement.Good);
            Resolve(HitJudgement.Bad);
            Assert.That(player.CurrentMana, Is.EqualTo(9), "A Bad hit drains immediately despite fractional recovery.");
            Resolve(HitJudgement.Good);
            Resolve(HitJudgement.Perfect);
            Assert.That(player.CurrentMana, Is.EqualTo(11));
        }

        [TestCase(ManaSource.TurnAccuracy, NoteResolutionSource.Modifier)]
        [TestCase(ManaSource.TurnAccuracy, NoteResolutionSource.SystemClear)]
        [TestCase(ManaSource.TurnAccuracy, NoteResolutionSource.Timeout)]
        [TestCase(ManaSource.PerHit, NoteResolutionSource.Modifier)]
        [TestCase(ManaSource.PerHit, NoteResolutionSource.SystemClear)]
        [TestCase(ManaSource.PerHit, NoteResolutionSource.Timeout)]
        public void NonPlayerResolutions_DoNotSpendMana(ManaSource source, NoteResolutionSource resolution)
        {
            SetField(rules, "manaSource", source);
            Resolve(HitJudgement.Bad, resolution);
            Assert.That(player.CurrentMana, Is.EqualTo(10));
        }

        [TestCase(ManaSource.TurnAccuracy)]
        [TestCase(ManaSource.PerHit)]
        public void AbilityCharts_DoNotApplyDefencePenalty(ManaSource source)
        {
            SetField(rules, "manaSource", source);
            states.TryTransition(CombatState.EnemyTurnEnd);
            states.TryTransition(CombatState.PlayerTurnStart);
            states.TryTransition(CombatState.PlayerAbilitySelection);
            states.TryTransition(CombatState.PlayerAbilityExecuting);
            SetMode(PatternRunMode.PlayerAbility);
            Resolve(HitJudgement.Bad);
            Assert.That(player.CurrentMana, Is.EqualTo(10));
            Assert.That(controller.PendingMana, Is.Zero);
        }

        [Test]
        public void AbilityChartModeDuringEnemyTurn_DoesNotApplyDefencePenalty()
        {
            SetMode(PatternRunMode.PlayerAbility);
            Resolve(HitJudgement.Bad);
            Assert.That(player.CurrentMana, Is.EqualTo(10));
            Assert.That(controller.PendingMana, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PreviewOrInactiveBattle_DoNotSpendMana(bool preview)
        {
            SetProperty(controller, "IsPreviewing", preview);
            SetProperty(controller, "IsBattleActive", preview);
            Resolve(HitJudgement.Bad);
            Assert.That(player.CurrentMana, Is.EqualTo(10));
            Assert.That(controller.PendingMana, Is.Zero);
        }

        [TestCase(3, 7)]
        [TestCase(0, 10)]
        [TestCase(-3, 10)]
        public void BadCost_IsConfigurableAndNonNegative(int cost, int expectedMana)
        {
            SetField(rules, "badDefenseManaCost", cost);
            Resolve(HitJudgement.Bad);
            Assert.That(player.CurrentMana, Is.EqualTo(expectedMana));
        }

        [Test]
        public void InsufficientMana_DrainsRemainingManaAndReportsOnlyActualLoss()
        {
            player.SpendMana(9);
            SetField(rules, "badDefenseManaCost", 5);
            var stats = new CombatStatsTracker(null);
            stats.Begin(player, null);
            int changes = 0;
            player.ManaChanged += (_, _) => changes++;
            try
            {
                Resolve(HitJudgement.Bad);
                Resolve(HitJudgement.Bad);
                Assert.That(player.CurrentMana, Is.Zero);
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(stats.Report.ManaSpent, Is.EqualTo(1));
            }
            finally { stats.Stop(); }
        }

        [Test]
        public void AbilitySpend_StillRequiresFullCost_AndDrainReturnsActualLoss()
        {
            Assert.That(player.SpendMana(11), Is.False);
            Assert.That(player.CurrentMana, Is.EqualTo(10));
            Assert.That(player.DrainMana(-1), Is.Zero);
            Assert.That(player.DrainMana(11), Is.EqualTo(10));
            Assert.That(player.CurrentMana, Is.Zero);
        }

        private void SetMode(PatternRunMode mode) =>
            SetField(runner, "currentContext", new PatternRunContext(mode, player));

        private void Resolve(HitJudgement judgement, NoteResolutionSource source = NoteResolutionSource.PlayerInput,
            string id = "note") => Invoke("HandleManaJudgement", new RhythmJudgementResult(id, 0, judgement, 0f, Vector3.zero, source));

        private void Invoke(string method, params object[] args) =>
            typeof(CombatController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, args);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
    }
}
