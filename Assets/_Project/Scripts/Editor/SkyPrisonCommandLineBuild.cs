using UnityEditor;
using UnityEngine;

/// <summary>
/// 命令行批处理构建入口——给 Unity.exe -batchmode -quit -executeMethod 调用，
/// 不用打开编辑器点 Build 按钮。用的是 EditorBuildSettings.scenes（跟手动 Build
/// 用的是同一份场景列表），输出到独立的 Builds/SoakTest 文件夹，不会覆盖已有的
/// 正式 Build（SkyPrison/、axia test/ 那两个）。
/// </summary>
public static class SkyPrisonCommandLineBuild
{
    public static void BuildSoakTestWindows()
    {
        RunBuild("SoakTest", "My project.exe", BuildOptions.None);
    }

    // 2026-07-15：Profiler 的完整二进制采样（Profiler.enabled + enableBinaryLog）在
    // Release Build 里被裁掉/静默失效，只有 Development Build 才真正工作——之前用
    // BuildSoakTestWindows 那个 Release Build 测采样，代码跑了、日志也打了，但硬盘上
    // 压根没生成文件，就是这个原因。特意分开一个独立的输出目录（Builds/ProfilerCapture），
    // 不会覆盖/污染平时用来测真实GC/FPS数据的那个 Release Build——Development Build 自带
    // 额外调试开销，两者数据不能混着比。
    public static void BuildProfilerCaptureWindows()
    {
        RunBuild("ProfilerCapture", "My project.exe", BuildOptions.Development);
    }

    private static void RunBuild(string subFolder, string exeName, BuildOptions options)
    {
        string outputDir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Builds", subFolder);
        System.IO.Directory.CreateDirectory(outputDir);
        string outputPath = System.IO.Path.Combine(outputDir, exeName);

        var buildOptions = new BuildPlayerOptions
        {
            scenes = GetEnabledScenePaths(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = options,
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError($"[SkyPrisonCommandLineBuild] Build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[SkyPrisonCommandLineBuild] Build succeeded -> {outputPath}");
    }

    private static string[] GetEnabledScenePaths()
    {
        var scenes = EditorBuildSettings.scenes;
        var list = new System.Collections.Generic.List<string>();
        foreach (var s in scenes)
        {
            if (s.enabled)
                list.Add(s.path);
        }
        return list.ToArray();
    }
}
