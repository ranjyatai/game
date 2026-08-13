using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把「没有碰撞体的遮挡装饰物」从逐三角面判定切换到碰撞体判定。
///
/// 为什么：遮挡判定的三条路径开销差一个数量级——
///   Mesh 三角面   O(三角面数)   草多的地方 CPU 直接爆掉
///   碰撞体射线    O(1)          打一个 Box，和三角面数无关
///   Renderer Bounds             已废弃，不参与判定
///
/// 而精度不受影响：最终逐像素的 hidden 是 ScreenSpaceOutlineRTManager 在 GPU 上
/// 用「角色轮廓 ∩ 已授权遮挡物」求交算出来的，CPU 这一步只负责挑出候选。
/// 用粗一点的 Box 做候选筛选，再交给 GPU 精确化，结果和用三角面筛选是一样的。
///
/// 注意顺序不能反：必须先让实例真的长出探测碰撞体，才能关三角面。
/// 反过来做的话中间会有一段时间三条路径全空 —— 那正是上一版把遮挡搞丢的原因。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonOccluderCheapPathSetup
{
    private const string LogPrefix = "[SkyPrison OccluderCheapPath]";
    private const string VersionKey = "SkyPrison.OccluderCheapPath.Version";
    // v3：改回三角面判定，并收掉探测碰撞体。
    //
    // Profiler 采样（1.5GB，机器人真实走位）给出的结论推翻了这个工具的前提：
    //   frame=1802  一个触发器 42.6ms，其余 20 个 ≤0.7ms
    //   frame=1924  43.8ms / ≤1.3ms
    //   frame=2866  54.9ms / ≤1.3ms
    //   frame=1803  47.6ms / ≤1.3ms
    // 开销**集中在单独一个实例**上，差 40 倍。不是「每次判定太贵、数量堆出来的」，
    // 所以把三角面换成 Box 根本没打中要害——便宜的实例更便宜了，贵的照样贵。
    //
    // 而 Box 贴合的是渲染包围盒，对草这种细碎形状明显偏粗，观感上有违和。
    // 既然它换不来性能，就没有理由牺牲精度。
    private const int Version = 3;

    static SkyPrisonOccluderCheapPathSetup()
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

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/切换到碰撞体判定（省 CPU）")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Run;
            return;
        }

        // 1. 收掉探测碰撞体——判定改回三角面之后它没有用途了。
        //    留着的话 useSelfColliderRayDepth 那条路也会跑，等于两条判定都在算。
        var switched = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainDecorationDefinition"))
        {
            var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (def == null || !def.generateOccluderProbeCollider)
                continue;

            Undo.RecordObject(def, "Disable Occluder Probe");
            def.generateOccluderProbeCollider = false;
            EditorUtility.SetDirty(def);
            switched.Add(string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName);
        }
        AssetDatabase.SaveAssets();

        // 2. 重建实例——定义改了不回溯已放置的对象，探测碰撞体要靠这一步长出来。
        SkyPrisonOcclusionStructureAuditAndRebuild_V1.RebuildAllSceneThroughBuilder();

        // 3. 只对「确实长出了探测碰撞体」的触发器关三角面。
        //    没长出来的保持三角面判定，否则它们会变成完全不遮挡。
        var triggers = Object.FindObjectsByType<SkyPrisonTerrainDecorationFrontOccluderTrigger>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 三个桶必须能加回总数。上一版「已经是目标状态」的既不计入改动、也不计入
        // 保持三角面，于是 21 个触发器只报出 1 个——数字看着正常，实际没覆盖全，
        // 「看起来跑对了」和「真的跑对了」分不开。
        int cheapened = 0;
        int alreadyCheap = 0;
        int keptExpensive = 0;
        int skippedNull = 0;

        foreach (var t in triggers)
        {
            if (t == null)
            {
                skippedNull++;
                continue;
            }

            bool dirty = false;

            // 判定回到三角面：Box 精度不够，而且换不来性能（开销集中在单个实例上）。
            if (!t.useSelfVisualMeshTriangleRayDepth)
            {
                t.useSelfVisualMeshTriangleRayDepth = true;
                dirty = true;
            }

            // 命中即退出，这一项与判定方式无关，是纯收益，保留。
            if (!t.stopAtFirstSelfMeshHit)
            {
                t.stopAtFirstSelfMeshHit = true;
                dirty = true;
            }

            if (!dirty)
            {
                alreadyCheap++;
                continue;
            }

            Undo.RecordObject(t, "Restore Occluder Triangle Path");
            EditorUtility.SetDirty(t);
            cheapened++;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
        bool saved = EditorSceneManager.SaveOpenScenes();

        int accounted = cheapened + alreadyCheap + keptExpensive + skippedNull;
        string integrity = accounted == triggers.Length
            ? "全部计入"
            : $"⚠ 只计入 {accounted}，与总数不符——统计逻辑有漏洞";

        Debug.Log(
            $"{LogPrefix} 完成。触发器共 {triggers.Length} 个（{integrity}）：\n" +
            $"  本次改回三角面判定：{cheapened}\n" +
            $"  之前已是三角面判定：{alreadyCheap}\n" +
            $"  已销毁跳过（含在下方）：{keptExpensive}\n" +
            (skippedNull > 0 ? $"  已销毁跳过：{skippedNull}\n" : "") +
            $"  收掉探测碰撞体的定义（{switched.Count}）：" +
            $"{(switched.Count > 0 ? string.Join("、", switched) : "无")}\n" +
            $"  场景保存：{(saved ? "成功" : "失败或被取消")}\n" +
            "遮挡精度已回到三角面。性能问题不在这里——Profiler 显示开销集中在单个触发器实例上。");
    }

    /// <summary>
    /// 这个遮挡物身上到底有没有长出探测碰撞体。
    /// 必须实测而不是看定义开关——定义改了但实例没重建时，开关是 true、实例却没有，
    /// 这时候关三角面就等于把遮挡关掉。
    /// </summary>
    private static bool HasProbeCollider(SkyPrisonTerrainDecorationFrontOccluderTrigger t)
    {
        // 找 CollisionRoot 而不是 VisualRoot——触发器的自判定只看前者。
        // 而且必须连节点是否 active 一起判断：节点关着的话底下的碰撞体不参与射线，
        // 存在等于不存在。
        Transform root = t.transform;
        for (int i = 0; i < 4 && root != null; i++)
        {
            Transform collision = root.Find("CollisionRoot");
            if (collision != null)
                return collision.gameObject.activeInHierarchy
                    && collision.GetComponentsInChildren<Collider>(true)
                        .Any(c => c != null && !c.isTrigger && c.enabled);
            root = root.parent;
        }
        return false;
    }
}
