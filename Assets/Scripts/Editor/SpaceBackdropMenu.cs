using RythmRPG.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Adds the combat space backdrop (<see cref="SpaceCombatBackdrop"/>) to the open scene's CombatController.</summary>
public static class SpaceBackdropMenu
{
    [MenuItem("Tools/Rythm RPG/Combat/Add Space Backdrop To Scene")]
    private static void AddToScene()
    {
        SpaceCombatBackdrop existing = Object.FindFirstObjectByType<SpaceCombatBackdrop>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[Combat] The scene already has a Space Combat Backdrop (selected).", existing);
            return;
        }

        CombatController combat = Object.FindFirstObjectByType<CombatController>(FindObjectsInactive.Include);
        if (combat == null)
        {
            EditorUtility.DisplayDialog("Space Backdrop", "No CombatController in the open scene.", "OK");
            return;
        }

        SpaceCombatBackdrop backdrop = Undo.AddComponent<SpaceCombatBackdrop>(combat.gameObject);
        EditorSceneManager.MarkSceneDirty(combat.gameObject.scene);
        Selection.activeObject = combat.gameObject;
        Debug.Log($"[Combat] Added Space Combat Backdrop to '{combat.name}'. Save the scene to keep it. Tick 'Preview In Edit Mode' " +
                  "to see it without playing.", backdrop);
    }
}
