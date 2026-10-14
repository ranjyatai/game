using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家初始点全项目只能有一个（天空囚笼只有一个出生点）。三道保护：
/// 1. 摆放工具：项目里（所有场景，不只是当前打开的）已经有初始点就不让再摆，并指给你它在哪。
/// 2. 复制 / 粘贴：当前打开的场景里一旦出现第二个，自动删掉新出现的那个并提示（Ctrl+D 是最常见的重复来源）。
/// 3. 保存场景：保存时发现别的场景里还有一个，报错提示。
/// 另有菜单「天空囚笼/检查/玩家初始点唯一性」随时手动检查全项目。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonPlayerStartPointGuard
{
    public struct Entry
    {
        public string scenePath;
        public int count;
        public string objectName;   // 只有已加载的场景能取到
    }

    private static int s_knownId;
    private static bool s_busy;

    static SkyPrisonPlayerStartPointGuard()
    {
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorSceneManager.sceneSaved += _ => InvalidateDiskCache();
        EditorSceneManager.sceneOpened += (_, __) => InvalidateDiskCache();
        EditorSceneManager.newSceneCreated += (_, __, ___) => InvalidateDiskCache();
    }

    // ── 查询 ─────────────────────────────────────────────────────

    private static string ScriptGuid()
    {
        foreach (string g in AssetDatabase.FindAssets("SkyPrisonPlayerStartPoint t:MonoScript"))
        {
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
            if (ms != null && ms.GetClass() == typeof(SkyPrisonPlayerStartPoint))
                return g;
        }
        return null;
    }

    // 没打开的场景要读文件才知道有没有初始点（全项目 100 多个场景，约 1.5 秒），
    // 摆放页每次重绘都会问，所以磁盘这部分缓存起来；保存 / 打开 / 新建场景后失效。
    private static List<Entry> s_diskCache;
    private static double s_diskCacheAt;
    private static string s_diskCacheOpenKey;

    public static void InvalidateDiskCache()
    {
        s_diskCache = null;
    }

    private static List<Entry> LiveEntries(HashSet<string> openPaths)
    {
        var result = new List<Entry>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene sc = SceneManager.GetSceneAt(i);
            if (!sc.isLoaded)
                continue;
            openPaths.Add(sc.path);
            int n = 0;
            string first = null;
            foreach (var root in sc.GetRootGameObjects())
                foreach (var sp in root.GetComponentsInChildren<SkyPrisonPlayerStartPoint>(true))
                {
                    n++;
                    if (first == null) first = sp.name;
                }
            if (n > 0)
                result.Add(new Entry { scenePath = sc.path, count = n, objectName = first });
        }
        return result;
    }

    private static List<Entry> DiskEntries(HashSet<string> openPaths)
    {
        string openKey = string.Join("|", openPaths.OrderBy(x => x));
        if (s_diskCache != null && s_diskCacheOpenKey == openKey && EditorApplication.timeSinceStartup - s_diskCacheAt < 300.0)
            return s_diskCache;

        var result = new List<Entry>();
        string guid = ScriptGuid();
        if (guid != null)
        {
            string needle = "guid: " + guid;
            foreach (string sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuid);
                if (openPaths.Contains(path) || !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    int n = CountOccurrences(path, needle);
                    if (n > 0)
                        result.Add(new Entry { scenePath = path, count = n, objectName = null });
                }
                catch (Exception)
                {
                    // 读不了的场景（被占用 / 二进制序列化）跳过。
                }
            }
        }
        s_diskCache = result;
        s_diskCacheAt = EditorApplication.timeSinceStartup;
        s_diskCacheOpenKey = openKey;
        return result;
    }

    /// <summary>全项目所有场景里的初始点（已打开的场景数实物，没打开的读文件里的脚本引用，磁盘部分有缓存）。</summary>
    public static List<Entry> FindAllInProject()
    {
        var openPaths = new HashSet<string>();
        List<Entry> result = LiveEntries(openPaths);
        result.AddRange(DiskEntries(openPaths));
        return result;
    }

    private static int CountOccurrences(string path, string needle)
    {
        int count = 0;
        foreach (string line in File.ReadLines(path))
            if (line.Contains(needle))
                count++;
        return count;
    }

    public static int TotalCount(List<Entry> entries) => entries.Sum(e => e.count);

    /// <summary>摆放前调用：项目里已经有初始点时返回 true，并说明它在哪。</summary>
    public static bool TryGetExisting(out string where, out string scenePath)
    {
        List<Entry> all = FindAllInProject();
        if (all.Count == 0)
        {
            where = null;
            scenePath = null;
            return false;
        }
        Entry e = all[0];
        scenePath = e.scenePath;
        where = (string.IsNullOrEmpty(e.objectName) ? "（未打开的场景）" : "「" + e.objectName + "」") + " 在 " + e.scenePath;
        return true;
    }

    // ── 保护 2：复制出第二个就删掉 ────────────────────────────────

    private static void OnHierarchyChanged()
    {
        if (Application.isPlaying || s_busy)
            return;

        var live = new List<SkyPrisonPlayerStartPoint>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene sc = SceneManager.GetSceneAt(i);
            if (!sc.isLoaded)
                continue;
            foreach (var root in sc.GetRootGameObjects())
                live.AddRange(root.GetComponentsInChildren<SkyPrisonPlayerStartPoint>(true));
        }

        if (live.Count == 0)
        {
            s_knownId = 0;
            return;
        }
        if (live.Count == 1)
        {
            s_knownId = live[0].GetInstanceID();
            return;
        }

        // 保留之前就存在的那一个；没有记录就保留第一个。
        SkyPrisonPlayerStartPoint keep = live.FirstOrDefault(p => p.GetInstanceID() == s_knownId) ?? live[0];
        var extras = live.Where(p => p != keep).Select(p => p.gameObject).Distinct().ToList();
        s_knownId = keep.GetInstanceID();

        s_busy = true;
        EditorApplication.delayCall += () =>
        {
            try
            {
                foreach (GameObject go in extras)
                    if (go != null)
                        Undo.DestroyObjectImmediate(go);
                EditorUtility.DisplayDialog("玩家初始点只能有一个",
                    "天空囚笼只能有一个玩家初始点，刚才多出来的那个已经删除。\n要换位置，选中原来的初始点直接移动它。", "知道了");
            }
            finally
            {
                s_busy = false;
            }
        };
    }

    // ── 保护 3：保存时检查别的场景 ────────────────────────────────

    private static void OnSceneSaving(Scene scene, string path)
    {
        bool hasHere = scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<SkyPrisonPlayerStartPoint>(true) != null);
        if (!hasHere)
            return;

        List<Entry> all = FindAllInProject();
        List<Entry> others = all.Where(e => e.scenePath != scene.path).ToList();
        if (others.Count > 0)
            Debug.LogError("[玩家初始点] 项目里只能有一个初始点，但除了当前场景「" + scene.path + "」，" +
                           string.Join("、", others.Select(e => e.scenePath)) + " 里也有。请删掉多余的。");
    }

    // ── 手动检查 ─────────────────────────────────────────────────

    [MenuItem("天空囚笼/检查/玩家初始点唯一性", false, 900)]
    public static void MenuCheck()
    {
        List<Entry> all = FindAllInProject();
        int total = TotalCount(all);
        if (total == 1)
        {
            Entry e = all[0];
            EditorUtility.DisplayDialog("玩家初始点", "正常：全项目只有 1 个玩家初始点。\n位置：" + e.scenePath, "好");
        }
        else if (total == 0)
        {
            EditorUtility.DisplayDialog("玩家初始点", "全项目还没有玩家初始点。\n到地图放置工具的「出生点」书签摆一个。", "好");
        }
        else
        {
            EditorUtility.DisplayDialog("玩家初始点", "发现 " + total + " 个玩家初始点，只能有一个：\n" +
                string.Join("\n", all.Select(e => e.scenePath + "（" + e.count + " 个）")), "好");
        }
    }
}
