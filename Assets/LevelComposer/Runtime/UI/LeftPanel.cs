using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Simulation;
using RythmRPG.LevelComposer.Validation;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>Left side: the attack steps, the music sections and the level settings, as three tabs.</summary>
    public sealed class LeftPanel : VisualElement
    {
        private readonly ComposerContext ctx;
        private readonly VisualElement stepsTab, musicTab, levelTab;
        private readonly ToggleButton stepsButton, musicButton, levelButton;
        private readonly VisualElement stepList;
        private readonly StepSettings stepSettings;
        private readonly MusicPanel music;
        private readonly LevelSettings levelSettings;
        private List<Issue> issues = new List<Issue>();

        public LeftPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("panel");
            AddToClassList("left-panel");

            VisualElement tabs = Ui.Row("tabs");
            stepsButton = new ToggleButton("Steps", true, "The enemy's attack steps, in order");
            musicButton = new ToggleButton("Music", false, "Intro, main loop, player turn and end music, tempo and offset");
            levelButton = new ToggleButton("Level", false, "Level id, name and preview settings");
            stepsButton.AddToClassList("tab");
            musicButton.AddToClassList("tab");
            levelButton.AddToClassList("tab");
            stepsButton.Toggled += on => ShowTab(0);
            musicButton.Toggled += on => ShowTab(1);
            levelButton.Toggled += on => ShowTab(2);
            tabs.Add(stepsButton);
            tabs.Add(musicButton);
            tabs.Add(levelButton);
            Add(tabs);

            // Steps.
            stepsTab = new ScrollView(ScrollViewMode.Vertical);
            stepsTab.AddToClassList("tab-body");
            stepsTab.Add(Ui.SectionTitle("Attack steps"));
            stepsTab.Add(Ui.Text("The enemy uses these in order. Each step is a wind-up animation and a chart.", "muted hint"));
            stepList = Ui.Col("step-list");
            stepsTab.Add(stepList);
            VisualElement stepButtons = Ui.Row("button-row");
            stepButtons.Add(Icon.Button(IconKind.Plus, () => ctx.Session.AddStep(), "Add a step after the selected one", "Add"));
            stepButtons.Add(Icon.Button(IconKind.Duplicate, () => ctx.Session.DuplicateStep(), "Duplicate the selected step"));
            stepButtons.Add(Icon.Button(IconKind.Up, () => ctx.Session.MoveStep(-1), "Move the step up"));
            stepButtons.Add(Icon.Button(IconKind.Down, () => ctx.Session.MoveStep(1), "Move the step down"));
            stepButtons.Add(Ui.Spacer());
            stepButtons.Add(Icon.Button(IconKind.Trash, DeleteStep, "Delete the selected step", null, "btn--danger"));
            stepsTab.Add(stepButtons);
            stepSettings = new StepSettings(ctx);
            stepsTab.Add(stepSettings);
            Add(stepsTab);

            // Music.
            music = new MusicPanel(ctx);
            musicTab = music;
            Add(musicTab);

            // Level.
            levelSettings = new LevelSettings(ctx);
            levelTab = levelSettings;
            Add(levelTab);

            ShowTab(0);
            ctx.Session.Changed += kind =>
            {
                if ((kind & (ChangeKind.Steps | ChangeKind.Notes | ChangeKind.File | ChangeKind.CurrentStep)) != 0) RebuildSteps();
            };
            RebuildSteps();
        }

        public MusicPanel Music { get { return music; } }

        public void ShowTab(int index)
        {
            Ui.Show(stepsTab, index == 0);
            Ui.Show(musicTab, index == 1);
            Ui.Show(levelTab, index == 2);
            stepsButton.SetOn(index == 0, false);
            musicButton.SetOn(index == 1, false);
            levelButton.SetOn(index == 2, false);
            if (index == 1) music.Refresh();
            if (index == 2) levelSettings.Refresh();
        }

        public void SetIssues(List<Issue> list)
        {
            issues = list;
            RebuildSteps();
            music.SetIssues(list);
        }

        private void DeleteStep()
        {
            LevelStep s = ctx.Session.Step;
            if (s.Notes.Count == 0) { ctx.Session.RemoveStep(); return; }
            ctx.Modal.Ask("Delete step", "Delete '" + s.Name + "' and its " + s.Notes.Count + " notes? You can undo this.",
                new[] { "Delete", "Cancel" }, i => { if (i == 0) ctx.Session.RemoveStep(); });
        }

        private void RebuildSteps()
        {
            stepList.Clear();
            CombatLevel level = ctx.Session.Level;
            var tempo = ctx.Session.Tempo;
            for (int i = 0; i < level.Steps.Count; i++)
            {
                LevelStep s = level.Steps[i];
                int index = i;
                VisualElement card = Ui.Col("step-card");
                card.EnableInClassList("step-card--selected", i == ctx.Session.StepIndex);
                VisualElement head = Ui.Row();
                head.Add(Ui.Text((i + 1) + ".", "step-card__index"));
                head.Add(Ui.Text(s.Name, "step-card__name"));
                int errors = 0, warnings = 0;
                foreach (Issue issue in issues)
                {
                    if (issue.StepIndex != i) continue;
                    if (issue.Severity == Severity.Error) errors++;
                    else if (issue.Severity == Severity.Warning) warnings++;
                }

                head.Add(Ui.Spacer());
                if (errors > 0) head.Add(Ui.Text(errors.ToString(), "badge badge--error"));
                else if (warnings > 0) head.Add(Ui.Text(warnings.ToString(), "badge badge--warning"));
                card.Add(head);
                double seconds = FlowPlan.ContentEndSeconds(s, ctx.Types, tempo) + FlowPlan.LeadSeconds(s, ctx.Types, tempo);
                string anim = string.IsNullOrEmpty(s.Animation) ? "no animation" : s.Animation;
                card.Add(Ui.Text(s.Notes.Count + " notes  ·  " + s.LaneCount + " lanes  ·  " + seconds.ToString("0.0") + " s  ·  " + anim, "step-card__meta"));
                card.RegisterCallback<PointerDownEvent>(evt => { ctx.Session.SelectStep(index); evt.StopPropagation(); });
                stepList.Add(card);
            }

            stepSettings.Refresh();
        }
    }

    /// <summary>Settings of the selected step (EnemyAttackStepDefinition fields).</summary>
    public sealed class StepSettings : VisualElement
    {
        private readonly ComposerContext ctx;
        private readonly TextEntry nameField, animation;
        private readonly NumberField anticipation;
        private readonly ChoiceField endPolicy;
        private readonly ToggleButton[] laneButtons = new ToggleButton[4];

        public StepSettings(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("card");
            Add(Ui.SectionTitle("Selected step"));
            nameField = new TextEntry("Name");
            nameField.Committed += v => ctx.Session.EditStep("Rename step", s => s.Name = string.IsNullOrEmpty(v) ? s.Name : v.Trim());
            Add(nameField);
            animation = new TextEntry("Enemy animation", "Animator state the enemy plays as its wind-up (e.g. Laser, Attack1).");
            animation.Committed += v => ctx.Session.EditStep("Change animation", s => s.Animation = (v ?? "").Trim());
            Add(animation);
            anticipation = new NumberField("Wind-up", "s", "Seconds the wind-up animation plays before the first projectile appears.");
            anticipation.Min = 0;
            anticipation.Max = 10;
            anticipation.Step = 0.05;
            anticipation.Format = "0.00";
            anticipation.Committed += v => ctx.Session.EditStep("Change wind-up", s => s.Anticipation = v);
            Add(anticipation);
            endPolicy = new ChoiceField("When it ends", new List<string> { "Wait for all notes", "Clear rest as misses", "Clear rest, no penalty" }, 0,
                "What happens to notes still on screen when the step's time is up (AttackStepEndPolicy).");
            endPolicy.Changed += (i, v) => ctx.Session.EditStep("Change end policy", s => s.EndPolicy = (StepEndPolicy)i);
            Add(endPolicy);

            VisualElement lanes = Ui.Row("field");
            lanes.Add(Ui.Text("Lanes", "field__label"));
            VisualElement seg = Ui.Row("segmented grow");
            for (int i = 0; i < 4; i++)
            {
                int count = i + 1;
                var b = new ToggleButton(count.ToString(), false, count + (count == 1 ? " lane (key J)" : count == 2 ? " lanes (S J)" : count == 3 ? " lanes (S J K)" : " lanes (A S J K)"));
                b.Toggled += on => SetLanes(count);
                laneButtons[i] = b;
                seg.Add(b);
            }

            lanes.Add(seg);
            Add(lanes);
            Refresh();
        }

        private void SetLanes(int count)
        {
            LevelStep s = ctx.Session.Step;
            int affected = 0;
            foreach (LevelNote n in s.Notes) if (n.Lane > count) affected++;
            if (affected == 0) { ctx.Session.SetLaneCount(count); Refresh(); return; }
            ctx.Modal.Ask("Fewer lanes", affected + " notes are on lanes above " + count + ". They will move to lane " + count + ".",
                new[] { "Change lanes", "Cancel" }, i =>
                {
                    if (i == 0) ctx.Session.SetLaneCount(count);
                    Refresh();
                });
        }

        public void Refresh()
        {
            LevelStep s = ctx.Session.Step;
            nameField.SetValue(s.Name);
            animation.SetValue(s.Animation);
            anticipation.SetValue(s.Anticipation);
            endPolicy.SetIndex((int)s.EndPolicy);
            for (int i = 0; i < laneButtons.Length; i++) laneButtons[i].SetOn(s.LaneCount == i + 1, false);
        }
    }

    /// <summary>Level id / name / preview settings and how to bring the level into the game.</summary>
    public sealed class LevelSettings : ScrollView
    {
        private readonly ComposerContext ctx;
        private readonly TextEntry id, levelName, author;
        private readonly TextField description;
        private readonly NumberField weight, playerTurnBars;
        private readonly Label stats;

        public LevelSettings(ComposerContext ctx) : base(ScrollViewMode.Vertical)
        {
            this.ctx = ctx;
            AddToClassList("tab-body");
            Add(Ui.SectionTitle("Level"));
            id = new TextEntry("Id", "Used for the imported asset names and EnemyAttackSequence id. Lowercase, no spaces.");
            id.Committed += v => ctx.Session.EditLevel("Change id", l => l.Id = CombatLevel.Slug(v));
            Add(id);
            levelName = new TextEntry("Name");
            levelName.Committed += v => { if (!string.IsNullOrEmpty(v)) ctx.Session.EditLevel("Rename level", l => l.Name = v.Trim()); };
            Add(levelName);
            author = new TextEntry("Author");
            author.Committed += v => ctx.Session.EditLevel("Change author", l => l.Author = (v ?? "").Trim());
            Add(author);
            Add(Ui.Text("Notes for the team", "field__label field__label--block"));
            description = new TextField { multiline = true };
            description.isDelayed = true;
            description.AddToClassList("textarea");
            description.RegisterValueChangedCallback(evt => ctx.Session.EditLevel("Change description", l => l.Description = evt.newValue ?? ""));
            Add(description);
            weight = new NumberField("Pick weight", null, "How likely the enemy picks this sequence when a phase has several (EnemyAttackSequence selection weight).");
            weight.Min = 0.01;
            weight.Max = 100;
            weight.Step = 0.1;
            weight.Committed += v => ctx.Session.EditLevel("Change weight", l => l.SelectionWeight = v);
            Add(weight);

            Add(Ui.SectionTitle("Level preview"));
            playerTurnBars = new NumberField("Player turn", "bars", "How long the player turn lasts between enemy steps in the Level preview.");
            playerTurnBars.Integer = true;
            playerTurnBars.Min = 0;
            playerTurnBars.Max = 64;
            playerTurnBars.Committed += v => ctx.Session.EditLevel("Change player turn length", l => l.Preview.PlayerTurnBars = (int)v, ChangeKind.Meta);
            Add(playerTurnBars);
            stats = Ui.Text("", "muted hint");
            Add(stats);

            Add(Ui.SectionTitle("Into the game"));
            Add(Ui.Text(
                "1. Save the level (.combatlevel.json). Keeping it and its music inside the game repository lets the whole team open it.\n" +
                "2. In Unity: Tools > Rythm RPG > Level Composer > Import Combat Level...\n" +
                "3. It creates (or updates) a CombatSong, one RhythmChart per step and an EnemyAttackSequence under Assets/CombatLevels/<id>/. Add the sequence to an enemy phase.\n" +
                "Existing sequences can be sent the other way with Export Enemy Attack Sequence.", "muted hint"));
            ctx.Session.Changed += kind => { if (parent != null && resolvedStyle.display != DisplayStyle.None) Refresh(); };
        }

        public void Refresh()
        {
            CombatLevel l = ctx.Session.Level;
            id.SetValue(l.Id);
            levelName.SetValue(l.Name);
            author.SetValue(l.Author);
            description.SetValueWithoutNotify(l.Description);
            weight.SetValue(l.SelectionWeight);
            playerTurnBars.SetValue(l.Preview.PlayerTurnBars);
            FlowPlan plan = FlowPlan.ForLevel(l, ctx.Types, ctx.Preview.AudioLengths());
            stats.text = l.Steps.Count + " steps  ·  " + l.TotalNotes + " notes  ·  full preview " + plan.Duration.ToString("0.0") + " s";
        }
    }
}
