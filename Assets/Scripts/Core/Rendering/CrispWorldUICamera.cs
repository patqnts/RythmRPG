using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RythmRPG.Core
{
    /// <summary>
    /// Draws the <see cref="CrispWorldUI"/> layer at full screen resolution, exactly over the upscaled pixel render
    /// texture. It is a URP Overlay camera stacked on the "Pixel Display Camera" (which draws the render texture's
    /// RawImage). Every frame, after Cinemachine and camera shake have moved the pixel camera, it copies that camera's
    /// pose and projection and fits the projection to where the RawImage actually sits on screen (the Envelope
    /// aspect fitter crops non-16:9 screens, and the RawImage's uvRect is respected), so crisp objects stay
    /// glued to the pixel world.
    /// <para>
    /// Occlusion: crisp UI cannot sort against the pixel world (different camera). So in Play mode, the silhouettes of
    /// <see cref="CrispWorldUIOccluder"/> objects (player, enemy, notes) are drawn into a mask with the pixel camera's
    /// view and pixel grid, and a full-screen pass at the start of this camera sets stencil bit
    /// <see cref="CrispWorldUI.OcclusionStencilBit"/> there. Materials set up with
    /// <see cref="CrispWorldUI.MakeOccludable"/> (hit line, key markers) are hidden on those pixels, so characters and
    /// notes appear in front of them. Judgement text and ability slots do not test the stencil and stay on top.
    /// </para>
    /// <para>Created by Tools > Rythm RPG > Rendering > Set Up Crisp World UI Camera.</para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(10000)]
    public sealed class CrispWorldUICamera : MonoBehaviour
    {
        private const string MaskShaderPath = "Rendering/CrispOccluderMask";
        private const string StencilShaderPath = "Rendering/CrispOcclusionStencil";

        private static readonly int PixelRectId = Shader.PropertyToID("_CrispPixelRect");
        private static readonly int OcclusionMaskId = Shader.PropertyToID("_CrispOcclusionMask");
        private static readonly int OccluderViewProjectionId = Shader.PropertyToID("_CrispOccluderViewProjection");
        private static readonly int OccluderFlipId = Shader.PropertyToID("_CrispOccluderFlip");
        private static readonly int OccluderAlphaId = Shader.PropertyToID("_CrispOccluderAlpha");
        private static readonly int OccluderCutoffId = Shader.PropertyToID("_CrispOccluderCutoff");

        [Tooltip("The low-resolution pixel camera (renders into the render texture). Defaults to Camera.main.")]
        [SerializeField] private Camera source;
        [Tooltip("The RawImage that shows the pixel camera's render texture on screen. Found automatically when empty.")]
        [SerializeField] private RawImage output;
        [Tooltip("At runtime, remove the CrispWorldUI layer from the pixel camera's culling mask so nothing is drawn twice.")]
        [SerializeField] private bool excludeLayerFromSource = true;

        [Header("Occlusion (Play mode)")]
        [Tooltip("Characters and notes (CrispWorldUIOccluder) cover the hit line and key markers, as they did inside the pixel render.")]
        [SerializeField] private bool occlusion = true;
        [Tooltip("Sprite alpha above this counts as covering.")]
        [SerializeField, Range(0.01f, 1f)] private float occlusionAlphaCutoff = 0.5f;

        private Camera crisp;
        private readonly Vector3[] corners = new Vector3[4];

        private RenderTexture occlusionMask;
        private CommandBuffer occlusionCommands;
        private Material maskMaterial;
        private Material stencilMaterial;
        private Mesh stencilQuadMesh;
        private GameObject stencilQuad;
        private MeshRenderer stencilRenderer;
        private bool missingShaderReported;
        private readonly List<Renderer> occluderRenderers = new();

        /// <summary>The enabled crisp camera, or null (then <see cref="CrispWorldUI.Apply"/> does nothing).</summary>
        public static CrispWorldUICamera Active { get; private set; }

        public Camera Source => source;
        public RawImage Output => output;

        public void Configure(Camera sourceCamera, RawImage outputImage)
        {
            source = sourceCamera;
            output = outputImage;
            ApplyMasks();
            Sync();
        }

        private void OnEnable()
        {
            crisp = GetComponent<Camera>();
            Active = this;
            RenderPipelineManager.beginContextRendering += HandleBeginContextRendering;
            ApplyMasks();
            Sync();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= HandleBeginContextRendering;
            if (Active == this) Active = null;
            if (crisp != null) crisp.ResetProjectionMatrix();
            ReleaseOcclusion();
        }

        private void LateUpdate() => Sync();

        // Runs after every LateUpdate (Cinemachine brain, CombatCameraShaker, note movement) and before any camera renders.
        private void HandleBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Oblique projection (if any) first: its matrix and upright sprites must be final before the copy and the occluder mask.
            if (ResolveSource()) ObliqueProjection.Prepare(source, true);
            Sync();
            if (Application.isPlaying) RenderOcclusion(context);
        }

        private void ApplyMasks()
        {
            int layer = CrispWorldUI.Layer;
            if (crisp == null || layer < 0) return;
            crisp.cullingMask = 1 << layer;
            if (Application.isPlaying && excludeLayerFromSource && ResolveSource())
                source.cullingMask &= ~(1 << layer);
        }

        private bool ResolveSource()
        {
            if (source != null && source != crisp) return true;
            source = Camera.main;
            if (source == crisp) source = null;
            return source != null;
        }

        private RawImage ResolveOutput()
        {
            RenderTexture target = source != null ? source.targetTexture : null;
            if (target == null) return null;
            if (output != null && output.texture == target) return output;
            output = null;
            foreach (RawImage candidate in FindObjectsByType<RawImage>(FindObjectsInactive.Exclude))
            {
                if (candidate.texture != target) continue;
                output = candidate;
                break;
            }
            return output;
        }

        private void Sync()
        {
            if (crisp == null || !ResolveSource()) return;

            Transform from = source.transform;
            if (transform.position != from.position || transform.rotation != from.rotation)
                transform.SetPositionAndRotation(from.position, from.rotation);
            crisp.orthographic = source.orthographic;
            crisp.orthographicSize = source.orthographicSize;
            crisp.fieldOfView = source.fieldOfView;
            crisp.nearClipPlane = source.nearClipPlane;
            crisp.farClipPlane = source.farClipPlane;

            ObliqueProjection.Prepare(source, false);
            Matrix4x4 projection = source.projectionMatrix;
            Rect rect = new(0f, 0f, 1f, 1f);
            if (TryGetOutputViewportRect(out Rect fitted))
            {
                rect = fitted;
                // Pixel camera clip space -> the part of the screen the render texture covers.
                Matrix4x4 fit = Matrix4x4.identity;
                fit.m00 = rect.width;
                fit.m03 = 2f * rect.x + rect.width - 1f;
                fit.m11 = rect.height;
                fit.m13 = 2f * rect.y + rect.height - 1f;
                projection = fit * projection;
            }
            crisp.projectionMatrix = projection;
            Shader.SetGlobalVector(PixelRectId, new Vector4(rect.x, rect.y, rect.width, rect.height));
        }

        /// <summary>
        /// Where the whole render texture lands on screen, in 0..1 screen units (can extend past 0..1 when the
        /// Envelope fitter crops it). False when the output is missing or not on a screen-space canvas.
        /// </summary>
        private bool TryGetOutputViewportRect(out Rect rect)
        {
            rect = new Rect(0f, 0f, 1f, 1f);
            RawImage image = ResolveOutput();
            if (image == null || image.canvas == null) return false;
            Canvas root = image.canvas.rootCanvas;
            if (root == null || root.renderMode == RenderMode.WorldSpace) return false;

            var rootRect = (RectTransform)root.transform;
            Rect bounds = rootRect.rect;
            if (bounds.width <= 0f || bounds.height <= 0f) return false;

            image.rectTransform.GetWorldCorners(corners);
            Vector3 min = rootRect.InverseTransformPoint(corners[0]);
            Vector3 max = rootRect.InverseTransformPoint(corners[2]);
            float x0 = (min.x - bounds.xMin) / bounds.width;
            float y0 = (min.y - bounds.yMin) / bounds.height;
            float x1 = (max.x - bounds.xMin) / bounds.width;
            float y1 = (max.y - bounds.yMin) / bounds.height;
            if (float.IsNaN(x0) || float.IsNaN(y0) || float.IsNaN(x1) || float.IsNaN(y1)) return false;

            Rect uv = image.uvRect;
            if (Mathf.Abs(uv.width) < 0.0001f || Mathf.Abs(uv.height) < 0.0001f) return false;
            float width = (x1 - x0) / uv.width;
            float height = (y1 - y0) / uv.height;
            if (width <= 0f || height <= 0f) return false;
            rect = new Rect(x0 - uv.x * width, y0 - uv.y * height, width, height);
            return true;
        }

        // ---------- Occlusion ----------

        private void RenderOcclusion(ScriptableRenderContext context)
        {
            RenderTexture target = source != null ? source.targetTexture : null;
            bool wanted = occlusion && crisp != null && CrispWorldUI.Layer >= 0 && target != null
                          && CrispWorldUIOccluder.Active.Count > 0;
            if (!wanted || !EnsureOcclusionResources(target.width, target.height))
            {
                if (stencilRenderer != null) stencilRenderer.enabled = false;
                return;
            }

            CommandBuffer commands = occlusionCommands;
            commands.Clear();
            commands.SetRenderTarget(occlusionMask);
            commands.ClearRenderTarget(false, true, Color.clear);
            // Same view and pixel grid as the pixel camera; "into texture" so the mask is sampled upright like the RT.
            commands.SetGlobalMatrix(OccluderViewProjectionId,
                GL.GetGPUProjectionMatrix(source.projectionMatrix, true) * source.worldToCameraMatrix);
            commands.SetGlobalFloat(OccluderCutoffId, occlusionAlphaCutoff);

            int drawn = 0;
            int visibleLayers = source.cullingMask;
            foreach (CrispWorldUIOccluder occluder in CrispWorldUIOccluder.Active)
            {
                if (occluder == null) continue;
                occluder.GetRenderers(occluderRenderers);
                foreach (Renderer candidate in occluderRenderers)
                {
                    if (!IsDrawableOccluder(candidate, visibleLayers)) continue;
                    if (candidate is SpriteRenderer sprite)
                    {
                        if (sprite.sprite == null) continue;
                        // Sprite flip and colour are applied by the sprite shader, not baked into the mesh.
                        commands.SetGlobalVector(OccluderFlipId, new Vector4(sprite.flipX ? -1f : 1f, sprite.flipY ? -1f : 1f, 1f, 1f));
                        commands.SetGlobalFloat(OccluderAlphaId, sprite.color.a);
                        commands.DrawRenderer(sprite, maskMaterial, 0, 0);
                        drawn++;
                        continue;
                    }
                    commands.SetGlobalVector(OccluderFlipId, Vector4.one);
                    commands.SetGlobalFloat(OccluderAlphaId, 1f);
                    int subMeshes = Mathf.Max(1, SubMeshCount(candidate));
                    for (int i = 0; i < subMeshes; i++) commands.DrawRenderer(candidate, maskMaterial, i, 0);
                    drawn++;
                }
            }
            occluderRenderers.Clear();

            if (drawn > 0)
            {
                context.ExecuteCommandBuffer(commands);
                context.Submit();
                Shader.SetGlobalTexture(OcclusionMaskId, occlusionMask);
            }
            stencilRenderer.enabled = drawn > 0;
        }

        private static bool IsDrawableOccluder(Renderer candidate, int visibleLayers)
        {
            if (candidate == null || !candidate.enabled || candidate.forceRenderingOff) return false;
            if (candidate.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            if ((visibleLayers & (1 << candidate.gameObject.layer)) == 0) return false;
            return candidate is SpriteRenderer || candidate is MeshRenderer || candidate is SkinnedMeshRenderer;
        }

        private static int SubMeshCount(Renderer candidate)
        {
            if (candidate is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh != null ? skinned.sharedMesh.subMeshCount : 0;
            return candidate.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null
                ? filter.sharedMesh.subMeshCount
                : 0;
        }

        private bool EnsureOcclusionResources(int width, int height)
        {
            if (maskMaterial == null || stencilMaterial == null)
            {
                Shader maskShader = Resources.Load<Shader>(MaskShaderPath);
                Shader stencilShader = Resources.Load<Shader>(StencilShaderPath);
                if (maskShader == null || stencilShader == null)
                {
                    if (!missingShaderReported)
                        Debug.LogWarning("[Crisp World UI] Occlusion shaders missing (Assets/Resources/Rendering/CrispOccluderMask.shader, " +
                                         "CrispOcclusionStencil.shader). The hit line will draw over characters.", this);
                    missingShaderReported = true;
                    return false;
                }
                maskMaterial = new Material(maskShader) { name = "Crisp Occluder Mask (runtime)", hideFlags = HideFlags.HideAndDontSave };
                stencilMaterial = new Material(stencilShader) { name = "Crisp Occlusion Stencil (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }

            if (occlusionMask == null || occlusionMask.width != width || occlusionMask.height != height)
            {
                if (occlusionMask != null) occlusionMask.Release();
                RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                    ? RenderTextureFormat.R8
                    : RenderTextureFormat.ARGB32;
                occlusionMask = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
                {
                    name = "Crisp Occlusion Mask",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                occlusionMask.Create();
            }

            occlusionCommands ??= new CommandBuffer { name = "Crisp World UI Occluder Mask" };

            if (stencilRenderer == null)
            {
                stencilQuadMesh = new Mesh { name = "Crisp Occlusion Quad", hideFlags = HideFlags.HideAndDontSave };
                // Positions are clip-space corners; the shader ignores the transform. Huge bounds: never culled.
                stencilQuadMesh.vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
                };
                stencilQuadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                stencilQuadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

                stencilQuad = new GameObject("Crisp Occlusion Stencil") { hideFlags = HideFlags.DontSave };
                stencilQuad.layer = CrispWorldUI.Layer;
                stencilQuad.transform.SetParent(transform, false);
                stencilQuad.AddComponent<MeshFilter>().sharedMesh = stencilQuadMesh;
                stencilRenderer = stencilQuad.AddComponent<MeshRenderer>();
                stencilRenderer.sharedMaterial = stencilMaterial;
                stencilRenderer.shadowCastingMode = ShadowCastingMode.Off;
                stencilRenderer.receiveShadows = false;
                stencilRenderer.lightProbeUsage = LightProbeUsage.Off;
                stencilRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                stencilRenderer.sortingOrder = short.MinValue; // before every crisp canvas
                stencilRenderer.enabled = false;
            }
            return true;
        }

        private void ReleaseOcclusion()
        {
            SafeDestroy(stencilQuad);
            SafeDestroy(stencilQuadMesh);
            SafeDestroy(maskMaterial);
            SafeDestroy(stencilMaterial);
            if (occlusionMask != null)
            {
                occlusionMask.Release();
                SafeDestroy(occlusionMask);
            }
            occlusionCommands?.Release();
            stencilQuad = null;
            stencilRenderer = null;
            stencilQuadMesh = null;
            maskMaterial = null;
            stencilMaterial = null;
            occlusionMask = null;
            occlusionCommands = null;
        }

        private static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
