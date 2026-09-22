using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    public enum TerrainAdjacencyMode
    {
        FourNeighbor,
        EightNeighbor
    }

    /// <summary>
    /// One rule: which tile to show when a terrain-managed cell's same-terrain neighbor bitmask equals
    /// <see cref="neighborMask"/>. Bit order matches <see cref="TerrainTransitionResolver"/>'s direction
    /// tables (4-neighbor: N,E,S,W = bits 0-3; 8-neighbor additionally NE,SE,SW,NW = bits 4-7).
    /// </summary>
    [Serializable]
    public struct TerrainRule
    {
        public int neighborMask;
        public TileDefinition tile;
    }

    /// <summary>
    /// A named auto-tiling rule set: paint with a "terrain" instead of a single raw tile, and every cell
    /// automatically picks the correct edge/corner/interior variant from its neighbors, per the World
    /// Builder's terrain-transition requirements (grass-to-dirt, path connections, water edges, etc.).
    /// Both 4-neighbor and 8-neighbor adjacency are supported; unset mask combinations fall back to
    /// <see cref="fallbackTile"/> so a rule set does not need every combination defined to be usable.
    /// </summary>
    [CreateAssetMenu(menuName = "World Builder/Terrain Transition Set", fileName = "NewTerrainSet")]
    public class TerrainTransitionSet : ScriptableObject
    {
        [SerializeField] private string terrainSetId;

        public string displayName = "Terrain";
        public TerrainAdjacencyMode adjacencyMode = TerrainAdjacencyMode.FourNeighbor;
        [Tooltip("Used whenever no rule matches a cell's neighbor mask (including before any rules are added).")]
        public TileDefinition fallbackTile;
        public List<TerrainRule> rules = new List<TerrainRule>();

        public string TerrainSetId
        {
            get
            {
                if (string.IsNullOrEmpty(terrainSetId)) terrainSetId = Guid.NewGuid().ToString("N");
                return terrainSetId;
            }
        }

        public int MaskBitCount => adjacencyMode == TerrainAdjacencyMode.FourNeighbor ? 4 : 8;
        public int MaskCombinationCount => 1 << MaskBitCount;

        private void Reset()
        {
            if (string.IsNullOrEmpty(terrainSetId)) terrainSetId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(displayName)) displayName = name;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(terrainSetId)) terrainSetId = Guid.NewGuid().ToString("N");
        }

        public TileDefinition Resolve(int neighborMask)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].neighborMask == neighborMask)
                {
                    return rules[i].tile != null ? rules[i].tile : fallbackTile;
                }
            }

            return fallbackTile;
        }
    }
}
