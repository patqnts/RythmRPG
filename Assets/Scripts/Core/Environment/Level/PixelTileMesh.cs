using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Puts pixel-art tiles from a <see cref="PixelTileset"/> onto the faces of any mesh (a rock, a house, a crate):
    /// select the object, press Paint Tiles, pick a tile and click faces in the Scene view. Each face shows its tile
    /// pixel-perfect: stretched to fit the face, or repeated once per world unit (32 px per unit). Faces you don't
    /// paint keep their original material. The original mesh is never modified; a painted copy is built from it
    /// (for builds, the source mesh needs Read/Write enabled; the editor turns it on for you).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Rythm RPG/Environment/Pixel Tile Mesh")]
    public sealed class PixelTileMesh : MonoBehaviour
    {
        [Serializable]
        public struct FaceTile
        {
            /// <summary>Triangle indices (into the source mesh's triangles, all sub-meshes in order).</summary>
            public int[] triangles;
            public int tile;
            public byte rotation;
            public bool flipX;
            /// <summary>Repeat the tile every Tile World Size (else stretch it over the face).</summary>
            public bool repeat;
        }

        [SerializeField] private PixelTileset tileset;
        [Tooltip("World size of one tile when a face repeats its tile (1 = 32 px tiles at 32 px per unit).")]
        [SerializeField, Min(0.01f)] private float tileWorldSize = 1f;
        [SerializeField, HideInInspector] private Mesh sourceMesh;
        [SerializeField, HideInInspector] private Material[] sourceMaterials;
        [SerializeField, HideInInspector] private List<FaceTile> faces = new();

        private const string GeneratedName = "Pixel Tile Mesh (generated)";
        private Mesh generated;
        private bool dirty = true;
        private Vector3[] cachedVertices;
        private int[] cachedTriangles;
        private Dictionary<long, List<int>> edgeMap;

        public PixelTileset Tileset { get => tileset; set { tileset = value; dirty = true; } }
        public float TileWorldSize => tileWorldSize;
        public Mesh SourceMesh => sourceMesh;
        public IReadOnlyList<FaceTile> Faces => faces;

        // ------------------------------------------------------------------ lifecycle

        private void Reset() => Capture();

        private void OnEnable()
        {
            Capture();
            dirty = true;
            Rebuild();
        }

        private void OnDisable() => Restore();

        private void OnValidate()
        {
            dirty = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private void Update()
        {
            if (dirty) Rebuild();
        }

        private void Capture()
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (sourceMesh == null && filter != null && filter.sharedMesh != null && filter.sharedMesh.name != GeneratedName)
            {
                sourceMesh = filter.sharedMesh;
                if (renderer != null) sourceMaterials = renderer.sharedMaterials;
            }
            if (sourceMaterials == null && renderer != null) sourceMaterials = renderer.sharedMaterials;
            cachedVertices = null;
            edgeMap = null;
        }

        /// <summary>Puts the original mesh and materials back (when the component is disabled or removed).</summary>
        private void Restore()
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter != null && sourceMesh != null && filter.sharedMesh == generated) filter.sharedMesh = sourceMesh;
            if (renderer != null && sourceMaterials != null) renderer.sharedMaterials = sourceMaterials;
            if (generated != null)
            {
                if (Application.isPlaying) Destroy(generated);
                else DestroyImmediate(generated);
                generated = null;
            }
        }

        public void MarkDirty() => dirty = true;

        // ------------------------------------------------------------------ editing

        /// <summary>Tiles a face (replacing whatever tile its triangles had).</summary>
        public void SetFace(int[] triangles, int tile, int rotation, bool flipX, bool repeat)
        {
            ClearFace(triangles);
            faces.Add(new FaceTile
            {
                triangles = (int[])triangles.Clone(),
                tile = tile,
                rotation = (byte)(((rotation % 4) + 4) % 4),
                flipX = flipX,
                repeat = repeat
            });
            dirty = true;
        }

        /// <summary>Removes the tiles from these triangles (they show the original material again).</summary>
        public void ClearFace(int[] triangles)
        {
            var remove = new HashSet<int>(triangles);
            for (int i = faces.Count - 1; i >= 0; i--)
            {
                FaceTile face = faces[i];
                var kept = new List<int>();
                foreach (int t in face.triangles)
                    if (!remove.Contains(t)) kept.Add(t);
                if (kept.Count == face.triangles.Length) continue;
                if (kept.Count == 0) faces.RemoveAt(i);
                else
                {
                    face.triangles = kept.ToArray();
                    faces[i] = face;
                }
            }
            dirty = true;
        }

        public bool TryGetFace(int triangle, out FaceTile face)
        {
            foreach (FaceTile f in faces)
                if (Array.IndexOf(f.triangles, triangle) >= 0)
                {
                    face = f;
                    return true;
                }
            face = default;
            return false;
        }

        public void ClearAll()
        {
            faces.Clear();
            dirty = true;
        }

        // ------------------------------------------------------------------ picking

        private void CacheSource()
        {
            if (cachedVertices != null || sourceMesh == null) return;
            cachedVertices = sourceMesh.vertices;
            cachedTriangles = sourceMesh.triangles; // all sub-meshes in order
            edgeMap = null;
        }

        /// <summary>The source triangle a world ray hits first, or -1.</summary>
        public int RaycastTriangle(Ray worldRay, out float distance)
        {
            distance = float.MaxValue;
            CacheSource();
            if (cachedVertices == null) return -1;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            Vector3 origin = toLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 direction = toLocal.MultiplyVector(worldRay.direction);
            int best = -1;
            for (int t = 0; t + 2 < cachedTriangles.Length; t += 3)
            {
                Vector3 a = cachedVertices[cachedTriangles[t]], b = cachedVertices[cachedTriangles[t + 1]], c = cachedVertices[cachedTriangles[t + 2]];
                if (!IntersectTriangle(origin, direction, a, b, c, out float d) || d >= distance) continue;
                distance = d;
                best = t / 3;
            }
            if (best >= 0) distance *= transform.lossyScale.magnitude / Mathf.Sqrt(3f);
            return best;
        }

        private static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 0f;
        }

        /// <summary>
        /// The flat face a triangle belongs to: it plus every neighbouring triangle (sharing an edge) that lies in the
        /// same plane (within <paramref name="angle"/> degrees). A cube side gives its 2 triangles.
        /// </summary>
        public int[] FindFace(int triangle, float angle = 1f)
        {
            CacheSource();
            if (cachedVertices == null || triangle < 0) return Array.Empty<int>();
            BuildEdgeMap();
            float cosLimit = Mathf.Cos(angle * Mathf.Deg2Rad);
            Vector3 n0 = TriangleNormal(triangle);
            Vector3 p0 = cachedVertices[cachedTriangles[triangle * 3]];
            float planeTolerance = sourceMesh.bounds.size.magnitude * 1e-4f + 1e-5f;

            var result = new List<int> { triangle };
            var seen = new HashSet<int> { triangle };
            var open = new Queue<int>();
            open.Enqueue(triangle);
            while (open.Count > 0)
            {
                int t = open.Dequeue();
                for (int e = 0; e < 3; e++)
                {
                    Vector3 a = cachedVertices[cachedTriangles[t * 3 + e]], b = cachedVertices[cachedTriangles[t * 3 + (e + 1) % 3]];
                    if (!edgeMap.TryGetValue(EdgeKey(a, b), out List<int> shared)) continue;
                    foreach (int other in shared)
                    {
                        if (!seen.Add(other)) continue;
                        if (Vector3.Dot(TriangleNormal(other), n0) < cosLimit) continue;
                        Vector3 q = cachedVertices[cachedTriangles[other * 3]];
                        if (Mathf.Abs(Vector3.Dot(q - p0, n0)) > planeTolerance) continue;
                        result.Add(other);
                        open.Enqueue(other);
                    }
                }
            }
            return result.ToArray();
        }

        private Vector3 TriangleNormal(int t)
        {
            Vector3 a = cachedVertices[cachedTriangles[t * 3]], b = cachedVertices[cachedTriangles[t * 3 + 1]], c = cachedVertices[cachedTriangles[t * 3 + 2]];
            return Vector3.Cross(b - a, c - a).normalized;
        }

        private void BuildEdgeMap()
        {
            if (edgeMap != null) return;
            edgeMap = new Dictionary<long, List<int>>();
            for (int t = 0; t * 3 + 2 < cachedTriangles.Length; t++)
                for (int e = 0; e < 3; e++)
                {
                    long key = EdgeKey(cachedVertices[cachedTriangles[t * 3 + e]], cachedVertices[cachedTriangles[t * 3 + (e + 1) % 3]]);
                    if (!edgeMap.TryGetValue(key, out List<int> list)) edgeMap[key] = list = new List<int>(2);
                    list.Add(t);
                }
        }

        // Order-independent key of an edge from its two (quantised) end points, so split vertices still match.
        private static long EdgeKey(Vector3 a, Vector3 b)
        {
            long ka = PointKey(a), kb = PointKey(b);
            return ka < kb ? ka * 73856093L ^ kb : kb * 73856093L ^ ka;
        }

        private static long PointKey(Vector3 p)
        {
            long x = Mathf.RoundToInt(p.x * 1000f), y = Mathf.RoundToInt(p.y * 1000f), z = Mathf.RoundToInt(p.z * 1000f);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        // ------------------------------------------------------------------ building

        public void Rebuild()
        {
            dirty = false;
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            if (filter == null || renderer == null || sourceMesh == null) return;

            if (faces.Count == 0 || tileset == null || tileset.texture == null)
            {
                Restore();
                return;
            }
            if (!sourceMesh.isReadable)
            {
                Debug.LogWarning($"[Pixel Tile Mesh] '{sourceMesh.name}' is not readable: turn on Read/Write in its import settings.", this);
                return;
            }

            Vector3[] vertices = sourceMesh.vertices;
            Vector3[] normals = sourceMesh.normals;
            Vector4[] tangents = sourceMesh.tangents;
            Vector2[] uv = sourceMesh.uv;
            int subMeshes = sourceMesh.subMeshCount;

            // Which face tiles each source triangle.
            int totalTriangles = 0;
            var subTriangles = new int[subMeshes][];
            for (int s = 0; s < subMeshes; s++)
            {
                subTriangles[s] = sourceMesh.GetTriangles(s);
                totalTriangles += subTriangles[s].Length / 3;
            }
            var faceOf = new int[totalTriangles];
            for (int i = 0; i < faceOf.Length; i++) faceOf[i] = -1;
            for (int f = 0; f < faces.Count; f++)
                foreach (int t in faces[f].triangles)
                    if (t >= 0 && t < totalTriangles) faceOf[t] = f;

            // Untiled triangles keep their vertices and sub-mesh.
            var outVertices = new List<Vector3>(vertices);
            var outNormals = new List<Vector3>(normals.Length == vertices.Length ? normals : new Vector3[vertices.Length]);
            var outTangents = new List<Vector4>(tangents.Length == vertices.Length ? tangents : Fill(vertices.Length, new Vector4(1, 0, 0, 1)));
            var outUV = new List<Vector2>(uv.Length == vertices.Length ? uv : new Vector2[vertices.Length]);
            var outSubs = new List<int>[subMeshes + 1];
            int global = 0;
            for (int s = 0; s < subMeshes; s++)
            {
                outSubs[s] = new List<int>(subTriangles[s].Length);
                int[] tris = subTriangles[s];
                for (int i = 0; i + 2 < tris.Length; i += 3, global++)
                {
                    if (faceOf[global] >= 0) continue;
                    outSubs[s].Add(tris[i]);
                    outSubs[s].Add(tris[i + 1]);
                    outSubs[s].Add(tris[i + 2]);
                }
            }

            // Tiled faces: new vertices with tile UVs, in one extra sub-mesh.
            var buffer = new PixelMeshBuffer();
            int[] allTriangles = sourceMesh.triangles;
            Vector3 scale = transform.lossyScale;
            foreach (FaceTile face in faces) BuildFace(face, vertices, allTriangles, scale, buffer);

            int offset = outVertices.Count;
            outVertices.AddRange(buffer.vertices);
            outNormals.AddRange(buffer.normals);
            outTangents.AddRange(buffer.tangents);
            outUV.AddRange(buffer.uvs);
            outSubs[subMeshes] = new List<int>(buffer.triangles.Count);
            foreach (int i in buffer.triangles) outSubs[subMeshes].Add(i + offset);

            if (generated == null)
                generated = new Mesh { name = GeneratedName, hideFlags = HideFlags.DontSave };
            generated.Clear();
            generated.indexFormat = outVertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            generated.SetVertices(outVertices);
            generated.SetNormals(outNormals);
            generated.SetTangents(outTangents);
            generated.SetUVs(0, outUV);
            generated.subMeshCount = subMeshes + 1;
            for (int s = 0; s <= subMeshes; s++) generated.SetTriangles(outSubs[s], s);
            if (normals.Length != vertices.Length) generated.RecalculateNormals();
            generated.RecalculateBounds();
            filter.sharedMesh = generated;

            var materials = new Material[subMeshes + 1];
            for (int s = 0; s < subMeshes; s++)
                materials[s] = sourceMaterials != null && sourceMaterials.Length > 0 ? sourceMaterials[Mathf.Min(s, sourceMaterials.Length - 1)] : null;
            materials[subMeshes] = tileset.GetMaterial();
            renderer.sharedMaterials = materials;
        }

        private static T[] Fill<T>(int count, T value)
        {
            var array = new T[count];
            for (int i = 0; i < count; i++) array[i] = value;
            return array;
        }

        private void BuildFace(FaceTile face, Vector3[] vertices, int[] triangles, Vector3 scale, PixelMeshBuffer buffer)
        {
            if (face.triangles == null || face.triangles.Length == 0) return;

            // Face normal (local) and a tiling basis in scaled space: "up" on walls, "north" on floors.
            Vector3 localNormal = Vector3.zero;
            foreach (int t in face.triangles)
            {
                if (t * 3 + 2 >= triangles.Length) continue;
                Vector3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
                localNormal += Vector3.Cross(b - a, c - a);
            }
            if (localNormal.sqrMagnitude < 1e-12f) return;
            localNormal.Normalize();
            Vector3 n = Vector3.Scale(localNormal, new Vector3(1f / Mathf.Max(1e-5f, Mathf.Abs(scale.x)), 1f / Mathf.Max(1e-5f, Mathf.Abs(scale.y)), 1f / Mathf.Max(1e-5f, Mathf.Abs(scale.z)))).normalized;
            Vector3 reference = Mathf.Abs(n.y) < 0.7f ? Vector3.up : Vector3.forward;
            Vector3 vAxis = (reference - n * Vector3.Dot(reference, n)).normalized;
            Vector3 uAxis = Vector3.Cross(vAxis, -n).normalized;

            Rect rect = tileset.TileUV(face.tile);
            var points = new List<Vector3>(8);
            var uvs = new List<Vector2>(8);

            // Stretch: the face's extent in (u, v) maps to the whole tile.
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            if (!face.repeat)
                foreach (int t in face.triangles)
                    for (int k = 0; k < 3 && t * 3 + k < triangles.Length; k++)
                    {
                        Vector2 key = Project(vertices[triangles[t * 3 + k]], scale, uAxis, vAxis);
                        min = Vector2.Min(min, key);
                        max = Vector2.Max(max, key);
                    }
            Vector2 size = new(Mathf.Max(1e-5f, max.x - min.x), Mathf.Max(1e-5f, max.y - min.y));

            foreach (int t in face.triangles)
            {
                if (t * 3 + 2 >= triangles.Length) continue;
                var polygon = new List<PixelMeshBuffer.ClipVertex>(3);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = vertices[triangles[t * 3 + k]];
                    Vector2 key = Project(p, scale, uAxis, vAxis);
                    key = face.repeat ? key / tileWorldSize : new Vector2((key.x - min.x) / size.x, (key.y - min.y) / size.y);
                    polygon.Add(new PixelMeshBuffer.ClipVertex(key, p));
                }

                if (!face.repeat)
                {
                    Emit(polygon, Vector2.zero, rect, face, localNormal, buffer, points, uvs);
                    continue;
                }

                // Repeat: cut the triangle along the tile grid, one tile per cell.
                Vector2 lo = Vector2.Min(polygon[0].key, Vector2.Min(polygon[1].key, polygon[2].key));
                Vector2 hi = Vector2.Max(polygon[0].key, Vector2.Max(polygon[1].key, polygon[2].key));
                int x0 = Mathf.FloorToInt(lo.x + 1e-4f), x1 = Mathf.CeilToInt(hi.x - 1e-4f);
                int y0 = Mathf.FloorToInt(lo.y + 1e-4f), y1 = Mathf.CeilToInt(hi.y - 1e-4f);
                if ((long)(x1 - x0) * (y1 - y0) > 4096) { Emit(polygon, lo, rect, face, localNormal, buffer, points, uvs); continue; }
                for (int cx = x0; cx < x1; cx++)
                {
                    List<PixelMeshBuffer.ClipVertex> column = PixelMeshBuffer.Clip(polygon, 0, cx, true);
                    if (column.Count < 3) continue;
                    column = PixelMeshBuffer.Clip(column, 0, cx + 1, false);
                    if (column.Count < 3) continue;
                    for (int cy = y0; cy < y1; cy++)
                    {
                        List<PixelMeshBuffer.ClipVertex> cell = PixelMeshBuffer.Clip(column, 1, cy, true);
                        if (cell.Count < 3) continue;
                        cell = PixelMeshBuffer.Clip(cell, 1, cy + 1, false);
                        if (cell.Count < 3) continue;
                        Emit(cell, new Vector2(cx, cy), rect, face, localNormal, buffer, points, uvs);
                    }
                }
            }
        }

        private static Vector2 Project(Vector3 local, Vector3 scale, Vector3 uAxis, Vector3 vAxis)
        {
            Vector3 p = Vector3.Scale(local, scale);
            return new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis));
        }

        private static void Emit(List<PixelMeshBuffer.ClipVertex> polygon, Vector2 origin, Rect rect, FaceTile face,
            Vector3 normal, PixelMeshBuffer buffer, List<Vector3> points, List<Vector2> uvs)
        {
            points.Clear();
            uvs.Clear();
            foreach (PixelMeshBuffer.ClipVertex v in polygon)
            {
                points.Add(v.position);
                uvs.Add(PixelMeshBuffer.TileUV(rect, v.key - origin, face.rotation, face.flipX));
            }
            buffer.AddPolygon(points, uvs, normal);
        }
    }
}
