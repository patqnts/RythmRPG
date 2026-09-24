using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Simulation;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;
using RythmRPG.LevelComposer.Validation;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>
    /// The chart editor: a horizontal beat timeline with a ruler, the loop's waveform and one row per lane.
    /// Draw tool: click an empty spot to place the armed note type (drag to set a hold's length). Select tool (or Shift):
    /// drag a box. Drag notes to move them, drag a hold's end to resize, drag the dot at the start of a selected note's
    /// travel line to change its travel time. Right-click deletes. Wheel scrolls, Ctrl+wheel zooms, middle-drag pans,
    /// click or drag the ruler to move the playhead.
    /// </summary>
    public sealed class TimelineView : VisualElement
    {
        public const float Gutter = 64f;
        public const float RulerHeight = 26f;
        public const float MusicHeight = 46f;
        private const float MinPxPerBeat = 8f;
        private const float MaxPxPerBeat = 480f;

        private enum DragKind { None, Pan, Move, Length, Travel, Marquee, Draw, Scrub }
        private enum Part { None, Body, LengthHandle, TravelHandle }

        private readonly ComposerContext ctx;
        private readonly List<Label> rulerLabels = new List<Label>();
        private readonly Label[] laneLabels = new Label[LevelStep.MaxLanes];
        private readonly Label musicLabel;
        private readonly Label emptyHint;
        private readonly Dictionary<string, Severity> marks = new Dictionary<string, Severity>();

        private double scrollBeat = -2d;
        private float pxPerBeat = 64f;

        private DragKind drag;
        private Vector2 downPos;
        private double downBeat;
        private int downLane;
        private bool dragStarted;
        private string dragNoteId;
        private readonly Dictionary<string, double> originBeats = new Dictionary<string, double>();
        private readonly Dictionary<string, int> originLanes = new Dictionary<string, int>();
        private double originValue;
        private Vector2 hoverPos;
        private bool hovering;
        private double panStartScroll;
        private bool marqueeAdditive;
        private bool clickedSelected;

        public TimelineView(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("timeline");
            generateVisualContent += OnGenerate;
            pickingMode = PickingMode.Position;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(evt => { hovering = false; MarkDirtyRepaint(); });
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(evt => Refresh());

            for (int i = 0; i < laneLabels.Length; i++)
            {
                var l = new Label();
                l.AddToClassList("timeline__lane-label");
                l.pickingMode = PickingMode.Ignore;
                laneLabels[i] = l;
                Add(l);
            }

            musicLabel = new Label("MUSIC");
            musicLabel.AddToClassList("timeline__music-label");
            musicLabel.pickingMode = PickingMode.Ignore;
            Add(musicLabel);

            emptyHint = new Label("Pick a note type above, then click in a lane to place it.  Space plays the preview.");
            emptyHint.AddToClassList("timeline__hint");
            emptyHint.pickingMode = PickingMode.Ignore;
            Add(emptyHint);

            ctx.Session.Changed += kind => Refresh();
            ctx.FocusRequested += (id, beat) => ScrollTo(beat, true);
        }

        // ------------------------------------------------------------------ geometry

        private LevelStep Step { get { return ctx.Session.Step; } }
        private LevelTempo Tempo { get { return ctx.Session.Tempo; } }
        private float Width { get { return contentRect.width; } }
        private float Height { get { return contentRect.height; } }
        private float LanesTop { get { return RulerHeight + MusicHeight; } }

        private float LaneHeight
        {
            get
            {
                int n = Math.Max(1, Step.LaneCount);
                return Mathf.Clamp((Height - LanesTop - 8f) / n, 30f, 96f);
            }
        }

        private float LaneTop(int lane) { return LanesTop + (lane - 1) * LaneHeight; }
        private float LaneCenter(int lane) { return LaneTop(lane) + LaneHeight * 0.5f; }
        private float XOf(double beat) { return Gutter + (float)((beat - scrollBeat) * pxPerBeat); }
        private double BeatAt(float x) { return scrollBeat + (x - Gutter) / pxPerBeat; }

        private int LaneAt(float y)
        {
            if (y < LanesTop) return 0;
            int lane = (int)Math.Floor((y - LanesTop) / LaneHeight) + 1;
            return lane >= 1 && lane <= Step.LaneCount ? lane : 0;
        }

        private int LaneAtClamped(float y)
        {
            int lane = (int)Math.Floor((y - LanesTop) / LaneHeight) + 1;
            return Math.Max(1, Math.Min(Step.LaneCount, lane));
        }

        private float GlyphRadius { get { return Mathf.Clamp(LaneHeight * 0.26f, 7f, 13f); } }

        public double VisibleStartBeat { get { return scrollBeat; } }
        public double VisibleEndBeat { get { return BeatAt(Width); } }
        public float PxPerBeat { get { return pxPerBeat; } }

        /// <summary>Notes to outline in red / amber (from validation).</summary>
        public void SetMarks(IEnumerable<Issue> issues, int stepIndex)
        {
            marks.Clear();
            foreach (Issue i in issues)
            {
                if (i.StepIndex != stepIndex || string.IsNullOrEmpty(i.NoteId) || i.Severity == Severity.Info) continue;
                Severity s;
                if (!marks.TryGetValue(i.NoteId, out s) || i.Severity > s) marks[i.NoteId] = i.Severity;
            }

            MarkDirtyRepaint();
        }

        // ------------------------------------------------------------------ view control

        public void Zoom(float factor, float anchorX)
        {
            double anchorBeat = BeatAt(anchorX);
            pxPerBeat = Mathf.Clamp(pxPerBeat * factor, MinPxPerBeat, MaxPxPerBeat);
            scrollBeat = anchorBeat - (anchorX - Gutter) / pxPerBeat;
            Refresh();
        }

        public void ZoomAtCenter(float factor) { Zoom(factor, Gutter + (Width - Gutter) * 0.5f); }

        /// <summary>Scrolls so <paramref name="beat"/> is visible (centered when <paramref name="center"/>).</summary>
        public void ScrollTo(double beat, bool center)
        {
            double visible = (Width - Gutter) / pxPerBeat;
            if (center) scrollBeat = beat - visible * 0.35;
            else if (beat < scrollBeat + visible * 0.05) scrollBeat = beat - visible * 0.1;
            else if (beat > scrollBeat + visible * 0.9) scrollBeat = beat - visible * 0.15;
            Refresh();
        }

        /// <summary>Zooms to fit the step's notes (and its lead-in).</summary>
        public void Fit()
        {
            double lead = FlowPlan.LeadSeconds(Step, ctx.Types, Tempo);
            double start = -Tempo.SecondsToBeat(lead);
            double end = Math.Max(Step.LastBeat, 4d) + 1d;
            float width = Mathf.Max(100f, Width - Gutter - 20f);
            pxPerBeat = Mathf.Clamp((float)(width / Math.Max(1d, end - start)), MinPxPerBeat, MaxPxPerBeat);
            scrollBeat = start - 0.5d;
            Refresh();
        }

        /// <summary>Keeps the playhead in view while playing.</summary>
        public void Follow(double playheadBeat)
        {
            double visible = (Width - Gutter) / pxPerBeat;
            if (playheadBeat > scrollBeat + visible * 0.85 || playheadBeat < scrollBeat)
            {
                scrollBeat = playheadBeat - visible * 0.15;
                Refresh();
            }
        }

        /// <summary>Re-lays out the text labels and repaints. Call when the view or data changed.</summary>
        public void Refresh()
        {
            LayoutLabels();
            MarkDirtyRepaint();
        }

        private void LayoutLabels()
        {
            if (float.IsNaN(Width) || Width <= 0f) return;
            int lanes = Step.LaneCount;
            for (int i = 0; i < laneLabels.Length; i++)
            {
                Label l = laneLabels[i];
                bool on = i < lanes;
                l.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                l.text = LaneKeys.KeyName(lanes, i + 1) + "\n<size=9>LANE " + (i + 1) + "</size>";
                l.style.top = LaneTop(i + 1);
                l.style.height = LaneHeight;
                l.style.width = Gutter;
            }

            musicLabel.style.top = RulerHeight;
            musicLabel.style.height = MusicHeight;
            musicLabel.style.width = Gutter;
            emptyHint.style.display = Step.Notes.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            emptyHint.style.top = LanesTop + LaneHeight * lanes + 12f;

            // Bar numbers.
            int bpb = Tempo.BeatsPerBar;
            double barPx = pxPerBeat * bpb;
            int every = barPx >= 60 ? 1 : barPx >= 30 ? 2 : barPx >= 14 ? 4 : 8;
            int firstBar = (int)Math.Floor(scrollBeat / bpb);
            int lastBar = (int)Math.Ceiling(VisibleEndBeat / bpb);
            int used = 0;
            for (int bar = firstBar; bar <= lastBar; bar++)
            {
                if (((bar % every) + every) % every != 0) continue;
                float x = XOf(bar * (double)bpb);
                if (x < Gutter - 2f) continue;
                Label l = RulerLabel(used++);
                l.text = bar >= 0 ? (bar + 1).ToString() : "−" + (-bar);
                l.style.left = x + 3f;
                l.style.display = DisplayStyle.Flex;
                l.EnableInClassList("timeline__ruler-label--lead", bar < 0);
            }

            for (int i = used; i < rulerLabels.Count; i++) rulerLabels[i].style.display = DisplayStyle.None;
        }

        private Label RulerLabel(int index)
        {
            while (rulerLabels.Count <= index)
            {
                var l = new Label();
                l.AddToClassList("timeline__ruler-label");
                l.pickingMode = PickingMode.Ignore;
                Add(l);
                rulerLabels.Add(l);
            }

            return rulerLabels[index];
        }

        // ------------------------------------------------------------------ drawing

        private void OnGenerate(MeshGenerationContext mgc)
        {
            float w = Width, h = Height;
            if (w <= 0f || h <= 0f || float.IsNaN(w)) return;
            Painter2D p = mgc.painter2D;
            LevelStep step = Step;
            LevelTempo tempo = Tempo;
            int lanes = step.LaneCount;
            float laneH = LaneHeight;
            float lanesBottom = LaneTop(lanes + 1);

            Painter.Rect(p, 0, 0, w, h, Palette.Bg0);
            for (int lane = 1; lane <= lanes; lane++)
                Painter.Rect(p, Gutter, LaneTop(lane), w - Gutter, laneH, lane % 2 == 0 ? Palette.Bg1 : Palette.Darken(Palette.Bg1, 0.12f));

            // Lead-in before beat 0 (notes spawn here but nothing can be hit).
            float zeroX = XOf(0d);
            if (zeroX > Gutter)
                Painter.Rect(p, Gutter, RulerHeight, Mathf.Min(zeroX, w) - Gutter, lanesBottom - RulerHeight, Palette.WithAlpha(Palette.Accent, 0.05f));

            DrawGrid(p, tempo, w, lanesBottom);
            DrawMusic(p, w);
            DrawNotes(p, step, tempo, laneH);
            DrawGhost(p, laneH);

            if (drag == DragKind.Marquee && dragStarted)
            {
                float x0 = Mathf.Min(downPos.x, hoverPos.x), y0 = Mathf.Min(downPos.y, hoverPos.y);
                float mw = Mathf.Abs(hoverPos.x - downPos.x), mh = Mathf.Abs(hoverPos.y - downPos.y);
                Painter.Rect(p, x0, y0, mw, mh, Palette.WithAlpha(Palette.Accent, 0.12f));
                Painter.RectOutline(p, x0, y0, mw, mh, Palette.WithAlpha(Palette.Accent, 0.8f));
            }

            // Ruler on top of everything scrolled.
            Painter.Rect(p, 0, 0, w, RulerHeight, Palette.Bg1);
            DrawRulerTicks(p, tempo, w);
            Painter.Line(p, 0, RulerHeight, w, RulerHeight, Palette.Line);
            Painter.Line(p, 0, LanesTop, w, LanesTop, Palette.Line);

            // Gutter.
            Painter.Rect(p, 0, RulerHeight, Gutter, h - RulerHeight, Palette.Bg1);
            for (int lane = 1; lane <= lanes; lane++)
            {
                Painter.Rect(p, 6f, LaneTop(lane) + laneH * 0.5f - 12f, 3f, 24f, LaneColor(lane));
                Painter.Line(p, 0, LaneTop(lane + 1), w, LaneTop(lane + 1), Palette.WithAlpha(Palette.Line, 0.6f));
            }

            Painter.Line(p, Gutter, 0, Gutter, h, Palette.Line);
            DrawPlayhead(p, h, lanesBottom);
        }

        private static Color LaneColor(int lane)
        {
            switch (lane)
            {
                case 1: return Palette.Hex("#33A0F2");
                case 2: return Palette.Hex("#59CC85");
                case 3: return Palette.Hex("#F2B338");
                default: return Palette.Hex("#E0618C");
            }
        }

        private void DrawGrid(Painter2D p, LevelTempo tempo, float w, float bottom)
        {
            int div = Math.Max(1, ctx.Session.SnapDivision);
            double start = Math.Floor(scrollBeat * div) / div;
            double end = VisibleEndBeat;
            bool showSub = pxPerBeat / div >= 7f;
            bool showBeats = pxPerBeat >= 10f;
            int bpb = tempo.BeatsPerBar;
            for (double b = start; b <= end + 1e-9; b += 1d / div)
            {
                float x = XOf(b);
                if (x < Gutter) continue;
                double rounded = Math.Round(b);
                bool isBeat = Math.Abs(b - rounded) < 1e-6;
                bool isBar = isBeat && (((long)rounded % bpb) + bpb) % bpb == 0;
                if (isBar) Painter.Line(p, x, RulerHeight, x, bottom, Palette.LineStrong, 1f);
                else if (isBeat && showBeats) Painter.Line(p, x, LanesTop, x, bottom, Palette.Line, 1f);
                else if (showSub) Painter.Line(p, x, LanesTop, x, bottom, Palette.WithAlpha(Palette.Line, 0.45f), 1f);
            }

            float zx = XOf(0d);
            if (zx >= Gutter && zx <= w) Painter.Line(p, zx, RulerHeight, zx, bottom, Palette.WithAlpha(Palette.Accent, 0.7f), 2f);
        }

        private void DrawRulerTicks(Painter2D p, LevelTempo tempo, float w)
        {
            int bpb = tempo.BeatsPerBar;
            double start = Math.Floor(scrollBeat);
            double end = VisibleEndBeat;
            p.strokeColor = Palette.LineStrong;
            p.lineWidth = 1f;
            p.BeginPath();
            for (double b = start; b <= end; b += 1d)
            {
                float x = XOf(b);
                if (x < Gutter) continue;
                bool bar = ((((long)b) % bpb) + bpb) % bpb == 0;
                if (!bar && pxPerBeat < 10f) continue;
                p.MoveTo(new Vector2(x, bar ? 4f : RulerHeight - 7f));
                p.LineTo(new Vector2(x, RulerHeight));
            }

            p.Stroke();
        }

        private void DrawMusic(Painter2D p, float w)
        {
            float top = RulerHeight, h = MusicHeight, mid = top + h * 0.5f;
            Painter.Rect(p, Gutter, top, w - Gutter, h, Palette.Darken(Palette.Bg0, 0.1f));
            FlowPlan plan = ctx.Preview.Plan;
            double zero = ctx.Preview.ZeroOfStep(ctx.Session.StepIndex);
            double spb = Tempo.SecondsPerBeat;
            AudioEntry loop = ctx.Section(MusicSection.Loop);

            // Level preview: tint the player turn and other segments that overlap the visible range.
            if (plan != null && plan.IsLevel)
            {
                foreach (FlowSegment s in plan.Segments)
                {
                    if (s.Kind == FlowSegmentKind.EnemyStep && s.StepIndex == ctx.Session.StepIndex) continue;
                    float x0 = XOf((s.Start - zero) / spb), x1 = XOf((s.End - zero) / spb);
                    if (x1 < Gutter || x0 > w) continue;
                    x0 = Mathf.Max(Gutter, x0);
                    Color c = s.Kind == FlowSegmentKind.PlayerTurn ? Palette.PlayerTurn : s.Kind == FlowSegmentKind.Intro ? Palette.Intro : s.Kind == FlowSegmentKind.End ? Palette.End : Palette.TextDim;
                    Painter.Rect(p, x0, top, Mathf.Min(w, x1) - x0, h, Palette.WithAlpha(c, 0.1f));
                }
            }

            if (loop == null || loop.Peaks == null || plan == null)
            {
                Painter.Line(p, Gutter, mid, w, mid, Palette.WithAlpha(Palette.Line, 0.8f));
                return;
            }

            float amp = Mathf.Max(0.05f, loop.Peaks.PeakAmplitude);
            double secPerPx = spb / pxPerBeat;
            const float stepPx = 2f;
            p.strokeColor = Palette.WithAlpha(Palette.Accent, 0.55f);
            p.lineWidth = stepPx - 0.5f;
            p.BeginPath();
            for (float x = Gutter; x < w; x += stepPx)
            {
                double t = zero + BeatAt(x) * spb;
                double clipT = plan.LoopClipTime(t);
                if (double.IsNaN(clipT)) continue;
                float mn, mx;
                if (!loop.Peaks.QueryRange(clipT, clipT + secPerPx * stepPx, out mn, out mx)) continue;
                float y0 = mid - Mathf.Clamp(mx / amp, -1f, 1f) * (h * 0.45f);
                float y1 = mid - Mathf.Clamp(mn / amp, -1f, 1f) * (h * 0.45f);
                if (y1 - y0 < 1f) { y0 -= 0.5f; y1 += 0.5f; }
                p.MoveTo(new Vector2(x, y0));
                p.LineTo(new Vector2(x, y1));
            }

            p.Stroke();

            // Loop restarts (sample 0) as thin markers.
            if (plan.LoopLength > 0d)
            {
                double firstT = zero + scrollBeat * spb, lastT = zero + VisibleEndBeat * spb;
                double k0 = Math.Ceiling((firstT - plan.LoopStart) / plan.LoopLength);
                for (double k = Math.Max(0d, k0); k < k0 + 64; k++)
                {
                    double t = plan.LoopStart + k * plan.LoopLength;
                    if (t > lastT || t >= plan.EndStart) break;
                    float x = XOf((t - zero) / spb);
                    Painter.Line(p, x, top, x, top + h, Palette.WithAlpha(Palette.Perfect, 0.6f), 1f);
                }
            }
        }

        private void DrawNotes(Painter2D p, LevelStep step, LevelTempo tempo, float laneH)
        {
            float r = GlyphRadius;
            double start = scrollBeat - 64d, end = VisibleEndBeat + 1d;
            string hoverId = null;
            Part hoverPart = Part.None;
            if (hovering && drag == DragKind.None) hoverId = HitTest(hoverPos, out hoverPart);

            // Travel lines first (behind the notes) for selected / hovered notes.
            foreach (LevelNote n in step.Notes)
            {
                bool sel = ctx.Session.IsSelected(n.Id);
                if (!sel && n.Id != hoverId) continue;
                NoteTypeDef def = ctx.Types.Find(n.Type);
                if (def == null || def.Output == NoteOutput.Sequence || def.FindBound(ParamBindings.TravelBeats) == null) continue;
                double travel = NoteParams.TravelBeats(n, def, tempo);
                float y = LaneCenter(n.Lane);
                float xs = XOf(n.Beat - travel), xh = XOf(n.Beat);
                Color c = Painter.ColorOf(def);
                if (def.Stationary)
                {
                    // Charge: a band that fills toward the hit.
                    Painter.RoundRect(p, xs, y - 3f, xh - xs, 6f, 3f, Palette.WithAlpha(c, sel ? 0.28f : 0.16f));
                }
                else
                {
                    Painter.DashedLine(p, xs, y, xh - r, Palette.WithAlpha(c, sel ? 0.75f : 0.45f), 1.5f);
                }

                if (sel)
                {
                    Painter.Circle(p, new Vector2(xs, y), 5f, Palette.Bg0);
                    Painter.Ring(p, new Vector2(xs, y), 5f, c, 2f);
                }
            }

            foreach (LevelNote n in step.Notes)
            {
                if (n.EndBeat < start || n.Beat > end) continue;
                NoteTypeDef def = ctx.Types.Find(n.Type);
                Color c = Painter.ColorOf(def);
                bool sel = ctx.Session.IsSelected(n.Id);
                float x = XOf(n.Beat), y = LaneCenter(n.Lane);
                if (n.Lane > step.LaneCount) continue;

                if (def != null && def.HasLength && n.Length > 0d)
                {
                    float xe = XOf(n.EndBeat);
                    float bh = Mathf.Max(8f, laneH * 0.34f);
                    Painter.RoundRect(p, x, y - bh * 0.5f, xe - x, bh, bh * 0.5f, Palette.WithAlpha(c, sel ? 0.6f : 0.42f));
                    if (sel || n.Id == hoverId)
                    {
                        Painter.RoundRect(p, xe - 4f, y - bh * 0.5f - 2f, 6f, bh + 4f, 2f, sel ? Palette.Text : Palette.WithAlpha(Palette.Text, 0.6f));
                    }
                }
                else if (def != null && def.Output == NoteOutput.Sequence)
                {
                    double estimate = SequenceBeats(n, def, tempo);
                    float xe = XOf(n.Beat + estimate);
                    Painter.DashedLine(p, x + r, y, xe, Palette.WithAlpha(c, 0.7f), 3f, 7f, 5f);
                    Painter.Line(p, xe, y - 7f, xe, y + 7f, Palette.WithAlpha(c, 0.7f), 2f);
                }

                Color outline = Palette.Darken(c, 0.45f);
                float ow = 1.5f;
                Severity sev;
                if (marks.TryGetValue(n.Id, out sev))
                {
                    outline = sev == Severity.Error ? Palette.Error : Palette.Warning;
                    ow = 2.5f;
                }

                if (def == null) { c = Palette.TextDim; outline = Palette.Error; ow = 2.5f; }
                if (sel)
                {
                    Painter.Circle(p, new Vector2(x, y), r + 5f, Palette.WithAlpha(Palette.Text, 0.16f));
                    outline = Palette.Text;
                    ow = 2.5f;
                }
                else if (n.Id == hoverId)
                {
                    c = Palette.Lighten(c, 0.2f);
                }

                Painter.Shape(p, def != null ? def.Shape : NoteShape.Square, new Vector2(x, y), r, c, outline, ow);
            }
        }

        private double SequenceBeats(LevelNote n, NoteTypeDef def, LevelTempo tempo)
        {
            if (def.Archetype != Archetypes.PingPong) return 1d;
            int volleys = (int)Math.Round(NoteParams.GetBound(n, def, ParamBindings.SeqVolleys, tempo, 3d));
            double s = PingPongBehaviour.EstimateSeconds(volleys,
                NoteParams.GetBound(n, def, ParamBindings.SeqInitialTravel, tempo, 2d),
                NoteParams.GetBound(n, def, ParamBindings.SeqSpeedUp, tempo, 0.85d),
                NoteParams.GetBound(n, def, ParamBindings.SeqMinTravel, tempo, 0.6d),
                NoteParams.GetBound(n, def, ParamBindings.SeqReturn, tempo, 0.5d));
            return tempo.SecondsToBeat(s);
        }

        private void DrawGhost(Painter2D p, float laneH)
        {
            if (!hovering || drag != DragKind.None || ctx.Tool != TimelineTool.Draw) return;
            int lane = LaneAt(hoverPos.y);
            if (lane == 0 || hoverPos.x < Gutter) return;
            Part part;
            if (HitTest(hoverPos, out part) != null) return;
            NoteTypeDef def = ctx.Types.Find(ctx.BrushType);
            if (def == null) return;
            double beat = Math.Max(0d, ctx.Session.Snap(BeatAt(hoverPos.x)));
            Color c = Painter.ColorOf(def);
            float x = XOf(beat), y = LaneCenter(lane);
            if (def.HasLength)
            {
                float bh = Mathf.Max(8f, laneH * 0.34f);
                Painter.RoundRect(p, x, y - bh * 0.5f, XOf(beat + def.DefaultLengthBeats) - x, bh, bh * 0.5f, Palette.WithAlpha(c, 0.18f));
            }

            Painter.Shape(p, def.Shape, new Vector2(x, y), GlyphRadius, Palette.WithAlpha(c, 0.35f), Palette.WithAlpha(Palette.Text, 0.35f), 1f);
        }

        private void DrawPlayhead(Painter2D p, float h, float lanesBottom)
        {
            double beat = ctx.Preview.PlayheadBeat;
            float x = XOf(beat);
            if (x < Gutter || x > Width) return;
            Painter.Line(p, x, 0, x, lanesBottom, Palette.Accent, 2f);
            p.fillColor = Palette.Accent;
            p.BeginPath();
            p.MoveTo(new Vector2(x - 6f, 0));
            p.LineTo(new Vector2(x + 6f, 0));
            p.LineTo(new Vector2(x, 9f));
            p.ClosePath();
            p.Fill();
        }

        // ------------------------------------------------------------------ hit testing

        private string HitTest(Vector2 pos, out Part part)
        {
            part = Part.None;
            int lane = LaneAt(pos.y);
            if (lane == 0 || pos.x < Gutter) return null;
            float r = GlyphRadius + 3f;
            LevelTempo tempo = Tempo;
            List<LevelNote> notes = Step.Notes;
            // Handles of selected notes first.
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                LevelNote n = notes[i];
                if (n.Lane != lane || !ctx.Session.IsSelected(n.Id)) continue;
                NoteTypeDef def = ctx.Types.Find(n.Type);
                if (def == null) continue;
                if (def.HasLength && n.Length > 0d && Mathf.Abs(pos.x - XOf(n.EndBeat)) <= 6f) { part = Part.LengthHandle; return n.Id; }
                if (def.Output != NoteOutput.Sequence && def.FindBound(ParamBindings.TravelBeats) != null)
                {
                    float xs = XOf(n.Beat - NoteParams.TravelBeats(n, def, tempo));
                    if (Mathf.Abs(pos.x - xs) <= 7f && Mathf.Abs(pos.y - LaneCenter(lane)) <= 8f) { part = Part.TravelHandle; return n.Id; }
                }
            }

            for (int i = notes.Count - 1; i >= 0; i--)
            {
                LevelNote n = notes[i];
                if (n.Lane != lane) continue;
                float x = XOf(n.Beat);
                if (Mathf.Abs(pos.x - x) <= r) { part = Part.Body; return n.Id; }
                NoteTypeDef def = ctx.Types.Find(n.Type);
                if (def != null && def.HasLength && n.Length > 0d && pos.x >= x && pos.x <= XOf(n.EndBeat) && Mathf.Abs(pos.y - LaneCenter(lane)) < LaneHeight * 0.25f)
                {
                    part = Part.Body;
                    return n.Id;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ input

        private void OnPointerDown(PointerDownEvent evt)
        {
            Vector2 pos = evt.localPosition;
            downPos = pos;
            hoverPos = pos;
            dragStarted = false;
            downBeat = BeatAt(pos.x);
            downLane = LaneAtClamped(pos.y);
            bool ctrl = evt.ctrlKey || evt.commandKey;

            if (evt.button == 2 || (evt.button == 0 && evt.altKey && pos.y >= LanesTop))
            {
                drag = DragKind.Pan;
                panStartScroll = scrollBeat;
                Capture(evt);
                return;
            }

            if (pos.y < LanesTop && pos.x >= Gutter)
            {
                if (evt.button != 0) return;
                drag = DragKind.Scrub;
                ctx.Preview.SeekToBeat(BeatAt(pos.x));
                Capture(evt);
                MarkDirtyRepaint();
                return;
            }

            if (pos.x < Gutter) return;
            Part part;
            string id = HitTest(pos, out part);

            if (evt.button == 1)
            {
                if (id != null) ctx.Session.DeleteNote(id);
                return;
            }

            if (evt.button != 0) return;

            if (id != null)
            {
                LevelNote n = Step.Find(id);
                dragNoteId = id;
                if (part == Part.LengthHandle)
                {
                    drag = DragKind.Length;
                    originValue = n.Length;
                    Capture(evt);
                    return;
                }

                if (part == Part.TravelHandle)
                {
                    drag = DragKind.Travel;
                    NoteTypeDef def = ctx.Types.Find(n.Type);
                    originValue = NoteParams.TravelBeats(n, def, Tempo);
                    Capture(evt);
                    return;
                }

                clickedSelected = !ctrl && ctx.Session.IsSelected(id) && ctx.Session.SelectionCount > 1;
                if (ctrl) ctx.Session.Toggle(id);
                else if (!ctx.Session.IsSelected(id)) ctx.Session.SelectOnly(id);
                if (evt.clickCount >= 2)
                {
                    ctx.Session.SelectOfType(n.Type);
                    return;
                }

                if (!ctx.Session.IsSelected(id)) return;
                drag = DragKind.Move;
                originBeats.Clear();
                originLanes.Clear();
                foreach (LevelNote s in ctx.Session.SelectedNotes())
                {
                    originBeats[s.Id] = s.Beat;
                    originLanes[s.Id] = s.Lane;
                }

                Capture(evt);
                return;
            }

            // Empty space.
            if (evt.shiftKey || ctx.Tool == TimelineTool.Select)
            {
                drag = DragKind.Marquee;
                marqueeAdditive = evt.shiftKey || ctrl;
                if (!marqueeAdditive) ctx.Session.ClearSelection();
                Capture(evt);
                return;
            }

            int lane = LaneAt(pos.y);
            if (lane == 0) return;
            NoteTypeDef brush = ctx.Types.Find(ctx.BrushType);
            if (brush == null) return;
            ctx.Session.BeginGesture("Add " + brush.Name);
            LevelNote added = ctx.Session.AddNote(brush.Id, lane, Math.Max(0d, BeatAt(pos.x)));
            if (added == null) { ctx.Session.EndGesture(); return; }
            dragNoteId = added.Id;
            originValue = added.Length;
            drag = DragKind.Draw;
            Capture(evt);
        }

        private void Capture(PointerDownEvent evt)
        {
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 pos = evt.localPosition;
            hoverPos = pos;
            hovering = true;
            if (drag == DragKind.None)
            {
                MarkDirtyRepaint();
                return;
            }

            if (!dragStarted && (pos - downPos).sqrMagnitude < 9f) return;
            bool first = !dragStarted;
            dragStarted = true;
            double beat = BeatAt(pos.x);

            switch (drag)
            {
                case DragKind.Pan:
                    scrollBeat = panStartScroll - (pos.x - downPos.x) / pxPerBeat;
                    Refresh();
                    break;
                case DragKind.Scrub:
                    ctx.Preview.SeekToBeat(beat);
                    MarkDirtyRepaint();
                    break;
                case DragKind.Marquee:
                    MarkDirtyRepaint();
                    break;
                case DragKind.Move:
                {
                    if (first) ctx.Session.BeginGesture("Move notes");
                    double anchorOrigin;
                    if (!originBeats.TryGetValue(dragNoteId, out anchorOrigin)) break;
                    double target = ctx.Session.Snap(anchorOrigin + (beat - downBeat));
                    double deltaBeats = target - anchorOrigin;
                    int deltaLanes = LaneAtClamped(pos.y) - downLane;
                    double minBeat = double.MaxValue;
                    int minLane = int.MaxValue, maxLane = int.MinValue;
                    foreach (KeyValuePair<string, double> kv in originBeats)
                    {
                        minBeat = Math.Min(minBeat, kv.Value);
                        minLane = Math.Min(minLane, originLanes[kv.Key]);
                        maxLane = Math.Max(maxLane, originLanes[kv.Key]);
                    }

                    if (minBeat + deltaBeats < 0d) deltaBeats = -minBeat;
                    if (minLane + deltaLanes < 1) deltaLanes = 1 - minLane;
                    if (maxLane + deltaLanes > Step.LaneCount) deltaLanes = Step.LaneCount - maxLane;
                    var beats = new Dictionary<string, double>();
                    var lanes = new Dictionary<string, int>();
                    foreach (KeyValuePair<string, double> kv in originBeats)
                    {
                        beats[kv.Key] = kv.Value + deltaBeats;
                        lanes[kv.Key] = originLanes[kv.Key] + deltaLanes;
                    }

                    ctx.Session.SetPositions(beats, lanes);
                    break;
                }
                case DragKind.Length:
                case DragKind.Draw:
                {
                    LevelNote n = Step.Find(dragNoteId);
                    NoteTypeDef def = n != null ? ctx.Types.Find(n.Type) : null;
                    if (n == null || def == null || !def.HasLength) break;
                    if (first && drag == DragKind.Length) ctx.Session.BeginGesture("Change length");
                    double endBeat = ctx.Session.Snap(beat);
                    double min = 1d / Math.Max(1, ctx.Session.SnapDivision);
                    ctx.Session.SetLength(n.Id, Math.Max(min, endBeat - n.Beat));
                    break;
                }
                case DragKind.Travel:
                {
                    LevelNote n = Step.Find(dragNoteId);
                    NoteTypeDef def = n != null ? ctx.Types.Find(n.Type) : null;
                    ParamDef travel = def != null ? def.FindBound(ParamBindings.TravelBeats) : null;
                    if (travel == null) break;
                    if (first) ctx.Session.BeginGesture("Change travel");
                    double spawn = ctx.Session.Snap(beat);
                    double value = Math.Max(travel.Min > 0 ? travel.Min : 0.25d, n.Beat - spawn);
                    // Every selected note of a type with travel gets the same lead-in (like the game's composer).
                    ctx.Session.ModifySelected("Change travel", (x, d) =>
                    {
                        ParamDef tp = d != null ? d.FindBound(ParamBindings.TravelBeats) : null;
                        if (tp != null) NoteParams.Set(x, d, tp.Key, value, ctx.Session.Tempo);
                    });
                    break;
                }
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            DragKind was = drag;
            drag = DragKind.None;
            switch (was)
            {
                case DragKind.Marquee:
                    if (dragStarted)
                    {
                        Vector2 a = downPos, b = evt.localPosition;
                        ctx.Session.SelectRect(BeatAt(Mathf.Min(a.x, b.x)), BeatAt(Mathf.Max(a.x, b.x)),
                            LaneAtClamped(Mathf.Min(a.y, b.y)), LaneAtClamped(Mathf.Max(a.y, b.y)), marqueeAdditive);
                    }

                    break;
                case DragKind.Move:
                    if (dragStarted) ctx.Session.EndGesture();
                    else if (clickedSelected) ctx.Session.SelectOnly(dragNoteId); // plain click inside a group picks that note
                    break;
                case DragKind.Length:
                case DragKind.Travel:
                    if (dragStarted) ctx.Session.EndGesture();
                    break;
                case DragKind.Draw:
                    ctx.Session.EndGesture();
                    break;
            }

            MarkDirtyRepaint();
        }

        private void OnWheel(WheelEvent evt)
        {
            float dy = evt.delta.y;
            if (evt.ctrlKey || evt.commandKey)
            {
                Zoom(dy > 0 ? 1f / 1.15f : 1.15f, evt.localMousePosition.x);
            }
            else
            {
                float d = Mathf.Abs(evt.delta.x) > Mathf.Abs(dy) ? evt.delta.x : dy;
                scrollBeat += d * 24f / pxPerBeat;
                Refresh();
            }

            evt.StopPropagation();
        }
    }
}
