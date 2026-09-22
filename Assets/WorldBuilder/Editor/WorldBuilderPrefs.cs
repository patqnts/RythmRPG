using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    public enum PaintTool
    {
        Brush,
        Rectangle,
        Fill,
        Eraser,
        Eyedropper,
        Line,
        Select,
        Move,
        Stamp
    }

    /// <summary>
    /// Persistent editor-only tool state (current tool, brush size, favorites, recents, overlay
    /// toggles). Stored in EditorPrefs rather than on the World Builder window or the world asset,
    /// since none of this is level data -- it is per-user editing preference and should survive across
    /// window layouts, projects staying open, and different worlds.
    /// </summary>
    public static class WorldBuilderPrefs
    {
        private const string Prefix = "RythmRPG.WorldBuilder.";
        private const int MaxRecents = 24;

        public static PaintTool ActiveTool
        {
            get => (PaintTool)EditorPrefs.GetInt(Prefix + "ActiveTool", (int)PaintTool.Brush);
            set => EditorPrefs.SetInt(Prefix + "ActiveTool", (int)value);
        }

        public static int BrushSize
        {
            get => Mathf.Max(1, EditorPrefs.GetInt(Prefix + "BrushSize", 1));
            set => EditorPrefs.SetInt(Prefix + "BrushSize", Mathf.Max(1, value));
        }

        public static bool ShowGrid
        {
            get => EditorPrefs.GetBool(Prefix + "ShowGrid", true);
            set => EditorPrefs.SetBool(Prefix + "ShowGrid", value);
        }

        public static bool ShowChunkBounds
        {
            get => EditorPrefs.GetBool(Prefix + "ShowChunkBounds", false);
            set => EditorPrefs.SetBool(Prefix + "ShowChunkBounds", value);
        }

        /// <summary>Rotation applied to newly-placed tiles for Brush/Rectangle/Line/Fill/Stamp/Paste.</summary>
        public static int PlacementRotationSteps
        {
            get => ((EditorPrefs.GetInt(Prefix + "PlacementRotation", 0) % 4) + 4) % 4;
            set => EditorPrefs.SetInt(Prefix + "PlacementRotation", ((value % 4) + 4) % 4);
        }

        /// <summary>Flip applied to newly-placed tiles for Brush/Rectangle/Line/Fill/Stamp/Paste.</summary>
        public static TileFlip PlacementFlip
        {
            get => (TileFlip)EditorPrefs.GetInt(Prefix + "PlacementFlip", (int)TileFlip.None);
            set => EditorPrefs.SetInt(Prefix + "PlacementFlip", (int)value);
        }

        public static int FillMaxTiles
        {
            get => Mathf.Max(16, EditorPrefs.GetInt(Prefix + "FillMaxTiles", 4096));
            set => EditorPrefs.SetInt(Prefix + "FillMaxTiles", Mathf.Max(16, value));
        }

        public static bool TerrainModeEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "TerrainModeEnabled", false);
            set => EditorPrefs.SetBool(Prefix + "TerrainModeEnabled", value);
        }

        public static string SelectedTerrainSetGuid
        {
            get => EditorPrefs.GetString(Prefix + "SelectedTerrainSetGuid", string.Empty);
            set => EditorPrefs.SetString(Prefix + "SelectedTerrainSetGuid", value);
        }

        public static string SelectedStampGuid
        {
            get => EditorPrefs.GetString(Prefix + "SelectedStampGuid", string.Empty);
            set => EditorPrefs.SetString(Prefix + "SelectedStampGuid", value);
        }

        /// <summary>The PropDefinition currently selected for placement in the Object Placement tab (Phase 3).</summary>
        public static string SelectedPropGuid
        {
            get => EditorPrefs.GetString(Prefix + "SelectedPropGuid", string.Empty);
            set => EditorPrefs.SetString(Prefix + "SelectedPropGuid", value);
        }

        public static string SearchFilter
        {
            get => EditorPrefs.GetString(Prefix + "SearchFilter", string.Empty);
            set => EditorPrefs.SetString(Prefix + "SearchFilter", value);
        }

        public static string SelectedTileId
        {
            get => EditorPrefs.GetString(Prefix + "SelectedTileId", string.Empty);
            set => EditorPrefs.SetString(Prefix + "SelectedTileId", value);
        }

        public static List<string> GetRecentTileIds()
        {
            string raw = EditorPrefs.GetString(Prefix + "RecentTiles", string.Empty);
            return string.IsNullOrEmpty(raw)
                ? new List<string>()
                : raw.Split('|').Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        public static void PushRecentTile(string tileId)
        {
            if (string.IsNullOrEmpty(tileId)) return;
            List<string> recents = GetRecentTileIds();
            recents.Remove(tileId);
            recents.Insert(0, tileId);
            if (recents.Count > MaxRecents) recents.RemoveRange(MaxRecents, recents.Count - MaxRecents);
            EditorPrefs.SetString(Prefix + "RecentTiles", string.Join("|", recents));
        }

        public static HashSet<string> GetFavoriteTileIds()
        {
            string raw = EditorPrefs.GetString(Prefix + "FavoriteTiles", string.Empty);
            return string.IsNullOrEmpty(raw)
                ? new HashSet<string>()
                : new HashSet<string>(raw.Split('|').Where(s => !string.IsNullOrEmpty(s)));
        }

        public static void ToggleFavorite(string tileId)
        {
            if (string.IsNullOrEmpty(tileId)) return;
            HashSet<string> favorites = GetFavoriteTileIds();
            if (!favorites.Remove(tileId)) favorites.Add(tileId);
            EditorPrefs.SetString(Prefix + "FavoriteTiles", string.Join("|", favorites));
        }
    }
}
