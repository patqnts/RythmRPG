using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Validation;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>
    /// The combat song: intro / main loop / player turn / end / defeat end files, tempo and offset, volumes and sync.
    /// Mirrors the CombatSong asset the level becomes in the game, including its authoring warnings.
    /// </summary>
    public sealed class MusicPanel : ScrollView
    {
        private static readonly MusicSection[] Sections = { MusicSection.Intro, MusicSection.Loop, MusicSection.PlayerTurn, MusicSection.End, MusicSection.DefeatEnd };

        private static readonly string[] Descriptions =
        {
            "Plays once when the fight starts. The main loop starts on its last sample.",
            "Plays for the whole fight. Every chart's beat 0 lands on one of its bar lines.",
            "A stem of the loop (same length), cross-faded in during the player's turn. Empty = the loop is low-passed.",
            "Plays when the enemy dies, on the next bar. Empty = the music fades out.",
            "Optional different ending when the player loses."
        };

        private readonly ComposerContext ctx;
        private readonly NumberField bpm, beatsPerBar, offset, loopBar, volume, stemVolume, mainOnTurn;
        private readonly ChoiceField chartSync, endSync;
        private readonly CheckField waitForLoop;
        private readonly Dictionary<MusicSection, SectionCard> cards = new Dictionary<MusicSection, SectionCard>();
        private readonly VisualElement warnings;
        private readonly List<double> taps = new List<double>();
        private readonly Button useTap;
        private double tapped;

        public MusicPanel(ComposerContext ctx) : base(ScrollViewMode.Vertical)
        {
            this.ctx = ctx;
            AddToClassList("tab-body");

            Add(Ui.SectionTitle("Tempo"));
            bpm = new NumberField("BPM", null, "Beats per minute of the song. Every chart in the level uses it.");
            bpm.Min = 20; bpm.Max = 400; bpm.Step = 1; bpm.Format = "0.###";
            bpm.Committed += v => ctx.Session.SetTempo(v, ctx.Session.Level.Music.BeatsPerBar);
            VisualElement bpmRow = Ui.Row("field-row");
            bpmRow.Add(bpm);
            bpmRow.Add(Ui.Button("Tap", Tap, "btn--small", "Tap along with the music to measure the BPM"));
            useTap = Ui.Button("", () => { ctx.Session.SetTempo(tapped, ctx.Session.Level.Music.BeatsPerBar); Ui.Show(useTap, false); taps.Clear(); }, "btn--small btn--accent", "Use the tapped tempo");
            Ui.Show(useTap, false);
            bpmRow.Add(useTap);
            Add(bpmRow);
            beatsPerBar = new NumberField("Beats per bar");
            beatsPerBar.Integer = true; beatsPerBar.Min = 1; beatsPerBar.Max = 16;
            beatsPerBar.Committed += v => ctx.Session.SetTempo(ctx.Session.Level.Music.Bpm, (int)v);
            Add(beatsPerBar);
            offset = new NumberField("Offset", "s", "Seconds into the main loop where bar 1 beat 1 falls. Turn on the metronome and nudge until the clicks sit on the beat.");
            offset.Min = 0; offset.Max = 60; offset.Step = 0.005; offset.Format = "0.000";
            offset.Committed += v => ctx.Session.EditLevel("Change offset", l => l.Music.OffsetSeconds = v, ChangeKind.Music);
            VisualElement offsetRow = Ui.Row("field-row");
            offsetRow.Add(offset);
            offsetRow.Add(Ui.Button("-10ms", () => Nudge(-0.01), "btn--small"));
            offsetRow.Add(Ui.Button("+10ms", () => Nudge(0.01), "btn--small"));
            Add(offsetRow);
            loopBar = new NumberField("Preview at bar", null, "Step preview only: which bar of the loop the chart's beat 0 is heard against. In the game the chart starts on the next bar line.");
            loopBar.Integer = true; loopBar.Min = 1; loopBar.Max = 999;
            loopBar.Committed += v => ctx.Session.EditLevel("Change preview bar", l => l.Preview.LoopBar = Math.Max(0, (int)v - 1), ChangeKind.Music);
            Add(loopBar);

            Add(Ui.SectionTitle("Sections"));
            foreach (MusicSection s in Sections)
            {
                var card = new SectionCard(ctx, s, Descriptions[(int)s]);
                cards[s] = card;
                Add(card);
            }

            warnings = Ui.Col("warnings");
            Add(warnings);

            Add(Ui.SectionTitle("Mix"));
            volume = new NumberField("Volume", null, "Song volume (0-1).");
            volume.Min = 0; volume.Max = 1; volume.Step = 0.05; volume.Format = "0.00";
            volume.Committed += v => ctx.Session.EditLevel("Change volume", l => l.Music.Volume = v, ChangeKind.Music);
            Add(volume);
            stemVolume = new NumberField("Player turn stem", null, "Volume of the player-turn stem when it is faded in.");
            stemVolume.Min = 0; stemVolume.Max = 1; stemVolume.Step = 0.05; stemVolume.Format = "0.00";
            stemVolume.Committed += v => ctx.Session.EditLevel("Change stem volume", l => l.Music.PlayerTurnVolume = v, ChangeKind.Music);
            Add(stemVolume);
            mainOnTurn = new NumberField("Loop on player turn", null, "Volume of the main loop while the stem plays (0 = only the stem is heard).");
            mainOnTurn.Min = 0; mainOnTurn.Max = 1; mainOnTurn.Step = 0.05; mainOnTurn.Format = "0.00";
            mainOnTurn.Committed += v => ctx.Session.EditLevel("Change loop volume", l => l.Music.MainVolumeOnPlayerTurn = v, ChangeKind.Music);
            Add(mainOnTurn);

            Add(Ui.SectionTitle("Sync"));
            var syncs = new List<string> { "Immediate", "Next beat", "Next bar" };
            chartSync = new ChoiceField("Charts start on", syncs, 2, "Where each chart's beat 0 may land. Next bar is most musical; next beat makes projectiles come sooner.");
            chartSync.Changed += (i, v) => ctx.Session.EditLevel("Change chart sync", l => l.Music.ChartSync = (MusicSyncMode)i, ChangeKind.Music);
            Add(chartSync);
            endSync = new ChoiceField("End starts on", syncs, 2, "When the ending music starts after the enemy dies.");
            endSync.Changed += (i, v) => ctx.Session.EditLevel("Change end sync", l => l.Music.EndSync = (MusicSyncMode)i, ChangeKind.Music);
            Add(endSync);
            waitForLoop = new CheckField("Charts wait for loop", true, "The first chart's beat 0 lands no earlier than the loop's first downbeat (never during the intro).");
            waitForLoop.Changed += v => ctx.Session.EditLevel("Change wait for loop", l => l.Music.ChartsWaitForLoop = v, ChangeKind.Music);
            Add(waitForLoop);

            ctx.Session.Changed += kind => { if ((kind & (ChangeKind.Music | ChangeKind.File)) != 0) Refresh(); };
            ctx.Library.Loaded += e => Refresh();
            Refresh();
        }

        private void Nudge(double delta)
        {
            double v = Math.Max(0d, ctx.Session.Level.Music.OffsetSeconds + delta);
            ctx.Session.EditLevel("Nudge offset", l => l.Music.OffsetSeconds = Math.Round(v, 4), ChangeKind.Music);
        }

        private void Tap()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (taps.Count > 0 && now - taps[taps.Count - 1] > 2d) taps.Clear();
            taps.Add(now);
            if (taps.Count > 12) taps.RemoveAt(0);
            if (taps.Count < 4)
            {
                ctx.Modal.Toast("Keep tapping... (" + taps.Count + ")");
                return;
            }

            double avg = (taps[taps.Count - 1] - taps[0]) / (taps.Count - 1);
            tapped = Math.Round(60d / avg * 2d) / 2d;
            useTap.text = "Use " + tapped.ToString("0.#");
            Ui.Show(useTap, true);
        }

        public void Refresh()
        {
            MusicSettings m = ctx.Session.Level.Music;
            bpm.SetValue(m.Bpm);
            beatsPerBar.SetValue(m.BeatsPerBar);
            offset.SetValue(m.OffsetSeconds);
            loopBar.SetValue(ctx.Session.Level.Preview.LoopBar + 1);
            volume.SetValue(m.Volume);
            stemVolume.SetValue(m.PlayerTurnVolume);
            mainOnTurn.SetValue(m.MainVolumeOnPlayerTurn);
            chartSync.SetIndex((int)m.ChartSync);
            endSync.SetIndex((int)m.EndSync);
            waitForLoop.SetValue(m.ChartsWaitForLoop);
            foreach (SectionCard c in cards.Values) c.Refresh();
            RefreshWarnings();
        }

        public void SetIssues(List<Issue> issues)
        {
            RefreshWarnings();
        }

        /// <summary>The same checks as the game's CombatSong inspector, on the loaded clips.</summary>
        private void RefreshWarnings()
        {
            warnings.Clear();
            var list = new List<string>();
            MusicSettings m = ctx.Session.Level.Music;
            AudioEntry loop = ctx.Section(MusicSection.Loop), stem = ctx.Section(MusicSection.PlayerTurn), intro = ctx.Section(MusicSection.Intro);
            if (loop != null && loop.Ready)
            {
                AudioClip clip = loop.Clip;
                if (stem != null && stem.Ready)
                {
                    if (stem.Clip.samples != clip.samples || stem.Clip.frequency != clip.frequency)
                        list.Add("Player turn stem (" + stem.Clip.samples + " samples @ " + stem.Clip.frequency + " Hz) is not the same length as the loop (" + clip.samples + " @ " + clip.frequency + " Hz). They drift apart after the first pass.");
                    if (stem.Clip.channels != clip.channels) list.Add("Player turn stem and loop have a different channel count.");
                }

                double bars = (loop.Seconds - m.OffsetSeconds) / m.BarSeconds;
                if (m.OffsetSeconds <= 1e-4 && Math.Abs(bars - Math.Round(bars)) > 0.01)
                    list.Add("The loop is " + bars.ToString("0.##") + " bars long at " + m.Bpm.ToString("0.##") + " BPM, not a whole number of bars. Check the BPM or trim the clip on a bar line.");
                if (intro != null && intro.Ready && intro.Clip.frequency != clip.frequency)
                    list.Add("Intro and loop have a different sample rate.");
            }

            foreach (string w in list) warnings.Add(Ui.Text(w, "warning-line"));
        }

        /// <summary>One music section: file, length, listen / choose / clear.</summary>
        private sealed class SectionCard : VisualElement
        {
            private readonly ComposerContext ctx;
            private readonly MusicSection section;
            private readonly Label file;
            private readonly Label info;
            private readonly Button listen;

            public SectionCard(ComposerContext ctx, MusicSection section, string description)
            {
                this.ctx = ctx;
                this.section = section;
                AddToClassList("section-card");
                AddToClassList("section-card--" + section.ToString().ToLowerInvariant());
                VisualElement head = Ui.Row();
                head.Add(Ui.Div("section-card__dot"));
                head.Add(Ui.Text(LevelValidator.SectionName(section), "section-card__title"));
                head.Add(Ui.Spacer());
                listen = Icon.Button(IconKind.Speaker, Listen, "Listen to this section on its own");
                head.Add(listen);
                head.Add(Icon.Button(IconKind.Folder, Choose, "Choose a WAV / OGG / MP3 file"));
                head.Add(Icon.Button(IconKind.Reload, () => ctx.Library.Reload(ctx.SectionPath(section)), "Reload the file from disk"));
                head.Add(Icon.Button(IconKind.Cross, ClearSection, "Remove this section"));
                Add(head);
                file = Ui.Text("", "section-card__file");
                Add(file);
                info = Ui.Text("", "section-card__info");
                Add(info);
                Tooltips.Set(this, description);
            }

            private void Choose()
            {
                string current = ctx.SectionPath(section);
                string start = !string.IsNullOrEmpty(current) && File.Exists(current) ? Path.GetDirectoryName(current) : Workspace.Audio;
                ctx.Files.Open("Choose " + LevelValidator.SectionName(section) + " music", start, AudioLibrary.Extensions, "audio", path =>
                {
                    string stored = LevelPaths.MakeRelative(path, ctx.Session.FilePath);
                    if (string.IsNullOrEmpty(ctx.Session.FilePath)) stored = path;
                    ctx.Session.EditLevel("Set " + LevelValidator.SectionName(section), l => l.Music.Set(section, stored), ChangeKind.Music);
                    ctx.Library.Get(path);
                    if (section == MusicSection.Loop && ctx.Session.Level.Music.Bpm == 120d)
                        ctx.Modal.Toast("Set the song's BPM in Tempo, then check the offset with the metronome.");
                });
            }

            private void ClearSection()
            {
                if (string.IsNullOrEmpty(ctx.Session.Level.Music.Get(section))) return;
                ctx.Session.EditLevel("Clear " + LevelValidator.SectionName(section), l => l.Music.Set(section, ""), ChangeKind.Music);
            }

            private void Listen()
            {
                AudioEntry e = ctx.Section(section);
                if (e == null || !e.Ready) { ctx.Modal.Toast("Nothing to play.", ToastKind.Warning); return; }
                Playback pb = ctx.Preview.Playback;
                if (pb.ListeningTo == e.Clip) pb.StopListening();
                else pb.Listen(e.Clip);
            }

            public void Refresh()
            {
                string stored = ctx.Session.Level.Music.Get(section);
                if (string.IsNullOrEmpty(stored))
                {
                    file.text = "None";
                    info.text = "";
                    EnableInClassList("section-card--empty", true);
                    listen.SetEnabled(false);
                    return;
                }

                EnableInClassList("section-card--empty", false);
                file.text = Path.GetFileName(stored);
                Tooltips.Set(file, stored);
                AudioEntry e = ctx.Section(section);
                if (e == null) { info.text = ""; return; }
                if (e.Loading) info.text = "Loading...";
                else if (e.Error != null) info.text = "<color=#FF5C6C>" + e.Error + "</color>";
                else if (e.Clip != null)
                {
                    double bars = e.Seconds / Math.Max(1e-6, ctx.Session.Level.Music.BarSeconds);
                    info.text = e.Seconds.ToString("0.00") + " s  ·  " + bars.ToString("0.##") + " bars  ·  " + e.Clip.frequency + " Hz  ·  " + (e.Clip.channels == 1 ? "mono" : "stereo")
                        + (stored.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? "  ·  <color=#FFB547>MP3</color>" : "");
                }

                listen.SetEnabled(e.Ready);
            }
        }
    }
}
