using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Builds the geometry of a <see cref="PixelLevel"/> chunk:
    /// <list type="bullet">
    /// <item>tops: flat tiles, sloped ramps, stairs (treads use the floor tile, risers the wall tile);</item>
    /// <item>walls: wherever a cell's edge is higher than its neighbour's (or the level's base at the edge of the
    /// painted area), cut into one tile per level so the wall tile repeats (the top row can use a Wall Top tile).
    /// Works for any mix of flat cells, ramps and stairs (sloped / stepped walls on their sides).</item>
    /// </list>
    /// The collision mesh is the same, except stairs are a smooth ramp so characters walk up them.
    /// </summary>
    internal static class PixelLevelMesher
    {
        private struct Segment
        {
            public float t0, t1, y0, y1;

            public Segment(float t0, float t1, float y0, float y1)
            {
                this.t0 = t0;
                this.t1 = t1;
                this.y0 = y0;
                this.y1 = y1;
            }

            public float Eval(float t) => t1 - t0 < 1e-6f ? y0 : Mathf.Lerp(y0, y1, (t - t0) / (t1 - t0));
        }

        private const float Eps = 1e-4f;

        private static readonly Vector3[] SideNormals =
        {
            Vector3.forward, Vector3.right, Vector3.back, Vector3.left
        };

        public static void Build(PixelLevel level, List<PixelCell> cells, PixelMeshBuffer visual, PixelMeshBuffer collision)
        {
            PixelTileset tileset = level.Tileset;
            var profileA = new List<Segment>(8);
            var profileB = new List<Segment>(8);
            var breaks = new List<float>(20);
            var points = new List<Vector3>(8);
            var uvs = new List<Vector2>(8);

            foreach (PixelCell cell in cells)
            {
                BuildTop(level, tileset, cell, visual, collision, points, uvs);
                for (int side = 0; side < 4; side++)
                {
                    Profile(level, cell, side, profileA);
                    Vector2Int o = PixelLevel.SideOffset(side);
                    if (level.TryGetCell(cell.x + o.x, cell.z + o.y, out PixelCell neighbour))
                        Profile(level, neighbour, (side + 2) % 4, profileB);
                    else
                    {
                        profileB.Clear();
                        profileB.Add(new Segment(0f, 1f, level.BaseHeight, level.BaseHeight));
                    }
                    BuildWall(level, tileset, cell, side, profileA, profileB, breaks, visual, collision, points, uvs);
                }
            }
        }

        // ------------------------------------------------------------------ edges

        /// <summary>(u, v) inside the cell of the point at t (0..1) along a side. t runs along +X or +Z.</summary>
        private static Vector2 EdgeUV(int side, float t) => side switch
        {
            0 => new Vector2(t, 1f),
            1 => new Vector2(1f, t),
            2 => new Vector2(t, 0f),
            _ => new Vector2(0f, t)
        };

        /// <summary>(u, v) of a point p along the rise (0 low .. 1 high) and q across it.</summary>
        private static Vector2 RiseUV(int direction, float p, float q) => direction switch
        {
            0 => new Vector2(q, p),
            1 => new Vector2(p, q),
            2 => new Vector2(q, 1f - p),
            _ => new Vector2(1f - p, q)
        };

        private static Vector3 Local(PixelLevel level, in PixelCell cell, Vector2 uv, float levels) =>
            new((cell.x + uv.x) * level.CellSize, levels * level.StepHeight, (cell.z + uv.y) * level.CellSize);

        /// <summary>Height of the cell's top along one side, as segments over t.</summary>
        private static void Profile(PixelLevel level, in PixelCell cell, int side, List<Segment> result)
        {
            result.Clear();
            float h = cell.height;
            if (cell.shape == PixelCellShape.Flat)
            {
                result.Add(new Segment(0f, 1f, h, h));
                return;
            }
            Vector2 e0 = EdgeUV(side, 0f), e1 = EdgeUV(side, 1f);
            float p0 = PixelLevel.RiseAmount(cell.direction, e0.x, e0.y);
            float p1 = PixelLevel.RiseAmount(cell.direction, e1.x, e1.y);
            if (cell.shape == PixelCellShape.Ramp)
            {
                result.Add(new Segment(0f, 1f, h + p0, h + p1));
                return;
            }
            // Stairs.
            if (Mathf.Abs(p0 - p1) < Eps)
            {
                float y = level.SurfaceHeight(cell, e0.x, e0.y);
                result.Add(new Segment(0f, 1f, y, y));
                return;
            }
            int n = level.StairSteps;
            bool rising = p1 > p0;
            for (int i = 0; i < n; i++)
            {
                float pa = i / (float)n, pb = (i + 1) / (float)n;
                float y = h + (i + 1) / (float)n;
                float ta = rising ? pa : 1f - pb;
                float tb = rising ? pb : 1f - pa;
                result.Add(new Segment(ta, tb, y, y));
            }
            result.Sort((a, b) => a.t0.CompareTo(b.t0));
        }

        private static Segment Find(List<Segment> profile, float t)
        {
            foreach (Segment s in profile)
                if (t >= s.t0 - Eps && t <= s.t1 + Eps) return s;
            return profile[profile.Count - 1];
        }

        // ------------------------------------------------------------------ walls

        private static void BuildWall(PixelLevel level, PixelTileset tileset, in PixelCell cell, int side,
            List<Segment> a, List<Segment> b, List<float> breaks, PixelMeshBuffer visual, PixelMeshBuffer collision,
            List<Vector3> points, List<Vector2> uvs)
        {
            breaks.Clear();
            breaks.Add(0f);
            breaks.Add(1f);
            foreach (Segment s in a) { breaks.Add(s.t0); breaks.Add(s.t1); }
            foreach (Segment s in b) { breaks.Add(s.t0); breaks.Add(s.t1); }
            breaks.Sort();

            for (int i = 0; i < breaks.Count - 1; i++)
            {
                float ta = breaks[i], tb = breaks[i + 1];
                if (tb - ta < 1e-5f) continue;
                float tm = (ta + tb) * 0.5f;
                Segment sa = Find(a, tm), sb = Find(b, tm);
                float a0 = sa.Eval(ta), a1 = sa.Eval(tb), b0 = sb.Eval(ta), b1 = sb.Eval(tb);
                float d0 = a0 - b0, d1 = a1 - b1;
                if (d0 <= Eps && d1 <= Eps) continue;
                if (d0 >= -Eps && d1 >= -Eps)
                {
                    EmitWall(level, tileset, cell, side, ta, tb, b0, b1, a0, a1, visual, collision, points, uvs);
                    continue;
                }
                // The two edges cross: keep the part where this cell is higher.
                float f = d0 / (d0 - d1);
                float tc = Mathf.Lerp(ta, tb, f);
                float yc = Mathf.Lerp(a0, a1, f);
                if (d0 > 0f) EmitWall(level, tileset, cell, side, ta, tc, b0, yc, a0, yc, visual, collision, points, uvs);
                else EmitWall(level, tileset, cell, side, tc, tb, yc, b1, yc, a1, visual, collision, points, uvs);
            }
        }

        private static void EmitWall(PixelLevel level, PixelTileset tileset, in PixelCell cell, int side,
            float ta, float tb, float b0, float b1, float a0, float a1, PixelMeshBuffer visual, PixelMeshBuffer collision,
            List<Vector3> points, List<Vector2> uvs)
        {
            var polygon = new List<PixelMeshBuffer.ClipVertex>(4);
            AddUnique(polygon, new Vector2(ta, Mathf.Min(b0, a0)));
            AddUnique(polygon, new Vector2(tb, Mathf.Min(b1, a1)));
            AddUnique(polygon, new Vector2(tb, a1));
            AddUnique(polygon, new Vector2(ta, a0));
            if (polygon.Count < 3) return;

            float top = Mathf.Max(a0, a1);
            float bottom = Mathf.Min(Mathf.Min(b0, b1), Mathf.Min(a0, a1));
            int kMin = Mathf.FloorToInt(bottom + Eps);
            int kMax = Mathf.CeilToInt(top - Eps);
            bool mirrored = side == 0 || side == 3; // seen from outside, t runs right-to-left on these sides
            Vector3 outward = SideNormals[side];

            for (int k = kMin; k < kMax; k++)
            {
                List<PixelMeshBuffer.ClipVertex> band = PixelMeshBuffer.Clip(polygon, 1, k, true);
                if (band.Count < 3) continue;
                band = PixelMeshBuffer.Clip(band, 1, k + 1, false);
                if (band.Count < 3) continue;

                int tile = k + 1 >= top - Eps && cell.wallTop >= 0 ? cell.wallTop : cell.wall;
                Rect rect = tileset.TileUV(tile);
                points.Clear();
                uvs.Clear();
                foreach (PixelMeshBuffer.ClipVertex v in band)
                {
                    float t = v.key.x, y = v.key.y;
                    points.Add(Local(level, cell, EdgeUV(side, t), y));
                    uvs.Add(PixelMeshBuffer.TileUV(rect, new Vector2(mirrored ? 1f - t : t, y - k), 0));
                }
                visual.AddPolygon(points, uvs, outward);
                collision?.AddPolygon(points, uvs, outward);
            }
        }

        private static void AddUnique(List<PixelMeshBuffer.ClipVertex> polygon, Vector2 key)
        {
            if (polygon.Count > 0)
            {
                Vector2 last = polygon[polygon.Count - 1].key, first = polygon[0].key;
                if ((last - key).sqrMagnitude < 1e-10f) return;
                if (polygon.Count >= 2 && (first - key).sqrMagnitude < 1e-10f) return;
            }
            polygon.Add(new PixelMeshBuffer.ClipVertex(key, Vector3.zero));
        }

        // ------------------------------------------------------------------ tops

        private static void BuildTop(PixelLevel level, PixelTileset tileset, in PixelCell cell, PixelMeshBuffer visual,
            PixelMeshBuffer collision, List<Vector3> points, List<Vector2> uvs)
        {
            Rect floor = tileset.TileUV(cell.floor);

            if (cell.shape != PixelCellShape.Stairs)
            {
                points.Clear();
                uvs.Clear();
                AddTopCorner(level, cell, floor, 0f, 0f, points, uvs);
                AddTopCorner(level, cell, floor, 1f, 0f, points, uvs);
                AddTopCorner(level, cell, floor, 1f, 1f, points, uvs);
                AddTopCorner(level, cell, floor, 0f, 1f, points, uvs);
                visual.AddPolygon(points, uvs, Vector3.up);
                collision?.AddPolygon(points, uvs, Vector3.up);
                return;
            }

            int n = level.StairSteps;
            float h = cell.height;
            int dir = cell.direction;
            Rect wall = tileset.TileUV(cell.wall);
            int faceSide = (dir + 2) % 4; // risers face down the stairs
            bool mirrored = faceSide == 0 || faceSide == 3;
            Vector3 riserOutward = SideNormals[faceSide];

            for (int i = 0; i < n; i++)
            {
                float p0 = i / (float)n, p1 = (i + 1) / (float)n, y = h + p1;

                // Tread: a slice of the floor tile, so the whole tile shows when seen from above.
                points.Clear();
                uvs.Clear();
                foreach (Vector2 pq in new[] { new Vector2(p0, 0f), new Vector2(p1, 0f), new Vector2(p1, 1f), new Vector2(p0, 1f) })
                {
                    Vector2 uv = RiseUV(dir, pq.x, pq.y);
                    points.Add(Local(level, cell, uv, y));
                    uvs.Add(PixelMeshBuffer.TileUV(floor, uv, cell.rotation));
                }
                visual.AddPolygon(points, uvs, Vector3.up);

                // Riser in front of this tread (the first one is built as a wall against the lower neighbour).
                if (i == 0) continue;
                float yLow = h + p0;
                points.Clear();
                uvs.Clear();
                foreach (Vector2 qy in new[] { new Vector2(0f, yLow), new Vector2(1f, yLow), new Vector2(1f, y), new Vector2(0f, y) })
                {
                    Vector2 uv = RiseUV(dir, p0, qy.x);
                    points.Add(Local(level, cell, uv, qy.y));
                    // Along-edge coordinate: x for risers facing north / south, z for east / west.
                    float t = faceSide == 0 || faceSide == 2 ? uv.x : uv.y;
                    uvs.Add(PixelMeshBuffer.TileUV(wall, new Vector2(mirrored ? 1f - t : t, qy.y - h), 0));
                }
                visual.AddPolygon(points, uvs, riserOutward);
            }

            // Collision: a smooth ramp, easy to walk up.
            if (collision == null) return;
            points.Clear();
            uvs.Clear();
            foreach (Vector2 pq in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) })
            {
                Vector2 uv = RiseUV(dir, pq.x, pq.y);
                points.Add(Local(level, cell, uv, h + pq.x));
                uvs.Add(uv);
            }
            collision.AddPolygon(points, uvs, Vector3.up);
        }

        private static void AddTopCorner(PixelLevel level, in PixelCell cell, Rect floor, float u, float v,
            List<Vector3> points, List<Vector2> uvs)
        {
            points.Add(level.CellTopLocal(cell, u, v));
            uvs.Add(PixelMeshBuffer.TileUV(floor, new Vector2(u, v), cell.rotation));
        }
    }
}
