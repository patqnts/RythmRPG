using System;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public static class RhythmTimelineGeometry
    {
        public const float LaneHeight = 42f;
        public const float RulerHeight = 28f;
        public const float NoteInset = 7f;
        public const float PointNoteWidth = 14f;
        public const float ResizeHandleWidth = 6f;

        public static float TimeToX(double time, Rect canvasRect, float pixelsPerSecond, double scrollTime)
        {
            return canvasRect.x + (float)((time - scrollTime) * pixelsPerSecond);
        }

        public static double XToTime(float x, Rect canvasRect, float pixelsPerSecond, double scrollTime)
        {
            return scrollTime + (x - canvasRect.x) / Math.Max(1f, pixelsPerSecond);
        }

        public static float LaneToY(int laneIndex, Rect lanesRect, float verticalScroll)
        {
            return lanesRect.y + laneIndex * LaneHeight - verticalScroll;
        }

        public static int YToLane(float y, Rect lanesRect, float verticalScroll, int laneCount)
        {
            int index = Mathf.FloorToInt((y - lanesRect.y + verticalScroll) / LaneHeight);
            return Mathf.Clamp(index, 0, Math.Max(0, laneCount - 1));
        }

        public static Rect GetNoteRect(RhythmNoteData note, int laneIndex, Rect lanesRect, float pixelsPerSecond, double scrollTime, float verticalScroll)
        {
            float x = TimeToX(note.HitTime, lanesRect, pixelsPerSecond, scrollTime);
            float width = note.IsHold
                ? Mathf.Max(PointNoteWidth, (float)(Math.Max(0d, note.HoldDuration) * pixelsPerSecond))
                : PointNoteWidth;
            float y = LaneToY(laneIndex, lanesRect, verticalScroll) + NoteInset;
            return new Rect(x - (note.IsHold ? 0f : PointNoteWidth * 0.5f), y, width, LaneHeight - NoteInset * 2f);
        }

        public static Rect GetLeadingHandle(Rect noteRect)
        {
            return new Rect(noteRect.xMin - ResizeHandleWidth * 0.5f, noteRect.y, ResizeHandleWidth, noteRect.height);
        }

        public static Rect GetTrailingHandle(Rect noteRect)
        {
            return new Rect(noteRect.xMax - ResizeHandleWidth * 0.5f, noteRect.y, ResizeHandleWidth, noteRect.height);
        }
    }
}
