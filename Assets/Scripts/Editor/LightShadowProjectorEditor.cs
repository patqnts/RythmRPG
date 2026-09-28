using System.IO;
using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="LightShadowProjector"/>: presets (with generated starter pixel-art cookies), the create
/// menus, and the path / preview tools for <see cref="ShadowFlyover"/>.
/// </summary>
[CustomEditor(typeof(LightShadowProjector))]
[CanEditMultipleObjects]
public sealed class LightShadowProjectorEditor : Editor
{
    private static LightShadowProjector.Preset preset = LightShadowProjector.Preset.WindowLight;
    private static bool useStarterCookie = true;

    public override void OnInspectorGUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            preset = (LightShadowProjector.Preset)EditorGUILayout.EnumPopup(preset);
            if (GUILayout.Button("Apply", GUILayout.Width(70)))
            {
                foreach (Object o in targets) ApplyPreset((LightShadowProjector)o, preset, useStarterCookie);
            }
            EditorGUILayout.EndHorizontal();
            useStarterCookie = EditorGUILayout.ToggleLeft(
                new GUIContent("Also assign the starter image",
                    "Uses a generated pixel-art cookie from " + LightCookieGenerator.Folder + " (made on first use). " +
                    "Replace it with your own art any time."),
                useStarterCookie);
            EditorGUILayout.HelpBox(
                "The box starts at this object and throws the image along the arrow. Everything opaque inside it " +
                "gets the light or shadow (floor, walls, grass, characters).\n" +
                "Window light: put it at the window, rotate it so the arrow points into the room.\n" +
                "Dragon shadow: Straight Down or Sun; set Anchor to the flying object, or add a Shadow Flyover.",
                MessageType.None);
        }

        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();
        if (changed)
            foreach (Object o in targets) ((LightShadowProjector)o).Refresh();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Starter Cookies"))
            {
                LightCookieGenerator.GenerateAll(overwrite: false);
            }
            bool anyFlyover = false;
            foreach (Object o in targets) anyFlyover |= ((Component)o).GetComponent<ShadowFlyover>() != null;
            using (new EditorGUI.DisabledScope(anyFlyover))
            {
                if (GUILayout.Button("Add Shadow Flyover"))
                {
                    foreach (Object o in targets)
                    {
                        var p = (LightShadowProjector)o;
                        if (p.GetComponent<ShadowFlyover>() != null) continue;
                        var fly = Undo.AddComponent<ShadowFlyover>(p.gameObject);
                        PlaceFlyover(fly);
                    }
                }
            }
        }

        if (!HasDepthTexture())
            EditorGUILayout.HelpBox(
                "The active URP asset has Depth Texture off. Game cameras get it switched on automatically while a " +
                "projector is active, but the Scene view won't show projectors until it is on in the URP asset.",
                MessageType.Info);
    }

    private static bool HasDepthTexture()
    {
        var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
            as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        return asset == null || asset.supportsCameraDepthTexture;
    }

    internal static void ApplyPreset(LightShadowProjector p, LightShadowProjector.Preset which, bool assignCookie)
    {
        Undo.RecordObject(p, "Apply Light/Shadow Preset");
        p.ApplyPreset(which);
        if (assignCookie)
        {
            Texture2D cookie = LightCookieGenerator.Get(LightShadowProjector.StarterCookieName(which));
            if (cookie != null)
            {
                p.Sprite = null;
                p.Frames = new Sprite[0];
                p.Texture = cookie;
            }
        }
        EditorUtility.SetDirty(p);
        p.Refresh();
    }

    internal static void PlaceFlyover(ShadowFlyover fly)
    {
        Undo.RecordObject(fly, "Place Shadow Flyover");
        // Offsets from the object: the path moves with it.
        fly.StartPoint = new Vector3(-14f, 0f, -5f);
        fly.EndPoint = new Vector3(14f, 0f, 7f);
        EditorUtility.SetDirty(fly);
    }

    // ---------------- Create menus ----------------

    [MenuItem("GameObject/Rythm RPG/Light or Shadow Projector", false, 32)]
    private static void CreateProjector(MenuCommand command)
    {
        GameObject go = CreateObject("Light Projector", command);
        go.transform.position += Vector3.up * 3f;
        // Slanting down into the room, away from the camera: points at the floor in front of the object.
        go.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
        var p = go.AddComponent<LightShadowProjector>();
        ApplyPreset(p, LightShadowProjector.Preset.WindowLight, true);
        Undo.RegisterCreatedObjectUndo(go, "Create Light Projector");
        Selection.activeGameObject = go;
    }

    [MenuItem("GameObject/Rythm RPG/Dragon Shadow Flyover", false, 33)]
    private static void CreateFlyover(MenuCommand command)
    {
        GameObject go = CreateObject("Dragon Shadow", command);
        var p = go.AddComponent<LightShadowProjector>();
        ApplyPreset(p, LightShadowProjector.Preset.DragonShadow, true);
        var fly = go.AddComponent<ShadowFlyover>();
        PlaceFlyover(fly);
        Undo.RegisterCreatedObjectUndo(go, "Create Dragon Shadow Flyover");
        Selection.activeGameObject = go;
    }

    private static GameObject CreateObject(string name, MenuCommand command)
    {
        var go = new GameObject(name);
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        if (command.context == null && SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            go.transform.position = new Vector3(pivot.x, 0f, pivot.z);
        }
        return go;
    }

    [MenuItem("Tools/Rythm RPG/Rendering/Generate Light Cookies")]
    private static void GenerateCookiesMenu() => LightCookieGenerator.GenerateAll(overwrite: true);
}

/// <summary>Scene handles for the flight path and a scrub slider to preview it in Edit mode.</summary>
[CustomEditor(typeof(ShadowFlyover))]
public sealed class ShadowFlyoverEditor : Editor
{
    private float preview;

    private void OnDisable()
    {
        // Leaving the inspector ends the preview: the shadow goes back to the object.
        if (target is ShadowFlyover fly && fly != null && !Application.isPlaying) fly.EndPreview();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var fly = (ShadowFlyover)target;

        EditorGUILayout.Space();
        if (Application.isPlaying)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play")) fly.Play();
                if (GUILayout.Button("Stop")) fly.Stop();
            }
            return;
        }

        EditorGUILayout.LabelField("Preview (Edit mode)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            preview = EditorGUILayout.Slider("Flight", preview, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                fly.Preview(preview);
                SceneView.RepaintAll();
            }
            using (new EditorGUI.DisabledScope(!fly.IsPreviewing))
            {
                if (GUILayout.Button("Back", GUILayout.Width(50)))
                {
                    fly.EndPreview();
                    SceneView.RepaintAll();
                }
            }
        }
        EditorGUILayout.HelpBox(
            "The object doesn't move: the flight is applied to the shadow only, so everything you set on the " +
            "Light Shadow Projector also applies while it flies. Move the object to move the whole path, or drag " +
            "the Start / End handles in the Scene view.",
            MessageType.None);
    }

    private void OnSceneGUI()
    {
        var fly = (ShadowFlyover)target;
        Vector3 origin = fly.transform.position;
        EditorGUI.BeginChangeCheck();
        Vector3 a = Handles.PositionHandle(fly.WorldStart, Quaternion.identity);
        Vector3 b = Handles.PositionHandle(fly.WorldEnd, Quaternion.identity);
        Handles.Label(fly.WorldStart + Vector3.up * 0.4f, "Start");
        Handles.Label(fly.WorldEnd + Vector3.up * 0.4f, "End");
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(fly, "Move Shadow Flyover Path");
            fly.StartPoint = a - origin;
            fly.EndPoint = b - origin;
            EditorUtility.SetDirty(fly);
            if (fly.IsPreviewing) fly.Preview(preview);
        }
    }
}

/// <summary>
/// Makes small pixel-art cookie textures for the presets (window, blinds, stained glass, leaf dapple, clouds, a
/// winged silhouette, a soft blob) so every preset works before you draw your own.
/// </summary>
public static class LightCookieGenerator
{
    public const string Folder = "Assets/Art/Cookies";

    private static readonly string[] Names =
    {
        "Cookie_Window", "Cookie_Blinds", "Cookie_StainedGlass", "Cookie_LeafDapple", "Cookie_Clouds",
        "Cookie_WingedSilhouette", "Cookie_SoftBlob",
    };

    public static Texture2D Get(string name)
    {
        string path = $"{Folder}/{name}.png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null) return tex;
        return Generate(name, overwrite: true);
    }

    public static void GenerateAll(bool overwrite)
    {
        foreach (string n in Names) Generate(n, overwrite);
        Debug.Log($"[Light Cookies] Starter cookies are in {Folder}.");
    }

    private static Texture2D Generate(string name, bool overwrite)
    {
        string path = $"{Folder}/{name}.png";
        if (!overwrite && File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        Color32[] pixels;
        int w, h;
        bool tiles = false;
        switch (name)
        {
            case "Cookie_Window": pixels = Window(out w, out h); break;
            case "Cookie_Blinds": pixels = Blinds(out w, out h); break;
            case "Cookie_StainedGlass": pixels = StainedGlass(out w, out h); break;
            case "Cookie_LeafDapple": pixels = LeafDapple(out w, out h); tiles = true; break;
            case "Cookie_Clouds": pixels = Clouds(out w, out h); tiles = true; break;
            case "Cookie_WingedSilhouette": pixels = Winged(out w, out h); break;
            case "Cookie_SoftBlob": pixels = Blob(out w, out h); break;
            default: return null;
        }

        Directory.CreateDirectory(Folder);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = tiles ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color32 White(float a) => new(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));

    // Four panes with a cross-shaped frame; transparent frame border.
    private static Color32[] Window(out int w, out int h)
    {
        w = 40; h = 56;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool border = x < 2 || x >= w - 2 || y < 2 || y >= h - 2;
            bool mullion = Mathf.Abs(x - w / 2 + 0.5f) < 1.6f;
            bool transom = Mathf.Abs(y - h / 2 + 0.5f) < 1.6f;
            px[y * w + x] = White(border || mullion || transom ? 0f : 1f);
        }
        return px;
    }

    // Slats: 5 px light, 3 px shadow.
    private static Color32[] Blinds(out int w, out int h)
    {
        w = 48; h = 48;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool edge = x < 1 || x >= w - 1;
            px[y * w + x] = White(!edge && (y % 8) < 5 ? 1f : 0f);
        }
        return px;
    }

    // Arched window with coloured panes and dark leading.
    private static Color32[] StainedGlass(out int w, out int h)
    {
        w = 36; h = 60;
        var px = new Color32[w * h];
        Color32[] palette =
        {
            new(230, 60, 60, 255), new(60, 110, 235, 255), new(250, 200, 60, 255),
            new(70, 190, 100, 255), new(170, 80, 220, 255), new(250, 140, 50, 255),
        };
        float cx = (w - 1) * 0.5f, archY = h - w * 0.5f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool inside = x >= 2 && x < w - 2 && y >= 2;
            if (y > archY)
            {
                float dx = x - cx, dy = y - archY;
                inside &= dx * dx + dy * dy < (cx - 2f) * (cx - 2f);
            }
            int col = x / 12, row = y / 12;
            bool lead = (x % 12) < 1 || (y % 12) < 1;
            Color32 c = palette[(col + row * 2) % palette.Length];
            px[y * w + x] = inside && !lead ? c : new Color32(0, 0, 0, 0);
        }
        return px;
    }

    // Tileable sun flecks through leaves: wrapped random discs.
    private static Color32[] LeafDapple(out int w, out int h)
    {
        w = 64; h = 64;
        var px = new Color32[w * h];
        var rng = new System.Random(11);
        const int count = 26;
        var discs = new Vector3[count];
        for (int i = 0; i < count; i++)
            discs[i] = new Vector3(rng.Next(w), rng.Next(h), 1.5f + (float)rng.NextDouble() * 4f);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float best = 0f;
            foreach (Vector3 d in discs)
            {
                float dx = Mathf.Abs(x - d.x); dx = Mathf.Min(dx, w - dx);
                float dy = Mathf.Abs(y - d.y); dy = Mathf.Min(dy, h - dy);
                float r = Mathf.Sqrt(dx * dx + dy * dy * 1.3f);
                best = Mathf.Max(best, Mathf.Clamp01((d.z - r) / 1.5f));
            }
            px[y * w + x] = White(best);
        }
        return px;
    }

    // Tileable soft cloud blobs (value noise fbm, thresholded).
    private static Color32[] Clouds(out int w, out int h)
    {
        w = 96; h = 96;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float n = 0f, amp = 0.55f;
            int period = 4;
            for (int o = 0; o < 4; o++)
            {
                n += amp * TileNoise(x * period / (float)w, y * period / (float)h, period, 17 + o * 31);
                amp *= 0.5f;
                period *= 2;
            }
            px[y * w + x] = White(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.68f, n)));
        }
        return px;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int hsh = x * 374761393 + y * 668265263 + seed * 144665;
            hsh = (hsh ^ (hsh >> 13)) * 1274126177;
            return ((hsh ^ (hsh >> 16)) & 0xffff) / 65535f;
        }
    }

    private static float TileNoise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int Wrap(int v) => ((v % period) + period) % period;
        float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
        float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // A winged creature seen from below, head at the top of the image.
    private static Color32[] Winged(out int w, out int h)
    {
        w = 64; h = 64;
        var px = new Color32[w * h];
        Vector2[] wing =
        {
            new(0.53f, 0.60f), new(0.64f, 0.70f), new(0.78f, 0.78f), new(0.90f, 0.80f), new(0.98f, 0.72f),
            new(0.93f, 0.66f), new(0.88f, 0.60f), new(0.83f, 0.62f), new(0.79f, 0.53f), new(0.72f, 0.55f),
            new(0.66f, 0.46f), new(0.57f, 0.47f),
        };
        Vector2[] tail =
        {
            new(0.46f, 0.36f), new(0.54f, 0.36f), new(0.53f, 0.20f), new(0.58f, 0.10f), new(0.52f, 0.12f),
            new(0.50f, 0.03f), new(0.48f, 0.12f), new(0.42f, 0.10f), new(0.47f, 0.20f),
        };
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var p = new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
            var mirrored = new Vector2(1f - p.x, p.y);
            bool body = Ellipse(p, new Vector2(0.5f, 0.52f), new Vector2(0.075f, 0.2f));
            bool neck = Ellipse(p, new Vector2(0.5f, 0.72f), new Vector2(0.035f, 0.07f));
            bool head = Ellipse(p, new Vector2(0.5f, 0.8f), new Vector2(0.045f, 0.05f));
            bool snout = Ellipse(p, new Vector2(0.5f, 0.86f), new Vector2(0.025f, 0.04f));
            bool legs = Ellipse(new Vector2(Mathf.Abs(p.x - 0.5f) + 0.5f, p.y), new Vector2(0.575f, 0.4f),
                                new Vector2(0.02f, 0.05f));
            bool wings = Inside(p, wing) || Inside(mirrored, wing);
            px[y * w + x] = White(body || neck || head || snout || legs || wings || Inside(p, tail) ? 1f : 0f);
        }
        return px;
    }

    // Round shadow with a soft edge (posterised by the projector).
    private static Color32[] Blob(out int w, out int h)
    {
        w = 32; h = 32;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            px[y * w + x] = White(1f - Mathf.SmoothStep(0.45f, 0.95f, r));
        }
        return px;
    }

    private static bool Ellipse(Vector2 p, Vector2 c, Vector2 r)
    {
        Vector2 d = new((p.x - c.x) / r.x, (p.y - c.y) / r.y);
        return d.sqrMagnitude <= 1f;
    }

    private static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}
