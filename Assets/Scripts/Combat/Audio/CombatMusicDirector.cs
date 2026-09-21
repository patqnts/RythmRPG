using System;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Combat music. The current sequence's <see cref="CombatSong"/> loops for as long as combat lasts; it is never
    /// restarted for a chart. Charts start on the song's next bar line (<see cref="NextBarDsp"/>), so every chart
    /// plays on the beat whatever moment of the song it lands on. Switching to a sequence with another song
    /// cross-fades to it. On the player turn the music is filtered (low-pass by default: muffled, "under water").
    /// </summary>
    public sealed class CombatMusicDirector : MonoBehaviour
    {
        public enum PlayerTurnFilter { LowPass, HighPass, None }

        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Min(0.05f)] private float leadInSeconds = 0.15f;
        [Tooltip("Cross-fade when a sequence with a different song starts.")]
        [SerializeField, Min(0f)] private float songChangeFadeSeconds = 1f;
        [SerializeField, Min(0f)] private float stopFadeSeconds = 0.8f;

        [Header("Player Turn Filter")]
        [Tooltip("LowPass = muffled, under water. HighPass = thin, tinny.")]
        [SerializeField] private PlayerTurnFilter playerTurnFilter = PlayerTurnFilter.LowPass;
        [Tooltip("Low-pass cutoff on the player turn (Hz). Lower = more muffled.")]
        [SerializeField, Range(100f, 22000f)] private float lowPassCutoff = 700f;
        [SerializeField, Range(1f, 10f)] private float lowPassResonance = 1.4f;
        [Tooltip("High-pass cutoff on the player turn (Hz). Higher = thinner.")]
        [SerializeField, Range(10f, 5000f)] private float highPassCutoff = 1200f;
        [Tooltip("Music volume on the player turn.")]
        [SerializeField, Range(0f, 1f)] private float playerTurnVolume = 0.85f;
        [SerializeField, Min(0f)] private float filterFadeSeconds = 0.5f;

        private const float OpenLowPass = 22000f;
        private const float OpenHighPass = 10f;
        /// <summary>Minimum scheduling lead so PlayScheduled lands sample-accurately.</summary>
        public const double MinLeadSeconds = 0.1d;

        private sealed class Voice
        {
            public AudioSource Source;
            public AudioLowPassFilter LowPass;
            public AudioHighPassFilter HighPass;
            public CombatSong Song;
            public double StartDsp;
            public float Gain;
            public float FadeSeconds;
            public bool FadingIn;
            public bool FadingOut;
        }

        private readonly Voice[] voices = new Voice[2];
        private int current = -1;
        private float filterAmount;
        private float filterTarget;

        public CombatSong CurrentSong => Current?.Song;
        /// <summary>True while a song is (or is about to be) playing and not fading out.</summary>
        public bool HasSong => Current != null && Current.Song != null && !Current.FadingOut
                               && (Current.Source.isPlaying || AudioSettings.dspTime < Current.StartDsp);

        private Voice Current => current >= 0 ? voices[current] : null;

        /// <summary>Starts the song (looping) unless it is already playing. A different song cross-fades in.</summary>
        public void PlaySong(CombatSong song)
        {
            if (song == null || song.Clip == null) return;
            if (HasSong && Current.Song == song) return;
            EnsureVoices();
            bool crossfade = Current != null && Current.Source.isPlaying && !Current.FadingOut;
            if (Current != null && Current.Source.isPlaying) BeginFadeOut(Current, crossfade ? songChangeFadeSeconds : 0f);

            int next = current < 0 ? 0 : 1 - current;
            Voice voice = voices[next];
            voice.Source.Stop();
            voice.Song = song;
            voice.Source.clip = song.Clip;
            voice.Source.loop = true;
            voice.Source.timeSamples = 0;
            voice.StartDsp = AudioSettings.dspTime + leadInSeconds;
            voice.Source.PlayScheduled(voice.StartDsp);
            voice.FadingOut = false;
            voice.FadingIn = crossfade && songChangeFadeSeconds > 0f;
            voice.FadeSeconds = songChangeFadeSeconds;
            voice.Gain = voice.FadingIn ? 0f : 1f;
            current = next;
            ApplyAll();
        }

        /// <summary>
        /// Dsp time of the running song's first bar line at or after <paramref name="earliestDsp"/> (accounts for the
        /// song's offset and loop). Returns <paramref name="earliestDsp"/> when no song plays.
        /// </summary>
        public double NextBarDsp(double earliestDsp)
        {
            if (!HasSong) return earliestDsp;
            Voice voice = Current;
            CombatSong song = voice.Song;
            double loop = song.Clip.length;
            double elapsed = earliestDsp - voice.StartDsp;
            if (elapsed < 0d) elapsed = 0d; // the song has not started yet: its first downbeat
            double loopsDone = loop > 0d ? Math.Floor(elapsed / loop) : 0d;
            double loopStartDsp = voice.StartDsp + loopsDone * loop;
            double inLoop = elapsed - loopsDone * loop;
            return loopStartDsp + CombatSong.NextBarInLoop(inLoop, song.AudioOffsetSeconds, song.BarSeconds, loop);
        }

        /// <summary>Player turn = filtered music, enemy turn = clean music. Fades; the music keeps running.</summary>
        public void SetPlayerTurn(bool playerTurn)
        {
            filterTarget = playerTurn && playerTurnFilter != PlayerTurnFilter.None ? 1f : 0f;
        }

        /// <summary>Fades out and stops (end of the encounter).</summary>
        public void Stop()
        {
            foreach (Voice voice in voices)
                if (voice != null && voice.Source.isPlaying) BeginFadeOut(voice, stopFadeSeconds);
            filterTarget = 0f;
        }

        private void BeginFadeOut(Voice voice, float seconds)
        {
            if (seconds <= 0f)
            {
                voice.Source.Stop();
                voice.Gain = 0f;
                voice.FadingOut = false;
                voice.FadingIn = false;
                return;
            }

            voice.FadingIn = false;
            voice.FadingOut = true;
            voice.FadeSeconds = seconds;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            filterAmount = Mathf.MoveTowards(filterAmount, filterTarget, filterFadeSeconds <= 0f ? 1f : dt / filterFadeSeconds);
            double dsp = AudioSettings.dspTime;
            foreach (Voice voice in voices)
            {
                if (voice == null) continue;
                float step = voice.FadeSeconds <= 0f ? 1f : dt / voice.FadeSeconds;
                if (voice.FadingIn && dsp >= voice.StartDsp)
                {
                    voice.Gain = Mathf.MoveTowards(voice.Gain, 1f, step);
                    if (voice.Gain >= 1f) voice.FadingIn = false;
                }
                else if (voice.FadingOut)
                {
                    voice.Gain = Mathf.MoveTowards(voice.Gain, 0f, step);
                    if (voice.Gain <= 0f)
                    {
                        voice.Source.Stop();
                        voice.FadingOut = false;
                    }
                }
            }

            ApplyAll();
        }

        private void OnDisable()
        {
            foreach (Voice voice in voices)
                if (voice != null) voice.Source.Stop();
            current = -1;
        }

        private void ApplyAll()
        {
            float t = Mathf.SmoothStep(0f, 1f, filterAmount);
            bool useLow = playerTurnFilter == PlayerTurnFilter.LowPass && t > 0f;
            bool useHigh = playerTurnFilter == PlayerTurnFilter.HighPass && t > 0f;
            foreach (Voice voice in voices)
            {
                if (voice == null) continue;
                float songVolume = voice.Song != null ? voice.Song.Volume : 1f;
                voice.Source.volume = volume * songVolume * voice.Gain * Mathf.Lerp(1f, playerTurnVolume, t);
                voice.LowPass.enabled = useLow;
                voice.HighPass.enabled = useHigh;
                // Interpolate in log-frequency so the sweep sounds even.
                if (useLow)
                {
                    voice.LowPass.cutoffFrequency = LogLerp(OpenLowPass, lowPassCutoff, t);
                    voice.LowPass.lowpassResonanceQ = Mathf.Lerp(1f, lowPassResonance, t);
                }
                if (useHigh) voice.HighPass.cutoffFrequency = LogLerp(OpenHighPass, highPassCutoff, t);
            }
        }

        private static float LogLerp(float from, float to, float t)
        {
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1f, from)), Mathf.Log(Mathf.Max(1f, to)), t));
        }

        private void EnsureVoices()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i] != null) continue;
                // One GameObject per voice: audio filters process the AudioSource on their own GameObject.
                GameObject child = new($"Combat Music {i}");
                child.transform.SetParent(transform, false);
                var voice = new Voice
                {
                    Source = child.AddComponent<AudioSource>(),
                    LowPass = child.AddComponent<AudioLowPassFilter>(),
                    HighPass = child.AddComponent<AudioHighPassFilter>()
                };
                voice.Source.playOnAwake = false;
                voice.Source.spatialBlend = 0f;
                voice.LowPass.enabled = false;
                voice.HighPass.enabled = false;
                voices[i] = voice;
            }
        }
    }
}
