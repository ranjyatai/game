using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 校正单位 / 孵化器的归属容器。
///
/// 放置工具现在会按身份分流（见 SkyPrisonUnitContainerLayout），所以新放的不会错。
/// 但在那之前放的全落在 WorldRoot/UnitRoot 这个空壳里，而且手动拖动、复制粘贴、
/// 从别的场景搬过来的对象也都可能跑偏——这类偏差不会报错，只有去数的时候才发现。
///
/// 这个工具做两件事：
///   1. 报告：每个单位现在在哪、按身份应该在哪
///   2. 校正：把跑偏的移回去（保持世界坐标），清掉空的历史容器
///
/// 先报告再校正，两个菜单分开——移动场景对象是有代价的操作，
/// 不该在你还没看过差异清单的时候就发生。
/// </summary>
public static class SkyPrisonUnitContainerAudit
{
    private const string LogPrefix = "[SkyPrison UnitContainer]";
    private const string MenuRoot = "天空囚笼/地图/单位归属容器/";

    private struct Mismatch
    {
        public Transform target;
        public string currentPath;
        public string expectedPath;
        public string label;
    }

    /// <summary>
    /// 场景里有绑定器、但没绑定义的单位。判断不出该去哪，所以不移动，但必须报出来。
    /// 手动把预制体拖进场景就是这种情况——PF_UnitRuntimeShell_Generic 的预制体默认值
    /// 就是空定义，只有走放置工具才会在实例上写 override。
    /// </summary>
    private static readonly List<Transform> unboundUnits = new List<Transform>();

    [MenuItem(MenuRoot + "1. 检查（只报告，不改动）", false, 132)]
    public static void Audit()
    {
        List<Mismatch> mismatches = CollectMismatches();
        ReportMismatches(mismatches, applied: false);
    }

    [MenuItem(MenuRoot + "2. 校正（移动到正确容器）", false, 133)]
    public static void Fix()
    {
        List<Mismatch> mismatches = CollectMismatches();
        if (mismatches.Count == 0)
        {
            Debug.Log($"{LogPrefix} 所有单位和孵化器都已在正确容器，无需校正。");
            CleanupStrayContainers();
            return;
        }

        foreach (Mismatch m in mismatches)
        {
            if (m.target == null)
                continue;

            Transform parent = GetOrCreateParent(m.expectedPath, m.target.gameObject.scene);
            if (parent == null)
                continue;

            // SetTransformParent 默认保持世界坐标，移动不会让对象在地图上跑位。
            Undo.SetTransformParent(m.target, parent, "校正单位归属容器");
        }

        ReportMismatches(mismatches, applied: true);
        CleanupStrayContainers();

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));

        Debug.Log($"{LogPrefix} 场景已标记为待保存——记得 Ctrl+S，否则改动只在内存里。");
    }

    private static List<Mismatch> CollectMismatches()
    {
        var result = new List<Mismatch>();
        unboundUnits.Clear();

        foreach (UnitDefinitionRuntimeBinder binder in CollectSceneInstances<UnitDefinitionRuntimeBinder>())
        {
            if (binder == null)
                continue;

            // 嵌套在另一个单位里的绑定器（挂件、召唤物挂点之类）不该被拽到顶层去。
            if (IsNestedInsideAnother(binder.transform, binder))
                continue;

            UnitDefinition definition = binder.UnitDefinitionAsset;
            if (definition == null)
            {
                // 没绑定义就判断不出该去哪。但绝不能静默跳过——
                // 手动拖进场景的预制体正是这种情况（PF_UnitRuntimeShell_Generic 的
                // 预制体默认值就是空），静默跳过会让人以为"审计说没问题"。
                unboundUnits.Add(binder.transform);
                continue;
            }

            string expected = SkyPrisonUnitContainerLayout.ResolveUnitParentPath(definition);
            string current = GetParentPath(binder.transform);
            if (current == expected)
                continue;

            result.Add(new Mismatch
            {
                target = binder.transform,
                currentPath = current,
                expectedPath = expected,
                label = $"{binder.name}（{definition.displayName} / {definition.characterIdentity}）"
            });
        }

        foreach (UnitSpawner spawner in CollectSceneInstances<UnitSpawner>())
        {
            if (spawner == null)
                continue;

            string expected = SkyPrisonUnitContainerLayout.SpawnerParentPath;
            string current = GetParentPath(spawner.transform);
            if (current == expected)
                continue;

            result.Add(new Mismatch
            {
                target = spawner.transform,
                currentPath = current,
                expectedPath = expected,
                label = $"{spawner.name}（单位孵化器）"
            });
        }

        return result;
    }

    private static void ReportMismatches(List<Mismatch> mismatches, bool applied)
    {
        ReportUnboundUnits();

        if (mismatches.Count == 0)
        {
            Debug.Log($"{LogPrefix} 检查完成：所有能判断归属的单位和孵化器都在正确容器。");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{LogPrefix} {(applied ? "已校正" : "发现")} {mismatches.Count} 个归属不正确的对象：");
        foreach (Mismatch m in mismatches)
        {
            sb.AppendLine($"  {m.label}");
            sb.AppendLine($"    现在：{m.currentPath}");
            sb.AppendLine($"    应在：{m.expectedPath}");
        }

        if (!applied)
            sb.AppendLine("跑「2. 校正」把它们移回去。移动保持世界坐标，地图上的位置不变。");

        Debug.Log(sb.ToString());
    }

    private static void ReportUnboundUnits()
    {
        if (unboundUnits.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine(
            $"{LogPrefix} 有 {unboundUnits.Count} 个单位没有绑定 UnitDefinition，判断不出归属，已跳过：");
        foreach (Transform t in unboundUnits)
        {
            if (t == null)
                continue;
            sb.AppendLine($"  {t.name}  现在：{GetParentPath(t)}");
        }
        sb.AppendLine("这些多半是手动把预制体拖进场景的。走放置工具放会自动绑定义，");
        sb.AppendLine("或者在 Inspector 里给它的 UnitDefinitionRuntimeBinder 指定定义，再跑一次检查。");

        Debug.LogWarning(sb.ToString());
    }

    /// <summary>
    /// 清掉空的历史容器。非空不删——里面还有东西说明有对象没归位，
    /// 这种时候删掉会连带销毁它们，比留着一个空壳严重得多。
    /// </summary>
    private static void CleanupStrayContainers()
    {
        foreach (string path in SkyPrisonUnitContainerLayout.StrayContainerPaths)
        {
            Transform stray = FindTransformByPath(path);
            if (stray == null)
                continue;

            if (stray.childCount > 0)
            {
                Debug.LogWarning(
                    $"{LogPrefix} 历史容器 {path} 里还有 {stray.childCount} 个对象，没有删除。" +
                    "先确认它们该去哪。", stray);
                continue;
            }

            Undo.DestroyObjectImmediate(stray.gameObject);
            Debug.Log($"{LogPrefix} 已删除空的历史容器 {path}。");
        }
    }

    private static bool IsNestedInsideAnother<T>(Transform t, T self) where T : Component
    {
        Transform p = t.parent;
        while (p != null)
        {
            if (p.GetComponent<T>() != null)
                return true;
            p = p.parent;
        }
        return false;
    }

    private static string GetParentPath(Transform t)
    {
        Transform parent = t.parent;
        if (parent == null)
            return "(场景根)";

        var parts = new List<string>();
        while (parent != null)
        {
            parts.Add(parent.name);
            parent = parent.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static List<T> CollectSceneInstances<T>() where T : Component
    {
        var found = new List<T>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return found;
    }

    private static Transform FindTransformByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string[] parts = path.Split('/');
        GameObject rootGo = GameObject.Find(parts[0]);
        if (rootGo == null)
            return null;

        Transform current = rootGo.transform;
        for (int i = 1; i < parts.Length && current != null; i++)
            current = current.Find(parts[i]);

        return current;
    }

    private static Transform GetOrCreateParent(string path, Scene scene)
    {
        Transform existing = FindTransformByPath(path);
        if (existing != null)
            return existing;

        string[] parts = path.Split('/');
        Transform current = null;
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]))
                continue;

            if (i == 0)
            {
                GameObject root = GameObject.Find(parts[i]);
                if (root == null)
                {
                    root = new GameObject(parts[i]);
                    if (scene.IsValid())
                        SceneManager.MoveGameObjectToScene(root, scene);
                    Undo.RegisterCreatedObjectUndo(root, "创建单位容器");
                }
                current = root.transform;
            }
            else
            {
                Transform child = current.Find(parts[i]);
                if (child == null)
                {
                    GameObject go = new GameObject(parts[i]);
                    Undo.RegisterCreatedObjectUndo(go, "创建单位容器");
                    go.transform.SetParent(current, false);
                    child = go.transform;
                }
                current = child;
            }
        }

        return current;
    }
}
