using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>What an ability is for. Several roles can be combined (a strike that also shields = Damage | Defense).</summary>
    [Flags]
    public enum AbilityRole
    {
        None = 0,
        Damage = 1 << 0,
        Healing = 1 << 1,
        Buff = 1 << 2,
        Defense = 1 << 3,
        Resource = 1 << 4
    }

    /// <summary>How an ability is delivered. Independent of role and element (a melee strike can carry fire damage).</summary>
    [Flags]
    public enum AbilityDelivery
    {
        None = 0,
        Melee = 1 << 0,
        Ranged = 1 << 1,
        Spell = 1 << 2,
        Technique = 1 << 3
    }

    /// <summary>The kind of outcome an effect produces; modifiers are scoped to one kind so damage bonuses never touch healing.</summary>
    public enum EffectKind
    {
        Damage,
        Healing,
        Shield,
        Mana
    }

    /// <summary>
    /// Modifier groups. Percentages add inside a group; groups multiply together in this order. Keeping the groups
    /// explicit gives one deterministic calculation order and a cap per group.
    /// </summary>
    public enum ModifierGroup
    {
        /// <summary>Dedicated upgrades on the ability instance.</summary>
        Upgrade = 0,
        /// <summary>Unconditional passives (melee enhancement, element specialisation...).</summary>
        Passive = 1,
        /// <summary>Temporary buffs consumed or active during the cast (Focus, follow-through...).</summary>
        Buff = 2,
        /// <summary>Execution / sequencing conditions (measured execution, versatility, mana surge).</summary>
        Execution = 3,
        /// <summary>Spent counterpower.</summary>
        Counter = 4
    }

    /// <summary>The turn boundary at which a buff or status ticks / counts down.</summary>
    public enum TurnBoundary
    {
        EnemyTurnStart,
        EnemyTurnEnd,
        PlayerTurnStart,
        PlayerTurnEnd
    }

    /// <summary>How a repeated application of the same buff / status / passive combines.</summary>
    public enum StackPolicy
    {
        /// <summary>The new application replaces the old one.</summary>
        Replace,
        /// <summary>Duration refreshes to the longer one; strength keeps the higher value.</summary>
        Refresh,
        /// <summary>Strength adds up to a cap; duration refreshes.</summary>
        CappedAdd
    }

    /// <summary>
    /// Which outcomes a modifier touches. Every condition set here must hold. Empty = everything of the chosen kind.
    /// Element matching uses the component's own element (a damage modifier for Fire never touches a physical hit or
    /// a Fire-attributed heal).
    /// </summary>
    [Serializable]
    public sealed class EffectFilter
    {
        [Tooltip("The action must carry at least one of these delivery tags (None = any).")]
        public AbilityDelivery delivery = AbilityDelivery.None;
        [Tooltip("The action must carry at least one of these roles (None = any).")]
        public AbilityRole role = AbilityRole.None;
        [Tooltip("Only components attributed to Element.")]
        public bool matchElement;
        public ElementType element = ElementType.None;

        public EffectFilter() { }

        public EffectFilter(AbilityDelivery delivery = AbilityDelivery.None, AbilityRole role = AbilityRole.None)
        {
            this.delivery = delivery;
            this.role = role;
        }

        public static EffectFilter ForElement(ElementType element) => new() { matchElement = true, element = element };

        public bool MatchesAction(AbilityDelivery actionDelivery, AbilityRole actionRoles) =>
            (delivery == AbilityDelivery.None || (actionDelivery & delivery) != 0)
            && (role == AbilityRole.None || (actionRoles & role) != 0);

        public bool Matches(AbilityDelivery actionDelivery, AbilityRole actionRoles, ElementType componentElement) =>
            MatchesAction(actionDelivery, actionRoles) && (!matchElement || componentElement == element);

        public string Describe()
        {
            string text = string.Empty;
            if (delivery != AbilityDelivery.None) text += delivery.ToString().Replace(", ", "/") + " ";
            if (role != AbilityRole.None) text += role.ToString().Replace(", ", "/") + " ";
            if (matchElement) text += (element == ElementType.None ? "Physical" : element.ToString()) + " ";
            return text.Trim();
        }
    }

    public static class BuildTagUtility
    {
        /// <summary>Role derived from the legacy ability type (for assets authored before role tags existed).</summary>
        public static AbilityRole RolesFromType(AbilityType type) => type switch
        {
            AbilityType.BasicAttack or AbilityType.SpecialAttack => AbilityRole.Damage,
            AbilityType.Defensive => AbilityRole.Defense,
            AbilityType.Healing => AbilityRole.Healing,
            _ => AbilityRole.None
        };

        /// <summary>The single most defining role (used by variety / sequencing passives).</summary>
        public static AbilityRole PrimaryRole(AbilityRole roles)
        {
            if ((roles & AbilityRole.Damage) != 0) return AbilityRole.Damage;
            if ((roles & AbilityRole.Healing) != 0) return AbilityRole.Healing;
            if ((roles & AbilityRole.Defense) != 0) return AbilityRole.Defense;
            if ((roles & AbilityRole.Buff) != 0) return AbilityRole.Buff;
            if ((roles & AbilityRole.Resource) != 0) return AbilityRole.Resource;
            return AbilityRole.None;
        }

        public static string ElementName(ElementType element) => element == ElementType.None ? "Physical" : element.ToString();

        public static string Percent(float fraction) => (fraction >= 0f ? "+" : "") + Mathf.RoundToInt(fraction * 100f) + "%";
    }
}
