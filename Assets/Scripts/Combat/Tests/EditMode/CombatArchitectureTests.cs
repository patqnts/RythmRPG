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
        public void LaneTarget_MeasuresTimingAlongThe3DTravelAxis()
        {
            GameObject targetObject = new("Lane Target Test");
            try
            {
                RhythmLaneTarget target = targetObject.AddComponent<RhythmLaneTarget>();
                target.Configure(3, new Vector3(0f, -1f, -1f));
                Vector3 travel = target.WorldTravelDirection;
                Vector3 lateral = Vector3.Cross(travel, Vector3.right).normalized;

                Assert.That(target.LaneId, Is.EqualTo(3));
                Assert.That(target.GetTimingDistance(target.transform.position + lateral * 10f), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(target.GetTimingDistance(target.transform.position + travel * 0.25f), Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(target.GetSignedProgressPastLine(target.transform.position + travel), Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void HorizontalCombatGeometry_KeepsLineNotesAndPlayerOnExpectedPlanes()
        {
            float height = HorizontalCombatGeometry.ResolveGameplayHeight(1.68f, 1.72f, 0.18f);
            Vector3 note = HorizontalCombatGeometry.AtHeight(new Vector3(4f, -10f, 8f), height);
            Vector3 player = HorizontalCombatGeometry.PositionBehindLine(
                new Vector3(0f, height, 3f), Vector3.forward, 0.45f, 1f);

            Assert.That(height, Is.EqualTo(1.9f).Within(0.0001f));
            Assert.That(note.y, Is.EqualTo(height));
            Assert.That(player, Is.EqualTo(new Vector3(0f, 1f, 2.55f)));
        }

        [Test]
        public void NoteSprites_FaceCameraWithoutRotatingMovementOrParticles()
        {
            GameObject root = new("Mixed Note", typeof(SpriteRenderer));
            GameObject cameraObject = new("Note Camera", typeof(Camera));
            GameObject particles = new("3D Visual");
            try
            {
                particles.transform.SetParent(root.transform);
                particles.transform.localRotation = Quaternion.Euler(12f, 23f, 34f);
                Quaternion particleRotation = particles.transform.rotation;
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
                SpriteRenderer source = root.GetComponent<SpriteRenderer>();
                RhythmNoteVisualLayer layer = root.AddComponent<RhythmNoteVisualLayer>();
                layer.FaceSpritesToCamera(camera);
                SpriteRenderer display = root.GetComponentsInChildren<SpriteRenderer>().First(r => r != source);
                Assert.That(Quaternion.Angle(root.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(particles.transform.rotation, particleRotation), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(display.transform.rotation, camera.transform.rotation), Is.LessThan(0.001f));
                source.color = Color.red;
                source.flipX = true;
                camera.transform.rotation = Quaternion.Euler(45f, 10f, 0f);
                typeof(RhythmNoteVisualLayer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(layer, null);
                Assert.That(display.color, Is.EqualTo(Color.red));
                Assert.That(display.flipX, Is.True);
                Assert.That(Quaternion.Angle(display.transform.rotation, camera.transform.rotation), Is.LessThan(0.001f));
                layer.enabled = false;
                // Runtime MonoBehaviour callbacks are not dispatched by EditMode tests.
                typeof(RhythmNoteVisualLayer).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(layer, null);
                Assert.That(source.forceRenderingOff, Is.False);
                Assert.That(display.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void PlayerFeet_StayBelowHitLineWhenOrthographicCameraMoves()
        {
            GameObject cameraObject = new("Placement Camera");
            GameObject presentationObject = new("Placement Lanes");
            GameObject targetObject = new("Placement Target");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
                CombatLanePresentation3D presentation = presentationObject.AddComponent<CombatLanePresentation3D>();
                typeof(CombatLanePresentation3D).GetField("worldCamera", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(presentation, camera);
                typeof(CombatLanePresentation3D).GetField("gameplayHeight", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(presentation, 0.18f);
                RhythmLaneTarget target = targetObject.AddComponent<RhythmLaneTarget>();
                ((List<RhythmLaneTarget>)typeof(CombatLanePresentation3D).GetField("targets",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presentation)).Add(target);
                foreach (Vector3 cameraPosition in new[] { new Vector3(0f, 7f, -9f), new Vector3(4f, 9f, -3f) })
                {
                    camera.transform.position = cameraPosition;
                    Ray lineRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.25f, 0f));
                    Plane plane = new(Vector3.up, Vector3.up * 0.18f);
                    Assert.That(plane.Raycast(lineRay, out float distance), Is.True);
                    target.transform.position = lineRay.GetPoint(distance);
                    Assert.That(presentation.TryGetPlayerPosition(0.3f, -0.3f, 0.45f, out Vector3 player), Is.True);
                    Vector3 feetViewport = camera.WorldToViewportPoint(player - Vector3.up * 0.3f);
                    Assert.That(feetViewport.x, Is.EqualTo(0.5f).Within(0.001f));
                    Assert.That(feetViewport.y, Is.EqualTo(presentation.PlayerFeetViewportY).Within(0.001f));
                    Assert.That(feetViewport.y, Is.LessThan(0.25f));
                    Assert.That(player.y, Is.EqualTo(0.3f).Within(0.001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(presentationObject);
                Object.DestroyImmediate(cameraObject);
            }
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

        [Test]
        public void AbilityPatternSpawnOrigin_ClampsAboveLaneButtons()
        {
            GameObject vfxObject = new("Combat VFX Test");
            GameObject inputObject = new("Input Test");
            GameObject keyObject = new("Center Key Test");
            try
            {
                CombatVFXController vfx = vfxObject.AddComponent<CombatVFXController>();
                LaneInputRouter input = inputObject.AddComponent<LaneInputRouter>();
                KeyButton key = keyObject.AddComponent<KeyButton>();
                key.keyIdentity = 3;
                keyObject.transform.position = new Vector3(0f, 5f, 0f);
                input.ConfigureForTests(new[] { new LaneKeyBinding(3, KeyCode.D, key) });

                vfx.Bind(null, input, null, null);

                Assert.That(vfx.AbilityPatternSpawnOrigin.position.y, Is.GreaterThan(keyObject.transform.position.y));
            }
            finally
            {
                Object.DestroyImmediate(vfxObject);
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(keyObject);
            }
        }

        [Test]
        public void AbilitySelectionCenter_UsesScreenCenterEvenWhenAbilityOriginIsLow()
        {
            GameObject vfxObject = new("Combat VFX Selection Test");
            GameObject lowOrigin = new("Low Ability Origin Test");
            GameObject cameraObject = new("Selection Camera Test");
            GameObject inputObject = new("Selection Input Test");
            GameObject keyObject = new("Selection Center Key Test");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);

                lowOrigin.transform.position = new Vector3(0f, -20f, 0f);
                CombatVFXController vfx = vfxObject.AddComponent<CombatVFXController>();
                typeof(CombatVFXController).GetField("abilityCenter", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(vfx, lowOrigin.transform);
                LaneInputRouter input = inputObject.AddComponent<LaneInputRouter>();
                KeyButton key = keyObject.AddComponent<KeyButton>();
                key.keyIdentity = 3;
                keyObject.transform.position = new Vector3(0f, 5f, 0f);
                input.ConfigureForTests(new[] { new LaneKeyBinding(3, KeyCode.D, key) });
                vfx.Bind(null, input, null, null);

                Assert.That(vfx.AbilitySelectionCenter.position.y, Is.GreaterThan(keyObject.transform.position.y));
            }
            finally
            {
                Object.DestroyImmediate(vfxObject);
                Object.DestroyImmediate(lowOrigin);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(keyObject);
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
