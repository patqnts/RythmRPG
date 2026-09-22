using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// Pure grid algorithms shared by the Rectangle, Line and Fill tools: rectangle iteration, a
    /// standard Bresenham line, and an iterative (non-recursive, so it can't stack-overflow) flood fill
    /// that is hard-capped so it stays bounded on this conceptually-infinite ground grid.
    /// </summary>
    public static class TileGridUtility
    {
        public static IEnumerable<TileCoord> Rectangle(TileCoord a, TileCoord b)
        {
            int minX = Mathf.Min(a.x, b.x);
            int maxX = Mathf.Max(a.x, b.x);
            int minZ = Mathf.Min(a.z, b.z);
            int maxZ = Mathf.Max(a.z, b.z);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    yield return new TileCoord(x, z, a.elevationLevel);
                }
            }
        }

        public static IEnumerable<TileCoord> Line(TileCoord a, TileCoord b)
        {
            int x0 = a.x, z0 = a.z, x1 = b.x, z1 = b.z;
            int dx = Mathf.Abs(x1 - x0);
            int dz = -Mathf.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx + dz;
            int x = x0, z = z0;

            while (true)
            {
                yield return new TileCoord(x, z, a.elevationLevel);
                if (x == x1 && z == z1) yield break;

                int e2 = 2 * err;
                if (e2 >= dz)
                {
                    err += dz;
                    x += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    z += sz;
                }
            }
        }

        /// <summary>
        /// 4-connected flood fill starting at seed, matching every reachable cell whose current tile id
        /// equals the seed cell's tile id (null/empty counts as its own matching "id" so filling empty
        /// space works too). Stops at maxTiles and reports whether it was cut short via capped.
        /// </summary>
        public static List<TileCoord> FloodFill(TileLayerData layer, TileCoord seed, int maxTiles, out bool capped)
        {
            capped = false;
            List<TileCoord> result = new List<TileCoord>();
            if (maxTiles <= 0) return result;

            layer.TryGetTile(seed, out TileCellData seedCell);
            string matchId = seedCell.IsEmpty ? null : seedCell.tileId;

            HashSet<TileCoord> visited = new HashSet<TileCoord> { seed };
            Queue<TileCoord> queue = new Queue<TileCoord>();
            queue.Enqueue(seed);

            (int dx, int dz)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

            while (queue.Count > 0)
            {
                if (result.Count >= maxTiles)
                {
                    capped = true;
                    break;
                }

                TileCoord current = queue.Dequeue();
                result.Add(current);

                foreach ((int dx, int dz) d in dirs)
                {
                    TileCoord next = new TileCoord(current.x + d.dx, current.z + d.dz, seed.elevationLevel);
                    if (!visited.Add(next)) continue;

                    layer.TryGetTile(next, out TileCellData nextCell);
                    string nextId = nextCell.IsEmpty ? null : nextCell.tileId;
                    if (nextId == matchId)
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return result;
        }
    }
}
