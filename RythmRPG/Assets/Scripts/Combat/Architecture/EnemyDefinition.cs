using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Enemy", fileName = "Enemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = "Enemy";
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private List<EnemyPhaseDefinition> phases = new();
        [Header("Animation")]
        [SerializeField] private string introAnimationName = string.Empty;
        [SerializeField, Min(0f)] private float introAnimationFallbackDuration = 0f;
        [SerializeField] private string battleAnimatorBoolParameter = string.Empty;
        [SerializeField] private string hitAnimationName = string.Empty;
        [SerializeField] private string deathAnimationName = "Death";
        [SerializeField, Min(0f)] private float deathAnimationFallbackDuration = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public IReadOnlyList<EnemyPhaseDefinition> Phases => phases;
        public string IntroAnimationName => introAnimationName;
        public float IntroAnimationFallbackDuration => introAnimationFallbackDuration;
        public string BattleAnimatorBoolParameter => battleAnimatorBoolParameter;
        public string HitAnimationName => hitAnimationName;
        public string DeathAnimationName => deathAnimationName;
        public float DeathAnimationFallbackDuration => deathAnimationFallbackDuration;
    }
}
