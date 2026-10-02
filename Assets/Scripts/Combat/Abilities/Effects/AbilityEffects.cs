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
        /// <summary>Build runtime (passives, damage service, buffs). Null outside a build-aware encounter (tests, legacy).</summary>
        public CombatBuildRuntime Build;
        /// <summary>The committed cast this resolution belongs to (frozen cost, tags, modifiers). May be null.</summary>
        public CastSnapshot Cast;

        public int BasePower => Ability != null ? Ability.BasePower : 0;
        /// <summary>Base Power multiplier from dedicated upgrades on the cast ability instance.</summary>
        public float UpgradePowerScale => Cast?.Quote != null ? Cast.Quote.PowerScale : 1f;
        public AbilityRole Roles => Cast != null ? Cast.Roles : Ability != null ? Ability.Roles : AbilityRole.None;
        public AbilityDelivery Delivery => Cast != null ? Cast.Delivery : Ability != null ? Ability.Delivery : AbilityDelivery.None;
        public ElementType AbilityElement => Cast != null ? Cast.Element : Ability != null ? Ability.Element : ElementType.None;

        /// <summary>basePower x scale x upgrades x performance (0-1 average judgement weight of the ability chart).</summary>
        public int ScaledPower(float scale) =>
            RhythmPerformanceCalculator.CalculatePower(Mathf.RoundToInt(BasePower * Mathf.Max(0f, scale) * UpgradePowerScale), Performance);

        /// <summary>Combined cast modifiers for one component (1 when there is no cast snapshot).</summary>
        public float Multiplier(EffectKind kind, ElementType element) =>
            Cast != null ? Cast.Multiplier(kind, element, Build != null ? Build.Rules : null) : 1f;
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

        public virtual EffectTiming Timing => timing;
        public virtual bool IsAmount => false;
        public virtual int TotalAmount(AbilityEffectContext context) => 0;
        /// <summary>
        /// Called once when the cast's totals are frozen (after pre-outcome modifiers). Defaults to
        /// <see cref="TotalAmount"/>; effects override it to record their breakdown on the cast.
        /// </summary>
        public virtual int FreezeAmount(AbilityEffectContext context) => TotalAmount(context);
        /// <summary>Pre-outcome hook, before any total is frozen (e.g. spend counter charges into a cast modifier).</summary>
        public virtual void BeforeFreeze(AbilityEffectContext context) { }
        /// <summary>Short text for previews.</summary>
        public virtual string Describe(AbilityDefinition ability) => GetType().Name.Replace("Effect", string.Empty);
        public virtual void ApplyAmount(AbilityEffectContext context, int amount) { }
        public virtual void ApplyOnce(AbilityEffectContext context) { }
    }

    /// <summary>Where a damage component's element comes from.</summary>
    public enum DamageElementSource
    {
        /// <summary>The ability's element (documented inheritance rule for damage components).</summary>
        AbilityElement,
        /// <summary>Always physical, whatever the ability's element.</summary>
        Physical,
        /// <summary>The element set on the effect.</summary>
        Specific
    }

    /// <summary>
    /// One damage component. Physical and elemental components of a cast resolve separately:
    /// authored amount x rhythm performance x matching offensive modifiers x the target's affinity for this element.
    /// </summary>
    [Serializable]
    public sealed class DealDamageEffect : AbilityEffect
    {
        [Tooltip("x the ability's Base Power, then x performance.")]
        [SerializeField, Min(0f)] private float powerScale = 1f;
        [Tooltip("Element of this component. Ability Element = inherit the ability's element (None = physical).")]
        [SerializeField] private DamageElementSource elementSource = DamageElementSource.AbilityElement;
        [SerializeField] private ElementType specificElement = ElementType.None;

        public DealDamageEffect() { }
        public DealDamageEffect(float scale) => powerScale = scale;
        public DealDamageEffect(float scale, ElementType element)
        {
            powerScale = scale;
            elementSource = element == ElementType.None ? DamageElementSource.Physical : DamageElementSource.Specific;
            specificElement = element;
        }

        public float PowerScale => powerScale;

        public ElementType ResolveElement(ElementType abilityElement) => elementSource switch
        {
            DamageElementSource.Physical => ElementType.None,
            DamageElementSource.Specific => specificElement,
            _ => abilityElement
        };

        public override bool IsAmount => true;

        public override int TotalAmount(AbilityEffectContext context)
        {
            ElementType element = ResolveElement(context.AbilityElement);
            int raw = context.ScaledPower(powerScale);
            float modified = raw * context.Multiplier(EffectKind.Damage, element);
            float affinity = context.Build != null ? context.Build.Damage.Affinity(element)
                : context.Enemy != null ? context.Enemy.AffinityFor(element) : 1f;
            return Mathf.Max(0, Mathf.RoundToInt(modified * affinity));
        }

        public override int FreezeAmount(AbilityEffectContext context)
        {
            ElementType element = ResolveElement(context.AbilityElement);
            int raw = context.ScaledPower(powerScale);
            int modified = Mathf.RoundToInt(raw * context.Multiplier(EffectKind.Damage, element));
            int total = TotalAmount(context);
            if (context.Cast != null)
            {
                context.Cast.DamageBeforeModifiers += raw;
                context.Cast.DamageAfterModifiers += modified;
                context.Cast.DamageAfterAffinity += total;
                context.Cast.Attribute(EffectKind.Damage, element, raw, modified);
                if (modified != total)
                    context.Cast.AddLog($"{BuildTagUtility.ElementName(element)} affinity x{(modified == 0 ? 1f : (float)total / modified):0.##}: {modified} -> {total}");
            }
            return total;
        }

        public override void ApplyAmount(AbilityEffectContext context, int amount)
        {
            if (amount <= 0) return;
            if (context.Build != null)
            {
                // Affinity was applied when the total was frozen: one resolved affinity per component.
                context.Build.Damage.DamageEnemy(amount, ResolveElement(context.AbilityElement), context.Cast?.CastId ?? "cast",
                    context.Cast?.CastId, CombatEventKind.AbilityDamage, applyAffinity: false, cast: context.Cast);
            }
            else if (context.Enemy != null) context.Enemy.ApplyDamage(amount);
        }

        public override string Describe(AbilityDefinition ability)
        {
            ElementType element = ResolveElement(ability != null ? ability.Element : ElementType.None);
            return $"{BuildTagUtility.ElementName(element)} damage x{powerScale:0.##}";
        }
    }

    /// <summary>
    /// Heals the player. Healing never inherits the ability's element (so a fire strike's element cannot change how
    /// its self-heal is treated); set Element explicitly for element-scoped healing bonuses. Enemy affinity never
    /// affects it.
    /// </summary>
    [Serializable]
    public sealed class HealEffect : AbilityEffect
    {
        [Tooltip("x the ability's Base Power, then x performance.")]
        [SerializeField, Min(0f)] private float powerScale = 1f;
        [Tooltip("Element attribution of this heal (for element healing enhancements). Not inherited from the ability.")]
        [SerializeField] private ElementType element = ElementType.None;

        public HealEffect() { }
        public HealEffect(float scale) => powerScale = scale;
        public HealEffect(float scale, ElementType element) { powerScale = scale; this.element = element; }

        public ElementType Element => element;
        public override bool IsAmount => true;

        public override int TotalAmount(AbilityEffectContext context) =>
            Mathf.Max(0, Mathf.RoundToInt(context.ScaledPower(powerScale) * context.Multiplier(EffectKind.Healing, element)));

        public override int FreezeAmount(AbilityEffectContext context)
        {
            int total = TotalAmount(context);
            context.Cast?.Attribute(EffectKind.Healing, element, context.ScaledPower(powerScale), total);
            return total;
        }

        public override void ApplyAmount(AbilityEffectContext context, int amount)
        {
            if (amount <= 0 || context.Player == null) return;
            int healed = context.Build != null
                ? context.Build.Damage.HealPlayer(amount, element, context.Cast?.CastId ?? "cast", context.Cast?.CastId, context.Cast).Actual
                : context.Player.Heal(amount);
            if (healed > 0) context.ShowText?.Invoke("+" + healed, context.Player.transform.position + Vector3.up * 1.2f,
                new Color(0.45f, 1f, 0.55f));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Heal x{powerScale:0.##}" + (element != ElementType.None ? $" ({element})" : string.Empty);
    }

    [Serializable]
    public sealed class RestoreManaEffect : AbilityEffect
    {
        [SerializeField, Min(0)] private int amount = 10;
        [Tooltip("Scale the amount by ability performance.")]
        [SerializeField] private bool scaleByPerformance = true;

        public RestoreManaEffect() { }
        public RestoreManaEffect(int amount, bool scaleByPerformance = true)
        {
            this.amount = amount;
            this.scaleByPerformance = scaleByPerformance;
        }

        public override bool IsAmount => true;
        public override int TotalAmount(AbilityEffectContext context) => scaleByPerformance
            ? Mathf.RoundToInt(amount * Mathf.Clamp01(context.Performance.AverageWeight)) : amount;
        public override void ApplyAmount(AbilityEffectContext context, int value)
        {
            if (value <= 0 || context.Player == null) return;
            if (context.Build != null) context.Build.Damage.RestoreMana(value, context.Cast?.CastId ?? "cast", context.Cast?.CastId, secondary: false);
            else context.Player.GainMana(value);
        }

        public override string Describe(AbilityDefinition ability) => $"Restore {amount} MP";
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
            context.Modifiers.Add(new LaneWardModifier(set, turns, blockBad, blockMiss, turns - enemyTurns)
            {
                Icon = context.Ability != null ? context.Ability.Icon : null,
                Label = context.Ability != null ? context.Ability.DisplayName : "Ward"
            });
            string where = set == null ? "ALL LANES" : "LANE " + string.Join(",", set);
            context.ShowText?.Invoke("WARD " + where + " x" + turns, at, new Color(0.6f, 0.8f, 1f));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Ward {(lanes == LaneScope.AllLanes ? "all lanes" : lanes == LaneScope.SelectedLane ? "the used lane" : "lanes")} for {enemyTurns} enemy turns";
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
        private readonly Dictionary<AbilityEffect, int> frozenTotals = new();
        private readonly HashSet<AbilityEffect> appliedOnce = new();
        private float delivered;
        private bool anyHit;
        private bool finished;

        public AbilityResolution(AbilityEffectContext context, IReadOnlyList<AbilityEffect> effects, float totalWeight)
        {
            this.context = context;
            this.effects = effects ?? Array.Empty<AbilityEffect>();
            this.totalWeight = Mathf.Max(0f, totalWeight);

            // Pre-outcome hooks, then freeze every total once: hits only distribute these amounts, they never
            // recompute them, so multi-hit presentation cannot change (or duplicate) the outcome.
            foreach (AbilityEffect effect in this.effects) effect?.BeforeFreeze(context);
            context?.Cast?.Freeze();
            foreach (AbilityEffect effect in this.effects)
                if (effect != null && effect.IsAmount && !frozenTotals.ContainsKey(effect))
                    frozenTotals[effect] = Mathf.Max(0, effect.FreezeAmount(context));
        }

        /// <summary>The frozen total of an amount effect (0 for once-effects).</summary>
        public int FrozenTotal(AbilityEffect effect) => effect != null && frozenTotals.TryGetValue(effect, out int total) ? total : 0;

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
                    frozenTotals.TryGetValue(effect, out int total);
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
