using System;
using System.Linq;
using NUnit.Framework;
using RythmRPG.Rhythm.Editor;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Tests
{
    public sealed class RhythmComposerEditModeTests
    {
        private RhythmChart chart;

        [SetUp]
        public void SetUp()
        {
            chart = ScriptableObject.CreateInstance<RhythmChart>();
            RhythmComposerAssetFactory.PopulateDefaults(chart, false);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            if (chart != null)
            {
                UnityEngine.Object.DestroyImmediate(chart);
            }
        }

        [Test]
        public void BeatAndSnapMath_UsesFractionsOfOneBpmBeat()
        {
            Assert.That(RhythmTimingUtility.GetSecondsPerBeat(120f), Is.EqualTo(0.5d).Within(0.000001d));
            Assert.That(RhythmTimingUtility.GetSnapInterval(120f, RhythmSnapDivision.QuarterBeat), Is.EqualTo(0.125d).Within(0.000001d));
            Assert.That(RhythmTimingUtility.SnapTime(0.19d, 120f, RhythmSnapDivision.QuarterBeat), Is.EqualTo(0.25d).Within(0.000001d));
            Assert.That(RhythmTimingUtility.IsMeasureBeat(8, 4), Is.True);
            Assert.That(RhythmTimingUtility.IsMeasureBeat(9, 4), Is.False);
        }

        [Test]
        public void TimelineCoordinates_RoundTripWithZoomAndScroll()
        {
            Rect canvas = new Rect(150f, 20f, 800f, 300f);
            const float zoom = 137f;
            const double scroll = 3.25d;
            const double time = 7.75d;
            float x = RhythmTimelineGeometry.TimeToX(time, canvas, zoom, scroll);

            Assert.That(RhythmTimelineGeometry.XToTime(x, canvas, zoom, scroll), Is.EqualTo(time).Within(0.00001d));
        }

        [Test]
        public void ExplicitTravelTime_DerivesUnclampedSpawnTime()
        {
            RhythmNoteData note = new RhythmNoteData(chart.Lanes[0].Id, 1d, RhythmNoteType.Normal)
            {
                TravelTime = 2.5d,
                Speed = 8f
            };

            Assert.That(note.SpawnTime, Is.EqualTo(-1.5d).Within(0.000001d));
        }

        [Test]
        public void PlaybackStartTime_IncludesNegativeSpawnPreRoll()
        {
            chart.Notes.Add(new RhythmNoteData(chart.Lanes[0].Id, 2d, RhythmNoteType.Normal)
            {
                TravelTime = 3.25d
            });
            chart.Notes.Add(new RhythmNoteData(chart.Lanes[1].Id, 1d, RhythmNoteType.Normal)
            {
                TravelTime = 1.5d
            });

            Assert.That(RhythmTimingUtility.GetPlaybackStartTime(chart), Is.EqualTo(-1.25d).Within(0.000001d));
        }

        [Test]
        public void PlaybackStartTime_DoesNotDelayChartsWithoutPreRoll()
        {
            chart.Notes.Add(new RhythmNoteData(chart.Lanes[0].Id, 4d, RhythmNoteType.Normal)
            {
                TravelTime = 1d
            });

            Assert.That(RhythmTimingUtility.GetPlaybackStartTime(chart), Is.Zero);
        }

        [Test]
        public void HoldDuration_ConvertsToLegacyLength()
        {
            RhythmNoteData note = new RhythmNoteData(chart.Lanes[0].Id, 2d, RhythmNoteType.Hold)
            {
                HoldDuration = 1.25d,
                Speed = 8f
            };

            Assert.That(note.LegacyHoldLength, Is.EqualTo(10f).Within(0.00001f));
        }

        [Test]
        public void LaneIdsRemainStableAndResolvable()
        {
            RhythmLaneData lane = chart.Lanes[2];
            string laneId = lane.Id;
            chart.Lanes.Reverse();

            Assert.That(chart.FindLane(laneId), Is.SameAs(lane));
            Assert.That(lane.Id, Is.EqualTo(laneId));
        }

        [Test]
        public void PrefabOverride_TakesPrecedenceOverTypeDefault()
        {
            GameObject defaultPrefab = new GameObject("Default");
            GameObject overridePrefab = new GameObject("Override");
            try
            {
                RhythmNoteData note = new RhythmNoteData(chart.Lanes[0].Id, 1d, RhythmNoteType.Normal);
                chart.FindDefinition(RhythmNoteType.Normal).DefaultPrefab = defaultPrefab;
                Assert.That(chart.ResolvePrefab(note), Is.SameAs(defaultPrefab));

                note.PrefabOverride = overridePrefab;
                Assert.That(chart.ResolvePrefab(note), Is.SameAs(overridePrefab));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(defaultPrefab);
                UnityEngine.Object.DestroyImmediate(overridePrefab);
            }
        }

        [Test]
        public void EffectiveDuration_UsesManualAudioAndFinalNoteEnd()
        {
            AudioClip clip = AudioClip.Create("Test", 5 * 44100, 1, 44100, false);
            try
            {
                chart.CompositionDuration = 2d;
                chart.AudioClip = clip;
                RhythmNoteData note = new RhythmNoteData(chart.Lanes[0].Id, 6d, RhythmNoteType.Hold) { HoldDuration = 2d };
                chart.Notes.Add(note);

                Assert.That(chart.EffectiveDuration, Is.EqualTo(8d).Within(0.000001d));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void MutationService_AddsSnapsExtendsAndDeletes()
        {
            chart.CompositionDuration = 0.5d;
            chart.SnapEnabled = true;
            chart.SnapDivision = RhythmSnapDivision.QuarterBeat;
            RhythmNoteData note = RhythmComposerMutationService.AddNote(chart, chart.Lanes[1], 1.19d, RhythmNoteType.Hold);

            Assert.That(note.HitTime, Is.EqualTo(1.19d).Within(0.000001d));
            Assert.That(note.HoldDuration, Is.EqualTo(0.5d).Within(0.000001d));
            Assert.That(chart.CompositionDuration, Is.GreaterThanOrEqualTo(note.EndTime));
            Assert.That(RhythmComposerMutationService.Snap(chart, 1.19d), Is.EqualTo(1.25d).Within(0.000001d));
            Assert.That(RhythmComposerMutationService.DeleteNote(chart, note.Id), Is.True);
            Assert.That(chart.Notes, Is.Empty);
        }

        [Test]
        public void ValidatorReportsUnknownLaneAndNegativeSpawn()
        {
            RhythmNoteData note = new RhythmNoteData("missing", 0.5d, RhythmNoteType.Normal)
            {
                Speed = 8f,
                TravelTime = 1d
            };
            chart.Notes.Add(note);

            RhythmValidationIssue[] issues = RhythmChartValidator.Validate(chart).ToArray();
            Assert.That(issues.Any(issue => issue.Severity == RhythmValidationSeverity.Error && issue.NoteId == note.Id), Is.True);
            Assert.That(issues.Any(issue => issue.Severity == RhythmValidationSeverity.Warning && issue.NoteId == note.Id && issue.Message.Contains("before composition")), Is.True);
        }

        [Test]
        public void DefaultChart_HasFiveLanesAndAllSevenTypeDefinitions()
        {
            Assert.That(chart.Bpm, Is.EqualTo(120f));
            Assert.That(chart.BeatsPerMeasure, Is.EqualTo(4));
            Assert.That(chart.Lanes.Count, Is.EqualTo(5));
            Assert.That(chart.Lanes.Select(lane => lane.KeyIdentity), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(chart.NoteDefinitions.Select(definition => definition.NoteType).Distinct().Count(), Is.EqualTo(7));
        }

        [Test]
        public void ExistingPrefabMappingsResolveWhenAssetsAreAvailable()
        {
            RhythmComposerAssetFactory.PopulateDefaults(chart, true);
            Assert.That(chart.NoteDefinitions.All(definition => definition.DefaultPrefab != null), Is.True);
        }

        [Test]
        public void HoldParticleTail_UsesMatchingVelocityCurveModes()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/HoldNotePixelFireTail.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Component tailVisual = instance.GetComponents<Component>()
                    .FirstOrDefault(component => component != null && component.GetType().Name == "HoldNoteParticleTailVisual");
                Assert.That(tailVisual, Is.Not.Null);

                tailVisual.GetType().GetMethod("Initialize")?.Invoke(tailVisual, new object[] { 4f, 8f });

                ParticleSystem.VelocityOverLifetimeModule velocity = instance.GetComponent<ParticleSystem>().velocityOverLifetime;
                Assert.That(velocity.x.mode, Is.EqualTo(velocity.y.mode));
                Assert.That(velocity.y.mode, Is.EqualTo(velocity.z.mode));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
