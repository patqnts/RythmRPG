using System;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public readonly struct RhythmTimelineLayout
    {
        public Rect LabelsHeaderRect { get; }
        public Rect LabelsRect { get; }
        public Rect RulerRect { get; }
        public Rect LanesRect { get; }
        public Rect HorizontalScrollbarRect { get; }
        public Rect VerticalScrollbarRect { get; }

        public RhythmTimelineLayout(Rect totalRect, float labelWidth = 145f, float scrollbarSize = 15f)
        {
            float contentHeight = Mathf.Max(1f, totalRect.height - scrollbarSize);
            LabelsHeaderRect = new Rect(totalRect.x, totalRect.y, labelWidth, RhythmTimelineGeometry.RulerHeight);
            LabelsRect = new Rect(totalRect.x, totalRect.y + RhythmTimelineGeometry.RulerHeight, labelWidth, contentHeight - RhythmTimelineGeometry.RulerHeight);
            RulerRect = new Rect(totalRect.x + labelWidth, totalRect.y, totalRect.width - labelWidth - scrollbarSize, RhythmTimelineGeometry.RulerHeight);
            LanesRect = new Rect(RulerRect.x, RulerRect.yMax, RulerRect.width, contentHeight - RhythmTimelineGeometry.RulerHeight);
            HorizontalScrollbarRect = new Rect(RulerRect.x, totalRect.yMax - scrollbarSize, RulerRect.width, scrollbarSize);
            VerticalScrollbarRect = new Rect(RulerRect.xMax, LabelsRect.y, scrollbarSize, LabelsRect.height);
        }
    }

    public sealed class RhythmTimelineRenderer
    {
        private static readonly Color Background = new Color(0.105f, 0.11f, 0.125f, 1f);
        private static readonly Color LaneOdd = new Color(0.145f, 0.15f, 0.17f, 1f);
        private static readonly Color LaneEven = new Color(0.125f, 0.13f, 0.15f, 1f);
        private static readonly Color BeatLine = new Color(0.42f, 0.44f, 0.49f, 0.48f);
        private static readonly Color SubdivisionLine = new Color(0.32f, 0.34f, 0.38f, 0.25f);
        private static readonly Color MeasureLine = new Color(0.82f, 0.84f, 0.90f, 0.72f);
        private static readonly Color SelectionColor = new Color(1f, 0.86f, 0.22f, 1f);
        private static readonly Color PlayheadColor = new Color(1f, 0.28f, 0.24f, 1f);

        private readonly GUIStyle centeredLabel;
        private readonly GUIStyle laneLabel;
        private readonly GUIStyle noteLabel;
        private readonly GUIStyle rulerLabel;

        public RhythmTimelineRenderer()
        {
            centeredLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            laneLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            noteLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            noteLabel.normal.textColor = Color.white;
            rulerLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft };
        }

        public void Draw(RhythmChart chart, RhythmTimelineLayout layout, float pixelsPerSecond, double scrollTime, float verticalScroll, double playhead, string selectedNoteId)
        {
            EditorGUI.DrawRect(layout.LabelsHeaderRect, new Color(0.16f, 0.17f, 0.19f));
            GUI.Label(layout.LabelsHeaderRect, "LANES", centeredLabel);
            DrawLabels(chart, layout.LabelsRect, verticalScroll);
            DrawRuler(chart, layout.RulerRect, pixelsPerSecond, scrollTime, playhead);
            DrawCanvas(chart, layout.LanesRect, pixelsPerSecond, scrollTime, verticalScroll, playhead, selectedNoteId);
        }

        private void DrawLabels(RhythmChart chart, Rect rect, float verticalScroll)
        {
            EditorGUI.DrawRect(rect, Background);
            GUI.BeginClip(rect);
            for (int i = 0; i < chart.Lanes.Count; i++)
            {
                RhythmLaneData lane = chart.Lanes[i];
                if (lane == null)
                {
                    continue;
                }

                Rect row = new Rect(0f, i * RhythmTimelineGeometry.LaneHeight - verticalScroll, rect.width, RhythmTimelineGeometry.LaneHeight);
                if (row.yMax < 0f || row.yMin > rect.height)
                {
                    continue;
                }

                EditorGUI.DrawRect(row, i % 2 == 0 ? LaneEven : LaneOdd);
                EditorGUI.DrawRect(new Rect(row.x, row.y, 5f, row.height), lane.Color);
                GUI.Label(new Rect(row.x + 11f, row.y, row.width - 14f, row.height), $"{lane.DisplayName}  [{lane.KeyIdentity}]", laneLabel);
                EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1f, row.width, 1f), new Color(0f, 0f, 0f, 0.35f));
            }
            GUI.EndClip();
        }

        private void DrawRuler(RhythmChart chart, Rect rect, float pixelsPerSecond, double scrollTime, double playhead)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.17f, 0.19f));
            GUI.BeginClip(rect);
            double secondsPerBeat = chart.SecondsPerBeat;
            long firstBeat = (long)Math.Floor(scrollTime / secondsPerBeat);
            double visibleEnd = scrollTime + rect.width / pixelsPerSecond;
            long lastBeat = (long)Math.Ceiling(visibleEnd / secondsPerBeat);

            for (long beat = firstBeat; beat <= lastBeat; beat++)
            {
                float x = (float)((beat * secondsPerBeat - scrollTime) * pixelsPerSecond);
                bool measure = RhythmTimingUtility.IsMeasureBeat(beat, chart.BeatsPerMeasure);
                EditorGUI.DrawRect(new Rect(x, measure ? 0f : rect.height * 0.45f, measure ? 2f : 1f, rect.height), measure ? MeasureLine : BeatLine);
                if (measure || secondsPerBeat * pixelsPerSecond >= 38f)
                {
                    long measureNumber = beat / Math.Max(1, chart.BeatsPerMeasure) + 1;
                    int beatInMeasure = (int)(beat % Math.Max(1, chart.BeatsPerMeasure)) + 1;
                    GUI.Label(new Rect(x + 4f, 2f, 90f, rect.height - 2f), measure ? $"M{measureNumber}" : $"{measureNumber}:{beatInMeasure}", rulerLabel);
                }
            }

            float playheadX = (float)((playhead - scrollTime) * pixelsPerSecond);
            EditorGUI.DrawRect(new Rect(playheadX - 1f, 0f, 2f, rect.height), PlayheadColor);
            GUI.EndClip();
        }

        private void DrawCanvas(RhythmChart chart, Rect rect, float pixelsPerSecond, double scrollTime, float verticalScroll, double playhead, string selectedNoteId)
        {
            EditorGUI.DrawRect(rect, Background);
            GUI.BeginClip(rect);

            float contentWidth = rect.width;
            for (int i = 0; i < chart.Lanes.Count; i++)
            {
                float y = i * RhythmTimelineGeometry.LaneHeight - verticalScroll;
                Rect row = new Rect(0f, y, contentWidth, RhythmTimelineGeometry.LaneHeight);
                if (row.yMax < 0f || row.yMin > rect.height)
                {
                    continue;
                }

                EditorGUI.DrawRect(row, i % 2 == 0 ? LaneEven : LaneOdd);
                EditorGUI.DrawRect(new Rect(0f, row.yMax - 1f, contentWidth, 1f), new Color(0f, 0f, 0f, 0.4f));
            }

            DrawGrid(chart, rect, pixelsPerSecond, scrollTime);
            DrawNotes(chart, rect, pixelsPerSecond, scrollTime, verticalScroll, selectedNoteId);

            float playheadX = (float)((playhead - scrollTime) * pixelsPerSecond);
            EditorGUI.DrawRect(new Rect(playheadX - 1f, 0f, 2f, rect.height), PlayheadColor);
            GUI.EndClip();

            AddNoteCursors(chart, rect, pixelsPerSecond, scrollTime, verticalScroll);
        }

        private static void DrawGrid(RhythmChart chart, Rect rect, float pixelsPerSecond, double scrollTime)
        {
            double beatInterval = chart.SecondsPerBeat;
            double subdivision = RhythmTimingUtility.GetSnapInterval(chart.Bpm, chart.SnapDivision);
            if (subdivision * pixelsPerSecond < 5d)
            {
                subdivision = beatInterval;
            }

            double end = scrollTime + rect.width / pixelsPerSecond;
            long first = (long)Math.Floor(scrollTime / subdivision);
            long last = (long)Math.Ceiling(end / subdivision);
            for (long index = first; index <= last && index - first < 2500; index++)
            {
                double time = index * subdivision;
                float x = (float)((time - scrollTime) * pixelsPerSecond);
                long nearestBeat = (long)Math.Round(time / beatInterval);
                bool isBeat = Math.Abs(time - nearestBeat * beatInterval) < 0.000001d;
                bool isMeasure = isBeat && RhythmTimingUtility.IsMeasureBeat(nearestBeat, chart.BeatsPerMeasure);
                Color color = isMeasure ? MeasureLine : isBeat ? BeatLine : SubdivisionLine;
                EditorGUI.DrawRect(new Rect(x, 0f, isMeasure ? 2f : 1f, rect.height), color);
            }
        }

        private void DrawNotes(RhythmChart chart, Rect rect, float pixelsPerSecond, double scrollTime, float verticalScroll, string selectedNoteId)
        {
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                RhythmNoteData note = chart.Notes[i];
                int laneIndex = GetLaneIndex(chart, note?.LaneId);
                if (note == null || laneIndex < 0)
                {
                    continue;
                }

                Rect globalRect = RhythmTimelineGeometry.GetNoteRect(note, laneIndex, rect, pixelsPerSecond, scrollTime, verticalScroll);
                Rect noteRect = new Rect(globalRect.x - rect.x, globalRect.y - rect.y, globalRect.width, globalRect.height);
                if (noteRect.xMax < 0f || noteRect.xMin > rect.width || noteRect.yMax < 0f || noteRect.yMin > rect.height)
                {
                    continue;
                }

                Color color = chart.FindDefinition(note.NoteType)?.DisplayColor ?? chart.Lanes[laneIndex].Color;
                color.a = 0.92f;
                EditorGUI.DrawRect(noteRect, color);
                EditorGUI.DrawRect(new Rect(noteRect.x, noteRect.y, noteRect.width, 2f), Color.Lerp(color, Color.white, 0.4f));
                GUI.Label(noteRect, GetNoteLabel(note), noteLabel);

                if (note.Id == selectedNoteId)
                {
                    DrawBorder(noteRect, SelectionColor, 2f);
                }

                if (note.IsHold)
                {
                    EditorGUI.DrawRect(new Rect(noteRect.xMin, noteRect.y, RhythmTimelineGeometry.ResizeHandleWidth, noteRect.height), new Color(1f, 1f, 1f, 0.28f));
                    EditorGUI.DrawRect(new Rect(noteRect.xMax - RhythmTimelineGeometry.ResizeHandleWidth, noteRect.y, RhythmTimelineGeometry.ResizeHandleWidth, noteRect.height), new Color(1f, 1f, 1f, 0.28f));
                }
            }
        }

        private static string GetNoteLabel(RhythmNoteData note)
        {
            switch (note.NoteType)
            {
                case RhythmNoteType.HoldLaser: return "H LASER";
                default: return note.NoteType.ToString().ToUpperInvariant();
            }
        }

        private static void DrawBorder(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private static void AddNoteCursors(RhythmChart chart, Rect viewport, float pixelsPerSecond, double scrollTime, float verticalScroll)
        {
            foreach (RhythmNoteData note in chart.Notes)
            {
                int laneIndex = GetLaneIndex(chart, note?.LaneId);
                if (note == null || laneIndex < 0)
                {
                    continue;
                }

                Rect noteRect = RhythmTimelineGeometry.GetNoteRect(note, laneIndex, viewport, pixelsPerSecond, scrollTime, verticalScroll);
                Rect visibleBody = Intersect(noteRect, viewport);
                if (visibleBody.width > 0f && visibleBody.height > 0f)
                {
                    EditorGUIUtility.AddCursorRect(visibleBody, MouseCursor.MoveArrow);
                }

                if (note.IsHold)
                {
                    Rect leading = Intersect(RhythmTimelineGeometry.GetLeadingHandle(noteRect), viewport);
                    Rect trailing = Intersect(RhythmTimelineGeometry.GetTrailingHandle(noteRect), viewport);
                    if (leading.width > 0f) EditorGUIUtility.AddCursorRect(leading, MouseCursor.ResizeHorizontal);
                    if (trailing.width > 0f) EditorGUIUtility.AddCursorRect(trailing, MouseCursor.ResizeHorizontal);
                }
            }
        }

        public static int GetLaneIndex(RhythmChart chart, string laneId)
        {
            if (chart == null || string.IsNullOrEmpty(laneId))
            {
                return -1;
            }

            for (int i = 0; i < chart.Lanes.Count; i++)
            {
                if (chart.Lanes[i] != null && chart.Lanes[i].Id == laneId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : Rect.zero;
        }
    }
}
