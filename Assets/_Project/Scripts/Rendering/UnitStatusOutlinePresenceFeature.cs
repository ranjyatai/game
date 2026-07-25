using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// 状态描边（灼烧等持续发光描边）专用的"每单位独立轮廓蒙版"。
///
/// 跟 CharacterPresenceFeature（全场所有Character2D角色合并成一张共享蒙版，本来是给
/// 全息掉落物遮挡判定用的）不是一回事——状态描边最初直接借用了那张共享蒙版，结果
/// 两个角色贴在一起/重叠时，合并蒙版会把双方轮廓的交界处焊成一片连续区域，边缘检测
/// 找不到边，表现为"重叠处描边缺口"。
///
/// 这个 Feature 每帧只为"当前描边强度>0"的单位单独渲染一张只包含它自己部件的蒙版
/// （复用 CharacterPresenceFeature 同一个捕获 shader "Hidden/SP/CharPresenceCapture"），
/// 单位互相之间完全隔离，天然不会有这个问题。只有真的开着描边的单位才有这份开销，
/// 平时没人在燃烧时这个 Feature 什么都不做。
/// </summary>
public sealed class UnitStatusOutlinePresenceFeature : ScriptableRendererFeature
{
    [Tooltip("拖入 CharPresenceCapture.shader（跟 CharacterPresenceFeature 用的是同一份）。Hidden/ shader 不会自动打包，必须显式引用。")]
    [SerializeField] private Shader captureShader;

    [Tooltip("单帧最多同时渲染几个单位的专属蒙版，超出的单位暂时不描边（防止大量单位同时燃烧时RT开销失控）。")]
    [SerializeField] private int maxConcurrentUnits = 8;

    [Tooltip("扫描场景里 UnitStatusOutlineEffect 的间隔帧数，不用每帧都做一次FindObjectsByType。")]
    [SerializeField] private int scanIntervalFrames = 15;

    private const string k_CaptureShaderName = "Hidden/SP/CharPresenceCapture";

    private UnitStatusOutlinePresencePass _pass;

    public override void Create()
    {
        _pass = new UnitStatusOutlinePresencePass();
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera cam = renderingData.cameraData.camera;
        if (!cam.CompareTag("MainCamera")) return;

        Shader shader = captureShader != null ? captureShader : Shader.Find(k_CaptureShaderName);
        if (shader == null) return;

        var desc = renderingData.cameraData.cameraTargetDescriptor;
        int w = desc.width;
        int h = desc.height;
        if (w <= 0 || h <= 0) return;

        _pass.Setup(shader, w, h, Mathf.Max(1, maxConcurrentUnits), Mathf.Max(1, scanIntervalFrames));
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }
}

sealed class UnitStatusOutlinePresencePass : ScriptableRenderPass
{
    private const float k_ActiveThreshold = 0.001f;

    private struct DrawEntry
    {
        public Renderer renderer;
        public Material captureMat;
        public int submesh;
    }

    private struct UnitGroup
    {
        public RenderTexture rt;
        public DrawEntry[] draws;
    }

    private Shader _captureShader;
    private int _width;
    private int _height;
    private int _maxConcurrentUnits;
    private int _scanIntervalFrames;

    private readonly Dictionary<Texture, Material> _captureMats = new Dictionary<Texture, Material>();
    private readonly Dictionary<UnitStatusOutlineEffect, RenderTexture> _unitRTs = new Dictionary<UnitStatusOutlineEffect, RenderTexture>();
    private readonly List<UnitStatusOutlineEffect> _cachedEffects = new List<UnitStatusOutlineEffect>();
    private readonly List<UnitStatusOutlineEffect> _activeBuffer = new List<UnitStatusOutlineEffect>();
    private readonly List<UnitStatusOutlineEffect> _staleKeysBuffer = new List<UnitStatusOutlineEffect>();
    private UnitGroup[] _currentGroups = System.Array.Empty<UnitGroup>();
    private int _lastScanFrame = -1000;

    public void Setup(Shader captureShader, int width, int height, int maxConcurrentUnits, int scanIntervalFrames)
    {
        _captureShader = captureShader;
        _width = width;
        _height = height;
        _maxConcurrentUnits = maxConcurrentUnits;
        _scanIntervalFrames = scanIntervalFrames;

        RefreshActiveUnits();
    }

    private void RefreshActiveUnits()
    {
        int frame = Time.frameCount;
        if (frame - _lastScanFrame >= _scanIntervalFrames)
        {
            _lastScanFrame = frame;
            _cachedEffects.Clear();
            _cachedEffects.AddRange(Object.FindObjectsByType<UnitStatusOutlineEffect>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        _activeBuffer.Clear();
        for (int i = 0; i < _cachedEffects.Count; i++)
        {
            UnitStatusOutlineEffect effect = _cachedEffects[i];
            if (effect == null || !effect.isActiveAndEnabled) continue;
            if (effect.CurrentIntensity <= k_ActiveThreshold) continue;
            if (effect.PresenceRenderers == null || effect.PresenceRenderers.Count == 0) continue;

            _activeBuffer.Add(effect);
            if (_activeBuffer.Count >= _maxConcurrentUnits) break;
        }

        // 释放已经不再活跃的单位占用的RT，并通知它们停止采样（避免残留贴图内容误判）
        _staleKeysBuffer.Clear();
        foreach (KeyValuePair<UnitStatusOutlineEffect, RenderTexture> kv in _unitRTs)
        {
            if (kv.Key == null || !_activeBuffer.Contains(kv.Key))
                _staleKeysBuffer.Add(kv.Key);
        }
        for (int i = 0; i < _staleKeysBuffer.Count; i++)
        {
            UnitStatusOutlineEffect key = _staleKeysBuffer[i];
            if (_unitRTs.TryGetValue(key, out RenderTexture staleRt) && staleRt != null)
                staleRt.Release();
            _unitRTs.Remove(key);
            if (key != null)
                key.SetPresenceRenderTexture(null, false);
        }

        var groups = new List<UnitGroup>(_activeBuffer.Count);
        for (int i = 0; i < _activeBuffer.Count; i++)
        {
            UnitStatusOutlineEffect effect = _activeBuffer[i];

            RenderTexture rt;
            if (!_unitRTs.TryGetValue(effect, out rt) || rt == null || rt.width != _width || rt.height != _height)
            {
                if (rt != null) rt.Release();
                rt = new RenderTexture(_width, _height, 0, RenderTextureFormat.R8)
                {
                    name = "RT_UnitStatusOutlinePresence",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                rt.Create();
                _unitRTs[effect] = rt;
            }

            IReadOnlyList<Renderer> renderers = effect.PresenceRenderers;
            var draws = new DrawEntry[renderers.Count];
            int drawCount = 0;
            for (int r = 0; r < renderers.Count; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null) continue;

                Material[] mats = renderer.sharedMaterials;
                for (int sub = 0; sub < mats.Length; sub++)
                {
                    Material srcMat = mats[sub];
                    if (srcMat == null) continue;

                    Texture tex = srcMat.mainTexture;
                    if (tex == null) continue;

                    Material capMat;
                    if (!_captureMats.TryGetValue(tex, out capMat))
                    {
                        capMat = new Material(_captureShader) { hideFlags = HideFlags.HideAndDontSave };
                        capMat.mainTexture = tex;
                        _captureMats[tex] = capMat;
                    }

                    if (drawCount >= draws.Length)
                        System.Array.Resize(ref draws, draws.Length + 4);

                    draws[drawCount++] = new DrawEntry { renderer = renderer, captureMat = capMat, submesh = sub };
                    break; // 每个 Renderer 只用第一个有效材质捕获轮廓形状，够用了
                }
            }

            if (drawCount != draws.Length)
                System.Array.Resize(ref draws, drawCount);

            effect.SetPresenceRenderTexture(rt, true);
            groups.Add(new UnitGroup { rt = rt, draws = draws });
        }

        _currentGroups = groups.ToArray();
    }

    private class PassData
    {
        public UnitGroup[] groups;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_currentGroups.Length == 0) return;

        using (var builder = renderGraph.AddUnsafePass<PassData>("UnitStatusOutlinePresence", out var passData))
        {
            passData.groups = _currentGroups;
            builder.AllowPassCulling(false);

            builder.SetRenderFunc<PassData>(static (PassData data, UnsafeGraphContext ctx) =>
            {
                for (int g = 0; g < data.groups.Length; g++)
                {
                    UnitGroup group = data.groups[g];
                    if (group.rt == null || group.draws == null) continue;

                    ctx.cmd.SetRenderTarget(group.rt);
                    ctx.cmd.SetViewport(new Rect(0, 0, group.rt.width, group.rt.height));
                    ctx.cmd.ClearRenderTarget(false, true, Color.clear);

                    for (int d = 0; d < group.draws.Length; d++)
                    {
                        DrawEntry entry = group.draws[d];
                        if (entry.renderer == null || entry.captureMat == null) continue;
                        ctx.cmd.DrawRenderer(entry.renderer, entry.captureMat, entry.submesh, 0);
                    }
                }
            });
        }
    }

#pragma warning disable CS0618
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0618

    public void Dispose()
    {
        foreach (KeyValuePair<UnitStatusOutlineEffect, RenderTexture> kv in _unitRTs)
        {
            if (kv.Value != null) kv.Value.Release();
            if (kv.Key != null) kv.Key.SetPresenceRenderTexture(null, false);
        }
        _unitRTs.Clear();

        foreach (Material mat in _captureMats.Values)
            if (mat != null) Object.DestroyImmediate(mat);
        _captureMats.Clear();
    }
}
