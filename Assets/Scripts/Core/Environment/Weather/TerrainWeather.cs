using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    public enum TerrainWeatherKind
    {
        /// <summary>Real snow: a 3D snow mesh built from what you paint, lit and shadowed, with footprints.</summary>
        Snow,
        /// <summary>Low mist hugging the ground: soft smoke volumes that part around characters.</summary>
        Mist,
        /// <summary>Fog in the air: bigger soft smoke volumes that drift and part around characters.</summary>
        Fog
    }

    public enum SnowTrailStyle
    {
        /// <summary>Alternating left / right footprints every stride.</summary>
        Footprints,
        /// <summary>A continuous furrow (sleds, crawling rats, rolling balls).</summary>
        Trail
    }

    public enum FogBlend
    {
        /// <summary>Normal see-through smoke.</summary>
        Smoke,
        /// <summary>Glowing, adds light (like the God Rays' soft blending): morning haze, magic mist.</summary>
        Glow
    }

    /// <summary>
    /// Paintable snow, mist or fog, like the GPU grass: select it, press Paint in the Inspector and drag in the Scene
    /// view.
    /// <list type="bullet">
    /// <item><b>Snow</b>: every painted puff is a mound of snow; together they form one real 3D snow mesh with
    /// thickness (tapered edges, rounded drifts) that is lit by the sun and lamps, casts and receives shadows and
    /// hides the feet of whoever stands in it. Characters press footprints / furrows into it that fill in again.
    /// Glints sparkle in the sun.</item>
    /// <item><b>Mist / Fog</b>: every puff is a soft smoke volume (an ellipsoid). Its density is how much of the view
    /// ray passes through it in front of the scene, so objects inside it stay visible and things in front of it are
    /// not covered, like the God Rays' soft intersection. Smooth, drifting smoke noise, optional pixel dust motes.
    /// Characters part it.</item>
    /// </list>
    /// No renderer feature needed. Every <see cref="GrassInteractor"/> interacts (one is added to each
    /// CharacterController automatically).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Rythm RPG/Environment/Terrain Weather")]
    public sealed class TerrainWeather : MonoBehaviour
    {
        [Serializable]
        public struct Puff
        {
            public Vector3 position;
            public float radius;
            public float seed;
        }

        [SerializeField] private TerrainWeatherKind kind = TerrainWeatherKind.Mist;
        [SerializeField, HideInInspector] private List<Puff> puffs = new();

        // ------------------------------------------------------------------ snow
        [Header("Snow")]
        [Tooltip("Thickness of the snow at the middle of a painted mound (world units; 1 unit = 32 pixels).")]
        [SerializeField, Range(0.02f, 1.5f)] private float snowDepth = 0.28f;
        [Tooltip("Flat-topped drifts (1) or rounded mounds (0).")]
        [SerializeField, Range(0f, 1f)] private float snowFlatness = 0.6f;
        [Tooltip("Small bumps on the surface.")]
        [SerializeField, Range(0f, 1f)] private float snowBumps = 0.25f;
        [Tooltip("Mesh detail: vertices per world unit (16 = one every 2 pixels).")]
        [SerializeField, Range(4, 32)] private int snowResolution = 16;
        [Tooltip("Colour of the snow where the sun hits it.")]
        [SerializeField] private Color snowColor = new(0.96f, 0.97f, 1f, 1f);
        [Tooltip("Colour of the snow where the sun does not reach (its shaded side and shadows). A light blue-grey " +
                 "looks like real snow; set it close to Snow Color for less blue.")]
        [SerializeField] private Color snowShadowTint = new(0.8f, 0.84f, 0.93f, 1f);
        [Tooltip("Light steps on the snow (0 = smooth; 3-5 = painted pixel-art tones). No dithering either way.")]
        [SerializeField, Range(0, 8)] private int snowLightBands = 4;
        [Tooltip("Tiny glints that twinkle where the sun hits the snow.")]
        [SerializeField, Range(0f, 1f)] private float snowSparkle = 0.35f;
        [SerializeField] private bool snowCastShadows = true;

        // ------------------------------------------------------------------ mist / fog
        [Header("Mist / Fog")]
        [Tooltip("Width / height of each puff (× its radius). Mist is usually wide and low.")]
        [SerializeField] private Vector2 puffShape = new(2.2f, 0.9f);
        [Tooltip("How thick the smoke is (per world unit of view ray inside it).")]
        [SerializeField, Range(0.05f, 6f)] private float density = 1.4f;
        [Tooltip("Most opacity the smoke can reach.")]
        [SerializeField, Range(0f, 1f)] private float maxOpacity = 0.75f;
        [Tooltip("Mist: denser toward the bottom of each puff so it hugs the ground.")]
        [SerializeField, Range(0f, 1f)] private float groundHug = 0.6f;
        [Tooltip("How far into an object the smoke fades out (soft intersection, like the God Rays).")]
        [SerializeField, Min(0.01f)] private float softDistance = 0.35f;
        [SerializeField] private Color lightColor = new(0.96f, 0.97f, 1f, 1f);
        [SerializeField] private Color shadeColor = new(0.66f, 0.71f, 0.8f, 1f);
        [SerializeField] private FogBlend blending = FogBlend.Smoke;
        [Tooltip("Size of the smoke swirls (world units).")]
        [SerializeField, Min(0.05f)] private float noiseScale = 0.9f;
        [Tooltip("0 = smooth puffs, 1 = broken wispy smoke.")]
        [SerializeField, Range(0f, 1f)] private float wispiness = 0.65f;
        [Tooltip("How fast the smoke swirls and rises.")]
        [SerializeField, Min(0f)] private float swirlSpeed = 0.25f;
        [Tooltip("Pixel dust motes drifting in the smoke (0 = none).")]
        [SerializeField, Range(0f, 1f)] private float motes = 0.25f;
        [SerializeField, Min(0.05f)] private float moteSpacing = 0.3f;

        [Header("Light")]
        [Tooltip("How much the scene's light (sun colour, sky ambient) tints and darkens it. 0 = exactly your colours; " +
                 "1 = fully lit by the scene (a blue sky ambient turns snow blue).")]
        [SerializeField, Range(0f, 1f)] private float sceneLight = 0.7f;

        [Header("Motion (Mist / Fog)")]
        [Tooltip("Slow wander of every puff around its painted spot (world units).")]
        [SerializeField, Range(0f, 1f)] private float wobble = 0.12f;
        [SerializeField, Min(0f)] private float wobbleSpeed = 0.35f;
        [Tooltip("Puffs slowly swell and shrink (share of their size).")]
        [SerializeField, Range(0f, 0.5f)] private float breathe = 0.08f;
        [Tooltip("Drift speed (world units / second). Puffs drift within Drift Range and loop back smoothly.")]
        [SerializeField, Min(0f)] private float driftSpeed = 0.12f;
        [Tooltip("Drift direction in degrees on the ground (0 = +X).")]
        [SerializeField, Range(0f, 360f)] private float driftDirection = 20f;
        [SerializeField, Min(0.1f)] private float driftRange = 1.5f;
        [Tooltip("Add the Weather Controller's wind to the drift.")]
        [SerializeField, Range(0f, 1f)] private float windInfluence = 0.3f;

        [Header("Interaction")]
        [SerializeField] private bool interactive = true;
        [Tooltip("Mist / Fog: how far characters push puffs aside.")]
        [SerializeField, Range(0f, 3f)] private float pushStrength = 1f;
        [Tooltip("Mist / Fog: how much the smoke thins around a character.")]
        [SerializeField, Range(0f, 1f)] private float clearStrength = 0.85f;
        [Tooltip("Mist / Fog: seconds for parted smoke to flow back.")]
        [SerializeField, Min(0.1f)] private float recoverSeconds = 2.5f;
        [Tooltip("Snow: footprints or a continuous furrow.")]
        [SerializeField] private SnowTrailStyle trailStyle = SnowTrailStyle.Footprints;
        [Tooltip("Snow: print width as a share of each interactor's radius.")]
        [SerializeField, Range(0.1f, 2f)] private float trailWidth = 0.5f;
        [Tooltip("Snow: distance between footprints.")]
        [SerializeField, Min(0.05f)] private float strideLength = 0.42f;
        [Tooltip("Snow: how deep prints press in (1 = down to the ground).")]
        [SerializeField, Range(0f, 1f)] private float trailDepth = 0.8f;
        [Tooltip("Snow: seconds for prints to fill in again (faster while it snows).")]
        [SerializeField, Min(0.5f)] private float trailRefillSeconds = 40f;
        [Tooltip("Characters clear it away where they go: snow is swept down to the ground, mist / fog vanishes " +
                 "(on top of the footprints / parting above). Play mode only.")]
        [SerializeField] private bool clearOnTouch;
        [Tooltip("Width of the cleared path (× each character's radius).")]
        [SerializeField, Range(0.2f, 4f)] private float clearWidth = 1.2f;
        [Tooltip("Seconds for a cleared spot to come back (0 = stays cleared).")]
        [SerializeField, Min(0f)] private float clearRegrowSeconds;

        [Header("Other")]
        [Tooltip("Hide while the world is hidden (space combat).")]
        [SerializeField] private bool hideWithWorld = true;
        [Tooltip("Adds a Grass Interactor to every CharacterController so they interact.")]
        [SerializeField] private bool autoAddInteractors = true;

        private static readonly List<TerrainWeather> active = new();
        public static IReadOnlyList<TerrainWeather> Active => active;

        private GraphicsBuffer buffer;
        private int uploadedCount;
        private bool dirty = true;
        private Bounds bounds;
        private Material fogMaterial;
        private Material snowMaterial;
        private Mesh fogQuad;
        private Mesh snowMesh;
        private GameObject snowObject;
        private MaterialPropertyBlock fogBlock;
        private float nextScan;
        private int lastSubmitFrame = -1;

        // Clear On Touch: a runtime top-down mask over this field (0 = untouched, 255 = cleared).
        private const float ClearTexelsPerUnit = 8f;
        private const int ClearMaxTexels = 512 * 512;
        private byte[] clearMask;
        private Texture2D clearTexture;
        private int clearWidthTexels, clearHeightTexels;
        private Vector2 clearOrigin, clearSize;
        private bool clearAny;
        private float clearRegrowCarry;
        private readonly Dictionary<GrassInteractor, Vector3> clearLast = new();

        public TerrainWeatherKind Kind => kind;
        public List<Puff> Puffs => puffs;
        public int PuffCount => puffs.Count;
        public bool Interactive => interactive;
        public SnowTrailStyle TrailStyle => trailStyle;
        public float TrailWidth => trailWidth;
        public float StrideLength => strideLength;
        public float TrailRefillSeconds => trailRefillSeconds;
        public float RecoverSeconds => recoverSeconds;
        public Bounds Bounds => bounds;
        public bool Hidden => hideWithWorld && Application.isPlaying && SceneVisibility.WorldHidden;

        public void MarkDirty() => dirty = true;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            dirty = true;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            active.Remove(this);
            Release();
            if (active.Count == 0) WeatherInteractionMap.Release();
        }

        private void OnValidate()
        {
            dirty = true;
            UpdateSnowMaterial();
        }

        private void Reset() => ApplyKindDefaults(kind);

        private void Update()
        {
            if (dirty) Rebuild();
            TickClear();
            UpdateSnowMaterial();
            if (snowObject != null) snowObject.SetActive(kind == TerrainWeatherKind.Snow && !Hidden && puffs.Count > 0);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (active.Count == 0 || active[0] != this) return;
            if (autoAddInteractors && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 2f;
                foreach (CharacterController controller in FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude))
                    if (controller != null && controller.GetComponent<GrassInteractor>() == null)
                        controller.gameObject.AddComponent<GrassInteractor>();
            }
            WeatherInteractionMap.Tick(Camera.main);
        }

        private void Release()
        {
            buffer?.Release();
            buffer = null;
            uploadedCount = 0;
            DestroySafe(fogMaterial);
            DestroySafe(snowMaterial);
            DestroySafe(fogQuad);
            DestroySafe(snowMesh);
            DestroySafe(snowObject);
            DestroySafe(clearTexture);
            clearTexture = null;
            clearMask = null;
            clearLast.Clear();
            fogMaterial = null;
            snowMaterial = null;
            fogQuad = null;
            snowMesh = null;
            snowObject = null;
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ editing

        public void AddPuff(Vector3 position, float radius)
        {
            puffs.Add(new Puff { position = position, radius = Mathf.Max(0.05f, radius), seed = UnityEngine.Random.value });
            dirty = true;
        }

        public int RemoveInRadius(Vector3 center, float radius)
        {
            float r2 = radius * radius;
            int removed = puffs.RemoveAll(p =>
            {
                float dx = p.position.x - center.x, dz = p.position.z - center.z;
                return dx * dx + dz * dz <= r2;
            });
            if (removed > 0) dirty = true;
            return removed;
        }

        public bool HasPuffNear(Vector3 point, float spacing)
        {
            float s2 = spacing * spacing;
            foreach (Puff p in puffs)
                if ((p.position - point).sqrMagnitude < s2) return true;
            return false;
        }

        /// <summary>Settings that suit the kind (the Inspector's "Apply Look" button).</summary>
        public void ApplyKindDefaults(TerrainWeatherKind newKind)
        {
            kind = newKind;
            switch (newKind)
            {
                case TerrainWeatherKind.Snow:
                    snowDepth = 0.28f;
                    snowFlatness = 0.6f;
                    snowBumps = 0.25f;
                    snowColor = new Color(0.96f, 0.97f, 1f, 1f);
                    snowShadowTint = new Color(0.8f, 0.84f, 0.93f, 1f);
                    snowLightBands = 4;
                    snowSparkle = 0.35f;
                    sceneLight = 0.35f;
                    break;
                case TerrainWeatherKind.Mist:
                    puffShape = new Vector2(2.4f, 0.8f);
                    density = 1.6f;
                    maxOpacity = 0.7f;
                    groundHug = 0.7f;
                    softDistance = 0.3f;
                    lightColor = new Color(0.96f, 0.97f, 1f, 1f);
                    shadeColor = new Color(0.7f, 0.74f, 0.82f, 1f);
                    noiseScale = 0.8f;
                    wispiness = 0.6f;
                    swirlSpeed = 0.2f;
                    motes = 0.15f;
                    wobble = 0.12f;
                    breathe = 0.08f;
                    driftSpeed = 0.1f;
                    windInfluence = 0.2f;
                    sceneLight = 0.7f;
                    break;
                default:
                    puffShape = new Vector2(1.5f, 1f);
                    density = 0.9f;
                    maxOpacity = 0.6f;
                    groundHug = 0.2f;
                    softDistance = 0.6f;
                    lightColor = new Color(0.92f, 0.94f, 0.97f, 1f);
                    shadeColor = new Color(0.6f, 0.65f, 0.74f, 1f);
                    noiseScale = 1.4f;
                    wispiness = 0.7f;
                    swirlSpeed = 0.3f;
                    motes = 0.3f;
                    wobble = 0.2f;
                    breathe = 0.1f;
                    driftSpeed = 0.2f;
                    windInfluence = 0.5f;
                    sceneLight = 0.7f;
                    break;
            }
            dirty = true;
        }

        // ------------------------------------------------------------------ build

        private void Rebuild()
        {
            dirty = false;
            UploadPuffs();
            if (kind == TerrainWeatherKind.Snow)
            {
                BuildSnowMesh();
            }
            else if (snowObject != null)
            {
                DestroySafe(snowObject);
                snowObject = null;
            }
        }

        private void UploadPuffs()
        {
            int count = puffs.Count;
            float maxRadius = 0.1f;
            Vector3 min = transform.position, max = transform.position;
            if (count > 0)
            {
                min = Vector3.positiveInfinity;
                max = Vector3.negativeInfinity;
            }
            var data = new Vector4[Mathf.Max(1, count) * 2];
            for (int i = 0; i < count; i++)
            {
                Puff p = puffs[i];
                data[i * 2] = new Vector4(p.position.x, p.position.y, p.position.z, p.radius);
                data[i * 2 + 1] = new Vector4(p.seed, 0f, 0f, 0f);
                maxRadius = Mathf.Max(maxRadius, p.radius);
                min = Vector3.Min(min, p.position);
                max = Vector3.Max(max, p.position);
            }
            float reach = maxRadius * Mathf.Max(1f, Mathf.Max(puffShape.x, puffShape.y)) * 1.6f + wobble + driftRange + 1f;
            bounds = new Bounds((min + max) * 0.5f, max - min + Vector3.one * reach * 2f);

            if (kind == TerrainWeatherKind.Snow || count == 0)
            {
                buffer?.Release();
                buffer = null;
                uploadedCount = 0;
                return;
            }
            if (buffer == null || buffer.count < count * 2)
            {
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(Mathf.Max(32, count * 2)), 16);
            }
            buffer.SetData(data, 0, 0, count * 2);
            uploadedCount = count;
        }

        // Snow: every puff is a mound; the mounds are summed on a grid into one height field and meshed.
        private void BuildSnowMesh()
        {
            if (puffs.Count == 0)
            {
                if (snowObject != null) snowObject.SetActive(false);
                return;
            }

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (Puff p in puffs)
            {
                minX = Mathf.Min(minX, p.position.x - p.radius);
                minZ = Mathf.Min(minZ, p.position.z - p.radius);
                maxX = Mathf.Max(maxX, p.position.x + p.radius);
                maxZ = Mathf.Max(maxZ, p.position.z + p.radius);
            }
            float cell = 1f / Mathf.Max(4, snowResolution);
            // Keep the mesh under ~250k vertices.
            while ((maxX - minX) / cell * ((maxZ - minZ) / cell) > 250000f) cell *= 1.25f;
            int nx = Mathf.Max(2, Mathf.CeilToInt((maxX - minX) / cell) + 1);
            int nz = Mathf.Max(2, Mathf.CeilToInt((maxZ - minZ) / cell) + 1);

            var weight = new float[nx * nz];
            var baseY = new float[nx * nz];
            foreach (Puff p in puffs)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((p.position.x - p.radius - minX) / cell));
                int x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((p.position.x + p.radius - minX) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((p.position.z - p.radius - minZ) / cell));
                int z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((p.position.z + p.radius - minZ) / cell));
                float r2 = p.radius * p.radius;
                for (int z = z0; z <= z1; z++)
                {
                    float dz = minZ + z * cell - p.position.z;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = minX + x * cell - p.position.x;
                        float q = (dx * dx + dz * dz) / r2;
                        if (q >= 1f) continue;
                        float k = (1f - q) * (1f - q);
                        int i = z * nx + x;
                        weight[i] += k;
                        baseY[i] += k * p.position.y;
                    }
                }
            }

            var vertices = new Vector3[nx * nz];
            var depthUv = new Vector2[nx * nz];
            var normals = new Vector3[nx * nz];
            // Edges are tucked just under the ground so the snow rises out of it without a seam.
            const float bury = 0.04f;
            float flat = Mathf.Clamp01(snowFlatness);
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int i = z * nx + x;
                    float w = weight[i];
                    float wx = minX + x * cell, wz = minZ + z * cell;
                    float ground = w > 0.0001f ? baseY[i] / w : transform.position.y;
                    float c = Mathf.Clamp01(w);
                    float flatTop = 1f - (1f - c) * (1f - c) * (1f - c);
                    float h = snowDepth * Mathf.Lerp(c, flatTop, flat);
                    if (h > 0f && snowBumps > 0f)
                        h *= 1f + snowBumps * 0.35f * (Mathf.PerlinNoise(wx * 2.3f + 11f, wz * 2.3f + 5f) - 0.5f);
                    vertices[i] = new Vector3(wx, ground - bury + h, wz);
                    depthUv[i] = new Vector2(Mathf.Max(0f, h - bury * 0.5f), 0f);
                }
            }
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int i = z * nx + x;
                    float hl = vertices[z * nx + Mathf.Max(0, x - 1)].y, hr = vertices[z * nx + Mathf.Min(nx - 1, x + 1)].y;
                    float hd = vertices[Mathf.Max(0, z - 1) * nx + x].y, hu = vertices[Mathf.Min(nz - 1, z + 1) * nx + x].y;
                    normals[i] = new Vector3(hl - hr, 2f * cell, hd - hu).normalized;
                }
            }

            var triangles = new List<int>((nx - 1) * (nz - 1) * 3);
            for (int z = 0; z < nz - 1; z++)
            {
                for (int x = 0; x < nx - 1; x++)
                {
                    int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
                    if (weight[a] < 0.002f && weight[b] < 0.002f && weight[c] < 0.002f && weight[d] < 0.002f) continue;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            if (snowMesh == null)
                snowMesh = new Mesh { name = "Snow (generated)", hideFlags = HideFlags.HideAndDontSave };
            snowMesh.Clear();
            snowMesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            snowMesh.vertices = vertices;
            snowMesh.normals = normals;
            snowMesh.SetUVs(1, depthUv);
            snowMesh.SetTriangles(triangles, 0);
            snowMesh.RecalculateBounds();
            Bounds meshBounds = snowMesh.bounds;
            meshBounds.Expand(0.2f);
            snowMesh.bounds = meshBounds;
            bounds = meshBounds;

            EnsureSnowObject();
        }

        private void EnsureSnowObject()
        {
            if (snowObject == null)
            {
                snowObject = new GameObject("Snow Mesh (generated)") { hideFlags = HideFlags.HideAndDontSave };
                snowObject.AddComponent<MeshFilter>();
                snowObject.AddComponent<MeshRenderer>();
            }
            // The mesh is built in world space, so the object stays at the origin (not parented, not scaled).
            snowObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            snowObject.transform.localScale = Vector3.one;
            snowObject.layer = gameObject.layer;
            snowObject.GetComponent<MeshFilter>().sharedMesh = snowMesh;
            var renderer = snowObject.GetComponent<MeshRenderer>();
            if (snowMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Rendering/Weather/TerrainSnow");
                if (shader != null)
                    snowMaterial = new Material(shader) { name = "Snow (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            renderer.sharedMaterial = snowMaterial;
            renderer.receiveShadows = true;
            UpdateSnowMaterial();
            snowObject.SetActive(!Hidden);
        }

        private static readonly int SnowColorId = Shader.PropertyToID("_SnowColor");
        private static readonly int SnowShadowId = Shader.PropertyToID("_SnowShadowTint");
        private static readonly int SnowParamsId = Shader.PropertyToID("_SnowParams");
        private static readonly int SnowLightId = Shader.PropertyToID("_SnowLight");
        private static readonly int ClearTexId = Shader.PropertyToID("_ClearTex");
        private static readonly int ClearParamsId = Shader.PropertyToID("_ClearParams");

        private void UpdateSnowMaterial()
        {
            if (snowMaterial == null || kind != TerrainWeatherKind.Snow) return;
            snowMaterial.SetColor(SnowColorId, snowColor);
            snowMaterial.SetColor(SnowShadowId, snowShadowTint);
            snowMaterial.SetVector(SnowParamsId, new Vector4(snowLightBands, snowSparkle, interactive ? trailDepth : 0f, 32f));
            snowMaterial.SetVector(SnowLightId, new Vector4(sceneLight, 0f, 0f, 0f));
            snowMaterial.SetTexture(ClearTexId, ClearTexture);
            snowMaterial.SetVector(ClearParamsId, ClearParams);
            if (snowObject != null)
                snowObject.GetComponent<MeshRenderer>().shadowCastingMode = snowCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ clear on touch

        private Texture ClearTexture => clearTexture != null && clearAny ? clearTexture : Texture2D.blackTexture;

        // x, z = origin, zw = 1 / size (0 when there is no mask, so the shader reads nothing).
        private Vector4 ClearParams => clearTexture != null && clearAny
            ? new Vector4(clearOrigin.x, clearOrigin.y, 1f / clearSize.x, 1f / clearSize.y)
            : Vector4.zero;

        private void TickClear()
        {
            if (!Application.isPlaying || !clearOnTouch || !interactive || puffs.Count == 0 || Hidden) return;
            EnsureClearMask();
            bool changed = false;

            // Regrow.
            if (clearAny && clearRegrowSeconds > 0f)
            {
                clearRegrowCarry += Time.deltaTime / clearRegrowSeconds * 255f;
                int step = Mathf.FloorToInt(clearRegrowCarry);
                if (step > 0)
                {
                    clearRegrowCarry -= step;
                    bool any = false;
                    for (int i = 0; i < clearMask.Length; i++)
                    {
                        int v = clearMask[i];
                        if (v == 0) continue;
                        v = Mathf.Max(0, v - step);
                        clearMask[i] = (byte)v;
                        any |= v > 0;
                    }
                    clearAny = any;
                    changed = true;
                }
            }

            if (clearLast.Count > 64) clearLast.Clear();

            // Stamp every character inside the field (a segment from last frame, so fast movers leave no gaps).
            float minY = bounds.min.y - 0.6f, maxY = bounds.max.y + 0.6f;
            foreach (GrassInteractor interactor in GrassInteractor.Active)
            {
                if (interactor == null || !interactor.isActiveAndEnabled) continue;
                Vector3 p = interactor.Position;
                float feet = interactor.FeetHeight;
                if (feet > maxY || p.y < minY) { clearLast.Remove(interactor); continue; }
                if (!clearLast.TryGetValue(interactor, out Vector3 last) || (last - p).sqrMagnitude > 4f) last = p;
                clearLast[interactor] = p;
                float radius = Mathf.Max(0.05f, interactor.Radius * clearWidth);
                if (p.x + radius < bounds.min.x || p.x - radius > bounds.max.x || p.z + radius < bounds.min.z || p.z - radius > bounds.max.z)
                    continue;
                changed |= StampClear(new Vector2(last.x, last.z), new Vector2(p.x, p.z), radius);
            }

            if (changed)
            {
                clearTexture.SetPixelData(clearMask, 0);
                clearTexture.Apply(false, false);
            }
        }

        private void EnsureClearMask()
        {
            Vector2 origin = new Vector2(bounds.min.x, bounds.min.z);
            Vector2 size = new Vector2(Mathf.Max(0.5f, bounds.size.x), Mathf.Max(0.5f, bounds.size.z));
            if (clearMask != null && clearTexture != null && (origin - clearOrigin).sqrMagnitude < 0.0001f
                && (size - clearSize).sqrMagnitude < 0.0001f) return;

            float tpu = ClearTexelsPerUnit;
            if (size.x * size.y * tpu * tpu > ClearMaxTexels) tpu = Mathf.Sqrt(ClearMaxTexels / (size.x * size.y));
            int w = Mathf.Clamp(Mathf.CeilToInt(size.x * tpu), 1, 2048);
            int h = Mathf.Clamp(Mathf.CeilToInt(size.y * tpu), 1, 2048);

            // Keep what was cleared if the field only grew / moved a little.
            byte[] previous = clearMask;
            int pw = clearWidthTexels, ph = clearHeightTexels;
            Vector2 po = clearOrigin, ps = clearSize;

            clearMask = new byte[w * h];
            clearWidthTexels = w;
            clearHeightTexels = h;
            clearOrigin = origin;
            clearSize = size;
            clearAny = false;
            if (previous != null)
            {
                for (int z = 0; z < h; z++)
                {
                    float wz = origin.y + (z + 0.5f) / h * size.y;
                    int oz = Mathf.FloorToInt((wz - po.y) / ps.y * ph);
                    if (oz < 0 || oz >= ph) continue;
                    for (int x = 0; x < w; x++)
                    {
                        float wx = origin.x + (x + 0.5f) / w * size.x;
                        int ox = Mathf.FloorToInt((wx - po.x) / ps.x * pw);
                        if (ox < 0 || ox >= pw) continue;
                        byte v = previous[oz * pw + ox];
                        clearMask[z * w + x] = v;
                        clearAny |= v > 0;
                    }
                }
            }

            if (clearTexture == null || clearTexture.width != w || clearTexture.height != h)
            {
                DestroySafe(clearTexture);
                clearTexture = new Texture2D(w, h, TextureFormat.R8, false, true)
                {
                    name = "Terrain Weather Clear Mask",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }
            clearTexture.SetPixelData(clearMask, 0);
            clearTexture.Apply(false, false);
        }

        // Soft-edged capsule from a to b (world XZ). Returns true if anything changed.
        private bool StampClear(Vector2 a, Vector2 b, float radius)
        {
            float sx = clearWidthTexels / clearSize.x, sz = clearHeightTexels / clearSize.y;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius - clearOrigin.x) * sx));
            int x1 = Mathf.Min(clearWidthTexels - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + radius - clearOrigin.x) * sx));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - radius - clearOrigin.y) * sz));
            int z1 = Mathf.Min(clearHeightTexels - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + radius - clearOrigin.y) * sz));
            if (x1 < x0 || z1 < z0) return false;

            Vector2 ab = b - a;
            float abLength2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            float edge = radius * 0.35f;
            bool changed = false;
            for (int z = z0; z <= z1; z++)
            {
                float wz = clearOrigin.y + (z + 0.5f) / sz;
                for (int x = x0; x <= x1; x++)
                {
                    float wx = clearOrigin.x + (x + 0.5f) / sx;
                    Vector2 p = new Vector2(wx, wz);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLength2);
                    float d = Vector2.Distance(p, a + ab * t);
                    if (d >= radius) continue;
                    float v = Mathf.Clamp01((radius - d) / edge);
                    v = v * v * (3f - 2f * v);
                    byte value = (byte)Mathf.RoundToInt(v * 255f);
                    int i = z * clearWidthTexels + x;
                    if (value <= clearMask[i]) continue;
                    clearMask[i] = value;
                    changed = true;
                }
            }
            clearAny |= changed;
            return changed;
        }

        // ------------------------------------------------------------------ mist / fog drawing

        private static readonly int PuffsId = Shader.PropertyToID("_Puffs");
        private static readonly int ShapeId = Shader.PropertyToID("_FogShape");
        private static readonly int DensityId = Shader.PropertyToID("_FogDensity");
        private static readonly int NoiseId = Shader.PropertyToID("_FogNoise");
        private static readonly int MotionId = Shader.PropertyToID("_FogMotion");
        private static readonly int DriftId = Shader.PropertyToID("_FogDrift");
        private static readonly int InteractId = Shader.PropertyToID("_FogInteract");
        private static readonly int LightColorId = Shader.PropertyToID("_FogLightColor");
        private static readonly int ShadeColorId = Shader.PropertyToID("_FogShadeColor");
        private static readonly int LightId = Shader.PropertyToID("_FogLight");
        private static readonly int ViewDirId = Shader.PropertyToID("_FogViewDir");
        private static readonly int MotesId = Shader.PropertyToID("_FogMotes");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly Vector3[] upDirection = { Vector3.up };
        private static readonly Color[] ambientSample = new Color[1];

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (Time.renderedFrameCount == lastSubmitFrame) return;
            lastSubmitFrame = Time.renderedFrameCount;
            if (!isActiveAndEnabled || kind == TerrainWeatherKind.Snow || Hidden) return;
            if (dirty) Rebuild();
            if (buffer == null || uploadedCount == 0) return;
            if (!EnsureFogResources()) return;

            Camera view = Camera.main;
            Vector3 viewDir = view != null ? ObliqueProjection.ViewDirection(view) : Vector3.forward;
            Vector2 wind = Weather.Current.Wind;
            float driftAngle = driftDirection * Mathf.Deg2Rad;
            Vector2 drift = new Vector2(Mathf.Cos(driftAngle), Mathf.Sin(driftAngle)) * driftSpeed + wind * windInfluence * 0.15f;
            float speed = drift.magnitude;
            Vector2 dir = speed > 0.0001f ? drift / speed : Vector2.right;
            float time = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600.0);

            fogMaterial.SetFloat(SrcBlendId, blending == FogBlend.Glow ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            fogMaterial.SetFloat(DstBlendId, blending == FogBlend.Glow ? (float)BlendMode.OneMinusSrcColor : (float)BlendMode.OneMinusSrcAlpha);
            fogBlock.SetBuffer(PuffsId, buffer);
            fogBlock.SetVector(ShapeId, new Vector4(Mathf.Max(0.1f, puffShape.x), Mathf.Max(0.1f, puffShape.y), groundHug, softDistance));
            fogBlock.SetVector(DensityId, new Vector4(density, maxOpacity, blending == FogBlend.Glow ? 1f : 0f, 0f));
            fogBlock.SetVector(NoiseId, new Vector4(1f / Mathf.Max(0.05f, noiseScale), wispiness, swirlSpeed, 0f));
            fogBlock.SetVector(MotionId, new Vector4(wobble, wobbleSpeed, breathe, time));
            fogBlock.SetVector(DriftId, new Vector4(dir.x, dir.y, speed, driftRange));
            fogBlock.SetVector(InteractId, new Vector4(interactive ? pushStrength : 0f, interactive ? clearStrength : 0f, 0f, 0f));
            fogBlock.SetColor(LightColorId, lightColor);
            fogBlock.SetColor(ShadeColorId, shadeColor);
            fogBlock.SetVector(LightId, SceneLight(sceneLight));
            fogBlock.SetVector(ViewDirId, viewDir);
            fogBlock.SetVector(MotesId, new Vector4(motes, moteSpacing, 32f, 0f));
            fogBlock.SetTexture(ClearTexId, ClearTexture);
            fogBlock.SetVector(ClearParamsId, ClearParams);

            var rp = new RenderParams(fogMaterial)
            {
                layer = gameObject.layer,
                worldBounds = bounds,
                matProps = fogBlock,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };
            Graphics.RenderMeshPrimitives(rp, fogQuad, 0, uploadedCount);
        }

        private bool EnsureFogResources()
        {
            if (fogMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Rendering/Weather/TerrainFog");
                if (shader == null) return false;
                fogMaterial = new Material(shader) { name = "Terrain Fog (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            if (fogQuad == null)
            {
                fogQuad = new Mesh { name = "Terrain Fog Quad", hideFlags = HideFlags.HideAndDontSave };
                fogQuad.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) });
                fogQuad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
                fogQuad.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            }
            fogBlock ??= new MaterialPropertyBlock();
            return true;
        }

        // rgb = light multiplier (1 = unlit).
        private static Vector4 SceneLight(float amount)
        {
            Color ambient;
            if (RenderSettings.ambientMode == AmbientMode.Flat) ambient = RenderSettings.ambientLight;
            else if (RenderSettings.ambientMode == AmbientMode.Trilight) ambient = RenderSettings.ambientSkyColor;
            else
            {
                RenderSettings.ambientProbe.Evaluate(upDirection, ambientSample);
                ambient = ambientSample[0];
            }
            Color light = ambient;
            Light sun = RenderSettings.sun;
            if (sun != null && sun.isActiveAndEnabled) light += sun.color * sun.intensity * 0.75f;
            float max = Mathf.Max(light.r, Mathf.Max(light.g, light.b));
            if (max > 1f) light /= max;
            return new Vector4(Mathf.Lerp(1f, Mathf.Max(light.r, 0.25f), amount), Mathf.Lerp(1f, Mathf.Max(light.g, 0.25f), amount),
                Mathf.Lerp(1f, Mathf.Max(light.b, 0.3f), amount), 0f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => active.Clear();

        private void OnDrawGizmosSelected()
        {
            if (puffs.Count == 0) return;
            Gizmos.color = new Color(0.7f, 0.85f, 1f, 0.5f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
