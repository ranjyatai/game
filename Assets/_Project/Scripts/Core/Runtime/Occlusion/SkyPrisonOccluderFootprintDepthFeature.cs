using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

/// <summary>
/// 遮挡物落地深度图。把每个遮挡物「根节点」的眼深度画进一张全屏 RFloat 纹理，
/// 角色着色器逐像素采样它来决定要不要藏。
///
/// 解决的问题：2.5D 的遮挡语义是「谁的脚在前面」，不是「谁的表面离相机近」。
/// 45 度俯视下高的物体顶部会朝相机倾过来——叉车顶棚的几何深度可以比站在叉车
/// 前面的角色脚底还小。直接拿 _CameraDepthTexture 比较，同一台叉车会出现
/// 「顶部判在前、底部判在后」，角色被从中间切开（实测：头绿身红）。
///
/// 这张图里每个物体是一个平坦的常数值（它自己的落地深度），所以一个遮挡物对
/// 角色只有「挡」或「不挡」两种结果，不会把角色切开。
///
/// 关键：这张图只决定「挡不挡」，不决定「藏哪些像素」。角色露在遮挡物轮廓外面的
/// 像素采样到的是清空值（远平面），不会被藏，正常显示——遮挡依然是逐像素的。
///
/// CPU 侧只提供「哪些 Renderer 算遮挡物」这一个列表（注册发生在 OnEnable /
/// 结构重建，不是每帧扫场景），判定全在 GPU，不做任何回读。
/// </summary>
public class SkyPrisonOccluderFootprintDepthFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public bool enabled = true;
        public Shader footprintShader;

        /// <summary>只在这台相机上生成。角色由 Main Camera（Base）绘制，
        /// 其余相机（UI / OverheadUI / OcclusionMask）不需要这张图。</summary>
        public string targetCameraName = "Main Camera";
    }

    public Settings settings = new Settings();

    private FootprintPass pass;
    private Material footprintMaterial;

    /// <summary>清空值。任何真实遮挡物的落地深度都远小于它，
    /// 所以「没有遮挡物覆盖」天然表现为「不遮挡」。</summary>
    public const float ClearDepth = 1e9f;

    public static readonly int FootprintTextureId =
        Shader.PropertyToID("_SkyPrison_OccluderFootprintDepth");

    /// <summary>每个遮挡物的落地世界坐标，DrawRenderer 之前逐个写入。</summary>
    public static readonly int FootprintRootId =
        Shader.PropertyToID("_SkyPrison_FootprintRootWS");

    public override void Create()
    {
        pass = new FootprintPass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.enabled || settings.footprintShader == null)
            return;

        Camera cam = renderingData.cameraData.camera;
        if (cam == null || cam.name != settings.targetCameraName)
            return;

        if (footprintMaterial == null || footprintMaterial.shader != settings.footprintShader)
            footprintMaterial = CoreUtils.CreateEngineMaterial(settings.footprintShader);

        pass.Setup(footprintMaterial);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(footprintMaterial);
        footprintMaterial = null;
        pass?.Dispose();
    }

    private class FootprintPass : ScriptableRenderPass
    {
        /// <summary>
        /// 诊断总开关。编译期常量，不是序列化字段——序列化字段会被存进场景/资产，
        /// 然后在谁都没注意的情况下跟着 Build 出去（这个项目已经发生过一次）。
        /// 要重新排查时把它改成 true 重新编译即可。
        /// </summary>
        private const bool DiagnosticsEnabled = false;

        private Material material;
        private RTHandle target;

        private class PassData
        {
            public Material material;
            public Renderer[] renderers;
        }

        public void Setup(Material mat) => material = mat;

        /// <summary>
        /// 把落地深度比较的两侧都打进日志。
        ///
        /// 这两个量（角色根节点、遮挡物根节点）都是 Transform 位置，CPU 上就能算，
        /// 不需要读回 GPU 纹理，也不需要靠诊断可视化去肉眼判断——而可视化在
        /// saturate 之后本来就分不清「图是空的」和「值差得远」，模式 4 全红就栽在这。
        ///
        /// 只要 occluderEye &lt; charEye，着色器就该判定为遮挡。日志能直接验证这一点，
        /// 从而把「渲染没写进去」和「值本身就不该遮挡」彻底分开。
        /// </summary>
        private static void LogAnchorDepths(Camera cam)
        {
            if (cam == null || Time.frameCount % 120 != 0)
                return;

            Matrix4x4 worldToCamera = cam.worldToCameraMatrix;

            float charEye = float.NaN;
            string charName = "(未找到)";
            foreach (var receiver in Object.FindObjectsByType<UnitOcclusionMaterialReceiver>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (receiver == null)
                    continue;

                charName = receiver.name;
                charEye = -worldToCamera.MultiplyPoint3x4(receiver.transform.position).z;
                break;
            }

            var list = SkyPrisonOccluderRegistry.Renderers;
            float nearest = float.MaxValue;
            string nearestName = "(无)";

            for (int i = 0; i < list.Count; i++)
            {
                Renderer r = list[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                float eye = -worldToCamera.MultiplyPoint3x4(r.transform.position).z;
                if (eye < nearest)
                {
                    nearest = eye;
                    nearestName = r.name;
                }
            }

            // 叉车单独打一份。它是当前正在测的遮挡物，而「最近遮挡物」经常是脚边的草，
            // 两者不是一回事——之前一直只看到草，差点据此误判叉车没注册。
            float forkliftEye = float.NaN;
            for (int i = 0; i < list.Count; i++)
            {
                Renderer r = list[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                if (r.name.IndexOf("Forklift", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                forkliftEye = -worldToCamera.MultiplyPoint3x4(r.transform.position).z;
                break;
            }
            pendingForkliftEye = forkliftEye;

            // 叉车所有渲染体在 pass 眼里的状态。pass 会跳过 !enabled 或未激活的，
            // LODGroup 正是靠禁用 Renderer 来切 LOD 的——如果所有 LOD 都被判为跳过，
            // 叉车就一个像素都不会写进落地深度图。
            if (!loggedRegistryNames)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    Renderer r = list[i];
                    if (r == null)
                        continue;
                    if (r.name.IndexOf("Forklift", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    sb.Append($"{r.name}[enabled={r.enabled},active={r.gameObject.activeInHierarchy}," +
                              $"mats={(r.sharedMaterials != null ? r.sharedMaterials.Length : 0)}] ");
                }
                Debug.Log($"[FootprintFork] {sb}");
            }

            Debug.Log($"[FootprintDepth] 角色 {charName} 根节点深度={charEye:F2} | " +
                      $"最近遮挡物 {nearestName} 落地深度={nearest:F2} | " +
                      $"叉车落地深度={forkliftEye:F2}（diff={charEye - forkliftEye:F2}，>0 才该被遮挡）");
        }

        private static string Fmt(float v, float charEye)
        {
            return v >= ClearDepth * 0.5f
                ? "清空值(无遮挡物覆盖)"
                : $"{v:F2}(diff={charEye - v:F2})";
        }

        private static bool readbackPending;
        // 发起回读那一刻的角色屏幕位置和深度。回调是异步的，那时相机可能已经动了，
        // 必须用发起时的快照，否则索引到的像素和当时的画面对不上。
        private static Vector2 pendingCharScreenUV;
        private static float pendingCharEye;
        private static float pendingForkliftEye = float.NaN;
        private static bool loggedRegistryNames;

        /// <summary>
        /// 把落地深度纹理读回来看内容。
        ///
        /// CPU 侧的值已经验证是对的（diff > 0 该判定为遮挡），所以剩下唯一的问题是
        /// 「这些值有没有真的写进纹理」。诊断可视化分不清这一点——saturate 之后
        /// 空图和值差得远画出来一模一样，已经误导过两次。读回来直接看数值最可靠。
        ///
        /// 仅诊断用，每 120 帧一次且异步，不参与任何运行时判定 —— 判定全在 GPU，
        /// 不存在「左手传右手」的回读。
        /// </summary>
        private void LogTextureContent(Camera cam)
        {
            if (readbackPending || target == null || target.rt == null || Time.frameCount % 120 != 0)
                return;

            if (cam == null)
                return;

            // 角色屏幕位置 —— 决定着色器实际会采样到哪一块像素。
            pendingCharScreenUV = new Vector2(-1f, -1f);
            pendingCharEye = float.NaN;

            foreach (var receiver in Object.FindObjectsByType<UnitOcclusionMaterialReceiver>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (receiver == null)
                    continue;

                Vector3 sp = cam.WorldToScreenPoint(receiver.transform.position);
                pendingCharScreenUV = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
                pendingCharEye = -cam.worldToCameraMatrix.MultiplyPoint3x4(receiver.transform.position).z;
                break;
            }

            if (!loggedRegistryNames)
            {
                loggedRegistryNames = true;
                var names = new System.Text.StringBuilder();
                var reg = SkyPrisonOccluderRegistry.Renderers;
                for (int i = 0; i < reg.Count; i++)
                {
                    if (reg[i] == null)
                        continue;
                    names.Append(reg[i].name).Append(", ");
                }
                Debug.Log($"[FootprintRT] 注册的遮挡物名单：{names}");
            }

            readbackPending = true;
            AsyncGPUReadback.Request(target.rt, 0, TextureFormat.RFloat, request =>
            {
                readbackPending = false;

                if (request.hasError)
                {
                    Debug.LogWarning("[FootprintRT] 回读失败");
                    return;
                }

                var data = request.GetData<float>();
                float min = float.MaxValue;
                int written = 0;
                int sampled = 0;

                // 隔点抽样即可，只要判断「有没有非清空值」和「最小值是多少」。
                for (int i = 0; i < data.Length; i += 97)
                {
                    float v = data[i];
                    sampled++;
                    if (v < ClearDepth * 0.5f)
                        written++;
                    if (v < min)
                        min = v;
                }

                // 决定性的一步：读角色所在像素的值，这正是着色器会采样到的东西。
                string atChar = "(角色不在屏幕内)";
                if (pendingCharScreenUV.x >= 0f && pendingCharScreenUV.x <= 1f &&
                    pendingCharScreenUV.y >= 0f && pendingCharScreenUV.y <= 1f)
                {
                    int w = request.width;
                    int h = request.height;
                    int px = Mathf.Clamp((int)(pendingCharScreenUV.x * w), 0, w - 1);
                    // 往上抬一点，取身体中部而不是脚下那一行像素。
                    int py = Mathf.Clamp((int)(pendingCharScreenUV.y * h) + h / 12, 0, h - 1);

                    // 两种行序都采一次。AsyncGPUReadback 的行序在 D3D 下是从上往下，
                    // 而 Unity 屏幕坐标 y=0 在下——上一版直接拿屏幕 y 当行号，
                    // 读到的很可能是上下镜像后的那一行（画面上方＝更远，正好能解释
                    // 「角色像素处 149.52」这个几何上说不通的值）。
                    // 在确定哪个是对的之前，不能拿其中任何一个给着色器定罪。
                    float vDirect = data[py * w + px];
                    float vFlipped = data[(h - 1 - py) * w + px];

                    atChar = $"直接索引={Fmt(vDirect, pendingCharEye)}，" +
                             $"翻转索引={Fmt(vFlipped, pendingCharEye)}";
                }

                // 叉车的落地深度是已知的（CPU 侧算得到）。它在纹理里出现了多少像素，
                // 直接回答「叉车到底画进去没有」——这是当前唯一还没确认的环节。
                // 角色像素处两个行序都是清空值，所以问题不在索引，而在内容。
                int forkliftPixels = 0;
                int nonClearFull = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    float v = data[i];
                    if (v >= ClearDepth * 0.5f)
                        continue;

                    nonClearFull++;
                    if (Mathf.Abs(v - pendingForkliftEye) < 1.0f)
                        forkliftPixels++;
                }

                Debug.Log($"[FootprintRT] 抽样 {sampled} 点，非清空 {written} 点，最小值={min:F2} | " +
                          $"角色像素处={atChar} | " +
                          $"全图非清空={nonClearFull} 像素，其中叉车({pendingForkliftEye:F2}±1)={forkliftPixels} 像素");
            });
        }

        public void Dispose()
        {
            target?.Release();
            target = null;
        }

        private void EnsureTarget(in RenderTextureDescriptor cameraDesc)
        {
            RenderTextureDescriptor desc = cameraDesc;
            // 只存一个标量深度值，不需要颜色通道/MSAA/深度附件。
            desc.colorFormat = RenderTextureFormat.RFloat;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;

            RenderingUtils.ReAllocateHandleIfNeeded(
                ref target, desc,
                FilterMode.Point,          // 深度值不能被插值，必须 Point
                TextureWrapMode.Clamp,
                name: "_SkyPrison_OccluderFootprintDepth");
        }

        /// <summary>每帧准备要画的遮挡物。注册表本身不是每帧构建的，
        /// 这里只是拷一份快照给 RenderGraph 的 PassData 用。</summary>
        private static Renderer[] CollectRenderers()
        {
            SkyPrisonOccluderRegistry.Compact();
            var list = SkyPrisonOccluderRegistry.Renderers;

            var result = new Renderer[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = list[i];

            return result;
        }

#if UNITY_6000_0_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            EnsureTarget(cameraData.cameraTargetDescriptor);
            // 诊断默认全关。
            //
            // LogTextureContent 里有 AsyncGPUReadback，每 120 帧把整张 4K 纹理读回 CPU——
            // 它定位出了「静态合批导致 unity_ObjectToWorld 退化成单位阵」这个根因，
            // 但常开就正好违反了这套架构的前提：判定全在 GPU，不做回读。
            // 这个项目上次已经吃过「调试开关被序列化进场景、打进 Build、1749 GC/分钟」的亏，
            // 所以这里用编译期常量而不是可序列化字段——不可能被 Inspector 或场景意外打开。
            if (DiagnosticsEnabled)
            {
                LogAnchorDepths(cameraData.camera);
                LogTextureContent(cameraData.camera);
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       "SkyPrison OccluderFootprintDepth", out PassData passData))
            {
                passData.material = material;
                passData.renderers = CollectRenderers();

                TextureHandle targetTexture = renderGraph.ImportTexture(target);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // 清成极远。没有遮挡物覆盖的像素就保持这个值，
                    // 角色在那里一定判定为「不被挡」，露出的部分正常显示。
                    context.cmd.ClearRenderTarget(false, true, new Color(ClearDepth, 0f, 0f, 0f));

                    if (data.renderers == null)
                        return;

                    for (int i = 0; i < data.renderers.Length; i++)
                    {
                        Renderer r = data.renderers[i];
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                            continue;

                        // 每画一个之前先写它的落地坐标。命令缓冲顺序执行，
                        // 「设值 → 画这一个」是可靠配对。不能依赖着色器里的
                        // unity_ObjectToWorld——静态合批会把它退化成单位阵，
                        // 实测叉车就是因此写出了错误的深度（纹理里 0 个像素匹配）。
                        Vector3 root = r.transform.position;
                        context.cmd.SetGlobalVector(FootprintRootId,
                            new Vector4(root.x, root.y, root.z, 1f));

                        int subMeshCount = Mathf.Max(1, r.sharedMaterials != null ? r.sharedMaterials.Length : 1);
                        for (int sub = 0; sub < subMeshCount; sub++)
                            context.cmd.DrawRenderer(r, data.material, sub, 0);
                    }
                });
            }

            // 供角色合成着色器采样。必须在 pass 之外设置，
            // 保证同一帧后续的透明物件都能读到。
            Shader.SetGlobalTexture(FootprintTextureId, target);
        }
#endif
    }
}
