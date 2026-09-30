using System.Collections.Generic;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Ability", fileName = "Ability")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [SerializeField] private string id = "basic-attack";
        [SerializeField] private string displayName = "Basic Attack";
        [SerializeField] private Sprite icon;
        [SerializeField] private AbilityType abilityType = AbilityType.BasicAttack;
        [SerializeField] private ElementType element = ElementType.None;
        [SerializeField, Min(0)] private int manaCost;
        [SerializeField, Min(0)] private int cooldown;
        [SerializeField, Min(0)] private int basePower = 100;
        [SerializeField] private RhythmChart rhythmPattern;
        [SerializeField] private AbilityOutcomeProfile outcomeProfile;
        [SerializeField] private AbilityVFXProfile vfxProfile;
        [Tooltip("How the character performs the attack after the rhythm part: move to the middle of the screen, animation / projectile / effects, move back. Empty = the old projectile impact from the VFX profile.")]
        [SerializeField] private CharacterAttackSequence attackSequence;
        [Tooltip("What the ability does (damage, heal, ward, mana...). Amount effects are divided across the attack sequence's hits. Empty = by Ability Type: attacks deal Base Power damage, Healing heals Base Power, Defensive does nothing.")]
        [SerializeReference, SubclassSelector] private List<AbilityEffect> effects = new();

        [Header("Build tags (independent of each other)")]
        [Tooltip("What the ability is for. Nothing = derived from Ability Type (attacks = Damage, Healing = Healing, Defensive = Defense).")]
        [SerializeField] private AbilityRole roles = AbilityRole.None;
        [Tooltip("How it is delivered: Melee, Ranged, Spell, Technique. Passives such as melee enhancement or spell efficiency filter on this.")]
        [SerializeField] private AbilityDelivery delivery = AbilityDelivery.None;
        [Tooltip("Shown in build previews and reward offers.")]
        [SerializeField, TextArea(2, 4)] private string description = string.Empty;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public AbilityType AbilityType => abilityType;
        public ElementType Element => element;
        public int ManaCost => manaCost;
        public int Cooldown => cooldown;
        public int BasePower => basePower;
        public RhythmChart RhythmPattern => rhythmPattern;
        public AbilityOutcomeProfile OutcomeProfile => outcomeProfile;
        public AbilityVFXProfile VFXProfile => vfxProfile;
        public CharacterAttackSequence AttackSequence => attackSequence;
        public IReadOnlyList<AbilityEffect> Effects => effects != null && effects.Count > 0
            ? effects
            : AbilityResolution.DefaultEffects(abilityType);

        /// <summary>Authored roles, or the roles implied by the legacy <see cref="AbilityType"/>.</summary>
        public AbilityRole Roles => roles != AbilityRole.None ? roles : BuildTagUtility.RolesFromType(abilityType);
        public AbilityDelivery Delivery => delivery;
        public string Description => description ?? string.Empty;

        /// <summary>
        /// Builds a definition in code (sample builds, tests). Presentation (icon, chart, VFX, attack sequence) is copied
        /// from <paramref name="presentation"/> so new abilities can reuse existing animation and charts. Treat the result
        /// as immutable once <see cref="Build"/> returns it.
        /// </summary>
        public sealed class Builder
        {
            private readonly AbilityDefinition definition;

            public Builder(string id, string displayName, AbilityDefinition presentation = null)
            {
                definition = CreateInstance<AbilityDefinition>();
                definition.name = displayName;
                definition.id = id;
                definition.displayName = displayName;
                definition.effects = new List<AbilityEffect>();
                if (presentation == null) return;
                definition.icon = presentation.icon;
                definition.rhythmPattern = presentation.rhythmPattern;
                definition.outcomeProfile = presentation.outcomeProfile;
                definition.vfxProfile = presentation.vfxProfile;
                definition.attackSequence = presentation.attackSequence;
                definition.abilityType = presentation.abilityType;
            }

            public Builder Type(AbilityType type) { definition.abilityType = type; return this; }
            public Builder Tags(AbilityRole roles, AbilityDelivery delivery) { definition.roles = roles; definition.delivery = delivery; return this; }
            public Builder Element(ElementType element) { definition.element = element; return this; }
            public Builder Cost(int mana, int cooldownTurns = 0) { definition.manaCost = Mathf.Max(0, mana); definition.cooldown = Mathf.Max(0, cooldownTurns); return this; }
            public Builder Power(int basePower) { definition.basePower = Mathf.Max(0, basePower); return this; }
            public Builder Describe(string text) { definition.description = text; return this; }
            public Builder Chart(RhythmChart chart) { if (chart != null) definition.rhythmPattern = chart; return this; }
            public Builder Sequence(CharacterAttackSequence sequence) { if (sequence != null) definition.attackSequence = sequence; return this; }
            public Builder Effect(AbilityEffect effect) { if (effect != null) definition.effects.Add(effect); return this; }
            public AbilityDefinition Build() => definition;
        }
    }
}
