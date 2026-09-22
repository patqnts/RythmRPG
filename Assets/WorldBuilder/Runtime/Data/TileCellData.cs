using System;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    [Flags]
    public enum TileFlip
    {
        None = 0,
        Horizontal = 1 << 0,
        Vertical = 1 << 1,
        Both = Horizontal | Vertical
    }

    /// <summary>
    /// What is painted into a single grid cell: which tile definition (by stable id, see
    /// <see cref="TileDefinition.TileId"/>), plus per-instance rotation/flip and whether it was
    /// placed manually (manual placements are not overwritten by future automatic terrain-transition
    /// rules, per the World Builder spec's terrain-transition requirements).
    /// </summary>
    [Serializable]
    public struct TileCellData : IEquatable<TileCellData>
    {
        public string tileId;
        [Range(0, 3)] public int rotationSteps;
        public TileFlip flip;

        /// <summary>
        /// True when this cell was explicitly placed by hand (or explicitly locked, see the Select
        /// tool's "Toggle Manual Override"). Manual cells are skipped by automatic terrain-transition
        /// re-resolution, per the World Builder spec's terrain-transition requirements.
        /// </summary>
        public bool manualOverride;

        /// <summary>
        /// Empty when this cell is a plain, non-automated tile. Non-empty ties this cell to a
        /// <see cref="TerrainTransitionSet"/> (by that asset's stable id): the cell's visible
        /// <see cref="tileId"/> is then kept in sync with its neighbors by
        /// <see cref="TerrainTransitionResolver"/> whenever painting changes an adjacent cell, unless
        /// <see cref="manualOverride"/> is set.
        /// </summary>
        public string terrainSetId;

        public bool IsEmpty => string.IsNullOrEmpty(tileId);
        public bool IsTerrainManaged => !string.IsNullOrEmpty(terrainSetId);

        public static TileCellData Empty => new TileCellData { tileId = null, rotationSteps = 0, flip = TileFlip.None, terrainSetId = null };

        public bool Equals(TileCellData other) =>
            tileId == other.tileId && rotationSteps == other.rotationSteps && flip == other.flip &&
            manualOverride == other.manualOverride && terrainSetId == other.terrainSetId;

        public override bool Equals(object obj) => obj is TileCellData other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (tileId != null ? tileId.GetHashCode() : 0);
                hash = hash * 31 + rotationSteps;
                hash = hash * 31 + (int)flip;
                hash = hash * 31 + (manualOverride ? 1 : 0);
                hash = hash * 31 + (terrainSetId != null ? terrainSetId.GetHashCode() : 0);
                return hash;
            }
        }
    }
}
