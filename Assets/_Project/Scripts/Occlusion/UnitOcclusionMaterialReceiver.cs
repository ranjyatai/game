using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(32000)]
public class UnitOcclusionMaterialReceiver : MonoBehaviour, IOcclusionStateReceiver
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V30 - 2026-06-06 - explicit front root ledger";
    [SerializeField] private int compileTouchVersion = 2026060602;

    [Header("Target Renderer")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Renderer[] extraTargetRenderers;
    [SerializeField] private bool autoFindRenderer = true;

    [Tooltip("只在特殊情况下开启。默认不要把 VisualRoot 下所有 Renderer 一锅端做材质替换。")]
    [SerializeField] private bool autoFindAllRenderers = false;

    [Header("Material Sets")]
    [SerializeField] private Material[] normalMaterials;
    [SerializeField] private Material[] occlusionCompositeMaterials;

    [Header("Runtime Fallback")]
    [Tooltip("Build 中如果 occlusionCompositeMaterials 丢失/为空，则用当前 Spine/Skeleton 材质临时生成 Spine/SpineOcclusionComposite 运行时材质。")]
    [SerializeField] private bool createRuntimeCompositeFallback = true;

    [Tooltip("运行时自动生成的 Composite 材质名后缀。")]
    [SerializeField] private string runtimeCompositeMaterialSuffix = "_RuntimeComposite";

    [Header("Options")]
    [SerializeField] private bool enforceEveryLateUpdate = false;

    [Tooltip("Spine/SkeletonRenderer may rebuild renderer.sharedMaterials after normal state events. Keep this on: it only compares the current material array in a very late LateUpdate and reapplies when another system has overwritten the expected state.")]
    [SerializeField] private bool repairExternalMaterialOverwriteInLateUpdate = true;

    [SerializeField] private bool debugLogs = false;

    [Header("Per Unit Isolation")]
    [Tooltip("开启后，本 Receiver 只允许管理自己单位边界内的 Renderer。遇到其它 IOcclusionStateReceiver 所属子树会直接拒绝。")]
    [SerializeField] private bool isolateRendererSearchPerReceiver = true;

    [Tooltip("自动搜索时拒绝挂在其它 IOcclusionStateReceiver 下面的 Renderer，防止敌人 Receiver 收到玩家 Renderer，或玩家 Receiver 收到敌人 Renderer。")]
    [SerializeField] private bool rejectRenderersOwnedByOtherReceiver = true;

    [Tooltip("自动搜索时拒绝挂在本 Receiver 根节点同一个 GameObject 上的根 SpriteRenderer。Player 根节点上的占位 SpriteRenderer 不应该参与遮挡材质切换。")]
    [SerializeField] private bool rejectRendererOnReceiverRoot = true;

    [Header("Per Unit Occluder Mask")]
    [Tooltip("旧链路：开启后，本 Receiver 会自己生成 SkyPrison_PerUnitOccluderMask_* 1024 RT 并写入 _OcclusionTex。当前由 ScreenSpaceOutlineRTManager / BindingAuthority 统一管理遮挡 RT，默认关闭。")]
    [SerializeField] private bool usePerUnitOccluderMask = false;

    [Tooltip("强制关闭 Receiver 自己管理的 PerUnit Mask。当前项目主线必须保持开启，避免旧 1024 mask 覆盖 RT_OcclusionMask_Player_Runtime。")]
    [SerializeField] private bool forceDisableReceiverManagedPerUnitMask = true;

    [Header("Runtime Authority Integration")]
    [Tooltip("运行时强制把旧 Receiver-managed per-unit mask 链路关掉。最终 _OcclusionTex 由 SkyPrisonOcclusionRuntimeAuthority / ScreenSpaceOutlineRTManager 绑定。")]
    [SerializeField] private bool useRuntimeAuthorityMaskBinding = true;

    [Tooltip("启用后，在 Awake/OnEnable/遮挡状态切换时都会把 usePerUnitOccluderMask=false、forceDisableReceiverManagedPerUnitMask=true，防止旧序列化值在 Build 中复活。")]
    [SerializeField] private bool forceDisableLegacyPerUnitMaskAtRuntime = true;

    [Tooltip("用于复制投影参数的遮挡 Mask 相机。为空时运行时按名字 OcclusionMaskCamera 自动查找。")]
    [SerializeField] private Camera perUnitMaskCameraTemplate;

    [Tooltip("本单位专属 Mask 的宽度。建议先保持和 rtOcclusionMask 接近；过低会边缘糊，过高会更耗。")]
    [SerializeField] private int perUnitMaskWidth = 1024;

    [Tooltip("本单位专属 Mask 的高度。建议先保持和 rtOcclusionMask 接近；过低会边缘糊，过高会更耗。")]
    [SerializeField] private int perUnitMaskHeight = 1024;

    [Tooltip("渲染本单位专属 Mask 时临时使用的 Layer。默认 31。相机会只看这一层，渲染后会恢复原 Layer。")]
    [SerializeField] private int perUnitMaskTempLayer = 31;

    [Tooltip("Composite 材质中的 Mask 贴图属性名。Spine/SpineOcclusionComposite 实际采样 _OcclusionTex；这里默认写入 _OcclusionTex，同时继续兼容 _MaskTex。")]
    [SerializeField] private string perUnitMaskTextureProperty = "_OcclusionTex";

    [Tooltip("同时兼容一些历史 Shader 命名；开启后会尝试设置 _OcclusionMask / _OcclusionMaskTex。")]
    [SerializeField] private bool setLegacyMaskTextureProperties = true;

    [Header("Debug")]
    [SerializeField] private int activeOccluderCount = 0;
    [SerializeField] private bool currentOccluded = false;
    [SerializeField] private int resolvedRendererCount = 0;
    [SerializeField] private int rejectedRendererCount = 0;
    [SerializeField] private string receiverRootPath = "";
    [SerializeField] private string lastRejectedRenderer = "";
    [SerializeField] private string lastApply = "";
    [SerializeField] private string lastWarning = "";
    [SerializeField] private string lastSetOccluderName = "";
    [SerializeField] private string lastSetOccluderPath = "";
    [SerializeField] private bool lastSetOccluderIsOccluded = false;
    [SerializeField] private int lastSetOccluderFrame = -1;
    [SerializeField] private string activeOccluderDebugList = "";
    [SerializeField] private string perUnitMaskStatus = "";
    [SerializeField] private string perUnitMaskOccluderRoots = "";
    [SerializeField] private Texture runtimeAuthorityMaskTexture;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int StraightAlphaInputId = Shader.PropertyToID("_StraightAlphaInput");
    private static readonly int OccludedAlphaId = Shader.PropertyToID("_OccludedAlpha");
    private static readonly int DebugMaskModeId = Shader.PropertyToID("_DebugMaskMode");
    private static readonly int RawDiscardThresholdId = Shader.PropertyToID("_RawDiscardThreshold");
    private static readonly int MaskThresholdId = Shader.PropertyToID("_MaskThreshold");
    private static readonly int MaskSoftnessId = Shader.PropertyToID("_MaskSoftness");
    private static readonly int OccludedBrightnessId = Shader.PropertyToID("_OccludedBrightness");
    private static readonly int OccludedSaturationId = Shader.PropertyToID("_OccludedSaturation");
    private static readonly int OccludedTintId = Shader.PropertyToID("_OccludedTint");
    private static readonly int DefaultMaskTexId = Shader.PropertyToID("_MaskTex");
    private static readonly int OcclusionTexId = Shader.PropertyToID("_OcclusionTex");
    private static readonly int LegacyOcclusionMaskId = Shader.PropertyToID("_OcclusionMask");
    private static readonly int LegacyOcclusionMaskTexId = Shader.PropertyToID("_OcclusionMaskTex");
    private static readonly int UseCleanCharacterOutlineTexId = Shader.PropertyToID("_SkyPrison_UseCleanCharacterOutlineTex");
    private static readonly int EnableHiddenOutlineId = Shader.PropertyToID("_SkyPrison_EnableHiddenOutline");
    private static readonly int UseHologramFillDefaultId = Shader.PropertyToID("_SkyPrison_UseHologramFill");
    private static readonly int HologramSilhouetteAlphaId = Shader.PropertyToID("_SkyPrison_HologramSilhouetteAlpha");
    private static readonly int HologramFillColorId = Shader.PropertyToID("_SkyPrison_HologramFillColor");
    private static readonly int Force3DPropOpaqueAlphaId = Shader.PropertyToID("_SkyPrison_Force3DPropOpaqueAlpha");
    private static readonly int CullModeId = Shader.PropertyToID("_SkyPrison_CullMode");
    private static readonly int ZWriteModeId = Shader.PropertyToID("_SkyPrison_ZWriteMode");
    private static readonly int ZTestModeId = Shader.PropertyToID("_SkyPrison_ZTestMode");
    private static readonly int UseRootAnchorDepthId = Shader.PropertyToID("_SkyPrison_UseRootAnchorDepth");
    private static readonly int SceneDepthDebugId = Shader.PropertyToID("_SkyPrison_SceneDepthDebug");

    private readonly HashSet<int> activeOccluders = new HashSet<int>();
    private readonly Dictionary<int, MonoBehaviour> activeOccluderRefs = new Dictionary<int, MonoBehaviour>();
    private readonly Dictionary<int, GameObject> activeOccluderRootRefs = new Dictionary<int, GameObject>();
    private readonly List<Renderer> resolvedRenderers = new List<Renderer>();
    private readonly Dictionary<Renderer, Material[]> cachedNormalMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, Material[]> runtimeCompositeCache = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, Material[]> perUnitCompositeInstanceCache = new Dictionary<Renderer, Material[]>();

    // V26: material switching is allowed only as a state transition bridge.
    // It must never run every LateUpdate or OnEnable/OnDisable, otherwise Spine/Skeleton
    // gets restored over the occlusion composite material at the first frame.
    private bool materialStateKnown = false;
    private bool materialStateOccluded = false;

    public Renderer TargetRenderer => targetRenderer;
    public Material[] NormalMaterials => normalMaterials;
    public Material[] OcclusionCompositeMaterials => occlusionCompositeMaterials;
    public int ResolvedRendererCount => resolvedRendererCount;
    public bool CurrentOccluded => currentOccluded;
    public int ActiveOccluderCount => activeOccluderCount;
    public IReadOnlyList<Renderer> ResolvedRenderers => resolvedRenderers;

    // 2026-08-18：3D 通道场景物专用注册表，供 SkyPrisonModel3DHologramOverlayFeature
    // 用。HologramOverlay3D 这条 Pass 跟 NormalBody 共用 LightMode="UniversalForward"，
    // URP 标准前向渲染每个物体每个 LightMode 只会挑一条 Pass 画，不会像内置管线那样
    // 自动把同 LightMode 的多条 Pass 都跑一遍——这条 Pass 从写出来那天起就没被调度过，
    // 材质本身、判定逻辑都是对的，纯粹是没人显式用 DrawRenderer(..., passIndex) 去点它。
    // 这里维护一份活跃的 3D 通道 receiver 列表，Feature 里按 pass 名找到具体索引，
    // 显式点名绘制。
    public static readonly List<UnitOcclusionMaterialReceiver> Active3DPropReceivers = new List<UnitOcclusionMaterialReceiver>();

    /// <summary>
    /// Unit-level occlusion ledger access for MaskRT render filters.
    /// This exposes only the current authorized occluder roots for this receiver;
    /// it does not control outline UI or outline RTs.
    /// </summary>
    public void CollectActiveFrontOccluderRoots(List<GameObject> results)
    {
        if (results == null)
            return;

        RefreshOccluderStateFromActiveSet();

        // Unit-level mask authorization must be strictly current.
        // When this receiver is not currently occluded, it must not contribute
        // any FrontOccluderRoot to Player/Enemy/Ally/Item MaskRT rendering.
        if (!currentOccluded || activeOccluderCount <= 0)
            return;

        foreach (int id in activeOccluders)
        {
            GameObject root = null;

            // V30: prefer the exact root authorized by SimpleDirectionalOccluder.
            // Do not rediscover from parent hierarchy unless the legacy caller did not provide one.
            if (activeOccluderRootRefs.TryGetValue(id, out GameObject explicitRoot) && explicitRoot != null && explicitRoot.activeInHierarchy)
            {
                root = explicitRoot;
            }
            else if (activeOccluderRefs.TryGetValue(id, out MonoBehaviour mb) && mb != null && mb.isActiveAndEnabled)
            {
                root = ResolveFrontOccluderRootForLedger(mb);
            }

            if (root != null && !results.Contains(root))
                results.Add(root);
        }
    }

    public bool IsOccludedBy(MonoBehaviour occluder)
    {
        if (occluder == null)
            return false;

        RefreshOccluderStateFromActiveSet();
        return activeOccluders.Contains(occluder.GetInstanceID());
    }

    public IReadOnlyCollection<int> ActiveOccluderIds => activeOccluders;

    private static GameObject ResolveFrontOccluderRootForLedger(MonoBehaviour occluder)
    {
        if (occluder == null)
            return null;

        System.Type type = occluder.GetType();
        System.Reflection.FieldInfo field = type.GetField(
            "frontOccluderRoot",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        if (field != null)
        {
            object value = field.GetValue(occluder);
            if (value is GameObject go && go != null)
                return go;
            if (value is Transform t && t != null)
                return t.gameObject;
        }

        Transform found = FindChildInAncestors(occluder.transform, "FrontOccluderRoot");
        return found != null ? found.gameObject : null;
    }

    private static Transform FindChildInAncestors(Transform start, string name)
    {
        Transform current = start;
        int guard = 0;
        while (current != null && guard++ < 64)
        {
            Transform found = FindDeepChildExact(current, name);
            if (found != null)
                return found;

            current = current.parent;
        }

        return null;
    }

    private static Transform FindDeepChildExact(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

    private void EnforceRuntimeAuthorityMaskMode()
    {
        // V26 policy:
        // Receiver is allowed to bridge Normal <-> OcclusionComposite only when its occlusion
        // ledger changes. It must not own per-unit mask rendering and must not enforce every frame.
        scriptVersion = "V30 - 2026-06-06 - explicit front root ledger";
        compileTouchVersion = 2026060602;
        enforceEveryLateUpdate = false;
        usePerUnitOccluderMask = false;
        forceDisableReceiverManagedPerUnitMask = true;
        useRuntimeAuthorityMaskBinding = true;
        forceDisableLegacyPerUnitMaskAtRuntime = true;
        setLegacyMaskTextureProperties = false;
        perUnitMaskStatus = "runtime authority: receiver-managed SkyPrison_PerUnitOccluderMask disabled; Receiver only switches material on occlusion state change";
        perUnitMaskOccluderRoots = "";
    }

    private void Awake()
    {
        EnforceRuntimeAuthorityMaskMode();
        RebuildRendererCacheInternal(false);
        materialStateKnown = false;
    }

    private void OnEnable()
    {
        EnforceRuntimeAuthorityMaskMode();
        RebuildRendererCacheInternal(false);

        ClearOccluderState();
        materialStateKnown = false;
    }

    private void LateUpdate()
    {
        EnforceRuntimeAuthorityMaskMode();
        RefreshOccluderStateFromActiveSet();

        // V27: this is not the old every-frame material switch.
        // It is a very-late guard against Spine/SkeletonRenderer rebuilding sharedMaterials
        // after SetOccludedBy has already applied the correct state. It only writes when the
        // currently visible material array no longer matches the expected state.
        if (enforceEveryLateUpdate || repairExternalMaterialOverwriteInLateUpdate)
            ApplyMaterialStateIfNeeded(false);

        // 一次性诊断——只在第300帧打一次，直接读渲染器"这一刻实际挂着"的材质实例和
        // 它身上_SkyPrison_SceneDepthDebug的实时值。EnsureCompositeDefaults创建时确实
        // 写过4（有日志为证），但箱子画面完全没反应——需要确认到渲染这一刻，materials
        // 是不是还是同一份、值是不是还是4，排除"后来被换了材质/被别的地方重置"。
        if (_is3DPropMode && Time.frameCount == 300 && resolvedRenderers.Count > 0)
        {
            Renderer r = resolvedRenderers[0];
            if (r != null)
            {
                Material[] live = r.sharedMaterials;
                for (int i = 0; i < live.Length; i++)
                {
                    Material m = live[i];
                    if (m == null) continue;
                    float dbgVal = m.HasProperty(SceneDepthDebugId) ? m.GetFloat(SceneDepthDebugId) : -999f;
                    Debug.Log($"[UnitOcclusionMaterialReceiver] LiveMaterialCheck -> {name} slot={i} mat={m.name}#{m.GetInstanceID()} sceneDepthDebug={dbgVal} shader={m.shader.name}", this);
                }
            }
        }
    }

    private void OnDisable()
    {
        ClearOccluderState();
        materialStateKnown = false;
        // Do not restore normal materials here. Spine/Authority initialization order may disable
        // and re-enable receivers during startup; restoring Skeleton here is the exact first-frame hijack.

        Active3DPropReceivers.Remove(this);
    }

    /// <summary>
    /// 由 UnitDefinitionRuntimeApplier 自动绑定真实 Spine 主模型 / 武器真实 Renderer / 材质组。
    /// </summary>
    public void ConfigureRendererAndMaterials(
        Renderer primaryRenderer,
        Renderer[] extraRenderers,
        Material[] normalSet,
        Material[] occlusionSet,
        bool includeAllRenderers)
    {
        targetRenderer = primaryRenderer != null ? primaryRenderer : targetRenderer;
        extraTargetRenderers = extraRenderers;
        autoFindRenderer = true;
        autoFindAllRenderers = includeAllRenderers;

        if (normalSet != null && normalSet.Length > 0)
            normalMaterials = CloneMaterials(normalSet);

        if (occlusionSet != null && occlusionSet.Length > 0)
            occlusionCompositeMaterials = CloneMaterials(occlusionSet);

        RebuildRendererCache();
        materialStateKnown = false;
    }

    [ContextMenu("Rebuild Renderer Cache")]
    public void RebuildRendererCache()
    {
        RebuildRendererCacheInternal(true);
    }

    /// <summary>
    /// 只重新扫一遍渲染器列表（比如 3D 通道单位运行时才实例化出网格，扫描当时那个
    /// 节点下还是空的，得补扫一次），不强制按 currentOccluded 重新套材质。
    /// RebuildRendererCache()（reapply=true）每次调用都会强制走一次"正常/合成"材质
    /// 二选一——如果调用方（比如 UnitDefinitionRuntimeBinder 每次刷新绑定都会调）
    /// 反复调这个，材质就会跟着反复横跳，被遮挡效果表现为一会儿正常一会儿合成的
    /// 闪烁。正常的材质切换应该交给这个组件自己的 OnEnable/LateUpdate 稳定地做一次，
    /// 外部只需要保证渲染器列表是最新的。
    /// </summary>
    public void RescanRenderersOnly()
    {
        RebuildRendererCacheInternal(false);
    }

    [ContextMenu("Clear Occlusion Receiver State")]
    public void ClearStateFromContextMenu()
    {
        ClearOccluderState();
        ApplyMaterialStateIfNeeded(true);
        if (debugLogs)
            Debug.Log($"[UnitOcclusionMaterialReceiver] Clear state -> {GetPath(transform)}", this);
    }

    [ContextMenu("Force Runtime Authority Mask Mode")]
    public void ForceRuntimeAuthorityMaskModeFromContextMenu()
    {
        useRuntimeAuthorityMaskBinding = true;
        forceDisableLegacyPerUnitMaskAtRuntime = true;
        EnforceRuntimeAuthorityMaskMode();
        perUnitCompositeInstanceCache.Clear();
        runtimeCompositeCache.Clear();
        if (debugLogs)
            Debug.Log("[UnitOcclusionMaterialReceiver] Forced runtime authority mask mode -> " + GetPath(transform), this);
    }

    [ContextMenu("Dump Occlusion Receiver State")]
    public void DumpState()
    {
        ResolveRenderer();

        string msg =
            "[UnitOcclusionMaterialReceiver Dump]\n" +
            $"name={name}\n" +
            $"currentOccluded={currentOccluded}, activeOccluderCount={activeOccluderCount}\n" +
            $"targetRenderer={(targetRenderer != null ? GetPath(targetRenderer.transform) : "NULL")}\n" +
            $"resolvedRendererCount={resolvedRenderers.Count}, rejectedRendererCount={rejectedRendererCount}\n" +
            $"receiverRootPath={receiverRootPath}\n" +
            $"lastRejectedRenderer={lastRejectedRenderer}\n" +
            $"normalMaterials={FormatMaterialArray(normalMaterials)}\n" +
            $"occlusionCompositeMaterials={FormatMaterialArray(occlusionCompositeMaterials)}\n" +
            $"lastApply={lastApply}\n" +
            $"lastWarning={lastWarning}\n" +
            $"lastSetOccluderName={lastSetOccluderName}\n" +
            $"lastSetOccluderPath={lastSetOccluderPath}\n" +
            $"lastSetOccluderIsOccluded={lastSetOccluderIsOccluded}\n" +
            $"lastSetOccluderFrame={lastSetOccluderFrame}\n" +
            $"activeOccluders={activeOccluderDebugList}\n";

        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer r = resolvedRenderers[i];
            msg += $"  renderer[{i}]={(r != null ? GetPath(r.transform) : "NULL")} mats={FormatMaterialArray(r != null ? r.sharedMaterials : null)}\n";
        }

        Debug.Log(msg, this);
    }

    /// <summary>
    /// V30: exact unit-level occlusion authorization.
    /// SimpleDirectionalOccluder must pass the FrontOccluderRoot that belongs to itself;
    /// this receiver will then expose only these explicitly authorized roots to mask rendering.
    /// </summary>
    public void SetOccludedByRoot(MonoBehaviour occluder, GameObject frontOccluderRoot, bool isOccluded)
    {
        EnforceRuntimeAuthorityMaskMode();

        if (occluder == null)
            return;

        int id = occluder.GetInstanceID();

        lastSetOccluderName = occluder.name;
        lastSetOccluderPath = GetPath(occluder.transform) + " | root=" + (frontOccluderRoot != null ? GetPath(frontOccluderRoot.transform) : "NULL");
        lastSetOccluderIsOccluded = isOccluded;
        lastSetOccluderFrame = Time.frameCount;

        if (isOccluded)
        {
            activeOccluders.Add(id);
            activeOccluderRefs[id] = occluder;
            if (frontOccluderRoot != null)
                activeOccluderRootRefs[id] = frontOccluderRoot;
            else
                activeOccluderRootRefs.Remove(id);
        }
        else
        {
            activeOccluders.Remove(id);
            activeOccluderRefs.Remove(id);
            activeOccluderRootRefs.Remove(id);
        }

        bool previousOccluded = currentOccluded;
        RefreshOccluderStateFromActiveSet();
        ApplyMaterialStateIfNeeded(!materialStateKnown || previousOccluded != currentOccluded);

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitOcclusionMaterialReceiver] {name} <- {occluder.name}, root={(frontOccluderRoot != null ? GetPath(frontOccluderRoot.transform) : "NULL")}, " +
                $"isOccluded={isOccluded}, active={activeOccluderCount}, final={currentOccluded}, renderers={resolvedRendererCount}",
                this
            );
        }
    }

    public void SetOccludedBy(MonoBehaviour occluder, bool isOccluded)
    {
        EnforceRuntimeAuthorityMaskMode();

        if (occluder == null)
            return;

        int id = occluder.GetInstanceID();

        lastSetOccluderName = occluder.name;
        lastSetOccluderPath = GetPath(occluder.transform);
        lastSetOccluderIsOccluded = isOccluded;
        lastSetOccluderFrame = Time.frameCount;

        if (isOccluded)
        {
            activeOccluders.Add(id);
            activeOccluderRefs[id] = occluder;
            GameObject fallbackRoot = ResolveFrontOccluderRootForLedger(occluder);
            if (fallbackRoot != null)
                activeOccluderRootRefs[id] = fallbackRoot;
        }
        else
        {
            activeOccluders.Remove(id);
            activeOccluderRefs.Remove(id);
            activeOccluderRootRefs.Remove(id);
        }

        bool previousOccluded = currentOccluded;
        RefreshOccluderStateFromActiveSet();

        // V26: only bridge materials when the ledger state actually changes.
        // Front triggers may re-notify true every evaluation; those repeated notifications must not
        // keep re-publishing materials or fight Spine initialization.
        ApplyMaterialStateIfNeeded(!materialStateKnown || previousOccluded != currentOccluded);

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitOcclusionMaterialReceiver] {name} <- {occluder.name}, " +
                $"isOccluded={isOccluded}, active={activeOccluderCount}, final={currentOccluded}, renderers={resolvedRendererCount}",
                this
            );
        }
    }


    private void ClearOccluderState()
    {
        activeOccluders.Clear();
        activeOccluderRefs.Clear();
        activeOccluderRootRefs.Clear();
        activeOccluderCount = 0;
        // 同上：GPU 模式下没有「不被遮挡」这个材质状态，合成材质常驻。
        currentOccluded = SkyPrisonOcclusionMode.UseGpuDepthOcclusion;
        activeOccluderDebugList = "";
        lastSetOccluderName = "";
        lastSetOccluderPath = "";
        lastSetOccluderIsOccluded = false;
        lastSetOccluderFrame = -1;
    }

    private void RefreshOccluderStateFromActiveSet()
    {
        List<int> invalidIds = null;
        foreach (int id in activeOccluders)
        {
            if (!activeOccluderRefs.TryGetValue(id, out MonoBehaviour mb) || mb == null || !mb.isActiveAndEnabled)
            {
                if (invalidIds == null)
                    invalidIds = new List<int>();
                invalidIds.Add(id);
            }
        }

        if (invalidIds != null)
        {
            for (int i = 0; i < invalidIds.Count; i++)
            {
                activeOccluders.Remove(invalidIds[i]);
                activeOccluderRefs.Remove(invalidIds[i]);
                activeOccluderRootRefs.Remove(invalidIds[i]);
            }
        }

        activeOccluderCount = activeOccluders.Count;
        // GPU 深度路径下合成材质必须常驻，不能等 CPU 授权。
        //
        // activeOccluders 唯一的填充者是 SkyPrisonTerrainDecorationFrontOccluderTrigger，
        // 而 GPU 模式下那个触发器已经整体 early return 了 —— 它同时承担着「逐三角面求交」
        // 和「把合成材质换上角色」两件事，砍掉求交时把材质切换也一起砍没了。
        // 结果是合成着色器根本没被绑定过，里面那套逐像素深度判定写得再对也没在运行
        // （诊断模式 4 一片空白就是这么来的；「帧率变得非常流畅」也是因为什么都没算）。
        //
        // 深度路径的判定已经完全下放到逐像素，本来就不需要 CPU 先判断「有没有被挡」，
        // 材质常驻即可，被挡与否由着色器自己决定。
        currentOccluded = SkyPrisonOcclusionMode.UseGpuDepthOcclusion || activeOccluderCount > 0;
        activeOccluderDebugList = BuildActiveOccluderDebugList();
    }

    private string BuildActiveOccluderDebugList()
    {
        if (activeOccluders.Count == 0)
            return "";

        string result = "";
        bool first = true;
        foreach (int id in activeOccluders)
        {
            if (!first)
                result += " | ";
            first = false;

            if (activeOccluderRefs.TryGetValue(id, out MonoBehaviour mb) && mb != null)
            {
                result += $"{mb.name}#{id} <= {GetPath(mb.transform)}";
                if (activeOccluderRootRefs.TryGetValue(id, out GameObject root) && root != null)
                    result += $" | root={GetPath(root.transform)}";
            }
            else
                result += $"MISSING#{id}";
        }

        return result;
    }

    private void RebuildRendererCacheInternal(bool reapply)
    {
        resolvedRenderers.Clear();
        cachedNormalMaterials.Clear();

        ResolveRenderer();
        CacheNormalMaterialsIfNeeded();

        if (!reapply)
            return;

        if (currentOccluded)
            ApplyOcclusionCompositeMaterials(true);
        else
            ApplyNormalMaterials(true);
    }

    /// <summary>
    /// 给非 Character 类型（走 3D 通道的场景物）用——UnitDefinitionRuntimeApplier 里
    /// 自动套阵营全息色的那条路径（ApplyHiddenOutlineColorForFaction）只在
    /// defineType==Character 时才跑，场景物这类单位需要自己在绑定完渲染器之后调这个
    /// 方法把颜色补上，不然全息色会一直停在运行时兜底材质的写死默认值（浅蓝）。
    /// 直接改当前实际挂在渲染器上的材质实例（不是 occlusionCompositeMaterials 那个
    /// 序列化字段——3D通道走的是运行时生成的合成材质，两者不是同一份）。
    /// </summary>
    public void ApplyHologramFillColor(Color color)
    {
        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                    continue;

                if (mat.HasProperty(UseHologramFillDefaultId))
                    mat.SetFloat(UseHologramFillDefaultId, 1f);
                if (mat.HasProperty(HologramFillColorId))
                    mat.SetColor(HologramFillColorId, color);
            }
        }
    }

    /// <summary>
    /// 同样是给非 Character 类型（3D 通道场景物）用——这类道具的贴图alpha从来不是
    /// 透明度语义，套 SpineOcclusionComposite 的镂空clip逻辑会把alpha低的区域裁成
    /// 黑洞（贴图上莫名其妙的透明块）。强制着色器把alpha当成恒定1处理，绕开整条
    /// 镂空判定——3D道具本来就该是完全不透明的。
    /// </summary>
    public void ApplyForceOpaqueAlpha(bool force)
    {
        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat != null && mat.HasProperty(Force3DPropOpaqueAlphaId))
                    mat.SetFloat(Force3DPropOpaqueAlphaId, force ? 1f : 0f);
            }
        }
    }

    /// <summary>
    /// 3D通道道具是有体积的实体几何体，不是没有厚度的Spine精灵——NormalBody这条Pass
    /// 原本"永远不裁背面/永远不测深度"这套状态对精灵没有副作用（精灵是没有厚度的
    /// 平面），套在实体网格上会导致自身内部结构（比如箱子的斜向撑木）没法正确前后
    /// 排序，表现为穿模。3D道具切成背面剔除+正常深度测试+写深度，恢复成实体几何体
    /// 该有的排序方式；被真实遮挡物挡住的部分交给 shader 里新增的
    /// HologramOverlay3D 那条 Pass 单独补上全息，两条Pass分工，不冲突。
    /// </summary>
    public void ApplyOpaqueGeometryRenderState(bool enable)
    {
        const float cullBack = 2f;   // UnityEngine.Rendering.CullMode.Back
        const float cullOff = 0f;    // UnityEngine.Rendering.CullMode.Off
        const float zWriteOn = 1f;
        const float zWriteOff = 0f;
        const float zTestLEqual = 4f; // UnityEngine.Rendering.CompareFunction.LEqual
        const float zTestAlways = 8f; // UnityEngine.Rendering.CompareFunction.Always

        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                    continue;

                bool hasCull = mat.HasProperty(CullModeId);
                bool hasZWrite = mat.HasProperty(ZWriteModeId);
                bool hasZTest = mat.HasProperty(ZTestModeId);

                if (hasCull)
                    mat.SetFloat(CullModeId, enable ? cullBack : cullOff);
                if (hasZWrite)
                    mat.SetFloat(ZWriteModeId, enable ? zWriteOn : zWriteOff);
                if (hasZTest)
                    mat.SetFloat(ZTestModeId, enable ? zTestLEqual : zTestAlways);
            }
        }
    }

    [ContextMenu("Auto Find Renderer")]
    public void ResolveRenderer()
    {
        resolvedRenderers.Clear();
        rejectedRendererCount = 0;
        receiverRootPath = GetPath(transform);
        lastRejectedRenderer = "";

        if (targetRenderer == null && autoFindRenderer)
        {
            Transform visualRoot = FindDeepChild(transform, "VisualRoot");
            if (visualRoot != null)
            {
                Transform spineGo = FindDeepChild(visualRoot, "Spine GameObject");
                if (spineGo != null)
                    targetRenderer = spineGo.GetComponentInChildren<Renderer>(true);

                if (targetRenderer == null)
                    targetRenderer = FindFirstUsableRendererForMaterialSwap(visualRoot);
            }

            if (targetRenderer == null)
                targetRenderer = FindFirstUsableRendererForMaterialSwap(transform);
        }

        AddRenderer(resolvedRenderers, targetRenderer);

        if (extraTargetRenderers != null)
        {
            for (int i = 0; i < extraTargetRenderers.Length; i++)
                AddRenderer(resolvedRenderers, extraTargetRenderers[i]);
        }

        if (autoFindRenderer && autoFindAllRenderers)
        {
            Transform visualRoot = FindDeepChild(transform, "VisualRoot");
            Transform searchRoot = visualRoot != null ? visualRoot : transform;
            Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsRendererAllowedForMaterialSwap(renderers[i]))
                    AddRenderer(resolvedRenderers, renderers[i]);
            }
        }

        resolvedRendererCount = resolvedRenderers.Count;
        CacheNormalMaterialsIfNeeded();
    }

    private Renderer FindFirstUsableRendererForMaterialSwap(Transform root)
    {
        if (root == null)
            return null;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (IsRendererAllowedForMaterialSwap(renderers[i]))
                return renderers[i];
        }

        return null;
    }

    private bool IsRendererAllowedForMaterialSwap(Renderer renderer)
    {
        if (renderer == null)
            return false;

        if (!IsRendererInsideThisReceiverBoundary(renderer))
            return false;

        string path = GetPathLower(renderer.transform);

        if (path.Contains("proxy") ||
            path.Contains("shadow") ||
            path.Contains("canvas") ||
            path.Contains("ui") ||
            path.Contains("health") ||
            path.Contains("bar") ||
            path.Contains("trigger") ||
            path.Contains("collider"))
            return false;

        Material[] mats = renderer.sharedMaterials;
        return mats != null && mats.Length > 0;
    }

    private static string GetPathLower(Transform t)
    {
        if (t == null)
            return "";

        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path.ToLowerInvariant();
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "NULL";

        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }

    private void AddRenderer(List<Renderer> list, Renderer renderer)
    {
        if (list == null || renderer == null)
            return;

        if (!IsRendererInsideThisReceiverBoundary(renderer))
            return;

        if (!list.Contains(renderer))
            list.Add(renderer);
    }

    private bool IsRendererInsideThisReceiverBoundary(Renderer renderer)
    {
        if (!isolateRendererSearchPerReceiver)
            return true;

        if (renderer == null)
            return false;

        if (rejectRendererOnReceiverRoot && renderer.transform == transform && renderer != targetRenderer)
        {
            RejectRenderer(renderer, "Renderer is on receiver root. Root SpriteRenderer should not be auto-managed.");
            return false;
        }

        if (!rejectRenderersOwnedByOtherReceiver)
            return true;

        // 首先只允许管理本 Receiver 所在单位 Transform 子树内的 Renderer。
        // 这一条才是最硬的单位边界。它不会被同一单位身上的其它 Receiver 误伤。
        if (!renderer.transform.IsChildOf(transform) && renderer.transform != transform)
        {
            RejectRenderer(renderer, "Renderer is outside this receiver transform subtree.");
            return false;
        }

        MonoBehaviour[] parents = renderer.GetComponentsInParent<MonoBehaviour>(true);

        for (int i = 0; i < parents.Length; i++)
        {
            MonoBehaviour mb = parents[i];
            if (mb == null)
                continue;

            if (!(mb is IOcclusionStateReceiver))
                continue;

            if (ReferenceEquals(mb, this))
                continue;

            // 同一个角色根节点上允许存在多个 Receiver：
            // UnitOcclusionMaterialReceiver / UnitOccludedOutlineProxyGate 都是同一单位的接收器。
            // V8B 把这里误判成“其它单位 Receiver”，会导致 Spine Renderer 被拒绝，透明材质无法切换。
            if (mb.transform == transform)
                continue;

            // 如果 Renderer 子树里存在另一个 Receiver 根，说明它属于嵌套的另一个单位，拒绝跨单位管理。
            if (renderer.transform.IsChildOf(mb.transform) || renderer.transform == mb.transform)
            {
                RejectRenderer(renderer, $"Owned by nested other receiver: {GetPath(mb.transform)}");
                return false;
            }
        }

        return true;
    }

    private void RejectRenderer(Renderer renderer, string reason)
    {
        rejectedRendererCount++;
        lastRejectedRenderer = renderer != null ? $"{GetPath(renderer.transform)} | {reason}" : reason;

        if (debugLogs && renderer != null)
            Debug.Log($"[UnitOcclusionMaterialReceiver] Reject renderer: {lastRejectedRenderer}", this);
    }

    private void CacheNormalMaterialsIfNeeded()
    {
        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            if (!cachedNormalMaterials.ContainsKey(renderer))
                cachedNormalMaterials[renderer] = CloneMaterials(renderer.sharedMaterials);
        }
    }

    private void ApplyMaterialStateIfNeeded(bool force)
    {
        EnforceRuntimeAuthorityMaskMode();

        bool overwritten = !force && materialStateKnown && materialStateOccluded == currentOccluded
            ? IsCurrentMaterialStateOverwritten()
            : false;

        if (!force && materialStateKnown && materialStateOccluded == currentOccluded && !overwritten)
            return;

        if (currentOccluded)
            ApplyOcclusionCompositeMaterials(force || overwritten);
        else
            ApplyNormalMaterials(force || overwritten);

        materialStateKnown = true;
        materialStateOccluded = currentOccluded;
    }

    private bool IsCurrentMaterialStateOverwritten()
    {
        if (resolvedRenderers.Count == 0)
            ResolveRenderer();

        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            Material[] expected = currentOccluded
                ? GetOcclusionMaterialSet(renderer)
                : GetNormalMaterialSet(renderer);

            if (!MaterialArraysSame(renderer.sharedMaterials, expected))
                return true;
        }

        return false;
    }

    private static bool MaterialArraysSame(Material[] a, Material[] b)
    {
        if (a == null || b == null)
            return a == b;

        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private void ApplyNormalMaterials(bool force)
    {
        ClearPerUnitMaskStateBecauseNotOccluded();

        if (resolvedRenderers.Count == 0)
            ResolveRenderer();

        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            Material[] targetSet = GetNormalMaterialSet(renderer);
            ApplyMaterialSet(renderer, targetSet, force, "Normal");
        }
    }


    private void ClearPerUnitMaskStateBecauseNotOccluded()
    {
        // The receiver-managed per-unit body mask must never leak into a non-occluded state.
        // This keeps Debug/Inspector and material binding honest after an occluder leaves.
        if (currentOccluded && activeOccluderCount > 0)
            return;

        perUnitMaskStatus = "not occluded: no authorized roots rendered";
        perUnitMaskOccluderRoots = "";
    }

    private void ApplyOcclusionCompositeMaterials(bool force)
    {
        EnforceRuntimeAuthorityMaskMode();

        if (resolvedRenderers.Count == 0)
            ResolveRenderer();

        RenderTexture perUnitMask = null;
        if (usePerUnitOccluderMask && !forceDisableReceiverManagedPerUnitMask)
        {
            perUnitMask = SkyPrisonPerUnitOccluderMaskRenderer.RenderMaskForReceiver(
                this,
                CollectActiveOccluderBehaviours(),
                perUnitMaskCameraTemplate,
                perUnitMaskWidth,
                perUnitMaskHeight,
                perUnitMaskTempLayer,
                ref perUnitMaskStatus,
                ref perUnitMaskOccluderRoots);
        }
        else
        {
            perUnitMaskStatus = useRuntimeAuthorityMaskBinding
                ? "runtime authority: Receiver-managed per-unit mask is off. BindingAuthority owns _OcclusionTex."
                : (forceDisableReceiverManagedPerUnitMask
                    ? "disabled: Receiver-managed per-unit mask camera is off. Fixed outline/mask chain owns RTs."
                    : "disabled");
            perUnitMaskOccluderRoots = "";
        }

        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            Material[] targetSet = GetOcclusionMaterialSet(renderer);
            if (usePerUnitOccluderMask && !forceDisableReceiverManagedPerUnitMask && perUnitMask != null)
            {
                targetSet = GetOrCreatePerUnitCompositeInstances(renderer, targetSet, perUnitMask);
            }
            else
            {
                // Alpha-hybrid repair:
                // In the current project route, ScreenSpaceOutlineRTManager / BindingAuthority owns
                // the mask RT. Runtime fallback composite materials created by this receiver do not
                // automatically receive that material property, so bind the global mask here.
                ApplyRuntimeAuthorityMaskTextureToMaterials(targetSet);
            }

            ApplyMaterialSet(renderer, targetSet, force, "Composite");
        }
    }

    private List<MonoBehaviour> CollectActiveOccluderBehaviours()
    {
        List<MonoBehaviour> result = new List<MonoBehaviour>();
        foreach (KeyValuePair<int, MonoBehaviour> pair in activeOccluderRefs)
        {
            MonoBehaviour mb = pair.Value;
            if (mb == null || !mb.isActiveAndEnabled)
                continue;

            result.Add(mb);
        }

        return result;
    }

    private Material[] GetOrCreatePerUnitCompositeInstances(Renderer renderer, Material[] sourceSet, RenderTexture perUnitMask)
    {
        if (renderer == null || sourceSet == null || sourceSet.Length == 0)
            return sourceSet;

        bool needsCreate = true;
        if (perUnitCompositeInstanceCache.TryGetValue(renderer, out Material[] cached) && cached != null && cached.Length == sourceSet.Length)
        {
            needsCreate = false;
            for (int i = 0; i < cached.Length; i++)
            {
                if (cached[i] == null)
                {
                    needsCreate = true;
                    break;
                }
            }
        }

        if (needsCreate)
        {
            cached = new Material[sourceSet.Length];
            for (int i = 0; i < sourceSet.Length; i++)
            {
                Material src = sourceSet[i];
                if (src == null)
                    continue;

                Material mat = new Material(src)
                {
                    name = src.name + "_PerUnitMask_" + GetInstanceID()
                };
                cached[i] = mat;
            }

            perUnitCompositeInstanceCache[renderer] = cached;
        }

        ApplyMaskTextureToMaterials(cached, perUnitMask);
        return cached;
    }

    /// <summary>
    /// V29: ScreenSpaceOutlineRTManager can now provide a receiver-owned runtime mask.
    /// This is the critical unit-level authority: the composite material must sample this
    /// receiver's own mask, not the old global/channel _OcclusionTex.
    /// </summary>
    public void SetRuntimeAuthorityMaskTexture(Texture mask)
    {
        runtimeAuthorityMaskTexture = mask;

        if (mask == null)
        {
            perUnitMaskStatus = "runtime authority: receiver mask cleared";
            return;
        }

        if (resolvedRenderers.Count == 0)
            ResolveRenderer();

        int appliedRendererCount = 0;
        for (int i = 0; i < resolvedRenderers.Count; i++)
        {
            Renderer renderer = resolvedRenderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            ApplyRuntimeAuthorityMaskTextureToMaterials(mats);
            appliedRendererCount++;
        }

        perUnitMaskStatus = "runtime authority: bound receiver mask " + mask.name + " to renderers=" + appliedRendererCount;
    }

    public void ClearRuntimeAuthorityMaskTexture()
    {
        SetRuntimeAuthorityMaskTexture(null);
    }

    private void ApplyRuntimeAuthorityMaskTextureToMaterials(Material[] mats)
    {
        if (mats == null || mats.Length == 0)
            return;

        Texture mask = runtimeAuthorityMaskTexture;

        // Prefer this receiver's own runtime authority mask. Fall back to globals only
        // when the manager has not provided a per-receiver mask yet.
        if (mask == null)
            mask = Shader.GetGlobalTexture(OcclusionTexId);

        // Fallbacks for older mask/outline chains.
        if (mask == null)
            mask = Shader.GetGlobalTexture(DefaultMaskTexId);
        if (mask == null)
            mask = Shader.GetGlobalTexture(LegacyOcclusionMaskId);
        if (mask == null)
            mask = Shader.GetGlobalTexture(LegacyOcclusionMaskTexId);

        if (mask == null)
        {
            lastWarning = "Runtime authority mask texture not found from global _OcclusionTex/_MaskTex/_OcclusionMask. Composite may be invisible.";
            return;
        }

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null)
                continue;

            mat.SetTexture(OcclusionTexId, mask);
            mat.SetTexture(DefaultMaskTexId, mask);

            if (setLegacyMaskTextureProperties)
            {
                mat.SetTexture(LegacyOcclusionMaskId, mask);
                mat.SetTexture(LegacyOcclusionMaskTexId, mask);
            }

            EnsureCompositeDefaults(mat);
        }

        perUnitMaskStatus = runtimeAuthorityMaskTexture != null ? ("runtime authority: bound receiver mask " + runtimeAuthorityMaskTexture.name) : "runtime authority: bound global mask texture to composite materials";
    }

    private void ApplyMaskTextureToMaterials(Material[] mats, RenderTexture perUnitMask)
    {
        if (mats == null || perUnitMask == null)
            return;

        int configuredId = string.IsNullOrWhiteSpace(perUnitMaskTextureProperty)
            ? DefaultMaskTexId
            : Shader.PropertyToID(perUnitMaskTextureProperty);

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null)
                continue;

            // V12: Spine/SpineOcclusionComposite actually samples _OcclusionTex.
            // Do not rely on Material.HasProperty here: _OcclusionTex may be declared only in CGPROGRAM
            // and not shown in the Properties block, but SetTexture still binds the runtime texture.
            mat.SetTexture(configuredId, perUnitMask);
            mat.SetTexture(OcclusionTexId, perUnitMask);

            // Keep older UI/diagnostic material names alive. This does not change the shader path;
            // it only prevents historical materials from losing their mask input.
            if (configuredId != DefaultMaskTexId)
                mat.SetTexture(DefaultMaskTexId, perUnitMask);

            if (setLegacyMaskTextureProperties)
            {
                mat.SetTexture(LegacyOcclusionMaskId, perUnitMask);
                mat.SetTexture(LegacyOcclusionMaskTexId, perUnitMask);
            }
        }
    }

    private Material[] GetNormalMaterialSet(Renderer renderer)
    {
        if (renderer == targetRenderer && normalMaterials != null && normalMaterials.Length > 0)
            return normalMaterials;

        if (cachedNormalMaterials.TryGetValue(renderer, out Material[] cached) && cached != null && cached.Length > 0)
            return cached;

        return renderer != null ? renderer.sharedMaterials : null;
    }

    private Material[] GetOcclusionMaterialSet(Renderer renderer)
    {
        if (renderer == null)
            return null;

        Material[] current = renderer.sharedMaterials;
        int slotCount = current != null && current.Length > 0 ? current.Length : 1;

        if (occlusionCompositeMaterials != null && occlusionCompositeMaterials.Length > 0)
        {
            if (occlusionCompositeMaterials.Length == slotCount)
                return occlusionCompositeMaterials;

            if (occlusionCompositeMaterials.Length == 1 && slotCount > 1)
            {
                Material[] expanded = new Material[slotCount];
                for (int i = 0; i < expanded.Length; i++)
                    expanded[i] = occlusionCompositeMaterials[0];

                return expanded;
            }

            return occlusionCompositeMaterials;
        }

        if (!createRuntimeCompositeFallback)
        {
            lastWarning = $"No occlusionCompositeMaterials for {renderer.name}.";
            return null;
        }

        return GetOrCreateRuntimeCompositeSet(renderer, slotCount);
    }

    private Material[] GetOrCreateRuntimeCompositeSet(Renderer renderer, int slotCount)
    {
        if (runtimeCompositeCache.TryGetValue(renderer, out Material[] cached) && cached != null && cached.Length == slotCount)
            return cached;

        Shader compositeShader = Shader.Find("Spine/SpineOcclusionComposite");
        if (compositeShader == null)
        {
            lastWarning = "Shader.Find(\"Spine/SpineOcclusionComposite\") failed. Make sure the shader is included in Build.";
            if (debugLogs)
                Debug.LogWarning("[UnitOcclusionMaterialReceiver] " + lastWarning, this);
            return null;
        }

        Material[] source = renderer.sharedMaterials;
        Material[] result = new Material[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            Material src = source != null && i < source.Length ? source[i] : null;
            Material mat = new Material(compositeShader)
            {
                name = (src != null ? src.name : renderer.name) + runtimeCompositeMaterialSuffix
            };

            CopyCommonProperties(src, mat);
            EnsureCompositeDefaults(mat);
            result[i] = mat;
        }

        runtimeCompositeCache[renderer] = result;

        if (debugLogs)
            Debug.Log($"[UnitOcclusionMaterialReceiver] Created runtime composite fallback for {renderer.name}: {FormatMaterialArray(result)}", this);

        return result;
    }

    private static void CopyCommonProperties(Material src, Material dst)
    {
        if (dst == null)
            return;

        if (src == null)
            return;

        if (src.HasProperty(MainTexId) && dst.HasProperty(MainTexId))
        {
            dst.SetTexture(MainTexId, src.GetTexture(MainTexId));
        }
        else if (dst.HasProperty(MainTexId))
        {
            // src 不是走 _MainTex 的老式着色器（比如 URP/Lit 用 _BaseMap）时，
            // Material.mainTexture 是 Unity 按着色器自己声明的"主贴图"槽位解析的，
            // 不管具体属性名叫什么都能拿到——不加这个兜底，3D 通道的道具（用
            // URP/Lit，没有 _MainTex）套上这套遮挡合成材质就会因为贴图没拷贝过去，
            // 直接用着色器默认的白贴图渲染成一整块白色。
            Texture mainTex = src.mainTexture;
            if (mainTex != null)
                dst.SetTexture(MainTexId, mainTex);
        }

        if (src.HasProperty(TintColorId) && dst.HasProperty(TintColorId))
            dst.SetColor(TintColorId, src.GetColor(TintColorId));

        if (src.HasProperty(StraightAlphaInputId) && dst.HasProperty(StraightAlphaInputId))
            dst.SetFloat(StraightAlphaInputId, src.GetFloat(StraightAlphaInputId));
    }

    // 3D 通道场景物的这几个属性覆盖——之前是靠外部（UnitDefinitionRuntimeBinder）
    // 调用 ApplyXxx 方法事后去改已经存在的材质实例，跟"合成材质到底什么时候真正
    // 创建/切换出来"之间是一场时序竞争：外部调用早于材质真正切到合成状态的话，
    // 属性设置会全部落空（改的是当时还在用的正常材质，合成材质创建出来时又是一份
    // 全新的、没被改过的实例）。实测过——箱子最终稳定用的合成材质，属性确实全是
    // 着色器默认值，没有一次外部调用生效过。改成在这里（合成材质真正被创建/刷新
    // 默认值的这一刻）直接烧进去，不管创建时机是什么时候，材质一出生就带着正确的
    // 值，不用赌时序。
    private bool _is3DPropMode;
    private bool _hasHologramFillColorOverride;
    private Color _hologramFillColorOverride;
    private float? _debugSceneDepthMode;

    public void SetIs3DPropMode(bool value)
    {
        _is3DPropMode = value;
        if (value)
        {
            if (!Active3DPropReceivers.Contains(this))
                Active3DPropReceivers.Add(this);
        }
        else
        {
            Active3DPropReceivers.Remove(this);
        }
    }

    /// <summary>一次性诊断用——4=判定结果本身（绿=判为被挡，红=判为在前，越亮
    /// |diff|越大），确认完记得清掉调用方那一行。</summary>
    public void SetDebugSceneDepthMode(float mode) => _debugSceneDepthMode = mode;

    public void SetHologramFillColorOverride(Color color)
    {
        _hologramFillColorOverride = color;
        _hasHologramFillColorOverride = true;
    }

    private bool _loggedEnsureCompositeDefaultsOnce;

    private void EnsureCompositeDefaults(Material mat)
    {
        if (mat == null)
            return;

        // 一次性诊断——只打一次（不是每帧），确认这个函数到底有没有真的在这份材质上
        // 跑过，以及跑的时候 _debugSceneDepthMode/_is3DPropMode 是什么值。今天已经
        // 反复踩过"以为写进去了、其实这条路径没被调用/调用时机比材质创建晚"这个坑。
        if (!_loggedEnsureCompositeDefaultsOnce)
        {
            _loggedEnsureCompositeDefaultsOnce = true;
            Debug.Log($"[UnitOcclusionMaterialReceiver] EnsureCompositeDefaults ran -> {name}, mat={mat.name}#{mat.GetInstanceID()}, " +
                      $"is3DPropMode={_is3DPropMode}, debugSceneDepthMode={(_debugSceneDepthMode.HasValue ? _debugSceneDepthMode.Value.ToString() : "null")}, " +
                      $"hasSceneDepthDebugProp={mat.HasProperty(SceneDepthDebugId)}, frame={Time.frameCount}", this);

            // 2026-08-17：确认 HologramOverlay3D 那条 Pass 到底有没有被这份材质实例真正
            // 拥有——如果 shader 编译时这条 Pass 出错（比如 target 3.0 在某些变体上失败），
            // Unity 有可能整条 Pass 直接从 passCount 里消失，material.FindPass 找不到，
            // 而不会报一个显眼的、能中断编译的错误。直接枚举实际 passCount 和每条
            // Pass 的名字，不用 Frame Debugger 肉眼找。
            int passCount = mat.passCount;
            var passNames = new System.Text.StringBuilder();
            for (int p = 0; p < passCount; p++)
            {
                if (p > 0) passNames.Append(" | ");
                passNames.Append(p).Append(':').Append(mat.GetPassName(p));
            }
            int overlayPassIndex = mat.FindPass("HologramOverlay3D");
            bool overlayEnabled = overlayPassIndex >= 0 && mat.GetShaderPassEnabled("HologramOverlay3D");
            Debug.Log($"[UnitOcclusionMaterialReceiver] PassAudit -> {name}, shader={mat.shader.name}, " +
                      $"passCount={passCount}, passes=[{passNames}], " +
                      $"HologramOverlay3D index={overlayPassIndex}, enabled={overlayEnabled}", this);
        }

        if (_is3DPropMode)
        {
            if (mat.HasProperty(Force3DPropOpaqueAlphaId))
                mat.SetFloat(Force3DPropOpaqueAlphaId, 1f);
            if (mat.HasProperty(CullModeId))
                mat.SetFloat(CullModeId, 2f); // UnityEngine.Rendering.CullMode.Back
            if (mat.HasProperty(ZWriteModeId))
                mat.SetFloat(ZWriteModeId, 1f); // On
            if (mat.HasProperty(ZTestModeId))
                mat.SetFloat(ZTestModeId, 4f); // UnityEngine.Rendering.CompareFunction.LEqual

            // 2026-08-17：改回单点根节点锚点，跟角色完全同一套判定。
            //
            // 之前关掉这个开关、改用每个像素自己的世界坐标，是为了解决"高箱子顶部
            // 穿出遮挡物"的问题——但代价是箱子朝相机这一面的表面像素天生比根节点离
            // 相机更近，跟落地深度图里"遮挡物拍扁成单点"的语义对不上：实测箱子根节点
            // 深度差有 +6.39（远超阈值，应该判定为完全遮挡），但用逐像素表面坐标算出
            // 来的差值被这个"表面比根节点近多少"的偏移吃掉，导致真正被挡住时全息完全
            // 不触发（"接着前面的事件"这次排查到的就是这个）。
            //
            // 这个项目里 3D 单位要的效果本来就是"整体一起显示/隐藏，跟角色一样"，不是
            // 逐像素分区域遮挡——用回单点根节点锚点正好是这个语义，跟角色一致，不用
            // 单独再调一套阈值。
            if (mat.HasProperty(UseRootAnchorDepthId))
                mat.SetFloat(UseRootAnchorDepthId, 1f);
        }

        if (_hasHologramFillColorOverride)
        {
            if (mat.HasProperty(UseHologramFillDefaultId))
                mat.SetFloat(UseHologramFillDefaultId, 1f);
            if (mat.HasProperty(HologramFillColorId))
                mat.SetColor(HologramFillColorId, _hologramFillColorOverride);
        }

        // V28: alpha hybrid needs the compensation layer to be visible.
        // Older beta/depth-only receivers forced this to 0, which made the composite pass invisible.
        if (mat.HasProperty(OccludedAlphaId))
            mat.SetFloat(OccludedAlphaId, 0.45f);

        if (mat.HasProperty(OccludedBrightnessId))
            mat.SetFloat(OccludedBrightnessId, 0.75f);

        if (mat.HasProperty(OccludedSaturationId))
            mat.SetFloat(OccludedSaturationId, 0.65f);

        if (mat.HasProperty(OccludedTintId))
            mat.SetColor(OccludedTintId, new Color(0.65f, 0.85f, 1f, 1f));

        if (mat.HasProperty(MaskThresholdId))
            mat.SetFloat(MaskThresholdId, 0.5f);

        if (mat.HasProperty(MaskSoftnessId))
            mat.SetFloat(MaskSoftnessId, 0.12f);

        if (mat.HasProperty(DebugMaskModeId))
            mat.SetFloat(DebugMaskModeId, 0f);

        if (mat.HasProperty(RawDiscardThresholdId))
            mat.SetFloat(RawDiscardThresholdId, 0.01f);

        // 2026-07-19：描边走 GetHiddenEdge() 分支——只吃 _OcclusionTex，跟裁切读的是
        // 同一张、这条生产链路本来就在写的贴图（SetRuntimeAuthorityMaskTexture 每帧
        // 逐单位推进来）。_SkyPrison_UseCleanCharacterOutlineTex=1 会切到需要
        // _SkyPrison_CleanCharacterOutlineTex 的另一条分支，但从没有任何系统写过那张
        // 贴图（永远采样黑图=0），描边永远算不出来，只会看到裁切、没有描边。
        if (mat.HasProperty(UseCleanCharacterOutlineTexId))
            mat.SetFloat(UseCleanCharacterOutlineTexId, 0f);

        // 2026-07-19：描边方案（GetHiddenEdge 逐网格找边缘）和全屏 RawImage 方案都已经
        // 放弃，现在固定走全息点阵填充（_SkyPrison_UseHologramFill 分支）。
        // _SkyPrison_EnableHiddenOutline 只在旧的描边分支里还有意义，留 1 不影响当前
        // 显示效果，纯粹是历史遗留没清干净，不必纠结。
        if (mat.HasProperty(EnableHiddenOutlineId))
            mat.SetFloat(EnableHiddenOutlineId, 1f);

        // 2026-07-19：_SkyPrison_UseHologramFill / _SkyPrison_HologramSilhouetteAlpha
        // 之前只在 ApplyHiddenOutlineColorForFaction 里设置一次（材质刚绑定阵营色的
        // 那一刻），不是每帧强制写。如果材质实例是在某次调整这两个值之前就已经创建好
        // 的，改 shader 默认值/改调用方代码都不会追溯生效，材质会一直卡着创建时刻的
        // 旧值——这是今天反复踩过的同一类坑（"安全默认值只在创建时写一次，不同实例
        // 创建时机不同导致表现不一致"），这次补齐，每帧强制写，不再依赖材质创建时机。
        if (mat.HasProperty(UseHologramFillDefaultId))
            mat.SetFloat(UseHologramFillDefaultId, 1f);
        if (mat.HasProperty(HologramSilhouetteAlphaId))
            mat.SetFloat(HologramSilhouetteAlphaId, 0f);

        if (_debugSceneDepthMode.HasValue && mat.HasProperty(SceneDepthDebugId))
            mat.SetFloat(SceneDepthDebugId, _debugSceneDepthMode.Value);
    }

    private void ApplyMaterialSet(Renderer renderer, Material[] targetSet, bool force, string label)
    {
        if (renderer == null || targetSet == null || targetSet.Length == 0)
        {
            if (renderer != null)
                lastWarning = $"Apply {label} skipped for {renderer.name}: targetSet is null/empty.";
            return;
        }

        Material[] current = renderer.sharedMaterials;
        bool same = current != null && current.Length == targetSet.Length;

        if (same)
        {
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != targetSet[i])
                {
                    same = false;
                    break;
                }
            }
        }

        if (!force && same)
            return;

        renderer.sharedMaterials = targetSet;

        string matName = targetSet.Length > 0 && targetSet[0] != null ? targetSet[0].name : "NULL";
        string shaderName = targetSet.Length > 0 && targetSet[0] != null && targetSet[0].shader != null ? targetSet[0].shader.name : "NULL";
        lastApply = $"Apply {label} -> {GetPath(renderer.transform)}, mat0={matName}, shader={shaderName}";

        // 诊断阶段这里改成过无条件 Debug.Log（不受debugLogs控制）——场景里所有 Spine
        // 单位的这套"检测材质被覆盖、自动修复"逻辑每帧都可能触发一次，无条件打印
        // 几分钟下来能把 Editor.log 刷到几个GB，实测已经把C盘写满过一次。诊断已经
        // 用完，改回受debugLogs控制。
        if (debugLogs)
            Debug.Log($"[UnitOcclusionMaterialReceiver] {lastApply} | force={force} | frame={Time.frameCount}", this);
    }

    private static Material[] CloneMaterials(Material[] source)
    {
        if (source == null)
            return null;

        Material[] clone = new Material[source.Length];
        for (int i = 0; i < source.Length; i++)
            clone[i] = source[i];

        return clone;
    }

    private Transform FindDeepChild(Transform root, string containsName)
    {
        if (root == null)
            return null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == containsName || t.name.Contains(containsName))
                return t;
        }

        return null;
    }

    private static class SkyPrisonPerUnitOccluderMaskRenderer
    {
        private const string DefaultMaskCameraName = "OcclusionMaskCamera";

        private static Camera runtimeCamera;
        private static readonly Dictionary<int, RenderTexture> maskByReceiverId = new Dictionary<int, RenderTexture>();
        private static readonly List<GameObjectLayerState> layerStates = new List<GameObjectLayerState>();
        private static readonly List<GameObjectActiveState> activeStates = new List<GameObjectActiveState>();
        private static readonly List<GameObject> resolvedRoots = new List<GameObject>();

        private struct GameObjectLayerState
        {
            public GameObject go;
            public int layer;
        }

        private struct GameObjectActiveState
        {
            public GameObject go;
            public bool activeSelf;
        }

        public static RenderTexture RenderMaskForReceiver(
            UnitOcclusionMaterialReceiver receiver,
            List<MonoBehaviour> activeOccluders,
            Camera templateCamera,
            int width,
            int height,
            int tempLayer,
            ref string status,
            ref string rootsDebug)
        {
            status = "";
            rootsDebug = "";

            if (receiver == null)
            {
                status = "receiver null";
                return null;
            }

            if (activeOccluders == null || activeOccluders.Count == 0)
            {
                status = "no active occluders";
                return null;
            }

            templateCamera = ResolveTemplateCamera(templateCamera);
            if (templateCamera == null)
            {
                status = "OcclusionMaskCamera not found";
                return null;
            }

            tempLayer = Mathf.Clamp(tempLayer, 0, 31);
            width = Mathf.Clamp(width, 64, 4096);
            height = Mathf.Clamp(height, 64, 4096);

            resolvedRoots.Clear();
            for (int i = 0; i < activeOccluders.Count; i++)
            {
                GameObject root = ResolveFrontOccluderRoot(activeOccluders[i]);
                if (root == null)
                    continue;

                if (!resolvedRoots.Contains(root))
                    resolvedRoots.Add(root);
            }

            if (resolvedRoots.Count == 0)
            {
                status = "active occluders have no FrontOccluderRoot";
                return null;
            }

            RenderTexture rt = GetOrCreateMask(receiver.GetInstanceID(), width, height);
            Camera cam = GetOrCreateRuntimeCamera(templateCamera);
            cam.CopyFrom(templateCamera);
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.cullingMask = 1 << tempLayer;

            layerStates.Clear();
            activeStates.Clear();

            for (int i = 0; i < resolvedRoots.Count; i++)
            {
                GameObject root = resolvedRoots[i];
                if (root == null)
                    continue;

                if (i > 0)
                    rootsDebug += " | ";
                rootsDebug += GetPath(root.transform);

                activeStates.Add(new GameObjectActiveState { go = root, activeSelf = root.activeSelf });
                if (!root.activeSelf)
                    root.SetActive(true);

                SetLayerRecursive(root, tempLayer, layerStates);
            }

            RenderTexture oldActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = oldActive;

            cam.Render();

            RestoreLayers(layerStates);
            RestoreActiveStates(activeStates);

            status = $"rendered roots={resolvedRoots.Count}, rt={rt.width}x{rt.height}, layer={tempLayer}";
            return rt;
        }

        private static Camera ResolveTemplateCamera(Camera explicitCamera)
        {
            if (explicitCamera != null)
                return explicitCamera;

            GameObject go = GameObject.Find(DefaultMaskCameraName);
            if (go != null)
            {
                Camera cam = go.GetComponent<Camera>();
                if (cam != null)
                    return cam;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam != null && cam.name == DefaultMaskCameraName)
                    return cam;
            }

            return null;
        }

        private static Camera GetOrCreateRuntimeCamera(Camera template)
        {
            if (runtimeCamera != null)
                return runtimeCamera;

            GameObject go = new GameObject("__SkyPrison_PerUnitOccluderMaskCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            runtimeCamera = go.AddComponent<Camera>();
            runtimeCamera.enabled = false;
            if (template != null)
                runtimeCamera.CopyFrom(template);
            return runtimeCamera;
        }

        private static RenderTexture GetOrCreateMask(int receiverId, int width, int height)
        {
            if (maskByReceiverId.TryGetValue(receiverId, out RenderTexture rt) && rt != null && rt.width == width && rt.height == height)
                return rt;

            if (rt != null)
                rt.Release();

            rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "SkyPrison_PerUnitOccluderMask_" + receiverId,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();
            maskByReceiverId[receiverId] = rt;
            return rt;
        }

        private static GameObject ResolveFrontOccluderRoot(MonoBehaviour occluder)
        {
            if (occluder == null)
                return null;

            System.Type type = occluder.GetType();
            System.Reflection.FieldInfo field = type.GetField(
                "frontOccluderRoot",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            if (field != null)
            {
                object value = field.GetValue(occluder);
                if (value is GameObject go && go != null)
                    return go;
                if (value is Transform t && t != null)
                    return t.gameObject;
            }

            Transform found = FindChildInAncestors(occluder.transform, "FrontOccluderRoot");
            return found != null ? found.gameObject : null;
        }

        private static Transform FindChildInAncestors(Transform start, string name)
        {
            Transform current = start;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                Transform found = FindDeepChildExact(current, name);
                if (found != null)
                    return found;
                current = current.parent;
            }

            return null;
        }

        private static Transform FindDeepChildExact(Transform root, string name)
        {
            if (root == null)
                return null;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }

            return null;
        }

        private static void SetLayerRecursive(GameObject root, int layer, List<GameObjectLayerState> states)
        {
            if (root == null)
                return;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject go = transforms[i].gameObject;
                states.Add(new GameObjectLayerState { go = go, layer = go.layer });
                go.layer = layer;
            }
        }

        private static void RestoreLayers(List<GameObjectLayerState> states)
        {
            if (states == null)
                return;

            for (int i = states.Count - 1; i >= 0; i--)
            {
                GameObject go = states[i].go;
                if (go != null)
                    go.layer = states[i].layer;
            }
        }

        private static void RestoreActiveStates(List<GameObjectActiveState> states)
        {
            if (states == null)
                return;

            for (int i = states.Count - 1; i >= 0; i--)
            {
                GameObject go = states[i].go;
                if (go != null && go.activeSelf != states[i].activeSelf)
                    go.SetActive(states[i].activeSelf);
            }
        }
    }

    private static string FormatMaterialArray(Material[] mats)
    {
        if (mats == null)
            return "NULL";

        if (mats.Length == 0)
            return "EMPTY";

        string s = "";
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (i > 0)
                s += ", ";

            s += m != null
                ? $"{m.name}/{(m.shader != null ? m.shader.name : "NULL_SHADER")}"
                : "NULL";
        }

        return s;
    }
}
