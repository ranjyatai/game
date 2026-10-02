using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 状态描边（灼烧等）的专属外发光。
///
/// 原来描边只是 Spine 着色器里一圈几像素的 HDR 细线，「向外发光」全靠 Volume 的全局 Bloom：
/// 调大 Bloom 会让场景里所有超过阈值的东西一起发光，而且细线能贡献给 Bloom 的能量很少，
/// 被色调映射压白，光晕出不来。
///
/// 这里只处理正在显示描边的单位自己的轮廓：拿 UnitStatusOutlinePresenceFeature 已经渲染好的
/// 单位专属蒙版，降到 1/4 分辨率、横纵各模糊两轮，扣掉身体内部，乘状态颜色加法叠到相机颜色。
/// 和全局 Bloom 互不影响；没有单位在燃烧时这个 Feature 什么都不做。
///
/// 必须排在 UnitStatusOutlinePresenceFeature 之后（它在 BeforeRenderingTransparents 填蒙版，
/// 这里在 AfterRenderingTransparents 读）。只对 MainCamera 生效，和蒙版是同一台相机画的。
/// </summary>
public sealed class UnitStatusOutlineGlowFeature : ScriptableRendererFeature
{
    [Tooltip("拖入 SkyPrisonStatusOutlineGlow.shader。Hidden/ shader 不会自动打包，必须显式引用。")]
    [SerializeField] private Shader glowShader;

    [Tooltip("扫描场景里 UnitStatusOutlineEffect 的间隔帧数。")]
    [SerializeField] private int scanIntervalFrames = 15;

    private const string k_ShaderName = "Hidden/SkyPrison/StatusOutlineGlow";
    private UnitStatusOutlineGlowPass _pass;

    public override void Create()
    {
        _pass = new UnitStatusOutlineGlowPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera cam = renderingData.cameraData.camera;
        if (cam == null || !cam.CompareTag("MainCamera"))
            return;

        Shader shader = glowShader != null ? glowShader : Shader.Find(k_ShaderName);
        if (shader == null)
            return;

        if (_pass.Prepare(shader, Mathf.Max(1, scanIntervalFrames)))
            renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }
}

sealed class UnitStatusOutlineGlowPass : ScriptableRenderPass
{
    private const int PassDownsample = 0, PassBlurH = 1, PassBlurV = 2, PassComposite = 3;
    private const float ReferenceHeight = 1080f;

    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int GlowColorInnerId = Shader.PropertyToID("_GlowColorInner");
    private static readonly int GlowStepId = Shader.PropertyToID("_GlowStep");
    private static readonly int GlowTexelId = Shader.PropertyToID("_GlowTexel");
    private static readonly int GlowPresenceId = Shader.PropertyToID("_GlowPresence");
    private static readonly int GlowNoiseTexId = Shader.PropertyToID("_GlowNoiseTex");
    private static readonly int GlowCenterId = Shader.PropertyToID("_GlowCenter");
    private static readonly int GlowNoiseId = Shader.PropertyToID("_GlowNoise");

    private Shader _shader;
    private int _scanInterval;
    private int _lastScanFrame = -1000;
    private readonly List<UnitStatusOutlineEffect> _cached = new List<UnitStatusOutlineEffect>();
    private readonly List<UnitStatusOutlineEffect> _active = new List<UnitStatusOutlineEffect>();

    // 每个单位一份材质：同一帧里多个单位的颜色/半径不同，材质参数是录制时写入、执行时才读，
    // 共用一份会被最后一个单位覆盖。
    private readonly Dictionary<UnitStatusOutlineEffect, Material> _materials = new Dictionary<UnitStatusOutlineEffect, Material>();
    private readonly Dictionary<RenderTexture, RTHandle> _imported = new Dictionary<RenderTexture, RTHandle>();
    private readonly List<UnitStatusOutlineEffect> _staleBuffer = new List<UnitStatusOutlineEffect>();

    public bool Prepare(Shader shader, int scanInterval)
    {
        _shader = shader;
        _scanInterval = scanInterval;

        if (Time.frameCount - _lastScanFrame >= _scanInterval)
        {
            _lastScanFrame = Time.frameCount;
            _cached.Clear();
            _cached.AddRange(Object.FindObjectsByType<UnitStatusOutlineEffect>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        _active.Clear();
        for (int i = 0; i < _cached.Count; i++)
        {
            UnitStatusOutlineEffect e = _cached[i];
            if (e == null || !e.isActiveAndEnabled) continue;
            if (e.CurrentIntensity <= 0.001f || e.GlowRadiusPixels <= 0.01f) continue;
            if (e.PresenceTexture == null) continue;
            _active.Add(e);
        }

        ReleaseStaleMaterials();
        return _active.Count > 0;
    }

    private class CompositeData
    {
        public TextureHandle glow;
        public Material material;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resources = frameData.Get<UniversalResourceData>();
        TextureHandle color = resources.activeColorTexture;
        if (!color.IsValid())
            return;

        Camera cam = frameData.Get<UniversalCameraData>().camera;
        Texture2D noise = UnitStatusOutlineEffect.DefaultNoiseTexture;

        for (int i = 0; i < _active.Count; i++)
        {
            UnitStatusOutlineEffect effect = _active[i];
            RenderTexture presence = effect.PresenceTexture;
            if (presence == null)
                continue;

            int w = Mathf.Max(1, presence.width / 4);
            int h = Mathf.Max(1, presence.height / 4);

            Material mat = GetMaterial(effect);
            // 半径按 1080p 定义，换算到当前分辨率，再换成 1/4 分辨率下的步长：
            // 9 tap 单侧 4 步，横纵两轮叠加约 √2 倍，所以半径 ≈ 4 × 步长 × 4 × √2。
            float radiusPx = effect.GlowRadiusPixels * (presence.height / ReferenceHeight);
            float step = radiusPx / (4f * 4f * 1.41421356f);
            mat.SetFloat(GlowStepId, Mathf.Max(0.25f, step));
            mat.SetVector(GlowTexelId, new Vector4(1f / w, 1f / h, w, h));
            mat.SetColor(GlowColorId, effect.GlowColor);
            mat.SetColor(GlowColorInnerId, effect.GlowInnerColor);
            mat.SetTexture(GlowPresenceId, presence);

            // 噪波以单位中心为原点，起伏斑块跟着单位走，不会在角色身上滑动。
            Vector3 vp = cam != null ? cam.WorldToViewportPoint(effect.transform.position) : new Vector3(0.5f, 0.5f, 1f);
            mat.SetVector(GlowCenterId, new Vector4(vp.x, vp.y, (float)presence.width / presence.height, 0f));
            mat.SetVector(GlowNoiseId, new Vector4(effect.GlowNoiseScale, effect.FlowSpeed, noise != null ? effect.GlowVariance : 0f, 0f));
            if (noise != null)
                mat.SetTexture(GlowNoiseTexId, noise);

            TextureHandle source = renderGraph.ImportTexture(GetImported(presence));
            var desc = new TextureDesc(w, h)
            {
                colorFormat = GraphicsFormat.R16_SFloat,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "StatusOutlineGlow",
            };
            TextureHandle a = renderGraph.CreateTexture(desc);
            TextureHandle b = renderGraph.CreateTexture(desc);

            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, a, mat, PassDownsample), "StatusOutlineGlow Downsample");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, mat, PassBlurH), "StatusOutlineGlow BlurH");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(b, a, mat, PassBlurV), "StatusOutlineGlow BlurV");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, mat, PassBlurH), "StatusOutlineGlow BlurH2");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(b, a, mat, PassBlurV), "StatusOutlineGlow BlurV2");

            // 合成不用 AddBlitPass：它把目标当只写，部分平台会丢掉相机颜色原有内容。
            // 这里声明 ReadWrite，加法混合叠在已有画面上。
            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("StatusOutlineGlow Composite", out var data))
            {
                data.glow = a;
                data.material = mat;
                builder.UseTexture(a, AccessFlags.Read);
                builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.glow, new Vector4(1f, 1f, 0f, 0f), d.material, PassComposite);
                });
            }
        }
    }

#pragma warning disable CS0618
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0618

    private Material GetMaterial(UnitStatusOutlineEffect effect)
    {
        if (_materials.TryGetValue(effect, out Material m) && m != null && m.shader == _shader)
            return m;

        if (m != null)
            CoreUtils.Destroy(m);
        m = CoreUtils.CreateEngineMaterial(_shader);
        _materials[effect] = m;
        return m;
    }

    private RTHandle GetImported(RenderTexture rt)
    {
        if (_imported.TryGetValue(rt, out RTHandle handle) && handle != null)
            return handle;

        handle = RTHandles.Alloc(rt);
        _imported[rt] = handle;
        return handle;
    }

    private void ReleaseStaleMaterials()
    {
        _staleBuffer.Clear();
        foreach (var kv in _materials)
            if (kv.Key == null || !_active.Contains(kv.Key))
                _staleBuffer.Add(kv.Key);

        for (int i = 0; i < _staleBuffer.Count; i++)
        {
            if (_materials.TryGetValue(_staleBuffer[i], out Material m) && m != null)
                CoreUtils.Destroy(m);
            _materials.Remove(_staleBuffer[i]);
        }

        // 蒙版 RT 被 UnitStatusOutlinePresenceFeature 释放后，丢掉对应的 RTHandle 包装。
        // 只丢引用、不调 RTHandle.Release()：包装外部 RenderTexture 的句柄 Release 会连底层
        // RT 一起销毁，而那张蒙版归 PresenceFeature 管，不是这里的。
        _deadRtBuffer.Clear();
        foreach (var kv in _imported)
            if (kv.Key == null || !kv.Key.IsCreated())
                _deadRtBuffer.Add(kv.Key);
        for (int i = 0; i < _deadRtBuffer.Count; i++)
            _imported.Remove(_deadRtBuffer[i]);
    }

    private readonly List<RenderTexture> _deadRtBuffer = new List<RenderTexture>();

    public void Dispose()
    {
        foreach (var m in _materials.Values)
            if (m != null) CoreUtils.Destroy(m);
        _materials.Clear();
        _imported.Clear();
    }
}
