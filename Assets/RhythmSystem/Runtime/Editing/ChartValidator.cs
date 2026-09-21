using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm.Editing
{
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ValidationIssue
    {
        public IssueSeverity Severity;
        public string Code;
        public string Message;
        public string NoteId;
        /// <summary>Set when the issue is about a pattern (or names one as the cause).</summary>
        public string PatternId;
        public double Beat;
    }

    /// <summary>
    /// Authoring checks for a ChartSession. Pure logic; the composer shows the result in its validation strip and
    /// outlines offending notes. Nothing here modifies the chart.
    /// </summary>
    public static class ChartValidator
    {
        /// <summary>Two hits in one lane closer than this (seconds) are effectively one press.</summary>
        public const double MinHitSpacingSeconds = 0.08d;
        private const double SameBeatEpsilon = 1e-6d;
        private const int MaxIssues = 200;

        public static bool IsHoldLike(string definitionId)
        {
            return definitionId == NoteMigration.DefinitionIdHold
                || definitionId == NoteMigration.DefinitionIdStationaryHold;
        }

        /// <param name="audioDurationSeconds">Length of the chart audio; 0 when there is none (skips the "after audio" check).</param>
        public static List<ValidationIssue> Validate(ChartSession session, double audioDurationSeconds = 0d, NoteHandleDefaults defaults = null)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (defaults == null) defaults = new NoteHandleDefaults();
            var issues = new List<ValidationIssue>();
            IList<NoteInstance> notes = session.Notes;
            TempoMap tempo = session.Tempo;

            if (notes.Count == 0 && session.Patterns.Count == 0)
            {
                Add(issues, IssueSeverity.Info, "empty", "The chart has no notes.", null, 0d);
                return issues;
            }

            var byLane = new Dictionary<string, List<NoteInstance>>();
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                if (session.LaneIndex(n.LaneId) < 0)
                {
                    Add(issues, IssueSeverity.Error, "unknown-lane", "Note is on lane '" + n.LaneId + "', which the chart does not have.", n.Id, n.HitBeat);
                    continue;
                }

                if (n.HitBeat < -SameBeatEpsilon)
                    Add(issues, IssueSeverity.Error, "negative-beat", "Note starts before beat 0.", n.Id, n.HitBeat);

                bool holdLike = IsHoldLike(n.DefinitionId);
                if (n.DefinitionId == NoteMigration.DefinitionIdMash)
                {
                    // The travel time is the clearing window: the note is destroyed before it crosses the hit line.
                    double seconds = NoteHandles.TravelSeconds(n, defaults, tempo);
                    int presses = n.MashRequiredPresses.HasValue ? n.MashRequiredPresses.Value : MashRules.DefaultRequiredPresses;
                    MashRateSeverity sev = MashRules.Severity(presses, seconds);
                    if (sev == MashRateSeverity.Error)
                        Add(issues, IssueSeverity.Error, "mash-too-fast",
                            presses + " presses in " + seconds.ToString("0.00") + " s of travel is not reliably achievable.", n.Id, n.HitBeat);
                    else if (sev == MashRateSeverity.Warning)
                        Add(issues, IssueSeverity.Warning, "mash-fast",
                            presses + " presses in " + seconds.ToString("0.00") + " s of travel is demanding.", n.Id, n.HitBeat);
                }
                else if (holdLike && n.HoldBeats <= 0d)
                {
                    Add(issues, IssueSeverity.Warning, "hold-no-length", "Hold note has no length, so it plays as a tap.", n.Id, n.HitBeat);
                }

                if (audioDurationSeconds > 0d && tempo.BeatToSeconds(n.HitBeat) > audioDurationSeconds + 0.5d)
                    Add(issues, IssueSeverity.Warning, "after-audio", "Note is later than the end of the audio.", n.Id, n.HitBeat);

                List<NoteInstance> lane;
                if (!byLane.TryGetValue(n.LaneId, out lane))
                {
                    lane = new List<NoteInstance>();
                    byLane[n.LaneId] = lane;
                }

                lane.Add(n);
            }

            foreach (KeyValuePair<string, List<NoteInstance>> pair in byLane)
            {
                List<NoteInstance> lane = pair.Value;
                lane.Sort(delegate (NoteInstance a, NoteInstance b) { return a.HitBeat.CompareTo(b.HitBeat); });
                for (int i = 0; i < lane.Count; i++)
                {
                    NoteInstance a = lane[i];
                    for (int j = i + 1; j < lane.Count; j++)
                    {
                        NoteInstance b = lane[j];
                        bool aHolds = IsHoldLike(a.DefinitionId) && a.HoldBeats > 0d;
                        double endA = a.HitBeat + (aHolds ? a.HoldBeats : 0d);
                        if (b.HitBeat - a.HitBeat < SameBeatEpsilon)
                        {
                            Add(issues, IssueSeverity.Warning, "stacked", "Two notes start on the same beat in this lane.", b.Id, b.HitBeat);
                            continue;
                        }

                        if (aHolds && b.HitBeat < endA - SameBeatEpsilon)
                        {
                            Add(issues, IssueSeverity.Warning, "hold-overlap", "Hold overlaps the next note in this lane.", b.Id, b.HitBeat);
                            continue;
                        }

                        double gap = tempo.BeatToSeconds(b.HitBeat) - tempo.BeatToSeconds(endA);
                        if (gap < MinHitSpacingSeconds)
                            Add(issues, IssueSeverity.Warning, "too-close",
                                "Notes are only " + (gap * 1000d).ToString("0") + " ms apart in this lane.", b.Id, b.HitBeat);
                        break;
                    }
                }
            }

            CheckMashLaneConflicts(session, defaults, issues);
            CheckReservations(session, issues);

            issues.Sort(delegate (ValidationIssue a, ValidationIssue b)
            {
                int c = ((int)b.Severity).CompareTo((int)a.Severity);
                return c != 0 ? c : a.Beat.CompareTo(b.Beat);
            });
            if (issues.Count > MaxIssues) issues.RemoveRange(MaxIssues, issues.Count - MaxIssues);
            return issues;
        }

        private static string PatternLabel(PatternInstance p)
        {
            PatternTemplate template = PatternLibrary.Find(p.TemplateId);
            return (template != null ? template.Name : p.TemplateId) + " (beats " + p.StartBeat.ToString("0.##") + "-" + p.EndBeat.ToString("0.##") + ")";
        }

        // Exclusive patterns reserve their lanes for their duration: hand-placed notes there, and other exclusive
        // patterns overlapping in time on a shared lane, are conflicts. Nothing is moved or deleted automatically.
        // A Mash note is on its way for its whole travel time and takes the lane key presses; another note in the
        // same lane that hits meanwhile would compete for those presses.
        private static void CheckMashLaneConflicts(ChartSession session, NoteHandleDefaults defaults, List<ValidationIssue> issues)
        {
            IList<NoteInstance> notes = session.Notes;
            TempoMap tempo = session.Tempo;
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance mash = notes[i];
                if (mash.DefinitionId != NoteMigration.DefinitionIdMash) continue;
                double from = mash.HitBeat - NoteHandles.TravelBeats(mash, defaults, tempo);
                double to = tempo.SecondsToBeat(tempo.BeatToSeconds(mash.HitBeat) + MashRules.LineGraceSeconds);
                for (int j = 0; j < notes.Count; j++)
                {
                    NoteInstance other = notes[j];
                    if (j == i || other.LaneId != mash.LaneId) continue;
                    if (other.HitBeat < from - SameBeatEpsilon || other.HitBeat > to + SameBeatEpsilon) continue;
                    Add(issues, IssueSeverity.Warning, "mash-lane-busy",
                        "This note hits while a Mash note is on its way in the same lane, so the Mash presses may go to it.", other.Id, other.HitBeat);
                }
            }
        }

        private static void CheckReservations(ChartSession session, List<ValidationIssue> issues)
        {
            IList<PatternInstance> patterns = session.Patterns;
            IList<NoteInstance> notes = session.Notes;
            for (int pi = 0; pi < patterns.Count; pi++)
            {
                PatternInstance p = patterns[pi];
                if (PatternLibrary.Find(p.TemplateId) == null)
                    Add(issues, IssueSeverity.Error, "pattern-unknown", "Pattern template '" + p.TemplateId + "' does not exist.", null, p.StartBeat, p.Id);
                if (p.Reservation != ReservationMode.Exclusive) continue;

                for (int i = 0; i < notes.Count; i++)
                {
                    NoteInstance n = notes[i];
                    int lane = session.LaneIndex(n.LaneId);
                    if (lane < 0 || !p.UsesLane(lane)) continue;
                    double hold = Math.Max(0d, n.HoldBeats);
                    bool overlaps = n.HitBeat < p.EndBeat - SameBeatEpsilon
                        && (hold > 0d ? n.HitBeat + hold > p.StartBeat + SameBeatEpsilon : n.HitBeat >= p.StartBeat - SameBeatEpsilon);
                    if (overlaps)
                        Add(issues, IssueSeverity.Error, "reserved-lane",
                            "Note is inside the reserved lanes of " + PatternLabel(p) + ".", n.Id, n.HitBeat, p.Id);
                }

                for (int qi = pi + 1; qi < patterns.Count; qi++)
                {
                    PatternInstance q = patterns[qi];
                    if (q.Reservation != ReservationMode.Exclusive) continue;
                    if (q.StartBeat >= p.EndBeat - SameBeatEpsilon || p.StartBeat >= q.EndBeat - SameBeatEpsilon) continue;
                    bool sharedLane = false;
                    for (int l = 0; l < session.LaneIds.Count; l++)
                    {
                        if (p.UsesLane(l) && q.UsesLane(l)) sharedLane = true;
                    }

                    if (sharedLane)
                        Add(issues, IssueSeverity.Error, "pattern-overlap",
                            PatternLabel(p) + " overlaps " + PatternLabel(q) + " on shared lanes.", null, Math.Max(p.StartBeat, q.StartBeat), q.Id);
                }
            }
        }

        /// <summary>Worst severity per note or pattern id, for outlining items on the timeline.</summary>
        public static Dictionary<string, IssueSeverity> WorstByNote(IList<ValidationIssue> issues)
        {
            var map = new Dictionary<string, IssueSeverity>();
            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                string key = issue.NoteId ?? issue.PatternId;
                if (key == null) continue;
                IssueSeverity existing;
                if (!map.TryGetValue(key, out existing) || issue.Severity > existing) map[key] = issue.Severity;
                if (issue.NoteId != null && issue.PatternId != null)
                {
                    if (!map.TryGetValue(issue.PatternId, out existing) || issue.Severity > existing) map[issue.PatternId] = issue.Severity;
                }
            }

            return map;
        }

        private static void Add(List<ValidationIssue> list, IssueSeverity severity, string code, string message, string noteId, double beat, string patternId = null)
        {
            list.Add(new ValidationIssue { Severity = severity, Code = code, Message = message, NoteId = noteId, PatternId = patternId, Beat = beat });
        }
    }
}
