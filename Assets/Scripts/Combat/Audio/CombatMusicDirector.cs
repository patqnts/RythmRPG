using System;
using RythmRPG.Rhythm;
using RythmRPG.Rhythm.Audio;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Encounter music: a chart's Audio Clip (main layer, enemy turn) and Turn Audio Clip (player turn) are scheduled
    /// for the same dsp time and loop together for the whole fight, so they can never drift apart. Turn changes only
    /// cross-fade their volumes; nothing is restarted or seeked. Charts started while the music plays are aligned to
    /// the music's bar grid, so notes stay on the beat.
    /// </summary>
    public sealed class CombatMusicDirector : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Min(0f)] private float turnFadeSeconds = 0.6f;
        [SerializeField, Min(0f)] private float stopFadeSeconds = 0.8f;
        [SerializeField, Min(0.05f)] private float leadInSeconds = 0.15f;
        [Tooltip("Start each enemy chart on a bar line of the running music so its notes land on the music's beats.")]
        [SerializeField] private bool alignChartsToBars = true;

        private AudioSource mainSource;
        private AudioSource turnSource;
        private readonly float[] weights = { 1f, 0f };
        private int activeLayer;
        private float fadeSeconds = 0.6f;
        private float masterFade = 1f;
        private float masterFadeTarget = 1f;
        private TempoMap tempo;
        private float stopFadeDuration = 0.8f;

        public bool IsPlaying { get; private set; }
        public double DspStartTime { get; private set; }
        public AudioClip MainClip => mainSource != null ? mainSource.clip : null;
        public AudioClip TurnClip => turnSource != null ? turnSource.clip : null;

        /// <summary>
        /// Makes sure this chart's music is playing (starts it if another pair or nothing is playing). Returns true when
        /// the running music belongs to the chart, i.e. the chart can be aligned to it.
        /// </summary>
        public bool PlayForChart(RhythmChart chart)
        {
            if (chart == null || chart.AudioClip == null) return false;
            if (IsPlaying && MainClip == chart.AudioClip && TurnClip == chart.TurnAudioClip)
            {
                tempo = chart.CreateTempoMap();
                return true;
            }

            Play(chart.AudioClip, chart.TurnAudioClip, chart.CreateTempoMap());
            return true;
        }

        public void Play(AudioClip main, AudioClip turn, TempoMap tempoMap)
        {
            StopImmediate();
            if (main == null) return;
            EnsureSources();
            tempo = tempoMap;
            DspStartTime = AudioSettings.dspTime + leadInSeconds;
            Schedule(mainSource, main);
            Schedule(turnSource, turn);
            masterFade = masterFadeTarget = 1f;
            weights[0] = activeLayer == 0 || turn == null ? 1f : 0f;
            weights[1] = 1f - weights[0];
            ApplyVolumes();
            IsPlaying = true;
        }

        /// <summary>Player turn = turn layer, enemy turn = main layer. Cross-fades; the music keeps running.</summary>
        public void SetPlayerTurn(bool playerTurn, float fadeOverSeconds = -1f)
        {
            activeLayer = playerTurn && TurnClip != null ? 1 : 0;
            fadeSeconds = fadeOverSeconds >= 0f ? fadeOverSeconds : turnFadeSeconds;
        }

        /// <summary>
        /// Chart zero (dsp) for a chart that wants to start no earlier than <paramref name="earliestDsp"/>: the next bar
        /// line of the running music. Chart time equals clip time, so bar-shifted charts keep every note on the beat.
        /// </summary>
        public double AlignChartStart(double earliestDsp)
        {
            if (!alignChartsToBars || !IsPlaying || tempo == null || tempo.Segments.Count == 0) return earliestDsp;
            double barSeconds = 60d / Math.Max(1d, tempo.Segments[0].Bpm) * Math.Max(1, tempo.BeatsPerMeasure);
            if (barSeconds <= 0d) return earliestDsp;
            double bars = Math.Ceiling((earliestDsp - DspStartTime) / barSeconds - 1e-6d);
            return DspStartTime + Math.Max(0d, bars) * barSeconds;
        }

        /// <summary>Fades out and stops (end of the encounter).</summary>
        public void Stop(float fadeOverSeconds = -1f)
        {
            if (!IsPlaying) return;
            float fade = fadeOverSeconds >= 0f ? fadeOverSeconds : stopFadeSeconds;
            if (fade <= 0f)
            {
                StopImmediate();
                return;
            }

            masterFadeTarget = 0f;
            stopFadeDuration = fade;
        }

        private void Update()
        {
            if (!IsPlaying) return;
            LayerMixer.Step(weights, activeLayer, Time.unscaledDeltaTime, fadeSeconds);
            if (masterFade > masterFadeTarget)
            {
                masterFade = Mathf.Max(masterFadeTarget,
                    masterFade - Time.unscaledDeltaTime / Mathf.Max(0.0001f, stopFadeDuration));
                if (masterFade <= 0f)
                {
                    StopImmediate();
                    return;
                }
            }

            ApplyVolumes();
        }

        private void OnDisable() => StopImmediate();

        private void StopImmediate()
        {
            IsPlaying = false;
            if (mainSource != null) mainSource.Stop();
            if (turnSource != null) turnSource.Stop();
            masterFade = masterFadeTarget = 1f;
        }

        private void Schedule(AudioSource source, AudioClip clip)
        {
            source.clip = clip;
            if (clip == null) return;
            source.loop = true;
            source.timeSamples = 0;
            source.PlayScheduled(DspStartTime);
        }

        private void ApplyVolumes()
        {
            if (mainSource != null) mainSource.volume = weights[0] * volume * masterFade;
            if (turnSource != null) turnSource.volume = weights[1] * volume * masterFade;
        }

        private void EnsureSources()
        {
            if (mainSource == null) mainSource = CreateSource("Main");
            if (turnSource == null) turnSource = CreateSource("Turn");
        }

        private AudioSource CreateSource(string layerName)
        {
            GameObject child = new($"Combat Music ({layerName})");
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }
    }
}
