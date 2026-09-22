using System;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// A logical tile-grid coordinate on the XZ ground plane, plus the elevation level (Y "floor")
    /// it belongs to. Also reused as a chunk coordinate (see <see cref="WorldBuilderWorld.ChunkKeyOf"/>),
    /// since a chunk key has the same shape as a tile coordinate.
    /// </summary>
    [Serializable]
    public struct TileCoord : IEquatable<TileCoord>
    {
        public int x;
        public int z;
        public int elevationLevel;

        public TileCoord(int x, int z, int elevationLevel)
        {
            this.x = x;
            this.z = z;
            this.elevationLevel = elevationLevel;
        }

        public bool Equals(TileCoord other) =>
            x == other.x && z == other.z && elevationLevel == other.elevationLevel;

        public override bool Equals(object obj) => obj is TileCoord other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + z;
                hash = hash * 31 + elevationLevel;
                return hash;
            }
        }

        public static bool operator ==(TileCoord a, TileCoord b) => a.Equals(b);
        public static bool operator !=(TileCoord a, TileCoord b) => !a.Equals(b);

        public override string ToString() => $"({x}, {z}) @L{elevationLevel}";
    }
}
