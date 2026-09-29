using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Battle conversations for this particular enemy (a boss, a named character), checked before the Combat Dialogue
    /// Library. Put it on the enemy object (the one with EnemyCombatant) or a parent of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyCombatDialogue : MonoBehaviour
    {
        [Tooltip("Conversations only this enemy uses (checked first, in order).")]
        public List<CombatDialogueEntry> entries = new();
        [Tooltip("Shared sets (checked after the entries above).")]
        public List<CombatDialogueSet> sets = new();
        [Tooltip("Also use the library's sets for this enemy's type and for every enemy.")]
        public bool useLibrary = true;

        private void Awake() => CombatDialogueEntry.FixUnsetDefaults(entries);

        private void OnValidate()
        {
            if (!CombatDialogueEntry.FixUnsetDefaults(entries)) return;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
