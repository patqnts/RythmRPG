using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Pause-aware audio clock. Game code reads <see cref="Now"/> instead of <c>AudioSettings.dspTime</c>: it follows
    /// the audio clock but stands still while the game is paused (and through the resume countdown), so charts,
    /// notes and music scheduling pick up exactly where they stopped.
    /// <para>
    /// Game time = audio time - total paused time. Anything handed to the audio engine (<c>PlayScheduled</c>,
    /// <c>SetScheduledEndTime</c>) must go through <see cref="ToAudioTime"/>, or better through
    /// <see cref="PausableAudio"/>, which also stops and re-schedules those sources around a pause.
    /// </para>
    /// </summary>
    public static class GameAudioClock
    {
        private static double pausedTotal;
        private static bool suspended;
        private static double heldAt;
        private static double pauseStartDsp;
        private static double releaseAtDsp = double.NegativeInfinity;

        /// <summary>Game time on the audio timeline (seconds). Frozen while paused.</summary>
        public static double Now
        {
            get
            {
                double dsp = AudioSettings.dspTime;
                return suspended || dsp < releaseAtDsp ? heldAt : dsp - pausedTotal;
            }
        }

        /// <summary>True while game time stands still: paused, or counting down to resume.</summary>
        public static bool IsHeld => suspended || AudioSettings.dspTime < releaseAtDsp;

        /// <summary>True while paused and nothing has been re-scheduled yet (before a resume countdown starts).</summary>
        public static bool IsSuspended => suspended;

        /// <summary>Audio time the game clock starts moving again after a resume (for countdown UIs).</summary>
        public static double ReleaseAtDsp => releaseAtDsp;

        public static double ToAudioTime(double gameTime) => gameTime + pausedTotal;
        public static double ToGameTime(double audioTime) => audioTime - pausedTotal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pausedTotal = 0d;
            suspended = false;
            heldAt = 0d;
            pauseStartDsp = 0d;
            releaseAtDsp = double.NegativeInfinity;
            PausableAudio.ResetStatics();
        }

        /// <summary>Freezes game time and stops every <see cref="PausableAudio"/> source.</summary>
        internal static void Suspend()
        {
            if (suspended) return;
            double dsp = AudioSettings.dspTime;
            if (dsp < releaseAtDsp)
            {
                // Paused again during the resume countdown: take the scheduled resume back.
                pausedTotal -= releaseAtDsp - pauseStartDsp;
            }
            else
            {
                heldAt = dsp - pausedTotal;
                pauseStartDsp = dsp;
            }

            releaseAtDsp = double.NegativeInfinity;
            suspended = true;
            PausableAudio.Suspend(heldAt);
        }

        /// <summary>
        /// Schedules game time to start again at audio time <paramref name="releaseAtAudioTime"/> and re-schedules every
        /// <see cref="PausableAudio"/> source to continue from where it stopped at that same moment.
        /// </summary>
        internal static void Release(double releaseAtAudioTime)
        {
            if (!suspended) return;
            releaseAtAudioTime = Math.Max(releaseAtAudioTime, AudioSettings.dspTime);
            pausedTotal += releaseAtAudioTime - pauseStartDsp;
            releaseAtDsp = releaseAtAudioTime;
            suspended = false;
            PausableAudio.Resume(heldAt);
        }

        /// <summary>Called once game time is moving again.</summary>
        internal static void Released() => PausableAudio.EndSuspension();
    }

    /// <summary>
    /// Plays scheduled audio on game time (<see cref="GameAudioClock"/>) so it survives a pause: when the game pauses,
    /// registered sources stop; when it resumes, each one is re-scheduled sample-accurately to continue from the exact
    /// sample it had reached (loops wrap), or to start at its original game time if it had not started yet. Use
    /// these instead of <c>AudioSource.PlayScheduled / SetScheduledEndTime / Stop</c> for music and any audio that is
    /// timed to the chart.
    /// </summary>
    public static class PausableAudio
    {
        private sealed class Entry
        {
            public double Start;
            public double End = double.PositiveInfinity;
            public bool Suspended;
        }

        private static readonly Dictionary<AudioSource, Entry> entries = new();
        private static readonly List<AudioSource> scratch = new();

        internal static void ResetStatics() => entries.Clear();

        /// <summary>True if <paramref name="source"/> is managed here (the pause system leaves it alone).</summary>
        public static bool IsManaged(AudioSource source) => source != null && entries.ContainsKey(source);

        /// <summary><c>PlayScheduled</c> at game time <paramref name="gameTime"/>.</summary>
        public static void PlayScheduled(AudioSource source, double gameTime)
        {
            if (source == null) return;
            bool suspended = GameAudioClock.IsSuspended;
            entries[source] = new Entry { Start = gameTime, Suspended = suspended };
            if (!suspended) source.PlayScheduled(GameAudioClock.ToAudioTime(gameTime));
        }

        /// <summary><c>SetScheduledEndTime</c> at game time <paramref name="gameTime"/>.</summary>
        public static void SetScheduledEndTime(AudioSource source, double gameTime)
        {
            if (source == null) return;
            if (entries.TryGetValue(source, out Entry entry)) entry.End = gameTime;
            if (!GameAudioClock.IsSuspended) source.SetScheduledEndTime(GameAudioClock.ToAudioTime(gameTime));
        }

        public static void Stop(AudioSource source)
        {
            if (source == null) return;
            entries.Remove(source);
            source.Stop();
        }

        internal static void Suspend(double pauseGameTime)
        {
            scratch.Clear();
            scratch.AddRange(entries.Keys);
            foreach (AudioSource source in scratch)
            {
                Entry entry = entries[source];
                if (source == null || entry.End <= pauseGameTime)
                {
                    entries.Remove(source);
                    continue;
                }

                // Already started and no longer playing = it finished, or someone stopped it directly.
                if (!entry.Suspended && pauseGameTime >= entry.Start && !source.isPlaying)
                {
                    entries.Remove(source);
                    continue;
                }

                entry.Suspended = true;
                source.Stop();
            }
        }

        internal static void Resume(double pauseGameTime)
        {
            scratch.Clear();
            scratch.AddRange(entries.Keys);
            foreach (AudioSource source in scratch)
            {
                Entry entry = entries[source];
                if (source == null)
                {
                    entries.Remove(source);
                    continue;
                }
                if (!entry.Suspended) continue;

                if (pauseGameTime < entry.Start)
                {
                    source.PlayScheduled(GameAudioClock.ToAudioTime(entry.Start));
                }
                else
                {
                    AudioClip clip = source.clip;
                    if (clip == null || clip.samples <= 0 || clip.frequency <= 0)
                    {
                        entries.Remove(source);
                        continue;
                    }

                    double played = (pauseGameTime - entry.Start) * clip.frequency * Math.Abs(source.pitch);
                    long sample = (long)Math.Round(played);
                    if (!source.loop && sample >= clip.samples)
                    {
                        entries.Remove(source);
                        continue;
                    }

                    source.timeSamples = (int)(sample % clip.samples);
                    source.PlayScheduled(GameAudioClock.ToAudioTime(pauseGameTime));
                }

                if (!double.IsPositiveInfinity(entry.End))
                    source.SetScheduledEndTime(GameAudioClock.ToAudioTime(entry.End));
            }
        }

        internal static void EndSuspension()
        {
            foreach (Entry entry in entries.Values) entry.Suspended = false;
        }
    }
}
