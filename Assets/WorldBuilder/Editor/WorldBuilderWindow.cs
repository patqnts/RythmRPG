using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Tools > World Builder. A centralized workspace organized into the spec'd sections. World
    /// Settings, Terrain & Ground, Tile Palette, Environment Layers (ground-layer management) and
    /// Optimization & Debugging are fully functional in this Phase 1 build. Object Placement, Elevation
    /// & Structures, full Collision & Navigation editing, and the render-accurate World Preview are
    /// clearly marked as upcoming phases rather than being faked with inert buttons.
    /// </summary>
    public sealed class WorldBuilderWindow : EditorWindow
    {
        private enum Section
        {
            WorldSettings,
            TerrainAndGround,
            TilePalette,
            ObjectPlacement,
            EnvironmentLayers,
            ElevationAndStructures,
            CollisionAndNavigation,
            WorldPreview,
            OptimizationAndDebugging
        }

        private static readonly string[] SectionLabels =
        {
            "World Settings", "Terrain & Ground", "Tile Palette", "Object Placement",
            "Environment Layers", "Elevation & Structures", "Collision & Navigation",
            "World Preview", "Optimization & Debugging"
        };

        [SerializeField] private WorldBuilderWorld activeWorld;

        private readonly WorldBuilderSceneTool sceneTool = new WorldBuilderSceneTool();
        private readonly WorldBuilderPropTool propTool = new WorldBuilderPropTool();
        private readonly WorldBuilderPaletteView paletteView = new WorldBuilderPaletteView();
        private Section currentSection = Section.TerrainAndGround;
        private Vector2 bodyScroll;
        private SerializedObject serializedWorld;

        private bool stampsFoldout = true;
        private bool terrainSetsFoldout;
        private string stampNameField = "New Stamp";
        private string terrainSetNameField = "New Terrain Set";
        private readonly Dictionary<string, bool> terrainSetExpanded = new Dictionary<string, bool>();
        private bool lastFillCapped;

        [MenuItem("Tools/World Builder", false, 50)]
        public static void OpenWindow()
        {
            WorldBuilderWindow window = GetWindow<WorldBuilderWindow>();
            window.titleContent = new GUIContent("World Builder");
            window.minSize = new Vector2(420f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (activeWorld == null)
            {
                activeWorld = FindAnyObjectByType<WorldBuilderWorld>();
            }

            RefreshSerializedWorld();
            paletteView.RestoreSelection(activeWorld != null ? activeWorld.palette : null);
            sceneTool.OnTileSampled = _ => paletteView.RestoreSelection(activeWorld != null ? activeWorld.palette : null);
            sceneTool.OnFillCapped = capped =>
            {
                lastFillCapped = capped;
                Repaint();
            };
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            sceneTool.ToolEnabled = false;
            propTool.ToolEnabled = false;
        }

        private void RefreshSerializedWorld()
        {
            serializedWorld = activeWorld != null ? new SerializedObject(activeWorld) : null;
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (activeWorld == null)
            {
                DrawNoWorldState();
                return;
            }

            if (serializedWorld == null || serializedWorld.targetObject != activeWorld)
            {
                RefreshSerializedWorld();
            }

            sceneTool.ActiveWorld = activeWorld;
            sceneTool.SelectedTile = paletteView.SelectedTile;
            propTool.ActiveWorld = activeWorld;
            propTool.SelectedProp = activeWorld.palette != null ? activeWorld.palette.FindPropById(WorldBuilderPrefs.SelectedPropGuid) : null;

            bodyScroll = EditorGUILayout.BeginScrollView(bodyScroll);
            DrawSectionTabs();
            EditorGUILayout.Space(4f);

            switch (currentSection)
            {
                case Section.WorldSettings: DrawWorldSettings(); break;
                case Section.TerrainAndGround: DrawTerrainAndGround(); break;
                case Section.TilePalette: DrawTilePalette(); break;
                case Section.ObjectPlacement: DrawObjectPlacement(); break;
                case Section.EnvironmentLayers: DrawEnvironmentLayers(); break;
                case Section.ElevationAndStructures: DrawElevationAndStructures(); break;
                case Section.CollisionAndNavigation: DrawCollisionAndNavigation(); break;
                case Section.WorldPreview: DrawWorldPreview(); break;
                case Section.OptimizationAndDebugging: DrawOptimizationAndDebugging(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            WorldBuilderWorld nextWorld = (WorldBuilderWorld)EditorGUILayout.ObjectField(activeWorld, typeof(WorldBuilderWorld), true, GUILayout.MinWidth(160f));
            if (EditorGUI.EndChangeCheck())
            {
                activeWorld = nextWorld;
                RefreshSerializedWorld();
                paletteView.RestoreSelection(activeWorld != null ? activeWorld.palette : null);
            }

            if (GUILayout.Button("New World", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                activeWorld = WorldBuilderAssetFactory.CreateWorldInScene("World");
                RefreshSerializedWorld();
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(activeWorld == null))
            {
                if (GUILayout.Button("Rebuild All", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                {
                    activeWorld.RebuildAllChunks();
                    activeWorld.RebuildAllProps();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSectionTabs()
        {
            Section[] values = (Section[])Enum.GetValues(typeof(Section));
            int currentIndex = Array.IndexOf(values, currentSection);
            int nextIndex = GUILayout.SelectionGrid(currentIndex, SectionLabels, 3, EditorStyles.miniButton);
            if (nextIndex != currentIndex) currentSection = values[nextIndex];
        }

        private void DrawNoWorldState()
        {
            EditorGUILayout.Space(30f);
            EditorGUILayout.HelpBox(
                "No World Builder World selected. Create a new world or select an existing WorldBuilderWorld GameObject in the scene.",
                MessageType.Info);

            if (GUILayout.Button("Create New World", GUILayout.Height(28f)))
            {
                activeWorld = WorldBuilderAssetFactory.CreateWorldInScene("World");
                RefreshSerializedWorld();
            }
        }

        private void DrawWorldSettings()
        {
            serializedWorld.Update();
            SerializedProperty settingsProp = serializedWorld.FindProperty("settings");

            EditorGUILayout.LabelField("World Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("referenceCamera"));

            if (GUILayout.Button("Sync Camera Tilt From Reference Camera"))
            {
                Camera reference = activeWorld.settings.referenceCamera;
                if (reference != null)
                {
                    float x = reference.transform.eulerAngles.x;
                    if (x > 180f) x -= 360f;
                    settingsProp.FindPropertyRelative("cameraTiltDegrees").floatValue = Mathf.Abs(x);
                }
            }

            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("cameraTiltDegrees"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("tilePixelSize"));
            if (activeWorld.settings.tilePixelSize == TilePixelSize.Custom)
            {
                EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("customTilePixelSize"));
            }

            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("tileWorldSize"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("chunkSizeInTiles"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("elevationIncrement"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Projection Compensation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("projectionCompensationEnabled"));
            EditorGUILayout.HelpBox(
                $"Current default compensation factor: {activeWorld.settings.DefaultCompensationFactor:0.###} " +
                $"(1 / sin({activeWorld.settings.cameraTiltDegrees:0.#} deg)). Individual tiles can opt out in the Tile Palette.",
                MessageType.None);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("generateGroundCollision"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("tileShaderOverride"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.PropertyField(serializedWorld.FindProperty("palette"));

            serializedWorld.ApplyModifiedProperties();
        }

        private void DrawTerrainAndGround()
        {
            EditorGUILayout.LabelField("Terrain and Ground", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            sceneTool.ToolEnabled = EditorGUILayout.ToggleLeft("Enable Ground Painting in Scene View", sceneTool.ToolEnabled);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

            PaintTool tool = WorldBuilderPrefs.ActiveTool;
            EditorGUI.BeginChangeCheck();
            tool = (PaintTool)GUILayout.Toolbar((int)tool,
                new[] { "Brush", "Rectangle", "Fill", "Eraser", "Eyedropper", "Line", "Select", "Move", "Stamp" });
            if (EditorGUI.EndChangeCheck())
            {
                WorldBuilderPrefs.ActiveTool = tool;
                SceneView.RepaintAll();
            }

            int brushSize = WorldBuilderPrefs.BrushSize;
            EditorGUI.BeginChangeCheck();
            brushSize = EditorGUILayout.IntSlider("Brush Size", brushSize, 1, 8);
            if (EditorGUI.EndChangeCheck()) WorldBuilderPrefs.BrushSize = brushSize;

            if (tool == PaintTool.Fill)
            {
                int fillMax = WorldBuilderPrefs.FillMaxTiles;
                EditorGUI.BeginChangeCheck();
                fillMax = EditorGUILayout.IntField("Fill Max Tiles", fillMax);
                if (EditorGUI.EndChangeCheck()) WorldBuilderPrefs.FillMaxTiles = fillMax;

                if (lastFillCapped)
                {
                    EditorGUILayout.HelpBox(
                        $"The last fill hit the {WorldBuilderPrefs.FillMaxTiles}-tile cap and was cut short. Raise Fill Max Tiles to fill a larger connected region.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.Space(6f);
            DrawPlacementTransformControls();

            EditorGUILayout.Space(6f);
            DrawTerrainModeControls();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Active Elevation Level", activeWorld.activeElevationLevel.ToString());
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("-")) activeWorld.activeElevationLevel--;
            if (GUILayout.Button("+")) activeWorld.activeElevationLevel++;
            if (GUILayout.Button("Reset to 0")) activeWorld.activeElevationLevel = 0;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            WorldBuilderPrefs.ShowGrid = EditorGUILayout.ToggleLeft("Show Placement Grid", WorldBuilderPrefs.ShowGrid);
            WorldBuilderPrefs.ShowChunkBounds = EditorGUILayout.ToggleLeft("Show Chunk Boundaries", WorldBuilderPrefs.ShowChunkBounds);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Selected Tile", paletteView.SelectedTile != null ? paletteView.SelectedTile.displayName : "None (pick one in the Tile Palette tab)");

            if (tool == PaintTool.Stamp)
            {
                TileStamp selectedStamp = sceneTool.ResolveSelectedStamp();
                EditorGUILayout.LabelField("Selected Stamp", selectedStamp != null ? selectedStamp.displayName : "None (pick one in the Tile Palette tab)");
            }

            if (!sceneTool.ToolEnabled)
            {
                EditorGUILayout.HelpBox("Ground painting is off. Enable it above, then click or drag in the Scene view.", MessageType.Info);
            }

            if (tool == PaintTool.Select)
            {
                EditorGUILayout.Space(8f);
                DrawSelectionActions();
            }
            else if (tool == PaintTool.Move)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    sceneTool.HasSelection
                        ? "Click and drag inside the highlighted selection in the Scene view to move its tiles."
                        : "Make a selection with the Select tool first, then switch to Move to drag it.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Rectangle, Fill, Line, Select, Move and Stamp are fully functional, alongside Brush, Eraser and " +
                "Eyedropper -- all with Undo support and chunked, multi-material mesh generation. Terrain Painting " +
                "above auto-tiles from neighboring cells using the Terrain Sets you define in the Tile Palette tab.",
                MessageType.None);
        }

        private void DrawPlacementTransformControls()
        {
            EditorGUILayout.LabelField("Placement Transform", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Applies to newly placed tiles for Brush, Rectangle, Line, Fill, Stamp and Paste.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Rotation: {WorldBuilderPrefs.PlacementRotationSteps * 90}°", GUILayout.Width(100f));
            if (GUILayout.Button("Rotate CCW", GUILayout.Width(90f))) WorldBuilderPrefs.PlacementRotationSteps -= 1;
            if (GUILayout.Button("Rotate CW", GUILayout.Width(90f))) WorldBuilderPrefs.PlacementRotationSteps += 1;
            EditorGUILayout.EndHorizontal();

            TileFlip flip = WorldBuilderPrefs.PlacementFlip;
            bool flipH = (flip & TileFlip.Horizontal) != 0;
            bool flipV = (flip & TileFlip.Vertical) != 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool nextFlipH = GUILayout.Toggle(flipH, "Flip H", EditorStyles.miniButton, GUILayout.Width(90f));
            bool nextFlipV = GUILayout.Toggle(flipV, "Flip V", EditorStyles.miniButton, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck())
            {
                TileFlip nextFlip = TileFlip.None;
                if (nextFlipH) nextFlip |= TileFlip.Horizontal;
                if (nextFlipV) nextFlip |= TileFlip.Vertical;
                WorldBuilderPrefs.PlacementFlip = nextFlip;
            }

            if (GUILayout.Button("Reset", GUILayout.Width(60f)))
            {
                WorldBuilderPrefs.PlacementRotationSteps = 0;
                WorldBuilderPrefs.PlacementFlip = TileFlip.None;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTerrainModeControls()
        {
            EditorGUILayout.LabelField("Terrain Painting", EditorStyles.boldLabel);

            bool terrainMode = WorldBuilderPrefs.TerrainModeEnabled;
            EditorGUI.BeginChangeCheck();
            terrainMode = EditorGUILayout.ToggleLeft("Paint As Terrain (auto-tile from neighbors)", terrainMode);
            if (EditorGUI.EndChangeCheck()) WorldBuilderPrefs.TerrainModeEnabled = terrainMode;

            if (!terrainMode) return;

            if (activeWorld.palette == null || activeWorld.palette.terrainSets.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Terrain Transition Sets on this palette yet. Create one in the Tile Palette tab's Terrain Sets section.",
                    MessageType.Info);
                return;
            }

            List<TerrainTransitionSet> sets = activeWorld.palette.terrainSets;
            string[] names = new string[sets.Count];
            for (int i = 0; i < sets.Count; i++) names[i] = sets[i] != null ? sets[i].displayName : "(missing)";

            string currentGuid = WorldBuilderPrefs.SelectedTerrainSetGuid;
            int currentIndex = -1;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sets[i])) == currentGuid)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0 && sets[0] != null)
            {
                currentIndex = 0;
                WorldBuilderPrefs.SelectedTerrainSetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sets[0]));
            }

            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup("Terrain Set", Mathf.Max(0, currentIndex), names);
            if (EditorGUI.EndChangeCheck() && sets[nextIndex] != null)
            {
                WorldBuilderPrefs.SelectedTerrainSetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sets[nextIndex]));
            }
        }

        private void DrawSelectionActions()
        {
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

            if (!sceneTool.HasSelection)
            {
                EditorGUILayout.HelpBox("Drag in the Scene view with the Select tool to make a selection.", MessageType.Info);
                return;
            }

            WorldBuilderSelection sel = sceneTool.Selection;
            EditorGUILayout.LabelField("Size", $"{sel.Width} x {sel.Height} tiles");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy")) sceneTool.CopySelection();
            if (GUILayout.Button("Cut")) sceneTool.CutSelection();
            if (GUILayout.Button("Delete")) sceneTool.DeleteSelection();
            if (GUILayout.Button("Clear Selection")) sceneTool.ClearSelection();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rotate CW")) sceneTool.RotateSelection(1);
            if (GUILayout.Button("Rotate CCW")) sceneTool.RotateSelection(-1);
            if (GUILayout.Button("Flip H")) sceneTool.FlipSelection(true);
            if (GUILayout.Button("Flip V")) sceneTool.FlipSelection(false);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Toggle Manual Override")) sceneTool.ToggleManualOverrideOnSelection();

            using (new EditorGUI.DisabledScope(!sceneTool.HasClipboardContent))
            {
                if (GUILayout.Button("Paste At Cursor")) sceneTool.PasteAtHover();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            stampNameField = EditorGUILayout.TextField(stampNameField);
            if (GUILayout.Button("Save as Stamp", GUILayout.Width(110f)))
            {
                TileStamp created = sceneTool.SaveSelectionAsStamp(stampNameField);
                if (created != null && activeWorld.palette != null)
                {
                    Undo.RecordObject(activeWorld.palette, "Add Stamp");
                    activeWorld.palette.stamps.Add(created);
                    EditorUtility.SetDirty(activeWorld.palette);
                    AssetDatabase.SaveAssets();
                }
                else if (created == null)
                {
                    EditorUtility.DisplayDialog("Save as Stamp", "The selection has no painted tiles to save.", "OK");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTilePalette()
        {
            EditorGUILayout.LabelField("Tile Palette", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            TilePalette next = (TilePalette)EditorGUILayout.ObjectField("Palette", activeWorld.palette, typeof(TilePalette), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(activeWorld, "Assign Tile Palette");
                activeWorld.palette = next;
                EditorUtility.SetDirty(activeWorld);
                paletteView.RestoreSelection(next);
            }

            if (GUILayout.Button("New", GUILayout.Width(50f)))
            {
                TilePalette created = WorldBuilderAssetFactory.CreatePalette("New Palette");
                Undo.RecordObject(activeWorld, "Assign Tile Palette");
                activeWorld.palette = created;
                EditorUtility.SetDirty(activeWorld);
            }

            EditorGUILayout.EndHorizontal();

            if (activeWorld.palette == null)
            {
                EditorGUILayout.HelpBox("Create or assign a Tile Palette to import tiles.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);
            DrawSpriteDropZone();

            EditorGUILayout.Space(4f);
            paletteView.Draw(activeWorld.palette, _ => { });

            EditorGUILayout.Space(10f);
            DrawStampsSection();

            EditorGUILayout.Space(10f);
            DrawTerrainSetsSection();
        }

        private void DrawStampsSection()
        {
            stampsFoldout = EditorGUILayout.Foldout(stampsFoldout, $"Stamps ({activeWorld.palette.stamps.Count})", true);
            if (!stampsFoldout) return;

            EditorGUILayout.HelpBox(
                "Make a selection with the Select tool in Terrain & Ground, then save it here as a reusable multi-tile arrangement.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(!sceneTool.HasSelection))
            {
                EditorGUILayout.BeginHorizontal();
                stampNameField = EditorGUILayout.TextField(stampNameField);
                if (GUILayout.Button("New Stamp From Selection", GUILayout.Width(190f)))
                {
                    TileStamp created = sceneTool.SaveSelectionAsStamp(stampNameField);
                    if (created != null)
                    {
                        Undo.RecordObject(activeWorld.palette, "Add Stamp");
                        activeWorld.palette.stamps.Add(created);
                        EditorUtility.SetDirty(activeWorld.palette);
                        AssetDatabase.SaveAssets();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("New Stamp", "The current selection has no painted tiles to save.", "OK");
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            for (int i = 0; i < activeWorld.palette.stamps.Count; i++)
            {
                TileStamp stamp = activeWorld.palette.stamps[i];
                if (stamp == null) continue;

                string stampGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(stamp));
                bool isSelected = WorldBuilderPrefs.SelectedStampGuid == stampGuid;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"{stamp.displayName} ({stamp.width}x{stamp.height})");

                using (new EditorGUI.DisabledScope(isSelected))
                {
                    if (GUILayout.Button(isSelected ? "Selected" : "Select", GUILayout.Width(70f)))
                    {
                        WorldBuilderPrefs.SelectedStampGuid = stampGuid;
                        WorldBuilderPrefs.ActiveTool = PaintTool.Stamp;
                    }
                }

                if (GUILayout.Button("Remove", GUILayout.Width(60f)))
                {
                    Undo.RecordObject(activeWorld.palette, "Remove Stamp");
                    activeWorld.palette.stamps.RemoveAt(i);
                    EditorUtility.SetDirty(activeWorld.palette);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTerrainSetsSection()
        {
            terrainSetsFoldout = EditorGUILayout.Foldout(terrainSetsFoldout, $"Terrain Sets ({activeWorld.palette.terrainSets.Count})", true);
            if (!terrainSetsFoldout) return;

            EditorGUILayout.HelpBox(
                "A Terrain Set auto-tiles a group of tiles based on same-terrain neighbors. Enable 'Paint As Terrain' in " +
                "Terrain & Ground and pick a set to paint with it.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            terrainSetNameField = EditorGUILayout.TextField(terrainSetNameField);
            if (GUILayout.Button("New Terrain Set", GUILayout.Width(140f)))
            {
                TerrainTransitionSet created = WorldBuilderAssetFactory.CreateTerrainSet(terrainSetNameField);
                Undo.RecordObject(activeWorld.palette, "Add Terrain Set");
                activeWorld.palette.terrainSets.Add(created);
                EditorUtility.SetDirty(activeWorld.palette);
            }

            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < activeWorld.palette.terrainSets.Count; i++)
            {
                TerrainTransitionSet set = activeWorld.palette.terrainSets[i];
                if (set == null) continue;

                string key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set));
                terrainSetExpanded.TryGetValue(key, out bool expanded);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool nextExpanded = EditorGUILayout.Foldout(expanded, set.displayName, true);
                terrainSetExpanded[key] = nextExpanded;
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Remove", GUILayout.Width(60f)))
                {
                    Undo.RecordObject(activeWorld.palette, "Remove Terrain Set");
                    activeWorld.palette.terrainSets.RemoveAt(i);
                    EditorUtility.SetDirty(activeWorld.palette);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndHorizontal();

                if (nextExpanded) DrawTerrainRuleEditor(set);

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawTerrainRuleEditor(TerrainTransitionSet set)
        {
            EditorGUI.BeginChangeCheck();
            string displayName = EditorGUILayout.TextField("Name", set.displayName);
            TerrainAdjacencyMode mode = (TerrainAdjacencyMode)EditorGUILayout.EnumPopup("Adjacency", set.adjacencyMode);
            TileDefinition fallback = (TileDefinition)EditorGUILayout.ObjectField("Fallback Tile", set.fallbackTile, typeof(TileDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(set, "Edit Terrain Set");
                set.displayName = displayName;
                set.adjacencyMode = mode;
                set.fallbackTile = fallback;
                EditorUtility.SetDirty(set);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Rules ({set.rules.Count} of {set.MaskCombinationCount} possible masks defined)");

            for (int i = 0; i < set.rules.Count; i++)
            {
                TerrainRule rule = set.rules[i];

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                int newMask = DrawMaskDiagram(rule.neighborMask, set.adjacencyMode);

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("Mask", newMask.ToString());
                TileDefinition newTile = (TileDefinition)EditorGUILayout.ObjectField(rule.tile, typeof(TileDefinition), false, GUILayout.Width(160f));
                EditorGUILayout.EndVertical();

                if (newMask != rule.neighborMask || newTile != rule.tile)
                {
                    Undo.RecordObject(set, "Edit Terrain Rule");
                    rule.neighborMask = newMask;
                    rule.tile = newTile;
                    set.rules[i] = rule;
                    EditorUtility.SetDirty(set);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", GUILayout.Width(22f)))
                {
                    Undo.RecordObject(set, "Remove Terrain Rule");
                    set.rules.RemoveAt(i);
                    EditorUtility.SetDirty(set);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Rule"))
            {
                Undo.RecordObject(set, "Add Terrain Rule");
                set.rules.Add(new TerrainRule { neighborMask = 0, tile = null });
                EditorUtility.SetDirty(set);
            }

            if (GUILayout.Button("Auto-Generate All Masks"))
            {
                int count = set.MaskCombinationCount;
                bool proceed = count <= 16 || EditorUtility.DisplayDialog(
                    "Auto-Generate All Masks",
                    $"This creates {count} rules -- one per possible neighbor combination for {set.adjacencyMode} adjacency -- " +
                    "skipping masks that already have a rule. Continue?",
                    "Generate", "Cancel");

                if (proceed)
                {
                    Undo.RecordObject(set, "Auto-Generate Terrain Rules");
                    HashSet<int> existingMasks = new HashSet<int>();
                    foreach (TerrainRule r in set.rules) existingMasks.Add(r.neighborMask);

                    for (int mask = 0; mask < count; mask++)
                    {
                        if (!existingMasks.Contains(mask))
                        {
                            set.rules.Add(new TerrainRule { neighborMask = mask, tile = null });
                        }
                    }

                    EditorUtility.SetDirty(set);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// A small N/NE/E/SE/S/SW/W/NW toggle grid representing a terrain rule's neighbor mask, matching
        /// <see cref="TerrainTransitionResolver"/>'s bit order (4-neighbor: N,E,S,W = bits 0-3; 8-neighbor
        /// additionally NE,SE,SW,NW = bits 4-7). Diagonal toggles are disabled in 4-neighbor mode.
        /// </summary>
        private static int DrawMaskDiagram(int mask, TerrainAdjacencyMode mode)
        {
            bool eight = mode == TerrainAdjacencyMode.EightNeighbor;
            const float w = 20f;

            EditorGUILayout.BeginVertical(GUILayout.Width(3f * w));

            EditorGUILayout.BeginHorizontal();
            DrawMaskBitToggle(ref mask, 7, eight, w); // NW
            DrawMaskBitToggle(ref mask, 0, true, w);  // N
            DrawMaskBitToggle(ref mask, 4, eight, w); // NE
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawMaskBitToggle(ref mask, 3, true, w); // W
            GUILayout.Label("•", GUILayout.Width(w));
            DrawMaskBitToggle(ref mask, 1, true, w); // E
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawMaskBitToggle(ref mask, 6, eight, w); // SW
            DrawMaskBitToggle(ref mask, 2, true, w);  // S
            DrawMaskBitToggle(ref mask, 5, eight, w); // SE
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            return mask;
        }

        private static void DrawMaskBitToggle(ref int mask, int bit, bool enabled, float size)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                bool value = enabled && (mask & (1 << bit)) != 0;
                bool next = GUILayout.Toggle(value, GUIContent.none, EditorStyles.miniButton, GUILayout.Width(size), GUILayout.Height(size));
                if (enabled && next != value)
                {
                    if (next) mask |= 1 << bit;
                    else mask &= ~(1 << bit);
                }
            }
        }

        private void DrawSpriteDropZone()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag Sprites (or spritesheet textures) here to add tiles to this palette", EditorStyles.helpBox);

            Event evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                const string folder = "Assets/WorldBuilder/Generated/Tiles";
                bool added = false;

                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is Sprite sprite)
                    {
                        added |= AddTileFromSprite(sprite, folder);
                    }
                    else if (obj is Texture2D texture)
                    {
                        string path = AssetDatabase.GetAssetPath(texture);
                        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        {
                            if (asset is Sprite spriteAsset)
                            {
                                added |= AddTileFromSprite(spriteAsset, folder);
                            }
                        }
                    }
                }

                if (added)
                {
                    AssetDatabase.SaveAssets();
                    EditorUtility.SetDirty(activeWorld.palette);
                }

                evt.Use();
            }
        }

        private bool AddTileFromSprite(Sprite sprite, string folder)
        {
            TileDefinition tile = WorldBuilderAssetFactory.CreateTileDefinitionFromSprite(sprite, folder);
            if (tile == null) return false;

            Undo.RecordObject(activeWorld.palette, "Add Tile");
            activeWorld.palette.tiles.Add(tile);
            return true;
        }

        // ---------------------------------------------------------------
        // Object Placement (Phase 3)
        // ---------------------------------------------------------------

        private void DrawObjectPlacement()
        {
            EditorGUILayout.LabelField("Object Placement", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click in the Scene view to place the selected prop below at the raycast hit point on the " +
                "active elevation plane (the same plane and Active Elevation Level control as Terrain & " +
                "Ground). Placed props are real GameObjects under the world's \"Props\" child -- once " +
                "placed, select one normally and use Unity's own Move/Rotate/Scale tools on it; this tool " +
                "only handles the initial placement.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            propTool.ToolEnabled = EditorGUILayout.ToggleLeft("Enable Object Placement in Scene View", propTool.ToolEnabled);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

            if (sceneTool.ToolEnabled && propTool.ToolEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Ground Painting is also enabled (Terrain & Ground tab) -- a single click will both " +
                    "paint a tile and place a prop. Turn one off if that's not what you want.",
                    MessageType.Warning);
            }

            if (activeWorld.palette == null)
            {
                EditorGUILayout.HelpBox("Create or assign a Tile Palette (Tile Palette tab) to hold props too.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4f);
            DrawPropDropZone();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"Props ({activeWorld.palette.props.Count})", EditorStyles.boldLabel);

            string selectedGuid = WorldBuilderPrefs.SelectedPropGuid;
            foreach (PropDefinition prop in activeWorld.palette.props)
            {
                if (prop == null) continue;

                EditorGUILayout.BeginHorizontal();
                bool isSelected = prop.PropId == selectedGuid;
                bool nextSelected = EditorGUILayout.ToggleLeft(prop.displayName, isSelected, GUILayout.Width(180f));
                if (nextSelected && !isSelected) WorldBuilderPrefs.SelectedPropGuid = prop.PropId;

                EditorGUI.BeginChangeCheck();
                PropOrientationMode nextOrientation = (PropOrientationMode)EditorGUILayout.EnumPopup(prop.defaultOrientation, GUILayout.Width(100f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(prop, "Change Prop Orientation");
                    prop.defaultOrientation = nextOrientation;
                    EditorUtility.SetDirty(prop);
                }

                EditorGUILayout.EndHorizontal();
            }

            if (activeWorld.palette.props.Count == 0)
            {
                EditorGUILayout.HelpBox("Drag a sprite above to create your first prop.", MessageType.Info);
            }

            if (!propTool.ToolEnabled)
            {
                EditorGUILayout.HelpBox("Object placement is off. Enable it above, then click in the Scene view.", MessageType.Info);
            }
            else if (propTool.SelectedProp == null)
            {
                EditorGUILayout.HelpBox("Select a prop above before clicking in the Scene view.", MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Occlusion against characters relies on prop art having hard (non-antialiased) alpha edges: " +
                "props render with an opaque alpha-clip material so Unity's normal depth buffer sorts them " +
                "correctly from any angle, with no manual sorting-order system needed.",
                MessageType.None);
        }

        private void DrawPropDropZone()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag Sprites here to add props to this palette", EditorStyles.helpBox);

            Event evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                const string folder = "Assets/WorldBuilder/Generated/Props";
                bool added = false;

                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is Sprite sprite)
                    {
                        added |= AddPropFromSprite(sprite, folder);
                    }
                    else if (obj is Texture2D texture)
                    {
                        string path = AssetDatabase.GetAssetPath(texture);
                        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        {
                            if (asset is Sprite spriteAsset)
                            {
                                added |= AddPropFromSprite(spriteAsset, folder);
                            }
                        }
                    }
                }

                if (added)
                {
                    AssetDatabase.SaveAssets();
                    EditorUtility.SetDirty(activeWorld.palette);
                }

                evt.Use();
            }
        }

        private bool AddPropFromSprite(Sprite sprite, string folder)
        {
            PropDefinition prop = WorldBuilderAssetFactory.CreatePropDefinitionFromSprite(sprite, folder);
            if (prop == null) return false;

            Undo.RecordObject(activeWorld.palette, "Add Prop");
            activeWorld.palette.props.Add(prop);
            return true;
        }

        private void DrawEnvironmentLayers()
        {
            EditorGUILayout.LabelField("Environment Layers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Ground layers created here are fully functional (visibility, lock, add, clear) and are composited " +
                "together when chunks render. Dedicated Decoration/Water/Structure/Prop/Obstacle/Character layer kinds " +
                "with placement tools of their own arrive in Phase 3-4.",
                MessageType.None);

            for (int i = 0; i < activeWorld.Layers.Count; i++)
            {
                TileLayerData layer = activeWorld.Layers[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                bool isActive = activeWorld.activeLayerIndex == i;
                bool toggled = GUILayout.Toggle(isActive, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(18f));
                if (toggled && !isActive) activeWorld.activeLayerIndex = i;

                layer.layerName = EditorGUILayout.TextField(layer.layerName);
                layer.visible = EditorGUILayout.ToggleLeft("Visible", layer.visible, GUILayout.Width(58f));
                layer.locked = EditorGUILayout.ToggleLeft("Locked", layer.locked, GUILayout.Width(58f));
                EditorGUILayout.LabelField($"{layer.TileCount} tiles", GUILayout.Width(60f));

                using (new EditorGUI.DisabledScope(layer.locked))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(46f)) &&
                        EditorUtility.DisplayDialog("Clear Layer", $"Clear all tiles on '{layer.layerName}'?", "Clear", "Cancel"))
                    {
                        Undo.RecordObject(activeWorld, "Clear Layer");
                        layer.Clear();
                        activeWorld.RebuildAllChunks();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Layer"))
            {
                Undo.RecordObject(activeWorld, "Add Layer");
                activeWorld.AddLayer($"Layer {activeWorld.Layers.Count + 1}");
            }
        }

        private void DrawElevationAndStructures()
        {
            EditorGUILayout.LabelField("Elevation, Walls and Structures", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Elevation is already part of the data model: every painted cell records its elevation level, and the " +
                "'Active Elevation Level' control in Terrain & Ground selects which floor new painting targets and which " +
                "one the ground-plane raycast intersects. Dedicated tools for raised platforms/steps/cliffs/bridges and " +
                "wall/edge painting (with corners, caps, doors and collision presets) are planned for Phase 4.",
                MessageType.None);

            EditorGUILayout.LabelField("Active Elevation Level", activeWorld.activeElevationLevel.ToString());
            EditorGUILayout.LabelField("Elevation Increment (world units)", activeWorld.settings.elevationIncrement.ToString("0.###"));
        }

        private void DrawCollisionAndNavigation()
        {
            EditorGUILayout.LabelField("Collisions and Navigation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Phase 1 generates one combined ground MeshCollider per chunk from tiles whose Tile Definition has " +
                "'Collision Enabled' checked, decoupled from any visual projection compensation -- collision geometry " +
                "always uses the uncompensated logical grid. Per-object collision, walkable-area painting, hazards, " +
                "interaction regions and a dedicated collision visualization mode arrive in Phase 5.",
                MessageType.None);

            serializedWorld.Update();
            EditorGUILayout.PropertyField(
                serializedWorld.FindProperty("settings").FindPropertyRelative("generateGroundCollision"),
                new GUIContent("Generate Ground Collision"));
            serializedWorld.ApplyModifiedProperties();

            if (GUILayout.Button("Rebuild All Chunks"))
            {
                activeWorld.RebuildAllChunks();
            }
        }

        private void DrawWorldPreview()
        {
            EditorGUILayout.LabelField("World Preview", EditorStyles.boldLabel);

            Camera reference = activeWorld.settings.referenceCamera;
            using (new EditorGUI.DisabledScope(reference == null))
            {
                if (GUILayout.Button("Align Scene View to Game Camera Angle"))
                {
                    SceneView view = SceneView.lastActiveSceneView;
                    if (view != null && reference != null)
                    {
                        view.rotation = reference.transform.rotation;
                        view.orthographic = reference.orthographic;
                        if (reference.orthographic) view.size = reference.orthographicSize;
                        view.Repaint();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(activeWorld.palette == null))
            {
                if (GUILayout.Button("Toggle Projection Compensation & Rebuild (compare footprints)"))
                {
                    SerializedObject so = new SerializedObject(activeWorld);
                    SerializedProperty enabledProp = so.FindProperty("settings").FindPropertyRelative("projectionCompensationEnabled");
                    enabledProp.boolValue = !enabledProp.boolValue;
                    so.ApplyModifiedProperties();
                    activeWorld.RebuildAllChunks();
                }
            }

            EditorGUILayout.HelpBox(
                "Full render-to-texture game camera preview, overlay toggles for sorting/collision/elevation and framing " +
                "diagnostics arrive in Phase 5. For now: use the alignment button to match the Scene view to the real " +
                "camera angle, use the toggle button above to compare compensated vs. uncompensated ground footprints, " +
                "and use Unity's own Game view (with the assigned render texture) to check final presentation.",
                MessageType.None);
        }

        private void DrawOptimizationAndDebugging()
        {
            EditorGUILayout.LabelField("Optimization and Debugging", EditorStyles.boldLabel);

            List<string> warnings = WorldBuilderDiagnostics.Evaluate(activeWorld);
            if (warnings.Count == 0)
            {
                EditorGUILayout.HelpBox("No issues detected.", MessageType.Info);
            }
            else
            {
                foreach (string warning in warnings)
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
            }

            EditorGUILayout.Space(6f);
            int totalTiles = 0;
            foreach (TileLayerData layer in activeWorld.Layers) totalTiles += layer.TileCount;
            EditorGUILayout.LabelField("Total Painted Tiles", totalTiles.ToString());

            if (GUILayout.Button("Force Rebuild All Chunks"))
            {
                activeWorld.RebuildAllChunks();
            }
        }

        private void DrawPlaceholder(string title, string phase)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"{title} is not implemented yet. Planned for {phase} of the World Builder roadmap.", MessageType.None);
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            sceneTool.OnSceneGUI(sceneView);
            propTool.OnSceneGUI(sceneView);
        }
    }
}
