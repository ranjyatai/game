using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 光学迷彩(怪人)的实时歪曲：在主相机的透明物体画完之后(AfterRenderingTransparents)，
///   1) 把当前相机颜色实时截一份(_SkyPrisonCamoGrab，含血迹、角色等透明物体)；
///   2) 在每只迷彩单位周围画一块面向相机的面片(Hidden/SP/CamoWarp)，按屏幕空间模糊遮罩(_SkyPrisonCamoMask，
///      SkyPrisonCamoMaskRenderer 在相机开始渲染时画好)推挤这张截图——液化 / 歪みツール的感觉，没有轮廓线、没有接缝。
/// 以前歪曲读的是 _CameraOpaqueTexture(透明物体之前截的)，里面没有血迹和角色，怪人经过时会把它们"擦掉"(用户 10-12 实测)。
/// 没有处于迷彩移动中的单位时这个 Feature 什么都不做。只对 MainCamera 生效(角色由主相机渲染)。
/// </summary>
public sealed class SkyPrisonCamoWarpFeature : ScriptableRendererFeature
{
    [Tooltip("拖入 SkyPrisonCamoWarp.shader。Hidden/ shader 不会自动打包，必须显式引用。")]
    [SerializeField] private Shader warpShader;

    private const string k_ShaderName = "Hidden/SP/CamoWarp";
    private CamoWarpPass _pass;

    public override void Create()
    {
        _pass = new CamoWarpPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera cam = renderingData.cameraData.camera;
        if (cam == null || !cam.CompareTag("MainCamera")) return;
        Shader shader = warpShader != null ? warpShader : Shader.Find(k_ShaderName);
        if (shader == null) return;
        if (_pass.Prepare(shader)) renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) { _pass?.Dispose(); }
}

sealed class CamoWarpPass : ScriptableRenderPass
{
    private static readonly int GrabId = Shader.PropertyToID("_SkyPrisonCamoGrab");
    private static readonly int StrengthId = Shader.PropertyToID("_WarpStrength");

    private Material _material;
    private Mesh _quad;
    private readonly List<SkyPrisonCamoMaskRenderer.Entry> _active = new List<SkyPrisonCamoMaskRenderer.Entry>();

    public bool Prepare(Shader shader)
    {
        if (_material == null || _material.shader != shader)
        {
            if (_material != null) CoreUtils.Destroy(_material);
            _material = CoreUtils.CreateEngineMaterial(shader);
        }
        if (_quad == null) _quad = BuildQuad();
        SkyPrisonCamoMaskRenderer.CollectActive(_active);
        return _active.Count > 0;
    }

    private class DrawData
    {
        public TextureHandle grab;
        public Material material;
        public Mesh quad;
        public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        public readonly List<float> strengths = new List<float>();
        public MaterialPropertyBlock mpb;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resources = frameData.Get<UniversalResourceData>();
        TextureHandle color = resources.activeColorTexture;
        if (!color.IsValid()) return;

        TextureDesc desc = renderGraph.GetTextureDesc(color);
        desc.name = "SkyPrisonCamoGrab";
        desc.clearBuffer = false;
        desc.msaaSamples = MSAASamples.None;
        TextureHandle grab = renderGraph.CreateTexture(desc);
        renderGraph.AddBlitPass(color, grab, Vector2.one, Vector2.zero, passName: "CamoWarp Grab");

        using (var builder = renderGraph.AddRasterRenderPass<DrawData>("CamoWarp Draw", out var data))
        {
            data.grab = grab;
            data.material = _material;
            data.quad = _quad;
            data.matrices.Clear(); data.strengths.Clear();
            for (int i = 0; i < _active.Count; i++) { data.matrices.Add(_active[i].warpMatrix); data.strengths.Add(_active[i].warpStrength); }
            if (data.mpb == null) data.mpb = new MaterialPropertyBlock();

            builder.UseTexture(grab, AccessFlags.Read);
            builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
            {
                Texture grabTex = (RTHandle)d.grab;
                for (int i = 0; i < d.matrices.Count; i++)
                {
                    d.mpb.Clear();
                    d.mpb.SetTexture(GrabId, grabTex);
                    d.mpb.SetFloat(StrengthId, d.strengths[i]);
                    ctx.cmd.DrawMesh(d.quad, d.matrices[i], d.material, 0, 0, d.mpb);
                }
            });
        }
    }

#pragma warning disable CS0618
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0618

    private static Mesh BuildQuad()
    {
        var m = new Mesh { name = "CamoWarpQuad", hideFlags = HideFlags.HideAndDontSave };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }

    public void Dispose()
    {
        if (_material != null) CoreUtils.Destroy(_material);
        if (_quad != null) CoreUtils.Destroy(_quad);
    }
}
