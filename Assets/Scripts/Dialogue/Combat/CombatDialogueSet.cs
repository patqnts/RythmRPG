using System.Collections.Generic;
using RythmRPG.Combat;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Battle conversations for one enemy type (or for every enemy when <see cref="enemy"/> is empty). Put it in the
    /// Combat Dialogue Library (Resources/CombatDialogueLibrary) or on an enemy's <see cref="EnemyCombatDialogue"/>.
    /// For each moment the first matching entry plays, so put specific entries above general ones.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Dialogue/Combat Dialogue Set", fileName = "Combat Dialogue", order = 401)]
    public sealed class CombatDialogueSet : ScriptableObject
    {
        [Tooltip("Enemy type these conversations belong to. Empty = every enemy (e.g. tutorial hints).")]
        public EnemyDefinition enemy;
        public List<CombatDialogueEntry> entries = new();

        public bool AppliesTo(EnemyDefinition definition) => enemy == null || enemy == definition;

        private void OnEnable() => CombatDialogueEntry.FixUnsetDefaults(entries);

        private void OnValidate()
        {
            if (!CombatDialogueEntry.FixUnsetDefaults(entries)) return;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
