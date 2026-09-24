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
    /// One-off migration from the 5-lane layout to 4 lanes (GameInput.LaneCount = 4).
    /// For every RhythmChart asset with lanes past lane 4:
    /// <list type="bullet">
    /// <item>Notes on those lanes move to lane 4. A moved note that would overlap a note already in lane 4 (same time
    /// within 1 ms, or inside a hold) is removed instead.</item>
    /// <item>Sequence attacks (Ping-Pong) that used a removed lane use lane 4 instead.</item>
    /// <item>Pattern lane masks are remapped to the new lane order.</item>
    /// <item>The extra lanes are deleted.</item>
    /// </list>
    /// The golden test baseline (Tests/EditMode/Golden/schedule.txt) is updated to match (lane of moved notes,
    /// removed notes). Undo reverts the charts; the baseline file is not covered by Undo.
    /// Close the Rhythm Composer before running it, so an open session does not save the old lanes back.
    /// </summary>
    public static class ChartLaneConverter
    {
        private const string MenuPath = "Tools/Rhythm/Convert All Charts To 4 Lanes";
        private const string GoldenPath = "Assets/RhythmSystem/Tests/EditMode/Golden/schedule.txt";
        private const double SameTimeSeconds = 0.001d;

        public const int TargetLaneCount = RhythmComposerAssetFactory.DefaultLaneCount;

        public sealed class Result
        {
            public int Moved;
            public int Removed;
            public int LanesDeleted;
            public readonly Dictionary<string, string> MovedNoteLanes = new();
            public readonly HashSet<string> RemovedNoteIds = new();
        }

        [MenuItem(MenuPath)]
        private static void ConvertAll()
        {
            List<RhythmChart> charts = AssetDatabase.FindAssets("t:" + nameof(RhythmChart))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<RhythmChart>)
                .Where(chart => chart != null)
                .ToList();
            List<RhythmChart> needing = charts.Where(NeedsConversion).ToList();
            if (needing.Count == 0)
            {
                EditorUtility.DisplayDialog("Convert Charts To 4 Lanes",
                    $"All {charts.Count} chart(s) already use lanes 1-{TargetLaneCount}.", "OK");
                return;
            }

            string list = string.Join("\n", needing.Select(chart => "- " + chart.name));
            if (!EditorUtility.DisplayDialog("Convert Charts To 4 Lanes",
                    $"{needing.Count} chart(s) have lanes past lane {TargetLaneCount}:\n{list}\n\n" +
                    $"Notes on those lanes move to lane {TargetLaneCount}. A note that would overlap a lane {TargetLaneCount} " +
                    "note (same time, or inside a hold) is removed. Then the extra lanes are deleted.\n\n" +
                    "Close the Rhythm Composer first. Edit > Undo reverts the charts.",
                    "Convert", "Cancel"))
                return;

            Undo.RecordObjects(needing.Cast<UnityEngine.Object>().ToArray(), "Convert Charts To 4 Lanes");
            var report = new StringBuilder("[Rhythm] Converted charts to 4 lanes:\n");
            var goldenChanges = new Dictionary<string, Result>();
            foreach (RhythmChart chart in needing)
            {
                Result result = Convert(chart);
                EditorUtility.SetDirty(chart);
                goldenChanges[AssetDatabase.GetAssetPath(chart)] = result;
                report.AppendLine($"  {chart.name}: {result.Moved} note(s) moved to lane {TargetLaneCount}, " +
                                  $"{result.Removed} removed (overlap), {result.LanesDeleted} lane(s) deleted.");
            }
            AssetDatabase.SaveAssets();
            if (UpdateGoldenBaseline(goldenChanges)) report.AppendLine("  Updated " + GoldenPath + ".");
            Debug.Log(report.ToString());
        }

        public static bool NeedsConversion(RhythmChart chart) =>
            chart != null && chart.Lanes.Any(lane => lane != null && lane.KeyIdentity > TargetLaneCount);

        /// <summary>Converts one chart in place (no Undo, no saving).</summary>
        public static Result Convert(RhythmChart chart)
        {
            var result = new Result();
            if (!NeedsConversion(chart)) return result;

            List<RhythmLaneData> oldLanes = chart.Lanes.ToList();
            var extraIds = new HashSet<string>(oldLanes
                .Where(lane => lane != null && lane.KeyIdentity > TargetLaneCount)
                .Select(lane => lane.Id));

            RhythmLaneData target = oldLanes
                .Where(lane => lane != null && lane.KeyIdentity >= 1 && lane.KeyIdentity <= TargetLaneCount)
                .OrderByDescending(lane => lane.KeyIdentity)
                .FirstOrDefault();
            if (target == null)
            {
                RhythmLaneData template = oldLanes.First(lane => lane != null && extraIds.Contains(lane.Id));
                target = new RhythmLaneData($"Lane {TargetLaneCount}", TargetLaneCount, template.Color, template.KeyType);
                chart.Lanes.Add(target);
                oldLanes.Add(target);
            }

            // Notes: move into the target lane unless they would overlap one already there.
            List<RhythmNoteData> occupied = chart.Notes.Where(note => note != null && note.LaneId == target.Id).ToList();
            List<RhythmNoteData> toMove = chart.Notes
                .Where(note => note != null && extraIds.Contains(note.LaneId))
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
                if (sequence.LaneIds.RemoveAll(id => extraIds.Contains(id)) > 0 && !sequence.LaneIds.Contains(target.Id))
                    sequence.LaneIds.Add(target.Id);
            }

            // Patterns: bit i = lane index i. Re-index for the lanes that remain; removed lanes map to the target.
            var newIndex = new Dictionary<string, int>();
            int next = 0;
            foreach (RhythmLaneData lane in oldLanes)
                if (lane != null && !extraIds.Contains(lane.Id)) newIndex[lane.Id] = next++;
            foreach (PatternInstance pattern in chart.Patterns.Where(pattern => pattern != null && pattern.LaneMask != 0))
            {
                int mask = 0;
                for (int i = 0; i < oldLanes.Count && i < 31; i++)
                {
                    if ((pattern.LaneMask & (1 << i)) == 0 || oldLanes[i] == null) continue;
                    string id = extraIds.Contains(oldLanes[i].Id) ? target.Id : oldLanes[i].Id;
                    if (newIndex.TryGetValue(id, out int index)) mask |= 1 << index;
                }
                pattern.LaneMask = mask;
            }

            result.LanesDeleted = chart.Lanes.RemoveAll(lane => lane != null && extraIds.Contains(lane.Id));
            return result;
        }

        private static bool Overlaps(RhythmNoteData a, RhythmNoteData b)
        {
            double aEnd = Math.Max(a.HitTime, a.EndTime);
            double bEnd = Math.Max(b.HitTime, b.EndTime);
            return a.HitTime <= bEnd + SameTimeSeconds && b.HitTime <= aEnd + SameTimeSeconds;
        }

        // Golden format: "# <asset path>" headers, then id|lane|type|hit|travel|hold|speed|damage per note.
        private static bool UpdateGoldenBaseline(Dictionary<string, Result> changes)
        {
            if (!File.Exists(GoldenPath)) return false;
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
