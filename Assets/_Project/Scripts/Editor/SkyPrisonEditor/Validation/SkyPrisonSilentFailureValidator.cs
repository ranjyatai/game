using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 静默失败校验器——把「看起来正常、实际什么都没做」的状态变成可见的报错。
///
/// 2026-08-13 一整天的排查里，下面这些问题没有一件会报错，全靠肉眼在画面上发现异常
/// 再花大量时间回溯：
///
///   前后遮挡        22 个模型 isReadable=0 → 三条判定路径全空 → 对所有装饰物失效
///   walkableSurface 字段存在、运行时消费它，但编辑器页面从没暴露 → 35 个定义无一填写
///   CharacterArc    数据库资产不存在 → GetAll() 返回空 → 人物记录静默不显示
///   浸测机器人      找不到玩家，静默重试十小时，测的是站着不动的角色
///   调试开关        序列化进场景，跟着打进 Build，1 分钟 1749 次 GC
///   死属性          材质上留着着色器根本不声明的属性，排查时反复误导
///
/// 性能问题至少可测量；静默失败不可测量——它的代价是时间，而且不出现在任何报告里。
/// 这个校验器针对的正是这一类。
///
/// 每条检查都必须满足两个条件才写进来：
///   1. 对应一个真实发生过的问题，不是假想
///   2. 判据可靠，不会产生噪声——误报会让人学会忽略它，那比没有更糟
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonSilentFailureValidator
{
    private const string LogPrefix = "[SkyPrison 校验]";
    private const string VersionKey = "SkyPrison.SilentFailureValidator.Version";
    private const int Version = 1;

    static SkyPrisonSilentFailureValidator()
    {
        EditorApplication.delayCall += RunOnce;
    }

    /// <summary>
    /// 只自动跑一次。这个扫描要遍历全项目材质和预制体，开销不小；而且校验结果需要人看，
    /// 每次重载都刷一遍很容易被当成噪音划过去。以后手动跑菜单即可。
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

    private sealed class Finding
    {
        public string category;
        public string detail;
        public Object context;
        public bool isError;
    }

    [MenuItem("天空囚笼/校验与清理/检查静默失败", false, 210)]
    public static void Run()
    {
        var findings = new List<Finding>();

        CheckOcclusionIntegrity(findings);
        CheckResourceDatabases(findings);
        CheckStaleSceneInstances(findings);
        CheckEnabledDebugLogFlags(findings);
        CheckDeadMaterialProperties(findings);

        Report(findings);
    }

    /// <summary>
    /// 遮挡链路完整性：occlusionMode 开着，但三条判定路径可能全都用不上。
    ///
    /// 判定要么走 Mesh 三角面（要求模型 Read/Write 可读），要么走碰撞体射线
    /// （要求 CollisionRoot 下有非 Trigger 碰撞体）。Renderer Bounds 那条已废弃。
    /// 两条都不满足时遮挡永远不生效，而且不报任何错——今天就是这个状态。
    /// </summary>
    private static void CheckOcclusionIntegrity(List<Finding> findings)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainDecorationDefinition"))
        {
            var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (def == null || def.occlusionMode == TerrainDecorationOcclusionMode.None)
                continue;

            bool hasCollider = def.generateOccluderProbeCollider
                            || def.collisionMode != TerrainDecorationCollisionMode.None;
            if (hasCollider)
                continue;

            // 没有碰撞体时，唯一可用的是三角面路径——检查模型可不可读。
            var unreadable = new List<string>();
            if (def.variants != null)
            {
                foreach (var v in def.variants)
                {
                    if (v == null || v.prefab == null)
                        continue;

                    foreach (MeshFilter mf in v.prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf == null || mf.sharedMesh == null)
                            continue;

                        string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                        if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                            unreadable.Add(System.IO.Path.GetFileName(path));
                    }
                }
            }

            if (unreadable.Count == 0)
                continue;

            findings.Add(new Finding
            {
                category = "遮挡永远不生效",
                detail = $"「{Describe(def)}」开了遮挡，但既没有碰撞体、模型也不可读："
                       + string.Join("、", unreadable.Distinct().Take(3))
                       + "。三条判定路径全空，遮挡不会有任何效果。",
                context = def,
                isError = true,
            });
        }
    }

    /// <summary>
    /// Resources 下的数据库必须存在且非空。
    /// 缺失时 Resources.Load 返回 null，各 Runtime 一律退化成空列表——不报错，
    /// 表现是「功能安静地不显示」，非常难查。
    /// </summary>
    private static void CheckResourceDatabases(List<Finding> findings)
    {
        CheckDatabase<QuestDatabase>(findings, "QuestDatabase", db => db.quests?.Count ?? 0, "任务");
        CheckDatabase<CharacterArcDatabase>(findings, "CharacterArcDatabase", db => db.arcs?.Count ?? 0, "人物记录");
        CheckDatabase<QuestMarkerIconSet>(findings, "QuestMarkerIconSet", _ => 1, "任务图标集");
    }

    private static void CheckDatabase<T>(List<Finding> findings, string resourcesName,
                                         System.Func<T, int> countOf, string label) where T : Object
    {
        T db = Resources.Load<T>(resourcesName);
        if (db == null)
        {
            findings.Add(new Finding
            {
                category = "数据库缺失",
                detail = $"Resources 下找不到 {resourcesName}，{label}系统会静默地什么都不显示。",
                isError = true,
            });
            return;
        }

        if (countOf(db) == 0)
        {
            findings.Add(new Finding
            {
                category = "数据库为空",
                detail = $"{resourcesName} 存在但一条{label}都没有。新建了定义忘了加进清单时，"
                       + "表现和数据库不存在完全一样。",
                context = db,
                isError = false,
            });
        }
    }

    /// <summary>
    /// 定义改了但场景实例没重建——这一整天反复出现的问题。
    /// 定义上填了 walkableSurface，实例却没有 GroundSurfaceMarker，
    /// 说明它是在填写之前放下的，地表脚步声不会生效。
    /// </summary>
    private static void CheckStaleSceneInstances(List<Finding> findings)
    {
        var binders = Object.FindObjectsByType<TerrainDecorationRuntimeBinder>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var stale = new List<string>();
        foreach (var b in binders)
        {
            if (b == null || b.definition == null || b.definition.walkableSurface == null)
                continue;
            if (b.GetComponent<GroundSurfaceMarker>() == null)
                stale.Add(b.name);
        }

        if (stale.Count == 0)
            return;

        findings.Add(new Finding
        {
            category = "实例落后于定义",
            detail = $"{stale.Count} 个装饰物实例的定义填了可站立表面材质，实例上却没有 "
                   + "GroundSurfaceMarker，地表脚步声不会生效。跑一次装饰物重建即可。"
                   + $"（例：{string.Join("、", stale.Take(3))}）",
            isError = false,
        });
    }

    /// <summary>
    /// 序列化的调试日志开关一旦保存就会跟着进 Build，在正式运行时逐帧打日志。
    /// 实测一分钟 1749 次 GC、7.4MB 日志。
    /// </summary>
    private static void CheckEnabledDebugLogFlags(List<Finding> findings)
    {
        var on = new List<string>();

        foreach (MonoBehaviour mb in CollectSceneComponents())
        {
            if (mb == null)
                continue;

            var so = new SerializedObject(mb);
            SerializedProperty p = so.GetIterator();
            while (p.NextVisible(true))
            {
                if (p.propertyType != SerializedPropertyType.Boolean || !p.boolValue)
                    continue;

                string n = p.name.ToLowerInvariant();
                bool isLogFlag = n.Contains("log") && (n.Contains("debug") || n.Contains("verbose") || n.Contains("trace"));
                if (isLogFlag || p.name == "debugHorizontalAttribution")
                    on.Add($"{mb.GetType().Name}.{p.name} ({mb.name})");
            }
        }

        if (on.Count == 0)
            return;

        findings.Add(new Finding
        {
            category = "调试日志会进 Build",
            detail = $"{on.Count} 处逐帧日志开关是打开且已序列化的：{string.Join("、", on.Take(4))}"
                   + (on.Count > 4 ? " …" : "")
                   + "。跑一次「关闭所有逐帧调试日志开关」。",
            isError = false,
        });
    }

    /// <summary>
    /// 材质上留着着色器根本不声明的属性。
    ///
    /// 这类死属性在排查时极具误导性——今天我按 _OccludedBrightness / _OccludedSaturation
    /// 追了很久，最后才发现没有任何着色器读它们。
    ///
    /// 只查项目自己的材质和自己的属性前缀，避免第三方资产的历史残留刷屏。
    /// </summary>
    private static void CheckDeadMaterialProperties(List<Finding> findings)
    {
        string[] ownPrefixes = { "_SkyPrison", "_SP_", "_Occluded" };
        var dead = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null)
                continue;

            var declared = new HashSet<string>();
            int count = mat.shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
                declared.Add(mat.shader.GetPropertyName(i));

            var so = new SerializedObject(mat);
            SerializedProperty floats = so.FindProperty("m_SavedProperties.m_Floats");
            if (floats == null)
                continue;

            for (int i = 0; i < floats.arraySize; i++)
            {
                string name = floats.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("first")?.stringValue;
                if (string.IsNullOrEmpty(name) || declared.Contains(name))
                    continue;
                if (!ownPrefixes.Any(name.StartsWith))
                    continue;

                dead.Add($"{System.IO.Path.GetFileName(path)} → {name}");
            }
        }

        if (dead.Count == 0)
            return;

        findings.Add(new Finding
        {
            category = "材质上的死属性",
            detail = $"{dead.Count} 处材质属性对应的着色器已经不声明它们了，改了也没有任何效果，"
                   + $"排查时会误导：{string.Join("、", dead.Take(4))}" + (dead.Count > 4 ? " …" : ""),
            isError = false,
        });
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

    private static string Describe(TerrainDecorationDefinition def)
        => string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName;

    private static void Report(List<Finding> findings)
    {
        if (findings.Count == 0)
        {
            Debug.Log($"{LogPrefix} 全部通过，没有发现静默失败。");
            return;
        }

        var sb = new StringBuilder();
        int errors = findings.Count(f => f.isError);
        sb.AppendLine($"{LogPrefix} {findings.Count} 项（其中 {errors} 项会导致功能完全不工作）：");

        foreach (Finding f in findings.OrderByDescending(f => f.isError))
            sb.AppendLine($"  [{(f.isError ? "失效" : "注意")}] {f.category}：{f.detail}");

        if (errors > 0)
            Debug.LogError(sb.ToString());
        else
            Debug.LogWarning(sb.ToString());

        // 逐条再打一次，带 context 便于点击定位。
        foreach (Finding f in findings.Where(x => x.context != null))
            Debug.LogWarning($"{LogPrefix} {f.category}：{f.detail}", f.context);
    }
}
