// SkyPrisonMainCameraShadowOccluderStencilFeature_V1.cs
// 2026-06-06
// Purpose:
//   Pixel-accurate stage/contact shadow suppression independent from character occlusion state.
//   Foreground terrain-decoration visuals write stencil Ref=1 from the Main Camera view.
//   Stage shadow shader then uses Stencil Comp NotEqual Ref=1, so shadows never darken those pixels.
//
// This feature does NOT drive character occlusion. It only prepares stencil for shadow projectors.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class SkyPrisonMainCameraShadowOccluderStencilFeature_V1 : ScriptableRendererFeature
{
    [Serializable]
    public sealed class Settings
    {
        [Header("Version")]
        public string scriptVersion = "V1 - 2026-06-06 - shadow occluder stencil prepass";

        [Header("Camera Filter")]
        public bool onlyMainCamera = true;
        public string requiredCameraName = "Main Camera";

        [Header("Pass")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;

        [Header("Stencil")]
        [Range(0, 255)] public int stencilRef = 1;
        [Range(0, 255)] public int stencilWriteMask = 255;
        [Tooltip("2026-07-21: Always is required, not just a fallback. LEqual compares against the CURRENT depth buffer, " +
            "so a unit standing nearer to camera than the occluder (e.g. a jumping character rising above a container) " +
            "defeats the stencil write for that screen region — the occluder simply fails to register there for that " +
            "frame, and any ground shadow behind it (which must render with ZTest Always due to this project's fake " +
            "2.5D depth) draws unclipped. Occluder registration must be unconditional and independent of what unit is " +
            "currently overlapping it on screen.")]
        public CompareFunction zTest = CompareFunction.Always;

        [Header("Renderer Source")]
        [Tooltip("ON = collect real VisualRoot renderers from SkyPrisonTerrainDecorationFrontOccluderTrigger.occluderContentRoot / child named VisualRoot.")]
        public bool collectDecorationVisualRoots = true;
        public string visualRootName = "VisualRoot";

        [Tooltip("ON = if no visual roots are found, collect renderers from this layer.")]
        public bool fallbackToLayer = true;
        public string fallbackLayerName = "OcclusionMask";

        public bool requireRendererEnabled = true;
        public bool requireActiveInHierarchy = true;

        [Tooltip("Reject renderers whose full hierarchy path contains any of these keywords. Semicolon separated, lower/upper insensitive.")]
        public string rejectPathKeywords = "outline;shadow;canvas;ui;character;player;enemy;ally;item;trigger;collider;collision;frontoccluder;occluder;proxy;gizmo;debug";

        [Header("Shader")]
        public Shader stencilWriterShader;
        public string stencilWriterShaderName = "Hidden/SkyPrison/ShadowOccluderStencilWriter";

        [Header("Debug")]
        public bool debugLogs = false;
        [TextArea(2, 4)] public string lastStatus = "-";
    }

    public Settings settings = new Settings();

    private Material stencilWriterMaterial;
    private ShadowOccluderStencilPass pass;
    private readonly List<Renderer> renderers = new List<Renderer>(256);

    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int StencilWriteMaskId = Shader.PropertyToID("_StencilWriteMask");
    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");

    public override void Create()
    {
        EnsureMaterial();
        pass = new ShadowOccluderStencilPass(settings, () => stencilWriterMaterial, () => renderers.ToArray());
        pass.renderPassEvent = settings.passEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (!ShouldRun(camera, renderingData.cameraData.cameraType))
        {
            settings.lastStatus = "SKIP camera=" + SafeName(camera) + ", type=" + renderingData.cameraData.cameraType;
            return;
        }

        EnsureMaterial();
        if (stencilWriterMaterial == null)
        {
            settings.lastStatus = "SKIP missing stencil writer shader=" + settings.stencilWriterShaderName;
            return;
        }

        CollectRenderers();
        if (pass == null)
            pass = new ShadowOccluderStencilPass(settings, () => stencilWriterMaterial, () => renderers.ToArray());

        pass.renderPassEvent = settings.passEvent;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (stencilWriterMaterial != null)
        {
            CoreUtils.Destroy(stencilWriterMaterial);
            stencilWriterMaterial = null;
        }
    }

    private bool ShouldRun(Camera camera, CameraType type)
    {
        if (camera == null)
            return false;
        if (type != CameraType.Game)
            return false;

        // HUD module post-process cameras must never run main-camera shadow/stencil passes.
        // Do not reject every targetTexture camera here: some builds/pipelines can legitimately
        // render the main camera to an RT.  Only skip cameras explicitly tagged/named as HUD internals.
        if (IsHUDInternalRenderCamera(camera))
            return false;

        if (!settings.onlyMainCamera)
            return true;
        if (camera.CompareTag("MainCamera"))
            return true;
        if (!string.IsNullOrWhiteSpace(settings.requiredCameraName) && camera.name == settings.requiredCameraName)
            return true;
        return false;
    }

    private static bool IsHUDInternalRenderCamera(Camera camera)
    {
        if (camera == null)
            return false;

        if (camera.GetComponent<SkyPrison.Runtime.UI.SkyPrisonHUDInternalRenderCameraTag>() != null)
            return true;

        string cameraName = camera.name;
        if (!string.IsNullOrEmpty(cameraName) && cameraName.StartsWith("__SkyPrisonHUDModuleRTCamera"))
            return true;

        RenderTexture rt = camera.targetTexture;
        if (rt != null && !string.IsNullOrEmpty(rt.name) && rt.name.StartsWith("RT_HUDModule_"))
            return true;

        return false;
    }

    private void EnsureMaterial()
    {
        if (stencilWriterMaterial != null)
            return;
        Shader shader = settings.stencilWriterShader != null ? settings.stencilWriterShader : Shader.Find(settings.stencilWriterShaderName);
        if (shader != null)
            stencilWriterMaterial = CoreUtils.CreateEngineMaterial(shader);
    }

    private void CollectRenderers()
    {
        renderers.Clear();
        int visualRoots = 0;
        int visualRenderers = 0;
        int fallbackRenderers = 0;

        if (settings.collectDecorationVisualRoots)
            visualRenderers = CollectFromDecorationVisualRoots(out visualRoots);

        if (visualRenderers <= 0 && settings.fallbackToLayer)
            fallbackRenderers = CollectFromLayerFallback();

        settings.lastStatus = "OK shadow stencil collect visualRoots=" + visualRoots +
                              ", visualRenderers=" + visualRenderers +
                              ", fallbackRenderers=" + fallbackRenderers +
                              ", total=" + renderers.Count;
    }

    private int CollectFromDecorationVisualRoots(out int visualRootCount)
    {
        visualRootCount = 0;
        int added = 0;
        string[] rejects = SplitKeywords(settings.rejectPathKeywords);

#if UNITY_2023_1_OR_NEWER
        SkyPrisonTerrainDecorationFrontOccluderTrigger[] triggers = UnityEngine.Object.FindObjectsByType<SkyPrisonTerrainDecorationFrontOccluderTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        SkyPrisonTerrainDecorationFrontOccluderTrigger[] triggers = Resources.FindObjectsOfTypeAll<SkyPrisonTerrainDecorationFrontOccluderTrigger>();
#endif
        if (triggers == null)
            return 0;

        HashSet<int> visitedRoots = new HashSet<int>();

        for (int i = 0; i < triggers.Length; i++)
        {
            SkyPrisonTerrainDecorationFrontOccluderTrigger trigger = triggers[i];
            if (trigger == null || trigger.gameObject == null)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying && EditorUtility.IsPersistent(trigger.gameObject))
                continue;
#endif
            Transform visualRoot = trigger.occluderContentRoot;
            if (visualRoot == null && !string.IsNullOrWhiteSpace(settings.visualRootName))
                visualRoot = FindNearestNamedChildOrSelf(trigger.transform, settings.visualRootName);
            if (visualRoot == null)
                continue;

            int rootId = visualRoot.GetInstanceID();
            if (!visitedRoots.Add(rootId))
                continue;
            visualRootCount++;

            Renderer[] rs = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (rs == null)
                continue;

            for (int r = 0; r < rs.Length; r++)
            {
                Renderer renderer = rs[r];
                if (IsValidRenderer(renderer, rejects))
                {
                    renderers.Add(renderer);
                    added++;
                }
            }
        }

        return added;
    }

    private int CollectFromLayerFallback()
    {
        int added = 0;
        string[] rejects = SplitKeywords(settings.rejectPathKeywords);
        int layer = LayerMask.NameToLayer(settings.fallbackLayerName);
        if (layer < 0)
            return 0;

#if UNITY_2023_1_OR_NEWER
        Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
#endif
        if (all == null)
            return 0;

        for (int i = 0; i < all.Length; i++)
        {
            Renderer renderer = all[i];
            if (renderer == null || renderer.gameObject == null)
                continue;
            if (renderer.gameObject.layer != layer)
                continue;
            if (IsValidRenderer(renderer, rejects))
            {
                renderers.Add(renderer);
                added++;
            }
        }
        return added;
    }

    private bool IsValidRenderer(Renderer renderer, string[] rejects)
    {
        if (renderer == null || renderer.gameObject == null)
            return false;
#if UNITY_EDITOR
        if (!Application.isPlaying && EditorUtility.IsPersistent(renderer.gameObject))
            return false;
#endif
        if (settings.requireRendererEnabled && !renderer.enabled)
            return false;
        if (settings.requireActiveInHierarchy && !renderer.gameObject.activeInHierarchy)
            return false;
        string path = GetPath(renderer.transform);
        if (ContainsAny(path, rejects))
            return false;
        if (!renderers.Contains(renderer))
            return true;
        return false;
    }

    private static Transform FindNearestNamedChildOrSelf(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return null;
        if (root.name == name)
            return root;
        Transform direct = root.Find(name);
        if (direct != null)
            return direct;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name)
                return all[i];
        }
        return null;
    }

    private static string[] SplitKeywords(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        string[] parts = raw.Split(new[] { ';', ',', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i].Trim().ToLowerInvariant();
        return parts;
    }

    private static bool ContainsAny(string text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text) || keywords == null)
            return false;
        string lower = text.ToLowerInvariant();
        for (int i = 0; i < keywords.Length; i++)
        {
            string k = keywords[i];
            if (!string.IsNullOrEmpty(k) && lower.Contains(k))
                return true;
        }
        return false;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "";
        List<string> parts = new List<string>(8);
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string SafeName(UnityEngine.Object o)
    {
        return o != null ? o.name : "null";
    }

    private sealed class ShadowOccluderStencilPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly Func<Material> materialGetter;
        private readonly Func<Renderer[]> renderersGetter;

        private sealed class PassData
        {
            public Settings settings;
            public Material material;
            public Camera camera;
            public Renderer[] renderers;
        }

        public ShadowOccluderStencilPass(Settings settings, Func<Material> materialGetter, Func<Renderer[]> renderersGetter)
        {
            this.settings = settings;
            this.materialGetter = materialGetter;
            this.renderersGetter = renderersGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = materialGetter();
            if (mat == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Shadow Occluder Stencil Prepass", out PassData passData))
            {
                passData.settings = settings;
                passData.material = mat;
                passData.camera = cameraData.camera;
                passData.renderers = renderersGetter();

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    if (data.material == null)
                        return;

                    data.material.SetFloat(StencilRefId, Mathf.Clamp(data.settings.stencilRef, 0, 255));
                    data.material.SetFloat(StencilWriteMaskId, Mathf.Clamp(data.settings.stencilWriteMask, 0, 255));
                    data.material.SetFloat(ZTestId, (float)data.settings.zTest);

                    int drawCalls = 0;
                    int rendererCount = 0;
                    Renderer[] rs = data.renderers;
                    if (rs != null)
                    {
                        for (int i = 0; i < rs.Length; i++)
                        {
                            Renderer r = rs[i];
                            if (r == null || r.gameObject == null)
                                continue;
                            if (data.settings.requireRendererEnabled && !r.enabled)
                                continue;
                            if (data.settings.requireActiveInHierarchy && !r.gameObject.activeInHierarchy)
                                continue;

                            int subCount = 1;
                            if (r.sharedMaterials != null && r.sharedMaterials.Length > 0)
                                subCount = r.sharedMaterials.Length;

                            bool drawnRenderer = false;
                            for (int sub = 0; sub < subCount; sub++)
                            {
                                context.cmd.DrawRenderer(r, data.material, sub, 0);
                                drawCalls++;
                                drawnRenderer = true;
                            }
                            if (drawnRenderer)
                                rendererCount++;
                        }
                    }

                    data.settings.lastStatus = "OK shadow stencil camera=" + SafeName(data.camera) +
                                               ", renderers=" + rendererCount +
                                               ", drawCalls=" + drawCalls +
                                               ", stencilRef=" + data.settings.stencilRef +
                                               ", zTest=" + data.settings.zTest;
                });
            }
        }
    }
}
