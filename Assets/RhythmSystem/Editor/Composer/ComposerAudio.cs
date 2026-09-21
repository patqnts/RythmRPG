using System;
using System.Collections.Generic;
using RythmRPG.Rhythm.Audio;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor.Composer
{
    /// <summary>
    /// Editor-side audio for the composer. Uses real AudioSources on a hidden object (they play in Edit Mode):
    /// the main and turn layers are scheduled for the same dsp time so they stay sample-locked, and metronome
    /// ticks are scheduled a little ahead on their own sources, on top of the music. Tempo, offset, listen
    /// layer and metronome can all change while playing without restarting the music.
    /// </summary>
    internal sealed class ComposerAudio : IDisposable
    {
        public enum Listen { Main, Turn, Both }

        private const double LeadSeconds = 0.08d;
        private const double ScheduleAheadSeconds = 0.25d;
        private const int ClickVoices = 8;
        private const float ClickGain = 0.7f;

        private GameObject host;
        private AudioSource mainSource;
        private AudioSource turnSource;
        private readonly List<AudioSource> clickSources = new List<AudioSource>();
        private AudioClip accentClick;
        private AudioClip normalClick;
        private int nextClick;

        private bool playing;
        private double dspStart;
        private double startSeconds;
        private double scheduledUntil;
        private TempoMap tempo;
        private bool metronome;
        private Listen listen = Listen.Main;

        // Smoothed dsp clock (dspTime advances in audio-buffer steps).
        private double lastDsp = -1d;
        private double realtimeAtDsp;

        public bool PlaybackAvailable { get { return true; } }
        public bool IsPlaying { get { return playing; } }

        /// <summary>Extracts a waveform summary. Returns null with an explanation when Unity will not hand over the samples.</summary>
        public static WaveformPeaks BuildPeaks(AudioClip clip, out string error)
        {
            error = null;
            if (clip == null) return null;
            try
            {
                clip.LoadAudioData();
                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0))
                {
                    error = "Waveform unavailable: set the clip's Load Type to Decompress On Load (or Compressed In Memory).";
                    return null;
                }

                return WaveformPeaks.Build(data, clip.channels, clip.frequency);
            }
            catch (Exception e)
            {
                error = "Waveform unavailable: " + e.Message;
                return null;
            }
        }

        /// <summary>Warning text when the turn layer cannot stay in step with the main layer, otherwise null.</summary>
        public static string CheckLayers(AudioClip main, AudioClip turn)
        {
            if (main == null || turn == null) return null;
            if (main.frequency != turn.frequency)
                return "Turn audio sample rate (" + turn.frequency + ") differs from main (" + main.frequency + "): layers will drift.";
            double diff = Math.Abs(main.length - turn.length);
            if (diff > 0.005d)
                return "Turn audio is " + diff.ToString("0.000") + " s " + (turn.length > main.length ? "longer" : "shorter") + " than main: loops will drift.";
            return null;
        }

        /// <summary>Smoothed AudioSettings.dspTime. Use it as the playhead's time source so the playhead follows the audio.</summary>
        public double Clock()
        {
            double dsp = AudioSettings.dspTime;
            double now = EditorApplication.timeSinceStartup;
            if (dsp != lastDsp)
            {
                lastDsp = dsp;
                realtimeAtDsp = now;
                return dsp;
            }

            return dsp + Math.Min(0.05d, Math.Max(0d, now - realtimeAtDsp));
        }

        /// <summary>Starts both layers at <paramref name="fromSeconds"/> (clip time). Returns the Clock() time at which audio begins.</summary>
        public double Play(double fromSeconds, AudioClip main, AudioClip turn, TempoMap tempoMap, bool metronomeOn, Listen listenTo)
        {
            Stop();
            EnsureHost();
            tempo = tempoMap;
            metronome = metronomeOn;
            listen = listenTo;
            startSeconds = Math.Max(0d, fromSeconds);
            dspStart = AudioSettings.dspTime + LeadSeconds;
            lastDsp = -1d;
            StartLayer(mainSource, main);
            StartLayer(turnSource, turn);
            ApplyListen();
            scheduledUntil = startSeconds;
            playing = true;
            ScheduleClicks();
            return dspStart;
        }

        public void Stop()
        {
            playing = false;
            if (mainSource != null) mainSource.Stop();
            if (turnSource != null) turnSource.Stop();
            StopClicks();
        }

        public void SetListen(Listen value)
        {
            listen = value;
            ApplyListen();
        }

        public void SetMetronome(bool on)
        {
            metronome = on;
            if (!playing) return;
            StopClicks();
            scheduledUntil = Math.Max(startSeconds, CurrentSeconds());
            ScheduleClicks();
        }

        /// <summary>BPM, beats per bar or offset changed: re-place the metronome ticks. The music keeps playing.</summary>
        public void Retime(TempoMap tempoMap)
        {
            tempo = tempoMap;
            if (!playing) return;
            StopClicks();
            scheduledUntil = Math.Max(startSeconds, CurrentSeconds());
            ScheduleClicks();
        }

        /// <summary>Call every editor tick while playing.</summary>
        public void Update()
        {
            if (playing) ScheduleClicks();
        }

        public void Dispose()
        {
            Stop();
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            host = null;
            if (accentClick != null) UnityEngine.Object.DestroyImmediate(accentClick);
            if (normalClick != null) UnityEngine.Object.DestroyImmediate(normalClick);
            accentClick = normalClick = null;
            clickSources.Clear();
        }

        private double CurrentSeconds()
        {
            return startSeconds + Math.Max(0d, AudioSettings.dspTime - dspStart);
        }

        private void StartLayer(AudioSource source, AudioClip clip)
        {
            source.clip = clip;
            if (clip == null || startSeconds >= clip.length) return;
            source.timeSamples = Mathf.Clamp((int)Math.Round(startSeconds * clip.frequency), 0, Math.Max(0, clip.samples - 1));
            source.PlayScheduled(dspStart);
        }

        private void ApplyListen()
        {
            if (mainSource == null) return;
            bool hasTurn = turnSource.clip != null;
            mainSource.volume = listen == Listen.Turn && hasTurn ? 0f : 1f;
            turnSource.volume = listen == Listen.Main || !hasTurn ? 0f : 1f;
        }

        private void ScheduleClicks()
        {
            if (!metronome || tempo == null || clickSources.Count == 0) return;
            double dspNow = AudioSettings.dspTime;
            double until = startSeconds + Math.Max(0d, dspNow + ScheduleAheadSeconds - dspStart);
            if (until <= scheduledUntil) return;
            var ticks = new List<MetronomeTick>();
            MetronomeClicks.CollectTicks(tempo, scheduledUntil, until, ticks);
            scheduledUntil = until;
            for (int i = 0; i < ticks.Count; i++)
            {
                double at = dspStart + (ticks[i].Seconds - startSeconds);
                if (at < dspNow) continue;
                AudioSource voice = clickSources[nextClick];
                nextClick = (nextClick + 1) % clickSources.Count;
                voice.clip = ticks[i].IsDownbeat ? accentClick : normalClick;
                voice.PlayScheduled(at);
            }
        }

        private void StopClicks()
        {
            foreach (AudioSource voice in clickSources)
                if (voice != null) voice.Stop();
        }

        private void EnsureHost()
        {
            if (host != null) return;
            host = EditorUtility.CreateGameObjectWithHideFlags("Rhythm Composer Audio", HideFlags.HideAndDontSave);
            mainSource = AddSource();
            turnSource = AddSource();
            clickSources.Clear();
            for (int i = 0; i < ClickVoices; i++) clickSources.Add(AddSource());
            accentClick = BuildClick("ComposerClickAccent", 1800d, ClickGain);
            normalClick = BuildClick("ComposerClick", 1200d, ClickGain * 0.7f);
            // Edit Mode audio needs a listener; add one only when the open scenes have none.
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null) host.AddComponent<AudioListener>();
        }

        private AudioSource AddSource()
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.bypassListenerEffects = true;
            return source;
        }

        private static AudioClip BuildClick(string name, double frequency, float gain)
        {
            const int rate = 44100;
            int frames = Math.Max(1, (int)(MetronomeClicks.ClickSeconds * rate));
            var data = new float[frames];
            for (int k = 0; k < frames; k++)
            {
                float env = 1f - (float)k / frames;
                data[k] = (float)Math.Sin(2d * Math.PI * frequency * k / rate) * env * env * gain;
            }

            AudioClip clip = AudioClip.Create(name, frames, 1, rate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(data, 0);
            return clip;
        }
    }
}
