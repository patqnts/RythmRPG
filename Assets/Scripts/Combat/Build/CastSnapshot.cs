using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>One percentage modifier on a cast (e.g. +25% melee damage from a passive).</summary>
    public sealed class CastModifier
    {
        public ModifierGroup Group;
        public EffectKind Kind;
        /// <summary>Component filter (element); the action filter was checked when the modifier was added. Null = all.</summary>
        public EffectFilter Filter;
        public float Percent;
        public string SourceId;
        public string Label;
        /// <summary>Modifiers sharing a key add up to <see cref="StackCap"/> (0 = no extra cap).</summary>
        public string StackKey;
        public float StackCap;

        public bool Applies(EffectKind kind, ElementType element) =>
            Kind == kind && (Filter == null || !Filter.matchElement || Filter.element == element);
    }

    /// <summary>
    /// The resolved action snapshot of one committed cast: frozen cost, tags, source identity, reserved bonuses and
    /// the modifiers that apply to it. Pre-outcome modifiers are added until <see cref="Freeze"/>; after that the
    /// totals are fixed and only get distributed over the attack's hits.
    /// </summary>
    public sealed class CastSnapshot
    {
        private readonly List<CastModifier> modifiers = new();
        private readonly List<string> log = new();

        public string CastId;
        public int EncounterId;
        public int PlayerTurn;
        public AbilityRuntimeInstance Ability;
        public AbilityQuote Quote;
        public string AbilityInstanceId;
        /// <summary>Ability slot index (0-3), the input lane that selected it and the chart lanes are separate things.</summary>
        public int SlotIndex = -1;
        public int InputLane = -1;
        public int PaidCost;
        public int Cooldown;
        /// <summary>The visible battle Combo when the player committed this ability.</summary>
        public int ComboAtCommit;
        public RhythmPerformanceResult Performance;
        /// <summary>The chart had opportunities and was played to the end (not cancelled): execution-based bonuses may apply.</summary>
        public bool ExecutionEligible;
        public CastChoice Choice;
        public PhraseOutcome ClosingPhrase;
        public int SmallShieldBeforeCast;
        public int ShieldSpent;
        public int ShieldForBedrock;
        public bool BurnConsumed;
        public float SupportStrengthMultiplier = 1f;
        public bool Frozen { get; private set; }
        /// <summary>Buffs reserved at commitment (consumed by this cast, e.g. a pending next-attack bonus).</summary>
        public readonly List<ICombatModifierRuntime> Reserved = new();
        /// <summary>Successful Hold, Mash, or full Ping-Pong challenges completed during this cast.</summary>
        public readonly HashSet<string> CompletedChallenges = new();

        // Outcomes (filled by the damage service).
        public int DamageBeforeModifiers;
        public int DamageAfterModifiers;
        public int DamageAfterAffinity;
        public int DamageDealt;
        public int Healed;
        public int Overheal;
        public int ShieldGained;
        /// <summary>Approximate share of the outcome contributed by each modifier source.</summary>
        public readonly Dictionary<string, int> Contributions = new();

        public AbilityDefinition Definition => Quote?.Definition;
        public AbilityRole Roles => Quote?.Roles ?? AbilityRole.None;
        public AbilityDelivery Delivery => Quote?.Delivery ?? AbilityDelivery.None;
        public ElementType Element => Quote?.Element ?? ElementType.None;
        public float PerformanceWeight => Performance.AverageWeight;
        public IReadOnlyList<CastModifier> Modifiers => modifiers;
        public IReadOnlyList<string> Log => log;
        public bool HasRole(AbilityRole role) => (Roles & role) != 0;

        /// <summary>Adds a pre-outcome modifier. Ignored once the cast is frozen.</summary>
        public bool AddModifier(ModifierGroup group, EffectKind kind, float percent, string sourceId, string label,
            EffectFilter componentFilter = null, string stackKey = null, float stackCap = 0f)
        {
            if (Frozen || Mathf.Approximately(percent, 0f)) return false;
            modifiers.Add(new CastModifier
            {
                Group = group, Kind = kind, Percent = percent, SourceId = sourceId, Label = label,
                Filter = componentFilter, StackKey = stackKey, StackCap = stackCap
            });
            AddLog($"{label}: {BuildTagUtility.Percent(percent)} {kind.ToString().ToLowerInvariant()}" +
                   (componentFilter != null && componentFilter.matchElement ? $" ({BuildTagUtility.ElementName(componentFilter.element)})" : ""));
            return true;
        }

        public void Freeze() => Frozen = true;

        public void AddLog(string line)
        {
            if (!string.IsNullOrEmpty(line)) log.Add(line);
        }

        /// <summary>
        /// The combined multiplier for one component: bonuses add inside each group (stack keys capped), the group
        /// totals are clamped by the balance rules, groups multiply in enum order, the product is capped.
        /// </summary>
        public float Multiplier(EffectKind kind, ElementType element, BuildBalanceRules rules = null)
        {
            rules ??= BuildBalanceRules.Load();
            float total = 1f;
            foreach (IGrouping<ModifierGroup, CastModifier> group in modifiers.Where(m => m.Applies(kind, element))
                         .GroupBy(m => m.Group).OrderBy(g => (int)g.Key))
            {
                float sum = 0f;
                foreach (IGrouping<string, CastModifier> keyed in group.GroupBy(m => m.StackKey ?? string.Empty))
                {
                    float keyedSum = keyed.Sum(m => m.Percent);
                    float cap = keyed.Max(m => m.StackCap);
                    if (!string.IsNullOrEmpty(keyed.Key) && cap > 0f) keyedSum = Mathf.Min(keyedSum, cap);
                    sum += keyedSum;
                }
                total *= 1f + Mathf.Clamp(sum, rules.GroupPenaltyFloor, rules.GroupBonusCap);
            }
            return Mathf.Clamp(total, 0f, rules.TotalMultiplierCap);
        }

        /// <summary>Splits the bonus (final - base) of a component across the positive modifiers that produced it.</summary>
        public void Attribute(EffectKind kind, ElementType element, int baseAmount, int finalAmount)
        {
            int bonus = finalAmount - baseAmount;
            if (bonus <= 0) return;
            List<CastModifier> positive = modifiers.Where(m => m.Applies(kind, element) && m.Percent > 0f).ToList();
            float weight = positive.Sum(m => m.Percent);
            if (weight <= 0f) return;
            int assigned = 0;
            for (int i = 0; i < positive.Count; i++)
            {
                int share = i == positive.Count - 1 ? bonus - assigned : Mathf.RoundToInt(bonus * positive[i].Percent / weight);
                assigned += share;
                string key = positive[i].Label ?? positive[i].SourceId;
                Contributions.TryGetValue(key, out int existing);
                Contributions[key] = existing + share;
            }
        }
    }
}
