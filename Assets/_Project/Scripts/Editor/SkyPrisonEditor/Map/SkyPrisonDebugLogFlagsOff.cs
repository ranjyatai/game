using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 关掉场景和预制体里所有「逐帧打日志」的调试开关。
///
/// 排查问题时打开这类开关很自然，但它们是序列化字段——一旦保存就跟着场景/预制体
/// 进 Build，然后在正式运行时逐帧 Debug.Log + 字符串插值，直接变成 GC 和卡顿来源。
/// 性能报告里 1 分钟 1749 次 GC、ScriptRunBehaviourLateUpdate 40.4ms 就是这么来的。
///
/// 只关名字里带 log 的布尔开关，不碰 debugDrawGizmos / debugMode 这类只影响
/// 编辑器可视化、不产生运行时开销的字段。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonDebugLogFlagsOff
{
    private const string LogPrefix = "[SkyPrison DebugFlags]";
    private const string VersionKey = "SkyPrison.DebugLogFlagsOff.Version";
    // v2：加入 ExtraLogFlags（debugHorizontalAttribution）后需要重跑一次。
    private const int Version = 2;

    static SkyPrisonDebugLogFlagsOff()
    {
        EditorApplication.delayCall += RunOnce;
    }

    /// <summary>
    /// 只跑一次。这个扫描要遍历全项目预制体，开销不小，不适合每次重载都跑。
    /// 以后又有人打开了忘关，手动跑一次菜单即可。
    /// </summary>
    private static void RunOnce()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunOnce;
            return;
        }

        EditorPrefs.SetInt(VersionKey, Version);
        Run();
    }

    /// <summary>
    /// 认定为「逐帧日志开关」的字段名。只匹配布尔字段，且名字里必须含 log——
    /// debugMode / debugDrawXxx 这些是可视化开关，关掉反而会让人以为工具坏了。
    /// </summary>
    /// <summary>
    /// 名字不符合「含 log」约定、但确实会打日志的开关，显式列出来。
    /// 名字规则覆盖不到所有情况——debugHorizontalAttribution 就是含 debug 不含 log，
    /// 上一版漏掉了，在一次十小时浸测里打了 600 组移动归因日志才被发现。
    /// </summary>
    private static readonly HashSet<string> ExtraLogFlags = new HashSet<string>
    {
        "debugHorizontalAttribution",
    };

    private static bool IsLogFlag(string name)
    {
        if (ExtraLogFlags.Contains(name))
            return true;

        string n = name.ToLowerInvariant();
        if (!n.Contains("log"))
            return false;
        return n.Contains("debug") || n.Contains("verbose") || n.Contains("trace");
    }

    [MenuItem("Tools/Sky Prison/Diagnostics/关闭所有逐帧调试日志开关")]
    public static void Run()
    {
        var perType = new Dictionary<string, int>();
        int changedObjects = 0;

        foreach (MonoBehaviour mb in CollectSceneComponents())
            changedObjects += Process(mb, perType, markScene: true);

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null)
                continue;

            foreach (MonoBehaviour mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                changedObjects += Process(mb, perType, markScene: false);
        }

        AssetDatabase.SaveAssets();

        if (changedObjects == 0)
        {
            Debug.Log($"{LogPrefix} 没有发现打开的逐帧日志开关。");
            return;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
        bool saved = EditorSceneManager.SaveOpenScenes();

        string detail = string.Join("\n  ",
            perType.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));

        Debug.Log(
            $"{LogPrefix} 关掉了 {changedObjects} 处逐帧日志开关。\n  {detail}\n" +
            $"场景保存：{(saved ? "成功" : "失败或被取消")}。");
    }

    private static int Process(MonoBehaviour mb, Dictionary<string, int> perType, bool markScene)
    {
        if (mb == null)
            return 0;

        var so = new SerializedObject(mb);
        SerializedProperty p = so.GetIterator();
        int changed = 0;

        while (p.NextVisible(true))
        {
            if (p.propertyType != SerializedPropertyType.Boolean)
                continue;
            if (!p.boolValue || !IsLogFlag(p.name))
                continue;

            p.boolValue = false;
            changed++;
        }

        if (changed == 0)
            return 0;

        so.ApplyModifiedPropertiesWithoutUndo();

        string typeName = mb.GetType().Name;
        perType.TryGetValue(typeName, out int n);
        perType[typeName] = n + changed;

        if (markScene)
            EditorSceneManager.MarkSceneDirty(mb.gameObject.scene);
        else
            EditorUtility.SetDirty(mb);

        return changed;
    }

    private static List<MonoBehaviour> CollectSceneComponents()
    {
        var found = new List<MonoBehaviour>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<MonoBehaviour>(true));
        }
        return found;
    }
}
