using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 查场景里的地形装饰物到底有没有真正可用的前景遮挡代理。
///
/// 为什么需要这个：定义上把 frontOccluderProxyMode 设成 ModelProxy 之后，结构是
/// Builder 在「放置那一刻」生成的。改定义不会回溯已经摆好的实例——它们保持着摆放
/// 当时的结构。所以「定义是对的」和「地图上是对的」是两件事，必须分开验证。
///
/// 而且光看 Hierarchy 里有没有 FrontOccluderRoot 不够：那个节点默认就是关的
/// （运行时由 FrontOccluderTrigger 开关），灰着是正常的。真正要看的是它底下有没有
/// 带 Renderer 的代理体。
/// </summary>
public static class SkyPrisonFrontOccluderAudit
{
    private const string LogPrefix = "[SkyPrison FrontOccluder]";
    private const string MenuRoot = "天空囚笼/遮挡/";

    private const string RuleRootName = "RuleRoot";
    private const string FrontOccluderRootName = "FrontOccluderRoot";

    /// <summary>
    /// 给「要遮挡但没有碰撞体」的定义打开遮挡探测碰撞体。
    ///
    /// 这个组合（occlusionMode≠None 且 collisionMode=None）在当前实现下必然不遮挡：
    /// 遮挡判定是射线找遮挡物自己的碰撞体，没有碰撞体就没有靶子，射线只会打到地形，
    /// 而地形属于 OtherHit、明确不作为遮挡依据。
    ///
    /// 逐个去定义里点一遍太容易漏——项目里现在就有三个定义同时踩了这个坑，
    /// 而且表现完全一样：配置看着全对，就是不遮挡。
    /// </summary>
    [MenuItem(MenuRoot + "给需要的定义打开遮挡探测碰撞体", false, 139)]
    public static void EnableProbeWhereNeeded()
    {
        string[] guids = AssetDatabase.FindAssets("t:TerrainDecorationDefinition");
        var changed = new List<string>();
        int alreadyOn = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(path);
            if (def == null)
                continue;

            bool needsProbe =
                def.occlusionMode != TerrainDecorationOcclusionMode.None &&
                def.collisionMode == TerrainDecorationCollisionMode.None;

            if (!needsProbe)
                continue;

            if (def.generateOccluderProbeCollider)
            {
                alreadyOn++;
                continue;
            }

            Undo.RecordObject(def, "Enable Occluder Probe");
            def.generateOccluderProbeCollider = true;
            EditorUtility.SetDirty(def);
            changed.Add(string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName);
        }

        AssetDatabase.SaveAssets();

        if (changed.Count == 0)
        {
            Debug.Log($"{LogPrefix} 没有需要打开的定义（已开启 {alreadyOn} 个）。");
            return;
        }

        Debug.Log(
            $"{LogPrefix} 已给 {changed.Count} 个定义打开遮挡探测碰撞体：{string.Join("、", changed)}\n" +
            "定义改了不会回溯已放置的实例，接着跑：\n" +
            "  Tools/Sky Prison/Debug/Occlusion Structure Audit/3. Rebuild All Scene Decoration Roots Through Builder\n" +
            "跑完 Ctrl+S。");
    }

    [MenuItem(MenuRoot + "检查场景里的遮挡代理", false, 140)]
    public static void Audit()
    {
        List<TerrainDecorationRuntimeBinder> binders = CollectSceneInstances<TerrainDecorationRuntimeBinder>();
        if (binders.Count == 0)
        {
            Debug.Log($"{LogPrefix} 场景里没有地形装饰物实例。");
            return;
        }

        // 按定义归类统计——同一个定义的实例要么全好要么全坏，逐个列出来只会刷屏。
        var stats = new Dictionary<string, (int total, int missing, TerrainDecorationRuntimeBinder sample)>();
        int noDefinition = 0;

        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null)
                continue;

            TerrainDecorationDefinition def = binder.definition;
            if (def == null)
            {
                noDefinition++;
                continue;
            }

            // 定义上就说不要代理的，不算问题。
            if (def.frontOccluderProxyMode == TerrainDecorationFrontOccluderProxyMode.None)
                continue;

            string key = string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName;
            stats.TryGetValue(key, out var entry);

            bool ok = HasUsableProxy(binder.transform);
            stats[key] = (
                entry.total + 1,
                entry.missing + (ok ? 0 : 1),
                entry.sample != null ? entry.sample : binder);
        }

        var sb = new StringBuilder();
        int brokenKinds = 0;

        foreach (var kv in stats)
        {
            bool broken = kv.Value.missing > 0;
            if (broken)
                brokenKinds++;

            sb.AppendLine(
                $"  {(broken ? "✗" : "✓")} {kv.Key}：{kv.Value.total} 个实例，" +
                $"{kv.Value.missing} 个缺代理体");
        }

        if (noDefinition > 0)
            sb.AppendLine($"  另有 {noDefinition} 个实例没绑定义，判断不了，已跳过。");

        string head = brokenKinds == 0
            ? $"{LogPrefix} 检查完成：所有需要遮挡代理的装饰物都有可用代理体。"
            : $"{LogPrefix} 检查完成：{brokenKinds} 种装饰物缺代理体。\n" +
              $"修法是重新走一次 Builder：\n" +
              $"  Tools/Sky Prison/Debug/Occlusion Structure Audit/3. Rebuild All Scene Decoration Roots Through Builder\n" +
              $"跑完记得 Ctrl+S。";

        Debug.Log(head + "\n" + sb);
    }

    /// <summary>
    /// 有没有真正能用的代理体。
    ///
    /// 判据是「FrontOccluderRoot 底下存在带 Renderer 的节点」，不是「FrontOccluderRoot
    /// 存在」——空壳节点在 Hierarchy 里看着和有内容的一模一样，只看节点在不在会把
    /// 空壳判成正常。这正是这轮反复踩的那类错：拿一个存在性当成有效性。
    ///
    /// 用 includeInactive：FrontOccluderRoot 默认关闭，不带这个参数一个都找不到。
    /// </summary>
    private static bool HasUsableProxy(Transform decorationRoot)
    {
        Transform ruleRoot = decorationRoot.Find(RuleRootName);
        if (ruleRoot == null)
            return false;

        Transform occluderRoot = ruleRoot.Find(FrontOccluderRootName);
        if (occluderRoot == null)
            return false;

        Renderer[] renderers = occluderRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                return true;

        return false;
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
}
