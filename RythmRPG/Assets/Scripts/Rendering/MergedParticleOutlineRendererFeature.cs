using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PixelMetaballParticles
{
    /// <summary>
    /// Unity 6000.3.x / URP RenderGraph-only particle outline.
    ///
    /// Pipeline:
    ///   Particle material
    ///       -> custom "MergedParticleMask" shader pass
    ///       -> ONE additive alpha field
    ///       -> threshold into one metaball/merged silhouette
    ///       -> screen-space pixel dilation
    ///       -> one external outline
    ///
    /// Overlapping particles therefore do NOT receive outlines between each other.
    /// </summary>
    public sealed class MergedParticleOutlineRendererFeature : ScriptableRendererFeature
    {
        public enum DebugView
        {
            Off = 0,
            MaskField = 1,
            ThresholdedMask = 2,
            OutlineOnly = 3
        }

        [System.Serializable]
        public sealed class Settings
        {
            [Header("Filtering")]
            [Tooltip("Only objects on these layers can contribute. Everything is a safe default because only shaders with the MergedParticleMask pass are drawn.")]
            public LayerMask particleLayers = ~0;

            [Header("Merged Outline")]
            public Color outlineColor = new Color(0.035f, 0.025f, 0.06f, 1f);

            [Range(0.25f, 4f)]
            [Tooltip("Width in pixels of the camera render target. Fractional values are supported, e.g. 0.6 for a thinner 1px-style contour.")]
            public float outlinePixels = 0.8f;

            [Range(0.001f, 0.25f)]
            [Tooltip("Softens only the OUTLINE edge. Increase slightly if the contour looks too staircase-like.")]
            public float outlineSoftness = 0.05f;

            [Range(0.01f, 2.0f)]
            [Tooltip("Threshold applied to the additive particle field. Lower values merge soft particles more aggressively.")]
            public float metaballThreshold = 0.35f;

            [Header("Depth / Cameras")]
            [Tooltip("Depth-test the mask against the scene. Recommended for a 2D-in-3D orthographic world.")]
            public bool respectSceneDepth = true;

            public bool showInSceneView = true;

            [Header("Debug")]
            public DebugView debugView = DebugView.Off;

            [Header("Injection")]
            [Tooltip("Keep this at AfterRenderingTransparents for normal transparent particle systems.")]
            public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
        }

        public Settings settings = new Settings();

        private Material _compositeMaterial;
        private MergedParticleOutlinePass _pass;

        public override void Create()
        {
            CoreUtils.Destroy(_compositeMaterial);

            Shader compositeShader =
                Shader.Find("Hidden/PixelMetaballParticles/MergedOutlineComposite");

            if (compositeShader != null)
                _compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);

            _pass = new MergedParticleOutlinePass(settings, _compositeMaterial)
            {
                renderPassEvent = settings.injectionPoint
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (_pass == null || _compositeMaterial == null)
                return;

            CameraType cameraType = renderingData.cameraData.cameraType;

            if (cameraType == CameraType.Preview ||
                cameraType == CameraType.Reflection)
                return;

            if (cameraType == CameraType.SceneView && !settings.showInSceneView)
                return;

            // Unity 6 expects custom passes to declare their inputs. Requesting
            // Color lets URP decide whether an intermediate camera color target
            // is required. Depth is also requested when the mask should be
            // occluded by scene geometry.
            ScriptableRenderPassInput input = ScriptableRenderPassInput.Color;

            if (settings.respectSceneDepth)
                input |= ScriptableRenderPassInput.Depth;

            _pass.ConfigureInput(input);
            _pass.renderPassEvent = settings.injectionPoint;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_compositeMaterial);
            _compositeMaterial = null;
        }

        private sealed class MergedParticleOutlinePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId MaskShaderTag =
                new ShaderTagId("MergedParticleMask");

            private static readonly int MaskTextureId =
                Shader.PropertyToID("_MergedParticleMask");

            private static readonly int OutlineColorId =
                Shader.PropertyToID("_OutlineColor");

            private static readonly int OutlinePixelsId =
                Shader.PropertyToID("_OutlinePixels");

            private static readonly int ThresholdId =
                Shader.PropertyToID("_MetaballThreshold");

            private static readonly int OutlineSoftnessId =
                Shader.PropertyToID("_OutlineSoftness");

            private static readonly int MaskTexelSizeId =
                Shader.PropertyToID("_MaskTexelSize");

            private static readonly int DebugModeId =
                Shader.PropertyToID("_DebugMode");

            private readonly Settings _settings;
            private readonly Material _compositeMaterial;

            private sealed class MaskPassData
            {
                public RendererListHandle rendererList;
            }

            private sealed class CompositePassData
            {
                public TextureHandle source;
                public Material material;
            }

            public MergedParticleOutlinePass(
                Settings settings,
                Material compositeMaterial)
            {
                _settings = settings;
                _compositeMaterial = compositeMaterial;
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_compositeMaterial == null)
                    return;

                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();

                UniversalRenderingData renderingData =
                    frameData.Get<UniversalRenderingData>();

                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();

                UniversalLightData lightData =
                    frameData.Get<UniversalLightData>();

                // We read the camera color in the composite pass, so do not try
                // to sample directly from a back buffer.
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                // ------------------------------------------------------------
                // 1. CREATE THE SHARED PARTICLE FIELD
                // ------------------------------------------------------------

                RenderTextureDescriptor maskDescriptor =
                    cameraData.cameraTargetDescriptor;

                maskDescriptor.depthBufferBits = 0;
                maskDescriptor.msaaSamples = 1;
                maskDescriptor.graphicsFormat =
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;

                TextureHandle maskTexture =
                    UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        maskDescriptor,
                        "_MergedParticleMask",
                        true);

                DrawingSettings drawingSettings =
                    RenderingUtils.CreateDrawingSettings(
                        MaskShaderTag,
                        renderingData,
                        cameraData,
                        lightData,
                        SortingCriteria.CommonTransparent);

                // IMPORTANT:
                // No override material is used here.
                //
                // The RendererList explicitly selects the MergedParticleMask
                // pass that lives INSIDE the particle's own material shader.
                // Therefore _BaseMap, _BaseColor, particle vertex color, and
                // metaball field settings are preserved.
                FilteringSettings filteringSettings =
                    new FilteringSettings(
                        RenderQueueRange.transparent,
                        _settings.particleLayers);

                RendererListParams rendererListParams =
                    new RendererListParams(
                        renderingData.cullResults,
                        drawingSettings,
                        filteringSettings);

                RendererListHandle rendererList =
                    renderGraph.CreateRendererList(rendererListParams);

                using (var builder =
                    renderGraph.AddRasterRenderPass<MaskPassData>(
                        "Pixel Metaball Particle Mask",
                        out var passData))
                {
                    passData.rendererList = rendererList;

                    builder.UseRendererList(rendererList);

                    builder.SetRenderAttachment(
                        maskTexture,
                        0,
                        AccessFlags.Write);

                    if (_settings.respectSceneDepth &&
                        resourceData.activeDepthTexture.IsValid())
                    {
                        builder.SetRenderAttachmentDepth(
                            resourceData.activeDepthTexture,
                            AccessFlags.Read);
                    }

                    // Unity 6 RenderGraph API: this method belongs to the pass
                    // builder, NOT the RenderGraph object.
                    builder.SetGlobalTextureAfterPass(
                        maskTexture,
                        MaskTextureId);

                    builder.SetRenderFunc(
                        static (MaskPassData data, RasterGraphContext context) =>
                        {
                            // Keep depth; clear only our R8 color field.
                            context.cmd.ClearRenderTarget(
                                false,
                                true,
                                Color.clear);

                            context.cmd.DrawRendererList(data.rendererList);
                        });
                }

                // ------------------------------------------------------------
                // 2. COMPOSITE ONE OUTLINE AROUND THE THRESHOLDED UNION
                // ------------------------------------------------------------

                TextureHandle source = resourceData.activeColorTexture;

                if (!source.IsValid())
                    return;

                int width = Mathf.Max(1, maskDescriptor.width);
                int height = Mathf.Max(1, maskDescriptor.height);

                _compositeMaterial.SetColor(
                    OutlineColorId,
                    _settings.outlineColor);

                _compositeMaterial.SetFloat(
                    OutlinePixelsId,
                    _settings.outlinePixels);

                _compositeMaterial.SetFloat(
                    ThresholdId,
                    _settings.metaballThreshold);

                _compositeMaterial.SetFloat(
                    OutlineSoftnessId,
                    _settings.outlineSoftness);

                _compositeMaterial.SetVector(
                    MaskTexelSizeId,
                    new Vector4(
                        1f / width,
                        1f / height,
                        width,
                        height));

                _compositeMaterial.SetFloat(
                    DebugModeId,
                    (float)_settings.debugView);

                RenderTextureDescriptor resultDescriptor =
                    cameraData.cameraTargetDescriptor;

                resultDescriptor.depthBufferBits = 0;
                resultDescriptor.msaaSamples = 1;

                TextureHandle destination =
                    UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        resultDescriptor,
                        "_PixelMetaballOutlineResult",
                        false);

                using (var builder =
                    renderGraph.AddRasterRenderPass<CompositePassData>(
                        "Pixel Metaball Particle Outline",
                        out var passData))
                {
                    passData.source = source;
                    passData.material = _compositeMaterial;

                    builder.UseTexture(
                        source,
                        AccessFlags.Read);

                    // Declares the dependency on the texture published by the
                    // preceding mask pass.
                    builder.UseGlobalTexture(
                        MaskTextureId,
                        AccessFlags.Read);

                    builder.SetRenderAttachment(
                        destination,
                        0,
                        AccessFlags.Write);

                    builder.SetRenderFunc(
                        static (
                            CompositePassData data,
                            RasterGraphContext context) =>
                        {
                            Blitter.BlitTexture(
                                context.cmd,
                                data.source,
                                new Vector4(1f, 1f, 0f, 0f),
                                data.material,
                                0);
                        });
                }

                // RenderGraph best practice: point camera color at the result
                // instead of doing a second blit back to the previous texture.
                resourceData.cameraColor = destination;
            }
        }
    }
}
