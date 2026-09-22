using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>When an effect lands relative to the attack's hits (impact steps, projectile hits, channel ticks...).</summary>
    public enum EffectTiming
    {
        /// <summary>Amount effects (damage, heal) are divided across all hits by hit weight; others land on the first hit.</summary>
        SplitAcrossHits,
        FirstHit,
        LastHit
    }

    /// <summary>What an effect can touch while an ability resolves.</summary>
    public sealed class AbilityEffectContext
    {
        public PlayerCombatant Player;
        public EnemyCombatant Enemy;
        public AbilityDefinition Ability;
        public RhythmPerformanceResult Performance;
        /// <summary>Lane of the ability slot the player used (-1 if unknown).</summary>
        public int LaneId = -1;
        public CombatModifierSystem Modifiers;
        public CombatResourceRules Rules;
        /// <summary>Optional floating text (value, world position, colour).</summary>
        public Action<string, Vector3, Color> ShowText;

        public int BasePower => Ability != null ? Ability.BasePower : 0;
        /// <summary>basePower x scale x performance (0-1 average judgement weight of the ability chart).</summary>
        public int ScaledPower(float scale) =>
            RhythmPerformanceCalculator.CalculatePower(Mathf.RoundToInt(BasePower * Mathf.Max(0f, scale)), Performance);
    }

    /// <summary>
    /// One outcome of an ability (damage, heal, ward, ...). Subclass it for new outcomes; the ability inspector's
    /// effect picker finds subclasses automatically. Amount effects override <see cref="TotalAmount"/> and
    /// <see cref="ApplyAmount"/> so the attack sequence can divide them over several hits.
    /// </summary>
    [Serializable]
    public abstract class AbilityEffect
    {
        [SerializeField] private EffectTiming timing = EffectTiming.SplitAcrossHits;

        public EffectTiming Timing => timing;
        public virtual bool IsAmount => false;
        public virtual int TotalAmount(AbilityEffectContext context) => 0;
        public virtual void ApplyAmount(AbilityEffectContext context, int amount) { }
        public virtual void ApplyOnce(AbilityEffectContext context) { }
    }

    [Serializable]
    public sealed class DealDamageEffect : AbilityEffect
    {
        [Tooltip("x the ability's Base Power, then x performance.")]
        [SerializeField, Min(0f)] private float powerScale = 1f;

        public DealDamageEffect() { }
        public DealDamageEffect(float scale) => powerScale = scale;

        public override bool IsAmount => true;
        public override int TotalAmount(AbilityEffectContext context) => context.ScaledPower(powerScale);
        public override void ApplyAmount(AbilityEffectContext context, int amount)
        {
            if (amount > 0 && context.Enemy != null) context.Enemy.ApplyDamage(amount);
        }
    }

    [Serializable]
    public sealed class HealEffect : AbilityEffect
    {
        [Tooltip("x the ability's Base Power, then x performance.")]
        [SerializeField, Min(0f)] private float powerScale = 1f;

        public HealEffect() { }
        public HealEffect(float scale) => powerScale = scale;

        public override bool IsAmount => true;
        public override int TotalAmount(AbilityEffectContext context) => context.ScaledPower(powerScale);
        public override void ApplyAmount(AbilityEffectContext context, int amount)
        {
            if (amount <= 0 || context.Player == null) return;
            int healed = context.Player.Heal(amount);
            if (healed > 0) context.ShowText?.Invoke("+" + healed, context.Player.transform.position + Vector3.up * 1.2f,
                new Color(0.45f, 1f, 0.55f));
        }
    }

    [Serializable]
    public sealed class RestoreManaEffect : AbilityEffect
    {
        [SerializeField, Min(0)] private int amount = 10;
        [Tooltip("Scale the amount by ability performance.")]
        [SerializeField] private bool scaleByPerformance = true;

        public override bool IsAmount => true;
        public override int TotalAmount(AbilityEffectContext context) => scaleByPerformance
            ? Mathf.RoundToInt(amount * Mathf.Clamp01(context.Performance.AverageWeight)) : amount;
        public override void ApplyAmount(AbilityEffectContext context, int value)
        {
            if (value > 0 && context.Player != null) context.Player.GainMana(value);
        }
    }

    /// <summary>Makes lanes invulnerable: Bad / Miss notes in them deal no damage for some enemy turns.</summary>
    [Serializable]
    public sealed class WardLanesEffect : AbilityEffect
    {
        public enum LaneScope { SelectedLane, AllLanes, SpecificLanes }

        [SerializeField] private LaneScope lanes = LaneScope.SelectedLane;
        [Tooltip("SpecificLanes only.")]
        [SerializeField] private List<int> laneIds = new();
        [Tooltip("Enemy turns the ward lasts.")]
        [SerializeField, Min(1)] private int enemyTurns = 2;
        [Tooltip("Extra enemy turn when the ability chart was played at or above this performance (0 = never).")]
        [SerializeField, Range(0f, 1f)] private float bonusTurnAtPerformance = 0.9f;
        [SerializeField] private bool blockBad = true;
        [SerializeField] private bool blockMiss = true;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Modifiers == null) return;
            float performance = context.Performance.AverageWeight;
            Vector3 at = context.Player != null ? context.Player.transform.position + Vector3.up * 1.2f : Vector3.zero;
            float minimum = context.Rules != null ? context.Rules.DefensiveMinimumPerformance : 0f;
            if (performance < minimum)
            {
                context.ShowText?.Invoke("FAILED", at, new Color(0.7f, 0.7f, 0.7f));
                return;
            }

            int turns = enemyTurns + (bonusTurnAtPerformance > 0f && performance >= bonusTurnAtPerformance ? 1 : 0);
            HashSet<int> set = lanes switch
            {
                LaneScope.AllLanes => null,
                LaneScope.SpecificLanes => new HashSet<int>(laneIds),
                _ => context.LaneId >= 0 ? new HashSet<int> { context.LaneId } : null
            };
            context.Modifiers.Add(new LaneWardModifier(set, turns, blockBad, blockMiss));
            string where = set == null ? "ALL LANES" : "LANE " + string.Join(",", set);
            context.ShowText?.Invoke("WARD " + where + " x" + turns, at, new Color(0.6f, 0.8f, 1f));
        }
    }

    /// <summary>
    /// Runtime resolution of one ability use: applies its effects as the attack's hits land. Amount effects are
    /// divided by hit weight (rounded so the total is exact); once-effects land on their chosen hit.
    /// </summary>
    public sealed class AbilityResolution
    {
        private readonly AbilityEffectContext context;
        private readonly IReadOnlyList<AbilityEffect> effects;
        private readonly float totalWeight;
        private readonly Dictionary<AbilityEffect, int> appliedAmount = new();
        private readonly HashSet<AbilityEffect> appliedOnce = new();
        private float delivered;
        private bool anyHit;
        private bool finished;

        public AbilityResolution(AbilityEffectContext context, IReadOnlyList<AbilityEffect> effects, float totalWeight)
        {
            this.context = context;
            this.effects = effects ?? Array.Empty<AbilityEffect>();
            this.totalWeight = Mathf.Max(0f, totalWeight);
        }

        public AbilityEffectContext Context => context;
        public bool IsFinished => finished;

        /// <summary>One hit of the attack with the given weight (share = weight / total weight of the sequence).</summary>
        public void Hit(float weight)
        {
            if (finished) return;
            delivered += Mathf.Max(0f, weight);
            float progress = totalWeight <= 0f ? 1f : Mathf.Clamp01(delivered / totalWeight);
            Apply(progress, !anyHit, progress >= 0.9999f);
            anyHit = true;
            if (progress >= 0.9999f) finished = true;
        }

        /// <summary>Delivers whatever is left (the sequence ended, was cut short, or had no hits).</summary>
        public void Finish()
        {
            if (finished) return;
            Apply(1f, !anyHit, true);
            anyHit = true;
            finished = true;
        }

        private void Apply(float progress, bool first, bool last)
        {
            foreach (AbilityEffect effect in effects)
            {
                if (effect == null) continue;
                bool due = effect.Timing switch
                {
                    EffectTiming.FirstHit => first,
                    EffectTiming.LastHit => last,
                    _ => true
                };
                if (!due) continue;

                if (effect.IsAmount)
                {
                    int total = effect.TotalAmount(context);
                    int target = effect.Timing == EffectTiming.SplitAcrossHits ? Mathf.RoundToInt(total * progress) : total;
                    appliedAmount.TryGetValue(effect, out int done);
                    int delta = target - done;
                    if (delta <= 0) continue;
                    appliedAmount[effect] = done + delta;
                    effect.ApplyAmount(context, delta);
                }
                else if (appliedOnce.Add(effect))
                {
                    effect.ApplyOnce(context);
                }
            }
        }

        /// <summary>Effects used when an ability lists none: damage for attacks, heal for healing, nothing otherwise.</summary>
        public static IReadOnlyList<AbilityEffect> DefaultEffects(AbilityType type) => type switch
        {
            AbilityType.BasicAttack or AbilityType.SpecialAttack => new AbilityEffect[] { new DealDamageEffect(1f) },
            AbilityType.Healing => new AbilityEffect[] { new HealEffect(1f) },
            _ => Array.Empty<AbilityEffect>()
        };
    }
}
