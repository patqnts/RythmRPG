using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Simulation;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>
    /// Flat top-down preview of combat: notes leave the enemy at the top and travel down their lane to the hit line
    /// above the player, stationary notes charge on the lane markers, judgements pop up like in the game. Driven entirely
    /// by the <see cref="Simulator"/> and the preview clock. Also shows the level flow (intro / steps / player turns / end).
    /// </summary>
    public sealed class SimulatorView : VisualElement
    {
        private const float FlowHeight = 30f;
        private readonly ComposerContext ctx;
        private readonly Label segmentLabel;
        private readonly Label comboLabel;
        private readonly Label statsLabel;
        private readonly Label centerLabel;
        private readonly Label[] keyLabels = new Label[4];
        private readonly List<Label> popupLabels = new List<Label>();
        private readonly List<Label> countLabels = new List<Label>();

        public SimulatorView(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("sim");
            generateVisualContent += OnGenerate;
            segmentLabel = Add("sim__segment");
            comboLabel = Add("sim__combo");
            statsLabel = Add("sim__stats");
            centerLabel = Add("sim__center");
            for (int i = 0; i < keyLabels.Length; i++) keyLabels[i] = Add("sim__key");
            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        private Label Add(string cls)
        {
            var l = new Label();
            l.AddToClassList(cls);
            l.pickingMode = PickingMode.Ignore;
            Add(l);
            return l;
        }

        // ------------------------------------------------------------------ geometry

        private float W { get { return contentRect.width; } }
        private float H { get { return contentRect.height; } }
        private float SpawnY { get { return FlowHeight + 58f; } }
        private float HitY { get { return H - 86f; } }

        private void LaneGeometry(int laneCount, out float laneW, out float left)
        {
            laneW = Mathf.Min(78f, (W - 36f) / Mathf.Max(1, laneCount));
            left = (W - laneW * laneCount) * 0.5f;
        }

        private float NoteY(double progress)
        {
            return SpawnY + (HitY - SpawnY) * (float)progress;
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Updates the labels and repaints. Called every frame by the app.</summary>
        public void Refresh()
        {
            if (float.IsNaN(W) || W <= 0f) return;
            PreviewController preview = ctx.Preview;
            double t = preview.Time;
            FlowPlan plan = preview.Plan;
            int lanes = preview.LaneCountAt(t);
            float laneW, left;
            LaneGeometry(lanes, out laneW, out left);

            FlowSegment seg = plan.SegmentAt(t);
            string segText = "";
            if (seg != null)
            {
                switch (seg.Kind)
                {
                    case FlowSegmentKind.Intro: segText = "INTRO"; break;
                    case FlowSegmentKind.EnemyStep: segText = "ENEMY  ·  " + (seg.StepIndex + 1) + ". " + seg.Label; break;
                    case FlowSegmentKind.PlayerTurn: segText = "PLAYER TURN"; break;
                    case FlowSegmentKind.End: segText = "END"; break;
                }
            }

            segmentLabel.text = segText + "   <color=#8E96A8>" + FormatTime(t) + "</color>";
            SimStats s = preview.Sim.Stats;
            comboLabel.text = s.Combo >= 2 ? s.Combo + "<size=12>\nCOMBO</size>" : "";
            statsLabel.text = "<color=#FFD84A>P " + s.Perfect + "</color>  <color=#3DDC97>G " + s.Good + "</color>  <color=#FF9F43>B " + s.Bad
                              + "</color>  <color=#FF5C6C>M " + s.Miss + "</color>\n" + (s.Accuracy * 100d).ToString("0.0") + "%  ·  max " + s.MaxCombo + "  ·  dmg " + s.DamageTaken;
            statsLabel.style.display = s.Judged > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            bool playerTurn = seg != null && seg.Kind == FlowSegmentKind.PlayerTurn;
            bool windUp = seg != null && seg.Kind == FlowSegmentKind.EnemyStep && t >= seg.WindUpStart && t < seg.WindUpStart + ctx.Session.Level.Steps[Math.Max(0, Math.Min(seg.StepIndex, ctx.Session.Level.Steps.Count - 1))].Anticipation;
            centerLabel.text = playerTurn ? "PLAYER TURN" : windUp ? "wind-up" : (!preview.IsPlaying && preview.Sim.Count == 0 ? "No notes in this step" : "");
            centerLabel.style.top = (SpawnY + HitY) * 0.5f - 14f;

            for (int i = 0; i < keyLabels.Length; i++)
            {
                Label l = keyLabels[i];
                bool on = i < lanes;
                l.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                l.text = LaneKeys.KeyName(lanes, i + 1);
                l.style.left = left + laneW * i;
                l.style.width = laneW;
                l.style.top = HitY + 26f;
                l.EnableInClassList("sim__key--down", preview.Sim.IsKeyHeld(i + 1));
            }

            LayoutPopups(t, lanes, laneW, left);
            LayoutMashCounts(t, lanes, laneW, left);
            MarkDirtyRepaint();
        }

        private void LayoutPopups(double t, int lanes, float laneW, float left)
        {
            int used = 0;
            IList<Popup> popups = ctx.Preview.Popups;
            for (int i = popups.Count - 1; i >= 0 && used < 12; i--)
            {
                Popup p = popups[i];
                double age = t - p.Time;
                if (age < -0.05d || age > 0.7d) continue;
                Label l = Pooled(popupLabels, used++, "sim__popup");
                string word = p.Judgement == Judgement.None ? "" : p.Judgement.ToString().ToUpperInvariant();
                string extra = string.IsNullOrEmpty(p.Text) ? "" : (word.Length > 0 ? "\n<size=10>" + p.Text + "</size>" : p.Text);
                l.text = word + extra;
                l.style.color = JudgementColor(p.Judgement);
                float lane = Mathf.Clamp(p.Lane, 1, lanes);
                float x = p.Lane >= 1 ? left + laneW * (lane - 1) : left;
                float width = p.Lane >= 1 ? laneW : laneW * lanes;
                l.style.left = x - 20f;
                l.style.width = width + 40f;
                l.style.top = HitY - 58f - (float)age * 40f;
                l.style.opacity = Mathf.Clamp01(1f - (float)(age - 0.35d) / 0.35f);
                l.style.display = DisplayStyle.Flex;
            }

            for (int i = used; i < popupLabels.Count; i++) popupLabels[i].style.display = DisplayStyle.None;
        }

        private void LayoutMashCounts(double t, int lanes, float laneW, float left)
        {
            int used = 0;
            foreach (SimNote n in ctx.Preview.Sim.Notes)
            {
                if (n.Def == null || n.Def.Archetype != Archetypes.Mash || n.State != SimNoteState.Active || t < n.SpawnTime) continue;
                if (n.Lane > lanes || used >= 8) continue;
                Label l = Pooled(countLabels, used++, "sim__count");
                l.text = n.Presses + "/" + n.RequiredPresses;
                l.style.left = left + laneW * (n.Lane - 1);
                l.style.width = laneW;
                l.style.top = NoteY(Math.Min(1.05d, n.Progress(t))) + 14f;
                l.style.display = DisplayStyle.Flex;
            }

            for (int i = used; i < countLabels.Count; i++) countLabels[i].style.display = DisplayStyle.None;
        }

        private Label Pooled(List<Label> pool, int index, string cls)
        {
            while (pool.Count <= index) pool.Add(Add(cls));
            return pool[index];
        }

        public static Color JudgementColor(Judgement j)
        {
            switch (j)
            {
                case Judgement.Perfect: return Palette.Perfect;
                case Judgement.Good: return Palette.Good;
                case Judgement.Bad: return Palette.Bad;
                case Judgement.Miss: return Palette.Miss;
                default: return Palette.Text;
            }
        }

        private static string FormatTime(double t)
        {
            string sign = t < 0 ? "-" : "";
            t = Math.Abs(t);
            int m = (int)(t / 60d);
            return sign + m + ":" + (t - m * 60).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ drawing

        private void OnGenerate(MeshGenerationContext mgc)
        {
            float w = W, h = H;
            if (w <= 0f || h <= 0f || float.IsNaN(w)) return;
            Painter2D p = mgc.painter2D;
            PreviewController preview = ctx.Preview;
            double t = preview.Time;
            FlowPlan plan = preview.Plan;
            int lanes = preview.LaneCountAt(t);
            float laneW, left;
            LaneGeometry(lanes, out laneW, out left);
            float spawnY = SpawnY, hitY = HitY;

            Painter.Rect(p, 0, 0, w, h, Palette.Bg0);
            DrawFlow(p, plan, t, w);

            FlowSegment seg = plan.SegmentAt(t);
            bool playerTurn = seg != null && seg.Kind == FlowSegmentKind.PlayerTurn;

            // Lanes.
            for (int i = 0; i < lanes; i++)
            {
                float x = left + laneW * i;
                Painter.Rect(p, x + 2f, spawnY, laneW - 4f, hitY - spawnY + 20f, Palette.WithAlpha(Palette.Bg2, playerTurn ? 0.35f : 0.85f));
            }

            // Enemy (with wind-up glow) and player.
            Vector2 enemy = new Vector2(w * 0.5f, FlowHeight + 26f);
            float windUpGlow = 0f;
            if (seg != null && seg.Kind == FlowSegmentKind.EnemyStep)
            {
                AttackStepInfo(seg, out double anticipation);
                double since = t - seg.WindUpStart;
                if (since >= 0d && since < anticipation + 0.15d) windUpGlow = 1f - Mathf.Clamp01((float)((since - anticipation) / 0.15d));
            }

            if (windUpGlow > 0f) Painter.Circle(p, enemy, 22f + 6f * windUpGlow, Palette.WithAlpha(Palette.Miss, 0.25f * windUpGlow));
            Painter.Shape(p, NoteShape.Hexagon, enemy, 15f, Palette.Hex("#3A3F4F"), Palette.Hex("#FF5C6C"), 2f);
            Painter.Circle(p, new Vector2(w * 0.5f, h - 22f), 11f, Palette.Hex("#3A3F4F"));
            Painter.Ring(p, new Vector2(w * 0.5f, h - 22f), 11f, Palette.Accent, 2f);

            // Hit line and key markers.
            Painter.Rect(p, left - 6f, hitY - 1.5f, laneW * lanes + 12f, 3f, Palette.WithAlpha(Palette.Text, 0.7f));
            var markerCenters = new Vector2[lanes + 1];
            for (int i = 0; i < lanes; i++)
            {
                float cx = left + laneW * (i + 0.5f);
                markerCenters[i + 1] = new Vector2(cx, hitY);
                bool down = preview.Sim.IsKeyHeld(i + 1);
                double since = preview.Sim.SinceLastPress(i + 1);
                float flash = since >= 0d && since < 0.12d ? 1f - (float)(since / 0.12d) : 0f;
                float size = 15f;
                Painter.RoundRect(p, cx - size, hitY - size, size * 2f, size * 2f, 6f, down ? Palette.WithAlpha(Palette.Accent, 0.55f) : Palette.WithAlpha(Palette.Bg3, 0.95f));
                if (flash > 0f) Painter.RoundRect(p, cx - size - 3f, hitY - size - 3f, size * 2f + 6f, size * 2f + 6f, 8f, Palette.WithAlpha(Palette.Text, 0.25f * flash));
            }

            DrawNotes(p, preview, t, lanes, laneW, left, markerCenters);
        }

        private void AttackStepInfo(FlowSegment seg, out double anticipation)
        {
            var steps = ctx.Session.Level.Steps;
            anticipation = seg.StepIndex >= 0 && seg.StepIndex < steps.Count ? steps[seg.StepIndex].Anticipation : 0d;
        }

        private void DrawFlow(Painter2D p, FlowPlan plan, double t, float w)
        {
            Painter.Rect(p, 0, 0, w, FlowHeight, Palette.Bg1);
            if (!plan.IsLevel)
            {
                Painter.Line(p, 0, FlowHeight, w, FlowHeight, Palette.Line);
                return;
            }

            double start = plan.TimelineStart, dur = Math.Max(1d, plan.Duration - start);
            float barTop = FlowHeight - 7f;
            foreach (FlowSegment s in plan.Segments)
            {
                float x0 = (float)((s.Start - start) / dur) * w, x1 = (float)((s.End - start) / dur) * w;
                Color c = s.Kind == FlowSegmentKind.PlayerTurn ? Palette.PlayerTurn : s.Kind == FlowSegmentKind.Intro ? Palette.Intro : s.Kind == FlowSegmentKind.End ? Palette.End : Palette.Accent;
                Painter.Rect(p, x0 + 0.5f, barTop, Mathf.Max(1f, x1 - x0 - 1f), 5f, Palette.WithAlpha(c, s.StepIndex == ctx.Session.StepIndex && s.Kind == FlowSegmentKind.EnemyStep ? 1f : 0.55f));
            }

            float px = (float)((t - start) / dur) * w;
            Painter.Rect(p, px - 1f, barTop - 4f, 2f, 12f, Palette.Text);
            Painter.Line(p, 0, FlowHeight, w, FlowHeight, Palette.Line);
        }

        private void DrawNotes(Painter2D p, PreviewController preview, double t, int lanes, float laneW, float left, Vector2[] markers)
        {
            bool idle = !preview.IsPlaying;
            float r = Mathf.Clamp(laneW * 0.2f, 6f, 12f);
            foreach (SimNote n in preview.Sim.Notes)
            {
                if (n.Lane < 1 || n.Lane > lanes || n.Def == null) continue;
                if (n.Def.Output == NoteOutput.Sequence) continue;
                if (t < n.SpawnTime) continue;
                if (n.Skipped && !idle) continue;
                Color c = Painter.ColorOf(n.Def);
                float cx = left + laneW * (n.Lane - 0.5f);
                bool resolved = !idle && n.State == SimNoteState.Done;
                double sinceResult = t - n.ResultTime;

                if (n.Def.Stationary)
                {
                    DrawStationary(p, n, t, markers[n.Lane], c, idle, resolved, sinceResult);
                    continue;
                }

                if (idle && t > n.EndTime + 0.3d) continue;
                if (resolved)
                {
                    if (n.Result != Judgement.Miss)
                    {
                        // A short burst ring at the line.
                        if (sinceResult < 0.25d)
                        {
                            float k = (float)(sinceResult / 0.25d);
                            Painter.Ring(p, new Vector2(cx, HitY), r + 14f * k, Palette.WithAlpha(SimulatorView.JudgementColor(n.Result), 1f - k), 3f);
                        }

                        continue;
                    }

                    if (sinceResult > 0.45d) continue;
                    c = Palette.WithAlpha(Palette.Miss, 0.8f * (1f - (float)(sinceResult / 0.45d)));
                }

                double prog = n.Progress(t);
                if (n.State == SimNoteState.Holding && !idle) prog = 1d;
                float y = NoteY(Math.Min(prog, 1.25d));

                if (n.IsHoldType)
                {
                    double tailProg = n.TailProgress(t);
                    float ty = NoteY(Mathf.Clamp((float)tailProg, 0f, 1.25f));
                    if (tailProg < 1.25d && ty < y)
                    {
                        float bw = r * 1.1f;
                        Painter.RoundRect(p, cx - bw * 0.5f, ty, bw, y - ty, bw * 0.5f, Palette.WithAlpha(c, n.State == SimNoteState.Holding ? 0.85f : 0.5f));
                    }
                }

                if (n.Def.Archetype == Archetypes.Mash && n.RequiredPresses > 0 && !resolved)
                {
                    Painter.Circle(p, new Vector2(cx, y), r + 6f, Palette.WithAlpha(c, 0.15f));
                    Painter.ArcProgress(p, new Vector2(cx, y), r + 6f, (float)n.Presses / n.RequiredPresses, c, 3f);
                }

                Painter.Shape(p, n.Def.Shape, new Vector2(cx, y), r, c, Palette.Darken(c, 0.5f), 1.5f);
            }
        }

        private void DrawStationary(Painter2D p, SimNote n, double t, Vector2 marker, Color c, bool idle, bool resolved, double sinceResult)
        {
            if (resolved)
            {
                if (sinceResult < 0.25d)
                {
                    float k = (float)(sinceResult / 0.25d);
                    Color jc = SimulatorView.JudgementColor(n.Result);
                    Painter.Ring(p, marker, 16f + 12f * k, Palette.WithAlpha(jc, 1f - k), 3f);
                }

                if (n.State == SimNoteState.Done) return;
            }

            if (idle && t > n.EndTime + 0.3d) return;
            float progress = Mathf.Clamp01((float)n.Progress(t));
            bool holding = !idle && n.State == SimNoteState.Holding;
            if (holding || (idle && t > n.HitTime && t <= n.EndTime))
            {
                // Holding: the marker stays lit until the end, with a ring showing the time left.
                float left = (float)((n.EndTime - t) / Math.Max(1e-3, n.EndTime - n.HitTime));
                Painter.RoundRect(p, marker.x - 15f, marker.y - 15f, 30f, 30f, 6f, Palette.WithAlpha(c, 0.55f));
                Painter.ArcProgress(p, marker, 21f, Mathf.Clamp01(left), c, 3f);
                return;
            }

            if (t > n.HitTime && !idle) return;
            if (n.Def.Archetype == Archetypes.StationaryHold)
            {
                // Charge fill grows until it fills the marker.
                float s = 15f * progress;
                Painter.RoundRect(p, marker.x - s, marker.y - s, s * 2f, s * 2f, 6f * progress, Palette.WithAlpha(c, 0.85f));
                Painter.RectOutline(p, marker.x - 15f, marker.y - 15f, 30f, 30f, Palette.WithAlpha(c, 0.9f), 2f);
            }
            else
            {
                // Outline shrinks onto the marker (2.2x -> 1x).
                float s = 15f * Mathf.Lerp(2.2f, 1f, progress);
                Painter.RectOutline(p, marker.x - s, marker.y - s, s * 2f, s * 2f, Palette.WithAlpha(c, 0.35f + 0.65f * progress), 2.5f);
                Painter.Shape(p, n.Def.Shape, marker, 5f + 4f * progress, Palette.WithAlpha(c, 0.9f), c, 0f);
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            // Clicking the flow bar seeks the level preview.
            FlowPlan plan = ctx.Preview.Plan;
            if (!plan.IsLevel || evt.localPosition.y > FlowHeight || evt.button != 0) return;
            double start = plan.TimelineStart, dur = Math.Max(1d, plan.Duration - start);
            ctx.Preview.Seek(start + evt.localPosition.x / Mathf.Max(1f, W) * dur);
            evt.StopPropagation();
        }
    }
}
