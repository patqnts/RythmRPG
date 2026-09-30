using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    public enum BuffKind
    {
        /// <summary>The next damaging cast deals +Strength% damage (one pending benefit, consumed at commitment).</summary>
        NextAttackBonus,
        /// <summary>Enemy-turn Perfect / Good defense reflects Strength% of the note's damage back (per-turn cap).</summary>
        Reflect,
        /// <summary>Incoming note damage is reduced by Strength%.</summary>
        DamageReduction
    }

    /// <summary>
    /// Something in effect that the HUD shows as an icon with a number (turns left, charges...): buffs, statuses, wards.
    /// </summary>
    public interface ICombatEffectIcon
    {
        Sprite Icon { get; }
        string IconLabel { get; }
        /// <summary>Number drawn on the icon (turns remaining). 0 or less = no number.</summary>
        int IconCount { get; }
        /// <summary>True for effects that sit on the enemy (statuses); false for the player's buffs.</summary>
        bool IconOnEnemy { get; }
        bool IconIsDebuff { get; }
    }

    /// <summary>A buff about to be applied. Passives (buff mastery) may change its named parameters before it lands.</summary>
    public sealed class BuffSpec
    {
        public BuffKind Kind;
        /// <summary>Fraction (0.3 = 30%).</summary>
        public float Strength;
        /// <summary>For Reflect: Good judgements reflect this fraction of Strength (Perfect reflects all of it).</summary>
        public float GoodFactor = 0.5f;
        public int Turns;
        /// <summary>Reflect: most damage reflected per enemy turn.</summary>
        public int PerTurnCap;
        public string SourceId;
        public string Label;
        /// <summary>Icon of the ability / passive that applied it (HUD).</summary>
        public Sprite Icon;
    }

    /// <summary>Serialized status application data (content): e.g. Burn = fire damage each enemy turn start.</summary>
    [Serializable]
    public sealed class StatusSpec
    {
        public string statusId = "burn";
        public string displayName = "Burn";
        [Tooltip("Damage type of each tick (affinity applies once per tick).")]
        public ElementType element = ElementType.Fire;
        public TurnBoundary tickAt = TurnBoundary.EnemyTurnStart;
        [Tooltip("Damage per tick = ability Base Power x this x performance at application (snapshot).")]
        [Min(0f)] public float powerScalePerTick = 0.2f;
        [Min(1)] public int ticks = 3;
        public StackPolicy stacking = StackPolicy.Refresh;
        [Min(1)] public int maxStacks = 1;
        [Tooltip("Off for marks that only count down (Soaked, Static, Cracked): no damage at the tick boundary.")]
        public bool dealsTickDamage = true;
    }

    /// <summary>Base for build buffs: counts down at one declared boundary and reports its state for the dev panel.</summary>
    public abstract class TimedBuff : ICombatModifierRuntime, ITurnBoundaryModifier, ICombatEffectIcon
    {
        protected TimedBuff(string stackKey, string label, int turns, TurnBoundary countdownAt)
        {
            StackKey = stackKey;
            Label = label;
            TurnsRemaining = Mathf.Max(1, turns);
            CountdownAt = countdownAt;
        }

        public string StackKey { get; }
        public string Label { get; protected set; }
        public int TurnsRemaining { get; protected set; }
        public TurnBoundary CountdownAt { get; }
        public virtual bool IsExpired => TurnsRemaining <= 0;
        /// <summary>Skip the first countdown boundary when it occurs in the same turn the buff was applied.</summary>
        protected bool SkipNextCountdown;

        public virtual void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary != CountdownAt) return;
            if (SkipNextCountdown)
            {
                SkipNextCountdown = false;
                return;
            }
            TurnsRemaining--;
        }

        public void Refresh(int turns) => TurnsRemaining = Mathf.Max(TurnsRemaining, turns);

        /// <summary>Icon of the ability / passive that applied it (the latest one for shared effects like the shield).</summary>
        public Sprite Icon { get; set; }
        public virtual string IconLabel => Label;
        public virtual int IconCount => TurnsRemaining;
        public virtual bool IconOnEnemy => false;
        public virtual bool IconIsDebuff => false;
        public void Expire() => TurnsRemaining = 0;
        public abstract string Describe();
        public virtual void OnEnemyTurnStarted() { }
        public virtual void OnEnemyTurnEnded() { }
        public virtual void OnJudgementResolved(RhythmJudgementResult result, RhythmPatternRunner runner) { }
    }

    /// <summary>Absorbs incoming damage before health. All sources share one shield with a capped capacity.</summary>
    public sealed class ShieldBuff : TimedBuff
    {
        public const string Key = "shield";

        public int Capacity { get; private set; }
        public override bool IsExpired => base.IsExpired || Capacity <= 0;
        /// <summary>Healed when the shield is broken by damage (Tidal Veil). Cleared when it breaks.</summary>
        public int BreakHeal { get; set; }

        public ShieldBuff(int amount, int enemyTurns) : base(Key, "Shield", enemyTurns, TurnBoundary.EnemyTurnEnd) => Capacity = Mathf.Max(0, amount);

        /// <summary>Adds capacity up to <paramref name="cap"/> and refreshes duration. Returns the capacity actually added.</summary>
        public int Add(int amount, int turns, int cap)
        {
            int before = Capacity;
            Capacity = Mathf.Min(Mathf.Max(0, cap), Capacity + Mathf.Max(0, amount));
            Refresh(turns);
            return Capacity - before;
        }

        public void ClampTo(int cap) => Capacity = Mathf.Min(Capacity, Mathf.Max(0, cap));

        public int Absorb(int amount)
        {
            int absorbed = Mathf.Min(Capacity, Mathf.Max(0, amount));
            Capacity -= absorbed;
            return absorbed;
        }

        public override string Describe() => $"Shield {Capacity} ({TurnsRemaining} enemy turns)";
    }

    public sealed class ReflectBuff : TimedBuff
    {
        public float Strength { get; }
        public float GoodFactor { get; }
        public int PerTurnCap { get; }
        public string SourceId { get; }
        public int ReflectedThisTurn { get; private set; }

        public ReflectBuff(BuffSpec spec) : base("reflect:" + spec.SourceId, spec.Label ?? "Reflect", spec.Turns, TurnBoundary.EnemyTurnEnd)
        {
            Strength = Mathf.Max(0f, spec.Strength);
            GoodFactor = Mathf.Clamp01(spec.GoodFactor);
            PerTurnCap = Mathf.Max(0, spec.PerTurnCap);
            SourceId = spec.SourceId;
        }

        public float FractionFor(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => Strength,
            HitJudgement.Good => Strength * GoodFactor,
            _ => 0f
        };

        /// <summary>Clamps a reflect amount to what is left of this enemy turn's cap and books it.</summary>
        public int Take(int amount)
        {
            int allowed = PerTurnCap > 0 ? Mathf.Min(amount, PerTurnCap - ReflectedThisTurn) : amount;
            allowed = Mathf.Max(0, allowed);
            ReflectedThisTurn += allowed;
            return allowed;
        }

        public override void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) ReflectedThisTurn = 0;
            base.OnTurnBoundary(boundary);
        }

        public override string Describe() =>
            $"{Label}: reflect {Mathf.RoundToInt(Strength * 100f)}% on Perfect ({TurnsRemaining} enemy turns, {ReflectedThisTurn}/{PerTurnCap} this turn)";
    }

    public sealed class DamageReductionBuff : TimedBuff
    {
        public float Strength { get; }

        public DamageReductionBuff(BuffSpec spec) : base("reduction:" + spec.SourceId, spec.Label ?? "Guard", spec.Turns, TurnBoundary.EnemyTurnEnd) =>
            Strength = Mathf.Clamp01(spec.Strength);

        public override string Describe() => $"{Label}: -{Mathf.RoundToInt(Strength * 100f)}% damage taken ({TurnsRemaining} enemy turns)";
    }

    /// <summary>One pending next-attack benefit per source. Reserved when a damaging cast is committed.</summary>
    public sealed class NextAttackBonusBuff : TimedBuff
    {
        public float Strength { get; private set; }
        public string SourceId { get; }

        public NextAttackBonusBuff(BuffSpec spec) : base("next-attack:" + spec.SourceId, spec.Label ?? "Next attack", spec.Turns, TurnBoundary.PlayerTurnEnd)
        {
            Strength = Mathf.Max(0f, spec.Strength);
            SourceId = spec.SourceId;
            // Applied during a player turn: that turn's end does not count.
            SkipNextCountdown = true;
        }

        public void Replace(BuffSpec spec)
        {
            Strength = Mathf.Max(Strength, spec.Strength);
            Refresh(spec.Turns);
            SkipNextCountdown = true;
        }

        public override string Describe() => $"{Label}: next attack {BuildTagUtility.Percent(Strength)} ({TurnsRemaining} player turns)";
    }

    /// <summary>A status on the enemy (e.g. Burn). Potency is snapshotted at application; ticks at its declared boundary.</summary>
    public sealed class StatusInstance : TimedBuff
    {
        private readonly CombatBuildRuntime runtime;

        public StatusSpec Spec { get; }
        public int DamagePerTick { get; private set; }
        public int Stacks { get; private set; } = 1;
        public int MaxStacks { get; internal set; }
        public string RootCauseId { get; private set; }
        /// <summary>Enemy turns the mark lasted when first applied (Wind extends it once per cast).</summary>
        public int BaseTurns { get; }

        public StatusInstance(CombatBuildRuntime runtime, StatusSpec spec, int damagePerTick, int ticks, int maxStacks, string rootCauseId)
            : base("status:" + spec.statusId, spec.displayName, ticks, spec.tickAt)
        {
            this.runtime = runtime;
            Spec = spec;
            DamagePerTick = Mathf.Max(0, damagePerTick);
            MaxStacks = Mathf.Max(1, maxStacks);
            RootCauseId = rootCauseId;
            BaseTurns = Mathf.Max(1, ticks);
        }

        /// <summary>Stacking re-application: more stacks, the stronger potency, refreshed duration.</summary>
        public void Add(int stacks, int damagePerTick, int ticks, string rootCauseId)
        {
            AddStacks(stacks);
            RaisePotency(damagePerTick);
            Refresh(ticks);
            RootCauseId = rootCauseId;
        }

        /// <summary>Adds stacks up to the cap (at least one stack stays). Returns how many were added.</summary>
        public int AddStacks(int amount)
        {
            int before = Stacks;
            Stacks = Mathf.Clamp(Stacks + amount, 1, MaxStacks);
            return Stacks - before;
        }

        public void SetStacks(int amount) => Stacks = Mathf.Clamp(amount, 1, MaxStacks);

        public void RaisePotency(int damagePerTick) => DamagePerTick = Mathf.Max(DamagePerTick, damagePerTick);

        public void Extend(int turns) => TurnsRemaining += Mathf.Max(0, turns);

        /// <summary>Remaining tick damage if it ran its course (Combust, Steam).</summary>
        public int RemainingDamage => Spec.dealsTickDamage ? DamagePerTick * Stacks * Mathf.Max(0, TurnsRemaining) : 0;

        /// <summary>Applies the declared stacking policy for a new application of the same status.</summary>
        public void Reapply(int damagePerTick, int ticks, string rootCauseId)
        {
            switch (Spec.stacking)
            {
                case StackPolicy.Replace:
                    DamagePerTick = Mathf.Max(0, damagePerTick);
                    TurnsRemaining = Mathf.Max(1, ticks);
                    break;
                case StackPolicy.CappedAdd:
                    Stacks = Mathf.Min(MaxStacks, Stacks + 1);
                    DamagePerTick = Mathf.Max(DamagePerTick, damagePerTick);
                    Refresh(ticks);
                    break;
                default:
                    DamagePerTick = Mathf.Max(DamagePerTick, damagePerTick);
                    Refresh(ticks);
                    break;
            }
            RootCauseId = rootCauseId;
        }

        public override void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary != CountdownAt || IsExpired) return;
            if (Spec.dealsTickDamage) runtime?.TickStatus(this, DamagePerTick * Stacks);
            TurnsRemaining--;
        }

        public override string Describe() =>
            $"{Label} x{Stacks}: {DamagePerTick * Stacks} {BuildTagUtility.ElementName(Spec.element)} at {Spec.tickAt} ({TurnsRemaining} ticks left)";

        public override bool IconOnEnemy => true;
        public override bool IconIsDebuff => true;
        /// <summary>Stacking marks (Burn, Static) show their stacks; the others their turns left.</summary>
        public override int IconCount => MaxStacks > 1 ? Stacks : TurnsRemaining;
        public override string IconLabel => MaxStacks > 1 ? $"{Label} x{Stacks}" : Label;
    }
}
