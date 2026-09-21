using System;

namespace RythmRPG.Rhythm
{
    /// <summary>
    /// Single source of musical time. Time is supplied by a delegate (AudioSettings.dspTime in game,
    /// a fake in tests). Supports pause/resume without drift.
    /// </summary>
    public sealed class MusicClock
    {
        private readonly Func<double> timeSource;
        private double startTime;
        private double pausedAt;
        private double pausedTotal;
        private bool running;
        private bool paused;

        public TempoMap Tempo { get; set; }
        public bool IsRunning { get { return running && !paused; } }

        public MusicClock(Func<double> timeSource, TempoMap tempo)
        {
            if (timeSource == null) throw new ArgumentNullException("timeSource");
            this.timeSource = timeSource;
            Tempo = tempo;
        }

        /// <summary>Starts (or restarts) the clock so that song time 0 occurs at <paramref name="startAtTime"/> on the source timeline.</summary>
        public void StartAt(double startAtTime)
        {
            startTime = startAtTime;
            pausedTotal = 0d;
            paused = false;
            running = true;
        }

        public void StartNow(double leadInSeconds = 0d)
        {
            StartAt(timeSource() + leadInSeconds);
        }

        public void Stop()
        {
            running = false;
            paused = false;
            pausedTotal = 0d;
        }

        public void Pause()
        {
            if (!running || paused) return;
            paused = true;
            pausedAt = timeSource();
        }

        public void Resume()
        {
            if (!running || !paused) return;
            pausedTotal += timeSource() - pausedAt;
            paused = false;
        }

        /// <summary>Song position in seconds (negative during lead-in).</summary>
        public double Seconds
        {
            get
            {
                if (!running) return 0d;
                double now = paused ? pausedAt : timeSource();
                return now - startTime - pausedTotal;
            }
        }

        public double Beat { get { return Tempo.SecondsToBeat(Seconds); } }

        /// <summary>Source-timeline time at which the given beat will occur (for PlayScheduled-style APIs).</summary>
        public double SourceTimeAtBeat(double beat)
        {
            return startTime + pausedTotal + Tempo.BeatToSeconds(beat);
        }
    }
}
