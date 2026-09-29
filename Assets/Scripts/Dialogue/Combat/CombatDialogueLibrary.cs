using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Every <see cref="CombatDialogueSet"/> the game uses. It must be at Assets/Resources/CombatDialogueLibrary.asset
    /// (any Resources folder works) or the game can't find it.
    /// Sets for a specific enemy are checked before sets for every enemy.
    /// Create it with Tools > Rythm RPG > Dialogue > Create Combat Dialogue Library (that also moves a library made
    /// somewhere else into Resources).
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Dialogue/Combat Dialogue Library", fileName = "CombatDialogueLibrary", order = 402)]
    public sealed class CombatDialogueLibrary : ScriptableObject
    {
        public const string ResourcePath = "CombatDialogueLibrary";

        public List<CombatDialogueSet> sets = new();

        private static CombatDialogueLibrary cached;
        private static bool searched;
        private static bool warnedMissing;

        public static CombatDialogueLibrary Load()
        {
            if (!searched || cached == null)
            {
                searched = true;
                cached = Resources.Load<CombatDialogueLibrary>(ResourcePath);
                if (cached == null && !warnedMissing)
                {
                    warnedMissing = true;
                    Debug.LogWarning("[Combat Dialogue] No Combat Dialogue Library found in a Resources folder, so its " +
                                     "sets can't play. It must be named CombatDialogueLibrary and sit in Assets/Resources " +
                                     "(Tools > Rythm RPG > Dialogue > Create Combat Dialogue Library moves it there).");
                }
            }
            return cached;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cached = null;
            searched = false;
            warnedMissing = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path)) return;
            bool inResources = path.Replace('\\', '/').Contains("/Resources/");
            bool rightName = System.IO.Path.GetFileNameWithoutExtension(path) == ResourcePath;
            if (inResources && rightName) return;
            Debug.LogWarning($"[Combat Dialogue] '{path}' won't be found by the game: the library must be named " +
                             $"{ResourcePath} and be in a Resources folder (e.g. Assets/Resources/{ResourcePath}.asset). " +
                             "Use Tools > Rythm RPG > Dialogue > Create Combat Dialogue Library to move it there.", this);
        }
#endif
    }
}
