using System;
using System.Collections.Generic;
using RythmRPG.Rhythm.Audio;
using RythmRPG.Rhythm.Editing;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.Rhythm.Editor.Composer
{
    /// <summary>Custom-painted DAW-style timeline: ruler, grid, lanes, notes, selection, playhead. Owns pointer/keyboard interaction.</summary>
    public sealed class TimelineElement : VisualElement
    {
        private enum DragMode { None, Move, ResizeHold, Handle, Marquee, Pan, MovePattern, ResizePattern }

        private static readonly Color BackgroundColor = new Color(0.16f, 0.16f, 0.17f);
        private static readonly Color LaneAlt = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color GridBeat = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color GridSub = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color GridMeasure = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color RulerColor = new Color(0.12f, 0.12f, 0.13f);
        private static readonly Color SelectColor = new Color(1f, 0.92f, 0.3f);
        private static readonly Color PlayheadColor = new Color(1f, 0.3f, 0.3f);
        private static readonly Color WaveColor = new Color(0.35f, 0.62f, 0.95f, 0.38f);
        private static readonly Color PatternColor = new Color(0.45f, 0.65f, 1f, 0.55f);
        private static readonly Color ReservedColor = new Color(0.95f, 0.55f, 0.2f, 0.11f);
        private static readonly Color GhostColor = new Color(0.6f, 0.8f, 1f, 0.55f);

        private readonly TimelineGeometry geo = new TimelineGeometry();
        private readonly VisualElement overlay;
        private ChartSession session;
        private Dictionary<string, NoteCatalogEntry> catalog = new Dictionary<string, NoteCatalogEntry>();

        private DragMode drag;
        private string dragAnchorId;
        private double dragGrabOffsetBeats;
        private int dragAnchorLane;
        private double previewBeats;
        private int previewLanes;
        private double previewHold;
        private NoteHandleKind dragHandle;
        private double previewHandleBeat;
        // Travel handle dragged on one of several selected notes: every selected note gets the same travel length.
        private bool dragTravelGroup;
        private Vector2 marqueeStart;
        private Vector2 marqueeEnd;
        private Vector2 panLast;
        private Vector2 lastPointerLocal;
        private string patternDragId;
        private double patternPreviewStart;
        private double patternPreviewDuration;

        public double PlayheadBeat { get; set; }

        /// <summary>First visible beat (left edge of the lanes). Setting it scrolls the view.</summary>
        public double ScrollBeat
        {
            get { return geo.ScrollBeat; }
            set
            {
                double v = Math.Max(0d, value);
                if (Math.Abs(v - geo.ScrollBeat) < 1e-9) return;
                geo.ScrollBeat = v;
                Refresh();
            }
        }

        /// <summary>Number of beats that fit in the visible lane area at the current zoom.</summary>
        public double VisibleBeatSpan
        {
            get { return Math.Max(0.01d, (Math.Max(1f, contentRect.width) - geo.Gutter) / geo.PixelsPerBeat); }
        }

        /// <summary>Raised (after the repaint) when scroll or zoom changed, e.g. to update a scrollbar.</summary>
        public event Action ViewChanged;
        private double lastReportedScroll = -1d;
        private double lastReportedZoom = -1d;
        private float lastReportedWidth = -1f;
        public string ArmedDefinitionId { get; set; }

        /// <summary>Optional waveform drawn behind the lanes, positioned through the session tempo (beat grid + audio offset).</summary>
        public WaveformPeaks Waveform { get; set; }

        /// <summary>Worst validation severity per note id; flagged notes get a coloured outline.</summary>
        public Dictionary<string, IssueSeverity> Issues { get; set; }

        /// <summary>Raised when the user clicks the ruler; the value is the new playhead beat.</summary>
        public event Action<double> PlayheadScrubbed;

        /// <summary>Raised when the user presses Space.</summary>
        public event Action TogglePlayRequested;
        public event Action<string> ArmedPlaced;

        public TimelineElement()
        {
            focusable = true;
            style.flexGrow = 1f;
            overlay = new VisualElement();
            overlay.pickingMode = PickingMode.Ignore;
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            Add(overlay);

            generateVisualContent += OnGenerate;
            RegisterCallback<GeometryChangedEvent>(e => Refresh());
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        public event Action SaveRequested;

        public void Bind(ChartSession newSession, List<NoteCatalogEntry> entries)
        {
            if (session != null) session.Changed -= Refresh;
            session = newSession;
            catalog = new Dictionary<string, NoteCatalogEntry>();
            for (int i = 0; i < entries.Count; i++) catalog[entries[i].Id] = entries[i];
            if (session != null)
            {
                session.Changed += Refresh;
                geo.LaneCount = Math.Max(1, session.LaneIds.Count);
            }

            Refresh();
        }

        /// <summary>Scrolls (only if needed) so the given beat is visible.</summary>
        public void RevealBeat(double beat)
        {
            float w = Math.Max(1f, contentRect.width);
            float x = geo.BeatToX(beat);
            if (x >= geo.Gutter + 20f && x <= w - 40f) return;
            geo.ScrollBeat = Math.Max(0d, beat - 1d);
            Refresh();
        }

        /// <summary>Scrolls so the playhead stays visible during playback.</summary>
        public void FollowPlayhead()
        {
            float w = Math.Max(1f, contentRect.width);
            float x = geo.BeatToX(PlayheadBeat);
            if (x >= geo.Gutter && x <= w - 40f) return;
            geo.ScrollBeat = Math.Max(0d, PlayheadBeat - 1d);
            Refresh();
        }

        public void Refresh()
        {
            RebuildLabels();
            MarkDirtyRepaint();
        }

        private NoteHandleDefaults DefaultsOf(NoteInstance n)
        {
            NoteCatalogEntry entry;
            return catalog.TryGetValue(n.DefinitionId, out entry) && entry.HandleDefaults != null ? entry.HandleDefaults : new NoteHandleDefaults();
        }

        private NoteBehaviorKind BehaviorOf(NoteInstance n)
        {
            NoteCatalogEntry entry;
            return catalog.TryGetValue(n.DefinitionId, out entry) ? entry.Behavior : NoteBehaviorKind.Moving;
        }

        // Travel length (beats) the dragged handle gives its note; applied to every selected note in a group drag.
        private double GroupTravelBeats()
        {
            NoteInstance anchor = dragAnchorId != null ? session.Find(dragAnchorId) : null;
            return anchor == null ? NoteHandles.MinTravelBeats
                : Math.Max(NoteHandles.MinTravelBeats, anchor.HitBeat - previewHandleBeat);
        }

        private List<NoteHandlePoint> HandlePointsOf(NoteInstance n)
        {
            return NoteHandles.Points(n, BehaviorOf(n), DefaultsOf(n), session.Tempo, false);
        }

        /// <summary>Places a note at a world (panel) position, e.g. from a library drag.</summary>
        public void PlaceAtWorld(string definitionId, Vector2 worldPosition)
        {
            if (session == null) return;
            Vector2 local = this.WorldToLocal(worldPosition);
            PlaceAtLocal(definitionId, local);
        }

        private void PlaceAtLocal(string definitionId, Vector2 local)
        {
            if (local.x < geo.Gutter) return;
            if (definitionId != null && definitionId.StartsWith(PatternLibrary.IdPrefix))
            {
                session.AddPattern(definitionId.Substring(PatternLibrary.IdPrefix.Length), geo.XToBeat(local.x));
                return;
            }

            int lane = geo.YToLane(local.y, false);
            if (lane < 0) return;
            double hold = 0d;
            NoteCatalogEntry entry;
            if (catalog.TryGetValue(definitionId, out entry) && entry.IsHold) hold = entry.DefaultHoldBeats;
            session.AddNote(session.LaneIds[lane], geo.XToBeat(local.x), definitionId, hold);
        }

        // ---------------- painting ----------------

        private void RebuildLabels()
        {
            overlay.Clear();
            if (session == null) return;
            for (int i = 0; i < session.LaneIds.Count; i++)
            {
                var l = new Label("Lane " + (i + 1));
                l.pickingMode = PickingMode.Ignore;
                l.style.position = Position.Absolute;
                l.style.left = 6f;
                l.style.top = geo.LaneTop(i) + geo.LaneHeight * 0.5f - 8f;
                l.style.color = new Color(0.8f, 0.8f, 0.8f);
                overlay.Add(l);
            }

            var trackLabel = new Label("Patterns");
            trackLabel.pickingMode = PickingMode.Ignore;
            trackLabel.style.position = Position.Absolute;
            trackLabel.style.left = 6f;
            trackLabel.style.top = geo.PatternTrackTop + geo.PatternTrackHeight * 0.5f - 8f;
            trackLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            overlay.Add(trackLabel);
            IList<PatternInstance> patternList = session.Patterns;
            for (int i = 0; i < patternList.Count; i++)
            {
                PatternInstance pat = patternList[i];
                float px0 = geo.BeatToX(pat.StartBeat), px1 = geo.BeatToX(pat.EndBeat);
                if (px1 < geo.Gutter) continue;
                float left = Mathf.Max(px0 + 4f, geo.Gutter + 2f);
                PatternTemplate template = PatternLibrary.Find(pat.TemplateId);
                var pl = new Label(template != null ? template.Name : pat.TemplateId);
                pl.pickingMode = PickingMode.Ignore;
                pl.style.position = Position.Absolute;
                pl.style.left = left;
                pl.style.top = geo.PatternTrackTop + 8f;
                pl.style.width = Mathf.Max(0f, px1 - left - 4f);
                pl.style.overflow = Overflow.Hidden;
                pl.style.whiteSpace = WhiteSpace.NoWrap;
                pl.style.color = Color.white;
                overlay.Add(pl);
            }

            double first, last;
            geo.VisibleBeats(Math.Max(1f, contentRect.width), out first, out last);
            int bpm = Math.Max(1, session.Tempo.BeatsPerMeasure);
            int startMeasure = Math.Max(0, (int)Math.Floor(first / bpm));
            int step = 1;
            while (bpm * step * geo.PixelsPerBeat < 60d) step *= 2;
            for (int m = (startMeasure / step) * step; m * bpm <= last; m += step)
            {
                float x = geo.BeatToX(m * bpm);
                if (x < geo.Gutter - 1f) continue;
                var l = new Label((m + 1).ToString());
                l.pickingMode = PickingMode.Ignore;
                l.style.position = Position.Absolute;
                l.style.left = x + 3f;
                l.style.top = 3f;
                l.style.color = new Color(0.75f, 0.75f, 0.75f);
                overlay.Add(l);
            }
        }

        private static void FillRect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            if (w <= 0f || h <= 0f) return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b, Color c, float width)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        private static void StrokeDiamond(Painter2D p, Vector2 c, float r, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y - r));
            p.LineTo(new Vector2(c.x + r, c.y));
            p.LineTo(new Vector2(c.x, c.y + r));
            p.LineTo(new Vector2(c.x - r, c.y));
            p.ClosePath();
            p.Stroke();
        }

        private static void FillDiamond(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y - r));
            p.LineTo(new Vector2(c.x + r, c.y));
            p.LineTo(new Vector2(c.x, c.y + r));
            p.LineTo(new Vector2(c.x - r, c.y));
            p.ClosePath();
            p.Fill();
        }

        private PatternInstance PreviewOf(PatternInstance pat)
        {
            if (patternDragId != pat.Id || (drag != DragMode.MovePattern && drag != DragMode.ResizePattern)) return pat;
            PatternInstance c = pat.Clone();
            if (drag == DragMode.MovePattern) c.StartBeat = patternPreviewStart;
            else c.DurationBeats = patternPreviewDuration;
            return c;
        }

        // Reserved lanes (exclusive patterns) are shaded and the notes a pattern will generate are shown as ghosts.
        private void DrawPatternOverlays(Painter2D p, float w)
        {
            IList<PatternInstance> patterns = session.Patterns;
            for (int i = 0; i < patterns.Count; i++)
            {
                PatternInstance pat = PreviewOf(patterns[i]);
                float x0 = Mathf.Max(geo.BeatToX(pat.StartBeat), geo.Gutter);
                float x1 = Mathf.Min(geo.BeatToX(pat.EndBeat), w);
                if (x1 <= x0) continue;
                if (pat.Reservation == ReservationMode.Exclusive)
                {
                    for (int lane = 0; lane < geo.LaneCount; lane++)
                    {
                        if (pat.UsesLane(lane)) FillRect(p, x0, geo.LaneTop(lane), x1 - x0, geo.LaneHeight, ReservedColor);
                    }
                }

                List<NoteInstance> ghosts = PatternExpander.Expand(pat, session.LaneIds);
                for (int g = 0; g < ghosts.Count; g++)
                {
                    float gx = geo.BeatToX(ghosts[g].HitBeat);
                    if (gx < geo.Gutter || gx > w) continue;
                    int lane = session.LaneIndex(ghosts[g].LaneId);
                    if (lane < 0) continue;
                    FillDiamond(p, new Vector2(gx, geo.LaneCenter(lane)), geo.HeadRadius * 0.7f, GhostColor);
                }
            }
        }

        private void DrawPatternTrack(Painter2D p, float w)
        {
            float top = geo.PatternTrackTop;
            FillRect(p, geo.Gutter, top, w - geo.Gutter, geo.PatternTrackHeight, new Color(1f, 1f, 1f, 0.04f));
            IList<PatternInstance> patterns = session.Patterns;
            for (int i = 0; i < patterns.Count; i++)
            {
                PatternInstance pat = PreviewOf(patterns[i]);
                float rx0 = geo.BeatToX(pat.StartBeat), rx1 = geo.BeatToX(pat.EndBeat);
                float x0 = Mathf.Max(rx0, geo.Gutter), x1 = Mathf.Min(rx1, w);
                if (x1 <= x0) continue;
                FillRect(p, x0, top + 2f, x1 - x0, geo.PatternTrackHeight - 4f, PatternColor);
                if (rx1 <= w && rx1 >= geo.Gutter) FillRect(p, rx1 - 3f, top + 2f, 3f, geo.PatternTrackHeight - 4f, new Color(1f, 1f, 1f, 0.6f));
                Color outline = Color.clear;
                IssueSeverity severity;
                if (Issues != null && Issues.TryGetValue(pat.Id, out severity) && severity != IssueSeverity.Info)
                    outline = severity == IssueSeverity.Error ? new Color(1f, 0.25f, 0.25f) : new Color(1f, 0.8f, 0.2f);
                if (session.IsSelected(pat.Id)) outline = SelectColor;
                if (outline.a > 0f)
                {
                    float y0 = top + 2f, y1 = top + geo.PatternTrackHeight - 2f;
                    Line(p, new Vector2(x0, y0), new Vector2(x1, y0), outline, 2f);
                    Line(p, new Vector2(x0, y1), new Vector2(x1, y1), outline, 2f);
                    Line(p, new Vector2(x0, y0), new Vector2(x0, y1), outline, 2f);
                    Line(p, new Vector2(x1, y0), new Vector2(x1, y1), outline, 2f);
                }
            }
        }

        // Lifecycle handles of a selected note: travel lead-in marker and (stationary) nested hit windows.
        private void DrawHandles(Painter2D p, NoteInstance n, double hitBeat, float cy, Color color, float w)
        {
            NoteHandleDefaults d = DefaultsOf(n);
            TempoMap tempo = session.Tempo;
            double shift = hitBeat - n.HitBeat;
            List<NoteHandlePoint> points = NoteHandles.Points(n, BehaviorOf(n), d, tempo, false);
            float hx = geo.BeatToX(hitBeat);
            float bandH = geo.LaneHeight - 10f;
            for (int i = points.Count - 1; i >= 0; i--)
            {
                NoteHandlePoint pt = points[i];
                float x = geo.BeatToX(pt.Beat + shift);
                if (pt.Kind == NoteHandleKind.Travel)
                {
                    if (x < geo.Gutter) continue;
                    FillRect(p, x - 4f, cy - 4f, 8f, 8f, new Color(color.r, color.g, color.b, 0.9f));
                    continue;
                }

                float alpha = pt.Kind == NoteHandleKind.WindowBad ? 0.10f : (pt.Kind == NoteHandleKind.WindowGood ? 0.14f : 0.20f);
                float xl = hx - (x - hx);
                FillRect(p, xl, cy - bandH * 0.5f, x - xl, bandH, new Color(color.r, color.g, color.b, alpha));
                Line(p, new Vector2(x, cy - bandH * 0.5f), new Vector2(x, cy + bandH * 0.5f), new Color(1f, 1f, 1f, 0.8f), 2f);
            }
        }

        private void DrawWaveform(Painter2D p, float w, float lanesBottom)
        {
            WaveformPeaks wave = Waveform;
            if (wave == null || wave.BucketCount == 0) return;
            const float step = 2f;
            float top = geo.RulerHeight;
            float mid = top + (lanesBottom - top) * 0.5f;
            float half = (lanesBottom - top) * 0.5f * 0.92f;
            float norm = wave.PeakAmplitude > 1e-5f ? 1f / wave.PeakAmplitude : 1f;
            p.strokeColor = WaveColor;
            p.lineWidth = step;
            p.BeginPath();
            bool any = false;
            for (float x = geo.Gutter; x < w; x += step)
            {
                double s0 = session.Tempo.BeatToSeconds(geo.XToBeat(x));
                double s1 = session.Tempo.BeatToSeconds(geo.XToBeat(x + step));
                float lo, hi;
                if (!wave.QueryRange(s0, s1, out lo, out hi)) continue;
                float yTop = mid - hi * norm * half;
                float yBottom = mid - lo * norm * half;
                if (yBottom - yTop < 1f) yBottom = yTop + 1f;
                p.MoveTo(new Vector2(x, yTop));
                p.LineTo(new Vector2(x, yBottom));
                any = true;
            }

            if (any) p.Stroke();
        }

        private void OnGenerate(MeshGenerationContext mgc)
        {
            if (geo.ScrollBeat != lastReportedScroll || geo.PixelsPerBeat != lastReportedZoom || contentRect.width != lastReportedWidth)
            {
                lastReportedScroll = geo.ScrollBeat;
                lastReportedZoom = geo.PixelsPerBeat;
                lastReportedWidth = contentRect.width;
                schedule.Execute(() => ViewChanged?.Invoke());
            }

            Painter2D p = mgc.painter2D;
            float w = contentRect.width;
            float h = contentRect.height;
            FillRect(p, 0f, 0f, w, h, BackgroundColor);
            if (session == null) return;

            int lanes = geo.LaneCount;
            float lanesBottom = geo.LaneTop(lanes);
            for (int i = 0; i < lanes; i++)
            {
                if (i % 2 == 1) FillRect(p, geo.Gutter, geo.LaneTop(i), w - geo.Gutter, geo.LaneHeight, LaneAlt);
            }

            DrawWaveform(p, w, lanesBottom);
            DrawPatternOverlays(p, w);

            double first, last;
            geo.VisibleBeats(w, out first, out last);
            int bpm = Math.Max(1, session.Tempo.BeatsPerMeasure);
            int div = Math.Max(1, session.SnapDivision);
            bool drawSub = geo.PixelsPerBeat / div >= 10d;
            double startBeat = Math.Floor(Math.Max(0d, first) * div) / div;
            for (double b = startBeat; b <= last; b += 1d / div)
            {
                float x = geo.BeatToX(b);
                if (x < geo.Gutter) continue;
                double whole = Math.Round(b);
                bool isBeat = Math.Abs(b - whole) < 1e-6;
                if (!isBeat && !drawSub) continue;
                bool isMeasure = isBeat && ((long)whole % bpm) == 0;
                Color c = isMeasure ? GridMeasure : (isBeat ? GridBeat : GridSub);
                Line(p, new Vector2(x, geo.RulerHeight), new Vector2(x, lanesBottom), c, 1f);
            }

            for (int i = 0; i <= lanes; i++)
            {
                float y = geo.LaneTop(i);
                Line(p, new Vector2(geo.Gutter, y), new Vector2(w, y), GridBeat, 1f);
            }

            // ruler
            FillRect(p, 0f, 0f, w, geo.RulerHeight, RulerColor);
            FillRect(p, 0f, 0f, geo.Gutter, h, RulerColor);
            for (double b = Math.Floor(Math.Max(0d, first)); b <= last; b += 1d)
            {
                float x = geo.BeatToX(b);
                if (x < geo.Gutter) continue;
                bool isMeasure = ((long)b % bpm) == 0;
                Line(p, new Vector2(x, geo.RulerHeight * (isMeasure ? 0.3f : 0.65f)), new Vector2(x, geo.RulerHeight), GridMeasure, 1f);
            }

            DrawPatternTrack(p, w);

            // notes
            IList<NoteInstance> notes = session.Notes;
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                bool selected = session.IsSelected(n.Id);
                if (drag == DragMode.Handle && n.Id == dragAnchorId)
                {
                    n = n.Clone();
                    NoteHandles.Apply(dragHandle, n, previewHandleBeat, DefaultsOf(n), session.Tempo);
                }
                else if (drag == DragMode.Handle && dragTravelGroup && selected)
                {
                    n = n.Clone();
                    n.TravelBeats = new Overridable<double>(GroupTravelBeats());
                }

                double beat = n.HitBeat;
                int lane = session.LaneIndex(n.LaneId);
                double hold = n.HoldBeats;
                if (selected && drag == DragMode.Move)
                {
                    beat += previewBeats;
                    lane += previewLanes;
                }
                else if (selected && drag == DragMode.ResizeHold && n.Id == dragAnchorId)
                {
                    hold = previewHold;
                }

                if (lane < 0 || lane >= lanes) continue;
                float hx = geo.BeatToX(beat);
                float ex = geo.BeatToX(beat + hold);
                if (ex < geo.Gutter || hx > w) continue;
                float cy = geo.LaneCenter(lane);

                Color color = Color.white;
                NoteCatalogEntry entry;
                if (catalog.TryGetValue(n.DefinitionId, out entry)) color = entry.Color;

                double travelBeats = NoteHandles.TravelBeats(n, DefaultsOf(n), session.Tempo);
                if (travelBeats > 0d)
                {
                    float sx = geo.BeatToX(beat - travelBeats);
                    Line(p, new Vector2(Mathf.Max(sx, geo.Gutter), cy), new Vector2(hx, cy), new Color(color.r, color.g, color.b, 0.25f), 1f);
                }

                if (selected) DrawHandles(p, n, beat, cy, color, w);

                if (hold > 0d)
                {
                    FillRect(p, hx, cy - 6f, ex - hx, 12f, new Color(color.r, color.g, color.b, 0.45f));
                    FillRect(p, ex - 2f, cy - 9f, 4f, 18f, color);
                }

                FillDiamond(p, new Vector2(hx, cy), geo.HeadRadius, color);
                if (selected) StrokeDiamond(p, new Vector2(hx, cy), geo.HeadRadius + 2f, SelectColor, 2f);
                IssueSeverity severity;
                if (Issues != null && Issues.TryGetValue(n.Id, out severity) && severity != IssueSeverity.Info)
                {
                    Color issueColor = severity == IssueSeverity.Error ? new Color(1f, 0.25f, 0.25f) : new Color(1f, 0.8f, 0.2f);
                    StrokeDiamond(p, new Vector2(hx, cy), geo.HeadRadius + (selected ? 5f : 3f), issueColor, 1.5f);
                }
            }

            // marquee
            if (drag == DragMode.Marquee)
            {
                float x0 = Mathf.Min(marqueeStart.x, marqueeEnd.x), y0 = Mathf.Min(marqueeStart.y, marqueeEnd.y);
                float mw = Mathf.Abs(marqueeEnd.x - marqueeStart.x), mh = Mathf.Abs(marqueeEnd.y - marqueeStart.y);
                FillRect(p, x0, y0, mw, mh, new Color(SelectColor.r, SelectColor.g, SelectColor.b, 0.15f));
            }

            // playhead
            float px = geo.BeatToX(PlayheadBeat);
            if (px >= geo.Gutter && px <= w)
            {
                Line(p, new Vector2(px, 0f), new Vector2(px, lanesBottom), PlayheadColor, 2f);
            }
        }

        // ---------------- interaction ----------------

        private void OnPointerDown(PointerDownEvent evt)
        {
            Focus();
            if (session == null) return;
            Vector2 local = evt.localPosition;
            lastPointerLocal = local;

            if (evt.button == 2)
            {
                drag = DragMode.Pan;
                panLast = local;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (evt.button != 0) return;

            if (local.y < geo.RulerHeight && local.x >= geo.Gutter)
            {
                PlayheadBeat = Math.Max(0d, session.Snap(geo.XToBeat(local.x)));
                if (PlayheadScrubbed != null) PlayheadScrubbed(PlayheadBeat);
                MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }

            PatternHit patternHit = geo.HitTestPattern(session, local.x, local.y);
            if (patternHit.Kind != PatternHitKind.None)
            {
                PatternInstance pat = session.FindPattern(patternHit.PatternId);
                if (evt.shiftKey || evt.actionKey) session.Toggle(pat.Id);
                else if (!session.IsSelected(pat.Id)) session.SelectOnly(pat.Id);
                patternDragId = pat.Id;
                if (patternHit.Kind == PatternHitKind.RightEdge)
                {
                    drag = DragMode.ResizePattern;
                    patternPreviewDuration = pat.DurationBeats;
                }
                else
                {
                    drag = DragMode.MovePattern;
                    dragGrabOffsetBeats = geo.XToBeat(local.x) - pat.StartBeat;
                    patternPreviewStart = pat.StartBeat;
                }

                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            TimelineHit hit = geo.HitTest(session, local.x, local.y, HandlePointsOf);
            if (hit.Kind == TimelineHitKind.Handle)
            {
                NoteInstance hn = session.Find(hit.NoteId);
                dragAnchorId = hit.NoteId;
                dragHandle = hit.Handle;
                previewHandleBeat = NoteHandles.BeatOf(hit.Handle, hn, DefaultsOf(hn), session.Tempo);
                dragTravelGroup = hit.Handle == NoteHandleKind.Travel && session.IsSelected(hit.NoteId)
                                  && session.SelectionCount > 1;
                drag = DragMode.Handle;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (hit.Kind != TimelineHitKind.None)
            {
                NoteInstance n = session.Find(hit.NoteId);
                if (evt.shiftKey || evt.actionKey) session.Toggle(hit.NoteId);
                else if (!session.IsSelected(hit.NoteId)) session.SelectOnly(hit.NoteId);

                dragAnchorId = hit.NoteId;
                dragAnchorLane = session.LaneIndex(n.LaneId);
                if (hit.Kind == TimelineHitKind.HoldEnd)
                {
                    drag = DragMode.ResizeHold;
                    previewHold = n.HoldBeats;
                }
                else
                {
                    drag = DragMode.Move;
                    dragGrabOffsetBeats = geo.XToBeat(local.x) - n.HitBeat;
                    previewBeats = 0d;
                    previewLanes = 0;
                }

                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            bool armedIsPattern = !string.IsNullOrEmpty(ArmedDefinitionId) && ArmedDefinitionId.StartsWith(PatternLibrary.IdPrefix);
            if (!string.IsNullOrEmpty(ArmedDefinitionId) && local.x >= geo.Gutter
                && (geo.YToLane(local.y, false) >= 0 || (armedIsPattern && geo.InPatternTrack(local.y))))
            {
                PlaceAtLocal(ArmedDefinitionId, local);
                if (ArmedPlaced != null) ArmedPlaced(ArmedDefinitionId);
                evt.StopPropagation();
                return;
            }

            if (!(evt.shiftKey || evt.actionKey)) session.ClearSelection();
            drag = DragMode.Marquee;
            marqueeStart = local;
            marqueeEnd = local;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (session == null || drag == DragMode.None || !this.HasPointerCapture(evt.pointerId)) return;
            Vector2 local = evt.localPosition;
            lastPointerLocal = local;
            NoteInstance anchor = dragAnchorId != null ? session.Find(dragAnchorId) : null;

            switch (drag)
            {
                case DragMode.Pan:
                    geo.ScrollByPixels(panLast.x - local.x);
                    panLast = local;
                    Refresh();
                    break;
                case DragMode.Move:
                    if (anchor == null) break;
                    double raw = geo.XToBeat(local.x) - dragGrabOffsetBeats;
                    double target = evt.altKey ? raw : session.Snap(raw);
                    int lane = geo.YToLane(local.y, true);
                    session.ClampMoveDelta(target - anchor.HitBeat, lane - dragAnchorLane, out previewBeats, out previewLanes);
                    MarkDirtyRepaint();
                    break;
                case DragMode.ResizeHold:
                    if (anchor == null) break;
                    double end = geo.XToBeat(local.x);
                    if (!evt.altKey) end = session.Snap(end);
                    previewHold = Math.Max(0d, end - anchor.HitBeat);
                    MarkDirtyRepaint();
                    break;
                case DragMode.Handle:
                {
                    double rawHandle = geo.XToBeat(local.x);
                    previewHandleBeat = evt.altKey ? rawHandle : session.Snap(rawHandle);
                    MarkDirtyRepaint();
                    break;
                }
                case DragMode.MovePattern:
                {
                    PatternInstance pat = session.FindPattern(patternDragId);
                    if (pat == null) break;
                    double rawStart = geo.XToBeat(local.x) - dragGrabOffsetBeats;
                    patternPreviewStart = Math.Max(0d, evt.altKey ? rawStart : session.Snap(rawStart));
                    MarkDirtyRepaint();
                    break;
                }
                case DragMode.ResizePattern:
                {
                    PatternInstance pat = session.FindPattern(patternDragId);
                    if (pat == null) break;
                    double rawEnd = geo.XToBeat(local.x);
                    if (!evt.altKey) rawEnd = session.Snap(rawEnd);
                    patternPreviewDuration = Math.Max(0.25d, rawEnd - pat.StartBeat);
                    MarkDirtyRepaint();
                    break;
                }
                case DragMode.Marquee:
                    marqueeEnd = local;
                    MarkDirtyRepaint();
                    break;
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (drag == DragMode.None) return;
            DragMode finished = drag;
            drag = DragMode.None;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            if (session == null) return;

            switch (finished)
            {
                case DragMode.Move:
                    session.MoveSelected(previewBeats, previewLanes);
                    break;
                case DragMode.ResizeHold:
                    if (dragAnchorId != null) session.SetHoldBeats(dragAnchorId, previewHold);
                    break;
                case DragMode.Handle:
                {
                    NoteInstance hn = dragAnchorId != null ? session.Find(dragAnchorId) : null;
                    if (hn != null && dragTravelGroup)
                    {
                        double travel = GroupTravelBeats();
                        session.ModifySelected("Edit Travel", n => n.TravelBeats = new Overridable<double>(travel));
                    }
                    else if (hn != null) session.SetNoteHandle(hn.Id, dragHandle, previewHandleBeat, DefaultsOf(hn));
                    dragTravelGroup = false;
                    break;
                }
                case DragMode.MovePattern:
                {
                    PatternInstance pat = session.FindPattern(patternDragId);
                    if (pat != null) session.MovePattern(pat.Id, patternPreviewStart - pat.StartBeat);
                    break;
                }
                case DragMode.ResizePattern:
                    if (patternDragId != null) session.ResizePattern(patternDragId, patternPreviewDuration);
                    break;
                case DragMode.Marquee:
                    session.SelectRect(
                        geo.XToBeat(Mathf.Min(marqueeStart.x, marqueeEnd.x)),
                        geo.XToBeat(Mathf.Max(marqueeStart.x, marqueeEnd.x)),
                        geo.YToLane(Mathf.Min(marqueeStart.y, marqueeEnd.y), true),
                        geo.YToLane(Mathf.Max(marqueeStart.y, marqueeEnd.y), true),
                        evt.shiftKey || evt.actionKey);
                    break;
            }

            previewBeats = 0d;
            previewLanes = 0;
            dragAnchorId = null;
            patternDragId = null;
            Refresh();
        }

        private void OnWheel(WheelEvent evt)
        {
            Vector2 local = evt.localMousePosition;
            if (evt.actionKey)
            {
                geo.ZoomAbout(evt.delta.y < 0f ? 1.15d : 1d / 1.15d, local.x);
            }
            else
            {
                geo.ScrollByPixels(evt.delta.y * 12f + evt.delta.x * 12f);
            }

            Refresh();
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (session == null) return;
            bool ctrl = evt.actionKey;
            bool handled = true;
            switch (evt.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    session.DeleteSelected();
                    break;
                case KeyCode.A when ctrl:
                    session.SelectAll();
                    break;
                case KeyCode.C when ctrl:
                    session.Copy();
                    break;
                case KeyCode.X when ctrl:
                    session.Cut();
                    break;
                case KeyCode.V when ctrl:
                    session.Paste(PlayheadBeat);
                    break;
                case KeyCode.D when ctrl:
                    session.DuplicateSelected();
                    break;
                case KeyCode.Z when ctrl:
                    if (evt.shiftKey) session.Redo(); else session.Undo();
                    break;
                case KeyCode.Y when ctrl:
                    session.Redo();
                    break;
                case KeyCode.S when ctrl:
                    if (SaveRequested != null) SaveRequested();
                    break;
                case KeyCode.Space:
                    if (TogglePlayRequested != null) TogglePlayRequested();
                    break;
                case KeyCode.LeftBracket:
                    session.SnapDivision = Math.Max(1, session.SnapDivision / 2);
                    Refresh();
                    break;
                case KeyCode.RightBracket:
                    session.SnapDivision = Math.Min(16, session.SnapDivision * 2);
                    Refresh();
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled) evt.StopPropagation();
        }
    }
}
