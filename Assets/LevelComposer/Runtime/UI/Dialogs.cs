using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Types;
using RythmRPG.LevelComposer.Validation;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>Bottom bar: last message, counts and problem summary.</summary>
    public sealed class StatusBar : VisualElement
    {
        private readonly ComposerContext ctx;
        private readonly Label message;
        private readonly Label counts;
        private readonly Label problems;

        public StatusBar(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("statusbar");
            message = Ui.Text("Ready", "statusbar__message grow");
            counts = Ui.Text("", "statusbar__counts");
            problems = Ui.Text("", "statusbar__problems");
            Add(message);
            Add(counts);
            Add(problems);
            ctx.Session.Changed += kind => RefreshCounts();
            RefreshCounts();
        }

        public void Say(string text) { message.text = text; }

        public void SetIssues(List<Issue> issues)
        {
            int e = 0, w = 0;
            foreach (Issue i in issues)
            {
                if (i.Severity == Severity.Error) e++;
                else if (i.Severity == Severity.Warning) w++;
            }

            problems.text = e == 0 && w == 0 ? "<color=#3DDC97>No problems</color>" : "<color=#FF5C6C>" + e + " errors</color>  <color=#FFB547>" + w + " warnings</color>";
        }

        private void RefreshCounts()
        {
            LevelEditSession s = ctx.Session;
            counts.text = "Step " + (s.StepIndex + 1) + "/" + s.Level.Steps.Count + "  ·  " + s.Step.Notes.Count + " notes" + (s.SelectionCount > 0 ? "  ·  " + s.SelectionCount + " selected" : "")
                          + "  ·  " + s.Level.Music.Bpm.ToString("0.##") + " BPM " + s.Level.Music.BeatsPerBar + "/4";
        }
    }

    /// <summary>Settings and help dialogs.</summary>
    public static class Dialogs
    {
        public static void Settings(ComposerContext ctx)
        {
            ComposerPrefs p = ctx.Prefs;
            VisualElement panel = Ui.Col("dialog dialog--wide");
            panel.Add(Ui.Text("Settings", "dialog__title"));
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("dialog__scroll");
            panel.Add(scroll);

            scroll.Add(Ui.SectionTitle("Audio"));
            scroll.Add(Slider("Music volume", p.MusicVolume, v => p.MusicVolume = v, ctx));
            scroll.Add(Slider("Hit sounds", p.HitSoundVolume, v => p.HitSoundVolume = v, ctx));
            scroll.Add(Slider("Metronome", p.MetronomeVolume, v => p.MetronomeVolume = v, ctx));
            var offset = new NumberField("Input offset", "ms", "Added to your key presses in the preview. If you hit late on the beat, use a negative value (Bluetooth headphones: about -150).");
            offset.Min = -400; offset.Max = 400; offset.Step = 5; offset.Format = "0";
            offset.SetValue(p.InputOffsetMs);
            offset.Committed += v => { p.InputOffsetMs = (float)v; ctx.NotifyPrefsChanged(); };
            scroll.Add(offset);

            scroll.Add(Ui.SectionTitle("Timing windows (moving notes)"));
            scroll.Add(Ui.Text("Milliseconds ± from the beat, like the game's JudgementConfig (defaults 45 / 90 / 125 / 180). Stationary notes use their own windows.", "muted hint"));
            scroll.Add(Window("Perfect", p.Windows.Perfect, v => p.Windows.Perfect = v, ctx));
            scroll.Add(Window("Good", p.Windows.Good, v => p.Windows.Good = v, ctx));
            scroll.Add(Window("Bad", p.Windows.Bad, v => p.Windows.Bad = v, ctx));
            scroll.Add(Window("Miss", p.Windows.Miss, v => p.Windows.Miss = v, ctx));

            scroll.Add(Ui.SectionTitle("Interface"));
            var scale = new NumberField("UI scale", "x", "Makes the whole interface bigger or smaller.");
            scale.Min = 0.75; scale.Max = 1.5; scale.Step = 0.05; scale.Format = "0.00";
            scale.SetValue(p.UiScale);
            scale.Committed += v => { p.UiScale = (float)v; ctx.NotifyPrefsChanged(); ctx.App.ApplyScale(); };
            scroll.Add(scale);

            scroll.Add(Ui.SectionTitle("Note types"));
            var list = Ui.Col("card");
            foreach (NoteTypeDef d in ctx.Types.All)
            {
                VisualElement row = Ui.Row("type-count");
                row.Add(new NoteSwatch(d));
                row.Add(Ui.Text(d.Name + "  <color=#8E96A8>" + d.Id + " · " + d.Archetype + (d.Source == "built-in" ? "" : " · " + System.IO.Path.GetFileName(d.Source)) + "</color>", "grow"));
                list.Add(row);
            }

            scroll.Add(list);
            foreach (string m in ctx.Types.LoadMessages) scroll.Add(Ui.Text(m, "warning-line"));
            VisualElement typeButtons = Ui.Row("button-row");
            typeButtons.Add(Ui.Button("Reload note types", () => { ctx.App.ReloadNoteTypes(); ctx.Modal.Close(); }, "btn--small"));
            typeButtons.Add(Ui.Button("Write example gimmick file", () => ctx.App.WriteExampleNoteType(), "btn--small btn--ghost",
                "Writes an example note-type JSON into your workspace's NoteTypes folder to copy from."));
            typeButtons.Add(Ui.Button("Open workspace folder", () => Application.OpenURL(new Uri(Workspace.Root).AbsoluteUri), "btn--small btn--ghost"));
            scroll.Add(typeButtons);
            scroll.Add(Ui.Text("Workspace: " + Workspace.Root, "muted hint"));

            VisualElement buttons = Ui.Row("dialog__buttons");
            buttons.Add(Ui.Spacer());
            buttons.Add(Ui.Button("Done", ctx.Modal.Close, "btn--accent"));
            panel.Add(buttons);
            ctx.Modal.Open(panel, ctx.Modal.Close, ctx.Modal.Close);
        }

        private static VisualElement Slider(string label, float value, Action<float> set, ComposerContext ctx)
        {
            VisualElement row = Ui.Row("field");
            row.Add(Ui.Text(label, "field__label"));
            var s = new Slider(0f, 1f) { value = value };
            s.focusable = false;
            s.AddToClassList("grow");
            s.RegisterValueChangedCallback(evt => { set(evt.newValue); ctx.NotifyPrefsChanged(); });
            row.Add(s);
            return row;
        }

        private static NumberField Window(string label, double seconds, Action<double> set, ComposerContext ctx)
        {
            var f = new NumberField(label, "ms");
            f.Min = 1; f.Max = 400; f.Step = 5; f.Format = "0";
            f.SetValue(seconds * 1000d);
            f.Committed += v =>
            {
                set(v / 1000d);
                ctx.Prefs.Windows.Sanitize();
                ctx.NotifyPrefsChanged();
                ctx.Preview.MarkDirty();
            };
            return f;
        }

        public static void Help(ComposerContext ctx)
        {
            VisualElement panel = Ui.Col("dialog dialog--wide");
            panel.Add(Ui.Text("How to compose a combat level", "dialog__title"));
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("dialog__scroll");
            panel.Add(scroll);
            scroll.Add(Ui.Text(
                "1.  Music tab: choose the Main loop (and optional Intro, Player turn stem, End). Set the BPM, turn on the metronome, play, and nudge the Offset until the clicks sit on the beat.\n" +
                "2.  Steps tab: one step per enemy attack. Give it the enemy animation, wind-up time and lane count.\n" +
                "3.  Pick a note type in the palette (or keys 1-9) and click in a lane. Holds: drag while placing, or drag the bar's end.\n" +
                "4.  Select notes to edit their properties on the right. Drag the dot at the start of a selected note's dashed line to change its travel time.\n" +
                "5.  Space plays the step. Auto-hit shows the perfect run; turn it off (T) and play with A S J K. Switch to Level (L) to hear the whole fight.\n" +
                "6.  Fix anything in Problems, save, and import the .combatlevel.json in Unity (Tools > Rythm RPG > Level Composer).", "help-text"));
            scroll.Add(Ui.SectionTitle("Shortcuts"));
            string[,] keys =
            {
                { "Space", "Play / pause" }, { "Esc", "Stop (back to where playback started) / clear selection" }, { "Home", "Back to start" },
                { "L", "Step / Level preview" }, { "T", "Auto-hit on / off" }, { "M", "Metronome" }, { "F", "Follow playhead" }, { "P", "Preview panel" },
                { "1 - 9", "Arm a note type" }, { "B / V", "Draw / Select tool" }, { "[  ]", "Snap finer / coarser" }, { "+  -  Z", "Zoom in / out / fit" },
                { "Click", "Place a note (Draw) · select" }, { "Shift+drag", "Box select" }, { "Ctrl+click", "Add to selection" }, { "Double-click note", "Select all of its type" },
                { "Right-click", "Delete a note" }, { "Middle-drag / Alt+drag", "Pan" }, { "Wheel / Ctrl+wheel", "Scroll / zoom" },
                { "Arrows", "Move selection by the snap / by a lane (no selection: move the playhead)" }, { "Q / X", "Quantize / mirror selection" },
                { "Ctrl+C X V D", "Copy, cut, paste at playhead, duplicate" }, { "Ctrl+A", "Select all" }, { "Del", "Delete selection" },
                { "Ctrl+Z / Ctrl+Y", "Undo / redo" }, { "Ctrl+N O S", "New, open, save  (Ctrl+Shift+S: save as)" }, { "A S J K", "Play the lanes while previewing (keys follow the lane count like the game)" }
            };
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                VisualElement row = Ui.Row("shortcut");
                row.Add(Ui.Text(keys[i, 0], "shortcut__key"));
                row.Add(Ui.Text(keys[i, 1], "shortcut__text"));
                scroll.Add(row);
            }

            scroll.Add(Ui.SectionTitle("New note gimmicks"));
            scroll.Add(Ui.Text("Note types are data. Drop a note-type .json into the workspace NoteTypes folder (or the game's StreamingAssets/ComposerNoteTypes) and it appears in the palette with its own properties, preview behaviour and import mapping. Settings > Write example gimmick file shows the format.", "help-text"));
            VisualElement buttons = Ui.Row("dialog__buttons");
            buttons.Add(Ui.Spacer());
            buttons.Add(Ui.Button("Close", ctx.Modal.Close, "btn--accent"));
            panel.Add(buttons);
            ctx.Modal.Open(panel, ctx.Modal.Close, ctx.Modal.Close);
        }
    }
}
