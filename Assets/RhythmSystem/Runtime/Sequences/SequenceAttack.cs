using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm.Sequences
{
    public enum SequenceState
    {
        Pending,
        Active,
        Completed,
        Failed,
        Cancelled
    }

    public enum SequenceCancelReason
    {
        Manual,
        Superseded,
        MaxAge,
        ChartEnded,
        EncounterInterrupted,
        TargetDefeated
    }

    public enum SequenceConcurrency
    {
        /// <summary>Runs alongside other sequences.</summary>
        Additive,
        /// <summary>Starting it cancels a running sequence with the same tag.</summary>
        ReplaceSameTag
    }

    /// <summary>How a sequence attack lives inside a turn.</summary>
    public sealed class SequencePolicy
    {
        /// <summary>The turn stays open until the sequence completes, fails or is cancelled.</summary>
        public bool BlocksTurnEnd = true;
        /// <summary>Hard failsafe so an authoring mistake cannot soft-lock a battle.</summary>
        public double MaxAgeSeconds = 45d;
        public SequenceConcurrency Concurrency = SequenceConcurrency.Additive;
    }

    /// <summary>
    /// A multi-step attack driven at run time (not a fixed list of chart notes). The pool ticks it with song time
    /// and tells it when notes it spawned resolve. Implementations must make <see cref="Cancel"/> idempotent.
    /// </summary>
    public interface ISequenceAttack
    {
        string Id { get; }
        string Tag { get; }
        SequenceState State { get; }
        SequencePolicy Policy { get; }
        void Activate(double now);
        void Tick(double now);
        /// <summary>Called for every note resolved by the runner; implementations ignore ids that are not theirs.</summary>
        void OnNoteResolved(string noteId, bool success, double now);
        void Cancel(SequenceCancelReason reason);
    }

    /// <summary>Common state handling for sequence attacks.</summary>
    public abstract class SequenceAttackBase : ISequenceAttack
    {
        protected SequenceAttackBase(string id, string tag, SequencePolicy policy)
        {
            Id = id;
            Tag = tag ?? "";
            Policy = policy ?? new SequencePolicy();
        }

        public string Id { get; private set; }
        public string Tag { get; private set; }
        public SequenceState State { get; private set; }
        public SequencePolicy Policy { get; private set; }
        public SequenceCancelReason CancelReason { get; private set; }
        public bool IsFinished { get { return State == SequenceState.Completed || State == SequenceState.Failed || State == SequenceState.Cancelled; } }

        public void Activate(double now)
        {
            if (State != SequenceState.Pending) return;
            State = SequenceState.Active;
            OnActivate(now);
        }

        public void Tick(double now)
        {
            if (State == SequenceState.Active) OnTick(now);
        }

        public void OnNoteResolved(string noteId, bool success, double now)
        {
            if (State == SequenceState.Active) HandleNoteResolved(noteId, success, now);
        }

        public void Cancel(SequenceCancelReason reason)
        {
            if (IsFinished) return;
            State = SequenceState.Cancelled;
            CancelReason = reason;
            OnCancelled(reason);
        }

        protected void Finish(SequenceState result)
        {
            if (IsFinished) return;
            State = result;
        }

        protected abstract void OnActivate(double now);
        protected abstract void OnTick(double now);
        protected abstract void HandleNoteResolved(string noteId, bool success, double now);
        protected virtual void OnCancelled(SequenceCancelReason reason) { }
    }

    /// <summary>
    /// Owns running sequence attacks. Pure logic: the caller supplies song time. Nothing here spawns objects;
    /// attacks talk to their own host (the combat runner) for that.
    /// </summary>
    public sealed class SequenceAttackPool
    {
        private sealed class Entry
        {
            public ISequenceAttack Attack;
            public double StartedAt;
        }

        private readonly List<Entry> entries = new List<Entry>();

        /// <summary>Fires once per attack when it leaves the pool (completed, failed or cancelled).</summary>
        public event Action<ISequenceAttack> AttackFinished;

        public int Count { get { return entries.Count; } }

        public int BlockingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < entries.Count; i++) if (entries[i].Attack.Policy.BlocksTurnEnd) n++;
                return n;
            }
        }

        public bool HasBlocking { get { return BlockingCount > 0; } }

        public void Start(ISequenceAttack attack, double now)
        {
            if (attack == null) throw new ArgumentNullException("attack");
            if (attack.Policy.Concurrency == SequenceConcurrency.ReplaceSameTag && !string.IsNullOrEmpty(attack.Tag))
            {
                Entry[] snapshot = entries.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (snapshot[i].Attack.Tag == attack.Tag) snapshot[i].Attack.Cancel(SequenceCancelReason.Superseded);
                }

                Sweep();
            }

            entries.Add(new Entry { Attack = attack, StartedAt = now });
            attack.Activate(now);
            Sweep();
        }

        public void Tick(double now)
        {
            Entry[] snapshot = entries.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Entry e = snapshot[i];
                double maxAge = e.Attack.Policy.MaxAgeSeconds;
                if (maxAge > 0d && now - e.StartedAt >= maxAge) e.Attack.Cancel(SequenceCancelReason.MaxAge);
                else e.Attack.Tick(now);
            }

            Sweep();
        }

        public void NotifyNoteResolved(string noteId, bool success, double now)
        {
            Entry[] snapshot = entries.ToArray();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Attack.OnNoteResolved(noteId, success, now);
            Sweep();
        }

        public void CancelAll(SequenceCancelReason reason)
        {
            Entry[] snapshot = entries.ToArray();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Attack.Cancel(reason);
            Sweep();
        }

        private void Sweep()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                SequenceState s = entries[i].Attack.State;
                if (s == SequenceState.Pending || s == SequenceState.Active) continue;
                ISequenceAttack done = entries[i].Attack;
                entries.RemoveAt(i);
                if (AttackFinished != null) AttackFinished(done);
            }
        }
    }
}
