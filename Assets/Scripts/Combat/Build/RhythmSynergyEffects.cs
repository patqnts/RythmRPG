using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    [Serializable]
    public sealed class ShieldSpendDamageEffect : AbilityEffect
    {
        public override bool IsAmount => true;
        public override int TotalAmount(AbilityEffectContext context) => Mathf.RoundToInt(
            (context.Cast?.ShieldSpent ?? 0) * Mathf.Clamp01(context.Performance.AverageWeight)
            * context.Multiplier(EffectKind.Damage, context.AbilityElement)
            * (context.Build?.Damage.Affinity(context.AbilityElement) ?? 1f));
        public override void ApplyAmount(AbilityEffectContext context, int amount) =>
            context.Build?.Damage.DamageEnemy(amount, context.AbilityElement, "shield-spend", context.Cast?.CastId,
                CombatEventKind.AbilityDamage, false, label: "Shield-spending damage", cast: context.Cast, allowReactions: false);
        public override string Describe(AbilityDefinition ability) => "Spent shield adds damage without extra reactions";
    }
    [Serializable]
    public sealed class BackbeatEffect : AbilityEffect
    {
        public override EffectTiming Timing => EffectTiming.LastHit;
        public override void ApplyOnce(AbilityEffectContext context)
        {
            PhraseOutcome closing = context.Cast?.ClosingPhrase;
            if (context.Build == null || !context.Cast.ExecutionEligible || closing == null
                || !closing.Successful(context.Build.Rules.Synergy.phraseSuccess)) return;
            context.Build.Counters.Gain(closing.Accuracy >= .85f ? 2 : 1, context.Cast.AbilityInstanceId, context.Cast.CastId);
        }
        public override string Describe(AbilityDefinition ability) => "Closing phrase: 1 counter at 70%+, 2 at 85%+";
    }

    [Serializable]
    public sealed class BreakwaterEffect : AbilityEffect
    {
        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Cast?.Choice?.SpendShield == true) return;
            new GainShieldEffect(.7f, 2).ApplyOnce(context);
        }
        public override string Describe(AbilityDefinition ability) => "Guard: shield x0.7; Break: spend one third of shield (max 30) for extra Water damage";
    }

    [Serializable]
    public sealed class CauterizeEffect : AbilityEffect, IElementalEffect
    {
        public bool DealsDamage => false;
        public ElementType EffectElement(ElementType abilityElement) => ElementType.Fire;
        public override bool IsAmount => true;
        public override int TotalAmount(AbilityEffectContext context)
        {
            int extra = context.Cast?.BurnConsumed == true ? 30 : 0;
            return Mathf.RoundToInt((context.ScaledPower(1f) + extra * context.Performance.AverageWeight)
                * context.Multiplier(EffectKind.Healing, ElementType.Fire));
        }
        public override int FreezeAmount(AbilityEffectContext context)
        {
            int total = TotalAmount(context);
            context.Cast?.Attribute(EffectKind.Healing, ElementType.Fire, context.ScaledPower(1f), total);
            return total;
        }
        public override void ApplyAmount(AbilityEffectContext context, int amount)
        {
            if (amount <= 0 || context.Player == null) return;
            int healed = context.Build != null
                ? context.Build.Damage.HealPlayer(amount, ElementType.Fire, context.Cast?.AbilityInstanceId, context.Cast?.CastId, context.Cast).Actual
                : context.Player.Heal(amount);
            if (healed > 0) context.ShowText?.Invoke("+" + healed, context.Player.transform.position + Vector3.up * 1.2f, new Color(.45f, 1f, .55f));
        }
        public override string Describe(AbilityDefinition ability) => "Fire healing x1; optionally consume 1 Burn stack for +30 healing before rhythm scaling";
    }

    [Serializable]
    public sealed class RepriseEffect : AbilityEffect
    {
        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null || context.Cast == null || !context.Cast.ExecutionEligible
                || context.Performance.AverageWeight < context.Build.Rules.Synergy.phraseSuccess) return;
            if (context.Build.ExtendPlayerEffect(context.Cast.Choice?.ExtendKey))
                context.Build.Toast("REPRISE +1 TURN", new Color(.8f, .8f, 1f));
        }
        public override string Describe(AbilityDefinition ability) => "At 70%+, extend a chosen player shield, ward or buff by 1 turn; once per application";
    }

    [Serializable]
    public sealed class ImprovisationPassive : PassiveEffect
    {
        public override string Describe(int level) => "Support actions spend up to 2 counters: +10% healing, shield and buff strength per charge";
        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(q =>
            (q.Roles & AbilityRole.Damage) == 0 && q.Effects.Any(e => e is HealEffect or CauterizeEffect or GainShieldEffect or ApplyBuffEffect));
        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            if (cast.HasRole(AbilityRole.Damage) || !cast.ExecutionEligible || cast.PerformanceWeight < .25f
                || !cast.Quote.Effects.Any(e => e is HealEffect or CauterizeEffect or GainShieldEffect or ApplyBuffEffect)) return;
            int spent = hook.Runtime.Counters.Spend(2, cast.CastId, hook.Label);
            if (spent <= 0) return;
            float bonus = spent * .1f;
            cast.SupportStrengthMultiplier = 1f + bonus;
            cast.AddModifier(ModifierGroup.Counter, EffectKind.Healing, bonus, hook.SourceId, hook.Label);
            cast.AddModifier(ModifierGroup.Counter, EffectKind.Shield, bonus, hook.SourceId, hook.Label);
        }
    }

    [Serializable]
    public sealed class SustainedGuardPassive : PassiveEffect
    {
        public override string Describe(int level) => "Complete a Hold, Mash or full Good-or-better Ping-Pong rally: +8 shield, once per cast/enemy turn (shared small-shield limit)";
        public override void OnRhythmChallenge(PassiveHook hook, PatternRunMode mode, string rootId)
        {
            if (!hook.Runtime.Events.TryClaim("sustained-guard:" + rootId)) return;
            hook.Runtime.GrantSmallShield(8, hook, rootId);
        }
    }

    [Serializable]
    public sealed class PressureCastPassive : PassiveEffect
    {
        public override string Describe(int level) => "Optional on any attack: spend 20% of existing shield (max 20) for extra damage; no extra reactions";
        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(q => (q.Roles & AbilityRole.Damage) != 0);
    }

    [Serializable]
    public sealed class StokePassive : PassiveEffect
    {
        public override string Describe(int level) => "First successful defense phrase adds 1 existing Burn and Static stack; no duration refresh or reactions";
        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(q =>
            q.AppliedStatuses().Any(id => id == ElementalMarks.Burn || id == ElementalMarks.Static));
        public override void OnDefensePhrase(PassiveHook hook, PhraseOutcome phrase, string rootId)
        {
            if (!phrase.Successful(hook.Runtime.Rules.Synergy.phraseSuccess)
                || !hook.Runtime.Events.TryClaim($"stoke:e{hook.Runtime.EncounterId}-et{hook.Runtime.EnemyTurn}")) return;
            foreach (string id in new[] { ElementalMarks.Burn, ElementalMarks.Static })
            {
                StatusInstance mark = hook.Runtime.Marks.Get(id);
                if (mark == null) continue;
                int added = mark.AddStacks(1);
                if (added > 0) hook.Runtime.Record(new CombatEvent { Kind = CombatEventKind.MarkApplied, SourceId = hook.SourceId, RootCauseId = rootId, Actual = added, Note = id });
            }
            hook.Runtime.Modifiers.NotifyChanged();
        }
    }

    [Serializable]
    public sealed class ReactionShelterPassive : PassiveEffect
    {
        public override string Describe(int level) => "First elemental reaction per cast/enemy turn: +10 shield (shared small-shield limit)";
        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.SelectMany(q => q.AllElements()).Distinct().Count() >= 2;
        public override void OnReactionResolved(PassiveHook hook, ReactionContext reaction)
        {
            CombatBuildRuntime runtime = hook.Runtime;
            string root = runtime.CurrentCast != null && runtime.CurrentCast.CastId == reaction.RootId
                ? reaction.RootId : $"e{runtime.EncounterId}-et{runtime.EnemyTurn}";
            if (runtime.Events.TryClaim("reaction-shelter:" + root)) runtime.GrantSmallShield(10, hook, reaction.RootId);
        }
    }

    [Serializable]
    public sealed class ReservoirPassive : PassiveEffect
    {
        public override string Describe(int level) => "End-of-defense mana overflow becomes shield (max 10; shared small-shield limit)";
        public override void OnDefenseManaOverflow(PassiveHook hook, int overflow)
        {
            string root = $"e{hook.Runtime.EncounterId}-et{hook.Runtime.EnemyTurn}";
            if (hook.Runtime.Events.TryClaim("reservoir:" + root)) hook.Runtime.GrantSmallShield(Mathf.Min(10, overflow), hook, root);
        }
    }

    [Serializable]
    public sealed class CloseoutPassive : PassiveEffect
    {
        public override string Describe(int level) => "Successful closing phrase restores 20% of paid MP (max 6; shares 12 MP/cast with Conservation)";
        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) => equipped.Any(q => q.ManaCost > 0);
        public override void OnCastResolved(PassiveHook hook, CastSnapshot cast)
        {
            if (!cast.ExecutionEligible || cast.PaidCost <= 0 || cast.ClosingPhrase == null
                || !cast.ClosingPhrase.Successful(hook.Runtime.Rules.Synergy.phraseSuccess)
                || !hook.Runtime.Events.TryClaim("closeout:" + cast.CastId)) return;
            hook.Runtime.RestorePassiveMana(Mathf.Min(6, Mathf.FloorToInt(cast.PaidCost * .2f)), hook, cast.CastId);
        }
    }
}
