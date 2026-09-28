using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// A body of Pixel Water (use a material with the "RythmRPG/Pixel Water" shader). Registers the surface so
    /// characters make ripples, wakes and splashes in it (every <see cref="GrassInteractor"/> counts, the Weather
    /// Controller gives one to each CharacterController), rain rings it, and ground snow / puddles skip it.
    /// It can generate a flat grid mesh with enough vertices for the waves.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Rythm RPG/Environment/Pixel Water")]
    public sealed class PixelWater : MonoBehaviour
    {
        [Header("Surface")]
        [Tooltip("Build a flat grid mesh of the size below (on). Off = keep the mesh already on the Mesh Filter.")]
        [SerializeField] private bool generateMesh = true;
        [Tooltip("World size of the generated surface (X, Z), centred on this object.")]
        [SerializeField] private Vector2 size = new(8f, 6f);
        [Tooltip("Vertices per world unit of the generated mesh (for the waves).")]
        [SerializeField, Range(0.5f, 8f)] private float vertexDensity = 2f;

        [Header("Interaction")]
        [SerializeField] private bool interactive = true;
        [Tooltip("Ripple strength when something enters or leaves the water.")]
        [SerializeField, Range(0f, 3f)] private float splashStrength = 1.2f;
        [Tooltip("Wake strength while moving through the water (scaled by speed).")]
        [SerializeField, Range(0f, 3f)] private float wakeStrength = 0.6f;
        [Tooltip("Seconds between small ripples while standing still in the water (0 = off).")]
        [SerializeField, Min(0f)] private float idleRippleSeconds = 1.4f;
        [Tooltip("How deep an interactor's feet must be under the surface to count as in the water.")]
        [SerializeField, Min(0f)] private float contactDepth = 0.02f;
        [Tooltip("Optional effect spawned where something enters / leaves the water (e.g. a splash particle).")]
        [SerializeField] private GameObject splashEffect;
        [Tooltip("Rain drops per second per square unit at full rain (rings on the water).")]
        [SerializeField, Min(0f)] private float rainDropsPerUnit = 1.2f;

        private static readonly List<PixelWater> bodies = new();
        public static IReadOnlyList<PixelWater> All => bodies;

        private MeshRenderer meshRenderer;
        private Mesh generated;
        private Vector2 builtSize;
        private float builtDensity;

        public bool Interactive => interactive;
        public float SplashStrength => splashStrength;
        public float WakeStrength => wakeStrength;
        public float IdleRippleSeconds => idleRippleSeconds;
        public float ContactDepth => contactDepth;
        public GameObject SplashEffect => splashEffect;
        public float RainDropsPerUnit => rainDropsPerUnit;

        /// <summary>World height of the (flat) surface.</summary>
        public float SurfaceHeight
        {
            get
            {
                if (generateMesh || meshRenderer == null) return transform.position.y;
                return meshRenderer.bounds.center.y;
            }
        }

        /// <summary>World XZ rectangle covered by the surface.</summary>
        public Rect SurfaceRect
        {
            get
            {
                if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
                Bounds b = meshRenderer != null ? meshRenderer.bounds : new Bounds(transform.position, new Vector3(size.x, 0f, size.y));
                return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            }
        }

        /// <summary>True when <paramref name="point"/> is over this water (XZ) and at or below its surface.</summary>
        public bool Contains(Vector3 point, float above = 0f) =>
            SurfaceRect.Contains(new Vector2(point.x, point.z)) && point.y <= SurfaceHeight + above;

        public void SetSize(Vector2 newSize, bool generate = true)
        {
            size = new Vector2(Mathf.Max(0.1f, newSize.x), Mathf.Max(0.1f, newSize.y));
            generateMesh = generate;
            RebuildMesh();
        }

        private void OnEnable()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            if (!bodies.Contains(this)) bodies.Add(this);
            RebuildMesh();
        }

        private void OnDisable()
        {
            bodies.Remove(this);
            if (bodies.Count == 0) WaterSimulation.Release();
        }

        private void OnDestroy()
        {
            if (generated != null) DestroyImmediate(generated);
        }

        private void OnValidate()
        {
            size = new Vector2(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y));
            if (isActiveAndEnabled) RebuildMesh();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (bodies.Count > 0 && bodies[0] == this) WaterSimulation.Tick(Camera.main);
        }

        private void RebuildMesh()
        {
            if (!generateMesh) return;
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null) return;
            if (generated != null && filter.sharedMesh == generated && builtSize == size && Mathf.Approximately(builtDensity, vertexDensity))
                return;

            int nx = Mathf.Clamp(Mathf.CeilToInt(size.x * vertexDensity), 1, 254);
            int nz = Mathf.Clamp(Mathf.CeilToInt(size.y * vertexDensity), 1, 254);
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            for (int z = 0; z <= nz; z++)
            {
                for (int x = 0; x <= nx; x++)
                {
                    int i = z * (nx + 1) + x;
                    float u = (float)x / nx, v = (float)z / nz;
                    vertices[i] = new Vector3((u - 0.5f) * size.x, 0f, (v - 0.5f) * size.y);
                    uvs[i] = new Vector2(u, v);
                    normals[i] = Vector3.up;
                    tangents[i] = new Vector4(1f, 0f, 0f, 1f);
                }
            }
            var triangles = new int[nx * nz * 6];
            int t = 0;
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int a = z * (nx + 1) + x;
                    int b = a + 1;
                    int c = a + nx + 1;
                    int d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }
            if (generated == null)
                generated = new Mesh { name = "Pixel Water Surface", hideFlags = HideFlags.DontSave };
            generated.Clear();
            generated.vertices = vertices;
            generated.uv = uvs;
            generated.normals = normals;
            generated.tangents = tangents;
            generated.triangles = triangles;
            // Room for the waves.
            Bounds bounds = generated.bounds;
            bounds.Expand(new Vector3(0f, 1f, 0f));
            generated.bounds = bounds;
            filter.sharedMesh = generated;
            builtSize = size;
            builtDensity = vertexDensity;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => bodies.Clear();

        private void OnDrawGizmosSelected()
        {
            Rect r = SurfaceRect;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(r.center.x, SurfaceHeight, r.center.y), new Vector3(r.width, 0.01f, r.height));
        }
    }
}
