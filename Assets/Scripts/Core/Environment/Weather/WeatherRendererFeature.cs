using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RythmRPG.Core
{
    /// <summary>
    /// Draws the weather that needs the screen (Terrain Weather draws itself and does not need this feature):
    /// <list type="bullet">
    /// <item><b>Sky</b> (after transparents, from the <see cref="WeatherController"/>): the storm tint, the lightning
    /// flash and heat shimmer (global heat and <see cref="HeatHaze"/> volumes).</item>
    /// </list>
    /// Install with Tools > Rythm RPG > Rendering > Install Weather Renderer Feature (adds it to every URP renderer
    /// the quality levels use).
    /// </summary>
    [DisallowMultipleRendererFeature("Weather (Heat, Lightning)")]
    public sealed class WeatherRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("Storm tint and lightning flash.")]
        public bool tintAndFlash = true;
        [Tooltip("Heat shimmer (global heat and Heat Haze volumes).")]
        public bool heatShimmer = true;
        public RenderPassEvent skyEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public bool showInSceneView = true;
        [Tooltip("Leave empty: found automatically (Resources/Rendering/Weather/WeatherScreen).")]
        public Shader shader;

        private Material material;
        private SkyPass skyPass;
        private DepthRequestPass depthRequest;

        public override void Create()
        {
            skyPass = new SkyPass(this);
            depthRequest = new DepthRequestPass();
        }

        private bool EnsureMaterial()
        {
            if (material != null) return true;
            Shader s = shader != null ? shader : Resources.Load<Shader>("Rendering/Weather/WeatherScreen");
            if (s == null) s = Shader.Find("Hidden/RythmRPG/WeatherScreen");
            if (s == null) return false;
            material = CoreUtils.CreateEngineMaterial(s);
            return material != null;
        }


        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (type == CameraType.SceneView && !showInSceneView) return;

            // Terrain Weather mist / fog is cut by the scene depth: make sure the camera has a depth texture.
            if (TerrainWeather.Active.Count > 0)
            {
                depthRequest.ConfigureInput(ScriptableRenderPassInput.Depth);
                renderer.EnqueuePass(depthRequest);
            }

            WeatherController controller = Weather.Controller;
            if (controller == null || !controller.isActiveAndEnabled || controller.Hidden) return;
            if (!EnsureMaterial()) return;
            bool sky = tintAndFlash && (Weather.Current.tint.a > 0.002f || Weather.Flash > 0.002f);
            if (sky || (heatShimmer && NeedsHeat()))
            {
                skyPass.renderPassEvent = skyEvent;
                skyPass.ConfigureInput(ScriptableRenderPassInput.Depth);
                renderer.EnqueuePass(skyPass);
            }
        }

        // Draws nothing: only asks URP for the camera depth texture.
        private sealed class DepthRequestPass : ScriptableRenderPass
        {
            public DepthRequestPass() => renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) { }
        }

        private static bool NeedsHeat()
        {
            if (Weather.Current.heat > 0.002f) return true;
            foreach (HeatHaze haze in HeatHaze.All)
                if (haze != null && haze.isActiveAndEnabled && haze.Strength > 0.001f) return true;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        // ------------------------------------------------------------------ shared

        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int GroundId = Shader.PropertyToID("_WxGround");
        private static readonly int TintId = Shader.PropertyToID("_WxTint");
        private static readonly int FlashId = Shader.PropertyToID("_WxFlash");
        private static readonly int HeatParamsId = Shader.PropertyToID("_WxHeatParams");
        private static readonly int HeatBandId = Shader.PropertyToID("_WxHeatBand");
        private static readonly int HazeRow0Id = Shader.PropertyToID("_WxHazeRow0");
        private static readonly int HazeRow1Id = Shader.PropertyToID("_WxHazeRow1");
        private static readonly int HazeRow2Id = Shader.PropertyToID("_WxHazeRow2");
        private static readonly int HazeParamsId = Shader.PropertyToID("_WxHazeParams");
        private static readonly int HazeCountId = Shader.PropertyToID("_WxHazeCount");

        private const int MaxHazes = 8;
        private const int PassTint = 0;
        private const int PassFlash = 1;
        private const int PassHeat = 2;
        private static readonly Vector4[] row0 = new Vector4[MaxHazes];
        private static readonly Vector4[] row1 = new Vector4[MaxHazes];
        private static readonly Vector4[] row2 = new Vector4[MaxHazes];
        private static readonly Vector4[] hazeParams = new Vector4[MaxHazes];
        private static readonly List<HeatHaze> hazeBuffer = new();

        private static RenderTextureDescriptor Descriptor(UniversalCameraData cameraData, GraphicsFormat format)
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

        // ------------------------------------------------------------------ sky: tint, flash, heat

        private sealed class DrawData
        {
            public Material material;
            public bool tint;
            public bool flash;
        }

        private sealed class CopyData
        {
            public TextureHandle source;
        }

        private sealed class HeatData
        {
            public Material material;
            public TextureHandle source;
        }

        private static void UploadHeat(Material m)
        {
            HeatSettings h = Weather.Controller.Heat;
            m.SetVector(HeatParamsId, new Vector4(Weather.Current.heat, h.maxOffsetPixels, 1f / Mathf.Max(0.05f, h.scale), h.speed));
            m.SetVector(HeatBandId, new Vector4(h.groundBand, 0f, 0f, 0f));
            m.SetVector(GroundId, new Vector4(Weather.GroundHeight, 0f, 0f, 0f));

            hazeBuffer.Clear();
            foreach (HeatHaze haze in HeatHaze.All)
                if (haze != null && haze.isActiveAndEnabled && haze.Strength > 0.001f) hazeBuffer.Add(haze);
            Vector3 focus = Weather.ViewFocus;
            if (hazeBuffer.Count > MaxHazes)
                hazeBuffer.Sort((x, y) => (x.transform.position - focus).sqrMagnitude.CompareTo((y.transform.position - focus).sqrMagnitude));
            int count = Mathf.Min(MaxHazes, hazeBuffer.Count);
            for (int i = 0; i < MaxHazes; i++)
            {
                if (i >= count)
                {
                    row0[i] = row1[i] = row2[i] = hazeParams[i] = Vector4.zero;
                    continue;
                }
                HeatHaze z = hazeBuffer[i];
                WeatherShapes.Pack(z.transform, out row0[i], out row1[i], out row2[i]);
                hazeParams[i] = new Vector4(z.Shape == WeatherZoneShape.Sphere ? 1f : 0f, z.Strength, z.EdgeSoftness, z.FadeUpward ? 1f : 0f);
            }
            m.SetVectorArray(HazeRow0Id, row0);
            m.SetVectorArray(HazeRow1Id, row1);
            m.SetVectorArray(HazeRow2Id, row2);
            m.SetVectorArray(HazeParamsId, hazeParams);
            m.SetInt(HazeCountId, count);
        }

        private sealed class SkyPass : ScriptableRenderPass
        {
            private readonly WeatherRendererFeature feature;

            public SkyPass(WeatherRendererFeature feature)
            {
                this.feature = feature;
                profilingSampler = new ProfilingSampler("Weather Sky");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                Material m = feature.material;
                if (m == null || Weather.Controller == null) return;
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.activeColorTexture.IsValid()) return;

                bool tint = feature.tintAndFlash && Weather.Current.tint.a > 0.002f;
                bool flash = feature.tintAndFlash && Weather.Flash > 0.002f;
                if (tint || flash)
                {
                    m.SetColor(TintId, Weather.Current.tint);
                    m.SetColor(FlashId, Weather.Controller.LightningFx.flashColor * Weather.Flash);
                    using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Weather Tint, Flash", out DrawData data);
                    data.material = m;
                    data.tint = tint;
                    data.flash = flash;
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) =>
                    {
                        Vector4 scaleBias = new(1f, 1f, 0f, 0f);
                        if (d.tint) Blitter.BlitTexture(context.cmd, scaleBias, d.material, PassTint);
                        if (d.flash) Blitter.BlitTexture(context.cmd, scaleBias, d.material, PassFlash);
                    });
                }

                if (!feature.heatShimmer || !NeedsHeat()) return;
                UploadHeat(m);

                TextureHandle copy = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
                    Descriptor(cameraData, cameraData.cameraTargetDescriptor.graphicsFormat), "_WeatherHeatSource", false, FilterMode.Point);

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Weather Heat Copy", out CopyData data))
                {
                    data.source = resources.activeColorTexture;
                    builder.UseTexture(resources.activeColorTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(copy, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (CopyData d, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), 0f, false);
                    });
                }

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Weather Heat Shimmer", out HeatData data))
                {
                    data.material = m;
                    data.source = copy;
                    if (resources.cameraDepthTexture.IsValid()) builder.UseGlobalTexture(CameraDepthTextureId, AccessFlags.Read);
                    builder.UseTexture(copy, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (HeatData d, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, PassHeat);
                    });
                }
            }
        }
    }
}
