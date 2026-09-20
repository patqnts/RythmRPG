using System;
using System.Collections.Generic;
using System.Linq;

namespace RythmRPG.Rhythm
{
    public static class RhythmTimingUtility
    {
        public static bool IsHoldType(RhythmNoteType type)
        {
            return type == RhythmNoteType.Hold || type == RhythmNoteType.HoldLaser;
        }

        public static double GetSecondsPerBeat(float bpm)
        {
            return 60d / Math.Max(0.01d, bpm);
        }

        public static double GetSnapInterval(float bpm, RhythmSnapDivision division)
        {
            return GetSecondsPerBeat(bpm) / Math.Max(1, (int)division);
        }

        public static double SnapTime(double time, float bpm, RhythmSnapDivision division)
        {
            double interval = GetSnapInterval(bpm, division);
            return Math.Round(Math.Max(0d, time) / interval, MidpointRounding.AwayFromZero) * interval;
        }

        public static double GetSpawnTime(RhythmNoteData note)
        {
            return note == null ? 0d : note.HitTime - note.TravelTime;
        }

        public static float GetLegacyHoldLength(RhythmNoteData note)
        {
            if (note == null || !note.IsHold)
            {
                return 0f;
            }

            return (float)Math.Max(0d, note.HoldDuration) * Math.Max(0f, note.Speed);
        }

        public static double GetPlaybackStartTime(RhythmChart chart)
        {
            if (chart == null)
            {
                return 0d;
            }

            RhythmNoteData firstNote = chart.GetNotesBySpawnTime().FirstOrDefault();
            return firstNote == null ? 0d : Math.Min(0d, firstNote.SpawnTime);
        }

        public static bool IsMeasureBeat(long beatIndex, int beatsPerMeasure)
        {
            return beatIndex % Math.Max(1, beatsPerMeasure) == 0;
        }
    }

    public enum RhythmValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct RhythmValidationIssue
    {
        public RhythmValidationSeverity Severity { get; }
        public string Message { get; }
        public string NoteId { get; }

        public RhythmValidationIssue(RhythmValidationSeverity severity, string message, string noteId = null)
        {
            Severity = severity;
            Message = message;
            NoteId = noteId;
        }
    }

    public static class RhythmChartValidator
    {
        public static IReadOnlyList<RhythmValidationIssue> Validate(RhythmChart chart)
        {
            List<RhythmValidationIssue> issues = new List<RhythmValidationIssue>();
            if (chart == null)
            {
                issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "No rhythm chart was supplied."));
                return issues;
            }

            if (chart.Bpm <= 0f)
            {
                issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "BPM must be greater than zero."));
            }

            if (chart.BeatsPerMeasure < 1)
            {
                issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "Beats per measure must be at least one."));
            }

            if (chart.CompositionDuration <= 0d)
            {
                issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "Composition duration must be greater than zero."));
            }

            if (chart.Lanes.Count == 0)
            {
                issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "The chart needs at least one lane."));
            }

            foreach (IGrouping<string, RhythmLaneData> duplicate in chart.Lanes.Where(lane => lane != null).GroupBy(lane => lane.Id).Where(group => group.Count() > 1))
            {
                issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, $"Lane id '{duplicate.Key}' is duplicated."));
            }

            foreach (IGrouping<int, RhythmLaneData> duplicate in chart.Lanes.Where(lane => lane != null).GroupBy(lane => lane.KeyIdentity).Where(group => group.Count() > 1))
            {
                issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Warning, $"Key identity {duplicate.Key} is used by multiple lanes."));
            }

            HashSet<string> laneIds = new HashSet<string>(chart.Lanes.Where(lane => lane != null).Select(lane => lane.Id));
            foreach (RhythmNoteData note in chart.Notes.Where(note => note != null))
            {
                if (!laneIds.Contains(note.LaneId))
                {
                    issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "Note references a lane that does not exist.", note.Id));
                }

                if (note.HitTime < 0d)
                {
                    issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "Hit time cannot be negative.", note.Id));
                }

                if (note.TravelTime < 0d)
                {
                    issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "Travel time cannot be negative.", note.Id));
                }
                else if (note.SpawnTime < 0d)
                {
                    issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Warning, "Spawn time is before composition start.", note.Id));
                }

                if (note.Speed <= 0f)
                {
                    issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "Note speed must be greater than zero.", note.Id));
                }

                if (note.IsHold && note.HoldDuration <= 0d)
                {
                    issues.Add(new RhythmValidationIssue(RhythmValidationSeverity.Error, "Hold duration must be greater than zero.", note.Id));
                }
            }

            return issues;
        }
    }
}
