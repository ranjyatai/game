using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 命令行带 -profilecapture 参数时，让 Build 自己把完整的 Profiler 采样数据写到硬盘
/// 一个 .raw 文件里——不需要 Editor 连着 Profiler 窗口全程盯着，事后用
/// SkyPrisonProfilerAnalyzer（Editor脚本，命令行批处理调用）加载这个文件分析。
///
/// 只用来做一次性、短时间的针对性复现（比如"跑进新区域卡顿"），不要跟 -soaktest
/// 挂一整晚同时用——完整采样数据文件会随时间线性变大。
/// </summary>
public static class SkyPrisonProfilerCaptureController
{
    private const string CommandLineArg = "-profilecapture";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void EnableIfRequested()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        bool requested = false;
        foreach (var a in args)
        {
            if (string.Equals(a, CommandLineArg, System.StringComparison.OrdinalIgnoreCase))
            {
                requested = true;
                break;
            }
        }

        if (!requested)
            return;

        string logPath = System.IO.Path.Combine(Application.persistentDataPath, "profiler_capture");
        Profiler.logFile = logPath;
        Profiler.enableBinaryLog = true;
        Profiler.enabled = true;

        Debug.Log($"[SkyPrisonProfilerCaptureController] Profiler 二进制采样已开启，写入：{logPath}.raw / .data（具体扩展名由 Unity 版本决定）。");
    }
}
