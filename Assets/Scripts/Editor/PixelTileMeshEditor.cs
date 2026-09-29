using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view tile painting for <see cref="PixelTileMesh"/>: pick a tile, click faces of the mesh.
/// Shift + click removes the tile from a face, Ctrl + click picks a face's tile, R rotates, F flips.
/// </summary>
[CustomEditor(typeof(PixelTileMesh))]
internal sealed class PixelTileMeshEditor : Editor
{
    private static bool painting;
    private static int tile;
    private static int rotation;
    private static bool flipX;
    private static bool repeat = true;
    private static float faceAngle = 1f;
    private static float zoom = 1.5f;
    private static Vector2 scroll;

    private PixelTileMesh tileMesh;
    private int hoverTriangle = -1;
    private int[] hoverFace = System.Array.Empty<int>();

    private void OnEnable()
    {
        tileMesh = (PixelTileMesh)target;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        if (painting) Tools.hidden = false;
    }

    private void OnUndoRedo()
    {
        if (tileMesh == null) return;
        tileMesh.MarkDirty();
        tileMesh.Rebuild();
        SceneView.RepaintAll();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("tileset"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("tileWorldSize"));
        if (serializedObject.ApplyModifiedProperties()) tileMesh.MarkDirty();

        Mesh source = tileMesh.SourceMesh;
        if (source == null)
        {
            EditorGUILayout.HelpBox("This object's Mesh Filter has no mesh.", MessageType.Warning);
            return;
        }
        if (!source.isReadable)
        {
            EditorGUILayout.HelpBox($"'{source.name}' needs Read/Write enabled to be tiled in builds.", MessageType.Warning);
            if (GUILayout.Button("Enable Read/Write")) EnableReadWrite(source);
        }

        EditorGUILayout.Space(6);
        Color previous = GUI.backgroundColor;
        if (painting) GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
        if (GUILayout.Button(painting ? "Painting Tiles (click to stop)" : "Paint Tiles", GUILayout.Height(30)))
        {
            painting = !painting;
            Tools.hidden = painting;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = previous;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            repeat = EditorGUILayout.Toggle(new GUIContent("Repeat Per Unit", "On: the tile repeats every Tile World Size (pixel-perfect). Off: one tile stretched over the face."), repeat);
            rotation = EditorGUILayout.IntSlider(new GUIContent("Rotation", "Quarter turns (R)."), rotation, 0, 3);
            flipX = EditorGUILayout.Toggle(new GUIContent("Flip", "Mirror the tile (F)."), flipX);
            faceAngle = EditorGUILayout.Slider(new GUIContent("Face Angle", "Neighbouring triangles within this angle count as one face."), faceAngle, 0.1f, 30f);
            EditorGUILayout.HelpBox("Click a face to tile it. Shift + click: remove the tile. Ctrl + click: pick the face's tile.", MessageType.None);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            PixelTilesetGUI.TileSlot("Tile", tileMesh.Tileset, tile, true);
            GUILayout.Label($"{tileMesh.Faces.Count} tiled face(s).", EditorStyles.wordWrappedMiniLabel);
        }
        PixelTilesetGUI.DrawPalette(tileMesh.Tileset, ref tile, ref zoom, ref scroll);

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Remove All Tiles") && EditorUtility.DisplayDialog("Pixel Tile Mesh", "Remove every tile from this mesh?", "Remove", "Cancel"))
        {
            Undo.RecordObject(tileMesh, "Remove Tiles");
            tileMesh.ClearAll();
            tileMesh.Rebuild();
            EditorUtility.SetDirty(tileMesh);
        }
    }

    private static void EnableReadWrite(Mesh mesh)
    {
        string path = AssetDatabase.GetAssetPath(mesh);
        if (AssetImporter.GetAtPath(path) is ModelImporter importer)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        else Debug.LogWarning($"[Pixel Tile Mesh] Could not change the import settings of '{mesh.name}' ({path}).");
    }

    private void OnSceneGUI()
    {
        if (!painting || tileMesh == null) return;
        Event e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.R) { rotation = (rotation + 1) % 4; e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.F) { flipX = !flipX; e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.Escape) { painting = false; Tools.hidden = false; e.Use(); Repaint(); return; }
        }

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            int triangle = tileMesh.RaycastTriangle(ray, out _);
            if (triangle != hoverTriangle)
            {
                hoverTriangle = triangle;
                hoverFace = triangle >= 0 ? tileMesh.FindFace(triangle, faceAngle) : System.Array.Empty<int>();
                SceneView.RepaintAll();
            }
        }

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (hoverTriangle >= 0 && hoverFace.Length > 0)
            {
                if (e.control || e.command)
                {
                    if (tileMesh.TryGetFace(hoverTriangle, out PixelTileMesh.FaceTile face))
                    {
                        tile = face.tile;
                        rotation = face.rotation;
                        flipX = face.flipX;
                        repeat = face.repeat;
                        Repaint();
                    }
                }
                else
                {
                    if (tileMesh.Tileset != null && !tileMesh.SourceMesh.isReadable) EnableReadWrite(tileMesh.SourceMesh);
                    Undo.RecordObject(tileMesh, e.shift ? "Remove Face Tile" : "Paint Face Tile");
                    if (e.shift) tileMesh.ClearFace(hoverFace);
                    else tileMesh.SetFace(hoverFace, tile, rotation, flipX, repeat);
                    tileMesh.Rebuild();
                    EditorUtility.SetDirty(tileMesh);
                }
            }
            if (e.type == EventType.MouseDown) GUIUtility.hotControl = id;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
            e.Use();
        }

        if (e.type == EventType.Repaint && hoverFace.Length > 0) DrawFace();
    }

    private void DrawFace()
    {
        Mesh mesh = tileMesh.SourceMesh;
        if (mesh == null) return;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Handles.matrix = tileMesh.transform.localToWorldMatrix;
        Handles.color = new Color(1f, 0.82f, 0.2f, 0.35f);
        foreach (int t in hoverFace)
        {
            if (t * 3 + 2 >= triangles.Length) continue;
            Vector3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
            Handles.DrawAAConvexPolygon(a, b, c);
        }
        Handles.color = Color.white;
        foreach (int t in hoverFace)
        {
            if (t * 3 + 2 >= triangles.Length) continue;
            Vector3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
            Handles.DrawPolyLine(a, b, c, a);
        }
        Handles.matrix = Matrix4x4.identity;
    }
}
