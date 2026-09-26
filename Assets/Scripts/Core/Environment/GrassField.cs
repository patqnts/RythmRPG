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
    /// <para>
    /// Grass can be cut, burnt (fire spreads tuft to tuft, pushed by the wind) and pushed by shockwaves, and grows
    /// back. Use the <see cref="Grass"/> API or the GrassCutter / GrassFire / GrassShockwaveEmitter components; the
    /// per-tuft state lives in GrassField.Burning.cs.
    /// </para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Environment/Grass Field")]
    public sealed partial class GrassField : MonoBehaviour
    {
        private const string ShaderPath = "Rendering/PixelGrass";
        private const string FlameShaderPath = "Rendering/GrassFlame";
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

        [Header("Cutting")]
        [SerializeField] private bool cuttable = true;
        [Tooltip("Height left after a cut when the cutter does not decide it, as a fraction of the tuft " +
                 "(0 = down to the ground). A GrassCutter can cut at its own height instead.")]
        [SerializeField, Range(0f, 1f)] private float stubbleHeight = 0.3f;
        [Tooltip("The lowest any cut can go, as a fraction of the tuft (0 = cutters may mow it to the ground).")]
        [SerializeField, Range(0f, 1f)] private float minStubble = 0.08f;

        [Header("Cut Pieces")]
        [Tooltip("The severed top of each tuft breaks into fragments that fly off, flutter and dissolve in mid-air.")]
        [SerializeField] private bool cutPieces = true;
        [Tooltip("Size of one fragment (world units): a severed top taller than this breaks into several rows " +
                 "(up to 4), and a wide one into two columns. Smaller = more, smaller bits.")]
        [SerializeField, Min(0.03f)] private float fragmentSize = 0.22f;
        [Tooltip("Seconds until a fragment has fully dissolved.")]
        [SerializeField, Min(0.2f)] private float fragmentLifetime = 1.1f;
        [Tooltip("When fragments start dissolving, as a share of their lifetime.")]
        [SerializeField, Range(0f, 0.95f)] private float fragmentFadeStart = 0.3f;
        [Tooltip("How fast fragments fly sideways, away from the cutter (units per second).")]
        [SerializeField, Min(0f)] private float fragmentSpeed = 1.6f;
        [Tooltip("How fast fragments are tossed up (units per second).")]
        [SerializeField, Min(0f)] private float fragmentJump = 1.6f;
        [SerializeField, Min(0f)] private float fragmentGravity = 6f;
        [Tooltip("Air resistance: higher = fragments slow down and float like leaves.")]
        [SerializeField, Min(0.01f)] private float fragmentDrag = 2.2f;
        [Tooltip("How fast fragments tumble (radians per second).")]
        [SerializeField, Min(0f)] private float fragmentSpin = 6f;
        [Tooltip("Pixel specks flying off each cut tuft (built-in effect; 0 = none). Ignored when Cut Effect is set.")]
        [SerializeField, Range(0, 6)] private int clippingSpecks = 1;
        [Tooltip("Colour of those specks.")]
        [SerializeField] private Color clippingColor = new(0.42f, 0.7f, 0.28f, 1f);

        [Header("Burning")]
        [SerializeField] private bool burnable = true;
        [Tooltip("Seconds for one tuft to burn down.")]
        [SerializeField, Min(0.1f)] private float burnDuration = 1.6f;
        [Tooltip("How far fire jumps from a burning tuft (world units). Should be more than the tuft spacing.")]
        [SerializeField, Min(0.05f)] private float spreadRadius = 0.75f;
        [Tooltip("How fast the fire front moves (world units per second, before wind).")]
        [SerializeField, Min(0.01f)] private float spreadSpeed = 1.1f;
        [Tooltip("Chance that a burning tuft lights each neighbour. Low values make fires die out on their own.")]
        [SerializeField, Range(0f, 1f)] private float spreadChance = 0.8f;
        [Tooltip("How much the wind pushes the fire (0 = spreads evenly in every direction).")]
        [SerializeField, Range(0f, 1f)] private float windSpread = 0.6f;
        [Tooltip("Height of the ash left behind, as a fraction of the tuft.")]
        [SerializeField, Range(0f, 1f)] private float ashHeight = 0.12f;
        [Tooltip("Seconds the embers keep glowing after a tuft has burnt out.")]
        [SerializeField, Min(0f)] private float emberTime = 2.5f;
        [Tooltip("Most tufts burning at once (a safety cap for very large fields).")]
        [SerializeField, Min(1)] private int maxBurning = 4000;
        [SerializeField, ColorUsage(false, true)] private Color flameColor = new(2f, 0.62f, 0.12f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color flameTipColor = new(2.2f, 1.8f, 0.6f, 1f);
        [SerializeField] private Color charColor = new(0.13f, 0.1f, 0.09f, 1f);
        [SerializeField] private Color ashColor = new(0.36f, 0.34f, 0.32f, 1f);
        [Tooltip("Rows of sprite pixels of flame licking above the burning edge (0 = none, e.g. when using Flame Frames).")]
        [SerializeField, Range(0, 8)] private int flameHeight = 3;
        [Tooltip("Flame flicker frames per second (a pixel-art flicker rather than a smooth one).")]
        [SerializeField, Range(1f, 30f)] private float flameFps = 12f;
        [Tooltip("How far burning grass curls over and shrivels (0 = burns straight down).")]
        [SerializeField, Range(0f, 2f)] private float curl = 1f;
        [Tooltip("Intensity of a point light that follows the fire and lights its surroundings (0 = no light).")]
        [SerializeField, Min(0f)] private float fireLightIntensity = 2.5f;

        [Header("Fire Animation (optional)")]
        [Tooltip("Sprite frames of a flame animation, played on every burning tuft (all frames from one texture). " +
                 "Empty = only the pixel flames drawn into the grass.")]
        [SerializeField] private Sprite[] flameFrames;
        [SerializeField, Range(1f, 30f)] private float flameFrameRate = 10f;
        [Tooltip("Size of the flame sprite (1 = its own pixel size).")]
        [SerializeField, Min(0.05f)] private float flameScale = 1f;
        [SerializeField, ColorUsage(true, true)] private Color flameTint = Color.white;
        [Tooltip("Alpha below which flame sprite pixels are discarded.")]
        [SerializeField, Range(0f, 1f)] private float flameCutoff = 0.1f;

        [Header("Particle Effects (optional)")]
        [Tooltip("Your particle system (prefab or scene object), emitted continuously from every burning tuft. " +
                 "A private copy is used, with its own emission switched off; empty = built-in pixel embers and smoke.")]
        [SerializeField] private ParticleSystem burningEffect;
        [Tooltip("Particles per second per burning tuft (the prefab's layers keep their own ratios).")]
        [SerializeField, Min(0f)] private float burningEffectRate = 2f;
        [Tooltip("Where in a burning tuft the particles start: 0 = at its root, 1 = at the burning edge. " +
                 "Low values make the fire sit in the grass instead of floating above it.")]
        [SerializeField, Range(0f, 1f)] private float burningEffectHeight = 0.2f;
        [Tooltip("How far across the tuft's width particles start (0 = its centre line, 1 = its full width).")]
        [SerializeField, Range(0f, 1f)] private float burningEffectSpread = 0.5f;
        [Tooltip("Moves the particles this far toward the camera (world units, along the view) so they are drawn in " +
                 "front of their own tuft rather than hidden behind it. Their place on screen does not change.")]
        [SerializeField, Min(0f)] private float burningEffectDepthBias = 0.12f;
        [Tooltip("Burst when a tuft catches fire.")]
        [SerializeField] private ParticleSystem igniteEffect;
        [Tooltip("Burst when a tuft burns out.")]
        [SerializeField] private ParticleSystem burnOutEffect;
        [Tooltip("Burst when a tuft is cut (replaces the built-in pixel specks).")]
        [SerializeField] private ParticleSystem cutEffect;
        [Tooltip("Particles per burst.")]
        [SerializeField, Range(1, 20)] private int burstCount = 2;

        [Header("Regrowth")]
        [SerializeField] private bool regrow = true;
        [Tooltip("Seconds before cut or burnt grass starts growing back.")]
        [SerializeField, Min(0f)] private float regrowDelay = 25f;
        [Tooltip("Seconds it takes to grow back to full height.")]
        [SerializeField, Min(0.1f)] private float regrowDuration = 8f;

        [Header("Built-in Effects")]
        [Tooltip("Built-in pixel particles (clipping specks, embers, smoke) wherever no particle effect is assigned above.")]
        [SerializeField] private bool particleEffects = true;
        [Tooltip("Optional material for those particles. Empty = URP Particles/Unlit, or Sprites/Default.")]
        [SerializeField] private Material effectMaterial;

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
        private static readonly int StatesId = Shader.PropertyToID("_GrassStates");
        private static readonly int SpriteExtraId = Shader.PropertyToID("_SpriteExtra");
        private static readonly int GrassTimeId = Shader.PropertyToID("_GrassTime");
        private static readonly int BurnId = Shader.PropertyToID("_Burn");
        private static readonly int FireId = Shader.PropertyToID("_Fire");
        private static readonly int RegrowId = Shader.PropertyToID("_Regrow");
        private static readonly int FlameColorId = Shader.PropertyToID("_FlameColor");
        private static readonly int FlameTipColorId = Shader.PropertyToID("_FlameTipColor");
        private static readonly int CharColorId = Shader.PropertyToID("_CharColor");
        private static readonly int AshColorId = Shader.PropertyToID("_AshColor");
        private static readonly int CurlId = Shader.PropertyToID("_Curl");
        private static readonly int CutsId = Shader.PropertyToID("_GrassCuts");
        private static readonly int PiecesId = Shader.PropertyToID("_Pieces");
        private static readonly int Pieces2Id = Shader.PropertyToID("_Pieces2");
        private static readonly int Pieces3Id = Shader.PropertyToID("_Pieces3");
        private const int PieceFragments = 8; // GRASS_PIECE_FRAGMENTS in PixelGrass.shader

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
        public bool Cuttable { get => cuttable; set => cuttable = value; }
        public bool Burnable { get => burnable; set => burnable = value; }
        public float StubbleHeight { get => stubbleHeight; set => stubbleHeight = Mathf.Clamp01(value); }

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
            ReleaseStates();
            if (activeFields.Count == 0) GrassInteractionMap.Release();
        }

        private void OnValidate()
        {
            interactionResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(32, interactionResolution)), 32, 1024);
            dirty = true;
        }

        // Cut / fire / regrowth and the interaction map are updated here, outside rendering, after gameplay has
        // moved everything this frame.
        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (dirty) Rebuild();
            SimulateStates(Time.time, Time.deltaTime);
            if (activeFields.Count > 0 && activeFields[0] == this) GrassInteractionMap.Tick(this, Camera.main);
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
            UploadStates();

            Camera view = Camera.main;
            Vector3 right = Vector3.right;
            if (view != null)
            {
                Vector3 flat = Vector3.ProjectOnPlane(view.transform.right, Vector3.up);
                if (flat.sqrMagnitude > 0.0001f) right = flat.normalized;
            }
            ApplyMaterialSettings(material, right);

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

            if (!Application.isPlaying) return;
            DrawCutPieces(rp, right);
            DrawFlames(right);
        }

        // The chunks where something was cut recently are drawn a second time, as flying / fallen pieces.
        private void DrawCutPieces(RenderParams rp, Vector3 right)
        {
            if (!cutPieces || groupPieceUntil == null) return;
            float now = Time.time;
            float reach = (fragmentSpeed * 1.5f + fragmentJump + 0.5f) / Mathf.Max(0.5f, fragmentDrag) + 0.5f;
            bool any = false;
            for (int g = 0; g < groups.Count && g < groupPieceUntil.Length; g++)
            {
                if (groupPieceUntil[g] < now) continue;
                if (!any)
                {
                    if (pieceMaterial == null)
                    {
                        pieceMaterial = new Material(material) { name = "Pixel Grass Pieces (runtime)", hideFlags = HideFlags.HideAndDontSave };
                        pieceMaterial.EnableKeyword("_GRASS_PIECES");
                    }
                    ApplyMaterialSettings(pieceMaterial, right);
                    rp.material = pieceMaterial;
                    any = true;
                }
                DrawGroup group = groups[g];
                Bounds bounds = group.Bounds;
                bounds.Expand(reach * 2f);
                rp.worldBounds = bounds;
                rp.matProps = group.Properties;
                Graphics.RenderMeshPrimitives(rp, bladeMesh, 0, group.Count * PieceFragments);
            }
        }

        private void ApplyMaterialSettings(Material target, Vector3 right)
        {
            Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.right;

            target.SetFloat(CutoffId, alphaCutoff);
            target.SetVector(RightId, right);
            target.SetVector(WindId, new Vector4(wind.x, wind.y, windStrength, windSpeed));
            target.SetVector(WindShapeId, new Vector4(windWaveFrequency, gustStrength, gustScale, bendHeight));
            target.SetVector(PushId, new Vector4(interactive ? pushBend : 0f, interactive ? pushFlatten : 0f,
                trampleShade, pixelsPerUnit));
            target.SetVector(PixelOptionsId, new Vector4(snapBendToPixels ? 1f : 0f, 0f, 0f, 0f));
            target.SetColor(ColorAId, patchColorA);
            target.SetColor(ColorBId, patchColorB);
            target.SetVector(PatchId, new Vector4(patchScale, 0f, normalUp, lightBands));
            target.SetVector(GrassTimeId, new Vector4(Application.isPlaying ? Time.time : 0f, 0f, 0f, 0f));
            target.SetVector(BurnId, new Vector4(burnDuration, ashHeight, emberTime, 0f));
            target.SetVector(FireId, new Vector4(flameHeight, 2f, flameFps, 0.3f));
            target.SetVector(RegrowId, new Vector4(regrowDuration, 0f, 0f, 0f));
            target.SetColor(FlameColorId, flameColor);
            target.SetColor(FlameTipColorId, flameTipColor);
            target.SetColor(CharColorId, charColor);
            target.SetColor(AshColorId, ashColor);
            target.SetVector(CurlId, new Vector4(curl, 0f, 0f, 0f));
            target.SetVector(PiecesId, new Vector4(fragmentLifetime, fragmentSpeed, fragmentJump, fragmentGravity));
            target.SetVector(Pieces2Id, new Vector4(fragmentSpin, 1.5f / Mathf.Max(1f, pixelsPerUnit), fragmentDrag, fragmentFadeStart));
            target.SetVector(Pieces3Id, new Vector4(fragmentSize, 0f, 0f, 0f));
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
            ReleaseStateBuffer();
            visibleRowsCache.Clear();
            GroundHeight = transform.position.y;
            if (blades.Count > 0)
            {
                double sum = 0;
                foreach (Blade blade in blades) sum += blade.position.y;
                GroundHeight = (float)(sum / blades.Count);
            }
            if (blades.Count == 0 || variants.Count == 0)
            {
                ResetStates();
                return;
            }

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
            if (order.Count == 0)
            {
                ResetStates();
                return;
            }
            order.Sort((a, b) => a.key.CompareTo(b.key));

            var data = new GpuBlade[order.Count];
            bladeTips = new float[order.Count];
            bladeBases = new float[order.Count];
            bladeHalfWidths = new float[order.Count];
            bladeGroups = new int[order.Count];
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
            var source = new int[order.Count];
            for (int n = 0; n < order.Count; n++) source[n] = order[n].index;
            BuildStates(data, source);
            foreach (DrawGroup group in groups)
            {
                group.Properties.SetBuffer(BladesId, bladeBuffer);
                group.Properties.SetBuffer(StatesId, stateBuffer);
                group.Properties.SetBuffer(CutsId, cutBuffer);
            }
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

            // The part of the quad the grass actually covers (sprites often have empty rows): cuts and burns are
            // measured on it.
            Vector2 visible = VisibleRows(sprite);
            float tip = (visible.y - pivot.y) * size.y;
            float bottom = (visible.x - pivot.y) * size.y;
            for (int i = offset; i < offset + count; i++)
            {
                float scale = data[i].PositionScale.w;
                bladeTips[i] = Mathf.Max(bottom * scale + 0.02f, tip * scale);
                bladeBases[i] = bottom * scale;
                bladeHalfWidths[i] = size.x * 0.5f * scale;
                bladeGroups[i] = groups.Count;
            }

            var properties = new MaterialPropertyBlock();
            properties.SetFloat(OffsetId, offset);
            properties.SetTexture(BaseMapId, texture);
            properties.SetVector(SpriteUVId, new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            properties.SetVector(SpriteSizeId, new Vector4(size.x, size.y, pivot.x, pivot.y));
            properties.SetVector(SpriteExtraId, new Vector4(visible.x, visible.y, rect.height, rect.width));

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
            DestroyMaterial(material);
            DestroyMaterial(pieceMaterial);
            DestroyMaterial(flameMaterial);
        }

        private static void DestroyMaterial(Material target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
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
