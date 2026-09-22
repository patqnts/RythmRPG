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
    }
}
