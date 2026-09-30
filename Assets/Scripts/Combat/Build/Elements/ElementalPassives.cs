using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    // Passives for the elemental system (marks, reactions, board effects). Values are "first level + per extra level".

    internal static class ElementPassiveUtility
    {
        public static bool Uses(IReadOnlyList<AbilityQuote> equipped, ElementType element) =>
            equipped.Any(quote => quote.AllElements().Contains(element));

        public static bool CastUses(CastSnapshot cast, ElementType element) =>
            cast?.Quote != null && cast.Quote.AllElements().Contains(element);
    }

    /// <summary>Pyre Keeper / Capacitor: a mark stacks higher and hits harder (Burn ticks, Static discharges).</summary>
    [Serializable]
    public sealed class MarkMasteryPassive : PassiveEffect
    {
        [SerializeField] private ApplyMarkEffect.Mark mark = ApplyMarkEffect.Mark.Burn;
        [SerializeField, Min(0)] private int extraStacks = 2;
        [SerializeField, Min(0)] private int extraStacksPerLevel = 1;
        [SerializeField, Min(0f)] private float potency = 0.1f;
        [SerializeField, Min(0f)] private float potencyPerLevel = 0.1f;

        public MarkMasteryPassive() { }
        public MarkMasteryPassive(ApplyMarkEffect.Mark mark, int extraStacks, int stacksPerLevel, float potency, float potencyPerLevel)
        {
            this.mark = mark;
            this.extraStacks = extraStacks;
            extraStacksPerLevel = stacksPerLevel;
            this.potency = potency;
            this.potencyPerLevel = potencyPerLevel;
        }

        private string MarkId => ApplyMarkEffect.MarkIdOf(mark);

        public override string Describe(int level)
        {
            string what = mark == ApplyMarkEffect.Mark.Static ? "discharge" : "tick damage";
            return $"{ElementalMarks.DisplayName(MarkId)}: +{LevelValue(extraStacks, extraStacksPerLevel, level)} max stacks, " +
                   $"{BuildTagUtility.Percent(LevelValue(potency, potencyPerLevel, level))} {what}";
        }

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            ElementPassiveUtility.Uses(equipped, ElementalMarks.ElementOf(MarkId));

        public override int StatusStackBonus(PassiveHook hook, string statusId) =>
            statusId == MarkId ? LevelValue(extraStacks, extraStacksPerLevel, hook.Level) : 0;

        public override void ModifyStatus(PassiveHook hook, StatusSpec spec, ref int damagePerTick, ref int ticks)
        {
            if (spec.statusId != MarkId) return;
            damagePerTick = Mathf.RoundToInt(damagePerTick * (1f + LevelValue(potency, potencyPerLevel, hook.Level)));
        }
    }

    /// <summary>Conductor: the first Perfects each enemy turn zap the next note (destroyed + Lightning damage).</summary>
    [Serializable]
    public sealed class ConductorPassive : PassiveEffect
    {
        [SerializeField, Min(1)] private int perfectsPerTurn = 3;
        [SerializeField, Min(0)] private int perfectsPerLevel = 1;
        [SerializeField, Min(1)] private int notesPerZap = 1;
        [SerializeField, Min(0)] private int damage = 8;
        [SerializeField, Min(0)] private int damagePerLevel = 4;

        public ConductorPassive() { }
        public ConductorPassive(int perfectsPerTurn, int perLevel, int notesPerZap, int damage, int damagePerLevel)
        {
            this.perfectsPerTurn = perfectsPerTurn;
            perfectsPerLevel = perLevel;
            this.notesPerZap = notesPerZap;
            this.damage = damage;
            this.damagePerLevel = damagePerLevel;
        }

        public override string Describe(int level) =>
            $"The first {LevelValue(perfectsPerTurn, perfectsPerLevel, level)} Perfects each enemy turn zap {notesPerZap} note " +
            $"({LevelValue(damage, damagePerLevel, level)} Lightning each)";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => ElementPassiveUtility.Uses(equipped, ElementType.Lightning);

        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) hook.Set("used", 0);
        }

        public override void OnDefenseNoteSettled(PassiveHook hook, DefenseNoteOutcome outcome)
        {
            if (!outcome.IsPlayerExecution || outcome.Result.Judgement != HitJudgement.Perfect) return;
            if (hook.Get("used") >= LevelValue(perfectsPerTurn, perfectsPerLevel, hook.Level)) return;
            CombatBuildRuntime runtime = hook.Runtime;
            int count = notesPerZap + (runtime.Marks.Conducting ? runtime.Rules.Elements.conductExtraZaps : 0);
            if (runtime.Zap(count, LevelValue(damage, damagePerLevel, hook.Level), outcome.Result.WorldPosition, hook.SourceId,
                    outcome.RootCauseId, hook.Label, secondary: true) > 0)
                hook.Add("used", 1);
        }
    }

    /// <summary>Riptide: overhealing from Water heals becomes Water damage to the enemy.</summary>
    [Serializable]
    public sealed class RiptidePassive : PassiveEffect
    {
        [SerializeField, Min(0f)] private float fraction = 0.5f;
        [SerializeField, Min(0f)] private float fractionPerLevel = 0.25f;

        public RiptidePassive() { }
        public RiptidePassive(float fraction, float perLevel) { this.fraction = fraction; fractionPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"{Mathf.RoundToInt(LevelValue(fraction, fractionPerLevel, level) * 100f)}% of Water overheal hits the enemy as Water damage";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => quote.HealingElements().Contains(ElementType.Water));

        public override void OnHealed(PassiveHook hook, HealOutcome heal)
        {
            if (heal.Element != ElementType.Water || heal.Overheal <= 0) return;
            int amount = Mathf.RoundToInt(heal.Overheal * LevelValue(fraction, fractionPerLevel, hook.Level));
            if (amount > 0)
                hook.Runtime.Damage.DamageEnemy(amount, ElementType.Water, hook.SourceId, heal.RootCauseId, CombatEventKind.PassiveDamage,
                    applyAffinity: true, secondary: true, label: hook.Label);
        }
    }

    /// <summary>Bedrock: while you have a shield, melee attacks deal a share of the shield as bonus damage.</summary>
    [Serializable]
    public sealed class BedrockPassive : PassiveEffect
    {
        [SerializeField, Min(0f)] private float fraction = 0.1f;
        [SerializeField, Min(0f)] private float fractionPerLevel = 0.05f;

        public BedrockPassive() { }
        public BedrockPassive(float fraction, float perLevel) { this.fraction = fraction; fractionPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"While shielded, melee attacks deal +{Mathf.RoundToInt(LevelValue(fraction, fractionPerLevel, level) * 100f)}% of the shield as damage";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => (quote.Delivery & AbilityDelivery.Melee) != 0 && (quote.Roles & AbilityRole.Damage) != 0);

        public override void OnCastResolved(PassiveHook hook, CastSnapshot cast)
        {
            if ((cast.Delivery & AbilityDelivery.Melee) == 0 || cast.DamageDealt <= 0) return;
            ShieldBuff shield = hook.Runtime.Modifiers?.Find<ShieldBuff>(ShieldBuff.Key);
            if (shield == null || shield.Capacity <= 0) return;
            int amount = Mathf.RoundToInt(shield.Capacity * LevelValue(fraction, fractionPerLevel, hook.Level));
            if (amount > 0)
                hook.Runtime.Damage.DamageEnemy(amount, ElementType.None, hook.SourceId, cast.CastId, CombatEventKind.PassiveDamage,
                    applyAffinity: true, secondary: true, label: hook.Label);
        }
    }

    /// <summary>Aftershock: Earth abilities hit again for a share of their damage at the start of the next enemy turn.</summary>
    [Serializable]
    public sealed class AftershockPassive : PassiveEffect
    {
        [SerializeField, Min(0f)] private float fraction = 0.3f;
        [SerializeField, Min(0f)] private float fractionPerLevel = 0.1f;

        public AftershockPassive() { }
        public AftershockPassive(float fraction, float perLevel) { this.fraction = fraction; fractionPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"Earth abilities hit again for {Mathf.RoundToInt(LevelValue(fraction, fractionPerLevel, level) * 100f)}% at the next enemy turn start";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => ElementPassiveUtility.Uses(equipped, ElementType.Earth);

        public override void OnCastResolved(PassiveHook hook, CastSnapshot cast)
        {
            if (!ElementPassiveUtility.CastUses(cast, ElementType.Earth) || cast.DamageDealt <= 0) return;
            hook.Add("pending", Mathf.RoundToInt(cast.DamageDealt * LevelValue(fraction, fractionPerLevel, hook.Level)));
        }

        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        {
            if (boundary != TurnBoundary.EnemyTurnStart) return;
            int pending = hook.Get("pending");
            hook.Set("pending", 0);
            if (pending > 0 && !hook.Runtime.CombatOver)
                hook.Runtime.Damage.DamageEnemy(pending, ElementType.Earth, hook.SourceId, $"aftershock-{hook.Runtime.EnemyTurn}",
                    CombatEventKind.PassiveDamage, applyAffinity: true, secondary: false, label: hook.Label);
        }
    }

    /// <summary>Tailwind: after a Wind ability, your next ability costs less mana.</summary>
    [Serializable]
    public sealed class TailwindPassive : PassiveEffect
    {
        [SerializeField, Range(0f, 1f)] private float discount = 0.3f;
        [SerializeField, Range(0f, 1f)] private float discountPerLevel = 0.1f;

        public TailwindPassive() { }
        public TailwindPassive(float discount, float perLevel) { this.discount = discount; discountPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"After a Wind ability, your next ability costs {Mathf.RoundToInt(LevelValue(discount, discountPerLevel, level) * 100f)}% less MP";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => ElementPassiveUtility.Uses(equipped, ElementType.Wind);

        public override void OnCastResolved(PassiveHook hook, CastSnapshot cast)
        {
            if (ElementPassiveUtility.CastUses(cast, ElementType.Wind))
                hook.Runtime.GrantNextCostDiscount(LevelValue(discount, discountPerLevel, hook.Level), hook.Label);
        }
    }

    /// <summary>Catalyst: reactions are stronger, and the first one each battle leaves its marks on the enemy.</summary>
    [Serializable]
    public sealed class CatalystPassive : PassiveEffect
    {
        [SerializeField, Min(0f)] private float bonus = 0.25f;
        [SerializeField, Min(0f)] private float bonusPerLevel = 0.1f;

        public CatalystPassive() { }
        public CatalystPassive(float bonus, float perLevel) { this.bonus = bonus; bonusPerLevel = perLevel; }

        public override string Describe(int level) =>
            $"Reactions {BuildTagUtility.Percent(LevelValue(bonus, bonusPerLevel, level))}; the first reaction each battle keeps its marks";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.SelectMany(quote => quote.AllElements()).Distinct().Count() >= 2;

        public override void ModifyReaction(PassiveHook hook, ReactionContext context)
        {
            context.Multiplier *= 1f + LevelValue(bonus, bonusPerLevel, hook.Level);
            if (hook.Get("kept") != 0) return;
            hook.Set("kept", 1);
            context.KeepMarks = true;
        }
    }

    /// <summary>Attunement: elemental damage grows with each different element used this battle.</summary>
    [Serializable]
    public sealed class AttunementPassive : PassiveEffect
    {
        private static readonly ElementType[] Elements =
            { ElementType.Fire, ElementType.Water, ElementType.Lightning, ElementType.Earth, ElementType.Wind };

        [SerializeField, Min(0f)] private float perElement = 0.04f;
        [SerializeField, Min(0f)] private float perElementPerLevel = 0.02f;
        [SerializeField, Min(1)] private int maxElements = 5;

        public AttunementPassive() { }
        public AttunementPassive(float perElement, float perLevel, int maxElements)
        {
            this.perElement = perElement;
            perElementPerLevel = perLevel;
            this.maxElements = maxElements;
        }

        public override string Describe(int level) =>
            $"{BuildTagUtility.Percent(LevelValue(perElement, perElementPerLevel, level))} elemental damage per different element used this battle (max {maxElements})";

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(quote => quote.AllElements().Any());

        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            int used = hook.Get("elements");
            if (cast.Quote != null) used |= Mask(cast.Quote.AllElements());
            hook.Set("elements", used);
            int count = Mathf.Min(maxElements, Elements.Count(element => (used & (1 << (int)element)) != 0));
            if (count <= 0) return;
            float percent = count * LevelValue(perElement, perElementPerLevel, hook.Level);
            // Only the elements this cast has components of (one log line each).
            foreach (ElementType element in cast.Quote != null ? cast.Quote.AllElements() : Enumerable.Empty<ElementType>())
                cast.AddModifier(ModifierGroup.Passive, EffectKind.Damage, percent, hook.SourceId, hook.Label,
                    EffectFilter.ForElement(element), hook.StackKey, hook.StackCap);
        }

        private static int Mask(IEnumerable<ElementType> elements) => elements.Aggregate(0, (mask, element) => mask | (1 << (int)element));
    }

    /// <summary>Purist: every equipped elemental ability shares one element: its damage is higher and its marks last longer.</summary>
    [Serializable]
    public sealed class PuristPassive : PassiveEffect
    {
        [SerializeField, Min(0f)] private float bonus = 0.15f;
        [SerializeField, Min(0f)] private float bonusPerLevel = 0.05f;
        [SerializeField, Min(0)] private int extraMarkTurns = 1;

        public PuristPassive() { }
        public PuristPassive(float bonus, float perLevel, int extraTurns) { this.bonus = bonus; bonusPerLevel = perLevel; extraMarkTurns = extraTurns; }

        public override string Describe(int level) =>
            $"If every elemental ability you equip shares one element: its damage {BuildTagUtility.Percent(LevelValue(bonus, bonusPerLevel, level))}, its marks last +{extraMarkTurns} turn";

        public static ElementType SharedElement(IEnumerable<AbilityQuote> equipped)
        {
            List<ElementType> elements = equipped.SelectMany(quote => quote.AllElements()).Distinct().ToList();
            return elements.Count == 1 ? elements[0] : ElementType.None;
        }

        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => SharedElement(equipped) != ElementType.None;

        private static ElementType Current(PassiveHook hook) =>
            hook.Runtime?.Build != null ? SharedElement(hook.Runtime.Build.EquippedQuotes()) : ElementType.None;

        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            ElementType element = Current(hook);
            if (element == ElementType.None) return;
            cast.AddModifier(ModifierGroup.Passive, EffectKind.Damage, LevelValue(bonus, bonusPerLevel, hook.Level), hook.SourceId, hook.Label,
                EffectFilter.ForElement(element), hook.StackKey, hook.StackCap);
        }

        public override void ModifyStatus(PassiveHook hook, StatusSpec spec, ref int damagePerTick, ref int ticks)
        {
            ElementType element = Current(hook);
            if (element != ElementType.None && ElementalMarks.IsMark(spec.statusId) && spec.element == element) ticks += extraMarkTurns;
        }
    }

    /// <summary>Unyielding: once per battle, a lethal hit leaves you at 1 HP.</summary>
    [Serializable]
    public sealed class UnyieldingPassive : PassiveEffect
    {
        public override string Describe(int level) => "Once per battle, a lethal hit leaves you at 1 HP";

        public override bool PreventLethal(PassiveHook hook)
        {
            if (hook.Get("used") != 0) return false;
            hook.Set("used", 1);
            return true;
        }
    }
}
