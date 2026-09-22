namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// Computes and applies auto-tiling for terrain-managed cells (see
    /// <see cref="TileCellData.terrainSetId"/>). Editing one cell can change up to 8 neighbors' correct
    /// variant, so callers should re-resolve the edited coordinate plus its neighbors after every terrain
    /// paint operation -- <see cref="ResolveCellAndNeighbors"/> does exactly that. Manually-overridden
    /// cells (<see cref="TileCellData.manualOverride"/>) are never touched, per the spec's requirement
    /// that manual overrides must not be unexpectedly replaced when adjacent tiles change.
    /// </summary>
    public static class TerrainTransitionResolver
    {
        // 4-neighbor: N, E, S, W = bits 0-3. 8-neighbor adds NE, SE, SW, NW = bits 4-7.
        private static readonly (int dx, int dz)[] FourDirs = { (0, 1), (1, 0), (0, -1), (-1, 0) };
        private static readonly (int dx, int dz)[] EightDirs =
        {
            (0, 1), (1, 0), (0, -1), (-1, 0),
            (1, 1), (1, -1), (-1, -1), (-1, 1)
        };

        private static (int dx, int dz)[] DirsFor(TerrainAdjacencyMode mode) =>
            mode == TerrainAdjacencyMode.FourNeighbor ? FourDirs : EightDirs;

        public static int ComputeMask(TileLayerData layer, TileCoord coord, string terrainSetId, TerrainAdjacencyMode mode)
        {
            (int dx, int dz)[] dirs = DirsFor(mode);
            int mask = 0;

            for (int i = 0; i < dirs.Length; i++)
            {
                TileCoord neighbor = new TileCoord(coord.x + dirs[i].dx, coord.z + dirs[i].dz, coord.elevationLevel);
                if (layer.TryGetTile(neighbor, out TileCellData cell) && !cell.IsEmpty && cell.terrainSetId == terrainSetId)
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }

        /// <summary>Re-resolves a single cell in place, if it belongs to terrainSet and isn't manually overridden.</summary>
        public static void ResolveSingle(TileLayerData layer, TerrainTransitionSet terrainSet, TileCoord coord)
        {
            if (terrainSet == null) return;
            if (!layer.TryGetTile(coord, out TileCellData cell) || cell.IsEmpty) return;
            if (cell.manualOverride) return;
            if (cell.terrainSetId != terrainSet.TerrainSetId) return;

            int mask = ComputeMask(layer, coord, terrainSet.TerrainSetId, terrainSet.adjacencyMode);
            TileDefinition resolved = terrainSet.Resolve(mask);
            if (resolved == null) return;

            cell.tileId = resolved.TileId;
            layer.SetTile(coord, cell);
        }

        /// <summary>Re-resolves coord plus every cell in its adjacency neighborhood -- call after any edit at coord.</summary>
        public static void ResolveCellAndNeighbors(TileLayerData layer, TerrainTransitionSet terrainSet, TileCoord coord)
        {
            if (terrainSet == null) return;

            ResolveSingle(layer, terrainSet, coord);

            (int dx, int dz)[] dirs = DirsFor(terrainSet.adjacencyMode);
            for (int i = 0; i < dirs.Length; i++)
            {
                TileCoord neighbor = new TileCoord(coord.x + dirs[i].dx, coord.z + dirs[i].dz, coord.elevationLevel);
                ResolveSingle(layer, terrainSet, neighbor);
            }
        }
    }
}
