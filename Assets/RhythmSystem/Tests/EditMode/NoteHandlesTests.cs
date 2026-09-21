using NUnit.Framework;
using RythmRPG.Rhythm.Editing;

namespace RythmRPG.Rhythm.Tests
{
    public class NoteHandlesTests
    {
        private static readonly TempoMap Tempo = new TempoMap(120d, 4);
        private static readonly NoteHandleDefaults Defaults = new NoteHandleDefaults();

        private static NoteInstance Note(double beat, double hold = 0d)
        {
            return new NoteInstance { Id = "n", LaneId = "a", DefinitionId = "x", HitBeat = beat, HoldBeats = hold };
        }

        private static bool Has(NoteHandleKind[] kinds, NoteHandleKind k)
        {
            for (int i = 0; i < kinds.Length; i++) if (kinds[i] == k) return true;
            return false;
        }

        [Test]
        public void Schema_ListsHandlesPerBehavior()
        {
            Assert.IsTrue(Has(NoteHandles.For(NoteBehaviorKind.Moving), NoteHandleKind.Travel));
            Assert.IsFalse(Has(NoteHandles.For(NoteBehaviorKind.Moving), NoteHandleKind.HoldEnd));
            Assert.IsTrue(Has(NoteHandles.For(NoteBehaviorKind.MovingHold), NoteHandleKind.HoldEnd));
            Assert.IsTrue(Has(NoteHandles.For(NoteBehaviorKind.Mash), NoteHandleKind.Travel));
            Assert.IsFalse(Has(NoteHandles.For(NoteBehaviorKind.Mash), NoteHandleKind.HoldEnd));
            Assert.IsTrue(Has(NoteHandles.For(NoteBehaviorKind.Stationary), NoteHandleKind.WindowPerfect));
            Assert.IsFalse(Has(NoteHandles.For(NoteBehaviorKind.Pong), NoteHandleKind.WindowBad));
            Assert.IsTrue(Has(NoteHandles.For(NoteBehaviorKind.StationaryHold), NoteHandleKind.WindowGood));
            Assert.IsTrue(Has(NoteHandles.For(NoteBehaviorKind.StationaryHold), NoteHandleKind.HoldEnd));
            Assert.IsFalse(Has(NoteHandles.For(NoteBehaviorKind.Mash), NoteHandleKind.WindowBad));
            Assert.AreEqual(2.5d, NoteHandles.TravelSeconds(Note(8d), Defaults, Tempo), 1e-9);
        }

        [Test]
        public void Travel_UsesDefaultUntilOverridden()
        {
            NoteInstance n = Note(8d);
            Assert.AreEqual(5d, NoteHandles.TravelBeats(n, Defaults, Tempo), 1e-9);
            Assert.AreEqual(3d, NoteHandles.BeatOf(NoteHandleKind.Travel, n, Defaults, Tempo), 1e-9);
            NoteHandles.Apply(NoteHandleKind.Travel, n, 6d, Defaults, Tempo);
            Assert.IsTrue(n.TravelBeats.HasValue);
            Assert.AreEqual(2d, n.TravelBeats.Value, 1e-9);
        }

        [Test]
        public void Travel_ClampsToMinimum()
        {
            NoteInstance n = Note(4d);
            NoteHandles.Apply(NoteHandleKind.Travel, n, 9d, Defaults, Tempo);
            Assert.AreEqual(NoteHandles.MinTravelBeats, n.TravelBeats.Value, 1e-9);
        }

        [Test]
        public void HoldEnd_NeverNegative()
        {
            NoteInstance n = Note(4d, 2d);
            NoteHandles.Apply(NoteHandleKind.HoldEnd, n, 7d, Defaults, Tempo);
            Assert.AreEqual(3d, n.HoldBeats, 1e-9);
            NoteHandles.Apply(NoteHandleKind.HoldEnd, n, 1d, Defaults, Tempo);
            Assert.AreEqual(0d, n.HoldBeats, 1e-9);
        }

        [Test]
        public void Windows_ConvertBetweenBeatsAndSeconds()
        {
            NoteInstance n = Note(4d);
            // 0.45 s default bad window at 120 BPM (0.5 s per beat) reaches 0.9 beats past the hit.
            Assert.AreEqual(4.9d, NoteHandles.BeatOf(NoteHandleKind.WindowBad, n, Defaults, Tempo), 1e-6);
            NoteHandles.Apply(NoteHandleKind.WindowBad, n, 6d, Defaults, Tempo);
            Assert.AreEqual(1f, n.StationaryBadWindow.Value, 1e-5);
        }

        [Test]
        public void Windows_KeepPerfectGoodBadOrdered()
        {
            NoteInstance n = Note(4d);
            NoteHandles.Apply(NoteHandleKind.WindowGood, n, 20d, Defaults, Tempo);
            Assert.AreEqual(0.45f, n.StationaryGoodWindow.Value, 1e-5);
            NoteHandles.Apply(NoteHandleKind.WindowPerfect, n, 20d, Defaults, Tempo);
            Assert.AreEqual(0.45f, n.StationaryPerfectWindow.Value, 1e-5);
            NoteHandles.Apply(NoteHandleKind.WindowBad, n, 4.1d, Defaults, Tempo);
            Assert.AreEqual(0.45f, n.StationaryBadWindow.Value, 1e-5);
            NoteHandles.Apply(NoteHandleKind.WindowGood, n, 4d, Defaults, Tempo);
            Assert.AreEqual(0.45f, n.StationaryGoodWindow.Value, 1e-5); // cannot drop below perfect
            NoteHandles.Apply(NoteHandleKind.WindowPerfect, n, 4d, Defaults, Tempo);
            Assert.AreEqual(NoteHandles.MinWindowSeconds, n.StationaryPerfectWindow.Value, 1e-5);
        }

        [Test]
        public void Session_SetNoteHandle_IsOneUndoStep()
        {
            var session = new ChartSession(Tempo, new[] { "a" });
            NoteInstance n = session.AddNote("a", 8d, "x");
            session.SetNoteHandle(n.Id, NoteHandleKind.Travel, 6d, Defaults);
            Assert.AreEqual(2d, session.Find(n.Id).TravelBeats.Value, 1e-9);
            session.Undo();
            Assert.IsFalse(session.Find(n.Id).TravelBeats.HasValue);
            session.Redo();
            Assert.AreEqual(2d, session.Find(n.Id).TravelBeats.Value, 1e-9);
        }

        [Test]
        public void Session_SetNoteHandle_NoChangeAddsNoUndo()
        {
            var session = new ChartSession(Tempo, new[] { "a" });
            NoteInstance n = session.AddNote("a", 8d, "x");
            session.SetNoteHandle(n.Id, NoteHandleKind.HoldEnd, 8d, Defaults);
            session.Undo();
            Assert.IsTrue(session.Find(n.Id) == null);
        }

        [Test]
        public void HitTest_HandlesOnlyForSelectedNotes()
        {
            var session = new ChartSession(Tempo, new[] { "a" });
            NoteInstance n = session.AddNote("a", 8d, "x");
            var geo = new TimelineGeometry { LaneCount = 1 };
            System.Func<NoteInstance, System.Collections.Generic.List<NoteHandlePoint>> provider =
                note => NoteHandles.Points(note, NoteBehaviorKind.Stationary, Defaults, Tempo, false);
            float x = geo.BeatToX(NoteHandles.BeatOf(NoteHandleKind.WindowBad, n, Defaults, Tempo));
            float y = geo.LaneCenter(0);
            session.ClearSelection();
            Assert.IsTrue(geo.HitTest(session, x, y, provider).Kind != TimelineHitKind.Handle);
            session.SelectOnly(n.Id);
            TimelineHit hit = geo.HitTest(session, x, y, provider);
            Assert.IsTrue(hit.Kind == TimelineHitKind.Handle);
            Assert.IsTrue(hit.Handle == NoteHandleKind.WindowBad);
        }
    }
}
