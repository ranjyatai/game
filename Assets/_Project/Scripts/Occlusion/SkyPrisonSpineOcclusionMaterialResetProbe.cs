// SkyPrisonSpineOcclusionMaterialResetProbe_V1.cs
// 2026-06-05
// Diagnostic RendererFeature only.
// Purpose: verify whether Spine/SpineOcclusionComposite is still being clipped by stale _OcclusionTex / mask settings.
// This feature does NOT render outlines and does NOT touch legacy RT_Outline_*.
// It overrides Spine occlusion material properties through MaterialPropertyBlock every frame.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class SkyPrisonSpineOcclusionMaterialResetProbeV1 : ScriptableRendererFeature
{
    [Serializable]
    public sealed class Settings
    {
        [Header("Version")]
        public string scriptVersion = "V1 - 2026-06-05 - force clear Spine occlusion material probe";

        [Header("Camera Filter")]
        public bool onlyMainCamera = true;
        public string requiredCameraName = "Main Camera";

        [Header("Pass")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;

        [Header("Target")]
        public string spineOcclusionShaderName = "Spine/SpineOcclusionComposite";
        public bool requireRendererEnabled = true;
        public bool requireActiveInHierarchy = true;
        public string rejectPathKeywords = "outline;shadow;canvas;ui";

        [Header("Probe Override")]
        public bool forceBlackOcclusionTex = true;
        public bool forceMaskSamplingDefaults = true;
        public bool forceNoOccludedBody = true;
        public bool forceTintAlphaOne = false;

        [Header("Mask Defaults")]
        [Range(0f, 1f)] public float maskThreshold = 0.5f;
        [Range(0.0001f, 0.5f)] public float maskSoftness = 0.001f;
        [Range(0f, 4f)] public float maskDilatePixels = 0f;

        [Header("Debug - read only")]
        [TextArea(2, 6)] public string lastStatus = "-";
    }

    public Settings settings = new Settings();

    private ResetPass resetPass;
    private readonly List<Renderer> renderers = new List<Renderer>(64);

    private static readonly int OcclusionTexId = Shader.PropertyToID("_OcclusionTex");
    private static readonly int FlipMaskYId = Shader.PropertyToID("_FlipMaskY");
    private static readonly int SampleBothYId = Shader.PropertyToID("_SampleBothY");
    private static readonly int MaskThresholdId = Shader.PropertyToID("_MaskThreshold");
    private static readonly int MaskSoftnessId = Shader.PropertyToID("_MaskSoftness");
    private static readonly int MaskDilatePixelsId = Shader.PropertyToID("_MaskDilatePixels");
    private static readonly int OccludedAlphaId = Shader.PropertyToID("_OccludedAlpha");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");

    public override void Create()
    {
        resetPass = new ResetPass(settings, () => renderers.ToArray());
        resetPass.renderPassEvent = settings.passEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (!ShouldRunForCamera(camera, renderingData.cameraData.cameraType))
        {
            settings.lastStatus = "SKIP camera=" + SafeName(camera) + ", type=" + renderingData.cameraData.cameraType;
            return;
        }

        CollectSpineRenderers();
        if (resetPass == null)
            resetPass = new ResetPass(settings, () => renderers.ToArray());

        resetPass.renderPassEvent = settings.passEvent;
        renderer.EnqueuePass(resetPass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        renderers.Clear();
    }

    private bool ShouldRunForCamera(Camera camera, CameraType cameraType)
    {
        if (camera == null)
            return false;
        if (cameraType != CameraType.Game)
            return false;
        if (!settings.onlyMainCamera)
            return true;
        return string.Equals(camera.name, settings.requiredCameraName, StringComparison.OrdinalIgnoreCase);
    }

    private void CollectSpineRenderers()
    {
        renderers.Clear();
        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
        if (all == null)
            return;

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null)
                continue;
            if (settings.requireRendererEnabled && !r.enabled)
                continue;
            if (settings.requireActiveInHierarchy && (r.gameObject == null || !r.gameObject.activeInHierarchy))
                continue;

            string path = GetPath(r.transform);
            if (ContainsAny(path, settings.rejectPathKeywords))
                continue;

            Material[] mats = r.sharedMaterials;
            if (mats == null)
                continue;

            bool matched = false;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat != null && mat.shader != null && mat.shader.name == settings.spineOcclusionShaderName)
                {
                    matched = true;
                    break;
                }
            }

            if (matched)
                renderers.Add(r);
        }
    }

    private sealed class ResetPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly Func<Renderer[]> rendererProvider;
        private sealed class PassData
        {
            public Settings settings;
            public Renderer[] renderers;
        }

        public ResetPass(Settings settings, Func<Renderer[]> rendererProvider)
        {
            this.settings = settings;
            this.rendererProvider = rendererProvider;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Reset Spine Occlusion Material Probe V1", out PassData passData))
            {
                passData.settings = settings;
                passData.renderers = rendererProvider != null ? rendererProvider() : null;
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    int rendererCount = 0;
                    int slotCount = 0;
                    if (data.renderers != null)
                    {
                        for (int i = 0; i < data.renderers.Length; i++)
                        {
                            Renderer r = data.renderers[i];
                            if (r == null || !r.enabled || r.gameObject == null || !r.gameObject.activeInHierarchy)
                                continue;

                            Material[] mats = r.sharedMaterials;
                            if (mats == null)
                                continue;

                            bool touched = false;
                            for (int m = 0; m < mats.Length; m++)
                            {
                                Material mat = mats[m];
                                if (mat == null || mat.shader == null || mat.shader.name != data.settings.spineOcclusionShaderName)
                                    continue;

                                MaterialPropertyBlock block = new MaterialPropertyBlock();
                                r.GetPropertyBlock(block, m);

                                if (data.settings.forceBlackOcclusionTex)
                                    block.SetTexture(OcclusionTexId, Texture2D.blackTexture);

                                if (data.settings.forceMaskSamplingDefaults)
                                {
                                    block.SetFloat(FlipMaskYId, 0f);
                                    block.SetFloat(SampleBothYId, 0f);
                                    block.SetFloat(MaskThresholdId, data.settings.maskThreshold);
                                    block.SetFloat(MaskSoftnessId, data.settings.maskSoftness);
                                    block.SetFloat(MaskDilatePixelsId, data.settings.maskDilatePixels);
                                }

                                if (data.settings.forceNoOccludedBody)
                                    block.SetFloat(OccludedAlphaId, 0f);

                                if (data.settings.forceTintAlphaOne)
                                    block.SetColor(TintColorId, Color.white);

                                r.SetPropertyBlock(block, m);
                                slotCount++;
                                touched = true;
                            }

                            if (touched)
                                rendererCount++;
                        }
                    }

                    data.settings.lastStatus = "OK reset probe: renderers=" + rendererCount
                        + ", materialSlots=" + slotCount
                        + ", blackOcclusionTex=" + data.settings.forceBlackOcclusionTex
                        + ", passEvent=" + data.settings.passEvent;
                });
            }
        }
    }

    private static bool ContainsAny(string value, string keywords)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(keywords))
            return false;
        string[] parts = keywords.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string k = parts[i].Trim();
            if (!string.IsNullOrEmpty(k) && value.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "null";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static string SafeName(UnityEngine.Object obj)
    {
        return obj == null ? "null" : obj.name;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Sky Prison/Rendering/Install Spine Occlusion Material Reset Probe V1")]
    private static void InstallFeatureMenu()
    {
        ScriptableRendererData[] renderers = Resources.FindObjectsOfTypeAll<ScriptableRendererData>();
        ScriptableRendererData target = null;
        for (int i = 0; i < renderers.Length; i++)
        {
            ScriptableRendererData r = renderers[i];
            if (r != null && r.name.IndexOf("UniversalRenderer3D", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                target = r;
                break;
            }
        }

        if (target == null)
        {
            EditorUtility.DisplayDialog("Sky Prison", "UniversalRenderer3D renderer data not found.", "OK");
            return;
        }

        SerializedObject so = new SerializedObject(target);
        SerializedProperty featuresProp = so.FindProperty("m_RendererFeatures");
        if (featuresProp == null || !featuresProp.isArray)
        {
            EditorUtility.DisplayDialog("Sky Prison", "Renderer feature list not found on UniversalRenderer3D.", "OK");
            return;
        }

        for (int i = 0; i < featuresProp.arraySize; i++)
        {
            UnityEngine.Object obj = featuresProp.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj is SkyPrisonSpineOcclusionMaterialResetProbeV1)
            {
                Selection.activeObject = target;
                EditorUtility.DisplayDialog("Sky Prison", "Spine Occlusion Material Reset Probe V1 is already installed.", "OK");
                return;
            }
        }

        SkyPrisonSpineOcclusionMaterialResetProbeV1 feature = CreateInstance<SkyPrisonSpineOcclusionMaterialResetProbeV1>();
        feature.name = "Sky Prison Spine Occlusion Material Reset Probe V1";
        AssetDatabase.AddObjectToAsset(feature, target);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));

        featuresProp.arraySize++;
        featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
        Selection.activeObject = target;
        EditorUtility.DisplayDialog("Sky Prison", "Installed Spine Occlusion Material Reset Probe V1. Use it only to test whether stale _OcclusionTex / mask parameters are hiding the visible Spine body.", "OK");
    }
#endif
}
