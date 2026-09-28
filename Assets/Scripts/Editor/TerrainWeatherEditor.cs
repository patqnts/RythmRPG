using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector and Scene-view brush for <see cref="TerrainWeather"/> (like the grass brush):
/// <list type="bullet">
/// <item>Paint: left-drag in the Scene view. Erase: Shift + left-drag. Brush size: [ and ].</item>
/// <item>Puffs land on colliders under the brush (Paint On Layers), otherwise on this object's height, then are
/// lifted by the Height range (0 for snow, low for mist, higher for fog).</item>
/// <item>Fill Area: fills a rectangle around this object in one go.</item>
/// </list>
/// </summary>
[CustomEditor(typeof(TerrainWeather))]
public sealed class TerrainWeatherEditor : Editor
{
    private const string PrefsPrefix = "RythmRPG.TerrainWeatherBrush.";

    private static bool painting;
    private static float brushRadius = 2f;
    private static int paintMask = ~(1 << 2);
    private static Vector2 fillSize = new(10f, 8f);

    // Per kind: density (puffs per square unit), spacing, size range, height range.
    // Snow: overlapping mounds that merge into one snow surface. Mist: low smoke hugging the ground. Fog: in the air.
    private static readonly float[] density = { 0.8f, 0.5f, 0.25f };
    private static readonly float[] spacing = { 0.4f, 0.8f, 1.3f };
    private static readonly Vector2[] sizeRange = { new(0.6f, 1.2f), new(0.6f, 1.1f), new(0.9f, 1.6f) };
    private static readonly Vector2[] heightRange = { new(0f, 0f), new(0.1f, 0.3f), new(0.6f, 2.2f) };

    private TerrainWeather field;

    private int K => (int)field.Kind;

    private void OnEnable()
    {
        field = (TerrainWeather)target;
        brushRadius = EditorPrefs.GetFloat(PrefsPrefix + "Radius", brushRadius);
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorPrefs.SetFloat(PrefsPrefix + "Radius", brushRadius);
    }

    private void OnUndoRedo()
    {
        if (field != null) field.MarkDirty();
    }

    private static readonly string[] SnowHidden =
    {
        "m_Script", "kind", "puffShape", "density", "maxOpacity", "groundHug", "softDistance", "lightColor", "shadeColor",
        "blending", "noiseScale", "wispiness", "swirlSpeed", "motes", "moteSpacing", "wobble", "wobbleSpeed", "breathe",
        "driftSpeed", "driftDirection", "driftRange", "windInfluence", "pushStrength", "clearStrength", "recoverSeconds"
    };

    private static readonly string[] AirHidden =
    {
        "m_Script", "kind", "snowDepth", "snowFlatness", "snowBumps", "snowResolution", "snowColor", "snowShadowTint",
        "snowLightBands", "snowSparkle", "snowCastShadows", "trailStyle", "trailWidth", "strideLength", "trailDepth",
        "trailRefillSeconds"
    };

    public override void OnInspectorGUI()
    {
        if (field.Kind != TerrainWeatherKind.Snow && !WeatherTools.FeatureInstalled())
        {
            EditorGUILayout.HelpBox("Mist and fog are cut by the scene depth, so the camera needs a depth texture. " +
                "Install the Weather renderer feature (it asks for it), or turn on Depth Texture in the URP asset.", MessageType.Info);
            if (GUILayout.Button("Install Weather Renderer Feature")) WeatherTools.InstallRendererFeature();
            EditorGUILayout.Space();
        }

        serializedObject.Update();
        SerializedProperty kindProperty = serializedObject.FindProperty("kind");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(kindProperty);
        if (GUILayout.Button(new GUIContent("Apply Look", "Set the colours, opacity, shape and motion that suit this kind."),
                GUILayout.Width(90)))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(field, "Apply Terrain Weather Look");
            field.ApplyKindDefaults(field.Kind);
            EditorUtility.SetDirty(field);
            serializedObject.Update();
        }
        EditorGUILayout.EndHorizontal();
        serializedObject.ApplyModifiedProperties();

        // Only the settings that apply to this kind.
        DrawPropertiesExcluding(serializedObject, field.Kind == TerrainWeatherKind.Snow ? SnowHidden : AirHidden);
        serializedObject.ApplyModifiedProperties();
        if (GUI.changed) field.MarkDirty();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            Color previous = GUI.backgroundColor;
            if (painting) GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
            if (GUILayout.Button(painting ? "Painting (click to stop)" : "Paint " + field.Kind, GUILayout.Height(28)))
            {
                painting = !painting;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = previous;

            int k = K;
            brushRadius = EditorGUILayout.Slider("Radius", brushRadius, 0.2f, 12f);
            density[k] = EditorGUILayout.Slider(new GUIContent("Density", "Puffs per square unit."), density[k], 0.05f, 10f);
            spacing[k] = EditorGUILayout.Slider(new GUIContent("Min Spacing", "No two puffs closer than this."), spacing[k], 0.05f, 4f);
            sizeRange[k] = RangeField("Puff Size", sizeRange[k]);
            heightRange[k] = RangeField(new GUIContent("Height", "Lift above the surface (snow: 0, mist: low, fog: in the air)."), heightRange[k]);
            int shown = UnityEditorInternal.InternalEditorUtility.LayerMaskToConcatenatedLayersMask(paintMask);
            shown = EditorGUILayout.MaskField("Paint On Layers", shown, UnityEditorInternal.InternalEditorUtility.layers);
            paintMask = UnityEditorInternal.InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(shown);
            EditorGUILayout.HelpBox("Scene view: left-drag paints, Shift + left-drag erases, [ and ] resize the brush.",
                MessageType.None);

            EditorGUILayout.Space(4);
            fillSize = EditorGUILayout.Vector2Field(new GUIContent("Fill Area Size", "Rectangle (X, Z) around this object."), fillSize);
            if (GUILayout.Button("Fill Area"))
            {
                Undo.RecordObject(field, "Fill Terrain Weather");
                Vector3 c = field.transform.position;
                int added = Scatter(c, new Vector2(Mathf.Max(0.1f, fillSize.x), Mathf.Max(0.1f, fillSize.y)), true);
                Changed();
                Debug.Log($"[Terrain Weather] Added {added} puffs.");
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Puffs: {field.PuffCount:N0}", EditorStyles.miniBoldLabel);
        if (GUILayout.Button("Clear All") && field.PuffCount > 0
            && EditorUtility.DisplayDialog("Terrain Weather", $"Remove all {field.PuffCount} puffs?", "Remove", "Cancel"))
        {
            Undo.RecordObject(field, "Clear Terrain Weather");
            field.Puffs.Clear();
            Changed();
        }
    }

    private static Vector2 RangeField(string label, Vector2 value) => RangeField(new GUIContent(label), value);

    private static Vector2 RangeField(GUIContent label, Vector2 value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        value.x = EditorGUILayout.FloatField(value.x);
        value.y = EditorGUILayout.FloatField(value.y);
        EditorGUILayout.EndHorizontal();
        if (value.y < value.x) value.y = value.x;
        return value;
    }

    private void Changed()
    {
        field.MarkDirty();
        EditorUtility.SetDirty(field);
        SceneView.RepaintAll();
    }

    // ---------- Scene brush ----------

    private void OnSceneGUI()
    {
        if (!painting || field == null) return;
        Event e = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket))
        {
            brushRadius = Mathf.Clamp(brushRadius * (e.keyCode == KeyCode.LeftBracket ? 0.85f : 1.18f), 0.2f, 12f);
            e.Use();
            Repaint();
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!TrySurface(ray, out Vector3 point)) return;

        bool erase = e.shift;
        Handles.color = erase ? new Color(1f, 0.35f, 0.3f, 0.9f) : new Color(0.6f, 0.85f, 1f, 0.9f);
        Handles.DrawWireDisc(point, Vector3.up, brushRadius);
        Handles.color = new Color(Handles.color.r, Handles.color.g, Handles.color.b, 0.08f);
        Handles.DrawSolidDisc(point, Vector3.up, brushRadius);

        bool press = e.type == EventType.MouseDown && e.button == 0 && !e.alt;
        bool drag = e.type == EventType.MouseDrag && e.button == 0 && !e.alt;
        if (press) Undo.RecordObject(field, erase ? "Erase Terrain Weather" : "Paint Terrain Weather");
        if (press || drag)
        {
            if (erase) field.RemoveInRadius(point, brushRadius);
            else Scatter(point, new Vector2(brushRadius, brushRadius), false);
            Changed();
            e.Use();
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) SceneView.RepaintAll();
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

    // Scatters puffs in a disc (brush dab) or a rectangle (fill), dropped onto the surface below.
    private int Scatter(Vector3 center, Vector2 extent, bool rectangle)
    {
        int k = K;
        float area = rectangle ? extent.x * extent.y : Mathf.PI * extent.x * extent.x;
        // A dab adds a share of the target density so dragging builds up evenly.
        int attempts = Mathf.CeilToInt(area * density[k] * (rectangle ? 1f : 0.35f));
        int added = 0;
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = rectangle
                ? new Vector2((Random.value - 0.5f) * extent.x, (Random.value - 0.5f) * extent.y)
                : Random.insideUnitCircle * extent.x;
            Vector3 p = new(center.x + offset.x, center.y, center.z + offset.y);
            Vector3 top = p + Vector3.up * 50f;
            if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 200f, paintMask, QueryTriggerInteraction.Ignore))
                p = hit.point;
            else
                p.y = field.transform.position.y;
            p.y += Random.Range(heightRange[k].x, heightRange[k].y);
            if (field.HasPuffNear(p, spacing[k])) continue;
            field.AddPuff(p, Random.Range(sizeRange[k].x, sizeRange[k].y));
            added++;
        }
        return added;
    }
}
