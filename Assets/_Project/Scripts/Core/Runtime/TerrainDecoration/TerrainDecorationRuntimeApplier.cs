using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TerrainDecorationRuntimeBinder))]
public class TerrainDecorationRuntimeApplier : MonoBehaviour
{
    private enum FrontOccluderProxyProfile
    {
        SmallProp = 0,
        Block = 1,
        Wall = 2,
        Pillar = 3,
        Irregular = 4,
        Custom = 99,
    }

    private struct ProjectedFootprintBounds
    {
        public Vector3[] corners;
        public Vector3 cameraForward;
        public Vector3 cameraRight;
        public float minRight;
        public float maxRight;
        public float minDepth;
        public float maxDepth;

        public float Width => Mathf.Max(0.05f, maxRight - minRight);
        public float Depth => Mathf.Max(0.05f, maxDepth - minDepth);
        public float CenterRight => (minRight + maxRight) * 0.5f;
        public float CenterDepth => (minDepth + maxDepth) * 0.5f;
        public Vector3 CenterLocal => cameraRight * CenterRight + cameraForward * CenterDepth;
    }


    private struct GeneratedColliderSnapshot
    {
        public bool exists;
        public Transform transform;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public bool colliderExists;
        public Vector3 colliderCenter;
        public Vector3 colliderSize;
        public bool colliderIsTrigger;
    }

    [Header("Apply")]
    [SerializeField] private bool applyOnEnable = true;
    [SerializeField] private bool applyInEditMode = true;

    [Header("Standard Nodes")]
    [SerializeField] private Transform ruleRoot;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform shadowCasterRoot;
    [SerializeField] private Transform collisionRoot;
    [SerializeField] private Transform visionBlockerRoot;
    [SerializeField] private Transform sortAnchor;
    [SerializeField] private Transform planeOrigin;
    [SerializeField] private Transform planeForwardReference;
    [SerializeField] private Transform backTrigger;
    [SerializeField] private Transform frontTrigger;
    [SerializeField] private Transform stencilWriterRoot;
    [SerializeField] private Transform frontOccluderRoot;
    [SerializeField] private Transform outlineMaskProxyRoot;
    [SerializeField] private Transform mossRoot;
    [SerializeField] private Transform fxRoot;
    [SerializeField] private Transform editorGizmoRoot;

    [Header("2.5D Occlusion Proxy Auto Fit")]
    [SerializeField, Range(0.45f, 0.80f)] private float upperFitHeightRatio = 0.60f;
    [SerializeField, Range(0.20f, 0.65f)] private float lowerInsetHeightRatio = 0.40f;
    [SerializeField, Range(0.40f, 1.00f)] private float lowerInsetWidthRatio = 0.72f;
    [SerializeField, Range(0.35f, 1.00f)] private float lowerInsetDepthRatio = 0.66f;
    [SerializeField] private float proxyPaddingXZ = 0.04f;
    [SerializeField] private float proxyPaddingY = 0.02f;
    [SerializeField] private float lowerFrontReserve = 0.10f;

    [Header("Collision Safety Margin")]
    [Tooltip("Main_Collision_Box is expanded from its center by this multiplier. 1.10 keeps characters from hugging the visible mesh too tightly, which is safer for 2.5D occlusion edge cases.")]
    [SerializeField, Range(1.00f, 1.50f)] private float mainCollisionSafetyScale = 1.10f;
    [Tooltip("Adds extra X/Z collision margin when the decoration's local footprint is diagonal to the fixed 2.5D camera plane. This solves extreme view-angle corner leaks better than a uniform 1.1 scale.")]
    [SerializeField] private bool useAngleAwareCollisionSafety = true;
    [Tooltip("Stable gameplay camera yaw used for angle-aware collision safety. Keep this fixed to the 2.5D game camera, not the SceneView camera.")]
    [SerializeField, Range(-180f, 180f)] private float collisionSafetyCameraYawDegrees = 45f;
    [Tooltip("Extra X/Z padding added at the worst diagonal angle, relative to the smaller footprint side.")]
    [SerializeField, Range(0.00f, 1.00f)] private float angledCollisionExtraByMinSide = 0.45f;
    [Tooltip("Minimum extra X/Z padding added at the worst diagonal angle.")]
    [SerializeField] private float angledCollisionMinExtraXZ = 0.35f;

    [Header("Projected Footprint Solver")]
    [Tooltip("Enterprise-style 2.5D footprint solver. It projects the decoration ground footprint corners to the fixed camera axes, then uses those projected extrema to build trigger/proxy bounds. This prevents early BackTrigger activation at diagonal corners.")]
    [SerializeField] private bool useProjectedFootprintBackTrigger = true;
    [Tooltip("Stable gameplay camera yaw used by the shared footprint projection solver. This is fixed 2.5D game-camera logic, not SceneView camera logic.")]
    [SerializeField, Range(-180f, 180f)] private float footprintProjectionCameraYawDegrees = 45f;
    [Tooltip("Enterprise fix: use the real 2.5D camera axes for footprint projection instead of trusting a guessed yaw. This solves mirrored / wrong-angle BackTrigger bands when the visual camera is not exactly the configured yaw.")]
    [SerializeField] private bool useWorldCameraAxesForFootprintProjection = true;
    [Tooltip("Optional camera transform used as the fixed 2.5D projection camera. If empty, Camera.main is used; if no camera exists, footprintProjectionCameraYawDegrees is used as fallback.")]
    [SerializeField] private Transform footprintProjectionCameraOverride;
    [Tooltip("Flip the projected camera forward if BackTrigger appears on the opposite side of the visual rear edge.")]
    [SerializeField] private bool invertFootprintProjectionForward = false;
    [Tooltip("Extra screen-horizontal margin for BackTrigger after projecting footprint corners.")]
    [SerializeField] private float projectedBackTriggerSidePadding = 0.35f;
    [Tooltip("BackTrigger starts behind the projected rear corner line by this amount. Increasing this delays the switch until the player has visually passed the turning edge.")]
    [SerializeField] private float projectedBackTriggerFrontSafetyOffset = 0.18f;
    [Tooltip("Extra depth added to the projected BackTrigger band.")]
    [SerializeField] private float projectedBackTriggerDepthPadding = 0.35f;
    [Tooltip("If true, the projected camera-forward max-depth side is treated as the visual back side. For the current fixed 2.5D camera setup this is intentionally false, so the min-depth side is used as the visual back side.")]
    [SerializeField] private bool projectedBackTriggerUseMaxDepthAsBack = false;

    [Header("Back Trigger Auto Fit")]
    [SerializeField] private float backTriggerExtraWidth = 0.20f;
    [SerializeField] private float backTriggerWidthMultiplier = 1.20f;
    [Tooltip("2.5D camera yaw used to calculate how much side exposure BackTrigger needs. 45 degrees means side expansion should at least equal trigger depth.")]
    [SerializeField, Range(0f, 75f)] private float backTriggerExposureAngle = 45f;
    [Tooltip("Safety factor for side exposure. 1.15 means 15% more than the theoretical minimum.")]
    [SerializeField, Range(0.20f, 1.20f)] private float backTriggerExposureSafety = 0.20f;
    [SerializeField] private float backTriggerExtraHeight = 1.80f;
    [SerializeField] private float backTriggerDepthMultiplier = 2.35f;
    [SerializeField] private float backTriggerMinDepth = 2.40f;
    [Tooltip("How far the BackTrigger center is shifted toward the rule-space back direction. 0.5 means the added band starts exactly at the model rear; larger values moves the whole box farther back.")]
    [SerializeField, Range(0.50f, 0.90f)] private float backTriggerRearShiftFactor = 0.72f;

    [Header("Front Occluder Proxy Safe Padding")]
    [Tooltip("Keep FrontOccluderProxy_Box at its rule-space auto generated position, then only enlarge its collider. This avoids baking one instance's local position into every decoration.")]
    [SerializeField] private bool useFrontOccluderProxySafePadding = true;
    [Tooltip("Default profile for the rule-space foreground occlusion proxy. Block is the verified safe default for box-like terrain props.")]
    [SerializeField] private FrontOccluderProxyProfile frontOccluderProxyProfile = FrontOccluderProxyProfile.Block;
    [Tooltip("Use fixed 2.5D camera axes plus the decoration footprint corners to generate FrontOccluderProxy_Box. This solves corner leaks at arbitrary object rotations.")]
    [SerializeField] private bool useCameraProjectedFrontProxyBounds = true;
    [Tooltip("Stable gameplay camera yaw used by FrontOccluderProxy_Box corner projection. Keep this fixed to the 2.5D game camera, not the SceneView camera.")]
    [SerializeField, Range(-180f, 180f)] private float frontProxyCameraYawDegrees = 45f;
    [Tooltip("Extra screen-horizontal margin added after projecting the footprint corners to the fixed camera right axis.")]
    [SerializeField] private float frontProxyProjectedRightPadding = 0.30f;
    [Tooltip("Extra screen-depth margin added after projecting the footprint corners to the fixed camera forward axis.")]
    [SerializeField] private float frontProxyProjectedDepthPadding = 0.45f;
    [Tooltip("Extra height margin for FrontOccluderProxy_Box after the projected-corner bounds are calculated.")]
    [SerializeField] private float frontProxyProjectedHeightPadding = 0.40f;
    [Tooltip("Only used when Profile is Custom.")]
    [SerializeField] private Vector3 customFrontOccluderProxySizeMultiplier = new Vector3(1.20f, 1.20f, 1.55f);
    [Tooltip("Only used when Profile is Custom.")]
    [SerializeField] private Vector3 customFrontOccluderProxySizeAdd = new Vector3(0.60f, 0.80f, 1.20f);
    [Tooltip("Only used when Profile is Custom. Local collider center offset, not Transform position.")]
    [SerializeField] private Vector3 customFrontOccluderProxyColliderCenterOffset = Vector3.zero;
    [SerializeField] private bool rebuildStencilShapeClone = true;
    [SerializeField] private bool rebuildOutlineMaskShapeClone = true;
    [Tooltip("Default off: prevents auto outline-mask mesh clone from being visible in Scene or placement preview. Enable only after connecting a dedicated Mask Camera or RenderFeature.")]
    [SerializeField] private bool renderOutlineMaskShapeClone = false;
    [SerializeField] private bool hideGeneratedProxyRenderers = true;
    [SerializeField] private Material outlineMaskMaterial;

    [Header("Material Safety")]
    [Tooltip("Default off: PF variants already carry their own correct materials. Turning this on will write variant material slot defaults back to renderers during ApplyDefinition.")]
    [SerializeField] private bool applyMaterialSlotsOnApply = false;

    private const string AutoStencilCloneName = "__AutoStencilClone";
    private const string AutoOutlineMaskCloneName = "__AutoOutlineMaskShapeClone";
    private const string AutoFrontOccluderCloneName = "__AutoFrontOccluderShapeClone";

    private TerrainDecorationRuntimeBinder binder;

    private void OnEnable()
    {
        // 新结构边界：RuntimeApplier 不再在 OnEnable / 脚本重载 / 进入 Play 时自动重建结构。
        // 地形装饰物的 CollisionRoot / RuleRoot / Trigger / Proxy 只能由
        // SkyPrisonTerrainDecorationInstanceBuilder 在明确入口生成。
        binder = GetComponent<TerrainDecorationRuntimeBinder>();
    }


    [ContextMenu("Apply Definition")]
    public void ApplyDefinition()
    {
        // 新结构边界：
        // RuntimeApplier 不再生成或修复 RuleRoot / CollisionRoot / BackTrigger / FrontTrigger / FrontOccluderProxy。
        // 结构生成唯一归 SkyPrisonTerrainDecorationInstanceBuilder。
        if (binder == null)
            binder = GetComponent<TerrainDecorationRuntimeBinder>();

        TerrainDecorationDefinition definition = binder != null ? binder.definition : null;
        if (definition == null)
            return;

        ApplyVisualRotation(definition);
        ApplyLayerStructure();
        ApplyShadowSettings(definition);
        ApplyHeightFade(definition);

        if (applyMaterialSlotsOnApply)
            ApplyMaterialSlots(definition);
    }

    // "高层建筑物"勾选框——像遮挡代理体一样，在ApplyDefinition这个编辑器放置/同步时机
    // （不是运行时，见调用方 SkyPrisonMapObjectPlacementToolWindow/
    // SkyPrisonTerrainDecorationTemplateSyncUtility，跟遮挡代理体是同一个时机）自动把
    // VisualRoot 下用官方 Lit Shader 的材质换成支持高度淡出的实例材质（Instance，不碰
    // 共享资源文件本身，不会影响其它复用同一份材质的装饰物），并挂好
    // SkyPrisonHeightFadeController——不用再手动跑批量转换工具，也不用挑着材质文件改。
    private void ApplyHeightFade(TerrainDecorationDefinition definition)
    {
        SkyPrisonHeightFadeController controller = GetComponent<SkyPrisonHeightFadeController>();

        if (!definition.enableHeightFade)
        {
            if (controller != null)
                controller.enabled = false;
            return;
        }

        if (controller == null)
            controller = gameObject.AddComponent<SkyPrisonHeightFadeController>();

        controller.enabled = true;
        ApplyHeightFadeMaterials();
        controller.Configure(definition.heightFadeThreshold, definition.heightFadeDistance);
    }

    private static Shader _heightFadeLitShaderCache;
    private const string StandardLitShaderName = "Universal Render Pipeline/Lit";
    private const string HeightFadeLitShaderName = "SkyPrison/Lit With Height Fade";

    // 只换材质，不动Mesh/Transform结构——这一步做完之后 SkyPrisonHeightFadeController.
    // Awake 会自己按 Renderer Bounds 重新算一次底部高度，不用在这里操心。
    // 换材质的是 Instance（new Material(src)），不是共享的 .mat 资源文件，多个装饰物
    // 复用同一份原始材质也互不影响，退出Play模式/重新构建也不会像批量转换工具那样
    // 动到资源文件本身。
    private void ApplyHeightFadeMaterials()
    {
        if (_heightFadeLitShaderCache == null)
            _heightFadeLitShaderCache = Shader.Find(HeightFadeLitShaderName);
        if (_heightFadeLitShaderCache == null)
        {
            Debug.LogWarning($"[TerrainDecorationRuntimeApplier] 找不到Shader「{HeightFadeLitShaderName}」，高度淡出材质没法自动切换。", this);
            return;
        }

        if (visualRoot == null)
            visualRoot = transform.Find("VisualRoot");
        if (visualRoot == null)
            return;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) continue;

            bool changed = false;
            for (int m = 0; m < materials.Length; m++)
            {
                Material source = materials[m];
                // 已经换过的、或者本来就不是标准Lit的（比如已经手动定制过的特殊材质）
                // 跳过，避免重复实例化/误伤。
                if (source == null || source.shader == null || source.shader.name != StandardLitShaderName)
                    continue;

                // 2026-07-17：高度淡出改用抖动裁切之后材质保持Opaque就行，不需要再调
                // Surface Type/Blend State（早期Alpha混合方案在这里调用过
                // UnityEditor.BaseShaderGUI.SetupMaterialBlendMode，已移除——顺带
                // 去掉了这个运行时脚本对UnityEditor命名空间的依赖）。
                Material instance = new Material(source) { name = source.name + " (Height Fade Instance)" };
                instance.shader = _heightFadeLitShaderCache;
                materials[m] = instance;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }


    [ContextMenu("Ensure Standard Structure")]
    public void EnsureStandardStructureMenu()
    {
        Debug.LogWarning("[TerrainDecorationRuntimeApplier] Ensure Standard Structure 已废弃。请通过放置工具 / Builder / Migration 生成结构。", this);
    }


    [ContextMenu("Rebuild 2.5D Occlusion Proxies")]
    public void Rebuild2p5DOcclusionProxiesMenu()
    {
        Debug.LogWarning("[TerrainDecorationRuntimeApplier] Rebuild 2.5D Occlusion Proxies 已废弃。前后遮挡结构由 SkyPrisonTerrainDecorationInstanceBuilder 生成。", this);
    }



    private GeneratedColliderSnapshot CaptureGeneratedColliderSnapshot(Transform target)
    {
        GeneratedColliderSnapshot snapshot = new GeneratedColliderSnapshot();
        if (target == null)
            return snapshot;

        snapshot.exists = true;
        snapshot.transform = target;
        snapshot.localPosition = target.localPosition;
        snapshot.localRotation = target.localRotation;
        snapshot.localScale = target.localScale;

        BoxCollider box = target.GetComponent<BoxCollider>();
        if (box != null)
        {
            snapshot.colliderExists = true;
            snapshot.colliderCenter = box.center;
            snapshot.colliderSize = box.size;
            snapshot.colliderIsTrigger = box.isTrigger;
        }

        return snapshot;
    }

    private void RestoreGeneratedColliderSnapshot(GeneratedColliderSnapshot snapshot, bool keepTrigger = true)
    {
        if (!snapshot.exists || snapshot.transform == null)
            return;

        snapshot.transform.localPosition = snapshot.localPosition;
        snapshot.transform.localRotation = snapshot.localRotation;
        snapshot.transform.localScale = snapshot.localScale;

        if (snapshot.colliderExists)
        {
            BoxCollider box = snapshot.transform.GetComponent<BoxCollider>();
            if (box == null)
                box = snapshot.transform.gameObject.AddComponent<BoxCollider>();

            box.center = snapshot.colliderCenter;
            box.size = SanitizeSize(snapshot.colliderSize);
            box.isTrigger = keepTrigger ? true : snapshot.colliderIsTrigger;
        }
    }

    private void EnforceTriggerRuntimeSafety(TerrainDecorationDefinition definition)
    {
        bool active = definition == null || definition.occlusionMode != TerrainDecorationOcclusionMode.None;
        int decorationTriggerLayer = GetLayer("DecorationTrigger", 26);

        if (backTrigger != null)
        {
            backTrigger.gameObject.SetActive(true);
            backTrigger.gameObject.layer = decorationTriggerLayer;
            BoxCollider backBox = backTrigger.GetComponent<BoxCollider>();
            if (backBox == null)
                backBox = backTrigger.gameObject.AddComponent<BoxCollider>();
            backBox.isTrigger = true;
            backBox.enabled = active;
        }

        if (frontTrigger != null)
        {
            frontTrigger.gameObject.SetActive(true);
            frontTrigger.gameObject.layer = decorationTriggerLayer;
            BoxCollider frontBox = frontTrigger.GetComponent<BoxCollider>();
            if (frontBox == null)
                frontBox = frontTrigger.gameObject.AddComponent<BoxCollider>();
            frontBox.isTrigger = true;
            frontBox.enabled = active;
        }

        SimpleDirectionalOccluder occluder = EnsureOcclusionDriverOnBackTrigger();
        if (occluder != null)
        {
            occluder.enabled = active;
            occluder.gameObject.SetActive(true);
        }
    }

    [ContextMenu("Repair And Rebuild Full Generated Structure")]
    public void RepairAndRebuildGeneratedStructure()
    {
        Debug.LogWarning("[TerrainDecorationRuntimeApplier] RepairAndRebuildGeneratedStructure 已废弃。结构生成请使用 Builder 或 Migration。", this);
    }


    [ContextMenu("Auto Fit Collision Box To Visual Base")]
    public void AutoFitCollisionBoxToVisualBaseMenu()
    {
        AutoFitCollisionBoxToVisualBase(true);
    }

    public bool AutoFitCollisionBoxToVisualBase(bool writeBackToDefinition)
    {
        Debug.LogWarning("[TerrainDecorationRuntimeApplier] AutoFitCollisionBoxToVisualBase 已废弃。碰撞盒参数应在定义页手动设置，或后续由 Validator/Migration 提供显式工具。", this);
        return false;
    }


    public void EnsureStandardStructure(bool missingOnly = true)
    {
        // Deprecated no-op.
        // 旧版这里会创建 RuleRoot / CollisionRoot / Trigger / Proxy 等结构；
        // 现在这些结构只能由 SkyPrisonTerrainDecorationInstanceBuilder 生成。
    }


    private void NormalizeRuleRootsAgainstRootScale()
    {
        // Root 可以被放置工具用于视觉随机缩放；RuleRoot 不能跟着放大。
        // 否则 Main_Collision_Box / BackTrigger / FrontTrigger / Occlusion Proxy
        // 会在 Scene 里出现异常巨大的绿色规则盒。
        Vector3 inv = SafeInverseScale(transform.localScale);

        if (ruleRoot != null)
        {
            ruleRoot.localPosition = Vector3.zero;
            ruleRoot.localRotation = Quaternion.identity;
            ruleRoot.localScale = inv;
        }

        if (editorGizmoRoot != null)
        {
            editorGizmoRoot.localPosition = Vector3.zero;
            editorGizmoRoot.localRotation = Quaternion.identity;
            editorGizmoRoot.localScale = inv;
        }
    }

    private Vector3 SafeInverseScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Abs(scale.x) > 0.0001f ? 1f / scale.x : 1f,
            Mathf.Abs(scale.y) > 0.0001f ? 1f / scale.y : 1f,
            Mathf.Abs(scale.z) > 0.0001f ? 1f / scale.z : 1f);
    }

    private void RestoreSingleVisualRootNodeBackToPrefabRoot()
    {
        if (visualRoot == null)
            return;

        // 如果根节点已经有模型 Renderer，就不要动。PF 根节点就是正常视觉节点。
        bool rootHasRenderer =
            GetComponent<MeshRenderer>() != null ||
            GetComponent<SkinnedMeshRenderer>() != null;
        if (rootHasRenderer)
            return;

        // 只修复旧版自动迁移造成的最窄情况：VisualRoot 下只有一个 Visual_01，且它本身挂 Renderer。
        // 多子节点、多 Mesh 的复杂 PF 不自动折叠，避免破坏美术层级。
        if (visualRoot.childCount != 1)
            return;

        Transform visualNode = visualRoot.GetChild(0);
        if (visualNode == null || visualNode.name != "Visual_01")
            return;

        MeshFilter srcFilter = visualNode.GetComponent<MeshFilter>();
        MeshRenderer srcRenderer = visualNode.GetComponent<MeshRenderer>();
        SkinnedMeshRenderer srcSkinned = visualNode.GetComponent<SkinnedMeshRenderer>();

        bool hasMeshRenderer = srcRenderer != null && srcFilter != null && srcFilter.sharedMesh != null;
        bool hasSkinnedRenderer = srcSkinned != null && srcSkinned.sharedMesh != null;
        if (!hasMeshRenderer && !hasSkinnedRenderer)
            return;

        // 只接受没有局部偏移/旋转/缩放的 Visual_01。否则说明它可能是有意的子模型，不强行回根。
        if (visualNode.localPosition.sqrMagnitude > 0.000001f ||
            Quaternion.Angle(visualNode.localRotation, Quaternion.identity) > 0.001f ||
            (visualNode.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            return;

        if (hasMeshRenderer)
        {
            MeshFilter dstFilter = GetComponent<MeshFilter>();
            if (dstFilter == null)
                dstFilter = gameObject.AddComponent<MeshFilter>();
            dstFilter.sharedMesh = srcFilter.sharedMesh;

            MeshRenderer dstRenderer = GetComponent<MeshRenderer>();
            if (dstRenderer == null)
                dstRenderer = gameObject.AddComponent<MeshRenderer>();
            CopyRendererSettings(srcRenderer, dstRenderer);
        }
        else if (hasSkinnedRenderer)
        {
            SkinnedMeshRenderer dstSkinned = GetComponent<SkinnedMeshRenderer>();
            if (dstSkinned == null)
                dstSkinned = gameObject.AddComponent<SkinnedMeshRenderer>();
            CopyRendererSettings(srcSkinned, dstSkinned);
            dstSkinned.sharedMesh = srcSkinned.sharedMesh;
            dstSkinned.rootBone = srcSkinned.rootBone;
            dstSkinned.bones = srcSkinned.bones;
            dstSkinned.updateWhenOffscreen = srcSkinned.updateWhenOffscreen;
            dstSkinned.skinnedMotionVectors = srcSkinned.skinnedMotionVectors;
            dstSkinned.localBounds = srcSkinned.localBounds;
        }

        SafeDestroyImmediate(visualNode.gameObject);
    }

    private void CopyRendererSettings(Renderer source, Renderer target)
    {
        if (source == null || target == null)
            return;

        target.enabled = source.enabled;
        target.sharedMaterials = source.sharedMaterials;
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.probeAnchor = source.probeAnchor;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder;
    }

    private Transform EnsureChild(Transform parent, string childName, Transform cached)
    {
        if (parent == null)
            return null;

        if (cached != null)
            return cached;

        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(go, "Create terrain decoration node");
#endif
        return go.transform;
    }

    private void MoveDirectChildToParent(string childName, Transform targetParent, Transform preferred)
    {
        if (targetParent == null)
            return;

        Transform old = transform.Find(childName);
        if (old == null || old == targetParent || old.parent == targetParent)
            return;

        // 如果已经有缓存/目标节点，则合并旧节点子物体后删除空旧节点；否则直接迁移旧节点。
        Transform dst = preferred != null ? preferred : targetParent.Find(childName);
        if (dst != null && dst != old)
        {
            List<Transform> children = new List<Transform>();
            foreach (Transform c in old)
                children.Add(c);

            for (int i = 0; i < children.Count; i++)
                children[i].SetParent(dst, true);

            SafeDestroyImmediate(old.gameObject);
        }
        else
        {
            old.SetParent(targetParent, true);
            old.localRotation = Quaternion.identity;
        }
    }

    private void MoveLooseGeneratedNodesIntoStandardRoots()
    {
        if (ruleRoot == null)
            ruleRoot = transform.Find("RuleRoot");

        if (collisionRoot == null && ruleRoot != null)
            collisionRoot = ruleRoot.Find("CollisionRoot");
        if (frontOccluderRoot == null && ruleRoot != null)
            frontOccluderRoot = ruleRoot.Find("FrontOccluderRoot");
        if (outlineMaskProxyRoot == null && ruleRoot != null)
            outlineMaskProxyRoot = ruleRoot.Find("OutlineMaskProxyRoot");

        MoveLooseNodeToParent("Main_Collision_Box", collisionRoot);
        MoveLooseNodeToParent("FrontOccluderProxy_Box", frontOccluderRoot);
        MoveLooseNodeToParent("OutlineMaskProxy_T_Box", outlineMaskProxyRoot);
        MoveLooseNodeToParent("SubOutlineMaskProxy_01", outlineMaskProxyRoot);
    }

    private void MoveLooseNodeToParent(string nodeName, Transform targetParent)
    {
        if (targetParent == null)
            return;

        Transform loose = transform.Find(nodeName);
        if (loose == null || loose.parent == targetParent)
            return;

        Transform existing = targetParent.Find(nodeName);
        if (existing != null && existing != loose)
        {
            SafeDestroyImmediate(loose.gameObject);
            return;
        }

        loose.SetParent(targetParent, true);
        loose.localRotation = Quaternion.identity;
        loose.localScale = Vector3.one;
    }

    private void StripVisibleMeshFromRuleProxyBoxes()
    {
        StripVisibleMeshFromProxy("FrontOccluderProxy_Box");
        StripVisibleMeshFromProxy("OutlineMaskProxy_T_Box");
        StripVisibleMeshFromProxy("SubOutlineMaskProxy_01");
    }

    private void StripVisibleMeshFromProxy(string nodeName)
    {
        Transform node = FindDeepChild(transform, nodeName);
        if (node == null)
            return;

        Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
        for (int i = renderers.Length - 1; i >= 0; i--)
        {
            if (renderers[i] == null)
                continue;
            renderers[i].enabled = false;
            renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
            SafeDestroyImmediate(renderers[i]);
        }

        MeshFilter[] filters = node.GetComponentsInChildren<MeshFilter>(true);
        for (int i = filters.Length - 1; i >= 0; i--)
        {
            if (filters[i] != null)
                SafeDestroyImmediate(filters[i]);
        }

        Collider[] colliders = node.GetComponentsInChildren<Collider>(true);
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (collider is BoxCollider)
                collider.isTrigger = true;
            else
                SafeDestroyImmediate(collider);
        }
    }

    private void RemoveDuplicateGeneratedShapeClones()
    {
        RemoveGeneratedChildrenByName(AutoStencilCloneName);
        RemoveGeneratedChildrenByName(AutoOutlineMaskCloneName);
    }

    private void RemoveGeneratedChildrenByName(string nodeName)
    {
        List<Transform> matches = new List<Transform>();
        CollectChildrenByName(transform, nodeName, matches);
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i] != null)
                SafeDestroyImmediate(matches[i].gameObject);
        }
    }

    private void CollectChildrenByName(Transform root, string nodeName, List<Transform> result)
    {
        if (root == null || result == null)
            return;

        foreach (Transform child in root)
        {
            if (child == null)
                continue;
            if (child.name == nodeName)
                result.Add(child);
            CollectChildrenByName(child, nodeName, result);
        }
    }

    private Transform FindDeepChild(Transform root, string nodeName)
    {
        if (root == null)
            return null;

        foreach (Transform child in root)
        {
            if (child == null)
                continue;
            if (child.name == nodeName)
                return child;
            Transform nested = FindDeepChild(child, nodeName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void ApplyVisualRotation(TerrainDecorationDefinition definition)
    {
        if (visualRoot == null)
            visualRoot = transform.Find("VisualRoot");

        if (visualRoot == null)
            return;

        Vector3 euler = binder != null ? binder.visualLocalEuler : Vector3.zero;
        if (definition != null && !definition.enableVisualRandomRotation)
            euler = Vector3.zero;

        bool affectsRules = definition != null && definition.visualRandomRotationAffectsRules;
        visualRoot.localRotation = affectsRules ? Quaternion.identity : Quaternion.Euler(euler);
    }


    private void ApplyCollision(TerrainDecorationDefinition definition)
    {
        if (collisionRoot == null)
            EnsureStandardStructure(true);

        Transform box = collisionRoot.Find("Main_Collision_Box");
        if (box == null)
            box = EnsureChild(collisionRoot, "Main_Collision_Box", null);

        bool useBox = definition.collisionMode == TerrainDecorationCollisionMode.Box;
        box.gameObject.SetActive(useBox || definition.collisionMode == TerrainDecorationCollisionMode.CustomRoot);

        if (!useBox)
            return;

        BoxCollider collider = EnsureBoxCollider(box, false);

        Vector3 size = GetEffectiveCollisionSize(definition);
        Vector3 offset = GetEffectiveCollisionOffset(definition);

        box.localPosition = offset;
        box.localRotation = Quaternion.identity;
        box.localScale = Vector3.one;
        collider.center = Vector3.zero;
        collider.size = SanitizeSize(size);
    }

    private void ApplyRulePlane(TerrainDecorationDefinition definition)
    {
        if (planeOrigin == null || planeForwardReference == null)
            EnsureStandardStructure(true);

        Vector3 forward = GetRuleForward(definition);
        Vector3 origin = definition.rulePlaneOriginLocal;

        if (definition.frontBackPlaneMode == TerrainDecorationFrontBackPlaneMode.CollisionBounds)
        {
            Vector3 size = GetEffectiveCollisionSize(definition);
            Vector3 offset = GetEffectiveCollisionOffset(definition);
            float extent = GetAxisExtent(size, forward);
            origin = offset + forward * (extent + definition.planePushOutDistance);
        }

        planeOrigin.localPosition = origin;
        planeOrigin.localRotation = Quaternion.identity;
        planeOrigin.localScale = Vector3.one;

        planeForwardReference.localPosition = origin + forward;
        planeForwardReference.localRotation = Quaternion.identity;
        planeForwardReference.localScale = Vector3.one;

        if (sortAnchor != null)
        {
            // SimpleDirectionalOccluder 旧逻辑用 sortAnchor.z 做前方豁免。
            // 放在规则平面附近最稳定。
            sortAnchor.localPosition = origin;
            sortAnchor.localRotation = Quaternion.identity;
            sortAnchor.localScale = Vector3.one;
        }
    }

    private void Apply2p5DOcclusionProxies(TerrainDecorationDefinition definition, Bounds visualBounds)
    {
        Vector3 forward = GetRuleForward(definition);
        Vector3 absForward = new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
        bool depthOnX = absForward.x > absForward.z;

        Vector3 size = visualBounds.size;
        Vector3 center = visualBounds.center;
        size = SanitizeSize(size);

        float width = depthOnX ? size.z : size.x;
        float depth = depthOnX ? size.x : size.z;
        float height = size.y;
        float bottomY = visualBounds.min.y;

        float upperHeight = Mathf.Max(0.05f, height * Mathf.Clamp01(upperFitHeightRatio));
        float lowerHeight = Mathf.Max(0.05f, height - upperHeight);
        if (lowerHeight < 0.05f)
        {
            lowerHeight = Mathf.Max(0.05f, height * Mathf.Clamp01(lowerInsetHeightRatio));
            upperHeight = Mathf.Max(0.05f, height - lowerHeight);
        }

        float fullWidth = Mathf.Max(0.05f, width + proxyPaddingXZ * 2f);
        float fullDepth = Mathf.Max(0.05f, depth + proxyPaddingXZ * 2f);
        float lowerWidth = Mathf.Max(0.05f, width * lowerInsetWidthRatio);
        float lowerDepth = Mathf.Max(0.05f, depth * lowerInsetDepthRatio);

        Vector3 lowerCenter = center;
        lowerCenter.y = bottomY + lowerHeight * 0.5f;
        lowerCenter += forward.normalized * lowerFrontReserve;

        Vector3 upperCenter = center;
        upperCenter.y = bottomY + lowerHeight + upperHeight * 0.5f;

        Vector3 upperSize = depthOnX
            ? new Vector3(fullDepth, upperHeight + proxyPaddingY * 2f, fullWidth)
            : new Vector3(fullWidth, upperHeight + proxyPaddingY * 2f, fullDepth);

        Vector3 lowerSize = depthOnX
            ? new Vector3(lowerDepth, lowerHeight + proxyPaddingY * 2f, lowerWidth)
            : new Vector3(lowerWidth, lowerHeight + proxyPaddingY * 2f, lowerDepth);

        Transform upper = outlineMaskProxyRoot != null ? outlineMaskProxyRoot.Find("OutlineMaskProxy_T_Box") : null;
        if (upper == null)
            upper = EnsureChild(outlineMaskProxyRoot, "OutlineMaskProxy_T_Box", null);

        Transform lower = outlineMaskProxyRoot != null ? outlineMaskProxyRoot.Find("SubOutlineMaskProxy_01") : null;
        if (lower == null)
            lower = EnsureChild(outlineMaskProxyRoot, "SubOutlineMaskProxy_01", null);

        ConfigureProxyBox(upper, upperCenter, upperSize, true, GetLayer("OcclusionMask", GetLayer("Default", 0)));
        ConfigureProxyBox(lower, lowerCenter, lowerSize, true, GetLayer("OcclusionMask", GetLayer("Default", 0)));

        Transform frontProxy = frontOccluderRoot != null ? frontOccluderRoot.Find("FrontOccluderProxy_Box") : null;
        if (frontProxy == null)
            frontProxy = EnsureChild(frontOccluderRoot, "FrontOccluderProxy_Box", null);

        if (useCameraProjectedFrontProxyBounds)
        {
            ConfigureCameraProjectedFrontOccluderProxy(definition, frontProxy, visualBounds);
        }
        else
        {
            Vector3 frontSize = new Vector3(
                Mathf.Max(0.05f, size.x + proxyPaddingXZ * 2f),
                Mathf.Max(0.05f, size.y + proxyPaddingY * 2f),
                Mathf.Max(0.05f, size.z + proxyPaddingXZ * 2f));

            ConfigureProxyBox(frontProxy, center, frontSize, true, GetLayer("OcclusionMask", GetLayer("Default", 0)));

            // 不再把某一个实例手调出来的 Transform Position 写死到所有模型上。
            // FrontOccluderProxy_Box 的位置必须继续由规则空间的 center 自动决定；
            // 这里只在自动生成位置的基础上扩大 Collider，避免整体偏到模型外侧。
            if (useFrontOccluderProxySafePadding)
            {
                BoxCollider frontBox = frontProxy.GetComponent<BoxCollider>();
                if (frontBox != null)
                {
                    Vector3 multiplier;
                    Vector3 add;
                    Vector3 centerOffset;
                    ResolveFrontOccluderProxySafePadding(out multiplier, out add, out centerOffset);

                    Vector3 multipliedSize = Vector3.Scale(frontSize, multiplier);
                    Vector3 addedSize = frontSize + add;

                    // 注意：这里只改 Collider 的中心和大小，不改 Transform Position。
                    // Transform Position 仍由规则空间 center 自动生成，避免某个实例的手动位置污染所有模型。
                    frontBox.center = centerOffset;
                    frontBox.size = SanitizeSize(new Vector3(
                        Mathf.Max(multipliedSize.x, addedSize.x),
                        Mathf.Max(multipliedSize.y, addedSize.y),
                        Mathf.Max(multipliedSize.z, addedSize.z)));
                    frontBox.isTrigger = true;
                }
            }
        }

        ConfigureBackTrigger(definition, visualBounds, center, size);
        ConfigureFrontTrigger(definition, visualBounds, center, size);
        ConfigureVisionBlocker(definition);
    }

    private ProjectedFootprintBounds ResolveProjectedFootprintBoundsLocal(TerrainDecorationDefinition definition, float cameraYawDegrees, float rightPadding, float depthPadding)
    {
        Vector3 footprintCenter = definition != null ? GetEffectiveCollisionOffset(definition) : Vector3.zero;
        Vector3 footprintSize = definition != null ? GetEffectiveCollisionSize(definition) : Vector3.one;
        footprintSize = SanitizeSize(footprintSize);

        // 企业版关键：Footprint 的四个角必须由“物体自己的规则转角”生成，不能直接用世界 X/Z 轴。
        // 也就是说：物体自身转角 = ruleForward / ruleRight；镜头视角 = cameraForward / cameraRight。
        // 旧版用 footprintCenter + (+/-X, +/-Z) 生成角点，会在物体斜摆时把转角算错，
        // 进而把 BackTrigger / FrontProxy 投影到错误的大斜盒。
        Vector3 ruleForward = GetRuleForward(definition);
        ruleForward.y = 0f;
        if (ruleForward.sqrMagnitude < 0.0001f)
            ruleForward = Vector3.forward;
        ruleForward.Normalize();

        Vector3 ruleRight = new Vector3(ruleForward.z, 0f, -ruleForward.x);
        if (ruleRight.sqrMagnitude < 0.0001f)
            ruleRight = Vector3.right;
        ruleRight.Normalize();

        float halfWidth = Mathf.Abs(footprintSize.x) * 0.5f;
        float halfDepth = Mathf.Abs(footprintSize.z) * 0.5f;

        Vector3[] corners =
        {
            footprintCenter - ruleRight * halfWidth - ruleForward * halfDepth,
            footprintCenter - ruleRight * halfWidth + ruleForward * halfDepth,
            footprintCenter + ruleRight * halfWidth - ruleForward * halfDepth,
            footprintCenter + ruleRight * halfWidth + ruleForward * halfDepth,
        };

        Vector3 cameraForward;
        Vector3 cameraRight;
        ResolveFootprintProjectionAxes(cameraYawDegrees, out cameraForward, out cameraRight);

        ProjectedFootprintBounds result = new ProjectedFootprintBounds
        {
            corners = corners,
            cameraForward = cameraForward,
            cameraRight = cameraRight,
            minRight = float.PositiveInfinity,
            maxRight = float.NegativeInfinity,
            minDepth = float.PositiveInfinity,
            maxDepth = float.NegativeInfinity,
        };

        float rightPad = Mathf.Max(0f, rightPadding);
        float depthPad = Mathf.Max(0f, depthPadding);

        for (int i = 0; i < corners.Length; i++)
        {
            float r = Vector3.Dot(corners[i], cameraRight);
            float d = Vector3.Dot(corners[i], cameraForward);

            if (r < result.minRight) result.minRight = r;
            if (r > result.maxRight) result.maxRight = r;
            if (d < result.minDepth) result.minDepth = d;
            if (d > result.maxDepth) result.maxDepth = d;
        }

        if (float.IsInfinity(result.minRight) || float.IsInfinity(result.maxRight) ||
            float.IsInfinity(result.minDepth) || float.IsInfinity(result.maxDepth))
        {
            result.minRight = -0.5f;
            result.maxRight = 0.5f;
            result.minDepth = -0.5f;
            result.maxDepth = 0.5f;
        }

        result.minRight -= rightPad;
        result.maxRight += rightPad;
        result.minDepth -= depthPad;
        result.maxDepth += depthPad;

        return result;
    }

    private void ResolveFootprintProjectionAxes(float fallbackYawDegrees, out Vector3 cameraForward, out Vector3 cameraRight)
    {
        cameraForward = Vector3.zero;
        cameraRight = Vector3.zero;

        Transform cameraTransform = footprintProjectionCameraOverride;
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (useWorldCameraAxesForFootprintProjection && cameraTransform != null)
        {
            cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            cameraRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up);

            if (invertFootprintProjectionForward)
            {
                cameraForward = -cameraForward;
                cameraRight = -cameraRight;
            }
        }

        if (cameraForward.sqrMagnitude < 0.0001f)
            cameraForward = Quaternion.Euler(0f, fallbackYawDegrees, 0f) * Vector3.forward;

        cameraForward.y = 0f;
        if (cameraForward.sqrMagnitude < 0.0001f)
            cameraForward = Vector3.forward;
        cameraForward.Normalize();

        if (cameraRight.sqrMagnitude < 0.0001f)
            cameraRight = new Vector3(cameraForward.z, 0f, -cameraForward.x);

        cameraRight.y = 0f;
        if (cameraRight.sqrMagnitude < 0.0001f)
            cameraRight = Vector3.right;
        cameraRight.Normalize();

        // Keep a right-handed, perpendicular 2D basis even if the camera is slightly tilted or skewed.
        cameraRight = Vector3.Cross(Vector3.up, cameraForward).normalized;
        if (cameraRight.sqrMagnitude < 0.0001f)
            cameraRight = Vector3.right;
    }

    private void ConfigureCameraProjectedFrontOccluderProxy(TerrainDecorationDefinition definition, Transform frontProxy, Bounds visualBounds)
    {
        if (frontProxy == null)
            return;

        // 企业级核心：用“物体规则 Footprint 的转角 × 固定 2.5D 镜头轴”生成前景遮挡代理盒。
        // 这里不读取 Mesh / VisualRoot / Blender 坐标。角度问题统一由投影极值解决。
        float rightPadding = Mathf.Max(0f, frontProxyProjectedRightPadding + proxyPaddingXZ);
        float depthPadding = Mathf.Max(0f, frontProxyProjectedDepthPadding + proxyPaddingXZ);
        float heightPadding = Mathf.Max(0f, frontProxyProjectedHeightPadding + proxyPaddingY);

        ProjectedFootprintBounds projected = ResolveProjectedFootprintBoundsLocal(
            definition,
            frontProxyCameraYawDegrees,
            rightPadding,
            depthPadding);

        Vector3 projectedCenter = projected.CenterLocal;
        projectedCenter.y = visualBounds.center.y;

        Vector3 projectedSize = new Vector3(
            Mathf.Max(0.05f, projected.Width),
            Mathf.Max(0.05f, visualBounds.size.y + heightPadding * 2f),
            Mathf.Max(0.05f, projected.Depth));

        ConfigureOrientedProxyBox(
            frontProxy,
            projectedCenter,
            Quaternion.LookRotation(projected.cameraForward, Vector3.up),
            projectedSize,
            true,
            GetLayer("OcclusionMask", GetLayer("Default", 0)));

        if (useFrontOccluderProxySafePadding)
        {
            BoxCollider frontBox = frontProxy.GetComponent<BoxCollider>();
            if (frontBox != null)
            {
                Vector3 multiplier;
                Vector3 add;
                Vector3 centerOffset;
                ResolveFrontOccluderProxySafePadding(out multiplier, out add, out centerOffset);

                Vector3 multipliedSize = Vector3.Scale(projectedSize, multiplier);
                Vector3 addedSize = projectedSize + add;

                frontBox.center = centerOffset;
                frontBox.size = SanitizeSize(new Vector3(
                    Mathf.Max(multipliedSize.x, addedSize.x),
                    Mathf.Max(multipliedSize.y, addedSize.y),
                    Mathf.Max(multipliedSize.z, addedSize.z)));
                frontBox.isTrigger = true;
            }
        }
    }

    private Vector3 GetFrontProxyCameraForwardLocal()
    {
        Vector3 forward = Quaternion.Euler(0f, frontProxyCameraYawDegrees, 0f) * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();
        return forward;
    }

    private void ConfigureOrientedProxyBox(Transform tr, Vector3 localCenter, Quaternion localRotation, Vector3 localSize, bool isTrigger, int layer)
    {
        if (tr == null)
            return;

        tr.gameObject.SetActive(true);
        tr.localPosition = localCenter;
        tr.localRotation = localRotation;
        tr.localScale = Vector3.one;
        tr.gameObject.layer = layer;

        BoxCollider box = EnsureBoxCollider(tr, true);
        box.enabled = true;
        box.center = Vector3.zero;
        box.size = SanitizeSize(localSize);
        box.isTrigger = isTrigger;

        Renderer[] renderers = tr.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = false;
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
        }
    }

    private void ResolveFrontOccluderProxySafePadding(out Vector3 multiplier, out Vector3 add, out Vector3 centerOffset)
    {
        switch (frontOccluderProxyProfile)
        {
            case FrontOccluderProxyProfile.SmallProp:
                multiplier = new Vector3(1.10f, 1.12f, 1.30f);
                add = new Vector3(0.25f, 0.35f, 0.55f);
                centerOffset = Vector3.zero;
                break;

            case FrontOccluderProxyProfile.Wall:
                multiplier = new Vector3(1.18f, 1.18f, 1.75f);
                add = new Vector3(0.50f, 0.70f, 1.60f);
                centerOffset = Vector3.zero;
                break;

            case FrontOccluderProxyProfile.Pillar:
                multiplier = new Vector3(1.35f, 1.18f, 1.35f);
                add = new Vector3(0.70f, 0.60f, 0.80f);
                centerOffset = Vector3.zero;
                break;

            case FrontOccluderProxyProfile.Irregular:
                multiplier = new Vector3(1.28f, 1.25f, 1.85f);
                add = new Vector3(0.80f, 1.00f, 1.80f);
                centerOffset = Vector3.zero;
                break;

            case FrontOccluderProxyProfile.Custom:
                multiplier = customFrontOccluderProxySizeMultiplier;
                add = customFrontOccluderProxySizeAdd;
                centerOffset = customFrontOccluderProxyColliderCenterOffset;
                break;

            case FrontOccluderProxyProfile.Block:
            default:
                // 已由水泥墩实例验证过的通用箱体安全边界。
                // X/Y 轻微扩大，Z 明显加深；只扩大 Collider，不移动 Transform。
                multiplier = new Vector3(1.20f, 1.20f, 1.55f);
                add = new Vector3(0.60f, 0.80f, 1.20f);
                centerOffset = Vector3.zero;
                break;
        }

        multiplier = new Vector3(
            Mathf.Max(1f, multiplier.x),
            Mathf.Max(1f, multiplier.y),
            Mathf.Max(1f, multiplier.z));

        add = new Vector3(
            Mathf.Max(0f, add.x),
            Mathf.Max(0f, add.y),
            Mathf.Max(0f, add.z));
    }

    private void ConfigureBackTrigger(TerrainDecorationDefinition definition, Bounds visualBounds, Vector3 center, Vector3 size)
    {
        if (backTrigger == null)
            return;

        if (useProjectedFootprintBackTrigger)
        {
            ConfigureProjectedFootprintBackTrigger(definition, visualBounds);
            return;
        }

        // 重要：BackTrigger 是 2.5D 规则盒，不是模型盒。
        // 它必须固定在地图 / 世界 XZ 规则轴上，不能继承 Blender 模型修正旋转，也不能跟随实例摆放旋转。
        //
        // 轴定义：
        // - BoxCollider.size.x = 地图规则横向宽度。
        // - BoxCollider.size.y = 世界竖直高度。
        // - BoxCollider.size.z = 地图规则前后厚度，也就是真正的“背部厚度”。
        // - BackTrigger.transform.forward 在世界中永远等于 ruleForwardWorld。
        Vector3 ruleForwardWorld = GetRuleForward(definition);
        ruleForwardWorld.y = 0f;
        if (ruleForwardWorld.sqrMagnitude < 0.0001f)
            ruleForwardWorld = Vector3.forward;
        ruleForwardWorld.Normalize();

        Vector3 ruleRightWorld = new Vector3(ruleForwardWorld.z, 0f, -ruleForwardWorld.x);
        if (ruleRightWorld.sqrMagnitude < 0.0001f)
            ruleRightWorld = Vector3.right;
        ruleRightWorld.Normalize();

        Vector3 objectCenterWorld;
        Vector3 ruleSize;
        ResolveBackTriggerWorldRuleSource(definition, visualBounds, center, size, out objectCenterWorld, out ruleSize);

        // ruleSize 是地图规则 X/Z 尺寸，不是 Blender 模型自身坐标。
        float modelHalfDepth = GetProjectedHalfExtentXZ(ruleSize, ruleForwardWorld);
        float modelHalfWidth = GetProjectedHalfExtentXZ(ruleSize, ruleRightWorld);

        float modelDepth = Mathf.Max(0.05f, modelHalfDepth * 2f);
        float modelWidth = Mathf.Max(0.05f, modelHalfWidth * 2f);

        float multiplierExtra = modelDepth * Mathf.Max(0f, backTriggerDepthMultiplier - 1f);

        // 背部额外厚度：这是真正往背后延伸的厚度，不是总深度，也不会被父节点旋转改轴。
        // 这版把厚度再收薄，但用 rearShiftFactor 把盒子整体往规则背面推，避免靠前覆盖太多。
        float minBackBandDepth = Mathf.Max(0f, backTriggerMinDepth, modelDepth * 0.95f);
        float backBandDepth = Mathf.Clamp(Mathf.Max(multiplierExtra, minBackBandDepth), 1.75f, 3.60f);

        float triggerDepth = Mathf.Max(0.10f, modelDepth + backBandDepth);

        float widthMultiplier = Mathf.Clamp(backTriggerWidthMultiplier, 1.00f, 3.00f);
        float minWidthByPadding = modelWidth + Mathf.Max(0f, backTriggerExtraWidth);
        float angle = Mathf.Clamp(backTriggerExposureAngle, 0f, 75f);
        float exposureTangent = Mathf.Tan(angle * Mathf.Deg2Rad);
        float exposureSafety = Mathf.Clamp(backTriggerExposureSafety, 0.20f, 1.20f);
        float requiredWidthByCameraExposure = modelWidth + backBandDepth * 2f * exposureTangent * exposureSafety;

        float triggerWidth = Mathf.Max(
            0.10f,
            modelWidth * widthMultiplier,
            minWidthByPadding,
            requiredWidthByCameraExposure);

        float triggerHeight = Mathf.Clamp(Mathf.Max(1.20f, backTriggerExtraHeight), 1.20f, 2.80f);
        float bottomY = 0f;

        // 额外厚度向世界规则背面延伸。
        // +ruleForwardWorld 是当前项目里确认过的背面方向。
        // 0.5 表示新增背部带刚好从模型后沿开始；这里用更大的 rearShiftFactor，
        // 让盒子整体往后挪，同时厚度本身更薄。
        // 注意这里在世界坐标里算中心，然后转回父节点本地坐标，避免父节点 / 实例旋转污染方向。
        float rearShiftFactor = Mathf.Clamp(backTriggerRearShiftFactor, 0.50f, 0.90f);
        Vector3 triggerCenterWorld = objectCenterWorld + ruleForwardWorld * (backBandDepth * rearShiftFactor);
        triggerCenterWorld.y = transform.position.y + bottomY + triggerHeight * 0.5f;

        Quaternion triggerWorldRotation = Quaternion.LookRotation(ruleForwardWorld, Vector3.up);

        Transform parent = backTrigger.parent;
        if (parent != null)
        {
            backTrigger.localPosition = parent.InverseTransformPoint(triggerCenterWorld);
            backTrigger.localRotation = Quaternion.Inverse(parent.rotation) * triggerWorldRotation;
        }
        else
        {
            backTrigger.position = triggerCenterWorld;
            backTrigger.rotation = triggerWorldRotation;
        }

        backTrigger.localScale = Vector3.one;

        BoxCollider trigger = EnsureBoxCollider(backTrigger, true);
        trigger.center = Vector3.zero;
        trigger.size = SanitizeSize(new Vector3(triggerWidth, triggerHeight, triggerDepth));
        trigger.enabled = definition.occlusionMode != TerrainDecorationOcclusionMode.None;
    }

    private void ConfigureProjectedFootprintBackTrigger(TerrainDecorationDefinition definition, Bounds visualBounds)
    {
        // BackTrigger 的企业版修正：
        // - 投影极值只用于找到“视觉背部转折线”，不能直接把完整投影包围盒当作 Trigger。
        // - Trigger 是一条从背部转折线之后开始的带状区域。
        // - Footprint 角点来自物体自己的 ruleForward / ruleRight，再投影到固定镜头轴。
        float sidePadding = Mathf.Max(0f, projectedBackTriggerSidePadding + backTriggerExtraWidth * 0.5f);
        ProjectedFootprintBounds projected = ResolveProjectedFootprintBoundsLocal(
            definition,
            footprintProjectionCameraYawDegrees,
            sidePadding,
            0f);

        float footprintDepth = Mathf.Max(0.05f, projected.Depth);
        float multiplierExtra = footprintDepth * Mathf.Max(0f, backTriggerDepthMultiplier - 1f);
        float minBandDepth = Mathf.Max(0f, backTriggerMinDepth, footprintDepth * 0.55f);
        float bandDepth = Mathf.Clamp(
            Mathf.Max(multiplierExtra, minBandDepth) + Mathf.Max(0f, projectedBackTriggerDepthPadding),
            1.20f,
            4.20f);

        // BackTrigger 的左右宽度只取投影后的左右极值 + padding。
        // 不再把 projected.Width 再乘一个很大的倍率，避免之前那种巨型斜盒。
        float triggerWidth = Mathf.Max(
            0.10f,
            projected.Width + Mathf.Max(0f, backTriggerExtraWidth));

        float triggerHeight = Mathf.Clamp(Mathf.Max(1.20f, backTriggerExtraHeight), 1.20f, 2.80f);

        // 当前项目的固定镜头/地图规则中，视觉“背后”在投影 depth 的 min 侧。
        // 因此 projectedBackTriggerUseMaxDepthAsBack 默认设为 false。
        // 如果后续换镜头方向或地图规则，仍可在 Inspector 中反转这个开关。
        bool useMaxAsBack = projectedBackTriggerUseMaxDepthAsBack;
        Vector3 behindAxis = useMaxAsBack ? projected.cameraForward : -projected.cameraForward;
        float backEdgeDepth = useMaxAsBack ? projected.maxDepth : projected.minDepth;
        float safety = Mathf.Max(0f, projectedBackTriggerFrontSafetyOffset);

        // BackTrigger 前缘：从视觉背部转折线之后开始。
        // useMaxAsBack=true  : [maxDepth + safety, maxDepth + safety + bandDepth]
        // useMaxAsBack=false : [minDepth - safety - bandDepth, minDepth - safety]
        float centerDepth = useMaxAsBack
            ? backEdgeDepth + safety + bandDepth * 0.5f
            : backEdgeDepth - safety - bandDepth * 0.5f;

        float centerRight = projected.CenterRight;

        Vector3 centerLocalXZ = projected.cameraRight * centerRight + projected.cameraForward * centerDepth;
        Vector3 triggerCenterWorld = transform.position + new Vector3(centerLocalXZ.x, 0f, centerLocalXZ.z);
        triggerCenterWorld.y = transform.position.y + triggerHeight * 0.5f;

        Quaternion triggerWorldRotation = Quaternion.LookRotation(behindAxis, Vector3.up);

        Transform parent = backTrigger.parent;
        if (parent != null)
        {
            backTrigger.localPosition = parent.InverseTransformPoint(triggerCenterWorld);
            backTrigger.localRotation = Quaternion.Inverse(parent.rotation) * triggerWorldRotation;
        }
        else
        {
            backTrigger.position = triggerCenterWorld;
            backTrigger.rotation = triggerWorldRotation;
        }

        backTrigger.localScale = Vector3.one;

        BoxCollider trigger = EnsureBoxCollider(backTrigger, true);
        trigger.center = Vector3.zero;
        trigger.size = SanitizeSize(new Vector3(triggerWidth, triggerHeight, bandDepth));
        trigger.enabled = definition == null || definition.occlusionMode != TerrainDecorationOcclusionMode.None;
    }

    private void ResolveBackTriggerWorldRuleSource(
        TerrainDecorationDefinition definition,
        Bounds visualBounds,
        Vector3 visualCenter,
        Vector3 visualSize,
        out Vector3 objectCenterWorld,
        out Vector3 ruleSize)
    {
        objectCenterWorld = transform.position;
        ruleSize = new Vector3(1f, 1f, 1f);

        // 首选规则碰撞数据。这里把 collisionOffset 当作“地图规则 XZ 偏移”，不经过 TransformPoint，
        // 所以不会被实例默认旋转 / Blender 90 度修正旋转带偏。
        if (definition != null)
        {
            Vector3 collisionSize = GetEffectiveCollisionSize(definition);
            Vector3 collisionOffset = GetEffectiveCollisionOffset(definition);

            if (collisionSize.sqrMagnitude > 0.000001f)
            {
                objectCenterWorld = transform.position + new Vector3(collisionOffset.x, 0f, collisionOffset.z);
                ruleSize = SanitizeSize(new Vector3(
                    Mathf.Abs(collisionSize.x),
                    1f,
                    Mathf.Abs(collisionSize.z)));
                return;
            }
        }

        // 兜底：没有规则碰撞数据时才用 visualBounds。
        // visualBounds 只能兜底，因为它可能已经混入模型导入轴。
        objectCenterWorld = new Vector3(visualBounds.center.x, transform.position.y, visualBounds.center.z);
        ruleSize = SanitizeSize(new Vector3(Mathf.Abs(visualBounds.size.x), 1f, Mathf.Abs(visualBounds.size.z)));
    }

    private void ConfigureFrontTrigger(TerrainDecorationDefinition definition, Bounds visualBounds, Vector3 center, Vector3 size)
    {
        if (frontTrigger == null)
            return;

        frontTrigger.localPosition = center;
        frontTrigger.localRotation = Quaternion.identity;
        frontTrigger.localScale = Vector3.one;

        BoxCollider trigger = EnsureBoxCollider(frontTrigger, true);
        trigger.center = Vector3.zero;
        trigger.size = SanitizeSize(new Vector3(size.x + 0.2f, size.y + 0.2f, size.z + 0.2f));
        trigger.enabled = definition.occlusionMode != TerrainDecorationOcclusionMode.None;
    }

    private void ConfigureVisionBlocker(TerrainDecorationDefinition definition)
    {
        if (visionBlockerRoot == null)
            return;

        Transform box = visionBlockerRoot.Find("Vision_Blocker_Box");
        if (box == null)
            box = EnsureChild(visionBlockerRoot, "Vision_Blocker_Box", null);

        bool active = definition.blockVision;
        box.gameObject.SetActive(active);
        if (!active)
            return;

        Vector3 size = GetEffectiveCollisionSize(definition);
        Vector3 offset = GetEffectiveCollisionOffset(definition);

        box.localPosition = offset;
        box.localRotation = Quaternion.identity;
        box.localScale = Vector3.one;

        BoxCollider collider = EnsureBoxCollider(box, true);
        collider.center = Vector3.zero;
        collider.size = SanitizeSize(size);
    }

    private void ApplyOcclusionDriver(TerrainDecorationDefinition definition)
    {
        if (backTrigger == null)
            return;

        bool active = definition.occlusionMode != TerrainDecorationOcclusionMode.None;

        BoxCollider trigger = backTrigger.GetComponent<BoxCollider>();
        if (trigger == null)
            trigger = backTrigger.gameObject.AddComponent<BoxCollider>();

        trigger.isTrigger = true;
        trigger.enabled = active;

        // 核心修复：SimpleDirectionalOccluder 必须挂在真正接收 Trigger 事件的 BackTrigger 节点上。
        // 旧版本如果被挂到 RuleRoot / 实例根 / 其他子节点，会引用 BackTrigger Collider，
        // 但 Unity 不会把 BackTrigger 的 OnTriggerEnter 转发给那个组件。
        // 所以这里每次 Apply 都自动清理错误位置，只保留 BackTrigger 上的一份驱动脚本。
        SimpleDirectionalOccluder occluder = EnsureOcclusionDriverOnBackTrigger();

        if (active)
        {
            // 地形装饰物不再通过“模型本体透明”制造遮挡关系。
            // 正确链路是：SimpleDirectionalOccluder 只做判定并通知角色的 IOcclusionStateReceiver；
            // 角色材质 / 描边代理负责隐藏与遮挡 Mask 重合的身体区域。
            // 这里显式断开 OccluderVisualFader，避免 Opaque 模型被强制透明后看穿内部。
            OccluderVisualFader existingFader = GetComponent<OccluderVisualFader>();
            if (existingFader != null)
                existingFader.SetOccluded(false);

            // 与已跑通的原型盒子保持一致：SimpleDirectionalOccluder 的 Occlusion Volumes
            // 只绑定 FrontOccluderProxy_Box。OutlineMaskProxy_T_Box / SubOutlineMaskProxy_01
            // 是遮罩形状辅助，不直接放进候选体积列表，避免角色侧拿到错误代理关系。
            BoxCollider frontVolume = ResolveFrontOccluderVolumeCollider();
            BoxCollider[] volumes = frontVolume != null ? new[] { frontVolume } : Array.Empty<BoxCollider>();

            occluder.ConfigureForTerrainDecoration(trigger, sortAnchor, volumes, null);
            occluder.enabled = true;
        }
        else if (occluder != null)
        {
            occluder.enabled = false;
        }
    }

    private SimpleDirectionalOccluder EnsureOcclusionDriverOnBackTrigger()
    {
        if (backTrigger == null)
            return null;

        SimpleDirectionalOccluder correct = backTrigger.GetComponent<SimpleDirectionalOccluder>();
        if (correct == null)
            correct = backTrigger.gameObject.AddComponent<SimpleDirectionalOccluder>();

        // 清理/禁用挂错位置的同类组件，避免旧 RuleRoot 脚本下一帧把 __AutoFrontOccluderShapeClone 关掉。
        SimpleDirectionalOccluder[] all = GetComponentsInChildren<SimpleDirectionalOccluder>(true);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            SimpleDirectionalOccluder other = all[i];
            if (other == null || other == correct)
                continue;

            if (other.transform == backTrigger)
            {
                SafeDestroyImmediate(other);
                continue;
            }

            other.enabled = false;
            SafeDestroyImmediate(other);
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(backTrigger.gameObject);
#endif
        return correct;
    }

    private void ApplyShadowSettings(TerrainDecorationDefinition definition)
    {
        if (visualRoot == null)
            visualRoot = transform.Find("VisualRoot");
        if (shadowCasterRoot == null)
            shadowCasterRoot = transform.Find("ShadowCasterRoot");

        if (shadowCasterRoot != null && definition != null)
        {
            bool shadowRootActive = definition.shadowMode != TerrainDecorationShadowMode.None;
            shadowCasterRoot.gameObject.SetActive(shadowRootActive);
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            bool isVisual = visualRoot != null && r.transform.IsChildOf(visualRoot);
            bool isShadow = shadowCasterRoot != null && r.transform.IsChildOf(shadowCasterRoot);
            bool isProxy = IsProxyRenderer(r);

            if (isProxy)
            {
                if (hideGeneratedProxyRenderers)
                    r.enabled = false;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                continue;
            }

            if (isVisual && definition != null)
            {
                r.enabled = true;
                r.shadowCastingMode = definition.castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = definition.receiveShadow;
            }
            else if (isShadow && definition != null)
            {
                if (definition.shadowMode == TerrainDecorationShadowMode.ShadowsOnlyProxy)
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                else if (definition.shadowMode == TerrainDecorationShadowMode.ShadowCasterProxy)
                    r.shadowCastingMode = ShadowCastingMode.On;
                else
                    r.shadowCastingMode = ShadowCastingMode.Off;

                r.receiveShadows = false;
            }
        }
    }


    private void ApplyMaterialSlots(TerrainDecorationDefinition definition)
    {
        TerrainDecorationVariant variant = binder != null ? binder.GetSelectedVariant() : definition.GetFirstVariant();
        if (variant == null || variant.materialSlots == null)
            return;

        for (int i = 0; i < variant.materialSlots.Count; i++)
        {
            TerrainDecorationMaterialSlot slot = variant.materialSlots[i];
            if (slot == null || slot.defaultMaterial == null || string.IsNullOrWhiteSpace(slot.rendererPath))
                continue;

            Transform rendererTr = ResolveMaterialSlotTransform(slot.rendererPath);
            if (rendererTr == null)
                continue;

            Renderer renderer = rendererTr.GetComponent<Renderer>();
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || slot.materialIndex < 0 || slot.materialIndex >= materials.Length)
                continue;

            materials[slot.materialIndex] = slot.defaultMaterial;
            renderer.sharedMaterials = materials;
        }
    }

    private Transform ResolveMaterialSlotTransform(string rendererPath)
    {
        if (string.IsNullOrWhiteSpace(rendererPath))
            return null;

        string path = rendererPath.Replace('\\', '/').Trim();

        // Page 扫描根节点 Renderer 时，会把 Renderer Path 写成 PF 名称，方便人读。
        // Scene 实例名可能变成中文名或带随机后缀，所以只要是单段 PF 路径且根节点有 Renderer，就回到根节点。
        if (!path.Contains("/"))
        {
            Renderer rootRenderer = GetComponent<Renderer>();
            if (rootRenderer != null)
                return transform;

            // 兼容旧版已经把 PF 根视觉迁移到 VisualRoot/Visual_01 的 Prefab。
            // MAT 槽仍显示 PF 名，但实际 Renderer 暂时还在 Visual_01 时，也能正确应用材质。
            if (visualRoot != null)
            {
                Transform migrated = visualRoot.Find("Visual_01");
                if (migrated != null && migrated.GetComponent<Renderer>() != null)
                    return migrated;
            }
        }

        Transform rendererTr = transform.Find(path);
        if (rendererTr == null && visualRoot != null)
            rendererTr = visualRoot.Find(path);
        return rendererTr;
    }

    private void ApplyLayerStructure()
    {
        int defaultLayer = GetLayer("Default", 0);
        int worldLayer = GetLayer("World3D", defaultLayer);
        int foregroundLayer = GetLayer("ForegroundOccluder", defaultLayer);
        int occlusionMaskLayer = GetLayer("OcclusionMask", defaultLayer);

        if (visualRoot == null) visualRoot = transform.Find("VisualRoot");
        if (shadowCasterRoot == null) shadowCasterRoot = transform.Find("ShadowCasterRoot");
        if (mossRoot == null) mossRoot = transform.Find("MossRoot");
        if (fxRoot == null) fxRoot = transform.Find("FXRoot");
        if (ruleRoot == null) ruleRoot = transform.Find("RuleRoot");

        if (ruleRoot != null)
        {
            if (collisionRoot == null) collisionRoot = ruleRoot.Find("CollisionRoot");
            if (visionBlockerRoot == null) visionBlockerRoot = ruleRoot.Find("VisionBlockerRoot");
            if (backTrigger == null) backTrigger = ruleRoot.Find("BackTrigger");
            if (frontTrigger == null) frontTrigger = ruleRoot.Find("FrontTrigger");
            if (frontOccluderRoot == null) frontOccluderRoot = ruleRoot.Find("FrontOccluderRoot");
            if (outlineMaskProxyRoot == null) outlineMaskProxyRoot = ruleRoot.Find("OutlineMaskProxyRoot");
            if (sortAnchor == null) sortAnchor = ruleRoot.Find("SortAnchor");
            if (planeOrigin == null) planeOrigin = ruleRoot.Find("PlaneOrigin");
            if (planeForwardReference == null) planeForwardReference = ruleRoot.Find("PlaneForwardReference");
        }

        if (stencilWriterRoot == null) stencilWriterRoot = transform.Find("StencilWriterRoot");
        if (editorGizmoRoot == null) editorGizmoRoot = transform.Find("EditorGizmoRoot");

        SetLayerRecursive(visualRoot, worldLayer);
        SetLayerRecursive(shadowCasterRoot, worldLayer);
        SetLayerRecursive(mossRoot, worldLayer);
        SetLayerRecursive(fxRoot, worldLayer);

        SetLayerRecursive(frontOccluderRoot, foregroundLayer);
        SetLayerRecursive(outlineMaskProxyRoot, occlusionMaskLayer);
        SetLayerRecursive(stencilWriterRoot, occlusionMaskLayer);

        SetLayerRecursive(ruleRoot, defaultLayer, false);
        SetLayerRecursive(collisionRoot, worldLayer);
        SetLayerRecursive(visionBlockerRoot, worldLayer);
        SetLayerRecursive(backTrigger, worldLayer);
        SetLayerRecursive(frontTrigger, worldLayer);
        SetLayerRecursive(sortAnchor, defaultLayer);
        SetLayerRecursive(planeOrigin, defaultLayer);
        SetLayerRecursive(planeForwardReference, defaultLayer);
        SetLayerRecursive(editorGizmoRoot, defaultLayer);
    }


    private void EnforceGeneratedOcclusionLayers()
    {
        int defaultLayer = GetLayer("Default", 0);
        int worldLayer = GetLayer("World3D", defaultLayer);
        int foregroundLayer = GetLayer("ForegroundOccluder", defaultLayer);
        int occlusionMaskLayer = GetLayer("OcclusionMask", defaultLayer);

        // 与已跑通的原型盒子保持一致：
        // FrontOccluderProxy_Box 虽然使用 M_OcclusionMask_ForegroundOccluder / Custom/FrontOccluder，
        // 但 Layer 仍然是 OcclusionMask，而不是 ForegroundOccluder。
        // ForegroundOccluder 层暂时不用于这条地形装饰物遮挡链，避免和原型关系错开。
        _ = foregroundLayer;
        SetLayerRecursive(frontOccluderRoot, occlusionMaskLayer);
        SetLayerRecursive(outlineMaskProxyRoot, occlusionMaskLayer);
        SetLayerRecursive(stencilWriterRoot, occlusionMaskLayer);

        // 触发器和实体碰撞仍然留在 World3D，不能被前景遮挡层污染。
        SetLayerRecursive(collisionRoot, worldLayer);
        SetLayerRecursive(visionBlockerRoot, worldLayer);
        SetLayerRecursive(backTrigger, worldLayer);
        SetLayerRecursive(frontTrigger, worldLayer);

        // 普通视觉模型仍是 World3D。注意：这里不改材质，不扫描 MAT，不动 PF 贴图逻辑。
        SetLayerRecursive(visualRoot, worldLayer);
        SetLayerRecursive(shadowCasterRoot, worldLayer);
        SetLayerRecursive(mossRoot, worldLayer);
        SetLayerRecursive(fxRoot, worldLayer);

        if (ruleRoot != null)
            ruleRoot.gameObject.layer = defaultLayer;
        if (sortAnchor != null)
            sortAnchor.gameObject.layer = defaultLayer;
        if (planeOrigin != null)
            planeOrigin.gameObject.layer = defaultLayer;
        if (planeForwardReference != null)
            planeForwardReference.gameObject.layer = defaultLayer;
        if (editorGizmoRoot != null)
            SetLayerRecursive(editorGizmoRoot, defaultLayer);
    }

    private void SetLayerRecursive(Transform root, int layer, bool includeChildren = true)
    {
        if (root == null || layer < 0)
            return;

        root.gameObject.layer = layer;
        if (!includeChildren)
            return;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }

    private int GetLayer(string layerName, int fallback)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : fallback;
    }

    private void DisableGeneratedRuleColliders()
    {
        SetCollidersEnabled(collisionRoot, false);
        SetCollidersEnabled(visionBlockerRoot, false);
        SetCollidersEnabled(backTrigger, false);
        SetCollidersEnabled(frontTrigger, false);
        SetCollidersEnabled(frontOccluderRoot, false);
        SetCollidersEnabled(outlineMaskProxyRoot, false);
    }

    private void SetCollidersEnabled(Transform root, bool enabled)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = enabled;
        }
    }

    private Bounds CalculateVisualFullFootprintInRuleSpace(Bounds fallbackBounds)
    {
        // 碰撞盒矫正采用完整占地外包络。
        // 同时采样 Mesh 顶点和 Renderer Bounds 八角点：
        // - Mesh 顶点保证真实模型轮廓参与计算；
        // - Renderer Bounds 兜底斜切模型、梯形块、导入资源 Bounds 与 Mesh 顶点不一致的情况。
        bool has = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);

        if (visualRoot == null || ruleRoot == null)
            return fallbackBounds;

        Renderer[] renderers = CollectVisualRenderers();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (IsProxyRenderer(renderer))
                continue;

            EncapsulateRendererFootprintBounds(renderer, ref result, ref has);

            Mesh mesh = null;
            Matrix4x4 localToWorld = renderer.localToWorldMatrix;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
                mesh = meshFilter.sharedMesh;
            else
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null)
                    mesh = skinned.sharedMesh;
            }

            if (mesh == null)
                continue;

            Vector3[] vertices = mesh.vertices;
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(vertices[v]);
                Vector3 local = ruleRoot.InverseTransformPoint(world);
                EncapsulatePoint(ref result, ref has, local);
            }
        }

        if (!has || result.size.x < 0.02f || result.size.z < 0.02f)
            return fallbackBounds;

        return result;
    }

    private void EncapsulatePoint(ref Bounds result, ref bool has, Vector3 point)
    {
        if (!has)
        {
            result = new Bounds(point, Vector3.zero);
            has = true;
        }
        else
        {
            result.Encapsulate(point);
        }
    }

    private void EncapsulateRendererFootprintBounds(Renderer renderer, ref Bounds result, ref bool has)
    {
        if (renderer == null || ruleRoot == null)
            return;

        Bounds worldBounds = renderer.bounds;
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z),
        };

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 local = ruleRoot.InverseTransformPoint(corners[i]);
            EncapsulatePoint(ref result, ref has, local);
        }
    }

    private Bounds CalculateVisualBaseFootprintInRuleSpace(Bounds fallbackBounds, float bottomHeightRatio)
    {
        bool has = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);

        if (visualRoot == null || ruleRoot == null)
            return fallbackBounds;

        float bottomLimit = fallbackBounds.min.y + Mathf.Max(0.02f, fallbackBounds.size.y * Mathf.Clamp01(bottomHeightRatio));
        Renderer[] renderers = CollectVisualRenderers();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            Mesh mesh = null;
            Matrix4x4 localToWorld = renderer.localToWorldMatrix;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
                mesh = meshFilter.sharedMesh;
            else
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null)
                    mesh = skinned.sharedMesh;
            }

            if (mesh == null)
            {
                EncapsulateRendererBottomBounds(renderer, bottomLimit, ref result, ref has);
                continue;
            }

            Vector3[] vertices = mesh.vertices;
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(vertices[v]);
                Vector3 local = ruleRoot.InverseTransformPoint(world);
                if (local.y > bottomLimit)
                    continue;

                Vector3 flat = new Vector3(local.x, fallbackBounds.min.y, local.z);
                if (!has)
                {
                    result = new Bounds(flat, Vector3.zero);
                    has = true;
                }
                else
                {
                    result.Encapsulate(flat);
                }
            }
        }

        if (!has || result.size.x < 0.02f || result.size.z < 0.02f)
            return fallbackBounds;

        result.Encapsulate(new Vector3(result.min.x, fallbackBounds.max.y, result.min.z));
        result.Encapsulate(new Vector3(result.max.x, fallbackBounds.max.y, result.max.z));
        return result;
    }

    private void EncapsulateRendererBottomBounds(Renderer renderer, float bottomLimit, ref Bounds result, ref bool has)
    {
        if (renderer == null || ruleRoot == null)
            return;

        Bounds worldBounds = renderer.bounds;
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        Vector3[] bottomCorners =
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
        };

        for (int i = 0; i < bottomCorners.Length; i++)
        {
            Vector3 local = ruleRoot.InverseTransformPoint(bottomCorners[i]);
            if (local.y > bottomLimit)
                continue;

            Vector3 flat = new Vector3(local.x, local.y, local.z);
            if (!has)
            {
                result = new Bounds(flat, Vector3.zero);
                has = true;
            }
            else
            {
                result.Encapsulate(flat);
            }
        }
    }

    private VisualBoundsResult CalculateVisualBoundsInRuleSpace()
    {
        VisualBoundsResult result = new VisualBoundsResult();
        if (visualRoot == null || ruleRoot == null)
            return result;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (IsProxyRenderer(r))
                continue;

            Bounds worldBounds = r.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };

            for (int c = 0; c < corners.Length; c++)
            {
                Vector3 local = ruleRoot.InverseTransformPoint(corners[c]);
                if (!has)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        result.hasBounds = has;
        result.bounds = bounds;
        return result;
    }

    private bool IsProxyRenderer(Renderer r)
    {
        if (r == null)
            return false;

        Transform tr = r.transform;
        return
            (stencilWriterRoot != null && tr.IsChildOf(stencilWriterRoot)) ||
            (frontOccluderRoot != null && tr.IsChildOf(frontOccluderRoot)) ||
            (outlineMaskProxyRoot != null && tr.IsChildOf(outlineMaskProxyRoot)) ||
            (collisionRoot != null && tr.IsChildOf(collisionRoot)) ||
            (visionBlockerRoot != null && tr.IsChildOf(visionBlockerRoot));
    }

    private Renderer[] CollectVisualRenderers()
    {
        if (visualRoot == null)
            return Array.Empty<Renderer>();

        List<Renderer> result = new List<Renderer>();
        Renderer[] renderers = CollectOcclusionSourceRenderers();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r != null && !IsProxyRenderer(r))
                result.Add(r);
        }
        return result.ToArray();
    }

    private void CollectBoxColliders(Transform root, List<BoxCollider> result)
    {
        if (root == null || result == null)
            return;

        BoxCollider[] boxes = root.GetComponentsInChildren<BoxCollider>(true);
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i] != null && boxes[i].enabled && !result.Contains(boxes[i]))
                result.Add(boxes[i]);
        }
    }

    private void NormalizeGeneratedRuleColliderPolicy(TerrainDecorationDefinition definition)
    {
        // 只有 CollisionRoot/Main_Collision_Box 允许承担真实阻挡。
        // 所有 2.5D 遮挡、前后开关、视野遮挡代理都必须是 Trigger，避免角色被装饰物的规则盒卡住。
        ForceTriggerCollider(backTrigger, true);
        ForceTriggerCollider(frontTrigger, true);
        ForceTriggerColliders(frontOccluderRoot, true);
        ForceTriggerColliders(outlineMaskProxyRoot, true);
        ForceTriggerColliders(visionBlockerRoot, true);

        Transform mainCollision = collisionRoot != null ? collisionRoot.Find("Main_Collision_Box") : null;
        if (mainCollision != null)
        {
            BoxCollider box = mainCollision.GetComponent<BoxCollider>();
            if (box != null)
            {
                bool useBox = definition != null && definition.collisionMode == TerrainDecorationCollisionMode.Box;
                box.isTrigger = false;
                box.enabled = useBox;
                mainCollision.gameObject.SetActive(useBox || (definition != null && definition.collisionMode == TerrainDecorationCollisionMode.CustomRoot));
            }
        }
    }

    private void ForceTriggerCollider(Transform root, bool removeNonBoxColliders)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponents<Collider>();
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            Collider c = colliders[i];
            if (c == null)
                continue;

            if (removeNonBoxColliders && !(c is BoxCollider))
            {
                SafeDestroyImmediate(c);
                continue;
            }

            c.isTrigger = true;
        }
    }

    private void ForceTriggerColliders(Transform root, bool removeNonBoxColliders)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            Collider c = colliders[i];
            if (c == null)
                continue;

            if (removeNonBoxColliders && !(c is BoxCollider))
            {
                SafeDestroyImmediate(c);
                continue;
            }

            c.isTrigger = true;
        }
    }

    private void ConfigureProxyBox(Transform tr, Vector3 localCenter, Vector3 localSize, bool isTrigger, int layer)
    {
        if (tr == null)
            return;

        tr.gameObject.SetActive(true);
        tr.localPosition = localCenter;
        tr.localRotation = Quaternion.identity;
        tr.localScale = Vector3.one;
        tr.gameObject.layer = layer;

        // 规则代理盒永远只能作为遮挡/判定体积，不能成为实体碰撞。
        // 即使旧 Prefab 上保存过 isTrigger=false，自动矫正也会在这里强制改回 Trigger。
        BoxCollider box = EnsureBoxCollider(tr, true);
        box.enabled = true;
        box.center = Vector3.zero;
        box.size = SanitizeSize(localSize);

        // 规则代理盒只用于 Collider / 触发判定，不允许作为普通画面或 Mask 形状直接渲染。
        // 真正的遮挡 Mask 形状由 OutlineMaskProxyRoot 下的 __AutoOutlineMaskShapeClone 负责。
        Renderer[] renderers = tr.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = false;
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
        }
    }

    private BoxCollider EnsureBoxCollider(Transform tr, bool isTrigger)
    {
        BoxCollider collider = tr.GetComponent<BoxCollider>();
        if (collider == null)
            collider = tr.gameObject.AddComponent<BoxCollider>();
        collider.isTrigger = isTrigger;
        return collider;
    }

    private Vector3 GetRuleForward(TerrainDecorationDefinition definition)
    {
        // 这里返回的是“地形装饰物对象坐标 / RuleRoot 坐标”里的规则前方。
        // 不叠加 VisualRoot.localRotation；VisualRoot 只负责修正 Blender 模型显示，不应该污染规则盒。
        Vector3 forward = definition.ruleForwardLocal.sqrMagnitude < 0.0001f
            ? Vector3.forward
            : definition.ruleForwardLocal.normalized;

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return forward.normalized;
    }

    private float GetProjectedHalfExtentXZ(Vector3 size, Vector3 axis)
    {
        Vector3 n = axis;
        n.y = 0f;
        if (n.sqrMagnitude < 0.0001f)
            return Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) * 0.5f;

        n.Normalize();

        return
            Mathf.Abs(n.x) * Mathf.Abs(size.x) * 0.5f +
            Mathf.Abs(n.z) * Mathf.Abs(size.z) * 0.5f;
    }

    private float GetAxisExtent(Vector3 size, Vector3 forward)
    {
        Vector3 abs = new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
        if (abs.x >= abs.y && abs.x >= abs.z)
            return Mathf.Abs(size.x) * 0.5f;
        if (abs.y >= abs.x && abs.y >= abs.z)
            return Mathf.Abs(size.y) * 0.5f;
        return Mathf.Abs(size.z) * 0.5f;
    }

    private Vector3 GetRawCollisionSize(TerrainDecorationDefinition definition)
    {
        if (definition == null)
            return Vector3.one;

        return binder != null && binder.overrideCollision
            ? binder.collisionSizeOverride
            : definition.collisionSize;
    }

    private Vector3 GetEffectiveCollisionSize(TerrainDecorationDefinition definition)
    {
        Vector3 raw = SanitizeSize(GetRawCollisionSize(definition));
        float scale = Mathf.Max(1.00f, mainCollisionSafetyScale);

        // 第一层：稳定的基础安全外扩。
        // 从中心等比放大，不移动碰撞盒中心。
        Vector3 result = raw * scale;

        // 第二层：视角夹角安全外扩。
        // 当模型局部 X/Z 与 2.5D 镜头方向接近 45 度时，角色会更容易贴到可见斜角边缘。
        // 这时单纯 1.1 倍不够，需要按“镜头方向与模型足迹轴的夹角”额外放大 X/Z。
        // 只改规则碰撞尺寸，不读取 VisualRoot / Mesh / Blender 坐标。
        if (useAngleAwareCollisionSafety)
        {
            Vector3 localCameraForward = GetCollisionSafetyCameraForwardLocal();
            float diagonalFactor = Mathf.Clamp01(Mathf.Abs(localCameraForward.x * localCameraForward.z) * 2f);

            if (diagonalFactor > 0.0001f)
            {
                float minSide = Mathf.Max(0.05f, Mathf.Min(Mathf.Abs(result.x), Mathf.Abs(result.z)));
                float extra = Mathf.Max(0f, Mathf.Max(angledCollisionMinExtraXZ, minSide * angledCollisionExtraByMinSide)) * diagonalFactor;

                result.x += extra;
                result.z += extra;
            }
        }

        return SanitizeSize(result);
    }

    private Vector3 GetCollisionSafetyCameraForwardLocal()
    {
        // 这里使用固定的游戏镜头 yaw，而不是 SceneView 镜头。
        // SceneView 会随着编辑器视角变化，不能作为生成规则的输入。
        Vector3 worldForward = Quaternion.Euler(0f, collisionSafetyCameraYawDegrees, 0f) * Vector3.forward;
        worldForward.y = 0f;
        if (worldForward.sqrMagnitude < 0.0001f)
            worldForward = Vector3.forward;
        worldForward.Normalize();

        // 转到当前装饰物本地规则空间。这样同一个模型旋转到极端角度时，
        // diagonalFactor 会自动变大；正对镜头/平行镜头时则基本不额外膨胀。
        Vector3 localForward = transform.InverseTransformDirection(worldForward);
        localForward.y = 0f;
        if (localForward.sqrMagnitude < 0.0001f)
            localForward = Vector3.forward;
        localForward.Normalize();
        return localForward;
    }

    private Vector3 GetEffectiveCollisionOffset(TerrainDecorationDefinition definition)
    {
        if (definition == null)
            return Vector3.zero;

        return binder != null && binder.overrideCollision
            ? binder.collisionOffsetOverride
            : definition.collisionOffset;
    }

    private Vector3 SanitizeSize(Vector3 size)
    {
        return new Vector3(Mathf.Max(0.001f, Mathf.Abs(size.x)), Mathf.Max(0.001f, Mathf.Abs(size.y)), Mathf.Max(0.001f, Mathf.Abs(size.z)));
    }

    private void RebuildStencilShapeClone()
    {
        if (stencilWriterRoot == null || visualRoot == null)
            return;

        Transform old = stencilWriterRoot.Find(AutoStencilCloneName);
        if (old != null)
            SafeDestroyImmediate(old.gameObject);

        GameObject cloneRoot = new GameObject(AutoStencilCloneName);
        cloneRoot.transform.SetParent(stencilWriterRoot, false);
        cloneRoot.transform.localPosition = Vector3.zero;
        cloneRoot.transform.localRotation = Quaternion.identity;
        cloneRoot.transform.localScale = Vector3.one;
        cloneRoot.layer = GetLayer("OcclusionMask", GetLayer("Default", 0));

        Renderer[] renderers = CollectOcclusionSourceRenderers();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer srcRenderer = renderers[i];
            if (srcRenderer == null || IsProxyRenderer(srcRenderer))
                continue;

            GameObject clone = new GameObject(srcRenderer.name + "_Stencil");
            clone.transform.SetParent(cloneRoot.transform, false);
            CopyWorldTransformIntoParentSpace(srcRenderer.transform, clone.transform, cloneRoot.transform);
            clone.layer = cloneRoot.layer;

            MeshFilter srcFilter = srcRenderer.GetComponent<MeshFilter>();
            SkinnedMeshRenderer srcSkinned = srcRenderer as SkinnedMeshRenderer;

            if (srcFilter != null && srcFilter.sharedMesh != null)
            {
                MeshFilter dstFilter = clone.AddComponent<MeshFilter>();
                dstFilter.sharedMesh = srcFilter.sharedMesh;
                MeshRenderer dstRenderer = clone.AddComponent<MeshRenderer>();
                dstRenderer.sharedMaterials = srcRenderer.sharedMaterials;
                dstRenderer.enabled = false;
                dstRenderer.shadowCastingMode = ShadowCastingMode.Off;
                dstRenderer.receiveShadows = false;
            }
            else if (srcSkinned != null && srcSkinned.sharedMesh != null)
            {
                SkinnedMeshRenderer dst = clone.AddComponent<SkinnedMeshRenderer>();
                dst.sharedMesh = srcSkinned.sharedMesh;
                dst.sharedMaterials = srcSkinned.sharedMaterials;
                dst.enabled = false;
                dst.shadowCastingMode = ShadowCastingMode.Off;
                dst.receiveShadows = false;
            }
            else
            {
                SafeDestroyImmediate(clone);
            }
        }
    }


    private BoxCollider ResolveFrontOccluderVolumeCollider()
    {
        if (frontOccluderRoot == null)
            return null;

        Transform proxy = frontOccluderRoot.Find("FrontOccluderProxy_Box");
        if (proxy == null)
            proxy = EnsureChild(frontOccluderRoot, "FrontOccluderProxy_Box", null);

        BoxCollider box = EnsureBoxCollider(proxy, true);
        box.isTrigger = true;
        box.enabled = proxy.gameObject.activeInHierarchy || proxy.gameObject.activeSelf;
        return box;
    }

    private void RebuildFrontOccluderShapeClone()
    {
        if (frontOccluderRoot == null)
            return;

        Transform old = frontOccluderRoot.Find(AutoFrontOccluderCloneName);
        if (old != null)
            SafeDestroyImmediate(old.gameObject);

        Material maskMaterial = ResolveOutlineMaskMaterial();
        if (maskMaterial == null)
            return;

        GameObject cloneRoot = new GameObject(AutoFrontOccluderCloneName);
        cloneRoot.transform.SetParent(frontOccluderRoot, false);
        cloneRoot.transform.localPosition = Vector3.zero;
        cloneRoot.transform.localRotation = Quaternion.identity;
        cloneRoot.transform.localScale = Vector3.one;
        cloneRoot.layer = GetLayer("OcclusionMask", GetLayer("Default", 0));
        cloneRoot.SetActive(false);

        Renderer[] renderers = CollectOcclusionSourceRenderers();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer srcRenderer = renderers[i];
            if (srcRenderer == null || IsProxyRenderer(srcRenderer))
                continue;

            // FrontOccluder 需要像原型盒子一样参与遮挡绘制，所以 rendererEnabled=true。
            // 注意：Layer 使用 OcclusionMask，材质仍使用 M_OcclusionMask_ForegroundOccluder / Custom/FrontOccluder。
            CreateMaskCloneRenderer(cloneRoot.transform, srcRenderer, maskMaterial, true);
        }
    }

    private Renderer[] CollectOcclusionSourceRenderers()
    {
        List<Renderer> result = new List<Renderer>();

        Renderer rootRenderer = GetComponent<Renderer>();
        if (rootRenderer != null && rootRenderer.enabled && !IsProxyRenderer(rootRenderer) && !ShouldIgnoreOcclusionSourceRenderer(rootRenderer))
            result.Add(rootRenderer);

        if (visualRoot != null)
        {
            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || IsProxyRenderer(r) || ShouldIgnoreOcclusionSourceRenderer(r) || result.Contains(r))
                    continue;

                result.Add(r);
            }
        }

        return result.ToArray();
    }

    private bool ShouldIgnoreOcclusionSourceRenderer(Renderer renderer)
    {
        if (renderer == null)
            return true;

        // 自动遮挡形状只应该来自“硬结构主模型”。
        // 苔藓、藤蔓、叶片、FX、透明贴片等视觉附着物即使大部分透明，Mesh 仍是整张 Quad，
        // 如果参与扫描，会把透明区域也变成遮挡范围。
        if (renderer is SpriteRenderer || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            return true;

        Transform tr = renderer.transform;
        if (IsChildOf(tr, mossRoot) || IsChildOf(tr, fxRoot))
            return true;

        string path = GetTransformPathLower(tr);
        if (ContainsDecorativeRendererKeyword(path))
            return true;

        Material[] mats = renderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return false;

        bool anyDecorativeMaterialName = false;
        bool anyTransparentMaterial = false;

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null)
                continue;

            string matName = mat.name != null ? mat.name.ToLowerInvariant() : string.Empty;
            string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;

            if (ContainsDecorativeRendererKeyword(matName) || ContainsDecorativeRendererKeyword(shaderName))
                anyDecorativeMaterialName = true;

            if (IsTransparentLikeMaterial(mat, matName, shaderName))
                anyTransparentMaterial = true;
        }

        // 透明材质 + 装饰性命名时，必定不参与遮挡代理体生成。
        // 这样不会误伤“主模型整体材质名里带 moss 但仍是实体水泥块”的情况。
        return anyTransparentMaterial && anyDecorativeMaterialName;
    }

    private bool IsChildOf(Transform child, Transform potentialParent)
    {
        if (child == null || potentialParent == null)
            return false;

        Transform current = child;
        while (current != null)
        {
            if (current == potentialParent)
                return true;
            current = current.parent;
        }

        return false;
    }

    private string GetTransformPathLower(Transform tr)
    {
        if (tr == null)
            return string.Empty;

        List<string> names = new List<string>();
        Transform current = tr;
        while (current != null && current != transform.parent)
        {
            names.Add(current.name != null ? current.name : string.Empty);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names).ToLowerInvariant();
    }

    private bool ContainsDecorativeRendererKeyword(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return
            value.Contains("moss") ||
            value.Contains("foliage") ||
            value.Contains("leaf") ||
            value.Contains("leaves") ||
            value.Contains("vine") ||
            value.Contains("grass") ||
            value.Contains("decal") ||
            value.Contains("card") ||
            value.Contains("billboard") ||
            value.Contains("sprite") ||
            value.Contains("fx") ||
            value.Contains("vfx") ||
            value.Contains("alpha") ||
            value.Contains("transparent") ||
            value.Contains("cutout") ||
            value.Contains("贴片") ||
            value.Contains("苔藓") ||
            value.Contains("藤") ||
            value.Contains("叶");
    }

    private bool IsTransparentLikeMaterial(Material mat, string matName, string shaderName)
    {
        if (mat == null)
            return false;

        // URP Lit: _Surface = 1 means Transparent.
        if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f)
            return true;

        if (mat.HasProperty("_Mode") && mat.GetFloat("_Mode") >= 2f)
            return true;

        if (mat.renderQueue >= 2501)
            return true;

        if (mat.HasProperty("_BaseColor") && mat.GetColor("_BaseColor").a < 0.995f)
            return true;

        if (mat.HasProperty("_Color") && mat.GetColor("_Color").a < 0.995f)
            return true;

        return
            matName.Contains("transparent") ||
            matName.Contains("alpha") ||
            matName.Contains("cutout") ||
            shaderName.Contains("transparent") ||
            shaderName.Contains("alpha") ||
            shaderName.Contains("cutout");
    }

    private void RebuildOutlineMaskShapeClone()
    {
        if (outlineMaskProxyRoot == null || visualRoot == null)
            return;

        Transform old = outlineMaskProxyRoot.Find(AutoOutlineMaskCloneName);
        if (old != null)
            SafeDestroyImmediate(old.gameObject);

        Material maskMaterial = ResolveOutlineMaskMaterial();
        if (maskMaterial == null)
        {
            // 没有专用 Mask 材质时不生成可渲染克隆，避免再次出现紫色块或普通材质被主画面看见。
            return;
        }

        GameObject cloneRoot = new GameObject(AutoOutlineMaskCloneName);
        cloneRoot.transform.SetParent(outlineMaskProxyRoot, false);
        cloneRoot.transform.localPosition = Vector3.zero;
        cloneRoot.transform.localRotation = Quaternion.identity;
        cloneRoot.transform.localScale = Vector3.one;
        cloneRoot.layer = GetLayer("OcclusionMask", GetLayer("Default", 0));

        Renderer[] renderers = CollectOcclusionSourceRenderers();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer srcRenderer = renderers[i];
            if (srcRenderer == null || IsProxyRenderer(srcRenderer))
                continue;

            CreateMaskCloneRenderer(cloneRoot.transform, srcRenderer, maskMaterial, renderOutlineMaskShapeClone);
        }
    }

    private Material ResolveOutlineMaskMaterial()
    {
        if (IsValidMaskMaterial(outlineMaskMaterial))
            return outlineMaskMaterial;

#if UNITY_EDITOR
        // 严格只从专用遮挡材质目录或材质名中寻找 Mask 材质。
        // 绝对不能 fallback 到普通模型材质，否则 Scene 里会出现一份贴图错位的可见复制体。
        string[] guids = AssetDatabase.FindAssets("t:Material", new[]
        {
            "Assets/_Project/Art/Materials/FrontOccluder",
            "Assets/_Project/Art/Materials/OcclusionMask",
            "Assets/_Project/Art/Materials/Stencil",
            "Assets/_Project/Art/Materials/Outline"
        });

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (IsValidMaskMaterial(mat))
                return mat;
        }
#endif

        Material found = FindMaskMaterialIn(outlineMaskProxyRoot);
        if (found != null)
            return found;

        found = FindMaskMaterialIn(frontOccluderRoot);
        if (found != null)
            return found;

        found = FindMaskMaterialIn(stencilWriterRoot);
        if (found != null)
            return found;

        return null;
    }

    private bool IsValidMaskMaterial(Material mat)
    {
        if (mat == null)
            return false;

        string n = mat.name != null ? mat.name.ToLowerInvariant() : string.Empty;
        string shaderName = mat.shader != null ? mat.shader.name.ToLowerInvariant() : string.Empty;

        return
            n.Contains("occlusion") ||
            n.Contains("mask") ||
            n.Contains("stencil") ||
            shaderName.Contains("occlusion") ||
            shaderName.Contains("mask") ||
            shaderName.Contains("stencil");
    }

    private Material FindMaskMaterialIn(Transform root)
    {
        if (root == null)
            return null;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || r.sharedMaterials == null)
                continue;

            Material[] mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (IsValidMaskMaterial(mat))
                    return mat;
            }
        }

        return null;
    }

    private void CreateMaskCloneRenderer(Transform cloneRoot, Renderer srcRenderer, Material maskMaterial, bool rendererEnabled)
    {
        if (cloneRoot == null || srcRenderer == null || !IsValidMaskMaterial(maskMaterial))
            return;

        MeshFilter srcFilter = srcRenderer.GetComponent<MeshFilter>();
        SkinnedMeshRenderer srcSkinned = srcRenderer as SkinnedMeshRenderer;

        bool hasMeshFilter = srcFilter != null && srcFilter.sharedMesh != null;
        bool hasSkinned = srcSkinned != null && srcSkinned.sharedMesh != null;
        if (!hasMeshFilter && !hasSkinned)
            return;

        GameObject clone = new GameObject(srcRenderer.name + "_OutlineMask");
        clone.transform.SetParent(cloneRoot, false);
        CopyWorldTransformIntoParentSpace(srcRenderer.transform, clone.transform, cloneRoot);
        clone.layer = cloneRoot.gameObject.layer;

        int materialCount = Mathf.Max(1, srcRenderer.sharedMaterials != null ? srcRenderer.sharedMaterials.Length : 1);
        Material[] mats = new Material[materialCount];
        for (int i = 0; i < mats.Length; i++)
            mats[i] = maskMaterial;

        if (hasMeshFilter)
        {
            MeshFilter dstFilter = clone.AddComponent<MeshFilter>();
            dstFilter.sharedMesh = srcFilter.sharedMesh;

            MeshRenderer dstRenderer = clone.AddComponent<MeshRenderer>();
            dstRenderer.sharedMaterials = mats;
            dstRenderer.enabled = rendererEnabled;
            dstRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dstRenderer.receiveShadows = false;
        }
        else if (hasSkinned)
        {
            SkinnedMeshRenderer dst = clone.AddComponent<SkinnedMeshRenderer>();
            dst.sharedMesh = srcSkinned.sharedMesh;
            dst.sharedMaterials = mats;
            dst.enabled = rendererEnabled;
            dst.shadowCastingMode = ShadowCastingMode.Off;
            dst.receiveShadows = false;
            dst.updateWhenOffscreen = true;
        }
    }

    private void CopyWorldTransformIntoParentSpace(Transform source, Transform target, Transform parent)
    {
        if (source == null || target == null)
            return;

        if (parent == null)
        {
            target.position = source.position;
            target.rotation = source.rotation;
            target.localScale = source.lossyScale;
            return;
        }

        target.localPosition = parent.InverseTransformPoint(source.position);
        target.localRotation = Quaternion.Inverse(parent.rotation) * source.rotation;

        Vector3 parentScale = parent.lossyScale;
        Vector3 sourceScale = source.lossyScale;
        target.localScale = new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? sourceScale.x / parentScale.x : sourceScale.x,
            Mathf.Abs(parentScale.y) > 0.0001f ? sourceScale.y / parentScale.y : sourceScale.y,
            Mathf.Abs(parentScale.z) > 0.0001f ? sourceScale.z / parentScale.z : sourceScale.z);
    }

    private void SetWorldScale(Transform tr, Vector3 worldScale)
    {
        if (tr == null)
            return;

        Vector3 parentScale = tr.parent != null ? tr.parent.lossyScale : Vector3.one;
        tr.localScale = new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? worldScale.x / parentScale.x : worldScale.x,
            Mathf.Abs(parentScale.y) > 0.0001f ? worldScale.y / parentScale.y : worldScale.y,
            Mathf.Abs(parentScale.z) > 0.0001f ? worldScale.z / parentScale.z : worldScale.z);
    }

    private void SafeDestroyImmediate(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.DestroyObjectImmediate(obj);
        else
            Destroy(obj);
#else
        Destroy(obj);
#endif
    }

    public bool IsTargetInFront(Vector3 worldPosition)
    {
        if (planeOrigin == null || planeForwardReference == null)
            return false;

        Vector3 origin = planeOrigin.position;
        Vector3 forward = (planeForwardReference.position - planeOrigin.position).normalized;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;

        return Vector3.Dot(worldPosition - origin, forward) >= 0f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        TerrainDecorationRuntimeBinder b = GetComponent<TerrainDecorationRuntimeBinder>();
        TerrainDecorationDefinition d = b != null ? b.definition : null;
        if (d == null)
            return;

        Gizmos.color = d.gizmoColor;
        Transform gizmoRoot = ruleRoot != null ? ruleRoot : transform;

        if (d.showCollisionGizmo)
        {
            Vector3 size = b.overrideCollision ? b.collisionSizeOverride : d.collisionSize;
            Vector3 offset = b.overrideCollision ? b.collisionOffsetOverride : d.collisionOffset;
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(gizmoRoot.position, gizmoRoot.rotation, Vector3.one);
            Gizmos.DrawWireCube(offset, size);
            Gizmos.matrix = old;
        }

        if (d.showFrontBackPlaneGizmo && planeOrigin != null && planeForwardReference != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(planeOrigin.position, 0.06f);
            Gizmos.DrawLine(planeOrigin.position, planeForwardReference.position);
        }
    }
#endif

    private struct VisualBoundsResult
    {
        public bool hasBounds;
        public Bounds bounds;
    }
}
