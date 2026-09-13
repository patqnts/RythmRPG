using System;
using System.Collections.Generic;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    [Serializable]
    public sealed class EnemyAttackStepDefinition
    {
        [SerializeField] private string animationName = string.Empty;
        [SerializeField, Min(0f)] private float anticipationDuration = 0.45f;
        [SerializeField] private RhythmChart rhythmPattern;
        [SerializeField, Min(0f)] private float durationOverride;
        [SerializeField] private AttackStepEndPolicy endPolicy = AttackStepEndPolicy.WaitForResolvedNotes;
        [SerializeField] private List<CombatModifierDefinition> modifiers = new();

        public string AnimationName => animationName;
        public float AnticipationDuration => anticipationDuration;
        public RhythmChart RhythmPattern => rhythmPattern;
        public float DurationOverride => durationOverride;
        public AttackStepEndPolicy EndPolicy => endPolicy;
        public IReadOnlyList<CombatModifierDefinition> Modifiers => modifiers;
    }

    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Enemy Attack Sequence", fileName = "EnemyAttackSequence")]
    public sealed class EnemyAttackSequenceDefinition : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField, Min(0.01f)] private float selectionWeight = 1f;
        [SerializeField] private List<EnemyAttackStepDefinition> steps = new();

        public string Id => id;
        public float SelectionWeight => Mathf.Max(0.01f, selectionWeight);
        public IReadOnlyList<EnemyAttackStepDefinition> Steps => steps;
    }
}
