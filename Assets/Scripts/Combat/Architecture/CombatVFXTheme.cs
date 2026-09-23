using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/VFX Theme", fileName = "CombatVFXTheme")]
    public sealed class CombatVFXTheme : ScriptableObject
    {
        [SerializeField, Min(0.05f)] private float selectionHoldDuration = 0.75f;
        [SerializeField, Min(0f)] private float terminalStateDelay = 1f;
        [SerializeField, Min(0f)] private float hitFlashDuration = 0.12f;
        [SerializeField, Min(0f)] private float hitShakeStrength = 0.12f;

        [Header("Ability cast defaults (used when an ability's VFX profile leaves a slot empty)")]
        [SerializeField] private GameObject defaultChargePrefab;
        [SerializeField] private GameObject defaultChargeReleasePrefab;
        [SerializeField] private GameObject defaultWispPrefab;
        [SerializeField] private GameObject defaultPopPrefab;
        [SerializeField] private GameObject defaultNoteSparkPrefab;

        [Header("Centre stage (where the wisp pops and the player's pattern comes from)")]
        [Tooltip("0 = at the player, 1 = at the enemy.")]
        [SerializeField, Range(0f, 1f)] private float centerStageBias = 0.5f;
        [Tooltip("Height above the note lanes' plane, in world units.")]
        [SerializeField] private float centerStageHeight = 0f;

        public float SelectionHoldDuration => selectionHoldDuration;
        public float TerminalStateDelay => terminalStateDelay;
        public float HitFlashDuration => hitFlashDuration;
        public float HitShakeStrength => hitShakeStrength;
        public GameObject DefaultChargePrefab => defaultChargePrefab;
        public GameObject DefaultChargeReleasePrefab => defaultChargeReleasePrefab;
        public GameObject DefaultWispPrefab => defaultWispPrefab;
        public GameObject DefaultPopPrefab => defaultPopPrefab;
        public GameObject DefaultNoteSparkPrefab => defaultNoteSparkPrefab;
        public float CenterStageBias => centerStageBias;
        public float CenterStageHeight => centerStageHeight;
    }
}
