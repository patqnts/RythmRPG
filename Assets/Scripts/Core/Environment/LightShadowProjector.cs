using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RythmRPG.Core
{
    /// <summary>
    /// Projects an image as light or shadow onto everything in a box: window light on the floor, blinds, leaf dapple,
    /// drifting cloud shadows, the shadow of a dragon flying overhead, stained glass.
    /// <para>
    /// The box starts at this object and runs along the projection direction for <see cref="Distance"/>. Every
    /// opaque surface inside it (floor, walls, props, grass, characters: anything that writes depth) receives the
    /// image, so a shadow climbs a wall and a character walking through window light gets lit. It is one small box
    /// mesh and one material; it needs the camera's Depth Texture (forced on for game cameras while any projector is
    /// active).
    /// </para>
    /// <para>
    /// Pixel art: the image is snapped to the world pixel grid (Pixels Per Unit), point sampled, and posterised into
    /// a few bands with ordered dithering, so it steps on whole pixels like the rest of the 480x270 render.
    /// </para>
    /// <para>Different from <see cref="GodRays"/>, which draws the visible shafts in the air. Use both for a window:
    /// God Rays for the beams, this for the bright patch on the floor.</para>
    /// <para>Create one with GameObject > Rythm RPG > Light / Shadow Projector, then pick a preset in the Inspector.</para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Rythm RPG/Environment/Light Shadow Projector")]
    public sealed class LightShadowProjector : MonoBehaviour
    {
        public enum Effect
        {
            /// <summary>Darken toward the shadow colour.</summary>
            Shadow,
            /// <summary>Brighten what is already there (keeps the surface's colour and texture). The natural look.</summary>
            LightMultiply,
            /// <summary>Add light on top (glowy; also shows on black surfaces).</summary>
            LightAdditive,
        }

        public enum Direction
        {
            /// <summary>Along this object's blue (Z) axis. The image turns with the object.</summary>
            TransformForward,
            /// <summary>Straight down. The top of the image points along this object's (or the anchor's) forward.</summary>
            StraightDown,
            /// <summary>Along the sun (scene sun / first directional light, or the one assigned).</summary>
            Sun,
            /// <summary>Along <see cref="customDirection"/> (world space).</summary>
            Custom,
        }

        public enum CookieChannel { Alpha, Red, Luminance, ColorAndAlpha }

        public enum Preset
        {
            WindowLight,
            FirelitWindow,
            Blinds,
            StainedGlass,
            LeafDapple,
            CloudShadows,
            DragonShadow,
            SoftBlobShadow,
        }

        private const string ShaderPath = "Rendering/LightShadowProjector";

        // ---------------- Image ----------------
        [Header("Image")]
        [Tooltip("The shape. Pixel art works best (Point filter, no compression). Alpha = where the light/shadow is.")]
        [SerializeField] private Sprite sprite;
        [Tooltip("Used when no Sprite is set.")]
        [SerializeField] private Texture2D texture;
        [Tooltip("Optional flipbook (wing flaps, flickering leaves). Overrides Sprite while it has frames.")]
        [SerializeField] private Sprite[] frames = new Sprite[0];
        [SerializeField, Min(0f)] private float frameRate = 8f;
        [Tooltip("Which part of the image is the mask. Colour + Alpha also tints the light/shadow with the image's " +
                 "colours (stained glass).")]
        [SerializeField] private CookieChannel channel = CookieChannel.Alpha;
        [SerializeField] private bool invert;
        [SerializeField] private bool flipX;
        [SerializeField] private bool flipY;

        // ---------------- Projection ----------------
        [Header("Projection")]
        [SerializeField] private Direction direction = Direction.TransformForward;
        [Tooltip("Custom direction (world space).")]
        [SerializeField] private Vector3 customDirection = new(0.35f, -1f, 0.5f);
        [Tooltip("Sun mode: the light to follow (empty = the scene's sun / first directional light).")]
        [SerializeField] private Light sun;
        [Tooltip("Optional object to project from (a dragon in the sky, a moving cloud). The box starts there.")]
        [SerializeField] private Transform anchor;
        [SerializeField] private Vector3 anchorOffset;
        [Tooltip("Width and height of the image (world units, across the projection).")]
        [SerializeField] private Vector2 size = new(2f, 2.5f);
        [Tooltip("How far the image is thrown along the direction (world units). Must reach the surfaces.")]
        [SerializeField, Min(0.05f)] private float distance = 6f;
        [Tooltip("Start the box this far along the direction (negative = behind this object).")]
        [SerializeField] private float startOffset;
        [Tooltip("Spin the image in its plane (degrees).")]
        [SerializeField, Range(-180f, 180f)] private float rotation;
        [Tooltip("Repeat the image (for tiling patterns like clouds or leaves).")]
        [SerializeField] private bool repeat;
        [SerializeField] private Vector2 tiling = Vector2.one;

        // ---------------- Effect ----------------
        [Header("Effect")]
        [SerializeField] private Effect effect = Effect.LightMultiply;
        [Tooltip("Shadow: the colour fully shadowed areas are multiplied by (darker = deeper shadow).\n" +
                 "Light: the light's colour (HDR; brighter = stronger).")]
        [SerializeField, ColorUsage(false, true)] private Color color = new(1f, 0.85f, 0.55f, 1f);
        [Tooltip("Shadow: opacity (0..1). Light: brightness.")]
        [SerializeField, Range(0f, 4f)] private float strength = 1f;
        [Tooltip("Where the sun is already blocked (URP main light shadows), don't add more shadow / light. " +
                 "Stops a dragon shadow double-darkening under trees.")]
        [SerializeField] private bool respectSunShadows;

        // ---------------- Look ----------------
        [Header("Pixel art look")]
        [Tooltip("Blur the edge by this much (world units). 0 = crisp cookie pixels.")]
        [SerializeField, Range(0f, 2f)] private float softness = 0.06f;
        [Tooltip("Posterise into this many steps (0 = smooth).")]
        [SerializeField, Range(0, 8)] private int bands = 3;
        [Tooltip("Ordered (Bayer) dithering between the steps.")]
        [SerializeField, Range(0f, 1f)] private float dither = 1f;
        [Tooltip("Snap the image to this world pixel grid (32 = the game's grid). 0 = off.")]
        [SerializeField, Min(0f)] private float pixelsPerUnit = 32f;

        // ---------------- Fades ----------------
        [Header("Fades")]
        [Tooltip("Fade in over this fraction of the box at its start.")]
        [SerializeField, Range(0f, 1f)] private float nearFade = 0.02f;
        [Tooltip("Fade out over this fraction of the box at its end.")]
        [SerializeField, Range(0f, 1f)] private float farFade = 0.25f;
        [Tooltip("Fade out toward the image's border (fraction of the image).")]
        [SerializeField, Range(0f, 1f)] private float edgeFade = 0.05f;
        [Tooltip("Surfaces turned away from the projection get less (0 = everything gets it equally).")]
        [SerializeField, Range(0f, 1f)] private float facingFade;

        // ---------------- Animation ----------------
        [Header("Animation")]
        [Tooltip("Move the image (image widths per second).")]
        [SerializeField] private Vector2 scroll;
        [Tooltip("Wobble the image (like leaves in wind), in image widths.")]
        [SerializeField, Range(0f, 0.2f)] private float sway;
        [SerializeField, Min(0f)] private float swaySpeed = 1.2f;
        [Tooltip("Brightness flicker (firelight).")]
        [SerializeField, Range(0f, 1f)] private float flicker;
        [SerializeField, Min(0f)] private float flickerSpeed = 3f;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int SpriteRectId = Shader.PropertyToID("_SpriteRect");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly int UVTransformId = Shader.PropertyToID("_UVTransform");
        private static readonly int UVAnimId = Shader.PropertyToID("_UVAnim");
        private static readonly int LookId = Shader.PropertyToID("_Look");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        private static readonly int FlipId = Shader.PropertyToID("_Flip");
        private static readonly int ProjDirId = Shader.PropertyToID("_ProjDir");
        private static readonly int ProjRow0Id = Shader.PropertyToID("_ProjRow0");
        private static readonly int ProjRow1Id = Shader.PropertyToID("_ProjRow1");
        private static readonly int ProjRow2Id = Shader.PropertyToID("_ProjRow2");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private const string SunShadowKeyword = "_RESPECT_SUN_SHADOWS";

        private static readonly Vector3[] UnitCorners =
        {
            new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f),
            new(-0.5f, -0.5f, 1f), new(0.5f, -0.5f, 1f), new(0.5f, 0.5f, 1f), new(-0.5f, 0.5f, 1f),
        };

        private static readonly int[] BoxTriangles =
        {
            0, 2, 1, 0, 3, 2, // start
            4, 5, 6, 4, 6, 7, // end
            0, 1, 5, 0, 5, 4, // bottom
            3, 7, 6, 3, 6, 2, // top
            0, 4, 7, 0, 7, 3, // left
            1, 2, 6, 1, 6, 5, // right
        };

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material runtimeMaterial;
        private readonly Vector3[] meshVertices = new Vector3[8];
        private Matrix4x4 builtBox;
        private Matrix4x4 builtLocalToWorld;
        private bool meshValid;
        private bool missingShaderReported;
        private float seed;

        // ---------------- Runtime multipliers (for scripts, cutscenes, ShadowFlyover) ----------------

        /// <summary>Multiplies <see cref="Strength"/> (fade a projector in or out from code). Not saved.</summary>
        public float StrengthMultiplier { get; set; } = 1f;

        /// <summary>Multiplies <see cref="Size"/> (e.g. wing flap, altitude). Not saved.</summary>
        public Vector2 SizeMultiplier { get; set; } = Vector2.one;

        /// <summary>Added to <see cref="Softness"/> (e.g. blurrier the higher a flyer is). Not saved.</summary>
        public float ExtraSoftness { get; set; }

        private bool poseOverride;
        private Vector3 overridePosition;
        private Vector3 overrideHeading;

        /// <summary>True while a script (e.g. <see cref="ShadowFlyover"/>) is placing the shadow.</summary>
        public bool HasPoseOverride => poseOverride;

        /// <summary>
        /// Draw the image from this position, with its top pointing along <paramref name="heading"/>, without moving
        /// the object itself (so the object stays where you placed it). Not saved.
        /// </summary>
        public void SetPoseOverride(Vector3 position, Vector3 heading)
        {
            poseOverride = true;
            overridePosition = position;
            overrideHeading = heading;
        }

        /// <summary>Back to the object's own position and rotation.</summary>
        public void ClearPoseOverride()
        {
            poseOverride = false;
        }

        /// <summary>Resets every runtime override (multipliers and pose).</summary>
        public void ClearRuntimeOverrides()
        {
            poseOverride = false;
            StrengthMultiplier = 1f;
            SizeMultiplier = Vector2.one;
            ExtraSoftness = 0f;
        }

        // ---------------- Public properties ----------------

        public Sprite Sprite { get => sprite; set => sprite = value; }
        public Texture2D Texture { get => texture; set => texture = value; }
        public Sprite[] Frames { get => frames; set => frames = value ?? new Sprite[0]; }
        public float FrameRate { get => frameRate; set => frameRate = Mathf.Max(0f, value); }
        public Effect Mode { get => effect; set => effect = value; }
        public Direction ProjectionDirection { get => direction; set => direction = value; }
        public Transform Anchor { get => anchor; set => anchor = value; }
        public Vector2 Size { get => size; set => size = Vector2.Max(value, Vector2.one * 0.01f); }
        public float Distance { get => distance; set => distance = Mathf.Max(0.05f, value); }
        public Color Color { get => color; set => color = value; }
        public float Strength { get => strength; set => strength = Mathf.Max(0f, value); }
        public float Softness { get => softness; set => softness = Mathf.Max(0f, value); }
        public float Rotation { get => rotation; set => rotation = value; }
        public Vector2 Scroll { get => scroll; set => scroll = value; }

        /// <summary>World position where the box starts.</summary>
        public Vector3 Origin => BoxMatrix().MultiplyPoint3x4(new Vector3(0f, 0f, 0f));

        /// <summary>World-space projection direction (unit).</summary>
        public Vector3 WorldDirection => ProjectionAxis(out _);

        // ---------------- Lifecycle ----------------

        private void OnEnable()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.allowOcclusionWhenDynamic = false;
            // Random phase so several projectors don't flicker / flip frames in sync.
            seed = UnityEngine.Random.Range(0f, 97f);
            meshValid = false;
            DepthTextureGuard.Register(this);
            Refresh();
        }

        private void OnDisable()
        {
            DepthTextureGuard.Unregister(this);
            if (meshFilter != null && meshFilter.sharedMesh == mesh) meshFilter.sharedMesh = null;
            meshValid = false;
        }

        private void OnDestroy()
        {
            SafeDestroy(mesh);
            SafeDestroy(runtimeMaterial);
        }

        private void OnValidate()
        {
            size = Vector2.Max(size, Vector2.one * 0.01f);
            tiling = new Vector2(Mathf.Approximately(tiling.x, 0f) ? 1f : tiling.x,
                                 Mathf.Approximately(tiling.y, 0f) ? 1f : tiling.y);
            if (frames == null) frames = new Sprite[0];
            if (isActiveAndEnabled) Refresh();
        }

        private void LateUpdate() => Refresh();

        /// <summary>Re-applies every setting now (normally done every LateUpdate).</summary>
        public void Refresh()
        {
            if (meshRenderer == null || meshFilter == null) return;
            if (!EnsureMaterial()) return;
            UpdateMesh();
            ApplyMaterial();
        }

        // ---------------- Geometry ----------------

        /// <summary>Unit box (x, y in -0.5..0.5, z 0..1) to world.</summary>
        public Matrix4x4 BoxMatrix()
        {
            Vector3 axis = ProjectionAxis(out Quaternion rot);
            Vector3 origin = poseOverride ? overridePosition
                : anchor != null ? anchor.position + anchorOffset : transform.position;
            origin += axis * startOffset;
            Vector2 s = Vector2.Scale(size, SizeMultiplier);
            s = Vector2.Max(s, Vector2.one * 0.001f);
            return Matrix4x4.TRS(origin, rot, new Vector3(s.x, s.y, Mathf.Max(0.05f, distance)));
        }

        private Vector3 ProjectionAxis(out Quaternion rot)
        {
            Transform source = anchor != null && !poseOverride ? anchor : transform;
            Vector3 heading = source.forward;
            Quaternion sourceRotation = source.rotation;
            if (poseOverride)
            {
                Vector3 flat = new(overrideHeading.x, 0f, overrideHeading.z);
                if (flat.sqrMagnitude > 1e-8f)
                {
                    // Turn the object's rotation around world up so its forward follows the heading.
                    Vector3 own = new(source.forward.x, 0f, source.forward.z);
                    Quaternion yaw = own.sqrMagnitude > 1e-8f
                        ? Quaternion.AngleAxis(Vector3.SignedAngle(own, flat, Vector3.up), Vector3.up)
                        : Quaternion.LookRotation(flat.normalized, Vector3.up);
                    sourceRotation = yaw * sourceRotation;
                    heading = flat.normalized;
                }
            }
            Vector3 axis;
            switch (direction)
            {
                case Direction.StraightDown: axis = Vector3.down; break;
                case Direction.Sun: axis = SunDirection(); break;
                case Direction.Custom:
                    axis = customDirection.sqrMagnitude > 1e-6f ? customDirection.normalized : Vector3.down;
                    break;
                default:
                    rot = sourceRotation;
                    return sourceRotation * Vector3.forward;
            }

            // The top of the image follows the source's heading (projected across the throw).
            Vector3 up = Vector3.ProjectOnPlane(heading, axis);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(sourceRotation * Vector3.up, axis);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, axis);
            rot = Quaternion.LookRotation(axis, up.normalized);
            return axis;
        }

        private Vector3 SunDirection()
        {
            Light source = sun != null ? sun : RenderSettings.sun;
            if (source == null)
            {
                foreach (Light candidate in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    if (candidate.type != LightType.Directional) continue;
                    source = candidate;
                    break;
                }
                if (source != null && sun == null && Application.isPlaying) sun = source;
            }
            Vector3 d = source != null ? source.transform.forward : new Vector3(0.3f, -1f, 0.45f).normalized;
            // A sun at or above the horizon would throw the image sideways forever: keep it pointing down a bit.
            if (d.y > -0.1f) d = new Vector3(d.x, -0.1f, d.z).normalized;
            return d;
        }

        private void UpdateMesh()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "Light Shadow Projector Box", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                meshValid = false;
            }
            if (meshFilter.sharedMesh != mesh) meshFilter.sharedMesh = mesh;

            Matrix4x4 box = BoxMatrix();
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            if (meshValid && box == builtBox && localToWorld == builtLocalToWorld) return;

            Matrix4x4 boxToLocal = transform.worldToLocalMatrix * box;
            for (int i = 0; i < 8; i++) meshVertices[i] = boxToLocal.MultiplyPoint3x4(UnitCorners[i]);
            mesh.Clear();
            mesh.vertices = meshVertices;
            mesh.triangles = BoxTriangles;
            mesh.RecalculateBounds();
            builtBox = box;
            builtLocalToWorld = localToWorld;
            meshValid = true;
        }

        // ---------------- Material ----------------

        private bool EnsureMaterial()
        {
            if (runtimeMaterial != null)
            {
                if (meshRenderer.sharedMaterial != runtimeMaterial) meshRenderer.sharedMaterial = runtimeMaterial;
                return true;
            }
            Shader shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null)
            {
                if (!missingShaderReported)
                    Debug.LogWarning("[Light Shadow Projector] Shader missing " +
                                     "(Assets/Resources/Rendering/LightShadowProjector.shader).", this);
                missingShaderReported = true;
                return false;
            }
            runtimeMaterial = new Material(shader) { name = "Light Shadow Projector (runtime)", hideFlags = HideFlags.DontSave };
            meshRenderer.sharedMaterial = runtimeMaterial;
            return true;
        }

        private void ApplyMaterial()
        {
            Material m = runtimeMaterial;

            // Blend: 2x multiply for shadow and multiply light, One One for additive light.
            bool additive = effect == Effect.LightAdditive;
            m.SetFloat(SrcBlendId, additive ? (float)BlendMode.One : (float)BlendMode.DstColor);
            m.SetFloat(DstBlendId, additive ? (float)BlendMode.One : (float)BlendMode.SrcColor);

            // Image
            Texture tex = null;
            Vector4 rect = new(0f, 0f, 1f, 1f);
            Sprite current = CurrentSprite();
            if (current != null)
            {
                tex = current.texture;
                rect = SpriteUVRect(current);
            }
            else if (texture != null)
            {
                tex = texture;
            }
            m.SetTexture(MainTexId, tex != null ? tex : Texture2D.whiteTexture);
            m.SetVector(SpriteRectId, rect);

            // Effect
            float mode = effect == Effect.Shadow ? 0f : effect == Effect.LightMultiply ? 1f : 2f;
            m.SetColor(ColorId, color);
            m.SetVector(ParamsId, new Vector4(Mathf.Max(0f, strength * StrengthMultiplier), mode, (float)channel, invert ? 1f : 0f));

            // Image transform + animation
            m.SetVector(UVTransformId, new Vector4(tiling.x, tiling.y, rotation * Mathf.Deg2Rad, repeat ? 1f : 0f));
            m.SetVector(UVAnimId, new Vector4(scroll.x, scroll.y, sway, swaySpeed));

            Vector2 s = Vector2.Max(Vector2.Scale(size, SizeMultiplier), Vector2.one * 0.001f);
            float soft = Mathf.Max(0f, softness + ExtraSoftness);
            m.SetVector(LookId, new Vector4(soft, bands, dither, pixelsPerUnit));
            m.SetVector(FadeId, new Vector4(nearFade, farFade, edgeFade, facingFade));
            // w = point sample the cookie when there is no blur.
            m.SetVector(FlickerId, new Vector4(flicker, flickerSpeed, seed, 1f));
            m.SetVector(FlipId, new Vector4(flipX ? -1f : 1f, flipY ? -1f : 1f, s.x, s.y));

            // Box
            Matrix4x4 box = builtBox;
            Matrix4x4 worldToBox = box.inverse;
            m.SetVector(ProjRow0Id, worldToBox.GetRow(0));
            m.SetVector(ProjRow1Id, worldToBox.GetRow(1));
            m.SetVector(ProjRow2Id, worldToBox.GetRow(2));
            Vector3 axis = box.GetColumn(2);
            axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.down;
            m.SetVector(ProjDirId, axis);

            if (respectSunShadows) m.EnableKeyword(SunShadowKeyword);
            else m.DisableKeyword(SunShadowKeyword);
        }

        private Sprite CurrentSprite()
        {
            if (frames != null && frames.Length > 0)
            {
                float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
                int index = frameRate > 0f ? Mathf.FloorToInt(time * frameRate + seed) : 0;
                index = ((index % frames.Length) + frames.Length) % frames.Length;
                Sprite frame = frames[index];
                if (frame != null) return frame;
            }
            return sprite;
        }

        /// <summary>The sprite's area in its texture, as (uv offset, uv size).</summary>
        private static Vector4 SpriteUVRect(Sprite s)
        {
            Texture2D tex = s.texture;
            if (tex == null) return new Vector4(0f, 0f, 1f, 1f);
            bool tight = s.packed && s.packingMode == SpritePackingMode.Tight;
            if (!tight)
            {
                Rect r = s.textureRect;
                return new Vector4(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
            }
            // Tightly packed: textureRect is not available; use the bounds of the sprite's uvs.
            Vector2[] uv = s.uv;
            if (uv == null || uv.Length == 0) return new Vector4(0f, 0f, 1f, 1f);
            Vector2 min = uv[0], max = uv[0];
            for (int i = 1; i < uv.Length; i++)
            {
                min = Vector2.Min(min, uv[i]);
                max = Vector2.Max(max, uv[i]);
            }
            return new Vector4(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        private static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        // ---------------- Presets ----------------

        /// <summary>Starter cookie for a preset (see the editor's Generate button): file name without extension.</summary>
        public static string StarterCookieName(Preset preset)
        {
            switch (preset)
            {
                case Preset.WindowLight:
                case Preset.FirelitWindow: return "Cookie_Window";
                case Preset.Blinds: return "Cookie_Blinds";
                case Preset.StainedGlass: return "Cookie_StainedGlass";
                case Preset.LeafDapple: return "Cookie_LeafDapple";
                case Preset.CloudShadows: return "Cookie_Clouds";
                case Preset.DragonShadow: return "Cookie_WingedSilhouette";
                default: return "Cookie_SoftBlob";
            }
        }

        /// <summary>Sets every look/projection setting for the preset. Leaves the image and anchor alone.</summary>
        public void ApplyPreset(Preset preset)
        {
            // Shared defaults
            channel = CookieChannel.Alpha;
            invert = false;
            flipX = flipY = false;
            customDirection = new Vector3(0.35f, -1f, 0.5f);
            anchorOffset = Vector3.zero;
            startOffset = 0f;
            rotation = 0f;
            repeat = false;
            tiling = Vector2.one;
            respectSunShadows = false;
            softness = 0.06f;
            bands = 3;
            dither = 1f;
            pixelsPerUnit = 32f;
            nearFade = 0.02f;
            farFade = 0.25f;
            edgeFade = 0.05f;
            facingFade = 0f;
            scroll = Vector2.zero;
            sway = 0f;
            swaySpeed = 1.2f;
            flicker = 0f;
            flickerSpeed = 3f;
            frameRate = 8f;

            switch (preset)
            {
                case Preset.WindowLight:
                    effect = Effect.LightMultiply;
                    direction = Direction.TransformForward;
                    color = new Color(1f, 0.82f, 0.5f) * 1.1f;
                    strength = 1f;
                    size = new Vector2(1.6f, 2.2f);
                    distance = 6f;
                    softness = 0.04f;
                    facingFade = 0.6f;
                    break;

                case Preset.FirelitWindow:
                    effect = Effect.LightAdditive;
                    direction = Direction.TransformForward;
                    color = new Color(1f, 0.45f, 0.12f) * 0.8f;
                    strength = 0.9f;
                    size = new Vector2(1.6f, 2.2f);
                    distance = 5f;
                    softness = 0.1f;
                    farFade = 0.5f;
                    facingFade = 0.6f;
                    flicker = 0.45f;
                    flickerSpeed = 3.5f;
                    sway = 0.01f;
                    swaySpeed = 4f;
                    break;

                case Preset.Blinds:
                    effect = Effect.LightMultiply;
                    direction = Direction.TransformForward;
                    color = new Color(1f, 0.92f, 0.75f) * 1.1f;
                    strength = 1f;
                    size = new Vector2(2f, 2f);
                    distance = 6f;
                    softness = 0.02f;
                    bands = 2;
                    facingFade = 0.6f;
                    break;

                case Preset.StainedGlass:
                    effect = Effect.LightMultiply;
                    direction = Direction.TransformForward;
                    channel = CookieChannel.ColorAndAlpha;
                    color = Color.white * 1.3f;
                    strength = 1f;
                    size = new Vector2(1.5f, 2.4f);
                    distance = 7f;
                    softness = 0.03f;
                    facingFade = 0.6f;
                    break;

                case Preset.LeafDapple:
                    effect = Effect.LightMultiply;
                    direction = Direction.Sun;
                    color = new Color(1f, 0.95f, 0.7f) * 0.9f;
                    strength = 1f;
                    size = new Vector2(8f, 8f);
                    distance = 20f;
                    startOffset = -10f;
                    repeat = true;
                    tiling = new Vector2(2f, 2f);
                    softness = 0.12f;
                    edgeFade = 0.25f;
                    farFade = 0f;
                    nearFade = 0f;
                    sway = 0.012f;
                    swaySpeed = 1.1f;
                    respectSunShadows = true;
                    break;

                case Preset.CloudShadows:
                    effect = Effect.Shadow;
                    direction = Direction.Sun;
                    color = new Color(0.45f, 0.5f, 0.68f);
                    strength = 0.8f;
                    size = new Vector2(40f, 40f);
                    distance = 40f;
                    startOffset = -20f;
                    repeat = true;
                    tiling = new Vector2(1.5f, 1.5f);
                    softness = 0.4f;
                    bands = 3;
                    edgeFade = 0.2f;
                    nearFade = 0f;
                    farFade = 0f;
                    scroll = new Vector2(0.006f, 0.002f);
                    respectSunShadows = true;
                    break;

                case Preset.DragonShadow:
                    effect = Effect.Shadow;
                    direction = Direction.StraightDown;
                    color = new Color(0.2f, 0.2f, 0.35f);
                    strength = 0.8f;
                    size = new Vector2(7f, 7f);
                    distance = 30f;
                    startOffset = -15f;
                    softness = 0.1f;
                    bands = 2;
                    edgeFade = 0f;
                    nearFade = 0f;
                    farFade = 0f;
                    respectSunShadows = true;
                    break;

                default: // SoftBlobShadow
                    effect = Effect.Shadow;
                    direction = Direction.StraightDown;
                    color = new Color(0.3f, 0.3f, 0.45f);
                    strength = 0.7f;
                    size = new Vector2(3f, 3f);
                    distance = 10f;
                    startOffset = -5f;
                    softness = 0f;
                    bands = 3;
                    edgeFade = 0f;
                    nearFade = 0f;
                    farFade = 0f;
                    break;
            }
            Refresh();
        }

        // ---------------- Gizmos ----------------

        private void OnDrawGizmos()
        {
            DrawBoxGizmo(new Color(1f, 0.85f, 0.4f, 0.25f), false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawBoxGizmo(effect == Effect.Shadow ? new Color(0.45f, 0.55f, 1f, 0.9f) : new Color(1f, 0.85f, 0.4f, 0.9f), true);
        }

        private void DrawBoxGizmo(Color c, bool detailed)
        {
            Matrix4x4 box = BoxMatrix();
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.color = c;
            Gizmos.matrix = box;
            Gizmos.DrawWireCube(new Vector3(0f, 0f, 0.5f), Vector3.one);
            if (detailed)
            {
                // Image plane (solid-ish) and the "top of image" tick.
                Gizmos.color = new Color(c.r, c.g, c.b, 0.15f);
                Gizmos.DrawCube(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 0.001f));
                Gizmos.color = c;
                Gizmos.DrawLine(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.62f, 0f));
            }
            Gizmos.matrix = previous;
            if (detailed)
            {
                Vector3 start = box.MultiplyPoint3x4(Vector3.zero);
                Vector3 end = box.MultiplyPoint3x4(new Vector3(0f, 0f, 1f));
                Gizmos.DrawLine(start, end);
                Gizmos.DrawWireSphere(end, 0.08f);
            }
        }

        // ---------------- Depth texture guard ----------------

        /// <summary>
        /// The projector reads the camera's Depth Texture. While any projector is active, game cameras whose URP
        /// settings would skip it get it turned on for their render only (restored right after).
        /// </summary>
        private static class DepthTextureGuard
        {
            private static readonly HashSet<LightShadowProjector> Active = new();
            private static readonly List<(UniversalAdditionalCameraData data, CameraOverrideOption option)> Forced = new();
            private static bool hooked;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetOnPlay()
            {
                Active.Clear();
                Forced.Clear();
            }

            public static void Register(LightShadowProjector p)
            {
                Active.Add(p);
                if (hooked) return;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                RenderPipelineManager.endCameraRendering += OnEndCamera;
                hooked = true;
            }

            public static void Unregister(LightShadowProjector p)
            {
                Active.Remove(p);
            }

            private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
            {
                if (Active.Count == 0 || camera == null || camera.cameraType != CameraType.Game) return;
                if (!camera.TryGetComponent(out UniversalAdditionalCameraData data)) return;
                if (data.renderType != CameraRenderType.Base) return;
                CameraOverrideOption option = data.requiresDepthOption;
                if (option == CameraOverrideOption.On) return;
                if (option == CameraOverrideOption.UsePipelineSettings)
                {
                    UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
                    if (asset == null || asset.supportsCameraDepthTexture) return;
                }
                Forced.Add((data, option));
                data.requiresDepthOption = CameraOverrideOption.On;
            }

            private static void OnEndCamera(ScriptableRenderContext context, Camera camera)
            {
                for (int i = Forced.Count - 1; i >= 0; i--)
                {
                    (UniversalAdditionalCameraData data, CameraOverrideOption option) = Forced[i];
                    if (data == null)
                    {
                        Forced.RemoveAt(i);
                        continue;
                    }
                    if (data.gameObject != camera.gameObject) continue;
                    data.requiresDepthOption = option;
                    Forced.RemoveAt(i);
                }
            }
        }
    }
}
