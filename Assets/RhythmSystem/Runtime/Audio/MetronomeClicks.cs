using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm.Audio
{
    public struct MetronomeTick
    {
        public long Beat;
        public double Seconds;
        public bool IsDownbeat;
    }

    /// <summary>Metronome tick positions and click rendering. Pure C#; the editor mixes the result into a preview clip.</summary>
    public static class MetronomeClicks
    {
        public const double ClickSeconds = 0.035d;

        /// <summary>Appends every whole-beat tick with Seconds in [fromSeconds, toSeconds). Beats below 0 are skipped.</summary>
        public static void CollectTicks(TempoMap tempo, double fromSeconds, double toSeconds, List<MetronomeTick> result)
        {
            if (tempo == null) throw new ArgumentNullException("tempo");
            if (result == null) throw new ArgumentNullException("result");
            if (toSeconds <= fromSeconds) return;
            long first = (long)Math.Ceiling(tempo.SecondsToBeat(fromSeconds) - 1e-9);
            if (first < 0) first = 0;
            int perMeasure = Math.Max(1, tempo.BeatsPerMeasure);
            for (long beat = first; ; beat++)
            {
                double s = tempo.BeatToSeconds(beat);
                if (s >= toSeconds) break;
                if (s < fromSeconds - 1e-9) continue;
                result.Add(new MetronomeTick { Beat = beat, Seconds = s, IsDownbeat = beat % perMeasure == 0 });
            }
        }

        /// <summary>
        /// Adds a short decaying sine click at every tick into an interleaved buffer that starts at song time 0.
        /// Downbeats are higher-pitched and louder.
        /// </summary>
        public static void MixInto(float[] interleaved, int channels, int sampleRate, TempoMap tempo, float gain)
        {
            if (interleaved == null) throw new ArgumentNullException("interleaved");
            channels = Math.Max(1, channels);
            int frames = interleaved.Length / channels;
            double duration = (double)frames / sampleRate;
            var ticks = new List<MetronomeTick>();
            CollectTicks(tempo, 0d, duration, ticks);
            int clickFrames = Math.Max(1, (int)(ClickSeconds * sampleRate));
            for (int i = 0; i < ticks.Count; i++)
            {
                MetronomeTick t = ticks[i];
                int start = (int)Math.Round(t.Seconds * sampleRate);
                double freq = t.IsDownbeat ? 1800d : 1200d;
                float amp = gain * (t.IsDownbeat ? 1f : 0.7f);
                for (int k = 0; k < clickFrames; k++)
                {
                    int f = start + k;
                    if (f < 0 || f >= frames) continue;
                    double time = (double)k / sampleRate;
                    float env = 1f - (float)k / clickFrames;
                    float v = (float)Math.Sin(2d * Math.PI * freq * time) * env * env * amp;
                    int baseIndex = f * channels;
                    for (int c = 0; c < channels; c++) interleaved[baseIndex + c] += v;
                }
            }
        }
    }
}
