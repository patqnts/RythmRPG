using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.Rhythm.Audio;

namespace RythmRPG.Rhythm.Tests
{
    public sealed class BeatSchedulerTests
    {
        [Test]
        public void At_FiresOnceInBeatOrder()
        {
            var s = new BeatScheduler();
            var log = new List<string>();
            s.At(2d, () => log.Add("b"));
            s.At(1d, () => log.Add("a"));
            s.At(2d, () => log.Add("c"));
            s.Advance(0.5d);
            Assert.AreEqual(0, log.Count);
            s.Advance(2.0d);
            Assert.AreEqual("a,b,c", string.Join(",", log.ToArray()));
            s.Advance(5d);
            Assert.AreEqual(3, log.Count);
            Assert.AreEqual(0, s.PendingCount);
        }

        [Test]
        public void Every_TicksOnEachInterval()
        {
            var s = new BeatScheduler();
            var ticks = new List<long>();
            s.Every(1d, i => ticks.Add(i));
            s.Advance(0d);
            s.Advance(0.99d);
            s.Advance(1d);
            s.Advance(3.2d);
            Assert.AreEqual("0,1,2,3", string.Join(",", ticks.ConvertAll(t => t.ToString()).ToArray()));
        }

        [Test]
        public void Every_RespectsStartAndCancel()
        {
            var s = new BeatScheduler();
            int count = 0;
            int id = s.Every(0.5d, i => count++, 2d);
            s.Advance(1.9d);
            Assert.AreEqual(0, count);
            s.Advance(2.5d);
            Assert.AreEqual(2, count);
            s.Cancel(id);
            s.Advance(10d);
            Assert.AreEqual(2, count);
        }

        [Test]
        public void Every_LargeJumpSkipsAheadInsteadOfFlooding()
        {
            var s = new BeatScheduler();
            int count = 0;
            s.Every(1d, i => count++);
            s.Advance(100000d);
            Assert.LessOrEqual(count, 64);
            Assert.Greater(count, 0);
        }

        [Test]
        public void Reposition_ReplaysTicksAfterSeek()
        {
            var s = new BeatScheduler();
            var ticks = new List<long>();
            s.Every(1d, i => ticks.Add(i));
            s.Advance(4d);
            ticks.Clear();
            s.Reposition(2d);
            s.Advance(3d);
            Assert.AreEqual(2, ticks.Count);
            Assert.AreEqual(2L, ticks[0]);
        }

        [Test]
        public void NextBoundary_IsStrictlyAfterBeat()
        {
            Assert.AreEqual(1d, BeatScheduler.NextBoundary(0.3d, 1d), 1e-9);
            Assert.AreEqual(2d, BeatScheduler.NextBoundary(1d, 1d), 1e-9);
            Assert.AreEqual(4d, BeatScheduler.NextBoundary(3.9d, 4d), 1e-9);
            Assert.AreEqual(0.5d, BeatScheduler.NextBoundary(0d, 0.5d), 1e-9);
        }

        [Test]
        public void LayerMixer_FadesActiveUpAndOthersDown()
        {
            var v = new float[] { 1f, 0f };
            LayerMixer.Step(v, 1, 0.5f, 1f);
            Assert.AreEqual(0.5f, v[0], 1e-6);
            Assert.AreEqual(0.5f, v[1], 1e-6);
            LayerMixer.Step(v, 1, 5f, 1f);
            Assert.AreEqual(0f, v[0], 1e-6);
            Assert.AreEqual(1f, v[1], 1e-6);
        }

        [Test]
        public void LayerMixer_ZeroFadeSnaps()
        {
            var v = new float[] { 1f, 0f, 0f };
            LayerMixer.Step(v, 2, 0.016f, 0f);
            Assert.AreEqual(0f, v[0], 1e-6);
            Assert.AreEqual(1f, v[2], 1e-6);
        }
    }
}
