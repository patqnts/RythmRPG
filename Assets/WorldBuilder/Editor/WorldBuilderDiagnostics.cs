using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Real, working checks surfaced in the Optimization and Debugging section -- not placeholders.
    /// Every warning here points at something the tool actually detected on the active world/palette.
    /// </summary>
    public static class WorldBuilderDiagnostics
    {
        public static List<string> Evaluate(WorldBuilderWorld world)
        {
            List<string> warnings = new List<string>();
            if (world == null) return warnings;

            if (world.palette == null)
            {
                warnings.Add("No Tile Palette assigned. Assign or create one in the Tile Palette section.");
            }
            else
            {
                if (world.palette.tiles.Count == 0)
                {
                    warnings.Add("The assigned Tile Palette has no tiles yet.");
                }

                foreach (TileDefinition tile in world.palette.tiles)
                {
                    if (tile == null) continue;

                    if (tile.sprite == null)
                    {
                        warnings.Add($"Tile '{tile.displayName}' has no sprite assigned.");
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(tile.sprite.texture);
                    TextureImporter importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && importer.filterMode != FilterMode.Point)
                    {
                        warnings.Add($"Texture '{tile.sprite.texture.name}' (used by tile '{tile.displayName}') is not using Point filtering, which will blur pixel art.");
                    }
                }
            }

            if (world.palette != null)
            {
                foreach (TerrainTransitionSet set in world.palette.terrainSets)
                {
                    if (set == null) continue;

                    if (set.fallbackTile == null && set.rules.Count == 0)
                    {
                        warnings.Add($"Terrain Set '{set.displayName}' has no fallback tile and no rules -- painting with it will place empty cells until you add one or the other.");
                        continue;
                    }

                    if (set.fallbackTile == null)
                    {
                        foreach (TerrainRule rule in set.rules)
                        {
                            if (rule.tile == null)
                            {
                                warnings.Add($"Terrain Set '{set.displayName}' has a rule with no tile and no fallback tile to fall back to -- some neighbor combinations will resolve to an empty cell.");
                                break;
                            }
                        }
                    }
                }

                foreach (TileStamp stamp in world.palette.stamps)
                {
                    if (stamp != null && stamp.cells.Count == 0)
                    {
                        warnings.Add($"Stamp '{stamp.displayName}' has no cells and will place nothing.");
                    }
                }
            }

            if (world.settings.referenceCamera == null)
            {
                warnings.Add("No reference camera assigned in World Settings. Camera-aware alignment and future preview tools are disabled until one is set.");
            }

            if (world.settings.tileWorldSize <= 0f)
            {
                warnings.Add("Tile World Size must be greater than zero.");
            }

            if (world.settings.chunkSizeInTiles <= 0)
            {
                warnings.Add("Chunk Size (In Tiles) must be at least 1.");
            }

            return warnings.Distinct().ToList();
        }
    }
}
