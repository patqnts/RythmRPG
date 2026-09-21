using System;

namespace RythmRPG.Rhythm.Editing
{
    public enum TimelineHitKind
    {
        None,
        Head,
        HoldEnd,
        Body,
        Handle
    }

    public enum PatternHitKind
    {
        None,
        Body,
        RightEdge
    }

    public struct PatternHit
    {
        public PatternHitKind Kind;
        public string PatternId;
    }

    public struct TimelineHit
    {
        public TimelineHitKind Kind;
        public string NoteId;
        public NoteHandleKind Handle;
    }

    /// <summary>Pure coordinate math for the composer timeline (pixels <-> beats/lanes) and note hit testing.</summary>
    public sealed class TimelineGeometry
    {
        public const double MinPixelsPerBeat = 12d;
        public const double MaxPixelsPerBeat = 600d;

        public float Gutter = 84f;
        public float RulerHeight = 26f;
        public float LaneHeight = 46f;
        public float HeadRadius = 8f;
        public float HandleWidth = 6f;
        public double PixelsPerBeat = 80d;
        public double ScrollBeat;
        public int LaneCount = 5;
        public float PatternTrackHeight = 34f;

        public float BeatToX(double beat)
        {
            return Gutter + (float)((beat - ScrollBeat) * PixelsPerBeat);
        }

        public double XToBeat(float x)
        {
            return ScrollBeat + (x - Gutter) / PixelsPerBeat;
        }

        public float LaneTop(int lane)
        {
            return RulerHeight + lane * LaneHeight;
        }

        /// <summary>The pattern track sits directly under the last lane.</summary>
        public float PatternTrackTop { get { return LaneTop(LaneCount) + 6f; } }
        public float PatternTrackBottom { get { return PatternTrackTop + PatternTrackHeight; } }

        public bool InPatternTrack(float y)
        {
            return y >= PatternTrackTop && y <= PatternTrackBottom;
        }

        /// <summary>Pattern block under a pixel position (right edge = resize handle), or None.</summary>
        public PatternHit HitTestPattern(ChartSession session, float x, float y)
        {
            var none = new PatternHit { Kind = PatternHitKind.None, PatternId = null };
            if (!InPatternTrack(y) || x < Gutter) return none;
            System.Collections.Generic.IList<PatternInstance> patterns = session.Patterns;
            for (int i = patterns.Count - 1; i >= 0; i--)
            {
                PatternInstance p = patterns[i];
                float x0 = BeatToX(p.StartBeat);
                float x1 = BeatToX(p.EndBeat);
                if (x < x0 - 1f || x > x1 + HandleWidth * 0.5f) continue;
                if (Math.Abs(x - x1) <= HandleWidth) return new PatternHit { Kind = PatternHitKind.RightEdge, PatternId = p.Id };
                if (x >= x0 && x <= x1) return new PatternHit { Kind = PatternHitKind.Body, PatternId = p.Id };
            }

            return none;
        }

        public float LaneCenter(int lane)
        {
            return LaneTop(lane) + LaneHeight * 0.5f;
        }

        /// <summary>Lane under a y pixel, clamped to a valid lane; -1 when clampToRange is false and y is outside the lanes.</summary>
        public int YToLane(float y, bool clampToRange)
        {
            int lane = (int)Math.Floor((y - RulerHeight) / LaneHeight);
            if (lane >= 0 && lane < LaneCount) return lane;
            if (!clampToRange) return -1;
            return Math.Max(0, Math.Min(LaneCount - 1, lane));
        }

        public void ZoomAbout(double factor, float cursorX)
        {
            double beatUnderCursor = XToBeat(cursorX);
            PixelsPerBeat = Math.Max(MinPixelsPerBeat, Math.Min(MaxPixelsPerBeat, PixelsPerBeat * factor));
            ScrollBeat = Math.Max(0d, beatUnderCursor - (cursorX - Gutter) / PixelsPerBeat);
        }

        public void ScrollByPixels(float pixels)
        {
            ScrollBeat = Math.Max(0d, ScrollBeat + pixels / PixelsPerBeat);
        }

        public void VisibleBeats(float width, out double first, out double last)
        {
            first = ScrollBeat;
            last = XToBeat(width);
        }

        /// <summary>Topmost note under a pixel position, or Kind None.</summary>
        public TimelineHit HitTest(ChartSession session, float x, float y)
        {
            return HitTest(session, x, y, null);
        }

        /// <summary>
        /// As above, but first tests the lifecycle handles that <paramref name="handleProvider"/> reports for selected notes
        /// (travel lead-in, stationary windows); these win over note heads so they stay reachable.
        /// </summary>
        public TimelineHit HitTest(ChartSession session, float x, float y, Func<NoteInstance, System.Collections.Generic.List<NoteHandlePoint>> handleProvider)
        {
            var none = new TimelineHit { Kind = TimelineHitKind.None, NoteId = null };
            int lane = YToLane(y, false);
            if (lane < 0) return none;
            System.Collections.Generic.IList<NoteInstance> notes = session.Notes;
            if (handleProvider != null)
            {
                for (int i = notes.Count - 1; i >= 0; i--)
                {
                    NoteInstance n = notes[i];
                    if (!session.IsSelected(n.Id) || session.LaneIndex(n.LaneId) != lane) continue;
                    System.Collections.Generic.List<NoteHandlePoint> points = handleProvider(n);
                    if (points == null) continue;
                    for (int h = 0; h < points.Count; h++)
                    {
                        if (x >= Gutter && Math.Abs(x - BeatToX(points[h].Beat)) <= HandleWidth)
                        {
                            return new TimelineHit { Kind = TimelineHitKind.Handle, NoteId = n.Id, Handle = points[h].Kind };
                        }
                    }
                }
            }

            for (int i = notes.Count - 1; i >= 0; i--)
            {
                NoteInstance n = notes[i];
                if (session.LaneIndex(n.LaneId) != lane) continue;
                float hx = BeatToX(n.HitBeat);
                if (Math.Abs(x - hx) <= HeadRadius)
                {
                    return new TimelineHit { Kind = TimelineHitKind.Head, NoteId = n.Id };
                }

                if (n.HoldBeats > 0d)
                {
                    float ex = BeatToX(n.HitBeat + n.HoldBeats);
                    if (Math.Abs(x - ex) <= HandleWidth)
                    {
                        return new TimelineHit { Kind = TimelineHitKind.HoldEnd, NoteId = n.Id };
                    }

                    if (x > hx && x < ex)
                    {
                        return new TimelineHit { Kind = TimelineHitKind.Body, NoteId = n.Id };
                    }
                }
            }

            return none;
        }
    }
}
