using System.Collections.Generic;
using System.IO;
using RythmRPG.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Material inspector for "RythmRPG/Pixel Sprite" and "RythmRPG/Pixel Mesh". Draws the normal property list, then
/// keeps the hidden state in sync: blend / depth write / queue for Cutout vs Transparent, the Lit and normal-map
/// keywords, and turns the outline and x-ray passes on or off so materials without them cost nothing.
/// </summary>
public sealed class PixelArtShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        EditorGUI.BeginChangeCheck();
        DrawProperties(materialEditor, properties);
        bool changed = EditorGUI.EndChangeCheck();

        foreach (Object target in materialEditor.targets)
        {
            if (target is Material material)
                PixelArtMaterials.Apply(material);
        }
        if (changed) SceneView.RepaintAll();

        if (!PixelArtMaterials.FeatureInstalled())
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Pixel Outline and X-Ray need the Pixel Art renderer feature on your URP renderer.",
                                    MessageType.Info);
            if (GUILayout.Button("Install Pixel Art Renderer Feature"))
                PixelArtMaterials.InstallRendererFeature();
        }
    }

    // Draws every property, hiding the outline / x-ray options while their toggle is off (shared by all selected
    // materials only when they agree).
    private static void DrawProperties(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        bool outlineOn = Toggle(properties, "_Outline");
        bool xRayOn = Toggle(properties, "_XRay");
        bool normalMapOn = Toggle(properties, "_UseNormalMap");
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Max(labelWidth, 230f);
        foreach (MaterialProperty prop in properties)
        {
            if ((prop.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0) continue;
            string n = prop.name;
            if (!outlineOn && (n == "_OutlineColor" || n == "_OutlineWidth")) continue;
            if (!xRayOn && n.StartsWith("_XRay") && n != "_XRay") continue;
            if (!normalMapOn && (n == "_NormalMap" || n == "_NormalStrength")) continue;
            if (prop.propertyType == UnityEngine.Rendering.ShaderPropertyType.Texture)
                materialEditor.TexturePropertySingleLine(new GUIContent(prop.displayName), prop);
            else
                materialEditor.ShaderProperty(prop, prop.displayName);
            if ((n == "_Outline" || n == "_XRay") && Toggle(properties, n)) EditorGUI.indentLevel = 1;
            if (n == "_OutlineWidth" || n == "_XRayPulseSpeed") EditorGUI.indentLevel = 0;
        }
        EditorGUI.indentLevel = 0;
        EditorGUIUtility.labelWidth = labelWidth;
        EditorGUILayout.Space();
        materialEditor.RenderQueueField();
        materialEditor.EnableInstancingField();
    }

    private static bool Toggle(MaterialProperty[] properties, string name)
    {
        MaterialProperty p = FindProperty(name, properties, false);
        return p == null || p.hasMixedValue || p.floatValue > 0.5f;
    }

    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
    {
        Texture mainTex = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
            : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        base.AssignNewShaderToMaterial(material, oldShader, newShader);
        if (mainTex != null && material.HasProperty("_MainTex")) material.SetTexture("_MainTex", mainTex);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        PixelArtMaterials.Apply(material);
    }
}

/// <summary>Material setup, starter materials, conversion and the renderer-feature installer for the pixel-art shaders.</summary>
public static class PixelArtMaterials
{
    public const string SpriteShaderName = "RythmRPG/Pixel Sprite";
    public const string MeshShaderName = "RythmRPG/Pixel Mesh";
    private const string StarterFolder = "Assets/Mats/PixelArt";

    public static bool IsPixelArt(Material m) =>
        m != null && m.shader != null && (m.shader.name == SpriteShaderName || m.shader.name == MeshShaderName);

    /// <summary>Syncs blend, queue, keywords and optional passes with the material's toggles.</summary>
    public static void Apply(Material m)
    {
        if (!IsPixelArt(m)) return;
        bool transparent = m.GetFloat("_Surface") > 0.5f;
        m.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        m.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        m.SetFloat("_ZWrite", transparent ? 0f : 1f);
        m.SetOverrideTag("RenderType", transparent ? "Transparent" : "TransparentCutout");
        int queue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.AlphaTest;
        if (m.renderQueue != queue) m.renderQueue = queue;

        SetKeyword(m, "_SURFACE_TYPE_TRANSPARENT", transparent);
        SetKeyword(m, "_PIXEL_LIT", m.GetFloat("_Lit") > 0.5f);
        SetKeyword(m, "_NORMALMAP", m.GetFloat("_UseNormalMap") > 0.5f);

        m.SetShaderPassEnabled("PixelOutlineMask", m.GetFloat("_Outline") > 0.5f);
        m.SetShaderPassEnabled("PixelXRay", m.GetFloat("_XRay") > 0.5f);
        m.SetShaderPassEnabled("DepthOnly", !transparent);
        m.SetShaderPassEnabled("DepthNormals", !transparent);
    }

    private static void SetKeyword(Material m, string keyword, bool on)
    {
        if (on) m.EnableKeyword(keyword);
        else m.DisableKeyword(keyword);
    }

    // ------------------------------------------------------------------ starter materials

    private struct Starter
    {
        public string name;
        public bool mesh;
        public bool lit;
        public bool outline;
        public bool xRay;
        public Color outlineColor;
    }

    [MenuItem("Tools/Rythm RPG/Pixel Art/Create Starter Materials")]
    public static void CreateStarterMaterials()
    {
        Color ink = new(0.06f, 0.04f, 0.09f, 1f);
        Starter[] starters =
        {
            new() { name = "Pixel Note (Unlit)", lit = false, outline = true, outlineColor = ink },
            new() { name = "Pixel Note (Lit)", lit = true, outline = true, outlineColor = ink },
            new() { name = "Pixel Player", lit = true, outline = true, xRay = true, outlineColor = ink },
            new() { name = "Pixel Character", lit = true, outline = true, outlineColor = ink },
            new() { name = "Pixel Prop Sprite", lit = true, outline = true, outlineColor = ink },
            new() { name = "Pixel Prop Mesh", mesh = true, lit = true, outline = true, outlineColor = ink },
            new() { name = "Pixel Ground Mesh", mesh = true, lit = true, outline = false, outlineColor = ink },
        };

        Directory.CreateDirectory(StarterFolder);
        var created = new List<Object>();
        foreach (Starter s in starters)
        {
            string path = $"{StarterFolder}/{s.name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
            Shader shader = Shader.Find(s.mesh ? MeshShaderName : SpriteShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Pixel Art] Shader '{(s.mesh ? MeshShaderName : SpriteShaderName)}' not found.");
                return;
            }
            var m = new Material(shader) { name = s.name };
            m.SetFloat("_Lit", s.lit ? 1f : 0f);
            m.SetFloat("_Outline", s.outline ? 1f : 0f);
            m.SetFloat("_XRay", s.xRay ? 1f : 0f);
            m.SetColor("_OutlineColor", s.outlineColor);
            Apply(m);
            AssetDatabase.CreateAsset(m, path);
            created.Add(m);
        }
        AssetDatabase.SaveAssets();
        if (created.Count > 0) Selection.objects = created.ToArray();
        Debug.Log($"[Pixel Art] Starter materials are in {StarterFolder} ({created.Count} new).");
        if (!FeatureInstalled())
            Debug.Log("[Pixel Art] Outlines and x-ray need the renderer feature: Tools > Rythm RPG > Rendering > " +
                      "Install Pixel Art Renderer Feature.");
    }

    // ------------------------------------------------------------------ conversion

    [MenuItem("Tools/Rythm RPG/Pixel Art/Convert Selected Materials To Pixel Sprite")]
    private static void ConvertToSprite() => ConvertSelection(SpriteShaderName);

    [MenuItem("Tools/Rythm RPG/Pixel Art/Convert Selected Materials To Pixel Mesh")]
    private static void ConvertToMesh() => ConvertSelection(MeshShaderName);

    [MenuItem("Tools/Rythm RPG/Pixel Art/Convert Selected Materials To Pixel Sprite", true)]
    [MenuItem("Tools/Rythm RPG/Pixel Art/Convert Selected Materials To Pixel Mesh", true)]
    private static bool ValidateConvert() => Selection.GetFiltered<Material>(SelectionMode.Assets).Length > 0;

    private static void ConvertSelection(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[Pixel Art] Shader '{shaderName}' not found.");
            return;
        }
        int count = 0;
        foreach (Material m in Selection.GetFiltered<Material>(SelectionMode.Assets))
        {
            Undo.RecordObject(m, "Convert To Pixel Art Shader");
            Convert(m, shader);
            EditorUtility.SetDirty(m);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Pixel Art] Converted {count} material(s) to {shaderName}.");
    }

    /// <summary>Switches a material to a pixel-art shader, keeping its texture, colour, cutoff and outline.</summary>
    public static void Convert(Material m, Shader shader)
    {
        Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
        if (tex == null && m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
        Vector2 scale = Vector2.one, offset = Vector2.zero;
        if (m.HasProperty("_BaseMap")) { scale = m.GetTextureScale("_BaseMap"); offset = m.GetTextureOffset("_BaseMap"); }
        Color color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
            : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
        bool hadOutline = m.shader != null && m.shader.name.Contains("Outline");
        Color outlineColor = m.HasProperty("_OutlineColor") ? m.GetColor("_OutlineColor") : new Color(0.06f, 0.04f, 0.09f, 1f);
        bool transparent = m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f;
        Texture emission = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
        Color emissionColor = m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION")
            ? m.GetColor("_EmissionColor") : Color.black;

        m.shader = shader;
        if (tex != null && shader.name == MeshShaderName)
        {
            m.SetTexture("_MainTex", tex);
            m.SetTextureScale("_MainTex", scale);
            m.SetTextureOffset("_MainTex", offset);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Cutoff", cutoff);
        m.SetFloat("_Surface", transparent ? 1f : 0f);
        m.SetFloat("_Lit", 1f);
        if (hadOutline)
        {
            m.SetFloat("_Outline", 1f);
            m.SetColor("_OutlineColor", outlineColor);
        }
        if (emission != null) m.SetTexture("_EmissionMap", emission);
        m.SetColor("_EmissionColor", emissionColor);
        Apply(m);
    }

    // ------------------------------------------------------------------ renderer feature

    public static bool FeatureInstalled()
    {
        foreach (ScriptableRendererData data in UsedRendererData())
        {
            if (!Has(data)) return false;
        }
        return true;
    }

    private static bool Has(ScriptableRendererData data)
    {
        foreach (ScriptableRendererFeature f in data.rendererFeatures)
        {
            if (f is PixelArtRendererFeature) return true;
        }
        return false;
    }

    /// <summary>Every Universal renderer used by the default pipeline asset or any quality level.</summary>
    private static List<ScriptableRendererData> UsedRendererData()
    {
        var assets = new List<RenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
        int levels = QualitySettings.names.Length;
        for (int i = 0; i < levels; i++)
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
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData data && !result.Contains(data))
                    result.Add(data);
            }
        }
        return result;
    }

    [MenuItem("Tools/Rythm RPG/Rendering/Install Pixel Art Renderer Feature")]
    public static void InstallRendererFeature()
    {
        Shader composite = Resources.Load<Shader>("Rendering/PixelArtComposite");
        int added = 0;
        var names = new List<string>();
        foreach (ScriptableRendererData data in UsedRendererData())
        {
            if (Has(data)) continue;
            var feature = ScriptableObject.CreateInstance<PixelArtRendererFeature>();
            feature.name = "Pixel Art (Outlines + X-Ray)";
            feature.compositeShader = composite;
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
            added++;
            names.Add(data.name);
        }
        AssetDatabase.SaveAssets();
        Debug.Log(added > 0
            ? $"[Pixel Art] Added the Pixel Art renderer feature to: {string.Join(", ", names)}."
            : "[Pixel Art] Every renderer already has the Pixel Art renderer feature.");
    }
}
