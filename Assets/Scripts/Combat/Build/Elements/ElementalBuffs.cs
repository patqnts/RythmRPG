using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>A modifier that reacts to the player's own defense judgements (after the note's damage transaction).</summary>
    public interface IDefenseExecutionModifier
    {
        void OnPlayerDefense(CombatBuildRuntime runtime, DefenseNoteOutcome outcome);
    }

    /// <summary>A modifier that acts on the board every frame while an enemy pattern runs (Stone Wall).</summary>
    public interface IBoardModifier
    {
        void TickBoard(CombatBuildRuntime runtime, ICombatNoteBoard board);
    }

    /// <summary>An ability effect that acts during its own chart, on each judgement (Chain Spark arcs).</summary>
    public interface ILiveChartEffect
    {
        void OnChartJudgement(CombatBuildRuntime runtime, CastSnapshot cast, RhythmJudgementResult result);
    }

    /// <summary>
    /// Storm Ward: each Perfect block zaps the next notes (they are destroyed and deal Lightning damage). Conduct
    /// (enemy Soaked) chains one note further. Limited per enemy turn.
    /// </summary>
    public sealed class StormWardBuff : TimedBuff, IDefenseExecutionModifier
    {
        private readonly int notesPerPerfect;
        private readonly int damagePerNote;
        private readonly int maxPerTurn;
        private readonly string sourceId;
        private int zappedThisTurn;

        public StormWardBuff(string sourceId, string label, int enemyTurns, int notesPerPerfect, int damagePerNote, int maxPerTurn)
            : base("storm-ward:" + sourceId, label ?? "Storm Ward", enemyTurns, TurnBoundary.EnemyTurnEnd)
        {
            this.sourceId = sourceId;
            this.notesPerPerfect = Mathf.Max(1, notesPerPerfect);
            this.damagePerNote = Mathf.Max(0, damagePerNote);
            this.maxPerTurn = Mathf.Max(1, maxPerTurn);
        }

        public int DamagePerNote => damagePerNote;

        public void OnPlayerDefense(CombatBuildRuntime runtime, DefenseNoteOutcome outcome)
        {
            if (outcome.Result.Judgement != HitJudgement.Perfect || zappedThisTurn >= maxPerTurn) return;
            int count = notesPerPerfect + (runtime.Marks.Conducting ? runtime.Rules.Elements.conductExtraZaps : 0);
            count = Mathf.Min(count, maxPerTurn - zappedThisTurn);
            zappedThisTurn += runtime.Zap(count, damagePerNote, outcome.Result.WorldPosition, sourceId, outcome.RootCauseId, Label);
        }

        public override void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) zappedThisTurn = 0;
            base.OnTurnBoundary(boundary);
        }

        public override string Describe() =>
            $"{Label}: Perfect zaps {notesPerPerfect} notes ({damagePerNote} Lightning each, {zappedThisTurn}/{maxPerTurn} this turn, {TurnsRemaining} enemy turns)";
    }

    /// <summary>Flame Guard: reaching a combo milestone during defense adds Burn to the enemy.</summary>
    public sealed class FlameGuardBuff : TimedBuff, IDefenseExecutionModifier
    {
        private readonly int stacksPerMilestone;
        private readonly int comboPerStack;
        private readonly int burnPerStack;
        private readonly int maxStacksPerTurn;
        private int addedThisTurn;

        public FlameGuardBuff(string sourceId, string label, int enemyTurns, int stacksPerMilestone, int comboPerStack,
            int burnPerStack, int maxStacksPerTurn)
            : base("flame-guard:" + sourceId, label ?? "Flame Guard", enemyTurns, TurnBoundary.EnemyTurnEnd)
        {
            this.stacksPerMilestone = Mathf.Max(1, stacksPerMilestone);
            this.comboPerStack = Mathf.Max(1, comboPerStack);
            this.burnPerStack = Mathf.Max(0, burnPerStack);
            this.maxStacksPerTurn = Mathf.Max(1, maxStacksPerTurn);
        }

        public void OnPlayerDefense(CombatBuildRuntime runtime, DefenseNoteOutcome outcome)
        {
            if (outcome.Combo <= 0 || outcome.Combo % comboPerStack != 0 || addedThisTurn >= maxStacksPerTurn) return;
            int stacks = Mathf.Min(stacksPerMilestone, maxStacksPerTurn - addedThisTurn);
            if (runtime.Marks.Apply(ElementalMarks.Burn, stacks, burnPerStack, outcome.RootCauseId, Icon)) addedThisTurn += stacks;
        }

        public override void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) addedThisTurn = 0;
            base.OnTurnBoundary(boundary);
        }

        public override string Describe() =>
            $"{Label}: every {comboPerStack} Combo during defense adds {stacksPerMilestone} Burn " +
            $"({burnPerStack}/stack, {addedThisTurn}/{maxStacksPerTurn} this turn, {TurnsRemaining} enemy turns)";
    }

    /// <summary>
    /// Stone Wall: walls off the lane with the most incoming notes (re-chosen at every attack step). Notes in that lane
    /// break against it before reaching the hit line: no damage, no judgement. Lasts until its charges are used.
    /// </summary>
    public sealed class StoneWallBuff : TimedBuff, IBoardModifier
    {
        private readonly string sourceId;
        private int patternSerial = -1;

        public StoneWallBuff(string sourceId, string label, int charges, int enemyTurns)
            : base("stone-wall:" + sourceId, label ?? "Stone Wall", enemyTurns, TurnBoundary.EnemyTurnEnd)
        {
            this.sourceId = sourceId;
            Charges = Mathf.Max(1, charges);
        }

        public int Charges { get; private set; }
        public int Lane { get; private set; }
        public override bool IsExpired => base.IsExpired || Charges <= 0;
        public override int IconCount => Charges;

        public void TickBoard(CombatBuildRuntime runtime, ICombatNoteBoard board)
        {
            if (Charges <= 0) return;
            if (patternSerial != board.PatternSerial)
            {
                patternSerial = board.PatternSerial;
                Lane = board.BusiestLane();
            }
            if (Lane <= 0) return;
            float lead = runtime.Rules.Elements.wallLeadSeconds;
            foreach (BoardNote note in board.Upcoming(lead + 0.05f))
            {
                if (Charges <= 0) break;
                if (note.Lane != Lane || note.SecondsUntilHit > lead) continue;
                if (runtime.ClearNote(note, sourceId, Label)) Charges--;
            }
            if (Charges <= 0) runtime.Modifiers.NotifyChanged();
        }

        public override string Describe() => $"{Label}: lane {(Lane > 0 ? Lane.ToString() : "?")}, {Charges} notes left ({TurnsRemaining} enemy turns)";
    }

    /// <summary>Gale Step: the first Miss(es) of the next enemy turn deal no damage.</summary>
    public sealed class DodgeBuff : TimedBuff, IDamageBlockingModifier
    {
        private readonly int perTurn;
        private int left;

        public DodgeBuff(string sourceId, string label, int missesPerTurn, int enemyTurns)
            : base("dodge:" + sourceId, label ?? "Dodge", enemyTurns, TurnBoundary.EnemyTurnEnd)
        {
            perTurn = Mathf.Max(1, missesPerTurn);
            left = perTurn;
        }

        public override int IconCount => left;

        public bool BlocksDamage(RhythmJudgementResult result)
        {
            if (IsExpired || left <= 0 || result.Judgement != HitJudgement.Miss) return false;
            left--;
            return true;
        }

        public override void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) left = perTurn;
            base.OnTurnBoundary(boundary);
        }

        public override string Describe() => $"{Label}: dodge {left}/{perTurn} Miss this enemy turn ({TurnsRemaining} enemy turns)";
    }

    /// <summary>Rain Dance: heals at the start of each player turn (Water healing, so Water healing bonuses and Riptide apply).</summary>
    public sealed class RegenBuff : TimedBuff
    {
        private readonly CombatBuildRuntime runtime;
        private readonly int healPerTick;
        private readonly ElementType element;
        private readonly string rootId;

        public RegenBuff(CombatBuildRuntime runtime, string sourceId, string label, int healPerTick, int ticks, ElementType element, string rootId)
            : base("regen:" + sourceId, label ?? "Regen", ticks, TurnBoundary.PlayerTurnStart)
        {
            this.runtime = runtime;
            this.healPerTick = Mathf.Max(0, healPerTick);
            this.element = element;
            this.rootId = rootId;
        }

        public int HealPerTick => healPerTick;

        public override void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary != CountdownAt || IsExpired) return;
            if (runtime != null && healPerTick > 0) runtime.Damage.HealPlayer(healPerTick, element, "regen", rootId);
            TurnsRemaining--;
        }

        public override string Describe() => $"{Label}: heal {healPerTick} at each player turn start ({TurnsRemaining} left)";
    }
}
