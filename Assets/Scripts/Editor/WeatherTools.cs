using System.Collections.Generic;
using RythmRPG.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menus and inspectors for the weather and Pixel Water:
/// GameObject > Rythm RPG > Weather / Weather Zone (Interior) / Heat Haze / Pixel Water / Terrain Weather (Snow, Mist, Fog),
/// Tools > Rythm RPG > Rendering > Install Weather Renderer Feature,
/// Tools > Rythm RPG > Water > Convert FlatKit Water To Pixel Water.
/// </summary>
public static class WeatherTools
{
    private const string WaterMaterialFolder = "Assets/Mats/Environment";
    private const string WaterShaderName = "RythmRPG/Pixel Water";
    private const string FlatKitWaterShader = "FlatKit/Water";

    // ------------------------------------------------------------------ create

    [MenuItem("GameObject/Rythm RPG/Weather", false, 10)]
    public static void CreateWeather(MenuCommand command)
    {
        WeatherController existing = Object.FindAnyObjectByType<WeatherController>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[Weather] This scene already has a Weather Controller (selected).");
            return;
        }
        var go = new GameObject("Weather");
        Undo.RegisterCreatedObjectUndo(go, "Create Weather");
        go.AddComponent<WeatherController>();
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Selection.activeObject = go;
        if (!FeatureInstalled())
        {
            if (EditorUtility.DisplayDialog("Weather",
                    "Heat shimmer, storm tint and the lightning flash need the Weather renderer feature (it also gives Terrain mist / fog the depth texture). Install it now?",
                    "Install", "Later"))
                InstallRendererFeature();
        }
    }

    [MenuItem("GameObject/Rythm RPG/Weather Zone (Interior)", false, 11)]
    public static void CreateWeatherZone(MenuCommand command)
    {
        var go = CreateAtView("Weather Zone (Interior)", command, new Vector3(6f, 4f, 6f));
        go.AddComponent<WeatherZone>();
    }

    [MenuItem("GameObject/Rythm RPG/Terrain Weather - Snow", false, 15)]
    public static void CreateTerrainSnow(MenuCommand command) => CreateTerrainWeather(command, TerrainWeatherKind.Snow);

    [MenuItem("GameObject/Rythm RPG/Terrain Weather - Mist", false, 16)]
    public static void CreateTerrainMist(MenuCommand command) => CreateTerrainWeather(command, TerrainWeatherKind.Mist);

    [MenuItem("GameObject/Rythm RPG/Terrain Weather - Fog", false, 17)]
    public static void CreateTerrainFog(MenuCommand command) => CreateTerrainWeather(command, TerrainWeatherKind.Fog);

    private static void CreateTerrainWeather(MenuCommand command, TerrainWeatherKind kind)
    {
        var go = CreateAtView("Terrain Weather (" + kind + ")", command, Vector3.one);
        TerrainWeather field = go.AddComponent<TerrainWeather>();
        field.ApplyKindDefaults(kind);
        EditorUtility.SetDirty(field);
        if (kind != TerrainWeatherKind.Snow && !FeatureInstalled()
            && EditorUtility.DisplayDialog("Terrain Weather", "Mist and fog need the camera depth texture. Install the Weather renderer feature (it asks for it)?",
                "Install", "Later"))
            InstallRendererFeature();
    }

    [MenuItem("GameObject/Rythm RPG/Heat Haze", false, 13)]
    public static void CreateHeatHaze(MenuCommand command)
    {
        var go = CreateAtView("Heat Haze", command, new Vector3(2f, 3f, 2f));
        go.transform.position += Vector3.up * 1.5f;
        go.AddComponent<HeatHaze>();
    }

    [MenuItem("GameObject/Rythm RPG/Pixel Water", false, 14)]
    public static void CreateWater(MenuCommand command)
    {
        var go = CreateAtView("Pixel Water", command, Vector3.one);
        go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.sharedMaterial = DefaultWaterMaterial();
        go.AddComponent<PixelWater>();
    }

    private static GameObject CreateAtView(string name, MenuCommand command, Vector3 scale)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        if (command.context == null && SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            go.transform.position = new Vector3(pivot.x, Weather.GroundHeight, pivot.z);
        }
        go.transform.localScale = scale;
        Selection.activeObject = go;
        return go;
    }

    private static Material DefaultWaterMaterial()
    {
        string path = WaterMaterialFolder + "/Pixel Water.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find(WaterShaderName);
        if (shader == null)
        {
            Debug.LogWarning("[Water] Shader 'RythmRPG/Pixel Water' not found.");
            return null;
        }
        EnsureFolder(WaterMaterialFolder);
        material = new Material(shader) { name = "Pixel Water" };
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    // ------------------------------------------------------------------ renderer feature

    public static bool FeatureInstalled()
    {
        foreach (ScriptableRendererData data in UsedRendererData())
            if (!Has(data)) return false;
        return true;
    }

    private static bool Has(ScriptableRendererData data)
    {
        foreach (ScriptableRendererFeature f in data.rendererFeatures)
            if (f is WeatherRendererFeature) return true;
        return false;
    }

    private static List<ScriptableRendererData> UsedRendererData()
    {
        var assets = new List<RenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            RenderPipelineAsset a = QualitySettings.GetRenderPipelineAssetAt(i);
            if (a != null && !assets.Contains(a)) assets.Add(a);
        }
        var result = new List<ScriptableRendererData>();
        foreach (RenderPipelineAsset a in assets)
        {
            if (a is not UniversalRenderPipelineAsset) continue;
            var so = new SerializedObject(a);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list == null) continue;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData data && !result.Contains(data))
                    result.Add(data);
        }
        return result;
    }

    [MenuItem("Tools/Rythm RPG/Rendering/Install Weather Renderer Feature")]
    public static void InstallRendererFeature()
    {
        Shader shader = Resources.Load<Shader>("Rendering/Weather/WeatherScreen");
        var names = new List<string>();
        foreach (ScriptableRendererData data in UsedRendererData())
        {
            if (Has(data)) continue;
            var feature = ScriptableObject.CreateInstance<WeatherRendererFeature>();
            feature.name = "Weather (Heat, Lightning)";
            feature.shader = shader;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            var so = new SerializedObject(data);
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            if (map != null)
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }
            so.ApplyModifiedProperties();
            data.SetDirty();
            EditorUtility.SetDirty(data);
            names.Add(data.name);
        }
        AssetDatabase.SaveAssets();
        Debug.Log(names.Count > 0
            ? $"[Weather] Added the Weather renderer feature to: {string.Join(", ", names)}."
            : "[Weather] Every renderer already has the Weather renderer feature.");
    }

    // ------------------------------------------------------------------ FlatKit water conversion

    [MenuItem("Tools/Rythm RPG/Water/Convert FlatKit Water To Pixel Water")]
    public static void ConvertFlatKitWater()
    {
        Shader pixelWater = Shader.Find(WaterShaderName);
        if (pixelWater == null)
        {
            EditorUtility.DisplayDialog("Pixel Water", "Shader 'RythmRPG/Pixel Water' not found.", "OK");
            return;
        }

        // Materials: the selected ones, or every FlatKit water material used in the open scenes.
        var sources = new List<Material>();
        foreach (Object o in Selection.objects)
            if (o is Material m && m.shader != null && m.shader.name == FlatKitWaterShader && !sources.Contains(m)) sources.Add(m);
        var renderers = new List<Renderer>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            bool uses = false;
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || m.shader == null || m.shader.name != FlatKitWaterShader) continue;
                uses = true;
                if (Selection.objects.Length == 0 || Selection.Contains(m) || Selection.Contains(r.gameObject))
                    if (!sources.Contains(m)) sources.Add(m);
            }
            if (uses) renderers.Add(r);
        }
        if (sources.Count == 0)
        {
            EditorUtility.DisplayDialog("Pixel Water",
                "No FlatKit water materials found (select them, or open the scenes that use them).", "OK");
            return;
        }

        var converted = new Dictionary<Material, Material>();
        foreach (Material source in sources)
        {
            string path = AssetDatabase.GetAssetPath(source);
            string folder = string.IsNullOrEmpty(path) ? WaterMaterialFolder : System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(folder);
            string target = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name} (Pixel Water).mat");
            var material = new Material(pixelWater) { name = System.IO.Path.GetFileNameWithoutExtension(target) };
            CopySettings(source, material);
            AssetDatabase.CreateAsset(material, target);
            converted[source] = material;
        }

        int swapped = 0;
        foreach (Renderer r in renderers)
        {
            Material[] materials = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && converted.TryGetValue(materials[i], out Material replacement))
                {
                    materials[i] = replacement;
                    changed = true;
                }
            }
            if (!changed) continue;
            Undo.RecordObject(r, "Convert To Pixel Water");
            r.sharedMaterials = materials;
            if (r is MeshRenderer && r.GetComponent<PixelWater>() == null)
            {
                // Keep the existing mesh: turn mesh generation off right after adding the component.
                MeshFilter filter = r.GetComponent<MeshFilter>();
                Mesh original = filter != null ? filter.sharedMesh : null;
                PixelWater water = Undo.AddComponent<PixelWater>(r.gameObject);
                var so = new SerializedObject(water);
                so.FindProperty("generateMesh").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                if (filter != null && original != null) filter.sharedMesh = original;
            }
            EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
            swapped++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Water] Made {converted.Count} Pixel Water material(s) and switched {swapped} renderer(s). " +
                  "The FlatKit materials were kept; the old WaterRippleManager / WaterInteractor objects are no longer " +
                  "needed (every character already makes ripples).");
    }

    private static void CopySettings(Material fk, Material m)
    {
        float Get(string name, float fallback) => fk.HasProperty(name) ? fk.GetFloat(name) : fallback;
        Color GetColor(string name, Color fallback) => fk.HasProperty(name) ? fk.GetColor(name) : fallback;

        // FlatKit mixes the scene in by (transparency * colour alpha); here alpha is opacity and See-Through scales it.
        Color shallow = GetColor("_ColorShallow", new Color(0.35f, 0.6f, 0.75f, 0.8f));
        Color deep = GetColor("_ColorDeep", new Color(0.65f, 0.9f, 1f, 1f));
        shallow.a = 1f - shallow.a;
        deep.a = 1f - deep.a;
        m.SetColor("_ShallowColor", shallow);
        m.SetColor("_DeepColor", deep);
        m.SetFloat("_Clarity", Mathf.Clamp01(Get("_WaterClearness", 0.3f)));
        m.SetFloat("_DepthDistance", Mathf.Max(0.05f, Get("_FadeDistance", 0.5f) + Get("_WaterDepth", 5f)));
        m.SetFloat("_ShadowStrength", Get("_ShadowStrength", 0.35f));
        m.SetFloat("_LightInfluence", Get("_LightContribution", 0f));

        m.SetColor("_CrestColor", GetColor("_CrestColor", Color.white));
        m.SetFloat("_CrestThreshold", 1f - Mathf.Clamp01(Get("_CrestSize", 0.1f)));

        bool wavesOff = fk.IsKeywordEnabled("_WAVEMODE_NONE");
        float frequency = Mathf.Max(0.05f, Get("_WaveFrequency", 1f));
        m.SetFloat("_WaveAmplitude", wavesOff ? 0f : Mathf.Clamp(Get("_WaveAmplitude", 0.25f), 0f, 0.5f));
        m.SetFloat("_WaveLength", 2f * Mathf.PI / frequency);
        m.SetFloat("_WaveSpeed", 2f * Get("_WaveSpeed", 0.5f) / frequency);
        m.SetFloat("_WaveDirection", Mathf.Repeat(Get("_WaveDirection", 0f) * 180f, 360f));

        m.SetColor("_FoamColor", GetColor("_FoamColor", Color.white));
        m.SetFloat("_FoamDistance", Mathf.Max(0.05f, Get("_FoamDepth", 0.5f)));
        if (fk.IsKeywordEnabled("_FOAMMODE_NONE")) m.SetFloat("_FoamBands", 0f);

        m.SetFloat("_RefractionPixels", Mathf.Clamp(Mathf.Round(Get("_RefractionAmplitude", 0.01f) * 270f), 0f, 6f));
        m.SetFloat("_RefractionSpeed", Get("_RefractionSpeed", 0.1f) * 4f);
        m.SetFloat("_RefractionScale", Mathf.Max(0.1f, Get("_RefractionScale", 1f)));
        m.renderQueue = -1;
    }
}

/// <summary>Weather Controller inspector: preset buttons (fade in Play mode, preview in Edit mode) and live status.</summary>
[CustomEditor(typeof(WeatherController))]
internal sealed class WeatherControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var controller = (WeatherController)target;
        if (!WeatherTools.FeatureInstalled())
        {
            EditorGUILayout.HelpBox("Heat shimmer, storm tint, the lightning flash and Terrain Weather need the Weather " +
                                    "renderer feature (rain and snowfall work without it).", MessageType.Info);
            if (GUILayout.Button("Install Weather Renderer Feature")) WeatherTools.InstallRendererFeature();
            EditorGUILayout.Space();
        }

        EditorGUILayout.LabelField(Application.isPlaying ? "Fade to" : "Start weather (preview)", EditorStyles.boldLabel);
        IReadOnlyList<WeatherProfile> profiles = controller.Profiles;
        const int perRow = 3;
        for (int i = 0; i < profiles.Count; i += perRow)
        {
            EditorGUILayout.BeginHorizontal();
            for (int j = i; j < Mathf.Min(i + perRow, profiles.Count); j++)
            {
                WeatherProfile profile = profiles[j];
                if (profile == null) continue;
                bool active = profile.name == controller.CurrentName;
                GUI.backgroundColor = active ? new Color(0.6f, 0.85f, 1f) : Color.white;
                if (GUILayout.Button(profile.name))
                {
                    if (Application.isPlaying)
                    {
                        controller.SetWeather(profile.name);
                    }
                    else
                    {
                        serializedObject.Update();
                        serializedObject.FindProperty("startWeather").stringValue = profile.name;
                        serializedObject.ApplyModifiedProperties();
                        SceneView.RepaintAll();
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        if (Application.isPlaying)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Strike Lightning")) controller.StrikeLightning();
            EditorGUILayout.EndHorizontal();
            WeatherState s = Weather.Current;
            EditorGUILayout.HelpBox(
                $"Now: {controller.CurrentName}   rain {s.rain:0.00}  snowfall {s.snowfall:0.00}  heat {s.heat:0.00}  " +
                $"wind {s.windSpeed:0.0}  lightning {s.lightning:0.00}",
                MessageType.None);
            Repaint();
        }
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
