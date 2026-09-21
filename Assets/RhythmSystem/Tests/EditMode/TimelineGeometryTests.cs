using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.Rhythm.Editing;

namespace RythmRPG.Rhythm.Tests
{
    public class TimelineGeometryTests
    {
        [Test]
        public void BeatXRoundTrip_AndZoomKeepsCursorBeat()
        {
            var g = new TimelineGeometry { LaneCount = 3 };
            Assert.AreEqual(3.3d, g.XToBeat(g.BeatToX(3.3d)), 1e-4);
            g.ZoomAbout(2d, 300f);
            Assert.AreEqual(300f, g.BeatToX(g.XToBeat(300f)), 0.01f);
        }

        [Test]
        public void LaneMapping_ClampsWhenAsked()
        {
            var g = new TimelineGeometry { LaneCount = 3 };
            Assert.AreEqual(1, g.YToLane(g.LaneCenter(1), false));
            Assert.AreEqual(-1, g.YToLane(2f, false));
            Assert.AreEqual(0, g.YToLane(2f, true));
        }

        [Test]
        public void HitTest_FindsHeadHoldEndAndBody()
        {
            var g = new TimelineGeometry { LaneCount = 3 };
            var s = new ChartSession(new TempoMap(120d), new List<string> { "a", "b", "c" });
            NoteInstance n = s.AddNote("b", 2d, NoteMigration.DefinitionIdHold, 2d);
            Assert.AreEqual(TimelineHitKind.Head, g.HitTest(s, g.BeatToX(2d), g.LaneCenter(1)).Kind);
            Assert.AreEqual(TimelineHitKind.HoldEnd, g.HitTest(s, g.BeatToX(4d), g.LaneCenter(1)).Kind);
            Assert.AreEqual(TimelineHitKind.Body, g.HitTest(s, g.BeatToX(3d), g.LaneCenter(1)).Kind);
            Assert.AreEqual(TimelineHitKind.None, g.HitTest(s, g.BeatToX(3d), g.LaneCenter(0)).Kind);
            Assert.AreEqual(n.Id, g.HitTest(s, g.BeatToX(2d), g.LaneCenter(1)).NoteId);
        }

        [Test]
        public void ModifySelected_IsOneUndoStep_AndClampsNegatives()
        {
            var s = new ChartSession(new TempoMap(120d), new List<string> { "a" });
            NoteInstance n = s.AddNote("a", 2d, NoteMigration.DefinitionIdNormal);
            s.ModifySelected("x", c => { c.HitBeat = -5d; c.Damage = new Overridable<int>(9); });
            Assert.AreEqual(0d, s.Find(n.Id).HitBeat, 1e-9);
            Assert.AreEqual(9, s.Find(n.Id).Damage.Value);
            s.Undo();
            Assert.AreEqual(2d, s.Find(n.Id).HitBeat, 1e-9);
        }
    }
}
