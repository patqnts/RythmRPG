using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Core
{
    /// <summary>
    /// A body of Pixel Water (use a material with the "RythmRPG/Pixel Water" shader). Registers the surface so
    /// characters make ripples, wakes and splashes in it (every <see cref="GrassInteractor"/> counts; Terrain
    /// Weather adds one to each CharacterController), rain rings it. The shader draws the ripples as pixel-outline
    /// rings, outlines the shore / anything standing in the water and wobbles the see-through image.
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

        [Header("Trail (rings left behind while moving)")]
        [Tooltip("How visible the trail rings are (0 = no trail). Faster movement makes them a bit stronger.")]
        [FormerlySerializedAs("wakeStrength")]
        [SerializeField, Range(0f, 3f)] private float trailStrength = 0.6f;
        [Tooltip("Distance moved between two trail rings (world units). Smaller = denser trail.")]
        [SerializeField, Range(0.04f, 1.5f)] private float trailSpacing = 0.18f;
        [Tooltip("Size of a trail ring when it appears (× the character's radius).")]
        [SerializeField, Range(0f, 2f)] private float trailStartSize = 0.45f;
        [Tooltip("How fast trail rings grow (world units per second).")]
        [SerializeField, Range(0f, 3f)] private float trailSpreadSpeed = 0.3f;
        [Tooltip("Extra growth speed per unit of the character's speed (fast movement throws wider rings).")]
        [SerializeField, Range(0f, 0.5f)] private float trailSpeedBoost = 0.06f;
        [Tooltip("Seconds a trail ring lives (it fades out over this time). Longer = longer trail.")]
        [SerializeField, Range(0.1f, 5f)] private float trailLifetime = 1.1f;

        [Header("Splash (falling in / Water.Splash)")]
        [Tooltip("How strong the splash rings and droplets are when something enters the water.")]
        [SerializeField, Range(0f, 3f)] private float splashStrength = 1.2f;
        [Tooltip("Splash size (× the character's radius; falling faster adds more).")]
        [SerializeField, Range(0.2f, 4f)] private float splashSize = 1f;

        [Header("Other")]
        [Tooltip("Seconds between small ripples while standing still in the water (0 = off).")]
        [SerializeField, Min(0f)] private float idleRippleSeconds = 1.4f;
        [Tooltip("How deep an interactor's feet must be under the surface to count as in the water.")]
        [SerializeField, Min(0f)] private float contactDepth = 0.02f;
        [Tooltip("Optional effect spawned where something enters / leaves the water (e.g. a splash particle).")]
        [SerializeField] private GameObject splashEffect;
        [Header("Rain")]
        [Tooltip("Rain drops per second per square unit at full rain (rings on the water).")]
        [SerializeField, Min(0f)] private float rainDropsPerUnit = 1.2f;
        [Tooltip("How big a rain ring grows before it fades (world units).")]
        [SerializeField, Range(0.05f, 1f)] private float rainRingSize = 0.22f;
        [Tooltip("Seconds a rain ring lives.")]
        [SerializeField, Range(0.1f, 2f)] private float rainRingLifetime = 0.55f;
        [Tooltip("Adds a Grass Interactor to every CharacterController so they make ripples.")]
        [SerializeField] private bool autoAddInteractors = true;

        private static readonly List<PixelWater> bodies = new();
        public static IReadOnlyList<PixelWater> All => bodies;

        private MeshRenderer meshRenderer;
        private Mesh generated;
        private Vector2 builtSize;
        private float builtDensity;
        private static float nextScan;

        public bool Interactive => interactive;
        public float SplashStrength => splashStrength;
        public float WakeStrength => trailStrength;
        public float TrailStrength => trailStrength;
        public float TrailSpacing => trailSpacing;
        public float TrailStartSize => trailStartSize;
        public float TrailSpreadSpeed => trailSpreadSpeed;
        public float TrailSpeedBoost => trailSpeedBoost;
        public float TrailLifetime => trailLifetime;
        public float SplashSize => splashSize;
        public float RainRingSize => rainRingSize;
        public float RainRingLifetime => rainRingLifetime;
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
            if (bodies.Count == 0 || bodies[0] != this) return;
            if (autoAddInteractors && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 2f;
                foreach (CharacterController controller in FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude))
                    if (controller != null && controller.GetComponent<GrassInteractor>() == null)
                        controller.gameObject.AddComponent<GrassInteractor>();
            }
            WaterSimulation.Tick(Camera.main);
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
        private static void ResetOnPlay()
        {
            bodies.Clear();
            nextScan = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Rect r = SurfaceRect;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(r.center.x, SurfaceHeight, r.center.y), new Vector3(r.width, 0.01f, r.height));
        }
    }
}
