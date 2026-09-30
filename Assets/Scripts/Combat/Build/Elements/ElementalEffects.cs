using System;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>An effect that carries an element even if it is not a plain damage component (marks, zaps, arcs).</summary>
    public interface IElementalEffect
    {
        ElementType EffectElement(ElementType abilityElement);
        /// <summary>True when it deals damage of that element (arcs, zaps); false for marks only.</summary>
        bool DealsDamage { get; }
    }

    /// <summary>Puts an elemental mark on the enemy: Burn / Static (stacks) or Soaked / Cracked.</summary>
    [Serializable]
    public sealed class ApplyMarkEffect : AbilityEffect, IElementalEffect
    {
        public enum Mark { Burn, Soaked, Static, Cracked }

        [SerializeField] private Mark mark = Mark.Soaked;
        [SerializeField, Min(1)] private int stacks = 1;
        [Tooltip("Burn: damage per stack per tick. Static: discharge damage per stack. x Base Power x performance.")]
        [SerializeField, Min(0f)] private float powerScalePerStack = 0.1f;
        [Tooltip("Enemy turns (0 = the balance rules' default for this mark).")]
        [SerializeField, Min(0)] private int turns;
        [Tooltip("Below this chart performance the mark is not applied.")]
        [SerializeField, Range(0f, 1f)] private float minimumPerformance = 0.25f;

        public ApplyMarkEffect() { }
        public ApplyMarkEffect(Mark mark, int stacks = 1, float powerScalePerStack = 0.1f, int turns = 0)
        {
            this.mark = mark;
            this.stacks = stacks;
            this.powerScalePerStack = powerScalePerStack;
            this.turns = turns;
        }

        public string MarkId => MarkIdOf(mark);
        public bool DealsDamage => false;
        public ElementType EffectElement(ElementType abilityElement) => ElementalMarks.ElementOf(MarkId);

        public static string MarkIdOf(Mark mark) => mark switch
        {
            Mark.Burn => ElementalMarks.Burn,
            Mark.Static => ElementalMarks.Static,
            Mark.Cracked => ElementalMarks.Cracked,
            _ => ElementalMarks.Soaked
        };

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null || context.Performance.AverageWeight < minimumPerformance) return;
            context.Build.Marks.Apply(MarkId, stacks, context.ScaledPower(powerScalePerStack), context.Cast?.CastId,
                context.Ability != null ? context.Ability.Icon : null, turns);
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Apply {ElementalMarks.DisplayName(MarkId)}" + (stacks > 1 ? $" x{stacks}" : string.Empty) + (turns > 0 ? $" ({turns} turns)" : string.Empty);
    }

    /// <summary>Ember Lash: adds mark stacks per Perfect in its chart (Burn by default), up to a limit.</summary>
    [Serializable]
    public sealed class MarkPerPerfectEffect : AbilityEffect, IElementalEffect
    {
        [SerializeField] private ApplyMarkEffect.Mark mark = ApplyMarkEffect.Mark.Burn;
        [SerializeField, Min(1)] private int stacksPerPerfect = 1;
        [SerializeField, Min(1)] private int maxStacks = 3;
        [SerializeField, Min(0f)] private float powerScalePerStack = 0.08f;

        public MarkPerPerfectEffect() { }
        public MarkPerPerfectEffect(ApplyMarkEffect.Mark mark, int perPerfect, int max, float powerScalePerStack)
        {
            this.mark = mark;
            stacksPerPerfect = perPerfect;
            maxStacks = max;
            this.powerScalePerStack = powerScalePerStack;
        }

        public bool DealsDamage => false;
        public ElementType EffectElement(ElementType abilityElement) => ElementalMarks.ElementOf(ApplyMarkEffect.MarkIdOf(mark));

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null || context.Performance.Judgements == null) return;
            int perfects = context.Performance.Judgements.Count(j => j.Judgement == HitJudgement.Perfect);
            int stacks = Mathf.Min(maxStacks, perfects * stacksPerPerfect);
            if (stacks <= 0) return;
            // Perfect-driven: the stack's potency is not reduced by performance.
            int power = Mathf.RoundToInt(context.BasePower * powerScalePerStack * context.UpgradePowerScale);
            context.Build.Marks.Apply(ApplyMarkEffect.MarkIdOf(mark), stacks, power, context.Cast?.CastId,
                context.Ability != null ? context.Ability.Icon : null);
        }

        public override string Describe(AbilityDefinition ability) =>
            $"+{stacksPerPerfect} {ElementalMarks.DisplayName(ApplyMarkEffect.MarkIdOf(mark))} per Perfect (max {maxStacks})";
    }

    /// <summary>Combust: removes Burn and deals its remaining damage at once (x multiplier) as Fire damage.</summary>
    [Serializable]
    public sealed class ConsumeBurnEffect : AbilityEffect, IElementalEffect
    {
        [SerializeField, Min(0f)] private float multiplier = 1.5f;

        public ConsumeBurnEffect() { }
        public ConsumeBurnEffect(float multiplier) => this.multiplier = multiplier;

        public bool DealsDamage => true;
        public ElementType EffectElement(ElementType abilityElement) => ElementType.Fire;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            CombatBuildRuntime runtime = context.Build;
            StatusInstance burn = runtime?.Marks.Get(ElementalMarks.Burn);
            if (burn == null) return;
            int burst = Mathf.RoundToInt(burn.RemainingDamage * multiplier * context.Multiplier(EffectKind.Damage, ElementType.Fire));
            runtime.Marks.Remove(ElementalMarks.Burn);
            runtime.ShowAtEnemy("COMBUST!", new Color(1f, 0.55f, 0.25f));
            runtime.Damage.DamageEnemy(burst, ElementType.Fire, context.Cast?.CastId ?? "cast", context.Cast?.CastId,
                CombatEventKind.AbilityDamage, applyAffinity: true, cast: context.Cast);
        }

        public override string Describe(AbilityDefinition ability) => $"Consume Burn: its remaining damage x{multiplier:0.##} at once";
    }

    /// <summary>Rain Dance: heal over time at the start of each of your turns (snapshotted now).</summary>
    [Serializable]
    public sealed class RegenEffect : AbilityEffect
    {
        [Tooltip("Total healing = Base Power x this x performance (x healing bonuses), spread over the ticks.")]
        [SerializeField, Min(0f)] private float powerScale = 1.2f;
        [SerializeField, Min(1)] private int ticks = 3;
        [SerializeField] private ElementType element = ElementType.Water;

        public RegenEffect() { }
        public RegenEffect(float scale, int ticks, ElementType element) { powerScale = scale; this.ticks = ticks; this.element = element; }

        public ElementType Element => element;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build?.Modifiers == null) return;
            int total = Mathf.RoundToInt(context.ScaledPower(powerScale) * context.Multiplier(EffectKind.Healing, element));
            int perTick = Mathf.Max(1, Mathf.RoundToInt(total / (float)ticks));
            string source = context.Cast?.AbilityInstanceId ?? context.Ability?.Id ?? "regen";
            context.Build.Modifiers.Find<RegenBuff>("regen:" + source)?.Expire();
            context.Build.Modifiers.Add(new RegenBuff(context.Build, source, context.Ability != null ? context.Ability.DisplayName : "Regen",
                perTick, ticks, element, context.Cast?.CastId) { Icon = context.Ability != null ? context.Ability.Icon : null });
            context.Build.Toast($"REGEN {perTick} x{ticks}", new Color(0.45f, 1f, 0.55f));
        }

        public override string Describe(AbilityDefinition ability) => $"Heal x{powerScale:0.##} over {ticks} turns ({element})";
    }

    /// <summary>
    /// Chain Spark: during its own chart, every Perfect fires a Lightning arc at the enemy right away and adds Static.
    /// While the enemy is Soaked (Conduct) each arc jumps twice.
    /// </summary>
    [Serializable]
    public sealed class ChainArcEffect : AbilityEffect, ILiveChartEffect, IElementalEffect
    {
        [Tooltip("Arc damage = Base Power x this (Perfects only, so no performance scaling).")]
        [SerializeField, Min(0f)] private float arcScale = 0.15f;
        [SerializeField, Min(0)] private int staticPerArc = 1;
        [Tooltip("Static discharge damage per stack = Base Power x this.")]
        [SerializeField, Min(0f)] private float staticScalePerStack = 0.1f;

        public ChainArcEffect() { }
        public ChainArcEffect(float arcScale, int staticPerArc, float staticScalePerStack)
        {
            this.arcScale = arcScale;
            this.staticPerArc = staticPerArc;
            this.staticScalePerStack = staticScalePerStack;
        }

        public bool DealsDamage => true;
        public ElementType EffectElement(ElementType abilityElement) => ElementType.Lightning;

        public void OnChartJudgement(CombatBuildRuntime runtime, CastSnapshot cast, RhythmJudgementResult result)
        {
            if (result.Judgement != HitJudgement.Perfect || cast?.Definition == null) return;
            float power = cast.Definition.BasePower * (cast.Quote != null ? cast.Quote.PowerScale : 1f);
            int arc = Mathf.Max(1, Mathf.RoundToInt(power * arcScale));
            Vector3 target = runtime.Enemy != null ? runtime.Enemy.transform.position + Vector3.up : result.WorldPosition;
            runtime.Board?.ShowArc(result.WorldPosition, target, new Color(0.65f, 0.85f, 1f));
            runtime.Damage.DamageEnemy(arc, ElementType.Lightning, cast.CastId, cast.CastId, CombatEventKind.AbilityDamage,
                applyAffinity: true, label: "Arc", cast: cast);
            if (runtime.Marks.Conducting)
            {
                int second = Mathf.RoundToInt(arc * runtime.Rules.Elements.conductArcScale);
                runtime.Damage.DamageEnemy(second, ElementType.Lightning, cast.CastId, cast.CastId, CombatEventKind.AbilityDamage,
                    applyAffinity: true, label: "Arc", cast: cast);
            }
            if (staticPerArc > 0)
                runtime.Marks.Apply(ElementalMarks.Static, staticPerArc, Mathf.RoundToInt(power * staticScalePerStack), cast.CastId,
                    cast.Definition.Icon);
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Each Perfect fires an arc (x{arcScale:0.##}) and adds {staticPerArc} Static";
    }

    /// <summary>Storm Ward: for some enemy turns, each Perfect block zaps the next notes (destroyed + Lightning damage).</summary>
    [Serializable]
    public sealed class StormWardEffect : AbilityEffect, IElementalEffect
    {
        [SerializeField, Min(1)] private int enemyTurns = 2;
        [SerializeField, Min(1)] private int notesPerPerfect = 2;
        [Tooltip("Damage per zapped note = Base Power x this x performance.")]
        [SerializeField, Min(0f)] private float zapScale = 0.1f;
        [SerializeField, Min(1)] private int maxPerTurn = 6;

        public StormWardEffect() { }
        public StormWardEffect(int turns, int notesPerPerfect, float zapScale, int maxPerTurn)
        {
            enemyTurns = turns;
            this.notesPerPerfect = notesPerPerfect;
            this.zapScale = zapScale;
            this.maxPerTurn = maxPerTurn;
        }

        public bool DealsDamage => true;
        public ElementType EffectElement(ElementType abilityElement) => ElementType.Lightning;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build?.Modifiers == null || !DefensiveOk(context)) return;
            string source = context.Cast?.AbilityInstanceId ?? context.Ability?.Id ?? "storm-ward";
            context.Build.Modifiers.Find<StormWardBuff>("storm-ward:" + source)?.Expire();
            context.Build.Modifiers.Add(new StormWardBuff(source, context.Ability != null ? context.Ability.DisplayName : null, enemyTurns,
                notesPerPerfect, Mathf.Max(1, context.ScaledPower(zapScale)), maxPerTurn) { Icon = context.Ability != null ? context.Ability.Icon : null });
            context.Build.Toast("STORM WARD", new Color(0.65f, 0.85f, 1f));
        }

        internal static bool DefensiveOk(AbilityEffectContext context)
        {
            float minimum = context.Rules != null ? context.Rules.DefensiveMinimumPerformance : 0f;
            if (context.Performance.AverageWeight >= minimum) return true;
            context.Build?.Toast("FAILED", new Color(0.7f, 0.7f, 0.7f));
            return false;
        }

        public override string Describe(AbilityDefinition ability) =>
            $"{enemyTurns} enemy turns: each Perfect zaps the next {notesPerPerfect} notes (max {maxPerTurn} per turn)";
    }

    /// <summary>Flame Guard: for some enemy turns, each Perfect block adds Burn stacks to the enemy.</summary>
    [Serializable]
    public sealed class FlameGuardEffect : AbilityEffect, IElementalEffect
    {
        [SerializeField, Min(1)] private int enemyTurns = 2;
        [SerializeField, Min(1)] private int stacksPerPerfect = 1;
        [SerializeField, Min(1)] private int maxStacksPerTurn = 5;
        [Tooltip("Burn damage per stack per tick = Base Power x this x performance.")]
        [SerializeField, Min(0f)] private float burnScalePerStack = 0.08f;

        public FlameGuardEffect() { }
        public FlameGuardEffect(int turns, int stacksPerPerfect, int maxPerTurn, float burnScale)
        {
            enemyTurns = turns;
            this.stacksPerPerfect = stacksPerPerfect;
            maxStacksPerTurn = maxPerTurn;
            burnScalePerStack = burnScale;
        }

        public bool DealsDamage => false;
        public ElementType EffectElement(ElementType abilityElement) => ElementType.Fire;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build?.Modifiers == null || !StormWardEffect.DefensiveOk(context)) return;
            string source = context.Cast?.AbilityInstanceId ?? context.Ability?.Id ?? "flame-guard";
            context.Build.Modifiers.Find<FlameGuardBuff>("flame-guard:" + source)?.Expire();
            context.Build.Modifiers.Add(new FlameGuardBuff(source, context.Ability != null ? context.Ability.DisplayName : null, enemyTurns,
                stacksPerPerfect, Mathf.Max(1, context.ScaledPower(burnScalePerStack)), maxStacksPerTurn)
                { Icon = context.Ability != null ? context.Ability.Icon : null });
            context.Build.Toast("FLAME GUARD", new Color(1f, 0.6f, 0.3f));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"{enemyTurns} enemy turns: each Perfect block adds {stacksPerPerfect} Burn (max {maxStacksPerTurn} per turn)";
    }

    /// <summary>Stone Wall: walls off the busiest lane; the wall absorbs a number of notes there.</summary>
    [Serializable]
    public sealed class StoneWallEffect : AbilityEffect
    {
        [SerializeField, Min(1)] private int charges = 4;
        [Tooltip("Enemy turns the wall stands if it still has charges.")]
        [SerializeField, Min(1)] private int enemyTurns = 2;

        public StoneWallEffect() { }
        public StoneWallEffect(int charges, int enemyTurns) { this.charges = charges; this.enemyTurns = enemyTurns; }

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build?.Modifiers == null || !StormWardEffect.DefensiveOk(context)) return;
            string source = context.Cast?.AbilityInstanceId ?? context.Ability?.Id ?? "stone-wall";
            context.Build.Modifiers.Find<StoneWallBuff>("stone-wall:" + source)?.Expire();
            context.Build.Modifiers.Add(new StoneWallBuff(source, context.Ability != null ? context.Ability.DisplayName : null, charges, enemyTurns)
                { Icon = context.Ability != null ? context.Ability.Icon : null });
            context.Build.Toast($"STONE WALL x{charges}", new Color(0.85f, 0.7f, 0.45f));
        }

        public override string Describe(AbilityDefinition ability) => $"Wall the busiest lane: absorbs {charges} notes ({enemyTurns} enemy turns)";
    }

    /// <summary>Tremor: staggers the enemy's next attack (it loses one step). Bosses have a limit.</summary>
    [Serializable]
    public sealed class StaggerEffect : AbilityEffect
    {
        [SerializeField, Range(0f, 1f)] private float minimumPerformance = 0.5f;

        public StaggerEffect() { }
        public StaggerEffect(float minimumPerformance) => this.minimumPerformance = minimumPerformance;

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Build == null) return;
            if (context.Performance.AverageWeight < minimumPerformance)
            {
                context.Build.Toast("NO STAGGER", new Color(0.7f, 0.7f, 0.7f));
                return;
            }
            context.Build.TryStagger(context.Cast?.AbilityInstanceId ?? "stagger", context.Cast?.CastId,
                context.Ability != null ? context.Ability.DisplayName : "Stagger");
        }

        public override string Describe(AbilityDefinition ability) => "Stagger: the enemy's next attack loses one step";
    }

    /// <summary>Gale Step: the first Miss of the next enemy turn is dodged.</summary>
    [Serializable]
    public sealed class DodgeEffect : AbilityEffect
    {
        [SerializeField, Min(1)] private int missesPerTurn = 1;
        [SerializeField, Min(1)] private int enemyTurns = 1;

        public DodgeEffect() { }
        public DodgeEffect(int missesPerTurn, int enemyTurns) { this.missesPerTurn = missesPerTurn; this.enemyTurns = enemyTurns; }

        public override void ApplyOnce(AbilityEffectContext context)
        {
            if (context.Modifiers == null) return;
            string source = context.Cast?.AbilityInstanceId ?? context.Ability?.Id ?? "dodge";
            context.Modifiers.Find<DodgeBuff>("dodge:" + source)?.Expire();
            context.Modifiers.Add(new DodgeBuff(source, context.Ability != null ? context.Ability.DisplayName : null, missesPerTurn, enemyTurns)
                { Icon = context.Ability != null ? context.Ability.Icon : null });
            context.Build?.Toast("DODGE", new Color(0.7f, 1f, 0.85f));
        }

        public override string Describe(AbilityDefinition ability) =>
            $"Dodge the first {(missesPerTurn > 1 ? missesPerTurn + " Misses" : "Miss")} of the next {(enemyTurns > 1 ? enemyTurns + " enemy turns" : "enemy turn")}";
    }

    /// <summary>
    /// Cyclone: one damage component delivered as many small hits (each one its own hit, so Wind feeds the marks per
    /// hit). The total is frozen like any damage component.
    /// </summary>
    [Serializable]
    public sealed class MultiHitDamageEffect : AbilityEffect
    {
        [SerializeField, Min(0f)] private float powerScale = 1f;
        [SerializeField, Min(1)] private int hits = 6;

        [NonSerialized] private int frozenTotal;
        [NonSerialized] private int hitsDone;
        [NonSerialized] private int amountDone;

        public MultiHitDamageEffect() { }
        public MultiHitDamageEffect(float scale, int hits) { powerScale = scale; this.hits = hits; }

        public int Hits => hits;
        public override bool IsAmount => true;

        public override int TotalAmount(AbilityEffectContext context)
        {
            ElementType element = context.AbilityElement;
            float modified = context.ScaledPower(powerScale) * context.Multiplier(EffectKind.Damage, element);
            float affinity = context.Build != null ? context.Build.Damage.Affinity(element)
                : context.Enemy != null ? context.Enemy.AffinityFor(element) : 1f;
            return Mathf.Max(0, Mathf.RoundToInt(modified * affinity));
        }

        public override int FreezeAmount(AbilityEffectContext context)
        {
            int raw = context.ScaledPower(powerScale);
            frozenTotal = TotalAmount(context);
            hitsDone = 0;
            amountDone = 0;
            if (context.Cast != null)
            {
                int modified = Mathf.RoundToInt(raw * context.Multiplier(EffectKind.Damage, context.AbilityElement));
                context.Cast.DamageBeforeModifiers += raw;
                context.Cast.DamageAfterModifiers += modified;
                context.Cast.DamageAfterAffinity += frozenTotal;
                context.Cast.Attribute(EffectKind.Damage, context.AbilityElement, raw, modified);
            }
            return frozenTotal;
        }

        public override void ApplyAmount(AbilityEffectContext context, int amount)
        {
            if (amount <= 0) return;
            amountDone += amount;
            // How many of the hits this share of the total covers (the last share takes the rest).
            int hitTarget = frozenTotal <= 0 ? hits : Mathf.Clamp(Mathf.RoundToInt(hits * amountDone / (float)frozenTotal), hitsDone + 1, hits);
            int pieces = Mathf.Max(1, hitTarget - hitsDone);
            hitsDone += pieces;
            int left = amount;
            for (int i = 0; i < pieces; i++)
            {
                int part = i == pieces - 1 ? left : Mathf.Max(0, amount / pieces);
                left -= part;
                if (part <= 0) continue;
                if (context.Build != null)
                    context.Build.Damage.DamageEnemy(part, context.AbilityElement, context.Cast?.CastId ?? "cast", context.Cast?.CastId,
                        CombatEventKind.AbilityDamage, applyAffinity: false, cast: context.Cast);
                else context.Enemy?.ApplyDamage(part);
            }
        }

        public override string Describe(AbilityDefinition ability) =>
            $"{BuildTagUtility.ElementName(ability != null ? ability.Element : ElementType.None)} damage x{powerScale:0.##} in {hits} hits";
    }
}
