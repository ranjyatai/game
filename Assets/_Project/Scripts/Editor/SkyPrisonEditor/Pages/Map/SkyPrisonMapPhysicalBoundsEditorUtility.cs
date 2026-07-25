#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SkyPrisonMapPhysicalBoundsEditorUtility
{
    private const string WorldLogicName = "WorldLogic";
    private const string MapBoundaryName = "MapBoundary";

    public static GameObject SyncPhysicalBoundsToCurrentScene(MapDefinition map)
    {
        if (map == null)
        {
            EditorUtility.DisplayDialog("物理地图边界", "未选择地图定义。", "确定");
            return null;
        }

        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("物理地图边界", "当前 Scene 无效。", "确定");
            return null;
        }

        Transform mapBoundaryParent = FindMapBoundaryParentInScene(scene);
        if (mapBoundaryParent == null)
        {
            EditorUtility.DisplayDialog(
                "物理地图边界",
                "当前 Scene 中找不到 WorldLogic/MapBoundary。请先补齐地图基础节点，再同步物理边界。",
                "确定");
            return null;
        }

        GameObject root = FindRootInScene(scene);
        if (root == null)
        {
            root = new GameObject(SkyPrisonMapPhysicalBounds.GeneratedRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Map Physical Bounds");
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.SetTransformParent(root.transform, mapBoundaryParent, "Parent Map Physical Bounds");
        }
        else
        {
            Undo.RegisterFullObjectHierarchyUndo(root, "Sync Map Physical Bounds");
            if (root.transform.parent != mapBoundaryParent)
                Undo.SetTransformParent(root.transform, mapBoundaryParent, "Move Map Physical Bounds Under MapBoundary");
        }

        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        SkyPrisonMapPhysicalBounds.Rebuild(root, map);

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(mapBoundaryParent.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);

        Selection.activeObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log($"[MapPhysicalBounds] 已同步物理边界到 WorldLogic/MapBoundary：{map.name}", root);
        return root;
    }

    public static void ClearPhysicalBoundsInCurrentScene()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject root = FindRootInScene(scene);
        if (root == null)
        {
            Debug.Log("[MapPhysicalBounds] 当前 Scene 没有物理边界根节点。");
            return;
        }

        Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[MapPhysicalBounds] 已删除当前 Scene 的物理地图边界。");
    }

    private static Transform FindMapBoundaryParentInScene(Scene scene)
    {
        if (!scene.IsValid())
            return null;

        GameObject worldLogic = FindGameObjectByNameInScene(scene, WorldLogicName);
        if (worldLogic == null)
            return null;

        Transform mapBoundary = worldLogic.transform.Find(MapBoundaryName);
        if (mapBoundary != null)
            return mapBoundary;

        // 兼容 MapBoundary 不是 WorldLogic 直接子节点的旧结构：只在 WorldLogic 下面递归找，不去误抓其他系统里的同名节点。
        return FindChildRecursive(worldLogic.transform, MapBoundaryName);
    }

    private static GameObject FindRootInScene(Scene scene)
    {
        if (!scene.IsValid())
            return null;

        return FindGameObjectByNameInScene(scene, SkyPrisonMapPhysicalBounds.GeneratedRootName);
    }

    private static GameObject FindGameObjectByNameInScene(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform matched = FindChildRecursiveIncludingSelf(roots[i].transform, objectName);
            if (matched != null)
                return matched.gameObject;
        }

        return null;
    }

    private static Transform FindChildRecursiveIncludingSelf(Transform root, string objectName)
    {
        if (root == null)
            return null;

        if (root.name == objectName)
            return root;

        return FindChildRecursive(root, objectName);
    }

    private static Transform FindChildRecursive(Transform root, string objectName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            if (child.name == objectName)
                return child;

            Transform matched = FindChildRecursive(child, objectName);
            if (matched != null)
                return matched;
        }

        return null;
    }
}
#endif
