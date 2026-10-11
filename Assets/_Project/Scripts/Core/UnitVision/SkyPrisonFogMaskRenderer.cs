using UnityEngine;

/// <summary>
/// 战争迷雾遮罩渲染桥。
/// 1. 玩家/友方单位提供动态视野源。
/// 2. 地形装饰物提供静态环境揭示区域，用于让 AlwaysVisible / DarkenInFog 的静态物体和投影不被 FogOverlay 盖死。
/// 3. 单位显隐仍由 SkyPrisonFogUnitVisibilityController / SkyPrisonVisionManager 控制，不会因为静态环境揭示而显示敌人。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonFogMaskRenderer : MonoBehaviour
{
    private const int MaxSources = 16;
    private const int MaxTerrainRevealSources = 64;

    [Header("References")]
    [SerializeField] private SkyPrisonVisionManager visionManager;
    [SerializeField] private bool autoFindVisionManager = true;
    [SerializeField] private Renderer overlayRenderer;

    [Header("Bounds Resolve")]
    [SerializeField] private SkyPrisonMapBounds mapBounds;
    [SerializeField] private bool autoFindMapBounds = true;
    [SerializeField] private bool fallbackToGroundRoot = true;
    [SerializeField] private Transform groundRoot;
    [SerializeField] private Vector3 fallbackCenter = new Vector3(0f, 0.08f, 0f);
    [SerializeField] private Vector2 fallbackSize = new Vector2(64f, 64f);
    [SerializeField] private float overlayHeight = 0.08f;
    [SerializeField] private float boundsPadding = 2.0f;

    [Header("Fog Style")]
    [SerializeField] private Color fogColor = new Color(0.08f, 0.09f, 0.10f, 0.72f);
    [SerializeField] private Color visibleColor = new Color(0f, 0f, 0f, 0f);
    [SerializeField, Range(0f, 1f)] private float globalFogStrength = 0.72f;
    [SerializeField, Min(0.1f)] private float softEdgeWidth = 3.5f;
    [SerializeField, Min(0.1f)] private float angleSoftness = 8f;

    [Header("Terrain Decoration Reveal")]
    [Tooltip("开启后，AlwaysVisible / DarkenInFog 的地形装饰物会给 FogOverlay 提供一个静态揭示区域，让地形装饰物和投影不被覆盖层盖死。单位显隐不受该区域影响。")]
    [SerializeField] private bool revealTerrainDecorations = true;
    [Tooltip("自动扫描场景中的 TerrainDecorationRuntimeBinder。")]
    [SerializeField] private bool autoFindTerrainDecorations = true;
    [Tooltip("地形装饰物揭示区域在碰撞盒 XZ 外额外扩张的距离。需要覆盖投影时可以适当加大。")]
    [SerializeField, Min(0f)] private float terrainRevealPadding = 1.5f;
    [Tooltip("地形装饰物揭示区域的边缘柔化宽度。")]
    [SerializeField, Min(0.01f)] private float terrainRevealSoftness = 1.5f;
    [Tooltip("地形装饰物揭示强度。1=完全挖开 FogOverlay；0.6=仍然保留部分迷雾暗化。")]
    [SerializeField, Range(0f, 1f)] private float terrainRevealVisibility = 0.95f;
    [Tooltip("刷新地形装饰物揭示源的间隔。放置/删除很多装饰物时可调小；稳定后可调大。")]
    [SerializeField, Min(0.05f)] private float terrainRevealRescanInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private MaterialPropertyBlock mpb;
    private readonly Vector4[] sourceOriginRadius = new Vector4[MaxSources];
    private readonly Vector4[] sourceForwardAngle = new Vector4[MaxSources];
    private readonly Vector4[] sourceFlags = new Vector4[MaxSources];

    private readonly Vector4[] terrainRevealCenterHalfX = new Vector4[MaxTerrainRevealSources];
    private readonly Vector4[] terrainRevealRightHalfZ = new Vector4[MaxTerrainRevealSources];
    private readonly Vector4[] terrainRevealForwardFlags = new Vector4[MaxTerrainRevealSources];
    private TerrainDecorationRuntimeBinder[] cachedTerrainDecorations;
    private float nextTerrainRevealRescanTime = -1f;

    private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
    private static readonly int VisibleColorId = Shader.PropertyToID("_VisibleColor");
    private static readonly int GlobalFogStrengthId = Shader.PropertyToID("_GlobalFogStrength");
    private static readonly int SoftEdgeWidthId = Shader.PropertyToID("_SoftEdgeWidth");
    private static readonly int AngleSoftnessId = Shader.PropertyToID("_AngleSoftness");
    private static readonly int VisionSourceCountId = Shader.PropertyToID("_VisionSourceCount");
    private static readonly int VisionSourceOriginRadiusId = Shader.PropertyToID("_VisionSourceOriginRadius");
    private static readonly int VisionSourceForwardAngleId = Shader.PropertyToID("_VisionSourceForwardAngle");
    private static readonly int VisionSourceFlagsId = Shader.PropertyToID("_VisionSourceFlags");

    private static readonly int TerrainRevealCountId = Shader.PropertyToID("_TerrainRevealCount");
    private static readonly int TerrainRevealCenterHalfXId = Shader.PropertyToID("_TerrainRevealCenterHalfX");
    private static readonly int TerrainRevealRightHalfZId = Shader.PropertyToID("_TerrainRevealRightHalfZ");
    private static readonly int TerrainRevealForwardFlagsId = Shader.PropertyToID("_TerrainRevealForwardFlags");
    private static readonly int TerrainRevealSoftnessId = Shader.PropertyToID("_TerrainRevealSoftness");
    private static readonly int TerrainRevealVisibilityId = Shader.PropertyToID("_TerrainRevealVisibility");

    // 战争迷雾曾经完全不显示，实测原因是 overlayRenderer.sharedMaterial 在运行时
    // 被换成了跟这个系统毫无关系的第三方插件占位材质（VLBDummyMaterialHD，来自
    // VolumetricLightBeam 插件，和战争迷雾没有任何调用关系）。场景文件里保存的
    // 材质本身是对的（着色器 GUID 正确指向 SkyPrisonFogOfWarOverlay），说明污染
    // 发生在运行时，来源没有定位到——但 MaterialPropertyBlock 的值全部正常写入，
    // 只是作用在了错误的着色器上，所以看起来"整条链路都对但没有任何效果"。
    //
    // 不再去追查是谁改的，改成 Awake 时（早于绝大多数其它系统跑）把当前材质缓存下来，
    // 之后每帧发现材质被换掉就恢复，不依赖找到并修好那个未知的污染源。
    private Material expectedMaterial;

    private void Awake()
    {
        ResolveReferences();
        EnsurePropertyBlock();
        RefreshOverlayBounds();
        RefreshTerrainRevealCache(force: true);

        if (overlayRenderer != null && overlayRenderer.sharedMaterial != null)
            expectedMaterial = overlayRenderer.sharedMaterial;

        ApplyMaterialState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePropertyBlock();
        RefreshOverlayBounds();
        RefreshTerrainRevealCache(force: true);
        ApplyMaterialState();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        RefreshOverlayBounds();
        RefreshTerrainRevealCache(force: false);
        ApplyMaterialState();
    }

    public void ResolveReferences()
    {
        if (visionManager == null && autoFindVisionManager)
            visionManager = SkyPrisonVisionManager.Instance != null
                ? SkyPrisonVisionManager.Instance
                : FindFirstObjectByType<SkyPrisonVisionManager>();

        if (overlayRenderer == null)
            overlayRenderer = GetComponent<Renderer>();

        if (mapBounds == null && autoFindMapBounds)
            mapBounds = FindFirstObjectByType<SkyPrisonMapBounds>();

        if (groundRoot == null && fallbackToGroundRoot)
        {
            GameObject go = GameObject.Find("GroundRoot");
            if (go != null)
                groundRoot = go.transform;
        }
    }

    public bool IsWorldPositionVisible(Vector3 worldPosition)
    {
        ResolveReferences();
        return visionManager != null && visionManager.IsWorldPositionVisibleToPlayerFaction(worldPosition);
    }

    public float EvaluateVisibility01(Vector3 worldPosition)
    {
        ResolveReferences();
        if (visionManager == null)
            return 0f;

        return visionManager.EvaluatePlayerFactionVisibility01(worldPosition);
    }

    public float GetVisibleMaskForMainView(Vector3 worldPosition)
    {
        return EvaluateVisibility01(worldPosition);
    }

    public float GetVisibleMaskForMinimap(Vector3 worldPosition)
    {
        return EvaluateVisibility01(worldPosition);
    }

    [ContextMenu("Refresh Overlay Bounds")]
    public void RefreshOverlayBounds()
    {
        if (overlayRenderer == null)
            return;

        ResolveReferences();

        if (mapBounds != null)
        {
            mapBounds.RefreshBounds();
            Bounds b = mapBounds.ResolvedBounds;
            transform.position = new Vector3(b.center.x, overlayHeight, b.center.z);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = new Vector3(b.size.x, b.size.z, 1f);

            if (debugLogs)
                Debug.Log($"[SkyPrisonFogMaskRenderer] From MapBounds center={transform.position} scale={transform.localScale}", this);
            return;
        }

        if (fallbackToGroundRoot && groundRoot != null)
        {
            Renderer[] renderers = groundRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);

                b.Expand(new Vector3(boundsPadding, 0f, boundsPadding));

                transform.position = new Vector3(b.center.x, overlayHeight, b.center.z);
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                transform.localScale = new Vector3(b.size.x, b.size.z, 1f);

                if (debugLogs)
                    Debug.Log($"[SkyPrisonFogMaskRenderer] From GroundRoot center={transform.position} scale={transform.localScale}", this);
                return;
            }
        }

        transform.position = new Vector3(fallbackCenter.x, overlayHeight, fallbackCenter.z);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        transform.localScale = new Vector3(fallbackSize.x, fallbackSize.y, 1f);
    }

    [ContextMenu("Refresh Terrain Reveal Cache")]
    public void RefreshTerrainRevealCacheNow()
    {
        RefreshTerrainRevealCache(force: true);
        ApplyMaterialState();
    }

    private void RefreshTerrainRevealCache(bool force)
    {
        if (!revealTerrainDecorations)
        {
            cachedTerrainDecorations = null;
            return;
        }

        if (!autoFindTerrainDecorations)
            return;

        if (!force && Time.time < nextTerrainRevealRescanTime && cachedTerrainDecorations != null)
            return;

        cachedTerrainDecorations = FindObjectsByType<TerrainDecorationRuntimeBinder>(FindObjectsSortMode.None);
        nextTerrainRevealRescanTime = Time.time + Mathf.Max(0.05f, terrainRevealRescanInterval);

        if (debugLogs)
            Debug.Log($"[SkyPrisonFogMaskRenderer] Terrain reveal cache={cachedTerrainDecorations.Length}", this);
    }

    private void EnsurePropertyBlock()
    {
        if (mpb == null)
            mpb = new MaterialPropertyBlock();
    }

    private void ApplyMaterialState()
    {
        if (overlayRenderer == null)
            return;

        // 自愈：材质被换掉就换回来。见 Awake 里的说明。
        if (expectedMaterial != null && overlayRenderer.sharedMaterial != expectedMaterial)
        {
            if (!_loggedMaterialHijack)
            {
                _loggedMaterialHijack = true;
                Debug.LogWarning($"[SkyPrisonFogMaskRenderer] 材质被外部换成了 " +
                    $"{(overlayRenderer.sharedMaterial != null ? overlayRenderer.sharedMaterial.name : "NULL")}，" +
                    $"已恢复为 {expectedMaterial.name}。污染源未定位，这里只做自愈。", this);
            }
            overlayRenderer.sharedMaterial = expectedMaterial;
        }

        // 夜视仪：迷雾遮罩整张关掉；退出夜视仪时只恢复"是我们关的"那一次，
        // 不去覆盖别处对 enabled 的设置。
        if (SkyPrisonNightVisionMode.IsActive)
        {
            if (overlayRenderer.enabled)
            {
                overlayRenderer.enabled = false;
                suppressedByNightVision = true;
            }
            return;
        }
        if (suppressedByNightVision)
        {
            overlayRenderer.enabled = true;
            suppressedByNightVision = false;
        }

        EnsurePropertyBlock();

        mpb.Clear();
        mpb.SetColor(FogColorId, fogColor);
        mpb.SetColor(VisibleColorId, visibleColor);
        mpb.SetFloat(GlobalFogStrengthId, globalFogStrength);
        mpb.SetFloat(SoftEdgeWidthId, softEdgeWidth);
        mpb.SetFloat(AngleSoftnessId, angleSoftness);
        mpb.SetFloat(TerrainRevealSoftnessId, Mathf.Max(0.01f, terrainRevealSoftness));
        mpb.SetFloat(TerrainRevealVisibilityId, Mathf.Clamp01(terrainRevealVisibility));

        int count = 0;
        if (visionManager != null)
        {
            var sources = visionManager.PlayerFactionSources;
            count = Mathf.Min(MaxSources, sources.Count);

            for (int i = 0; i < count; i++)
            {
                var src = sources[i];
                sourceOriginRadius[i] = new Vector4(src.originWorld.x, src.originWorld.y, src.originWorld.z, src.radius);
                sourceForwardAngle[i] = new Vector4(src.forwardWorld.x, src.forwardWorld.y, src.forwardWorld.z, src.angle);
                sourceFlags[i] = new Vector4(src.useCircle ? 1f : 0f, src.useFacingDirection ? 1f : 0f, 0f, 0f);
            }
        }

        for (int i = count; i < MaxSources; i++)
        {
            sourceOriginRadius[i] = Vector4.zero;
            sourceForwardAngle[i] = Vector4.zero;
            sourceFlags[i] = Vector4.zero;
        }

        int terrainCount = BuildTerrainRevealSources();

        mpb.SetInt(VisionSourceCountId, count);
        mpb.SetVectorArray(VisionSourceOriginRadiusId, sourceOriginRadius);
        mpb.SetVectorArray(VisionSourceForwardAngleId, sourceForwardAngle);
        mpb.SetVectorArray(VisionSourceFlagsId, sourceFlags);

        mpb.SetInt(TerrainRevealCountId, terrainCount);
        mpb.SetVectorArray(TerrainRevealCenterHalfXId, terrainRevealCenterHalfX);
        mpb.SetVectorArray(TerrainRevealRightHalfZId, terrainRevealRightHalfZ);
        mpb.SetVectorArray(TerrainRevealForwardFlagsId, terrainRevealForwardFlags);

        overlayRenderer.SetPropertyBlock(mpb);

        // 一次性诊断：查"战争迷雾没显示"。静态检查场景文件排除不了运行时才会出现的
        // 空引用/空数据情况（这类问题只能看运行时状态，参见项目规矩），
        // 不受 debugLogs 序列化开关影响，只跑一次不会刷屏。
        if (!_loggedFogDiagnostic)
        {
            _loggedFogDiagnostic = true;
            Debug.Log($"[FogDiag] visionManager={(visionManager != null ? "有" : "NULL")} " +
                      $"sources={count} globalFogStrength={globalFogStrength} " +
                      $"fogColor={fogColor} overlayRenderer.enabled={overlayRenderer.enabled} " +
                      $"gameObject.activeInHierarchy={gameObject.activeInHierarchy} " +
                      $"material={(overlayRenderer.sharedMaterial != null ? overlayRenderer.sharedMaterial.name : "NULL")}",
                      this);
        }
    }

    private bool suppressedByNightVision;
    private bool _loggedFogDiagnostic;
    private bool _loggedMaterialHijack;

    private int BuildTerrainRevealSources()
    {
        int count = 0;

        if (revealTerrainDecorations && cachedTerrainDecorations != null)
        {
            for (int i = 0; i < cachedTerrainDecorations.Length && count < MaxTerrainRevealSources; i++)
            {
                TerrainDecorationRuntimeBinder binder = cachedTerrainDecorations[i];
                if (binder == null || binder.definition == null)
                    continue;

                TerrainDecorationDefinition def = binder.definition;
                if (def.fogMode == TerrainDecorationFogMode.HideInFog || def.fogMode == TerrainDecorationFogMode.RevealOnlyWhenSeen)
                    continue;

                Vector3 size = binder.overrideCollision ? binder.collisionSizeOverride : def.collisionSize;
                Vector3 offset = binder.overrideCollision ? binder.collisionOffsetOverride : def.collisionOffset;

                if (size.x <= 0.0001f || size.z <= 0.0001f)
                {
                    Vector2 footprint = def.footprintSize;
                    size.x = Mathf.Max(0.01f, footprint.x);
                    size.z = Mathf.Max(0.01f, footprint.y);
                }

                Vector3 center = binder.transform.TransformPoint(offset);
                Vector3 right = binder.transform.TransformDirection(Vector3.right);
                Vector3 forward = binder.transform.TransformDirection(Vector3.forward);
                right.y = 0f;
                forward.y = 0f;

                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.right;
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;

                right.Normalize();
                forward.Normalize();

                Vector3 lossy = binder.transform.lossyScale;
                float halfX = Mathf.Abs(size.x * lossy.x) * 0.5f + terrainRevealPadding;
                float halfZ = Mathf.Abs(size.z * lossy.z) * 0.5f + terrainRevealPadding;

                terrainRevealCenterHalfX[count] = new Vector4(center.x, center.y, center.z, Mathf.Max(0.01f, halfX));
                terrainRevealRightHalfZ[count] = new Vector4(right.x, right.y, right.z, Mathf.Max(0.01f, halfZ));
                terrainRevealForwardFlags[count] = new Vector4(forward.x, forward.y, forward.z, 1f);
                count++;
            }
        }

        for (int i = count; i < MaxTerrainRevealSources; i++)
        {
            terrainRevealCenterHalfX[i] = Vector4.zero;
            terrainRevealRightHalfZ[i] = Vector4.zero;
            terrainRevealForwardFlags[i] = Vector4.zero;
        }

        return count;
    }
}
