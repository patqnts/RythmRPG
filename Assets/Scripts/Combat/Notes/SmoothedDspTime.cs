using System;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// <c>AudioSettings.dspTime</c> only moves once per audio buffer (about 21 ms with a 1024-sample buffer), so notes
    /// placed from it move in small steps and are judged against a value that can be one buffer stale. This clock
    /// follows real time at the audio clock's offset: it learns the offset from the moments dspTime has just stepped
    /// (the latest, tightest estimate wins, and it slowly relaxes so real-time/audio drift is followed), stays within
    /// one buffer of dspTime and never goes backwards. One value per frame, so every note in a frame sees the same time.
    /// <para>
    /// The result is on the pause-aware <see cref="GameAudioClock"/> timeline: it stands still while the game is paused
    /// (and during the resume countdown) and continues seamlessly afterwards.
    /// </para>
    /// </summary>
    public static class SmoothedDspTime
    {
        private const double DriftPerSecond = 0.001d; // how fast the offset estimate may relax (s per s)

        private static double offset = double.NaN;
        private static double lastDsp;
        private static double lastReal;
        private static double lastResult;
        private static double lastGameTime = double.NegativeInfinity;
        private static int cachedFrame = -1;
        private static double cachedValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            offset = double.NaN;
            lastDsp = lastReal = lastResult = 0d;
            lastGameTime = double.NegativeInfinity;
            cachedFrame = -1;
            cachedValue = 0d;
        }

        /// <summary>Smoothed game time (see <see cref="GameAudioClock.Now"/>).</summary>
        public static double Now
        {
            get
            {
                int frame = Time.frameCount;
                if (frame == cachedFrame) return cachedValue;

                if (GameAudioClock.IsHeld)
                {
                    cachedValue = Math.Max(GameAudioClock.Now, lastGameTime);
                    lastGameTime = cachedValue;
                    cachedFrame = frame;
                    return cachedValue;
                }

                double dsp = AudioSettings.dspTime;
                double real = Time.realtimeSinceStartupAsDouble;
                double sample = dsp - real; // dspTime is at or just behind the true audio time
                bool clockWentBack = dsp < lastDsp; // audio device restarted
                bool resync = double.IsNaN(offset) || clockWentBack || Math.Abs(sample - offset) > 0.5d;
                offset = resync ? sample : Math.Max(sample, offset - (real - lastReal) * DriftPerSecond);
                if (resync) lastResult = dsp;
                lastDsp = dsp;
                lastReal = real;

                double value = real + offset;
                value = Math.Min(Math.Max(value, dsp), dsp + BufferSeconds());
                value = Math.Max(value, lastResult); // monotonic
                lastResult = value;

                double gameTime = GameAudioClock.ToGameTime(value);
                if (clockWentBack) lastGameTime = double.NegativeInfinity;
                gameTime = Math.Max(gameTime, lastGameTime);
                lastGameTime = gameTime;
                cachedFrame = frame;
                cachedValue = gameTime;
                return gameTime;
            }
        }

        private static double BufferSeconds()
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
            int rate = AudioSettings.outputSampleRate;
            return rate > 0 && bufferLength > 0 ? (double)bufferLength / rate : 0.025d;
        }
    }
}
