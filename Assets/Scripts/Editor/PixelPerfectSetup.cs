using System.Linq;
using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>Adds / removes the <see cref="PixelPerfectRig"/> on the pixel camera (the one rendering into a render texture).</summary>
public static class PixelPerfectSetup
{
    private const string AddMenu = "Tools/Rythm RPG/Rendering/Add Pixel Perfect Rig To Pixel Camera";
    private const string RemoveMenu = "Tools/Rythm RPG/Rendering/Remove Pixel Perfect Rig";

    [MenuItem(AddMenu)]
    private static void Add()
    {
        Camera pixelCamera = FindPixelCamera();
        if (pixelCamera == null)
        {
            EditorUtility.DisplayDialog("Pixel Perfect Rig",
                "No pixel camera found: it should be a camera that renders into a Render Texture (shown by a RawImage).", "OK");
            return;
        }
        if (pixelCamera.GetComponent<PixelPerfectRig>() != null)
        {
            Selection.activeObject = pixelCamera.gameObject;
            Debug.Log($"[Pixel Perfect] '{pixelCamera.name}' already has a Pixel Perfect Rig.", pixelCamera);
            return;
        }
        Undo.AddComponent<PixelPerfectRig>(pixelCamera.gameObject);
        Selection.activeObject = pixelCamera.gameObject;
        EditorUtility.SetDirty(pixelCamera.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pixelCamera.gameObject.scene);
        Debug.Log($"[Pixel Perfect] Added a Pixel Perfect Rig to '{pixelCamera.name}'. Save the scene to keep it.", pixelCamera);
    }

    [MenuItem(RemoveMenu)]
    private static void Remove()
    {
        foreach (PixelPerfectRig rig in Object.FindObjectsByType<PixelPerfectRig>(FindObjectsInactive.Include))
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
            Undo.DestroyObjectImmediate(rig);
        }
    }

    [MenuItem(RemoveMenu, true)]
    private static bool CanRemove() => Object.FindObjectsByType<PixelPerfectRig>(FindObjectsInactive.Include).Length > 0;

    private static Camera FindPixelCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.targetTexture != null && main.GetComponent<CrispWorldUICamera>() == null) return main;
        return Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
            .FirstOrDefault(camera => camera.targetTexture != null && camera.GetComponent<CrispWorldUICamera>() == null);
    }
}
