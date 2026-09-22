using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// A reusable collection of <see cref="TileDefinition"/> assets. Palettes are ScriptableObject
    /// assets so the same palette can be shared across multiple worlds/scenes, per the World Builder's
    /// data architecture requirements. Categories are derived from each tile's own
    /// <see cref="TileDefinition.category"/> string rather than duplicated here, so there is a single
    /// source of truth.
    /// </summary>
    [CreateAssetMenu(menuName = "World Builder/Tile Palette", fileName = "NewTilePalette")]
    public class TilePalette : ScriptableObject
    {
        public string paletteName = "New Palette";
        public List<TileDefinition> tiles = new List<TileDefinition>();

        [Tooltip("Reusable multi-tile arrangements (Phase 2 Stamp tool) that belong with this palette.")]
        public List<TileStamp> stamps = new List<TileStamp>();

        [Tooltip("Auto-tiling rule sets (Phase 2 terrain transitions) that belong with this palette.")]
        public List<TerrainTransitionSet> terrainSets = new List<TerrainTransitionSet>();

        [Tooltip("Placeable props (Phase 3 Object Placement) that belong with this palette.")]
        public List<PropDefinition> props = new List<PropDefinition>();

        private Dictionary<string, TileDefinition> lookup;
        private int lookupBuiltForCount = -1;

        private Dictionary<string, PropDefinition> propLookup;
        private int propLookupBuiltForCount = -1;

        public TileDefinition FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (lookup == null || lookupBuiltForCount != tiles.Count) RebuildLookup();
            return lookup.TryGetValue(id, out TileDefinition tile) ? tile : null;
        }

        public void RebuildLookup()
        {
            lookup = new Dictionary<string, TileDefinition>();
            foreach (TileDefinition tile in tiles)
            {
                if (tile == null) continue;
                lookup[tile.TileId] = tile;
            }

            lookupBuiltForCount = tiles.Count;
        }

        public PropDefinition FindPropById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (propLookup == null || propLookupBuiltForCount != props.Count) RebuildPropLookup();
            return propLookup.TryGetValue(id, out PropDefinition prop) ? prop : null;
        }

        public void RebuildPropLookup()
        {
            propLookup = new Dictionary<string, PropDefinition>();
            foreach (PropDefinition prop in props)
            {
                if (prop == null) continue;
                propLookup[prop.PropId] = prop;
            }

            propLookupBuiltForCount = props.Count;
        }

        public IEnumerable<string> Categories =>
            tiles.Where(t => t != null)
                .Select(t => string.IsNullOrEmpty(t.category) ? "Uncategorized" : t.category)
                .Distinct()
                .OrderBy(c => c);

        public IEnumerable<TileDefinition> TilesInCategory(string category) =>
            tiles.Where(t => t != null &&
                              (string.IsNullOrEmpty(t.category) ? "Uncategorized" : t.category) == category);

        public IEnumerable<string> PropCategories =>
            props.Where(p => p != null)
                .Select(p => string.IsNullOrEmpty(p.category) ? "Uncategorized" : p.category)
                .Distinct()
                .OrderBy(c => c);

        public IEnumerable<PropDefinition> PropsInCategory(string category) =>
            props.Where(p => p != null &&
                              (string.IsNullOrEmpty(p.category) ? "Uncategorized" : p.category) == category);
    }
}
