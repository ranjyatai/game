#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 前景遮挡触发器的自诊断——找出「为什么每帧总有一个实例吃掉 40~55ms」。
///
/// Profiler 采样（1.5GB，浸测机器人真实走位）显示：
///   frame=1802  一个触发器 42.6ms，其余 20 个 ≤0.7ms
///   frame=2866  54.9ms / ≤1.3ms
/// 21 个实例的序列化配置完全一致（scanAllTargetLayerCollidersForDebug 全 0、
/// activeWakeScan 全 1、wakeScanInterval 全 0.03），所以差异只可能来自运行时状态。
/// self≈total 说明开销在方法自身的循环里，符合「在遍历一个很大的集合」。
///
/// 设计上刻意避开上次的坑：
///   - 没有任何序列化字段，不会被存进场景、不会跟着进 Build
///   - 只在 DEVELOPMENT_BUILD / 编辑器下编译
///   - 跑满 DurationSeconds 自动停，打印一次结果就彻底关掉
/// 上一次留下的 debugLogs 是序列化的，被保存进场景又打进 Build，白测十小时。
/// </summary>
public static class OccluderSelfProfiler
{
    private const float StartDelaySeconds = 5f;    // 跳过开局加载，避免统计到 Awake/JIT
    private const float DurationSeconds = 30f;

    private sealed class Stat
    {
        public string path;
        public long ticks;
        public int frames;
        public int maxCandidates;
        public Vector3 boundsSize;
        public int rendererCount;
    }

    private static readonly Dictionary<int, Stat> stats = new Dictionary<int, Stat>();
    private static float startTime = -1f;
    private static bool reported;

    /// <summary>关掉之后调用点连时间戳都不取，开销归零。</summary>
    public static bool Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        stats.Clear();
        reported = false;
        startTime = -1f;
        Active = false;

        var go = new GameObject("~OccluderSelfProfiler");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<Ticker>();
    }

    public static void Record(SkyPrisonTerrainDecorationFrontOccluderTrigger t, long ticks, int candidates)
    {
        if (!Active || t == null)
            return;

        int id = t.GetInstanceID();
        if (!stats.TryGetValue(id, out Stat s))
        {
            s = new Stat { path = BuildPath(t.transform) };

            // 只在第一次记录时取一次静态信息，之后不再碰 —— 诊断本身不能变成开销。
            Transform visual = FindVisualRoot(t.transform);
            if (visual != null)
            {
                Renderer[] rs = visual.GetComponentsInChildren<Renderer>(true);
                s.rendererCount = rs.Length;
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++)
                        b.Encapsulate(rs[i].bounds);
                    s.boundsSize = b.size;
                }
            }
            stats[id] = s;
        }

        s.ticks += ticks;
        s.frames++;
        if (candidates > s.maxCandidates)
            s.maxCandidates = candidates;
    }

    private sealed class Ticker : MonoBehaviour
    {
        private void Update()
        {
            if (reported)
                return;

            if (startTime < 0f)
            {
                startTime = Time.unscaledTime + StartDelaySeconds;
                return;
            }

            if (!Active)
            {
                if (Time.unscaledTime >= startTime)
                {
                    Active = true;
                    Debug.Log($"[OccluderSelfProfiler] 开始统计，{DurationSeconds} 秒后自动输出并关闭。");
                }
                return;
            }

            if (Time.unscaledTime < startTime + DurationSeconds)
                return;

            Active = false;
            reported = true;
            Report();
            Destroy(gameObject);
        }
    }

    private static void Report()
    {
        if (stats.Count == 0)
        {
            Debug.Log("[OccluderSelfProfiler] 统计期内没有任何遮挡触发器执行。");
            return;
        }

        double tickToMs = 1000.0 / Stopwatch.Frequency;
        var sb = new StringBuilder();
        sb.AppendLine($"[OccluderSelfProfiler] {stats.Count} 个实例，按累计耗时排序：");

        foreach (Stat s in stats.Values.OrderByDescending(x => x.ticks).Take(8))
        {
            double totalMs = s.ticks * tickToMs;
            double avgMs = s.frames > 0 ? totalMs / s.frames : 0;
            sb.AppendLine(
                $"  累计 {totalMs,9:F1}ms  均 {avgMs,7:F3}ms/帧  帧数 {s.frames,5}  " +
                $"候选峰值 {s.maxCandidates,3}  渲染体 {s.rendererCount,3}  " +
                $"包围盒 {s.boundsSize.x:F1}x{s.boundsSize.y:F1}x{s.boundsSize.z:F1}\n" +
                $"      {s.path}");
        }

        sb.AppendLine("对比最贵和最便宜的两行：候选峰值 / 渲染体 / 包围盒 哪一项差得最多，哪一项就是原因。");
        Debug.Log(sb.ToString());
    }

    private static Transform FindVisualRoot(Transform from)
    {
        for (int i = 0; i < 4 && from != null; i++)
        {
            Transform v = from.Find("VisualRoot");
            if (v != null)
                return v;
            from = from.parent;
        }
        return null;
    }

    private static string BuildPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        int guard = 0;
        while (t.parent != null && guard++ < 8)
        {
            t = t.parent;
            sb.Insert(0, t.name + "/");
        }
        return sb.ToString();
    }
}
#endif
