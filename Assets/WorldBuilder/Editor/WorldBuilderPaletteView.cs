using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Draws the searchable, thumbnail-based tile palette grid: Favorites and Recently Used pseudo-
    /// categories on top, then every real category from the palette's tiles, each collapsible.
    /// </summary>
    public class WorldBuilderPaletteView
    {
        private const float ThumbnailSize = 48f;

        private Vector2 scroll;
        private readonly Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();

        public TileDefinition SelectedTile { get; private set; }

        public void Draw(TilePalette palette, Action<TileDefinition> onSelect)
        {
            EditorGUILayout.BeginVertical();

            string search = WorldBuilderPrefs.SearchFilter;
            EditorGUI.BeginChangeCheck();
            search = EditorGUILayout.TextField("Search", search);
            if (EditorGUI.EndChangeCheck()) WorldBuilderPrefs.SearchFilter = search;

            if (palette == null)
            {
                EditorGUILayout.HelpBox("Assign a Tile Palette to begin painting.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));

            HashSet<string> favorites = WorldBuilderPrefs.GetFavoriteTileIds();

            List<TileDefinition> favoriteTiles = palette.tiles
                .Where(t => t != null && favorites.Contains(t.TileId) && MatchesSearch(t, search))
                .ToList();
            if (favoriteTiles.Count > 0) DrawCategory("Favorites", favoriteTiles, onSelect, favorites);

            List<TileDefinition> recentTiles = WorldBuilderPrefs.GetRecentTileIds()
                .Select(id => palette.FindById(id))
                .Where(t => t != null && MatchesSearch(t, search))
                .ToList();
            if (recentTiles.Count > 0) DrawCategory("Recently Used", recentTiles, onSelect, favorites);

            foreach (string category in palette.Categories)
            {
                List<TileDefinition> tilesInCategory = palette.TilesInCategory(category)
                    .Where(t => MatchesSearch(t, search))
                    .ToList();
                if (tilesInCategory.Count == 0) continue;
                DrawCategory(category, tilesInCategory, onSelect, favorites);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCategory(string category, List<TileDefinition> tiles, Action<TileDefinition> onSelect, HashSet<string> favorites)
        {
            if (!categoryFoldouts.TryGetValue(category, out bool expanded)) expanded = true;
            expanded = EditorGUILayout.Foldout(expanded, $"{category} ({tiles.Count})", true);
            categoryFoldouts[category] = expanded;
            if (!expanded) return;

            float availableWidth = EditorGUIUtility.currentViewWidth - 40f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / (ThumbnailSize + 6f)));

            int index = 0;
            while (index < tiles.Count)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < columns && index < tiles.Count; c++, index++)
                {
                    DrawTileButton(tiles[index], onSelect, favorites);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTileButton(TileDefinition tile, Action<TileDefinition> onSelect, HashSet<string> favorites)
        {
            bool isSelected = SelectedTile == tile;
            Rect rect = GUILayoutUtility.GetRect(ThumbnailSize, ThumbnailSize, GUILayout.Width(ThumbnailSize), GUILayout.Height(ThumbnailSize));

            Color previousColor = GUI.color;
            if (isSelected) GUI.color = new Color(0.55f, 0.8f, 1f, 1f);

            Texture2D preview = tile.sprite != null ? AssetPreview.GetAssetPreview(tile.sprite) : null;
            if (preview == null && tile.sprite != null) preview = AssetPreview.GetMiniThumbnail(tile.sprite);

            GUIContent content = preview != null ? new GUIContent(preview, tile.displayName) : new GUIContent(tile.displayName);
            if (GUI.Button(rect, content))
            {
                SelectedTile = tile;
                WorldBuilderPrefs.SelectedTileId = tile.TileId;
                WorldBuilderPrefs.PushRecentTile(tile.TileId);
                onSelect?.Invoke(tile);
            }

            GUI.color = previousColor;

            if (favorites.Contains(tile.TileId))
            {
                GUI.Label(new Rect(rect.xMax - 14f, rect.y, 14f, 14f), "★");
            }

            if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            {
                GenericMenu menu = new GenericMenu();
                string label = favorites.Contains(tile.TileId) ? "Remove Favorite" : "Add Favorite";
                menu.AddItem(new GUIContent(label), false, () => WorldBuilderPrefs.ToggleFavorite(tile.TileId));
                menu.ShowAsContext();
                Event.current.Use();
            }
        }

        private static bool MatchesSearch(TileDefinition tile, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            string lower = search.ToLowerInvariant();

            if (!string.IsNullOrEmpty(tile.displayName) && tile.displayName.ToLowerInvariant().Contains(lower)) return true;
            if (!string.IsNullOrEmpty(tile.category) && tile.category.ToLowerInvariant().Contains(lower)) return true;

            if (tile.tags != null)
            {
                foreach (string tag in tile.tags)
                {
                    if (!string.IsNullOrEmpty(tag) && tag.ToLowerInvariant().Contains(lower)) return true;
                }
            }

            return false;
        }

        public void RestoreSelection(TilePalette palette)
        {
            SelectedTile = null;
            if (palette == null) return;

            string id = WorldBuilderPrefs.SelectedTileId;
            if (!string.IsNullOrEmpty(id)) SelectedTile = palette.FindById(id);
        }
    }
}
