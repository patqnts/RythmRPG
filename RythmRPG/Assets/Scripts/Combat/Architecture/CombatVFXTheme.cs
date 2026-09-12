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

        public float SelectionHoldDuration => selectionHoldDuration;
        public float TerminalStateDelay => terminalStateDelay;
        public float HitFlashDuration => hitFlashDuration;
        public float HitShakeStrength => hitShakeStrength;
    }
}
