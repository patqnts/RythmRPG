using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Per-encounter totals for the result report: what the build contributed beyond base output.</summary>
    public sealed class BuildCombatStats
    {
        public readonly Dictionary<string, int> Contributions = new();
        public readonly List<string> UnusedBenefits = new();
        public int Casts;
        public int ManaPaid;
        public int ManaRestored;
        public int Reflected;
        public int StatusDamage;
        public int Resisted;
        public int Absorbed;
        public int Prevented;
        public int Overheal;
        public int ShieldGained;
        public int CountersGained;
        public int CountersSpent;
        public int CountersExpired;
        public int Reactions;
        public int NotesCleared;
        public int Staggers;

        public void Contribute(string label, int amount)
        {
            if (string.IsNullOrEmpty(label) || amount == 0) return;
            Contributions.TryGetValue(label, out int existing);
            Contributions[label] = existing + amount;
        }
    }

    /// <summary>
    /// Authoritative damage / healing / mana / shield transactions. Resolves one affinity per damage component, records
    /// attributed events, stops payouts once combat is over and bounds passive-generated (secondary) effects per root.
    /// Every route (abilities, statuses, reflection, passives) goes through here; there is no second damage path.
    /// </summary>
    public sealed class CombatDamageService
    {
        private static readonly Color ReflectColor = new(0.75f, 0.9f, 1f);
        private static readonly Color ManaColor = new(0.45f, 0.7f, 1f);
        private static readonly Color ShieldColor = new(0.8f, 0.85f, 1f);
        private static readonly Color StatusColor = new(1f, 0.6f, 0.3f);
        private static readonly Color ZapColor = new(0.65f, 0.85f, 1f);
        private static readonly Color ReactionColor = new(1f, 0.85f, 0.35f);

        private readonly CombatBuildRuntime runtime;

        public CombatDamageService(CombatBuildRuntime runtime) => this.runtime = runtime;

        private BuildBalanceRules Rules => runtime.Rules;

        /// <summary>The target's affinity for a damage type, clamped by the balance rules.</summary>
        public float Affinity(ElementType element)
        {
            EnemyCombatant enemy = runtime.Enemy;
            float raw = enemy != null ? enemy.AffinityFor(element) : 1f;
            return Mathf.Clamp(raw, Rules.MinAffinity, Rules.MaxAffinity);
        }

        public int DamageEnemy(int amount, ElementType element, string sourceId, string rootId, CombatEventKind kind,
            bool applyAffinity, bool secondary = false, string label = null, CastSnapshot cast = null)
        {
            EnemyCombatant enemy = runtime.Enemy;
            if (amount <= 0 || enemy == null || runtime.CombatOver) return 0;
            if (secondary && !runtime.Events.TrySpendSecondary(rootId, Rules.MaxSecondaryPerRoot)) return 0;

            int final = applyAffinity ? Mathf.Max(0, Mathf.RoundToInt(amount * Affinity(element))) : amount;
            // Cracked: melee hits of a cast land harder.
            if (kind == CombatEventKind.AbilityDamage)
            {
                float cracked = runtime.Marks.CrackedMultiplier(cast);
                if (cracked > 1f) final = Mathf.RoundToInt(final * cracked);
            }
            int actual = enemy.ApplyDamage(final);
            runtime.Record(new CombatEvent
            {
                Kind = kind, SourceId = sourceId, RootCauseId = rootId, CastId = cast?.CastId, Secondary = secondary,
                Element = element, Attempted = amount, Prevented = Mathf.Max(0, amount - final), Actual = actual,
                Roles = cast?.Roles ?? AbilityRole.None, Delivery = cast?.Delivery ?? AbilityDelivery.None,
                SlotIndex = cast?.SlotIndex ?? -1, InputLane = cast?.InputLane ?? -1, Note = label
            });
            BuildCombatStats stats = runtime.Stats;
            if (applyAffinity && final < amount) stats.Resisted += amount - final;
            if (cast != null) cast.DamageDealt += actual;
            switch (kind)
            {
                case CombatEventKind.ReflectDamage:
                    stats.Reflected += actual;
                    stats.Contribute(label ?? sourceId, actual);
                    runtime.ShowAtEnemy("REFLECT " + actual, ReflectColor);
                    break;
                case CombatEventKind.StatusDamage:
                    stats.StatusDamage += actual;
                    stats.Contribute(label ?? sourceId, actual);
                    runtime.ShowAtEnemy(actual.ToString(), StatusColor);
                    break;
                case CombatEventKind.PassiveDamage:
                case CombatEventKind.ZapDamage:
                case CombatEventKind.ReactionDamage:
                    stats.Contribute(label ?? sourceId, actual);
                    if (kind != CombatEventKind.PassiveDamage)
                        runtime.ShowAtEnemy(actual.ToString(), kind == CombatEventKind.ZapDamage ? ZapColor : ReactionColor);
                    break;
            }
            // Elemental hits feed marks and reactions. Status ticks and reaction damage never do (no chains).
            if (kind is CombatEventKind.AbilityDamage or CombatEventKind.PassiveDamage or CombatEventKind.ZapDamage)
                runtime.Marks.OnElementalHit(element, actual, rootId);
            return actual;
        }

        public HealOutcome HealPlayer(int amount, ElementType element, string sourceId, string rootId, CastSnapshot cast = null,
            bool secondary = false)
        {
            PlayerCombatant player = runtime.Player;
            if (amount <= 0 || player == null || runtime.CombatOver)
                return new HealOutcome(0, 0, element, sourceId, rootId, secondary);
            if (secondary && !runtime.Events.TrySpendSecondary(rootId, Rules.MaxSecondaryPerRoot))
                return new HealOutcome(0, 0, element, sourceId, rootId, true);
            int actual = player.Heal(amount);
            var outcome = new HealOutcome(amount, actual, element, sourceId, rootId, secondary);
            runtime.Record(new CombatEvent
            {
                Kind = CombatEventKind.Heal, SourceId = sourceId, RootCauseId = rootId, CastId = cast?.CastId, Secondary = secondary,
                Element = element, Attempted = amount, Actual = actual, Overflow = outcome.Overheal
            });
            runtime.Stats.Overheal += outcome.Overheal;
            if (cast != null)
            {
                cast.Healed += actual;
                cast.Overheal += outcome.Overheal;
            }
            runtime.NotifyHealed(outcome);
            return outcome;
        }

        public int RestoreMana(int amount, string sourceId, string rootId, bool secondary, string label = null)
        {
            PlayerCombatant player = runtime.Player;
            if (amount <= 0 || player == null || runtime.CombatOver) return 0;
            if (secondary && !runtime.Events.TrySpendSecondary(rootId, Rules.MaxSecondaryPerRoot)) return 0;
            int gained = player.GainMana(amount);
            runtime.Record(new CombatEvent
            {
                Kind = CombatEventKind.ManaRestored, SourceId = sourceId, RootCauseId = rootId, Secondary = secondary,
                Attempted = amount, Actual = gained, Overflow = amount - gained, Note = label
            });
            runtime.Stats.ManaRestored += gained;
            if (secondary) runtime.Stats.Contribute((label ?? sourceId) + " (MP)", gained);
            if (gained > 0) runtime.ShowAtPlayer("+" + gained + " MP", ManaColor);
            return gained;
        }

        /// <summary>Adds shield capacity (all sources share one capped shield). Returns the capacity actually added.</summary>
        public int AddShield(int amount, int enemyTurns, string sourceId, string rootId, bool secondary, string label = null, Sprite icon = null,
            float healOnBreak = 0f)
        {
            PlayerCombatant player = runtime.Player;
            CombatModifierSystem modifiers = runtime.Modifiers;
            if (amount <= 0 || player == null || modifiers == null || runtime.CombatOver) return 0;
            if (secondary && !runtime.Events.TrySpendSecondary(rootId, Rules.MaxSecondaryPerRoot)) return 0;
            int cap = Mathf.RoundToInt(player.MaxHealth * Rules.ShieldCapFraction);
            ShieldBuff shield = modifiers.Find<ShieldBuff>(ShieldBuff.Key);
            int added;
            if (shield == null)
            {
                shield = new ShieldBuff(0, enemyTurns);
                added = shield.Add(amount, enemyTurns, cap);
                if (added > 0) modifiers.Add(shield);
            }
            else
            {
                added = shield.Add(amount, enemyTurns, cap);
                modifiers.NotifyChanged();
            }
            if (icon != null) shield.Icon = icon;
            if (healOnBreak > 0f && added > 0) shield.BreakHeal += Mathf.RoundToInt(added * healOnBreak);
            runtime.Record(new CombatEvent
            {
                Kind = CombatEventKind.ShieldGained, SourceId = sourceId, RootCauseId = rootId, Secondary = secondary,
                Attempted = amount, Actual = added, Overflow = amount - added, Note = label
            });
            runtime.Stats.ShieldGained += added;
            if (secondary) runtime.Stats.Contribute((label ?? sourceId) + " (shield)", added);
            if (added > 0) runtime.ShowAtPlayer("SHIELD +" + added, ShieldColor);
            return added;
        }

        /// <summary>
        /// Enemy note damage transaction: passive mitigation, damage-reduction buffs, then shields, then health.
        /// Raw judgements are untouched; only the damage outcome changes. Returns the actual HP lost.
        /// </summary>
        public int ApplyIncomingNoteDamage(DefenseNoteOutcome outcome)
        {
            PlayerCombatant player = runtime.Player;
            int amount = outcome.Attempted;
            if (amount <= 0 || player == null) return 0;
            if (!runtime.CombatOver)
            {
                amount = runtime.ModifyIncomingDamage(outcome.Result, amount);
                foreach (DamageReductionBuff reduction in runtime.Modifiers.OfType<DamageReductionBuff>())
                    amount = Mathf.RoundToInt(amount * (1f - reduction.Strength));
                // Soaked enemy, staggered single-step attack.
                amount = runtime.Marks.ApplySoaked(amount);
                if (runtime.EnemyDamageScale < 1f) amount = Mathf.RoundToInt(amount * runtime.EnemyDamageScale);
                amount = Mathf.Max(0, amount);
                outcome.Prevented = outcome.Attempted - amount;
                ShieldBuff shield = runtime.Modifiers.Find<ShieldBuff>(ShieldBuff.Key);
                if (shield != null && amount > 0)
                {
                    outcome.Absorbed = shield.Absorb(amount);
                    amount -= outcome.Absorbed;
                    runtime.Modifiers.NotifyChanged();
                    // Tidal Veil: a broken shield heals.
                    if (shield.Capacity <= 0 && shield.BreakHeal > 0)
                    {
                        int heal = shield.BreakHeal;
                        shield.BreakHeal = 0;
                        HealPlayer(heal, ElementType.Water, "shield-break", outcome.RootCauseId);
                    }
                }
                // Once-per-battle survival (Unyielding).
                if (amount > 0 && amount >= player.CurrentHealth && player.CurrentHealth > 0 && runtime.PreventLethal())
                {
                    outcome.Prevented += amount - (player.CurrentHealth - 1);
                    amount = player.CurrentHealth - 1;
                }
            }
            outcome.Actual = player.ApplyDamage(amount);
            runtime.Stats.Prevented += outcome.Prevented;
            runtime.Stats.Absorbed += outcome.Absorbed;
            runtime.Record(new CombatEvent
            {
                Kind = CombatEventKind.DefenseDamage, SourceId = "enemy", RootCauseId = outcome.RootCauseId,
                RawJudgement = outcome.Result.Judgement, ResolutionSource = outcome.Result.Source, ChartLane = outcome.Result.LaneId,
                Attempted = outcome.Attempted, Prevented = outcome.Prevented + outcome.Absorbed, Actual = outcome.Actual,
                Note = outcome.Absorbed > 0 ? $"shield absorbed {outcome.Absorbed}" : null
            });
            return outcome.Actual;
        }
    }
}
