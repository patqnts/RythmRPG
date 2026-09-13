using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RythmRPG.Rhythm;
using UnityEditor;
using UnityEditor.Animations;
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

        [Test]
        public void StationaryLaser_AlignsLaneXAndPreservesSpawnY()
        {
            GameObject laserObject = new("Laser Test");
            GameObject keyObject = new("Key Test");
            try
            {
                LaserNote laser = laserObject.AddComponent<LaserNote>();

                KeyButton key = keyObject.AddComponent<KeyButton>();
                key.keyIdentity = 1;
                key.SetInteractable(true);
                keyObject.transform.position = new Vector3(2f, -1.65f, 0f);
                laserObject.transform.position = new Vector3(-4f, 4.5f, 0f);

                RhythmNoteData data = new("lane", 1d, RhythmNoteType.Laser)
                {
                    TravelTime = 0.01d
                };

                laser.Initialize(new RhythmNoteSpawnContext(null, data, data.Id, 1, 8f, 1, new[] { key }));

                Assert.That(laser.transform.position.x, Is.EqualTo(key.transform.position.x));
                Assert.That(laser.transform.position.y, Is.EqualTo(4.5f));
                Assert.That(laser.ShouldAutoMissByPosition, Is.False);
                Assert.That(laser.CanReceiveHit(key), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(laserObject);
                Object.DestroyImmediate(keyObject);
            }
        }

        [Test]
        public void StationaryLaser_UsesPerNoteTimingWindows()
        {
            GameObject laserObject = new("Laser Override Test");
            GameObject keyObject = new("Key Override Test");
            try
            {
                LaserNote laser = laserObject.AddComponent<LaserNote>();

                KeyButton key = keyObject.AddComponent<KeyButton>();
                key.keyIdentity = 1;
                key.SetInteractable(true);
                keyObject.transform.position = new Vector3(0f, -1.65f, 0f);

                RhythmNoteData data = new("lane", 1d, RhythmNoteType.Laser)
                {
                    TravelTime = 0.01d,
                    StationaryBadWindow = 0.25f,
                    StationaryGoodWindow = 0.1f,
                    StationaryPerfectWindow = 0.02f
                };

                laser.Initialize(new RhythmNoteSpawnContext(null, data, data.Id, 1, 8f, 1, new[] { key }));

                SetStationarySecondsUntilAnticipationEnd(laser, 0.03f);
                Assert.That(laser.AdjustJudgement(HitJudgement.Miss, 0.03f), Is.EqualTo(HitJudgement.Good));
                SetStationarySecondsUntilAnticipationEnd(laser, 0.2f);
                Assert.That(laser.AdjustJudgement(HitJudgement.Miss, 0.2f), Is.EqualTo(HitJudgement.Bad));
                SetStationarySecondsUntilAnticipationEnd(laser, 0.3f);
                Assert.That(laser.AdjustJudgement(HitJudgement.Bad, 0.3f), Is.EqualTo(HitJudgement.Miss));
            }
            finally
            {
                Object.DestroyImmediate(laserObject);
                Object.DestroyImmediate(keyObject);
            }
        }

        [Test]
        public void StationaryLaser_VeryEarlyInputDuringChargeIsMiss()
        {
            GameObject laserObject = new("Laser Charge Test");
            GameObject keyObject = new("Key Charge Test");
            try
            {
                LaserNote laser = laserObject.AddComponent<LaserNote>();

                KeyButton key = keyObject.AddComponent<KeyButton>();
                key.keyIdentity = 1;
                key.SetInteractable(true);

                RhythmNoteData data = new("lane", 1d, RhythmNoteType.Laser)
                {
                    TravelTime = 10d,
                    StationaryBadWindow = 0.25f,
                    StationaryGoodWindow = 0.1f,
                    StationaryPerfectWindow = 0.02f
                };

                laser.Initialize(new RhythmNoteSpawnContext(null, data, data.Id, 1, 8f, 1, new[] { key }));

                Assert.That(laser.CanReceiveHit(key), Is.True);
                Assert.That(laser.AdjustJudgement(HitJudgement.Perfect, laser.GetTimingError(key)), Is.EqualTo(HitJudgement.Miss));
            }
            finally
            {
                Object.DestroyImmediate(laserObject);
                Object.DestroyImmediate(keyObject);
            }
        }

        [Test]
        public void StationaryLaser_BindsExistingChargeVisualNames()
        {
            GameObject laserObject = new("Laser Visual Test");
            GameObject anticipator = new("Anticipator");
            GameObject fill = new("FIll");
            GameObject keyObject = new("Key Visual Test");
            try
            {
                anticipator.transform.SetParent(laserObject.transform, false);
                fill.transform.SetParent(anticipator.transform, false);

                LaserNote laser = laserObject.AddComponent<LaserNote>();
                KeyButton key = keyObject.AddComponent<KeyButton>();
                key.keyIdentity = 1;
                key.SetInteractable(true);

                RhythmNoteData data = new("lane", 1d, RhythmNoteType.Laser)
                {
                    TravelTime = 10d
                };

                laser.Initialize(new RhythmNoteSpawnContext(null, data, data.Id, 1, 8f, 1, new[] { key }));

                Assert.That(anticipator.activeSelf, Is.True);
                Assert.That(fill.activeSelf, Is.True);
                Assert.That(fill.transform.localScale.x, Is.LessThan(0.3f));
            }
            finally
            {
                Object.DestroyImmediate(laserObject);
                Object.DestroyImmediate(keyObject);
            }
        }

        [Test]
        public void HoldLaserAnimator_HasPlayableEndState()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Animation/Hold Laser/hold laser_0.controller");

            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.layers[0].stateMachine.states.Any(state => state.state.name == "End"), Is.True);
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

        private static void SetStationarySecondsUntilAnticipationEnd(StationaryNote note, float secondsUntilEnd)
        {
            const float anticipationDuration = 1f;
            typeof(StationaryNote).GetField("anticipationDuration", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(note, anticipationDuration);
            typeof(StationaryNote).GetField("spawnedAtTime", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(note, Time.time - anticipationDuration + secondsUntilEnd);
        }

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
