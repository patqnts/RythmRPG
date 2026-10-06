using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.EditorTools
{
    /// <summary>
    /// A frame timeline like the Animation window's, for editor tools: a ruler in seconds with frame ticks, one row per
    /// track, spans and markers you drag (snapping to frames; hold Alt for free), points inside spans (hits), shaded bands
    /// and reference lines, and a playhead. Ctrl + wheel zooms around the mouse, the wheel or the middle button pans,
    /// F fits everything. The owner rebuilds the tracks every repaint and applies the edits it is called back with.
    /// </summary>
    public sealed class FrameTimeline
    {
        public sealed class Track
        {
            public string Label = string.Empty;
            public string Tooltip = string.Empty;
            public Color Color = new(0.5f, 0.7f, 1f, 1f);
            public object Key;
            public readonly List<Item> Items = new();
        }

        public sealed class Item
        {
            public string Label = string.Empty;
            public string Tooltip = string.Empty;
            public float Start;
            /// <summary>Equal to Start for a marker.</summary>
            public float End;
            /// <summary>A lighter extension after End (an animation or effect that outlives the step). NaN = none.</summary>
            public float GhostEnd = float.NaN;
            /// <summary>A hatched lead before Start (a delay). NaN = none.</summary>
            public float LeadStart = float.NaN;
            public Color Color = new(0.35f, 0.7f, 1f, 1f);
            public bool Draggable = true;
            public bool EndResizable;
            /// <summary>The length is a guess (drawn dimmer, with a "?").</summary>
            public bool Guess;
            public object Key;
            public readonly List<Point> Points = new();

            public bool IsMarker => End <= Start + 0.00001f;
        }

        public sealed class Point
        {
            public float Time;
            public string Label = string.Empty;
            public bool Draggable;
            public object Key;
            public Color Color = new(1f, 0.35f, 0.3f, 1f);
        }

        public struct Band
        {
            public float Start;
            public float End;
            public Color Color;
            public string Label;
        }

        public struct Line
        {
            public float Time;
            public Color Color;
            public string Label;
        }

        private enum Drag { None, Move, ResizeEnd, MovePoint, Scrub, Pan }

        public const float RulerHeight = 24f;
        public const float RowHeight = 22f;

        public readonly List<Track> Tracks = new();
        public readonly List<Band> Bands = new();
        public readonly List<Line> Lines = new();
        public float FrameRate = 24f;
        public bool SnapToFrames = true;
        public float LabelWidth = 160f;
        /// <summary>Seconds at the left edge of the track area.</summary>
        public float ViewStart = -0.25f;
        public float PixelsPerSecond = 200f;
        /// <summary>Playhead time; NaN = hidden.</summary>
        public float Playhead = float.NaN;
        /// <summary>Selected item key (the owner shows its inspector).</summary>
        public object SelectedKey;
        /// <summary>Lowest time an item may be dragged to.</summary>
        public float MinTime = float.NegativeInfinity;

        /// <summary>A drag is about to change things: record Undo here.</summary>
        public Action BeginEdit;
        /// <summary>An item's start was dragged to a new time.</summary>
        public Action<Item, float> MoveStart;
        /// <summary>An item's end was dragged to a new time.</summary>
        public Action<Item, float> MoveEnd;
        /// <summary>A point inside an item was dragged to a new time.</summary>
        public Action<Item, Point, float> MovePoint;
        /// <summary>The ruler was clicked / dragged: the owner may scrub.</summary>
        public Action<float> Scrubbed;
        public Action<object> Selected;
        /// <summary>A drag ended (save / repaint).</summary>
        public Action EndEdit;

        private Drag drag;
        private Item dragItem;
        private Point dragPoint;
        private float grabOffset;
        private bool editStarted;
        private Vector2 panFrom;
        private float panViewStart;
        private Rect lastRect;
        private static GUIStyle labelStyle;
        private static GUIStyle smallStyle;
        private static GUIStyle rulerStyle;

        public float Height => RulerHeight + Mathf.Max(1, Tracks.Count) * RowHeight + 4f;

        private static readonly int[] FrameRates = { 12, 24, 30, 60 };
        private static readonly string[] FrameRateNames = { "12 fps", "24 fps", "30 fps", "60 fps" };

        /// <summary>A toolbar row: frame rate, snapping and Fit. Call inside a horizontal layout or on its own.</summary>
        public void ToolbarGUI(float width)
        {
            int index = Array.IndexOf(FrameRates, Mathf.RoundToInt(FrameRate));
            int picked = EditorGUILayout.Popup(Mathf.Max(0, index), FrameRateNames, EditorStyles.toolbarPopup, GUILayout.Width(64f));
            if (picked != index) FrameRate = FrameRates[picked];
            SnapToFrames = GUILayout.Toggle(SnapToFrames, new GUIContent("Snap", "Snap drags to frames (hold Alt while dragging for free timing)."),
                EditorStyles.toolbarButton, GUILayout.Width(44f));
            if (GUILayout.Button(new GUIContent("Fit", "Show everything (F)"), EditorStyles.toolbarButton, GUILayout.Width(34f))) FitAll(width);
        }

        public float Snap(float time)
        {
            bool free = Event.current != null && Event.current.alt;
            if (!SnapToFrames || free || FrameRate <= 0f) return time;
            return Mathf.Round(time * FrameRate) / FrameRate;
        }

        public string FormatTime(float time)
        {
            int frame = FrameRate > 0f ? Mathf.RoundToInt(time * FrameRate) : 0;
            return $"{time:0.###}s ({frame}f)";
        }

        /// <summary>Zooms so <paramref name="start"/> .. <paramref name="end"/> fills the width.</summary>
        public void Fit(float start, float end, float width)
        {
            float span = Mathf.Max(0.1f, end - start);
            float usable = Mathf.Max(50f, width - LabelWidth - 20f);
            PixelsPerSecond = Mathf.Clamp(usable / (span * 1.1f), 10f, 4000f);
            ViewStart = start - span * 0.05f;
        }

        public void FitAll(float width)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (Track track in Tracks)
                foreach (Item item in track.Items)
                {
                    min = Mathf.Min(min, float.IsNaN(item.LeadStart) ? item.Start : item.LeadStart);
                    max = Mathf.Max(max, float.IsNaN(item.GhostEnd) ? item.End : Mathf.Max(item.End, item.GhostEnd));
                }
            foreach (Band band in Bands)
            {
                min = Mathf.Min(min, band.Start);
                max = Mathf.Max(max, band.End);
            }
            foreach (Line line in Lines)
            {
                min = Mathf.Min(min, line.Time);
                max = Mathf.Max(max, line.Time);
            }
            if (float.IsInfinity(min)) Fit(0f, 1f, width);
            else Fit(min, max, width);
        }

        private float TimeToX(Rect area, float time) => area.x + (time - ViewStart) * PixelsPerSecond;
        private float XToTime(Rect area, float x) => ViewStart + (x - area.x) / PixelsPerSecond;

        /// <summary>Draws the timeline in <paramref name="rect"/> and handles its mouse and keyboard input.</summary>
        public void Draw(Rect rect)
        {
            EnsureStyles();
            lastRect = rect;
            Event e = Event.current;
            var labels = new Rect(rect.x, rect.y + RulerHeight, LabelWidth, rect.height - RulerHeight);
            var area = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth, rect.height);
            var ruler = new Rect(area.x, area.y, area.width, RulerHeight);
            var rows = new Rect(area.x, area.y + RulerHeight, area.width, area.height - RulerHeight);

            int control = GUIUtility.GetControlID(FocusType.Keyboard, rect);
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f, 1f));
                EditorGUI.DrawRect(labels, new Color(0.2f, 0.2f, 0.2f, 1f));
                for (int i = 0; i < Tracks.Count; i++)
                {
                    var row = new Rect(rows.x, rows.y + i * RowHeight, rows.width, RowHeight);
                    if (i % 2 == 1) EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.025f));
                }
            }

            GUI.BeginClip(area);
            var local = new Rect(0f, 0f, area.width, area.height);
            var localRows = new Rect(0f, RulerHeight, area.width, area.height - RulerHeight);
            if (e.type == EventType.Repaint)
            {
                DrawBands(local, localRows);
                DrawRuler(new Rect(0f, 0f, area.width, RulerHeight));
                DrawLines(local, localRows);
                DrawItems(localRows);
                DrawPlayhead(local);
            }
            GUI.EndClip();

            DrawTrackLabels(labels);
            HandleInput(e, control, area, ruler, rows);
        }

        // ---------- drawing ----------

        private void EnsureStyles()
        {
            labelStyle ??= new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip, normal = { textColor = new Color(0.95f, 0.95f, 0.95f) } };
            smallStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            rulerStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft, normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
        }

        private float LX(float time) => (time - ViewStart) * PixelsPerSecond;

        private void DrawRuler(Rect r)
        {
            EditorGUI.DrawRect(r, new Color(0.22f, 0.22f, 0.22f, 1f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(0f, 0f, 0f, 0.6f));
            float start = ViewStart;
            float end = ViewStart + r.width / PixelsPerSecond;
            // Frame ticks when there is room for them.
            if (FrameRate > 0f && PixelsPerSecond / FrameRate >= 5f)
            {
                int first = Mathf.CeilToInt(start * FrameRate);
                int last = Mathf.FloorToInt(end * FrameRate);
                for (int f = first; f <= last; f++)
                {
                    float x = LX(f / FrameRate);
                    EditorGUI.DrawRect(new Rect(x, r.yMax - 5f, 1f, 5f), new Color(1f, 1f, 1f, 0.18f));
                }
            }
            float step = NiceStep(80f / PixelsPerSecond);
            for (float t = Mathf.Floor(start / step) * step; t <= end; t += step)
            {
                float x = LX(t);
                EditorGUI.DrawRect(new Rect(x, r.y + 4f, 1f, r.height - 4f), new Color(1f, 1f, 1f, 0.35f));
                string text = FrameRate > 0f ? $"{t:0.##}s  {Mathf.RoundToInt(t * FrameRate)}f" : $"{t:0.##}s";
                GUI.Label(new Rect(x + 3f, r.y + 1f, 90f, 14f), text, rulerStyle);
            }
        }

        private static float NiceStep(float minimum)
        {
            float[] steps = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 5f, 10f };
            foreach (float s in steps) if (s >= minimum) return s;
            return 10f;
        }

        private void DrawBands(Rect local, Rect rows)
        {
            foreach (Band band in Bands)
            {
                float x0 = LX(band.Start), x1 = LX(band.End);
                if (x1 <= x0) continue;
                EditorGUI.DrawRect(new Rect(x0, rows.y, x1 - x0, rows.height), band.Color);
                if (!string.IsNullOrEmpty(band.Label))
                    GUI.Label(new Rect(x0 + 3f, rows.yMax - 14f, Mathf.Max(0f, x1 - x0 - 4f), 14f), band.Label, smallStyle);
            }
        }

        private void DrawLines(Rect local, Rect rows)
        {
            foreach (Line line in Lines)
            {
                float x = LX(line.Time);
                EditorGUI.DrawRect(new Rect(x, local.y + 6f, 2f, local.height - 6f), line.Color);
                if (!string.IsNullOrEmpty(line.Label))
                {
                    Vector2 size = smallStyle.CalcSize(new GUIContent(line.Label));
                    var tag = new Rect(x + 3f, RulerHeight - 12f, size.x + 4f, 12f);
                    EditorGUI.DrawRect(tag, new Color(line.Color.r * 0.5f, line.Color.g * 0.5f, line.Color.b * 0.5f, 0.9f));
                    GUI.Label(tag, line.Label, smallStyle);
                }
            }
        }

        private void DrawItems(Rect rows)
        {
            for (int t = 0; t < Tracks.Count; t++)
            {
                Track track = Tracks[t];
                float y = rows.y + t * RowHeight;
                foreach (Item item in track.Items) DrawItem(item, y);
            }
        }

        private void DrawItem(Item item, float y)
        {
            bool selected = item.Key != null && Equals(item.Key, SelectedKey);
            Color color = item.Color;
            if (!item.Draggable) color = Color.Lerp(color, new Color(0.45f, 0.45f, 0.45f, 1f), 0.5f);
            if (!float.IsNaN(item.LeadStart) && item.LeadStart < item.Start)
            {
                float lx0 = LX(item.LeadStart), lx1 = LX(item.Start);
                var lead = new Rect(lx0, y + RowHeight * 0.45f, lx1 - lx0, 2f);
                EditorGUI.DrawRect(lead, new Color(color.r, color.g, color.b, 0.6f));
                EditorGUI.DrawRect(new Rect(lx0, y + 6f, 1f, RowHeight - 12f), new Color(color.r, color.g, color.b, 0.6f));
            }
            if (item.IsMarker)
            {
                float x = LX(item.Start);
                if (!float.IsNaN(item.GhostEnd) && item.GhostEnd > item.Start)
                    EditorGUI.DrawRect(new Rect(x, y + 6f, LX(item.GhostEnd) - x, RowHeight - 12f), new Color(color.r, color.g, color.b, 0.25f));
                Diamond(new Vector2(x, y + RowHeight * 0.5f), 6f, color, selected);
                GUI.Label(new Rect(x + 8f, y + 3f, 220f, RowHeight - 4f), item.Label, labelStyle);
                return;
            }

            float x0 = LX(item.Start), x1 = LX(item.End);
            var bar = new Rect(x0, y + 3f, Mathf.Max(3f, x1 - x0), RowHeight - 6f);
            if (!float.IsNaN(item.GhostEnd) && item.GhostEnd > item.End)
                EditorGUI.DrawRect(new Rect(bar.xMax, bar.y + 3f, LX(item.GhostEnd) - bar.xMax, bar.height - 6f), new Color(color.r, color.g, color.b, 0.25f));
            Color fill = item.Guess ? new Color(color.r, color.g, color.b, 0.45f) : new Color(color.r, color.g, color.b, 0.85f);
            EditorGUI.DrawRect(bar, fill);
            Color edge = selected ? Color.white : new Color(0f, 0f, 0f, 0.5f);
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, 1f), edge);
            EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), edge);
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, 1f, bar.height), edge);
            EditorGUI.DrawRect(new Rect(bar.xMax - 1f, bar.y, 1f, bar.height), edge);
            if (item.EndResizable) EditorGUI.DrawRect(new Rect(bar.xMax - 3f, bar.y + 2f, 2f, bar.height - 4f), new Color(1f, 1f, 1f, 0.6f));
            GUI.Label(new Rect(bar.x + 3f, bar.y, Mathf.Max(0f, bar.width - 6f), bar.height), item.Guess ? item.Label + " ?" : item.Label, labelStyle);
            foreach (Point point in item.Points)
                Diamond(new Vector2(LX(point.Time), y + RowHeight * 0.5f), 5f, point.Color, ReferenceEquals(point, dragPoint));
        }

        // A pixel diamond from stacked rects (no Handles, so it clips with the timeline like everything else).
        private static void Diamond(Vector2 c, float r, Color color, bool selected)
        {
            Color outline = selected ? Color.white : new Color(0f, 0f, 0f, 0.8f);
            int outer = Mathf.RoundToInt(r) + 1;
            for (int i = -outer; i <= outer; i++)
            {
                float half = outer - Mathf.Abs(i);
                EditorGUI.DrawRect(new Rect(Mathf.Round(c.x - half), Mathf.Round(c.y + i), half * 2f + 1f, 1f), outline);
                float inner = half - 1f;
                if (inner >= 0f && Mathf.Abs(i) < outer)
                    EditorGUI.DrawRect(new Rect(Mathf.Round(c.x - inner), Mathf.Round(c.y + i), inner * 2f + 1f, 1f), color);
            }
        }

        private void DrawPlayhead(Rect local)
        {
            if (float.IsNaN(Playhead)) return;
            float x = LX(Playhead);
            EditorGUI.DrawRect(new Rect(x - 1f, 0f, 2f, local.height), new Color(1f, 0.25f, 0.25f, 0.95f));
            var tag = new Rect(x + 2f, 2f, 92f, 12f);
            EditorGUI.DrawRect(tag, new Color(0.6f, 0.1f, 0.1f, 0.9f));
            GUI.Label(tag, FormatTime(Playhead), smallStyle);
        }

        private void DrawTrackLabels(Rect labels)
        {
            for (int i = 0; i < Tracks.Count; i++)
            {
                Track track = Tracks[i];
                var row = new Rect(labels.x, labels.y + i * RowHeight, labels.width, RowHeight);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 3f, 3f, row.height - 6f), track.Color);
                    bool selected = track.Key != null && Equals(track.Key, SelectedKey);
                    if (selected) EditorGUI.DrawRect(row, new Color(0.24f, 0.37f, 0.59f, 0.6f));
                }
                GUI.Label(new Rect(row.x + 7f, row.y + 3f, row.width - 9f, row.height - 4f), new GUIContent(track.Label, track.Tooltip), labelStyle);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && row.Contains(Event.current.mousePosition))
                {
                    SelectedKey = track.Key;
                    Selected?.Invoke(track.Key);
                    Event.current.Use();
                }
            }
        }

        // ---------- input ----------

        private void HandleInput(Event e, int control, Rect area, Rect ruler, Rect rows)
        {
            switch (e.type)
            {
                case EventType.ScrollWheel when area.Contains(e.mousePosition):
                    if (e.control || e.command)
                    {
                        float pivot = XToTime(area, e.mousePosition.x);
                        float factor = e.delta.y > 0f ? 1f / 1.15f : 1.15f;
                        PixelsPerSecond = Mathf.Clamp(PixelsPerSecond * factor, 10f, 4000f);
                        ViewStart = pivot - (e.mousePosition.x - area.x) / PixelsPerSecond;
                    }
                    else
                    {
                        float delta = (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : e.delta.y) * 12f;
                        ViewStart += delta / PixelsPerSecond;
                    }
                    e.Use();
                    break;

                case EventType.MouseDown when area.Contains(e.mousePosition):
                    GUIUtility.keyboardControl = control;
                    if (e.button == 2 || (e.button == 0 && e.shift && !rows.Contains(e.mousePosition)))
                    {
                        drag = Drag.Pan;
                        panFrom = e.mousePosition;
                        panViewStart = ViewStart;
                        GUIUtility.hotControl = control;
                        e.Use();
                        break;
                    }
                    if (e.button != 0) break;
                    if (ruler.Contains(e.mousePosition))
                    {
                        drag = Drag.Scrub;
                        GUIUtility.hotControl = control;
                        Scrub(area, e.mousePosition.x);
                        e.Use();
                        break;
                    }
                    if (rows.Contains(e.mousePosition) && Pick(area, rows, e.mousePosition, out Item item, out Point point, out bool onEnd))
                    {
                        SelectedKey = item.Key;
                        Selected?.Invoke(item.Key);
                        dragItem = item;
                        dragPoint = point;
                        editStarted = false;
                        float mouseTime = XToTime(area, e.mousePosition.x);
                        if (point != null && point.Draggable)
                        {
                            drag = Drag.MovePoint;
                            grabOffset = mouseTime - point.Time;
                        }
                        else if (onEnd && item.EndResizable)
                        {
                            drag = Drag.ResizeEnd;
                            grabOffset = mouseTime - item.End;
                        }
                        else if (item.Draggable)
                        {
                            drag = Drag.Move;
                            grabOffset = mouseTime - item.Start;
                        }
                        else drag = Drag.None;
                        if (drag != Drag.None) GUIUtility.hotControl = control;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == control:
                    switch (drag)
                    {
                        case Drag.Pan:
                            ViewStart = panViewStart - (e.mousePosition.x - panFrom.x) / PixelsPerSecond;
                            break;
                        case Drag.Scrub:
                            Scrub(area, e.mousePosition.x);
                            break;
                        case Drag.Move:
                        case Drag.ResizeEnd:
                        case Drag.MovePoint:
                            float time = Mathf.Max(MinTime, Snap(XToTime(area, e.mousePosition.x) - grabOffset));
                            if (!editStarted)
                            {
                                editStarted = true;
                                BeginEdit?.Invoke();
                            }
                            if (drag == Drag.Move) MoveStart?.Invoke(dragItem, time);
                            else if (drag == Drag.ResizeEnd) MoveEnd?.Invoke(dragItem, Mathf.Max(dragItem.Start, time));
                            else MovePoint?.Invoke(dragItem, dragPoint, time);
                            break;
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == control:
                    GUIUtility.hotControl = 0;
                    if (editStarted) EndEdit?.Invoke();
                    drag = Drag.None;
                    dragItem = null;
                    dragPoint = null;
                    editStarted = false;
                    e.Use();
                    break;

                case EventType.KeyDown when GUIUtility.keyboardControl == control && e.keyCode == KeyCode.F:
                    FitAll(lastRect.width);
                    e.Use();
                    break;

                case EventType.Repaint:
                    // Resize cursors over draggable ends.
                    foreach (Rect handle in ResizeHandles(area, rows)) EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);
                    break;
            }
        }

        private void Scrub(Rect area, float x)
        {
            Playhead = Snap(XToTime(area, x));
            Scrubbed?.Invoke(Playhead);
        }

        private IEnumerable<Rect> ResizeHandles(Rect area, Rect rows)
        {
            for (int t = 0; t < Tracks.Count; t++)
                foreach (Item item in Tracks[t].Items)
                    if (item.EndResizable && !item.IsMarker)
                        yield return new Rect(TimeToX(area, item.End) - 4f, rows.y + t * RowHeight, 8f, RowHeight);
        }

        private bool Pick(Rect area, Rect rows, Vector2 mouse, out Item picked, out Point pickedPoint, out bool onEnd)
        {
            picked = null;
            pickedPoint = null;
            onEnd = false;
            int row = Mathf.FloorToInt((mouse.y - rows.y) / RowHeight);
            if (row < 0 || row >= Tracks.Count) return false;
            float best = float.MaxValue;
            // Last drawn wins (top-most); markers and points are small, so they take priority within a few pixels.
            foreach (Item item in Tracks[row].Items)
            {
                foreach (Point point in item.Points)
                {
                    float d = Mathf.Abs(TimeToX(area, point.Time) - mouse.x);
                    if (d <= 6f && d < best)
                    {
                        best = d;
                        picked = item;
                        pickedPoint = point;
                        onEnd = false;
                    }
                }
                if (pickedPoint != null) continue;
                float x0 = TimeToX(area, item.Start);
                if (item.IsMarker)
                {
                    float d = Mathf.Abs(x0 - mouse.x);
                    if (d <= 7f && d < best)
                    {
                        best = d;
                        picked = item;
                        onEnd = false;
                    }
                    continue;
                }
                float x1 = Mathf.Max(x0 + 3f, TimeToX(area, item.End));
                if (mouse.x < x0 - 2f || mouse.x > x1 + 4f) continue;
                bool nearEnd = item.EndResizable && Mathf.Abs(mouse.x - x1) <= 5f;
                float score = nearEnd ? 0f : 10f;
                if (score < best)
                {
                    best = score;
                    picked = item;
                    onEnd = nearEnd;
                }
            }
            return picked != null;
        }
    }
}
