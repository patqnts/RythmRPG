using System;

namespace RythmRPG.Rhythm.Audio
{
    /// <summary>
    /// Min/max waveform summary of a clip. Built once from interleaved samples, then queried cheaply
    /// for any zoom level. Pure C# (no UnityEngine) so it is unit-testable.
    /// </summary>
    public sealed class WaveformPeaks
    {
        public const int DefaultBucketSize = 256;

        private readonly float[] mins;
        private readonly float[] maxs;

        public int SampleRate { get; private set; }
        public int BucketSize { get; private set; }
        public double Duration { get; private set; }
        public int BucketCount { get { return mins.Length; } }

        /// <summary>Largest absolute sample value; the painter divides by it so quiet tracks still fill the lane area.</summary>
        public float PeakAmplitude { get; private set; }

        private WaveformPeaks(float[] mins, float[] maxs, int sampleRate, int bucketSize, double duration)
        {
            for (int i = 0; i < mins.Length; i++)
            {
                PeakAmplitude = Math.Max(PeakAmplitude, Math.Max(Math.Abs(mins[i]), Math.Abs(maxs[i])));
            }

            this.mins = mins;
            this.maxs = maxs;
            SampleRate = sampleRate;
            BucketSize = bucketSize;
            Duration = duration;
        }

        /// <summary>Builds peaks from interleaved samples. Channels are averaged to mono per frame.</summary>
        public static WaveformPeaks Build(float[] interleaved, int channels, int sampleRate, int bucketSize = DefaultBucketSize)
        {
            if (interleaved == null) throw new ArgumentNullException("interleaved");
            channels = Math.Max(1, channels);
            sampleRate = Math.Max(1, sampleRate);
            bucketSize = Math.Max(1, bucketSize);
            int frames = interleaved.Length / channels;
            int buckets = (frames + bucketSize - 1) / bucketSize;
            var mins = new float[buckets];
            var maxs = new float[buckets];
            for (int b = 0; b < buckets; b++)
            {
                int f0 = b * bucketSize;
                int f1 = Math.Min(frames, f0 + bucketSize);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int f = f0; f < f1; f++)
                {
                    float v = 0f;
                    int baseIndex = f * channels;
                    for (int c = 0; c < channels; c++) v += interleaved[baseIndex + c];
                    v /= channels;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }

                mins[b] = lo;
                maxs[b] = hi;
            }

            return new WaveformPeaks(mins, maxs, sampleRate, bucketSize, (double)frames / sampleRate);
        }

        /// <summary>
        /// Min/max over [startSeconds, endSeconds). Returns false (and zeros) when the range lies outside the clip.
        /// </summary>
        public bool QueryRange(double startSeconds, double endSeconds, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (mins.Length == 0 || endSeconds <= 0d || startSeconds >= Duration) return false;
            double bucketSeconds = (double)BucketSize / SampleRate;
            int b0 = (int)Math.Floor(Math.Max(0d, startSeconds) / bucketSeconds);
            int b1 = (int)Math.Floor((Math.Min(Duration, endSeconds) - 1e-9) / bucketSeconds);
            b0 = Math.Max(0, Math.Min(mins.Length - 1, b0));
            b1 = Math.Max(b0, Math.Min(mins.Length - 1, b1));
            float lo = float.MaxValue, hi = float.MinValue;
            for (int b = b0; b <= b1; b++)
            {
                if (mins[b] < lo) lo = mins[b];
                if (maxs[b] > hi) hi = maxs[b];
            }

            min = lo;
            max = hi;
            return true;
        }
    }
}
