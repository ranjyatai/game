using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地形装饰物实例矫正桥接。
/// 只处理已经生成好的 Scene 实例：
/// - 不走 PF_TD / RuntimeTemplateUtility
/// - 遮挡盒使用定义页按钮同款投影规则
/// - 网格碰撞只从当前实例 VisualRoot 真实模型 Mesh 生成
/// - 放置后补齐触发器运行状态，避免 BackTrigger/FrontTrigger 不开遮挡代理
/// - V2：FrontOccluderProxy_Model 只做模型轮廓代理，按 VisualRoot Bounds 对齐；不再吃投影盒扩宽误差。
/// </summary>
public static class SkyPrisonTerrainDecorationButtonCorrectionBridge
{
    private const string DecorationTriggerLayerName = "DecorationTrigger";
    private const string OcclusionMaskLayerName = "OcclusionMask";
    private const string WorldLayerName = "World3D";
    private const string UnitBodyLayerName = "UnitBody";

    public static bool CorrectRuntimeInstanceLikeDefinitionButton(GameObject root, bool useUndo)
    {
        if (root == null)
            return false;

        bool changed = false;

        // 前后遮挡必须和物理碰撞解耦：
        // 草、花、苔藓这类 collisionMode=无 的装饰物，只要定义里开了前后遮挡，
        // 也必须拥有 BackTrigger / FrontTrigger / FrontOccluderProxy_Box / 自动形状代理。
        changed |= EnsureOcclusionNodesForEnabledDefinition(root, useUndo);

        // 先按按钮标准修遮挡盒。
        changed |= RepairProjectedOcclusionBoxesFromVisualRoot(root, useUndo);

        // V2：FrontOccluderProxy_Model 是模型轮廓代理，不是投影盒。
        // 它必须贴合 VisualRoot，不能继承 Box 代理那套扩宽/偏移。
        changed |= AlignFrontOccluderProxyModelToVisualRoot(root, useUndo);

        // 如果 RuntimeApplier 因为无碰撞分支没有生成前景遮挡形状，这里从当前 VisualRoot 补一个自动形状代理。
        changed |= EnsureAutoFrontOccluderShapeCloneFromVisual(root, useUndo);

        // 再按当前定义的碰撞模式补齐物理碰撞。
        // 注意：这里只处理当前 Scene 实例，不读取 customPhysicsMesh，不读 PF_TD。
        // 盒体碰撞必须严格吃定义里的 collisionSize / collisionOffset，不能只拿它做放置重叠预检。
        changed |= RebuildBoxCollisionFromDefinitionIfNeeded(root, useUndo);
        changed |= RebuildMeshCollisionFromCurrentVisualIfNeeded(root, useUndo);

        // 最后补齐运行时触发状态：Trigger / Layer / Enabled。
        changed |= ForceOcclusionTriggerRuntimeState(root);

        if (changed)
        {
            EditorUtility.SetDirty(root);
            Physics.SyncTransforms();
        }

        return changed;
    }


    private static bool EnsureOcclusionNodesForEnabledDefinition(GameObject root, bool useUndo)
    {
        if (root == null)
            return false;

        if (!ShouldEnsureOcclusionForRoot(root))
            return false;

        bool changed = false;
        Transform ruleRoot = EnsureChild(root.transform, "RuleRoot", useUndo, ref changed);
        if (ruleRoot == null)
            return changed;

        Transform backTrigger = EnsureChild(ruleRoot, "BackTrigger", useUndo, ref changed);
        Transform frontTrigger = EnsureChild(ruleRoot, "FrontTrigger", useUndo, ref changed);
        Transform frontOccluderRoot = EnsureChild(ruleRoot, "FrontOccluderRoot", useUndo, ref changed);
        Transform frontProxy = EnsureChild(frontOccluderRoot, "FrontOccluderProxy_Box", useUndo, ref changed);

        int decorationTriggerLayer = LayerMask.NameToLayer(DecorationTriggerLayerName);
        int occlusionMaskLayer = LayerMask.NameToLayer(OcclusionMaskLayerName);

        changed |= NormalizeTrigger(backTrigger, decorationTriggerLayer);
        changed |= NormalizeTrigger(frontTrigger, decorationTriggerLayer);

        if (frontProxy != null)
        {
            frontProxy.gameObject.SetActive(true);
            changed |= SetLayerRecursivelyIfDifferent(frontProxy, occlusionMaskLayer);

            BoxCollider box = frontProxy.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = useUndo ? Undo.AddComponent<BoxCollider>(frontProxy.gameObject) : frontProxy.gameObject.AddComponent<BoxCollider>();
                changed = true;
            }
            if (box != null)
            {
                if (!box.enabled) { box.enabled = true; changed = true; }
                // FrontOccluderProxy_Box 只负责遮挡代理形状，不参与物理阻挡。
                if (!box.isTrigger) { box.isTrigger = true; changed = true; }
                EditorUtility.SetDirty(box);
            }

            EditorUtility.SetDirty(frontProxy.gameObject);
        }

        return changed;
    }

    private static bool ShouldEnsureOcclusionForRoot(GameObject root)
    {
        if (root == null)
            return false;

        // 已经出现任一遮挡节点时，说明这个实例处在前后遮挡路线里；即使物理碰撞为“无”，也要补齐代理体。
        Transform ruleRoot = root.transform.Find("RuleRoot");
        if (ruleRoot != null)
        {
            if (ruleRoot.Find("BackTrigger") != null) return true;
            if (ruleRoot.Find("FrontTrigger") != null) return true;
            if (ruleRoot.Find("FrontOccluderRoot") != null) return true;
            if (ruleRoot.Find("FrontOccluderRoot/FrontOccluderProxy_Box") != null) return true;
        }

        TerrainDecorationRuntimeBinder binder = root.GetComponent<TerrainDecorationRuntimeBinder>();
        Object definition = binder != null ? binder.definition : null;
        return DefinitionRequestsFrontBackOcclusion(definition);
    }

    private static bool DefinitionRequestsFrontBackOcclusion(Object definition)
    {
        if (definition == null)
            return false;

        SerializedObject so = new SerializedObject(definition);

        // 不同版本字段名可能不同，这里只读不写，避免把定义类绑死。
        string[] boolNames =
        {
            "enableFrontBackOcclusion",
            "useFrontBackOcclusion",
            "frontBackOcclusion",
            "enableOcclusion",
            "useOcclusion",
            "enableForegroundOcclusion",
            "useForegroundOcclusion",
            "enableFrontOccluder",
            "generateFrontOccluder",
            "hasFrontBackOcclusion"
        };

        for (int i = 0; i < boolNames.Length; i++)
        {
            SerializedProperty prop = so.FindProperty(boolNames[i]);
            if (prop != null && prop.propertyType == SerializedPropertyType.Boolean && prop.boolValue)
                return true;
        }

        string[] enumNames =
        {
            "occlusionMode",
            "frontBackOcclusionMode",
            "frontOcclusionMode",
            "foregroundOcclusionMode"
        };

        for (int i = 0; i < enumNames.Length; i++)
        {
            SerializedProperty prop = so.FindProperty(enumNames[i]);
            if (prop != null && prop.propertyType == SerializedPropertyType.Enum && prop.enumValueIndex > 0)
                return true;
        }

        SerializedProperty iterator = so.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            string propertyName = iterator.name ?? string.Empty;
            string lower = propertyName.ToLowerInvariant();
            bool looksLikeOcclusion = lower.Contains("occlusion") || lower.Contains("occluder") ||
                                      (lower.Contains("front") && lower.Contains("back"));
            if (!looksLikeOcclusion || lower.Contains("collision"))
                continue;

            if (iterator.propertyType == SerializedPropertyType.Boolean && iterator.boolValue)
                return true;
            if (iterator.propertyType == SerializedPropertyType.Enum && iterator.enumValueIndex > 0)
                return true;
        }

        return false;
    }

    private static Transform EnsureChild(Transform parent, string childName, bool useUndo, ref bool changed)
    {
        if (parent == null || string.IsNullOrWhiteSpace(childName))
            return null;

        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        if (useUndo)
            Undo.RegisterCreatedObjectUndo(go, "补齐前后遮挡节点");

        child = go.transform;
        child.SetParent(parent, false);
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        changed = true;
        EditorUtility.SetDirty(go);
        return child;
    }


    private static bool AlignFrontOccluderProxyModelToVisualRoot(GameObject root, bool useUndo)
    {
        if (root == null)
            return false;

        Transform visualRoot = root.transform.Find("VisualRoot");
        Transform proxyModel = root.transform.Find("RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Model");
        if (visualRoot == null || proxyModel == null)
            return false;

        Renderer[] visualRenderers = CollectOcclusionRepairVisualRenderers(visualRoot);
        Renderer[] proxyRenderers = proxyModel.GetComponentsInChildren<Renderer>(true);
        if (visualRenderers == null || visualRenderers.Length == 0 || proxyRenderers == null || proxyRenderers.Length == 0)
            return false;

        if (!TryCalculateWorldBounds(visualRenderers, out Bounds visualBounds))
            return false;
        if (!TryCalculateWorldBounds(proxyRenderers, out Bounds proxyBounds))
            return false;

        const float centerEpsilon = 0.005f;
        const float sizeEpsilon = 0.005f;
        const float minSize = 0.0001f;
        const float minScaleFactor = 0.25f;
        const float maxScaleFactor = 4.0f;

        bool changed = false;

        Vector3 visualSize = visualBounds.size;
        Vector3 proxySize = proxyBounds.size;

        Vector3 scaleFactor = new Vector3(
            Mathf.Clamp(SafeDiv(Mathf.Max(minSize, visualSize.x), Mathf.Max(minSize, proxySize.x)), minScaleFactor, maxScaleFactor),
            Mathf.Clamp(SafeDiv(Mathf.Max(minSize, visualSize.y), Mathf.Max(minSize, proxySize.y)), minScaleFactor, maxScaleFactor),
            Mathf.Clamp(SafeDiv(Mathf.Max(minSize, visualSize.z), Mathf.Max(minSize, proxySize.z)), minScaleFactor, maxScaleFactor));

        bool sizeMismatch = Mathf.Abs(visualSize.x - proxySize.x) > sizeEpsilon ||
                            Mathf.Abs(visualSize.y - proxySize.y) > sizeEpsilon ||
                            Mathf.Abs(visualSize.z - proxySize.z) > sizeEpsilon;

        if (sizeMismatch)
        {
            if (useUndo)
                Undo.RecordObject(proxyModel, "对齐模型遮挡代理尺寸");

            Vector3 oldLocalScale = proxyModel.localScale;
            Vector3 newLocalScale = new Vector3(
                oldLocalScale.x * scaleFactor.x,
                oldLocalScale.y * scaleFactor.y,
                oldLocalScale.z * scaleFactor.z);

            if ((newLocalScale - oldLocalScale).sqrMagnitude > 0.0000001f)
            {
                proxyModel.localScale = newLocalScale;
                changed = true;
            }
        }

        // 缩放之后重新算 Bounds，再做世界中心对齐。
        proxyRenderers = proxyModel.GetComponentsInChildren<Renderer>(true);
        if (TryCalculateWorldBounds(proxyRenderers, out proxyBounds))
        {
            Vector3 centerDelta = visualBounds.center - proxyBounds.center;
            if (centerDelta.sqrMagnitude > centerEpsilon * centerEpsilon)
            {
                if (useUndo)
                    Undo.RecordObject(proxyModel, "对齐模型遮挡代理中心");

                proxyModel.position += centerDelta;
                changed = true;
            }
        }

        int occlusionMaskLayer = LayerMask.NameToLayer(OcclusionMaskLayerName);
        changed |= SetLayerRecursivelyIfDifferent(proxyModel, occlusionMaskLayer);

        if (!proxyModel.gameObject.activeSelf)
        {
            proxyModel.gameObject.SetActive(true);
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(proxyModel.gameObject);
            Debug.Log($"[TD_OCCLUSION_PROXY_MODEL_ALIGN] 已按 VisualRoot 对齐 FrontOccluderProxy_Model：{root.name}", root);
        }

        return changed;
    }

    private static bool TryCalculateWorldBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool initialized = false;
        if (renderers == null)
            return false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;

            if (!initialized)
            {
                bounds = r.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return initialized;
    }

    private static bool EnsureAutoFrontOccluderShapeCloneFromVisual(GameObject root, bool useUndo)
    {
        if (root == null || !ShouldEnsureOcclusionForRoot(root))
            return false;

        Transform visualRoot = root.transform.Find("VisualRoot");
        Transform frontProxy = root.transform.Find("RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Box");
        if (visualRoot == null || frontProxy == null)
            return false;

        Transform autoRoot = frontProxy.Find("__AutoFrontOccluderShapeClone");
        if (autoRoot != null && autoRoot.GetComponentsInChildren<Renderer>(true).Length > 0)
            return false;

        bool changed = false;
        if (autoRoot == null)
        {
            autoRoot = EnsureChild(frontProxy, "__AutoFrontOccluderShapeClone", useUndo, ref changed);
        }
        else
        {
            autoRoot.gameObject.SetActive(true);
        }

        List<MeshFilter> sources = CollectCurrentVisualMeshFilters(visualRoot);
        if (sources.Count == 0)
            return changed;

        int occlusionMaskLayer = LayerMask.NameToLayer(OcclusionMaskLayerName);
        changed |= SetLayerRecursivelyIfDifferent(autoRoot, occlusionMaskLayer);

        int created = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            MeshFilter srcFilter = sources[i];
            if (srcFilter == null || srcFilter.sharedMesh == null)
                continue;

            Renderer srcRenderer = srcFilter.GetComponent<Renderer>();
            if (srcRenderer == null)
                continue;

            GameObject proxy = new GameObject($"__OcclusionShape_{created:00}_{SanitizeName(srcFilter.name)}");
            if (useUndo)
                Undo.RegisterCreatedObjectUndo(proxy, "生成前景遮挡代理形状");

            proxy.transform.SetParent(autoRoot, false);
            CopyWorldTransform(proxy.transform, srcFilter.transform);
            if (occlusionMaskLayer >= 0)
                proxy.layer = occlusionMaskLayer;

            MeshFilter dstFilter = proxy.AddComponent<MeshFilter>();
            dstFilter.sharedMesh = srcFilter.sharedMesh;

            MeshRenderer dstRenderer = proxy.AddComponent<MeshRenderer>();
            dstRenderer.sharedMaterials = srcRenderer.sharedMaterials;
            dstRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dstRenderer.receiveShadows = false;
            dstRenderer.enabled = true;

            EditorUtility.SetDirty(proxy);
            created++;
            changed = true;
        }

        if (created > 0)
        {
            autoRoot.gameObject.SetActive(true);
            changed |= SetLayerRecursivelyIfDifferent(autoRoot, occlusionMaskLayer);
            EditorUtility.SetDirty(autoRoot.gameObject);
            Debug.Log($"[TD_OCCLUSION_PROXY_REPAIR] 已为无物理/缺代理的前后遮挡物体生成前景遮挡形状代理：{created} 个 Mesh。", root);
        }

        return changed;
    }

    private static bool RepairProjectedOcclusionBoxesFromVisualRoot(GameObject root, bool useUndo)
    {
        Transform visualRoot = root.transform.Find("VisualRoot");
        Transform ruleRoot = root.transform.Find("RuleRoot");
        if (visualRoot == null || ruleRoot == null)
            return false;

        Renderer[] renderers = CollectOcclusionRepairVisualRenderers(visualRoot);
        if (renderers == null || renderers.Length == 0)
            return false;

        bool changed = false;

        // BackTrigger 单独反向：之前已经确认它不能跟 FrontOccluderProxy 一起改方向。
        changed |= ApplyProjectedOcclusionBox(ruleRoot.Find("BackTrigger"), renderers, 1.10f, 0.85f, -1f, 0f, useUndo);

        // FrontTrigger / FrontOccluderProxy / VisionBlocker 保持按钮方向。
        changed |= ApplyProjectedOcclusionBox(ruleRoot.Find("FrontTrigger"), renderers, 1.10f, 0.85f, 1f, 0f, useUndo);

        // FrontOccluderProxy_Box 是真正写前景遮挡的代理体。它不能只包模型原始宽度，
        // 还要把模型高度按 45° 镜头投影到地面的左右两侧，否则叉车、货架这类高物体
        // 在侧面角度会出现“代理体开了，但侧边压不住角色”的窄盒问题。
        changed |= ApplyProjectedOcclusionBox(ruleRoot.Find("FrontOccluderRoot/FrontOccluderProxy_Box"), renderers, 1.18f, 0.95f, 1f, 1f, useUndo);

        changed |= ApplyProjectedOcclusionBox(ruleRoot.Find("VisionBlockerRoot"), renderers, 1.12f, 0.85f, 1f, 0f, useUndo);

        return changed;
    }

    private static bool ApplyProjectedOcclusionBox(
        Transform target,
        Renderer[] visualRenderers,
        float depthMultiplier,
        float minimumDepth,
        float zShiftSign,
        float sideProjectionMultiplier,
        bool useUndo)
    {
        if (target == null || visualRenderers == null || visualRenderers.Length == 0)
            return false;

        BoxCollider box = target.GetComponent<BoxCollider>();
        if (box == null)
            box = useUndo ? Undo.AddComponent<BoxCollider>(target.gameObject) : target.gameObject.AddComponent<BoxCollider>();
        if (box == null)
            return false;

        ProjectedOcclusionBounds projected = CalculateProjectedOcclusionBounds(target, visualRenderers, depthMultiplier, minimumDepth, zShiftSign, sideProjectionMultiplier);

        if (useUndo)
            Undo.RecordObject(box, "按完整模型包围前后遮挡盒");

        // 保持定义页按钮的写法：不乱改节点 Transform，只写 BoxCollider。
        box.center = projected.center;
        box.size = projected.size;
        box.isTrigger = true;
        box.enabled = true;

        target.gameObject.SetActive(true);
        EditorUtility.SetDirty(target.gameObject);
        EditorUtility.SetDirty(box);
        return true;
    }

    private static ProjectedOcclusionBounds CalculateProjectedOcclusionBounds(
        Transform targetSpace,
        Renderer[] visualRenderers,
        float depthMultiplier,
        float minimumDepth,
        float zShiftSign,
        float sideProjectionMultiplier)
    {
        bool initialized = false;
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer r = visualRenderers[i];
            if (r == null)
                continue;

            Bounds wb = r.bounds;
            Vector3 min = wb.min;
            Vector3 max = wb.max;

            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            for (int c = 0; c < corners.Length; c++)
            {
                Vector3 local = targetSpace.InverseTransformPoint(corners[c]);
                if (!initialized)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(local);
                }
            }
        }

        if (!initialized)
            localBounds = new Bounds(Vector3.zero, Vector3.one);

        Vector3 center = localBounds.center;
        Vector3 size = localBounds.size;

        const float cameraElevationDegrees = 45f;
        const float frontReserveRatio = 0.18f;
        const float backReserveRatio = 0.82f;
        const float horizontalPadding = 0.08f;
        const float verticalPadding = 0.08f;
        const float depthPadding = 0.12f;

        float modelHeight = Mathf.Max(0.01f, size.y);
        float elevationRad = Mathf.Clamp(cameraElevationDegrees, 5f, 85f) * Mathf.Deg2Rad;
        float projectedDepth = modelHeight / Mathf.Tan(elevationRad);
        float projectedSideWidth = projectedDepth * Mathf.Max(0f, sideProjectionMultiplier);

        float baseDepth = Mathf.Max(0.01f, size.z);
        float totalDepth = Mathf.Max(minimumDepth, baseDepth + projectedDepth * Mathf.Max(0f, depthMultiplier) + depthPadding);
        float extraDepth = Mathf.Max(0f, totalDepth - baseDepth);
        float backwardShift = extraDepth * (backReserveRatio - frontReserveRatio) * 0.5f;

        center.z += backwardShift * zShiftSign;

        // X 方向默认只加少量安全边。只有 FrontOccluderProxy_Box 会额外吃 sideProjectionMultiplier：
        // 以 45° 镜头把模型高度投影到地面，并对左右两侧同时扩展，避免侧面遮挡代理体过窄。
        size.x = Mathf.Max(0.05f, size.x + projectedSideWidth * 2f + horizontalPadding * 2f);
        size.y = Mathf.Max(0.05f, size.y + verticalPadding * 2f);
        size.z = totalDepth;

        return new ProjectedOcclusionBounds(center, size);
    }

    private static bool RebuildBoxCollisionFromDefinitionIfNeeded(GameObject root, bool useUndo)
    {
        if (root == null)
            return false;

        TerrainDecorationRuntimeBinder binder = root.GetComponent<TerrainDecorationRuntimeBinder>();
        Object definition = binder != null ? binder.definition : null;
        if (definition == null)
            return false;

        int collisionMode = GetCollisionModeIndex(definition);
        // 0 = 无, 1 = 盒体碰撞, 2 = 网格碰撞, 3 = 自定义碰撞根节点。
        if (collisionMode != 1)
            return false;

        if (!TryReadDefinitionVector3(definition, "collisionSize", out Vector3 rawSize))
            return false;

        TryReadDefinitionVector3(definition, "collisionOffset", out Vector3 offset);

        Vector3 size = new Vector3(
            Mathf.Max(0.05f, Mathf.Abs(rawSize.x)),
            Mathf.Max(0.05f, Mathf.Abs(rawSize.y)),
            Mathf.Max(0.05f, Mathf.Abs(rawSize.z)));

        Transform ruleRoot = root.transform.Find("RuleRoot");
        if (ruleRoot == null)
        {
            GameObject ruleRootGo = new GameObject("RuleRoot");
            if (useUndo) Undo.RegisterCreatedObjectUndo(ruleRootGo, "创建规则根节点");
            ruleRoot = ruleRootGo.transform;
            ruleRoot.SetParent(root.transform, false);
            ruleRoot.localPosition = Vector3.zero;
            ruleRoot.localRotation = Quaternion.identity;
            ruleRoot.localScale = Vector3.one;
        }

        Transform collisionRoot = ruleRoot.Find("CollisionRoot");
        if (collisionRoot == null)
        {
            GameObject collisionRootGo = new GameObject("CollisionRoot");
            if (useUndo) Undo.RegisterCreatedObjectUndo(collisionRootGo, "创建盒体碰撞根节点");
            collisionRoot = collisionRootGo.transform;
            collisionRoot.SetParent(ruleRoot, false);
            collisionRoot.localPosition = Vector3.zero;
            collisionRoot.localRotation = Quaternion.identity;
            collisionRoot.localScale = Vector3.one;
        }

        // 盒体模式下只保留 Main_Collision_Box。网格模式残留必须清掉，否则会出现“看起来参数没生效”的混合碰撞。
        Transform visualRoot = root.transform.Find("VisualRoot");
        RemoveChildIfExists(collisionRoot, "Sub_Collision_Box", useUndo);
        RemoveChildIfExists(collisionRoot, "Main_Collision_MeshRoot", useUndo);
        if (visualRoot != null)
            RemoveVisualPhysicsPollutionRoots(visualRoot, useUndo);

        Transform boxTransform = collisionRoot.Find("Main_Collision_Box");
        if (boxTransform == null)
        {
            GameObject boxGo = new GameObject("Main_Collision_Box");
            if (useUndo) Undo.RegisterCreatedObjectUndo(boxGo, "创建盒体碰撞");
            boxTransform = boxGo.transform;
            boxTransform.SetParent(collisionRoot, false);
        }

        boxTransform.localPosition = Vector3.zero;
        boxTransform.localRotation = Quaternion.identity;
        boxTransform.localScale = Vector3.one;
        boxTransform.gameObject.SetActive(true);
        SetLayerIfExists(boxTransform.gameObject, WorldLayerName);

        BoxCollider box = boxTransform.GetComponent<BoxCollider>();
        if (box == null)
            box = useUndo ? Undo.AddComponent<BoxCollider>(boxTransform.gameObject) : boxTransform.gameObject.AddComponent<BoxCollider>();
        if (box == null)
            return false;

        if (useUndo)
            Undo.RecordObject(box, "按定义参数重建盒体碰撞");

        box.center = offset;
        box.size = size;
        box.isTrigger = false;
        box.enabled = true;

        EditorUtility.SetDirty(boxTransform.gameObject);
        EditorUtility.SetDirty(box);
        EditorUtility.SetDirty(collisionRoot.gameObject);
        EditorUtility.SetDirty(ruleRoot.gameObject);
        return true;
    }

    private static bool TryReadDefinitionVector3(Object definition, string propertyName, out Vector3 value)
    {
        value = Vector3.zero;
        if (definition == null || string.IsNullOrWhiteSpace(propertyName))
            return false;

        SerializedObject so = new SerializedObject(definition);
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null || prop.propertyType != SerializedPropertyType.Vector3)
            return false;

        value = prop.vector3Value;
        return true;
    }

    private static bool RebuildMeshCollisionFromCurrentVisualIfNeeded(GameObject root, bool useUndo)
    {
        if (root == null)
            return false;

        TerrainDecorationRuntimeBinder binder = root.GetComponent<TerrainDecorationRuntimeBinder>();
        Object definition = binder != null ? binder.definition : null;
        if (definition == null)
            return false;

        int collisionMode = GetCollisionModeIndex(definition);
        // 0 = 无, 1 = 盒体碰撞, 2 = 网格碰撞, 3 = 自定义碰撞根节点。
        if (collisionMode != 2)
            return false;

        Transform visualRoot = root.transform.Find("VisualRoot");
        Transform ruleRoot = root.transform.Find("RuleRoot");
        if (visualRoot == null || ruleRoot == null)
            return false;

        Transform collisionRoot = ruleRoot.Find("CollisionRoot");
        if (collisionRoot == null)
        {
            GameObject collisionRootGo = new GameObject("CollisionRoot");
            if (useUndo) Undo.RegisterCreatedObjectUndo(collisionRootGo, "创建网格碰撞根节点");
            collisionRoot = collisionRootGo.transform;
            collisionRoot.SetParent(ruleRoot, false);
        }

        // 清掉旧盒体和旧 MeshRoot，避免 Main_Collision_Box 回潮，也避免 __PhysicsMeshCollider_Custom 继续吃旧 custom mesh。
        RemoveChildIfExists(collisionRoot, "Main_Collision_Box", useUndo);
        RemoveChildIfExists(collisionRoot, "Sub_Collision_Box", useUndo);
        RemoveChildIfExists(collisionRoot, "Main_Collision_MeshRoot", useUndo);
        RemoveVisualPhysicsPollutionRoots(visualRoot, useUndo);

        GameObject meshRootGo = new GameObject("Main_Collision_MeshRoot");
        if (useUndo) Undo.RegisterCreatedObjectUndo(meshRootGo, "创建网格碰撞代理根节点");
        Transform meshRoot = meshRootGo.transform;
        meshRoot.SetParent(collisionRoot, false);
        meshRoot.localPosition = Vector3.zero;
        meshRoot.localRotation = Quaternion.identity;
        meshRoot.localScale = Vector3.one;
        SetLayerIfExists(meshRootGo, WorldLayerName);

        List<MeshFilter> sources = CollectCurrentVisualMeshFilters(visualRoot);
        int created = 0;
        StringBuilder log = new StringBuilder();
        log.AppendLine("[TD_PLACE_ACTIVE][MAP_WINDOW][MESH_FROM_VISUAL_DETAIL] 当前 VisualRoot 网格碰撞源：");

        for (int i = 0; i < sources.Count; i++)
        {
            MeshFilter mf = sources[i];
            if (mf == null || mf.sharedMesh == null)
                continue;

            GameObject proxy = new GameObject($"__PhysicsMeshCollider_{created:00}_{SanitizeName(mf.name)}");
            if (useUndo) Undo.RegisterCreatedObjectUndo(proxy, "创建 MeshCollider 代理");
            proxy.transform.SetParent(meshRoot, false);
            CopyWorldTransform(proxy.transform, mf.transform);
            SetLayerIfExists(proxy, WorldLayerName);

            MeshCollider mc = proxy.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.isTrigger = false;
            mc.enabled = true;

            log.AppendLine($"  #{created:00} {GetRelativePath(visualRoot, mf.transform)} mesh={mf.sharedMesh.name}");
            created++;
        }

        if (created == 0)
        {
            Debug.LogWarning("[TD_PLACE_ACTIVE][MAP_WINDOW][MESH_FROM_VISUAL_DETAIL] collisionMode=网格碰撞，但 VisualRoot 下没有找到可用 MeshFilter。", root);
        }
        else
        {
            Debug.Log(log.ToString(), root);
        }

        EditorUtility.SetDirty(meshRootGo);
        return true;
    }

    private static int GetCollisionModeIndex(Object definition)
    {
        if (definition == null)
            return -1;

        SerializedObject so = new SerializedObject(definition);
        SerializedProperty prop = so.FindProperty("collisionMode");
        return prop != null ? prop.enumValueIndex : -1;
    }

    private static List<MeshFilter> CollectCurrentVisualMeshFilters(Transform visualRoot)
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
            if (!IsValidVisualMeshFilter(visualRoot, mf))
                continue;
            result.Add(mf);
        }

        return result;
    }

    private static bool IsValidVisualMeshFilter(Transform visualRoot, MeshFilter mf)
    {
        if (visualRoot == null || mf == null)
            return false;

        Transform t = mf.transform;
        if (t == visualRoot)
            return false;

        bool underVisualRoot = false;
        Transform p = t;
        while (p != null)
        {
            if (p == visualRoot)
            {
                underVisualRoot = true;
                break;
            }
            p = p.parent;
        }
        if (!underVisualRoot)
            return false;

        p = t;
        while (p != null && p != visualRoot.parent)
        {
            string n = p.name;
            if (n.StartsWith("__Auto", System.StringComparison.Ordinal)) return false;
            if (n.StartsWith("__PhysicsMeshCollider", System.StringComparison.Ordinal)) return false;
            if (n == "PushableColliderRoot") return false;
            if (n == "PhysicsMeshColliderRoot") return false;
            if (n == "Main_Collision_MeshRoot") return false;
            if (n.IndexOf("Collision", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Proxy", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Trigger", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Stencil", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Mask", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Occluder", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

            // LOD1/LOD2 通常是重复低模；保留 LOD0，跳过更低 LOD，避免重复碰撞。
            if (n.IndexOf("LOD1", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("LOD2", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("LOD3", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

            if (p == visualRoot)
                break;
            p = p.parent;
        }

        return true;
    }

    private static Renderer[] CollectOcclusionRepairVisualRenderers(Transform visualRoot)
    {
        List<Renderer> result = new List<Renderer>();
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            if (!IsValidOcclusionRepairVisualRenderer(r))
                continue;
            result.Add(r);
        }
        return result.ToArray();
    }

    private static bool IsValidOcclusionRepairVisualRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        Transform t = renderer.transform;
        while (t != null)
        {
            string n = t.name;
            if (n.StartsWith("__Auto", System.StringComparison.Ordinal)) return false;
            if (n.StartsWith("__PhysicsMeshCollider", System.StringComparison.Ordinal)) return false;
            if (n.IndexOf("Collision", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Proxy", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Trigger", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Mask", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Occluder", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            t = t.parent;
        }

        return true;
    }

    private static bool ForceOcclusionTriggerRuntimeState(GameObject root)
    {
        if (root == null)
            return false;

        bool changed = false;
        Transform ruleRoot = root.transform.Find("RuleRoot");
        if (ruleRoot == null)
            return false;

        int decorationTriggerLayer = LayerMask.NameToLayer(DecorationTriggerLayerName);
        int occlusionMaskLayer = LayerMask.NameToLayer(OcclusionMaskLayerName);
        int unitBodyLayer = LayerMask.NameToLayer(UnitBodyLayerName);

        changed |= NormalizeTrigger(ruleRoot.Find("BackTrigger"), decorationTriggerLayer);
        changed |= NormalizeTrigger(ruleRoot.Find("FrontTrigger"), decorationTriggerLayer);

        Transform frontProxy = ruleRoot.Find("FrontOccluderRoot/FrontOccluderProxy_Box");
        if (frontProxy != null)
        {
            frontProxy.gameObject.SetActive(true);

            // 关键修复：FrontOccluderProxy_Box 下面真正写遮挡的是 __AutoFrontOccluderShapeClone
            // 以及它的各个 MeshRenderer 子节点。Unity 的 Layer 不会从父物体自动继承，
            // 所以这里必须递归设置，否则会出现“代理体开了、Scene 里发白，但角色不被遮挡”。
            changed |= SetLayerRecursivelyIfDifferent(frontProxy, occlusionMaskLayer);

            BoxCollider proxyBox = frontProxy.GetComponent<BoxCollider>();
            if (proxyBox != null && !proxyBox.enabled)
            {
                proxyBox.enabled = true;
                EditorUtility.SetDirty(proxyBox);
                changed = true;
            }
            EditorUtility.SetDirty(frontProxy.gameObject);
        }

        Transform visionBlocker = ruleRoot.Find("VisionBlockerRoot");
        if (visionBlocker != null)
        {
            visionBlocker.gameObject.SetActive(true);
            BoxCollider box = visionBlocker.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.enabled = true;
                box.isTrigger = true;
                EditorUtility.SetDirty(box);
            }
            EditorUtility.SetDirty(visionBlocker.gameObject);
        }

        if (unitBodyLayer >= 0 && decorationTriggerLayer >= 0)
            Physics.IgnoreLayerCollision(unitBodyLayer, decorationTriggerLayer, false);

        EnableBehaviourByTypeName(ruleRoot.Find("BackTrigger"), "SimpleDirectionalOccluder");
        EnableBehaviourByTypeName(ruleRoot.Find("FrontTrigger"), "SimpleDirectionalOccluder");

        return changed;
    }

    private static bool NormalizeTrigger(Transform trigger, int layer)
    {
        if (trigger == null)
            return false;

        bool changed = false;
        trigger.gameObject.SetActive(true);

        if (layer >= 0 && trigger.gameObject.layer != layer)
        {
            trigger.gameObject.layer = layer;
            changed = true;
        }

        BoxCollider box = trigger.GetComponent<BoxCollider>();
        if (box == null)
            box = trigger.gameObject.AddComponent<BoxCollider>();

        if (box != null)
        {
            if (!box.enabled) { box.enabled = true; changed = true; }
            if (!box.isTrigger) { box.isTrigger = true; changed = true; }
            EditorUtility.SetDirty(box);
        }

        EditorUtility.SetDirty(trigger.gameObject);
        return changed;
    }

    private static void EnableBehaviourByTypeName(Transform node, string typeName)
    {
        if (node == null || string.IsNullOrWhiteSpace(typeName))
            return;

        Behaviour[] behaviours = node.GetComponents<Behaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour b = behaviours[i];
            if (b == null)
                continue;
            if (b.GetType().Name == typeName)
            {
                b.enabled = true;
                EditorUtility.SetDirty(b);
            }
        }
    }

    private static void RemoveVisualPhysicsPollutionRoots(Transform visualRoot, bool useUndo)
    {
        if (visualRoot == null)
            return;

        string[] names = { "PushableColliderRoot", "PhysicsMeshColliderRoot", "Main_Collision_MeshRoot" };
        for (int i = 0; i < names.Length; i++)
            RemoveChildIfExists(visualRoot, names[i], useUndo);
    }

    private static void RemoveChildIfExists(Transform parent, string childName, bool useUndo)
    {
        if (parent == null || string.IsNullOrWhiteSpace(childName))
            return;

        Transform child = parent.Find(childName);
        if (child == null)
            return;

        if (useUndo)
            Undo.DestroyObjectImmediate(child.gameObject);
        else
            Object.DestroyImmediate(child.gameObject);
    }

    private static void SetLayerIfExists(GameObject go, string layerName)
    {
        if (go == null || string.IsNullOrWhiteSpace(layerName))
            return;
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
            go.layer = layer;
    }

    private static bool SetLayerRecursivelyIfDifferent(Transform root, int layer)
    {
        if (root == null || layer < 0)
            return false;

        bool changed = false;
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            Transform node = nodes[i];
            if (node == null || node.gameObject == null)
                continue;

            if (node.gameObject.layer == layer)
                continue;

            node.gameObject.layer = layer;
            EditorUtility.SetDirty(node.gameObject);
            changed = true;
        }

        return changed;
    }

    private static void CopyWorldTransform(Transform dst, Transform src)
    {
        if (dst == null || src == null)
            return;

        dst.position = src.position;
        dst.rotation = src.rotation;
        Vector3 parentLossy = dst.parent != null ? dst.parent.lossyScale : Vector3.one;
        dst.localScale = new Vector3(
            SafeDiv(src.lossyScale.x, parentLossy.x),
            SafeDiv(src.lossyScale.y, parentLossy.y),
            SafeDiv(src.lossyScale.z, parentLossy.z));
    }

    private static float SafeDiv(float a, float b)
    {
        return Mathf.Abs(b) < 0.0001f ? a : a / b;
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Mesh";

        string s = name.Replace(' ', '_').Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        if (s.Length > 48)
            s = s.Substring(0, 48);
        return s;
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return "-";

        List<string> parts = new List<string>();
        Transform t = target;
        while (t != null && t != root)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private readonly struct ProjectedOcclusionBounds
    {
        public readonly Vector3 center;
        public readonly Vector3 size;

        public ProjectedOcclusionBounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }
    }
}
