using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

/// <summary>
/// 夜视仪调色通道。插在 Main Camera（画世界和角色）画完之后、Overlay 相机（GamePlayCamera / OverheadUICamera）
/// 开始画之前，所以头顶 UI（名字、血条、任务图标）和战术识别框不会被染色。
///
/// 为什么不用 Volume 做调色：URP 的后处理只能开在相机栈尾（OverheadUICamera），否则镜头畸变这类
/// 效果只作用在角色上、UI 不跟着变，两者就对不齐——见 SkyPrisonCameraStackPostProcessAuthority。
/// 但栈尾后处理是对整幅画面（含头顶 UI）一起跑的，夜视仪的单色化会把 UI 的红 / 白 / 青全抹掉。
/// 所以把夜视仪调色单独拿出来，放到 UI 之前；镜头畸变等仍留在最后的统一后处理里，照样作用在 UI 上。
///
/// 强度 / 参数全部来自 <see cref="SkyPrisonNightVisionMode"/>；夜视仪关闭时（Weight 为 0）不排队，零开销。
/// </summary>
public class SkyPrisonNightVisionRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader shader;
        public string targetCameraName = "Main Camera";
        public bool applyToSceneView = false;
    }

    public Settings settings = new Settings();

    private Material material;
    private NightVisionPass pass;

    public override void Create()
    {
        EnsureMaterial();
        pass = new NightVisionPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    private void EnsureMaterial()
    {
        if (settings.shader == null)
            settings.shader = Shader.Find("Hidden/SkyPrison/NightVisionGrade");
        if (settings.shader == null)
            return;
        if (material == null || material.shader != settings.shader)
        {
            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(settings.shader);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (SkyPrisonNightVisionMode.Weight <= 0.001f)
            return;

        EnsureMaterial();
        if (material == null)
            return;
        if (pass == null)
            pass = new NightVisionPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };

        Camera camera = renderingData.cameraData.camera;
        CameraType type = renderingData.cameraData.cameraType;
        if (type != CameraType.Game && !(settings.applyToSceneView && type == CameraType.SceneView))
            return;
        if (renderingData.cameraData.renderType != CameraRenderType.Base)
            return;
        if (camera == null || !string.Equals(camera.name, settings.targetCameraName, System.StringComparison.Ordinal))
            return;

        pass.Setup(material);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }

    private sealed class NightVisionPass : ScriptableRenderPass
    {
        private static readonly int WeightId = Shader.PropertyToID("_NVWeight");
        private static readonly int ExposureId = Shader.PropertyToID("_NVExposure");
        private static readonly int ContrastId = Shader.PropertyToID("_NVContrast");
        private static readonly int PivotId = Shader.PropertyToID("_NVPivot");
        private static readonly int GammaLiftId = Shader.PropertyToID("_NVGammaLift");
        private static readonly int MonoId = Shader.PropertyToID("_NVMono");
        private static readonly int TintId = Shader.PropertyToID("_NVTint");
        private static readonly int VignetteId = Shader.PropertyToID("_NVVignette");
        private static readonly int GrainId = Shader.PropertyToID("_NVGrain");
        private static readonly int TimeId = Shader.PropertyToID("_NVTime");

        private readonly ProfilingSampler sampler = new ProfilingSampler("SkyPrison Night Vision Grade");
        private Material material;

        public void Setup(Material material)
        {
            this.material = material;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        private void PushParameters()
        {
            material.SetFloat(WeightId, SkyPrisonNightVisionMode.Weight);
            material.SetFloat(ExposureId, SkyPrisonNightVisionMode.Exposure);
            material.SetFloat(ContrastId, SkyPrisonNightVisionMode.ContrastPower);
            material.SetFloat(PivotId, SkyPrisonNightVisionMode.ContrastPivot);
            material.SetFloat(GammaLiftId, SkyPrisonNightVisionMode.GammaLift);
            material.SetFloat(MonoId, SkyPrisonNightVisionMode.MonoStrength);
            material.SetVector(TintId, SkyPrisonNightVisionMode.MonoTint);
            material.SetFloat(VignetteId, SkyPrisonNightVisionMode.VignetteIntensity);
            material.SetFloat(GrainId, SkyPrisonNightVisionMode.GrainIntensity);
            material.SetFloat(TimeId, Mathf.Repeat(Time.unscaledTime, 100f) * 37.7f);
        }

#if UNITY_6000_0_OR_NEWER
        private sealed class PassData
        {
            public TextureHandle source;
            public Material material;
            public int passIndex;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData == null || resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
                return;

            PushParameters();

            TextureDesc desc = source.GetDescriptor(renderGraph);
            desc.name = "_SkyPrisonNightVisionTemp";
            desc.clearBuffer = false;
            desc.depthBufferBits = DepthBits.None;
            TextureHandle temp = renderGraph.CreateTexture(desc);

            AddBlit(renderGraph, source, temp, 0, "SkyPrison Night Vision Grade");
            AddBlit(renderGraph, temp, source, 1, "SkyPrison Night Vision Copy Back");
        }

        private void AddBlit(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, int passIndex, string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, sampler))
            {
                passData.source = source;
                passData.material = material;
                passData.passIndex = passIndex;

                builder.UseTexture(passData.source);
                builder.SetRenderAttachment(destination, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, data.passIndex);
                });
            }
        }
#endif
    }
}
