using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds "Grass Fire (Pixel)": a burning-grass particle prefab made for GrassField. It reuses the materials of
/// your MagicArsenal "Grass Flame" (the pixel metaball outline shader), but is shaped for grass fire:
/// small flames that lick up from the base of the tuft instead of big blobs floating above it.
/// <list type="bullet">
/// <item>Body: orange flame tongues, born low, rising and shrinking as they cool to red.</item>
/// <item>Core: a bright yellow-white heart at the base, drawn in front.</item>
/// <item>Smoke: a few dark puffs rising above the fire.</item>
/// <item>Embers: single-pixel sparks drifting up.</item>
/// </list>
/// The layers' emission rates set their ratios when GrassField emits them (Burning Effect Rate = body flames per
/// tuft per second). Tools > Rythm RPG > Grass > Create Pixel Grass Fire Effect, or the button on a Grass Field.
/// </summary>
public static class GrassFireEffectBuilder
{
    private const string ReferencePrefab = "Assets/MagicArsenal/Effects/Prefabs/Flames/Grass Flame.prefab";
    private const string ReferenceFlameMaterialGuid = "5daa80d5c645f8d45b2550329edcdf52"; // pixeldrop03(Modular) 1
    private const string MetaballShader = "PixelMetaballParticles/Pixel Metaball Outlined Particle";
    private const string Folder = "Assets/Prefab/Grass";
    private const string PrefabPath = Folder + "/Grass Fire (Pixel).prefab";

    private enum Layer { Body, Core, Smoke, Embers }

    [MenuItem("Tools/Rythm RPG/Grass/Create Pixel Grass Fire Effect")]
    private static void CreateFromMenu()
    {
        ParticleSystem effect = CreatePrefab();
        if (effect == null) return;
        int assigned = 0;
        foreach (GameObject selected in Selection.gameObjects)
        {
            var field = selected.GetComponent<GrassField>();
            if (field == null) continue;
            Assign(field, effect);
            assigned++;
        }
        Selection.activeObject = effect.gameObject;
        EditorGUIUtility.PingObject(effect.gameObject);
        Debug.Log(assigned > 0
            ? $"[Grass] Created {PrefabPath} and set it as the Burning Effect of {assigned} Grass Field(s)."
            : $"[Grass] Created {PrefabPath}. Drag it into a Grass Field's Burning Effect (or select the field and run this again).");
    }

    /// <summary>Builds (or rebuilds) the prefab and returns its particle system.</summary>
    public static ParticleSystem CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
            !EditorUtility.DisplayDialog("Grass Fire (Pixel)", PrefabPath + " already exists. Rebuild it?", "Rebuild", "Keep"))
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<ParticleSystem>();

        FindMaterials(out Material flame, out Material ember);
        if (flame == null)
        {
            EditorUtility.DisplayDialog("Grass Fire (Pixel)", "Could not find or create a particle material.", "OK");
            return null;
        }

        GameObject root = Build(flame, ember);
        EnsureFolder(Folder);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefab != null ? prefab.GetComponent<ParticleSystem>() : null;
    }

    /// <summary>Sets the effect on a field with settings that suit it (undoable).</summary>
    public static void Assign(GrassField field, ParticleSystem effect)
    {
        var so = new SerializedObject(field);
        so.FindProperty("burningEffect").objectReferenceValue = effect;
        so.FindProperty("burningEffectRate").floatValue = 9f;
        so.FindProperty("burningEffectHeight").floatValue = 0.15f;
        so.FindProperty("burningEffectSpread").floatValue = 0.55f;
        so.FindProperty("burningEffectDepthBias").floatValue = 0.12f;
        so.FindProperty("flameHeight").intValue = 2; // the particles do the tall flames; keep the grass's own low
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(field);
    }

    private static GameObject Build(Material flame, Material ember)
    {
        var root = new GameObject("Grass Fire (Pixel)");
        Configure(root.AddComponent<ParticleSystem>(), Layer.Body, flame);
        AddLayer(root, "Core", Layer.Core, flame);
        AddLayer(root, "Smoke", Layer.Smoke, flame);
        AddLayer(root, "Embers", Layer.Embers, ember != null ? ember : flame);
        return root;
    }

    private static void AddLayer(GameObject root, string name, Layer layer, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        Configure(go.AddComponent<ParticleSystem>(), layer, material);
    }

    private static void Configure(ParticleSystem system, Layer layer, Material material)
    {
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 500;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.rotation = new Vector3(-90f, 0f, 0f); // the cone opens upward

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.damping = true;
        noise.scrollSpeed = 0.8f;

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        switch (layer)
        {
            case Layer.Body:
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
                main.gravityModifier = -0.12f;
                emission.rateOverTime = 24f;
                shape.angle = 12f;
                shape.radius = 0.08f;
                color.color = Gradient(
                    new[] { Key(1f, 0.95f, 0.55f, 0f), Key(1f, 0.62f, 0.12f, 0.3f), Key(0.9f, 0.25f, 0.06f, 0.7f), Key(0.45f, 0.08f, 0.04f, 1f) },
                    new[] { Alpha(1f, 0f), Alpha(1f, 0.75f), Alpha(0f, 1f) });
                size.size = Curve(0f, 0.55f, 0.2f, 1f, 1f, 0.15f);
                noise.strength = 0.25f;
                noise.frequency = 2f;
                renderer.sortingFudge = 0f;
                break;
            case Layer.Core:
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
                main.gravityModifier = -0.08f;
                emission.rateOverTime = 16f;
                shape.angle = 8f;
                shape.radius = 0.05f;
                color.color = Gradient(
                    new[] { Key(1f, 1f, 0.85f, 0f), Key(1f, 0.88f, 0.4f, 0.5f), Key(1f, 0.55f, 0.12f, 1f) },
                    new[] { Alpha(1f, 0f), Alpha(1f, 0.7f), Alpha(0f, 1f) });
                size.size = Curve(0f, 0.8f, 1f, 0.2f);
                noise.strength = 0.15f;
                noise.frequency = 2.5f;
                renderer.sortingFudge = -2f; // drawn over the body
                break;
            case Layer.Smoke:
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
                main.gravityModifier = -0.06f;
                emission.rateOverTime = 3f;
                shape.angle = 18f;
                shape.radius = 0.06f;
                shape.position = new Vector3(0f, 0.15f, 0f); // starts above the flames
                color.color = Gradient(
                    new[] { Key(0.26f, 0.22f, 0.22f, 0f), Key(0.2f, 0.19f, 0.2f, 1f) },
                    new[] { Alpha(0f, 0f), Alpha(0.55f, 0.15f), Alpha(0.35f, 0.6f), Alpha(0f, 1f) });
                size.size = Curve(0f, 0.5f, 0.5f, 1f, 1f, 1.2f);
                noise.strength = 0.3f;
                noise.frequency = 1f;
                renderer.sortingFudge = 2f; // behind the flames
                break;
            default: // Embers
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                main.gravityModifier = -0.03f;
                emission.rateOverTime = 4f;
                shape.angle = 30f;
                shape.radius = 0.1f;
                color.color = Gradient(
                    new[] { Key(1f, 0.9f, 0.5f, 0f), Key(1f, 0.45f, 0.1f, 0.5f), Key(0.6f, 0.12f, 0.05f, 1f) },
                    new[] { Alpha(1f, 0f), Alpha(1f, 0.8f), Alpha(0f, 1f) });
                size.size = Curve(0f, 1f, 1f, 0.3f);
                noise.strength = 0.6f;
                noise.frequency = 2.5f;
                renderer.sortingFudge = -1f;
                break;
        }
    }

    private static GradientColorKey Key(float r, float g, float b, float time) => new GradientColorKey(new Color(r, g, b), time);
    private static GradientAlphaKey Alpha(float alpha, float time) => new GradientAlphaKey(alpha, time);

    private static ParticleSystem.MinMaxGradient Gradient(GradientColorKey[] colors, GradientAlphaKey[] alphas)
    {
        var gradient = new Gradient();
        gradient.SetKeys(colors, alphas);
        return new ParticleSystem.MinMaxGradient(gradient);
    }

    // Keys as (time, value) pairs.
    private static ParticleSystem.MinMaxCurve Curve(params float[] timeValue)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i + 1 < timeValue.Length; i += 2) curve.AddKey(timeValue[i], timeValue[i + 1]);
        return new ParticleSystem.MinMaxCurve(1f, curve);
    }

    // Your Grass Flame's materials (flame + embers), else the pixel flame material by GUID, else a new one.
    private static void FindMaterials(out Material flame, out Material ember)
    {
        flame = null;
        ember = null;
        var reference = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefab);
        if (reference != null)
        {
            foreach (ParticleSystemRenderer renderer in reference.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (renderer.sharedMaterial == null) continue;
                if (renderer.transform == reference.transform) flame = renderer.sharedMaterial;
                else if (ember == null) ember = renderer.sharedMaterial;
            }
        }
        if (flame == null)
        {
            string path = AssetDatabase.GUIDToAssetPath(ReferenceFlameMaterialGuid);
            if (!string.IsNullOrEmpty(path)) flame = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        if (flame == null)
        {
            Shader shader = Shader.Find(MetaballShader);
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return;
            EnsureFolder(Folder);
            flame = new Material(shader) { name = "Grass Fire Pixel" };
            AssetDatabase.CreateAsset(flame, Folder + "/Grass Fire Pixel.mat");
        }
        if (ember == null) ember = flame;
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
