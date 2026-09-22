using System.Collections.Generic;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// In-memory copy/paste buffer for a rectangular tile selection. Editor-session-only (not persisted
    /// across Unity restarts) -- consistent with how the system clipboard behaves for most editors.
    /// </summary>
    public static class WorldBuilderClipboard
    {
        public struct ClipboardCell
        {
            public int dx;
            public int dz;
            public TileCellData cell;
        }

        private static readonly List<ClipboardCell> Cells = new List<ClipboardCell>();

        public static bool HasContent { get; private set; }
        public static int Width { get; private set; }
        public static int Height { get; private set; }

        public static void Copy(TileLayerData layer, TileCoord min, TileCoord max)
        {
            Cells.Clear();

            for (int x = min.x; x <= max.x; x++)
            {
                for (int z = min.z; z <= max.z; z++)
                {
                    TileCoord coord = new TileCoord(x, z, min.elevationLevel);
                    if (layer.TryGetTile(coord, out TileCellData cell) && !cell.IsEmpty)
                    {
                        Cells.Add(new ClipboardCell { dx = x - min.x, dz = z - min.z, cell = cell });
                    }
                }
            }

            Width = max.x - min.x + 1;
            Height = max.z - min.z + 1;
            HasContent = true;
        }

        public static IReadOnlyList<ClipboardCell> Snapshot() => Cells;
    }
}
