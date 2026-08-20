using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Spine.Unity;

// V26 - 2026-06-09: Runtime PlayerAuthority integration.
// Auto ensures SkyPrisonUnitRuntimeIdentity on character units at the definition binding source.
// Keeps PF/UnitDefinition assets untouched; runtime identity is a separate override layer.
// V25 - 2026-06-05: KeepOccludedProxyOnly. Bind real Spine source plus only OutlineProxy_*_Occluded required by current occlusion mask chain. Removed old normal OutlineProxy_* and old camera route.

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class UnitDefinitionRuntimeBinder : MonoBehaviour
{
    [Header("Runtime Binding")]
    [SerializeField] private UnitDefinition unitDefinitionAsset;

    [Header("Options")]
    [SerializeField] private bool applyDefinitionOnAwake = true;
    [SerializeField] private bool applyDefinitionOnEnable = false;
    [SerializeField] private bool debugLogs = false;

    [Header("Runtime Authority")]
    [Tooltip("角色单位绑定定义后，自动补 SkyPrisonUnitRuntimeIdentity。这里是运行时身份层，不修改 UnitDefinition 资产。")]
    [SerializeField] private bool autoEnsureRuntimeIdentityForCharacterUnits = true;

    [Header("Spine 4.3 Source Binding / V25 Keep Occluded Proxy Only")]
    [SerializeField] private bool autoBindSpine43Renderers = true;
    [SerializeField] private string spineRootName = "SpineRoot";
    [SerializeField] private string spineSourceNameContains = "Spine GameObject";

    [Header("3D 通道 Source Binding")]
    [SerializeField] private bool autoBindModel3D = true;
    [SerializeField] private string model3DRootName = "Model3DRoot";

    [Header("Occluded Proxy Binding - Required Current Baseline")]
    [SerializeField] private bool bindOccludedOutlineProxies = true;
    [SerializeField] private bool createMissingOccludedProxyComponents = true;
    [SerializeField] private string outlineProxyRootName = "OutlineProxyRoot";

    private static readonly string[] RequiredOccludedProxyNames =
    {
        "OutlineProxy_Player_Occluded",
        "OutlineProxy_Enemy_Occluded",
        "OutlineProxy_Item_Occluded",
        "OutlineProxy_Ally_Occluded",
    };

    public UnitDefinition UnitDefinitionAsset => unitDefinitionAsset;

    private void Awake()
    {
        RefreshSceneMarkerCache();
        EnsureRuntimeIdentityForCurrentDefinition();

        if (applyDefinitionOnAwake)
            ApplyDefinitionIfPossible();
        else
            RefreshVisualChannelBindingsIfPossible();
    }

    private void OnEnable()
    {
        RefreshSceneMarkerCache();
        EnsureRuntimeIdentityForCurrentDefinition();

        if (applyDefinitionOnEnable)
            ApplyDefinitionIfPossible();
        else
            RefreshVisualChannelBindingsIfPossible();

        if (Application.isPlaying)
            SkyPrisonVisionManager.Instance?.RegisterUnit(this);
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            SkyPrisonVisionManager.Instance?.UnregisterUnit(this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RefreshSceneMarkerCache();
        EnsureRuntimeIdentityForCurrentDefinition();

        if (!Application.isPlaying)
            RefreshVisualChannelBindingsIfPossible();
    }
#endif

    [ContextMenu("Apply Definition If Possible")]
    public void ApplyDefinitionIfPossible()
    {
        RefreshSceneMarkerCache();

        if (unitDefinitionAsset == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: UnitDefinition is null.", this);

            RefreshVisualChannelBindingsIfPossible();
            return;
        }

        UnitDefinitionRuntimeApplier applier = GetComponent<UnitDefinitionRuntimeApplier>();
        if (applier != null)
        {
            applier.ApplyDefinition();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Applied '{unitDefinitionAsset.name}'.", this);
        }
        else
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: No UnitDefinitionRuntimeApplier found.", this);

            // 2026-08-19：没有 Applier 的单位（3D 通道场景物/可破坏物品，比如箱子）从
            // 走 Binder 这条路开始，UnitDefinition 上配的 dropProfiles 就从来没人读过——
            // 掉落池这段逻辑之前只写在 UnitDefinitionRuntimeApplier.ApplyDefinition()
            // 里，Binder 找不到 Applier 组件时直接整段跳过。这里补一份最小的等价逻辑，
            // 不拉全套 Applier（那套是给 Character 单位准备的，里面一堆 Spine
            // 判定框/听觉视野这些对一个静态3D道具没有意义，硬挂上去风险更大）。
            EnsureDropProfilesApplied();
        }

        EnsureRuntimeIdentityForCurrentDefinition();
        RefreshVisualChannelBindingsIfPossible();
    }

    /// <summary>两条视觉通道（Spine / 3D）各自的绑定刷新入口都从这里统一分发——
    /// 调用方（Awake/OnEnable/OnValidate/ApplyDefinitionIfPossible）不用关心当前
    /// UnitDefinition 选的是哪条通道，两个刷新方法各自会在"不是自己的通道/没有
    /// 对应资源"时静默跳过，不冲突。</summary>
    private void RefreshVisualChannelBindingsIfPossible()
    {
        RefreshSpine43RendererBindingsIfPossible();
        RefreshModel3DBindingsIfPossible();
    }

    [ContextMenu("Refresh 3D Channel Source Binding")]
    public void RefreshModel3DBindingsIfPossible()
    {
        if (!autoBindModel3D || unitDefinitionAsset == null)
            return;

        if (unitDefinitionAsset.visualChannel != UnitVisualChannel.Model3D)
            return;

        Transform model3DRoot = FindDeepChild(transform, model3DRootName);
        if (model3DRoot == null)
        {
            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: No {model3DRootName} found. Skip 3D channel binding.", this);
            return;
        }

        GameObject sourcePrefab = unitDefinitionAsset.model3DPrefab;
        if (sourcePrefab == null)
        {
            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: UnitDefinition.model3DPrefab is null. Skip 3D channel binding.", this);
            return;
        }

        SkyPrisonModel3DVisualInstanceMarker existingMarker =
            model3DRoot.GetComponentInChildren<SkyPrisonModel3DVisualInstanceMarker>(true);

        // 已经实例化过同一份来源就不重建——Awake/OnEnable/OnValidate 都会调用这里，
        // 不加这道判断每次都会删了重建，编辑器里连续跳一遍就是一堆没必要的销毁/实例化。
        if (existingMarker == null || existingMarker.SourcePrefab != sourcePrefab)
        {
            for (int i = model3DRoot.childCount - 1; i >= 0; i--)
                DestroyModel3DChild(model3DRoot.GetChild(i).gameObject);

            GameObject instance = InstantiateModel3DSource(sourcePrefab, model3DRoot);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.name = sourcePrefab.name;

            // 来源资产（美术FBX）自带的层一般是Default——相机剔除遮罩/遮挡合成管线不
            // 处理Default层，不显式改成World3D这个模型渲染不出来（跟箱子Visual子节点
            // 之前踩的是同一个坑）。
            int world3DLayer = LayerMask.NameToLayer("World3D");
            if (world3DLayer >= 0)
                SetLayerRecursively(instance, world3DLayer);

            SkyPrisonModel3DVisualInstanceMarker newMarker = instance.AddComponent<SkyPrisonModel3DVisualInstanceMarker>();
            newMarker.SourcePrefab = sourcePrefab;

            MarkDirty(instance);

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: 3D channel instantiated '{sourcePrefab.name}' under {model3DRootName}.", this);

            // RebuildRendererCache()（强制按 currentOccluded 重新套一次材质）只能放在
            // "刚实例化网格"这个一次性分支里调一次——它每被调一次就会强制在"正常/合成"
            // 材质之间重新判定一次。如果放到分支外面、每次刷新绑定都调一遍，材质会跟着
            // 反复横跳，被遮挡效果表现为忽正常忽合成的闪烁（今天实测踩过，日志里能看到
            // 同一个渲染器在几十帧内来回切换）。真正的材质切换交给
            // UnitOcclusionMaterialReceiver 自己的 OnEnable/LateUpdate 稳定做一次，
            // 这里只在网格刚生成、它还没机会扫到这个新渲染器时，强制补一次。
            UnitOcclusionMaterialReceiver newMeshOcclusionReceiver = GetComponent<UnitOcclusionMaterialReceiver>();
            if (newMeshOcclusionReceiver != null)
                newMeshOcclusionReceiver.RebuildRendererCache();
        }

        // 命中碰撞体、渲染器扫描、全息配色这几步不能只放在"刚实例化网格"这个一次性
        // 分支里——已经在编辑器里放置过一次的场景实例，网格/marker 早就存在了，之后
        // 代码怎么改，下次刷新都会因为 marker 匹配直接跳过上面整个 if 块，改了也白改。
        // 这几步跟"网格是不是新造的"无关，每次都应该重新跑一遍；但只能用
        // RescanRenderersOnly()（只重新扫渲染器列表，不强制切材质状态），不能再调
        // RebuildRendererCache()——原因同上面的注释，会闪烁。
        Transform meshInstanceTransform = model3DRoot.childCount > 0 ? model3DRoot.GetChild(0) : null;
        if (meshInstanceTransform != null)
        {
            EnsureModel3DHitCollider(meshInstanceTransform.gameObject);
            EnsureModel3DSolidBlocker(meshInstanceTransform.gameObject);
        }

        UnitOcclusionMaterialReceiver occlusionReceiver = GetComponent<UnitOcclusionMaterialReceiver>();
        if (occlusionReceiver != null)
        {
            occlusionReceiver.RescanRenderersOnly();

            // 之前这里是事后调 ApplyXxx 方法去改"已经存在的"合成材质实例，跟
            // "合成材质到底什么时候真正创建/切换出来"之间是一场时序竞争，实测过：
            // 这个调用经常发生在材质还没真正切成合成状态的时候，全部落空——材质真正
            // 稳定下来时用的是一份全新实例，从来没被这几行改过，穿模问题的真正根因
            // 就在这。改成设置持久化的标记/颜色（SetIs3DPropMode/
            // SetHologramFillColorOverride），由 UnitOcclusionMaterialReceiver 自己在
            // 每次真正创建/刷新合成材质默认值的那一刻（EnsureCompositeDefaults）读取
            // 并烧进去，不管材质创建时机是什么时候都保证生效，不用赌时序。
            occlusionReceiver.SetIs3DPropMode(true);

            // ApplyHiddenOutlineColorForFaction（真正定阵营全息色的地方）只在
            // defineType==Character 时才跑——3D 通道场景物没有这条自动配色路径，
            // 不补的话全息色会停在运行时兜底材质的写死默认值（浅蓝），跟"可破坏物品
            // 应该是白色全息"这个项目既有约定（OutlineGroup.Item→白色）对不上。
            Color hologramColor = UnitDefinitionRuntimeApplier.ResolveHologramFillColorForGroup(unitDefinitionAsset.ResolvedOutlineGroup);
            occlusionReceiver.SetHologramFillColorOverride(hologramColor);

            // 万一当前已经存在一份合成材质实例（比如上一次调用时机凑巧对了），顺手
            // 也直接补一次——不依赖这次调用是否命中，只是不浪费已经命中的情况。
            // 注意：不调 ApplyHologramFillColor——那是直接写入方法，会把
            // EnsureCompositeDefaults 里设置的状态覆盖掉（2026-08-17 实测踩过）。
            occlusionReceiver.ApplyForceOpaqueAlpha(true);
            occlusionReceiver.ApplyOpaqueGeometryRenderState(true);
        }

        float scale = unitDefinitionAsset.model3DVisualScale;
        if (scale <= 0f)
            scale = 1f;
        model3DRoot.localScale = Vector3.one * scale;

        MarkDirty(model3DRoot.gameObject);
    }

    // 2026-08-19：受击框的层之前写死成 UnitBody，注释里说"跟所有角色的受击层保持
    // 一致"——查证后这句话是错的。真正角色的 Hurtbox/Hitbox（见
    // UnitDefinitionRuntimeApplier.EnsureCombatRuntimeForDefinition）从来没有显式
    // 设置过 layer，两边都停在 Unity 新建物体的默认层 Default(0)。物理层碰撞矩阵里
    // UnitBody × Default 是 ignore=true（互相无视），受击框被强行放到 UnitBody 后
    // physics 引擎从来没让它跟玩家攻击判定框的触发器互相看见过——这是箱子从接入
    // 战斗系统那天起就一直打不中的根因，不是判定形状/阵营逻辑的问题。改成不设置
    // layer，跟角色的 Hurtbox 用完全同一套约定（Default）。
    //
    // 网格数据是这个节点局部空间下的顶点，得挂在 meshFilter 所在节点下面（不是笼统
    // 挂在 instance 根上）——FBX 内部经常还有一层子节点包着实际网格，挂错节点会导致
    // 碰撞体位置/朝向跟视觉对不上，靠父子关系继承 Model3DRoot 的缩放，不用另外同步。
    private void EnsureModel3DHitCollider(GameObject instance)
    {
        UnitCombatHurtbox existingHurtbox = instance.GetComponentInChildren<UnitCombatHurtbox>(true);
        if (existingHurtbox != null)
        {
            // 已经存在的受击框（比如 Prefab 里手动/历史脚本存死过 UnitBody 层）也要
            // 纠正——早退在这上面会导致代码改了、Prefab 里旧数据却永远追不上。
            existingHurtbox.gameObject.layer = 0;
            return;
        }

        MeshFilter meshFilter = instance.GetComponentInChildren<MeshFilter>(true);
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        GameObject hurtboxGo = new GameObject("CombatHurtbox");
        hurtboxGo.transform.SetParent(meshFilter.transform, false);

        MeshCollider meshCollider = hurtboxGo.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.sharedMesh;
        meshCollider.convex = true;
        meshCollider.isTrigger = true;

        UnitCombatHurtbox hurtbox = hurtboxGo.AddComponent<UnitCombatHurtbox>();
        UnitHealthController health = GetComponent<UnitHealthController>();
        if (health != null)
            hurtbox.SetHealthController(health);
    }

    // 2026-08-19：CombatHurtbox 是 isTrigger=true，只负责"武器判定打没打中"，本来就不
    // 会挡人——3D 通道单位（可破坏箱子这类）角色能直接穿过去，是因为从来没有一个真正
    // 阻挡移动的实体碰撞体。跟 CombatHurtbox 分开单独挂一个，不复用同一个碰撞体，
    // 避免以后谁把 isTrigger 改成 false 时误伤受击判定（受击判定必须保持 Trigger，
    // 否则会跟真实物理体一起参与位移解算）。
    //
    // 不挂 Rigidbody、不用 SkyPrisonPushablePropRuntime 那套推动/击倒物理——这里只要
    // "人物走不进去"，静态的非 Trigger MeshCollider 已经够了，不需要凸包
    // （convex=false）：Unity 的物理引擎允许静态、非 Trigger 的凹网格碰撞体，只有会
    // 移动的 Rigidbody 才要求 convex=true。层用 World3D，跟其他能挡人的场景几何体
    // 一致——UnitMovementController 的 blockingLayers 默认 ~0（减去 PushableProp），
    // World3D 已经在里面，不用额外配置。
    private void EnsureModel3DSolidBlocker(GameObject instance)
    {
        if (instance.transform.Find("SolidBlocker") != null)
            return;

        MeshFilter meshFilter = instance.GetComponentInChildren<MeshFilter>(true);
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        GameObject blockerGo = new GameObject("SolidBlocker");
        blockerGo.transform.SetParent(meshFilter.transform, false);

        int world3DLayer = LayerMask.NameToLayer("World3D");
        blockerGo.layer = world3DLayer >= 0 ? world3DLayer : instance.layer;

        MeshCollider blockerCollider = blockerGo.AddComponent<MeshCollider>();
        blockerCollider.sharedMesh = meshFilter.sharedMesh;
        blockerCollider.convex = false;
        blockerCollider.isTrigger = false;
    }

    // 2026-08-19：等价于 UnitDefinitionRuntimeApplier.ApplyDefinition() 里那一小段
    // dropProfiles 处理——没有 Applier 的单位（3D 通道场景物）需要自己补这一份，
    // 否则 UnitDefinition 上配的掉落池永远不会真正生效。
    private void EnsureDropProfilesApplied()
    {
        if (unitDefinitionAsset.dropProfiles == null || unitDefinitionAsset.dropProfiles.Count == 0)
            return;

        UnitDeathDropController dropController = GetComponent<UnitDeathDropController>();
        if (dropController == null)
            dropController = gameObject.AddComponent<UnitDeathDropController>();

        dropController.SetDropProfiles(unitDefinitionAsset.dropProfiles);
    }

    private static GameObject InstantiateModel3DSource(GameObject sourcePrefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            GameObject editorInstance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, parent);
            return editorInstance;
        }
#endif
        GameObject runtimeInstance = UnityEngine.Object.Instantiate(sourcePrefab, parent);
        return runtimeInstance;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void DestroyModel3DChild(GameObject go)
    {
        if (go == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEngine.Object.DestroyImmediate(go);
            return;
        }
#endif
        UnityEngine.Object.Destroy(go);
    }

    [ContextMenu("Refresh Spine 4.3 Source Binding")]
    public void RefreshSpine43RendererBindingsIfPossible()
    {
        if (!autoBindSpine43Renderers)
            return;

        SkeletonAnimation sourceAnimation = FindMainSpineAnimation();
        if (sourceAnimation == null)
        {
            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: No main Spine SkeletonAnimation found. Skip Spine 4.3 binding.", this);
            return;
        }

        GameObject sourceObject = sourceAnimation.gameObject;
        EnsureSourceSpine43Components(sourceObject);

        SkeletonRenderer sourceRenderer = sourceObject.GetComponent<SkeletonRenderer>();

        // Spine 4.3 升级后，运行时 Prefab 上的 SkeletonRenderer / SkeletonAnimation
        // 可能已经存在，但 skeletonDataAsset 会丢失。这里必须以 UnitDefinition
        // 当前选择的 Spine .asset 为第一来源，再回退到场景对象自身。
        SkeletonDataAsset sourceDataAsset = ResolveSkeletonDataAssetFromUnitDefinition(unitDefinitionAsset)
            ?? ResolveSkeletonDataAsset(sourceAnimation, sourceRenderer);

        Material[] sourceMaterials = ResolveSourceMaterials(sourceObject, sourceDataAsset);

        if (sourceDataAsset == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: Source Spine object has no SkeletonDataAsset.", sourceObject);
            return;
        }

        BindSourceSpineObject(sourceObject, sourceAnimation, sourceRenderer, sourceDataAsset, sourceMaterials);
        ApplySpineVisualScale();
        // 头顶锚点高度不再在这里算一次性的——SkeletonData.Height是Spine文件里
        // 声明的参考尺寸，不一定跟实际渲染出来的模型大小一致(踩过一次坑：漏乘
        // scale直接把UI摆到天上)。改成 UnitOverheadUIView 每帧用真实渲染出来的
        // Renderer.bounds 动态测量，量的是眼见为实的实际大小，不用再猜任何换算关系。

        int boundOccludedProxyCount = RefreshRequiredOccludedProxyBindings(sourceAnimation, sourceDataAsset, sourceMaterials);

        if (debugLogs)
            Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Spine source binding refreshed. Source='{sourceObject.name}', skeleton='{sourceDataAsset.name}', occludedProxies={boundOccludedProxyCount}.", this);
    }

    public void SetUnitDefinitionAsset(UnitDefinition definitionAsset, bool applyNow = true)
    {
        unitDefinitionAsset = definitionAsset;
        RefreshSceneMarkerCache();

        if (debugLogs)
        {
            string assetName = unitDefinitionAsset != null ? unitDefinitionAsset.name : "None";
            Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Set UnitDefinition -> {assetName}", this);
        }

        if (applyNow)
            ApplyDefinitionIfPossible();
        else
            RefreshVisualChannelBindingsIfPossible();

        EnsureRuntimeIdentityForCurrentDefinition();
    }

    public void ClearUnitDefinitionAsset()
    {
        unitDefinitionAsset = null;
        RefreshSceneMarkerCache();

        if (debugLogs)
            Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Cleared UnitDefinition.", this);
    }

    public bool HasDefinition()
    {
        return unitDefinitionAsset != null;
    }

    [ContextMenu("Runtime Authority/Ensure Runtime Identity")]
    public void EnsureRuntimeIdentityForCurrentDefinition()
    {
        if (!autoEnsureRuntimeIdentityForCharacterUnits)
            return;

        UnitDefinition definition = unitDefinitionAsset;
        if (definition == null || definition.defineType != UnitDefineType.Character)
            return;

        SkyPrisonUnitRuntimeIdentity identity = GetComponent<SkyPrisonUnitRuntimeIdentity>();
        if (identity == null)
        {
            identity = gameObject.AddComponent<SkyPrisonUnitRuntimeIdentity>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Auto added SkyPrisonUnitRuntimeIdentity.", this);
        }

        if (identity == null)
            return;

        identity.ResolveReferences();
        identity.ResolveDefinitionIdentity();

        if (Application.isPlaying)
        {
            identity.InitializeFromDefinitionIfNeeded();
            identity.ApplyRuntimeIdentitySideEffects();
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
#endif
    }

    private void EnsureSourceSpine43Components(GameObject sourceObject)
    {
        if (sourceObject == null)
            return;

        if (sourceObject.GetComponent<MeshFilter>() == null)
            sourceObject.AddComponent<MeshFilter>();

        if (sourceObject.GetComponent<MeshRenderer>() == null)
            sourceObject.AddComponent<MeshRenderer>();

        if (sourceObject.GetComponent<SkeletonRenderer>() == null)
            sourceObject.AddComponent<SkeletonRenderer>();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(sourceObject);
#endif
    }

    private void BindSourceSpineObject(GameObject sourceObject, SkeletonAnimation sourceAnimation, SkeletonRenderer sourceRenderer, SkeletonDataAsset sourceDataAsset, Material[] sourceMaterials)
    {
        if (sourceObject == null || sourceDataAsset == null)
            return;

        MeshRenderer meshRenderer = sourceObject.GetComponent<MeshRenderer>();
        MeshFilter meshFilter = sourceObject.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh { name = "Skeleton Mesh" };

        if (sourceAnimation != null)
            SetSkeletonDataAsset(sourceAnimation, sourceDataAsset);
        if (sourceRenderer != null)
            SetSkeletonDataAsset(sourceRenderer, sourceDataAsset);

        if (meshRenderer != null && sourceMaterials != null && sourceMaterials.Length > 0)
            meshRenderer.sharedMaterials = sourceMaterials;

        TryInvokeInitialize(sourceRenderer);
        TryInvokeInitialize(sourceAnimation);

        MarkDirty(sourceObject, sourceAnimation, sourceRenderer, meshRenderer, meshFilter);
    }

    private int RefreshRequiredOccludedProxyBindings(SkeletonAnimation sourceAnimation, SkeletonDataAsset sourceDataAsset, Material[] sourceMaterials)
    {
        if (!bindOccludedOutlineProxies || sourceAnimation == null || sourceDataAsset == null)
            return 0;

        Transform outlineRoot = FindDeepChild(transform, outlineProxyRootName);
        Transform searchRoot = outlineRoot != null ? outlineRoot : transform;

        int boundCount = 0;
        for (int i = 0; i < RequiredOccludedProxyNames.Length; i++)
        {
            Transform proxy = FindDeepChild(searchRoot, RequiredOccludedProxyNames[i]);
            if (proxy == null)
                continue;

            if (BindOneOccludedSpineProxy(proxy.gameObject, sourceAnimation, sourceDataAsset, sourceMaterials))
                boundCount++;
        }

        return boundCount;
    }

    private bool BindOneOccludedSpineProxy(GameObject proxyObject, SkeletonAnimation sourceAnimation, SkeletonDataAsset sourceDataAsset, Material[] sourceMaterials)
    {
        if (proxyObject == null || sourceAnimation == null || sourceDataAsset == null)
            return false;

        MeshFilter meshFilter = proxyObject.GetComponent<MeshFilter>();
        if (meshFilter == null && createMissingOccludedProxyComponents)
            meshFilter = proxyObject.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer = proxyObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null && createMissingOccludedProxyComponents)
            meshRenderer = proxyObject.AddComponent<MeshRenderer>();

        SkeletonAnimation targetAnimation = proxyObject.GetComponent<SkeletonAnimation>();
        if (targetAnimation == null && createMissingOccludedProxyComponents)
            targetAnimation = proxyObject.AddComponent<SkeletonAnimation>();

        SkeletonRenderer targetRenderer = proxyObject.GetComponent<SkeletonRenderer>();
        if (targetRenderer == null && createMissingOccludedProxyComponents)
            targetRenderer = proxyObject.GetComponent<SkeletonRenderer>() ?? proxyObject.AddComponent<SkeletonRenderer>();

        if (meshFilter != null && meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh { name = "Skeleton Mesh" };

        if (meshRenderer != null && sourceMaterials != null && sourceMaterials.Length > 0)
            meshRenderer.sharedMaterials = sourceMaterials;

        if (targetAnimation == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: Occluded proxy '{proxyObject.name}' has no SkeletonAnimation.", proxyObject);
            return false;
        }

        SetSkeletonDataAsset(targetAnimation, sourceDataAsset);
        if (targetRenderer != null)
            SetSkeletonDataAsset(targetRenderer, sourceDataAsset);

        TryInvokeInitialize(targetRenderer);
        TryInvokeInitialize(targetAnimation);

        SpineOutlineFollower follower = proxyObject.GetComponent<SpineOutlineFollower>();
        if (follower == null && createMissingOccludedProxyComponents)
            follower = proxyObject.AddComponent<SpineOutlineFollower>();

        if (follower != null)
        {
            SetPrivateField(follower, "source", sourceAnimation);
            SetPrivateField(follower, "target", targetAnimation);
            MarkDirty(follower);
        }

        ForceSilhouetteMaterial forceMaterial = proxyObject.GetComponent<ForceSilhouetteMaterial>();
        if (forceMaterial != null && meshRenderer != null)
        {
            SetPrivateField(forceMaterial, "targetRenderer", meshRenderer);
            MarkDirty(forceMaterial);
        }

        MarkDirty(proxyObject, meshFilter, meshRenderer, targetAnimation, targetRenderer);
        return true;
    }


    /// <summary>运行时预制体壳现在是多个单位共用的，不同UnitDefinition换上去的骨架
    /// 导出比例可能不一样——缩放只作用在SpineRoot(纯视觉)上，不碰碰撞体/描边代理这些
    /// 独立于SpineRoot之外的节点，换骨架大小不对时不用另外做一份专属预制体去调。</summary>
    private void ApplySpineVisualScale()
    {
        if (unitDefinitionAsset == null)
            return;

        Transform spineRoot = FindDeepChild(transform, spineRootName);
        if (spineRoot == null)
            return;

        float scale = unitDefinitionAsset.spineVisualScale;
        if (scale <= 0f)
            scale = 1f;

        spineRoot.localScale = Vector3.one * scale;
    }

    private SkeletonAnimation FindMainSpineAnimation()
    {
        Transform spineRoot = FindDeepChild(transform, spineRootName);
        SkeletonAnimation[] animations = spineRoot != null
            ? spineRoot.GetComponentsInChildren<SkeletonAnimation>(true)
            : GetComponentsInChildren<SkeletonAnimation>(true);

        if (animations == null || animations.Length == 0)
            return null;

        SkeletonAnimation fallback = null;

        for (int i = 0; i < animations.Length; i++)
        {
            SkeletonAnimation candidate = animations[i];
            if (candidate == null)
                continue;

            if (IsOutlineProxyTransform(candidate.transform))
                continue;

            if (fallback == null)
                fallback = candidate;

            if (!string.IsNullOrWhiteSpace(spineSourceNameContains) &&
                candidate.name.Contains(spineSourceNameContains))
            {
                return candidate;
            }
        }

        return fallback;
    }

    private bool IsOutlineProxyTransform(Transform t)
    {
        while (t != null && t != transform)
        {
            if (t.name.StartsWith("OutlineProxy_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(t.name, outlineProxyRootName, StringComparison.OrdinalIgnoreCase))
                return true;

            t = t.parent;
        }

        return false;
    }


    private static SkeletonDataAsset ResolveSkeletonDataAssetFromUnitDefinition(UnitDefinition definition)
    {
        if (definition == null)
            return null;

        HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        SkeletonDataAsset best = null;
        int bestScore = int.MinValue;
        ScanObjectForSkeletonDataAsset(definition, "UnitDefinition", 0, visited, ref best, ref bestScore);
        return best;
    }

    private static void ScanObjectForSkeletonDataAsset(object obj, string path, int depth, HashSet<object> visited, ref SkeletonDataAsset best, ref int bestScore)
    {
        if (obj == null || depth > 4)
            return;

        if (obj is SkeletonDataAsset directAsset)
        {
            int directScore = ScoreSkeletonDataPath(path);
            if (directScore > bestScore)
            {
                best = directAsset;
                bestScore = directScore;
            }
            return;
        }

        Type type = obj.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return;

        // 不深入 Unity 资源内部，避免扫进 Material / Texture / GameObject 的大量字段。
        if (obj is UnityEngine.Object && !(obj is UnitDefinition))
            return;

        if (!type.IsValueType)
        {
            if (visited.Contains(obj))
                return;
            visited.Add(obj);
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field == null || field.IsStatic)
                continue;

            object value = null;
            try { value = field.GetValue(obj); }
            catch { continue; }

            string childPath = path + "." + field.Name;
            if (value is SkeletonDataAsset asset)
            {
                int score = ScoreSkeletonDataPath(childPath);
                if (score > bestScore)
                {
                    best = asset;
                    bestScore = score;
                }
                continue;
            }

            if (ShouldScanNestedValue(value, field.FieldType))
                ScanObjectForSkeletonDataAsset(value, childPath, depth + 1, visited, ref best, ref bestScore);
        }

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            object value = null;
            try { value = property.GetValue(obj, null); }
            catch { continue; }

            string childPath = path + "." + property.Name;
            if (value is SkeletonDataAsset asset)
            {
                int score = ScoreSkeletonDataPath(childPath);
                if (score > bestScore)
                {
                    best = asset;
                    bestScore = score;
                }
                continue;
            }

            if (ShouldScanNestedValue(value, property.PropertyType))
                ScanObjectForSkeletonDataAsset(value, childPath, depth + 1, visited, ref best, ref bestScore);
        }
    }

    private static bool ShouldScanNestedValue(object value, Type declaredType)
    {
        if (value == null || declaredType == null)
            return false;

        if (value is SkeletonDataAsset)
            return true;

        if (value is UnityEngine.Object)
            return false;

        if (value is IEnumerable && !(value is string))
            return false;

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return false;

        string fullName = type.FullName ?? string.Empty;
        if (fullName.StartsWith("System."))
            return false;

        return true;
    }

    private static int ScoreSkeletonDataPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return 0;

        string lower = path.ToLowerInvariant();
        int score = 0;
        if (lower.Contains("spine")) score += 100;
        if (lower.Contains("skeleton")) score += 80;
        if (lower.Contains("asset")) score += 20;
        if (lower.Contains("current")) score += 10;
        if (lower.Contains("runtime")) score += 5;
        if (lower.Contains("prefab")) score -= 40;
        if (lower.Contains("icon")) score -= 80;
        return score;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => obj != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj) : 0;
    }

    private static SkeletonDataAsset ResolveSkeletonDataAsset(SkeletonAnimation animation, SkeletonRenderer renderer = null)
    {
        if (animation != null)
        {
            object value = GetMemberValue(animation, "skeletonDataAsset");
            if (value is SkeletonDataAsset direct)
                return direct;

            value = GetMemberValue(animation, "SkeletonDataAsset");
            if (value is SkeletonDataAsset directProperty)
                return directProperty;
        }

        if (renderer == null && animation != null)
            renderer = animation.GetComponent<SkeletonRenderer>();

        if (renderer != null)
        {
            object value = GetMemberValue(renderer, "skeletonDataAsset");
            if (value is SkeletonDataAsset rendererAsset)
                return rendererAsset;

            value = GetMemberValue(renderer, "SkeletonDataAsset");
            if (value is SkeletonDataAsset rendererAssetProperty)
                return rendererAssetProperty;
        }

        return null;
    }

    private static Material[] ResolveSourceMaterials(GameObject sourceObject, SkeletonDataAsset sourceDataAsset)
    {
        // V24: UnitDefinition 的 Spine .asset 是第一来源。
        // 旧版先读当前 MeshRenderer.sharedMaterials，导致切换 SkeletonDataAsset 后仍沿用旧模型材质，表现为“单位定义改了但视觉没有同步”。
        if (sourceDataAsset != null)
        {
            Material[] fromAsset = TryResolveMaterialsFromSkeletonDataAsset(sourceDataAsset);
            if (fromAsset != null && fromAsset.Length > 0)
                return fromAsset;
        }

        MeshRenderer meshRenderer = sourceObject != null ? sourceObject.GetComponent<MeshRenderer>() : null;
        if (meshRenderer != null && meshRenderer.sharedMaterials != null && meshRenderer.sharedMaterials.Length > 0 && meshRenderer.sharedMaterials[0] != null)
            return meshRenderer.sharedMaterials;

        return null;
    }

    private static Material[] TryResolveMaterialsFromSkeletonDataAsset(SkeletonDataAsset asset)
    {
        if (asset == null)
            return null;

        object atlasAssetsObject = GetMemberValue(asset, "atlasAssets");
        if (atlasAssetsObject is AtlasAssetBase[] atlasAssets)
        {
            for (int i = 0; i < atlasAssets.Length; i++)
            {
                Material[] materials = ResolveMaterialsFromAtlasAsset(atlasAssets[i]);
                if (materials != null && materials.Length > 0)
                    return materials;
            }
        }

        atlasAssetsObject = GetMemberValue(asset, "AtlasAssets");
        if (atlasAssetsObject is AtlasAssetBase[] atlasAssetsProperty)
        {
            for (int i = 0; i < atlasAssetsProperty.Length; i++)
            {
                Material[] materials = ResolveMaterialsFromAtlasAsset(atlasAssetsProperty[i]);
                if (materials != null && materials.Length > 0)
                    return materials;
            }
        }

        return null;
    }

    private static Material[] ResolveMaterialsFromAtlasAsset(UnityEngine.Object atlasAsset)
    {
        if (atlasAsset == null)
            return null;

        object materialsObject = GetMemberValue(atlasAsset, "materials");
        if (materialsObject is Material[] materials && materials.Length > 0)
            return materials;

        materialsObject = GetMemberValue(atlasAsset, "Materials");
        if (materialsObject is Material[] materialsProperty && materialsProperty.Length > 0)
            return materialsProperty;

        return null;
    }

    private static void SetSkeletonDataAsset(Component component, SkeletonDataAsset asset)
    {
        if (component == null || asset == null)
            return;

        if (SetMemberValue(component, "skeletonDataAsset", asset))
            return;

        SetMemberValue(component, "SkeletonDataAsset", asset);
    }

    private static void TryInvokeInitialize(Component component)
    {
        if (component == null)
            return;

        System.Type type = component.GetType();
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != "Initialize")
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
            {
                method.Invoke(component, new object[] { true });
                return;
            }

            if (parameters.Length == 0)
            {
                method.Invoke(component, null);
                return;
            }
        }
    }

    private static object GetMemberValue(object target, string memberName)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
            return null;

        System.Type type = target.GetType();

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            return field.GetValue(target);

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanRead)
            return property.GetValue(target, null);

        return null;
    }

    private static bool SetMemberValue(object target, string memberName, object value)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
            return false;

        System.Type type = target.GetType();

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && (value == null || field.FieldType.IsInstanceOfType(value)))
        {
            field.SetValue(target, value);
            return true;
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && (value == null || property.PropertyType.IsInstanceOfType(value)))
        {
            property.SetValue(target, value, null);
            return true;
        }

        return false;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && (value == null || field.FieldType.IsInstanceOfType(value)))
            field.SetValue(target, value);
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform match = FindDeepChild(child, childName);
            if (match != null)
                return match;
        }

        return null;
    }

    private static void MarkDirty(params UnityEngine.Object[] objects)
    {
#if UNITY_EDITOR
        if (Application.isPlaying || objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                EditorUtility.SetDirty(objects[i]);
        }
#endif
    }

    private void RefreshSceneMarkerCache()
    {
        SkyPrisonSceneUnitMarker marker = GetComponent<SkyPrisonSceneUnitMarker>();
        if (marker != null)
        {
            marker.RefreshBindingCache();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(marker);
#endif
        }
    }
}
