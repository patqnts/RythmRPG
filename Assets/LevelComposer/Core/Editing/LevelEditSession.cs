using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;

namespace RythmRPG.LevelComposer.Editing
{
    [Flags]
    public enum ChangeKind
    {
        None = 0,
        Notes = 1,
        Selection = 2,
        Steps = 4,
        Music = 8,
        Meta = 16,
        File = 32,
        /// <summary>Another step was selected (its notes are now the ones on show).</summary>
        CurrentStep = 64,
        All = Notes | Selection | Steps | Music | Meta | File | CurrentStep
    }

    /// <summary>
    /// The open level plus everything needed to edit it: current step, selection, clipboard and undo/redo.
    /// Undo stores whole-level snapshots (levels are small), which makes every operation reversible without
    /// per-operation undo code. Pure C#; the UI only calls these methods and listens to <see cref="Changed"/>.
    /// </summary>
    public sealed class LevelEditSession
    {
        private sealed class Snapshot
        {
            public string Label;
            public CombatLevel Level;
            public int StepIndex;
            public List<string> Selection;
        }

        public const int MaxUndo = 300;

        private readonly List<Snapshot> undo = new List<Snapshot>();
        private readonly List<Snapshot> redo = new List<Snapshot>();
        private readonly HashSet<string> selection = new HashSet<string>();
        private List<LevelNote> clipboard = new List<LevelNote>();
        private Snapshot gesture;
        private int stepIndex;

        public NoteTypeRegistry Types { get; private set; }
        public CombatLevel Level { get; private set; }
        /// <summary>Full path of the level file, or "" for a new unsaved level.</summary>
        public string FilePath { get; set; }
        public bool Dirty { get; private set; }
        public int SnapDivision = 4;
        public bool SnapEnabled = true;

        /// <summary>Raised after every change. The flags say what changed so views can refresh only what they show.</summary>
        public event Action<ChangeKind> Changed;

        public LevelEditSession(NoteTypeRegistry types)
        {
            Types = types ?? NoteTypeRegistry.CreateDefault();
            Level = CombatLevel.CreateNew();
            FilePath = "";
        }

        // ------------------------------------------------------------------ state

        public int StepIndex { get { return stepIndex; } }
        public LevelStep Step { get { return Level.Steps[Math.Max(0, Math.Min(stepIndex, Level.Steps.Count - 1))]; } }
        public LevelTempo Tempo { get { return LevelTempo.Of(Level); } }
        public IEnumerable<string> Selection { get { return selection; } }
        public int SelectionCount { get { return selection.Count; } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public string UndoLabel { get { return undo.Count > 0 ? undo[undo.Count - 1].Label : ""; } }
        public string RedoLabel { get { return redo.Count > 0 ? redo[redo.Count - 1].Label : ""; } }
        public bool HasClipboard { get { return clipboard.Count > 0; } }
        public bool IsSelected(string id) { return selection.Contains(id); }

        public List<LevelNote> SelectedNotes()
        {
            var list = new List<LevelNote>();
            foreach (LevelNote n in Step.Notes)
                if (selection.Contains(n.Id)) list.Add(n);
            return list;
        }

        public NoteTypeDef TypeOf(LevelNote note) { return note == null ? null : Types.Find(note.Type); }

        public double Snap(double beat)
        {
            return SnapEnabled ? LevelTempo.Snap(beat, SnapDivision) : beat;
        }

        // ------------------------------------------------------------------ file

        public void Load(CombatLevel level, string path)
        {
            Level = level ?? CombatLevel.CreateNew();
            if (Level.Steps.Count == 0) Level.Steps.Add(new LevelStep { Name = "Step 1" });
            FilePath = path ?? "";
            stepIndex = 0;
            selection.Clear();
            undo.Clear();
            redo.Clear();
            gesture = null;
            Dirty = false;
            Raise(ChangeKind.All);
        }

        public void MarkSaved(string path)
        {
            FilePath = path ?? FilePath;
            Dirty = false;
            Raise(ChangeKind.File);
        }

        // ------------------------------------------------------------------ undo

        /// <summary>Records the current state for undo. Call before a change; <see cref="Commit"/> after it.</summary>
        private void Record(string label)
        {
            if (gesture != null) return; // a drag already recorded its starting state
            PushUndo(TakeSnapshot(label));
        }

        private Snapshot TakeSnapshot(string label)
        {
            return new Snapshot { Label = label, Level = Level.Clone(), StepIndex = stepIndex, Selection = new List<string>(selection) };
        }

        private void PushUndo(Snapshot s)
        {
            undo.Add(s);
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
            redo.Clear();
        }

        private void Commit(ChangeKind kind)
        {
            Dirty = true;
            Raise(kind);
        }

        /// <summary>Starts a continuous edit (e.g. a drag): one undo step covers everything until <see cref="EndGesture"/>.</summary>
        public void BeginGesture(string label)
        {
            if (gesture != null) EndGesture();
            gesture = TakeSnapshot(label);
        }

        public void EndGesture()
        {
            if (gesture == null) return;
            Snapshot g = gesture;
            gesture = null;
            if (!LevelsEqual(g.Level, Level)) PushUndo(g);
        }

        /// <summary>Cancels a gesture and restores the state it started from.</summary>
        public void CancelGesture()
        {
            if (gesture == null) return;
            Restore(gesture);
            gesture = null;
            Raise(ChangeKind.All);
        }

        public bool InGesture { get { return gesture != null; } }

        public void Undo()
        {
            if (gesture != null) EndGesture();
            if (undo.Count == 0) return;
            Snapshot s = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            redo.Add(TakeSnapshot(s.Label));
            Restore(s);
            Dirty = true;
            Raise(ChangeKind.All);
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            Snapshot s = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            undo.Add(TakeSnapshot(s.Label));
            Restore(s);
            Dirty = true;
            Raise(ChangeKind.All);
        }

        private void Restore(Snapshot s)
        {
            Level = s.Level.Clone();
            stepIndex = Math.Max(0, Math.Min(s.StepIndex, Level.Steps.Count - 1));
            selection.Clear();
            foreach (string id in s.Selection) selection.Add(id);
            PruneSelection();
        }

        private static bool LevelsEqual(CombatLevel a, CombatLevel b)
        {
            return LevelSerializer.ToJson(a) == LevelSerializer.ToJson(b);
        }

        // ------------------------------------------------------------------ level / music / meta

        /// <summary>Applies any change to the level (name, music, preview settings...) as one undo step.</summary>
        public void EditLevel(string label, Action<CombatLevel> mutate, ChangeKind kind = ChangeKind.Meta)
        {
            if (mutate == null) return;
            Record(label);
            mutate(Level);
            Commit(kind);
        }

        /// <summary>Changes the tempo. Notes keep their beats (they move in time with the grid, like the game's composer).</summary>
        public void SetTempo(double bpm, int beatsPerBar)
        {
            bpm = Math.Max(1d, Math.Min(999d, bpm));
            beatsPerBar = Math.Max(1, Math.Min(16, beatsPerBar));
            if (Math.Abs(bpm - Level.Music.Bpm) < 1e-9 && beatsPerBar == Level.Music.BeatsPerBar) return;
            EditLevel("Change tempo", l => { l.Music.Bpm = bpm; l.Music.BeatsPerBar = beatsPerBar; }, ChangeKind.Music | ChangeKind.Notes);
        }

        // ------------------------------------------------------------------ steps

        public void SelectStep(int index)
        {
            index = Math.Max(0, Math.Min(index, Level.Steps.Count - 1));
            if (index == stepIndex) return;
            if (gesture != null) EndGesture();
            stepIndex = index;
            selection.Clear();
            Raise(ChangeKind.CurrentStep | ChangeKind.Selection);
        }

        public void AddStep()
        {
            Record("Add step");
            var s = new LevelStep { Name = "Step " + (Level.Steps.Count + 1), LaneCount = Step.LaneCount, Animation = Step.Animation, Anticipation = Step.Anticipation };
            Level.Steps.Insert(stepIndex + 1, s);
            stepIndex++;
            selection.Clear();
            Commit(ChangeKind.Steps | ChangeKind.Notes | ChangeKind.Selection);
        }

        public void DuplicateStep()
        {
            Record("Duplicate step");
            LevelStep copy = Step.CloneWithNewIds();
            copy.Name = Step.Name + " copy";
            Level.Steps.Insert(stepIndex + 1, copy);
            stepIndex++;
            selection.Clear();
            Commit(ChangeKind.Steps | ChangeKind.Notes | ChangeKind.Selection);
        }

        public void RemoveStep()
        {
            if (Level.Steps.Count <= 1)
            {
                // Keep one step: clear it instead.
                if (Step.Notes.Count == 0) return;
                Record("Clear step");
                Step.Notes.Clear();
                selection.Clear();
                Commit(ChangeKind.Notes | ChangeKind.Selection);
                return;
            }

            Record("Delete step");
            Level.Steps.RemoveAt(stepIndex);
            stepIndex = Math.Max(0, Math.Min(stepIndex, Level.Steps.Count - 1));
            selection.Clear();
            Commit(ChangeKind.Steps | ChangeKind.Notes | ChangeKind.Selection);
        }

        public void MoveStep(int direction)
        {
            int target = stepIndex + Math.Sign(direction);
            if (direction == 0 || target < 0 || target >= Level.Steps.Count) return;
            Record("Reorder steps");
            LevelStep s = Level.Steps[stepIndex];
            Level.Steps.RemoveAt(stepIndex);
            Level.Steps.Insert(target, s);
            stepIndex = target;
            Commit(ChangeKind.Steps);
        }

        public void EditStep(string label, Action<LevelStep> mutate)
        {
            if (mutate == null) return;
            Record(label);
            mutate(Step);
            Commit(ChangeKind.Steps);
        }

        /// <summary>Sets the step's lane count (1..4). Notes on removed lanes move to the new last lane.</summary>
        public int SetLaneCount(int count)
        {
            count = Math.Max(1, Math.Min(LevelStep.MaxLanes, count));
            if (count == Step.LaneCount) return 0;
            Record("Change lane count");
            int moved = 0;
            foreach (LevelNote n in Step.Notes)
            {
                if (n.Lane > count) { n.Lane = count; moved++; }
            }

            Step.LaneCount = count;
            Commit(ChangeKind.Steps | ChangeKind.Notes);
            return moved;
        }

        // ------------------------------------------------------------------ selection

        public void SelectOnly(string id)
        {
            selection.Clear();
            if (id != null && Step.Find(id) != null) selection.Add(id);
            Raise(ChangeKind.Selection);
        }

        public void Toggle(string id)
        {
            if (!selection.Remove(id) && Step.Find(id) != null) selection.Add(id);
            Raise(ChangeKind.Selection);
        }

        public void SelectAll()
        {
            selection.Clear();
            foreach (LevelNote n in Step.Notes) selection.Add(n.Id);
            Raise(ChangeKind.Selection);
        }

        public void ClearSelection()
        {
            if (selection.Count == 0) return;
            selection.Clear();
            Raise(ChangeKind.Selection);
        }

        /// <summary>Selects notes whose span touches [beatA, beatB] on lanes laneA..laneB (1-based, inclusive).</summary>
        public void SelectRect(double beatA, double beatB, int laneA, int laneB, bool additive)
        {
            if (!additive) selection.Clear();
            double b0 = Math.Min(beatA, beatB), b1 = Math.Max(beatA, beatB);
            int l0 = Math.Min(laneA, laneB), l1 = Math.Max(laneA, laneB);
            foreach (LevelNote n in Step.Notes)
                if (n.Lane >= l0 && n.Lane <= l1 && n.EndBeat >= b0 && n.Beat <= b1) selection.Add(n.Id);
            Raise(ChangeKind.Selection);
        }

        public void SelectOfType(string typeId)
        {
            selection.Clear();
            foreach (LevelNote n in Step.Notes)
                if (n.Type == typeId) selection.Add(n.Id);
            Raise(ChangeKind.Selection);
        }

        private void PruneSelection()
        {
            selection.RemoveWhere(id => Step.Find(id) == null);
        }

        // ------------------------------------------------------------------ notes

        /// <summary>Adds a note of a registered type at a (snapped) beat and selects it.</summary>
        public LevelNote AddNote(string typeId, int lane, double beat, double length = -1d)
        {
            NoteTypeDef def = Types.Find(typeId);
            if (def == null) return null;
            Record("Add " + def.Name);
            var n = new LevelNote
            {
                Type = def.Id,
                Lane = Math.Max(1, Math.Min(Step.LaneCount, lane)),
                Beat = Math.Max(0d, Snap(beat)),
                Length = def.HasLength ? (length >= 0d ? length : def.DefaultLengthBeats) : 0d
            };
            Step.Notes.Add(n);
            selection.Clear();
            selection.Add(n.Id);
            Commit(ChangeKind.Notes | ChangeKind.Selection);
            return n;
        }

        /// <summary>Adds many notes (pattern stamps, imports) as one undo step and selects them.</summary>
        public void AddNotes(IList<LevelNote> notes, string label)
        {
            if (notes == null || notes.Count == 0) return;
            Record(label);
            selection.Clear();
            foreach (LevelNote src in notes)
            {
                LevelNote n = src.Clone();
                if (string.IsNullOrEmpty(n.Id) || Step.Find(n.Id) != null) n.Id = LevelNote.NewId();
                n.Lane = Math.Max(1, Math.Min(Step.LaneCount, n.Lane));
                n.Beat = Math.Max(0d, n.Beat);
                Step.Notes.Add(n);
                selection.Add(n.Id);
            }

            Commit(ChangeKind.Notes | ChangeKind.Selection);
        }

        public void DeleteSelected()
        {
            if (selection.Count == 0) return;
            Record(selection.Count == 1 ? "Delete note" : "Delete notes");
            Step.Notes.RemoveAll(n => selection.Contains(n.Id));
            selection.Clear();
            Commit(ChangeKind.Notes | ChangeKind.Selection);
        }

        public void DeleteNote(string id)
        {
            LevelNote n = Step.Find(id);
            if (n == null) return;
            Record("Delete note");
            Step.Notes.Remove(n);
            selection.Remove(id);
            Commit(ChangeKind.Notes | ChangeKind.Selection);
        }

        /// <summary>Clamps a move so the selection stays at beat &gt;= 0 and inside the lanes.</summary>
        public void ClampMove(double deltaBeats, int deltaLanes, out double beats, out int lanes)
        {
            double minBeat = double.MaxValue;
            int minLane = int.MaxValue, maxLane = int.MinValue;
            foreach (LevelNote n in Step.Notes)
            {
                if (!selection.Contains(n.Id)) continue;
                minBeat = Math.Min(minBeat, n.Beat);
                minLane = Math.Min(minLane, n.Lane);
                maxLane = Math.Max(maxLane, n.Lane);
            }

            beats = deltaBeats;
            lanes = deltaLanes;
            if (minBeat == double.MaxValue) return;
            if (minBeat + beats < 0d) beats = -minBeat;
            if (minLane + lanes < 1) lanes = 1 - minLane;
            if (maxLane + lanes > Step.LaneCount) lanes = Step.LaneCount - maxLane;
        }

        /// <summary>Moves the selection by beats and lanes (clamped). Inside a gesture no extra undo step is made.</summary>
        public void MoveSelected(double deltaBeats, int deltaLanes)
        {
            if (selection.Count == 0) return;
            double db;
            int dl;
            ClampMove(deltaBeats, deltaLanes, out db, out dl);
            if (Math.Abs(db) < 1e-12 && dl == 0) return;
            Record("Move notes");
            foreach (LevelNote n in Step.Notes)
            {
                if (!selection.Contains(n.Id)) continue;
                n.Beat = Math.Max(0d, n.Beat + db);
                n.Lane += dl;
            }

            Commit(ChangeKind.Notes);
        }

        /// <summary>Sets the selection's positions from a drag origin (absolute, so rounding never accumulates during a drag).</summary>
        public void SetPositions(IDictionary<string, double> beats, IDictionary<string, int> lanes)
        {
            Record("Move notes");
            foreach (LevelNote n in Step.Notes)
            {
                double b;
                int l;
                if (beats != null && beats.TryGetValue(n.Id, out b)) n.Beat = Math.Max(0d, b);
                if (lanes != null && lanes.TryGetValue(n.Id, out l)) n.Lane = Math.Max(1, Math.Min(Step.LaneCount, l));
            }

            Commit(ChangeKind.Notes);
        }

        public void SetLength(string id, double lengthBeats)
        {
            LevelNote n = Step.Find(id);
            NoteTypeDef def = TypeOf(n);
            if (n == null || def == null || !def.HasLength) return;
            double v = Math.Max(0d, lengthBeats);
            if (Math.Abs(v - n.Length) < 1e-12) return;
            Record("Change length");
            n.Length = v;
            Commit(ChangeKind.Notes);
        }

        /// <summary>Applies a change to every selected note as one undo step.</summary>
        public void ModifySelected(string label, Action<LevelNote, NoteTypeDef> mutate)
        {
            if (selection.Count == 0 || mutate == null) return;
            Record(label);
            foreach (LevelNote n in Step.Notes)
            {
                if (!selection.Contains(n.Id)) continue;
                mutate(n, TypeOf(n));
                n.Beat = Math.Max(0d, n.Beat);
                n.Length = Math.Max(0d, n.Length);
                n.Lane = Math.Max(1, Math.Min(Step.LaneCount, n.Lane));
            }

            Commit(ChangeKind.Notes);
        }

        /// <summary>Sets a parameter on every selected note that has it (others are skipped).</summary>
        public void SetParamOnSelected(string key, object value)
        {
            LevelTempo tempo = Tempo;
            ModifySelected("Change " + key, (n, def) =>
            {
                if (def != null && def.FindParam(key) == null) return;
                NoteParams.Set(n, def, key, value, tempo);
            });
        }

        /// <summary>Removes a parameter override on the selected notes (back to the type default).</summary>
        public void ResetParamOnSelected(string key)
        {
            ModifySelected("Reset " + key, (n, def) => n.Params.Remove(key));
        }

        /// <summary>Changes the type of the selected notes. Parameters the new type does not have are dropped.</summary>
        public void SetTypeOfSelected(string typeId)
        {
            NoteTypeDef def = Types.Find(typeId);
            if (def == null) return;
            ModifySelected("Change type", (n, old) =>
            {
                n.Type = def.Id;
                var keep = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> kv in n.Params)
                    if (def.FindParam(kv.Key) != null) keep[kv.Key] = kv.Value;
                n.Params = keep;
                if (!def.HasLength) n.Length = 0d;
                else if (n.Length <= 0d) n.Length = def.DefaultLengthBeats;
            });
        }

        public void QuantizeSelected()
        {
            int div = Math.Max(1, SnapDivision);
            ModifySelected("Quantize", (n, def) =>
            {
                n.Beat = LevelTempo.Snap(n.Beat, div);
                if (n.Length > 0d) n.Length = Math.Max(1d / div, LevelTempo.Snap(n.Length, div));
            });
        }

        /// <summary>Mirrors the selection's lanes (1 &lt;-&gt; N).</summary>
        public void MirrorSelected()
        {
            int count = Step.LaneCount;
            ModifySelected("Mirror lanes", (n, def) => n.Lane = count + 1 - n.Lane);
        }

        // ------------------------------------------------------------------ clipboard

        public void Copy()
        {
            clipboard = new List<LevelNote>();
            foreach (LevelNote n in SelectedNotes()) clipboard.Add(n.Clone());
        }

        public void Cut()
        {
            Copy();
            if (clipboard.Count == 0) return;
            Record("Cut notes");
            Step.Notes.RemoveAll(n => selection.Contains(n.Id));
            selection.Clear();
            Commit(ChangeKind.Notes | ChangeKind.Selection);
        }

        /// <summary>Pastes so the earliest copied note lands on <paramref name="atBeat"/> (snapped). Pasted notes become the selection.</summary>
        public void Paste(double atBeat)
        {
            if (clipboard.Count == 0) return;
            double min = double.MaxValue;
            foreach (LevelNote n in clipboard) min = Math.Min(min, n.Beat);
            double shift = Snap(Math.Max(0d, atBeat)) - min;
            var copies = new List<LevelNote>();
            foreach (LevelNote n in clipboard)
            {
                LevelNote c = n.CloneWithNewId();
                c.Beat = Math.Max(0d, c.Beat + shift);
                copies.Add(c);
            }

            AddNotes(copies, "Paste notes");
        }

        /// <summary>Duplicates the selection right after itself.</summary>
        public void DuplicateSelected()
        {
            List<LevelNote> sel = SelectedNotes();
            if (sel.Count == 0) return;
            double min = double.MaxValue, max = double.MinValue;
            foreach (LevelNote n in sel)
            {
                min = Math.Min(min, n.Beat);
                max = Math.Max(max, n.EndBeat);
            }

            double step = 1d / Math.Max(1, SnapDivision);
            double span = Math.Max(max - min, 0d);
            span = Math.Max(step, Math.Ceiling(span / step - 1e-9) * step);
            if (Math.Abs(max - min) < 1e-9) span = step;
            var copies = new List<LevelNote>();
            foreach (LevelNote n in sel)
            {
                LevelNote c = n.CloneWithNewId();
                c.Beat = n.Beat + span;
                copies.Add(c);
            }

            AddNotes(copies, "Duplicate notes");
        }

        private void Raise(ChangeKind kind)
        {
            Action<ChangeKind> h = Changed;
            if (h != null) h(kind);
        }
    }
}
