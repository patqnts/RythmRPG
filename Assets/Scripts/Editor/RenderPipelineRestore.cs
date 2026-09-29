using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Puts the project back on a 3D URP pipeline after FlatKit (and its URP asset / renderer) was removed:
/// <list type="bullet">
/// <item>Graphics default + every quality level -> Assets/URPDefaultResources (Ultra for Ultra and any missing one).</item>
/// <item>Those pipeline assets: Depth + Opaque texture on, no opaque downsampling, HDR on (what FlatKit's asset had).
/// Ultra also gets FlatKit's shadow distance (100) and shadow resolution (2048).</item>
/// <item>Default_Forward_Renderer: depth copied after opaques (water / fog read it), and the two particle outline
/// features that lived on the FlatKit renderer are added back with their old settings.</item>
/// <item>Cameras in the open scenes: default renderer, depth / opaque texture from the pipeline.</item>
/// <item>Lists materials whose shader is missing (pink), e.g. ones that used FlatKit shaders.</item>
/// </list>
/// Runs once by itself after it compiles; Tools > Rythm RPG > Rendering > Restore 3D Pipeline runs it again.
/// </summary>
[InitializeOnLoad]
internal static class RenderPipelineRestore
{
    private const string DoneKey = "RythmRPG.RestorePipelineAfterFlatKit.v1";
    private const string Folder = "Assets/URPDefaultResources/";
    private const string RendererPath = Folder + "Default_Forward_Renderer.asset";
    private const int AfterRenderingTransparents = 500;

    static RenderPipelineRestore()
    {
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            EditorPrefs.SetBool(DoneKey, true);
            Run(true);
        };
    }

    [MenuItem("Tools/Rythm RPG/Rendering/Restore 3D Pipeline (after FlatKit removal)")]
    private static void RunFromMenu() => Run(false);

    private static void Run(bool automatic)
    {
        var log = new List<string>();
        var ultra = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Folder + "Ultra.asset");
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (ultra == null || renderer == null)
        {
            EditorUtility.DisplayDialog("Restore 3D Pipeline",
                "Could not find Assets/URPDefaultResources/Ultra.asset or Default_Forward_Renderer.asset. Nothing changed.", "OK");
            return;
        }

        // ---------- quality levels + graphics default
        var used = new List<UniversalRenderPipelineAsset> { ultra };
        UnityEngine.Object qualityObject = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
        if (qualityObject != null)
        {
            var quality = new SerializedObject(qualityObject);
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            for (int i = 0; levels != null && i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                string levelName = level.FindPropertyRelative("name").stringValue;
                SerializedProperty pipeline = level.FindPropertyRelative("customRenderPipeline");
                var current = pipeline.objectReferenceValue as UniversalRenderPipelineAsset;
                bool rendersSprites = current != null && UsesOnly2DRenderer(current);
                if (current != null && !rendersSprites && AssetDatabase.GetAssetPath(current).StartsWith(Folder))
                {
                    if (!used.Contains(current)) used.Add(current);
                    continue;
                }
                var match = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Folder + levelName + ".asset");
                UniversalRenderPipelineAsset pick = levelName == "Ultra" || match == null ? ultra : match;
                pipeline.objectReferenceValue = pick;
                if (!used.Contains(pick)) used.Add(pick);
                log.Add($"Quality '{levelName}': render pipeline -> {pick.name}");
            }
            quality.ApplyModifiedPropertiesWithoutUndo();
        }
        if (GraphicsSettings.defaultRenderPipeline != ultra)
        {
            GraphicsSettings.defaultRenderPipeline = ultra;
            log.Add("Graphics: default render pipeline -> Ultra");
        }

        // ---------- pipeline assets: what FlatKit's asset had
        foreach (UniversalRenderPipelineAsset asset in used)
        {
            var so = new SerializedObject(asset);
            SetInt(so, "m_RequireDepthTexture", 1);
            SetInt(so, "m_RequireOpaqueTexture", 1);
            SetInt(so, "m_OpaqueDownsampling", 0);
            SetInt(so, "m_SupportsHDR", 1);
            if (asset == ultra)
            {
                SetFloat(so, "m_ShadowDistance", 100f);
                SetInt(so, "m_MainLightShadowmapResolution", 2048);
            }
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list != null && (list.arraySize == 0 || list.GetArrayElementAtIndex(0).objectReferenceValue == null))
            {
                if (list.arraySize == 0) list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                SetInt(so, "m_DefaultRendererIndex", 0);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            log.Add($"{asset.name}: Depth + Opaque texture on, no downsampling, HDR on");
        }

        // ---------- renderer: depth after opaques, particle outline features back
        var rso = new SerializedObject(renderer);
        SetInt(rso, "m_CopyDepthMode", 0);
        rso.ApplyModifiedPropertiesWithoutUndo();
        log.Add("Default_Forward_Renderer: Depth Texture Mode -> After Opaques");

        AddMergedOutline(renderer, log);
        AddPixelParticleOutline(renderer, log);
        renderer.SetDirty();
        EditorUtility.SetDirty(renderer);

        // ---------- cameras in the open scenes
        int cameras = 0;
        foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            var data = camera.GetUniversalAdditionalCameraData();
            if (data == null) continue;
            Undo.RecordObject(data, "Restore 3D Pipeline");
            data.SetRenderer(-1);
            data.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
            data.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            EditorUtility.SetDirty(data);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            cameras++;
        }
        if (cameras > 0) log.Add($"{cameras} camera(s) in the open scene(s): default renderer, depth / opaque from the pipeline (save the scene)");

        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("File/Save Project");

        // ---------- pink materials
        var broken = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) continue;
            Shader shader = material.shader;
            if (shader == null || shader.name == "Hidden/InternalErrorShader" || !shader.isSupported) broken.Add(path);
        }

        string summary = "- " + string.Join("\n- ", log);
        Debug.Log("[Rendering] Restored the 3D pipeline after FlatKit removal:\n" + summary);
        if (broken.Count > 0)
            Debug.LogWarning($"[Rendering] {broken.Count} material(s) have a missing shader (pink), probably FlatKit ones. " +
                             "Switch them to URP/Lit or a Rythm RPG pixel shader:\n" + string.Join("\n", broken));

        EditorUtility.DisplayDialog("Restore 3D Pipeline",
            (automatic ? "The project was switched back to the 3D pipeline:\n\n" : "Done:\n\n") + summary +
            (broken.Count > 0 ? $"\n\n{broken.Count} material(s) still use a missing shader (pink). The Console lists them." : "") +
            "\n\nSave your open scene(s) to keep the camera changes.", "OK");
    }

    private static bool UsesOnly2DRenderer(UniversalRenderPipelineAsset asset)
    {
        var so = new SerializedObject(asset);
        SerializedProperty list = so.FindProperty("m_RendererDataList");
        if (list == null || list.arraySize == 0) return false;
        for (int i = 0; i < list.arraySize; i++)
        {
            UnityEngine.Object data = list.GetArrayElementAtIndex(i).objectReferenceValue;
            if (data is UniversalRendererData) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ features

    private static Type FindFeatureType(string className)
    {
        foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableRendererFeature>())
            if (type.Name == className && !type.IsAbstract) return type;
        return null;
    }

    private static ScriptableRendererFeature AddFeature(UniversalRendererData renderer, Type type, string name, List<string> log)
    {
        foreach (ScriptableRendererFeature existing in renderer.rendererFeatures)
            if (existing != null && existing.GetType() == type)
            {
                log.Add($"Default_Forward_Renderer already has {name}");
                return null;
            }

        var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
        feature.name = name;
        AssetDatabase.AddObjectToAsset(feature, renderer);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
        var so = new SerializedObject(renderer);
        SerializedProperty features = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        if (map != null)
        {
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add($"Default_Forward_Renderer: added {name}");
        return feature;
    }

    private static void AddMergedOutline(UniversalRendererData renderer, List<string> log)
    {
        Type type = FindFeatureType("MergedParticleOutlineRendererFeature");
        if (type == null)
        {
            log.Add("MergedParticleOutlineRendererFeature script not found: skipped");
            return;
        }
        ScriptableRendererFeature feature = AddFeature(renderer, type, "MergedParticleOutlineRendererFeature", log);
        if (feature == null) return;

        var so = new SerializedObject(feature);
        SerializedProperty groups = so.FindProperty("settings.outlineGroups");
        if (groups != null)
        {
            groups.arraySize = 2;
            SetGroup(groups.GetArrayElementAtIndex(0), "Pixel Particles", 1 << 9, Color.white);
            SetGroup(groups.GetArrayElementAtIndex(1), "Dark", 1 << 10, new Color(0.09251513f, 0.3917927f, 0.9339623f, 1f));
        }
        SetBool(so, "settings.respectSceneDepth", true);
        SetBool(so, "settings.showInSceneView", true);
        SetEnum(so, "settings.injectionPoint", AfterRenderingTransparents);
        so.ApplyModifiedPropertiesWithoutUndo();
        feature.Create();
        EditorUtility.SetDirty(feature);
    }

    private static void SetGroup(SerializedProperty group, string name, int layers, Color color)
    {
        SerializedProperty p;
        if ((p = group.FindPropertyRelative("name")) != null) p.stringValue = name;
        if ((p = group.FindPropertyRelative("enabled")) != null) p.boolValue = true;
        if ((p = group.FindPropertyRelative("particleLayers")) != null) p.intValue = layers;
        if ((p = group.FindPropertyRelative("outlineColor")) != null) p.colorValue = color;
        if ((p = group.FindPropertyRelative("outlinePixels")) != null) p.floatValue = 0.8f;
        if ((p = group.FindPropertyRelative("outlineSoftness")) != null) p.floatValue = 0.05f;
        if ((p = group.FindPropertyRelative("metaballThreshold")) != null) p.floatValue = 0.35f;
    }

    private static void AddPixelParticleOutline(UniversalRendererData renderer, List<string> log)
    {
        Type type = FindFeatureType("PixelParticleOutlineRendererFeature");
        if (type == null)
        {
            log.Add("PixelParticleOutlineRendererFeature script not found in the project: skipped (the merged outline covers it)");
            return;
        }
        ScriptableRendererFeature feature = AddFeature(renderer, type, "PixelParticleOutlineRendererFeature", log);
        if (feature == null) return;

        var so = new SerializedObject(feature);
        SerializedProperty p;
        if ((p = so.FindProperty("settings.particleLayer")) != null) p.intValue = 1 << 9;
        if ((p = so.FindProperty("settings.maskMaterial")) != null) p.objectReferenceValue = LoadByGuid<Material>("c92359e2a135c8248b4556459b521f18");
        if ((p = so.FindProperty("settings.compositeMaterial")) != null) p.objectReferenceValue = LoadByGuid<Material>("bc55f86d00123ab0c824ffbe2278000c");
        if ((p = so.FindProperty("settings.outlineWidth")) != null)
        {
            if (p.propertyType == SerializedPropertyType.Integer) p.intValue = 1;
            else p.floatValue = 1f;
        }
        if ((p = so.FindProperty("settings.outlineColor")) != null) p.colorValue = new Color(0.03f, 0.025f, 0.05f, 1f);
        SetEnum(so, "settings.renderPassEvent", AfterRenderingTransparents);
        so.ApplyModifiedPropertiesWithoutUndo();
        feature.Create();
        EditorUtility.SetDirty(feature);
    }

    private static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
    }

    // ------------------------------------------------------------------ helpers

    private static void SetInt(SerializedObject so, string path, int value)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null) return;
        if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
        else if (p.propertyType == SerializedPropertyType.Enum) p.intValue = value;
        else p.intValue = value;
    }

    private static void SetFloat(SerializedObject so, string path, float value)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p != null) p.floatValue = value;
    }

    private static void SetBool(SerializedObject so, string path, bool value)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p != null) p.boolValue = value;
    }

    // Enums are stored by value; set the value directly (not the index into the names list).
    private static void SetEnum(SerializedObject so, string path, int value)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p != null) p.intValue = value;
    }
}
