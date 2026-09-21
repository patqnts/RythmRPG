using System;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Encounter music: a chart's Audio Clip loops for the whole fight. On the player turn the music is filtered
    /// (low-pass by default, a muffled "under water" sound) and opens up again on the enemy turn. Nothing is
    /// restarted or seeked, so the beat never jumps. Charts started while the music plays are aligned to its bar
    /// grid, so notes stay on the beat.
    /// </summary>
    public sealed class CombatMusicDirector : MonoBehaviour
    {
        public enum PlayerTurnFilter { LowPass, HighPass, None }

        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Min(0.05f)] private float leadInSeconds = 0.15f;
        [SerializeField, Min(0f)] private float stopFadeSeconds = 0.8f;
        [Tooltip("Start each enemy chart on a bar line of the running music so its notes land on the music's beats.")]
        [SerializeField] private bool alignChartsToBars = true;

        [Header("Player Turn Filter")]
        [Tooltip("LowPass = muffled, under water. HighPass = thin, tinny.")]
        [SerializeField] private PlayerTurnFilter playerTurnFilter = PlayerTurnFilter.LowPass;
        [Tooltip("Low-pass cutoff on the player turn (Hz). Lower = more muffled.")]
        [SerializeField, Range(100f, 22000f)] private float lowPassCutoff = 700f;
        [SerializeField, Range(1f, 10f)] private float lowPassResonance = 1.4f;
        [Tooltip("High-pass cutoff on the player turn (Hz). Higher = thinner.")]
        [SerializeField, Range(10f, 5000f)] private float highPassCutoff = 1200f;
        [Tooltip("Music volume on the player turn (the filter also removes energy, so you may want this at 1).")]
        [SerializeField, Range(0f, 1f)] private float playerTurnVolume = 0.85f;
        [SerializeField, Min(0f)] private float filterFadeSeconds = 0.5f;

        private const float OpenLowPass = 22000f;
        private const float OpenHighPass = 10f;

        private AudioSource source;
        private AudioLowPassFilter lowPass;
        private AudioHighPassFilter highPass;
        private TempoMap tempo;
        private float filterAmount;        // 0 = open (enemy turn), 1 = fully filtered (player turn)
        private float filterTarget;
        private float masterFade = 1f;
        private bool stopping;

        public bool IsPlaying { get; private set; }
        public double DspStartTime { get; private set; }
        public AudioClip MainClip => source != null ? source.clip : null;

        /// <summary>
        /// Makes sure this chart's music is playing (starts it if another clip or nothing is playing). Returns true when
        /// the running music belongs to the chart, i.e. the chart can be aligned to it.
        /// </summary>
        public bool PlayForChart(RhythmChart chart)
        {
            if (chart == null || chart.AudioClip == null) return false;
            if (IsPlaying && !stopping && MainClip == chart.AudioClip)
            {
                tempo = chart.CreateTempoMap();
                return true;
            }

            Play(chart.AudioClip, chart.CreateTempoMap());
            return true;
        }

        public void Play(AudioClip clip, TempoMap tempoMap)
        {
            StopImmediate();
            if (clip == null) return;
            EnsureSource();
            tempo = tempoMap;
            DspStartTime = AudioSettings.dspTime + leadInSeconds;
            source.clip = clip;
            source.loop = true;
            source.timeSamples = 0;
            source.PlayScheduled(DspStartTime);
            masterFade = 1f;
            stopping = false;
            IsPlaying = true;
            ApplyFilter();
        }

        /// <summary>Player turn = filtered music, enemy turn = clean music. Fades; the music keeps running.</summary>
        public void SetPlayerTurn(bool playerTurn)
        {
            filterTarget = playerTurn && playerTurnFilter != PlayerTurnFilter.None ? 1f : 0f;
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
        public void Stop()
        {
            if (!IsPlaying) return;
            if (stopFadeSeconds <= 0f) StopImmediate();
            else stopping = true;
        }

        private void Update()
        {
            if (!IsPlaying) return;
            float dt = Time.unscaledDeltaTime;
            float step = filterFadeSeconds <= 0f ? 1f : dt / filterFadeSeconds;
            filterAmount = Mathf.MoveTowards(filterAmount, filterTarget, step);
            if (stopping)
            {
                masterFade = Mathf.MoveTowards(masterFade, 0f, dt / Mathf.Max(0.0001f, stopFadeSeconds));
                if (masterFade <= 0f)
                {
                    StopImmediate();
                    return;
                }
            }

            ApplyFilter();
        }

        private void OnDisable() => StopImmediate();

        private void StopImmediate()
        {
            IsPlaying = false;
            stopping = false;
            if (source != null) source.Stop();
            masterFade = 1f;
            filterAmount = filterTarget = 0f;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (source == null) return;
            float t = Mathf.SmoothStep(0f, 1f, filterAmount);
            source.volume = volume * masterFade * Mathf.Lerp(1f, playerTurnVolume, t);

            bool useLow = playerTurnFilter == PlayerTurnFilter.LowPass && t > 0f;
            bool useHigh = playerTurnFilter == PlayerTurnFilter.HighPass && t > 0f;
            lowPass.enabled = useLow;
            highPass.enabled = useHigh;
            // Interpolate in log-frequency so the sweep sounds even.
            if (useLow)
            {
                lowPass.cutoffFrequency = LogLerp(OpenLowPass, lowPassCutoff, t);
                lowPass.lowpassResonanceQ = Mathf.Lerp(1f, lowPassResonance, t);
            }
            if (useHigh) highPass.cutoffFrequency = LogLerp(OpenHighPass, highPassCutoff, t);
        }

        private static float LogLerp(float from, float to, float t)
        {
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1f, from)), Mathf.Log(Mathf.Max(1f, to)), t));
        }

        private void EnsureSource()
        {
            if (source != null) return;
            GameObject child = new("Combat Music");
            child.transform.SetParent(transform, false);
            source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            // Filters must sit on the same GameObject as the source they process.
            lowPass = child.AddComponent<AudioLowPassFilter>();
            highPass = child.AddComponent<AudioHighPassFilter>();
            lowPass.enabled = false;
            highPass.enabled = false;
        }
    }
}
