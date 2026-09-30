using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// A dedicated improvement attached to one ability instance. It follows that instance between slots and is never
    /// transferred to a different ability.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Ability Upgrade", fileName = "AbilityUpgrade")]
    public sealed class AbilityUpgradeDefinition : ScriptableObject
    {
        [SerializeField] private string id = "upgrade";
        [SerializeField] private string displayName = "Upgrade";
        [SerializeField, TextArea(2, 4)] private string description = string.Empty;
        [Tooltip("Ability ids this can attach to. Empty = any ability matching the requirement below.")]
        [SerializeField] private List<string> abilityIds = new();
        [SerializeField] private AbilityRequirement requirement = new();
        [Tooltip("x Base Power (0.25 = +25%).")]
        [SerializeField] private float powerBonus;
        [SerializeField] private int manaCostDelta;
        [SerializeField] private int cooldownDelta;
        [Tooltip("Extra effects the ability gains (e.g. a fire damage component or a status).")]
        [SerializeReference, SubclassSelector] private List<AbilityEffect> addedEffects = new();
        [SerializeField] private List<string> styleTags = new();

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<string> AbilityIds => abilityIds;
        public AbilityRequirement Requirement => requirement ??= new AbilityRequirement();
        public float PowerBonus => powerBonus;
        public int ManaCostDelta => manaCostDelta;
        public int CooldownDelta => cooldownDelta;
        public IReadOnlyList<AbilityEffect> AddedEffects => addedEffects;
        public IReadOnlyList<string> StyleTags => styleTags;

        public bool IsCompatible(AbilityInstance instance)
        {
            if (instance?.Definition == null) return false;
            if (abilityIds.Count > 0 && !abilityIds.Contains(instance.Definition.Id)) return false;
            return Requirement.IsEmpty || Requirement.SatisfiedBy(AbilityQuote.FromDefinition(instance.Definition));
        }

        public string Summary()
        {
            var parts = new List<string>();
            if (!Mathf.Approximately(powerBonus, 0f)) parts.Add(BuildTagUtility.Percent(powerBonus) + " power");
            if (manaCostDelta != 0) parts.Add((manaCostDelta > 0 ? "+" : "") + manaCostDelta + " MP");
            if (cooldownDelta != 0) parts.Add((cooldownDelta > 0 ? "+" : "") + cooldownDelta + " cooldown");
            parts.AddRange(addedEffects.Where(effect => effect != null).Select(effect => "adds " + effect.Describe(null)));
            return parts.Count == 0 ? description : string.Join(", ", parts);
        }

        public sealed class Builder
        {
            private readonly AbilityUpgradeDefinition definition;

            public Builder(string id, string displayName)
            {
                definition = CreateInstance<AbilityUpgradeDefinition>();
                definition.name = displayName;
                definition.id = id;
                definition.displayName = displayName;
                definition.abilityIds = new List<string>();
                definition.addedEffects = new List<AbilityEffect>();
                definition.styleTags = new List<string>();
            }

            public Builder For(params string[] ids) { definition.abilityIds.AddRange(ids); return this; }
            public Builder Requires(AbilityRequirement requirement) { definition.requirement = requirement; return this; }
            public Builder Power(float bonus) { definition.powerBonus = bonus; return this; }
            public Builder Cost(int delta) { definition.manaCostDelta = delta; return this; }
            public Builder CooldownDelta(int delta) { definition.cooldownDelta = delta; return this; }
            public Builder Adds(AbilityEffect effect) { if (effect != null) definition.addedEffects.Add(effect); return this; }
            public Builder Describe(string text) { definition.description = text; return this; }
            public Builder Style(params string[] tags) { definition.styleTags.AddRange(tags); return this; }
            public AbilityUpgradeDefinition Build() => definition;
        }
    }
}
