using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>Inspector for <see cref="GodRays"/>: one-click presets, rebuild, and the create menu.</summary>
[CustomEditor(typeof(GodRays))]
[CanEditMultipleObjects]
public sealed class GodRaysEditor : Editor
{
    private static GodRays.Preset preset = GodRays.Preset.ForestCanopy;

    public override void OnInspectorGUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            preset = (GodRays.Preset)EditorGUILayout.EnumPopup(preset);
            if (GUILayout.Button("Apply", GUILayout.Width(70)))
            {
                foreach (Object o in targets)
                {
                    var rays = (GodRays)o;
                    Undo.RecordObject(rays, "Apply God Rays Preset");
                    rays.ApplyPreset(preset);
                    EditorUtility.SetDirty(rays);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("The yellow box is the opening the light comes through; move, rotate (Y) and resize it " +
                                    "to fit a window, a gap in the trees or a crack in a cave roof.", MessageType.None);
        }

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
            foreach (Object o in targets) ((GodRays)o).MarkDirty();

        if (GUILayout.Button("Rebuild"))
            foreach (Object o in targets) ((GodRays)o).MarkDirty();
    }

    [MenuItem("GameObject/Rythm RPG/God Rays", false, 31)]
    private static void Create(MenuCommand command)
    {
        var go = new GameObject("God Rays");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        if (command.context == null && SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            go.transform.position = new Vector3(pivot.x, 0f, pivot.z);
        }
        var rays = go.AddComponent<GodRays>();
        rays.ApplyPreset(GodRays.Preset.ForestCanopy);
        Undo.RegisterCreatedObjectUndo(go, "Create God Rays");
        Selection.activeGameObject = go;
    }
}
