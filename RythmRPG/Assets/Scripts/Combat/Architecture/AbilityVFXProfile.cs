using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Ability VFX Profile", fileName = "AbilityVFXProfile")]
    public sealed class AbilityVFXProfile : ScriptableObject
    {
        [SerializeField, Min(0f)] private float iconTravelDuration = 0.35f;
        [SerializeField, Min(0f)] private float burstDuration = 0.2f;
        [SerializeField] private GameObject burstPrefab;
        [Header("Impact")]
        [SerializeField, Min(0f)] private float impactAnticipationDuration = 0.25f;
        [SerializeField] private GameObject impactProjectilePrefab;
        [SerializeField, Min(0.01f)] private float impactProjectileSpeed = 14f;
        [SerializeField, Min(0.01f)] private float impactProjectileScale = 0.55f;
        [SerializeField] private Vector3 impactOriginOffset = Vector3.up * 1.05f;
        [SerializeField] private Vector3 impactTargetOffset = Vector3.up * 0.35f;
        [SerializeField, Min(0f)] private float impactSettleDuration = 0.25f;
        [SerializeField] private Color accentColor = Color.white;

        public float IconTravelDuration => iconTravelDuration;
        public float BurstDuration => burstDuration;
        public GameObject BurstPrefab => burstPrefab;
        public float ImpactAnticipationDuration => impactAnticipationDuration;
        public GameObject ImpactProjectilePrefab => impactProjectilePrefab;
        public float ImpactProjectileSpeed => impactProjectileSpeed;
        public float ImpactProjectileScale => impactProjectileScale;
        public Vector3 ImpactOriginOffset => impactOriginOffset;
        public Vector3 ImpactTargetOffset => impactTargetOffset;
        public float ImpactSettleDuration => impactSettleDuration;
        public Color AccentColor => accentColor;
    }
}
