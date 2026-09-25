using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RythmRPG.Core
{
    /// <summary>
    /// Makes the low-resolution pixel render rock-steady and smooth. Sits on the pixel camera (the one that renders into
    /// the render texture shown by a RawImage). Every frame, after Cinemachine and camera shake, and only while rendering:
    /// <list type="number">
    /// <item><b>Pixel grid.</b> The orthographic size is nudged so 1 world unit is exactly <see cref="pixelsPerUnit"/>
    /// render-texture pixels, which is what sprites are drawn at (no doubled or dropped sprite pixels).</item>
    /// <item><b>Camera snap.</b> The camera is moved to the nearest whole pixel, so the world is drawn on the same pixel
    /// grid every frame and never shimmers while the camera moves (damping, shake).</item>
    /// <item><b>Sub-pixel scroll.</b> The camera renders one extra pixel around the edge, and the upscaled image is slid
    /// by the part of a pixel the snap removed, so camera motion stays smooth instead of stepping.</item>
    /// <item><b>Object snap.</b> Moving objects (<see cref="PixelSnap"/>, CharacterControllers, and the combat's
    /// <see cref="CrispWorldUIOccluder"/>s: players, enemies, notes) are snapped to whole pixels too, so their sprites
    /// keep their shape.</item>
    /// <item><b>Follow stabilizer.</b> When the camera follows a snapped object (the player while exploring), the camera
    /// takes that object's rounding, so the player stays perfectly still on screen while walking.</item>
    /// <item><b>Upscaler.</b> The RawImage draws the render texture with a sharp-bilinear shader: every pixel is the same
    /// size at any screen resolution and the sub-pixel slide is smooth.</item>
    /// </list>
    /// Everything is render-only: positions, orthographic size, the render texture and the RawImage are put back as soon
    /// as the frame is drawn, so gameplay code always sees the real camera.
    /// <para>Added by Tools > Rythm RPG > Rendering > Add Pixel Perfect Rig To Pixel Camera.</para>
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(31000)] // after Cinemachine, the lane layout and CombatCameraShaker (30000); before canvases rebuild
    public sealed class PixelPerfectRig : MonoBehaviour
    {
        private const int Margin = 1;                 // extra render-texture pixels around the edge, for the sub-pixel slide
        private const float SnapBias = 1f / 64f;      // snapped pivots sit just past a pixel corner, never exactly on a sample
        private const float PixelsPerUnitTolerance = 0.05f;
        private const string UpscaleShaderPath = "Rendering/PixelArtUpscale";

        [Header("Pixel grid")]
        [Tooltip("Sprite pixels per world unit. The orthographic size is nudged so 1 unit is exactly this many render-" +
                 "texture pixels (only when it is already within 5%, so zooms are left alone). 0 = leave the size alone.")]
        [SerializeField, Min(0)] private int pixelsPerUnit = 32;
        [Tooltip("World point that sits exactly on a pixel corner (floor tiles line up with the pixel grid). " +
                 "Auto = the Oblique Projection's ground height at the world origin.")]
        [SerializeField] private bool autoGridOrigin = true;
        [SerializeField] private Vector3 gridOrigin;

        [Header("Camera")]
        [Tooltip("Snap the camera to whole render-texture pixels, so the world never shimmers when it moves.")]
        [SerializeField] private bool snapCamera = true;
        [Tooltip("Slide the upscaled image by the part of a pixel the camera snap removed, so camera motion is smooth " +
                 "instead of stepping one pixel at a time.")]
        [SerializeField] private bool smoothSubPixelScroll = true;

        [Header("Moving objects")]
        [Tooltip("Snap moving objects to whole pixels while rendering: PixelSnap objects, CharacterControllers and the " +
                 "combat's characters, enemies and notes.")]
        [SerializeField] private bool snapMovingObjects = true;
        [Tooltip("Give every CharacterController a PixelSnap automatically.")]
        [SerializeField] private bool autoSnapCharacterControllers = true;
        [Tooltip("When the active Cinemachine camera follows a snapped object (the player while exploring), keep that " +
                 "object perfectly still on screen; the camera takes its sub-pixel rounding instead.")]
        [SerializeField] private bool stabilizeFollowTarget = true;

        [Header("Display")]
        [Tooltip("Draw the render texture with a sharp-bilinear upscaler: every pixel the same size at any resolution, " +
                 "and a smooth sub-pixel slide. Off = plain point sampling (the slide then moves in whole screen pixels).")]
        [SerializeField] private bool pixelArtUpscaler = true;
        [Tooltip("The RawImage that shows the render texture. Found automatically when empty.")]
        [SerializeField] private RawImage output;

        private Camera cam;
        private CinemachineBrain brain;
        private RenderTexture original;
        private RenderTexture padded;
        private Material upscaleMaterial;
        private Material outputOriginalMaterial;
        private bool outputMaterialReplaced;
        private float nextControllerScan;
        private bool missingShaderReported;

        // Render-only state, put back after the frame is drawn.
        private bool applied;
        private Vector3 restorePosition;
        private float restoreSize;
        private Texture outputOriginalTexture;
        private Rect outputOriginalUv;
        private readonly List<(Transform transform, Vector3 position)> movedObjects = new();
        private readonly List<Transform> snapCandidates = new();
        private readonly HashSet<Transform> candidateSet = new();

        // Current pixel grid (valid while applied).
        private Matrix4x4 worldToClip;
        private Vector2 paddedSize;
        private Vector3 gridRight;
        private Vector3 gridForward;
        private Vector2 texelsPerRight;
        private Vector2 texelsPerForward;
        private Vector3 origin;

        /// <summary>The sub-pixel slide applied this frame, in render-texture pixels (for debugging).</summary>
        public Vector2 SubPixelOffset { get; private set; }

        private void OnEnable()
        {
            cam = GetComponent<Camera>();
            brain = GetComponent<CinemachineBrain>();
            RenderPipelineManager.endContextRendering += HandleEndContextRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.endContextRendering -= HandleEndContextRendering;
            Restore();
            RestoreOutputMaterial();
            if (padded != null)
            {
                if (cam != null && cam.targetTexture == padded) cam.targetTexture = original;
                padded.Release();
                Destroy(padded);
                padded = null;
            }
            if (upscaleMaterial != null) Destroy(upscaleMaterial);
            upscaleMaterial = null;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            Restore(); // the previous frame was not drawn (minimised window, no game view)
            if (cam == null || !cam.orthographic || !ResolveTargets()) return;
            if (autoSnapCharacterControllers && Time.unscaledTime >= nextControllerScan) ScanCharacterControllers();
            Apply();
        }

        // ---------- setup ----------

        private bool ResolveTargets()
        {
            RenderTexture current = cam.targetTexture;
            if (current != null && current != padded) original = current;
            if (original == null) return false;

            if (output == null || (output.texture != original && output.texture != padded))
            {
                output = null;
                foreach (RawImage candidate in FindObjectsByType<RawImage>(FindObjectsInactive.Exclude))
                {
                    if (candidate.texture != original) continue;
                    output = candidate;
                    break;
                }
            }
            if (output == null) return false;

            int width = original.width + 2 * Margin, height = original.height + 2 * Margin;
            FilterMode filter = pixelArtUpscaler ? FilterMode.Bilinear : FilterMode.Point;
            if (padded == null || padded.width != width || padded.height != height)
            {
                if (padded != null)
                {
                    padded.Release();
                    Destroy(padded);
                }
                RenderTextureDescriptor descriptor = original.descriptor;
                descriptor.width = width;
                descriptor.height = height;
                descriptor.msaaSamples = 1;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                padded = new RenderTexture(descriptor)
                {
                    name = original.name + " (Pixel Perfect)",
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                padded.Create();
            }
            padded.filterMode = filter;
            UpdateOutputMaterial();
            return true;
        }

        private void UpdateOutputMaterial()
        {
            if (!pixelArtUpscaler)
            {
                RestoreOutputMaterial();
                return;
            }
            if (upscaleMaterial == null)
            {
                Shader shader = Resources.Load<Shader>(UpscaleShaderPath);
                if (shader == null || !shader.isSupported)
                {
                    if (!missingShaderReported)
                        Debug.LogWarning($"[PixelPerfectRig] Upscale shader Resources/{UpscaleShaderPath} missing or unsupported; using point sampling.", this);
                    missingShaderReported = true;
                    if (padded != null) padded.filterMode = FilterMode.Point;
                    return;
                }
                upscaleMaterial = new Material(shader) { name = "Pixel Art Upscale (Runtime)", hideFlags = HideFlags.DontSave };
            }
            if (!outputMaterialReplaced)
            {
                outputOriginalMaterial = output.material == output.defaultMaterial ? null : output.material;
                outputMaterialReplaced = true;
            }
            if (output.material != upscaleMaterial) output.material = upscaleMaterial;
        }

        private void RestoreOutputMaterial()
        {
            if (!outputMaterialReplaced) return;
            outputMaterialReplaced = false;
            if (output != null) output.material = outputOriginalMaterial;
        }

        private void ScanCharacterControllers()
        {
            nextControllerScan = Time.unscaledTime + 2f;
            foreach (CharacterController controller in FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude))
                PixelSnap.Ensure(controller.gameObject);
        }

        // ---------- per frame ----------

        private void Apply()
        {
            Transform view = transform;
            restorePosition = view.position;
            restoreSize = cam.orthographicSize;
            outputOriginalTexture = output.texture;
            outputOriginalUv = output.uvRect;
            applied = true;

            int width = original.width, height = original.height;
            float size = cam.orthographicSize;
            if (pixelsPerUnit > 0)
            {
                float exact = height / (2f * pixelsPerUnit);
                if (Mathf.Abs(size / exact - 1f) < PixelsPerUnitTolerance) size = exact;
            }
            // Render one extra pixel all round; the RawImage crops it back off.
            cam.targetTexture = padded;
            cam.orthographicSize = size * (height + 2f * Margin) / height;
            paddedSize = new Vector2(padded.width, padded.height);
            RefreshGrid();

            // The player is followed while exploring: move the camera by the player's rounding so it stays still on screen.
            if (stabilizeFollowTarget && snapMovingObjects && TryGetFollowedSnapRoot(out Transform followed))
            {
                Vector3 rounding = SnapDelta(followed.position);
                if (rounding.sqrMagnitude > 0f)
                {
                    view.position += rounding;
                    RefreshGrid();
                }
            }

            Vector2 fraction = Vector2.zero;
            if (snapCamera)
            {
                Vector2 texel = Texel(origin);
                fraction = texel - new Vector2(Mathf.Round(texel.x), Mathf.Round(texel.y));
                // Moving the camera by w shifts every point by -J*w pixels: solve J*w = fraction.
                Vector3 move = WorldFromTexels(fraction);
                if (move.sqrMagnitude > 0f)
                {
                    view.position += move;
                    RefreshGrid();
                }
            }

            if (snapMovingObjects) SnapObjects();

            // The snapped image is drawn `fraction` pixels off from where the real camera would draw it; slide it back.
            Vector2 slide = snapCamera && smoothSubPixelScroll ? fraction : Vector2.zero;
            SubPixelOffset = slide;
            Rect uv = outputOriginalUv;
            output.texture = padded;
            output.uvRect = new Rect(
                (Margin - slide.x + uv.x * width) / padded.width,
                (Margin - slide.y + uv.y * height) / padded.height,
                uv.width * width / padded.width,
                uv.height * height / padded.height);
        }

        // Rebuilds the pixel mapping after the camera moved (and refreshes the oblique matrix, if any).
        private void RefreshGrid()
        {
            ObliqueProjection.Prepare(cam, false);
            worldToClip = cam.projectionMatrix * cam.worldToCameraMatrix;

            origin = gridOrigin;
            if (autoGridOrigin)
                origin = ObliqueProjection.TryGet(cam, out ObliqueProjection oblique)
                    ? new Vector3(0f, oblique.GroundHeight, 0f)
                    : Vector3.zero;

            Transform view = transform;
            gridRight = Vector3.ProjectOnPlane(view.right, Vector3.up);
            if (gridRight.sqrMagnitude < 0.000001f) gridRight = view.right;
            gridRight.Normalize();
            gridForward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (gridForward.sqrMagnitude < 0.000001f) gridForward = Vector3.ProjectOnPlane(view.up, Vector3.up);
            if (gridForward.sqrMagnitude < 0.000001f) gridForward = view.up;
            gridForward.Normalize();

            Vector2 at = Texel(origin);
            texelsPerRight = Texel(origin + gridRight) - at;
            texelsPerForward = Texel(origin + gridForward) - at;
        }

        // Render-texture pixel coordinates (padded texture, 0,0 = bottom-left corner) of a world point.
        private Vector2 Texel(Vector3 world)
        {
            Vector3 ndc = worldToClip.MultiplyPoint(world);
            return new Vector2((ndc.x * 0.5f + 0.5f) * paddedSize.x, (ndc.y * 0.5f + 0.5f) * paddedSize.y);
        }

        // World move (along the ground) that shifts a point by `texels` pixels on screen.
        private Vector3 WorldFromTexels(Vector2 texels)
        {
            float det = texelsPerRight.x * texelsPerForward.y - texelsPerForward.x * texelsPerRight.y;
            if (Mathf.Abs(det) < 0.000001f) return Vector3.zero;
            float a = (texels.x * texelsPerForward.y - texelsPerForward.x * texels.y) / det;
            float b = (texelsPerRight.x * texels.y - texels.x * texelsPerRight.y) / det;
            return gridRight * a + gridForward * b;
        }

        // World move that puts `world` on the pixel grid (a pixel corner relative to the grid origin, plus a tiny bias).
        private Vector3 SnapDelta(Vector3 world)
        {
            Vector2 relative = Texel(world) - Texel(origin);
            Vector2 target = new(Mathf.Round(relative.x) + SnapBias, Mathf.Round(relative.y) + SnapBias);
            return WorldFromTexels(target - relative);
        }

        private bool TryGetFollowedSnapRoot(out Transform root)
        {
            root = null;
            if (brain == null) brain = GetComponent<CinemachineBrain>();
            if (brain == null || brain.IsBlending) return false;
            if (!(brain.ActiveVirtualCamera is CinemachineVirtualCameraBase active) || active.Follow == null) return false;
            PixelSnap snap = active.Follow.GetComponentInParent<PixelSnap>();
            if (snap == null || !snap.isActiveAndEnabled) return false;
            root = snap.transform;
            return true;
        }

        private void SnapObjects()
        {
            snapCandidates.Clear();
            candidateSet.Clear();
            foreach (PixelSnap snap in PixelSnap.Active)
                if (snap != null && snap.isActiveAndEnabled && candidateSet.Add(snap.transform)) snapCandidates.Add(snap.transform);
            foreach (CrispWorldUIOccluder occluder in CrispWorldUIOccluder.Active)
                if (occluder != null && occluder.isActiveAndEnabled && candidateSet.Add(occluder.transform))
                    snapCandidates.Add(occluder.transform);
            if (snapCandidates.Count == 0) return;

            // Parents first; restored in reverse, so nested snaps come back exactly.
            snapCandidates.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
            foreach (Transform target in snapCandidates)
            {
                if (target == null || target == transform) continue;
                Vector3 position = target.position;
                Vector3 delta = SnapDelta(position);
                if (delta.sqrMagnitude < 1e-12f) continue;
                movedObjects.Add((target, position));
                target.position = position + delta;
            }
            snapCandidates.Clear();
            candidateSet.Clear();
        }

        // ---------- restore ----------

        private void HandleEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Only after the frame that drew this camera (the Scene view renders separately in the Editor).
            if (applied && cameras != null && cameras.Contains(cam)) Restore();
        }

        private void Restore()
        {
            if (!applied) return;
            applied = false;
            for (int i = movedObjects.Count - 1; i >= 0; i--)
                if (movedObjects[i].transform != null) movedObjects[i].transform.position = movedObjects[i].position;
            movedObjects.Clear();

            if (cam != null)
            {
                transform.position = restorePosition;
                cam.orthographicSize = restoreSize;
                if (cam.targetTexture == padded && original != null) cam.targetTexture = original;
                ObliqueProjection.Prepare(cam, false);
            }
            if (output != null)
            {
                output.texture = outputOriginalTexture;
                output.uvRect = outputOriginalUv;
            }
        }

        private static int Depth(Transform t)
        {
            int depth = 0;
            for (Transform p = t.parent; p != null; p = p.parent) depth++;
            return depth;
        }
    }
}
