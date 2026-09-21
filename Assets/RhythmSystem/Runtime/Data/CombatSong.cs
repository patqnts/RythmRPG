using System;
using UnityEngine;

namespace RythmRPG.Rhythm
{
    /// <summary>
    /// One piece of combat music and the beat rules every chart played over it follows (BPM, beats per bar, where
    /// beat 0 falls in the clip). An Enemy Attack Sequence references a song; the song loops for as long as combat
    /// lasts and each of the sequence's charts starts on its next bar line, so charts are never tied to a fixed
    /// moment of the audio.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Combat Song", fileName = "CombatSong")]
    public sealed class CombatSong : ScriptableObject
    {
        [SerializeField] private AudioClip clip;
        [SerializeField, Min(1f)] private float bpm = 120f;
        [SerializeField, Min(1)] private int beatsPerMeasure = 4;
        [Tooltip("Seconds into the clip where the first downbeat (bar 1, beat 1) falls. Tune it in the composer with the metronome.")]
        [SerializeField, Min(0f)] private double audioOffsetSeconds;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        public AudioClip Clip { get => clip; set => clip = value; }
        public float Bpm { get => bpm; set => bpm = Mathf.Max(1f, value); }
        public int BeatsPerMeasure { get => beatsPerMeasure; set => beatsPerMeasure = Math.Max(1, value); }
        public double AudioOffsetSeconds { get => audioOffsetSeconds; set => audioOffsetSeconds = Math.Max(0d, value); }
        public float Volume => volume;
        public double SecondsPerBeat => 60d / Math.Max(1d, bpm);
        public double BarSeconds => SecondsPerBeat * Math.Max(1, beatsPerMeasure);

        public TempoMap CreateTempoMap() => new TempoMap(bpm, beatsPerMeasure, audioOffsetSeconds);

        /// <summary>
        /// Clip time (seconds) of the first bar line at or after <paramref name="clipSeconds"/>, for a clip that loops
        /// every <paramref name="loopLength"/> seconds. May return a value past the loop end (the next loop's first bar).
        /// Bar lines restart from the offset at each loop, so a loop that is not a whole number of bars stays in phase.
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
    }
}
