using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 命令行批处理入口——加载 SkyPrisonProfilerCaptureController 写出来的 .raw 采样文件，
/// 找到耗时最长的一帧，把主线程调用树按自身耗时(self time)从大到小展开，写成一份
/// 纯文字报告。不需要打开 Editor、不需要点 Profiler 窗口。
///
/// 用法：
///   Unity.exe -batchmode -quit -projectPath "..." -executeMethod SkyPrisonProfilerAnalyzer.AnalyzeWorstFrame
///     -profilerCaptureFile "C:\...\profiler_capture.raw"
///     -profilerReportOut "C:\...\profiler_report.txt"
/// </summary>
public static class SkyPrisonProfilerAnalyzer
{
    public static void AnalyzeWorstFrame()
    {
        string capturePath = GetArgValue("-profilerCaptureFile");
        string reportPath = GetArgValue("-profilerReportOut");

        if (string.IsNullOrEmpty(capturePath) || !File.Exists(capturePath))
        {
            Debug.LogError($"[SkyPrisonProfilerAnalyzer] 找不到采样文件：{capturePath}");
            EditorApplication.Exit(1);
            return;
        }

        if (string.IsNullOrEmpty(reportPath))
            reportPath = Path.Combine(Path.GetDirectoryName(capturePath) ?? ".", "profiler_report.txt");

        var sb = new StringBuilder();

        if (!ProfilerDriver.LoadProfile(capturePath, false))
        {
            Debug.LogError($"[SkyPrisonProfilerAnalyzer] ProfilerDriver.LoadProfile 加载失败：{capturePath}");
            EditorApplication.Exit(1);
            return;
        }

        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;
        sb.AppendLine($"采样文件：{capturePath}");
        sb.AppendLine($"帧范围：{first} ~ {last}（共 {last - first + 1} 帧）");
        sb.AppendLine();

        if (last < first)
        {
            sb.AppendLine("没有有效帧数据。");
            File.WriteAllText(reportPath, sb.ToString());
            Debug.LogError("[SkyPrisonProfilerAnalyzer] 没有有效帧数据。");
            EditorApplication.Exit(1);
            return;
        }

        // 排除开局宽限期——frame 0 附近往往是引导/资源加载耗时被记成一帧，量级跟真实
        // 游戏内卡顿完全不是一回事（能到几秒），跟 SkyPrisonFrameSpikeWatchdog 自己的
        // startupGraceFrames 是同一个道理。跳过前150帧，同时记录前5名方便交叉核对，
        // 不只依赖单独一帧防止再抓到一次别的异常点。
        const int startupGraceFrames = 150;
        int scanStart = Math.Min(first + startupGraceFrames, last);

        var topFrames = new System.Collections.Generic.List<(int frame, float ms)>();
        for (int frame = scanStart; frame <= last; frame++)
        {
            using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                frame, 0, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnDontSort, false))
            {
                if (view == null || !view.valid)
                    continue;

                float ms = view.GetRootItemID() >= 0
                    ? view.GetItemColumnDataAsSingle(view.GetRootItemID(), HierarchyFrameDataView.columnTotalTime)
                    : 0f;

                topFrames.Add((frame, ms));
            }
        }

        topFrames.Sort((a, b) => b.ms.CompareTo(a.ms));

        sb.AppendLine($"跳过开局前 {startupGraceFrames} 帧（frame {first}~{scanStart - 1}）后，耗时最长的前5帧：");
        for (int i = 0; i < Math.Min(5, topFrames.Count); i++)
            sb.AppendLine($"  #{i + 1}  frame={topFrames[i].frame}  {topFrames[i].ms:0.###}ms");
        sb.AppendLine();

        // 展开前5名里的每一帧，而不是只看第1名——696/743 离开局很近，很可能是场景刚加载完
        // 的初始化开销；1752/1734/1712 隔得很远且彼此挨得很近，更像是真正的中途卡顿聚集。
        // 把每一帧的自身耗时明细都摆出来对比，看这几个是不是同一种成因。
        int dumpCount = Math.Min(5, topFrames.Count);
        for (int i = 0; i < dumpCount; i++)
        {
            int frame = topFrames[i].frame;
            float ms = topFrames[i].ms;
            sb.AppendLine("========================================");
            sb.AppendLine($"#{i + 1}  frame={frame}  主线程总耗时={ms:0.###}ms");
            sb.AppendLine("========================================");

            using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                frame, 0, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnSelfTime, false))
            {
                if (view != null && view.valid)
                {
                    DumpTopSelfTimeFlat(view, sb);
                }
                else
                {
                    sb.AppendLine("（这一帧的详细视图不可用）");
                }
            }
            sb.AppendLine();
        }

        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log($"[SkyPrisonProfilerAnalyzer] 报告已写入：{reportPath}");
        EditorApplication.Exit(0);
    }

    // 不递归展开树形结构（不同 Unity 版本 API 细节差异大，容易出错），改成用
    // GetItemDescendantsThatHaveChildren(rootID) 拿到所有节点后，按 SelfTime 列排序打印
    // 前N个——这样不管调用树多深，都能直接看到"到底哪个函数自己吃了最多时间"。
    private static void DumpTopSelfTimeFlat(HierarchyFrameDataView view, StringBuilder sb)
    {
        int rootId = view.GetRootItemID();
        if (rootId < 0)
        {
            sb.AppendLine("（根节点无效）");
            return;
        }

        var allIds = new System.Collections.Generic.List<int>();
        CollectAllIds(view, rootId, allIds, 0, 64);

        var rows = new System.Collections.Generic.List<(string name, float selfMs, float totalMs, int calls, int depth)>();
        foreach (int id in allIds)
        {
            float selfMs = view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnSelfTime);
            if (selfMs < 0.05f)
                continue;
            float totalMs = view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnTotalTime);
            string name = view.GetItemName(id);
            int calls = 1;
            try { calls = (int)view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnCalls); } catch { }
            rows.Add((name, selfMs, totalMs, calls, 0));
        }

        rows.Sort((a, b) => b.selfMs.CompareTo(a.selfMs));

        int shown = 0;
        foreach (var row in rows)
        {
            if (shown >= 60) break;
            sb.AppendLine($"  self={row.selfMs,8:0.000}ms  total={row.totalMs,8:0.000}ms  calls={row.calls,5}  {row.name}");
            shown++;
        }

        if (shown == 0)
            sb.AppendLine("（没有找到 self time >= 0.05ms 的样本）");
    }

    private static void CollectAllIds(HierarchyFrameDataView view, int id, System.Collections.Generic.List<int> results, int depth, int maxDepth)
    {
        results.Add(id);
        if (depth >= maxDepth)
            return;

        var children = new System.Collections.Generic.List<int>();
        view.GetItemChildren(id, children);
        foreach (int child in children)
            CollectAllIds(view, child, results, depth + 1, maxDepth);
    }

    private static string GetArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
