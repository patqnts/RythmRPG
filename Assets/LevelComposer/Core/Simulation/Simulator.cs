using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;

namespace RythmRPG.LevelComposer.Simulation
{
    /// <summary>Where a step's beat 0 falls on the simulator's clock.</summary>
    public struct StepPlacement
    {
        public LevelStep Step;
        public int StepIndex;
        public double Zero;

        public StepPlacement(LevelStep step, int index, double zero)
        {
            Step = step;
            StepIndex = index;
            Zero = zero;
        }
    }

    /// <summary>
    /// Plays one or more attack steps against a clock: spawns notes, routes lane presses to the best note (like the
    /// game's RhythmPatternRunner), judges them, and can auto-play perfectly. Deterministic and time driven: the view
    /// just calls <see cref="Advance"/> with the music clock each frame and draws <see cref="Notes"/>.
    /// </summary>
    public sealed class Simulator : ISimContext
    {
        public const int MaxLanes = 4;

        private readonly List<SimNote> notes = new List<SimNote>();
        private readonly List<SimNote> waiting = new List<SimNote>();
        private readonly List<AutoInput> autoQueue = new List<AutoInput>();
        private readonly List<AutoInput> manualQueue = new List<AutoInput>();
        private readonly List<SimEvent> events = new List<SimEvent>();
        private readonly bool[] keyHeld = new bool[MaxLanes + 1];
        private readonly bool[] autoHeld = new bool[MaxLanes + 1];
        private readonly double[] lastPressAt = new double[MaxLanes + 1];
        private int waitingCursor;

        public JudgementWindows Windows { get; private set; }
        public SimStats Stats { get; private set; }
        public LevelTempo Tempo { get; private set; }
        public double Now { get; private set; }
        /// <summary>Auto-hit: presses every note perfectly. Manual presses still work (e.g. to test a miss).</summary>
        public bool AutoPlay { get; set; }
        public IList<SimNote> Notes { get { return notes; } }
        public int Count { get { return notes.Count; } }

        public Simulator()
        {
            Windows = new JudgementWindows();
            Stats = new SimStats();
            Tempo = new LevelTempo(120d, 4);
            for (int i = 0; i < lastPressAt.Length; i++) lastPressAt[i] = double.NegativeInfinity;
        }

        /// <summary>Builds the run from steps placed on the clock. Call <see cref="ResetTo"/> before advancing.</summary>
        public void Build(NoteTypeRegistry registry, LevelTempo tempo, IList<StepPlacement> placements)
        {
            Tempo = tempo;
            notes.Clear();
            waiting.Clear();
            foreach (StepPlacement p in placements)
            {
                if (p.Step == null) continue;
                foreach (LevelNote src in p.Step.Notes)
                {
                    NoteTypeDef def = registry.Find(src.Type);
                    if (def == null) continue;
                    var n = new SimNote
                    {
                        Source = src,
                        Def = def,
                        Behaviour = SimBehaviours.Create(def.Archetype),
                        StepIndex = p.StepIndex,
                        StepZero = p.Zero,
                        Lane = Math.Max(1, Math.Min(MaxLanes, src.Lane)),
                        HitTime = p.Zero + tempo.BeatToSeconds(src.Beat),
                    };
                    n.EndTime = n.HitTime + (def.HasLength ? tempo.BeatToSeconds(Math.Max(0d, src.Length)) : 0d);
                    if (def.Output == NoteOutput.Sequence)
                    {
                        n.SpawnTime = n.HitTime;
                        n.TravelSeconds = 0d;
                    }
                    else
                    {
                        n.TravelSeconds = NoteParams.TravelSeconds(src, def, tempo);
                        n.SpawnTime = n.HitTime - n.TravelSeconds;
                    }

                    n.Damage = Math.Max(0, (int)Math.Round(NoteParams.GetBound(src, def, ParamBindings.Damage, tempo, 1d)));
                    n.Behaviour.Setup(n, this);
                    notes.Add(n);
                }
            }

            ResetTo(double.NegativeInfinity);
        }

        /// <summary>
        /// Restarts the run at <paramref name="t"/>: notes that are already over are skipped, notes on screen are live,
        /// sequences that already started are skipped (they cannot resume mid-way). Stats are cleared.
        /// </summary>
        public void ResetTo(double t)
        {
            notes.RemoveAll(n => n.IsGenerated);
            foreach (SimNote n in notes) n.Children.Clear();
            waiting.Clear();
            autoQueue.Clear();
            manualQueue.Clear();
            events.Clear();
            Stats.Reset();
            for (int i = 0; i < keyHeld.Length; i++) { keyHeld[i] = false; autoHeld[i] = false; lastPressAt[i] = double.NegativeInfinity; }
            Now = t;
            foreach (SimNote n in notes)
            {
                n.Result = Judgement.None;
                n.ResultText = "";
                n.ErrorSeconds = 0d;
                n.Presses = 0;
                n.Skipped = false;
                n.State = SimNoteState.Waiting;
                n.Behaviour.Setup(n, this);
                bool sequence = n.Def.Output == NoteOutput.Sequence;
                // Anything whose hit (or start, for sequences) is behind the playhead is skipped, including holds in progress.
                bool over = sequence ? n.SpawnTime < t - 1e-6 : n.HitTime < t - 1e-6;
                if (over)
                {
                    n.State = SimNoteState.Done;
                    n.Skipped = true;
                    continue;
                }

                waiting.Add(n);
            }

            waiting.Sort((a, b) => a.SpawnTime.CompareTo(b.SpawnTime));
            waitingCursor = 0;
            // Notes already on screen appear immediately but get no auto inputs before t.
            while (waitingCursor < waiting.Count && waiting[waitingCursor].SpawnTime <= t)
            {
                Spawn(waiting[waitingCursor], t);
                waitingCursor++;
            }
        }

        // ------------------------------------------------------------------ input

        /// <summary>Queues a lane press (1-based lane) at simulator time <paramref name="t"/>.</summary>
        public void Press(int lane, double t)
        {
            if (lane < 1 || lane > MaxLanes) return;
            manualQueue.Add(new AutoInput { Lane = lane, Press = true, Time = t });
        }

        public void Release(int lane, double t)
        {
            if (lane < 1 || lane > MaxLanes) return;
            manualQueue.Add(new AutoInput { Lane = lane, Press = false, Time = t });
        }

        public bool IsKeyHeld(int lane) { return lane >= 1 && lane <= MaxLanes && (keyHeld[lane] || autoHeld[lane]); }

        /// <summary>Seconds since the lane was last pressed (for key flash visuals).</summary>
        public double SinceLastPress(int lane) { return lane >= 1 && lane <= MaxLanes ? Now - lastPressAt[lane] : double.PositiveInfinity; }

        // ------------------------------------------------------------------ clock

        /// <summary>Runs everything due up to <paramref name="t"/> in time order.</summary>
        public void Advance(double t)
        {
            if (t < Now) return;
            manualQueue.Sort((a, b) => a.Time.CompareTo(b.Time));
            int guard = 0;
            while (guard++ < 100000)
            {
                double spawnAt = waitingCursor < waiting.Count ? waiting[waitingCursor].SpawnTime : double.PositiveInfinity;
                double autoAt = AutoPlay && autoQueue.Count > 0 ? autoQueue[0].Time : double.PositiveInfinity;
                double manualAt = manualQueue.Count > 0 ? manualQueue[0].Time : double.PositiveInfinity;
                double next = Math.Min(spawnAt, Math.Min(autoAt, manualAt));
                if (next > t || double.IsPositiveInfinity(next)) break;
                double at = Math.Max(Now, next);
                TickAll(at);
                Now = at;
                if (next == spawnAt)
                {
                    Spawn(waiting[waitingCursor], at);
                    waitingCursor++;
                }
                else if (next == autoAt)
                {
                    AutoInput input = autoQueue[0];
                    autoQueue.RemoveAt(0);
                    if (input.Note == null || input.Note.State != SimNoteState.Done || !input.Press)
                    {
                        if (input.Press) { autoHeld[input.Lane] = true; DoPress(input.Lane, at, input.Note); }
                        else { autoHeld[input.Lane] = false; DoRelease(input.Lane, at); }
                    }
                }
                else
                {
                    AutoInput input = manualQueue[0];
                    manualQueue.RemoveAt(0);
                    if (input.Press) { keyHeld[input.Lane] = true; DoPress(input.Lane, at, null); }
                    else { keyHeld[input.Lane] = false; DoRelease(input.Lane, at); }
                }
            }

            TickAll(t);
            Now = t;
            if (!AutoPlay && autoQueue.Count > 0)
            {
                // Drop stale auto inputs so turning auto-hit on later does not replay the past.
                autoQueue.RemoveAll(a => a.Time <= t);
                for (int i = 1; i <= MaxLanes; i++) autoHeld[i] = false;
            }
        }

        private void TickAll(double t)
        {
            for (int i = 0; i < notes.Count; i++)
            {
                SimNote n = notes[i];
                if (n.State == SimNoteState.Active || n.State == SimNoteState.Holding) n.Behaviour.Tick(n, t, this);
            }
        }

        private void Spawn(SimNote n, double at)
        {
            if (n.State != SimNoteState.Waiting) return;
            n.State = SimNoteState.Active;
            n.Behaviour.OnSpawn(n, this);
            if (n.State == SimNoteState.Done) return;
            var list = new List<AutoInput>();
            n.Behaviour.BuildAutoInputs(n, this, list);
            foreach (AutoInput a in list)
                if (a.Time >= at - 1e-9) InsertAuto(a);
        }

        private void InsertAuto(AutoInput a)
        {
            int lo = 0, hi = autoQueue.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (autoQueue[mid].Time <= a.Time) lo = mid + 1; else hi = mid;
            }

            autoQueue.Insert(lo, a);
        }

        private void DoPress(int lane, double t, SimNote preferred)
        {
            lastPressAt[lane] = t;
            Stats.Presses++;
            Emit(SimEventKind.KeyPress, null, t, Judgement.None, "", lane);
            SimNote best = null;
            double bestPriority = double.PositiveInfinity;
            for (int i = 0; i < notes.Count; i++)
            {
                SimNote n = notes[i];
                if (n.Lane != lane || n.State != SimNoteState.Active) continue;
                double priority;
                if (!n.Behaviour.CanTakePress(n, t, this, out priority)) continue;
                if (n == preferred) priority -= 1e6; // auto-hit targets the note it was scheduled for
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    best = n;
                }
            }

            if (best != null) best.Behaviour.OnPress(best, t, this);
        }

        private void DoRelease(int lane, double t)
        {
            Emit(SimEventKind.KeyRelease, null, t, Judgement.None, "", lane);
            if (keyHeld[lane] || autoHeld[lane]) return; // the other input source still holds the lane
            for (int i = 0; i < notes.Count; i++)
            {
                SimNote n = notes[i];
                if (n.Lane == lane && n.State == SimNoteState.Holding) n.Behaviour.OnRelease(n, t, this);
            }
        }

        // ------------------------------------------------------------------ ISimContext

        public void Resolve(SimNote note, Judgement judgement, double time, string text, double error = 0d)
        {
            if (note.State == SimNoteState.Done) return;
            note.State = SimNoteState.Done;
            note.Result = judgement;
            note.ResultTime = time;
            note.ResultText = text ?? "";
            note.ErrorSeconds = error;
            bool sequence = note.Def != null && note.Def.Output == NoteOutput.Sequence;
            if (!sequence)
            {
                bool damages = judgement == Judgement.Miss || (judgement == Judgement.Bad && text == "Released early" && note.Def != null && note.Def.Archetype == Archetypes.StationaryHold);
                Stats.Add(judgement, damages ? note.Damage : 0);
            }

            Emit(SimEventKind.Judged, note, time, judgement, note.ResultText, note.Lane);
            if (note.Parent != null) note.Parent.Behaviour.OnChildResolved(note.Parent, note, time, this);
        }

        public void Emit(SimEventKind kind, SimNote note, double time, Judgement judgement, string text)
        {
            Emit(kind, note, time, judgement, text, note != null ? note.Lane : 0);
        }

        private void Emit(SimEventKind kind, SimNote note, double time, Judgement judgement, string text, int lane)
        {
            if (events.Count > 2000) events.RemoveRange(0, 1000);
            events.Add(new SimEvent
            {
                Kind = kind,
                Time = time,
                Lane = lane,
                StepIndex = note != null ? note.StepIndex : -1,
                Judgement = judgement,
                Text = text ?? "",
                Note = note
            });
        }

        public SimNote SpawnChild(SimNote parent, int lane, double spawnTime, double hitTime, string archetype, int damage)
        {
            NoteTypeDef shotDef = new NoteTypeDef
            {
                Id = parent.Def.Id + ".shot",
                Name = parent.Def.Name + " shot",
                Color = parent.Def.Color,
                Shape = NoteShape.Circle,
                Archetype = archetype,
                Category = parent.Def.Category
            };
            var child = new SimNote
            {
                Source = new LevelNote { Id = parent.Id + ":" + parent.Children.Count, Type = shotDef.Id, Lane = lane },
                Def = shotDef,
                Behaviour = SimBehaviours.Create(archetype),
                StepIndex = parent.StepIndex,
                StepZero = parent.StepZero,
                Lane = Math.Max(1, Math.Min(MaxLanes, lane)),
                SpawnTime = spawnTime,
                HitTime = hitTime,
                EndTime = hitTime,
                TravelSeconds = Math.Max(0.01d, hitTime - spawnTime),
                Damage = damage,
                Parent = parent,
                State = SimNoteState.Waiting
            };
            parent.Children.Add(child);
            notes.Add(child);
            child.Behaviour.Setup(child, this);
            Spawn(child, spawnTime);
            return child;
        }

        /// <summary>Takes the events raised since the last call (judgements, key presses...).</summary>
        public List<SimEvent> DrainEvents()
        {
            var copy = new List<SimEvent>(events);
            events.Clear();
            return copy;
        }

        /// <summary>True when every (non-skipped) note is resolved.</summary>
        public bool AllResolved
        {
            get
            {
                foreach (SimNote n in notes) if (n.State != SimNoteState.Done) return false;
                return true;
            }
        }
    }
}
