using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;

namespace RythmRPG.LevelComposer.Validation
{
    public enum Severity
    {
        Info,
        Warning,
        Error
    }

    public sealed class Issue
    {
        public Severity Severity;
        public string Message = "";
        /// <summary>-1 for level-wide issues.</summary>
        public int StepIndex = -1;
        public string NoteId;
        public double Beat;

        public override string ToString() { return Severity + ": " + Message; }
    }

    /// <summary>
    /// Authoring checks shown in the Problems list. They mirror the game's ChartValidator / MashRules / CombatSong
    /// warnings so a level that validates here imports cleanly. Nothing here changes the level.
    /// </summary>
    public static class LevelValidator
    {
        public const double MinHitSpacingSeconds = 0.08d;
        public const double MinReadableTravelSeconds = 0.35d;
        // Same limits as RythmRPG.Rhythm.MashRules.
        public const double MashWarnPressesPerSecond = 7d;
        public const double MashErrorPressesPerSecond = 10d;
        public const double MashShortWindowSeconds = 0.3d;
        private const int MaxIssues = 300;
        private const double Eps = 1e-6;

        public static List<Issue> Validate(CombatLevel level, NoteTypeRegistry types, string levelFilePath = null)
        {
            var issues = new List<Issue>();
            LevelTempo tempo = LevelTempo.Of(level);
            MusicSettings m = level.Music;

            if (string.IsNullOrEmpty(level.Id.Trim()))
                Add(issues, Severity.Error, "The level needs an id (used for the imported asset names).", -1, null, 0);
            if (string.IsNullOrEmpty(m.Loop))
                Add(issues, Severity.Warning, "No main loop music: the fight plays in silence and charts start without a bar grid.", -1, null, 0);
            if (!string.IsNullOrEmpty(m.PlayerTurn) && string.IsNullOrEmpty(m.Loop))
                Add(issues, Severity.Warning, "A player-turn stem needs a main loop to follow.", -1, null, 0);

            foreach (MusicSection section in (MusicSection[])Enum.GetValues(typeof(MusicSection)))
            {
                string path = m.Get(section);
                if (string.IsNullOrEmpty(path)) continue;
                string full = LevelPaths.Resolve(path, levelFilePath);
                if (!string.IsNullOrEmpty(levelFilePath) || Path.IsPathRooted(full))
                {
                    if (!File.Exists(full))
                        Add(issues, Severity.Error, SectionName(section) + " music file not found: " + path, -1, null, 0);
                }

                if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    Add(issues, Severity.Warning, SectionName(section) + " is an MP3: encoder padding breaks gapless joins. Export WAV or Ogg.", -1, null, 0);
            }

            if (level.TotalNotes == 0)
                Add(issues, Severity.Info, "The level has no notes yet.", -1, null, 0);

            for (int s = 0; s < level.Steps.Count && issues.Count < MaxIssues; s++)
                ValidateStep(level.Steps[s], s, types, tempo, issues);

            issues.Sort((a, b) =>
            {
                int c = b.Severity.CompareTo(a.Severity);
                if (c != 0) return c;
                c = a.StepIndex.CompareTo(b.StepIndex);
                return c != 0 ? c : a.Beat.CompareTo(b.Beat);
            });
            return issues;
        }

        public static string SectionName(MusicSection section)
        {
            switch (section)
            {
                case MusicSection.Intro: return "Intro";
                case MusicSection.Loop: return "Main loop";
                case MusicSection.PlayerTurn: return "Player turn";
                case MusicSection.End: return "End";
                default: return "Defeat end";
            }
        }

        private static void ValidateStep(LevelStep step, int s, NoteTypeRegistry types, LevelTempo tempo, List<Issue> issues)
        {
            string where = "'" + step.Name + "'";
            if (step.Notes.Count == 0)
            {
                Add(issues, Severity.Warning, where + " has no notes: the enemy step ends immediately.", s, null, 0);
                return;
            }

            var byLane = new Dictionary<int, List<LevelNote>>();
            foreach (LevelNote n in step.Notes)
            {
                NoteTypeDef def = types.Find(n.Type);
                if (def == null)
                {
                    Add(issues, Severity.Error, "Unknown note type '" + n.Type + "' (add its note-type file or change the type).", s, n.Id, n.Beat);
                    continue;
                }

                if (n.Lane < 1 || n.Lane > step.LaneCount)
                    Add(issues, Severity.Error, def.Name + " is on lane " + n.Lane + " but the step has " + step.LaneCount + " lanes.", s, n.Id, n.Beat);

                if (def.HasLength && n.Length <= Eps)
                    Add(issues, Severity.Warning, def.Name + " has no length, so it plays as a tap.", s, n.Id, n.Beat);

                if (def.Output == NoteOutput.Sequence)
                {
                    ValidateSequence(n, def, step, s, tempo, issues);
                    continue; // sequences spawn their own notes at run time
                }

                double travel = NoteParams.TravelSeconds(n, def, tempo);
                if (def.Archetype == Archetypes.Mash)
                {
                    int presses = (int)NoteParams.GetBound(n, def, ParamBindings.MashPresses, tempo, 8d);
                    double rate = Math.Max(1, presses) / Math.Max(1e-4, travel);
                    if (rate > MashErrorPressesPerSecond)
                        Add(issues, Severity.Error, presses + " presses in " + travel.ToString("0.00") + " s (" + rate.ToString("0.0") + "/s) is not reliably achievable.", s, n.Id, n.Beat);
                    else if (rate > MashWarnPressesPerSecond || travel < MashShortWindowSeconds)
                        Add(issues, Severity.Warning, presses + " presses in " + travel.ToString("0.00") + " s (" + rate.ToString("0.0") + "/s) is demanding.", s, n.Id, n.Beat);
                }
                else if (!def.Stationary && travel < MinReadableTravelSeconds)
                {
                    Add(issues, Severity.Warning, def.Name + " travels in " + travel.ToString("0.00") + " s: very hard to read.", s, n.Id, n.Beat);
                }

                ParamDef pw = def.FindBound(ParamBindings.PerfectWindow), gw = def.FindBound(ParamBindings.GoodWindow), bw = def.FindBound(ParamBindings.BadWindow);
                if (pw != null && gw != null && bw != null)
                {
                    double p = NoteParams.GetNumber(n, def, pw.Key, tempo, 0.1), g = NoteParams.GetNumber(n, def, gw.Key, tempo, 0.25), b = NoteParams.GetNumber(n, def, bw.Key, tempo, 0.45);
                    if (!(p <= g + Eps && g <= b + Eps))
                        Add(issues, Severity.Warning, def.Name + " windows should grow: Perfect <= Good <= Bad.", s, n.Id, n.Beat);
                    if (b > travel + Eps)
                        Add(issues, Severity.Info, def.Name + " Bad window (" + b.ToString("0.00") + " s) is longer than its charge (" + travel.ToString("0.00") + " s).", s, n.Id, n.Beat);
                }

                List<LevelNote> lane;
                if (!byLane.TryGetValue(n.Lane, out lane)) byLane[n.Lane] = lane = new List<LevelNote>();
                lane.Add(n);
            }

            foreach (KeyValuePair<int, List<LevelNote>> kv in byLane)
            {
                List<LevelNote> lane = kv.Value;
                lane.Sort(LevelSerializer.CompareNotes);
                for (int i = 0; i + 1 < lane.Count; i++)
                {
                    // Only neighbours matter: the next note in the same lane.
                    LevelNote a = lane[i], b = lane[i + 1];
                    NoteTypeDef da = types.Find(a.Type), db = types.Find(b.Type);
                    double gap = tempo.BeatToSeconds(b.Beat - a.Beat);
                    bool aHolds = da != null && da.HasLength && a.Length > Eps;
                    if (b.Beat - a.Beat < Eps)
                    {
                        Add(issues, Severity.Warning, "Two notes on the same beat in lane " + kv.Key + ".", s, b.Id, b.Beat);
                    }
                    else if (aHolds && b.Beat < a.EndBeat - Eps)
                    {
                        Add(issues, Severity.Error, "Starts while the hold before it in lane " + kv.Key + " is still held.", s, b.Id, b.Beat);
                    }
                    else if (gap < MinHitSpacingSeconds && !aHolds)
                    {
                        Add(issues, Severity.Warning, "Only " + (gap * 1000).ToString("0") + " ms after the previous note in lane " + kv.Key + ": plays as one press.", s, b.Id, b.Beat);
                    }
                    else if (db != null && db.Stationary)
                    {
                        // A stationary note charges on the lane marker from its spawn; a note still due then steals presses.
                        double spawn = NoteParams.SpawnSeconds(b, db, tempo);
                        double aEnd = tempo.BeatToSeconds(aHolds ? a.EndBeat : a.Beat);
                        if (spawn < aEnd - Eps)
                            Add(issues, Severity.Info, db.Name + " starts charging before the previous note in lane " + kv.Key + " is done.", s, b.Id, b.Beat);
                    }
                }
            }
        }

        private static void ValidateSequence(LevelNote n, NoteTypeDef def, LevelStep step, int s, LevelTempo tempo, List<Issue> issues)
        {
            ParamDef lanes = def.FindBound(ParamBindings.SeqLanes);
            if (lanes == null) return;
            string text = NoteParams.GetString(n, def, lanes.Key, tempo);
            foreach (int lane in ParseLaneList(text))
            {
                if (lane < 1 || lane > step.LaneCount)
                {
                    Add(issues, Severity.Warning, def.Name + " lane cycle uses lane " + lane + ", which the step does not have.", s, n.Id, n.Beat);
                    break;
                }
            }
        }

        /// <summary>Parses "2 3, 1" into [2,3,1]; ignores anything that is not a number.</summary>
        public static List<int> ParseLaneList(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string part in text.Split(new[] { ' ', ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(part.Trim(), out v)) result.Add(v);
            }

            return result;
        }

        private static void Add(List<Issue> issues, Severity severity, string message, int step, string noteId, double beat)
        {
            if (issues.Count >= MaxIssues) return;
            issues.Add(new Issue { Severity = severity, Message = message, StepIndex = step, NoteId = noteId, Beat = beat });
        }
    }
}
