using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// 每帧把 Character2D 层（layer 8）的角色轮廓渲染到 R8 RT，
/// 供 LootDropHologram shader 做逐像素前后遮挡判断。
///
/// 使用独立的 HLSL 捕获 shader（Hidden/SP/CharPresenceCapture）和
/// AddUnsafePass + DrawRenderer，完全绕开 cullResults 与 CG shader 兼容问题。
/// </summary>
public sealed class CharacterPresenceFeature : ScriptableRendererFeature
{
    [Tooltip("拖入 CharPresenceCapture.shader。Hidden/ shader 不会自动打包，必须显式引用。")]
    [SerializeField] private Shader captureShader;

    private static readonly int k_TexId    = Shader.PropertyToID("_SP_CharPresence");
    private static readonly int k_ZId      = Shader.PropertyToID("_SP_CharWorldZ");
    private static readonly int k_ActiveId = Shader.PropertyToID("_SP_CharPresenceActive");

    private RenderTexture         _rt;
    private CharacterPresencePass _pass;
    private int _lastWriteFrame = -1;
    private int _lastRtW        = -1;
    private int _lastRtH        = -1;

    public override void Create()
    {
        _pass = new CharacterPresencePass();
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera cam = renderingData.cameraData.camera;
        if (!cam.CompareTag("MainCamera")) return;

        // RT 尺寸跟随相机实际渲染分辨率（非 Screen.width，避免 render scale 错位）
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        int w = desc.width;
        int h = desc.height;
        if (w <= 0 || h <= 0) return;

        if (_rt == null || _lastRtW != w || _lastRtH != h)
        {
            if (_rt != null) _rt.Release();
            _rt      = new RenderTexture(w, h, 0, RenderTextureFormat.R8)
            {
                name       = "RT_CharacterPresence",
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };
            _rt.Create();
            _lastRtW = w;
            _lastRtH = h;
            _pass.SetRT(_rt);
            Shader.SetGlobalTexture(k_TexId, _rt);
        }

        int frame = Time.frameCount;
        if (frame == _lastWriteFrame) return;
        _lastWriteFrame = frame;

        float charZ    = 9999f;
        var   playerGO = GameObject.FindWithTag("Player");
        if (playerGO != null) charZ = playerGO.transform.position.z;

        Shader.SetGlobalTexture(k_TexId, _rt);
        Shader.SetGlobalFloat(k_ZId,      charZ);
        Shader.SetGlobalFloat(k_ActiveId, 1f);

        _pass.SetCaptureShader(captureShader);
        _pass.RefreshRenderers();
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        Shader.SetGlobalFloat(k_ActiveId, 0f);
        _pass?.Dispose();
        if (_rt != null) { _rt.Release(); _rt = null; }
    }
}

// ── Pass ────────────────────────────────────────────────────────────────────

sealed class CharacterPresencePass : ScriptableRenderPass
{
    private const int k_CharLayer = 8;

    // 独立 HLSL 捕获 shader — 不依赖 SpineOcclusionComposite 的任何 pass
    private static readonly string k_CaptureShaderName = "Hidden/SP/CharPresenceCapture";

    private RenderTexture _rt;
    private Shader        _captureShaderOverride;

    public void SetRT(RenderTexture rt) { _rt = rt; }
    public void SetCaptureShader(Shader s) { if (s != null) _captureShaderOverride = s; }

    // 按纹理缓存捕获材质（Spine 通常单角色单张 atlas）
    private readonly Dictionary<Texture, Material> _captureMats = new Dictionary<Texture, Material>();

    private struct DrawEntry
    {
        public Renderer renderer;
        public Material captureMat;
        public int      submesh;
    }

    private readonly List<DrawEntry> _drawList  = new List<DrawEntry>();
    private DrawEntry[] _drawArray               = System.Array.Empty<DrawEntry>();
    private int         _lastRefreshFrame        = -100;

    // 每隔 30 帧重新扫描 layer 8 的 Renderer
    public void RefreshRenderers()
    {
        int frame = Time.frameCount;
        if (frame - _lastRefreshFrame < 30) return;
        _lastRefreshFrame = frame;

        Shader captureShader = _captureShaderOverride != null
            ? _captureShaderOverride
            : Shader.Find(k_CaptureShaderName);
        if (captureShader == null)
        {
            Debug.LogWarning("[CharPresence] Shader not found: " + k_CaptureShaderName +
                             "。请在 URP Renderer 的 CharacterPresenceFeature 组件里拖入 CharPresenceCapture.shader。");
            _drawArray = System.Array.Empty<DrawEntry>();
            return;
        }

        _drawList.Clear();

        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var r in all)
        {
            if (r.gameObject.layer != k_CharLayer) continue;

            var mats = r.sharedMaterials;
            for (int sub = 0; sub < mats.Length; sub++)
            {
                Material srcMat = mats[sub];
                if (srcMat == null) continue;

                Texture tex = srcMat.mainTexture;
                if (tex == null) continue;

                // 按纹理复用或新建捕获材质
                if (!_captureMats.TryGetValue(tex, out Material capMat))
                {
                    capMat = new Material(captureShader) { hideFlags = HideFlags.HideAndDontSave };
                    capMat.mainTexture = tex;
                    _captureMats[tex]  = capMat;
                }

                _drawList.Add(new DrawEntry { renderer = r, captureMat = capMat, submesh = sub });
            }
        }

        _drawArray = _drawList.ToArray();
    }

    public void Dispose()
    {
        foreach (var mat in _captureMats.Values)
            if (mat != null) Object.DestroyImmediate(mat);
        _captureMats.Clear();
    }

    // ── RenderGraph 路径 ─────────────────────────────────────────────────────

    class PassData
    {
        public DrawEntry[]   draws;
        public RenderTexture rt;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_rt == null || _drawArray.Length == 0) return;

        using (var builder = renderGraph.AddUnsafePass<PassData>("CharacterPresence", out var passData))
        {
            passData.draws = _drawArray;
            passData.rt    = _rt;
            builder.AllowPassCulling(false);

            builder.SetRenderFunc<PassData>(static (PassData data, UnsafeGraphContext ctx) =>
            {
                ctx.cmd.SetRenderTarget(data.rt);
                ctx.cmd.SetViewport(new Rect(0, 0, data.rt.width, data.rt.height));
                ctx.cmd.ClearRenderTarget(false, true, Color.clear);

                foreach (var d in data.draws)
                {
                    if (d.renderer == null || d.captureMat == null) continue;
                    // pass 0 = CharPresenceCapture（独立 HLSL shader，无 CG 依赖）
                    ctx.cmd.DrawRenderer(d.renderer, d.captureMat, d.submesh, 0);
                }
            });
        }
    }

#pragma warning disable CS0618
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0618
}
