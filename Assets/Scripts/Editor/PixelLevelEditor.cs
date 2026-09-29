using System.Collections.Generic;
using RythmRPG.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view painting for <see cref="PixelLevel"/>: floor tiles, raising / lowering cells (walls appear by
/// themselves), ramps, stairs, wall tiles, erase and pick. Brush or rectangle, all undoable.
/// </summary>
[CustomEditor(typeof(PixelLevel))]
internal sealed class PixelLevelEditor : Editor
{
    private enum Tool { Paint, Height, Ramp, Stairs, Wall, Erase, Pick }
    private enum Slot { Floor, Wall, WallTop }

    private static readonly string[] ToolNames = { "Paint", "Height", "Ramp", "Stairs", "Wall", "Erase", "Pick" };
    private static readonly string[] DirectionNames = { "North (+Z)", "East (+X)", "South (-Z)", "West (-X)" };
    private static readonly Color[] ToolColors =
    {
        new(0.4f, 1f, 0.5f), new(1f, 0.8f, 0.3f), new(0.4f, 0.8f, 1f), new(0.6f, 0.6f, 1f),
        new(1f, 0.6f, 0.9f), new(1f, 0.35f, 0.35f), new(1f, 1f, 1f)
    };

    // Editor state (kept between selections).
    private static bool editing;
    private static Tool tool = Tool.Paint;
    private static Slot slot = Slot.Floor;
    private static int brushSize = 1;
    private static bool rectangle;
    private static int paintHeight;
    private static bool setHeight;
    private static int setHeightValue = 1;
    private static int direction;
    private static int floorRotation;
    private static bool wallTopTarget;
    private static int floorTile, wallTile = 1, wallTopTile = -1;
    private static float zoom = 1.5f;
    private static Vector2 scroll;
    private static Vector2Int fillSize = new(12, 8);

    private PixelLevel level;
    private readonly HashSet<Vector2Int> strokeCells = new();
    private bool stroking;
    private Vector2Int rectStart;
    private PixelLevelHit hoverHit;
    private bool hoverValid;

    private void OnEnable()
    {
        level = (PixelLevel)target;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        if (editing) Tools.hidden = false;
    }

    private void OnUndoRedo()
    {
        if (level == null) return;
        level.MarkAllDirty();
        level.RebuildDirty();
        SceneView.RepaintAll();
    }

    // ------------------------------------------------------------------ inspector

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("tileset"));
        DrawPropertiesExcluding(serializedObject, "m_Script", "tileset", "cells");
        serializedObject.ApplyModifiedProperties();

        PixelTileset tileset = level.Tileset;
        EditorGUILayout.Space(6);

        Color previous = GUI.backgroundColor;
        if (editing) GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
        if (GUILayout.Button(editing ? "Editing Level (click to stop)" : "Edit Level", GUILayout.Height(30)))
        {
            editing = !editing;
            Tools.hidden = editing;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = previous;

        tool = (Tool)GUILayout.Toolbar((int)tool, ToolNames, GUILayout.Height(24));
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (tool != Tool.Pick)
            {
                brushSize = EditorGUILayout.IntSlider(new GUIContent("Brush Size", "[ and ] in the Scene view"), brushSize, 1, 16);
                rectangle = EditorGUILayout.Toggle(new GUIContent("Rectangle", "Drag a rectangle instead of brushing."), rectangle);
            }
            paintHeight = EditorGUILayout.IntField(new GUIContent("Paint Height", "Level of new cells painted on empty ground."), paintHeight);

            switch (tool)
            {
                case Tool.Paint:
                    floorRotation = EditorGUILayout.IntSlider(new GUIContent("Floor Rotation", "Quarter turns (R in the Scene view)."), floorRotation, 0, 3);
                    EditorGUILayout.HelpBox("Paints the Floor tile. Empty ground gets new cells at Paint Height. Shift + click erases.", MessageType.None);
                    break;
                case Tool.Height:
                    setHeight = EditorGUILayout.Toggle(new GUIContent("Set To Value", "Off: click raises, Shift + click lowers."), setHeight);
                    if (setHeight) setHeightValue = EditorGUILayout.IntField("Height", setHeightValue);
                    EditorGUILayout.HelpBox(setHeight
                        ? "Sets cells to the height. Ctrl + click picks a cell's height."
                        : "Click raises by one level, Shift + click lowers. Walls appear on the sides by themselves.", MessageType.None);
                    break;
                case Tool.Ramp:
                case Tool.Stairs:
                    direction = EditorGUILayout.Popup(new GUIContent("Goes Up Towards", "Used when the cell has no single higher neighbour (R rotates)."), direction, DirectionNames);
                    EditorGUILayout.HelpBox($"Click a cell to make it a {(tool == Tool.Ramp ? "ramp" : "staircase")} up one level. It faces the " +
                                            "neighbour that is one level higher by itself. Shift + click makes it flat again.", MessageType.None);
                    break;
                case Tool.Wall:
                    wallTopTarget = EditorGUILayout.Toggle(new GUIContent("Paint Wall Top", "Paint the top-row tile instead of the wall tile."), wallTopTarget);
                    EditorGUILayout.HelpBox("Click cells (or their walls) to give their walls the Wall / Wall Top tile.", MessageType.None);
                    break;
                case Tool.Erase:
                    EditorGUILayout.HelpBox("Removes cells.", MessageType.None);
                    break;
                case Tool.Pick:
                    EditorGUILayout.HelpBox("Click a cell to take its tiles, rotation and height.", MessageType.None);
                    break;
            }
        }

        // Tiles
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Tiles", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (PixelTilesetGUI.TileSlot("Floor", tileset, floorTile, slot == Slot.Floor)) slot = Slot.Floor;
            if (PixelTilesetGUI.TileSlot("Wall", tileset, wallTile, slot == Slot.Wall)) slot = Slot.Wall;
            if (PixelTilesetGUI.TileSlot("Wall Top", tileset, wallTopTile, slot == Slot.WallTop)) slot = Slot.WallTop;
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label($"Click a slot, then a tile below.\nActive: {slot}", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("No Wall Top", EditorStyles.miniButton, GUILayout.Width(90))) wallTopTile = -1;
            }
        }
        int selected = slot == Slot.Floor ? floorTile : slot == Slot.Wall ? wallTile : wallTopTile;
        if (PixelTilesetGUI.DrawPalette(tileset, ref selected, ref zoom, ref scroll))
        {
            if (slot == Slot.Floor) floorTile = selected;
            else if (slot == Slot.Wall) wallTile = selected;
            else wallTopTile = selected;
        }

        // Quick actions
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Area", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            fillSize = EditorGUILayout.Vector2IntField(GUIContent.none, fillSize);
            if (GUILayout.Button("Fill Area", GUILayout.Width(80)))
            {
                Undo.RecordObject(level, "Fill Pixel Level");
                for (int x = 0; x < Mathf.Max(1, fillSize.x); x++)
                    for (int z = 0; z < Mathf.Max(1, fillSize.y); z++)
                        level.SetCell(NewCell(x, z, paintHeight));
                level.RebuildDirty();
                EditorUtility.SetDirty(level);
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rebuild"))
            {
                level.MarkAllDirty();
                level.RebuildDirty();
            }
            if (GUILayout.Button("Clear All") && EditorUtility.DisplayDialog("Pixel Level", "Remove every cell?", "Clear", "Cancel"))
            {
                Undo.RecordObject(level, "Clear Pixel Level");
                level.ClearAll();
                level.RebuildDirty();
                EditorUtility.SetDirty(level);
            }
        }
        EditorGUILayout.HelpBox($"{level.CellCount} cells.  Scene view: left-drag = tool, [ ] = brush size, R = rotate, Esc = stop editing.",
            MessageType.None);
    }

    // ------------------------------------------------------------------ scene view

    private void OnSceneGUI()
    {
        if (!editing || level == null) return;
        Event e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.LeftBracket) { brushSize = Mathf.Max(1, brushSize - 1); e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.RightBracket) { brushSize = Mathf.Min(16, brushSize + 1); e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.R)
            {
                if (tool == Tool.Ramp || tool == Tool.Stairs) direction = (direction + 1) % 4;
                else floorRotation = (floorRotation + 1) % 4;
                e.Use();
                Repaint();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                editing = false;
                Tools.hidden = false;
                e.Use();
                Repaint();
                return;
            }
        }

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.Layout)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            hoverValid = level.Raycast(ray, paintHeight, out hoverHit);
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            stroking = true;
            strokeCells.Clear();
            Undo.IncrementCurrentGroup();
            if (hoverValid)
            {
                rectStart = hoverHit.cell;
                if (!rectangle || tool == Tool.Pick) Apply(Footprint(hoverHit.cell), e);
            }
            GUIUtility.hotControl = id;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && stroking)
        {
            if (hoverValid && !rectangle && tool != Tool.Pick) Apply(Footprint(hoverHit.cell), e);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && stroking)
        {
            if (hoverValid && rectangle && tool != Tool.Pick) Apply(RectCells(rectStart, hoverHit.cell), e);
            stroking = false;
            strokeCells.Clear();
            if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
            e.Use();
        }
        else if (e.type == EventType.MouseMove)
        {
            SceneView.RepaintAll();
        }

        if (e.type == EventType.Repaint && hoverValid) DrawPreview(e);
    }

    private IEnumerable<Vector2Int> Footprint(Vector2Int center)
    {
        int size = tool == Tool.Pick ? 1 : brushSize;
        int lo = -(size - 1) / 2;
        for (int dx = 0; dx < size; dx++)
            for (int dz = 0; dz < size; dz++)
                yield return new Vector2Int(center.x + lo + dx, center.y + lo + dz);
    }

    private static IEnumerable<Vector2Int> RectCells(Vector2Int a, Vector2Int b)
    {
        int x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x);
        int z0 = Mathf.Min(a.y, b.y), z1 = Mathf.Max(a.y, b.y);
        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 65536) yield break;
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                yield return new Vector2Int(x, z);
    }

    private PixelCell NewCell(int x, int z, int height) => new()
    {
        x = x,
        z = z,
        height = height,
        floor = floorTile,
        wall = wallTile,
        wallTop = wallTopTile,
        shape = PixelCellShape.Flat,
        rotation = (byte)floorRotation
    };

    private void Apply(IEnumerable<Vector2Int> targets, Event e)
    {
        bool shift = e.shift, ctrl = e.control || e.command;
        Undo.RecordObject(level, "Pixel Level " + tool);
        int baseHeight = hoverHit.exists && level.TryGetCell(hoverHit.cell.x, hoverHit.cell.y, out PixelCell under) ? under.height : paintHeight;
        bool changed = false;

        foreach (Vector2Int c in targets)
        {
            if (!strokeCells.Add(c)) continue; // each cell once per stroke
            bool exists = level.TryGetCell(c.x, c.y, out PixelCell cell);
            switch (tool)
            {
                case Tool.Paint:
                    if (shift) { changed |= level.RemoveCell(c.x, c.y); break; }
                    if (!exists) cell = NewCell(c.x, c.y, baseHeight);
                    cell.floor = floorTile;
                    cell.rotation = (byte)floorRotation;
                    level.SetCell(cell);
                    changed = true;
                    break;

                case Tool.Height:
                    if (ctrl && setHeight && exists)
                    {
                        setHeightValue = cell.height;
                        Repaint();
                        return;
                    }
                    if (!exists) cell = NewCell(c.x, c.y, paintHeight);
                    cell.height = setHeight ? setHeightValue : cell.height + (shift ? -1 : 1);
                    level.SetCell(cell);
                    changed = true;
                    break;

                case Tool.Ramp:
                case Tool.Stairs:
                    if (!exists) break;
                    if (shift) cell.shape = PixelCellShape.Flat;
                    else
                    {
                        cell.shape = tool == Tool.Ramp ? PixelCellShape.Ramp : PixelCellShape.Stairs;
                        cell.direction = (byte)AutoDirection(cell);
                    }
                    level.SetCell(cell);
                    changed = true;
                    break;

                case Tool.Wall:
                    if (!exists) break;
                    if (wallTopTarget) cell.wallTop = wallTopTile;
                    else cell.wall = wallTile;
                    level.SetCell(cell);
                    changed = true;
                    break;

                case Tool.Erase:
                    changed |= level.RemoveCell(c.x, c.y);
                    break;

                case Tool.Pick:
                    if (!exists) break;
                    floorTile = cell.floor;
                    wallTile = cell.wall;
                    wallTopTile = cell.wallTop;
                    floorRotation = cell.rotation;
                    paintHeight = cell.height;
                    setHeightValue = cell.height;
                    Repaint();
                    break;
            }
        }

        if (!changed) return;
        level.RebuildDirty();
        EditorUtility.SetDirty(level);
    }

    // The side whose neighbour is exactly one level higher (if there is just one), else the chosen direction.
    private int AutoDirection(PixelCell cell)
    {
        int found = -1, count = 0;
        for (int side = 0; side < 4; side++)
        {
            Vector2Int o = PixelLevel.SideOffset(side);
            if (level.TryGetCell(cell.x + o.x, cell.z + o.y, out PixelCell n) && n.height == cell.height + 1)
            {
                found = side;
                count++;
            }
        }
        return count == 1 ? found : direction;
    }

    private void DrawPreview(Event e)
    {
        Color color = ToolColors[(int)tool];
        Handles.matrix = level.transform.localToWorldMatrix;
        IEnumerable<Vector2Int> cells = stroking && rectangle && tool != Tool.Pick ? RectCells(rectStart, hoverHit.cell) : Footprint(hoverHit.cell);
        float size = level.CellSize;
        int drawn = 0;
        foreach (Vector2Int c in cells)
        {
            if (++drawn > 1024) break;
            float y = level.TryGetCell(c.x, c.y, out PixelCell cell)
                ? (cell.shape == PixelCellShape.Flat ? cell.height : cell.height + 0.5f) * level.StepHeight
                : paintHeight * level.StepHeight;
            y += 0.01f;
            var corners = new[]
            {
                new Vector3(c.x * size, y, c.y * size),
                new Vector3((c.x + 1) * size, y, c.y * size),
                new Vector3((c.x + 1) * size, y, (c.y + 1) * size),
                new Vector3(c.x * size, y, (c.y + 1) * size)
            };
            Handles.DrawSolidRectangleWithOutline(corners, new Color(color.r, color.g, color.b, 0.18f), color);
        }

        // Ramp / stairs direction arrow on the hovered cell.
        if ((tool == Tool.Ramp || tool == Tool.Stairs) && hoverHit.exists && level.TryGetCell(hoverHit.cell.x, hoverHit.cell.y, out PixelCell hovered))
        {
            int dir = AutoDirection(hovered);
            Vector2Int o = PixelLevel.SideOffset(dir);
            Vector3 center = new((hovered.x + 0.5f) * size, hovered.height * level.StepHeight + 0.02f, (hovered.z + 0.5f) * size);
            Handles.color = color;
            Handles.DrawLine(center - new Vector3(o.x, 0f, o.y) * size * 0.35f, center + new Vector3(o.x, 0f, o.y) * size * 0.35f, 3f);
            Handles.DrawSolidDisc(center + new Vector3(o.x, 0f, o.y) * size * 0.35f, Vector3.up, size * 0.08f);
        }

        Handles.matrix = Matrix4x4.identity;
        string label = tool switch
        {
            Tool.Height => setHeight ? $"Set height {setHeightValue}" : e.shift ? "Lower" : "Raise",
            Tool.Paint => e.shift ? "Erase" : "Paint",
            _ => tool.ToString()
        };
        Handles.Label(level.transform.TransformPoint(hoverHit.localPoint) + Vector3.up * 0.3f,
            $"{label}  ({hoverHit.cell.x}, {hoverHit.cell.y})", EditorStyles.whiteMiniLabel);
    }
}
