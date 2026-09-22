namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// The Select tool's current rectangular selection, in tile coordinates (inclusive on both ends).
    /// Consumed by the Move tool, Copy/Cut/Delete, Rotate/Flip Selection and "Save Selection as Stamp".
    /// </summary>
    public class WorldBuilderSelection
    {
        public bool HasSelection { get; private set; }
        public TileCoord Min { get; private set; }
        public TileCoord Max { get; private set; }

        public void Set(TileCoord a, TileCoord b)
        {
            int minX = a.x < b.x ? a.x : b.x;
            int maxX = a.x > b.x ? a.x : b.x;
            int minZ = a.z < b.z ? a.z : b.z;
            int maxZ = a.z > b.z ? a.z : b.z;

            Min = new TileCoord(minX, minZ, a.elevationLevel);
            Max = new TileCoord(maxX, maxZ, a.elevationLevel);
            HasSelection = true;
        }

        public void Clear() => HasSelection = false;

        public bool Contains(TileCoord coord) =>
            HasSelection &&
            coord.elevationLevel == Min.elevationLevel &&
            coord.x >= Min.x && coord.x <= Max.x &&
            coord.z >= Min.z && coord.z <= Max.z;

        public int Width => HasSelection ? Max.x - Min.x + 1 : 0;
        public int Height => HasSelection ? Max.z - Min.z + 1 : 0;
    }
}
