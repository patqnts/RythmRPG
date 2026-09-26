using System.Collections.Generic;
using System.IO;
using RythmRPG.Combat;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the slime bubble ability cast effects (inspired by Sam's energy bubble in Eastward). Every blob is a particle
/// with your pixel metaball outline shader ("PixelMetaballParticles/Pixel Metaball Outlined Particle") on the
/// "Pixel Particles" layer, so the Merged Particle Outline renderer feature melts them into one silhouette with one
/// pixel outline - that lumpy, always-moving outline is what sells the slime.
/// <list type="bullet">
/// <item><b>Slime Bubble Wisp</b> (default wisp): a cluster of overlapping blobs + a paler core + a pixel shine,
/// stretched along its motion on a spring (<see cref="SlimeBubble"/>), leaving drips that merge with it.</item>
/// <item><b>Slime Bubble Pop</b> (default pop): the skin bursts (a blob that swells and vanishes) and slime droplets
/// splash out and fall.</item>
/// <item><b>Slime Bubble Splash</b> (default charge release): a smaller splash.</item>
/// </list>
/// Tools > Rythm RPG > Combat > Create Slime Bubble Cast Effects. Creates the textures, material and prefabs in
/// Assets/Prefab/Abilities/Slime Bubble and sets them as the Combat VFX Theme's defaults. The charge is left alone.
/// </summary>
public static class SlimeBubbleVfxBuilder
{
    private const string MetaballShader = "PixelMetaballParticles/Pixel Metaball Outlined Particle";
    private const string Folder = "Assets/Prefab/Abilities/Slime Bubble";
    private const string BlobTexturePath = Folder + "/Slime Blob.png";
    private const string ShineTexturePath = Folder + "/Slime Shine.png";
    private const string MaterialPath = Folder + "/Slime Bubble.mat";
    private const string WispPath = Folder + "/Slime Bubble Wisp.prefab";
    private const string PopPath = Folder + "/Slime Bubble Pop.prefab";
    private const string SplashPath = Folder + "/Slime Bubble Splash.prefab";
    private const string ThemePath = "Assets/Resources/Combat/VFX/CombatVFXTheme.asset";
    private const string ParticleLayerName = "Pixel Particles";
    private const int SortingOrder = 505; // CastVisuals.SortingOrder

    // Sam-like pink energy; the ability's accent colour tints it at runtime (Tint Amount on the components).
    private static readonly Color BodyColor = new(1f, 0.52f, 0.8f, 1f);
    private static readonly Color CoreColor = new(1f, 0.84f, 0.94f, 1f);
    private static readonly Color DripColor = new(0.96f, 0.46f, 0.76f, 1f);

    [MenuItem("Tools/Rythm RPG/Combat/Create Slime Bubble Cast Effects")]
    private static void CreateFromMenu()
    {
        if (Shader.Find(MetaballShader) == null)
        {
            EditorUtility.DisplayDialog("Slime Bubble", $"Shader '{MetaballShader}' not found. The effects need the pixel " +
                "metaball outline shader (Assets/Shader/PixelOutline).", "OK");
            return;
        }

        EnsureFolder(Folder);
        Texture2D blob = WriteTexture(BlobTexturePath, BlobPixels(16), 16, false);
        WriteTexture(ShineTexturePath, ShinePixels(), 6, true);
        Sprite shine = AssetDatabase.LoadAssetAtPath<Sprite>(ShineTexturePath);
        Material material = CreateMaterial(blob);

        int layer = LayerMask.NameToLayer(ParticleLayerName);
        if (layer < 0)
        {
            layer = 9;
            Debug.LogWarning($"[Slime Bubble] No '{ParticleLayerName}' layer; using layer 9. The particles must be on a layer " +
                             "that the Merged Particle Outline renderer feature outlines.");
        }

        GameObject wisp = SavePrefab(BuildWisp(material, shine, layer), WispPath);
        GameObject pop = SavePrefab(BuildBurst("Slime Bubble Pop", material, layer, 1f), PopPath);
        GameObject splash = SavePrefab(BuildBurst("Slime Bubble Splash", material, layer, 0.6f), SplashPath);
        AssetDatabase.SaveAssets();

        string report = AssignToTheme(wisp, pop, splash);
        List<AbilityVFXProfile> overriding = FindOverridingProfiles();
        if (overriding.Count > 0 && EditorUtility.DisplayDialog("Slime Bubble",
                $"{overriding.Count} Ability VFX Profile(s) have their own wisp / pop / charge release prefabs, which win over " +
                "the theme's defaults. Use the slime bubble on them too?", "Use slime bubble", "Leave them"))
        {
            foreach (AbilityVFXProfile profile in overriding) AssignToProfile(profile, wisp, pop, splash);
            report += $" Also set on {overriding.Count} Ability VFX Profile(s).";
        }
        AssetDatabase.SaveAssets();

        Selection.activeObject = wisp;
        EditorGUIUtility.PingObject(wisp);
        Debug.Log($"[Slime Bubble] Created the slime bubble cast effects in {Folder}. {report}");
    }

    // ---------- prefabs ----------

    private static GameObject BuildWisp(Material material, Sprite shineSprite, int layer)
    {
        var root = new GameObject("Slime Bubble Wisp");
        var bodyPivot = new GameObject("Body");
        bodyPivot.transform.SetParent(root.transform, false);

        // Blobs orbiting the centre, growing and shrinking out of phase: the merged outline never sits still.
        ParticleSystem blobs = AddSystem(bodyPivot.transform, "Blobs", material, layer, ParticleSystemSimulationSpace.Local, 0);
        {
            ParticleSystem.MainModule main = blobs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.26f, 0.34f);
            main.startColor = BodyColor;
            main.maxParticles = 40;
            ParticleSystem.EmissionModule emission = blobs.emission;
            emission.rateOverTime = 16f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            ParticleSystem.ShapeModule shape = blobs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.09f;
            shape.radiusThickness = 1f;
            ParticleSystem.SizeOverLifetimeModule size = blobs.sizeOverLifetime;
            size.enabled = true;
            size.size = Curve(0f, 0.55f, 0.25f, 1f, 0.75f, 0.95f, 1f, 0.45f);
            ParticleSystem.NoiseModule noise = blobs.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 1.6f;
            noise.scrollSpeed = 1.2f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            ParticleSystem.LimitVelocityOverLifetimeModule limit = blobs.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.35f;
            limit.dampen = 0.3f;
        }

        // Paler core: a lighter centre, like a lit bubble.
        ParticleSystem core = AddSystem(bodyPivot.transform, "Core", material, layer, ParticleSystemSimulationSpace.Local, 1);
        {
            ParticleSystem.MainModule main = core.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.24f);
            main.startColor = CoreColor;
            main.maxParticles = 12;
            ParticleSystem.EmissionModule emission = core.emission;
            emission.rateOverTime = 7f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            ParticleSystem.ShapeModule shape = core.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.03f;
            shape.position = new Vector3(-0.02f, 0.02f, 0f);
            ParticleSystem.SizeOverLifetimeModule size = core.sizeOverLifetime;
            size.enabled = true;
            size.size = Curve(0f, 0.7f, 0.3f, 1f, 1f, 0.6f);
        }

        // Drips left behind in the world: they stay merged with the bubble for a moment, then fall off.
        ParticleSystem drips = AddSystem(root.transform, "Drips", material, layer, ParticleSystemSimulationSpace.World, -1);
        {
            ParticleSystem.MainModule main = drips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startColor = DripColor;
            main.gravityModifier = 0.35f;
            main.maxParticles = 80;
            ParticleSystem.EmissionModule emission = drips.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 16f;
            ParticleSystem.ShapeModule shape = drips.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;
            ParticleSystem.SizeOverLifetimeModule size = drips.sizeOverLifetime;
            size.enabled = true;
            size.size = Curve(0f, 1f, 1f, 0f);
        }

        // Shine: plain sprite (not on the outline layer), drawn over the bubble.
        var shineObject = new GameObject("Shine");
        shineObject.transform.SetParent(root.transform, false);
        SpriteRenderer shine = shineObject.AddComponent<SpriteRenderer>();
        shine.sprite = shineSprite;
        shine.color = Color.white;
        shine.sortingOrder = SortingOrder + 3;

        SlimeBubble bubble = root.AddComponent<SlimeBubble>();
        var so = new SerializedObject(bubble);
        so.FindProperty("body").objectReferenceValue = bodyPivot.transform;
        so.FindProperty("shine").objectReferenceValue = shine;
        SetArray(so.FindProperty("bubbleSystems"), blobs, core);
        SetArray(so.FindProperty("trailSystems"), drips);
        so.FindProperty("radius").floatValue = 0.25f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // scale 1 = the pop, 0.6 = the charge release splash.
    private static GameObject BuildBurst(string name, Material material, int layer, float scale)
    {
        var root = new GameObject(name);

        // The skin: one blob that swells and is gone in a blink.
        ParticleSystem skin = AddSystem(root.transform, "Skin", material, layer, ParticleSystemSimulationSpace.World, 1);
        {
            ParticleSystem.MainModule main = skin.main;
            main.loop = false;
            main.startLifetime = 0.09f;
            main.startSpeed = 0f;
            main.startSize = 0.5f * scale;
            main.startColor = CoreColor;
            ParticleSystem.EmissionModule emission = skin.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule skinShape = skin.shape;
            skinShape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = skin.sizeOverLifetime;
            size.enabled = true;
            size.size = Curve(0f, 0.8f, 0.6f, 1.35f, 1f, 1.5f);
        }

        // Droplets splashing out in the screen plane and falling.
        ParticleSystem droplets = AddSystem(root.transform, "Droplets", material, layer, ParticleSystemSimulationSpace.World, 0);
        {
            ParticleSystem.MainModule main = droplets.main;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f * scale, 4f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f * Mathf.Sqrt(scale), 0.22f * Mathf.Sqrt(scale));
            main.startColor = BodyColor;
            main.gravityModifier = 1.4f;
            ParticleSystem.EmissionModule emission = droplets.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(12 * scale + 2)) });
            ParticleSystem.ShapeModule shape = droplets.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle; // local XY = the screen (SlimeBurst faces the camera)
            shape.radius = 0.08f * scale;
            shape.radiusThickness = 1f;
            ParticleSystem.SizeOverLifetimeModule size = droplets.sizeOverLifetime;
            size.enabled = true;
            size.size = Curve(0f, 1f, 0.6f, 0.8f, 1f, 0f);
            ParticleSystem.LimitVelocityOverLifetimeModule limit = droplets.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1.2f;
            limit.dampen = 0.12f;
        }

        // Fine specks.
        ParticleSystem specks = AddSystem(root.transform, "Specks", material, layer, ParticleSystemSimulationSpace.World, 2);
        {
            ParticleSystem.MainModule main = specks.main;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale, 2.6f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
            main.startColor = CoreColor;
            main.gravityModifier = 0.5f;
            ParticleSystem.EmissionModule emission = specks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(8 * scale + 2)) });
            ParticleSystem.ShapeModule shape = specks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.05f * scale;
        }

        SlimeBurst burst = root.AddComponent<SlimeBurst>();
        var so = new SerializedObject(burst);
        SetArray(so.FindProperty("systems"), skin, droplets, specks);
        so.FindProperty("lifetime").floatValue = 1.2f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static ParticleSystem AddSystem(Transform parent, string name, Material material, int layer,
        ParticleSystemSimulationSpace space, int orderOffset)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(parent, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = space;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startRotation = 0f;
        main.gravityModifier = 0f;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = SortingOrder + orderOffset;
        renderer.alignment = ParticleSystemRenderSpace.View;
        return system;
    }

    // Keys as (time, value) pairs.
    private static ParticleSystem.MinMaxCurve Curve(params float[] timeValue)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i + 1 < timeValue.Length; i += 2) curve.AddKey(timeValue[i], timeValue[i + 1]);
        return new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private static void SetArray(SerializedProperty array, params Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static GameObject SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---------- theme / profiles ----------

    private static string AssignToTheme(GameObject wisp, GameObject pop, GameObject splash)
    {
        var theme = AssetDatabase.LoadAssetAtPath<CombatVFXTheme>(ThemePath);
        if (theme == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:CombatVFXTheme");
            if (guids.Length > 0) theme = AssetDatabase.LoadAssetAtPath<CombatVFXTheme>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
        if (theme == null) return "No Combat VFX Theme found: assign the prefabs by hand (Default Wisp / Pop / Charge Release Prefab).";
        var so = new SerializedObject(theme);
        Undo.RecordObject(theme, "Use Slime Bubble Cast Effects");
        so.FindProperty("defaultWispPrefab").objectReferenceValue = wisp;
        so.FindProperty("defaultPopPrefab").objectReferenceValue = pop;
        so.FindProperty("defaultChargeReleasePrefab").objectReferenceValue = splash;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(theme);
        return $"Set as the default wisp, pop and charge release of {AssetDatabase.GetAssetPath(theme)}.";
    }

    private static List<AbilityVFXProfile> FindOverridingProfiles()
    {
        var result = new List<AbilityVFXProfile>();
        foreach (string guid in AssetDatabase.FindAssets("t:AbilityVFXProfile"))
        {
            var profile = AssetDatabase.LoadAssetAtPath<AbilityVFXProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (profile == null) continue;
            if (profile.WispPrefab != null || profile.PopPrefab != null || profile.ChargeReleasePrefab != null) result.Add(profile);
        }
        return result;
    }

    private static void AssignToProfile(AbilityVFXProfile profile, GameObject wisp, GameObject pop, GameObject splash)
    {
        var so = new SerializedObject(profile);
        Undo.RecordObject(profile, "Use Slime Bubble Cast Effects");
        so.FindProperty("wispPrefab").objectReferenceValue = wisp;
        so.FindProperty("popPrefab").objectReferenceValue = pop;
        so.FindProperty("chargeReleasePrefab").objectReferenceValue = splash;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(profile);
    }

    // ---------- assets ----------

    private static Material CreateMaterial(Texture2D blob)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find(MetaballShader)) { name = "Slime Bubble" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = Shader.Find(MetaballShader);
        material.SetTexture("_BaseMap", blob);
        material.SetColor("_BaseColor", Color.white);
        // The fill shows exactly where each blob's field counts, so fill and merged outline agree.
        material.SetFloat("_AlphaCutoff", 0.3f);
        material.SetFloat("_MetaballFieldStrength", 1f);
        material.SetFloat("_MetaballFieldPower", 1f);
        material.SetFloat("_MetaballMaskCutoff", 0.001f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Hard-edged pixel disc: one blob = one crisp round shape; overlapping blobs merge under one outline.
    private static Color32[] BlobPixels(int size)
    {
        var pixels = new Color32[size * size];
        float center = size * 0.5f;
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - center, dy = y + 0.5f - center;
            bool inside = dx * dx + dy * dy <= (radius - 0.25f) * (radius - 0.25f);
            pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        }
        return pixels;
    }

    // 6 x 6 pixel shine: a bright slanted streak with a dot, like a bubble's window reflection.
    private static Color32[] ShinePixels()
    {
        string[] rows =
        {
            "..##..",
            ".###..",
            ".##...",
            "##....",
            "#...#.",
            "......",
        };
        var pixels = new Color32[36];
        for (int y = 0; y < 6; y++)
        for (int x = 0; x < 6; x++)
        {
            bool on = rows[5 - y][x] == '#';
            pixels[y * 6 + x] = on ? new Color32(255, 255, 255, 235) : new Color32(255, 255, 255, 0);
        }
        return pixels;
    }

    private static Texture2D WriteTexture(string path, Color32[] pixels, int size, bool sprite)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        if (sprite)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32f; // one texel = one pixel-art pixel
        }
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        string parent = folder.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
    }
}
