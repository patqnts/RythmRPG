using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>A live player effect that reacts after the visible Combo has been updated.</summary>
    public interface IComboJudgementModifier
    {
        void OnComboJudgement(CombatBuildRuntime runtime, ComboJudgementOutcome outcome);
    }

    [Serializable]
    public sealed class FortissimoEffect : AbilityEffect
    {
        [SerializeField, Min(1)] private int comboStep = 15;
        [SerializeField, Min(0f)] private float bonusPerStep = .2f;
        [SerializeField, Min(0f)] private float maximumBonus = .6f;

        public FortissimoEffect() { }
        public FortissimoEffect(int comboStep, float bonusPerStep, float maximumBonus)
        {
            this.comboStep = comboStep;
            this.bonusPerStep = bonusPerStep;
            this.maximumBonus = maximumBonus;
        }

        public override void BeforeFreeze(AbilityEffectContext context)
        {
            if (context.Cast == null) return;
            int steps = context.Cast.ComboAtCommit / Mathf.Max(1, comboStep);
            float bonus = Mathf.Min(maximumBonus, steps * bonusPerStep);
            if (bonus > 0f)
                context.Cast.AddModifier(ModifierGroup.Execution, EffectKind.Damage, bonus,
                    context.Cast.AbilityInstanceId, "Fortissimo", EffectFilter.ForElement(ElementType.None));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Physical damage gains +{Mathf.RoundToInt(bonusPerStep * 100f)}% per {comboStep} Combo already held " +
            $"(max +{Mathf.RoundToInt(maximumBonus * 100f)}%); Combo is not spent";
    }

    public sealed class GraceNoteBuff : TimedBuff
    {
        private bool used;

        public GraceNoteBuff(string sourceId, string label) : base("grace-note:" + sourceId,
            label ?? "Grace Note", 1, TurnBoundary.EnemyTurnEnd) { }

        public bool TryUse()
        {
            if (used || IsExpired) return false;
            used = true;
            return true;
        }

        public override int IconCount => used ? 0 : 1;
        public override string Describe() => used
            ? $"{Label}: Combo save used"
            : $"{Label}: the next Combo break this enemy turn is ignored";
    }

    [Serializable]
    public sealed class GraceNoteEffect : AbilityEffect
    {
        [SerializeField, Range(0f, 1f)] private float minimumPerformance = .7f;

        public GraceNoteEffect() { }
        public GraceNoteEffect(float minimumPerformance) => this.minimumPerformance = minimumPerformance;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build?.Modifiers == null || !context.Cast.ExecutionEligible
                || context.Performance.AverageWeight < minimumPerformance) return;
            string source = context.Cast.AbilityInstanceId;
            context.Build.Modifiers.Find<GraceNoteBuff>("grace-note:" + source)?.Expire();
            context.Build.Modifiers.Add(new GraceNoteBuff(source, context.Ability?.DisplayName) { Icon = context.Ability?.Icon });
            context.Build.Toast("GRACE NOTE READY", new Color(.7f, 1f, .85f));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"At {Mathf.RoundToInt(minimumPerformance * 100f)}%+: the next Combo-breaking judgement during the next enemy turn does not reset Combo";
    }

    public sealed class HeartbeatBuff : TimedBuff, IComboJudgementModifier
    {
        private readonly CombatBuildRuntime runtime;
        private readonly int heal;
        private readonly string sourceId;
        private readonly string rootId;
        private int healedThisTurn;

        public HeartbeatBuff(CombatBuildRuntime runtime, string sourceId, string label, int heal, int enemyTurns, string rootId)
            : base("heartbeat:" + sourceId, label ?? "Heartbeat", enemyTurns, TurnBoundary.EnemyTurnEnd)
        {
            this.runtime = runtime;
            this.sourceId = sourceId;
            this.heal = Mathf.Max(0, heal);
            this.rootId = rootId;
        }

        public void OnComboJudgement(CombatBuildRuntime owner, ComboJudgementOutcome outcome)
        {
            if (!outcome.IsEnemyDefense || !outcome.ReachedMilestone(15) || healedThisTurn >= 2 || heal <= 0) return;
            healedThisTurn++;
            runtime.Damage.HealPlayer(heal, ElementType.Water, sourceId, rootId);
        }

        public override void OnTurnBoundary(TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.EnemyTurnStart) healedThisTurn = 0;
            base.OnTurnBoundary(boundary);
        }

        public override string Describe() =>
            $"{Label}: heal {heal} at every 15 Combo ({healedThisTurn}/2 this turn, {TurnsRemaining} enemy turns)";
    }

    [Serializable]
    public sealed class HeartbeatEffect : AbilityEffect, IElementalEffect
    {
        [SerializeField, Min(0f)] private float healScale = .3f;
        [SerializeField, Min(1)] private int enemyTurns = 2;

        public HeartbeatEffect() { }
        public HeartbeatEffect(float healScale, int enemyTurns) { this.healScale = healScale; this.enemyTurns = enemyTurns; }

        public bool DealsDamage => false;
        public ElementType EffectElement(ElementType abilityElement) => ElementType.Water;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build?.Modifiers == null || !StormWardEffect.DefensiveOk(context)) return;
            int raw = context.ScaledPower(healScale);
            int heal = Mathf.RoundToInt(raw * context.Multiplier(EffectKind.Healing, ElementType.Water));
            context.Cast?.Attribute(EffectKind.Healing, ElementType.Water, raw, heal);
            string source = context.Cast?.AbilityInstanceId ?? context.Ability?.Id ?? "heartbeat";
            context.Build.Modifiers.Find<HeartbeatBuff>("heartbeat:" + source)?.Expire();
            context.Build.Modifiers.Add(new HeartbeatBuff(context.Build, source, context.Ability?.DisplayName, heal,
                enemyTurns, context.Cast?.CastId) { Icon = context.Ability?.Icon });
            context.Build.Toast($"HEARTBEAT +{heal}", new Color(.45f, 1f, .7f));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"For {enemyTurns} enemy turns, heal Water x{healScale:0.##} whenever Combo reaches 15, 30, 45... (twice per turn)";
    }

    [Serializable]
    public sealed class ChallengeStoneWallEffect : AbilityEffect
    {
        [SerializeField, Min(1)] private int charges = 2;
        [SerializeField, Range(0f, 1f)] private float minimumPerformance = .7f;

        public ChallengeStoneWallEffect() { }
        public ChallengeStoneWallEffect(int charges, float minimumPerformance)
        { this.charges = charges; this.minimumPerformance = minimumPerformance; }

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Cast == null || context.Cast.CompletedChallenges.Count == 0
                || context.Performance.AverageWeight < minimumPerformance) return;
            new StoneWallEffect(charges, 1).ApplyOnce(context);
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Complete its Mash at Good or better and finish at {Mathf.RoundToInt(minimumPerformance * 100f)}%+: gain a {charges}-charge Stone Wall for the next enemy turn";
    }

    [Serializable]
    public sealed class CatalyzeEffect : AbilityEffect
    {
        [SerializeField, Range(0f, 1f)] private float minimumPerformance = .7f;
        [SerializeField, Min(0)] private int extraTurns = 1;
        [SerializeField, Min(0f)] private float reactionBonus = .25f;

        public CatalyzeEffect() { }
        public CatalyzeEffect(float minimumPerformance, int extraTurns, float reactionBonus)
        { this.minimumPerformance = minimumPerformance; this.extraTurns = extraTurns; this.reactionBonus = reactionBonus; }

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null || context.Cast == null || !context.Cast.ExecutionEligible
                || context.Performance.AverageWeight < minimumPerformance) return;
            if (context.Build.CatalyzeMark(context.Cast.Choice?.MarkId, extraTurns, reactionBonus,
                    context.Ability?.DisplayName ?? "Catalyze"))
                context.Build.Toast($"CATALYZE +{extraTurns} TURN", new Color(1f, .75f, .35f));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Choose an existing Elemental Mark. At {Mathf.RoundToInt(minimumPerformance * 100f)}%+, extend it {extraTurns} turn and empower the next reaction by {Mathf.RoundToInt(reactionBonus * 100f)}%";
    }

    [Serializable]
    public sealed class TempoGuardPassive : PassiveEffect
    {
        public override string Describe(int level) => "Every 15 Combo during enemy defense gives 6 shield (max 12 per enemy turn; shared small-shield limit)";
        public override void OnComboJudgement(PassiveHook hook, ComboJudgementOutcome outcome)
        {
            if (!outcome.IsEnemyDefense || !outcome.ReachedMilestone(15) || hook.Get("turn") >= 2) return;
            hook.Add("turn", 1);
            hook.Runtime.GrantSmallShield(6, hook, $"combo:e{hook.Runtime.EncounterId}-et{hook.Runtime.EnemyTurn}-{outcome.ComboAfter}");
        }
        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        { if (boundary == TurnBoundary.EnemyTurnStart) hook.Set("turn", 0); }
    }

    [Serializable]
    public sealed class FlowStatePassive : PassiveEffect
    {
        public override string Describe(int level) => "Every 15 Combo during enemy defense restores 3 MP (max 6 per enemy turn; shared passive-mana limit)";
        public override void OnComboJudgement(PassiveHook hook, ComboJudgementOutcome outcome)
        {
            if (!outcome.IsEnemyDefense || !outcome.ReachedMilestone(15) || hook.Get("turn") >= 2) return;
            hook.Add("turn", 1);
            hook.Runtime.RestorePassiveMana(3, hook, $"combo:e{hook.Runtime.EncounterId}-et{hook.Runtime.EnemyTurn}");
        }
        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        { if (boundary == TurnBoundary.EnemyTurnStart) hook.Set("turn", 0); }
    }

    [Serializable]
    public sealed class ComebackBeatPassive : PassiveEffect
    {
        public override string Describe(int level) => "After Combo truly breaks, the next Perfect gives 1 counter (once per enemy turn)";
        public override void OnComboJudgement(PassiveHook hook, ComboJudgementOutcome outcome)
        {
            if (!outcome.IsEnemyDefense || hook.Get("used") != 0) return;
            if (outcome.BrokeCombo) { hook.Set("armed", 1); return; }
            if (hook.Get("armed") == 0 || outcome.Result.Judgement != HitJudgement.Perfect) return;
            hook.Set("armed", 0);
            hook.Set("used", 1);
            hook.Runtime.Counters.Gain(1, hook.SourceId, $"comeback:e{hook.Runtime.EncounterId}-et{hook.Runtime.EnemyTurn}");
        }
        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        { if (boundary == TurnBoundary.EnemyTurnStart) { hook.Set("armed", 0); hook.Set("used", 0); } }
    }

    [Serializable]
    public sealed class SafetyNetPassive : PassiveEffect
    {
        [SerializeField, Min(1)] private int shieldCost = 10;
        public SafetyNetPassive() { }
        public SafetyNetPassive(int shieldCost) => this.shieldCost = shieldCost;
        public override string Describe(int level) => $"The first Combo break each enemy turn spends {shieldCost} shield to preserve Combo; shares its one save with Grace Note";
        public override bool TryPreventComboBreak(PassiveHook hook, RhythmJudgementResult result, int combo)
        {
            ShieldBuff shield = hook.Runtime.Modifiers?.Find<ShieldBuff>(ShieldBuff.Key);
            if (shield == null || shield.Capacity < shieldCost) return false;
            return hook.Runtime.Damage.SpendShield(shieldCost, result.NoteId) == shieldCost;
        }
    }

    [Serializable]
    public sealed class MarkedOpeningPassive : PassiveEffect
    {
        [SerializeField, Min(0f)] private float bonus = .2f;
        public MarkedOpeningPassive() { }
        public MarkedOpeningPassive(float bonus) => this.bonus = bonus;
        public override string Describe(int level) => $"Physical attacks deal +{Mathf.RoundToInt(bonus * 100f)}% when the enemy has any Elemental Mark";
        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.Any(quote => quote.DamageElements(false).Contains(ElementType.None));
        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            if (!cast.Quote.DamageElements(false).Contains(ElementType.None)
                || !new[] { ElementalMarks.Burn, ElementalMarks.Soaked, ElementalMarks.Static, ElementalMarks.Cracked }.Any(hook.Runtime.Marks.Has)) return;
            cast.AddModifier(ModifierGroup.Execution, EffectKind.Damage, bonus, hook.SourceId, hook.Label,
                EffectFilter.ForElement(ElementType.None), hook.StackKey, hook.StackCap);
        }
    }

    [Serializable]
    public sealed class LongMeasurePassive : PassiveEffect
    {
        public override string Describe(int level) => "Complete a Hold, Mash, or full Good-or-better Ping-Pong rally: reduce the longest equipped cooldown by 1, once per turn";
        public override void OnRhythmChallenge(PassiveHook hook, PatternRunMode mode, string rootId)
        {
            if (!hook.Runtime.Events.TryClaim("long-measure:" + rootId)) return;
            hook.Runtime.ReduceLongestCooldown(hook.Label);
        }
    }

    [Serializable]
    public sealed class ElementalRelayPassive : PassiveEffect
    {
        [SerializeField, Min(0f)] private float bonus = .2f;
        [SerializeField, Min(1)] private int playerTurns = 2;
        public ElementalRelayPassive() { }
        public ElementalRelayPassive(float bonus, int playerTurns) { this.bonus = bonus; this.playerTurns = playerTurns; }
        public override string Describe(int level) =>
            $"After a reaction, the next healing, shield, or strength-based buff action gains +{Mathf.RoundToInt(bonus * 100f)}%; expires after {playerTurns} player turns";
        public override bool IsActive(IReadOnlyList<AbilityQuote> equipped) =>
            equipped.SelectMany(quote => quote.AllElements()).Distinct().Count() >= 2;
        public override void OnReactionResolved(PassiveHook hook, ReactionContext reaction)
        {
            hook.Set("ready", 1);
            hook.Set("expires", hook.Runtime.PlayerTurn + playerTurns);
        }
        public override void OnPreOutcome(PassiveHook hook, CastSnapshot cast)
        {
            if (hook.Get("ready") == 0 || hook.Runtime.PlayerTurn > hook.Get("expires")) return;
            bool healing = cast.Quote.HealingElements().Any();
            bool shield = cast.Quote.Effects.Any(effect => effect is GainShieldEffect or BreakwaterEffect);
            bool buff = cast.Quote.Effects.Any(effect => effect is ApplyBuffEffect);
            if (!healing && !shield && !buff) return;
            if (healing) cast.AddModifier(ModifierGroup.Buff, EffectKind.Healing, bonus, hook.SourceId, hook.Label);
            if (shield) cast.AddModifier(ModifierGroup.Buff, EffectKind.Shield, bonus, hook.SourceId, hook.Label);
            if (buff) cast.SupportStrengthMultiplier *= 1f + bonus;
            hook.Set("ready", 0);
        }
        public override void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary)
        {
            if (boundary == TurnBoundary.PlayerTurnStart && hook.Get("ready") != 0
                && hook.Runtime.PlayerTurn > hook.Get("expires")) hook.Set("ready", 0);
        }
    }
}
