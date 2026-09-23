using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Rhythm
{
    /// <summary>When a music change happens: right away, or on the song's next beat / bar line.</summary>
    public enum MusicSync { Immediate, NextBeat, NextBar }

    /// <summary>
    /// One piece of combat music, split into sections, and the beat rules every chart played over it follows (BPM,
    /// beats per bar, where beat 0 falls in the loop). An Enemy Attack Sequence references a song.
    /// <list type="bullet">
    /// <item><b>Intro</b> (optional): plays once when the encounter starts (after the player has moved to its spot).
    /// The loop is scheduled to start on the intro's last sample, so the hand-over is gapless.</item>
    /// <item><b>Loop</b>: the main music; loops for as long as combat lasts. Each chart starts on its next bar line.</item>
    /// <item><b>Player Turn</b> (optional): a stem of the loop (beat / bass only) that plays in parallel with it,
    /// sample-locked, and is cross-faded in on the player turn. Without one the loop is filtered instead.</item>
    /// <item><b>End</b> (optional): plays once when the battle ends (enemy or player death), on the loop's next bar
    /// (or beat); the loop stops on that exact sample. Without one the music fades out.</item>
    /// </list>
    /// For gapless joins use WAV or Ogg Vorbis (MP3 adds encoder padding), cut exactly on bar lines, at the same BPM.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Combat Song", fileName = "CombatSong")]
    public sealed class CombatSong : ScriptableObject
    {
        [Header("Intro (optional)")]
        [Tooltip("Plays once when the encounter starts, after the player has moved into place. The loop starts on its last sample. Author it at the song's BPM and end it right before the loop's first sample.")]
        [SerializeField] private AudioClip introClip;
        [Tooltip("Safety net for charts that start while the intro plays: On = their beat 0 lands no earlier than the loop's first downbeat. Off = they may start on a bar line inside the intro. (The Combat Controller's 'Start Combat With Loop' already holds the first attack until the loop starts.)")]
        [SerializeField] private bool chartsWaitForLoop = true;

        [Header("Loop")]
        [Tooltip("The main loop. Plays for as long as combat lasts.")]
        [SerializeField] private AudioClip clip;
        [SerializeField, Min(1f)] private float bpm = 120f;
        [SerializeField, Min(1)] private int beatsPerMeasure = 4;
        [Tooltip("Seconds into the loop clip where the first downbeat (bar 1, beat 1) falls. Tune it in the composer with the metronome.")]
        [SerializeField, Min(0f)] private double audioOffsetSeconds;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [Tooltip("Where each chart's beat 0 may land. Next Bar = on a downbeat (most musical; the first projectile can wait up to a bar). Next Beat = on any beat (projectiles come sooner).")]
        [SerializeField] private MusicSync chartSync = MusicSync.NextBar;

        [Header("Player Turn (optional)")]
        [Tooltip("A stem of the loop (e.g. beat or bass only), the same length as the loop. Plays in parallel with the loop and is cross-faded in on the player turn. Empty = the loop is filtered on the player turn (low-pass by default, set on the Combat Music Director).")]
        [SerializeField] private AudioClip playerTurnClip;
        [SerializeField, Range(0f, 1f)] private float playerTurnVolume = 1f;
        [Tooltip("Volume of the main loop while the player-turn stem plays. 0 = only the stem is heard.")]
        [SerializeField, Range(0f, 1f)] private float mainVolumeOnPlayerTurn;

        [Header("End (optional)")]
        [Tooltip("Plays once when the battle ends (enemy or player death). The loop stops on the same sample. Empty = the music fades out.")]
        [SerializeField] private AudioClip endClip;
        [Tooltip("Optional: a different ending when the player loses. Empty = End Clip is used for both.")]
        [SerializeField] private AudioClip defeatEndClip;
        [Tooltip("When the ending starts. Next Bar is the most musical; Next Beat reacts faster.")]
        [SerializeField] private MusicSync endSync = MusicSync.NextBar;

        public AudioClip Clip { get => clip; set => clip = value; }
        public AudioClip IntroClip { get => introClip; set => introClip = value; }
        public AudioClip PlayerTurnClip { get => playerTurnClip; set => playerTurnClip = value; }
        public AudioClip EndClip { get => endClip; set => endClip = value; }
        public AudioClip DefeatEndClip { get => defeatEndClip; set => defeatEndClip = value; }
        public bool ChartsWaitForLoop { get => chartsWaitForLoop; set => chartsWaitForLoop = value; }
        public MusicSync EndSync { get => endSync; set => endSync = value; }
        public MusicSync ChartSync { get => chartSync; set => chartSync = value; }
        public float Bpm { get => bpm; set => bpm = Mathf.Max(1f, value); }
        public int BeatsPerMeasure { get => beatsPerMeasure; set => beatsPerMeasure = Math.Max(1, value); }
        public double AudioOffsetSeconds { get => audioOffsetSeconds; set => audioOffsetSeconds = Math.Max(0d, value); }
        public float Volume => volume;
        public float PlayerTurnVolume => playerTurnVolume;
        public float MainVolumeOnPlayerTurn => mainVolumeOnPlayerTurn;
        public bool HasPlayerTurnLayer => playerTurnClip != null;
        public double SecondsPerBeat => 60d / Math.Max(1d, bpm);
        public double BarSeconds => SecondsPerBeat * Math.Max(1, beatsPerMeasure);
        /// <summary>Exact loop length (samples / frequency, not the rounded float <c>AudioClip.length</c>).</summary>
        public double LoopSeconds => ExactSeconds(clip);
        public double IntroSeconds => ExactSeconds(introClip);

        public AudioClip EndClipFor(bool victory) => !victory && defeatEndClip != null ? defeatEndClip : endClip;

        public TempoMap CreateTempoMap() => new TempoMap(bpm, beatsPerMeasure, audioOffsetSeconds);

        public double GridSeconds(MusicSync sync) => sync switch
        {
            MusicSync.NextBar => BarSeconds,
            MusicSync.NextBeat => SecondsPerBeat,
            _ => 0d
        };

        public static double ExactSeconds(AudioClip audioClip)
        {
            if (audioClip == null || audioClip.frequency <= 0) return 0d;
            return (double)audioClip.samples / audioClip.frequency;
        }

        /// <summary>
        /// Clip time (seconds) of the first bar line at or after <paramref name="clipSeconds"/>, for a clip that loops
        /// every <paramref name="loopLength"/> seconds. May return a value past the loop end (the next loop's first bar).
        /// Bar lines restart from the offset at each loop, so a loop that is not a whole number of bars stays in phase.
        /// Works for any grid (pass beat length instead of bar length for beats).
        /// </summary>
        public static double NextBarInLoop(double clipSeconds, double offset, double barSeconds, double loopLength)
        {
            if (barSeconds <= 0d) return clipSeconds;
            if (loopLength <= 0d) loopLength = double.MaxValue;
            double bars = Math.Ceiling((clipSeconds - offset) / barSeconds - 1e-6d);
            double candidate = offset + bars * barSeconds;
            if (candidate >= loopLength - 1e-6d) candidate = loopLength + offset;
            return candidate;
        }

        /// <summary>
        /// Absolute time of the first grid line (bar or beat) at or after <paramref name="earliest"/> on a timeline where
        /// the loop first starts at <paramref name="loopStart"/> (an intro may play before it). Inside the loop the grid
        /// restarts from <paramref name="offset"/> each pass. Before the loop (during the intro) the grid is counted
        /// backwards from the loop's first downbeat, so an intro of any length (pickup bars included) stays on the beat.
        /// With <paramref name="waitForLoop"/> nothing earlier than the loop's first downbeat is returned.
        /// </summary>
        public static double NextGridTime(double earliest, double loopStart, double loopLength, double offset,
            double gridSeconds, bool waitForLoop = false)
        {
            double firstDownbeat = loopStart + offset;
            if (gridSeconds <= 0d) return waitForLoop ? Math.Max(earliest, firstDownbeat) : earliest;
            if (earliest < loopStart)
            {
                // offset >= 0, so earliest is before the first downbeat here.
                if (waitForLoop) return firstDownbeat;
                double steps = Math.Floor((firstDownbeat - earliest) / gridSeconds + 1e-6d);
                return firstDownbeat - steps * gridSeconds;
            }

            double elapsed = earliest - loopStart;
            double loopsDone = loopLength > 0d ? Math.Floor(elapsed / loopLength) : 0d;
            double passStart = loopStart + loopsDone * loopLength;
            double inLoop = elapsed - loopsDone * loopLength;
            return passStart + NextBarInLoop(inLoop, offset, gridSeconds, loopLength);
        }

        /// <summary>Authoring problems that break seamless transitions (shown in the inspector).</summary>
        public List<string> GetSectionWarnings()
        {
            var warnings = new List<string>();
            if (clip == null)
            {
                warnings.Add("No loop clip: this song plays nothing.");
                return warnings;
            }

            if (playerTurnClip != null)
            {
                if (playerTurnClip.samples != clip.samples || playerTurnClip.frequency != clip.frequency)
                    warnings.Add($"Player Turn clip ({playerTurnClip.samples} samples @ {playerTurnClip.frequency} Hz) is not the same length as the loop ({clip.samples} @ {clip.frequency} Hz). They will drift apart after the first pass. Export both from the same session with the same start and end.");
                if (playerTurnClip.channels != clip.channels)
                    warnings.Add("Player Turn clip and loop have a different channel count.");
            }

            double bars = (LoopSeconds - audioOffsetSeconds) / BarSeconds;
            if (audioOffsetSeconds <= 1e-4d && Math.Abs(bars - Math.Round(bars)) > 0.01d)
                warnings.Add($"The loop is {bars:0.##} bars long at {bpm} BPM, not a whole number of bars. Check the BPM or trim the clip on a bar line.");

            if (introClip != null && introClip.frequency != clip.frequency)
                warnings.Add("Intro and loop have a different sample rate; set both to the same rate in the import settings.");
            return warnings;
        }
    }
}
