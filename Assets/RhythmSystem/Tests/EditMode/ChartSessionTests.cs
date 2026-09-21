using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.Rhythm.Editing;
using UnityEngine;

namespace RythmRPG.Rhythm.Tests
{
    public class ChartSessionTests
    {
        private static ChartSession NewSession()
        {
            return new ChartSession(new TempoMap(120d), new List<string> { "a", "b", "c" });
        }

        [Test]
        public void AddNote_SnapsToGrid()
        {
            ChartSession s = NewSession();
            NoteInstance n = s.AddNote("a", 1.03, NoteMigration.DefinitionIdNormal);
            Assert.AreEqual(1d, n.HitBeat, 1e-9);
        }

        [Test]
        public void Move_ClampsLanes_AndUndoRedoRestore()
        {
            ChartSession s = NewSession();
            NoteInstance n1 = s.AddNote("a", 1, NoteMigration.DefinitionIdNormal);
            s.AddNote("b", 2, NoteMigration.DefinitionIdHold, 2d);
            s.SelectAll();
            s.MoveSelected(1d, 1);
            Assert.AreEqual(2d, s.Find(n1.Id).HitBeat, 1e-9);
            Assert.AreEqual("b", s.Find(n1.Id).LaneId);
            s.MoveSelected(0d, 5); // already at the last lane: no-op
            s.Undo();
            Assert.AreEqual(1d, s.Find(n1.Id).HitBeat, 1e-9);
            s.Redo();
            Assert.AreEqual(2d, s.Find(n1.Id).HitBeat, 1e-9);
        }

        [Test]
        public void CopyPaste_UsesNewIds_AndOneUndoRemovesGroup()
        {
            ChartSession s = NewSession();
            s.AddNote("a", 1, NoteMigration.DefinitionIdNormal);
            s.AddNote("a", 2, NoteMigration.DefinitionIdNormal);
            s.SelectAll();
            s.Copy();
            s.Paste(8d);
            Assert.AreEqual(4, s.Notes.Count);
            Assert.AreEqual(2, s.SelectionCount);
            double min = double.MaxValue;
            foreach (string id in s.Selection) min = System.Math.Min(min, s.Find(id).HitBeat);
            Assert.AreEqual(8d, min, 1e-9);
            s.Undo();
            Assert.AreEqual(2, s.Notes.Count);
        }

        [Test]
        public void Delete_ThenUndo_RestoresNotes()
        {
            ChartSession s = NewSession();
            s.AddNote("a", 1, NoteMigration.DefinitionIdNormal);
            s.SelectAll();
            s.DeleteSelected();
            Assert.AreEqual(0, s.Notes.Count);
            s.Undo();
            Assert.AreEqual(1, s.Notes.Count);
        }

        [Test]
        public void Bridge_RoundTrip_PreservesUnsupportedNotesAndUnmodelledFields()
        {
            RhythmChart chart = ScriptableObject.CreateInstance<RhythmChart>();
            try
            {
                chart.Lanes.Clear();
                chart.Lanes.Add(new RhythmLaneData("L1", 1, Color.white));
                string lane = chart.Lanes[0].Id;
                chart.Bpm = 120f;
                var hold = new RhythmNoteData(lane, 3d, RhythmNoteType.Hold) { HoldDuration = 1.5d, TravelTime = 2.5d, Speed = 8f, Damage = 7 };
                var arrow = new RhythmNoteData(lane, 1d, RhythmNoteType.Arrow);
                chart.Notes.Clear();
                chart.Notes.Add(hold);
                chart.Notes.Add(arrow);

                ChartSession s = ChartSessionBridge.Load(chart);
                Assert.AreEqual(1, s.Notes.Count);
                s.SelectOnly(hold.Id);
                s.MoveSelected(2d, 0);
                s.AddNote(lane, 4d, NoteMigration.DefinitionIdNormal);
                ChartSessionBridge.Apply(s, chart);

                Assert.AreEqual(3, chart.Notes.Count);
                Assert.AreEqual(4d, hold.HitTime, 1e-9);
                Assert.AreEqual(1.5d, hold.HoldDuration, 1e-9);
                Assert.AreEqual(7, hold.Damage);
                Assert.IsTrue(chart.Notes.Contains(arrow));
            }
            finally
            {
                Object.DestroyImmediate(chart);
            }
        }
    }
}
