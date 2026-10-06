using System.Collections.Generic;
using System.Linq;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// <see cref="ICombatNoteBoard"/> over the pattern runner: lets the build runtime find and clear enemy notes (Storm
    /// Ward zaps, Stone Wall). Cleared notes resolve as <see cref="NoteResolutionSource.Modifier"/> Perfects, which the
    /// controller turns into no damage, no combo and no mana.
    /// </summary>
    public sealed class RunnerNoteBoard : ICombatNoteBoard
    {
        private readonly List<BoardNote> buffer = new();
        private RhythmPatternRunner runner;
        private RhythmChart chart;
        private int patternSerial;

        public RunnerNoteBoard Bind(RhythmPatternRunner patternRunner)
        {
            if (runner == patternRunner) return this;
            if (runner != null) runner.PatternStarted -= HandlePatternStarted;
            runner = patternRunner;
            chart = null;
            if (runner != null) runner.PatternStarted += HandlePatternStarted;
            return this;
        }

        private void HandlePatternStarted(RhythmChart started, PatternRunContext context)
        {
            if (context.Mode != PatternRunMode.EnemyDefense) return;
            chart = started;
            patternSerial++;
        }

        public bool IsDefending => runner != null && runner.IsRunning && runner.CurrentMode == PatternRunMode.EnemyDefense;
        public int PatternSerial => patternSerial;

        /// <summary>Only plain notes can be cleared: holds, mashes and rally shots stay the player's job.</summary>
        public static bool CanClear(Note note) =>
            note != null && !note.IsResolved && note.isActiveAndEnabled
            && !(note is HoldNoteObject) && !(note is MashNote) && !(note is PongNote)
            && !(note is CombatNote combat && !combat.ClearableByEffects);

        public IReadOnlyList<BoardNote> Upcoming(float withinSeconds)
        {
            buffer.Clear();
            if (!IsDefending) return buffer;
            foreach (Note note in runner.ActiveNotes)
            {
                if (!CanClear(note)) continue;
                float seconds = note.SecondsUntilHit;
                if (float.IsNaN(seconds) || seconds < -0.02f || seconds > withinSeconds) continue;
                buffer.Add(new BoardNote(note, note.RuntimeNoteId, note.GetNoteIdentity(), seconds, note.damage, note.GetJudgementWorldPosition()));
            }
            buffer.Sort((a, b) => a.SecondsUntilHit.CompareTo(b.SecondsUntilHit));
            return buffer;
        }

        public int BusiestLane()
        {
            if (!IsDefending) return 0;
            var counts = new Dictionary<int, int>();
            double now = runner.ChartSeconds;
            if (chart != null)
            {
                foreach (RhythmNoteData data in chart.Notes)
                {
                    if (data == null || data.HitTime < now) continue;
                    RhythmLaneData lane = chart.FindLane(data.LaneId);
                    if (lane == null) continue;
                    counts.TryGetValue(lane.KeyIdentity, out int count);
                    counts[lane.KeyIdentity] = count + 1;
                }
            }
            if (counts.Count == 0)
                foreach (Note note in runner.ActiveNotes.Where(CanClear))
                {
                    counts.TryGetValue(note.GetNoteIdentity(), out int count);
                    counts[note.GetNoteIdentity()] = count + 1;
                }
            return counts.Count == 0 ? 0 : counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
        }

        public bool Clear(BoardNote boardNote)
        {
            if (!(boardNote.Handle is Note note) || !CanClear(note)) return false;
            note.ForceResolve(new RhythmJudgementResult(note.RuntimeNoteId, note.GetNoteIdentity(), HitJudgement.Perfect, 0f,
                note.GetJudgementWorldPosition(), NoteResolutionSource.Modifier));
            return true;
        }

        public void ShowArc(Vector3 from, Vector3 to, Color color) => LightningArcVfx.Spawn(from, to, color);
    }
}
