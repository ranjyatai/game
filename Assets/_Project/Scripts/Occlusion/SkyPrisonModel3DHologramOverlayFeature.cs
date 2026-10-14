using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 2026-08-18：显式点名绘制 3D 通道场景物（比如可破坏木箱）材质里的 "HologramOverlay3D"
/// Pass。这条 Pass 跟角色用的 "NormalBody" 共用 Tags{"LightMode"="UniversalForward"}——
/// URP 标准前向渲染每个物体每个 LightMode 只会挑一条 Pass 画，不会像内置管线那样把
/// 同一个 LightMode 下的多条 Pass 都跑一遍。这条 Pass 从加进 shader 那天起材质、判定
/// 逻辑都是对的（Frame Debugger 验证过：材质在渲染器上，判定值也对），但从来没被
/// 调度执行过——不接一个显式按 pass 索引 DrawRenderer 的 Feature，它永远不会跑。
///
/// 只处理 UnitOcclusionMaterialReceiver.Active3DPropReceivers 登记过的实例，
/// 不用每帧全场景扫。
/// </summary>
public sealed class SkyPrisonModel3DHologramOverlayFeature : ScriptableRendererFeature
{
    [Header("只在这台相机上执行")]
    public string targetCameraName = "Main Camera";

    private const string PassName = "HologramOverlay3D";

    private HologramOverlayPass _pass;

    public override void Create()
    {
        _pass = new HologramOverlayPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera cam = renderingData.cameraData.camera;
        if (!renderingData.cameraData.isSceneViewCamera && cam.name != targetCameraName)
            return;

        if (UnitOcclusionMaterialReceiver.Active3DPropReceivers.Count == 0)
            return;

        renderer.EnqueuePass(_pass);
    }

    private sealed class HologramOverlayPass : ScriptableRenderPass
    {
        private struct DrawEntry
        {
            public Renderer renderer;
            public Material material;
            public int submesh;
            public int passIndex;
        }

        private readonly List<DrawEntry> _draws = new List<DrawEntry>(8);

        private sealed class PassData
        {
            public DrawEntry[] draws;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            _draws.Clear();

            var receivers = UnitOcclusionMaterialReceiver.Active3DPropReceivers;
            for (int i = 0; i < receivers.Count; i++)
            {
                UnitOcclusionMaterialReceiver receiver = receivers[i];
                if (receiver == null || !receiver.isActiveAndEnabled)
                    continue;

                var resolved = receiver.ResolvedRenderers;
                for (int r = 0; r < resolved.Count; r++)
                {
                    Renderer renderer = resolved[r];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;

                    Material[] mats = renderer.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        Material mat = mats[m];
                        if (mat == null)
                            continue;

                        int passIndex = mat.FindPass(PassName);
                        if (passIndex < 0)
                            continue;

                        _draws.Add(new DrawEntry
                        {
                            renderer = renderer,
                            material = mat,
                            submesh = m,
                            passIndex = passIndex
                        });
                    }
                }
            }

            if (_draws.Count == 0)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                "SkyPrison Model3D HologramOverlay3D", out PassData passData);

            passData.draws = _draws.ToArray();

            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                for (int i = 0; i < data.draws.Length; i++)
                {
                    DrawEntry d = data.draws[i];
                    if (d.renderer == null || d.material == null)
                        continue;

                    context.cmd.DrawRenderer(d.renderer, d.material, d.submesh, d.passIndex);
                }
            });
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
    }
}
