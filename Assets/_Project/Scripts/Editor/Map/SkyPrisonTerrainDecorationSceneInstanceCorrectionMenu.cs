using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sky Prison 地图实例遮挡矫正菜单。
/// 只处理 Scene 里的已摆放地图实例，不处理定义资源 / prefab asset。
///
/// 用途：
/// - 给已经摆在地图上的地形装饰物重新执行按钮同款矫正逻辑。
/// - 适合修复 FrontOccluderProxy_Model / Box / Trigger / Collision 等当前实例结构。
/// - 不依赖定义页面，不要求重新删除摆放。
///
/// V1 - 2026-05-22 - selected scene instance correction menu
/// </summary>
public static class SkyPrisonTerrainDecorationSceneInstanceCorrectionMenu
{
    private const string MenuRoot = "Tools/Sky Prison/Map/遮挡矫正/";

    [MenuItem(MenuRoot + "矫正选中的地图物体", priority = 2100)]
    public static void CorrectSelectedMapObjects()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("地图物体遮挡矫正", "请先在 Hierarchy 或 Scene 中选择一个或多个已摆放的地图物体实例。", "知道了");
            return;
        }

        List<GameObject> roots = CollectUniqueTerrainDecorationRoots(selectedObjects);
        if (roots.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "地图物体遮挡矫正",
                "当前选择中没有找到可矫正的地图物体实例。\n\n请选中已摆放实例本体，或其 VisualRoot / RuleRoot / BackTrigger / FrontOccluderRoot 子节点。",
                "知道了");
            return;
        }

        int changedCount = 0;
        int failedCount = 0;

        try
        {
            EditorUtility.DisplayProgressBar("地图物体遮挡矫正", "正在矫正选中实例...", 0f);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("矫正地图物体遮挡代理体");

            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                EditorUtility.DisplayProgressBar(
                    "地图物体遮挡矫正",
                    root.name,
                    roots.Count <= 1 ? 1f : (float)i / (roots.Count - 1));

                try
                {
                    Undo.RegisterFullObjectHierarchyUndo(root, "矫正地图物体遮挡代理体");
                    bool changed = SkyPrisonTerrainDecorationButtonCorrectionBridge.CorrectRuntimeInstanceLikeDefinitionButton(root, true);
                    if (changed)
                    {
                        changedCount++;
                        EditorUtility.SetDirty(root);
                        MarkOwningSceneDirty(root);
                    }
                }
                catch (System.Exception ex)
                {
                    failedCount++;
                    Debug.LogError($"[SkyPrisonTerrainDecorationSceneInstanceCorrectionMenu] 矫正失败: {GetPath(root != null ? root.transform : null)}\n{ex}");
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Physics.SyncTransforms();
            SceneView.RepaintAll();
        }

        Debug.Log($"[SkyPrisonTerrainDecorationSceneInstanceCorrectionMenu] 矫正完成。roots={roots.Count}, changed={changedCount}, failed={failedCount}");
    }

    [MenuItem(MenuRoot + "矫正选中的地图物体", true)]
    public static bool ValidateCorrectSelectedMapObjects()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem(MenuRoot + "矫正场景内全部硬遮挡物", priority = 2101)]
    public static void CorrectAllSceneHardOccluders()
    {
        if (!EditorUtility.DisplayDialog(
                "矫正场景内全部硬遮挡物",
                "这会扫描当前已加载场景中所有带 RuleRoot / BackTrigger / FrontOccluderRoot 的地图物体实例，并执行遮挡矫正。\n\n建议先保存场景。是否继续？",
                "继续",
                "取消"))
        {
            return;
        }

        List<GameObject> roots = CollectAllSceneTerrainDecorationRootsWithOcclusionNodes();
        if (roots.Count == 0)
        {
            EditorUtility.DisplayDialog("矫正场景内全部硬遮挡物", "当前场景没有找到带遮挡结构的地图物体实例。", "知道了");
            return;
        }

        int changedCount = 0;
        int failedCount = 0;

        try
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("批量矫正地图物体遮挡代理体");

            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                EditorUtility.DisplayProgressBar(
                    "批量矫正地图物体遮挡代理体",
                    $"{i + 1}/{roots.Count}  {root.name}",
                    roots.Count <= 1 ? 1f : (float)i / (roots.Count - 1));

                try
                {
                    Undo.RegisterFullObjectHierarchyUndo(root, "批量矫正地图物体遮挡代理体");
                    bool changed = SkyPrisonTerrainDecorationButtonCorrectionBridge.CorrectRuntimeInstanceLikeDefinitionButton(root, true);
                    if (changed)
                    {
                        changedCount++;
                        EditorUtility.SetDirty(root);
                        MarkOwningSceneDirty(root);
                    }
                }
                catch (System.Exception ex)
                {
                    failedCount++;
                    Debug.LogError($"[SkyPrisonTerrainDecorationSceneInstanceCorrectionMenu] 批量矫正失败: {GetPath(root.transform)}\n{ex}");
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Physics.SyncTransforms();
            SceneView.RepaintAll();
        }

        Debug.Log($"[SkyPrisonTerrainDecorationSceneInstanceCorrectionMenu] 批量矫正完成。roots={roots.Count}, changed={changedCount}, failed={failedCount}");
    }

    private static List<GameObject> CollectUniqueTerrainDecorationRoots(GameObject[] selectedObjects)
    {
        List<GameObject> roots = new List<GameObject>();
        HashSet<int> seen = new HashSet<int>();

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selected = selectedObjects[i];
            GameObject root = FindTerrainDecorationRootFromSelection(selected);
            if (root == null)
                continue;

            int id = root.GetInstanceID();
            if (seen.Add(id))
                roots.Add(root);
        }

        return roots;
    }

    private static GameObject FindTerrainDecorationRootFromSelection(GameObject selected)
    {
        if (selected == null)
            return null;

        if (EditorUtility.IsPersistent(selected))
            return null;

        Transform current = selected.transform;
        while (current != null)
        {
            if (LooksLikeTerrainDecorationRoot(current))
                return current.gameObject;

            current = current.parent;
        }

        return null;
    }

    private static bool LooksLikeTerrainDecorationRoot(Transform t)
    {
        if (t == null)
            return false;

        if (t.GetComponent<TerrainDecorationRuntimeBinder>() != null)
            return true;

        if (t.GetComponent<TerrainDecorationRuntimeApplier>() != null)
            return true;

        bool hasVisualRoot = t.Find("VisualRoot") != null;
        bool hasRuleRoot = t.Find("RuleRoot") != null;
        bool hasOcclusionNodes =
            t.Find("RuleRoot/BackTrigger") != null ||
            t.Find("RuleRoot/FrontTrigger") != null ||
            t.Find("RuleRoot/FrontOccluderRoot") != null;

        return hasVisualRoot && (hasRuleRoot || hasOcclusionNodes);
    }

    private static List<GameObject> CollectAllSceneTerrainDecorationRootsWithOcclusionNodes()
    {
        List<GameObject> roots = new List<GameObject>();
        HashSet<int> seen = new HashSet<int>();

        TerrainDecorationRuntimeBinder[] binders = Object.FindObjectsByType<TerrainDecorationRuntimeBinder>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < binders.Length; i++)
        {
            TerrainDecorationRuntimeBinder binder = binders[i];
            if (binder == null || binder.gameObject == null || EditorUtility.IsPersistent(binder.gameObject))
                continue;

            Transform root = binder.transform;
            if (!HasOcclusionNodes(root))
                continue;

            int id = root.gameObject.GetInstanceID();
            if (seen.Add(id))
                roots.Add(root.gameObject);
        }

        SkyPrisonTerrainDecorationFrontOccluderTrigger[] triggers = Object.FindObjectsByType<SkyPrisonTerrainDecorationFrontOccluderTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < triggers.Length; i++)
        {
            SkyPrisonTerrainDecorationFrontOccluderTrigger trigger = triggers[i];
            if (trigger == null || trigger.gameObject == null || EditorUtility.IsPersistent(trigger.gameObject))
                continue;

            GameObject root = FindTerrainDecorationRootFromSelection(trigger.gameObject);
            if (root == null)
                continue;

            int id = root.GetInstanceID();
            if (seen.Add(id))
                roots.Add(root);
        }

        return roots;
    }

    private static bool HasOcclusionNodes(Transform root)
    {
        if (root == null)
            return false;

        return root.Find("RuleRoot/BackTrigger") != null ||
               root.Find("RuleRoot/FrontTrigger") != null ||
               root.Find("RuleRoot/FrontOccluderRoot") != null ||
               root.Find("RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Box") != null ||
               root.Find("RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Model") != null;
    }

    private static void MarkOwningSceneDirty(GameObject root)
    {
        if (root == null)
            return;

        Scene scene = root.scene;
        if (scene.IsValid() && scene.isLoaded)
            EditorSceneManager.MarkSceneDirty(scene);
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "<null>";

        Stack<string> stack = new Stack<string>();
        Transform current = t;
        while (current != null)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", stack.ToArray());
    }
}
