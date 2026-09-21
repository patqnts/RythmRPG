using System;

namespace RythmRPG.Rhythm.Audio
{
    /// <summary>
    /// Play/pause/seek clock for an editor playhead. Time comes from an injected delegate
    /// (EditorApplication.timeSinceStartup in the composer, a fake in tests).
    /// </summary>
    public sealed class PlaybackTransport
    {
        private readonly Func<double> timeSource;
        private double basePosition;
        private double startedAt;

        public double Duration { get; set; }
        public bool IsPlaying { get; private set; }

        public PlaybackTransport(Func<double> timeSource)
        {
            if (timeSource == null) throw new ArgumentNullException("timeSource");
            this.timeSource = timeSource;
        }

        /// <summary>Playhead position in seconds, clamped to [0, Duration].</summary>
        public double Position
        {
            get
            {
                double p = IsPlaying ? basePosition + Math.Max(0d, timeSource() - startedAt) : basePosition;
                return Math.Max(0d, Math.Min(Duration, p));
            }
        }

        public void Play()
        {
            if (IsPlaying) return;
            if (basePosition >= Duration) basePosition = 0d;
            startedAt = timeSource();
            IsPlaying = true;
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            basePosition = Position;
            IsPlaying = false;
        }

        /// <summary>Stops and returns to <paramref name="returnTo"/> seconds.</summary>
        public void Stop(double returnTo = 0d)
        {
            IsPlaying = false;
            basePosition = Math.Max(0d, Math.Min(Duration, returnTo));
        }

        /// <summary>
        /// Re-anchors the playhead: it reads <paramref name="position"/> until the time source reaches
        /// <paramref name="startAt"/> (e.g. the dsp time scheduled audio begins), then advances from there.
        /// </summary>
        public void SyncTo(double position, double startAt)
        {
            basePosition = Math.Max(0d, Math.Min(Duration, position));
            startedAt = startAt;
        }

        public void Seek(double seconds)
        {
            basePosition = Math.Max(0d, Math.Min(Duration, seconds));
            startedAt = timeSource();
        }

        /// <summary>Call every editor tick. Stops at the end; returns true on the tick playback finished.</summary>
        public bool Update()
        {
            if (!IsPlaying) return false;
            if (Position < Duration) return false;
            basePosition = Duration;
            IsPlaying = false;
            return true;
        }
    }
}
