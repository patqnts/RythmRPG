using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.LevelComposer.Json;

namespace RythmRPG.LevelComposer.Model
{
    /// <summary>Reads and writes .combatlevel.json files. Unknown fields are ignored; missing ones get defaults.</summary>
    public static class LevelSerializer
    {
        public static string ToJson(CombatLevel level)
        {
            return MiniJson.Serialize(ToDictionary(level), true) + "\n";
        }

        public static Dictionary<string, object> ToDictionary(CombatLevel level)
        {
            var root = new Dictionary<string, object>();
            root["format"] = CombatLevel.FormatId;
            root["version"] = CombatLevel.CurrentVersion;
            root["id"] = level.Id;
            root["name"] = level.Name;
            root["author"] = level.Author;
            root["description"] = level.Description;
            root["selectionWeight"] = level.SelectionWeight;

            MusicSettings m = level.Music;
            var music = new Dictionary<string, object>();
            music["bpm"] = m.Bpm;
            music["beatsPerBar"] = m.BeatsPerBar;
            music["offsetSeconds"] = m.OffsetSeconds;
            music["volume"] = m.Volume;
            music["chartSync"] = m.ChartSync.ToString();
            music["endSync"] = m.EndSync.ToString();
            music["chartsWaitForLoop"] = m.ChartsWaitForLoop;
            music["playerTurnVolume"] = m.PlayerTurnVolume;
            music["mainVolumeOnPlayerTurn"] = m.MainVolumeOnPlayerTurn;
            music["intro"] = m.Intro;
            music["loop"] = m.Loop;
            music["playerTurn"] = m.PlayerTurn;
            music["end"] = m.End;
            music["defeatEnd"] = m.DefeatEnd;
            root["music"] = music;

            var preview = new Dictionary<string, object>();
            preview["playerTurnBars"] = level.Preview.PlayerTurnBars;
            preview["loopBar"] = level.Preview.LoopBar;
            root["preview"] = preview;

            var steps = new List<object>();
            foreach (LevelStep s in level.Steps)
            {
                var sd = new Dictionary<string, object>();
                sd["id"] = s.Id;
                sd["name"] = s.Name;
                sd["animation"] = s.Animation;
                sd["anticipation"] = s.Anticipation;
                sd["endPolicy"] = s.EndPolicy.ToString();
                sd["lanes"] = s.LaneCount;
                var notes = new List<LevelNote>(s.Notes);
                notes.Sort(CompareNotes);
                var nl = new List<object>();
                foreach (LevelNote n in notes)
                {
                    var nd = new Dictionary<string, object>();
                    nd["id"] = n.Id;
                    nd["type"] = n.Type;
                    nd["lane"] = n.Lane;
                    nd["beat"] = Round(n.Beat);
                    if (n.Length > 0d) nd["length"] = Round(n.Length);
                    if (n.Params.Count > 0)
                    {
                        var keys = new List<string>(n.Params.Keys);
                        keys.Sort(StringComparer.Ordinal);
                        var pd = new Dictionary<string, object>();
                        foreach (string k in keys) pd[k] = n.Params[k];
                        nd["params"] = pd;
                    }

                    nl.Add(nd);
                }

                sd["notes"] = nl;
                steps.Add(sd);
            }

            root["steps"] = steps;
            return root;
        }

        public static int CompareNotes(LevelNote a, LevelNote b)
        {
            int c = a.Beat.CompareTo(b.Beat);
            if (c != 0) return c;
            c = a.Lane.CompareTo(b.Lane);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        }

        private static double Round(double beat)
        {
            // Beats come from snapped grids; trim float noise (1e-9 beats is far below a sample).
            return Math.Round(beat, 9);
        }

        /// <summary>Parses a level. Throws <see cref="FormatException"/> for files that are not combat levels.</summary>
        public static CombatLevel FromJson(string json, List<string> warnings = null)
        {
            Dictionary<string, object> root = JsonRead.Obj(MiniJson.Parse(json));
            if (root == null) throw new FormatException("The file is not a JSON object.");
            string format = JsonRead.Str(root, "format");
            if (format.Length > 0 && format != CombatLevel.FormatId) throw new FormatException("Not a combat level file (format '" + format + "').");
            int version = JsonRead.Int(root, "version", 1);
            if (version > CombatLevel.CurrentVersion && warnings != null)
                warnings.Add("This level was saved by a newer composer (version " + version + "). Some settings may be lost when saving.");

            var level = new CombatLevel();
            level.Id = JsonRead.Str(root, "id", level.Id);
            level.Name = JsonRead.Str(root, "name", level.Name);
            level.Author = JsonRead.Str(root, "author");
            level.Description = JsonRead.Str(root, "description");
            level.SelectionWeight = Math.Max(0.01d, JsonRead.Num(root, "selectionWeight", 1d));

            Dictionary<string, object> music = JsonRead.Obj(root, "music");
            MusicSettings m = level.Music;
            if (music != null)
            {
                m.Bpm = Clamp(JsonRead.Num(music, "bpm", 120d), 1d, 999d);
                m.BeatsPerBar = Math.Max(1, Math.Min(16, JsonRead.Int(music, "beatsPerBar", 4)));
                m.OffsetSeconds = Math.Max(0d, JsonRead.Num(music, "offsetSeconds"));
                m.Volume = Clamp(JsonRead.Num(music, "volume", 1d), 0d, 1d);
                m.ChartSync = ParseEnum(JsonRead.Str(music, "chartSync"), MusicSyncMode.NextBar);
                m.EndSync = ParseEnum(JsonRead.Str(music, "endSync"), MusicSyncMode.NextBar);
                m.ChartsWaitForLoop = JsonRead.Bool(music, "chartsWaitForLoop", true);
                m.PlayerTurnVolume = Clamp(JsonRead.Num(music, "playerTurnVolume", 1d), 0d, 1d);
                m.MainVolumeOnPlayerTurn = Clamp(JsonRead.Num(music, "mainVolumeOnPlayerTurn", 0d), 0d, 1d);
                m.Intro = JsonRead.Str(music, "intro");
                m.Loop = JsonRead.Str(music, "loop");
                m.PlayerTurn = JsonRead.Str(music, "playerTurn");
                m.End = JsonRead.Str(music, "end");
                m.DefeatEnd = JsonRead.Str(music, "defeatEnd");
            }

            Dictionary<string, object> preview = JsonRead.Obj(root, "preview");
            if (preview != null)
            {
                level.Preview.PlayerTurnBars = Math.Max(0, Math.Min(64, JsonRead.Int(preview, "playerTurnBars", 2)));
                level.Preview.LoopBar = Math.Max(0, JsonRead.Int(preview, "loopBar", 0));
            }

            List<object> steps = JsonRead.Arr(root, "steps");
            var usedStepIds = new HashSet<string>();
            if (steps != null)
            {
                foreach (object so in steps)
                {
                    Dictionary<string, object> sd = JsonRead.Obj(so);
                    if (sd == null) continue;
                    var s = new LevelStep();
                    s.Id = JsonRead.Str(sd, "id", s.Id);
                    if (!usedStepIds.Add(s.Id)) { s.Id = LevelNote.NewId(); usedStepIds.Add(s.Id); }
                    s.Name = JsonRead.Str(sd, "name", "Step " + (level.Steps.Count + 1));
                    s.Animation = JsonRead.Str(sd, "animation");
                    s.Anticipation = Math.Max(0d, JsonRead.Num(sd, "anticipation", 0.45d));
                    s.EndPolicy = ParseEnum(JsonRead.Str(sd, "endPolicy"), StepEndPolicy.WaitForResolvedNotes);
                    s.LaneCount = Math.Max(1, Math.Min(LevelStep.MaxLanes, JsonRead.Int(sd, "lanes", 4)));
                    var usedNoteIds = new HashSet<string>();
                    List<object> notes = JsonRead.Arr(sd, "notes");
                    if (notes != null)
                    {
                        foreach (object no in notes)
                        {
                            Dictionary<string, object> nd = JsonRead.Obj(no);
                            if (nd == null) continue;
                            var n = new LevelNote();
                            n.Id = JsonRead.Str(nd, "id", n.Id);
                            if (n.Id.Length == 0 || !usedNoteIds.Add(n.Id)) { n.Id = LevelNote.NewId(); usedNoteIds.Add(n.Id); }
                            n.Type = JsonRead.Str(nd, "type", "normal");
                            int lane = JsonRead.Int(nd, "lane", 1);
                            if ((lane < 1 || lane > s.LaneCount) && warnings != null)
                                warnings.Add("Step '" + s.Name + "': a note on lane " + lane + " was moved into the step's " + s.LaneCount + " lanes.");
                            n.Lane = Math.Max(1, Math.Min(s.LaneCount, lane));
                            n.Beat = Math.Max(0d, JsonRead.Num(nd, "beat"));
                            n.Length = Math.Max(0d, JsonRead.Num(nd, "length"));
                            Dictionary<string, object> pd = JsonRead.Obj(nd, "params");
                            if (pd != null) foreach (KeyValuePair<string, object> kv in pd) n.Params[kv.Key] = kv.Value;
                            s.Notes.Add(n);
                        }
                    }

                    level.Steps.Add(s);
                }
            }

            if (level.Steps.Count == 0) level.Steps.Add(new LevelStep { Name = "Step 1" });
            return level;
        }

        private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : v > hi ? hi : v; }

        private static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            T v;
            return !string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out v) ? v : fallback;
        }
    }

    /// <summary>Stores audio paths relative to the level file so a level and its music can move together (e.g. in the repo).</summary>
    public static class LevelPaths
    {
        /// <summary>Absolute path of <paramref name="stored"/>; relative paths are resolved against the level file's folder.</summary>
        public static string Resolve(string stored, string levelFilePath)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            string p = stored.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(p) || string.IsNullOrEmpty(levelFilePath)) return p;
            string dir = Path.GetDirectoryName(Path.GetFullPath(levelFilePath));
            return Path.GetFullPath(Path.Combine(dir ?? "", p));
        }

        /// <summary>Path of <paramref name="absolute"/> relative to the level file's folder (forward slashes); absolute when on another drive.</summary>
        public static string MakeRelative(string absolute, string levelFilePath)
        {
            if (string.IsNullOrEmpty(absolute)) return "";
            if (string.IsNullOrEmpty(levelFilePath) || !Path.IsPathRooted(absolute)) return absolute.Replace('\\', '/');
            string fromDir = Path.GetDirectoryName(Path.GetFullPath(levelFilePath)) ?? "";
            string to = Path.GetFullPath(absolute);
            string rootA = Path.GetPathRoot(fromDir) ?? "", rootB = Path.GetPathRoot(to) ?? "";
            if (!string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase)) return to.Replace('\\', '/');

            string[] a = fromDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            string[] b = to.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            int common = 0;
            while (common < a.Length && common < b.Length && string.Equals(a[common], b[common], StringComparison.OrdinalIgnoreCase)) common++;
            var parts = new List<string>();
            for (int i = common; i < a.Length; i++) parts.Add("..");
            for (int i = common; i < b.Length; i++) parts.Add(b[i]);
            // More than 4 levels up is almost certainly not a shared folder: keep it absolute.
            if (a.Length - common > 4) return to.Replace('\\', '/');
            return string.Join("/", parts.ToArray());
        }
    }
}
