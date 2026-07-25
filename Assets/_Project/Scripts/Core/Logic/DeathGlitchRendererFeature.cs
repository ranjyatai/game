using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// URP 17 Render Graph 全屏故障艺术效果。
/// intensity=0 时 shader 直接返回原图，Pass 无条件入队。
/// </summary>
public class DeathGlitchRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Range(0.5f, 10f)]   public float speed     = 3f;
        [Range(0.01f, 0.5f)] public float blockSize = 0.08f;
        [Range(0f,   0.05f)] public float rgbShift  = 0.012f;
        [Range(0f,   1f)]    public float scanline   = 0.18f;
    }

    public Settings settings = new Settings();

    // 直接引用 Shader 资产——Build 时 Unity 会自动将其打入包，Shader.Find 在 Build 中会失败
    [SerializeField] private Shader glitchShader;

    private static readonly int _SpeedId        = Shader.PropertyToID("_DeathGlitchSpeed");
    private static readonly int _BlockSizeId    = Shader.PropertyToID("_DeathGlitchBlockSize");
    private static readonly int _RGBShiftId     = Shader.PropertyToID("_DeathGlitchRGBShift");
    private static readonly int _ScanlineId     = Shader.PropertyToID("_DeathGlitchScanline");
    private static readonly int _BlitTextureId  = Shader.PropertyToID("_BlitTexture");
    private static readonly int _BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

    private Material        _mat;
    private DeathGlitchPass _pass;

    public override void Create()
    {
        _pass = new DeathGlitchPass();
        _pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        if (_mat == null)
        {
            // 优先用序列化引用（Build 安全），回退到 Shader.Find（仅编辑器可用）
            Shader sh = glitchShader != null ? glitchShader
                      : Shader.Find("SkyPrison/PostProcess/DeathGlitch");
            if (sh == null)
            {
                Debug.LogWarning("[DeathGlitchRendererFeature] 找不到 DeathGlitch Shader，" +
                                 "请在 RendererFeature 的 Glitch Shader 字段拖入对应 Shader 资产。");
                return;
            }
            _mat = CoreUtils.CreateEngineMaterial(sh);
        }

        Shader.SetGlobalFloat(_SpeedId,     settings.speed);
        Shader.SetGlobalFloat(_BlockSizeId, settings.blockSize);
        Shader.SetGlobalFloat(_RGBShiftId,  settings.rgbShift);
        Shader.SetGlobalFloat(_ScanlineId,  settings.scanline);

        _pass.Setup(_mat, _BlitTextureId, _BlitScaleBiasId);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (_mat != null) { CoreUtils.Destroy(_mat); _mat = null; }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private class DeathGlitchPass : ScriptableRenderPass
    {
        private Material _mat;
        private int      _blitTexId;
        private int      _blitScaleBiasId;

        // 兼容模式（Compatibility Mode ON）用的临时 RT
        private RenderTexture _compatTempRT;

        private static readonly MaterialPropertyBlock s_Props = new MaterialPropertyBlock();

        public void Setup(Material mat, int blitTexId, int blitScaleBiasId)
        {
            _mat             = mat;
            _blitTexId       = blitTexId;
            _blitScaleBiasId = blitScaleBiasId;
        }

        // ── Compatibility Mode 路径（URP Compatibility Mode ON 或老版本 URP）────
        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_mat == null) return;
            float intensity = Shader.GetGlobalFloat(Shader.PropertyToID("_DeathGlitchIntensity"));
            if (intensity <= 0.001f) return;

            var cmd = CommandBufferPool.Get("DeathGlitch_Compat");
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1; desc.depthBufferBits = 0;

            if (_compatTempRT == null || _compatTempRT.width != desc.width || _compatTempRT.height != desc.height)
            {
                if (_compatTempRT != null) _compatTempRT.Release();
                _compatTempRT = new RenderTexture(desc);
            }

            cmd.Blit(renderingData.cameraData.renderer.cameraColorTargetHandle, _compatTempRT, _mat);
            cmd.Blit(_compatTempRT, renderingData.cameraData.renderer.cameraColorTargetHandle);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // ── Render Graph 路径（URP 17 Compatibility Mode OFF）────────────────

        private class GlitchPassData
        {
            public TextureHandle source;
            public Material      material;
            public int           blitTexId;
            public int           blitScaleBiasId;
        }

        private class CopyBackPassData
        {
            public TextureHandle source;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_mat == null) return;

            var cameraData   = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            // isActiveTargetBackBuffer 时无法中途读屏，降级：跳过但不报错
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle src = resourceData.activeColorTexture;

            var desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples     = 1;
            desc.depthBufferBits = 0;

            TextureHandle tmp = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_DeathGlitchTemp", false, FilterMode.Bilinear);

            using (var builder = renderGraph.AddRasterRenderPass<GlitchPassData>(
                       "DeathGlitch", out var data))
            {
                data.source          = src;
                data.material        = _mat;
                data.blitTexId       = _blitTexId;
                data.blitScaleBiasId = _blitScaleBiasId;

                builder.UseTexture(src, AccessFlags.Read);
                builder.SetRenderAttachment(tmp, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (GlitchPassData d, RasterGraphContext ctx) =>
                {
                    s_Props.Clear();
                    s_Props.SetTexture(d.blitTexId,        d.source);
                    s_Props.SetVector(d.blitScaleBiasId,  new Vector4(1, 1, 0, 0));
                    ctx.cmd.DrawProcedural(
                        Matrix4x4.identity, d.material, 0,
                        MeshTopology.Triangles, 3, 1, s_Props);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<CopyBackPassData>(
                       "DeathGlitch_CopyBack", out var data))
            {
                data.source = tmp;
                builder.UseTexture(tmp, AccessFlags.Read);
                builder.SetRenderAttachment(src, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (CopyBackPassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), 0f, false);
                });
            }
        }
    }
}
