using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Spine 部件重叠去重：被遮挡角色的全息 + 闪避残影。
///
/// Spine 角色是一片片部件分别画的。被挡住时每片部件各自往画面上叠一层全息，
/// 手压身体、头发压头这些重叠处就会亮一倍、网格密一倍，整个人看起来是拼起来的。
/// 闪避残影改成加算以后也是同一个问题。
///
/// 做法：在画透明物体之前，把每个像素上「参与叠加的部件 alpha」累加进一张计数图，
/// 正式绘制时每片按 alpha / 总和 缩放，重叠几层加起来都正好是一层。
///   全息：SpineOcclusionComposite 的 HologramCoverage Pass → _SP_HoloCoverage（R16F）
///   残影：SkyPrisonAfterimageFill 的 GhostCoverage Pass → _SP_GhostCoverage（RGBA16F，
///         每个残影占一个通道，残影之间照常叠加，只去掉同一个残影内部的重叠）
///
/// 开销：不额外渲染场景，只把用到这两种材质的网格再画一遍（一个角色/残影一两个
/// DrawRenderer），加一次全屏清空。没有对应物体时 Pass 不入队。
/// 正式绘制仍然在各自原来的顺序里，前后排序不受影响。
/// </summary>
public sealed class HologramCoverageFeature : ScriptableRendererFeature
{
    private static readonly int k_HoloActiveId = Shader.PropertyToID("_SP_HoloCoverageActive");
    private static readonly int k_GhostActiveId = Shader.PropertyToID("_SP_GhostCoverageActive");

    [Tooltip("扫描 Character2D 层渲染器的间隔帧数。材质切换每帧都会检查，这里只管新生成/销毁的角色。")]
    [SerializeField] private int scanIntervalFrames = 30;

    private HologramCoveragePass _holoPass;
    private GhostCoveragePass _ghostPass;
    private RenderTexture _holoRt;
    private RenderTexture _ghostRt;

    public override void Create()
    {
        _holoPass = new HologramCoveragePass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        _ghostPass = new GhostCoveragePass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera cam = renderingData.cameraData.camera;

        // 非游戏相机（场景视图、材质预览）不画计数图：关掉开关，着色器退回旧行为，
        // 不会读到游戏相机那一帧的计数图导致错位。
        if (cam == null || cam.cameraType != CameraType.Game)
        {
            Shader.SetGlobalFloat(k_HoloActiveId, 0f);
            Shader.SetGlobalFloat(k_GhostActiveId, 0f);
            return;
        }

        if (!cam.CompareTag("MainCamera"))
            return;

        // 先关掉，Pass 真正执行完才在命令缓冲里打开——这一帧没入队就保持旧行为。
        Shader.SetGlobalFloat(k_HoloActiveId, 0f);
        Shader.SetGlobalFloat(k_GhostActiveId, 0f);

        var desc = renderingData.cameraData.cameraTargetDescriptor;
        int w = desc.width, h = desc.height;
        if (w <= 0 || h <= 0)
            return;

        if (_holoPass.CollectDraws(Mathf.Max(1, scanIntervalFrames)))
        {
            EnsureRT(ref _holoRt, w, h, RenderTextureFormat.RHalf, "RT_HologramCoverage");
            _holoPass.SetTarget(_holoRt);
            renderer.EnqueuePass(_holoPass);
        }

        if (_ghostPass.CollectDraws())
        {
            EnsureRT(ref _ghostRt, w, h, RenderTextureFormat.ARGBHalf, "RT_GhostCoverage");
            _ghostPass.SetTarget(_ghostRt);
            renderer.EnqueuePass(_ghostPass);
        }
    }

    private static void EnsureRT(ref RenderTexture rt, int w, int h, RenderTextureFormat format, string name)
    {
        if (rt != null && rt.width == w && rt.height == h)
            return;
        if (rt != null) rt.Release();
        rt = new RenderTexture(w, h, 0, format)
        {
            name = name,
            // 必须点采样：每个像素读回的就是自己这个像素的累加值。
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        rt.Create();
    }

    protected override void Dispose(bool disposing)
    {
        Shader.SetGlobalFloat(k_HoloActiveId, 0f);
        Shader.SetGlobalFloat(k_GhostActiveId, 0f);
        if (_holoRt != null) { _holoRt.Release(); _holoRt = null; }
        if (_ghostRt != null) { _ghostRt.Release(); _ghostRt = null; }
    }

    private struct DrawEntry
    {
        public Renderer renderer;
        public Material material;
        public int submesh;
        public int pass;
    }

    /// <summary>两种计数图共用的绘制：清空 → DrawRenderer 指定 Pass → 发布全局纹理并打开开关。</summary>
    private abstract class CoveragePassBase : ScriptableRenderPass
    {
        protected readonly List<DrawEntry> _draws = new List<DrawEntry>();
        private RenderTexture _rt;
        private readonly string _name;
        private readonly int _texId;
        private readonly int _activeId;

        protected CoveragePassBase(string name, string texName, int activeId)
        {
            _name = name;
            _texId = Shader.PropertyToID(texName);
            _activeId = activeId;
        }

        public void SetTarget(RenderTexture rt) => _rt = rt;

        private class PassData
        {
            public List<DrawEntry> draws;
            public RenderTexture rt;
            public int texId;
            public int activeId;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_rt == null || _draws.Count == 0)
                return;

            using (var builder = renderGraph.AddUnsafePass<PassData>(_name, out var data))
            {
                data.draws = _draws;
                data.rt = _rt;
                data.texId = _texId;
                data.activeId = _activeId;
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData d, UnsafeGraphContext ctx) =>
                {
                    ctx.cmd.SetRenderTarget(d.rt);
                    ctx.cmd.SetViewport(new Rect(0, 0, d.rt.width, d.rt.height));
                    ctx.cmd.ClearRenderTarget(false, true, Color.clear);

                    for (int i = 0; i < d.draws.Count; i++)
                    {
                        DrawEntry e = d.draws[i];
                        if (e.renderer == null || e.material == null)
                            continue;
                        // 用物体自己的材质（带上它的 MaterialPropertyBlock），判定参数和正式绘制完全一致。
                        ctx.cmd.DrawRenderer(e.renderer, e.material, e.submesh, e.pass);
                    }

                    ctx.cmd.SetGlobalTexture(d.texId, d.rt);
                    ctx.cmd.SetGlobalFloat(d.activeId, 1f);
                });
            }
        }

#pragma warning disable CS0618
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0618
    }

    /// <summary>被遮挡角色的全息计数：当前材质带 HologramCoverage Pass 且开着全息填充的部件。</summary>
    private sealed class HologramCoveragePass : CoveragePassBase
    {
        private const int k_CharLayer = 8; // Character2D
        private static readonly int k_UseHologramFillId = Shader.PropertyToID("_SkyPrison_UseHologramFill");

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly Dictionary<Shader, int> _passIndexByShader = new Dictionary<Shader, int>();
        private readonly List<Material> _matBuffer = new List<Material>();
        private int _lastScanFrame = -1000;

        public HologramCoveragePass() : base("HologramCoverage", "_SP_HoloCoverage", k_HoloActiveId) { }

        public bool CollectDraws(int scanInterval)
        {
            if (Time.frameCount - _lastScanFrame >= scanInterval)
            {
                _lastScanFrame = Time.frameCount;
                _renderers.Clear();
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (r.gameObject.layer == k_CharLayer)
                        _renderers.Add(r);
            }

            _draws.Clear();
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible)
                    continue;

                r.GetSharedMaterials(_matBuffer);
                for (int sub = 0; sub < _matBuffer.Count; sub++)
                {
                    Material m = _matBuffer[sub];
                    if (m == null || m.shader == null)
                        continue;

                    if (!_passIndexByShader.TryGetValue(m.shader, out int pass))
                    {
                        pass = m.FindPass("HologramCoverage");
                        _passIndexByShader[m.shader] = pass;
                    }
                    if (pass < 0)
                        continue;
                    if (m.HasProperty(k_UseHologramFillId) && m.GetFloat(k_UseHologramFillId) < 0.5f)
                        continue;

                    _draws.Add(new DrawEntry { renderer = r, material = m, submesh = sub, pass = pass });
                }
            }

            return _draws.Count > 0;
        }
    }

    /// <summary>闪避残影的计数：SkyPrisonAfterimageEmitter 登记的、正在显示的残影。</summary>
    private sealed class GhostCoveragePass : CoveragePassBase
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Material> _matBuffer = new List<Material>();

        public GhostCoveragePass() : base("GhostCoverage", "_SP_GhostCoverage", k_GhostActiveId) { }

        public bool CollectDraws()
        {
            _renderers.Clear();
            _draws.Clear();
            SkyPrisonAfterimageEmitter.CollectActiveGhostRenderers(_renderers);

            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null || !r.isVisible)
                    continue;

                r.GetSharedMaterials(_matBuffer);
                for (int sub = 0; sub < _matBuffer.Count; sub++)
                {
                    Material m = _matBuffer[sub];
                    if (m == null)
                        continue;
                    int pass = m.FindPass("GhostCoverage");
                    if (pass < 0)
                        continue;
                    _draws.Add(new DrawEntry { renderer = r, material = m, submesh = sub, pass = pass });
                }
            }

            return _draws.Count > 0;
        }
    }
}
