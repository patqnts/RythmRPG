using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RythmRPG.Rendering
{
    /// <summary>
    /// Draws the inverted-hull pass from materials using Pixel Toon Outline.
    /// Compatible with Unity 6's Render Graph and the project's URP 2D Renderer.
    /// </summary>
    public sealed class PixelToonOutlineFeature : ScriptableRendererFeature
    {
        [SerializeField] private LayerMask layerMask = ~0;

        private PixelToonOutlinePass outlinePass;

        public override void Create()
        {
            outlinePass = new PixelToonOutlinePass(layerMask)
            {
                // In the 2D Renderer this maps to the point after all scene
                // renderers and before post-processing, so depth is available.
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (outlinePass != null)
                renderer.EnqueuePass(outlinePass);
        }

        private sealed class PixelToonOutlinePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId OutlineTag = new("PixelToonOutline");
            private static readonly ProfilingSampler Profiling = new("Pixel Toon Outlines");

            private readonly LayerMask layerMask;

            private sealed class PassData
            {
                internal RendererListHandle rendererList;
            }

            internal PixelToonOutlinePass(LayerMask layerMask)
            {
                this.layerMask = layerMask;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                    OutlineTag,
                    renderingData,
                    cameraData,
                    lightData,
                    cameraData.defaultOpaqueSortFlags);

                FilteringSettings filteringSettings = new(RenderQueueRange.opaque, layerMask);
                RendererListParams rendererListParams = new(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings);

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                    "Pixel Toon Outlines",
                    out PassData passData,
                    Profiling);

                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                if (!passData.rendererList.IsValid())
                    return;

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }

#if URP_COMPATIBILITY_MODE
#pragma warning disable CS0618
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                DrawingSettings drawingSettings = CreateDrawingSettings(
                    OutlineTag,
                    ref renderingData,
                    renderingData.cameraData.defaultOpaqueSortFlags);
                FilteringSettings filteringSettings = new(RenderQueueRange.opaque, layerMask);
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
            }
#pragma warning restore CS0618
#endif
        }
    }
}
