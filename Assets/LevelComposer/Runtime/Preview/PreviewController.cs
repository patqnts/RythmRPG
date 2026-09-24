using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Simulation;
using RythmRPG.LevelComposer.Timing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RythmRPG.LevelComposer.App
{
    public enum PreviewMode
    {
        /// <summary>The selected step over the loop.</summary>
        Step,
        /// <summary>The whole level: intro, steps and player turns, end.</summary>
        Level
    }

    /// <summary>A judgement popup the simulator view animates.</summary>
    public struct Popup
    {
        public int Lane;
        public Judgement Judgement;
        public string Text;
        public double Time;
    }

    /// <summary>
    /// Ties the level to the preview: builds the <see cref="FlowPlan"/> and <see cref="Simulator"/> from the current
    /// level, runs the preview clock and music, reads the lane keys (A S J K like the game) and turns judgements into
    /// popups and hit sounds. Rebuilds itself when the level changes, also while playing (live editing).
    /// </summary>
    public sealed class PreviewController
    {
        private readonly ComposerContext ctx;
        private readonly Simulator sim = new Simulator();
        private readonly List<Popup> popups = new List<Popup>();
        private FlowPlan plan;
        private bool dirty = true;
        private PreviewMode mode = PreviewMode.Step;
        private double playStartedAt;
        private int shownStep = -1;

        public Playback Playback { get; private set; }
        public Simulator Sim { get { return sim; } }
        public FlowPlan Plan { get { EnsurePlan(); return plan; } }
        public IList<Popup> Popups { get { return popups; } }
        public PreviewMode Mode { get { return mode; } }
        public bool IsPlaying { get { return Playback.IsPlaying; } }
        public double Time { get { return Playback.Time; } }
        public event Action ModeChanged;
        public event Action PlayStateChanged;

        public PreviewController(ComposerContext ctx, Playback playback)
        {
            this.ctx = ctx;
            Playback = playback;
            Playback.ReachedEnd += () => { if (PlayStateChanged != null) PlayStateChanged(); };
            ctx.Session.Changed += kind =>
            {
                if ((kind & (ChangeKind.Notes | ChangeKind.Steps | ChangeKind.Music | ChangeKind.Meta)) != 0) dirty = true;
                // Selecting another step only changes the step preview (the level preview plays every step).
                if ((kind & ChangeKind.CurrentStep) != 0 && mode == PreviewMode.Step) dirty = true;
            };
            ctx.Library.Loaded += e => dirty = true;
        }

        public void MarkDirty() { dirty = true; }

        public void SetMode(PreviewMode m)
        {
            if (m == mode) return;
            bool wasPlaying = IsPlaying;
            if (wasPlaying) Playback.Pause();
            mode = m;
            dirty = true;
            EnsurePlan();
            Seek(StartTime());
            if (ModeChanged != null) ModeChanged();
            if (PlayStateChanged != null) PlayStateChanged();
        }

        /// <summary>Where "play from start" begins: the step's lead-in, or 0 for the level.</summary>
        public double StartTime()
        {
            EnsurePlan();
            return plan.TimelineStart;
        }

        // ------------------------------------------------------------------ mapping between chart beats and preview time

        /// <summary>Preview time of the current step's beat 0.</summary>
        public double ZeroOfStep(int stepIndex)
        {
            EnsurePlan();
            foreach (StepPlacement p in plan.Placements)
                if (p.StepIndex == stepIndex) return p.Zero;
            return 0d;
        }

        public double BeatToTime(int stepIndex, double beat)
        {
            return ZeroOfStep(stepIndex) + ctx.Session.Tempo.BeatToSeconds(beat);
        }

        public double TimeToBeat(int stepIndex, double t)
        {
            return ctx.Session.Tempo.SecondsToBeat(t - ZeroOfStep(stepIndex));
        }

        /// <summary>The playhead as a beat of the step shown in the timeline.</summary>
        public double PlayheadBeat { get { return TimeToBeat(ctx.Session.StepIndex, Time); } }

        // ------------------------------------------------------------------ transport

        public void TogglePlay()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void Play()
        {
            EnsurePlan();
            double t = Time;
            if (t >= plan.Duration - 0.05d || t < plan.TimelineStart - 0.001d) t = StartTime();
            playStartedAt = t;
            sim.AutoPlay = ctx.Prefs.AutoHit;
            sim.ResetTo(t);
            popups.Clear();
            Playback.Play(t);
            if (PlayStateChanged != null) PlayStateChanged();
        }

        public void Pause()
        {
            Playback.Pause();
            if (PlayStateChanged != null) PlayStateChanged();
        }

        /// <summary>Stops and returns to where playback started (or the start when stopped already).</summary>
        public void Stop()
        {
            bool was = IsPlaying;
            Playback.Pause();
            Seek(was ? playStartedAt : StartTime());
            if (PlayStateChanged != null) PlayStateChanged();
        }

        public void Seek(double t)
        {
            EnsurePlan();
            // Allow the playhead past the content (e.g. to paste there); playback restarts from the start when past the end.
            t = Math.Max(plan.TimelineStart - 4d, Math.Min(plan.Duration + 3600d, t));
            sim.ResetTo(t);
            popups.Clear();
            Playback.Seek(t);
        }

        public void SeekToBeat(double beat)
        {
            Seek(BeatToTime(ctx.Session.StepIndex, beat));
        }

        // ------------------------------------------------------------------ per frame

        public void Update()
        {
            if (dirty && !ctx.Session.InGesture) Rebuild();
            if (!IsPlaying)
            {
                Playback.Update(TempoInfo());
                return;
            }

            double t = Time;
            ReadKeys(t);
            sim.AutoPlay = ctx.Prefs.AutoHit;
            sim.Advance(t);
            foreach (SimEvent e in sim.DrainEvents())
            {
                switch (e.Kind)
                {
                    case SimEventKind.Judged:
                        if (e.Note != null && e.Note.Def != null && e.Note.Def.Output == Types.NoteOutput.Sequence)
                        {
                            AddPopup(e.Lane, Judgement.None, e.Text, e.Time);
                            break;
                        }

                        AddPopup(e.Lane, e.Judgement, e.Text, e.Time);
                        if (e.Text != "Held") Playback.PlayHit(e.Judgement, e.Time);
                        break;
                    case SimEventKind.HoldStart:
                        AddPopup(e.Lane, e.Judgement, e.Text, e.Time);
                        Playback.PlayHit(e.Judgement, e.Time);
                        break;
                    case SimEventKind.MashPress:
                        Playback.PlayHit(Judgement.None, e.Time);
                        break;
                    case SimEventKind.SequenceStart:
                    case SimEventKind.SequenceEnd:
                        AddPopup(e.Lane, Judgement.None, e.Text, e.Time);
                        break;
                }
            }

            Playback.Update(TempoInfo());
            FollowStep(t);
        }

        private void AddPopup(int lane, Judgement j, string text, double time)
        {
            popups.Add(new Popup { Lane = lane, Judgement = j, Text = text, Time = time });
            if (popups.Count > 24) popups.RemoveAt(0);
        }

        /// <summary>Level preview: shows the step that is playing in the timeline.</summary>
        private void FollowStep(double t)
        {
            if (mode != PreviewMode.Level || !ctx.Prefs.FollowPlayhead) return;
            FlowSegment s = plan.SegmentAt(t);
            if (s == null || s.Kind != FlowSegmentKind.EnemyStep || s.StepIndex == shownStep) return;
            shownStep = s.StepIndex;
            if (ctx.Session.StepIndex != s.StepIndex) ctx.Session.SelectStep(s.StepIndex);
        }

        public int LaneCountAt(double t)
        {
            EnsurePlan();
            return plan.LaneCountAt(t, ctx.Session.Step.LaneCount);
        }

        private void ReadKeys(double t)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || ctx.IsTyping() || ctx.Modal.IsOpen) return;
            double pressTime = t - ctx.Prefs.InputOffsetMs / 1000d;
            int lanes = LaneCountAt(t);
            HandleKey(kb.aKey.wasPressedThisFrame, kb.aKey.wasReleasedThisFrame, 1, lanes, pressTime);
            HandleKey(kb.sKey.wasPressedThisFrame, kb.sKey.wasReleasedThisFrame, 2, lanes, pressTime);
            HandleKey(kb.jKey.wasPressedThisFrame, kb.jKey.wasReleasedThisFrame, 3, lanes, pressTime);
            HandleKey(kb.kKey.wasPressedThisFrame, kb.kKey.wasReleasedThisFrame, 4, lanes, pressTime);
        }

        private void HandleKey(bool pressed, bool released, int slot, int laneCount, double t)
        {
            int lane = LaneKeys.LaneForSlot(laneCount, slot);
            if (lane <= 0) return;
            if (pressed) sim.Press(lane, t);
            if (released) sim.Release(lane, t);
        }

        // ------------------------------------------------------------------ building

        private void EnsurePlan()
        {
            if (plan == null || (dirty && !ctx.Session.InGesture)) Rebuild();
        }

        public FlowAudio AudioLengths()
        {
            var a = new FlowAudio();
            AudioEntry intro = ctx.Section(MusicSection.Intro), loop = ctx.Section(MusicSection.Loop), end = ctx.Section(MusicSection.End);
            if (intro != null && intro.Ready) a.IntroSeconds = intro.Seconds;
            if (loop != null && loop.Ready) a.LoopSeconds = loop.Seconds;
            if (end != null && end.Ready) a.EndSeconds = end.Seconds;
            return a;
        }

        public SectionClips Clips()
        {
            var c = new SectionClips();
            AudioEntry e;
            if ((e = ctx.Section(MusicSection.Intro)) != null) c.Intro = e.Clip;
            if ((e = ctx.Section(MusicSection.Loop)) != null) c.Loop = e.Clip;
            if ((e = ctx.Section(MusicSection.PlayerTurn)) != null) c.PlayerTurn = e.Clip;
            if ((e = ctx.Section(MusicSection.End)) != null) c.End = e.Clip;
            if ((e = ctx.Section(MusicSection.DefeatEnd)) != null) c.DefeatEnd = e.Clip;
            return c;
        }

        private void Rebuild()
        {
            dirty = false;
            CombatLevel level = ctx.Session.Level;
            FlowAudio audio = AudioLengths();
            plan = mode == PreviewMode.Level
                ? FlowPlan.ForLevel(level, ctx.Types, audio)
                : FlowPlan.ForStep(level, ctx.Session.StepIndex, ctx.Types, audio, level.Preview.LoopBar);
            sim.Windows.Perfect = ctx.Prefs.Windows.Perfect;
            sim.Windows.Good = ctx.Prefs.Windows.Good;
            sim.Windows.Bad = ctx.Prefs.Windows.Bad;
            sim.Windows.Miss = ctx.Prefs.Windows.Miss;
            sim.Build(ctx.Types, LevelTempo.Of(level), plan.Placements);
            double t = Time;
            sim.ResetTo(t);
            Playback.SetPlan(plan, Clips(), level.Music);
            if (!IsPlaying && t < plan.TimelineStart - 4d) Playback.Seek(plan.TimelineStart);
        }

        private LevelTempoInfo TempoInfo()
        {
            LevelTempo tempo = ctx.Session.Tempo;
            double origin = 0d;
            if (plan != null && plan.IsLevel) origin = plan.LoopStart + ctx.Session.Level.Music.OffsetSeconds;
            return new LevelTempoInfo { SecondsPerBeat = tempo.SecondsPerBeat, BeatsPerBar = tempo.BeatsPerBar, GridOrigin = origin };
        }
    }
}
