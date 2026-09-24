using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Types;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>
    /// The strip above the timeline: tools, every registered note type (grouped by category, keys 1-9), snap, zoom and
    /// the pattern generator. New note types from JSON files appear here automatically.
    /// </summary>
    public sealed class PaletteBar : VisualElement
    {
        public static readonly int[] SnapChoices = { 1, 2, 3, 4, 6, 8, 12, 16 };

        private readonly ComposerContext ctx;
        private readonly VisualElement typeRow;
        private readonly ToggleButton selectTool, drawTool;
        private readonly ChoiceField snap;
        private readonly Dictionary<string, Button> chips = new Dictionary<string, Button>();
        private readonly List<string> order = new List<string>();

        public TimelineView Timeline;

        public PaletteBar(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("palette");

            VisualElement tools = Ui.Row("segmented");
            selectTool = new ToggleButton("Select", false, "Select tool (V): drag boxes, move notes. Shift+drag also selects in Draw mode.");
            drawTool = new ToggleButton("Draw", true, "Draw tool (B): click a lane to place the armed note type.");
            selectTool.Toggled += on => ctx.SetTool(TimelineTool.Select);
            drawTool.Toggled += on => ctx.SetTool(TimelineTool.Draw);
            tools.Add(selectTool);
            tools.Add(drawTool);
            Add(tools);
            Add(Ui.Separator());

            typeRow = Ui.Row("palette__types");
            Add(typeRow);
            Add(Ui.Spacer());

            var snapLabels = new List<string>();
            foreach (int d in SnapChoices) snapLabels.Add("1/" + d);
            snapLabels.Add("Off");
            snap = new ChoiceField("Snap", snapLabels, 3, "Grid for placing and moving notes, in beats. [ and ] change it; Off places freely.");
            snap.AddToClassList("palette__snap");
            snap.Changed += (i, v) =>
            {
                if (i >= SnapChoices.Length) ctx.Session.SnapEnabled = false;
                else
                {
                    ctx.Session.SnapEnabled = true;
                    ctx.Session.SnapDivision = SnapChoices[i];
                }

                if (Timeline != null) Timeline.Refresh();
            };
            Add(snap);
            Add(Icon.Button(IconKind.Minus, () => { if (Timeline != null) Timeline.ZoomAtCenter(1f / 1.25f); }, "Zoom out (-)"));
            Add(Icon.Button(IconKind.Plus, () => { if (Timeline != null) Timeline.ZoomAtCenter(1.25f); }, "Zoom in (+)"));
            Add(Icon.Button(IconKind.Fit, () => { if (Timeline != null) Timeline.Fit(); }, "Fit the step in view (Z)"));
            Add(Ui.Separator());
            Add(Ui.Button("Pattern...", () => PatternDialog.Show(ctx), "btn--ghost", "Generate notes from a pattern (sweep, stairs, stream...) at the playhead"));

            ctx.Types.Changed += Rebuild;
            ctx.BrushChanged += RefreshState;
            Rebuild();
        }

        /// <summary>Note types in palette order (keys 1-9 pick the first nine).</summary>
        public IList<string> Order { get { return order; } }

        public void Rebuild()
        {
            typeRow.Clear();
            chips.Clear();
            order.Clear();
            int key = 1;
            foreach (string category in ctx.Types.Categories())
            {
                VisualElement group = Ui.Row("palette__group");
                group.Add(Ui.Text(category.ToUpperInvariant(), "palette__category"));
                foreach (NoteTypeDef def in ctx.Types.InCategory(category))
                {
                    string id = def.Id;
                    order.Add(id);
                    Button chip = Ui.Button("", () => ctx.SetBrush(id), "chip");
                    var swatch = new NoteSwatch(def);
                    chip.Add(swatch);
                    chip.Add(Ui.Text(def.Name, "chip__name"));
                    if (key <= 9) chip.Add(Ui.Text(key.ToString(), "chip__key"));
                    Tooltips.Set(chip, def.Name + (key <= 9 ? "  (" + key + ")" : "") + "\n" + def.Description);
                    chips[id] = chip;
                    group.Add(chip);
                    key++;
                }

                typeRow.Add(group);
            }

            RefreshState();
        }

        public void RefreshState()
        {
            foreach (KeyValuePair<string, Button> kv in chips) kv.Value.EnableInClassList("chip--armed", ctx.Tool == TimelineTool.Draw && kv.Key == ctx.BrushType);
            selectTool.SetOn(ctx.Tool == TimelineTool.Select, false);
            drawTool.SetOn(ctx.Tool == TimelineTool.Draw, false);
            int index = ctx.Session.SnapEnabled ? Array.IndexOf(SnapChoices, ctx.Session.SnapDivision) : SnapChoices.Length;
            snap.SetIndex(index < 0 ? 3 : index);
        }

        /// <summary>[ / ] shortcuts.</summary>
        public void StepSnap(int direction)
        {
            int index = Array.IndexOf(SnapChoices, ctx.Session.SnapDivision);
            if (index < 0) index = 3;
            index = Mathf.Clamp(index + direction, 0, SnapChoices.Length - 1);
            ctx.Session.SnapEnabled = true;
            ctx.Session.SnapDivision = SnapChoices[index];
            RefreshState();
            if (Timeline != null) Timeline.Refresh();
            ctx.Modal.Toast("Snap 1/" + SnapChoices[index] + " beat");
        }
    }

    /// <summary>The note glyph in its colour, for palette chips and the inspector.</summary>
    public sealed class NoteSwatch : VisualElement
    {
        private NoteTypeDef def;

        public NoteSwatch(NoteTypeDef def)
        {
            this.def = def;
            AddToClassList("swatch");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += mgc =>
            {
                if (this.def == null) return;
                float s = Mathf.Min(contentRect.width, contentRect.height);
                if (s <= 0f || float.IsNaN(s)) return;
                Color c = Painter.ColorOf(this.def);
                Painter.Shape(mgc.painter2D, this.def.Shape, contentRect.center, s * 0.36f, c, Palette.Darken(c, 0.45f), 1f);
            };
        }

        public void SetType(NoteTypeDef d)
        {
            def = d;
            MarkDirtyRepaint();
        }
    }

    /// <summary>Generates notes from a pattern and inserts them at the playhead (or after the selection).</summary>
    public static class PatternDialog
    {
        private static string lastPattern = "sweep";
        private static double lastLength = 4d;
        private static double lastSpacing;
        private static int lastSeed = 1;

        public static void Show(ComposerContext ctx)
        {
            VisualElement panel = Ui.Col("dialog");
            panel.Add(Ui.Text("Insert pattern", "dialog__title"));

            var names = new List<string>();
            int index = 0;
            for (int i = 0; i < PatternStamps.All.Count; i++)
            {
                names.Add(PatternStamps.All[i].Name + "  -  " + PatternStamps.All[i].Description);
                if (PatternStamps.All[i].Id == lastPattern) index = i;
            }

            var pattern = new ChoiceField("Pattern", names, index);
            panel.Add(pattern);

            var typeNames = new List<string>();
            var typeIds = new List<string>();
            foreach (NoteTypeDef d in ctx.Types.All)
            {
                if (d.Hidden || d.Output == NoteOutput.Sequence) continue;
                typeIds.Add(d.Id);
                typeNames.Add(d.Name);
            }

            var type = new ChoiceField("Note type", typeNames, Math.Max(0, typeIds.IndexOf(ctx.BrushType)));
            panel.Add(type);
            var length = new NumberField("Length", "beats");
            length.Min = 0.25; length.Max = 256; length.Step = 1; length.SetValue(lastLength);
            panel.Add(length);
            var spacing = new NumberField("Spacing", "beats", "Beats between notes. 0 = the pattern's own spacing.");
            spacing.Min = 0; spacing.Max = 16; spacing.Step = 0.25; spacing.SetValue(lastSpacing);
            panel.Add(spacing);
            var seed = new NumberField("Seed", null, "Random patterns: another seed gives another variation.");
            seed.Integer = true; seed.Min = 1; seed.Max = 99999; seed.SetValue(lastSeed);
            panel.Add(seed);

            double at = Math.Max(0d, ctx.Session.Snap(ctx.Preview.PlayheadBeat));
            List<LevelNote> sel = ctx.Session.SelectedNotes();
            if (sel.Count > 0)
            {
                double end = 0d;
                foreach (LevelNote n in sel) end = Math.Max(end, n.EndBeat);
                at = ctx.Session.Snap(end + 1d / Math.Max(1, ctx.Session.SnapDivision));
            }

            var start = new NumberField("Start beat", null, "Where the first note goes (defaults to the playhead, or right after the selection).");
            start.Min = 0; start.Max = 100000; start.Step = 1; start.SetValue(at);
            panel.Add(start);

            VisualElement buttons = Ui.Row("dialog__buttons");
            buttons.Add(Ui.Spacer());
            Action insert = () =>
            {
                int pi = Math.Max(0, pattern.Index);
                int ti = Math.Max(0, type.Index);
                lastPattern = PatternStamps.All[pi].Id;
                lastLength = length.Value;
                lastSpacing = spacing.Value;
                lastSeed = (int)seed.Value;
                var request = new StampRequest
                {
                    PatternId = lastPattern,
                    NoteType = typeIds.Count > 0 ? typeIds[ti] : BuiltInNoteTypes.Normal,
                    StartBeat = start.Value,
                    LengthBeats = lastLength,
                    Spacing = lastSpacing,
                    LaneCount = ctx.Session.Step.LaneCount,
                    Seed = lastSeed
                };
                List<LevelNote> notes = PatternStamps.Generate(request);
                NoteTypeDef def = ctx.Types.Find(request.NoteType);
                if (def != null && def.HasLength)
                    foreach (LevelNote n in notes) n.Length = Math.Max(0.25d, Math.Min(def.DefaultLengthBeats, request.Spacing > 0 ? request.Spacing : 0.5d));
                ctx.Modal.Close();
                ctx.Session.AddNotes(notes, "Insert " + PatternStamps.All[pi].Name);
                ctx.Modal.Toast("Inserted " + notes.Count + " notes (selected).", ToastKind.Success);
            };
            buttons.Add(Ui.Button("Cancel", ctx.Modal.Close));
            buttons.Add(Ui.Button("Insert", insert, "btn--accent"));
            panel.Add(buttons);
            ctx.Modal.Open(panel, ctx.Modal.Close, insert);
        }
    }
}
