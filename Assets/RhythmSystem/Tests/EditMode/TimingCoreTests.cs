using NUnit.Framework;

namespace RythmRPG.Rhythm.Tests
{
    public class TimingCoreTests
    {
        [Test]
        public void TempoMap_ConstantTempo_RoundTrips()
        {
            var t = new TempoMap(120d, 4);
            Assert.AreEqual(2d, t.BeatToSeconds(4d), 1e-9);
            Assert.AreEqual(4d, t.SecondsToBeat(2d), 1e-9);
        }

        [Test]
        public void TempoMap_TempoChangeAndOffset_RoundTrips()
        {
            var t = new TempoMap(new[] { new TempoSegment(0, 120), new TempoSegment(4, 60) }, 4, 0.5);
            Assert.AreEqual(4.5d, t.BeatToSeconds(6d), 1e-9);
            for (double b = 0; b < 20; b += 0.37)
            {
                Assert.AreEqual(b, t.SecondsToBeat(t.BeatToSeconds(b)), 1e-9);
            }
        }

        [Test]
        public void MusicClock_PauseDoesNotDrift()
        {
            double now = 100d;
            var c = new MusicClock(() => now, new TempoMap(120d));
            c.StartAt(101d);
            now = 102d;
            c.Pause();
            now = 110d;
            Assert.AreEqual(1d, c.Seconds, 1e-9);
            c.Resume();
            now = 111d;
            Assert.AreEqual(2d, c.Seconds, 1e-9);
        }

        [Test]
        public void BeatSync_ResolvesBoundaries()
        {
            Assert.AreEqual(2d, BeatSync.ResolveTargetBeat(1.2, SyncPolicy.NextBeat, 4), 1e-9);
            Assert.AreEqual(2d, BeatSync.ResolveTargetBeat(2.0, SyncPolicy.NextBeat, 4), 1e-9);
            Assert.AreEqual(8d, BeatSync.ResolveTargetBeat(5.1, SyncPolicy.NextMeasure, 4), 1e-9);
        }

        [Test]
        public void Migration_PreservesTimingAndSkipsUnsupportedTypes()
        {
            var t = new TempoMap(120d);
            var n = new RhythmNoteData("L", 3d, RhythmNoteType.Normal) { TravelTime = 2.5d };
            NoteInstance i = NoteMigration.ToInstance(n, t);
            Assert.AreEqual(3d, NoteMigration.HitSeconds(i, t), 1e-9);
            Assert.AreEqual(2.5d, NoteMigration.TravelSeconds(i, t), 1e-9);

            string warning = null;
            var arrow = new RhythmNoteData("L", 1d, RhythmNoteType.Arrow);
            Assert.IsNull(NoteMigration.ToInstance(arrow, t, s => warning = s));
            Assert.IsNotNull(warning);
        }
    }
}
