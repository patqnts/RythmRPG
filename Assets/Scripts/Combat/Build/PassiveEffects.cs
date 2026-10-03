using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    // Concrete passive behaviours. Each one is bounded: a stacking group, a per-cast / per-turn limit, a cap or a
    // fixed turn budget. Values are "first level + per extra level".

    /// <summary>
    /// Unconditional enhancement of one outcome kind for matching actions: melee damage, spell damage, Element E
    /// damage, Element E healing, shields... The action filter picks the actions; the element filter picks components.
    /// </summary>
    [Serializable]
    public sealed class ActionModifierPassive : PassiveEffect
    {
        [SerializeField] private EffectKind kind = EffectKind.Damage;
        [SerializeField] private EffectFilter filter = new();
        [SerializeField] private float percent = 0.15f;
        [SerializeField] private float percentPerLevel = 0.1f;

        public ActionModifierPassive() { }
        public ActionModifierPassive(EffectKind kind, EffectFilter filter, float percent, float perLevel)
        {
            this.kind = kind;
            this.filter = filter ?? new EffectFilter();
            this.percent = percent;
            percentPerLevel = perLevel;
        }

        public override string Describe(int level)
        {
            string scope = filter.Describe();
            return $"{BuildTagUtility.Percent(LevelValue(percent, percentPerLevel, level))} {(string.IsNullOrEmpty(scope) ? "" : scope + " ")}{kind.ToString().ToLowerInvariant()}";
        }

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(quote =>
            filter.MatchesAction(quote.Delivery, quote.Roles) && (!filter.matchElement || ComponentElements(quote).Contains(filter.element)));

        private IEnumerable<ElementType> ComponentElements(AbilityQuote quote) => kind switch
        {
            EffectKind.Healing => quote.HealingElements(),
            EffectKind.Damage => quote.DamageElements(includeStatus: false),
            _ => new[] { ElementType.None }
        };

        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            if (!filter.MatchesAction(cast.Delivery, cast.Roles)) return;
            cast.AddModifier(ModifierGroup.Passive, kind, LevelValue(percent, percentPerLevel, hook.Level), hook.SourceId, hook.Label,
                filter.matchElement ? filter : null, hook.StackKey, hook.StackCap);
        }
    }

    /// <summary>Magic efficiency: lowers the mana cost of matching actions (shown in the preview, paid on commit).</summary>
    [Serializable]
    public sealed class ManaCostPassive : PassiveEffect
    {
        [SerializeField] private EffectFilter filter = new(AbilityDelivery.Spell);
        [SerializeField, Range(0f, 0.9f)] private float reduction = 0.15f;
        [SerializeField, Range(0f, 0.5f)] private float reductionPerLevel = 0.05f;
        [Tooltip("A costed ability never drops below this.")]
        [SerializeField, Min(0)] private int minimumCost = 1;

        public ManaCostPassive() { }
        public ManaCostPassive(EffectFilter filter, float reduction, float perLevel, int minimumCost = 1)
        {
            this.filter = filter;
            this.reduction = reduction;
            reductionPerLevel = perLevel;
            this.minimumCost = minimumCost;
        }

        public override string Describe(int level) =>
            $"{filter.Describe()} abilities cost {Mathf.RoundToInt(LevelValue(reduction, reductionPerLevel, level) * 100f)}% less mana (min {minimumCost})";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => quote.BaseManaCost > 0 && filter.MatchesAction(quote.Delivery, quote.Roles));

        public override void ModifyQuote(AbilityQuote quote, int level, string label)
        {
            if (quote.ManaCost <= 0 || !filter.MatchesAction(quote.Delivery, quote.Roles)) return;
            float fraction = Mathf.Clamp(LevelValue(reduction, reductionPerLevel, level), 0f, 0.9f);
            int reduced = Mathf.Max(Mathf.Min(minimumCost, quote.ManaCost), Mathf.RoundToInt(quote.ManaCost * (1f - fraction)));
            if (reduced == quote.ManaCost) return;
            quote.Changes.Add($"{label}: {quote.ManaCost} -> {reduced} MP");
            quote.ManaCost = reduced;
        }
    }

    /// <summary>Max health change at encounter start (negative = the glass-cannon survivability sacrifice).</summary>
    [Serializable]
    public sealed class MaxHealthPassive : PassiveEffect
    {
        [SerializeField, Range(-0.9f, 2f)] private float percent = 0.1f;
        [SerializeField, Range(-0.5f, 1f)] private float percentPerLevel = 0.05f;

        public MaxHealthPassive() { }
        public MaxHealthPassive(float percent, float perLevel) { this.percent = percent; percentPerLevel = perLevel; }

        public override string Describe(int level) => $"{BuildTagUtility.Percent(LevelValue(percent, percentPerLevel, level))} max HP";

        public override int ModifyMaxHealth(int maxHealth, int level) =>
            Mathf.Max(1, Mathf.RoundToInt(maxHealth * (1f + Mathf.Max(-0.9f, LevelValue(percent, percentPerLevel, level)))));
    }

    /// <summary>Measured execution: a strong chart result improves that cast. Once per cast; the threshold is visible.</summary>
    [Serializable]
    public sealed class MeasuredExecutionPassive : PassiveEffect
    {
        [SerializeField, Range(0f, 1f)] private float threshold = 0.85f;
        [SerializeField] private float bonus = 0.15f;
        [SerializeField] private float bonusPerLevel = 0.05f;

        public MeasuredExecutionPassive() { }
        public MeasuredExecutionPassive(float threshold, float bonus, float perLevel) { this.threshold = threshold; this.bonus = bonus; bonusPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"{BuildTagUtility.Percent(LevelValue(bonus, bonusPerLevel, level))} damage when the chart is played at {Mathf.RoundToInt(threshold * 100f)}%+";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(quote => (quote.Roles & AbilityRole.Damage) != 0);

        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            if (!cast.ExecutionEligible || !cast.HasRole(AbilityRole.Damage) || cast.PerformanceWeight < threshold) return;
            cast.AddModifier(ModifierGroup.Execution, EffectKind.Damage, LevelValue(bonus, bonusPerLevel, hook.Level), hook.SourceId, hook.Label);
        }
    }

    /// <summary>
    /// Counter preparation: accurate enemy-turn defense stores counter charges (per-turn budget, stored cap, expiry).
    /// Works without an active counter ability: a damaging cast without its own spender spends a few charges for a
    /// smaller bonus. An ability with <see cref="SpendCountersEffect"/> spends them for its stronger payoff instead.
    /// </summary>
    [Serializable]
    public sealed class CounterPreparationPassive : PassiveEffect
    {
        [SerializeField] private bool goodCounts;
        [SerializeField, Min(1)] private int maxPerEnemyTurn = 2;
        [SerializeField, Min(0)] private int maxPerEnemyTurnPerLevel = 1;
        [SerializeField, Min(0f)] private float autoSpendPerCharge = 0.1f;
        [SerializeField, Min(0)] private int autoSpendMax = 3;

        public CounterPreparationPassive() { }
        public CounterPreparationPassive(int maxPerTurn, int perLevel, float autoSpendPerCharge, int autoSpendMax, bool goodCounts = false)
        {
            maxPerEnemyTurn = maxPerTurn;
            maxPerEnemyTurnPerLevel = perLevel;
            this.autoSpendPerCharge = autoSpendPerCharge;
            this.autoSpendMax = autoSpendMax;
            this.goodCounts = goodCounts;
        }

        public override string Describe(int level) =>
            $"Each Perfect{(goodCounts ? "/Good" : "")} defense stores a counter charge (max {LevelValue(maxPerEnemyTurn, maxPerEnemyTurnPerLevel, level)} per enemy turn). " +
            "Successful defense phrases can fill the same allowance. " +
            $"Attacks spend up to {autoSpendMax} for {BuildTagUtility.Percent(autoSpendPerCharge)} each";

        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) hook.Set("gained", 0);
        }

        public override void OnDefenseNoteSettled(PassiveHook hook, DefenseNoteOutcome outcome)
        {
            if (!outcome.IsPlayerExecution) return;
            HitJudgement judgement = outcome.Result.Judgement;
            if (judgement != HitJudgement.Perfect && !(goodCounts && judgement == HitJudgement.Good)) return;
            if (hook.Get("gained") >= LevelValue(maxPerEnemyTurn, maxPerEnemyTurnPerLevel, hook.Level)) return;
            if (hook.Runtime.Counters.Gain(1, hook.SourceId, outcome.RootCauseId) > 0) hook.Add("gained", 1);
        }

        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            if (autoSpendPerCharge <= 0f || autoSpendMax <= 0 || !cast.HasRole(AbilityRole.Damage)) return;
            if (cast.Quote != null && cast.Quote.Effects.Any(effect => effect is SpendCountersEffect)) return;
            int spent = hook.Runtime.Counters.Spend(autoSpendMax, cast.CastId, hook.Label);
            if (spent > 0) cast.AddModifier(ModifierGroup.Counter, EffectKind.Damage, spent * autoSpendPerCharge, hook.SourceId, $"{hook.Label} x{spent}");
        }

        public override void OnDefensePhrase(PassiveHook hook, PhraseOutcome phrase, string rootId)
        {
            if (!phrase.Successful(hook.Runtime.Rules.Synergy.phraseSuccess)
                || hook.Get("gained") >= LevelValue(maxPerEnemyTurn, maxPerEnemyTurnPerLevel, hook.Level)) return;
            if (hook.Runtime.Counters.Gain(1, hook.SourceId, rootId) > 0) hook.Add("gained", 1);
        }
    }

    /// <summary>Conditional reflection: Perfect defense sends part of the note's damage back. Capped per enemy turn.</summary>
    [Serializable]
    public sealed class DeflectionPassive : PassiveEffect
    {
        [SerializeField, Range(0f, 2f)] private float fraction = 0.3f;
        [SerializeField, Range(0f, 1f)] private float fractionPerLevel = 0.1f;
        [SerializeField, Min(1)] private int capPerEnemyTurn = 40;
        [SerializeField, Min(0)] private int capPerLevel = 15;
        [Tooltip("Damage type of the reflected damage (enemy affinity applies once).")]
        [SerializeField] private ElementType element = ElementType.None;

        public DeflectionPassive() { }
        public DeflectionPassive(float fraction, float perLevel, int cap, int capPerLevel)
        {
            this.fraction = fraction;
            fractionPerLevel = perLevel;
            capPerEnemyTurn = cap;
            this.capPerLevel = capPerLevel;
        }

        public override string Describe(int level) =>
            $"Perfect defense reflects {Mathf.RoundToInt(LevelValue(fraction, fractionPerLevel, level) * 100f)}% of the note's damage (max {LevelValue(capPerEnemyTurn, capPerLevel, level)} per enemy turn)";

        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        {
            if (boundary != TurnBoundary.EnemyTurnStart) return;
            hook.Set("reflected", 0);
            hook.Set("reflect-remainder", 0);
        }

        public override void OnDefenseNoteSettled(PassiveHook hook, DefenseNoteOutcome outcome)
        {
            if (!outcome.IsPlayerExecution || outcome.Result.Judgement != HitJudgement.Perfect || outcome.NoteDamage <= 0) return;
            int cap = LevelValue(capPerEnemyTurn, capPerLevel, hook.Level) - hook.Get("reflected");
            if (cap <= 0) return;
            const int fractionScale = 1000;
            int scaled = Mathf.Max(0, Mathf.RoundToInt(outcome.NoteDamage * LevelValue(fraction, fractionPerLevel, hook.Level) * fractionScale));
            int accumulated = hook.Get("reflect-remainder") + scaled;
            int amount = Mathf.Min(cap, accumulated / fractionScale);
            hook.Set("reflect-remainder", amount >= cap ? 0 : accumulated % fractionScale);
            if (amount <= 0) return;
            hook.Add("reflected", amount);
            hook.Runtime.Damage.DamageEnemy(amount, element, hook.SourceId, outcome.RootCauseId, CombatEventKind.ReflectDamage,
                applyAffinity: true, secondary: true, label: hook.Label);
        }
    }

    /// <summary>Conservation: a successful low-cost attack restores mana for later spells. Once per cast.</summary>
    [Serializable]
    public sealed class ConservationPassive : PassiveEffect
    {
        [SerializeField, Min(0)] private int maxPaidCost = 5;
        [SerializeField, Range(0f, 1f)] private float threshold = 0.6f;
        [SerializeField, Min(0)] private int mana = 5;
        [SerializeField, Min(0)] private int manaPerLevel = 3;

        public ConservationPassive() { }
        public ConservationPassive(int maxPaidCost, float threshold, int mana, int perLevel)
        {
            this.maxPaidCost = maxPaidCost;
            this.threshold = threshold;
            this.mana = mana;
            manaPerLevel = perLevel;
        }

        public override string Describe(int level) =>
            $"Attacks costing {maxPaidCost} MP or less, played at {Mathf.RoundToInt(threshold * 100f)}%+, restore {LevelValue(mana, manaPerLevel, level)} MP";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => (quote.Roles & AbilityRole.Damage) != 0 && quote.ManaCost <= maxPaidCost);

        public override void OnCastResolved(PassiveHook hook, CastSnapshot cast)
        {
            if (!cast.ExecutionEligible || !cast.HasRole(AbilityRole.Damage) || cast.PaidCost > maxPaidCost
                || cast.PerformanceWeight < threshold) return;
            if (!hook.Runtime.Events.TryClaim("conservation:" + cast.CastId)) return;
            hook.Runtime.RestorePassiveMana(LevelValue(mana, manaPerLevel, hook.Level), hook, cast.CastId);
        }
    }

    /// <summary>Follow-through: a successful buff / heal / defense action prepares one pending bonus for the next attack.</summary>
    [Serializable]
    public sealed class FollowThroughPassive : PassiveEffect
    {
        [SerializeField] private AbilityRole triggerRoles = AbilityRole.Buff | AbilityRole.Healing | AbilityRole.Defense;
        [SerializeField, Range(0f, 1f)] private float threshold = 0.5f;
        [SerializeField] private float bonus = 0.2f;
        [SerializeField] private float bonusPerLevel = 0.1f;
        [SerializeField, Min(1)] private int playerTurns = 2;

        public FollowThroughPassive() { }
        public FollowThroughPassive(float threshold, float bonus, float perLevel, int playerTurns)
        {
            this.threshold = threshold;
            this.bonus = bonus;
            bonusPerLevel = perLevel;
            this.playerTurns = playerTurns;
        }

        public override string Describe(int level) =>
            $"A support action played at {Mathf.RoundToInt(threshold * 100f)}%+ gives the next attack {BuildTagUtility.Percent(LevelValue(bonus, bonusPerLevel, level))}";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => (quote.Roles & triggerRoles) != 0) && equipped.Any(quote => (quote.Roles & AbilityRole.Damage) != 0);

        public override void OnCastResolved(PassiveHook hook, CastSnapshot cast)
        {
            if (!cast.ExecutionEligible || cast.HasRole(AbilityRole.Damage) || (cast.Roles & triggerRoles) == 0
                || cast.PerformanceWeight < threshold) return;
            hook.Runtime.ApplyBuff(new BuffSpec
            {
                Kind = BuffKind.NextAttackBonus,
                Strength = LevelValue(bonus, bonusPerLevel, hook.Level),
                Turns = playerTurns,
                SourceId = hook.SourceId,
                Label = hook.Label,
                Icon = hook.Definition != null ? hook.Definition.Icon : null
            }, cast.CastId);
        }
    }

    /// <summary>Buff mastery: improves a named parameter (duration / strength) of one buff kind.</summary>
    [Serializable]
    public sealed class BuffMasteryPassive : PassiveEffect
    {
        [SerializeField] private BuffKind kind = BuffKind.Reflect;
        [SerializeField, Min(0)] private int extraTurns = 1;
        [SerializeField, Min(0)] private int extraTurnsPerLevel;
        [SerializeField, Min(0f)] private float strengthBonus = 0.1f;
        [SerializeField, Min(0f)] private float strengthBonusPerLevel = 0.1f;

        public BuffMasteryPassive() { }
        public BuffMasteryPassive(BuffKind kind, int extraTurns, int turnsPerLevel, float strengthBonus, float strengthPerLevel)
        {
            this.kind = kind;
            this.extraTurns = extraTurns;
            extraTurnsPerLevel = turnsPerLevel;
            this.strengthBonus = strengthBonus;
            strengthBonusPerLevel = strengthPerLevel;
        }

        public override string Describe(int level) =>
            $"{kind} buffs: +{LevelValue(extraTurns, extraTurnsPerLevel, level)} turns, {BuildTagUtility.Percent(LevelValue(strengthBonus, strengthBonusPerLevel, level))} strength";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => quote.Effects.OfType<ApplyBuffEffect>().Any(buff => buff.Kind == kind));

        public override void ModifyBuff(PassiveHook hook, BuffSpec spec)
        {
            if (spec.Kind != kind) return;
            spec.Turns += LevelValue(extraTurns, extraTurnsPerLevel, hook.Level);
            spec.Strength *= 1f + LevelValue(strengthBonus, strengthBonusPerLevel, hook.Level);
        }
    }

    /// <summary>Status duration / potency enhancement for explicit applications of one status.</summary>
    [Serializable]
    public sealed class StatusMasteryPassive : PassiveEffect
    {
        [SerializeField] private string statusId = "burn";
        [SerializeField, Min(0)] private int extraTicks = 1;
        [SerializeField, Min(0)] private int extraTicksPerLevel;
        [SerializeField, Min(0f)] private float potencyBonus = 0.2f;
        [SerializeField, Min(0f)] private float potencyPerLevel = 0.1f;

        public StatusMasteryPassive() { }
        public StatusMasteryPassive(string statusId, int extraTicks, int ticksPerLevel, float potency, float potencyPerLevel)
        {
            this.statusId = statusId;
            this.extraTicks = extraTicks;
            extraTicksPerLevel = ticksPerLevel;
            potencyBonus = potency;
            this.potencyPerLevel = potencyPerLevel;
        }

        public override string Describe(int level) =>
            $"{statusId}: +{LevelValue(extraTicks, extraTicksPerLevel, level)} ticks, {BuildTagUtility.Percent(LevelValue(potencyBonus, potencyPerLevel, level))} damage per tick";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => quote.AppliedStatuses().Contains(statusId));

        public override void ModifyStatus(PassiveHook hook, StatusSpec spec, ref int damagePerTick, ref int ticks)
        {
            if (spec.statusId != statusId) return;
            ticks += LevelValue(extraTicks, extraTicksPerLevel, hook.Level);
            damagePerTick = Mathf.RoundToInt(damagePerTick * (1f + LevelValue(potencyBonus, potencyPerLevel, hook.Level)));
        }
    }

    /// <summary>Recovery conversion: part of overheal becomes shield (shared shield capacity and expiry).</summary>
    [Serializable]
    public sealed class RecoveryConversionPassive : PassiveEffect
    {
        [SerializeField, Range(0f, 1f)] private float fraction = 0.5f;
        [SerializeField, Range(0f, 1f)] private float fractionPerLevel = 0.15f;
        [SerializeField, Min(1)] private int enemyTurns = 2;

        public RecoveryConversionPassive() { }
        public RecoveryConversionPassive(float fraction, float perLevel, int enemyTurns)
        {
            this.fraction = fraction;
            fractionPerLevel = perLevel;
            this.enemyTurns = enemyTurns;
        }

        public override string Describe(int level) =>
            $"{Mathf.RoundToInt(LevelValue(fraction, fractionPerLevel, level) * 100f)}% of overheal becomes shield ({enemyTurns} enemy turns)";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(quote => quote.HealingElements().Any());

        public override void OnHealed(PassiveHook hook, HealOutcome heal)
        {
            if (heal.Secondary || heal.Overheal <= 0) return;
            int amount = Mathf.RoundToInt(heal.Overheal * Mathf.Clamp01(LevelValue(fraction, fractionPerLevel, hook.Level)));
            if (amount > 0) hook.Runtime.Damage.AddShield(amount, enemyTurns, hook.SourceId, heal.RootCauseId, secondary: true, label: hook.Label,
                icon: hook.Definition != null ? hook.Definition.Icon : null);
        }
    }

    /// <summary>Mana surge: casts that actually paid a lot of mana hit harder. Uses paid cost, never refunds.</summary>
    [Serializable]
    public sealed class ManaSurgePassive : PassiveEffect
    {
        [SerializeField, Min(1)] private int minimumPaid = 30;
        [SerializeField] private float bonus = 0.2f;
        [SerializeField] private float bonusPerLevel = 0.1f;

        public ManaSurgePassive() { }
        public ManaSurgePassive(int minimumPaid, float bonus, float perLevel) { this.minimumPaid = minimumPaid; this.bonus = bonus; bonusPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"Attacks that cost {minimumPaid}+ MP deal {BuildTagUtility.Percent(LevelValue(bonus, bonusPerLevel, level))} damage";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => (quote.Roles & AbilityRole.Damage) != 0 && quote.ManaCost >= minimumPaid);

        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            if (cast.PaidCost < minimumPaid || !cast.HasRole(AbilityRole.Damage)) return;
            cast.AddModifier(ModifierGroup.Execution, EffectKind.Damage, LevelValue(bonus, bonusPerLevel, hook.Level), hook.SourceId, hook.Label);
        }
    }

    /// <summary>Versatility: an action whose role differs from the previous action's gains a bonus (no rigid rotation).</summary>
    [Serializable]
    public sealed class VersatilityPassive : PassiveEffect
    {
        [SerializeField] private float bonus = 0.15f;
        [SerializeField] private float bonusPerLevel = 0.05f;

        public VersatilityPassive() { }
        public VersatilityPassive(float bonus, float perLevel) { this.bonus = bonus; bonusPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"Switching role from your previous action: {BuildTagUtility.Percent(LevelValue(bonus, bonusPerLevel, level))} damage / healing / shield";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Select(quote => BuildTagUtility.PrimaryRole(quote.Roles)).Where(role => role != AbilityRole.None).Distinct().Count() >= 2;

        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            AbilityRole previous = hook.Runtime.PreviousCastRole;
            AbilityRole current = BuildTagUtility.PrimaryRole(cast.Roles);
            if (previous == AbilityRole.None || current == AbilityRole.None || previous == current) return;
            float value = LevelValue(bonus, bonusPerLevel, hook.Level);
            string label = $"{hook.Label} ({previous} -> {current})";
            cast.AddModifier(ModifierGroup.Execution, EffectKind.Damage, value, hook.SourceId, label);
            cast.AddModifier(ModifierGroup.Execution, EffectKind.Healing, value, hook.SourceId, label);
            cast.AddModifier(ModifierGroup.Execution, EffectKind.Shield, value, hook.SourceId, label);
        }
    }

    /// <summary>Finite mitigation: reduces incoming note damage, up to a prevented-damage budget per enemy turn.</summary>
    [Serializable]
    public sealed class MitigationPassive : PassiveEffect
    {
        [SerializeField, Range(0f, 0.9f)] private float fraction = 0.2f;
        [SerializeField, Range(0f, 0.5f)] private float fractionPerLevel = 0.05f;
        [SerializeField, Min(1)] private int maxPreventedPerEnemyTurn = 40;
        [SerializeField, Min(0)] private int maxPerLevel = 15;

        public MitigationPassive() { }
        public MitigationPassive(float fraction, float perLevel, int maxPerTurn, int maxPerLevel)
        {
            this.fraction = fraction;
            fractionPerLevel = perLevel;
            maxPreventedPerEnemyTurn = maxPerTurn;
            this.maxPerLevel = maxPerLevel;
        }

        public override string Describe(int level) =>
            $"Take {Mathf.RoundToInt(LevelValue(fraction, fractionPerLevel, level) * 100f)}% less note damage (up to {LevelValue(maxPreventedPerEnemyTurn, maxPerLevel, level)} per enemy turn)";

        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) hook.Set("prevented", 0);
        }

        public override int ModifyIncomingDamage(PassiveHook hook, RhythmJudgementResult result, int amount)
        {
            int budget = LevelValue(maxPreventedPerEnemyTurn, maxPerLevel, hook.Level) - hook.Get("prevented");
            int prevent = Mathf.Min(budget, Mathf.RoundToInt(amount * Mathf.Clamp01(LevelValue(fraction, fractionPerLevel, hook.Level))));
            if (prevent <= 0) return amount;
            hook.Add("prevented", prevent);
            hook.Runtime.RecordContribution(hook.Label + " (prevented)", prevent);
            return amount - prevent;
        }
    }

    /// <summary>Whole-enemy-turn trigger: a clean defense (few misses) grants a shield. Fires once per enemy turn.</summary>
    [Serializable]
    public sealed class SteadyGuardPassive : PassiveEffect
    {
        [SerializeField, Min(0)] private int allowedMisses = 1;
        [SerializeField, Min(0)] private int shield = 30;
        [SerializeField, Min(0)] private int shieldPerLevel = 15;
        [SerializeField, Min(1)] private int enemyTurns = 1;

        public SteadyGuardPassive() { }
        public SteadyGuardPassive(int allowedMisses, int shield, int perLevel, int enemyTurns)
        {
            this.allowedMisses = allowedMisses;
            this.shield = shield;
            shieldPerLevel = perLevel;
            this.enemyTurns = enemyTurns;
        }

        public override string Describe(int level) =>
            $"An enemy turn with {allowedMisses} or fewer misses grants a {LevelValue(shield, shieldPerLevel, level)} shield";

        public override void OnEnemyTurnEnded(PassiveHook hook, EnemyTurnSummary summary)
        {
            if (!summary.Eligible || summary.Misses > allowedMisses) return;
            if (!hook.Runtime.Events.TryClaim($"steady-guard:{hook.Runtime.EncounterId}:{summary.EnemyTurn}")) return;
            hook.Runtime.Damage.AddShield(LevelValue(shield, shieldPerLevel, hook.Level), enemyTurns, hook.SourceId,
                "enemy-turn:" + summary.EnemyTurn, secondary: true, label: hook.Label, icon: hook.Definition != null ? hook.Definition.Icon : null);
        }
    }
}
