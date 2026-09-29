using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Turns a saved Profiler capture (ProfilerCaptures/*.data) into a readable text report next to it
/// (*.report.md): frame time stats, the most expensive markers on the main thread and the render thread
/// (self and total time, averaged per frame), the biggest spikes and what was in them.
/// Tools > Rythm RPG > Profiling > Report Latest Capture (runs once by itself for the newest capture).
/// </summary>
[InitializeOnLoad]
internal static class ProfilerCaptureReport
{
    private const string AutoKey = "RythmRPG.ProfilerCaptureReport.AutoRan.v1";
    private const int MaxDepth = 6;

    static ProfilerCaptureReport()
    {
        if (EditorPrefs.GetBool(AutoKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            EditorPrefs.SetBool(AutoKey, true);
            string latest = LatestCapture();
            if (latest != null && !File.Exists(ReportPath(latest))) Report(latest);
        };
    }

    [MenuItem("Tools/Rythm RPG/Profiling/Report Latest Capture")]
    private static void ReportLatest()
    {
        string latest = LatestCapture();
        if (latest == null)
        {
            EditorUtility.DisplayDialog("Profiler Report", "No .data capture in the ProfilerCaptures folder.", "OK");
            return;
        }
        Report(latest);
    }

    private static string CaptureFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ProfilerCaptures");

    private static string LatestCapture()
    {
        if (!Directory.Exists(CaptureFolder)) return null;
        return new DirectoryInfo(CaptureFolder).GetFiles("*.data").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }

    private static string ReportPath(string capture) => Path.ChangeExtension(capture, ".report.md");

    private sealed class Stat
    {
        public double total;
        public double self;
        public double calls;
        public int frames;
        public double max;
    }

    private sealed class ThreadStats
    {
        public readonly Dictionary<string, Stat> byPath = new();
        public readonly Dictionary<string, Stat> byName = new();
        public int frames;
    }

    private static void Report(string capture)
    {
        try
        {
            EditorUtility.DisplayProgressBar("Profiler Report", "Loading " + Path.GetFileName(capture), 0f);
            if (!ProfilerDriver.LoadProfile(capture, false))
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Profiler Report", "Could not load " + capture, "OK");
                return;
            }

            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var frameMs = new List<(int frame, float ms)>();
            var threads = new Dictionary<string, ThreadStats>();
            var spikeTops = new Dictionary<int, List<(string name, float self)>>();
            var children = new List<int>();

            for (int frame = first; frame <= last; frame++)
            {
                if ((frame - first) % 20 == 0)
                    EditorUtility.DisplayProgressBar("Profiler Report", $"Frame {frame - first + 1} / {last - first + 1}",
                        (frame - first) / (float)Math.Max(1, last - first));

                for (int thread = 0; thread < 64; thread++)
                {
                    string threadName;
                    using (RawFrameDataView raw = ProfilerDriver.GetRawFrameDataView(frame, thread))
                    {
                        if (raw == null || !raw.valid) break;
                        threadName = raw.threadName;
                    }
                    bool main = thread == 0;
                    bool render = threadName == "Render Thread";
                    if (!main && !render) continue;

                    using HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, thread,
                        HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
                    if (view == null || !view.valid) continue;
                    if (main) frameMs.Add((frame, view.frameTimeMs));

                    string key = main ? "Main Thread" : "Render Thread";
                    if (!threads.TryGetValue(key, out ThreadStats stats)) threads[key] = stats = new ThreadStats();
                    stats.frames++;
                    var frameSelf = new Dictionary<string, float>();
                    Walk(view, view.GetRootItemID(), "", 0, stats, frameSelf, children);
                    if (main)
                        spikeTops[frame] = frameSelf.OrderByDescending(p => p.Value).Take(8).Select(p => (p.Key, p.Value)).ToList();
                }
            }

            string text = Build(capture, frameMs, threads, spikeTops);
            File.WriteAllText(ReportPath(capture), text);
            EditorUtility.ClearProgressBar();
            Debug.Log("[Profiler Report] Written: " + ReportPath(capture));
            EditorUtility.RevealInFinder(ReportPath(capture));
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(e);
        }
    }

    private static void Walk(HierarchyFrameDataView view, int id, string parentPath, int depth, ThreadStats stats,
        Dictionary<string, float> frameSelf, List<int> scratch)
    {
        var kids = new List<int>();
        view.GetItemChildren(id, kids);
        foreach (int child in kids)
        {
            string name = view.GetItemName(child);
            float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
            float self = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
            float calls = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
            string path = parentPath.Length == 0 ? name : parentPath + " > " + name;

            if (depth < MaxDepth) Add(stats.byPath, path, total, self, calls);
            Add(stats.byName, name, total, self, calls);
            frameSelf[name] = (frameSelf.TryGetValue(name, out float s) ? s : 0f) + self;

            if (total >= 0.01f) Walk(view, child, depth < MaxDepth ? path : parentPath, depth + 1, stats, frameSelf, scratch);
        }
    }

    private static void Add(Dictionary<string, Stat> map, string key, float total, float self, float calls)
    {
        if (!map.TryGetValue(key, out Stat stat)) map[key] = stat = new Stat();
        stat.total += total;
        stat.self += self;
        stat.calls += calls;
        stat.frames++;
        stat.max = Math.Max(stat.max, total);
    }

    private static string Build(string capture, List<(int frame, float ms)> frameMs, Dictionary<string, ThreadStats> threads,
        Dictionary<int, List<(string name, float self)>> spikeTops)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Profiler report: " + Path.GetFileName(capture));
        sb.AppendLine();
        if (frameMs.Count == 0)
        {
            sb.AppendLine("No main-thread frames found.");
            return sb.ToString();
        }

        List<float> sorted = frameMs.Select(f => f.ms).OrderBy(v => v).ToList();
        float P(float q) => sorted[Mathf.Clamp((int)(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
        float mean = sorted.Average();
        sb.AppendLine($"Frames: {frameMs.Count}  |  Unity {Application.unityVersion}");
        sb.AppendLine();
        sb.AppendLine("| | ms | fps |");
        sb.AppendLine("|---|---|---|");
        void Row(string label, float ms) => sb.AppendLine(string.Format(c, "| {0} | {1:0.00} | {2:0} |", label, ms, ms > 0 ? 1000f / ms : 0));
        Row("min", sorted[0]);
        Row("median", P(0.5f));
        Row("mean", mean);
        Row("95th %", P(0.95f));
        Row("99th %", P(0.99f));
        Row("max", sorted[sorted.Count - 1]);
        sb.AppendLine();
        int over16 = sorted.Count(v => v > 16.7f), over33 = sorted.Count(v => v > 33.3f);
        sb.AppendLine($"Frames over 16.7 ms: {over16}  |  over 33.3 ms: {over33}");
        sb.AppendLine();

        foreach (KeyValuePair<string, ThreadStats> pair in threads)
        {
            ThreadStats stats = pair.Value;
            double n = Math.Max(1, stats.frames);
            sb.AppendLine($"## {pair.Key} ({stats.frames} frames)");
            sb.AppendLine();
            sb.AppendLine("### Hierarchy (average total ms per frame, top 45)");
            sb.AppendLine();
            sb.AppendLine("| avg total | avg self | max total | calls/frame | path |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (KeyValuePair<string, Stat> p in stats.byPath.OrderByDescending(p => p.Value.total).Take(45))
                sb.AppendLine(string.Format(c, "| {0:0.000} | {1:0.000} | {2:0.00} | {3:0.#} | {4} |",
                    p.Value.total / n, p.Value.self / n, p.Value.max, p.Value.calls / n, p.Key));
            sb.AppendLine();
            sb.AppendLine("### Self time by marker (average ms per frame, top 40)");
            sb.AppendLine();
            sb.AppendLine("| avg self | avg total | calls/frame | marker |");
            sb.AppendLine("|---|---|---|---|");
            foreach (KeyValuePair<string, Stat> p in stats.byName.OrderByDescending(p => p.Value.self).Take(40))
                sb.AppendLine(string.Format(c, "| {0:0.000} | {1:0.000} | {2:0.#} | {3} |",
                    p.Value.self / n, p.Value.total / n, p.Value.calls / n, p.Key));
            sb.AppendLine();
        }

        sb.AppendLine("## Worst 12 frames (main thread, top self-time markers)");
        sb.AppendLine();
        foreach ((int frame, float ms) in frameMs.OrderByDescending(f => f.ms).Take(12))
        {
            sb.AppendLine(string.Format(c, "- frame {0}: {1:0.00} ms", frame, ms));
            if (spikeTops.TryGetValue(frame, out List<(string name, float self)> tops))
                foreach ((string name, float self) in tops)
                    sb.AppendLine(string.Format(c, "  - {0:0.00} ms  {1}", self, name));
        }
        return sb.ToString();
    }
}
