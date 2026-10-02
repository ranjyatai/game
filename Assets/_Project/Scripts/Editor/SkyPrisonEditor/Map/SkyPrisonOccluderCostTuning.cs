using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把前景遮挡触发器调到合理的开销配置。
///
/// 背景：遮挡判定走 Mesh 三角面射线求交，开销是
///   采样点数 × 三角面数 × 遮挡物数
/// 而场景里 21 个触发器全是最贵的组合：stopAtFirstSelfMeshHit=0（每条射线遍历完
/// 所有三角面、不提前退出）+ 三角面上限 20000 + 每 0.03 秒主动扫描。
/// 草多的地方遮挡物数翻几倍，帧率掉到 10~20。
///
/// 这套配置以前不痛，是因为大部分模型 isReadable=0，三角面路径根本跑不起来
/// （直接 return）。我为了修「草不遮挡」把 22 个模型改成可读，等于第一次
/// 真正打开了这条最贵的路径——性能问题是那次改动暴露出来的。
///
/// 这里只改「纯收益」的项，不动会改变判定结果的参数：
///   stopAtFirstSelfMeshHit 0 -> 1
/// 遮挡判定只需要知道「挡没挡住」，不需要最近命中点。命中即退出，结果完全相同。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonOccluderCostTuning
{
    private const string LogPrefix = "[SkyPrison OccluderCost]";
    private const string VersionKey = "SkyPrison.OccluderCostTuning.Version";
    // v3：把 useSelfVisualMeshTriangleRayDepth 改回 true。
    //
    // v2 关掉它是基于一个错误判断——我以为关掉之后会退回「包围盒粗判 + GPU 精确化」，
    // 但 useSelfRendererBoundsRayDepth 早已标记废弃、不参与最终判定，代码里根本没有
    // 包围盒那条路。草又没有碰撞体，三条判定路径同时为空，遮挡直接消失，
    // 回到了最初「草不遮挡」的状态。
    private const int Version = 3;

    static SkyPrisonOccluderCostTuning()
    {
        EditorApplication.delayCall += RunOnce;
    }

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

    [MenuItem("天空囚笼/遮挡/调整遮挡判定开销", false, 123)]
    public static void Run()
    {
        var triggers = Object.FindObjectsByType<SkyPrisonTerrainDecorationFrontOccluderTrigger>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int changed = 0;
        foreach (var t in triggers)
        {
            if (t == null)
                continue;

            bool dirty = false;

            if (!t.stopAtFirstSelfMeshHit)
            {
                t.stopAtFirstSelfMeshHit = true;
                dirty = true;
            }

            // 三角面判定必须开着。
            //
            // 关掉它不会退回「包围盒粗判」——useSelfRendererBoundsRayDepth 已废弃、
            // 不参与最终判定，代码里没有那条路。草又没有碰撞体，
            // useSelfColliderRayDepth 也无靶可打。三条路径同时为空 = 完全不遮挡。
            //
            // 想降开销只能是「换一条更便宜的判定」，不是「关掉唯一在工作的那条」。
            if (!t.useSelfVisualMeshTriangleRayDepth)
            {
                t.useSelfVisualMeshTriangleRayDepth = true;
                dirty = true;
            }

            if (!dirty)
                continue;

            Undo.RecordObject(t, "Tune Occluder Cost");
            EditorUtility.SetDirty(t);
            changed++;
        }

        if (changed == 0)
        {
            Debug.Log($"{LogPrefix} 已经是低开销配置，无需改动（共 {triggers.Length} 个触发器）。");
            return;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
        bool saved = EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            $"{LogPrefix} 调整了 {changed} 个触发器（共 {triggers.Length} 个）：\n" +
            "  stopAtFirstSelfMeshHit            -> true   命中即退出，纯收益\n" +
            "  useSelfVisualMeshTriangleRayDepth -> true   恢复：这是唯一在工作的判定路径\n" +
            $"场景保存：{(saved ? "成功" : "失败或被取消")}。");
    }
}
