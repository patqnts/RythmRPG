using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.Rhythm.Editing;

namespace RythmRPG.Rhythm.Tests
{
    public sealed class PatternTests
    {
        private static readonly List<string> Lanes = new List<string> { "L1", "L2", "L3", "L4", "L5" };

        private static ChartSession NewSession()
        {
            var s = new ChartSession(new TempoMap(120d, 4), Lanes);
            s.SnapEnabled = false;
            return s;
        }

        private static PatternInstance Make(string template, double start, double duration, int seed = 7)
        {
            return new PatternInstance { Id = "p1", TemplateId = template, StartBeat = start, DurationBeats = duration, Seed = seed };
        }

        [Test]
        public void Expand_IsDeterministicForSameSeed()
        {
            List<NoteInstance> a = PatternExpander.Expand(Make("stream", 8d, 4d, 42), Lanes);
            List<NoteInstance> b = PatternExpander.Expand(Make("stream", 8d, 4d, 42), Lanes);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].LaneId, b[i].LaneId);
                Assert.AreEqual(a[i].HitBeat, b[i].HitBeat, 1e-9);
                Assert.AreEqual(a[i].Id, b[i].Id);
            }
        }

        [Test]
        public void Expand_DifferentSeedsDiffer()
        {
            List<NoteInstance> a = PatternExpander.Expand(Make("stream", 0d, 8d, 1), Lanes);
            List<NoteInstance> b = PatternExpander.Expand(Make("stream", 0d, 8d, 2), Lanes);
            bool differs = false;
            for (int i = 0; i < a.Count && i < b.Count; i++) if (a[i].LaneId != b[i].LaneId) differs = true;
            Assert.IsTrue(differs);
        }

        [Test]
        public void Expand_NotesStayInsideTheWindowAndTagged()
        {
            PatternInstance p = Make("barrage", 8d, 4d);
            List<NoteInstance> notes = PatternExpander.Expand(p, Lanes);
            Assert.AreEqual(8, notes.Count); // 4 steps x 2 lanes
            for (int i = 0; i < notes.Count; i++)
            {
                Assert.IsTrue(notes[i].HitBeat >= 8d - 1e-9 && notes[i].HitBeat < 12d - 1e-9);
                Assert.AreEqual("pattern", notes[i].Metadata[0].Key);
                Assert.AreEqual("p1", notes[i].Metadata[0].Value);
            }
        }

        [Test]
        public void Expand_RespectsLaneMask()
        {
            PatternInstance p = Make("stairs", 0d, 4d);
            p.LaneMask = (1 << 1) | (1 << 3); // lanes 2 and 4
            List<NoteInstance> notes = PatternExpander.Expand(p, Lanes);
            Assert.AreEqual(8, notes.Count);
            for (int i = 0; i < notes.Count; i++) Assert.IsTrue(notes[i].LaneId == "L2" || notes[i].LaneId == "L4");
            Assert.AreEqual("L2", notes[0].LaneId);
            Assert.AreEqual("L4", notes[1].LaneId);
        }

        [Test]
        public void Sweep_PingPongsAcrossLanes()
        {
            List<NoteInstance> notes = PatternExpander.Expand(Make("sweep", 0d, 5d), Lanes); // 10 steps, period 8
            string[] expected = { "L1", "L2", "L3", "L4", "L5", "L4", "L3", "L2", "L1", "L2" };
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], notes[i].LaneId);
        }

        [Test]
        public void Expand_UnknownTemplateIsEmpty()
        {
            Assert.AreEqual(0, PatternExpander.Expand(Make("nope", 0d, 4d), Lanes).Count);
        }

        [Test]
        public void Session_AddMoveResizeDeleteUndo()
        {
            ChartSession s = NewSession();
            PatternInstance p = s.AddPattern("sweep", 4d);
            Assert.AreEqual(1, s.Patterns.Count);
            Assert.AreEqual(4d, p.StartBeat, 1e-9);
            s.MovePattern(p.Id, 2d);
            Assert.AreEqual(6d, s.FindPattern(p.Id).StartBeat, 1e-9);
            s.ResizePattern(p.Id, 8d);
            Assert.AreEqual(8d, s.FindPattern(p.Id).DurationBeats, 1e-9);
            s.Undo();
            Assert.AreEqual(4d, s.FindPattern(p.Id).DurationBeats, 1e-9);
            s.Undo();
            Assert.AreEqual(4d, s.FindPattern(p.Id).StartBeat, 1e-9);
            s.SelectOnly(p.Id);
            s.DeleteSelected();
            Assert.AreEqual(0, s.Patterns.Count);
            s.Undo();
            Assert.AreEqual(1, s.Patterns.Count);
            s.Redo();
            Assert.AreEqual(0, s.Patterns.Count);
        }

        [Test]
        public void Session_MoveClampsAtZero_AndUnknownTemplateRefused()
        {
            ChartSession s = NewSession();
            PatternInstance p = s.AddPattern("stairs", 1d);
            s.MovePattern(p.Id, -5d);
            Assert.AreEqual(0d, s.FindPattern(p.Id).StartBeat, 1e-9);
            Assert.IsTrue(s.AddPattern("missing", 0d) == null);
        }

        [Test]
        public void Selection_CanHoldAPattern()
        {
            ChartSession s = NewSession();
            PatternInstance p = s.AddPattern("sweep", 0d);
            s.ClearSelection();
            s.SelectOnly(p.Id);
            Assert.IsTrue(s.SelectedPattern != null);
            Assert.AreEqual(p.Id, s.SelectedPattern.Id);
        }

        [Test]
        public void Reservation_NoteInsideExclusivePatternIsConflict()
        {
            ChartSession s = NewSession();
            PatternInstance p = s.AddPattern("sweep", 8d); // 8..12, all lanes
            s.AddNote("L2", 9d, NoteMigration.DefinitionIdNormal);
            s.AddNote("L2", 12d, NoteMigration.DefinitionIdNormal); // at the end: outside
            s.AddNote("L2", 4d, NoteMigration.DefinitionIdNormal);  // before
            List<ValidationIssue> issues = ChartValidator.Validate(s);
            int reserved = 0;
            for (int i = 0; i < issues.Count; i++) if (issues[i].Code == "reserved-lane") { reserved++; Assert.AreEqual(p.Id, issues[i].PatternId); }
            Assert.AreEqual(1, reserved);
        }

        [Test]
        public void Reservation_OtherLanesAndNonExclusiveAreFine()
        {
            ChartSession s = NewSession();
            PatternInstance p = s.AddPattern("sweep", 8d);
            s.ModifyPattern(p.Id, "mask", c => c.LaneMask = 1); // lane 1 only
            s.AddNote("L3", 9d, NoteMigration.DefinitionIdNormal);
            Assert.AreEqual(0, ChartValidator.Validate(s).FindAll(i => i.Code == "reserved-lane").Count);
            s.ModifyPattern(p.Id, "mask", c => c.LaneMask = 0);
            s.ModifyPattern(p.Id, "mode", c => c.Reservation = ReservationMode.None);
            Assert.AreEqual(0, ChartValidator.Validate(s).FindAll(i => i.Code == "reserved-lane").Count);
        }

        [Test]
        public void Reservation_OverlappingExclusivePatternsConflict_DisjointLanesDoNot()
        {
            ChartSession s = NewSession();
            PatternInstance a = s.AddPattern("sweep", 0d);
            PatternInstance b = s.AddPattern("stairs", 2d);
            Assert.AreEqual(1, ChartValidator.Validate(s).FindAll(i => i.Code == "pattern-overlap").Count);
            s.ModifyPattern(a.Id, "mask", c => c.LaneMask = 1 | 2);
            s.ModifyPattern(b.Id, "mask", c => c.LaneMask = 4 | 8);
            Assert.AreEqual(0, ChartValidator.Validate(s).FindAll(i => i.Code == "pattern-overlap").Count);
        }

        [Test]
        public void Bake_ReplacesPatternWithEditableNotes_AndUndoRestores()
        {
            ChartSession s = NewSession();
            PatternInstance p = s.AddPattern("barrage", 0d); // 4 steps x 2 lanes
            int expected = PatternExpander.Expand(p, Lanes).Count;
            Assert.AreEqual(expected, s.BakePattern(p.Id));
            Assert.AreEqual(0, s.Patterns.Count);
            Assert.AreEqual(expected, s.Notes.Count);
            Assert.AreEqual(0, s.Notes[0].Metadata.Count);
            Assert.AreEqual(expected, s.SelectionCount);
            s.Undo();
            Assert.AreEqual(1, s.Patterns.Count);
            Assert.AreEqual(0, s.Notes.Count);
        }

        [Test]
        public void Geometry_HitTestPatternBodyAndEdge()
        {
            ChartSession s = NewSession();
            PatternInstance p = s.AddPattern("sweep", 2d); // beats 2..6
            var geo = new TimelineGeometry { LaneCount = 5 };
            float y = geo.PatternTrackTop + 5f;
            float x0 = geo.BeatToX(2d), x1 = geo.BeatToX(6d);
            Assert.AreEqual(PatternHitKind.Body, geo.HitTestPattern(s, (x0 + x1) / 2f, y).Kind);
            Assert.AreEqual(PatternHitKind.RightEdge, geo.HitTestPattern(s, x1 - 2f, y).Kind);
            Assert.AreEqual(PatternHitKind.None, geo.HitTestPattern(s, x1 + 40f, y).Kind);
            Assert.AreEqual(PatternHitKind.None, geo.HitTestPattern(s, (x0 + x1) / 2f, 5f).Kind);
        }
    }
}
