#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Builds the template result screen as a real scene object and assigns it to the scene's CombatController, so
    /// its layout can be edited by hand or saved as a prefab (then set it as the Result Style's Screen Prefab).
    /// </summary>
    internal static class CombatResultMenu
    {
        [MenuItem("Tools/Rythm RPG/Combat/Create Result Screen In Scene")]
        private static void CreateResultScreen()
        {
            CombatResultScreen screen = CombatResultScreen.CreateTemplate(CombatResultStyle.LoadOrDefault());
            Undo.RegisterCreatedObjectUndo(screen.gameObject, "Create Result Screen");
            CombatController controller = Object.FindAnyObjectByType<CombatController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                Undo.RecordObject(controller, "Assign Result Screen");
                controller.EditorAssignResultScreen(screen);
                EditorUtility.SetDirty(controller);
            }
            else Debug.LogWarning("No CombatController in the open scenes: the result screen was created but not assigned.");
            Selection.activeGameObject = screen.gameObject;
            Debug.Log("Result screen created. It hides itself when Play starts and opens when a battle ends. " +
                      "Rows fill in at runtime from the Result Style, so the containers look empty here.");
        }
    }
}
#endif
