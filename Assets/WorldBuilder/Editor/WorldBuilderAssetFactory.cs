using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Creation helpers for World Builder assets and scene objects. Kept separate from the window/GUI
    /// code so asset creation can be unit-tested or invoked from other editor tooling later.
    /// </summary>
    public static class WorldBuilderAssetFactory
    {
        private const string DefaultFolder = "Assets/WorldBuilder/Generated";
        private const string StampFolder = "Assets/WorldBuilder/Generated/Stamps";
        private const string TerrainSetFolder = "Assets/WorldBuilder/Generated/TerrainSets";

        public static WorldBuilderWorld CreateWorldInScene(string worldName)
        {
            GameObject go = new GameObject(string.IsNullOrEmpty(worldName) ? "World" : worldName);
            WorldBuilderWorld world = go.AddComponent<WorldBuilderWorld>();
            Undo.RegisterCreatedObjectUndo(go, "Create World Builder World");
            Selection.activeGameObject = go;
            return world;
        }

        public static TilePalette CreatePalette(string suggestedName)
        {
            EnsureFolder(DefaultFolder);
            TilePalette palette = ScriptableObject.CreateInstance<TilePalette>();
            palette.paletteName = string.IsNullOrEmpty(suggestedName) ? "New Palette" : suggestedName;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{DefaultFolder}/{palette.paletteName}.asset");
            AssetDatabase.CreateAsset(palette, path);
            AssetDatabase.SaveAssets();
            return palette;
        }

        public static TileDefinition CreateTileDefinitionFromSprite(Sprite sprite, string folder)
        {
            if (sprite == null) return null;

            EnsureFolder(folder);
            TileDefinition tile = ScriptableObject.CreateInstance<TileDefinition>();
            tile.sprite = sprite;
            tile.displayName = sprite.name;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Tile_{sprite.name}.asset");
            AssetDatabase.CreateAsset(tile, path);
            return tile;
        }

        /// <summary>Same convenience flow as CreateTileDefinitionFromSprite (Phase 3's Object Placement drop zone).</summary>
        public static PropDefinition CreatePropDefinitionFromSprite(Sprite sprite, string folder)
        {
            if (sprite == null) return null;

            EnsureFolder(folder);
            PropDefinition prop = ScriptableObject.CreateInstance<PropDefinition>();
            prop.sprite = sprite;
            prop.displayName = sprite.name;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Prop_{sprite.name}.asset");
            AssetDatabase.CreateAsset(prop, path);
            return prop;
        }

        /// <summary>
        /// Captures the tile cells currently inside a Select-tool selection into a new <see cref="TileStamp"/>
        /// asset, relative to the selection's min corner. Returns null if the selection is empty (no
        /// non-empty cells) so callers don't add an unusable, empty stamp to the palette.
        /// </summary>
        public static TileStamp CreateStampFromSelection(TileLayerData layer, WorldBuilderSelection selection, string suggestedName)
        {
            if (layer == null || !selection.HasSelection) return null;

            System.Collections.Generic.List<StampCell> cells = new System.Collections.Generic.List<StampCell>();
            for (int x = selection.Min.x; x <= selection.Max.x; x++)
            {
                for (int z = selection.Min.z; z <= selection.Max.z; z++)
                {
                    TileCoord coord = new TileCoord(x, z, selection.Min.elevationLevel);
                    if (layer.TryGetTile(coord, out TileCellData cell) && !cell.IsEmpty)
                    {
                        cells.Add(new StampCell { dx = x - selection.Min.x, dz = z - selection.Min.z, cell = cell });
                    }
                }
            }

            if (cells.Count == 0) return null;

            EnsureFolder(StampFolder);
            TileStamp stamp = ScriptableObject.CreateInstance<TileStamp>();
            stamp.displayName = string.IsNullOrEmpty(suggestedName) ? "New Stamp" : suggestedName;
            stamp.width = selection.Width;
            stamp.height = selection.Height;
            stamp.cells = cells;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{StampFolder}/{stamp.displayName}.asset");
            AssetDatabase.CreateAsset(stamp, path);
            AssetDatabase.SaveAssets();
            return stamp;
        }

        public static TerrainTransitionSet CreateTerrainSet(string suggestedName)
        {
            EnsureFolder(TerrainSetFolder);
            TerrainTransitionSet set = ScriptableObject.CreateInstance<TerrainTransitionSet>();
            set.displayName = string.IsNullOrEmpty(suggestedName) ? "New Terrain Set" : suggestedName;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{TerrainSetFolder}/{set.displayName}.asset");
            AssetDatabase.CreateAsset(set, path);
            AssetDatabase.SaveAssets();
            return set;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
