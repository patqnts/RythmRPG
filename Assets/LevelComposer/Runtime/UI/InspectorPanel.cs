using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
    /// Right side: properties of the selected notes, generated from their note types' parameter lists (so new gimmicks
    /// get an inspector for free), and the Problems list from validation.
    /// </summary>
    public sealed class InspectorPanel : VisualElement
    {
        private readonly ComposerContext ctx;
        private readonly ScrollView body;
        private readonly VisualElement problemsHeader;
        private readonly Label problemsTitle;
        private readonly ScrollView problemsList;
        private readonly List<Action> refreshers = new List<Action>();
        private string structureKey = "";
        private List<Issue> issues = new List<Issue>();
        private bool advancedOpen;

        public InspectorPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("panel");
            AddToClassList("inspector");
            body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("inspector__body");
            Add(body);

            problemsHeader = Ui.Row("problems__header");
            problemsTitle = Ui.Text("Problems", "section-title");
            problemsHeader.Add(problemsTitle);
            Add(problemsHeader);
            problemsList = new ScrollView(ScrollViewMode.Vertical);
            problemsList.AddToClassList("problems");
            Add(problemsList);

            ctx.Session.Changed += OnChanged;
            ctx.Types.Changed += () => { structureKey = ""; OnChanged(ChangeKind.All); };
            Rebuild();
        }

        private void OnChanged(ChangeKind kind)
        {
            string key = StructureKey();
            if (key != structureKey) Rebuild();
            else foreach (Action r in refreshers) r();
        }

        private string StructureKey()
        {
            var sb = new StringBuilder();
            sb.Append(ctx.Session.StepIndex).Append('|');
            foreach (LevelNote n in ctx.Session.SelectedNotes()) sb.Append(n.Id).Append(':').Append(n.Type).Append(',');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ building

        private void Rebuild()
        {
            structureKey = StructureKey();
            body.Clear();
            refreshers.Clear();
            List<LevelNote> sel = ctx.Session.SelectedNotes();
            if (sel.Count == 0) BuildEmpty();
            else BuildSelection(sel);
            foreach (Action r in refreshers) r();
        }

        private void BuildEmpty()
        {
            body.Add(Ui.SectionTitle("Inspector"));
            body.Add(Ui.Text("Select notes to edit them. Drag a box with Shift (or the Select tool) to pick several.", "muted hint"));
            LevelStep step = ctx.Session.Step;
            var counts = new Dictionary<string, int>();
            foreach (LevelNote n in step.Notes)
            {
                int c;
                counts.TryGetValue(n.Type, out c);
                counts[n.Type] = c + 1;
            }

            VisualElement card = Ui.Col("card");
            card.Add(Ui.Text((ctx.Session.StepIndex + 1) + ". " + step.Name, "card__title"));
            foreach (KeyValuePair<string, int> kv in counts)
            {
                NoteTypeDef def = ctx.Types.Find(kv.Key);
                VisualElement row = Ui.Row("type-count");
                if (def != null) row.Add(new NoteSwatch(def));
                row.Add(Ui.Text((def != null ? def.Name : kv.Key + " (unknown)") + "  ×" + kv.Value, "grow"));
                string id = kv.Key;
                row.Add(Ui.Button("Select", () => ctx.Session.SelectOfType(id), "btn--small btn--ghost"));
                card.Add(row);
            }

            if (counts.Count == 0) card.Add(Ui.Text("No notes yet.", "muted"));
            body.Add(card);

            NoteTypeDef brush = ctx.Types.Find(ctx.BrushType);
            if (brush != null)
            {
                VisualElement b = Ui.Col("card");
                VisualElement head = Ui.Row();
                head.Add(new NoteSwatch(brush));
                head.Add(Ui.Text("Drawing: " + brush.Name, "card__title"));
                b.Add(head);
                b.Add(Ui.Text(brush.Description, "muted hint"));
                body.Add(b);
            }
        }

        private void BuildSelection(List<LevelNote> sel)
        {
            LevelTempo tempo = ctx.Session.Tempo;
            var defs = new List<NoteTypeDef>();
            bool allKnown = true;
            foreach (LevelNote n in sel)
            {
                NoteTypeDef d = ctx.Types.Find(n.Type);
                if (d == null) { allKnown = false; continue; }
                if (!defs.Contains(d)) defs.Add(d);
            }

            string title = sel.Count == 1 ? (defs.Count == 1 ? defs[0].Name : sel[0].Type) : sel.Count + " notes";
            VisualElement head = Ui.Row("inspector__head");
            if (defs.Count == 1) head.Add(new NoteSwatch(defs[0]));
            head.Add(Ui.Text(title, "inspector__title"));
            body.Add(head);
            if (defs.Count == 1 && !string.IsNullOrEmpty(defs[0].Description)) body.Add(Ui.Text(defs[0].Description, "muted hint"));
            if (!allKnown) body.Add(Ui.Text("Some selected notes use a note type this app does not know. Add its note-type file or change the type.", "warning-line"));

            // Type.
            var typeIds = new List<string>();
            var typeNames = new List<string>();
            foreach (NoteTypeDef d in ctx.Types.All)
            {
                if (d.Hidden) continue;
                typeIds.Add(d.Id);
                typeNames.Add(d.Category + " / " + d.Name);
            }

            var type = new ChoiceField("Type", typeNames, 0);
            type.Changed += (i, v) => ctx.Session.SetTypeOfSelected(typeIds[i]);
            body.Add(type);
            refreshers.Add(() =>
            {
                List<LevelNote> s = ctx.Session.SelectedNotes();
                string common = s.Count > 0 ? s[0].Type : null;
                foreach (LevelNote n in s) if (n.Type != common) common = null;
                type.SetIndex(common != null ? typeIds.IndexOf(common) : -1);
            });

            // Position.
            var beat = new NumberField(sel.Count == 1 ? "Beat" : "Starts at", "beat", "Hit beat (0 = the chart's first beat, which the game lands on a bar line of the loop). With several notes: moves them together.");
            beat.Min = 0; beat.Max = 100000; beat.Step = 1d / Math.Max(1, ctx.Session.SnapDivision); beat.Format = "0.####";
            beat.Committed += v =>
            {
                double min = double.MaxValue;
                foreach (LevelNote n in ctx.Session.SelectedNotes()) min = Math.Min(min, n.Beat);
                double delta = v - min;
                ctx.Session.ModifySelected("Change beat", (n, d) => n.Beat += delta);
            };
            body.Add(beat);
            var barLabel = Ui.Text("", "muted field-note");
            body.Add(barLabel);
            refreshers.Add(() =>
            {
                double min = double.MaxValue;
                foreach (LevelNote n in ctx.Session.SelectedNotes()) min = Math.Min(min, n.Beat);
                if (min == double.MaxValue) return;
                beat.SetValue(min);
                barLabel.text = "Bar " + CombatLevel.FormatBeat(min, tempo.BeatsPerBar) + "  ·  " + tempo.BeatToSeconds(min).ToString("0.000") + " s after beat 0";
            });

            var lane = new NumberField("Lane", null, "1 = leftmost lane. Keys: " + KeyList(ctx.Session.Step.LaneCount));
            lane.Integer = true; lane.Min = 1; lane.Max = ctx.Session.Step.LaneCount;
            lane.Committed += v =>
            {
                int target = (int)v;
                int first = int.MaxValue;
                foreach (LevelNote n in ctx.Session.SelectedNotes()) first = Math.Min(first, n.Lane);
                ctx.Session.MoveSelected(0d, target - first);
            };
            body.Add(lane);
            refreshers.Add(() =>
            {
                List<LevelNote> s = ctx.Session.SelectedNotes();
                if (s.Count == 0) return;
                bool mixed = false;
                foreach (LevelNote n in s) if (n.Lane != s[0].Lane) mixed = true;
                lane.SetValue(s[0].Lane, mixed);
            });

            bool allLength = defs.Count > 0 && allKnown;
            foreach (NoteTypeDef d in defs) if (!d.HasLength) allLength = false;
            if (allLength)
            {
                var length = new NumberField("Length", "beats", "Hold length. Drag the bar's end on the timeline too.");
                length.Min = 1d / 16d; length.Max = 1000; length.Step = 1d / Math.Max(1, ctx.Session.SnapDivision); length.Format = "0.####";
                length.Committed += v => ctx.Session.ModifySelected("Change length", (n, d) => n.Length = v);
                body.Add(length);
                var lengthNote = Ui.Text("", "muted field-note");
                body.Add(lengthNote);
                refreshers.Add(() =>
                {
                    List<LevelNote> s = ctx.Session.SelectedNotes();
                    if (s.Count == 0) return;
                    bool mixed = false;
                    foreach (LevelNote n in s) if (Math.Abs(n.Length - s[0].Length) > 1e-9) mixed = true;
                    length.SetValue(s[0].Length, mixed);
                    lengthNote.text = mixed ? "" : tempo.BeatToSeconds(s[0].Length).ToString("0.000") + " s";
                });
            }

            // Parameters shared by every selected type.
            if (defs.Count > 0 && allKnown)
            {
                List<ParamDef> shared = SharedParams(defs);
                var main = new List<ParamDef>();
                var advanced = new List<ParamDef>();
                foreach (ParamDef p in shared) (string.IsNullOrEmpty(p.Group) ? main : advanced).Add(p);
                if (main.Count > 0) body.Add(Ui.SectionTitle("Properties"));
                foreach (ParamDef p in main) body.Add(ParamRow(p, defs));
                if (advanced.Count > 0)
                {
                    var fold = new Foldout { text = "Advanced", value = advancedOpen };
                    fold.AddToClassList("foldout");
                    fold.RegisterValueChangedCallback(evt => advancedOpen = evt.newValue);
                    foreach (ParamDef p in advanced) fold.Add(ParamRow(p, defs));
                    body.Add(fold);
                }

                if (shared.Count == 0 && defs.Count > 1) body.Add(Ui.Text("These note types share no properties.", "muted hint"));
            }

            // Derived info for one note.
            if (sel.Count == 1 && defs.Count == 1)
            {
                var info = Ui.Text("", "muted hint");
                body.Add(info);
                refreshers.Add(() =>
                {
                    List<LevelNote> s = ctx.Session.SelectedNotes();
                    if (s.Count != 1) return;
                    info.text = DerivedInfo(s[0], defs[0], ctx.Session.Tempo);
                });
            }

            VisualElement actions = Ui.Row("button-row");
            actions.Add(Ui.Button("Quantize", () => ctx.Session.QuantizeSelected(), "btn--small", "Snap beats and lengths to the grid (Q)"));
            actions.Add(Ui.Button("Mirror", () => ctx.Session.MirrorSelected(), "btn--small", "Flip lanes left/right (X)"));
            actions.Add(Icon.Button(IconKind.Duplicate, () => ctx.Session.DuplicateSelected(), "Duplicate after the selection (Ctrl+D)"));
            actions.Add(Ui.Spacer());
            actions.Add(Icon.Button(IconKind.Trash, () => ctx.Session.DeleteSelected(), "Delete (Del)", null, "btn--danger"));
            body.Add(actions);
        }

        private static string KeyList(int lanes)
        {
            var parts = new List<string>();
            for (int i = 1; i <= lanes; i++) parts.Add(i + "=" + LaneKeys.KeyName(lanes, i));
            return string.Join(", ", parts.ToArray());
        }

        private static List<ParamDef> SharedParams(List<NoteTypeDef> defs)
        {
            var result = new List<ParamDef>();
            foreach (ParamDef p in defs[0].Params)
            {
                bool everywhere = true;
                foreach (NoteTypeDef d in defs)
                {
                    ParamDef q = d.FindParam(p.Key);
                    if (q == null || q.Kind != p.Kind) { everywhere = false; break; }
                }

                if (everywhere) result.Add(p);
            }

            return result;
        }

        private static string DerivedInfo(LevelNote n, NoteTypeDef def, LevelTempo tempo)
        {
            var sb = new StringBuilder();
            if (def.Output == NoteOutput.Sequence)
            {
                sb.Append("Starts ").Append(tempo.BeatToSeconds(n.Beat).ToString("0.00")).Append(" s after beat 0. It spawns its own shots while it runs.");
                return sb.ToString();
            }

            double travel = NoteParams.TravelSeconds(n, def, tempo);
            double spawnBeat = n.Beat - NoteParams.TravelBeats(n, def, tempo);
            sb.Append(def.Stationary ? "Charges " : "Spawns ").Append(travel.ToString("0.00")).Append(" s before its hit");
            sb.Append(" (beat ").Append(spawnBeat.ToString("0.##", CultureInfo.InvariantCulture)).Append(").");
            if (spawnBeat < 0d) sb.Append(" It appears before beat 0: part of the chart's lead-in.");
            if (def.Archetype == Archetypes.Mash)
            {
                double presses = NoteParams.GetBound(n, def, ParamBindings.MashPresses, tempo, 8d);
                double rate = presses / Math.Max(1e-3, travel);
                sb.Append("\n").Append(rate.ToString("0.0")).Append(" presses per second needed");
                sb.Append(rate > LevelValidator.MashErrorPressesPerSecond ? " (too fast)." : rate > LevelValidator.MashWarnPressesPerSecond ? " (demanding)." : ".");
            }

            return sb.ToString();
        }

        /// <summary>One parameter editor, generated from its <see cref="ParamDef"/>.</summary>
        private VisualElement ParamRow(ParamDef p, List<NoteTypeDef> defs)
        {
            VisualElement row = Ui.Row("param-row");
            Button reset = Ui.Button("", () => ctx.Session.ResetParamOnSelected(p.Key), "btn--icon btn--reset", "Back to the note type's default");
            reset.Add(new Icon(IconKind.Reload, 11f));
            LevelTempo tempo = ctx.Session.Tempo;
            string tip = p.Tooltip;

            switch (p.Kind)
            {
                case ParamKind.Bool:
                {
                    var f = new CheckField(p.Label, false, tip);
                    f.AddToClassList("grow");
                    f.Changed += v => ctx.Session.SetParamOnSelected(p.Key, v);
                    row.Add(f);
                    refreshers.Add(() =>
                    {
                        bool mixed;
                        object v = Common(p.Key, out mixed);
                        f.SetValue(v is bool && (bool)v, mixed);
                    });
                    break;
                }
                case ParamKind.Choice:
                {
                    var options = new List<string>(p.Options);
                    var f = new ChoiceField(p.Label, options, 0, tip);
                    f.AddToClassList("grow");
                    f.Changed += (i, v) => ctx.Session.SetParamOnSelected(p.Key, v);
                    row.Add(f);
                    refreshers.Add(() =>
                    {
                        bool mixed;
                        object v = Common(p.Key, out mixed);
                        f.SetIndex(mixed ? -1 : options.IndexOf(Convert.ToString(v, CultureInfo.InvariantCulture)));
                    });
                    break;
                }
                case ParamKind.Text:
                {
                    var f = new TextEntry(p.Label, tip);
                    f.AddToClassList("grow");
                    f.Committed += v => ctx.Session.SetParamOnSelected(p.Key, v);
                    row.Add(f);
                    refreshers.Add(() =>
                    {
                        bool mixed;
                        object v = Common(p.Key, out mixed);
                        f.SetValue(mixed ? "" : Convert.ToString(v, CultureInfo.InvariantCulture));
                    });
                    break;
                }
                default:
                {
                    string suffix = p.Kind == ParamKind.Beats ? "beats" : p.Kind == ParamKind.Seconds ? "s" : null;
                    var f = new NumberField(p.Label, suffix, tip);
                    f.AddToClassList("grow");
                    f.Integer = p.Kind == ParamKind.Int;
                    f.Min = p.Min;
                    f.Max = p.Max;
                    f.Step = p.Step > 0 ? p.Step : (p.Kind == ParamKind.Int ? 1 : 0.05);
                    f.Format = p.Kind == ParamKind.Int ? "0" : "0.###";
                    f.Committed += v => ctx.Session.SetParamOnSelected(p.Key, v);
                    row.Add(f);
                    Label note = null;
                    if (p.Kind == ParamKind.Beats)
                    {
                        note = Ui.Text("", "param-row__note");
                        row.Add(note);
                        f.Scrubbing += v => note.text = ctx.Session.Tempo.BeatToSeconds(v).ToString("0.00") + " s";
                    }

                    refreshers.Add(() =>
                    {
                        bool mixed;
                        object v = Common(p.Key, out mixed);
                        double d = Json.JsonRead.ToDouble(v, 0d);
                        f.SetValue(d, mixed);
                        if (note != null) note.text = mixed ? "" : ctx.Session.Tempo.BeatToSeconds(d).ToString("0.00") + " s";
                    });

                    if (p.Kind == ParamKind.Beats && p.Bind == ParamBindings.TravelBeats)
                    {
                        // Quick presets like the game's composer (½ / 1 / 1½ / 2 bars) plus faster / slower.
                        VisualElement presets = Ui.Row("presets");
                        int bpb = tempo.BeatsPerBar;
                        foreach (double bars in new[] { 0.5, 1.0, 1.5, 2.0 })
                        {
                            double beats = bars * bpb;
                            string label = bars == 0.5 ? "½ bar" : bars == 1.5 ? "1½ bars" : bars + (bars == 1 ? " bar" : " bars");
                            presets.Add(Ui.Button(label, () => ctx.Session.SetParamOnSelected(p.Key, beats), "btn--small btn--ghost"));
                        }

                        presets.Add(Ui.Button("Faster", () => ScaleTravel(p, 0.9), "btn--small btn--ghost", "Travel x0.9"));
                        presets.Add(Ui.Button("Slower", () => ScaleTravel(p, 1d / 0.9), "btn--small btn--ghost", "Travel ÷0.9"));
                        var wrap = Ui.Col("param-block");
                        row.AddToClassList("grow");
                        wrap.Add(row);
                        row.Add(reset);
                        wrap.Add(presets);
                        refreshers.Add(() => Ui.Show(reset, AnyOverride(p.Key)));
                        return wrap;
                    }

                    break;
                }
            }

            row.Add(reset);
            refreshers.Add(() => Ui.Show(reset, AnyOverride(p.Key)));
            return row;
        }

        private void ScaleTravel(ParamDef p, double factor)
        {
            LevelTempo tempo = ctx.Session.Tempo;
            ctx.Session.ModifySelected("Scale travel", (n, d) =>
            {
                if (d == null || d.FindParam(p.Key) == null) return;
                double v = NoteParams.GetNumber(n, d, p.Key, tempo, 4d) * factor;
                NoteParams.Set(n, d, p.Key, Math.Round(v * 64d) / 64d, tempo);
            });
        }

        private bool AnyOverride(string key)
        {
            foreach (LevelNote n in ctx.Session.SelectedNotes()) if (n.HasParam(key)) return true;
            return false;
        }

        /// <summary>The selection's value of a parameter, or <paramref name="mixed"/> when notes differ.</summary>
        private object Common(string key, out bool mixed)
        {
            mixed = false;
            object first = null;
            bool any = false;
            LevelTempo tempo = ctx.Session.Tempo;
            foreach (LevelNote n in ctx.Session.SelectedNotes())
            {
                NoteTypeDef d = ctx.Types.Find(n.Type);
                object v = NoteParams.Get(n, d, key, tempo);
                if (!any) { first = v; any = true; continue; }
                if (!Same(first, v)) { mixed = true; break; }
            }

            return first;
        }

        private static bool Same(object a, object b)
        {
            if (a is double && b is double) return Math.Abs((double)a - (double)b) < 1e-9;
            return Equals(a, b);
        }

        // ------------------------------------------------------------------ problems

        public void SetIssues(List<Issue> list)
        {
            issues = list;
            problemsList.Clear();
            int errors = 0, warnings = 0, infos = 0;
            foreach (Issue i in list)
            {
                if (i.Severity == Severity.Error) errors++;
                else if (i.Severity == Severity.Warning) warnings++;
                else infos++;
            }

            problemsTitle.text = "Problems" + (errors + warnings + infos == 0 ? "" : "   <color=#FF5C6C>" + errors + "</color> · <color=#FFB547>" + warnings + "</color> · <color=#8E96A8>" + infos + "</color>");
            if (list.Count == 0)
            {
                problemsList.Add(Ui.Text("No problems found.", "muted problem"));
                return;
            }

            int shown = 0;
            foreach (Issue i in list)
            {
                if (shown++ >= 120) break;
                Issue issue = i;
                VisualElement row = Ui.Row("problem problem--" + i.Severity.ToString().ToLowerInvariant());
                row.Add(Ui.Div("problem__dot"));
                string where = i.StepIndex >= 0 && i.StepIndex < ctx.Session.Level.Steps.Count
                    ? "Step " + (i.StepIndex + 1) + (i.NoteId != null ? " · bar " + CombatLevel.FormatBeat(i.Beat, ctx.Session.Tempo.BeatsPerBar) : "")
                    : "Level";
                row.Add(Ui.Text(i.Message + "\n<color=#8E96A8><size=10>" + where + "</size></color>", "problem__text"));
                row.RegisterCallback<PointerDownEvent>(evt => Jump(issue));
                problemsList.Add(row);
            }
        }

        private void Jump(Issue i)
        {
            if (i.StepIndex >= 0 && i.StepIndex < ctx.Session.Level.Steps.Count) ctx.Session.SelectStep(i.StepIndex);
            if (i.NoteId != null)
            {
                ctx.Session.SelectOnly(i.NoteId);
                ctx.RequestFocus(i.NoteId, i.Beat);
            }
            else if (i.StepIndex < 0 && (i.Message.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0 || i.Message.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0 || i.Message.IndexOf("MP3", StringComparison.Ordinal) >= 0))
            {
                ctx.App.ShowMusicTab();
            }
        }
    }
}
