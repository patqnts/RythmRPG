using System.Collections.Generic;
using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector and Scene-view brush for <see cref="GrassField"/>:
/// <list type="bullet">
/// <item>Paint: left-drag in the Scene view. Erase: Shift + left-drag. Brush size: [ and ].</item>
/// <item>Blades land on colliders under the brush (Paint Layers), otherwise on the field's height.</item>
/// <item>Import: turns existing sprite grass (e.g. the "Medium Grass" objects) into GPU grass and switches the
/// originals off, all undoable.</item>
/// <item>Play mode: try cutting, burning and shockwaves by clicking in the Scene view.</item>
/// </list>
/// </summary>
[CustomEditor(typeof(GrassField))]
public sealed class GrassFieldEditor : Editor
{
    private const string PrefsPrefix = "RythmRPG.GrassBrush.";

    private static bool painting;
    private static float brushRadius = 1.5f;
    private static float density = 3f;
    private static float minSpacing = 0.28f;
    private static Vector2 scaleRange = new(0.85f, 1.15f);
    private static Vector2 shadeRange = new(0.88f, 1.08f);
    private static bool randomFlip = true;
    private static int paintMask = ~(1 << 2);
    private static Transform importRoot;
    private static bool disableImported = true;

    private enum TestTool { Off, Cut, Ignite, Extinguish, Shockwave, Explosion }
    private static readonly string[] TestToolNames = { "Off", "Cut", "Ignite", "Put Out", "Shockwave", "Explosion" };
    private static TestTool testTool;
    private static float testRadius = 1f;
    private static float testCutHeight;
    private static GrassShockwaveShape testWaveShape;

    private readonly System.Random random = new();
    private GrassField field;

    private void OnEnable()
    {
        field = (GrassField)target;
        brushRadius = EditorPrefs.GetFloat(PrefsPrefix + "Radius", brushRadius);
        density = EditorPrefs.GetFloat(PrefsPrefix + "Density", density);
        minSpacing = EditorPrefs.GetFloat(PrefsPrefix + "Spacing", minSpacing);
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorPrefs.SetFloat(PrefsPrefix + "Radius", brushRadius);
        EditorPrefs.SetFloat(PrefsPrefix + "Density", density);
        EditorPrefs.SetFloat(PrefsPrefix + "Spacing", minSpacing);
    }

    private void OnUndoRedo()
    {
        if (field != null) field.MarkDirty();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            Color previous = GUI.backgroundColor;
            if (painting) GUI.backgroundColor = new Color(0.55f, 1f, 0.55f);
            if (GUILayout.Button(painting ? "Painting (click to stop)" : "Paint Grass", GUILayout.Height(28)))
            {
                painting = !painting;
                if (painting) testTool = TestTool.Off;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = previous;

            brushRadius = EditorGUILayout.Slider("Radius", brushRadius, 0.1f, 10f);
            density = EditorGUILayout.Slider(new GUIContent("Density", "Tufts per square unit."), density, 0.2f, 20f);
            minSpacing = EditorGUILayout.Slider(new GUIContent("Min Spacing", "No two tufts closer than this."), minSpacing, 0.05f, 2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Scale");
            scaleRange.x = EditorGUILayout.FloatField(scaleRange.x);
            scaleRange.y = EditorGUILayout.FloatField(scaleRange.y);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Brightness");
            shadeRange.x = EditorGUILayout.FloatField(shadeRange.x);
            shadeRange.y = EditorGUILayout.FloatField(shadeRange.y);
            EditorGUILayout.EndHorizontal();
            randomFlip = EditorGUILayout.Toggle("Random Flip", randomFlip);
            // MaskField works on the list of named layers, so convert to and from a real layer mask.
            int shown = UnityEditorInternal.InternalEditorUtility.LayerMaskToConcatenatedLayersMask(paintMask);
            shown = EditorGUILayout.MaskField("Paint On Layers", shown, UnityEditorInternal.InternalEditorUtility.layers);
            paintMask = UnityEditorInternal.InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(shown);
            EditorGUILayout.HelpBox("Scene view: left-drag paints, Shift + left-drag erases, [ and ] resize the brush.",
                MessageType.None);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Blades: {field.BladeCount:N0}", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild")) field.Rebuild();
        if (GUILayout.Button("Clear All") && field.BladeCount > 0
            && EditorUtility.DisplayDialog("Grass Field", $"Remove all {field.BladeCount} blades?", "Remove", "Cancel"))
        {
            Undo.RecordObject(field, "Clear Grass");
            field.Blades.Clear();
            Changed();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Try it (Play mode)", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode, pick a tool and click (or drag) in the Scene view to cut, burn " +
                    "or shake the grass. From code: Grass.Cut / Ignite / Extinguish / Shockwave.", MessageType.None);
            }
            else
            {
                TestTool previousTool = testTool;
                testTool = (TestTool)GUILayout.Toolbar((int)testTool, TestToolNames);
                if (testTool != previousTool)
                {
                    painting = false;
                    SceneView.RepaintAll();
                }
                testRadius = EditorGUILayout.Slider("Radius", testRadius, 0.1f, 8f);
                if (testTool == TestTool.Shockwave || testTool == TestTool.Explosion)
                    testWaveShape = (GrassShockwaveShape)EditorGUILayout.EnumPopup(new GUIContent("Wave Shape",
                        "Cone and Line travel the way the Scene view camera looks."), testWaveShape);
                if (testTool == TestTool.Cut || testTool == TestTool.Explosion)
                    testCutHeight = EditorGUILayout.Slider(new GUIContent("Cut Height",
                        "Fraction of the tuft the cut goes through (0 = the field's Stubble Height)."), testCutHeight, 0f, 1f);
                EditorGUILayout.LabelField($"Burning: {field.BurningCount}", EditorStyles.miniLabel);
                if (GUILayout.Button("Restore All Grass")) field.RestoreAll();
                if (field.BurningCount > 0) Repaint();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Import sprite grass", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            importRoot = (Transform)EditorGUILayout.ObjectField(new GUIContent("From Under", "Every SpriteRenderer under this object becomes a blade."),
                importRoot, typeof(Transform), true);
            disableImported = EditorGUILayout.Toggle(new GUIContent("Switch Originals Off", "Deactivate the imported objects (undoable)."), disableImported);
            using (new EditorGUI.DisabledScope(importRoot == null))
                if (GUILayout.Button("Import")) Import(importRoot);
        }
    }

    // ---------- Scene brush ----------

    private void OnSceneGUI()
    {
        if (Application.isPlaying && testTool != TestTool.Off && field != null)
        {
            TestInScene();
            return;
        }
        if (!painting || field == null) return;
        Event e = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket))
        {
            brushRadius = Mathf.Clamp(brushRadius * (e.keyCode == KeyCode.LeftBracket ? 0.85f : 1.18f), 0.1f, 10f);
            e.Use();
            Repaint();
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!TrySurface(ray, out Vector3 point)) return;

        bool erase = e.shift;
        Handles.color = erase ? new Color(1f, 0.35f, 0.3f, 0.9f) : new Color(0.45f, 1f, 0.45f, 0.9f);
        Handles.DrawWireDisc(point, Vector3.up, brushRadius);
        Handles.color = new Color(Handles.color.r, Handles.color.g, Handles.color.b, 0.08f);
        Handles.DrawSolidDisc(point, Vector3.up, brushRadius);

        bool press = e.type == EventType.MouseDown && e.button == 0 && !e.alt;
        bool drag = e.type == EventType.MouseDrag && e.button == 0 && !e.alt;
        if (press) Undo.RecordObject(field, erase ? "Erase Grass" : "Paint Grass");
        if (press || drag)
        {
            if (erase) field.RemoveInRadius(point, brushRadius);
            else Paint(point);
            Changed();
            e.Use();
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) SceneView.RepaintAll();
    }

    private void TestInScene()
    {
        Event e = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        var plane = new Plane(Vector3.up, new Vector3(0f, field.GroundHeight, 0f));
        Vector3 point;
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, paintMask, QueryTriggerInteraction.Ignore)) point = hit.point;
        else if (plane.Raycast(ray, out float distance)) point = ray.GetPoint(distance);
        else return;

        Handles.color = testTool == TestTool.Cut ? new Color(0.9f, 1f, 0.4f, 0.9f)
            : testTool == TestTool.Ignite ? new Color(1f, 0.5f, 0.1f, 0.9f)
            : testTool == TestTool.Extinguish ? new Color(0.3f, 0.6f, 1f, 0.9f)
            : new Color(0.6f, 0.9f, 1f, 0.9f);
        bool wave = testTool == TestTool.Shockwave || testTool == TestTool.Explosion;
        Handles.DrawWireDisc(point, Vector3.up, wave ? testRadius * 4f : testRadius);

        bool press = e.type == EventType.MouseDown && e.button == 0 && !e.alt;
        bool drag = e.type == EventType.MouseDrag && e.button == 0 && !e.alt;
        if (press || (drag && !wave))
        {
            switch (testTool)
            {
                case TestTool.Cut:
                    Grass.Cut(point, testRadius, testCutHeight > 0f ? GrassCutHeight.AtFraction(testCutHeight) : GrassCutHeight.FieldDefault);
                    break;
                case TestTool.Ignite: Grass.Ignite(point, testRadius); break;
                case TestTool.Extinguish: Grass.Extinguish(point, testRadius); break;
                case TestTool.Shockwave:
                    Grass.Shockwave(TestWave(point));
                    break;
                case TestTool.Explosion:
                {
                    GrassShockwave blast = TestWave(point);
                    blast.speed = 16f;
                    blast.strength = 2f;
                    blast.effects = GrassShockwaveEffect.Cut | GrassShockwaveEffect.Ignite;
                    blast.effectDistance = testRadius * 1.5f;
                    blast.cutHeight = testCutHeight > 0f ? GrassCutHeight.AtFraction(testCutHeight) : GrassCutHeight.FieldDefault;
                    Grass.Shockwave(blast);
                    break;
                }
            }
            e.Use();
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) SceneView.RepaintAll();
    }

    private static GrassShockwave TestWave(Vector3 point)
    {
        SceneView view = SceneView.lastActiveSceneView;
        Vector3 forward = view != null && view.camera != null ? view.camera.transform.forward : Vector3.forward;
        GrassShockwave wave = GrassShockwave.Ring(point, testRadius * 4f);
        wave.shape = testWaveShape;
        wave.direction = new Vector3(forward.x, 0f, forward.z);
        wave.angle = 70f;
        wave.lineLength = testRadius * 3f;
        return wave;
    }

    private void Paint(Vector3 center)
    {
        if (!HasUsableVariant())
        {
            Debug.LogWarning("[Grass] Add a sprite to the field's Variants before painting.", field);
            return;
        }
        // A share of the brush area per event, so dragging fills evenly without piling up.
        float area = Mathf.PI * brushRadius * brushRadius;
        int attempts = Mathf.Clamp(Mathf.CeilToInt(area * density * 0.12f), 1, 64);
        for (int i = 0; i < attempts; i++)
        {
            double angle = random.NextDouble() * Mathf.PI * 2.0;
            float distance = brushRadius * Mathf.Sqrt((float)random.NextDouble());
            Vector3 candidate = center + new Vector3(Mathf.Cos((float)angle), 0f, Mathf.Sin((float)angle)) * distance;
            if (!GroundAt(candidate, out candidate)) continue;
            if (field.HasBladeNear(candidate, minSpacing)) continue;
            field.Blades.Add(new GrassField.Blade
            {
                position = candidate,
                scale = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)random.NextDouble()),
                variant = field.PickVariant(random),
                shade = Mathf.Lerp(shadeRange.x, shadeRange.y, (float)random.NextDouble()),
                flip = randomFlip && random.NextDouble() < 0.5
            });
        }
    }

    private bool HasUsableVariant()
    {
        foreach (GrassField.Variant v in field.Variants)
            if (v != null && v.sprite != null) return true;
        return false;
    }

    private bool TrySurface(Ray ray, out Vector3 point)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, paintMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }
        var plane = new Plane(Vector3.up, field.transform.position);
        if (plane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = default;
        return false;
    }

    // Drops a point onto the collider below it (or the field's height).
    private bool GroundAt(Vector3 point, out Vector3 ground)
    {
        Vector3 from = point + Vector3.up * 3f;
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 12f, paintMask, QueryTriggerInteraction.Ignore))
        {
            ground = hit.point;
            return true;
        }
        ground = new Vector3(point.x, field.transform.position.y, point.z);
        return true;
    }

    // ---------- Import ----------

    private void Import(Transform root)
    {
        var renderers = new List<SpriteRenderer>(root.GetComponentsInChildren<SpriteRenderer>(false));
        if (renderers.Count == 0)
        {
            EditorUtility.DisplayDialog("Grass Field", "No active SpriteRenderers under \"" + root.name + "\".", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Import Sprite Grass");
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(field, "Import Sprite Grass");
        int imported = 0;
        foreach (SpriteRenderer sprite in renderers)
        {
            if (sprite.sprite == null) continue;
            field.Blades.Add(new GrassField.Blade
            {
                position = sprite.transform.position,
                scale = Mathf.Abs(sprite.transform.lossyScale.y),
                variant = field.FindOrAddVariant(sprite.sprite),
                shade = 1f,
                flip = sprite.flipX
            });
            imported++;
            if (disableImported && sprite.gameObject != root.gameObject)
            {
                Undo.RecordObject(sprite.gameObject, "Import Sprite Grass");
                sprite.gameObject.SetActive(false);
            }
        }
        Undo.CollapseUndoOperations(group);
        Changed();
        Debug.Log($"[Grass] Imported {imported} sprite tufts into \"{field.name}\".", field);
    }

    private void Changed()
    {
        field.MarkDirty();
        EditorUtility.SetDirty(field);
    }

    // ---------- Menu ----------

    [MenuItem("GameObject/Rythm RPG/Grass Field", false, 30)]
    private static void CreateField(MenuCommand command)
    {
        var go = new GameObject("Grass Field");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        if (command.context == null && SceneView.lastActiveSceneView != null)
        {
            Vector3 pivot = SceneView.lastActiveSceneView.pivot;
            go.transform.position = new Vector3(pivot.x, 0f, pivot.z);
        }
        go.AddComponent<GrassField>();
        Undo.RegisterCreatedObjectUndo(go, "Create Grass Field");
        Selection.activeGameObject = go;
        painting = true;
    }
}
