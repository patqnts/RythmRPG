using UnityEngine;

namespace RythmRPG.Combat
{
    public abstract class CombatModifierDefinition : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField, Min(0)] private int enemyTurnDuration;

        public string Id => id;
        public int EnemyTurnDuration => enemyTurnDuration;
    }
}
