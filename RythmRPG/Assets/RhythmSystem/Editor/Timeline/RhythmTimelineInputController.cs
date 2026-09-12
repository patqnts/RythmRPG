using System;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public sealed class RhythmTimelineInputController
    {
        private enum DragMode
        {
            None,
            Move,
            ResizeLeading,
            ResizeTrailing,
            Scrub,
            Pan
        }

        private DragMode dragMode;
        private int hotControl;
        private int undoGroup = -1;
        private Vector2 dragMouseStart;
        private double dragTimeStart;
        private double originalHitTime;
        private double originalEndTime;
        private double originalScrollTime;
        private float originalVerticalScroll;
        private RhythmNoteData draggedNote;

        public float PixelsPerSecond { get; set; } = 100f;
        public double ScrollTime { get; set; }
        public float VerticalScroll { get; set; }
        public string SelectedNoteId { get; set; }
        public Action<double> SeekRequested { get; set; }

        public bool Handle(Event current, RhythmChart chart, RhythmTimelineLayout layout, RhythmNoteType drawingType)
        {
            if (chart == null)
            {
                return false;
            }

            int controlId = GUIUtility.GetControlID("RhythmTimeline".GetHashCode(), FocusType.Passive);
            bool inTimeline = layout.RulerRect.Contains(current.mousePosition) || layout.LanesRect.Contains(current.mousePosition);

            if (current.type == EventType.ScrollWheel && inTimeline)
            {
                if (current.control || current.command)
                {
                    ZoomAt(current.mousePosition.x, current.delta.y, chart, layout);
                }
                else
                {
                    ScrollTime += current.delta.y * Math.Max(0.02d, 36d / PixelsPerSecond);
                }

                ClampScroll(chart, layout);
                current.Use();
                return true;
            }

            if (current.type == EventType.MouseDown && current.button == 2 && inTimeline)
            {
                dragMode = DragMode.Pan;
                hotControl = controlId;
                GUIUtility.hotControl = controlId;
                dragMouseStart = current.mousePosition;
                originalScrollTime = ScrollTime;
                originalVerticalScroll = VerticalScroll;
                current.Use();
                return true;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && layout.RulerRect.Contains(current.mousePosition))
            {
                dragMode = DragMode.Scrub;
                hotControl = controlId;
                GUIUtility.hotControl = controlId;
                Seek(current.mousePosition.x, chart, layout);
                current.Use();
                return true;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && layout.LanesRect.Contains(current.mousePosition))
            {
                if (TryHitNote(chart, layout, current.mousePosition, out RhythmNoteData note, out DragMode hitMode))
                {
                    SelectedNoteId = note.Id;
                    BeginNoteDrag(controlId, current, chart, note, hitMode, layout);
                }
                else if (chart.Lanes.Count > 0)
                {
                    int laneIndex = RhythmTimelineGeometry.YToLane(current.mousePosition.y, layout.LanesRect, VerticalScroll, chart.Lanes.Count);
                    double time = RhythmComposerMutationService.Snap(chart, RhythmTimelineGeometry.XToTime(current.mousePosition.x, layout.LanesRect, PixelsPerSecond, ScrollTime));
                    RhythmNoteData created = RhythmComposerMutationService.AddNote(chart, chart.Lanes[laneIndex], time, drawingType);
                    SelectedNoteId = created?.Id;
                }

                current.Use();
                return true;
            }

            if (current.type == EventType.MouseDrag && GUIUtility.hotControl == hotControl && dragMode != DragMode.None)
            {
                switch (dragMode)
                {
                    case DragMode.Pan:
                        ScrollTime = originalScrollTime - (current.mousePosition.x - dragMouseStart.x) / PixelsPerSecond;
                        VerticalScroll = originalVerticalScroll - (current.mousePosition.y - dragMouseStart.y);
                        break;
                    case DragMode.Scrub:
                        Seek(current.mousePosition.x, chart, layout);
                        break;
                    default:
                        AutoScroll(current.mousePosition, chart, layout);
                        UpdateNoteDrag(current.mousePosition, chart, layout);
                        break;
                }

                ClampScroll(chart, layout);
                current.Use();
                return true;
            }

            if (current.type == EventType.MouseUp && GUIUtility.hotControl == hotControl && dragMode != DragMode.None)
            {
                EndDrag();
                current.Use();
                return true;
            }

            return false;
        }

        public void ZoomBy(float factor, RhythmChart chart, RhythmTimelineLayout layout)
        {
            float anchor = layout.LanesRect.center.x;
            double anchorTime = RhythmTimelineGeometry.XToTime(anchor, layout.LanesRect, PixelsPerSecond, ScrollTime);
            PixelsPerSecond = Mathf.Clamp(PixelsPerSecond * factor, 20f, 800f);
            ScrollTime = anchorTime - (anchor - layout.LanesRect.x) / PixelsPerSecond;
            ClampScroll(chart, layout);
        }

        public void ClampScroll(RhythmChart chart, RhythmTimelineLayout layout)
        {
            double visibleDuration = layout.LanesRect.width / Math.Max(1f, PixelsPerSecond);
            ScrollTime = Math.Max(0d, Math.Min(ScrollTime, Math.Max(0d, chart.EffectiveDuration - visibleDuration)));
            float contentHeight = chart.Lanes.Count * RhythmTimelineGeometry.LaneHeight;
            VerticalScroll = Mathf.Clamp(VerticalScroll, 0f, Mathf.Max(0f, contentHeight - layout.LanesRect.height));
        }

        private void ZoomAt(float mouseX, float wheelDelta, RhythmChart chart, RhythmTimelineLayout layout)
        {
            double anchorTime = RhythmTimelineGeometry.XToTime(mouseX, layout.LanesRect, PixelsPerSecond, ScrollTime);
            float factor = Mathf.Pow(1.12f, -wheelDelta);
            PixelsPerSecond = Mathf.Clamp(PixelsPerSecond * factor, 20f, 800f);
            ScrollTime = anchorTime - (mouseX - layout.LanesRect.x) / PixelsPerSecond;
            ClampScroll(chart, layout);
        }

        private void BeginNoteDrag(int controlId, Event current, RhythmChart chart, RhythmNoteData note, DragMode mode, RhythmTimelineLayout layout)
        {
            dragMode = mode;
            hotControl = controlId;
            GUIUtility.hotControl = controlId;
            draggedNote = note;
            dragMouseStart = current.mousePosition;
            dragTimeStart = RhythmTimelineGeometry.XToTime(current.mousePosition.x, layout.LanesRect, PixelsPerSecond, ScrollTime);
            originalHitTime = note.HitTime;
            originalEndTime = note.EndTime;
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(mode == DragMode.Move ? "Move Rhythm Note" : "Resize Rhythm Note");
            Undo.RecordObject(chart, Undo.GetCurrentGroupName());
        }

        private void UpdateNoteDrag(Vector2 mousePosition, RhythmChart chart, RhythmTimelineLayout layout)
        {
            if (draggedNote == null)
            {
                return;
            }

            double currentTime = RhythmTimelineGeometry.XToTime(mousePosition.x, layout.LanesRect, PixelsPerSecond, ScrollTime);
            double minimum = RhythmComposerMutationService.MinimumDuration(chart);
            switch (dragMode)
            {
                case DragMode.Move:
                    draggedNote.HitTime = RhythmComposerMutationService.Snap(chart, originalHitTime + currentTime - dragTimeStart);
                    int laneIndex = RhythmTimelineGeometry.YToLane(mousePosition.y, layout.LanesRect, VerticalScroll, chart.Lanes.Count);
                    draggedNote.LaneId = chart.Lanes[laneIndex].Id;
                    break;
                case DragMode.ResizeLeading:
                    double leading = Math.Min(RhythmComposerMutationService.Snap(chart, currentTime), originalEndTime - minimum);
                    draggedNote.HitTime = Math.Max(0d, leading);
                    draggedNote.HoldDuration = originalEndTime - draggedNote.HitTime;
                    break;
                case DragMode.ResizeTrailing:
                    double trailing = Math.Max(RhythmComposerMutationService.Snap(chart, currentTime), originalHitTime + minimum);
                    draggedNote.HoldDuration = trailing - originalHitTime;
                    break;
            }

            RhythmComposerMutationService.ExtendDuration(chart, draggedNote.EndTime);
            EditorUtility.SetDirty(chart);
        }

        private bool TryHitNote(RhythmChart chart, RhythmTimelineLayout layout, Vector2 mousePosition, out RhythmNoteData hitNote, out DragMode hitMode)
        {
            for (int i = chart.Notes.Count - 1; i >= 0; i--)
            {
                RhythmNoteData note = chart.Notes[i];
                int laneIndex = RhythmTimelineRenderer.GetLaneIndex(chart, note?.LaneId);
                if (note == null || laneIndex < 0)
                {
                    continue;
                }

                Rect noteRect = RhythmTimelineGeometry.GetNoteRect(note, laneIndex, layout.LanesRect, PixelsPerSecond, ScrollTime, VerticalScroll);
                if (note.IsHold && RhythmTimelineGeometry.GetLeadingHandle(noteRect).Contains(mousePosition))
                {
                    hitNote = note;
                    hitMode = DragMode.ResizeLeading;
                    return true;
                }

                if (note.IsHold && RhythmTimelineGeometry.GetTrailingHandle(noteRect).Contains(mousePosition))
                {
                    hitNote = note;
                    hitMode = DragMode.ResizeTrailing;
                    return true;
                }

                if (noteRect.Contains(mousePosition))
                {
                    hitNote = note;
                    hitMode = DragMode.Move;
                    return true;
                }
            }

            hitNote = null;
            hitMode = DragMode.None;
            return false;
        }

        private void Seek(float mouseX, RhythmChart chart, RhythmTimelineLayout layout)
        {
            double time = RhythmTimelineGeometry.XToTime(mouseX, layout.LanesRect, PixelsPerSecond, ScrollTime);
            SeekRequested?.Invoke(Math.Max(0d, Math.Min(time, chart.EffectiveDuration)));
        }

        private void AutoScroll(Vector2 mousePosition, RhythmChart chart, RhythmTimelineLayout layout)
        {
            const float edge = 28f;
            if (mousePosition.x < layout.LanesRect.x + edge)
            {
                ScrollTime -= Math.Max(0.02d, 12d / PixelsPerSecond);
            }
            else if (mousePosition.x > layout.LanesRect.xMax - edge)
            {
                ScrollTime += Math.Max(0.02d, 12d / PixelsPerSecond);
            }

            if (mousePosition.y < layout.LanesRect.y + edge)
            {
                VerticalScroll -= 8f;
            }
            else if (mousePosition.y > layout.LanesRect.yMax - edge)
            {
                VerticalScroll += 8f;
            }

            ClampScroll(chart, layout);
        }

        private void EndDrag()
        {
            if (undoGroup >= 0)
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            GUIUtility.hotControl = 0;
            dragMode = DragMode.None;
            hotControl = 0;
            undoGroup = -1;
            draggedNote = null;
        }
    }
}
