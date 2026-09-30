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
                else if (includeStatus && effect is ApplyStatusEffect status) yield return status.Spec.element;
            }
        }

        public IEnumerable<ElementType> HealingElements() =>
            Effects.OfType<HealEffect>().Select(heal => heal.Element);

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
            quote.ManaCost = Mathf.Max(0, quote.ManaCost);
            quote.Cooldown = Mathf.Max(0, quote.Cooldown);
            return quote;
        }
    }
}
