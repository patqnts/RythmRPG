using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Passed to passives when a reaction happens (Catalyst makes it stronger or keeps the marks).</summary>
    public sealed class ReactionContext
    {
        public string Name;
        public ElementType Trigger;
        public string RootId;
        /// <summary>Multiplies the reaction's damage / shield.</summary>
        public float Multiplier = 1f;
        /// <summary>The marks stay on the enemy instead of being used up.</summary>
        public bool KeepMarks;
    }

    /// <summary>
    /// Elemental marks on the enemy and the reactions between them. Marks are statuses (so they tick, count down, show as
    /// icons and respect enemy status responses):
    /// <list type="bullet">
    /// <item><b>Burn</b> (Fire): stacks, deals damage at each enemy turn start.</item>
    /// <item><b>Soaked</b> (Water): enemy notes deal less damage.</item>
    /// <item><b>Static</b> (Lightning): stacks; a Lightning hit at the stack cap discharges them.</item>
    /// <item><b>Cracked</b> (Earth): the enemy takes more melee damage.</item>
    /// <item><b>Wind</b> has no mark: Wind hits add stacks to Burn / Static and extend every mark.</item>
    /// </list>
    /// Hitting (or marking) an enemy that carries another element's mark reacts: Conduct, Steam, Overload, Wildfire,
    /// Mudlock, Magnetize. Each reaction happens at most once per cast / enemy note (idempotency key).
    /// </summary>
    public sealed class ElementalMarks
    {
        public const string Burn = "burn";
        public const string Soaked = "soaked";
        public const string Static = "static";
        public const string Cracked = "cracked";

        public const string Conduct = "Conduct";
        public const string Steam = "Steam";
        public const string Overload = "Overload";
        public const string Wildfire = "Wildfire";
        public const string Mudlock = "Mudlock";
        public const string Magnetize = "Magnetize";

        private static readonly Color ReactionColor = new(1f, 0.85f, 0.35f);
        private readonly CombatBuildRuntime runtime;
        private readonly Dictionary<string, StatusSpec> specs = new();

        public ElementalMarks(CombatBuildRuntime runtime) => this.runtime = runtime;

        private ElementalRules Rules => runtime.Rules.Elements;

        public static bool IsMark(string statusId) => statusId is Burn or Soaked or Static or Cracked;

        public static ElementType ElementOf(string markId) => markId switch
        {
            Burn => ElementType.Fire,
            Soaked => ElementType.Water,
            Static => ElementType.Lightning,
            Cracked => ElementType.Earth,
            _ => ElementType.None
        };

        public static string MarkOf(ElementType element) => element switch
        {
            ElementType.Fire => Burn,
            ElementType.Water => Soaked,
            ElementType.Lightning => Static,
            ElementType.Earth => Cracked,
            _ => null
        };

        public static string DisplayName(string markId) => markId switch
        {
            Burn => "Burn",
            Soaked => "Soaked",
            Static => "Static",
            Cracked => "Cracked",
            _ => markId
        };

        /// <summary>The status spec of a mark (stack cap and duration from the balance rules).</summary>
        public StatusSpec Spec(string markId)
        {
            ElementalRules rules = Rules;
            if (!specs.TryGetValue(markId, out StatusSpec spec))
            {
                spec = new StatusSpec { statusId = markId, displayName = DisplayName(markId), element = ElementOf(markId) };
                specs[markId] = spec;
            }
            switch (markId)
            {
                case Burn:
                    spec.tickAt = TurnBoundary.EnemyTurnStart;
                    spec.stacking = StackPolicy.CappedAdd;
                    spec.maxStacks = rules.burnMaxStacks;
                    spec.ticks = rules.burnTurns;
                    spec.dealsTickDamage = true;
                    break;
                case Static:
                    spec.tickAt = TurnBoundary.EnemyTurnEnd;
                    spec.stacking = StackPolicy.CappedAdd;
                    spec.maxStacks = rules.staticMaxStacks;
                    spec.ticks = rules.staticTurns;
                    spec.dealsTickDamage = false;
                    break;
                default:
                    spec.tickAt = TurnBoundary.EnemyTurnEnd;
                    spec.stacking = StackPolicy.Refresh;
                    spec.maxStacks = 1;
                    spec.ticks = markId == Soaked ? rules.soakedTurns : rules.crackedTurns;
                    spec.dealsTickDamage = false;
                    break;
            }
            return spec;
        }

        public StatusInstance Get(string markId) =>
            runtime.Modifiers != null ? runtime.Modifiers.Find<StatusInstance>("status:" + markId) : null;

        public bool Has(string markId) => Get(markId) != null;
        public int Stacks(string markId) => Get(markId)?.Stacks ?? 0;
        /// <summary>Stack cap after passives (Pyre Keeper, Capacitor).</summary>
        public int StackCap(string markId) => runtime.StatusStackCap(Spec(markId));

        /// <summary>
        /// Puts a mark on the enemy (or adds stacks / refreshes it). <paramref name="powerPerStack"/> is Burn's damage per
        /// stack per tick, or Static's discharge damage per stack (the stronger value is kept).
        /// </summary>
        public bool Apply(string markId, int stacks, int powerPerStack, string rootId, Sprite icon = null, int turns = 0)
        {
            StatusSpec spec = Spec(markId);
            if (turns > 0)
            {
                // A custom duration for this application only (the shared spec keeps the rules' default).
                spec = new StatusSpec
                {
                    statusId = spec.statusId, displayName = spec.displayName, element = spec.element, tickAt = spec.tickAt,
                    stacking = spec.stacking, maxStacks = spec.maxStacks, ticks = turns, dealsTickDamage = spec.dealsTickDamage
                };
            }
            return runtime.ApplyStatus(spec, Mathf.Max(0, powerPerStack), rootId, icon, Mathf.Max(1, stacks));
        }

        public void Remove(string markId)
        {
            StatusInstance mark = Get(markId);
            if (mark == null) return;
            mark.Expire();
            runtime.Modifiers.Remove(mark);
        }

        // ---------- effects of marks ----------

        /// <summary>Enemy note damage while Soaked.</summary>
        public int ApplySoaked(int amount) =>
            amount > 0 && Has(Soaked) ? Mathf.RoundToInt(amount * (1f - Mathf.Clamp01(Rules.soakedDamageReduction))) : amount;

        /// <summary>Damage multiplier for a hit of this cast while the enemy is Cracked (melee casts only).</summary>
        public float CrackedMultiplier(CastSnapshot cast) =>
            cast != null && (cast.Delivery & AbilityDelivery.Melee) != 0 && Has(Cracked) ? 1f + Rules.crackedMeleeBonus : 1f;

        /// <summary>Conduct: while the enemy is Soaked, Lightning chains further.</summary>
        public bool Conducting => Has(Soaked);

        // ---------- hits and reactions ----------

        /// <summary>A mark was applied: its element may react with the other marks.</summary>
        internal void OnMarkApplied(string markId, string rootId, int hitDamage = 0)
        {
            if (runtime.CombatOver) return;
            React(ElementOf(markId), rootId, hitDamage, markId);
        }

        /// <summary>
        /// An elemental hit landed (ability, passive or zap damage; never status ticks or reaction damage): Wind feeds
        /// the marks, Lightning discharges a full Static, then reactions.
        /// </summary>
        internal void OnElementalHit(ElementType element, int damage, string rootId)
        {
            if (element == ElementType.None || runtime.CombatOver) return;
            string root = Root(rootId);
            if (element == ElementType.Wind) FeedMarks(root);
            if (element == ElementType.Lightning)
            {
                StatusInstance charge = Get(Static);
                if (charge != null && charge.Stacks >= StackCap(Static)) Discharge(1f, "Discharge", root, keep: false);
            }
            React(element, root, damage, null);
        }

        // Wind: +stacks to Burn / Static, +turns to every mark (once per mark per cast).
        private void FeedMarks(string root)
        {
            ElementalRules rules = Rules;
            foreach (string markId in new[] { Burn, Soaked, Static, Cracked })
            {
                StatusInstance mark = Get(markId);
                if (mark == null) continue;
                if ((markId is Burn or Static) && rules.windStacks > 0)
                {
                    mark.MaxStacks = Mathf.Max(mark.MaxStacks, StackCap(markId));
                    mark.AddStacks(rules.windStacks);
                }
                if (rules.windExtendTurns > 0 && runtime.Events.TryClaim($"wind:{markId}:{root}")) mark.Extend(rules.windExtendTurns);
            }
            runtime.Modifiers?.NotifyChanged();
        }

        private void React(ElementType element, string rootId, int hitDamage, string appliedMark)
        {
            string root = Root(rootId);
            switch (element)
            {
                case ElementType.Fire:
                    if (Has(Soaked)) SteamReaction(ElementType.Fire, root, hitDamage);
                    if (Has(Static)) Trigger(Overload, ElementType.Fire, root, ctx => Discharge(Rules.overloadMultiplier * ctx.Multiplier, Overload, root, ctx.KeepMarks));
                    break;
                case ElementType.Water:
                    if (Has(Burn) && appliedMark != Burn) SteamReaction(ElementType.Water, root, hitDamage);
                    if (Has(Cracked)) MudlockReaction(ElementType.Water, root);
                    break;
                case ElementType.Lightning:
                    if (Has(Soaked)) Trigger(Conduct, ElementType.Lightning, root, _ => { }, consumes: false);
                    break;
                case ElementType.Earth:
                    if (Has(Soaked) && appliedMark != Soaked) MudlockReaction(ElementType.Earth, root);
                    if (Has(Static)) Trigger(Magnetize, ElementType.Earth, root, ctx =>
                    {
                        int shield = Mathf.RoundToInt(DischargeAmount(1f) * Rules.magnetizeShieldScale * ctx.Multiplier);
                        if (!ctx.KeepMarks) Remove(Static);
                        if (shield > 0) runtime.Damage.AddShield(shield, 2, "reaction:magnetize", root, secondary: false, label: Magnetize);
                    });
                    break;
                case ElementType.Wind:
                    if (Has(Burn)) Trigger(Wildfire, ElementType.Wind, root, ctx =>
                    {
                        StatusInstance burn = Get(Burn);
                        if (burn == null) return;
                        burn.MaxStacks = Mathf.Max(burn.MaxStacks, StackCap(Burn));
                        burn.SetStacks(Mathf.CeilToInt(burn.Stacks * Rules.wildfireMultiplier * ctx.Multiplier));
                        runtime.Modifiers.NotifyChanged();
                    }, consumes: false);
                    break;
            }
        }

        private void SteamReaction(ElementType trigger, string root, int hitDamage)
        {
            Trigger(Steam, trigger, root, ctx =>
            {
                StatusInstance burn = Get(Burn);
                int fromBurn = burn != null ? Mathf.RoundToInt(burn.DamagePerTick * burn.Stacks * Rules.steamMultiplier) : 0;
                int minimum = Mathf.RoundToInt(Mathf.Max(0, hitDamage) * Rules.steamMinimumOfHit);
                int burst = Mathf.RoundToInt(Mathf.Max(fromBurn, minimum) * ctx.Multiplier);
                if (!ctx.KeepMarks)
                {
                    Remove(Soaked);
                    Remove(Burn);
                }
                DealReactionDamage(burst, ElementType.None, Steam, root, applyAffinity: false);
            });
        }

        private void MudlockReaction(ElementType trigger, string root)
        {
            Trigger(Mudlock, trigger, root, ctx =>
            {
                if (!ctx.KeepMarks)
                {
                    Remove(Soaked);
                    Remove(Cracked);
                }
                runtime.TryStagger("reaction:mudlock", root, Mudlock);
            });
        }

        private int DischargeAmount(float multiplier)
        {
            StatusInstance charge = Get(Static);
            return charge == null ? 0 : Mathf.RoundToInt(charge.DamagePerTick * charge.Stacks * multiplier);
        }

        private void Discharge(float multiplier, string label, string root, bool keep)
        {
            int amount = DischargeAmount(multiplier);
            if (!keep) Remove(Static);
            DealReactionDamage(amount, ElementType.Lightning, label, root, applyAffinity: true);
        }

        private void DealReactionDamage(int amount, ElementType element, string label, string root, bool applyAffinity)
        {
            if (amount <= 0) return;
            runtime.Damage.DamageEnemy(amount, element, "reaction:" + label.ToLowerInvariant(), root, CombatEventKind.ReactionDamage,
                applyAffinity, secondary: false, label: label);
        }

        /// <summary>Runs a reaction once per root: passives adjust it, it is recorded and announced.</summary>
        private void Trigger(string name, ElementType trigger, string root, System.Action<ReactionContext> apply, bool consumes = true)
        {
            if (!runtime.Events.TryClaim($"react:{name}:{root}")) return;
            var context = new ReactionContext { Name = name, Trigger = trigger, RootId = root };
            if (consumes) runtime.ModifyReaction(context);
            runtime.Record(new CombatEvent
            {
                Kind = CombatEventKind.Reaction, SourceId = "reaction:" + name.ToLowerInvariant(), RootCauseId = root, Element = trigger,
                Note = name + (context.KeepMarks ? " (marks kept)" : string.Empty)
            });
            runtime.Stats.Reactions++;
            runtime.ShowAtEnemy(name.ToUpperInvariant() + "!", ReactionColor);
            apply(context);
            runtime.Modifiers?.NotifyChanged();
        }

        private string Root(string rootId) => string.IsNullOrEmpty(rootId) ? $"e{runtime.EncounterId}-t{runtime.PlayerTurn}-{runtime.EnemyTurn}" : rootId;
    }
}
