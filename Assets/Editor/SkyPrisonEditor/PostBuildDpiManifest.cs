using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// Windows打包出来的.exe如果没有声明DPI感知，系统缩放不是100%时Windows会把它当成
/// 旧程序做位图拉伸，Unity内部读到的 Screen.width/height 跟Editor里Game窗口手动选
/// 固定分辨率算出来的结果不一致——UI锚点、文字换行宽度这些跟屏幕尺寸相关的计算全部
/// 跟着跑偏（今天状态图标位置、物品详情文字重叠这两个"Editor正常Build不正常"的bug，
/// 根源都是这个）。
///
/// Windows支持在.exe同目录放一个同名"xxx.exe.manifest"外部文件，系统会自动识别，
/// 不需要用 mt.exe 把manifest注入进二进制内部，打包完自动复制一份过去即可。
/// </summary>
public static class PostBuildDpiManifest
{
    private const string ManifestSourcePath = "Assets/Plugins/My project.manifest";

    [PostProcessBuild]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows)
            return;

        if (!File.Exists(ManifestSourcePath))
        {
            Debug.LogWarning($"[PostBuildDpiManifest] 找不到 {ManifestSourcePath}，跳过DPI manifest注入。");
            return;
        }

        string exeManifestPath = pathToBuiltProject + ".manifest";
        File.Copy(ManifestSourcePath, exeManifestPath, true);
        Debug.Log($"[PostBuildDpiManifest] 已生成 {exeManifestPath}，声明PerMonitorV2 DPI感知。");
    }
}
