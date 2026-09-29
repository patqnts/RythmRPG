using System.IO;
using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared editor bits for pixel tilesets: the clickable tile palette (used by the Pixel Level and Pixel Tile Mesh
/// inspectors), tile previews, and the menus that make a tileset from a texture / add a Pixel Level.
/// </summary>
internal static class PixelTilesetGUI
{
    private static readonly Color SelectedColor = new(1f, 0.82f, 0.2f, 1f);

    /// <summary>Draws the tileset as a grid of tiles; clicking one sets <paramref name="selected"/>.</summary>
    public static bool DrawPalette(PixelTileset tileset, ref int selected, ref float zoom, ref Vector2 scroll, float maxHeight = 340f)
    {
        if (tileset == null || tileset.texture == null || tileset.Count == 0)
        {
            EditorGUILayout.HelpBox("Assign a Pixel Tileset with a texture (Tools > Rythm RPG > Level > Create Tileset From Selected Texture).",
                MessageType.Info);
            return false;
        }

        zoom = EditorGUILayout.Slider("Palette Zoom", zoom, 0.5f, 4f);
        int columns = tileset.Columns, rows = tileset.Rows;
        float cell = Mathf.Max(8f, tileset.tileSize * zoom);
        float width = columns * cell, height = rows * cell;
        bool changed = false;

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(height + 18f, maxHeight)));
        Rect area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f));
            for (int i = 0; i < tileset.Count; i++)
            {
                Rect r = TileRect(area, i, columns, cell);
                GUI.DrawTextureWithTexCoords(r, tileset.texture, tileset.TileUV(i));
            }
            if (tileset.IsValid(selected)) Outline(TileRect(area, selected, columns, cell), SelectedColor, 2f);
        }
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition))
        {
            int column = Mathf.FloorToInt((e.mousePosition.x - area.x) / cell);
            int row = Mathf.FloorToInt((e.mousePosition.y - area.y) / cell);
            int index = row * columns + column;
            if (column >= 0 && column < columns && tileset.IsValid(index))
            {
                selected = index;
                changed = true;
                GUI.changed = true;
            }
            e.Use();
        }
        EditorGUILayout.EndScrollView();
        return changed;
    }

    private static Rect TileRect(Rect area, int index, int columns, float cell) =>
        new(area.x + index % columns * cell, area.y + index / columns * cell, cell, cell);

    /// <summary>A tile preview box with a label; returns true when clicked.</summary>
    public static bool TileSlot(string label, PixelTileset tileset, int tile, bool active, float size = 48f)
    {
        bool clicked = false;
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(size + 12f)))
        {
            Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.14f));
                if (tileset != null && tileset.texture != null && tileset.IsValid(tile))
                    GUI.DrawTextureWithTexCoords(r, tileset.texture, tileset.TileUV(tile));
                if (active) Outline(r, SelectedColor, 2f);
            }
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                clicked = true;
                Event.current.Use();
            }
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(size + 12f));
        }
        return clicked;
    }

    public static void Outline(Rect r, Color color, float width)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, width), color);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - width, r.width, width), color);
        EditorGUI.DrawRect(new Rect(r.x, r.y, width, r.height), color);
        EditorGUI.DrawRect(new Rect(r.xMax - width, r.y, width, r.height), color);
    }

    // ------------------------------------------------------------------ menus

    [MenuItem("Tools/Rythm RPG/Level/Create Tileset From Selected Texture")]
    [MenuItem("Assets/Create/Rythm RPG/Pixel Tileset From Texture", false, 200)]
    private static void CreateTilesetFromTexture()
    {
        var texture = Selection.activeObject as Texture2D;
        if (texture == null)
        {
            EditorUtility.DisplayDialog("Pixel Tileset", "Select a texture (your tileset sprite sheet) in the Project window first.", "OK");
            return;
        }
        string path = AssetDatabase.GetAssetPath(texture);
        string folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
        string baseName = Path.GetFileNameWithoutExtension(path);

        // Pixel-art import: crisp, uncompressed, no mip maps.
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            if (importer.textureType == TextureImporterType.Default) importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        Shader shader = Shader.Find("RythmRPG/Pixel Mesh");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader) { name = baseName + " Tiles" };
        string materialPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName} Tiles.mat");
        AssetDatabase.CreateAsset(material, materialPath);

        var tileset = ScriptableObject.CreateInstance<PixelTileset>();
        tileset.texture = texture;
        tileset.tileSize = texture.width % 32 == 0 && texture.height % 32 == 0 ? 32 : 16;
        tileset.material = material;
        tileset.ApplyTexture(material);
        EditorUtility.SetDirty(material);
        string tilesetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName} Tileset.asset");
        AssetDatabase.CreateAsset(tileset, tilesetPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = tileset;
        EditorGUIUtility.PingObject(tileset);
        Debug.Log($"[Pixel Tileset] Created {tilesetPath} ({tileset.Columns} x {tileset.Rows} tiles of {tileset.tileSize} px) and {materialPath}.");
    }

    [MenuItem("Tools/Rythm RPG/Level/Create Tileset From Selected Texture", true)]
    [MenuItem("Assets/Create/Rythm RPG/Pixel Tileset From Texture", true)]
    private static bool CanCreateTileset() => Selection.activeObject is Texture2D;

    [MenuItem("GameObject/Rythm RPG/Pixel Level", false, 20)]
    private static void CreatePixelLevel(MenuCommand command)
    {
        var go = new GameObject("Pixel Level");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        var level = go.AddComponent<PixelLevel>();
        string[] found = AssetDatabase.FindAssets("t:PixelTileset");
        if (found.Length > 0) level.Tileset = AssetDatabase.LoadAssetAtPath<PixelTileset>(AssetDatabase.GUIDToAssetPath(found[0]));
        Undo.RegisterCreatedObjectUndo(go, "Create Pixel Level");
        Selection.activeObject = go;
    }
}
