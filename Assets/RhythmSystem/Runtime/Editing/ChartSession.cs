using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm.Editing
{
    /// <summary>Reversible edit expressed as before/after snapshots of the notes it touched.</summary>
    public sealed class EditCommand
    {
        public string Label;
        public List<NoteInstance> Before = new List<NoteInstance>();
        public List<NoteInstance> After = new List<NoteInstance>();
        public List<PatternInstance> PatternBefore = new List<PatternInstance>();
        public List<PatternInstance> PatternAfter = new List<PatternInstance>();
    }

    /// <summary>
    /// Editor-side model of one chart: beat-based notes, selection, clipboard, undo/redo.
    /// Pure C# (no UnityEngine) so it is unit-testable. Times are beats.
    /// </summary>
    public sealed class ChartSession
    {
        private readonly List<NoteInstance> notes = new List<NoteInstance>();
        private readonly List<PatternInstance> patterns = new List<PatternInstance>();
        private readonly HashSet<string> selection = new HashSet<string>();
        private readonly List<EditCommand> undoStack = new List<EditCommand>();
        private readonly List<EditCommand> redoStack = new List<EditCommand>();
        private List<NoteInstance> clipboard = new List<NoteInstance>();

        public TempoMap Tempo { get; set; }
        public IList<string> LaneIds { get; private set; }
        public int SnapDivision { get; set; }
        public bool SnapEnabled { get; set; }
        public bool Dirty { get; set; }
        public event Action Changed;

        public ChartSession(TempoMap tempo, IList<string> laneIds)
        {
            Tempo = tempo;
            LaneIds = laneIds;
            SnapDivision = 4;
            SnapEnabled = true;
        }

        public IList<NoteInstance> Notes { get { return notes.AsReadOnly(); } }
        public IList<PatternInstance> Patterns { get { return patterns.AsReadOnly(); } }

        public PatternInstance FindPattern(string id)
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                if (patterns[i].Id == id) return patterns[i];
            }

            return null;
        }

        /// <summary>The selected pattern when exactly one item is selected and it is a pattern; otherwise null.</summary>
        public PatternInstance SelectedPattern
        {
            get
            {
                if (selection.Count != 1) return null;
                foreach (string id in selection) return FindPattern(id);
                return null;
            }
        }
        public IEnumerable<string> Selection { get { return selection; } }
        public int SelectionCount { get { return selection.Count; } }
        public bool CanUndo { get { return undoStack.Count > 0; } }
        public bool CanRedo { get { return redoStack.Count > 0; } }
        public bool HasClipboard { get { return clipboard.Count > 0; } }

        public NoteInstance Find(string id)
        {
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Id == id) return notes[i];
            }

            return null;
        }

        public bool IsSelected(string id) { return selection.Contains(id); }

        public double Snap(double beat)
        {
            return SnapEnabled ? Tempo.SnapBeat(beat, SnapDivision) : beat;
        }

        // ---- loading ----

        public void Load(IEnumerable<NoteInstance> source, IEnumerable<PatternInstance> patternSource = null)
        {
            notes.Clear();
            patterns.Clear();
            if (patternSource != null)
            {
                foreach (PatternInstance p in patternSource) patterns.Add(p.Clone());
            }

            selection.Clear();
            undoStack.Clear();
            redoStack.Clear();
            foreach (NoteInstance n in source) notes.Add(n);
            Dirty = false;
            RaiseChanged();
        }

        // ---- selection ----

        public void SelectOnly(string id)
        {
            selection.Clear();
            if (id != null && (Find(id) != null || FindPattern(id) != null)) selection.Add(id);
            RaiseChanged();
        }

        public void Toggle(string id)
        {
            if (!selection.Remove(id) && (Find(id) != null || FindPattern(id) != null)) selection.Add(id);
            RaiseChanged();
        }

        public void SelectAll()
        {
            selection.Clear();
            for (int i = 0; i < notes.Count; i++) selection.Add(notes[i].Id);
            RaiseChanged();
        }

        public void ClearSelection()
        {
            selection.Clear();
            RaiseChanged();
        }

        /// <summary>Selects every note whose hit beat is in [beatA, beatB] and lane index in [laneA, laneB].</summary>
        public void SelectRect(double beatA, double beatB, int laneA, int laneB, bool additive)
        {
            if (!additive) selection.Clear();
            double b0 = Math.Min(beatA, beatB), b1 = Math.Max(beatA, beatB);
            int l0 = Math.Min(laneA, laneB), l1 = Math.Max(laneA, laneB);
            for (int i = 0; i < notes.Count; i++)
            {
                NoteInstance n = notes[i];
                int lane = LaneIndex(n.LaneId);
                double end = n.HitBeat + Math.Max(0d, n.HoldBeats);
                if (lane >= l0 && lane <= l1 && end >= b0 && n.HitBeat <= b1) selection.Add(n.Id);
            }

            RaiseChanged();
        }

        public int LaneIndex(string laneId)
        {
            for (int i = 0; i < LaneIds.Count; i++)
            {
                if (LaneIds[i] == laneId) return i;
            }

            return -1;
        }

        // ---- editing operations (each is one undo step) ----

        public NoteInstance AddNote(string laneId, double beat, string definitionId, double holdBeats = 0d)
        {
            var n = new NoteInstance
            {
                Id = Guid.NewGuid().ToString("N"),
                LaneId = laneId,
                DefinitionId = definitionId,
                HitBeat = Math.Max(0d, Snap(beat)),
                HoldBeats = holdBeats
            };
            var cmd = new EditCommand { Label = "Add note" };
            cmd.After.Add(n);
            Execute(cmd);
            selection.Clear();
            selection.Add(n.Id);
            RaiseChanged();
            return Find(n.Id);
        }

        public void DeleteSelected()
        {
            if (selection.Count == 0) return;
            var cmd = new EditCommand { Label = "Delete notes" };
            foreach (string id in selection)
            {
                NoteInstance n = Find(id);
                if (n != null) cmd.Before.Add(n.Clone());
                PatternInstance p = FindPattern(id);
                if (p != null) cmd.PatternBefore.Add(p.Clone());
            }

            if (cmd.Before.Count == 0 && cmd.PatternBefore.Count == 0) return;

            selection.Clear();
            Execute(cmd);
        }

        /// <summary>Clamps a proposed move so the selection stays at beat >= 0 and inside the lane range.</summary>
        public void ClampMoveDelta(double deltaBeats, int deltaLanes, out double clampedBeats, out int clampedLanes)
        {
            double minBeat = double.MaxValue;
            int minLane = int.MaxValue, maxLane = int.MinValue;
            foreach (string id in selection)
            {
                NoteInstance n = Find(id);
                if (n == null) continue;
                minBeat = Math.Min(minBeat, n.HitBeat);
                int li = LaneIndex(n.LaneId);
                minLane = Math.Min(minLane, li);
                maxLane = Math.Max(maxLane, li);
            }

            clampedBeats = deltaBeats;
            clampedLanes = deltaLanes;
            if (minBeat == double.MaxValue) return;
            if (minBeat + clampedBeats < 0d) clampedBeats = -minBeat;
            if (minLane + clampedLanes < 0) clampedLanes = -minLane;
            if (maxLane + clampedLanes >= LaneIds.Count) clampedLanes = LaneIds.Count - 1 - maxLane;
        }

        /// <summary>Moves the selection by beats and lanes. Beat delta is applied as-is (caller snaps); the move is clamped.</summary>
        public void MoveSelected(double deltaBeats, int deltaLanes)
        {
            if (selection.Count == 0) return;
            double db;
            int dl;
            ClampMoveDelta(deltaBeats, deltaLanes, out db, out dl);
            if (db == 0d && dl == 0) return;

            var cmd = new EditCommand { Label = "Move notes" };
            foreach (string id in selection)
            {
                NoteInstance n = Find(id);
                if (n == null) continue;
                cmd.Before.Add(n.Clone());
                NoteInstance moved = n.Clone();
                moved.HitBeat = n.HitBeat + db;
                moved.LaneId = LaneIds[LaneIndex(n.LaneId) + dl];
                cmd.After.Add(moved);
            }

            Execute(cmd);
        }

        /// <summary>Applies an arbitrary change to every selected note as one undo step.</summary>
        public void ModifySelected(string label, Action<NoteInstance> mutate)
        {
            if (selection.Count == 0 || mutate == null) return;
            var cmd = new EditCommand { Label = label };
            foreach (string id in selection)
            {
                NoteInstance n = Find(id);
                if (n == null) continue;
                cmd.Before.Add(n.Clone());
                NoteInstance c = n.Clone();
                mutate(c);
                c.HitBeat = Math.Max(0d, c.HitBeat);
                c.HoldBeats = Math.Max(0d, c.HoldBeats);
                cmd.After.Add(c);
            }

            if (cmd.Before.Count > 0) Execute(cmd);
        }

        public void SetHoldBeats(string id, double holdBeats)
        {
            NoteInstance n = Find(id);
            if (n == null) return;
            double value = Math.Max(0d, holdBeats);
            if (value == n.HoldBeats) return;
            var cmd = new EditCommand { Label = "Resize hold" };
            cmd.Before.Add(n.Clone());
            NoteInstance c = n.Clone();
            c.HoldBeats = value;
            cmd.After.Add(c);
            Execute(cmd);
        }

        /// <summary>Drags one lifecycle handle (travel lead-in, hold end, stationary windows) of a note to a beat as one undo step.</summary>
        public void SetNoteHandle(string id, NoteHandleKind kind, double beat, NoteHandleDefaults defaults)
        {
            NoteInstance n = Find(id);
            if (n == null) return;
            NoteInstance c = n.Clone();
            NoteHandles.Apply(kind, c, beat, defaults ?? new NoteHandleDefaults(), Tempo);
            if (c.HoldBeats == n.HoldBeats
                && c.TravelBeats.HasValue == n.TravelBeats.HasValue && c.TravelBeats.Value == n.TravelBeats.Value
                && c.StationaryBadWindow.Value == n.StationaryBadWindow.Value && c.StationaryBadWindow.HasValue == n.StationaryBadWindow.HasValue
                && c.StationaryGoodWindow.Value == n.StationaryGoodWindow.Value && c.StationaryGoodWindow.HasValue == n.StationaryGoodWindow.HasValue
                && c.StationaryPerfectWindow.Value == n.StationaryPerfectWindow.Value && c.StationaryPerfectWindow.HasValue == n.StationaryPerfectWindow.HasValue) return;
            var cmd = new EditCommand { Label = "Edit " + kind };
            cmd.Before.Add(n.Clone());
            cmd.After.Add(c);
            Execute(cmd);
        }

        // ---- clipboard ----

        public void Copy()
        {
            clipboard = new List<NoteInstance>();
            foreach (string id in selection)
            {
                NoteInstance n = Find(id);
                if (n != null) clipboard.Add(n.Clone());
            }
        }

        public void Cut()
        {
            Copy();
            DeleteSelected();
        }

        /// <summary>Pastes so the earliest copied note lands on <paramref name="atBeat"/>. New ids; the pasted notes become the selection.</summary>
        public void Paste(double atBeat)
        {
            if (clipboard.Count == 0) return;
            double min = double.MaxValue;
            for (int i = 0; i < clipboard.Count; i++) min = Math.Min(min, clipboard[i].HitBeat);
            double shift = Snap(atBeat) - min;
            var cmd = new EditCommand { Label = "Paste notes" };
            var newIds = new List<string>();
            for (int i = 0; i < clipboard.Count; i++)
            {
                NoteInstance c = clipboard[i].CloneWithNewId();
                c.HitBeat = Math.Max(0d, c.HitBeat + shift);
                cmd.After.Add(c);
                newIds.Add(c.Id);
            }

            Execute(cmd);
            selection.Clear();
            for (int i = 0; i < newIds.Count; i++) selection.Add(newIds[i]);
            RaiseChanged();
        }

        /// <summary>Duplicates the selection immediately after itself (span of the selection) and selects the copies.</summary>
        public void DuplicateSelected()
        {
            if (selection.Count == 0) return;
            double min = double.MaxValue, max = double.MinValue;
            foreach (string id in selection)
            {
                NoteInstance n = Find(id);
                if (n == null) continue;
                min = Math.Min(min, n.HitBeat);
                max = Math.Max(max, n.HitBeat + Math.Max(0d, n.HoldBeats));
            }

            Copy();
            double span = Math.Max(max - min, 1d / Math.Max(1, SnapDivision));
            Paste(min + span);
        }

        // ---- undo / redo ----

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            EditCommand cmd = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            Apply(cmd.After, cmd.Before);
            ApplyPatterns(cmd.PatternAfter, cmd.PatternBefore);
            redoStack.Add(cmd);
            Dirty = true;
            PruneSelection();
            RaiseChanged();
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;
            EditCommand cmd = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            Apply(cmd.Before, cmd.After);
            ApplyPatterns(cmd.PatternBefore, cmd.PatternAfter);
            undoStack.Add(cmd);
            Dirty = true;
            PruneSelection();
            RaiseChanged();
        }

        private void Execute(EditCommand cmd)
        {
            Apply(cmd.Before, cmd.After);
            ApplyPatterns(cmd.PatternBefore, cmd.PatternAfter);
            undoStack.Add(cmd);
            redoStack.Clear();
            Dirty = true;
            PruneSelection();
            RaiseChanged();
        }

        // Removes 'remove' (by id) and inserts 'add' (as copies) so undo/redo never alias stored snapshots.
        private void Apply(List<NoteInstance> remove, List<NoteInstance> add)
        {
            var removeIds = new HashSet<string>();
            for (int i = 0; i < remove.Count; i++) removeIds.Add(remove[i].Id);
            notes.RemoveAll(n => removeIds.Contains(n.Id));
            for (int i = 0; i < add.Count; i++) notes.Add(add[i].Clone());
        }

        private void ApplyPatterns(List<PatternInstance> remove, List<PatternInstance> add)
        {
            if (remove.Count == 0 && add.Count == 0) return;
            var removeIds = new HashSet<string>();
            for (int i = 0; i < remove.Count; i++) removeIds.Add(remove[i].Id);
            patterns.RemoveAll(p => removeIds.Contains(p.Id));
            for (int i = 0; i < add.Count; i++) patterns.Add(add[i].Clone());
        }

        private void PruneSelection()
        {
            selection.RemoveWhere(id => Find(id) == null && FindPattern(id) == null);
        }

        // ---- programmed patterns ----

        /// <summary>Places a pattern from the library at <paramref name="startBeat"/> (snapped). Returns null for an unknown template.</summary>
        public PatternInstance AddPattern(string templateId, double startBeat)
        {
            PatternTemplate template = PatternLibrary.Find(templateId);
            if (template == null) return null;
            var p = new PatternInstance
            {
                Id = Guid.NewGuid().ToString("N"),
                TemplateId = templateId,
                StartBeat = Math.Max(0d, Snap(startBeat)),
                DurationBeats = template.DefaultDurationBeats,
                Seed = Environment.TickCount & 0x7fffffff,
                LaneMask = 0,
                Reservation = ReservationMode.Exclusive
            };
            var cmd = new EditCommand { Label = "Add pattern" };
            cmd.PatternAfter.Add(p);
            Execute(cmd);
            selection.Clear();
            selection.Add(p.Id);
            RaiseChanged();
            return FindPattern(p.Id);
        }

        /// <summary>Applies a change to one pattern as one undo step. Start is clamped to >= 0 and duration to at least a quarter beat.</summary>
        public void ModifyPattern(string id, string label, Action<PatternInstance> mutate)
        {
            PatternInstance p = FindPattern(id);
            if (p == null || mutate == null) return;
            var cmd = new EditCommand { Label = label };
            cmd.PatternBefore.Add(p.Clone());
            PatternInstance c = p.Clone();
            mutate(c);
            c.StartBeat = Math.Max(0d, c.StartBeat);
            c.DurationBeats = Math.Max(0.25d, c.DurationBeats);
            cmd.PatternAfter.Add(c);
            Execute(cmd);
        }

        /// <summary>Replaces a pattern with the ordinary notes it generates (fully editable afterwards). One undo step.</summary>
        public int BakePattern(string id)
        {
            PatternInstance p = FindPattern(id);
            if (p == null) return 0;
            List<NoteInstance> generated = PatternExpander.Expand(p, LaneIds);
            var cmd = new EditCommand { Label = "Bake pattern" };
            cmd.PatternBefore.Add(p.Clone());
            var newIds = new List<string>();
            for (int i = 0; i < generated.Count; i++)
            {
                NoteInstance n = generated[i].CloneWithNewId();
                n.Metadata.Clear();
                cmd.After.Add(n);
                newIds.Add(n.Id);
            }

            selection.Clear();
            Execute(cmd);
            for (int i = 0; i < newIds.Count; i++) selection.Add(newIds[i]);
            RaiseChanged();
            return newIds.Count;
        }

        public void MovePattern(string id, double deltaBeats)
        {
            PatternInstance p = FindPattern(id);
            if (p == null || Math.Abs(deltaBeats) < 1e-9) return;
            ModifyPattern(id, "Move pattern", c => c.StartBeat = p.StartBeat + deltaBeats);
        }

        public void ResizePattern(string id, double durationBeats)
        {
            PatternInstance p = FindPattern(id);
            if (p == null || Math.Abs(durationBeats - p.DurationBeats) < 1e-9) return;
            ModifyPattern(id, "Resize pattern", c => c.DurationBeats = durationBeats);
        }

        /// <summary>Call after replacing Tempo (BPM/offset edits): marks dirty and repaints listeners.</summary>
        public void NotifyTempoChanged()
        {
            Dirty = true;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }
    }
}
