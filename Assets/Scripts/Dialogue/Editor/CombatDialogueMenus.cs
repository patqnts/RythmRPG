using RythmRPG.Combat;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Dialogue.EditorTools
{
    /// <summary>Menu items for battle conversations (see CombatDialogueDirector).</summary>
    public static class CombatDialogueMenus
    {
        private const string LibraryPath = "Assets/Resources/" + CombatDialogueLibrary.ResourcePath + ".asset";

        [MenuItem("Tools/Rythm RPG/Dialogue/Create Combat Dialogue Library", priority = 220)]
        public static void CreateLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<CombatDialogueLibrary>(LibraryPath);
            if (library == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

                // A library made with Create > Rythm RPG > Dialogue somewhere else: move it (keeps its sets and links).
                foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(CombatDialogueLibrary)))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string error = AssetDatabase.MoveAsset(path, LibraryPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogError($"[Combat Dialogue] Couldn't move {path} to {LibraryPath}: {error}");
                        continue;
                    }
                    library = AssetDatabase.LoadAssetAtPath<CombatDialogueLibrary>(LibraryPath);
                    Debug.Log($"[Combat Dialogue] Moved {path} to {LibraryPath} so the game can find it.", library);
                    break;
                }
            }
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CombatDialogueLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Combat Dialogue] Created {LibraryPath}. Add Combat Dialogue Sets to it " +
                          "(Create > Rythm RPG > Dialogue > Combat Dialogue Set).", library);
            }
            Selection.activeObject = library;
            EditorGUIUtility.PingObject(library);
        }

        [MenuItem("Tools/Rythm RPG/Dialogue/Add Combat Dialogue To Selected Enemy", priority = 221)]
        public static void AddToSelection()
        {
            int added = 0;
            foreach (GameObject selected in Selection.gameObjects)
            {
                EnemyCombatant enemy = selected.GetComponentInChildren<EnemyCombatant>(true);
                if (enemy == null) enemy = selected.GetComponentInParent<EnemyCombatant>(true);
                if (enemy == null) continue;
                if (enemy.GetComponentInParent<EnemyCombatDialogue>(true) != null) continue;
                Undo.AddComponent<EnemyCombatDialogue>(enemy.gameObject);
                added++;
            }
            if (added == 0)
                EditorUtility.DisplayDialog("Combat Dialogue", "Select an enemy (an object with EnemyCombatant) that " +
                                            "doesn't have Enemy Combat Dialogue yet.", "OK");
        }

        [MenuItem("Tools/Rythm RPG/Dialogue/Add Combat Dialogue To Selected Enemy", true)]
        private static bool CanAddToSelection() => Selection.activeGameObject != null;
    }
}
