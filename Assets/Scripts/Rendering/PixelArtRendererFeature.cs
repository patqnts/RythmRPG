using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RythmRPG.Rendering
{
    /// <summary>
    /// Screen-space pixel-art outlines and the "seen through walls" x-ray outline for materials using
    /// "RythmRPG/Pixel Sprite" or "RythmRPG/Pixel Mesh".
    /// <para>
    /// <b>Pixel outline</b> (before transparents): every material with Pixel Outline on draws its "PixelOutlineMask"
    /// pass (depth-tested, so hidden parts don't count) into a colour mask (outline colour + width) and an eye-depth
    /// target. A full-screen pass then paints each pixel that has an outlined surface within 1-3 pixels in front of
    /// it. Because it runs at the camera's own resolution (480x270 for the pixel camera) the outline is exactly one
    /// art pixel wide, for sprites and meshes alike, and it also appears where an outlined object overlaps another.
    /// </para>
    /// <para>
    /// <b>X-ray</b> (after transparents): materials with Show When Hidden (X-Ray) draw their "PixelXRay" pass without a
    /// depth test and mark which of their pixels are behind something, with their own colour, outline width and
    /// opacity, fill amount / style / sprite detail and pulse. A full-screen pass draws the fill over the hidden part
    /// and the outline around it, so the player stays visible behind walls and trees.
    /// </para>
    /// Add it with Tools > Rythm RPG > Rendering > Install Pixel Art Renderer Feature (adds it to every URP renderer
    /// your quality levels use), or by hand on the renderer asset.
    /// </summary>
    [DisallowMultipleRendererFeature("Pixel Art (Outlines + X-Ray)")]
    public sealed class PixelArtRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class OutlineSettings
        {
            public bool enabled = true;
            [Tooltip("Only renderers on these layers can be outlined.")]
            public LayerMask layers = ~0;
            [Tooltip("An outline is drawn where the outlined surface is at least this much (world units) in front of " +
                     "what is behind it. Larger = fewer lines inside an object where its own parts overlap.")]
            [Min(0.001f)] public float depthStep = 0.25f;
            [Range(0f, 1f)] public float opacity = 1f;
            [Tooltip("Off = diamond (classic pixel-art corners, no diagonal pixel at 1 px). On = square corners.")]
            public bool squareCorners;
            public RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingTransparents;
        }

        [System.Serializable]
        public sealed class XRaySettings
        {
            public bool enabled = true;
            [Tooltip("Only renderers on these layers can show through walls.")]
            public LayerMask layers = ~0;
            [Tooltip("Global strength for every x-ray (multiplies each material's fill amount and outline opacity). " +
                     "Colour, width, fill, style, sprite detail and pulse are set per material.")]
            [Range(0f, 1f)] public float strength = 1f;
            [Tooltip("A part counts as hidden when something is at least this much (world units) in front of it.")]
            [Min(0.001f)] public float depthBias = 0.1f;
            public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
        }

        public OutlineSettings outline = new();
        public XRaySettings xRay = new();
        [Tooltip("Also draw in the Scene view.")]
        public bool showInSceneView = true;
        [Tooltip("Leave empty: found automatically (Resources/Rendering/PixelArtComposite).")]
        public Shader compositeShader;

        private Material compositeMaterial;
        private OutlinePass outlinePass;
        private XRayPass xRayPass;

        public override void Create()
        {
            outlinePass = new OutlinePass(this);
            xRayPass = new XRayPass(this);
        }

        private bool EnsureMaterial()
        {
            if (compositeMaterial != null) return true;
            Shader shader = compositeShader != null ? compositeShader : Resources.Load<Shader>("Rendering/PixelArtComposite");
            if (shader == null) shader = Shader.Find("Hidden/RythmRPG/PixelArtComposite");
            if (shader == null) return false;
            compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
            return compositeMaterial != null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (type == CameraType.SceneView && !showInSceneView) return;
            if (!EnsureMaterial()) return;

            if (outline.enabled)
            {
                outlinePass.renderPassEvent = outline.injectionPoint;
                outlinePass.ConfigureInput(ScriptableRenderPassInput.Depth);
                renderer.EnqueuePass(outlinePass);
            }
            if (xRay.enabled)
            {
                xRayPass.renderPassEvent = xRay.injectionPoint;
                xRayPass.ConfigureInput(ScriptableRenderPassInput.Depth);
                renderer.EnqueuePass(xRayPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(compositeMaterial);
            compositeMaterial = null;
        }

        // ------------------------------------------------------------------ shared

        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int OutlineMaskId = Shader.PropertyToID("_PixelOutlineMask");
        private static readonly int OutlineDepthId = Shader.PropertyToID("_PixelOutlineDepth");
        private static readonly int XRayMaskId = Shader.PropertyToID("_PixelXRayMask");
        private static readonly int XRayFillId = Shader.PropertyToID("_PixelXRayFill");
        private static readonly int XRayStyleId = Shader.PropertyToID("_PixelXRayStyle");
        private static readonly int TargetSizeId = Shader.PropertyToID("_PixelTargetSize");
        private static readonly int OutlineParamsId = Shader.PropertyToID("_PixelOutlineParams");
        private static readonly int XRayParamsId = Shader.PropertyToID("_PixelXRayParams");
        private static readonly int XRayDepthBiasId = Shader.PropertyToID("_PixelXRayDepthBias");

        private sealed class DrawData
        {
            public RendererListHandle rendererList;
        }

        private sealed class CompositeData
        {
            public Material material;
            public int pass;
        }

        private static RenderTextureDescriptor MaskDescriptor(UniversalCameraData cameraData, GraphicsFormat format)
        {
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1;
            desc.graphicsFormat = format;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            return desc;
        }

        private static RendererListHandle CreateList(RenderGraph renderGraph, ContextContainer frameData, ShaderTagId tag,
                                                     LayerMask layers)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(tag, renderingData, cameraData, lightData,
                                                                           SortingCriteria.CommonOpaque);
            FilteringSettings filtering = new(RenderQueueRange.all, layers);
            RendererListParams listParams = new(renderingData.cullResults, drawing, filtering);
            return renderGraph.CreateRendererList(listParams);
        }

        private static void AddComposite(RenderGraph renderGraph, UniversalResourceData resources, Material material,
                                         int pass, string name, int[] textureIds, bool needsDepth)
        {
            using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(name, out CompositeData data);
            data.material = material;
            data.pass = pass;
            foreach (int id in textureIds) builder.UseGlobalTexture(id, AccessFlags.Read);
            if (needsDepth && resources.cameraDepthTexture.IsValid())
                builder.UseGlobalTexture(CameraDepthTextureId, AccessFlags.Read);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (CompositeData d, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, new Vector4(1f, 1f, 0f, 0f), d.material, d.pass);
            });
        }

        private static Vector4 TargetSize(RenderTextureDescriptor desc)
        {
            float w = Mathf.Max(1, desc.width), h = Mathf.Max(1, desc.height);
            return new Vector4(w, h, 1f / w, 1f / h);
        }

        // ------------------------------------------------------------------ outline

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId Tag = new("PixelOutlineMask");
            private readonly PixelArtRendererFeature feature;

            public OutlinePass(PixelArtRendererFeature feature)
            {
                this.feature = feature;
                profilingSampler = new ProfilingSampler("Pixel Outlines");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                Material material = feature.compositeMaterial;
                if (material == null) return;
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.activeColorTexture.IsValid()) return;

                RenderTextureDescriptor colorDesc = MaskDescriptor(cameraData, GraphicsFormat.R8G8B8A8_UNorm);
                RenderTextureDescriptor depthDesc = MaskDescriptor(cameraData, GraphicsFormat.R32_SFloat);
                TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, colorDesc, "_PixelOutlineMask", true);
                TextureHandle maskDepth = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc, "_PixelOutlineDepth", true);
                RendererListHandle list = CreateList(renderGraph, frameData, Tag, feature.outline.layers);

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Pixel Outline Mask", out DrawData data))
                {
                    data.rendererList = list;
                    builder.UseRendererList(list);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(maskDepth, 1, AccessFlags.Write);
                    // Depth-test against the scene so hidden parts are not outlined (needs matching sample counts).
                    if (resources.activeDepthTexture.IsValid() && cameraData.cameraTargetDescriptor.msaaSamples <= 1)
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(mask, OutlineMaskId);
                    builder.SetGlobalTextureAfterPass(maskDepth, OutlineDepthId);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(false, true, Color.clear);
                        context.cmd.DrawRendererList(d.rendererList);
                    });
                }

                OutlineSettings s = feature.outline;
                material.SetVector(TargetSizeId, TargetSize(colorDesc));
                material.SetVector(OutlineParamsId, new Vector4(s.depthStep, s.opacity, s.squareCorners ? 1f : 0f, 0f));
                AddComposite(renderGraph, resources, material, 0, "Pixel Outline Composite",
                             new[] { OutlineMaskId, OutlineDepthId }, true);
            }
        }

        // ------------------------------------------------------------------ x-ray

        private sealed class XRayPass : ScriptableRenderPass
        {
            private static readonly ShaderTagId Tag = new("PixelXRay");
            private readonly PixelArtRendererFeature feature;

            public XRayPass(PixelArtRendererFeature feature)
            {
                this.feature = feature;
                profilingSampler = new ProfilingSampler("Pixel X-Ray");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                Material material = feature.compositeMaterial;
                if (material == null) return;
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.activeColorTexture.IsValid() || !resources.cameraDepthTexture.IsValid()) return;

                RenderTextureDescriptor desc = MaskDescriptor(cameraData, GraphicsFormat.R8G8B8A8_UNorm);
                TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_PixelXRayMask", true);
                TextureHandle fill = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_PixelXRayFill", true);
                TextureHandle style = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_PixelXRayStyle", true);
                RendererListHandle list = CreateList(renderGraph, frameData, Tag, feature.xRay.layers);
                Shader.SetGlobalFloat(XRayDepthBiasId, feature.xRay.depthBias);

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Pixel X-Ray Mask", out DrawData data))
                {
                    data.rendererList = list;
                    builder.UseRendererList(list);
                    builder.UseGlobalTexture(CameraDepthTextureId, AccessFlags.Read);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(fill, 1, AccessFlags.Write);
                    builder.SetRenderAttachment(style, 2, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(mask, XRayMaskId);
                    builder.SetGlobalTextureAfterPass(fill, XRayFillId);
                    builder.SetGlobalTextureAfterPass(style, XRayStyleId);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(false, true, Color.clear);
                        context.cmd.DrawRendererList(d.rendererList);
                    });
                }

                XRaySettings s = feature.xRay;
                material.SetVector(TargetSizeId, TargetSize(desc));
                material.SetVector(XRayParamsId, new Vector4(s.strength, 0f, 0f, 0f));
                AddComposite(renderGraph, resources, material, 1, "Pixel X-Ray Composite",
                             new[] { XRayMaskId, XRayFillId, XRayStyleId }, false);
            }
        }
    }
}
