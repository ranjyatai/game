using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 正式地形装饰物实例结构生成器。
///
/// 边界：
/// - 只在明确入口调用：新放置、重新应用当前选中实例、旧数据迁移。
/// - 只读取 TerrainDecorationDefinition。
/// - 只修改传入的当前实例 root。
/// - 不注册 InitializeOnLoad / hierarchyChanged / delayCall。
/// - 不扫描全场景，不修 Prefab，不生成 RuntimeTemplate。
/// </summary>
public static class SkyPrisonTerrainDecorationInstanceBuilder
{
    private const string LogPrefix = "[TD_INSTANCE_BUILDER]";

    private const string VisualRootName = "VisualRoot";
    private const string RuleRootName = "RuleRoot";
    private const string CollisionRootName = "CollisionRoot";
    private const string MainCollisionBoxName = "Main_Collision_Box";
    private const string MainCollisionMeshRootName = "Main_Collision_MeshRoot";
    private const string BackTriggerName = "BackTrigger";
    private const string FrontTriggerName = "FrontTrigger";
    private const string FrontOccluderRootName = "FrontOccluderRoot";
    private const string FrontOccluderProxyBoxName = "FrontOccluderProxy_Box";
    private const string FrontOccluderProxyModelRootName = "FrontOccluderProxy_Model";
    private const string FrontOccluderProxyManualRootName = "FrontOccluderProxy_Manual";
    private const string PushableColliderRootName = "PushableColliderRoot";

    // BackTrigger 现在只是粗唤醒范围。为支持叠层 / 高架遮挡，唤醒盒需要向下覆盖足够高度，
    // 真正是否遮挡由 SkyPrisonTerrainDecorationFrontOccluderTrigger 的 footprint + 高度规则判断。
    private const float OcclusionWakeTriggerDownwardPadding = 20f;
    private const float OcclusionWakeTriggerUpwardPadding = 1f;
    private const string PhysicsSettingsFolder = "Assets/_Project/Data/Definitions/Physics/TerrainDecorations";

    // TerrainDecorationCollisionMode 当前定义页顺序：无 / 盒体碰撞 / 网格碰撞 / 自定义碰撞根节点。
    private const int CollisionNone = 0;
    private const int CollisionBox = 1;
    private const int CollisionMesh = 2;
    private const int CollisionCustomRoot = 3;

    // TerrainDecorationOcclusionMode 当前定义页顺序：无 / 前后遮挡 / 挡住玩家时半透明 / 前后遮挡 + 半透明。
    private const int OcclusionNone = 0;
    private const int OcclusionFrontBack = 1;
    private const int OcclusionFadeOnly = 2;
    private const int OcclusionFrontBackAndFade = 3;

    // TerrainDecorationFrontOccluderProxyMode：无 / 盒体代理 / 参考模型代理 / 手动代理 Prefab。
    private const int ProxyNone = 0;
    private const int ProxyBox = 1;
    private const int ProxyModel = 2;
    private const int ProxyManualPrefab = 3;

    public static void BuildStructureFromDefinition(GameObject root, TerrainDecorationDefinition definition, bool logResult)
    {
        if (root == null || definition == null)
            return;

        SerializedObject definitionSO = new SerializedObject(definition);

        Transform visualRoot = EnsureChild(root.transform, VisualRootName);
        Transform collisionRoot = EnsureChild(root.transform, CollisionRootName);
        Transform ruleRoot = EnsureChild(root.transform, RuleRootName);

        // 旧版本曾把 CollisionRoot 生在 RuleRoot 下；新结构要求物理碰撞与前后遮挡分离。
        RemoveLegacyCollisionRootUnderRuleRoot(ruleRoot);

        BuildCollisionFromDefinition(root, collisionRoot, visualRoot, definitionSO);
        BuildPhysicsStructureFromDefinition(root, visualRoot, collisionRoot, definition);
        BuildOcclusionFromDefinition(root, ruleRoot, visualRoot, definitionSO);
        ApplyStandardLayers(root, definitionSO);

        EditorUtility.SetDirty(root);
        if (logResult)
            Debug.Log(BuildResultLog(root, definition, definitionSO), root);
    }

    private static void BuildCollisionFromDefinition(GameObject root, Transform collisionRoot, Transform visualRoot, SerializedObject definitionSO)
    {
        int collisionMode = GetEnumIndex(definitionSO, "collisionMode", CollisionNone);
        bool blockPlayer = GetBool(definitionSO, "blockPlayer", true);
        bool blockEnemy = GetBool(definitionSO, "blockEnemy", true);
        bool blockProjectile = GetBool(definitionSO, "blockProjectile", true);

        ClearGeneratedCollisionChildren(collisionRoot);

        if (collisionMode == CollisionNone)
        {
            collisionRoot.gameObject.SetActive(false);
            return;
        }

        collisionRoot.gameObject.SetActive(true);

        if (collisionMode == CollisionBox)
        {
            Vector3 size = GetVector3(definitionSO, "collisionSize", Vector3.one);
            Vector3 offset = GetVector3(definitionSO, "collisionOffset", Vector3.zero);
            if (size.sqrMagnitude <= 0.0001f)
                size = Vector3.one;

            Transform boxNode = EnsureChild(collisionRoot, MainCollisionBoxName);
            ResetLocalTransform(boxNode);
            BoxCollider box = EnsureComponent<BoxCollider>(boxNode.gameObject);
            box.isTrigger = false;
            box.size = AbsSize(size);
            box.center = offset;
            SetLayerIfExists(boxNode.gameObject, ResolveBlockingLayer(blockPlayer, blockEnemy, blockProjectile));
            return;
        }

        if (collisionMode == CollisionMesh)
        {
            Transform meshRoot = EnsureChild(collisionRoot, MainCollisionMeshRootName);
            ResetLocalTransform(meshRoot);
            BuildMeshCollisionProxies(meshRoot, visualRoot, ResolveBlockingLayer(blockPlayer, blockEnemy, blockProjectile));
            return;
        }

        // CustomRoot：Builder 不擅自生成，避免覆盖用户手工结构。
        // 只保留 CollisionRoot，并让验证器以后报告是否缺少自定义结构。
    }



    private static void BuildPhysicsStructureFromDefinition(GameObject root, Transform visualRoot, Transform collisionRoot, TerrainDecorationDefinition definition)
    {
        SkyPrisonTerrainDecorationPhysicsSettings settings = FindPhysicsSettingsForDefinition(definition);
        if (settings == null)
        {
            ClearPushablePhysicsStructure(root, collisionRoot);
            return;
        }

        SerializedObject settingsSO = new SerializedObject(settings);
        bool enabled = GetBool(settingsSO, "enablePhysicsStructure", false);
        if (!enabled)
        {
            ClearPushablePhysicsStructure(root, collisionRoot);
            return;
        }

        string pushableLayerName = GetString(settingsSO, "pushableLayerName", "PushableProp");
        if (string.IsNullOrWhiteSpace(pushableLayerName))
            pushableLayerName = "PushableProp";

        // Pushable 结构开启后，静态 CollisionRoot 保留为定义结构记录，但不参与物理，避免和 PushableColliderRoot 双重碰撞。
        if (collisionRoot != null)
            collisionRoot.gameObject.SetActive(false);

        SetLayerIfExists(root, pushableLayerName);

        Rigidbody rb = EnsureComponent<Rigidbody>(root);
        rb.mass = Mathf.Max(0.01f, GetFloat(settingsSO, "mass", 0.35f));
        rb.linearDamping = Mathf.Max(0f, GetFloat(settingsSO, "linearDamping", 6f));
        rb.angularDamping = Mathf.Max(0f, GetFloat(settingsSO, "angularDamping", 9f));
        rb.useGravity = false;
        rb.isKinematic = true;

        SkyPrisonPushablePropRuntime runtime = EnsureComponent<SkyPrisonPushablePropRuntime>(root);
        ApplyPushableRuntimeSettings(runtime, settingsSO, pushableLayerName);

        Transform pushableColliderRoot = EnsureChild(root.transform, PushableColliderRootName);
        ResetLocalTransform(pushableColliderRoot);
        pushableColliderRoot.gameObject.SetActive(true);
        SetLayerRecursivelyIfExists(pushableColliderRoot.gameObject, pushableLayerName);

        BuildPushableColliderProxies(pushableColliderRoot, visualRoot, settingsSO, pushableLayerName);

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(runtime);
        EditorUtility.SetDirty(rb);
    }

    private static void ClearPushablePhysicsStructure(GameObject root, Transform collisionRoot)
    {
        if (root == null)
            return;

        DestroyChildIfExists(root.transform, PushableColliderRootName);

        SkyPrisonPushablePropRuntime runtime = root.GetComponent<SkyPrisonPushablePropRuntime>();
        if (runtime != null)
            Object.DestroyImmediate(runtime);

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb != null)
            Object.DestroyImmediate(rb);

        if (collisionRoot != null)
            collisionRoot.gameObject.SetActive(true);
    }

    private static SkyPrisonTerrainDecorationPhysicsSettings FindPhysicsSettingsForDefinition(TerrainDecorationDefinition definition)
    {
        if (definition == null)
            return null;

        string id = !string.IsNullOrWhiteSpace(definition.decorationId)
            ? definition.decorationId
            : definition.name;

        if (string.IsNullOrWhiteSpace(id))
            return null;

        string[] folders = System.IO.Directory.Exists(PhysicsSettingsFolder)
            ? new[] { PhysicsSettingsFolder }
            : null;

        string[] guids = folders != null
            ? AssetDatabase.FindAssets($"t:SkyPrisonTerrainDecorationPhysicsSettings {id}", folders)
            : AssetDatabase.FindAssets($"t:SkyPrisonTerrainDecorationPhysicsSettings {id}");

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            SkyPrisonTerrainDecorationPhysicsSettings found = AssetDatabase.LoadAssetAtPath<SkyPrisonTerrainDecorationPhysicsSettings>(path);
            if (found == null)
                continue;

            SerializedObject so = new SerializedObject(found);
            SerializedProperty idProp = so.FindProperty("decorationId");
            if (idProp == null || idProp.stringValue == id)
                return found;
        }

        return null;
    }

    private static void ApplyPushableRuntimeSettings(SkyPrisonPushablePropRuntime runtime, SerializedObject settingsSO, string pushableLayerName)
    {
        if (runtime == null || settingsSO == null)
            return;

        SerializedObject runtimeSO = new SerializedObject(runtime);
        runtimeSO.Update();

        CopyBool(settingsSO, runtimeSO, "receiveVolumeCollision", true);
        CopyBool(settingsSO, runtimeSO, "receiveAttackImpulse", true);
        CopyBool(settingsSO, runtimeSO, "receiveExplosionImpulse", true);
        CopyBool(settingsSO, runtimeSO, "receiveScriptedImpulse", true);

        CopyFloat(settingsSO, runtimeSO, "mass", 0.35f);
        CopyFloat(settingsSO, runtimeSO, "linearDamping", 6f);
        CopyFloat(settingsSO, runtimeSO, "angularDamping", 9f);
        CopyFloat(settingsSO, runtimeSO, "maxPlanarSpeed", 3.5f);

        CopyFloat(settingsSO, runtimeSO, "externalPushMultiplier", 2.2f);
        CopyBool(settingsSO, runtimeSO, "applyForceAtTop", true);
        CopyFloat(settingsSO, runtimeSO, "topForceHeight", 0.9f);
        CopyFloat(settingsSO, runtimeSO, "topForceMultiplier", 2.0f);

        CopyBool(settingsSO, runtimeSO, "enableKnockdown", true);
        CopyBool(settingsSO, runtimeSO, "protectAfterPivotRelease", false);
        CopyBool(settingsSO, runtimeSO, "useLastKnownGroundWhenRayMisses", false);
        CopyBool(settingsSO, runtimeSO, "useFallbackGroundPlaneWhenRayMisses", false);
        CopyFloat(settingsSO, runtimeSO, "fallbackGroundY", 0f);

        SetSerializedString(runtimeSO, "pushableLayerName", pushableLayerName);

        // 当前地形装饰物 Pushable 默认按 XZ 地面平面运动，和现有 2.5D 场景一致。
        SetSerializedBool(runtimeSO, "useHorizontalGroundPlane", true);

        runtimeSO.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildPushableColliderProxies(Transform pushableColliderRoot, Transform visualRoot, SerializedObject settingsSO, string layerName)
    {
        if (pushableColliderRoot == null)
            return;

        ClearChildren(pushableColliderRoot);

        Mesh customMesh = GetObject<Mesh>(settingsSO, "customPhysicsMesh", null);
        bool autoPickLargest = GetBool(settingsSO, "autoPickLargestVisibleMesh", true);
        bool convex = GetBool(settingsSO, "forceConvexMeshCollider", true);

        // 动态 Rigidbody 下的 MeshCollider 必须 Convex；这里强制兜底，避免运行时报错或物理无效。
        convex = true;

        if (customMesh != null)
        {
            GameObject proxy = new GameObject("__PushableMeshCollider_Custom");
            proxy.transform.SetParent(pushableColliderRoot, false);
            ResetLocalTransform(proxy.transform);
            SetLayerIfExists(proxy, layerName);

            MeshCollider mc = proxy.AddComponent<MeshCollider>();
            mc.sharedMesh = customMesh;
            mc.convex = convex;
            return;
        }

        List<MeshFilter> candidates = CollectVisibleMeshFilters(visualRoot);
        if (candidates.Count > 0)
        {
            if (autoPickLargest)
            {
                MeshFilter largest = PickLargestVisibleMeshFilter(candidates);
                if (largest != null)
                    CreatePushableMeshColliderProxy(pushableColliderRoot, largest, layerName, convex, 0);
            }
            else
            {
                int created = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (CreatePushableMeshColliderProxy(pushableColliderRoot, candidates[i], layerName, convex, created))
                        created++;
                }
            }

            if (pushableColliderRoot.childCount > 0)
                return;
        }

        // 兜底：没有可用 Mesh 时，用视觉 Bounds 生成一个 BoxCollider，避免启用物理结构后完全没有实体碰撞。
        Bounds localBounds;
        if (!TryCalculateVisualBoundsInRootLocalSpace(pushableColliderRoot.root, visualRoot, out localBounds))
            localBounds = new Bounds(Vector3.zero, Vector3.one);

        GameObject boxProxy = new GameObject("__PushableBoxCollider_Fallback");
        boxProxy.transform.SetParent(pushableColliderRoot, false);
        ResetLocalTransform(boxProxy.transform);
        SetLayerIfExists(boxProxy, layerName);

        BoxCollider box = boxProxy.AddComponent<BoxCollider>();
        box.center = localBounds.center;
        box.size = AbsSize(localBounds.size);
    }

    private static List<MeshFilter> CollectVisibleMeshFilters(Transform visualRoot)
    {
        List<MeshFilter> result = new List<MeshFilter>();
        if (visualRoot == null)
            return result;

        MeshFilter[] filters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null || mf.sharedMesh == null)
                continue;

            MeshRenderer renderer = mf.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                continue;

            result.Add(mf);
        }

        return result;
    }

    private static MeshFilter PickLargestVisibleMeshFilter(List<MeshFilter> filters)
    {
        MeshFilter best = null;
        float bestVolume = -1f;

        for (int i = 0; i < filters.Count; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null)
                continue;

            Renderer r = mf.GetComponent<Renderer>();
            if (r == null)
                continue;

            Vector3 s = r.bounds.size;
            float volume = Mathf.Abs(s.x * s.y * s.z);
            if (volume > bestVolume)
            {
                bestVolume = volume;
                best = mf;
            }
        }

        return best;
    }

    private static bool CreatePushableMeshColliderProxy(Transform pushableColliderRoot, MeshFilter source, string layerName, bool convex, int index)
    {
        if (pushableColliderRoot == null || source == null || source.sharedMesh == null)
            return false;

        GameObject proxy = new GameObject("__PushableMeshCollider_" + index.ToString("00") + "_" + MakeSafeName(source.gameObject.name));
        proxy.transform.SetParent(pushableColliderRoot, false);
        CopyWorldTransform(source.transform, proxy.transform);
        SetLayerIfExists(proxy, layerName);

        MeshCollider meshCollider = proxy.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = source.sharedMesh;
        meshCollider.convex = convex;
        return true;
    }


    private static Vector3[] GetWorldBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z),
        };
    }

    private static bool TryCalculateVisualBoundsInRootLocalSpace(Transform root, Transform visualRoot, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        if (root == null || visualRoot == null)
            return false;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            Bounds wb = r.bounds;
            Vector3[] corners = GetWorldBoundsCorners(wb);
            for (int c = 0; c < corners.Length; c++)
            {
                Vector3 local = root.InverseTransformPoint(corners[c]);
                if (!hasAny)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return hasAny;
    }

    private static void CopyBool(SerializedObject source, SerializedObject target, string propertyName, bool fallback)
    {
        SetSerializedBool(target, propertyName, GetBool(source, propertyName, fallback));
    }

    private static void CopyFloat(SerializedObject source, SerializedObject target, string propertyName, float fallback)
    {
        SetSerializedFloat(target, propertyName, GetFloat(source, propertyName, fallback));
    }

    private static void SetSerializedBool(SerializedObject target, string propertyName, bool value)
    {
        SerializedProperty prop = target.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.Boolean)
            prop.boolValue = value;
    }

    private static void SetSerializedFloat(SerializedObject target, string propertyName, float value)
    {
        SerializedProperty prop = target.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.Float)
            prop.floatValue = value;
    }

    private static void SetSerializedString(SerializedObject target, string propertyName, string value)
    {
        SerializedProperty prop = target.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.String)
            prop.stringValue = value;
    }


    private static void BuildOcclusionFromDefinition(GameObject root, Transform ruleRoot, Transform visualRoot, SerializedObject definitionSO)
    {
        int occlusionMode = GetEnumIndex(definitionSO, "occlusionMode", OcclusionNone);
        bool useFrontBack = occlusionMode == OcclusionFrontBack || occlusionMode == OcclusionFrontBackAndFade;
        bool blockVision = GetBool(definitionSO, "blockVision", false);

        if (!useFrontBack)
            ClearFrontBackOcclusionNodes(ruleRoot);

        if (!useFrontBack && !blockVision)
            return;

        Bounds projectionBounds;
        bool hasBounds = TryCalculateVisualBoundsInCameraFacingSpace(root.transform, visualRoot, out projectionBounds);
        if (!hasBounds)
            projectionBounds = new Bounds(Vector3.zero, Vector3.one);

        if (useFrontBack)
        {
            OcclusionProjectionSettings settings = ReadOcclusionProjectionSettings(definitionSO);
            ProjectedOcclusionBoxes boxes = CalculateProjectedOcclusionBoxes(projectionBounds, settings);

            Transform backTrigger = EnsureChild(ruleRoot, BackTriggerName);
            Transform frontTrigger = EnsureChild(ruleRoot, FrontTriggerName);
            Transform frontOccluderRoot = EnsureChild(ruleRoot, FrontOccluderRootName);

            Vector3 wakeBackCenter = boxes.backCenter;
            Vector3 wakeBackSize = boxes.backSize;
            ExpandWakeTriggerVerticalRange(root.transform, visualRoot, ref wakeBackCenter, ref wakeBackSize);

            ApplyCameraFacingBoxCollider(root.transform, backTrigger, wakeBackCenter, wakeBackSize, isTrigger: true, "DecorationTrigger");
            ApplyCameraFacingBoxCollider(root.transform, frontTrigger, boxes.frontCenter, boxes.frontSize, isTrigger: true, "DecorationTrigger");
            BuildFrontOccluderProxy(root.transform, frontOccluderRoot, visualRoot, definitionSO, boxes.proxyCenter, boxes.proxySize);
            ConfigureBackTriggerOcclusionController(backTrigger, frontOccluderRoot, root.transform, visualRoot);

            backTrigger.gameObject.SetActive(true);
            frontTrigger.gameObject.SetActive(true);

            // 代理体子节点保持可渲染状态；真正的显隐由 BackTrigger 上的运行时控制脚本控制 FrontOccluderRoot。
            frontOccluderRoot.gameObject.SetActive(false);
        }
    }

    private struct OcclusionProjectionSettings
    {
        public float widthMultiplier;
        public float heightMultiplier;
        public float depthMultiplier;
        public float frontRatio;
        public float backRatio;
        public float centerOffset;
        public Vector3 totalOffset;
        public Vector3 proxyMultiplier;
        public Vector3 proxyOffset;
    }

    private struct ProjectedOcclusionBoxes
    {
        public Vector3 backCenter;
        public Vector3 backSize;
        public Vector3 frontCenter;
        public Vector3 frontSize;
        public Vector3 proxyCenter;
        public Vector3 proxySize;
    }

    private static OcclusionProjectionSettings ReadOcclusionProjectionSettings(SerializedObject definitionSO)
    {
        OcclusionProjectionSettings s = new OcclusionProjectionSettings
        {
            widthMultiplier = Mathf.Max(0.01f, GetFloat(definitionSO, "frontBackOcclusionWidthMultiplier", 1f)),
            heightMultiplier = Mathf.Max(0.01f, GetFloat(definitionSO, "frontBackOcclusionHeightMultiplier", 1f)),
            depthMultiplier = Mathf.Max(0.01f, GetFloat(definitionSO, "frontBackOcclusionDepthMultiplier", 1f)),
            frontRatio = Mathf.Max(0f, GetFloat(definitionSO, "frontOcclusionDepthRatio", 0.18f)),
            backRatio = Mathf.Max(0f, GetFloat(definitionSO, "backOcclusionDepthRatio", 0.82f)),
            centerOffset = GetFloat(definitionSO, "frontBackOcclusionCenterOffset", 0f),
            totalOffset = new Vector3(
                GetFloat(definitionSO, "frontBackOcclusionHorizontalOffset", 0f),
                GetFloat(definitionSO, "frontBackOcclusionHeightOffset", 0f),
                GetFloat(definitionSO, "frontBackOcclusionDepthOffset", 0f)),
            proxyMultiplier = new Vector3(
                Mathf.Max(0.01f, GetFloat(definitionSO, "frontOccluderProxyWidthMultiplier", 1f)),
                Mathf.Max(0.01f, GetFloat(definitionSO, "frontOccluderProxyHeightMultiplier", 1f)),
                Mathf.Max(0.01f, GetFloat(definitionSO, "frontOccluderProxyDepthMultiplier", 1f))),
            proxyOffset = GetVector3(definitionSO, "frontOccluderProxyOffset", Vector3.zero)
        };

        float sum = s.frontRatio + s.backRatio;
        if (sum <= 0.0001f)
        {
            s.frontRatio = 0.18f;
            s.backRatio = 0.82f;
        }

        return s;
    }

    private static ProjectedOcclusionBoxes CalculateProjectedOcclusionBoxes(Bounds bounds, OcclusionProjectionSettings settings)
    {
        Vector3 rawSize = AbsSize(bounds.size);

        // 这里的 bounds 已经不是普通世界 AABB，而是把最终摆放后的 VisualRoot 所有 Renderer 角点
        // 投影到 45° 镜头判定坐标系后的范围：
        // X = 画面横向 / 宽度轴，Y = 世界高度，Z = 镜头前后 / 深度轴。
        // 因此这里不再使用 (x + z) * 0.707 进行二次估算；rawSize 本身就是 1.0 基准。
        float projectedWidth = Mathf.Max(0.05f, rawSize.x);
        float projectedHeight = Mathf.Max(0.05f, rawSize.y);
        float projectedDepth = Mathf.Max(0.05f, rawSize.z);

        float width = projectedWidth * settings.widthMultiplier;
        float height = projectedHeight * settings.heightMultiplier;
        float depth = projectedDepth * settings.depthMultiplier;

        float ratioSum = Mathf.Max(0.0001f, settings.frontRatio + settings.backRatio);
        float frontDepth = Mathf.Max(0.05f, depth * (settings.frontRatio / ratioSum));
        float backDepth = Mathf.Max(0.05f, depth * (settings.backRatio / ratioSum));

        Vector3 center = bounds.center + settings.totalOffset;
        center.z += settings.centerOffset;

        ProjectedOcclusionBoxes boxes = new ProjectedOcclusionBoxes
        {
            backSize = new Vector3(width, height, backDepth),
            frontSize = new Vector3(width, height, frontDepth),
            proxySize = new Vector3(
                Mathf.Max(0.05f, width * settings.proxyMultiplier.x),
                Mathf.Max(0.05f, height * settings.proxyMultiplier.y),
                Mathf.Max(0.05f, depth * settings.proxyMultiplier.z))
        };

        // +Z 是后方。
        boxes.backCenter = center + new Vector3(0f, 0f, backDepth * 0.5f);
        boxes.frontCenter = center + new Vector3(0f, 0f, -frontDepth * 0.5f);
        boxes.proxyCenter = center + settings.proxyOffset;

        return boxes;
    }

    private static void ApplyBoxCollider(Transform node, Vector3 center, Vector3 size, bool isTrigger, string layerName)
    {
        ResetLocalTransform(node);
        BoxCollider box = EnsureComponent<BoxCollider>(node.gameObject);
        box.isTrigger = isTrigger;
        box.center = center;
        box.size = AbsSize(size);
        SetLayerIfExists(node.gameObject, layerName);
    }

    private static void ApplyProxyBox(Transform proxy, Vector3 center, Vector3 size)
    {
        ApplyBoxCollider(proxy, center, size, isTrigger: true, "OcclusionMask");

        Renderer renderer = proxy.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = FindOcclusionMaskMaterial();
            if (mat != null)
                renderer.sharedMaterial = mat;
            renderer.enabled = true;
        }
    }

    private static void ExpandWakeTriggerVerticalRange(Transform instanceRoot, Transform visualRoot, ref Vector3 cameraFacingCenter, ref Vector3 worldSize)
    {
        if (instanceRoot == null || visualRoot == null)
            return;

        if (!GetVisualWorldHeightRange(visualRoot, out float visualMinY, out float visualMaxY))
            return;

        float rootY = instanceRoot.position.y;

        // BackTrigger 是“唤醒范围”，不是最终遮挡判定。
        // 叠放/高架物体的 Visual Bounds 可能整体高于角色 Collider，
        // 如果 Trigger 只贴着物体本体高度，角色在下方永远不会进入 Trigger，Footprint 也就没有机会判断。
        // 因此唤醒盒向下扩展一段安全高度；是否真正遮挡仍由 footprintMinY/MaxY + footprint 多边形决定。
        float currentMinY = rootY + cameraFacingCenter.y - Mathf.Abs(worldSize.y) * 0.5f;
        float currentMaxY = rootY + cameraFacingCenter.y + Mathf.Abs(worldSize.y) * 0.5f;

        float desiredMinY = Mathf.Min(currentMinY, visualMinY, rootY - OcclusionWakeTriggerDownwardPadding);
        float desiredMaxY = Mathf.Max(currentMaxY, visualMaxY + OcclusionWakeTriggerUpwardPadding);

        if (desiredMaxY <= desiredMinY + 0.01f)
            return;

        cameraFacingCenter.y = ((desiredMinY + desiredMaxY) * 0.5f) - rootY;
        worldSize.y = Mathf.Max(0.05f, desiredMaxY - desiredMinY);
    }

    private static void ApplyCameraFacingBoxCollider(Transform instanceRoot, Transform node, Vector3 cameraFacingCenter, Vector3 worldSize, bool isTrigger, string layerName)
    {
        if (instanceRoot == null || node == null)
            return;

        // 前后判定盒是 2.5D 镜头规则，不跟随 Q/E 摆放旋转。
        // 盒子朝向固定在 45° 镜头判定坐标系：
        // Local X = 画面横向 / 宽度轴，Local Y = 世界高度，Local Z = 镜头前后 / 深度轴。
        // 尺寸则由摆放确认后的 VisualRoot 最外角点投影范围决定。
        node.position = instanceRoot.position;
        node.rotation = GetOcclusionBasisRotation();

        BoxCollider box = EnsureComponent<BoxCollider>(node.gameObject);
        box.isTrigger = isTrigger;
        box.center = cameraFacingCenter;
        box.size = WorldSizeToColliderLocalSize(node, AbsSize(worldSize));
        SetLayerIfExists(node.gameObject, layerName);
    }

    private static void ApplyCameraFacingProxyBox(Transform instanceRoot, Transform proxy, Vector3 cameraFacingCenter, Vector3 worldSize)
    {
        ApplyCameraFacingBoxCollider(instanceRoot, proxy, cameraFacingCenter, worldSize, isTrigger: true, "OcclusionMask");

        Renderer renderer = proxy.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = FindOcclusionMaskMaterial();
            if (mat != null)
                renderer.sharedMaterial = mat;
            renderer.enabled = true;
        }
    }

    private static Vector3 WorldSizeToColliderLocalSize(Transform node, Vector3 worldSize)
    {
        if (node == null)
            return worldSize;

        Vector3 scale = node.lossyScale;
        return new Vector3(
            Mathf.Abs(scale.x) > 0.0001f ? worldSize.x / Mathf.Abs(scale.x) : worldSize.x,
            Mathf.Abs(scale.y) > 0.0001f ? worldSize.y / Mathf.Abs(scale.y) : worldSize.y,
            Mathf.Abs(scale.z) > 0.0001f ? worldSize.z / Mathf.Abs(scale.z) : worldSize.z);
    }

    private static void ClearFrontBackOcclusionNodes(Transform ruleRoot)
    {
        DestroyChildIfExists(ruleRoot, BackTriggerName);
        DestroyChildIfExists(ruleRoot, FrontTriggerName);
        DestroyChildIfExists(ruleRoot, FrontOccluderRootName);
    }

    private static Transform EnsureBoxOccluderProxy(Transform frontOccluderRoot)
    {
        Transform existing = frontOccluderRoot.Find(FrontOccluderProxyBoxName);
        if (existing != null)
            return existing;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = FrontOccluderProxyBoxName;
        go.transform.SetParent(frontOccluderRoot, false);
        return go.transform;
    }

    private static void ConfigureBackTriggerOcclusionController(Transform backTrigger, Transform frontOccluderRoot, Transform instanceRoot, Transform visualRoot)
    {
        if (backTrigger == null || frontOccluderRoot == null)
            return;

        SkyPrisonTerrainDecorationFrontOccluderTrigger controller = EnsureComponent<SkyPrisonTerrainDecorationFrontOccluderTrigger>(backTrigger.gameObject);
        controller.frontOccluderRoot = frontOccluderRoot.gameObject;
        controller.useFootprintPrecision = true;

        int unitBodyLayer = LayerMask.NameToLayer("UnitBody");
        if (unitBodyLayer < 0)
            unitBodyLayer = LayerMask.NameToLayer("Unitbody");
        if (unitBodyLayer < 0)
            unitBodyLayer = LayerMask.NameToLayer("Player");

        controller.targetLayers = unitBodyLayer >= 0 ? (1 << unitBodyLayer) : ~0;

        Vector2[] footprint = BuildVisualFootprintPolygon(instanceRoot, visualRoot);
        GetVisualWorldHeightRange(visualRoot, out float footprintMinY, out float footprintMaxY);
        controller.ConfigureFootprint(
            instanceRoot != null ? instanceRoot.position : Vector3.zero,
            GetOcclusionRightAxis(),
            GetOcclusionDepthAxis(),
            footprint,
            footprintMinY,
            footprintMaxY);

        controller.ResetRuntimeState();
    }

    private static void BuildFrontOccluderProxy(Transform instanceRoot, Transform frontOccluderRoot, Transform visualRoot, SerializedObject definitionSO, Vector3 cameraFacingCenter, Vector3 worldSize)
    {
        if (frontOccluderRoot == null)
            return;

        ClearGeneratedFrontOccluderProxies(frontOccluderRoot);

        int mode = GetEnumIndex(definitionSO, "frontOccluderProxyMode", ProxyModel);
        if (mode == ProxyNone)
            return;

        if (mode == ProxyBox)
        {
            Transform proxy = EnsureBoxOccluderProxy(frontOccluderRoot);
            ApplyCameraFacingProxyBox(instanceRoot, proxy, cameraFacingCenter, worldSize);
            proxy.gameObject.SetActive(true);
            return;
        }

        if (mode == ProxyManualPrefab)
        {
            GameObject prefab = GetObject<GameObject>(definitionSO, "manualFrontOccluderProxyPrefab", null);
            if (prefab == null)
                return;

            GameObject instance = Object.Instantiate(prefab, frontOccluderRoot);
            instance.name = FrontOccluderProxyManualRootName;
            ResetLocalTransform(instance.transform);
            SetLayerRecursivelyIfExists(instance, "OcclusionMask");
            ReplaceRendererMaterialsWithProxyMaterials(instance, definitionSO);
            instance.SetActive(true);
            return;
        }

        // 默认模式：参考模型代理。
        // 复制 VisualRoot 下的 Mesh / UV / Transform，替换成代理体材质。
        // 这样草、栅栏、铁网等透明贴图资产不会被一整块 Box 错误遮挡。
        BuildModelBasedFrontOccluderProxy(frontOccluderRoot, visualRoot, definitionSO);
    }

    private static void BuildModelBasedFrontOccluderProxy(Transform frontOccluderRoot, Transform visualRoot, SerializedObject definitionSO)
    {
        Transform modelRoot = EnsureChild(frontOccluderRoot, FrontOccluderProxyModelRootName);
        ClearChildren(modelRoot);
        ResetLocalTransform(modelRoot);
        SetLayerIfExists(modelRoot.gameObject, "OcclusionMask");

        if (visualRoot == null)
        {
            modelRoot.gameObject.SetActive(true);
            return;
        }

        int created = 0;

        MeshFilter[] meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter sourceFilter = meshFilters[i];
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                continue;

            MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null || !sourceRenderer.enabled)
                continue;

            GameObject proxy = new GameObject("__FrontOccluderProxy_Model_" + created.ToString("00") + "_" + MakeSafeName(sourceFilter.gameObject.name));
            proxy.transform.SetParent(modelRoot, false);
            CopyWorldTransform(sourceFilter.transform, proxy.transform);
            SetLayerIfExists(proxy, "OcclusionMask");

            MeshFilter proxyFilter = proxy.AddComponent<MeshFilter>();
            proxyFilter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer proxyRenderer = proxy.AddComponent<MeshRenderer>();
            proxyRenderer.sharedMaterials = BuildProxyMaterials(sourceRenderer.sharedMaterials, definitionSO);
            proxyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            proxyRenderer.receiveShadows = false;
            created++;
        }

        SkinnedMeshRenderer[] skinnedRenderers = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer sourceRenderer = skinnedRenderers[i];
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null || !sourceRenderer.enabled)
                continue;

            GameObject proxy = new GameObject("__FrontOccluderProxy_Model_" + created.ToString("00") + "_" + MakeSafeName(sourceRenderer.gameObject.name));
            proxy.transform.SetParent(modelRoot, false);
            CopyWorldTransform(sourceRenderer.transform, proxy.transform);
            SetLayerIfExists(proxy, "OcclusionMask");

            MeshFilter proxyFilter = proxy.AddComponent<MeshFilter>();
            proxyFilter.sharedMesh = sourceRenderer.sharedMesh;

            MeshRenderer proxyRenderer = proxy.AddComponent<MeshRenderer>();
            proxyRenderer.sharedMaterials = BuildProxyMaterials(sourceRenderer.sharedMaterials, definitionSO);
            proxyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            proxyRenderer.receiveShadows = false;
            created++;
        }

        // 代理体自身保持开启；FrontOccluderRoot 默认关闭，由 BackTrigger 上的运行时控制脚本整体开关。
        modelRoot.gameObject.SetActive(true);
    }

    private static void ClearGeneratedFrontOccluderProxies(Transform frontOccluderRoot)
    {
        if (frontOccluderRoot == null)
            return;

        for (int i = frontOccluderRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = frontOccluderRoot.GetChild(i);
            string n = child.name;
            if (n == FrontOccluderProxyBoxName ||
                n == FrontOccluderProxyModelRootName ||
                n == FrontOccluderProxyManualRootName ||
                n.StartsWith("__FrontOccluderProxy_", System.StringComparison.Ordinal))
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    private static void ReplaceRendererMaterialsWithProxyMaterials(GameObject root, SerializedObject definitionSO)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            r.sharedMaterials = BuildProxyMaterials(r.sharedMaterials, definitionSO);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private static Material[] BuildProxyMaterials(Material[] sourceMaterials, SerializedObject definitionSO)
    {
        Material baseProxyMaterial = GetObject<Material>(definitionSO, "frontOccluderProxyMaterial", null);
        if (baseProxyMaterial == null)
            baseProxyMaterial = FindOcclusionMaskMaterial();

        if (sourceMaterials == null || sourceMaterials.Length == 0)
        {
            if (baseProxyMaterial == null)
                return new Material[0];
            return new[] { CreateProxyMaterial(baseProxyMaterial, null, definitionSO) };
        }

        Material[] result = new Material[sourceMaterials.Length];
        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            Material source = sourceMaterials[i];
            if (baseProxyMaterial != null)
                result[i] = CreateProxyMaterial(baseProxyMaterial, source, definitionSO);
            else
                result[i] = source;
        }
        return result;
    }

    private static Material CreateProxyMaterial(Material baseProxyMaterial, Material sourceMaterial, SerializedObject definitionSO)
    {
        if (baseProxyMaterial == null)
            return sourceMaterial;

        // 必须创建独立代理材质实例，不能直接改模板材质资产。
        // 模板材质只提供 Shader / 默认参数；每个代理材质实例从原 Renderer 材质读取 Alpha 来源贴图。
        Material mat = new Material(baseProxyMaterial);
        mat.name = sourceMaterial != null
            ? baseProxyMaterial.name + "__Proxy__" + MakeSafeName(sourceMaterial.name)
            : baseProxyMaterial.name + "__Proxy__White";

        TextureBinding sourceTexture = FindMainTextureBinding(sourceMaterial);
        if (sourceTexture.texture != null)
        {
            SetTextureIfPropertyExists(mat, "_MainTex", sourceTexture.texture, sourceTexture.scale, sourceTexture.offset);
            SetTextureIfPropertyExists(mat, "_BaseMap", sourceTexture.texture, sourceTexture.scale, sourceTexture.offset);
            SetTextureIfPropertyExists(mat, "_BaseColorMap", sourceTexture.texture, sourceTexture.scale, sourceTexture.offset);
            SetTextureIfPropertyExists(mat, "_AlbedoMap", sourceTexture.texture, sourceTexture.scale, sourceTexture.offset);
        }

        float cutoff = Mathf.Clamp01(GetFloat(definitionSO, "frontOccluderAlphaCutoff", 0.35f));
        SetFloatIfPropertyExists(mat, "_Cutoff", cutoff);
        SetFloatIfPropertyExists(mat, "_AlphaCutoff", cutoff);
        SetFloatIfPropertyExists(mat, "_Threshold", cutoff);
        return mat;
    }

    private struct TextureBinding
    {
        public Texture texture;
        public Vector2 scale;
        public Vector2 offset;

        public TextureBinding(Texture texture, Vector2 scale, Vector2 offset)
        {
            this.texture = texture;
            this.scale = scale;
            this.offset = offset;
        }
    }

    private static TextureBinding FindMainTextureBinding(Material material)
    {
        if (material == null)
            return new TextureBinding(null, Vector2.one, Vector2.zero);

        // URP/HDRP/内置管线常见主贴图字段都查一遍。
        // HDRP Lit 常用 _BaseColorMap；之前只查 _BaseMap / _MainTex 时会导致代理材质的 Alpha Source Texture 为空。
        string[] names =
        {
            "_BaseColorMap",
            "_BaseMap",
            "_MainTex",
            "_AlbedoMap",
            "_ColorMap",
            "_DiffuseMap",
            "_BaseColorTexture"
        };

        for (int i = 0; i < names.Length; i++)
        {
            string propertyName = names[i];
            if (!material.HasProperty(propertyName))
                continue;

            Texture texture = material.GetTexture(propertyName);
            if (texture == null)
                continue;

            Vector2 scale = material.GetTextureScale(propertyName);
            Vector2 offset = material.GetTextureOffset(propertyName);
            return new TextureBinding(texture, scale, offset);
        }

        return new TextureBinding(null, Vector2.one, Vector2.zero);
    }

    private static void SetTextureIfPropertyExists(Material material, string propertyName, Texture texture, Vector2 scale, Vector2 offset)
    {
        if (material == null || texture == null || !material.HasProperty(propertyName))
            return;

        material.SetTexture(propertyName, texture);
        material.SetTextureScale(propertyName, scale);
        material.SetTextureOffset(propertyName, offset);
    }

    private static void SetFloatIfPropertyExists(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    private static void BuildMeshCollisionProxies(Transform meshRoot, Transform visualRoot, string layerName)
    {
        ClearChildren(meshRoot);

        if (visualRoot == null)
            return;

        List<MeshFilter> meshFilters = new List<MeshFilter>(visualRoot.GetComponentsInChildren<MeshFilter>(true));
        int created = 0;
        for (int i = 0; i < meshFilters.Count; i++)
        {
            MeshFilter source = meshFilters[i];
            if (source == null || source.sharedMesh == null)
                continue;

            Renderer renderer = source.GetComponent<Renderer>();
            if (renderer != null && !renderer.enabled)
                continue;

            GameObject proxy = new GameObject("__PhysicsMeshCollider_" + created.ToString("00") + "_" + MakeSafeName(source.gameObject.name));
            proxy.transform.SetParent(meshRoot, false);
            CopyWorldTransform(source.transform, proxy.transform);
            SetLayerIfExists(proxy, layerName);

            MeshCollider meshCollider = proxy.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = source.sharedMesh;
            meshCollider.convex = false;
            created++;
        }
    }

    private static void ClearGeneratedCollisionChildren(Transform collisionRoot)
    {
        if (collisionRoot == null)
            return;

        for (int i = collisionRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = collisionRoot.GetChild(i);
            string n = child.name;
            if (n == MainCollisionBoxName || n == MainCollisionMeshRootName || n.StartsWith("__PhysicsMeshCollider_", System.StringComparison.Ordinal))
                Object.DestroyImmediate(child.gameObject);
        }
    }

    private static bool GetVisualWorldHeightRange(Transform visualRoot, out float minY, out float maxY)
    {
        minY = 0f;
        maxY = 0f;

        if (visualRoot == null)
            return false;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            Bounds b = r.bounds;
            if (!hasAny)
            {
                minY = b.min.y;
                maxY = b.max.y;
                hasAny = true;
            }
            else
            {
                if (b.min.y < minY) minY = b.min.y;
                if (b.max.y > maxY) maxY = b.max.y;
            }
        }

        if (!hasAny)
        {
            minY = float.NegativeInfinity;
            maxY = float.PositiveInfinity;
        }

        return hasAny;
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
            return;
        for (int i = root.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.GetChild(i).gameObject);
    }


    private static Vector2[] BuildVisualFootprintPolygon(Transform instanceRoot, Transform visualRoot)
    {
        if (instanceRoot == null || visualRoot == null)
            return null;

        Vector3 origin = instanceRoot.position;
        Vector3 rightAxis = GetOcclusionRightAxis();
        Vector3 depthAxis = GetOcclusionDepthAxis();

        List<Vector2> points = new List<Vector2>(128);

        MeshFilter[] meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter mf = meshFilters[i];
            if (mf == null || mf.sharedMesh == null)
                continue;

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled)
                continue;

            AddMeshVerticesToFootprint(points, mf.sharedMesh, mf.transform, origin, rightAxis, depthAxis);
        }

        SkinnedMeshRenderer[] skinned = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer sr = skinned[i];
            if (sr == null || sr.sharedMesh == null || !sr.enabled)
                continue;

            AddMeshVerticesToFootprint(points, sr.sharedMesh, sr.transform, origin, rightAxis, depthAxis);
        }

        if (points.Count < 3)
            AddRendererBoundsCornersToFootprint(points, visualRoot, origin, rightAxis, depthAxis);

        if (points.Count < 3)
            return null;

        return BuildConvexHull(points).ToArray();
    }

    private static void AddMeshVerticesToFootprint(List<Vector2> points, Mesh mesh, Transform transform, Vector3 origin, Vector3 rightAxis, Vector3 depthAxis)
    {
        if (points == null || mesh == null || transform == null)
            return;

        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
            return;

        // 编辑器生成时运行，可接受直接遍历顶点。若以后遇到超大网格，再加采样上限。
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = transform.TransformPoint(vertices[i]);
            Vector3 relative = world - origin;
            points.Add(new Vector2(Vector3.Dot(relative, rightAxis), Vector3.Dot(relative, depthAxis)));
        }
    }

    private static void AddRendererBoundsCornersToFootprint(List<Vector2> points, Transform visualRoot, Vector3 origin, Vector3 rightAxis, Vector3 depthAxis)
    {
        if (points == null || visualRoot == null)
            return;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            Bounds b = r.bounds;
            Vector3 min = b.min;
            Vector3 max = b.max;
            AddFootprintPoint(points, new Vector3(min.x, min.y, min.z), origin, rightAxis, depthAxis);
            AddFootprintPoint(points, new Vector3(min.x, min.y, max.z), origin, rightAxis, depthAxis);
            AddFootprintPoint(points, new Vector3(max.x, min.y, min.z), origin, rightAxis, depthAxis);
            AddFootprintPoint(points, new Vector3(max.x, min.y, max.z), origin, rightAxis, depthAxis);
            AddFootprintPoint(points, new Vector3(min.x, max.y, min.z), origin, rightAxis, depthAxis);
            AddFootprintPoint(points, new Vector3(min.x, max.y, max.z), origin, rightAxis, depthAxis);
            AddFootprintPoint(points, new Vector3(max.x, max.y, min.z), origin, rightAxis, depthAxis);
            AddFootprintPoint(points, new Vector3(max.x, max.y, max.z), origin, rightAxis, depthAxis);
        }
    }

    private static void AddFootprintPoint(List<Vector2> points, Vector3 world, Vector3 origin, Vector3 rightAxis, Vector3 depthAxis)
    {
        Vector3 relative = world - origin;
        points.Add(new Vector2(Vector3.Dot(relative, rightAxis), Vector3.Dot(relative, depthAxis)));
    }

    private static List<Vector2> BuildConvexHull(List<Vector2> input)
    {
        List<Vector2> points = new List<Vector2>(input);
        points.Sort((a, b) =>
        {
            int x = a.x.CompareTo(b.x);
            return x != 0 ? x : a.y.CompareTo(b.y);
        });

        // 去掉非常接近的重复点，降低边界抖动。
        List<Vector2> unique = new List<Vector2>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (unique.Count == 0 || (points[i] - unique[unique.Count - 1]).sqrMagnitude > 0.000001f)
                unique.Add(points[i]);
        }

        if (unique.Count <= 3)
            return unique;

        List<Vector2> lower = new List<Vector2>();
        for (int i = 0; i < unique.Count; i++)
        {
            Vector2 p = unique[i];
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0f)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        List<Vector2> upper = new List<Vector2>();
        for (int i = unique.Count - 1; i >= 0; i--)
        {
            Vector2 p = unique[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0f)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    private static bool TryCalculateVisualBoundsInCameraFacingSpace(Transform instanceRoot, Transform visualRoot, out Bounds cameraFacingBounds)
    {
        // 这里的空间不是物体本地空间，也不是普通世界 AABB，
        // 而是“摆放确认后的 45° 镜头判定空间”：
        // - 原点：实例根节点位置。
        // - X：画面横向 / 宽度轴。
        // - Y：世界高度。
        // - Z：镜头前后 / 深度轴，+Z 为后方。
        //
        // 关键点：长条物体斜着摆放时，不能只用普通 Bounds.size.x / size.z 估算。
        // 必须收集最终视觉姿态下所有 Renderer Bounds 的 8 个世界角点，
        // 投影到上述 45°判定坐标系后取 min / max。
        // 这样才会覆盖“宽度两侧最外顶点形成的对角线范围”。
        cameraFacingBounds = new Bounds(Vector3.zero, Vector3.one);
        if (instanceRoot == null || visualRoot == null)
            return false;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;

        Vector3 rightAxis = GetOcclusionRightAxis();
        Vector3 upAxis = GetOcclusionUpAxis();
        Vector3 depthAxis = GetOcclusionDepthAxis();
        Vector3 origin = instanceRoot.position;

        float minX = 0f, maxX = 0f;
        float minY = 0f, maxY = 0f;
        float minZ = 0f, maxZ = 0f;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            Bounds b = r.bounds;
            Vector3 min = b.min;
            Vector3 max = b.max;

            EncapsulateProjectedCorner(min.x, min.y, min.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            EncapsulateProjectedCorner(min.x, min.y, max.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            EncapsulateProjectedCorner(min.x, max.y, min.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            EncapsulateProjectedCorner(min.x, max.y, max.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            EncapsulateProjectedCorner(max.x, min.y, min.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            EncapsulateProjectedCorner(max.x, min.y, max.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            EncapsulateProjectedCorner(max.x, max.y, min.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
            EncapsulateProjectedCorner(max.x, max.y, max.z, origin, rightAxis, upAxis, depthAxis, ref hasAny, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
        }

        if (!hasAny)
            return false;

        Vector3 center = new Vector3(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f,
            (minZ + maxZ) * 0.5f);

        Vector3 size = new Vector3(
            Mathf.Max(0.05f, maxX - minX),
            Mathf.Max(0.05f, maxY - minY),
            Mathf.Max(0.05f, maxZ - minZ));

        cameraFacingBounds = new Bounds(center, size);
        return true;
    }

    private static void EncapsulateProjectedCorner(
        float x,
        float y,
        float z,
        Vector3 origin,
        Vector3 rightAxis,
        Vector3 upAxis,
        Vector3 depthAxis,
        ref bool hasAny,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY,
        ref float minZ,
        ref float maxZ)
    {
        Vector3 relative = new Vector3(x, y, z) - origin;
        float px = Vector3.Dot(relative, rightAxis);
        float py = Vector3.Dot(relative, upAxis);
        float pz = Vector3.Dot(relative, depthAxis);

        if (!hasAny)
        {
            minX = maxX = px;
            minY = maxY = py;
            minZ = maxZ = pz;
            hasAny = true;
            return;
        }

        if (px < minX) minX = px;
        if (px > maxX) maxX = px;
        if (py < minY) minY = py;
        if (py > maxY) maxY = py;
        if (pz < minZ) minZ = pz;
        if (pz > maxZ) maxZ = pz;
    }

    private static Vector3 GetOcclusionRightAxis()
    {
        // 注意：这里不能再写死为 (1,0,-1)。
        // 前后判定盒必须“朝向当前游戏镜头”，红线式的前后分界线应平行于画面横向。
        // 因此优先从 GamePlayCamera / OcclusionMaskCamera 的真实朝向取得水平投影轴。
        if (TryGetCameraFacingOcclusionAxes(out Vector3 rightAxis, out _))
            return rightAxis;

        return new Vector3(1f, 0f, -1f).normalized;
    }

    private static Vector3 GetOcclusionUpAxis()
    {
        return Vector3.up;
    }

    private static Vector3 GetOcclusionDepthAxis()
    {
        // +Depth 代表“后方”。优先使用游戏镜头 forward 在地面上的投影，
        // 这样 Q/E 或 Ctrl+滚轮旋转长条物体时，判定盒的分界线仍然对齐屏幕横向，
        // 尺寸则由最终模型角点投影决定。
        if (TryGetCameraFacingOcclusionAxes(out _, out Vector3 depthAxis))
            return depthAxis;

        return new Vector3(1f, 0f, 1f).normalized;
    }

    private static Quaternion GetOcclusionBasisRotation()
    {
        Vector3 depth = GetOcclusionDepthAxis();
        Vector3 up = GetOcclusionUpAxis();

        if (depth.sqrMagnitude < 0.0001f)
            depth = new Vector3(1f, 0f, 1f).normalized;

        return Quaternion.LookRotation(depth.normalized, up);
    }

    private static bool TryGetCameraFacingOcclusionAxes(out Vector3 rightAxis, out Vector3 depthAxis)
    {
        rightAxis = Vector3.zero;
        depthAxis = Vector3.zero;

        Camera cam = FindOcclusionReferenceCamera();
        if (cam == null)
            return false;

        Vector3 up = Vector3.up;

        // Camera.forward 是屏幕“向里”的方向。投影到地面后，就是 2.5D 前后判定轴。
        depthAxis = Vector3.ProjectOnPlane(cam.transform.forward, up);
        if (depthAxis.sqrMagnitude < 0.0001f)
            return false;

        depthAxis.Normalize();

        // 保持和旧项目约定一致：+Z 大致为后方。
        // 避免不同相机命名 / 负轴导致 BackTrigger 与 FrontTrigger 整体反转。
        Vector3 legacyBack = new Vector3(1f, 0f, 1f).normalized;
        if (Vector3.Dot(depthAxis, legacyBack) < 0f)
            depthAxis = -depthAxis;

        // 画面横向轴必须和 depthAxis 正交。先用数学正交轴，再用 Camera.right 修正符号。
        rightAxis = Vector3.Cross(up, depthAxis);
        if (rightAxis.sqrMagnitude < 0.0001f)
            return false;

        rightAxis.Normalize();

        Vector3 cameraRight = Vector3.ProjectOnPlane(cam.transform.right, up);
        if (cameraRight.sqrMagnitude > 0.0001f && Vector3.Dot(rightAxis, cameraRight.normalized) < 0f)
            rightAxis = -rightAxis;

        return true;
    }

    private static Camera FindOcclusionReferenceCamera()
    {
        // 优先使用真实 Game 相机，其次使用遮挡 Mask 相机。不要使用 SceneView 相机生成正式结构，
        // 否则编辑视角一变，摆放结果就会变，这又会变成暗箱。
        string[] preferredNames =
        {
            "GamePlayCamera",
            "GameplayCamera",
            "Main Camera",
            "OcclusionMaskCamera"
        };

        for (int i = 0; i < preferredNames.Length; i++)
        {
            GameObject go = GameObject.Find(preferredNames[i]);
            if (go == null)
                continue;

            Camera c = go.GetComponent<Camera>();
            if (c != null)
                return c;
        }

        if (Camera.main != null)
            return Camera.main;

        Camera[] cameras = Object.FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c == null)
                continue;

            string n = c.name;
            if (n.Contains("Game") || n.Contains("Occlusion") || n.Contains("Mask"))
                return c;
        }

        return null;
    }

    private static void RemoveLegacyCollisionRootUnderRuleRoot(Transform ruleRoot)
    {
        if (ruleRoot == null)
            return;

        Transform legacy = ruleRoot.Find(CollisionRootName);
        if (legacy != null)
            Object.DestroyImmediate(legacy.gameObject);
    }

    private static void DestroyChildIfExists(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName))
            return;

        Transform child = parent.Find(childName);
        if (child != null)
            Object.DestroyImmediate(child.gameObject);
    }

    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
            component = go.AddComponent<T>();
        return component;
    }

    private static void ResetLocalTransform(Transform t)
    {
        if (t == null)
            return;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
    }

    private static void CopyWorldTransform(Transform source, Transform target)
    {
        if (source == null || target == null)
            return;
        target.position = source.position;
        target.rotation = source.rotation;
        target.localScale = source.lossyScale;
    }

    private static void ApplyStandardLayers(GameObject root, SerializedObject definitionSO)
    {
        Transform visualRoot = root.transform.Find(VisualRootName);
        if (visualRoot != null)
            SetLayerRecursivelyIfExists(visualRoot.gameObject, "World3D");
    }

    private static string ResolveBlockingLayer(bool blockPlayer, bool blockEnemy, bool blockProjectile)
    {
        // 暂时统一走 World3D，避免凭空发明不存在的层。
        // 后续若你有专用 DecorationPhysics / ProjectileBlocker 层，再在这里集中映射。
        return "World3D";
    }

    private static Material FindOcclusionMaskMaterial()
    {
        string[] guids = AssetDatabase.FindAssets("M_OcclusionMask_ForegroundOccluder t:Material");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
        }
        return null;
    }

    private static T GetObject<T>(SerializedObject so, string propertyName, T fallback) where T : Object
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
            return prop.objectReferenceValue as T ?? fallback;
        return fallback;
    }

    private static int GetEnumIndex(SerializedObject so, string propertyName, int fallback)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null && prop.propertyType == SerializedPropertyType.Enum ? prop.enumValueIndex : fallback;
    }

    private static bool GetBool(SerializedObject so, string propertyName, bool fallback)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null && prop.propertyType == SerializedPropertyType.Boolean ? prop.boolValue : fallback;
    }

    private static float GetFloat(SerializedObject so, string propertyName, float fallback)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null && prop.propertyType == SerializedPropertyType.Float ? prop.floatValue : fallback;
    }

    private static string GetString(SerializedObject so, string propertyName, string fallback)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null && prop.propertyType == SerializedPropertyType.String ? prop.stringValue : fallback;
    }

    private static Vector3 GetVector3(SerializedObject so, string propertyName, Vector3 fallback)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null && prop.propertyType == SerializedPropertyType.Vector3 ? prop.vector3Value : fallback;
    }

    private static Vector3 AbsSize(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static string MakeSafeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Mesh";
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace('/', '_').Replace(' ', '_');
    }

    private static void SetLayerIfExists(GameObject go, string layerName)
    {
        if (go == null || string.IsNullOrEmpty(layerName))
            return;
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
            go.layer = layer;
    }

    private static void SetLayerRecursivelyIfExists(GameObject go, string layerName)
    {
        if (go == null || string.IsNullOrEmpty(layerName))
            return;
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            return;
        Transform[] children = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
            children[i].gameObject.layer = layer;
    }

    private static string BuildResultLog(GameObject root, TerrainDecorationDefinition definition, SerializedObject definitionSO)
    {
        int collisionMode = GetEnumIndex(definitionSO, "collisionMode", CollisionNone);
        int occlusionMode = GetEnumIndex(definitionSO, "occlusionMode", OcclusionNone);
        int proxyMode = GetEnumIndex(definitionSO, "frontOccluderProxyMode", ProxyModel);
        Vector3 size = GetVector3(definitionSO, "collisionSize", Vector3.zero);
        Vector3 offset = GetVector3(definitionSO, "collisionOffset", Vector3.zero);
        return $"{LogPrefix} 已按定义生成实例结构：{root.name}\n" +
               $"- Definition: {definition.name}\n" +
               $"- CollisionMode Index: {collisionMode}\n" +
               $"- Collision Size: {size}\n" +
               $"- Collision Offset: {offset}\n" +
               $"- OcclusionMode Index: {occlusionMode}\n" +
               $"- FrontOccluderProxyMode Index: {proxyMode}\n" +
               "- FrontOccluderProxy 默认不强行显示，由运行时遮挡逻辑控制。";
    }
}
