using System.Linq;
using RythmRPG.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds or removes the <see cref="ObliqueProjection"/> on the pixel camera (the camera that renders into the pixel
/// render texture, normally "Pixel Main Camera"). Undoable. The world, its meshes and its transforms are not touched.
/// </summary>
public static class ObliqueProjectionSetup
{
    private const string MenuRoot = "Tools/Rythm RPG/Rendering/";

    [MenuItem(MenuRoot + "Add Oblique Projection To Pixel Camera")]
    public static void Add()
    {
        Camera camera = FindPixelCamera();
        if (camera == null)
        {
            EditorUtility.DisplayDialog("Oblique Projection", "No pixel camera found in the open scene (Camera.main or a camera rendering into a render texture).", "OK");
            return;
        }
        if (!camera.orthographic)
        {
            EditorUtility.DisplayDialog("Oblique Projection", "\"" + camera.name + "\" is not orthographic. The oblique projection only works on an orthographic camera.", "OK");
            return;
        }

        ObliqueProjection projection = camera.GetComponent<ObliqueProjection>();
        if (projection == null)
        {
            projection = Undo.AddComponent<ObliqueProjection>(camera.gameObject);
        }
        else if (!projection.enabled)
        {
            Undo.RecordObject(projection, "Enable Oblique Projection");
            projection.enabled = true;
        }
        EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
        Selection.activeObject = projection;
        EditorGUIUtility.PingObject(projection);
        Debug.Log("[Oblique Projection] Added to \"" + camera.name + "\". Ground and walls are now drawn at 1:1. " +
                  "Disable the component to compare with the old look.", projection);
    }

    [MenuItem(MenuRoot + "Remove Oblique Projection")]
    public static void Remove()
    {
        ObliqueProjection[] found = Object.FindObjectsByType<ObliqueProjection>(FindObjectsInactive.Include);
        if (found.Length == 0)
        {
            EditorUtility.DisplayDialog("Oblique Projection", "There is no Oblique Projection in the open scene.", "OK");
            return;
        }
        foreach (ObliqueProjection projection in found)
        {
            Camera camera = projection.GetComponent<Camera>();
            EditorSceneManager.MarkSceneDirty(projection.gameObject.scene);
            Undo.DestroyObjectImmediate(projection);
            if (camera != null) camera.ResetProjectionMatrix();
        }
    }

    private static Camera FindPixelCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.GetComponent<CrispWorldUICamera>() == null) return main;
        return Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
            .FirstOrDefault(camera => camera.targetTexture != null && camera.GetComponent<CrispWorldUICamera>() == null);
    }
}
