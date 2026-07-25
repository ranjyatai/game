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

    public override void Create()
    {
        if (settings.shader == null)
            settings.shader = Shader.Find("Hidden/SkyPrison/OrthographicDistanceBlur");

        if (settings.shader != null)
        {
            if (material == null || material.shader != settings.shader)
            {
                CoreUtils.Destroy(material);
                material = CoreUtils.CreateEngineMaterial(settings.shader);
            }
        }

        pass = new DistanceBlurPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
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
                shouldRun = renderingData.cameraData.renderType == CameraRenderType.Base;
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
                return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
                return;

            PushMaterialParameters();

            TextureDesc fullDesc = source.GetDescriptor(renderGraph);
            fullDesc.clearBuffer = false;
            fullDesc.depthBufferBits = DepthBits.None;

            TextureDesc halfDesc = fullDesc;
            halfDesc.name = "_SkyPrisonGaussianHalfA";
            halfDesc.width = Mathf.Max(1, halfDesc.width / 2);
            halfDesc.height = Mathf.Max(1, halfDesc.height / 2);

            TextureDesc quarterDesc = fullDesc;
            quarterDesc.name = "_SkyPrisonGaussianQuarterA";
            quarterDesc.width = Mathf.Max(1, quarterDesc.width / 4);
            quarterDesc.height = Mathf.Max(1, quarterDesc.height / 4);

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

            AddBlit(renderGraph, halfAHandle, quarterAHandle, material, 0, "SkyPrison Downsample To Quarter");
            AddBlit(renderGraph, quarterAHandle, quarterBHandle, material, 3, "SkyPrison Quarter Gaussian Horizontal");
            AddBlit(renderGraph, quarterBHandle, quarterAHandle, material, 4, "SkyPrison Quarter Gaussian Vertical");

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
