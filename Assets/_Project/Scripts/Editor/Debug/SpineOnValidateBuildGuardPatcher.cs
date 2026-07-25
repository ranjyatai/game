#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SpineOnValidateBuildGuardPatcher
{
    private const string SpineFilePath = "Assets/Spine/Runtime/spine-unity/Components/Base/SkeletonAnimationBase.cs";

    [MenuItem("Tools/Sky Prison/Debug/给 Spine OnValidate 加 Build 防炸保护")]
    public static void Patch()
    {
        string fullPath = Path.GetFullPath(SpineFilePath);

        if (!File.Exists(fullPath))
        {
            Debug.LogError("[Spine补丁失败] 找不到文件: " + SpineFilePath);
            return;
        }

        string text = File.ReadAllText(fullPath);

        if (text.Contains("SkyPrison_BuildGuard_OnValidate"))
        {
            Debug.Log("[Spine补丁] 已经打过补丁，不重复处理。");
            return;
        }

        int methodIndex = text.IndexOf("OnValidate", System.StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            Debug.LogError("[Spine补丁失败] 找不到 OnValidate 方法。请手动打开 SkeletonAnimationBase.cs 搜索 OnValidate。");
            return;
        }

        int braceStart = text.IndexOf('{', methodIndex);
        if (braceStart < 0)
        {
            Debug.LogError("[Spine补丁失败] 找不到 OnValidate 方法体开始。");
            return;
        }

        int braceEnd = FindMatchingBrace(text, braceStart);
        if (braceEnd < 0)
        {
            Debug.LogError("[Spine补丁失败] 找不到 OnValidate 方法体结束。");
            return;
        }

        string originalBody = text.Substring(braceStart + 1, braceEnd - braceStart - 1);

        string guardedBody =
@"
#if UNITY_EDITOR
            // SkyPrison_BuildGuard_OnValidate
            // Spine 运行时在 Build 校验阶段可能因为内部缓存链未初始化而抛 NullReferenceException。
            // 这里只在 BuildPlayer 期间吞掉这个第三方 OnValidate 空引用，避免阻断打包；
            // 平时编辑器状态仍然暴露异常，避免掩盖真实资源问题。
            try
            {
                if (UnityEditor.BuildPipeline.isBuildingPlayer)
                {
" + Indent(originalBody, "                    ") + @"
                    return;
                }
            }
            catch (System.NullReferenceException ex)
            {
                if (UnityEditor.BuildPipeline.isBuildingPlayer)
                {
                    UnityEngine.Debug.LogWarning(""[Spine Build Guard] SkeletonAnimationBase.OnValidate 在 Build 阶段触发空引用，已跳过。"" + ex.Message, this);
                    return;
                }

                throw;
            }
#endif
" + originalBody;

        string backupPath = fullPath + "." + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
        File.Copy(fullPath, backupPath, false);

        string patched = text.Substring(0, braceStart + 1) + guardedBody + text.Substring(braceEnd);
        File.WriteAllText(fullPath, patched);

        AssetDatabase.Refresh();

        Debug.Log("[Spine补丁完成] 已备份原文件: " + backupPath + "\n已修改: " + SpineFilePath);
    }

    private static int FindMatchingBrace(string text, int openBraceIndex)
    {
        int depth = 0;

        for (int i = openBraceIndex; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static string Indent(string source, string prefix)
    {
        string normalized = source.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalized.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0)
                lines[i] = prefix + lines[i];
        }

        return string.Join("\n", lines);
    }
}
#endif
