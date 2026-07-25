using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 每帧把所有活跃 LootDropVisual 的全息模型用纯白 Cull Off 写进
/// RT_MainCameraCharacterMask_Item，供 UIExpandMasked 做遮挡描边合成。
/// 不做 raycast 偵測：交叉判断由 HiddenMask = CharacterMask × OccluderMask 自动完成，
/// 和 Spine 角色的接入方式完全一致。
/// </summary>
public sealed class LootDropOcclusionOutlineFeature : ScriptableRendererFeature
{
    [Header("CharacterMask RT 名称（须与主 Feature 中的 Item 频道一致）")]
    public string characterMaskItemRTName = "RT_MainCameraCharacterMask_Item";

    [Header("渲染事件（必须在主 Feature 的 CharacterMask pass 之后、HiddenComposite 之前）")]
    public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;

    [Header("只在 Main Camera 上执行")]
    public bool onlyMainCamera = true;

    private LootDropSilhouettePass _pass;

    public override void Create()
    {
        _pass = new LootDropSilhouettePass(this) { renderPassEvent = passEvent };
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Cleanup();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (onlyMainCamera
            && !renderingData.cameraData.isSceneViewCamera
            && renderingData.cameraData.camera != Camera.main)
            return;

        if (LootDropVisual.ActiveDrops.Count == 0) return;
        renderer.EnqueuePass(_pass);
    }

    // ── Inner Pass ────────────────────────────────────────────────────────────

    private sealed class LootDropSilhouettePass : ScriptableRenderPass
    {
        private readonly LootDropOcclusionOutlineFeature _feat;
        private static Material  _mat;
        private RTHandle         _cachedHandle;
        private RenderTexture    _cachedRT;

        private sealed class PassData
        {
            public RTHandle     target;
            public Renderer[][] rendererGroups;
        }

        public LootDropSilhouettePass(LootDropOcclusionOutlineFeature feat) => _feat = feat;

        public void Cleanup()
        {
            if (_cachedHandle != null) { _cachedHandle.Release(); _cachedHandle = null; }
            _cachedRT = null;
        }

        private static Material GetMat()
        {
            if (_mat != null) return _mat;
            var shader = Shader.Find("Hidden/SkyPrison/OccluderSolidMask");
            if (shader == null)
            {
                Debug.LogError("[LootDropOcclusionOutlineFeature] 找不到 Hidden/SkyPrison/OccluderSolidMask");
                return null;
            }
            _mat = new Material(shader) { name = "M_LootDropSilhouette", hideFlags = HideFlags.DontSave };
            _mat.SetColor("_MaskColor", Color.white);
            return _mat;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = GetMat();
            if (mat == null) return;

            // 收集所有活跃掉落物的 renderer（无条件写入 CharacterMask，
            // 遮挡交集由 HiddenMask = CharacterMask × OccluderMask 自动处理）
            var active = LootDropVisual.ActiveDrops;
            var groups = new List<Renderer[]>(active.Count);
            for (int i = 0; i < active.Count; i++)
            {
                LootDropVisual dv = active[i];
                if (dv == null) continue;
                Renderer[] rs = dv.ModelRenderers;
                if (rs != null && rs.Length > 0) groups.Add(rs);
            }
            if (groups.Count == 0) return;

            // 找 CharacterMask Item RT（按名称，缓存避免每帧全扫）
            RenderTexture rt = FindRT(_feat.characterMaskItemRTName);
            if (rt == null)
            {
                Debug.LogWarning($"[LootDropOcclusionOutlineFeature] 未找到 RT '{_feat.characterMaskItemRTName}'，请确认主 Feature 已初始化该 RT。");
                return;
            }

            if (_cachedRT != rt)
            {
                _cachedHandle?.Release();
                _cachedRT     = rt;
                _cachedHandle = RTHandles.Alloc(rt);
            }

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                "LootDrop Silhouette → CharacterMask Item", out PassData data);

            TextureHandle tex = renderGraph.ImportTexture(_cachedHandle);
            builder.SetRenderAttachment(tex, 0, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            data.target         = _cachedHandle;
            data.rendererGroups = groups.ToArray();

            Material captureMat = mat;
            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                // 不 clear：叠加到已有内容（同频道 Spine 角色已由主 Feature 写入）
                foreach (var group in d.rendererGroups)
                    foreach (var r in group)
                    {
                        if (r == null || !r.enabled) continue;
                        ctx.cmd.DrawRenderer(r, captureMat, 0, 0);
                    }
            });
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }

        private static RenderTexture FindRT(string name)
        {
            foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
                if (rt != null && rt.name == name) return rt;
            return null;
        }
    }
}
