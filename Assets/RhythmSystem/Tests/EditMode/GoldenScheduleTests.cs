using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace RythmRPG.Rhythm.Tests
{
    /// <summary>
    /// Compares every existing chart against a baseline captured from the chart YAML before the overhaul
    /// (Golden/schedule.txt: id|lane|type|hit|travel|hold|speed|damage), then proves the beat-based
    /// migration reproduces the same hit/travel/hold seconds.
    /// </summary>
    public class GoldenScheduleTests
    {
        private const string GoldenPath = "Assets/RhythmSystem/Tests/EditMode/Golden/schedule.txt";
        private const double Tol = 1e-6;

        private static Dictionary<string, Dictionary<string, string[]>> LoadGolden()
        {
            var result = new Dictionary<string, Dictionary<string, string[]>>();
            Dictionary<string, string[]> current = null;
            foreach (string line in File.ReadAllLines(GoldenPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("# "))
                {
                    current = new Dictionary<string, string[]>();
                    result[line.Substring(2).Trim()] = current;
                    continue;
                }

                string[] p = line.Split('|');
                current[p[0]] = p;
            }

            return result;
        }

        private static double D(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }

        [Test]
        public void ExistingCharts_MatchGoldenBaseline_AndSurviveMigration()
        {
            var golden = LoadGolden();
            Assert.IsNotEmpty(golden);
            foreach (var kv in golden)
            {
                var chart = AssetDatabase.LoadAssetAtPath<RhythmChart>(kv.Key);
                Assert.IsNotNull(chart, "Missing chart " + kv.Key);
                Assert.AreEqual(kv.Value.Count, chart.Notes.Count, "Note count changed in " + kv.Key);

                var tempo = new TempoMap(chart.Bpm, chart.BeatsPerMeasure);
                // Setting the audio offset in the composer moves every note by the same amount; the baseline stores
                // times relative to it so aligning a chart to music does not count as a schedule change.
                double offset = chart.AudioOffsetSeconds;
                var byId = new Dictionary<string, NoteInstance>();
                foreach (NoteInstance inst in NoteMigration.ToInstances(chart.Notes, tempo)) byId[inst.Id] = inst;
                Assert.AreEqual(chart.Notes.Count, byId.Count, "Migration dropped notes in " + kv.Key);

                foreach (RhythmNoteData n in chart.Notes)
                {
                    string[] g;
                    Assert.IsTrue(kv.Value.TryGetValue(n.Id, out g), "Unknown note " + n.Id + " in " + kv.Key);
                    string where = kv.Key + " / " + n.Id;
                    Assert.AreEqual(g[1], n.LaneId, where);
                    Assert.AreEqual(int.Parse(g[2], CultureInfo.InvariantCulture), (int)n.NoteType, where);
                    Assert.AreEqual(D(g[3]) + offset, n.HitTime, Tol, where + " hit");
                    Assert.AreEqual(D(g[4]), n.TravelTime, Tol, where + " travel");
                    Assert.AreEqual(D(g[5]), n.HoldDuration, Tol, where + " hold");

                    NoteInstance i = byId[n.Id];
                    Assert.AreEqual(D(g[3]) + offset, NoteMigration.HitSeconds(i, tempo), Tol, where + " migrated hit");
                    Assert.AreEqual(D(g[4]), NoteMigration.TravelSeconds(i, tempo), Tol, where + " migrated travel");
                    double holdSeconds = tempo.BeatToSeconds(i.HitBeat + i.HoldBeats) - tempo.BeatToSeconds(i.HitBeat);
                    Assert.AreEqual(D(g[5]), holdSeconds, Tol, where + " migrated hold");
                }
            }
        }
    }
}
