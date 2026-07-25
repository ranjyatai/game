using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SkyPrisonTerrainDecorationSceneCleanupUtility
{
    private static readonly string[] PreviewNameKeywords =
    {
        "__TerrainDecorationPreview__",
        "__TerrainDecorationFastBoundsPreview__",
        "TerrainDecorationPreview",
        "TD_Preview",
        "PlacementPreview"
    };

    private static readonly string[] StandardNodeNames =
    {
        "Main_Collision_Box",
        "Vision_Blocker_Box",
        "FrontOccluderProxy_Box",
        "OutlineMaskProxy_T_Box",
        "SubOutlineMaskProxy_01",
        "ShadowCaster_01",
        "__AutoStencilClone"
    };

    private static readonly string[] StandardRootNames =
    {
        "CollisionRoot",
        "VisionBlockerRoot",
        "FrontOccluderRoot",
        "OutlineMaskProxyRoot",
        "ShadowCasterRoot",
        "StencilWriterRoot",
        "RuleRoot",
        "EditorGizmoRoot"
    };

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/选择残留预览体")]
    public static void SelectPreviewResidues()
    {
        SelectObjects(FindPreviewResidues(), "残留预览体");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/删除残留预览体")]
    public static void DeletePreviewResidues()
    {
        DeleteObjectsWithConfirm(FindPreviewResidues(), "残留预览体");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/选择非托管标准节点残留")]
    public static void SelectUnmanagedStandardNodeResidues()
    {
        SelectObjects(FindUnmanagedStandardNodeResidues(), "非托管标准节点残留");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/删除非托管标准节点残留")]
    public static void DeleteUnmanagedStandardNodeResidues()
    {
        DeleteObjectsWithConfirm(FindUnmanagedStandardNodeResidues(), "非托管标准节点残留");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/选择疑似残留碰撞盒")]
    public static void SelectSuspiciousCollisionBoxes()
    {
        SelectObjects(FindSuspiciousCollisionBoxes(), "疑似残留碰撞盒");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/删除疑似残留碰撞盒")]
    public static void DeleteSuspiciousCollisionBoxes()
    {
        DeleteObjectsWithConfirm(FindSuspiciousCollisionBoxes(), "疑似残留碰撞盒");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/选择全部疑似残留")]
    public static void SelectAllTerrainDecorationResidues()
    {
        HashSet<GameObject> set = new HashSet<GameObject>();
        foreach (GameObject go in FindPreviewResidues()) set.Add(go);
        foreach (GameObject go in FindUnmanagedStandardNodeResidues()) set.Add(go);
        foreach (GameObject go in FindSuspiciousCollisionBoxes()) set.Add(go);
        SelectObjects(set.ToList(), "全部疑似残留");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/删除当前选中的疑似残留")]
    public static void DeleteSelectedResiduesOnly()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("删除残留", "当前没有选择任何对象。", "知道了");
            return;
        }

        List<GameObject> deletable = selected
            .Where(IsSceneObject)
            .Where(IsTerrainDecorationResidueCandidate)
            .Distinct()
            .ToList();

        if (deletable.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "删除残留",
                "当前选择里没有符合规则的地形装饰物残留。\n\n为了安全，这个命令只会删除 Preview 或非托管标准节点。",
                "知道了");
            return;
        }

        DeleteObjectsWithConfirm(deletable, "当前选中的疑似残留");
    }

    // 兼容旧版放置工具窗口调用：静默清理 Preview 残留，不弹窗。
    public static void CleanupPreviewObjectsSilent()
    {
        DeleteObjectsImmediateNoDialog(FindPreviewResidues(), "残留预览体");
    }

    // 兼容旧版放置工具窗口调用。showDialog=true 时弹窗确认，false 时静默清理 Preview。
    public static void CleanupPreviewObjects(bool showDialog)
    {
        if (showDialog)
            DeleteObjectsWithConfirm(FindPreviewResidues(), "残留预览体");
        else
            CleanupPreviewObjectsSilent();
    }

    // 兼容旧版放置工具窗口调用：只选择，不删除。
    public static void SelectSuspiciousOrphanCollisionBoxesMenu()
    {
        SelectAllTerrainDecorationResidues();
    }

    public static List<GameObject> FindPreviewResidues()
    {
        return FindAllSceneObjects()
            .Where(go => PreviewNameKeywords.Any(k => go.name.Contains(k)))
            .ToList();
    }

    public static List<GameObject> FindSuspiciousCollisionBoxes()
    {
        return FindAllSceneObjects()
            .Where(go => go.name == "Main_Collision_Box")
            .Where(go => !IsInsideManagedTerrainDecoration(go))
            .ToList();
    }

    public static List<GameObject> FindUnmanagedStandardNodeResidues()
    {
        return FindAllSceneObjects()
            .Where(go => IsStandardGeneratedNode(go.name))
            .Where(go => !IsInsideManagedTerrainDecoration(go))
            .ToList();
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/选择托管但无模型的赃碰撞实例")]
    public static void SelectManagedCollisionOnlyDecorations()
    {
        SelectObjects(FindManagedCollisionOnlyDecorations(), "托管但无模型的赃碰撞实例");
    }

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/删除托管但无模型的赃碰撞实例")]
    public static void DeleteManagedCollisionOnlyDecorations()
    {
        DeleteObjectsWithConfirm(FindManagedCollisionOnlyDecorations(), "托管但无模型的赃碰撞实例");
    }

    public static List<GameObject> FindManagedCollisionOnlyDecorations()
    {
        return FindAllSceneObjects()
            .Select(go => go.GetComponent<TerrainDecorationRuntimeBinder>())
            .Where(b => b != null)
            .Select(b => b.gameObject)
            .Distinct()
            .Where(IsManagedDecorationWithNoVisualRenderer)
            .ToList();
    }

    private static bool IsTerrainDecorationResidueCandidate(GameObject go)
    {
        if (go == null || !IsSceneObject(go))
            return false;

        if (PreviewNameKeywords.Any(k => go.name.Contains(k)))
            return true;

        if (IsStandardGeneratedNode(go.name) && !IsInsideManagedTerrainDecoration(go))
            return true;

        if (go.GetComponent<TerrainDecorationRuntimeBinder>() != null && IsManagedDecorationWithNoVisualRenderer(go))
            return true;

        return false;
    }

    private static bool IsStandardGeneratedNode(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (StandardNodeNames.Contains(name))
            return true;

        if (StandardRootNames.Contains(name))
            return true;

        return false;
    }

    private static bool IsInsideManagedTerrainDecoration(GameObject go)
    {
        if (go == null)
            return false;

        return go.GetComponentInParent<TerrainDecorationRuntimeBinder>(true) != null;
    }

    private static bool IsManagedDecorationWithNoVisualRenderer(GameObject root)
    {
        if (root == null || root.GetComponent<TerrainDecorationRuntimeBinder>() == null)
            return false;

        Transform visualRoot = root.transform.Find("VisualRoot");
        if (visualRoot != null)
        {
            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsRealVisualRenderer(renderers[i], root.transform))
                    return false;
            }
        }

        Renderer rootRenderer = root.GetComponent<Renderer>();
        if (IsRealVisualRenderer(rootRenderer, root.transform))
            return false;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        return colliders != null && colliders.Length > 0;
    }

    private static bool IsRealVisualRenderer(Renderer renderer, Transform root)
    {
        if (renderer == null || root == null)
            return false;

        string path = GetTransformPath(renderer.transform, root);
        if (path.Contains("RuleRoot") || path.Contains("CollisionRoot") || path.Contains("VisionBlockerRoot") ||
            path.Contains("FrontOccluder") || path.Contains("OutlineMask") || path.Contains("StencilWriter") ||
            path.Contains("ShadowCaster") || path.Contains("EditorGizmo") || path.Contains("FXRoot") || path.Contains("MossRoot"))
            return false;

        return true;
    }

    private static string GetTransformPath(Transform target, Transform stopAt)
    {
        if (target == null)
            return string.Empty;
        List<string> parts = new List<string>();
        Transform current = target;
        while (current != null && current != stopAt)
        {
            parts.Add(current.name);
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static List<GameObject> FindAllSceneObjects()
    {
        List<GameObject> result = new List<GameObject>();
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();

        foreach (GameObject go in all)
        {
            if (!IsSceneObject(go))
                continue;

            // 排除 Unity 内部临时对象。
            if ((go.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave)
                continue;

            result.Add(go);
        }

        return result;
    }

    private static bool IsSceneObject(GameObject go)
    {
        if (go == null)
            return false;

        Scene scene = go.scene;
        return scene.IsValid() && scene.isLoaded;
    }

    private static void SelectObjects(List<GameObject> objects, string label)
    {
        objects = objects
            .Where(x => x != null)
            .Distinct()
            .OrderBy(GetHierarchyPath)
            .ToList();

        Selection.objects = objects.Cast<Object>().ToArray();

        if (objects.Count > 0)
            EditorGUIUtility.PingObject(objects[0]);

        Debug.Log($"[TerrainDecorationCleanup] 选中 {objects.Count} 个{label}。");
    }

    private static void DeleteObjectsWithConfirm(List<GameObject> objects, string label)
    {
        objects = objects
            .Where(x => x != null)
            .Distinct()
            .OrderByDescending(GetHierarchyDepth)
            .ToList();

        if (objects.Count == 0)
        {
            EditorUtility.DisplayDialog("清理完成", $"没有找到{label}。", "知道了");
            return;
        }

        string preview = string.Join("\n", objects.Take(12).Select(GetHierarchyPath));
        if (objects.Count > 12)
            preview += $"\n... 以及另外 {objects.Count - 12} 个";

        bool ok = EditorUtility.DisplayDialog(
            "确认删除",
            $"将删除 {objects.Count} 个{label}：\n\n{preview}\n\n建议先使用“选择”命令确认位置。是否继续？",
            "删除",
            "取消");

        if (!ok)
            return;

        foreach (GameObject go in objects)
        {
            if (go == null)
                continue;

            Undo.DestroyObjectImmediate(go);
        }

        Debug.Log($"[TerrainDecorationCleanup] 已删除 {objects.Count} 个{label}。");
    }

    private static void DeleteObjectsImmediateNoDialog(List<GameObject> objects, string label)
    {
        objects = objects
            .Where(x => x != null)
            .Distinct()
            .OrderByDescending(GetHierarchyDepth)
            .ToList();

        if (objects.Count == 0)
            return;

        foreach (GameObject go in objects)
        {
            if (go == null)
                continue;

            Undo.DestroyObjectImmediate(go);
        }

        Debug.Log($"[TerrainDecorationCleanup] 已静默删除 {objects.Count} 个{label}。");
    }

    private static int GetHierarchyDepth(GameObject go)
    {
        int depth = 0;
        Transform t = go != null ? go.transform : null;
        while (t != null)
        {
            depth++;
            t = t.parent;
        }

        return depth;
    }

    private static string GetHierarchyPath(GameObject go)
    {
        if (go == null)
            return "<null>";

        Stack<string> names = new Stack<string>();
        Transform t = go.transform;
        while (t != null)
        {
            names.Push(t.name);
            t = t.parent;
        }

        return string.Join("/", names);
    }
}
