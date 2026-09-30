using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Gives the player a shield (absorbs incoming damage before health). Shares the capped shield capacity.</summary>
    [Serializable]
    public sealed class GainShieldEffect : AbilityEffect
    {
        [Tooltip("Shield = Base Power x this x performance (x shield modifiers).")]
        [SerializeField, Min(0f)] private float powerScale = 1f;
        [SerializeField, Min(1)] private int enemyTurns = 2;

        public GainShieldEffect() { }
        public GainShieldEffect(float scale, int turns) { powerScale = scale; enemyTurns = turns; }

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null) return;
            float minimum = context.Rules != null ? context.Rules.DefensiveMinimumPerformance : 0f;
            if (context.Performance.AverageWeight < minimum)
            {
                context.Build.Toast("SHIELD FAILED", new Color(0.7f, 0.7f, 0.7f));
                return;
            }
            int raw = context.ScaledPower(powerScale);
            int amount = Mathf.RoundToInt(raw * context.Multiplier(EffectKind.Shield, ElementType.None));
            context.Cast?.Attribute(EffectKind.Shield, ElementType.None, raw, amount);
            int added = context.Build.Damage.AddShield(amount, enemyTurns, context.Cast?.CastId ?? "cast", context.Cast?.CastId, secondary: false,
                icon: context.Ability != null ? context.Ability.Icon : null);
            if (context.Cast != null) context.Cast.ShieldGained += added;
        }

        public override string Describe(AbilityDefinition ability) => $"Shield x{powerScale:0.##} for {enemyTurns} enemy turns";
    }

    /// <summary>Applies a temporary buff to the player (next-attack bonus, reflection, damage reduction).</summary>
    [Serializable]
    public sealed class ApplyBuffEffect : AbilityEffect
    {
        [SerializeField] private BuffKind kind = BuffKind.NextAttackBonus;
        [Tooltip("Fraction: 0.4 = +40% next attack / reflect 40% / take 40% less.")]
        [SerializeField, Min(0f)] private float strength = 0.4f;
        [Tooltip("Scale strength by chart performance (0-1).")]
        [SerializeField] private bool scaleByPerformance = true;
        [Tooltip("NextAttackBonus: player turns. Reflect / DamageReduction: enemy turns.")]
        [SerializeField, Min(1)] private int turns = 1;
        [Tooltip("Reflect only: most damage reflected per enemy turn.")]
        [SerializeField, Min(0)] private int reflectCapPerTurn = 60;

        public ApplyBuffEffect() { }
        public ApplyBuffEffect(BuffKind kind, float strength, int turns, bool scaleByPerformance = true, int reflectCap = 60)
        {
            this.kind = kind;
            this.strength = strength;
            this.turns = turns;
            this.scaleByPerformance = scaleByPerformance;
            reflectCapPerTurn = reflectCap;
        }

        public BuffKind Kind => kind;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null) return;
            float minimum = context.Rules != null ? context.Rules.DefensiveMinimumPerformance : 0f;
            if (context.Performance.AverageWeight < minimum)
            {
                context.Build.Toast("BUFF FAILED", new Color(0.7f, 0.7f, 0.7f));
                return;
            }
            var spec = new BuffSpec
            {
                Kind = kind,
                Strength = strength * (scaleByPerformance ? Mathf.Clamp01(context.Performance.AverageWeight) : 1f),
                Turns = turns,
                PerTurnCap = reflectCapPerTurn,
                SourceId = context.Cast?.AbilityInstanceId ?? context.Ability?.Id ?? "ability",
                Label = context.Ability != null ? context.Ability.DisplayName : kind.ToString(),
                Icon = context.Ability != null ? context.Ability.Icon : null
            };
            context.Build.ApplyBuff(spec, context.Cast?.CastId);
        }

        public override string Describe(AbilityDefinition ability) => kind switch
        {
            BuffKind.NextAttackBonus => $"Next attack +{Mathf.RoundToInt(strength * 100f)}% ({turns} turns)",
            BuffKind.Reflect => $"Reflect {Mathf.RoundToInt(strength * 100f)}% on Perfect defense ({turns} enemy turns, cap {reflectCapPerTurn}/turn)",
            _ => $"Take {Mathf.RoundToInt(strength * 100f)}% less damage ({turns} enemy turns)"
        };
    }

    /// <summary>Applies a status to the enemy. Its potency is snapshotted now; enemy responses may block or scale it.</summary>
    [Serializable]
    public sealed class ApplyStatusEffect : AbilityEffect
    {
        [SerializeField] private StatusSpec spec = new();
        [Tooltip("Below this chart performance the status is not applied.")]
        [SerializeField, Range(0f, 1f)] private float minimumPerformance = 0.25f;

        public ApplyStatusEffect() { }
        public ApplyStatusEffect(StatusSpec spec, float minimumPerformance = 0.25f)
        {
            this.spec = spec;
            this.minimumPerformance = minimumPerformance;
        }

        public StatusSpec Spec => spec;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null || spec == null) return;
            if (context.Performance.AverageWeight < minimumPerformance) return;
            int perTick = context.ScaledPower(spec.powerScalePerTick);
            context.Build.ApplyStatus(spec, perTick, context.Cast?.CastId, context.Ability != null ? context.Ability.Icon : null);
        }

        public override string Describe(AbilityDefinition ability) =>
            $"{spec.displayName}: {BuildTagUtility.ElementName(spec.element)} x{spec.powerScalePerTick:0.##} per tick, {spec.ticks} ticks";
    }

    /// <summary>
    /// Counter payoff: spends stored counter charges into this cast before its damage is frozen.
    /// A cast with this effect replaces any automatic counter spending from passives.
    /// </summary>
    [Serializable]
    public sealed class SpendCountersEffect : AbilityEffect
    {
        [SerializeField, Min(1)] private int maxCharges = 5;
        [SerializeField, Min(0f)] private float damagePerCharge = 0.3f;

        public SpendCountersEffect() { }
        public SpendCountersEffect(int maxCharges, float damagePerCharge) { this.maxCharges = maxCharges; this.damagePerCharge = damagePerCharge; }

        public override void BeforeFreeze(AbilityEffectContext context)
        {
            if (context.Build == null || context.Cast == null || context.Cast.Frozen) return;
            int spent = context.Build.Counters.Spend(maxCharges, context.Cast.CastId, "counter-spend");
            if (spent <= 0) return;
            context.Cast.AddModifier(ModifierGroup.Counter, EffectKind.Damage, spent * damagePerCharge,
                context.Cast.AbilityInstanceId, $"Counterpower x{spent}");
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Spend up to {maxCharges} counter charges: +{Mathf.RoundToInt(damagePerCharge * 100f)}% damage each";
    }

    /// <summary>Counter preparation as an action: gain counter charges (more on a strong chart).</summary>
    [Serializable]
    public sealed class GainCountersEffect : AbilityEffect
    {
        [SerializeField, Min(0)] private int charges = 1;
        [SerializeField, Min(0)] private int bonusChargesAtThreshold = 1;
        [SerializeField, Range(0f, 1f)] private float threshold = 0.85f;

        public GainCountersEffect() { }
        public GainCountersEffect(int charges, int bonus, float threshold) { this.charges = charges; bonusChargesAtThreshold = bonus; this.threshold = threshold; }

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null) return;
            bool strong = context.Cast != null ? context.Cast.ExecutionEligible && context.Performance.AverageWeight >= threshold
                : context.Performance.AverageWeight >= threshold;
            int amount = charges + (strong ? bonusChargesAtThreshold : 0);
            context.Build.Counters.Gain(amount, context.Cast?.AbilityInstanceId ?? "ability", context.Cast?.CastId);
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Gain {charges} counter charge(s) (+{bonusChargesAtThreshold} at {Mathf.RoundToInt(threshold * 100f)}% performance)";
    }
}
