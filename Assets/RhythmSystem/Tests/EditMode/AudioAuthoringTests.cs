using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.Rhythm.Audio;

namespace RythmRPG.Rhythm.Tests
{
    public sealed class AudioAuthoringTests
    {
        [Test]
        public void Peaks_CaptureMinMaxAndAverageChannels()
        {
            // 2 channels, 8 frames, bucket 4 -> 2 buckets. Frame values (L,R): mono avg.
            var data = new float[] { 1f, 1f, -1f, -1f, 0.5f, 0.5f, 0f, 0f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f };
            WaveformPeaks p = WaveformPeaks.Build(data, 2, 8, 4);
            Assert.AreEqual(2, p.BucketCount);
            Assert.AreEqual(1f, p.PeakAmplitude, 1e-6);
            Assert.AreEqual(1d, p.Duration, 1e-9);
            float min, max;
            Assert.IsTrue(p.QueryRange(0d, 0.5d, out min, out max));
            Assert.AreEqual(-1f, min, 1e-6);
            Assert.AreEqual(1f, max, 1e-6);
            Assert.IsTrue(p.QueryRange(0.5d, 1d, out min, out max));
            Assert.AreEqual(0.2f, min, 1e-6);
            Assert.AreEqual(0.2f, max, 1e-6);
        }

        [Test]
        public void Peaks_QueryOutsideClipReturnsFalse()
        {
            WaveformPeaks p = WaveformPeaks.Build(new float[100], 1, 100, 10);
            float min, max;
            Assert.IsFalse(p.QueryRange(-2d, -1d, out min, out max));
            Assert.IsFalse(p.QueryRange(1d, 2d, out min, out max));
            Assert.IsTrue(p.QueryRange(-1d, 0.1d, out min, out max));
        }

        [Test]
        public void Peaks_RaggedLastBucketIsIncluded()
        {
            var data = new float[10];
            data[9] = 0.9f;
            WaveformPeaks p = WaveformPeaks.Build(data, 1, 10, 4);
            Assert.AreEqual(3, p.BucketCount);
            float min, max;
            p.QueryRange(0.9d, 1d, out min, out max);
            Assert.AreEqual(0.9f, max, 1e-6);
        }

        [Test]
        public void Ticks_FollowBeatsAndMarkDownbeats()
        {
            var tempo = new TempoMap(120d, 4); // 0.5 s per beat
            var ticks = new List<MetronomeTick>();
            MetronomeClicks.CollectTicks(tempo, 0d, 2.5d, ticks);
            Assert.AreEqual(5, ticks.Count); // beats 0..4 at 0,0.5,1,1.5,2
            Assert.IsTrue(ticks[0].IsDownbeat);
            Assert.IsFalse(ticks[1].IsDownbeat);
            Assert.IsTrue(ticks[4].IsDownbeat);
            Assert.AreEqual(1.5d, ticks[3].Seconds, 1e-9);
        }

        [Test]
        public void Ticks_RespectAudioOffsetAndRangeStart()
        {
            var tempo = new TempoMap(120d, 4, 0.25d); // beat 0 at 0.25 s
            var ticks = new List<MetronomeTick>();
            MetronomeClicks.CollectTicks(tempo, 0d, 1.3d, ticks);
            Assert.AreEqual(3, ticks.Count); // 0.25, 0.75, 1.25
            Assert.AreEqual(0.25d, ticks[0].Seconds, 1e-9);

            ticks.Clear();
            MetronomeClicks.CollectTicks(tempo, 0.75d, 1.3d, ticks);
            Assert.AreEqual(2, ticks.Count);
            Assert.AreEqual(1L, ticks[0].Beat);
        }

        [Test]
        public void MixInto_PutsClickAtTickAndSilenceBetween()
        {
            const int rate = 1000;
            var tempo = new TempoMap(60d, 4); // 1 s per beat
            var buffer = new float[3 * rate * 2]; // 3 s stereo
            MetronomeClicks.MixInto(buffer, 2, rate, tempo, 1f);
            float peakAtTick = 0f, peakBetween = 0f;
            for (int f = 1000; f < 1035; f++) peakAtTick = System.Math.Max(peakAtTick, System.Math.Abs(buffer[f * 2]));
            for (int f = 1200; f < 1800; f++) peakBetween = System.Math.Max(peakBetween, System.Math.Abs(buffer[f * 2]));
            Assert.Greater(peakAtTick, 0.1f);
            Assert.AreEqual(0f, peakBetween, 1e-9);
            Assert.AreEqual(buffer[1010 * 2], buffer[1010 * 2 + 1], 1e-9); // both channels
        }

        [Test]
        public void Transport_AdvancesPausesAndSeeks()
        {
            double now = 100d;
            var t = new PlaybackTransport(() => now) { Duration = 10d };
            t.Play();
            now = 103d;
            Assert.AreEqual(3d, t.Position, 1e-9);
            t.Pause();
            now = 120d;
            Assert.AreEqual(3d, t.Position, 1e-9);
            t.Seek(8d);
            t.Play();
            now = 121d;
            Assert.AreEqual(9d, t.Position, 1e-9);
        }

        [Test]
        public void Transport_StopsAtEndAndReplaysFromStart()
        {
            double now = 0d;
            var t = new PlaybackTransport(() => now) { Duration = 2d };
            t.Play();
            now = 5d;
            Assert.IsTrue(t.Update());
            Assert.IsFalse(t.IsPlaying);
            Assert.AreEqual(2d, t.Position, 1e-9);
            Assert.IsFalse(t.Update());
            t.Play(); // at end -> restarts
            Assert.AreEqual(0d, t.Position, 1e-9);
        }
    }
}
