using System;
using RythmRPG.Rhythm;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Combat music, played in sections (see <see cref="CombatSong"/>):
    /// <b>Intro</b> once at the start of the encounter → <b>Loop</b> for as long as combat lasts, with a sample-locked
    /// <b>Player Turn</b> stem cross-faded in on the player turn (or a filter when the song has no stem) → <b>End</b>
    /// once when someone dies. Every hand-over is scheduled on the audio clock (<c>PlayScheduled</c> /
    /// <c>SetScheduledEndTime</c>), so there are no gaps: the loop starts on the intro's last sample and the ending
    /// starts on the loop's next bar line, exactly when the loop stops. The loop is never restarted for a chart:
    /// charts start on the song's next bar line (<see cref="NextBarDsp"/>). Switching to a sequence with another song
    /// cross-fades to that song's loop on the next bar.
    /// </summary>
    public sealed class CombatMusicDirector : MonoBehaviour
    {
        public enum PlayerTurnFilter { LowPass, HighPass, None }
        public enum Section { None, Intro, Loop, End }

        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [Tooltip("Delay before the first section starts, so it is scheduled sample-accurately.")]
        [SerializeField, Min(0.05f)] private float leadInSeconds = 0.15f;
        [Tooltip("Cross-fade when a sequence with a different song starts (begins on the current song's next bar).")]
        [SerializeField, Min(0f)] private float songChangeFadeSeconds = 1f;
        [Tooltip("Fade-out when the battle is cancelled, or when it ends and the song has no End clip.")]
        [SerializeField, Min(0f)] private float stopFadeSeconds = 0.8f;

        [Header("Player Turn")]
        [Tooltip("When the switch to / from the player-turn mix begins. Next Beat keeps the cross-fade on the groove.")]
        [SerializeField] private MusicSync turnSwitchSync = MusicSync.NextBeat;
        [FormerlySerializedAs("filterFadeSeconds")]
        [Tooltip("Cross-fade length between the loop and the player-turn mix.")]
        [SerializeField, Min(0f)] private float playerTurnFadeSeconds = 0.5f;

        [Header("Player Turn Filter (songs without a Player Turn clip)")]
        [Tooltip("LowPass = muffled, under water. HighPass = thin, tinny. None = no change.")]
        [SerializeField] private PlayerTurnFilter playerTurnFilter = PlayerTurnFilter.LowPass;
        [Tooltip("Low-pass cutoff on the player turn (Hz). Lower = more muffled.")]
        [SerializeField, Range(100f, 22000f)] private float lowPassCutoff = 700f;
        [SerializeField, Range(1f, 10f)] private float lowPassResonance = 1.4f;
        [Tooltip("High-pass cutoff on the player turn (Hz). Higher = thinner.")]
        [SerializeField, Range(10f, 5000f)] private float highPassCutoff = 1200f;
        [Tooltip("Music volume on the player turn (filter mode only).")]
        [SerializeField, Range(0f, 1f)] private float playerTurnVolume = 0.85f;

        private const float OpenLowPass = 22000f;
        private const float OpenHighPass = 10f;
        /// <summary>Minimum scheduling lead so PlayScheduled lands sample-accurately.</summary>
        public const double MinLeadSeconds = 0.1d;

        /// <summary>One song's sources. Two decks let one song fade out while the next fades in.</summary>
        private sealed class Deck
        {
            public AudioSource Intro;
            public AudioSource Main;
            public AudioLowPassFilter MainLowPass;
            public AudioHighPassFilter MainHighPass;
            public AudioSource Layer;
            public AudioSource End;

            public CombatSong Song;
            public bool Alive;
            public double IntroStartDsp;
            public double LoopStartDsp;
            public bool Ending;
            public double EndDsp = double.MaxValue;
            public double EndSeconds;

            public float Gain = 1f;
            public bool FadingIn;
            public bool FadingOut;
            public float FadeSeconds;
            public double FadeStartDsp;

            public bool HasIntro => Intro.clip != null;
        }

        private readonly Deck[] decks = new Deck[2];
        private int current = -1;
        private float turnMix;
        private float turnTarget;
        private double turnSwitchDsp;

        private Deck Current => current >= 0 ? decks[current] : null;

        public CombatSong CurrentSong => Current?.Song;

        /// <summary>True while a song is (or is about to be) playing its intro or loop: not ending, not fading out.</summary>
        public bool HasSong => Current != null && Current.Alive && Current.Song != null && !Current.Ending && !Current.FadingOut;

        /// <summary>True once the battle-end music has been triggered (the End section is scheduled or playing).</summary>
        public bool IsEnding => Current != null && Current.Alive && Current.Ending;

        /// <summary>Dsp time the loop starts (right after the intro), or 0 when nothing plays.</summary>
        public double LoopStartDsp => Current != null && Current.Alive ? Current.LoopStartDsp : 0d;

        /// <summary>True while the song's intro is (or is about to be) playing, i.e. before its loop has started.</summary>
        public bool IsPlayingIntro => HasSong && AudioSettings.dspTime < Current.LoopStartDsp;

        /// <summary>Section the audio output is in right now.</summary>
        public Section CurrentSection
        {
            get
            {
                Deck deck = Current;
                if (deck == null || !deck.Alive) return Section.None;
                double dsp = AudioSettings.dspTime;
                if (deck.Ending && dsp >= deck.EndDsp) return Section.End;
                return deck.HasIntro && dsp < deck.LoopStartDsp ? Section.Intro : Section.Loop;
            }
        }

        // ---------- Sections ----------

        /// <summary>
        /// Loads every section's audio data ahead of time. Clips imported with "Preload Audio Data" off are otherwise
        /// only loaded when first played, which can make a scheduled section (the loop after the intro, the ending)
        /// start late. Call it as early as possible (encounter start); calling it again is cheap.
        /// </summary>
        public static void Preload(CombatSong song)
        {
            if (song == null) return;
            Load(song.IntroClip);
            Load(song.Clip);
            Load(song.PlayerTurnClip);
            Load(song.EndClip);
            Load(song.DefeatEndClip);
        }

        private static void Load(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        /// <summary>
        /// Encounter start (call it once the player is in place): Intro, then the Loop from the intro's last sample.
        /// Does nothing if this song is already playing.
        /// </summary>
        public void BeginEncounter(CombatSong song)
        {
            if (song == null || song.Clip == null) return;
            if (HasSong && Current.Song == song) return;
            EnsureDecks();
            foreach (Deck deck in decks)
                if (deck.Alive) BeginFadeOut(deck, deck.Ending ? stopFadeSeconds : songChangeFadeSeconds, AudioSettings.dspTime);

            int next = current < 0 ? 0 : 1 - current;
            StartDeck(decks[next], song, AudioSettings.dspTime + leadInSeconds, withIntro: true);
            current = next;
            turnMix = turnTarget = 0f;
            ApplyAll();
        }

        /// <summary>
        /// Makes sure <paramref name="song"/> is playing. Nothing playing yet: same as <see cref="BeginEncounter"/>.
        /// Another song playing: this song's loop (no intro) cross-fades in on the current song's next bar line.
        /// </summary>
        public void PlaySong(CombatSong song)
        {
            if (song == null || song.Clip == null) return;
            if (HasSong && Current.Song == song) return;
            if (!HasSong)
            {
                BeginEncounter(song);
                return;
            }

            EnsureDecks();
            Deck previous = Current;
            double startDsp = GridDsp(previous, AudioSettings.dspTime + MinLeadSeconds, MusicSync.NextBar, false);
            bool fade = songChangeFadeSeconds > 0f;
            BeginFadeOut(previous, songChangeFadeSeconds, startDsp);

            int next = 1 - current;
            Deck deck = decks[next];
            StartDeck(deck, song, startDsp, withIntro: false);
            deck.FadingIn = fade;
            deck.FadeSeconds = songChangeFadeSeconds;
            deck.FadeStartDsp = startDsp;
            deck.Gain = fade ? 0f : 1f;
            current = next;
            ApplyAll();
        }

        /// <summary>
        /// Battle end (enemy or player death): the song's End clip starts on the loop's next bar (or beat, see the
        /// song's End Sync) and the loop stops on that same sample. Without an End clip the music fades out.
        /// Calling it again while the ending is already scheduled does nothing. Returns the dsp time the ending starts.
        /// </summary>
        public double PlayEnd(bool victory)
        {
            Deck deck = Current;
            double now = AudioSettings.dspTime;
            if (deck == null || !deck.Alive || deck.FadingOut) return now;
            if (deck.Ending) return deck.EndDsp;

            AudioClip endClip = deck.Song != null ? deck.Song.EndClipFor(victory) : null;
            if (endClip == null)
            {
                Stop();
                return now;
            }

            double endDsp = GridDsp(deck, now + MinLeadSeconds, deck.Song.EndSync, false);
            if (endDsp < deck.LoopStartDsp) CutAt(deck.Intro, endDsp, deck.IntroStartDsp); // still in the intro
            CutAt(deck.Main, endDsp, deck.LoopStartDsp);
            CutAt(deck.Layer, endDsp, deck.LoopStartDsp);

            deck.End.clip = endClip;
            deck.End.loop = false;
            deck.End.timeSamples = 0;
            deck.End.PlayScheduled(endDsp);
            deck.EndDsp = endDsp;
            deck.EndSeconds = CombatSong.ExactSeconds(endClip);
            deck.Ending = true;
            ApplyAll();
            return endDsp;
        }

        /// <summary>Player turn = player-turn stem (or filtered loop); enemy turn = full loop. The music keeps running.</summary>
        public void SetPlayerTurn(bool playerTurn)
        {
            float target = playerTurn ? 1f : 0f;
            if (Mathf.Approximately(target, turnTarget)) return;
            turnTarget = target;
            double now = AudioSettings.dspTime;
            turnSwitchDsp = HasSong ? GridDsp(Current, now, turnSwitchSync, false) : now;
        }

        /// <summary>
        /// Where a chart's beat 0 may land: the running song's first bar line (or beat, per the song's Chart Sync) at or
        /// after <paramref name="earliestDsp"/> (accounts for the intro, the loop's offset and its wrap). With the song's
        /// "Charts Wait For Loop" on, never earlier than the loop's first downbeat. Returns
        /// <paramref name="earliestDsp"/> when no song plays.
        /// </summary>
        public double NextBarDsp(double earliestDsp)
        {
            if (!HasSong) return earliestDsp;
            return GridDsp(Current, earliestDsp, Current.Song.ChartSync, Current.Song.ChartsWaitForLoop);
        }

        /// <summary>Dsp time of the running song's next beat (or bar) at or after <paramref name="earliestDsp"/>.</summary>
        public double NextGridDsp(double earliestDsp, MusicSync sync)
        {
            return HasSong ? GridDsp(Current, earliestDsp, sync, false) : earliestDsp;
        }

        /// <summary>Fades everything out and stops (battle cancelled, or it ended without an End clip).</summary>
        public void Stop()
        {
            double now = AudioSettings.dspTime;
            foreach (Deck deck in decks)
                if (deck != null && deck.Alive) BeginFadeOut(deck, stopFadeSeconds, now);
            turnTarget = 0f;
        }

        // ---------- Internals ----------

        private static double GridDsp(Deck deck, double earliest, MusicSync sync, bool waitForLoop)
        {
            CombatSong song = deck.Song;
            if (song == null) return earliest;
            return CombatSong.NextGridTime(earliest, deck.LoopStartDsp, song.LoopSeconds, song.AudioOffsetSeconds,
                song.GridSeconds(sync), waitForLoop);
        }

        private static void StartDeck(Deck deck, CombatSong song, double startDsp, bool withIntro)
        {
            Preload(song);
            StopDeck(deck);
            deck.Song = song;
            deck.Alive = true;
            deck.Ending = false;
            deck.EndDsp = double.MaxValue;
            deck.Gain = 1f;
            deck.FadingIn = deck.FadingOut = false;
            deck.IntroStartDsp = startDsp;

            AudioClip intro = withIntro ? song.IntroClip : null;
            deck.Intro.clip = intro;
            double loopStart = startDsp;
            if (intro != null)
            {
                deck.Intro.loop = false;
                deck.Intro.timeSamples = 0;
                deck.Intro.PlayScheduled(startDsp);
                loopStart = startDsp + CombatSong.ExactSeconds(intro);
            }

            deck.LoopStartDsp = loopStart;
            Schedule(deck.Main, song.Clip, loopStart);
            deck.Layer.clip = song.PlayerTurnClip;
            if (song.PlayerTurnClip != null) Schedule(deck.Layer, song.PlayerTurnClip, loopStart);
            deck.End.clip = null;
        }

        private static void Schedule(AudioSource source, AudioClip clip, double dsp)
        {
            source.clip = clip;
            source.loop = true;
            source.timeSamples = 0;
            source.PlayScheduled(dsp);
        }

        /// <summary>Stops a source exactly at <paramref name="dsp"/>; if it has not started by then it never starts.</summary>
        private static void CutAt(AudioSource source, double dsp, double sourceStartDsp)
        {
            if (source.clip == null) return;
            if (dsp <= sourceStartDsp) source.Stop();
            else source.SetScheduledEndTime(dsp);
        }

        private static void StopDeck(Deck deck)
        {
            deck.Intro.Stop();
            deck.Main.Stop();
            deck.Layer.Stop();
            deck.End.Stop();
            deck.Alive = false;
            deck.Ending = false;
            deck.FadingIn = deck.FadingOut = false;
        }

        private static void BeginFadeOut(Deck deck, float seconds, double startDsp)
        {
            if (seconds <= 0f)
            {
                StopDeck(deck);
                deck.Gain = 0f;
                return;
            }

            deck.FadingIn = false;
            deck.FadingOut = true;
            deck.FadeSeconds = seconds;
            deck.FadeStartDsp = startDsp;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            double dsp = AudioSettings.dspTime;
            if (dsp >= turnSwitchDsp)
                turnMix = Mathf.MoveTowards(turnMix, turnTarget, playerTurnFadeSeconds <= 0f ? 1f : dt / playerTurnFadeSeconds);

            foreach (Deck deck in decks)
            {
                if (deck == null || !deck.Alive) continue;
                float step = deck.FadeSeconds <= 0f ? 1f : dt / deck.FadeSeconds;
                if (deck.FadingIn && dsp >= deck.FadeStartDsp)
                {
                    deck.Gain = Mathf.MoveTowards(deck.Gain, 1f, step);
                    if (deck.Gain >= 1f) deck.FadingIn = false;
                }
                else if (deck.FadingOut && dsp >= deck.FadeStartDsp)
                {
                    deck.Gain = Mathf.MoveTowards(deck.Gain, 0f, step);
                    if (deck.Gain <= 0f) StopDeck(deck);
                }

                // The ending rang out: the deck is free again.
                if (deck.Alive && deck.Ending && dsp > deck.EndDsp + deck.EndSeconds + 0.1d) StopDeck(deck);
            }

            ApplyAll();
        }

        private void OnDisable()
        {
            foreach (Deck deck in decks)
                if (deck != null) StopDeck(deck);
            current = -1;
            turnMix = turnTarget = 0f;
        }

        private void ApplyAll()
        {
            float t = Mathf.SmoothStep(0f, 1f, turnMix);
            foreach (Deck deck in decks)
            {
                if (deck == null) continue;
                CombatSong song = deck.Song;
                float baseVolume = volume * (song != null ? song.Volume : 1f) * deck.Gain;
                deck.Intro.volume = baseVolume;
                deck.End.volume = baseVolume;

                if (song != null && song.HasPlayerTurnLayer)
                {
                    // Stem mode: the stem is part of the loop's mix, so a linear cross-fade keeps the level constant.
                    deck.Main.volume = baseVolume * Mathf.Lerp(1f, song.MainVolumeOnPlayerTurn, t);
                    deck.Layer.volume = baseVolume * song.PlayerTurnVolume * t;
                    deck.MainLowPass.enabled = false;
                    deck.MainHighPass.enabled = false;
                    continue;
                }

                // Filter mode: no stem, so the loop itself is filtered.
                bool useLow = playerTurnFilter == PlayerTurnFilter.LowPass && t > 0f;
                bool useHigh = playerTurnFilter == PlayerTurnFilter.HighPass && t > 0f;
                float turnVolume = playerTurnFilter == PlayerTurnFilter.None ? 1f : playerTurnVolume;
                deck.Main.volume = baseVolume * Mathf.Lerp(1f, turnVolume, t);
                deck.Layer.volume = 0f;
                deck.MainLowPass.enabled = useLow;
                deck.MainHighPass.enabled = useHigh;
                // Interpolate in log-frequency so the sweep sounds even.
                if (useLow)
                {
                    deck.MainLowPass.cutoffFrequency = LogLerp(OpenLowPass, lowPassCutoff, t);
                    deck.MainLowPass.lowpassResonanceQ = Mathf.Lerp(1f, lowPassResonance, t);
                }
                if (useHigh) deck.MainHighPass.cutoffFrequency = LogLerp(OpenHighPass, highPassCutoff, t);
            }
        }

        private static float LogLerp(float from, float to, float t)
        {
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1f, from)), Mathf.Log(Mathf.Max(1f, to)), t));
        }

        private void EnsureDecks()
        {
            for (int i = 0; i < decks.Length; i++)
            {
                if (decks[i] != null) continue;
                // One GameObject per source: audio filters only process the AudioSource on their own GameObject.
                var deck = new Deck
                {
                    Intro = CreateSource($"Combat Music {i} Intro"),
                    Main = CreateSource($"Combat Music {i} Loop"),
                    Layer = CreateSource($"Combat Music {i} Player Turn"),
                    End = CreateSource($"Combat Music {i} End")
                };
                deck.MainLowPass = deck.Main.gameObject.AddComponent<AudioLowPassFilter>();
                deck.MainHighPass = deck.Main.gameObject.AddComponent<AudioHighPassFilter>();
                deck.MainLowPass.enabled = false;
                deck.MainHighPass.enabled = false;
                decks[i] = deck;
            }
        }

        private AudioSource CreateSource(string objectName)
        {
            GameObject child = new(objectName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0; // music must never be culled by voice stealing
            return source;
        }
    }
}
