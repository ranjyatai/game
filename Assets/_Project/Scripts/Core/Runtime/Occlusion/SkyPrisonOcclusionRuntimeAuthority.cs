using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Sky Prison runtime occlusion binding authority.
///
/// V45 goals:
/// - Restore shadow/projector occlusion binding that was accidentally dropped in later Authority versions.
/// - Do NOT touch Spine materials, sharedMaterials, renderer.materials, or Spine CustomMaterialOverride.
/// - Authority is only a raw-mask texture binder for renderers whose current material already supports _OcclusionTex.
/// - This prevents fighting Spine/Skeleton material ownership while making Shadow_Projector and any occlusion-ready body material consume RT_OcclusionRaw_* again.
///
/// Class name intentionally remains SkyPrisonOcclusionRuntimeAuthority_V30 so existing scene/bootstrap references survive file replacement.
/// </summary>
[DefaultExecutionOrder(32700)]
public sealed class SkyPrisonOcclusionRuntimeAuthority_V30 : MonoBehaviour
{
    private const string Version = "V45 - 2026-05-22 - restore projector raw binding no material authority";

    private static SkyPrisonOcclusionRuntimeAuthority_V30 instance;

    [Header("Runtime Authority")]
    [SerializeField] private bool enableAuthority = true;
    [SerializeField] private bool bindEveryLateUpdate = false;
    [SerializeField] private bool bindBeforeCameraRendering = false;

    [Header("Warmup / Repair")]
    [SerializeField] private int warmupFrames = 12;
    [SerializeField] private bool enablePeriodicRepair = true;
    [SerializeField] private float periodicRepairInterval = 0.5f;

    [Header("Body Occlusion Parameters")]
    [SerializeField, Range(0f, 1f)] private float bodyOccludedAlpha = 0f;
    [SerializeField] private bool bodyUseHardDiscard = false;

    [Header("Projector Occlusion Parameters")]
    [SerializeField] private bool forceProjectorUseOcclusionMask = true;
    [SerializeField, Range(0f, 1f)] private float projectorInsideMaskAlpha = 0f;

    [Header("RenderTexture Names")]
    [SerializeField] private string playerRawMaskName = "RT_OcclusionRaw_Player_Runtime";
    [SerializeField] private string enemyRawMaskName = "RT_OcclusionRaw_Enemy_Runtime";
    [SerializeField] private string allyRawMaskName = "RT_OcclusionRaw_Ally_Runtime";
    [SerializeField] private string itemRawMaskName = "RT_OcclusionRaw_Item_Runtime";

    [Header("Diagnostics")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private int logEveryNFrames = 120;
    [SerializeField] private string lastSummary = string.Empty;
    [SerializeField] private int warmupFramesRemaining;
    [SerializeField] private int lastScannedRenderers;
    [SerializeField] private int lastBoundRenderers;
    [SerializeField] private int lastBoundProjectors;
    [SerializeField] private int lastBoundBodies;
    [SerializeField] private int lastSkippedNoOcclusionProperty;
    [SerializeField] private int lastMissingRawMask;

    private float nextPeriodicRepairTime;

    private readonly Dictionary<string, RenderTexture> rtCache = new Dictionary<string, RenderTexture>();
    private readonly Dictionary<Renderer, MaterialPropertyBlock> blockCache = new Dictionary<Renderer, MaterialPropertyBlock>();

    private static readonly int OcclusionTexId = Shader.PropertyToID("_OcclusionTex");
    private static readonly int OccludedAlphaId = Shader.PropertyToID("_OccludedAlpha");
    private static readonly int UseHardDiscardId = Shader.PropertyToID("_UseHardDiscard");
    private static readonly int UseOcclusionMaskId = Shader.PropertyToID("_UseOcclusionMask");
    private static readonly int InsideMaskAlphaId = Shader.PropertyToID("_InsideMaskAlpha");

    private enum UnitChannel
    {
        Unknown,
        Player,
        Enemy,
        Ally,
        Item
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        SkyPrisonOcclusionRuntimeAuthority_V30 existing = FindFirstObjectByType<SkyPrisonOcclusionRuntimeAuthority_V30>(FindObjectsInactive.Include);
        if (existing != null)
        {
            instance = existing;
            return;
        }

        GameObject go = new GameObject("__SkyPrisonOcclusionRuntimeAuthority_V45");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SkyPrisonOcclusionRuntimeAuthority_V30>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);

        RefreshRTCache();
        BeginWarmup("Awake");
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        SceneManager.sceneLoaded += OnSceneLoaded;
        BeginWarmup("OnEnable");
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        rtCache.Clear();
        blockCache.Clear();
        RefreshRTCache();
        BeginWarmup("SceneLoaded");
    }

    private void BeginWarmup(string reason)
    {
        warmupFramesRemaining = Mathf.Max(0, warmupFrames);
        nextPeriodicRepairTime = Time.unscaledTime + Mathf.Max(0.1f, periodicRepairInterval);

        if (debugLogs)
            Debug.Log("[SkyPrisonOcclusionRuntimeAuthority] " + Version + " begin warmup: " + reason, this);
    }

    private void LateUpdate()
    {
        if (!enableAuthority)
            return;

        if (bindEveryLateUpdate)
        {
            ApplyAuthority("LateUpdate legacy", false);
            return;
        }

        if (warmupFramesRemaining > 0)
        {
            warmupFramesRemaining--;
            ApplyAuthority("warmup:" + warmupFramesRemaining, true);
            return;
        }

        if (enablePeriodicRepair && Time.unscaledTime >= nextPeriodicRepairTime)
        {
            nextPeriodicRepairTime = Time.unscaledTime + Mathf.Max(0.1f, periodicRepairInterval);
            ApplyAuthority("periodic repair", true);
        }
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!enableAuthority || !bindBeforeCameraRendering || camera == null)
            return;

        if (camera.targetTexture == null)
            ApplyAuthority("beginCameraRendering legacy:" + camera.name, false);
    }

    [ContextMenu("Apply Authority Now")]
    public void ApplyAuthorityNow()
    {
        ApplyAuthority("ContextMenu", true);
    }

    private void ApplyAuthority(string reason, bool refreshTextures)
    {
        if (refreshTextures)
            RefreshRTCache();
        else
            RefreshRTCacheIfMissing();

        int scanned = 0;
        int bound = 0;
        int projectors = 0;
        int bodies = 0;
        int skippedNoProperty = 0;
        int missingRaw = 0;

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;

            scanned++;

            string path = GetPath(r.transform);
            UnitChannel channel = ResolveChannelFromPath(path);
            if (channel == UnitChannel.Unknown)
                continue;

            RenderTexture raw = GetRawMask(channel);
            if (raw == null)
            {
                missingRaw++;
                continue;
            }

            bool isProjector = IsShadowProjector(path, r);
            bool isBody = IsSpineBody(path, r);
            bool supportsOcclusion = RendererHasMaterialProperty(r, "_OcclusionTex");

            if (!supportsOcclusion)
            {
                skippedNoProperty++;
                continue;
            }

            // Authority must not own material switching. It only binds raw mask to consumers that already support it.
            BindRawMask(r, raw, isProjector, isBody);
            bound++;
            if (isProjector) projectors++;
            if (isBody) bodies++;
        }

        lastScannedRenderers = scanned;
        lastBoundRenderers = bound;
        lastBoundProjectors = projectors;
        lastBoundBodies = bodies;
        lastSkippedNoOcclusionProperty = skippedNoProperty;
        lastMissingRawMask = missingRaw;
        lastSummary = Version + " reason=" + reason
            + " scanned=" + scanned
            + " bound=" + bound
            + " projectors=" + projectors
            + " bodies=" + bodies
            + " skippedNoProp=" + skippedNoProperty
            + " missingRaw=" + missingRaw;

        if (debugLogs && Time.frameCount % Mathf.Max(1, logEveryNFrames) == 0)
            Debug.Log("[SkyPrisonOcclusionRuntimeAuthority] " + lastSummary, this);
    }

    private void BindRawMask(Renderer r, RenderTexture raw, bool isProjector, bool isBody)
    {
        MaterialPropertyBlock block;
        if (!blockCache.TryGetValue(r, out block) || block == null)
        {
            block = new MaterialPropertyBlock();
            blockCache[r] = block;
        }

        r.GetPropertyBlock(block);
        block.SetTexture(OcclusionTexId, raw);

        if (isBody)
        {
            block.SetFloat(OccludedAlphaId, bodyOccludedAlpha);
            block.SetFloat(UseHardDiscardId, bodyUseHardDiscard ? 1f : 0f);
        }

        if (isProjector)
        {
            if (forceProjectorUseOcclusionMask)
                block.SetFloat(UseOcclusionMaskId, 1f);
            block.SetFloat(InsideMaskAlphaId, projectorInsideMaskAlpha);
        }

        r.SetPropertyBlock(block);
    }

    private bool RendererHasMaterialProperty(Renderer r, string propertyName)
    {
        if (r == null || string.IsNullOrEmpty(propertyName))
            return false;

        Material[] mats = r.sharedMaterials;
        if (mats == null)
            return false;

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat != null && mat.HasProperty(propertyName))
                return true;
        }

        return false;
    }

    private bool IsShadowProjector(string path, Renderer r)
    {
        if (!string.IsNullOrEmpty(path) && path.Contains("Shadow_Projector"))
            return true;

        Material mat = r != null ? r.sharedMaterial : null;
        Shader shader = mat != null ? mat.shader : null;
        string shaderName = shader != null ? shader.name : string.Empty;
        return shaderName.Contains("FrontOccluder/Shadows/Projector");
    }

    private bool IsSpineBody(string path, Renderer r)
    {
        if (r == null || string.IsNullOrEmpty(path))
            return false;

        if (path.Contains("OutlineProxy") || path.Contains("Shadow_Projector"))
            return false;

        return path.Contains("SpineRoot") || path.Contains("Spine GameObject");
    }

    private UnitChannel ResolveChannelFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return UnitChannel.Unknown;

        if (path.Contains("PlayerRoot") || path.Contains("/PlayerRuntime/") || path.Contains("/Player/"))
            return UnitChannel.Player;
        if (path.Contains("EnemyRoot") || path.Contains("/EnemyRuntime/") || path.Contains("PF_Unit_Enemy"))
            return UnitChannel.Enemy;
        if (path.Contains("AllyRoot") || path.Contains("/AllyRuntime/"))
            return UnitChannel.Ally;
        if (path.Contains("ItemRoot") || path.Contains("/ItemRuntime/"))
            return UnitChannel.Item;

        return UnitChannel.Unknown;
    }

    private RenderTexture GetRawMask(UnitChannel channel)
    {
        switch (channel)
        {
            case UnitChannel.Player:
                return GetRT(playerRawMaskName);
            case UnitChannel.Enemy:
                return GetRT(enemyRawMaskName);
            case UnitChannel.Ally:
                return GetRT(allyRawMaskName);
            case UnitChannel.Item:
                return GetRT(itemRawMaskName);
            default:
                return null;
        }
    }

    private RenderTexture GetRT(string rtName)
    {
        if (string.IsNullOrEmpty(rtName))
            return null;

        RenderTexture rt;
        if (rtCache.TryGetValue(rtName, out rt) && rt != null)
            return rt;

        RefreshRTCache();
        rtCache.TryGetValue(rtName, out rt);
        return rt;
    }

    private void RefreshRTCacheIfMissing()
    {
        if (!rtCache.ContainsKey(playerRawMaskName)
            || !rtCache.ContainsKey(enemyRawMaskName)
            || !rtCache.ContainsKey(allyRawMaskName)
            || !rtCache.ContainsKey(itemRawMaskName))
        {
            RefreshRTCache();
        }
    }

    private void RefreshRTCache()
    {
        rtCache.Clear();
        RenderTexture[] all = Resources.FindObjectsOfTypeAll<RenderTexture>();
        for (int i = 0; i < all.Length; i++)
        {
            RenderTexture rt = all[i];
            if (rt == null || string.IsNullOrEmpty(rt.name))
                continue;

            if (!rtCache.ContainsKey(rt.name))
                rtCache.Add(rt.name, rt);
        }
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return string.Empty;

        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    public string DebugGetLastSummary()
    {
        return lastSummary;
    }
}
