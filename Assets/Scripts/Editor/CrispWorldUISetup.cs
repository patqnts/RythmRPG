using System.Linq;
using RythmRPG.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Scene setup for crisp world-space UI on top of the pixel render texture (see <see cref="CrispWorldUICamera"/>):
/// <list type="bullet">
/// <item>Adds the "CrispWorldUI" layer and removes it from the pixel camera's culling mask.</item>
/// <item>Adds "Pixel Display Rig" with a Base camera ("Pixel Display Camera") that draws the render texture's
/// canvas, switched from Screen Space - Overlay to Screen Space - Camera so it no longer covers every camera.</item>
/// <item>Stacks an Overlay camera ("Crisp World UI Camera") on it that draws only the CrispWorldUI layer at screen
/// resolution, lined up with the pixel camera.</item>
/// </list>
/// Screen Space - Overlay canvases (combat HUD, result screen, pause menu) still draw on top of everything.
/// Everything is undoable; "Remove Crisp World UI Camera" puts the scene back.
/// </summary>
public static class CrispWorldUISetup
{
    private const string MenuRoot = "Tools/Rythm RPG/Rendering/";
    private const string RigName = "Pixel Display Rig";
    private const string DisplayCameraName = "Pixel Display Camera";
    private const string CrispCameraName = "Crisp World UI Camera";
    // Far from the level, so the display camera never sees world objects even if they share the UI layer.
    private static readonly Vector3 RigPosition = new(0f, -5000f, 0f);

    [MenuItem(MenuRoot + "Set Up Crisp World UI Camera")]
    public static void SetUp()
    {
        int layer = EnsureLayer(CrispWorldUI.LayerName);
        if (layer < 0)
        {
            EditorUtility.DisplayDialog("Crisp World UI", "No free user layer (8-31) left for \"" + CrispWorldUI.LayerName + "\".", "OK");
            return;
        }
        CrispWorldUI.ResetCache();

        Camera source = FindPixelCamera();
        if (source == null)
        {
            EditorUtility.DisplayDialog("Crisp World UI", "No camera in the open scene renders into a render texture shown by a RawImage.", "OK");
            return;
        }
        RawImage output = FindOutput(source);
        Canvas outputCanvas = output != null && output.canvas != null ? output.canvas.rootCanvas : null;
        if (outputCanvas == null)
        {
            EditorUtility.DisplayDialog("Crisp World UI", "Could not find the RawImage/Canvas that shows \"" + source.name + "\"'s render texture.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Set Up Crisp World UI Camera");
        int undoGroup = Undo.GetCurrentGroup();

        // Pixel camera: stop drawing the crisp layer into the render texture.
        Undo.RecordObject(source, "Exclude Crisp World UI layer");
        source.cullingMask &= ~(1 << layer);

        // Display camera (Base): draws the render texture canvas to the screen, after the pixel camera.
        Camera display = FindCamera(DisplayCameraName);
        Transform rig = display != null ? display.transform.parent : null;
        if (rig == null)
        {
            var rigObject = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(rigObject, "Create " + RigName);
            MoveToSceneOf(rigObject, source.gameObject);
            rig = rigObject.transform;
            rig.position = RigPosition;
        }
        if (display == null)
        {
            var displayObject = new GameObject(DisplayCameraName, typeof(Camera));
            Undo.RegisterCreatedObjectUndo(displayObject, "Create " + DisplayCameraName);
            displayObject.transform.SetParent(rig, false);
            display = displayObject.GetComponent<Camera>();
        }
        Undo.RecordObject(display, "Configure " + DisplayCameraName);
        display.tag = "Untagged";
        display.targetTexture = null;
        display.clearFlags = CameraClearFlags.SolidColor;
        display.backgroundColor = Color.black;
        display.cullingMask = 1 << outputCanvas.gameObject.layer;
        display.orthographic = true;
        display.orthographicSize = 1f;
        display.nearClipPlane = 0.01f;
        display.farClipPlane = 10f;
        display.depth = source.depth + 1f; // after the pixel camera, so both show the same frame
        display.allowMSAA = false;
        UniversalAdditionalCameraData displayData = display.GetUniversalAdditionalCameraData();
        Undo.RecordObject(displayData, "Configure " + DisplayCameraName);
        displayData.renderType = CameraRenderType.Base;
        displayData.renderPostProcessing = false;
        displayData.antialiasing = AntialiasingMode.None;
        displayData.renderShadows = false;

        // Render texture canvas: Screen Space - Camera on the display camera.
        Undo.RecordObject(outputCanvas, "Render texture canvas to Screen Space - Camera");
        outputCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        outputCanvas.worldCamera = display;
        outputCanvas.planeDistance = 1f;

        // Crisp camera (Overlay): only the CrispWorldUI layer, stacked on the display camera.
        Camera crisp = FindCamera(CrispCameraName);
        if (crisp == null)
        {
            var crispObject = new GameObject(CrispCameraName, typeof(Camera));
            Undo.RegisterCreatedObjectUndo(crispObject, "Create " + CrispCameraName);
            crispObject.transform.SetParent(rig, false);
            crisp = crispObject.GetComponent<Camera>();
        }
        Undo.RecordObject(crisp, "Configure " + CrispCameraName);
        crisp.tag = "Untagged";
        crisp.targetTexture = null;
        crisp.clearFlags = CameraClearFlags.Depth;
        crisp.cullingMask = 1 << layer;
        crisp.allowMSAA = false;
        UniversalAdditionalCameraData crispData = crisp.GetUniversalAdditionalCameraData();
        Undo.RecordObject(crispData, "Configure " + CrispCameraName);
        crispData.renderType = CameraRenderType.Overlay;
        crispData.renderPostProcessing = false;
        crispData.antialiasing = AntialiasingMode.None;
        crispData.renderShadows = false;
        // clearDepth is read-only in the URP API; set the serialized field (Inspector: Rendering > Clear Depth).
        var serializedCrispData = new SerializedObject(crispData);
        SerializedProperty clearDepth = serializedCrispData.FindProperty("m_ClearDepth");
        if (clearDepth != null)
        {
            clearDepth.boolValue = true;
            serializedCrispData.ApplyModifiedProperties();
        }
        if (!displayData.cameraStack.Contains(crisp)) displayData.cameraStack.Add(crisp);

        CrispWorldUICamera sync = crisp.GetComponent<CrispWorldUICamera>();
        if (sync == null) sync = Undo.AddComponent<CrispWorldUICamera>(crisp.gameObject);
        var serializedSync = new SerializedObject(sync);
        serializedSync.FindProperty("source").objectReferenceValue = source;
        serializedSync.FindProperty("output").objectReferenceValue = output;
        serializedSync.ApplyModifiedProperties();
        sync.Configure(source, output);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
        Selection.activeGameObject = crisp.gameObject;
        Debug.Log($"[Crisp World UI] Set up: '{source.name}' renders the pixel world (without layer '{CrispWorldUI.LayerName}'), " +
                  $"'{DisplayCameraName}' shows '{output.name}' on '{outputCanvas.name}' (Screen Space - Camera), " +
                  $"'{CrispCameraName}' draws layer '{CrispWorldUI.LayerName}' at screen resolution on top. Save the scene to keep it.", crisp);
    }

    [MenuItem(MenuRoot + "Remove Crisp World UI Camera")]
    public static void Remove()
    {
        Camera display = FindCamera(DisplayCameraName);
        Camera crisp = FindCamera(CrispCameraName);
        CrispWorldUICamera sync = crisp != null ? crisp.GetComponent<CrispWorldUICamera>() : null;
        Camera source = sync != null && sync.Source != null ? sync.Source : FindPixelCamera();

        Undo.SetCurrentGroupName("Remove Crisp World UI Camera");
        int undoGroup = Undo.GetCurrentGroup();

        int layer = LayerMask.NameToLayer(CrispWorldUI.LayerName);
        if (source != null && layer >= 0)
        {
            Undo.RecordObject(source, "Include Crisp World UI layer");
            source.cullingMask |= 1 << layer;
        }
        RawImage output = sync != null && sync.Output != null ? sync.Output : source != null ? FindOutput(source) : null;
        Canvas outputCanvas = output != null && output.canvas != null ? output.canvas.rootCanvas : null;
        if (outputCanvas != null && outputCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            Undo.RecordObject(outputCanvas, "Render texture canvas to Screen Space - Overlay");
            outputCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            outputCanvas.worldCamera = null;
        }
        Transform rig = display != null ? display.transform.parent : crisp != null ? crisp.transform.parent : null;
        if (rig != null && rig.name == RigName) Undo.DestroyObjectImmediate(rig.gameObject);
        else
        {
            if (crisp != null) Undo.DestroyObjectImmediate(crisp.gameObject);
            if (display != null) Undo.DestroyObjectImmediate(display.gameObject);
        }

        Undo.CollapseUndoOperations(undoGroup);
        if (source != null) EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
        Debug.Log("[Crisp World UI] Removed. World UI renders into the pixel render texture again.");
    }

    [MenuItem(MenuRoot + "Put Selection On Crisp World UI Layer")]
    public static void PutSelectionOnLayer()
    {
        int layer = EnsureLayer(CrispWorldUI.LayerName);
        if (layer < 0) return;
        CrispWorldUI.ResetCache();
        foreach (GameObject selected in Selection.gameObjects)
        {
            if (selected.GetComponent<CrispWorldUIObject>() == null) Undo.AddComponent<CrispWorldUIObject>(selected);
            foreach (Transform child in selected.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(child.gameObject, "Put on Crisp World UI layer");
                child.gameObject.layer = layer;
            }
        }
    }

    [MenuItem(MenuRoot + "Put Selection On Crisp World UI Layer", true)]
    private static bool CanPutSelectionOnLayer() => Selection.gameObjects.Length > 0;

    private static int EnsureLayer(string layerName)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;
        var tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null) return -1;
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty entry = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(entry.stringValue)) continue;
            entry.stringValue = layerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Crisp World UI] Added layer '{layerName}' at index {i}.");
            return i;
        }
        return -1;
    }

    private static Camera FindPixelCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.targetTexture != null && main.GetComponent<CrispWorldUICamera>() == null) return main;
        return Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
            .Where(camera => camera.targetTexture != null && camera.GetComponent<CrispWorldUICamera>() == null)
            .FirstOrDefault(camera => FindOutput(camera) != null);
    }

    private static RawImage FindOutput(Camera source) =>
        source == null || source.targetTexture == null
            ? null
            : Object.FindObjectsByType<RawImage>(FindObjectsInactive.Include)
                .FirstOrDefault(image => image.texture == source.targetTexture);

    private static Camera FindCamera(string cameraName) =>
        Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).FirstOrDefault(camera => camera.name == cameraName);

    private static void MoveToSceneOf(GameObject created, GameObject sceneReference)
    {
        if (created.scene != sceneReference.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(created, sceneReference.scene);
    }
}
