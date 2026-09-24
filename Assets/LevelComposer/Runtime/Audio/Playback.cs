using System;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Simulation;
using UnityEngine;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>Clips for the five music sections (any may be null).</summary>
    public struct SectionClips
    {
        public AudioClip Intro, Loop, PlayerTurn, End, DefeatEnd;
    }

    /// <summary>
    /// Plays the preview's music on the audio (dsp) clock, following a <see cref="FlowPlan"/>: intro, loop (+ player-turn
    /// stem cross-fade, or a low-pass when there is no stem) and end, all sample-scheduled like the game's
    /// CombatMusicDirector. Also the preview clock, the metronome and hit sounds.
    /// </summary>
    public sealed class Playback
    {
        private const double ScheduleLead = 0.12d;
        private const double MetronomeLookahead = 0.25d;

        private readonly AudioSource intro, loop, stem, end, listen;
        private readonly AudioLowPassFilter loopFilter;
        private readonly AudioSource[] clickPool = new AudioSource[8];
        private readonly AudioSource[] hitPool = new AudioSource[12];
        private readonly AudioClip clickAccent, clickBeat, hitPerfect, hitGood, hitBad, hitMiss, keyTap;
        private int clickIndex, hitIndex;

        private FlowPlan plan;
        private SectionClips clips;
        private MusicSettings music = new MusicSettings();
        private bool playing;
        private double startT;
        private double dspStart;
        private double pausedAt;
        private double metronomeUntil;
        private double lastDsp = -1d, lastRealtime, lastReturned;

        public ComposerPrefs Prefs;
        public bool IsPlaying { get { return playing; } }
        public bool IsListening { get { return listen.isPlaying; } }
        /// <summary>Called when playback reaches the end of the plan.</summary>
        public event Action ReachedEnd;

        public Playback(GameObject host, ComposerPrefs prefs)
        {
            Prefs = prefs;
            intro = MakeSource(host, "Intro");
            loop = MakeSource(host, "Loop");
            loop.loop = true;
            loopFilter = loop.gameObject.AddComponent<AudioLowPassFilter>();
            loopFilter.cutoffFrequency = 22000f;
            stem = MakeSource(host, "Player Turn Stem");
            stem.loop = true;
            end = MakeSource(host, "End");
            listen = MakeSource(host, "Listen");
            for (int i = 0; i < clickPool.Length; i++) clickPool[i] = MakeSource(host, "Click " + i);
            for (int i = 0; i < hitPool.Length; i++) hitPool[i] = MakeSource(host, "Hit " + i);
            clickAccent = SynthClips.Click("click-accent", 1760f, 0.05f, 90f);
            clickBeat = SynthClips.Click("click-beat", 1180f, 0.04f, 110f, 0f, 0.55f);
            hitPerfect = SynthClips.Click("hit-perfect", 2350f, 0.06f, 70f, 0.25f);
            hitGood = SynthClips.Click("hit-good", 1900f, 0.05f, 80f, 0.3f);
            hitBad = SynthClips.Click("hit-bad", 1200f, 0.05f, 80f, 0.45f);
            hitMiss = SynthClips.Click("hit-miss", 140f, 0.12f, 30f, 0.2f);
            keyTap = SynthClips.Click("key-tap", 900f, 0.03f, 140f, 0.6f, 0.4f);
        }

        private static AudioSource MakeSource(GameObject host, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(host.transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        // ------------------------------------------------------------------ clock

        /// <summary>Preview time (seconds on the plan's clock).</summary>
        public double Time { get { return playing ? startT + (SmoothDsp() - dspStart) : pausedAt; } }

        private double SmoothDsp()
        {
            double dsp = AudioSettings.dspTime;
            double rt = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (dsp != lastDsp)
            {
                lastDsp = dsp;
                lastRealtime = rt;
            }

            // dspTime only moves once per audio buffer (~20 ms); extrapolate between updates, never backwards.
            double estimate = lastDsp + Math.Min(0.1d, rt - lastRealtime);
            if (estimate < lastReturned) estimate = lastReturned;
            lastReturned = estimate;
            return estimate;
        }

        /// <summary>The dsp time at which preview time <paramref name="t"/> is heard (only meaningful while playing).</summary>
        public double DspAt(double t) { return dspStart + (t - startT); }

        // ------------------------------------------------------------------ plan

        /// <summary>Sets what to play. While playing, the music is rescheduled at the current time.</summary>
        public void SetPlan(FlowPlan newPlan, SectionClips newClips, MusicSettings settings)
        {
            bool sameClips = newClips.Intro == clips.Intro && newClips.Loop == clips.Loop && newClips.PlayerTurn == clips.PlayerTurn && newClips.End == clips.End;
            bool samePlan = plan != null && newPlan != null && Math.Abs(plan.LoopStart - newPlan.LoopStart) < 1e-9
                && Math.Abs(plan.EndStart - newPlan.EndStart) < 1e-9 && Math.Abs(plan.IntroEnd - newPlan.IntroEnd) < 1e-9;
            plan = newPlan;
            clips = newClips;
            music = settings ?? new MusicSettings();
            if (playing && !(sameClips && samePlan)) Reschedule(Time);
        }

        public void Play(double from)
        {
            StopListening();
            StopSources();
            startT = from;
            dspStart = AudioSettings.dspTime + ScheduleLead;
            lastReturned = 0d;
            playing = true;
            metronomeUntil = from;
            Schedule(from);
            ApplyVolumes(from);
        }

        public void Pause()
        {
            if (!playing) return;
            pausedAt = Math.Max(startT, Time);
            playing = false;
            StopSources();
        }

        public void Seek(double t)
        {
            if (playing) Play(t);
            else pausedAt = t;
        }

        private void Reschedule(double at)
        {
            Play(at);
        }

        private void StopSources()
        {
            intro.Stop();
            loop.Stop();
            stem.Stop();
            end.Stop();
            foreach (AudioSource s in clickPool) s.Stop();
        }

        private void Schedule(double from)
        {
            if (plan == null) return;
            // Intro.
            if (clips.Intro != null && plan.HasIntro && from < plan.IntroEnd)
                ScheduleClip(intro, clips.Intro, plan.IntroStart, from, plan.IntroEnd, false);
            // Loop and its player-turn stem (always running together, sample-locked).
            if (clips.Loop != null && from < plan.EndStart)
            {
                ScheduleLoop(loop, clips.Loop, from);
                if (clips.PlayerTurn != null) ScheduleLoop(stem, clips.PlayerTurn, from);
            }

            // End.
            if (clips.End != null && !double.IsInfinity(plan.EndStart) && from < plan.EndStart + (double)clips.End.samples / clips.End.frequency)
                ScheduleClip(end, clips.End, plan.EndStart, from, double.PositiveInfinity, false);
        }

        private void ScheduleClip(AudioSource src, AudioClip clip, double clipStart, double from, double stopAt, bool looping)
        {
            src.clip = clip;
            src.loop = looping;
            double offset = from - clipStart;
            if (offset >= 0d)
            {
                src.timeSamples = Mathf.Clamp((int)(offset * clip.frequency), 0, clip.samples - 1);
                src.PlayScheduled(dspStart);
            }
            else
            {
                src.timeSamples = 0;
                src.PlayScheduled(dspStart - offset);
            }

            if (!double.IsInfinity(stopAt)) src.SetScheduledEndTime(dspStart + (stopAt - from));
        }

        private void ScheduleLoop(AudioSource src, AudioClip clip, double from)
        {
            src.clip = clip;
            src.loop = true;
            if (from >= plan.LoopStart)
            {
                double clipT = plan.LoopClipTime(from);
                if (double.IsNaN(clipT)) clipT = 0d;
                // A stem shorter than the loop wraps on its own length.
                double len = (double)clip.samples / clip.frequency;
                if (len > 0d) clipT %= len;
                src.timeSamples = Mathf.Clamp((int)(clipT * clip.frequency), 0, clip.samples - 1);
                src.PlayScheduled(dspStart);
            }
            else
            {
                src.timeSamples = 0;
                src.PlayScheduled(dspStart + (plan.LoopStart - from));
            }

            if (!double.IsInfinity(plan.EndStart)) src.SetScheduledEndTime(dspStart + (plan.EndStart - from));
        }

        // ------------------------------------------------------------------ per frame

        public void Update(LevelTempoInfo tempo)
        {
            if (!playing || plan == null) return;
            double t = Time;
            ApplyVolumes(t);
            if (Prefs.Metronome) ScheduleMetronome(t, tempo);
            else metronomeUntil = t;
            if (t >= plan.Duration)
            {
                Pause();
                pausedAt = plan.Duration;
                if (ReachedEnd != null) ReachedEnd();
            }
        }

        private void ApplyVolumes(double t)
        {
            float master = Prefs.MusicVolume * (float)music.Volume;
            float w = plan != null ? (float)plan.PlayerTurnWeight(t) : 0f;
            intro.volume = master;
            end.volume = master;
            if (clips.PlayerTurn != null)
            {
                loop.volume = master * Mathf.Lerp(1f, (float)music.MainVolumeOnPlayerTurn, w);
                stem.volume = master * (float)music.PlayerTurnVolume * w;
                loopFilter.cutoffFrequency = 22000f;
            }
            else
            {
                loop.volume = master;
                // No stem: the game filters the loop on the player turn.
                loopFilter.cutoffFrequency = Mathf.Lerp(22000f, 900f, w);
            }

            listen.volume = master;
        }

        private void ScheduleMetronome(double t, LevelTempoInfo tempo)
        {
            double horizon = t + MetronomeLookahead;
            if (metronomeUntil < t) metronomeUntil = t;
            double spb = tempo.SecondsPerBeat;
            double origin = tempo.GridOrigin;
            double k = Math.Ceiling((metronomeUntil - origin) / spb - 1e-9);
            for (int guard = 0; guard < 64; guard++, k++)
            {
                double beatTime = origin + k * spb;
                if (beatTime > horizon) break;
                if (beatTime < t - 0.01d) continue;
                bool accent = ((long)Math.Round(k) % tempo.BeatsPerBar + tempo.BeatsPerBar) % tempo.BeatsPerBar == 0;
                AudioSource s = clickPool[clickIndex++ % clickPool.Length];
                s.clip = accent ? clickAccent : clickBeat;
                s.volume = Prefs.MetronomeVolume;
                s.PlayScheduled(DspAt(beatTime));
            }

            // Every beat up to the horizon is scheduled now.
            metronomeUntil = Math.Max(metronomeUntil, horizon);
        }

        /// <summary>Plays a hit sound for a judgement at preview time <paramref name="at"/> (now if already past).</summary>
        public void PlayHit(Judgement j, double at)
        {
            if (!Prefs.HitSounds || !playing) return;
            AudioClip c;
            switch (j)
            {
                case Judgement.Perfect: c = hitPerfect; break;
                case Judgement.Good: c = hitGood; break;
                case Judgement.Bad: c = hitBad; break;
                case Judgement.Miss: c = hitMiss; break;
                default: c = keyTap; break;
            }

            AudioSource s = hitPool[hitIndex++ % hitPool.Length];
            s.Stop();
            s.clip = c;
            s.volume = Prefs.HitSoundVolume * (j == Judgement.None ? 0.6f : 1f);
            double dsp = DspAt(at);
            if (dsp > AudioSettings.dspTime + 0.005d) s.PlayScheduled(dsp);
            else s.Play();
        }

        // ------------------------------------------------------------------ audition

        /// <summary>Plays one clip on its own (Music panel "listen" buttons). Stops the preview.</summary>
        public void Listen(AudioClip clip)
        {
            if (playing) Pause();
            listen.Stop();
            if (clip == null) return;
            listen.clip = clip;
            listen.loop = false;
            listen.volume = Prefs.MusicVolume * (float)music.Volume;
            listen.Play();
        }

        public AudioClip ListeningTo { get { return listen.isPlaying ? listen.clip : null; } }
        public float ListenProgress { get { return listen.isPlaying && listen.clip != null ? (float)listen.timeSamples / Mathf.Max(1, listen.clip.samples) : 0f; } }

        public void StopListening()
        {
            listen.Stop();
        }
    }

    /// <summary>What the metronome needs to know about the grid.</summary>
    public struct LevelTempoInfo
    {
        public double SecondsPerBeat;
        public int BeatsPerBar;
        /// <summary>Preview time of a downbeat (grid origin).</summary>
        public double GridOrigin;
    }
}
