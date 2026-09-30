using System;
using System.Collections.Generic;
using System.Linq;

namespace RythmRPG.Combat
{
    public enum CombatEventKind
    {
        CastCommitted,
        CastResolved,
        AbilityDamage,
        StatusDamage,
        ReflectDamage,
        PassiveDamage,
        DefenseDamage,
        Heal,
        ShieldGained,
        ManaPaid,
        ManaRestored,
        ManaRefunded,
        CounterGained,
        CounterSpent,
        BuffApplied,
        StatusApplied,
        PassiveTriggered,
        EnemyDefeated,
        PlayerDefeated
    }

    /// <summary>
    /// One combat transaction with full attribution. Damage events distinguish attempted, prevented and actual HP
    /// damage; heals distinguish actual healing from overheal; mana distinguishes paid, restored and refunded.
    /// Slot index, input lane and chart lane are kept as separate fields.
    /// </summary>
    public sealed class CombatEvent
    {
        public long EventId;
        public CombatEventKind Kind;
        public int EncounterId;
        public int Turn;
        public CombatState Phase;
        /// <summary>The cast / enemy note / turn that started this chain of effects.</summary>
        public string RootCauseId;
        public string CastId;
        public string PatternRunId;
        /// <summary>Ability instance or passive that produced this event.</summary>
        public string SourceId;
        public bool Secondary;
        public AbilityRole Roles;
        public AbilityDelivery Delivery;
        public ElementType Element;
        public NoteResolutionSource ResolutionSource;
        public HitJudgement? RawJudgement;
        public int SlotIndex = -1;
        public int InputLane = -1;
        public int ChartLane = -1;
        public int Attempted;
        public int Prevented;
        public int Actual;
        public int Overflow;
        public string Note;

        public override string ToString()
        {
            string amount = Kind switch
            {
                CombatEventKind.Heal => $"+{Actual}" + (Overflow > 0 ? $" (overheal {Overflow})" : string.Empty),
                CombatEventKind.DefenseDamage => $"-{Actual}" + (Prevented > 0 ? $" (prevented {Prevented} of {Attempted})" : string.Empty),
                _ => Actual != 0 ? Actual.ToString() : string.Empty
            };
            string element = Element != ElementType.None ? " " + Element : string.Empty;
            return $"#{EventId} T{Turn} {Kind}{element} {amount} [{SourceId}]" + (string.IsNullOrEmpty(Note) ? string.Empty : " " + Note);
        }
    }

    /// <summary>
    /// Ordered, bounded log of combat transactions. Also provides idempotency keys (a payout claimed once can never be
    /// claimed again, e.g. "conservation:cast-12") and per-root secondary-effect budgets.
    /// </summary>
    public sealed class CombatEventLog
    {
        private readonly List<CombatEvent> events = new();
        private readonly HashSet<string> claimed = new();
        private readonly Dictionary<string, int> secondaryPerRoot = new();
        private long nextId = 1;

        public int Capacity { get; set; } = 400;
        public IReadOnlyList<CombatEvent> Events => events;
        public event Action<CombatEvent> Recorded;

        public CombatEvent Record(CombatEvent combatEvent)
        {
            if (combatEvent == null) return null;
            combatEvent.EventId = nextId++;
            events.Add(combatEvent);
            if (events.Count > Capacity) events.RemoveRange(0, events.Count - Capacity);
            Recorded?.Invoke(combatEvent);
            return combatEvent;
        }

        /// <summary>True the first time a key is claimed, false afterwards.</summary>
        public bool TryClaim(string key) => !string.IsNullOrEmpty(key) && claimed.Add(key);

        public bool IsClaimed(string key) => !string.IsNullOrEmpty(key) && claimed.Contains(key);

        /// <summary>Consumes one secondary-effect slot for this root. False when the root's budget is used up.</summary>
        public bool TrySpendSecondary(string rootId, int budget)
        {
            if (string.IsNullOrEmpty(rootId)) return true;
            secondaryPerRoot.TryGetValue(rootId, out int used);
            if (used >= budget) return false;
            secondaryPerRoot[rootId] = used + 1;
            return true;
        }

        public IEnumerable<CombatEvent> Recent(int count) => events.Skip(Math.Max(0, events.Count - count));

        public void Clear()
        {
            events.Clear();
            claimed.Clear();
            secondaryPerRoot.Clear();
        }
    }
}
