using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    /// <summary>
    /// GPU grass: thousands of pixel-art grass tufts with wind and trampling, drawn with no GameObjects.
    /// <para>
    /// Blades live in one list on this component (paint them with the brush in the Inspector / Scene view, or import
    /// your existing sprite grass). At runtime they are uploaded once to a GPU buffer, split into chunks
    /// (<see cref="chunkSize"/>) so off-screen chunks are culled, and drawn with one procedural draw per visible
    /// (chunk, sprite variant). Wind and interaction happen in the vertex shader; interaction comes from one small
    /// texture (<see cref="GrassInteractionMap"/>) that every <see cref="GrassInteractor"/> is stamped into.
    /// </para>
    /// <para>
    /// Fits the game's rendering: blades are upright cards facing the camera's yaw, so the
    /// <see cref="ObliqueProjection"/> draws them 1:1; bending can move in whole render-texture pixels; the grass hides
    /// with the rest of the world during space combat (<see cref="SceneVisibility"/>). Because it has no Renderer,
    /// the ObliqueBillboard scan and the space backdrop's renderer toggling do not touch it.
    /// </para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Environment/Grass Field")]
    public sealed class GrassField : MonoBehaviour
    {
        private const string ShaderPath = "Rendering/PixelGrass";
        private const int BladeStride = sizeof(float) * 8;

        [Serializable]
        public struct Blade
        {
            public Vector3 position;
            public float scale;
            public int variant;
            [Tooltip("Brightness multiplier (1 = sprite colours).")]
            public float shade;
            public bool flip;
        }

        [Serializable]
        public sealed class Variant
        {
            [Tooltip("Grass sprite. Point filtering, bottom-centre pivot.")]
            public Sprite sprite;
            [Tooltip("How often the brush picks this variant.")]
            [Min(0f)] public float weight = 1f;
        }

        [Header("Sprites")]
        [SerializeField] private List<Variant> variants = new() { new Variant() };

        [Header("Colour")]
        [Tooltip("Tint for one kind of patch (multiplies the sprite).")]
        [SerializeField] private Color patchColorA = Color.white;
        [Tooltip("Tint for the other kind of patch. Patches are world-space noise, so the field looks natural.")]
        [SerializeField] private Color patchColorB = new(0.86f, 0.95f, 0.8f, 1f);
        [SerializeField, Min(0.001f)] private float patchScale = 0.35f;
        [Tooltip("Tufts are this much darker while flattened.")]
        [SerializeField, Range(0.3f, 1f)] private float trampleShade = 0.8f;

        [Header("Lighting")]
        [Tooltip("0 = lit like an upright card facing the camera, 1 = lit like the ground (matches the floor).")]
        [SerializeField, Range(0f, 1f)] private float normalUp = 0.75f;
        [Tooltip("Cel-shade the light into this many steps (0 = smooth).")]
        [SerializeField, Range(0, 8)] private int lightBands = 0;
        [SerializeField] private bool castShadows;
        [SerializeField, Range(0.01f, 1f)] private float alphaCutoff = 0.5f;

        [Header("Wind")]
        [SerializeField] private Vector2 windDirection = new(1f, 0.25f);
        [Tooltip("How far the tips lean with the wind (world units).")]
        [SerializeField, Min(0f)] private float windStrength = 0.09f;
        [SerializeField, Min(0f)] private float windSpeed = 1.6f;
        [Tooltip("Wave frequency across the field (higher = shorter waves).")]
        [SerializeField, Min(0f)] private float windWaveFrequency = 0.9f;
        [Tooltip("How much slow gusts vary the wind (0 = steady).")]
        [SerializeField, Range(0f, 1f)] private float gustStrength = 0.7f;
        [SerializeField, Min(0.001f)] private float gustScale = 0.12f;
        [Tooltip("Height (world units, at scale 1) over which a tuft bends: the tip moves fully, the root stays.")]
        [SerializeField, Min(0.05f)] private float bendHeight = 0.9f;

        [Header("Interaction")]
        [SerializeField] private bool interactive = true;
        [Tooltip("How far a fully pushed tip moves away (world units).")]
        [SerializeField, Min(0f)] private float pushBend = 0.45f;
        [Tooltip("How much fully trampled grass is squashed down (0 = not at all).")]
        [SerializeField, Range(0f, 0.9f)] private float pushFlatten = 0.35f;
        [Tooltip("How fast trampled grass springs back (per second).")]
        [SerializeField, Min(0.1f)] private float recoverySpeed = 2.2f;
        [Tooltip("World size of the interaction texture around the view.")]
        [SerializeField, Min(4f)] private float interactionMapSize = 24f;
        [SerializeField] private int interactionResolution = 128;
        [Tooltip("In Play mode, give every CharacterController a GrassInteractor automatically.")]
        [SerializeField] private bool autoAddInteractors = true;

        [Header("Pixel art")]
        [Tooltip("Move bent tips in whole render-texture pixels, so swaying grass stays crisp.")]
        [SerializeField] private bool snapBendToPixels = true;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;

        [Header("Performance")]
        [Tooltip("Blades are grouped into square chunks of this size; chunks outside the view are skipped.")]
        [SerializeField, Min(1f)] private float chunkSize = 8f;
        [Tooltip("Draw only this fraction of the blades (a quality setting; the pattern stays stable).")]
        [SerializeField, Range(0.05f, 1f)] private float densityScale = 1f;

        [SerializeField, HideInInspector] private List<Blade> blades = new();

        private struct DrawGroup
        {
            public Bounds Bounds;
            public int Offset;
            public int Count;
            public int Variant;
            public MaterialPropertyBlock Properties;
        }

        private static readonly List<GrassField> activeFields = new();
        private static Mesh bladeMesh;

        private static readonly int BladesId = Shader.PropertyToID("_GrassBlades");
        private static readonly int OffsetId = Shader.PropertyToID("_GrassInstanceOffset");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int SpriteUVId = Shader.PropertyToID("_SpriteUV");
        private static readonly int SpriteSizeId = Shader.PropertyToID("_SpriteSize");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int RightId = Shader.PropertyToID("_GrassRight");
        private static readonly int WindId = Shader.PropertyToID("_Wind");
        private static readonly int WindShapeId = Shader.PropertyToID("_WindShape");
        private static readonly int PushId = Shader.PropertyToID("_Push");
        private static readonly int PixelOptionsId = Shader.PropertyToID("_PixelOptions");
        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int PatchId = Shader.PropertyToID("_Patch");

        private readonly List<DrawGroup> groups = new();
        private GraphicsBuffer bladeBuffer;
        private Material material;
        private bool dirty = true;
        private int lastSubmitFrame = -1;
        private float nextInteractorScan;
        private bool missingShaderReported;

        public IReadOnlyList<Variant> Variants => variants;
        public List<Blade> Blades => blades;
        public int BladeCount => blades.Count;
        public bool Interactive => interactive;
        public float RecoverySpeed => recoverySpeed;
        public float InteractionMapSize => interactionMapSize;
        public int InteractionResolution => interactionResolution;
        public float PixelsPerUnit => pixelsPerUnit;

        /// <summary>Average height of the blade roots: the ground the grass stands on (used for interaction).</summary>
        public float GroundHeight { get; private set; }

        /// <summary>Call after changing <see cref="Blades"/> or <see cref="Variants"/> from code.</summary>
        public void MarkDirty() => dirty = true;

        /// <summary>The fields currently drawing (first one drives the interaction map).</summary>
        public static IReadOnlyList<GrassField> ActiveFields => activeFields;

        // ---------- Lifecycle ----------

        private void OnEnable()
        {
            if (!activeFields.Contains(this)) activeFields.Add(this);
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            dirty = true;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            activeFields.Remove(this);
            ReleaseGpu();
            if (activeFields.Count == 0) GrassInteractionMap.Release();
        }

        private void OnValidate()
        {
            interactionResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(32, interactionResolution)), 32, 1024);
            dirty = true;
        }

        // The interaction map is updated here, outside rendering, after gameplay has moved everything this frame.
        private void LateUpdate()
        {
            if (!Application.isPlaying || activeFields.Count == 0 || activeFields[0] != this) return;
            if (dirty) Rebuild();
            GrassInteractionMap.Tick(this, Camera.main);
        }

        private void Update()
        {
            if (!Application.isPlaying || !autoAddInteractors || activeFields.Count == 0 || activeFields[0] != this) return;
            if (Time.unscaledTime < nextInteractorScan) return;
            nextInteractorScan = Time.unscaledTime + 2f;
            foreach (CharacterController controller in FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude))
                if (controller != null && controller.GetComponent<GrassInteractor>() == null)
                    controller.gameObject.AddComponent<GrassInteractor>();
        }

        // ---------- Drawing ----------

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Draws submitted here are picked up by every camera rendering this frame (Game and Scene view).
            if (Time.renderedFrameCount == lastSubmitFrame) return;
            lastSubmitFrame = Time.renderedFrameCount;
            if (!isActiveAndEnabled || blades.Count == 0) return;
            if (Application.isPlaying && SceneVisibility.WorldHidden) return;
            if (!EnsureMaterial()) return;
            if (dirty) Rebuild();
            if (bladeBuffer == null || groups.Count == 0) return;

            Camera view = Camera.main;
            ApplyMaterialSettings(view);

            if (bladeMesh == null) bladeMesh = CreateBladeMesh();
            var rp = new RenderParams(material)
            {
                layer = gameObject.layer,
                shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            foreach (DrawGroup group in groups)
            {
                rp.worldBounds = group.Bounds;
                rp.matProps = group.Properties;
                Graphics.RenderMeshPrimitives(rp, bladeMesh, 0, group.Count);
            }
        }

        private void ApplyMaterialSettings(Camera view)
        {
            Vector3 right = Vector3.right;
            if (view != null)
            {
                Vector3 flat = Vector3.ProjectOnPlane(view.transform.right, Vector3.up);
                if (flat.sqrMagnitude > 0.0001f) right = flat.normalized;
            }
            Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.right;

            material.SetFloat(CutoffId, alphaCutoff);
            material.SetVector(RightId, right);
            material.SetVector(WindId, new Vector4(wind.x, wind.y, windStrength, windSpeed));
            material.SetVector(WindShapeId, new Vector4(windWaveFrequency, gustStrength, gustScale, bendHeight));
            material.SetVector(PushId, new Vector4(interactive ? pushBend : 0f, interactive ? pushFlatten : 0f,
                trampleShade, pixelsPerUnit));
            material.SetVector(PixelOptionsId, new Vector4(snapBendToPixels ? 1f : 0f, 0f, 0f, 0f));
            material.SetColor(ColorAId, patchColorA);
            material.SetColor(ColorBId, patchColorB);
            material.SetVector(PatchId, new Vector4(patchScale, 0f, normalUp, lightBands));
        }

        private bool EnsureMaterial()
        {
            if (material != null) return true;
            Shader shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null)
            {
                if (!missingShaderReported)
                    Debug.LogWarning("[Grass] Shader missing (Assets/Resources/Rendering/PixelGrass.shader).", this);
                missingShaderReported = true;
                return false;
            }
            material = new Material(shader) { name = "Pixel Grass (runtime)", hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        // ---------- GPU data ----------

        private struct GpuBlade
        {
            public Vector4 PositionScale;
            public Vector4 Data;
        }

        /// <summary>Re-uploads the blades (done automatically when they change).</summary>
        public void Rebuild()
        {
            dirty = false;
            ReleaseGpu();
            GroundHeight = transform.position.y;
            if (blades.Count > 0)
            {
                double sum = 0;
                foreach (Blade blade in blades) sum += blade.position.y;
                GroundHeight = (float)(sum / blades.Count);
            }
            if (blades.Count == 0 || variants.Count == 0) return;

            // Sort the kept blades by (chunk, variant) so each draw is one contiguous range.
            var order = new List<(long key, int index)>(blades.Count);
            float inverseChunk = 1f / Mathf.Max(1f, chunkSize);
            for (int i = 0; i < blades.Count; i++)
            {
                Blade blade = blades[i];
                int variant = Mathf.Clamp(blade.variant, 0, variants.Count - 1);
                if (!VariantUsable(variant)) continue;
                if (densityScale < 0.999f && StableRandom(blade.position) > densityScale) continue;
                int cx = Mathf.FloorToInt(blade.position.x * inverseChunk);
                int cz = Mathf.FloorToInt(blade.position.z * inverseChunk);
                long key = (((long)(cx + 32768) & 0xFFFF) << 40) | (((long)(cz + 32768) & 0xFFFF) << 24) | (uint)variant;
                order.Add((key, i));
            }
            if (order.Count == 0) return;
            order.Sort((a, b) => a.key.CompareTo(b.key));

            var data = new GpuBlade[order.Count];
            groups.Clear();
            int start = 0;
            for (int n = 0; n < order.Count; n++)
            {
                Blade blade = blades[order[n].index];
                data[n] = new GpuBlade
                {
                    PositionScale = new Vector4(blade.position.x, blade.position.y, blade.position.z,
                        Mathf.Max(0.01f, blade.scale)),
                    Data = new Vector4(blade.flip ? -1f : 1f, blade.shade <= 0f ? 1f : blade.shade,
                        StableRandom(blade.position * 1.37f), 0f)
                };
                bool last = n == order.Count - 1 || order[n + 1].key != order[n].key;
                if (!last) continue;
                AddGroup(data, start, n + 1 - start, Mathf.Clamp(blades[order[start].index].variant, 0, variants.Count - 1));
                start = n + 1;
            }

            bladeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, BladeStride);
            bladeBuffer.SetData(data);
            foreach (DrawGroup group in groups) group.Properties.SetBuffer(BladesId, bladeBuffer);
        }

        private void AddGroup(GpuBlade[] data, int offset, int count, int variant)
        {
            Sprite sprite = variants[variant].sprite;
            Texture texture = sprite.texture;
            Rect rect = sprite.textureRect;
            Vector2 size = rect.size / Mathf.Max(0.0001f, sprite.pixelsPerUnit);
            Vector2 pivot = new(sprite.pivot.x / Mathf.Max(1f, sprite.rect.width), sprite.pivot.y / Mathf.Max(1f, sprite.rect.height));

            // Bounds: every root in the range, grown by the largest tuft and its bend.
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            float largest = 0f;
            for (int i = offset; i < offset + count; i++)
            {
                Vector3 p = data[i].PositionScale;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                largest = Mathf.Max(largest, data[i].PositionScale.w);
            }
            float reach = Mathf.Max(size.x, size.y) * largest + pushBend + windStrength + 0.25f;
            var bounds = new Bounds((min + max) * 0.5f, max - min);
            bounds.Expand(reach * 2f);

            var properties = new MaterialPropertyBlock();
            properties.SetFloat(OffsetId, offset);
            properties.SetTexture(BaseMapId, texture);
            properties.SetVector(SpriteUVId, new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            properties.SetVector(SpriteSizeId, new Vector4(size.x, size.y, pivot.x, pivot.y));

            groups.Add(new DrawGroup { Bounds = bounds, Offset = offset, Count = count, Variant = variant, Properties = properties });
        }

        private bool VariantUsable(int variant) =>
            variant >= 0 && variant < variants.Count && variants[variant] != null && variants[variant].sprite != null
            && variants[variant].sprite.texture != null;

        private void ReleaseGpu()
        {
            bladeBuffer?.Release();
            bladeBuffer = null;
            groups.Clear();
        }

        private void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }

        // Unit strip, 2 columns x 9 rows: uv is the corner in the sprite (the shader places and bends it).
        private static Mesh CreateBladeMesh()
        {
            const int rows = 9;
            var vertices = new Vector3[rows * 2];
            var uvs = new Vector2[rows * 2];
            var triangles = new int[(rows - 1) * 6];
            for (int r = 0; r < rows; r++)
            {
                float v = r / (float)(rows - 1);
                vertices[r * 2] = new Vector3(0f, v, 0f);
                vertices[r * 2 + 1] = new Vector3(1f, v, 0f);
                uvs[r * 2] = new Vector2(0f, v);
                uvs[r * 2 + 1] = new Vector2(1f, v);
            }
            for (int r = 0; r < rows - 1; r++)
            {
                int i = r * 6, a = r * 2;
                triangles[i] = a; triangles[i + 1] = a + 2; triangles[i + 2] = a + 1;
                triangles[i + 3] = a + 1; triangles[i + 4] = a + 2; triangles[i + 5] = a + 3;
            }
            var mesh = new Mesh { name = "Grass Blade Strip", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), Vector3.one * 1000f);
            return mesh;
        }

        // ---------- Editing API (used by the brush) ----------

        /// <summary>A variant index picked by weight.</summary>
        public int PickVariant(System.Random random)
        {
            float total = 0f;
            foreach (Variant v in variants) if (v != null && v.sprite != null) total += v.weight;
            if (total <= 0f) return 0;
            float roll = (float)random.NextDouble() * total;
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i] == null || variants[i].sprite == null) continue;
                roll -= variants[i].weight;
                if (roll <= 0f) return i;
            }
            return variants.Count - 1;
        }

        /// <summary>Index of a variant using <paramref name="sprite"/>, adding it if needed.</summary>
        public int FindOrAddVariant(Sprite sprite)
        {
            for (int i = 0; i < variants.Count; i++)
                if (variants[i] != null && variants[i].sprite == sprite) return i;
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i] != null && variants[i].sprite == null)
                {
                    variants[i].sprite = sprite;
                    return i;
                }
            }
            variants.Add(new Variant { sprite = sprite, weight = 1f });
            return variants.Count - 1;
        }

        /// <summary>Removes blades within <paramref name="radius"/> (XZ) of <paramref name="center"/>.</summary>
        public int RemoveInRadius(Vector3 center, float radius)
        {
            float r2 = radius * radius;
            int removed = blades.RemoveAll(b =>
            {
                float dx = b.position.x - center.x, dz = b.position.z - center.z;
                return dx * dx + dz * dz <= r2;
            });
            if (removed > 0) dirty = true;
            return removed;
        }

        /// <summary>True if any blade root is closer than <paramref name="spacing"/> (XZ) to <paramref name="point"/>.</summary>
        public bool HasBladeNear(Vector3 point, float spacing)
        {
            float s2 = spacing * spacing;
            foreach (Blade b in blades)
            {
                float dx = b.position.x - point.x, dz = b.position.z - point.z;
                if (dx * dx + dz * dz < s2) return true;
            }
            return false;
        }

        private static float StableRandom(Vector3 p)
        {
            float h = Mathf.Sin(p.x * 12.9898f + p.z * 78.233f + p.y * 37.719f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => activeFields.Clear();

        private void OnDrawGizmosSelected()
        {
            if (blades.Count == 0) return;
            Gizmos.color = new Color(0.5f, 1f, 0.4f, 0.35f);
            foreach (DrawGroup group in groups) Gizmos.DrawWireCube(group.Bounds.center, group.Bounds.size);
        }
    }
}
