using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm
{
    /// <summary>One tempo change: from <see cref="Beat"/> onward the tempo is <see cref="Bpm"/>.</summary>
    [Serializable]
    public struct TempoSegment
    {
        public double Beat;
        public double Bpm;

        public TempoSegment(double beat, double bpm)
        {
            Beat = beat;
            Bpm = bpm;
        }
    }

    /// <summary>
    /// Beat/seconds conversion. Beats are the authoritative unit; seconds are derived.
    /// Supports tempo changes (piecewise constant) and an audio offset (seconds).
    /// Pure C#, no UnityEngine dependency.
    /// </summary>
    public sealed class TempoMap
    {
        private readonly List<TempoSegment> segments = new List<TempoSegment>();
        private readonly List<double> segmentStartSeconds = new List<double>();

        public double AudioOffsetSeconds { get; private set; }
        public int BeatsPerMeasure { get; private set; }
        public IList<TempoSegment> Segments { get { return segments.AsReadOnly(); } }

        public TempoMap(double bpm, int beatsPerMeasure = 4, double audioOffsetSeconds = 0d)
            : this(new[] { new TempoSegment(0d, bpm) }, beatsPerMeasure, audioOffsetSeconds)
        {
        }

        public TempoMap(IEnumerable<TempoSegment> tempoSegments, int beatsPerMeasure = 4, double audioOffsetSeconds = 0d)
        {
            BeatsPerMeasure = Math.Max(1, beatsPerMeasure);
            AudioOffsetSeconds = audioOffsetSeconds;
            if (tempoSegments != null)
            {
                segments.AddRange(tempoSegments);
            }

            segments.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            if (segments.Count == 0 || segments[0].Beat > 0d)
            {
                double first = segments.Count > 0 ? segments[0].Bpm : 120d;
                segments.Insert(0, new TempoSegment(0d, first));
            }

            for (int i = 0; i < segments.Count; i++)
            {
                TempoSegment s = segments[i];
                s.Bpm = Math.Max(0.01d, s.Bpm);
                segments[i] = s;
            }

            double t = 0d;
            segmentStartSeconds.Add(0d);
            for (int i = 1; i < segments.Count; i++)
            {
                t += (segments[i].Beat - segments[i - 1].Beat) * 60d / segments[i - 1].Bpm;
                segmentStartSeconds.Add(t);
            }
        }

        public double BeatToSeconds(double beat)
        {
            int i = SegmentIndexForBeat(beat);
            return AudioOffsetSeconds + segmentStartSeconds[i] + (beat - segments[i].Beat) * 60d / segments[i].Bpm;
        }

        public double SecondsToBeat(double seconds)
        {
            double local = seconds - AudioOffsetSeconds;
            int i = SegmentIndexForSeconds(local);
            return segments[i].Beat + (local - segmentStartSeconds[i]) * segments[i].Bpm / 60d;
        }

        public double BpmAtBeat(double beat)
        {
            return segments[SegmentIndexForBeat(beat)].Bpm;
        }

        public double SnapBeat(double beat, int division)
        {
            double d = Math.Max(1, division);
            return Math.Round(beat * d, MidpointRounding.AwayFromZero) / d;
        }

        private int SegmentIndexForBeat(double beat)
        {
            int idx = 0;
            for (int i = 1; i < segments.Count; i++)
            {
                if (segments[i].Beat <= beat) idx = i; else break;
            }

            return idx;
        }

        private int SegmentIndexForSeconds(double local)
        {
            int idx = 0;
            for (int i = 1; i < segments.Count; i++)
            {
                if (segmentStartSeconds[i] <= local) idx = i; else break;
            }

            return idx;
        }
    }
}
