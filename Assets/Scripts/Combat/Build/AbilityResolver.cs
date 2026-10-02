using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// The effective configuration of one ability instance: what the player sees before committing and exactly what
    /// is paid on commit. Built by <see cref="AbilityResolver"/> from the definition, the instance's dedicated upgrades
    /// and the passive collection.
    /// </summary>
    public sealed class AbilityQuote
    {
        public AbilityDefinition Definition;
        public AbilityInstance Instance;
        public int BaseManaCost;
        public int ListedManaCost;
        public int ManaCost;
        public int BaseCooldown;
        public int Cooldown;
        /// <summary>Multiplier on Base Power from dedicated upgrades (1 = unchanged).</summary>
        public float PowerScale = 1f;
        public AbilityRole Roles;
        public AbilityDelivery Delivery;
        public ElementType Element;
        public readonly List<AbilityEffect> Effects = new();
        /// <summary>Human-readable list of what changed the ability (upgrade / passive name and what it did).</summary>
        public readonly List<string> Changes = new();

        public int BasePower => Definition != null ? Definition.BasePower : 0;

        public static AbilityQuote FromDefinition(AbilityDefinition definition)
        {
            var quote = new AbilityQuote { Definition = definition };
            if (definition == null) return quote;
            quote.BaseManaCost = quote.ManaCost = definition.ManaCost;
            quote.ListedManaCost = quote.ManaCost;
            quote.BaseCooldown = quote.Cooldown = definition.Cooldown;
            quote.Roles = definition.Roles;
            quote.Delivery = definition.Delivery;
            quote.Element = definition.Element;
            quote.Effects.AddRange(definition.Effects.Where(effect => effect != null));
            return quote;
        }

        /// <summary>Elements of every damage component this ability can deal (for passive activity and previews).</summary>
        public IEnumerable<ElementType> DamageElements(bool includeStatus = true)
        {
            foreach (AbilityEffect effect in Effects)
            {
                if (effect is DealDamageEffect damage) yield return damage.ResolveElement(Element);
                else if (effect is MultiHitDamageEffect) yield return Element;
                else if (includeStatus && effect is ApplyStatusEffect status) yield return status.Spec.element;
                else if (effect is IElementalEffect elemental && (includeStatus || elemental.DealsDamage)) yield return elemental.EffectElement(Element);
            }
        }

        public IEnumerable<ElementType> HealingElements() =>
            Effects.OfType<HealEffect>().Select(heal => heal.Element).Concat(Effects.OfType<RegenEffect>().Select(regen => regen.Element))
                .Concat(Effects.OfType<CauterizeEffect>().Select(_ => ElementType.Fire));

        /// <summary>Status-producing abilities share compatibility regardless of their effect implementation.</summary>
        public IEnumerable<string> AppliedStatuses()
        {
            foreach (AbilityEffect effect in Effects)
            {
                if (effect is ApplyStatusEffect status) yield return status.Spec.statusId;
                else if (effect is ApplyMarkEffect mark) yield return mark.MarkId;
                else if (effect is MarkPerPerfectEffect rhythmMark) yield return rhythmMark.MarkId;
                else if (effect is FlameGuardEffect) yield return ElementalMarks.Burn;
                else if (effect is ChainArcEffect) yield return ElementalMarks.Static;
            }
        }

        /// <summary>Every element this ability touches: its own element and any elemental component, mark or zap.</summary>
        public IEnumerable<ElementType> AllElements() =>
            new[] { Element }.Concat(DamageElements()).Concat(HealingElements()).Where(element => element != ElementType.None).Distinct();

        public string CostText => ManaCost == BaseManaCost ? ManaCost + " MP" : $"{ManaCost} MP (base {BaseManaCost})";
        public string CooldownText => Cooldown == BaseCooldown ? Cooldown + " turns" : $"{Cooldown} turns (base {BaseCooldown})";
    }

    /// <summary>Resolves the effective ability for preview and commitment. Pure: reads definitions and build state only.</summary>
    public static class AbilityResolver
    {
        public static AbilityQuote Resolve(AbilityDefinition definition, AbilityInstance instance, RunBuildState build)
        {
            AbilityQuote quote = AbilityQuote.FromDefinition(definition);
            quote.Instance = instance;
            if (definition == null) return quote;

            // 1. Dedicated upgrades (follow the ability instance wherever it is equipped).
            int costDelta = 0;
            int cooldownDelta = 0;
            if (instance != null)
            {
                foreach (AbilityUpgradeDefinition upgrade in instance.Upgrades)
                {
                    if (upgrade == null) continue;
                    quote.PowerScale *= 1f + upgrade.PowerBonus;
                    costDelta += upgrade.ManaCostDelta;
                    cooldownDelta += upgrade.CooldownDelta;
                    quote.Effects.AddRange(upgrade.AddedEffects.Where(effect => effect != null));
                    quote.Changes.Add(upgrade.DisplayName + ": " + upgrade.Summary());
                }
            }
            quote.ManaCost = Mathf.Max(0, quote.BaseManaCost + costDelta);
            quote.ListedManaCost = quote.ManaCost;
            quote.Cooldown = Mathf.Max(0, quote.BaseCooldown + cooldownDelta);

            // 2. Passive cost modifiers (magic efficiency), deterministic order: passive acquisition order.
            if (build != null)
            {
                foreach (PassiveInstance passive in build.Passives)
                {
                    if (passive?.Definition == null) continue;
                    foreach (PassiveEffect effect in passive.Definition.Effects)
                        effect?.ModifyQuote(quote, passive.Level, passive.Definition.DisplayName);
                }
            }
            // 3. Live battle state (a pending one-shot discount such as Tailwind) for the build in battle.
            CombatBuildRuntime live = CombatBuildRuntime.Active;
            if (live != null && build != null && live.Build == build) live.AdjustQuote(quote);
            quote.ManaCost = Mathf.Max(0, quote.ManaCost);
            ClampCost(quote);
            quote.Cooldown = Mathf.Max(0, quote.Cooldown);
            return quote;
        }

        public static void ClampCost(AbilityQuote quote)
        {
            if (quote.ListedManaCost <= 0) return;
            int floor = Mathf.CeilToInt(quote.ListedManaCost * (1f - BuildBalanceRules.Load().Synergy.maximumCostDiscount));
            if (quote.ManaCost < floor) quote.Changes.Add($"Combined discounts capped: {floor} MP minimum");
            quote.ManaCost = Mathf.Max(quote.ManaCost, floor);
        }
    }
}
