using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// One named ground layer (e.g. "Ground", "Ground Decorations"). Holds a sparse map of painted
    /// cells. Unity cannot serialize Dictionary directly, so the map is flattened to parallel key/value
    /// lists on serialize and rebuilt into a real Dictionary on deserialize for O(1) edit-time lookups.
    /// </summary>
    [Serializable]
    public class TileLayerData : ISerializationCallbackReceiver
    {
        public string layerName = "Ground";
        public bool visible = true;
        public bool locked;

        [SerializeField] private List<TileCoord> serializedKeys = new List<TileCoord>();
        [SerializeField] private List<TileCellData> serializedValues = new List<TileCellData>();

        private Dictionary<TileCoord, TileCellData> map = new Dictionary<TileCoord, TileCellData>();

        public int TileCount => map.Count;

        public bool TryGetTile(TileCoord coord, out TileCellData cell) => map.TryGetValue(coord, out cell);

        public bool HasTile(TileCoord coord) => map.ContainsKey(coord);

        public void SetTile(TileCoord coord, TileCellData cell)
        {
            if (cell.IsEmpty)
            {
                map.Remove(coord);
                return;
            }

            map[coord] = cell;
        }

        public void RemoveTile(TileCoord coord) => map.Remove(coord);

        public void Clear() => map.Clear();

        public IEnumerable<KeyValuePair<TileCoord, TileCellData>> AllTiles => map;

        /// <summary>
        /// Tile coordinates that fall inside the given chunk (in tile units) at the given elevation.
        /// Chunk membership follows a floor-division of the tile coordinate by chunkSize, matching
        /// <see cref="WorldBuilderWorld.ChunkKeyOf"/>.
        /// </summary>
        public IEnumerable<TileCoord> CoordsInChunk(int chunkX, int chunkZ, int chunkSize, int elevationLevel)
        {
            foreach (KeyValuePair<TileCoord, TileCellData> kvp in map)
            {
                TileCoord c = kvp.Key;
                if (c.elevationLevel != elevationLevel) continue;
                if (FloorDiv(c.x, chunkSize) != chunkX) continue;
                if (FloorDiv(c.z, chunkSize) != chunkZ) continue;
                yield return c;
            }
        }

        internal static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

        public void OnBeforeSerialize()
        {
            serializedKeys.Clear();
            serializedValues.Clear();
            foreach (KeyValuePair<TileCoord, TileCellData> kvp in map)
            {
                serializedKeys.Add(kvp.Key);
                serializedValues.Add(kvp.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            int count = Math.Min(serializedKeys.Count, serializedValues.Count);
            map = new Dictionary<TileCoord, TileCellData>(count);
            for (int i = 0; i < count; i++)
            {
                map[serializedKeys[i]] = serializedValues[i];
            }
        }
    }
}
