using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    /// <summary>
    /// Changes how many lanes a chart has (1 to <see cref="RhythmChart.MaxLanes"/>). Used by the Rhythm Composer's Lanes
    /// menu, and by <c>Tools > Rhythm > Fit All Charts To 4 Lanes</c> for charts from the old 5-lane layout.
    /// <list type="bullet">
    /// <item>More lanes: empty lanes are added (Key Identity count+1.., default colours).</item>
    /// <item>Fewer lanes: notes on removed lanes move to the new last lane. A moved note that would overlap a note
    /// already there (same time within 1 ms, or inside a hold) is removed instead. Ping-Pong sequences that used a
    /// removed lane use the last lane instead; pattern lane masks are remapped.</item>
    /// </list>
    /// Lanes end up ordered by Key Identity 1..count. The golden test baseline
    /// (Tests/EditMode/Golden/schedule.txt) follows the moved and removed notes. Undo covers the charts, not that file.
    /// </summary>
    public static class ChartLaneConverter
    {
        private const string MenuPath = "Tools/Rhythm/Fit All Charts To 4 Lanes";
        private const string GoldenPath = "Assets/RhythmSystem/Tests/EditMode/Golden/schedule.txt";
        private const double SameTimeSeconds = 0.001d;

        public sealed class Result
        {
            public int Moved;
            public int Removed;
            public int LanesDeleted;
            public int LanesAdded;
            public readonly Dictionary<string, string> MovedNoteLanes = new();
            public readonly HashSet<string> RemovedNoteIds = new();
        }

        [MenuItem(MenuPath)]
        private static void FitAll()
        {
            List<RhythmChart> charts = AssetDatabase.FindAssets("t:" + nameof(RhythmChart))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<RhythmChart>)
                .Where(chart => chart != null)
                .ToList();
            List<RhythmChart> needing = charts.Where(HasTooManyLanes).ToList();
            if (needing.Count == 0)
            {
                EditorUtility.DisplayDialog("Fit Charts To 4 Lanes",
                    $"All {charts.Count} chart(s) already use at most {RhythmChart.MaxLanes} lanes.", "OK");
                return;
            }

            string list = string.Join("\n", needing.Select(chart => "- " + chart.name));
            if (!EditorUtility.DisplayDialog("Fit Charts To 4 Lanes",
                    $"{needing.Count} chart(s) have more than {RhythmChart.MaxLanes} lanes:\n{list}\n\n" +
                    $"Notes on the extra lanes move to lane {RhythmChart.MaxLanes}; a note that would overlap one there " +
                    "(same time, or inside a hold) is removed. Then the extra lanes are deleted.\n\n" +
                    "Close the Rhythm Composer first. Edit > Undo reverts the charts.",
                    "Fit", "Cancel"))
                return;

            Undo.RecordObjects(needing.Cast<UnityEngine.Object>().ToArray(), "Fit Charts To 4 Lanes");
            var report = new StringBuilder("[Rhythm] Fitted charts to 4 lanes:\n");
            var goldenChanges = new Dictionary<string, Result>();
            foreach (RhythmChart chart in needing)
            {
                Result result = SetLaneCount(chart, RhythmChart.MaxLanes);
                EditorUtility.SetDirty(chart);
                goldenChanges[AssetDatabase.GetAssetPath(chart)] = result;
                report.AppendLine("  " + chart.name + ": " + Describe(result));
            }
            AssetDatabase.SaveAssets();
            if (UpdateGoldenBaseline(goldenChanges)) report.AppendLine("  Updated " + GoldenPath + ".");
            Debug.Log(report.ToString());
        }

        public static bool HasTooManyLanes(RhythmChart chart) =>
            chart != null && chart.Lanes.Any(lane => lane != null && lane.KeyIdentity > RhythmChart.MaxLanes);

        public static string Describe(Result result) =>
            $"{result.LanesAdded} lane(s) added, {result.LanesDeleted} deleted, {result.Moved} note(s) moved, " +
            $"{result.Removed} removed (overlap).";

        /// <summary>Gives <paramref name="chart"/> exactly <paramref name="count"/> lanes (in place; no Undo, no saving).</summary>
        public static Result SetLaneCount(RhythmChart chart, int count)
        {
            var result = new Result();
            if (chart == null) return result;
            count = Mathf.Clamp(count, 1, RhythmChart.MaxLanes);

            List<RhythmLaneData> oldLanes = chart.Lanes.ToList();

            // Lanes 1..count must exist (the last one receives notes from removed lanes).
            for (int identity = 1; identity <= count; identity++)
            {
                int id = identity;
                if (chart.Lanes.Any(lane => lane != null && lane.KeyIdentity == id)) continue;
                chart.Lanes.Add(new RhythmLaneData($"Lane {identity}", identity,
                    RhythmComposerAssetFactory.LaneColor(identity - 1), KeyType.DEFAULT));
                result.LanesAdded++;
            }

            var removedIds = new HashSet<string>(chart.Lanes
                .Where(lane => lane != null && (lane.KeyIdentity < 1 || lane.KeyIdentity > count))
                .Select(lane => lane.Id));
            RhythmLaneData target = chart.Lanes.First(lane => lane != null && lane.KeyIdentity == count);

            if (removedIds.Count > 0)
            {
                // Notes: move into the target lane unless they would overlap one already there.
                List<RhythmNoteData> occupied = chart.Notes.Where(note => note != null && note.LaneId == target.Id).ToList();
                List<RhythmNoteData> toMove = chart.Notes
                    .Where(note => note != null && removedIds.Contains(note.LaneId))
                    .OrderBy(note => note.HitTime)
                    .ToList();
                foreach (RhythmNoteData note in toMove)
                {
                    if (occupied.Any(other => Overlaps(other, note)))
                    {
                        chart.Notes.Remove(note);
                        result.RemovedNoteIds.Add(note.Id);
                        result.Removed++;
                        continue;
                    }
                    note.LaneId = target.Id;
                    occupied.Add(note);
                    result.MovedNoteLanes[note.Id] = target.Id;
                    result.Moved++;
                }

                // Sequence attacks.
                foreach (SequenceActivationData sequence in chart.Sequences.Where(sequence => sequence != null))
                {
                    if (sequence.LaneIds.RemoveAll(id => removedIds.Contains(id)) > 0 && !sequence.LaneIds.Contains(target.Id))
                        sequence.LaneIds.Add(target.Id);
                }

                result.LanesDeleted = chart.Lanes.RemoveAll(lane => lane != null && removedIds.Contains(lane.Id));
            }

            chart.Lanes.RemoveAll(lane => lane == null);
            chart.Lanes.Sort((a, b) => a.KeyIdentity.CompareTo(b.KeyIdentity));

            // Patterns: bit i = lane index i (in the lane list). Remap to the new order; removed lanes map to the target.
            var newIndex = new Dictionary<string, int>();
            for (int i = 0; i < chart.Lanes.Count; i++) newIndex[chart.Lanes[i].Id] = i;
            foreach (PatternInstance pattern in chart.Patterns.Where(pattern => pattern != null && pattern.LaneMask != 0))
            {
                int mask = 0;
                for (int i = 0; i < oldLanes.Count && i < 31; i++)
                {
                    if ((pattern.LaneMask & (1 << i)) == 0 || oldLanes[i] == null) continue;
                    string id = removedIds.Contains(oldLanes[i].Id) ? target.Id : oldLanes[i].Id;
                    if (newIndex.TryGetValue(id, out int index)) mask |= 1 << index;
                }
                pattern.LaneMask = mask == 0 ? 1 << newIndex[target.Id] : mask;
            }
            return result;
        }

        private static bool Overlaps(RhythmNoteData a, RhythmNoteData b)
        {
            double aEnd = Math.Max(a.HitTime, a.EndTime);
            double bEnd = Math.Max(b.HitTime, b.EndTime);
            return a.HitTime <= bEnd + SameTimeSeconds && b.HitTime <= aEnd + SameTimeSeconds;
        }

        /// <summary>
        /// Keeps the golden test baseline in step with charts whose notes moved or were removed. Keyed by asset path.
        /// Format: "# &lt;asset path&gt;" headers, then id|lane|type|hit|travel|hold|speed|damage per note.
        /// </summary>
        public static bool UpdateGoldenBaseline(Dictionary<string, Result> changes)
        {
            if (changes == null || changes.Count == 0 || !File.Exists(GoldenPath)) return false;
            string text = File.ReadAllText(GoldenPath);
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var output = new List<string>(lines.Length);
            Result current = null;
            bool changed = false;
            foreach (string line in lines)
            {
                if (line.StartsWith("# "))
                {
                    changes.TryGetValue(line.Substring(2).Trim(), out current);
                    output.Add(line);
                    continue;
                }
                if (current == null || string.IsNullOrWhiteSpace(line))
                {
                    output.Add(line);
                    continue;
                }
                string[] parts = line.Split('|');
                if (current.RemovedNoteIds.Contains(parts[0]))
                {
                    changed = true;
                    continue;
                }
                if (parts.Length > 1 && current.MovedNoteLanes.TryGetValue(parts[0], out string lane) && parts[1] != lane)
                {
                    parts[1] = lane;
                    output.Add(string.Join("|", parts));
                    changed = true;
                    continue;
                }
                output.Add(line);
            }
            if (!changed) return false;
            File.WriteAllText(GoldenPath, string.Join(newline, output));
            AssetDatabase.ImportAsset(GoldenPath);
            return true;
        }
    }
}
