using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// V2: 修复 / 校对地形装饰物的视野遮挡节点。
/// 只处理 VisionBlockerRoot / Vision_Blocker_Box，不修改 BackTrigger / FrontTrigger / FrontOccluderProxy / MeshCollider。
/// 
/// V2 修正点：
/// 1. “修复当前场景全部”不再只依赖 Resources.FindObjectsOfTypeAll，改为三路收集：
///    FindObjectsOfType(true) + Resources.FindObjectsOfTypeAll + Scene Roots 扫描。
/// 2. 增加详细统计日志，能看出是没找到 Binder、definition 为空、blockVision=false，还是没找到 VisualRoot。
/// 3. VisualRoot 查找更稳：优先直接子节点，其次递归查找，避免旧实例结构稍微不一致时全部跳过。
/// 4. 选中修复与全场景修复共用同一套逻辑。
/// </summary>
public static class SkyPrisonTerrainDecorationVisionBlockerRepairUtility
{
    private const string MenuRoot = "Tools/Sky Prison/修复/";
    private const string RuleRootName = "RuleRoot";
    private const string VisionBlockerRootName = "VisionBlockerRoot";
    private const string VisionBlockerBoxName = "Vision_Blocker_Box";
    private const string VisualRootName = "VisualRoot";

    private struct RepairStats
    {
        public int scanned;
        public int repaired;
        public int disabled;
        public int skippedNull;
        public int skippedNoDefinition;
        public int skippedNoVisualRoot;
        public int skippedNoRenderer;
        public int skippedNoChange;
    }

    [MenuItem(MenuRoot + "修复当前场景全部地形装饰物视野遮挡节点", priority = 2300)]
    private static void RepairAllSceneTerrainDecorationVisionBlockers()
    {
        List<TerrainDecorationRuntimeBinder> binders = CollectAllSceneTerrainDecorationBinders();
        RepairStats stats = new RepairStats();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("修复当前场景全部地形装饰物视野遮挡节点");
        int undoGroup = Undo.GetCurrentGroup();

        for (int i = 0; i < binders.Count; i++)
            RepairOne(binders[i], ref stats);

        Undo.CollapseUndoOperations(undoGroup);
        MarkOpenScenesDirty();
        LogStats("当前场景全部", binders.Count, stats);
    }

    [MenuItem(MenuRoot + "修复选中地形装饰物视野遮挡节点", priority = 2301)]
    private static void RepairSelectedTerrainDecorationVisionBlockers()
    {
        List<TerrainDecorationRuntimeBinder> binders = CollectSelectedTerrainDecorationBinders();
        RepairStats stats = new RepairStats();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("修复选中地形装饰物视野遮挡节点");
        int undoGroup = Undo.GetCurrentGroup();

        for (int i = 0; i < binders.Count; i++)
            RepairOne(binders[i], ref stats);

        Undo.CollapseUndoOperations(undoGroup);
        MarkOpenScenesDirty();
        LogStats("选中对象", binders.Count, stats);
    }

    [MenuItem(MenuRoot + "修复选中地形装饰物视野遮挡节点", true)]
    private static bool ValidateRepairSelectedTerrainDecorationVisionBlockers()
    {
        return CollectSelectedTerrainDecorationBinders().Count > 0;
    }

    private static void RepairOne(TerrainDecorationRuntimeBinder binder, ref RepairStats stats)
    {
        stats.scanned++;

        if (binder == null || binder.gameObject == null)
        {
            stats.skippedNull++;
            return;
        }

        TerrainDecorationDefinition definition = binder.definition;
        if (definition == null)
        {
            stats.skippedNoDefinition++;
            return;
        }

        GameObject root = ResolveDecorationRoot(binder);
        if (root == null)
        {
            stats.skippedNull++;
            return;
        }

        if (!definition.blockVision)
        {
            if (DisableExistingVisionBlocker(root))
                stats.disabled++;
            else
                stats.skippedNoChange++;
            return;
        }

        Transform visualRoot = FindVisualRoot(root.transform);
        if (visualRoot == null)
        {
            Debug.LogWarning($"[SkyPrison][VisionBlockerRepair] 跳过：{root.name} 找不到 VisualRoot。", root);
            stats.skippedNoVisualRoot++;
            return;
        }

        if (RepairOrCreateVisionBlocker(root, visualRoot, definition))
            stats.repaired++;
        else
            stats.skippedNoRenderer++;
    }

    private static GameObject ResolveDecorationRoot(TerrainDecorationRuntimeBinder binder)
    {
        if (binder == null)
            return null;

        // 标准情况：Binder 挂在地形装饰物实例根节点。
        if (binder.transform.Find(VisualRootName) != null || binder.transform.Find(RuleRootName) != null)
            return binder.gameObject;

        // 兼容少数旧结构：往上找一个包含 VisualRoot / RuleRoot 的父节点。
        Transform t = binder.transform.parent;
        while (t != null)
        {
            if (t.Find(VisualRootName) != null || t.Find(RuleRootName) != null)
                return t.gameObject;
            t = t.parent;
        }

        return binder.gameObject;
    }

    private static bool DisableExistingVisionBlocker(GameObject root)
    {
        if (root == null)
            return false;

        Transform blockerBox = FindVisionBlockerBox(root.transform);
        if (blockerBox == null)
            return false;

        Undo.RecordObject(blockerBox.gameObject, "关闭视野遮挡盒");
        blockerBox.gameObject.SetActive(false);

        BoxCollider existingCollider = blockerBox.GetComponent<BoxCollider>();
        if (existingCollider != null)
        {
            Undo.RecordObject(existingCollider, "关闭视野遮挡碰撞盒");
            existingCollider.enabled = false;
            EditorUtility.SetDirty(existingCollider);
        }

        EditorUtility.SetDirty(blockerBox.gameObject);
        return true;
    }

    private static bool RepairOrCreateVisionBlocker(GameObject root, Transform visualRoot, TerrainDecorationDefinition definition)
    {
        if (root == null || visualRoot == null || definition == null)
            return false;

        Transform ruleRoot = root.transform.Find(RuleRootName);
        if (ruleRoot == null)
            ruleRoot = EnsureChild(root.transform, RuleRootName);

        Transform visionBlockerRoot = ruleRoot.Find(VisionBlockerRootName);
        if (visionBlockerRoot == null)
            visionBlockerRoot = EnsureChild(ruleRoot, VisionBlockerRootName);

        Transform blockerBox = visionBlockerRoot.Find(VisionBlockerBoxName);
        if (blockerBox == null)
            blockerBox = EnsureChild(visionBlockerRoot, VisionBlockerBoxName);

        Undo.RecordObject(visionBlockerRoot, "校对视野遮挡根节点");
        visionBlockerRoot.localPosition = Vector3.zero;
        visionBlockerRoot.localRotation = Quaternion.identity;
        visionBlockerRoot.localScale = Vector3.one;

        Undo.RecordObject(blockerBox, "校对视野遮挡盒节点");
        blockerBox.localPosition = Vector3.zero;
        blockerBox.localRotation = Quaternion.identity;
        blockerBox.localScale = Vector3.one;

        Undo.RecordObject(blockerBox.gameObject, "启用视野遮挡盒");
        blockerBox.gameObject.SetActive(true);

        // VisionManager 当前使用 Physics.RaycastAll(~0, QueryTriggerInteraction.Collide)，所以层级不强制参与过滤。
        // 这里保留原本节点层级，不碰 World3D / DecorationTrigger 等既有配置，避免误伤其他系统。

        BoxCollider collider = blockerBox.GetComponent<BoxCollider>();
        if (collider == null)
            collider = Undo.AddComponent<BoxCollider>(blockerBox.gameObject);
        else
            Undo.RecordObject(collider, "校对视野遮挡碰撞盒");

        collider.isTrigger = true;
        collider.enabled = true;

        Bounds localBounds;
        if (!TryCalculateVisualRendererBoundsInLocal(visualRoot, blockerBox, out localBounds))
        {
            Debug.LogWarning($"[SkyPrison][VisionBlockerRepair] 跳过：{root.name} 的 VisualRoot 下没有可用 Renderer。", root);
            return false;
        }

        collider.center = localBounds.center;
        collider.size = SanitizeVisionBlockerSize(localBounds.size);

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(visionBlockerRoot.gameObject);
        EditorUtility.SetDirty(blockerBox.gameObject);
        EditorUtility.SetDirty(collider);
        return true;
    }

    private static Transform EnsureChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(go, "创建视野遮挡节点");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private static Transform FindVisualRoot(Transform root)
    {
        if (root == null)
            return null;

        Transform direct = root.Find(VisualRootName);
        if (direct != null)
            return direct;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child.name == VisualRootName)
                return child;
        }

        return null;
    }

    private static Transform FindVisionBlockerBox(Transform root)
    {
        if (root == null)
            return null;

        Transform ruleRoot = root.Find(RuleRootName);
        Transform visionBlockerRoot = ruleRoot != null ? ruleRoot.Find(VisionBlockerRootName) : null;
        Transform blockerBox = visionBlockerRoot != null ? visionBlockerRoot.Find(VisionBlockerBoxName) : null;
        if (blockerBox != null)
            return blockerBox;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child.name == VisionBlockerBoxName)
                return child;
        }

        return null;
    }

    private static List<TerrainDecorationRuntimeBinder> CollectAllSceneTerrainDecorationBinders()
    {
        List<TerrainDecorationRuntimeBinder> result = new List<TerrainDecorationRuntimeBinder>();
        HashSet<TerrainDecorationRuntimeBinder> visited = new HashSet<TerrainDecorationRuntimeBinder>();

        TerrainDecorationRuntimeBinder[] activeAndInactive = Object.FindObjectsOfType<TerrainDecorationRuntimeBinder>(true);
        AddValidBinders(activeAndInactive, result, visited);

        TerrainDecorationRuntimeBinder[] allResources = Resources.FindObjectsOfTypeAll<TerrainDecorationRuntimeBinder>();
        AddValidBinders(allResources, result, visited);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                GameObject root = roots[r];
                if (root == null)
                    continue;

                TerrainDecorationRuntimeBinder[] childBinders = root.GetComponentsInChildren<TerrainDecorationRuntimeBinder>(true);
                AddValidBinders(childBinders, result, visited);
            }
        }

        return result;
    }

    private static List<TerrainDecorationRuntimeBinder> CollectSelectedTerrainDecorationBinders()
    {
        List<TerrainDecorationRuntimeBinder> result = new List<TerrainDecorationRuntimeBinder>();
        HashSet<TerrainDecorationRuntimeBinder> visited = new HashSet<TerrainDecorationRuntimeBinder>();

        GameObject[] selected = Selection.gameObjects;
        for (int i = 0; i < selected.Length; i++)
        {
            GameObject go = selected[i];
            if (go == null)
                continue;

            TerrainDecorationRuntimeBinder parentBinder = go.GetComponentInParent<TerrainDecorationRuntimeBinder>();
            if (IsValidSceneBinder(parentBinder) && visited.Add(parentBinder))
                result.Add(parentBinder);

            TerrainDecorationRuntimeBinder[] childBinders = go.GetComponentsInChildren<TerrainDecorationRuntimeBinder>(true);
            AddValidBinders(childBinders, result, visited);
        }

        return result;
    }

    private static void AddValidBinders(TerrainDecorationRuntimeBinder[] binders, List<TerrainDecorationRuntimeBinder> result, HashSet<TerrainDecorationRuntimeBinder> visited)
    {
        if (binders == null)
            return;

        for (int i = 0; i < binders.Length; i++)
        {
            TerrainDecorationRuntimeBinder binder = binders[i];
            if (!IsValidSceneBinder(binder))
                continue;

            if (visited.Add(binder))
                result.Add(binder);
        }
    }

    private static bool IsValidSceneBinder(TerrainDecorationRuntimeBinder binder)
    {
        if (binder == null || binder.gameObject == null)
            return false;

        if (!binder.gameObject.scene.IsValid() || !binder.gameObject.scene.isLoaded)
            return false;

        if (EditorUtility.IsPersistent(binder.gameObject))
            return false;

        return true;
    }

    private static bool TryCalculateVisualRendererBoundsInLocal(Transform visualRoot, Transform targetLocalSpace, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        if (visualRoot == null || targetLocalSpace == null)
            return false;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            Bounds rendererLocalBounds;
            Matrix4x4 rendererLocalToBlockerLocal;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                rendererLocalBounds = meshFilter.sharedMesh.bounds;
                rendererLocalToBlockerLocal = targetLocalSpace.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            }
            else
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    rendererLocalBounds = skinned.localBounds;
                    rendererLocalToBlockerLocal = targetLocalSpace.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                }
                else
                {
                    rendererLocalBounds = renderer.bounds;
                    rendererLocalToBlockerLocal = targetLocalSpace.worldToLocalMatrix;
                }
            }

            EncapsulateTransformedBounds(rendererLocalBounds, rendererLocalToBlockerLocal, ref bounds, ref hasAny);
        }

        return hasAny;
    }

    private static void EncapsulateTransformedBounds(Bounds sourceBounds, Matrix4x4 localToTarget, ref Bounds targetBounds, ref bool hasAny)
    {
        Vector3 min = sourceBounds.min;
        Vector3 max = sourceBounds.max;

        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z)), ref targetBounds, ref hasAny);
    }

    private static void EncapsulatePoint(Vector3 point, ref Bounds bounds, ref bool hasAny)
    {
        if (!hasAny)
        {
            bounds = new Bounds(point, Vector3.zero);
            hasAny = true;
            return;
        }

        bounds.Encapsulate(point);
    }

    private static Vector3 SanitizeVisionBlockerSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(0.05f, Mathf.Abs(size.x)),
            Mathf.Max(0.05f, Mathf.Abs(size.y)),
            Mathf.Max(0.05f, Mathf.Abs(size.z)));
    }

    private static void MarkOpenScenesDirty()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    private static void LogStats(string label, int collectedCount, RepairStats stats)
    {
        string message =
            $"[SkyPrison][VisionBlockerRepair][V2] {label}校对完成。" +
            $" collected={collectedCount}, scanned={stats.scanned}, repaired={stats.repaired}, disabled={stats.disabled}, " +
            $"noDefinition={stats.skippedNoDefinition}, noVisualRoot={stats.skippedNoVisualRoot}, noRenderer={stats.skippedNoRenderer}, noChange={stats.skippedNoChange}, null={stats.skippedNull}";

        Debug.Log(message);
        EditorUtility.DisplayDialog("Sky Prison 视野遮挡修复", message, "OK");
    }
}
