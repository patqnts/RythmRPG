using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    /// <summary>
    /// Collects flat-shaded polygons (one tile each) and turns them into a mesh. Used by <see cref="PixelLevel"/>
    /// and <see cref="PixelTileMesh"/>. Every polygon gets its own vertices (hard edges, one flat normal), and its
    /// winding is fixed from the outward direction you pass, so callers never worry about front / back faces.
    /// </summary>
    public sealed class PixelMeshBuffer
    {
        public readonly List<Vector3> vertices = new();
        public readonly List<Vector3> normals = new();
        public readonly List<Vector4> tangents = new();
        public readonly List<Vector2> uvs = new();
        public readonly List<int> triangles = new();

        public int VertexCount => vertices.Count;

        public void Clear()
        {
            vertices.Clear();
            normals.Clear();
            tangents.Clear();
            uvs.Clear();
            triangles.Clear();
        }

        /// <summary>Adds a convex polygon (3+ points) facing <paramref name="outward"/>.</summary>
        public void AddPolygon(IReadOnlyList<Vector3> points, IReadOnlyList<Vector2> uv, Vector3 outward)
        {
            int n = points.Count;
            if (n < 3) return;

            // Newell normal (robust for any convex polygon).
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % n];
                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
            }
            if (normal.sqrMagnitude < 1e-12f) return;
            normal.Normalize();
            if (Vector3.Dot(normal, outward) < 0f) normal = -normal;

            Vector4 tangent = Tangent(points, uv, normal);
            int start = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                vertices.Add(points[i]);
                normals.Add(normal);
                tangents.Add(tangent);
                uvs.Add(uv[i]);
            }
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = points[0], b = points[i], c = points[i + 1];
                // Unity: a front face has Cross(b - a, c - a) pointing at the viewer.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0f)
                {
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
                else
                {
                    triangles.Add(start);
                    triangles.Add(start + i + 1);
                    triangles.Add(start + i);
                }
            }
        }

        private static Vector4 Tangent(IReadOnlyList<Vector3> p, IReadOnlyList<Vector2> uv, Vector3 normal)
        {
            Vector3 e1 = p[1] - p[0], e2 = p[2] - p[0];
            Vector2 d1 = uv[1] - uv[0], d2 = uv[2] - uv[0];
            float det = d1.x * d2.y - d2.x * d1.y;
            if (Mathf.Abs(det) < 1e-12f) return new Vector4(1, 0, 0, 1);
            float r = 1f / det;
            Vector3 t = (e1 * d2.y - e2 * d1.y) * r;
            Vector3 b = (e2 * d1.x - e1 * d2.x) * r;
            t = (t - normal * Vector3.Dot(normal, t)).normalized;
            if (t.sqrMagnitude < 1e-8f) return new Vector4(1, 0, 0, 1);
            float w = Vector3.Dot(Vector3.Cross(normal, t), b) < 0f ? -1f : 1f;
            return new Vector4(t.x, t.y, t.z, w);
        }

        public void ToMesh(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ helpers shared by the builders

        /// <summary>A polygon vertex in some 2D tiling space (key) with its 3D position.</summary>
        public struct ClipVertex
        {
            public Vector2 key;
            public Vector3 position;

            public ClipVertex(Vector2 key, Vector3 position)
            {
                this.key = key;
                this.position = position;
            }
        }

        /// <summary>Keeps the part of a convex polygon where key[axis] is ≥ value (keepAbove) or ≤ value.</summary>
        public static List<ClipVertex> Clip(List<ClipVertex> polygon, int axis, float value, bool keepAbove)
        {
            var result = new List<ClipVertex>(polygon.Count + 2);
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                ClipVertex a = polygon[i], b = polygon[(i + 1) % n];
                float da = (keepAbove ? 1f : -1f) * (a.key[axis] - value);
                float db = (keepAbove ? 1f : -1f) * (b.key[axis] - value);
                bool inA = da >= -1e-6f, inB = db >= -1e-6f;
                if (inA) result.Add(a);
                if (inA != inB)
                {
                    float t = da / (da - db);
                    result.Add(new ClipVertex(Vector2.Lerp(a.key, b.key, t), Vector3.Lerp(a.position, b.position, t)));
                }
            }
            return result;
        }

        /// <summary>Maps a 0..1 position inside a tile to the tileset UV, with quarter-turn rotation and flip.</summary>
        public static Vector2 TileUV(Rect tile, Vector2 local, int rotation, bool flipX = false)
        {
            local.x = Mathf.Clamp01(local.x);
            local.y = Mathf.Clamp01(local.y);
            if (flipX) local.x = 1f - local.x;
            switch (((rotation % 4) + 4) % 4)
            {
                case 1: local = new Vector2(local.y, 1f - local.x); break;
                case 2: local = new Vector2(1f - local.x, 1f - local.y); break;
                case 3: local = new Vector2(1f - local.y, local.x); break;
            }
            return new Vector2(tile.xMin + local.x * tile.width, tile.yMin + local.y * tile.height);
        }
    }
}
