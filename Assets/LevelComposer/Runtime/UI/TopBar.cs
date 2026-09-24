using System;
using RythmRPG.LevelComposer.Editing;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>File actions, level name, transport (play / stop / mode) and preview toggles.</summary>
    public sealed class TopBar : VisualElement
    {
        private readonly ComposerContext ctx;
        private readonly Label title;
        private readonly Label dirtyDot;
        private readonly Button playButton;
        private readonly Icon playIcon;
        private readonly Label timeLabel;
        private readonly ToggleButton stepMode, levelMode, autoHit, metronome, hitSounds, previewPanel, follow;
        private readonly Button undoButton, redoButton;

        public event Action TogglePreviewPanel;

        public TopBar(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("topbar");

            VisualElement brand = Ui.Row("brand");
            brand.Add(Ui.Div("brand__mark"));
            brand.Add(Ui.Text("Level Composer", "brand__text"));
            Add(brand);

            Add(Icon.Button(IconKind.File, () => ctx.App.NewLevel(), "New level (Ctrl+N)", "New"));
            Add(Icon.Button(IconKind.Folder, () => ctx.App.OpenLevel(), "Open a .combatlevel.json (Ctrl+O)", "Open"));
            Add(Icon.Button(IconKind.Save, () => ctx.App.Save(), "Save (Ctrl+S)", "Save"));
            Add(Ui.Button("Save As", () => ctx.App.SaveAs(), "btn--ghost", "Save to a new file (Ctrl+Shift+S)"));
            Add(Ui.Separator());
            undoButton = Ui.Button("Undo", () => ctx.Session.Undo(), "btn--ghost", "Undo (Ctrl+Z)");
            redoButton = Ui.Button("Redo", () => ctx.Session.Redo(), "btn--ghost", "Redo (Ctrl+Y)");
            Add(undoButton);
            Add(redoButton);
            Add(Ui.Separator());

            VisualElement name = Ui.Row("topbar__title");
            title = Ui.Text("", "topbar__level");
            title.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                    ctx.Modal.Prompt("Level name", "Name", ctx.Session.Level.Name, v =>
                    {
                        if (!string.IsNullOrEmpty(v)) ctx.Session.EditLevel("Rename level", l => l.Name = v.Trim());
                    });
            });
            Tooltips.Set(title, "Level name (double-click to rename). Change the id and more in the Level tab.");
            dirtyDot = Ui.Text("  unsaved", "topbar__dirty");
            name.Add(title);
            name.Add(dirtyDot);
            Add(name);

            Add(Ui.Spacer());

            // Transport.
            VisualElement transport = Ui.Row("transport");
            transport.Add(Icon.Button(IconKind.ToStart, () => ctx.Preview.Seek(ctx.Preview.StartTime()), "Back to start (Home)"));
            playButton = Ui.Button("", () => ctx.Preview.TogglePlay(), "btn--icon btn--play", "Play / pause (Space)");
            playIcon = new Icon(IconKind.Play, 16f);
            playButton.Add(playIcon);
            transport.Add(playButton);
            transport.Add(Icon.Button(IconKind.Stop, () => ctx.Preview.Stop(), "Stop and return (Esc)"));
            timeLabel = Ui.Text("0:00.00", "transport__time");
            transport.Add(timeLabel);
            Add(transport);

            VisualElement modes = Ui.Row("segmented");
            stepMode = new ToggleButton("Step", true, "Preview the selected step over the main loop (L to switch)");
            levelMode = new ToggleButton("Level", false, "Preview the whole fight: intro, every step with player turns between, end (L to switch)");
            stepMode.Toggled += on => ctx.Preview.SetMode(PreviewMode.Step);
            levelMode.Toggled += on => ctx.Preview.SetMode(PreviewMode.Level);
            modes.Add(stepMode);
            modes.Add(levelMode);
            Add(modes);

            Add(Ui.Separator());
            autoHit = new ToggleButton("Auto-hit", ctx.Prefs.AutoHit, "Plays every note perfectly (T). Off: play with A S J K like in the game.");
            autoHit.Toggled += on => { ctx.Prefs.AutoHit = on; ctx.NotifyPrefsChanged(); };
            metronome = new ToggleButton("Metronome", ctx.Prefs.Metronome, "Click on every beat (M)");
            metronome.Toggled += on => { ctx.Prefs.Metronome = on; ctx.NotifyPrefsChanged(); };
            hitSounds = new ToggleButton("Hit sounds", ctx.Prefs.HitSounds, "A tick for every judged note");
            hitSounds.Toggled += on => { ctx.Prefs.HitSounds = on; ctx.NotifyPrefsChanged(); };
            follow = new ToggleButton("Follow", ctx.Prefs.FollowPlayhead, "Scroll the timeline with the playhead (F)");
            follow.Toggled += on => { ctx.Prefs.FollowPlayhead = on; ctx.NotifyPrefsChanged(); };
            Add(autoHit);
            Add(metronome);
            Add(hitSounds);
            Add(follow);
            Add(Ui.Separator());
            previewPanel = new ToggleButton("Preview", ctx.Prefs.ShowPreview, "Show / hide the combat preview panel (P)");
            previewPanel.Toggled += on => { ctx.Prefs.ShowPreview = on; ctx.NotifyPrefsChanged(); if (TogglePreviewPanel != null) TogglePreviewPanel(); };
            Add(previewPanel);
            Add(Icon.Button(IconKind.Gear, () => ctx.App.ShowSettings(), "Settings: volumes, timing windows, input offset, note types"));
            Add(Icon.Button(IconKind.Help, () => ctx.App.ShowHelp(), "Shortcuts and how-to (F1)"));

            ctx.Session.Changed += kind => RefreshFile();
            ctx.Preview.ModeChanged += RefreshToggles;
            ctx.Preview.PlayStateChanged += RefreshToggles;
            ctx.PrefsChanged += RefreshToggles;
            RefreshFile();
            RefreshToggles();
        }

        public void RefreshFile()
        {
            LevelEditSession s = ctx.Session;
            string file = string.IsNullOrEmpty(s.FilePath) ? "not saved yet" : System.IO.Path.GetFileName(s.FilePath);
            title.text = s.Level.Name + "  <color=#8E96A8><size=11>" + file + "</size></color>";
            dirtyDot.style.display = s.Dirty ? DisplayStyle.Flex : DisplayStyle.None;
            undoButton.SetEnabled(s.CanUndo);
            redoButton.SetEnabled(s.CanRedo);
            Tooltips.Set(undoButton, s.CanUndo ? "Undo " + s.UndoLabel + " (Ctrl+Z)" : "Nothing to undo");
            Tooltips.Set(redoButton, s.CanRedo ? "Redo " + s.RedoLabel + " (Ctrl+Y)" : "Nothing to redo");
        }

        public void RefreshToggles()
        {
            bool level = ctx.Preview.Mode == PreviewMode.Level;
            stepMode.SetOn(!level, false);
            levelMode.SetOn(level, false);
            autoHit.SetOn(ctx.Prefs.AutoHit, false);
            metronome.SetOn(ctx.Prefs.Metronome, false);
            hitSounds.SetOn(ctx.Prefs.HitSounds, false);
            follow.SetOn(ctx.Prefs.FollowPlayhead, false);
            previewPanel.SetOn(ctx.Prefs.ShowPreview, false);
            playIcon.Kind = ctx.Preview.IsPlaying ? IconKind.Pause : IconKind.Play;
            playButton.EnableInClassList("on", ctx.Preview.IsPlaying);
        }

        /// <summary>Per frame: the time readout.</summary>
        public void Tick()
        {
            double t = ctx.Preview.Time;
            double beat = ctx.Preview.PlayheadBeat;
            int bpb = ctx.Session.Tempo.BeatsPerBar;
            string pos = beat >= 0 ? Model.CombatLevel.FormatBeat(beat, bpb) : "lead-in";
            string sign = t < 0 ? "-" : "";
            double a = Math.Abs(t);
            int m = (int)(a / 60d);
            timeLabel.text = sign + m + ":" + (a - m * 60).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture) + "  <color=#8E96A8>" + pos + "</color>";
            bool playing = ctx.Preview.IsPlaying;
            if ((playIcon.Kind == IconKind.Pause) != playing) RefreshToggles();
        }
    }
}
