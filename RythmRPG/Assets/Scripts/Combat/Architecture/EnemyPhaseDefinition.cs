using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Enemy Phase", fileName = "EnemyPhase")]
    public sealed class EnemyPhaseDefinition : ScriptableObject
    {
        [SerializeField, Range(0f, 1f)] private float enterAtHealthPercent = 1f;
        [SerializeField] private List<EnemyAttackSequenceDefinition> attackSequences = new();

        public float EnterAtHealthPercent => enterAtHealthPercent;
        public IReadOnlyList<EnemyAttackSequenceDefinition> AttackSequences => attackSequences;
    }
}
