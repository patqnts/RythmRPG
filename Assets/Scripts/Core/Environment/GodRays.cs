using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// All-in-one god rays: soft light shafts falling through an opening (tree canopy, window, cave crack), with
    /// light pools where they land, drifting dust motes, shimmer, and pixel-art banding / ordered dithering.
    /// <para>
    /// Everything is one small mesh and one material on this object (a few quads per beam), additive, no post
    /// processing and no textures, so it costs almost nothing. Beams are ribbons turned to face the real view
    /// direction (<see cref="ObliqueProjection.ViewDirection"/>), so they keep their full width under the oblique
    /// camera. They are drawn into the 480x270 pixel render, so bands and dither land on whole pixels.
    /// </para>
    /// <para>Create one with GameObject > Rythm RPG > God Rays, then pick a preset in the Inspector.</para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Rythm RPG/Environment/God Rays")]
    public sealed class GodRays : MonoBehaviour
    {
        public enum DirectionSource { Custom, SunLight }
        public enum Blending { Additive, Soft }
        public enum Preset { ForestCanopy, WindowLight, HolyBeam, CaveCrack, DustyRoom, Moonlight }

        private const string ShaderPath = "Rendering/GodRays";
        private const int Segments = 8;

        [Header("Opening")]
        [Tooltip("Size of the opening the light comes through (X = width, Y = depth, world units), centred on this " +
                 "object and turned with its Y rotation.")]
        [SerializeField] private Vector2 sourceSize = new(3f, 1.5f);
        [Tooltip("Height of the opening above this object.")]
        [SerializeField, Min(0.1f)] private float sourceHeight = 5f;

        [Header("Direction")]
        [SerializeField] private DirectionSource direction = DirectionSource.Custom;
        [Tooltip("Downward angle of the light (90 = straight down).")]
        [SerializeField, Range(15f, 90f)] private float pitch = 62f;
        [Tooltip("Which way the light travels across the ground (degrees, 0 = toward +Z / away from the camera).")]
        [SerializeField, Range(-180f, 180f)] private float yaw = -130f;
        [Tooltip("Sun Light mode: the light to follow (empty = the scene's sun / first directional light).")]
        [SerializeField] private Light sun;

        [Header("Ground")]
        [Tooltip("Beams end on the plane at this height relative to this object...")]
        [SerializeField] private float groundOffset;
        [Tooltip("...or on the first collider they hit.")]
        [SerializeField] private bool raycastGround = true;
        [SerializeField] private LayerMask groundLayers = ~(1 << 2);

        [Header("Beams")]
        [SerializeField, Range(1, 32)] private int beamCount = 5;
        [Tooltip("Random width of each beam at the opening (world units).")]
        [SerializeField] private Vector2 widthRange = new(0.25f, 0.9f);
        [Tooltip("Width at the ground relative to the opening (light spreads a little).")]
        [SerializeField, Range(0.5f, 3f)] private float endWidth = 1.35f;
        [Tooltip("Random brightness difference between beams.")]
        [SerializeField, Range(0f, 1f)] private float brightnessJitter = 0.45f;
        [SerializeField] private int seed = 7;

        [Header("Colour")]
        [SerializeField, ColorUsage(false, true)] private Color topColor = new(1f, 0.95f, 0.75f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color bottomColor = new(1f, 0.82f, 0.52f, 1f);
        [SerializeField, Range(0f, 4f)] private float intensity = 0.55f;
        [Tooltip("Additive = bright, glowing. Soft = screen-like, never blows out to white.")]
        [SerializeField] private Blending blending = Blending.Additive;

        [Header("Shape")]
        [SerializeField, Range(0.02f, 1f)] private float edgeSoftness = 0.55f;
        [Tooltip("Fade-in length at the opening (0..1 of the beam).")]
        [SerializeField, Range(0f, 1f)] private float topFade = 0.2f;
        [Tooltip("Fade-out length toward the ground (0..1 of the beam).")]
        [SerializeField, Range(0f, 1f)] private float bottomFade = 0.4f;

        [Header("Animation")]
        [Tooltip("Slow streaks running down the beams.")]
        [SerializeField, Range(0f, 1f)] private float streaks = 0.4f;
        [SerializeField, Min(0.01f)] private float streakScale = 1.5f;
        [SerializeField] private float streakSpeed = 0.25f;
        [Tooltip("Beams slowly brighten and dim (like leaves moving in front of the sun).")]
        [SerializeField, Range(0f, 1f)] private float shimmer = 0.3f;
        [SerializeField, Min(0f)] private float shimmerSpeed = 0.5f;

        [Header("Light pools")]
        [SerializeField] private bool groundPools = true;
        [SerializeField, Range(0.5f, 4f)] private float poolSize = 1.6f;
        [SerializeField, Range(0f, 2f)] private float poolIntensity = 0.45f;

        [Header("Dust motes")]
        [SerializeField] private bool dustMotes = true;
        [SerializeField, Range(0f, 1f)] private float moteDensity = 0.3f;
        [Tooltip("Average spacing of the motes (world units).")]
        [SerializeField, Min(0.05f)] private float moteSpacing = 0.35f;
        [SerializeField] private float moteFallSpeed = 0.12f;
        [SerializeField, Range(0f, 4f)] private float moteBrightness = 1.3f;
        [Tooltip("Mote size in render-texture pixels.")]
        [SerializeField, Range(1, 3)] private int motePixels = 1;

        [Header("Pixel art")]
        [Tooltip("Posterise the light into this many steps (0 = smooth).")]
        [SerializeField, Range(0, 16)] private int colorBands = 5;
        [Tooltip("Ordered (Bayer) dithering between the steps, on the render-texture pixel grid.")]
        [SerializeField] private bool dither = true;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;

        [Header("Depth")]
        [Tooltip("Fade beams where they meet geometry (needs the camera's Depth Texture).")]
        [SerializeField] private bool softIntersection = true;
        [SerializeField, Min(0.01f)] private float softDistance = 0.6f;

        [Header("Advanced")]
        [Tooltip("Optional material using the RythmRPG/God Rays shader (otherwise one is made at runtime).")]
        [SerializeField] private Material materialOverride;

        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");
        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int StreaksId = Shader.PropertyToID("_Streaks");
        private static readonly int ShimmerId = Shader.PropertyToID("_Shimmer");
        private static readonly int MotesId = Shader.PropertyToID("_Motes");
        private static readonly int PixelId = Shader.PropertyToID("_Pixel");

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material runtimeMaterial;
        private bool dirty = true;
        private Vector3 builtViewDirection;
        private Vector3 builtLightDirection;
        private bool missingShaderReported;

        private readonly List<Vector3> vertices = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<Vector4> beamData = new();
        private readonly List<Color> colors = new();
        private readonly List<int> triangles = new();

        /// <summary>Rebuilds the beams (immediately in Edit mode, next frame in Play mode).</summary>
        public void MarkDirty()
        {
            dirty = true;
            if (!Application.isPlaying && isActiveAndEnabled) Refresh();
        }

        // ---------- Lifecycle ----------

        private void OnEnable()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            dirty = true;
            Refresh();
        }

        private void OnDisable()
        {
            if (meshFilter != null && meshFilter.sharedMesh == mesh) meshFilter.sharedMesh = null;
        }

        private void OnDestroy()
        {
            SafeDestroy(mesh);
            SafeDestroy(runtimeMaterial);
        }

        private void OnValidate()
        {
            widthRange.x = Mathf.Max(0.02f, widthRange.x);
            widthRange.y = Mathf.Max(widthRange.x, widthRange.y);
            sourceSize = Vector2.Max(sourceSize, Vector2.one * 0.05f);
            dirty = true;
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (meshRenderer == null) return;
            if (!EnsureMaterial()) return;
            ApplyMaterial();

            Vector3 view = ViewDirection();
            Vector3 light = LightDirection();
            if (transform.hasChanged)
            {
                transform.hasChanged = false;
                dirty = true;
            }
            if (Vector3.Angle(view, builtViewDirection) > 0.25f || Vector3.Angle(light, builtLightDirection) > 0.25f)
                dirty = true;
            if (dirty) BuildMesh(view, light);
        }

        // ---------- Directions ----------

        /// <summary>World direction the light travels (pointing down).</summary>
        public Vector3 LightDirection()
        {
            if (direction == DirectionSource.SunLight)
            {
                Light source = sun != null ? sun : RenderSettings.sun;
                if (source == null)
                {
                    foreach (Light candidate in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                        if (candidate.type == LightType.Directional) { source = candidate; break; }
                }
                if (source != null)
                {
                    Vector3 forward = source.transform.forward;
                    if (forward.y < -0.05f) return forward.normalized;
                }
            }
            return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        private static Vector3 ViewDirection()
        {
            Camera view = Camera.main;
            if (view == null) return new Vector3(0f, -0.7071f, 0.7071f);
            return ObliqueProjection.ViewDirection(view);
        }

        // ---------- Mesh ----------

        private void BuildMesh(Vector3 view, Vector3 light)
        {
            dirty = false;
            builtViewDirection = view;
            builtLightDirection = light;

            vertices.Clear(); uvs.Clear(); beamData.Clear(); colors.Clear(); triangles.Clear();

            var random = new System.Random(seed);
            Vector3 origin = transform.position;
            Quaternion yawOnly = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 openingRight = yawOnly * Vector3.right;
            Vector3 openingForward = yawOnly * Vector3.forward;
            float groundY = origin.y + groundOffset;

            Vector3 side = Vector3.Cross(light, view);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();
            Vector3 flatSide = Vector3.ProjectOnPlane(side, Vector3.up);
            flatSide = flatSide.sqrMagnitude > 0.0001f ? flatSide.normalized : Vector3.right;
            Vector3 flatTravel = Vector3.ProjectOnPlane(light, Vector3.up);
            flatTravel = flatTravel.sqrMagnitude > 0.0001f ? flatTravel.normalized : Vector3.forward;
            float slope = Mathf.Clamp(1f / Mathf.Max(0.3f, -light.y), 1f, 2.2f);

            for (int b = 0; b < beamCount; b++)
            {
                float rx = (float)random.NextDouble() - 0.5f;
                float rz = (float)random.NextDouble() - 0.5f;
                // Spread beams evenly across the width, with jitter, so they do not clump.
                if (beamCount > 1) rx = Mathf.Lerp(-0.5f, 0.5f, (b + 0.5f + (rx * 0.8f)) / beamCount);
                float width = Mathf.Lerp(widthRange.x, widthRange.y, (float)random.NextDouble());
                float brightness = 1f - brightnessJitter * (float)random.NextDouble();
                float beamRandom = (float)random.NextDouble();

                Vector3 start = origin + openingRight * (rx * sourceSize.x) + openingForward * (rz * sourceSize.y)
                                + Vector3.up * sourceHeight;
                float length = -light.y > 0.0001f ? (start.y - groundY) / -light.y : sourceHeight;
                if (length <= 0.01f) continue;
                if (raycastGround && Physics.Raycast(start, light, out RaycastHit hit, length * 1.5f, groundLayers,
                        QueryTriggerInteraction.Ignore))
                    length = hit.distance;
                Vector3 end = start + light * length;

                AddBeam(start, end, side, width, width * endWidth, length, brightness, beamRandom);
                if (groundPools && poolIntensity > 0f)
                    AddPool(end, flatSide, flatTravel, width * endWidth * poolSize * 0.5f, slope, brightness, beamRandom);
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "God Rays (runtime)", hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, beamData);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.Expand(0.5f);
            mesh.bounds = bounds;
            meshFilter.sharedMesh = mesh;
        }

        private void AddBeam(Vector3 start, Vector3 end, Vector3 side, float topWidth, float bottomWidth, float length,
            float brightness, float beamRandom)
        {
            int first = vertices.Count;
            var data = new Vector4((topWidth + bottomWidth) * 0.5f, length, beamRandom, 0f);
            for (int s = 0; s <= Segments; s++)
            {
                float v = s / (float)Segments;
                Vector3 center = Vector3.Lerp(start, end, v);
                float half = Mathf.Lerp(topWidth, bottomWidth, v) * 0.5f;
                AddVertex(center - side * half, new Vector2(0f, v), data, brightness);
                AddVertex(center + side * half, new Vector2(1f, v), data, brightness);
            }
            for (int s = 0; s < Segments; s++)
            {
                int a = first + s * 2;
                triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3);
            }
        }

        private void AddPool(Vector3 center, Vector3 flatSide, Vector3 flatTravel, float radius, float stretch,
            float brightness, float beamRandom)
        {
            int first = vertices.Count;
            var data = new Vector4(radius * 2f, radius * 2f * stretch, beamRandom, 1f);
            Vector3 lift = Vector3.up * 0.02f;
            Vector3 a = flatSide * radius;
            Vector3 b = flatTravel * radius * stretch;
            center += flatTravel * radius * stretch * 0.2f + lift;
            AddVertex(center - a - b, new Vector2(0f, 0f), data, brightness);
            AddVertex(center + a - b, new Vector2(1f, 0f), data, brightness);
            AddVertex(center - a + b, new Vector2(0f, 1f), data, brightness);
            AddVertex(center + a + b, new Vector2(1f, 1f), data, brightness);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
            triangles.Add(first + 1); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        private void AddVertex(Vector3 world, Vector2 uv, Vector4 data, float brightness)
        {
            vertices.Add(transform.InverseTransformPoint(world));
            uvs.Add(uv);
            beamData.Add(data);
            colors.Add(new Color(brightness, 1f, 1f, 1f));
        }

        // ---------- Material ----------

        private bool EnsureMaterial()
        {
            if (materialOverride != null)
            {
                if (meshRenderer.sharedMaterial != materialOverride) meshRenderer.sharedMaterial = materialOverride;
                return true;
            }
            if (runtimeMaterial == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null)
                {
                    if (!missingShaderReported)
                        Debug.LogWarning("[God Rays] Shader missing (Assets/Resources/Rendering/GodRays.shader).", this);
                    missingShaderReported = true;
                    return false;
                }
                runtimeMaterial = new Material(shader) { name = "God Rays (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            if (meshRenderer.sharedMaterial != runtimeMaterial) meshRenderer.sharedMaterial = runtimeMaterial;
            return true;
        }

        private void ApplyMaterial()
        {
            Material m = meshRenderer.sharedMaterial;
            if (m == null) return;
            bool soft = blending == Blending.Soft;
            m.SetFloat(SrcBlendId, soft ? (float)UnityEngine.Rendering.BlendMode.OneMinusDstColor : (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.One);
            m.SetColor(TopColorId, topColor);
            m.SetColor(BottomColorId, bottomColor);
            m.SetVector(ShapeId, new Vector4(edgeSoftness, topFade, bottomFade, intensity));
            m.SetVector(StreaksId, new Vector4(streaks, streakScale, streakSpeed, softDistance));
            m.SetVector(ShimmerId, new Vector4(shimmer, shimmerSpeed, poolIntensity, 0f));
            m.SetVector(MotesId, new Vector4(dustMotes ? moteDensity : 0f, moteSpacing, moteFallSpeed, moteBrightness));
            m.SetVector(PixelId, new Vector4(colorBands, pixelsPerUnit, motePixels, dither ? 1f : 0f));
            SetKeyword(m, "_SOFT_INTERSECTION", softIntersection);
        }

        private static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        private static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        // ---------- Presets ----------

        /// <summary>Sets every look value to a ready-made style (keeps the opening size and position).</summary>
        public void ApplyPreset(Preset preset)
        {
            direction = DirectionSource.Custom;
            dustMotes = true;
            groundPools = true;
            blending = Blending.Additive;
            switch (preset)
            {
                case Preset.ForestCanopy:
                    pitch = 62f; yaw = -130f; beamCount = 6; widthRange = new Vector2(0.2f, 0.7f); endWidth = 1.4f;
                    topColor = new Color(1f, 0.96f, 0.72f); bottomColor = new Color(0.95f, 0.9f, 0.55f);
                    intensity = 0.5f; edgeSoftness = 0.6f; topFade = 0.25f; bottomFade = 0.45f;
                    streaks = 0.45f; shimmer = 0.45f; shimmerSpeed = 0.6f; moteDensity = 0.3f; poolIntensity = 0.45f;
                    colorBands = 5; brightnessJitter = 0.5f;
                    break;
                case Preset.WindowLight:
                    pitch = 45f; yaw = 160f; beamCount = 3; widthRange = new Vector2(0.6f, 0.9f); endWidth = 1.1f;
                    topColor = new Color(1f, 0.9f, 0.7f); bottomColor = new Color(1f, 0.78f, 0.5f);
                    intensity = 0.65f; edgeSoftness = 0.25f; topFade = 0.1f; bottomFade = 0.3f;
                    streaks = 0.2f; shimmer = 0.08f; shimmerSpeed = 0.3f; moteDensity = 0.45f; poolIntensity = 0.7f;
                    colorBands = 4; brightnessJitter = 0.15f;
                    break;
                case Preset.HolyBeam:
                    pitch = 88f; yaw = 0f; beamCount = 1; widthRange = new Vector2(1.2f, 1.2f); endWidth = 1.2f;
                    topColor = new Color(1f, 0.98f, 0.85f) * 1.5f; bottomColor = new Color(1f, 0.9f, 0.6f);
                    intensity = 1.1f; edgeSoftness = 0.7f; topFade = 0.05f; bottomFade = 0.2f;
                    streaks = 0.55f; shimmer = 0.2f; shimmerSpeed = 1.2f; moteDensity = 0.6f; poolIntensity = 1.1f;
                    colorBands = 6; brightnessJitter = 0f;
                    break;
                case Preset.CaveCrack:
                    pitch = 75f; yaw = -140f; beamCount = 2; widthRange = new Vector2(0.15f, 0.35f); endWidth = 1.8f;
                    topColor = new Color(0.85f, 0.95f, 1f); bottomColor = new Color(0.55f, 0.75f, 1f);
                    intensity = 0.7f; edgeSoftness = 0.5f; topFade = 0.05f; bottomFade = 0.55f;
                    streaks = 0.3f; shimmer = 0.1f; shimmerSpeed = 0.4f; moteDensity = 0.5f; poolIntensity = 0.5f;
                    colorBands = 4; brightnessJitter = 0.3f;
                    break;
                case Preset.DustyRoom:
                    pitch = 40f; yaw = 150f; beamCount = 4; widthRange = new Vector2(0.35f, 0.6f); endWidth = 1.25f;
                    topColor = new Color(1f, 0.88f, 0.68f); bottomColor = new Color(0.95f, 0.7f, 0.45f);
                    intensity = 0.45f; edgeSoftness = 0.35f; topFade = 0.1f; bottomFade = 0.35f;
                    streaks = 0.35f; shimmer = 0.05f; shimmerSpeed = 0.2f; moteDensity = 0.85f; poolIntensity = 0.55f;
                    colorBands = 4; brightnessJitter = 0.25f;
                    break;
                case Preset.Moonlight:
                    pitch = 58f; yaw = -160f; beamCount = 4; widthRange = new Vector2(0.3f, 0.8f); endWidth = 1.3f;
                    topColor = new Color(0.7f, 0.82f, 1f); bottomColor = new Color(0.45f, 0.55f, 0.95f);
                    intensity = 0.4f; edgeSoftness = 0.65f; topFade = 0.2f; bottomFade = 0.5f;
                    streaks = 0.3f; shimmer = 0.2f; shimmerSpeed = 0.3f; moteDensity = 0.2f; poolIntensity = 0.35f;
                    blending = Blending.Soft; colorBands = 5; brightnessJitter = 0.35f;
                    break;
            }
            dirty = true;
            Refresh();
        }

        // ---------- Gizmos ----------

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.6f);
            Quaternion yawOnly = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 center = transform.position + Vector3.up * sourceHeight;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, yawOnly, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(sourceSize.x, 0.02f, sourceSize.y));
            Gizmos.matrix = previous;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.9f);
            Vector3 light = LightDirection();
            Vector3 center = transform.position + Vector3.up * sourceHeight;
            float length = -light.y > 0.0001f ? (sourceHeight - groundOffset) / -light.y : sourceHeight;
            Gizmos.DrawLine(center, center + light * length);
            Gizmos.DrawWireSphere(center + light * length, 0.1f);
        }
    }
}
