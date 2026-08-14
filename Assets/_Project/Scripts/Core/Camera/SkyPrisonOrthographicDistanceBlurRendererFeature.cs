using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

/// <summary>
/// 2.5D / Orthographic friendly distance blur for Sky Prison.
///
/// This version uses a real gaussian-pyramid structure:
/// final stack color -> half downsample -> separable gaussian -> quarter downsample -> separable gaussian -> composite.
/// It looks much closer to a real gaussian / soft lens blur than a full-resolution variable-radius smear.
/// </summary>
public class SkyPrisonOrthographicDistanceBlurRendererFeature : ScriptableRendererFeature
{
    public enum BlurTargetMode
    {
        World3DOnlyBeforeOverlays = 0,
        FullStackAfterNamedOverlay = 1,
        BaseCameraFinalAfterStack = 2,
    }

    public enum BlurMaskMode
    {
        SceneDepth = 0,
        ScreenY = 1,
        MixedDepthAndScreenY = 2,
        ScreenFocusBand = 3,
    }

    [System.Serializable]
    public class Settings
    {
        public bool enabled = true;
        public Shader shader;
        public bool applyToSceneView = false;

        [Header("Camera Stack Target")]
        public BlurTargetMode targetMode = BlurTargetMode.FullStackAfterNamedOverlay;
        public string afterOverlayCameraName = "GamePlayCamera";

        [Header("Blur Mask")]
        public BlurMaskMode maskMode = BlurMaskMode.ScreenFocusBand;
        [Min(0.01f)] public float focusDistance = 35f;
        [Min(0.01f)] public float blurRange = 24f;
        [Range(0f, 1f)] public float screenBlurStartY = 0.35f;
        [Range(0.01f, 1f)] public float screenBlurEndY = 0.9f;

        [Header("Focus Band Mask")]
        [Range(0f, 1f)] public float screenFocusY = 0.48f;
        [Range(0f, 0.5f)] public float screenClearHalfHeight = 0.16f;
        [Range(0.01f, 1f)] public float screenFocusFadeRange = 0.35f;

        [Header("Gaussian Pyramid")]
        [Range(0f, 64f)] public float maxRadius = 10f;
        [Range(0f, 1f)] public float intensity = 0.45f;
        [Tooltip("Half-res gaussian radius scale. 1.0 is natural; lower is cheaper/sharper.")]
        [Range(0.1f, 2f)] public float halfBlurRadiusScale = 1.0f;
        [Tooltip("Quarter-res gaussian radius scale. Usually slightly larger than half-res for lens-like softness.")]
        [Range(0.1f, 3f)] public float quarterBlurRadiusScale = 1.25f;

        [Header("Debug")]
        public bool debugShowBlurMask = false;
    }

    public Settings settings = new Settings();

    private Material material;
    private DistanceBlurPass pass;

    /// <summary>
    /// 建材质的实际逻辑，Create() 和 AddRenderPasses() 共用。
    ///
    /// 用户实测：单纯进 Play 模糊不出现，要在编辑器里点一下 URP 资产才会生效。
    /// 说明 Create() 没有在每次进 Play 时都被调用——它只在特定时机触发（比如脚本
    /// 重编译引发的 Domain Reload），点击资产大概率是间接触发了 Inspector 重绘或
    /// 资产 reload，才把 Create() 顺带叫了一遍。这和早上 OcclusionPathCompareHUD、
    /// AudioListener 探针失效是同一类问题：依赖了一个不保证被调用的生命周期时机。
    ///
    /// 不再去猜 Create() 什么时候会被调用，改成 AddRenderPasses 里每帧检查一次，
    /// 材质缺了就地补上——不需要用户做任何"碰一下"这种操作。
    /// </summary>
    private void EnsureMaterial()
    {
        if (settings.shader == null)
            settings.shader = Shader.Find("Hidden/SkyPrison/OrthographicDistanceBlur");

        if (settings.shader == null)
            return;

        if (material == null || material.shader != settings.shader)
        {
            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(settings.shader);
        }
    }

    public override void Create()
    {
        EnsureMaterial();
        pass = new DistanceBlurPass();

        // Create 是否被调用、材质有没有建起来 —— 这两点决定后面所有分支是否有意义。
        Debug.Log($"[BlurGate] Create() 被调用。shader={(settings.shader != null ? settings.shader.name : "NULL")} " +
                  $"material={(material != null ? "已创建" : "NULL")}");
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 每帧自愈：不依赖 Create() 一定被调用过。见 EnsureMaterial 的注释。
        EnsureMaterial();

        // 诊断放在最开头，任何 early return 都拦不住它。
        // 上一版放在两个 return 之后，结果一条日志都没有 —— 而「从没被调用」和
        // 「被提前返回」需要完全不同的排查方向，那样的空结果什么也说明不了。
        if (Time.frameCount % 180 == 0)
        {
            Debug.Log($"[BlurGate] 进入 AddRenderPasses。相机={renderingData.cameraData.camera?.name} " +
                      $"类型={renderingData.cameraData.cameraType} " +
                      $"enabled={settings.enabled} material={(material != null ? "有" : "NULL")} " +
                      $"maxRadius={settings.maxRadius} intensity={settings.intensity}");
        }

        if (!settings.enabled || material == null || settings.maxRadius <= 0.001f || settings.intensity <= 0.001f)
            return;

        Camera camera = renderingData.cameraData.camera;
        CameraType cameraType = renderingData.cameraData.cameraType;

        if (cameraType != CameraType.Game && !(settings.applyToSceneView && cameraType == CameraType.SceneView))
            return;

        bool shouldRun = false;
        RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        switch (settings.targetMode)
        {
            case BlurTargetMode.World3DOnlyBeforeOverlays:
                // 只看 renderType==Base 不够——项目里给 HUD 图标合成用的
                // __SkyPrisonHUDModuleRTCamera_V87_* 系列相机也是 Base 类型
                // （它们各自独立渲染到自己的 RT，不属于任何相机栈，renderType 默认
                // 就是 Base）。不收紧这条判断，这些 RT 相机会一起被排进模糊 pass，
                // 结果是 HUD 图标（血条、快捷栏）也被糊——[BlurGate] 日志已经实测
                // 到这些相机会走进这个分支。
                //
                // 用名字精确匹配主世界相机，不靠"是不是 Base"这种间接推断。
                shouldRun = renderingData.cameraData.renderType == CameraRenderType.Base
                            && camera != null
                            && string.Equals(camera.name, "Main Camera", System.StringComparison.Ordinal);
                passEvent = RenderPassEvent.AfterRenderingPostProcessing;
                break;

            case BlurTargetMode.FullStackAfterNamedOverlay:
                shouldRun = renderingData.cameraData.renderType == CameraRenderType.Overlay
                            && camera != null
                            && string.Equals(camera.name, settings.afterOverlayCameraName, System.StringComparison.Ordinal);
                passEvent = RenderPassEvent.AfterRenderingPostProcessing;
                break;

            case BlurTargetMode.BaseCameraFinalAfterStack:
                shouldRun = renderingData.cameraData.renderType == CameraRenderType.Base;
                passEvent = RenderPassEvent.AfterRenderingPostProcessing;
                break;
        }

        // 诊断：这个 Feature 至今没在游戏里生效过，而失败可能发生在四个互不相同的地方
        // （材质没建 / 相机类型不匹配 / shouldRun 为假 / RecordRenderGraph 里提前返回），
        // 它们在画面上表现完全一样，都是「没有模糊」。逐个报出来。
        if (Time.frameCount % 180 == 0)
        {
            Debug.Log($"[BlurGate] 相机={camera?.name} 类型={renderingData.cameraData.renderType} " +
                      $"targetMode={settings.targetMode} shouldRun={shouldRun} " +
                      $"maskMode={settings.maskMode} intensity={settings.intensity} " +
                      $"maxRadius={settings.maxRadius}");
        }

        if (!shouldRun)
            return;

        pass.renderPassEvent = passEvent;
        pass.Setup(material, settings);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        pass?.Dispose();
    }

    private sealed class DistanceBlurPass : ScriptableRenderPass
    {
        private static readonly int FocusDistanceId = Shader.PropertyToID("_SkyPrisonFocusDistance");
        private static readonly int BlurRangeId = Shader.PropertyToID("_SkyPrisonBlurRange");
        private static readonly int MaxRadiusId = Shader.PropertyToID("_SkyPrisonMaxRadius");
        private static readonly int IntensityId = Shader.PropertyToID("_SkyPrisonIntensity");
        private static readonly int MaskModeId = Shader.PropertyToID("_SkyPrisonMaskMode");
        private static readonly int ScreenBlurStartYId = Shader.PropertyToID("_SkyPrisonScreenBlurStartY");
        private static readonly int ScreenBlurEndYId = Shader.PropertyToID("_SkyPrisonScreenBlurEndY");
        private static readonly int ScreenFocusYId = Shader.PropertyToID("_SkyPrisonScreenFocusY");
        private static readonly int ScreenClearHalfHeightId = Shader.PropertyToID("_SkyPrisonScreenClearHalfHeight");
        private static readonly int ScreenFocusFadeRangeId = Shader.PropertyToID("_SkyPrisonScreenFocusFadeRange");
        private static readonly int DebugShowMaskId = Shader.PropertyToID("_SkyPrisonDebugShowMask");
        private static readonly int HalfBlurRadiusScaleId = Shader.PropertyToID("_SkyPrisonHalfBlurRadiusScale");
        private static readonly int QuarterBlurRadiusScaleId = Shader.PropertyToID("_SkyPrisonQuarterBlurRadiusScale");
        private static readonly int BlurHalfTexId = Shader.PropertyToID("_SkyPrisonBlurHalfTex");
        private static readonly int BlurQuarterTexId = Shader.PropertyToID("_SkyPrisonBlurQuarterTex");

        private readonly ProfilingSampler profilingSampler = new ProfilingSampler("SkyPrison True Gaussian Distance Blur");

        private RTHandle halfA;
        private RTHandle halfB;
        private RTHandle quarterA;
        private RTHandle quarterB;
        private RTHandle compositeTemp;

        private Material material;
        private Settings settings;

        public void Setup(Material material, Settings settings)
        {
            this.material = material;
            this.settings = settings;

            if (settings.maskMode == BlurMaskMode.ScreenY || settings.maskMode == BlurMaskMode.ScreenFocusBand)
                ConfigureInput(ScriptableRenderPassInput.Color);
            else
                ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        }

        private void PushMaterialParameters()
        {
            if (material == null || settings == null)
                return;

            float focus = Mathf.Max(0.01f, settings.focusDistance);
            float range = Mathf.Max(0.01f, settings.blurRange);
            float radius = Mathf.Max(0f, settings.maxRadius);
            float intensity = Mathf.Clamp01(settings.intensity);
            float startY = Mathf.Clamp01(settings.screenBlurStartY);
            float endY = Mathf.Clamp(settings.screenBlurEndY, startY + 0.01f, 1f);
            float focusY = Mathf.Clamp01(settings.screenFocusY);
            float clearHalfHeight = Mathf.Clamp(settings.screenClearHalfHeight, 0f, 0.5f);
            float fadeRange = Mathf.Max(0.01f, settings.screenFocusFadeRange);

            material.SetFloat(FocusDistanceId, focus);
            material.SetFloat(BlurRangeId, range);
            material.SetFloat(MaxRadiusId, radius);
            material.SetFloat(IntensityId, intensity);
            material.SetFloat(MaskModeId, (float)settings.maskMode);
            material.SetFloat(ScreenBlurStartYId, startY);
            material.SetFloat(ScreenBlurEndYId, endY);
            material.SetFloat(ScreenFocusYId, focusY);
            material.SetFloat(ScreenClearHalfHeightId, clearHalfHeight);
            material.SetFloat(ScreenFocusFadeRangeId, fadeRange);
            material.SetFloat(DebugShowMaskId, settings.debugShowBlurMask ? 1f : 0f);
            material.SetFloat(HalfBlurRadiusScaleId, Mathf.Max(0.01f, settings.halfBlurRadiusScale));
            material.SetFloat(QuarterBlurRadiusScaleId, Mathf.Max(0.01f, settings.quarterBlurRadiusScale));
        }

#if UNITY_6000_0_OR_NEWER
        private sealed class BlitPassData
        {
            public TextureHandle source;
            public Material material;
            public int passIndex;
        }

        private sealed class CompositePassData
        {
            public TextureHandle original;
            public TextureHandle blurHalf;
            public TextureHandle blurQuarter;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null || settings == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData == null || resourceData.isActiveTargetBackBuffer)
            {
                // 直接渲染到后备缓冲时这个 pass 无法工作。这是最容易被忽略的一条：
                // 它静默返回，画面上和「pass 压根没排队」一模一样。
                if (Time.frameCount % 180 == 0)
                    Debug.LogWarning("[BlurGate] RecordRenderGraph 提前返回：目标是后备缓冲" +
                                     "（渲染器的 Intermediate Texture 需要设为 Always）");
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                if (Time.frameCount % 180 == 0)
                    Debug.LogWarning("[BlurGate] RecordRenderGraph 提前返回：activeColorTexture 无效");
                return;
            }

            if (Time.frameCount % 180 == 0)
                Debug.Log("[BlurGate] RecordRenderGraph 正常执行，已排入模糊 pass");

            PushMaterialParameters();

            TextureDesc fullDesc = source.GetDescriptor(renderGraph);
            fullDesc.clearBuffer = false;
            fullDesc.depthBufferBits = DepthBits.None;

            TextureDesc halfDesc = fullDesc;
            halfDesc.name = "_SkyPrisonGaussianHalfA";
            halfDesc.width = Mathf.Max(1, halfDesc.width / 2);
            halfDesc.height = Mathf.Max(1, halfDesc.height / 2);
            // 中间纹理精度提到 16-bit 浮点，不再继承屏幕的 8-bit 颜色格式。
            //
            // 之前只加了抖动去打散量化台阶，但抖动只是"掩盖"，源头没变——8-bit 在
            // 大面积低对比度渐变（阴天、雾气）上台阶间距依然肉眼可见，叠加两级
            // 下采样后更明显，看起来像马赛克。改成 16-bit 浮点，色阶间距细到不可见，
            // 不需要再靠抖动掩盖（抖动代码保留，双保险，不冲突）。
            halfDesc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

            TextureDesc quarterDesc = fullDesc;
            quarterDesc.name = "_SkyPrisonGaussianQuarterA";
            quarterDesc.width = Mathf.Max(1, quarterDesc.width / 4);
            quarterDesc.height = Mathf.Max(1, quarterDesc.height / 4);
            quarterDesc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

            if (settings.debugShowBlurMask)
            {
                fullDesc.name = "_SkyPrisonDistanceBlurDebugMask";
                TextureHandle debugTemp = renderGraph.CreateTexture(fullDesc);
                AddBlit(renderGraph, source, debugTemp, material, 6, "SkyPrison Gaussian Debug Mask");
                AddBlit(renderGraph, debugTemp, source, material, 7, "SkyPrison Gaussian Debug Copy Back");
                return;
            }

            TextureHandle halfAHandle = renderGraph.CreateTexture(halfDesc);
            halfDesc.name = "_SkyPrisonGaussianHalfB";
            TextureHandle halfBHandle = renderGraph.CreateTexture(halfDesc);

            TextureHandle quarterAHandle = renderGraph.CreateTexture(quarterDesc);
            quarterDesc.name = "_SkyPrisonGaussianQuarterB";
            TextureHandle quarterBHandle = renderGraph.CreateTexture(quarterDesc);

            fullDesc.name = "_SkyPrisonGaussianCompositeTemp";
            TextureHandle composite = renderGraph.CreateTexture(fullDesc);

            AddBlit(renderGraph, source, halfAHandle, material, 0, "SkyPrison Downsample To Half");
            AddBlit(renderGraph, halfAHandle, halfBHandle, material, 1, "SkyPrison Half Gaussian Horizontal");
            AddBlit(renderGraph, halfBHandle, halfAHandle, material, 2, "SkyPrison Half Gaussian Vertical");

            // 模糊两遍，抹掉稀疏采样在硬边缘上产生的梳齿纹。
            //
            // 13 个采样点、半径 20 个像素，点间距约 3.33 个纹素——两个采样点之间的
            // 纹素完全没被平均到。平坦区域看不出来，但叉车轮廓这种高对比度边缘上
            // 会出现规律的竖直细纹（用户截图里那种），换成 16-bit 纹理精度对此毫无
            // 帮助（已实测排除），因为这根本不是精度问题，是欠采样。
            //
            // 把同一个稀疏核卷积它自己一次，能把采样点间的空隙相互填平，数学上
            // 显著抹平梳齿而不改变核的形状/权重，是处理这类伪影的标准做法。
            AddBlit(renderGraph, halfAHandle, halfBHandle, material, 1, "SkyPrison Half Gaussian Horizontal 2");
            AddBlit(renderGraph, halfBHandle, halfAHandle, material, 2, "SkyPrison Half Gaussian Vertical 2");

            AddBlit(renderGraph, halfAHandle, quarterAHandle, material, 0, "SkyPrison Downsample To Quarter");
            AddBlit(renderGraph, quarterAHandle, quarterBHandle, material, 3, "SkyPrison Quarter Gaussian Horizontal");
            AddBlit(renderGraph, quarterBHandle, quarterAHandle, material, 4, "SkyPrison Quarter Gaussian Vertical");

            AddBlit(renderGraph, quarterAHandle, quarterBHandle, material, 3, "SkyPrison Quarter Gaussian Horizontal 2");
            AddBlit(renderGraph, quarterBHandle, quarterAHandle, material, 4, "SkyPrison Quarter Gaussian Vertical 2");

            AddComposite(renderGraph, source, halfAHandle, quarterAHandle, composite, material, "SkyPrison Gaussian Pyramid Composite");
            AddBlit(renderGraph, composite, source, material, 7, "SkyPrison Gaussian Pyramid Copy Back");
        }

        private void AddBlit(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, Material mat, int passIndex, string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>(passName, out var passData, profilingSampler))
            {
                passData.source = source;
                passData.material = mat;
                passData.passIndex = passIndex;

                builder.UseTexture(passData.source);
                builder.SetRenderAttachment(destination, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BlitPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        data.passIndex);
                });
            }
        }

        private void AddComposite(RenderGraph renderGraph, TextureHandle original, TextureHandle halfBlur, TextureHandle quarterBlur, TextureHandle destination, Material mat, string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(passName, out var passData, profilingSampler))
            {
                passData.original = original;
                passData.blurHalf = halfBlur;
                passData.blurQuarter = quarterBlur;
                passData.material = mat;

                builder.UseTexture(passData.original);
                builder.UseTexture(passData.blurHalf);
                builder.UseTexture(passData.blurQuarter);
                builder.SetRenderAttachment(destination, 0);
                builder.AllowPassCulling(false);
                // RenderGraph forbids SetGlobalTexture unless the pass explicitly declares
                // that it will modify global state. The composite shader reads the two
                // gaussian pyramid textures through global texture ids, so this pass must
                // opt in.
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(BlurHalfTexId, data.blurHalf);
                    context.cmd.SetGlobalTexture(BlurQuarterTexId, data.blurQuarter);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.original,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        5);
                });
            }
        }
#endif

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor fullDesc = renderingData.cameraData.cameraTargetDescriptor;
            fullDesc.depthBufferBits = 0;
            fullDesc.msaaSamples = 1;

            RenderTextureDescriptor halfDesc = fullDesc;
            halfDesc.width = Mathf.Max(1, halfDesc.width / 2);
            halfDesc.height = Mathf.Max(1, halfDesc.height / 2);

            RenderTextureDescriptor quarterDesc = fullDesc;
            quarterDesc.width = Mathf.Max(1, quarterDesc.width / 4);
            quarterDesc.height = Mathf.Max(1, quarterDesc.height / 4);

            RenderingUtils.ReAllocateIfNeeded(ref halfA, halfDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SkyPrisonGaussianHalfA");
            RenderingUtils.ReAllocateIfNeeded(ref halfB, halfDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SkyPrisonGaussianHalfB");
            RenderingUtils.ReAllocateIfNeeded(ref quarterA, quarterDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SkyPrisonGaussianQuarterA");
            RenderingUtils.ReAllocateIfNeeded(ref quarterB, quarterDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SkyPrisonGaussianQuarterB");
            RenderingUtils.ReAllocateIfNeeded(ref compositeTemp, fullDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SkyPrisonGaussianCompositeTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || settings == null || halfA == null || halfB == null || quarterA == null || quarterB == null || compositeTemp == null)
                return;

            RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (source == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("SkyPrison True Gaussian Distance Blur");
            using (new ProfilingScope(cmd, profilingSampler))
            {
                PushMaterialParameters();

                if (settings.debugShowBlurMask)
                {
                    Blitter.BlitCameraTexture(cmd, source, compositeTemp, material, 6);
                    Blitter.BlitCameraTexture(cmd, compositeTemp, source, material, 7);
                }
                else
                {
                    Blitter.BlitCameraTexture(cmd, source, halfA, material, 0);
                    Blitter.BlitCameraTexture(cmd, halfA, halfB, material, 1);
                    Blitter.BlitCameraTexture(cmd, halfB, halfA, material, 2);

                    Blitter.BlitCameraTexture(cmd, halfA, quarterA, material, 0);
                    Blitter.BlitCameraTexture(cmd, quarterA, quarterB, material, 3);
                    Blitter.BlitCameraTexture(cmd, quarterB, quarterA, material, 4);

                    cmd.SetGlobalTexture(BlurHalfTexId, halfA);
                    cmd.SetGlobalTexture(BlurQuarterTexId, quarterA);
                    Blitter.BlitCameraTexture(cmd, source, compositeTemp, material, 5);
                    Blitter.BlitCameraTexture(cmd, compositeTemp, source, material, 7);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            halfA?.Release();
            halfB?.Release();
            quarterA?.Release();
            quarterB?.Release();
            compositeTemp?.Release();
            halfA = null;
            halfB = null;
            quarterA = null;
            quarterB = null;
            compositeTemp = null;
        }
    }
}
