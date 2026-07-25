using System.Linq;
using UnityEditor;
using UnityEngine;

public class DebugFlagBatchWindow : EditorWindow
{
    private bool includeInactive = true;
    private string fieldNamesText =
        "debugLogs\n" +
        "debugDraw\n" +
        "debugMode\n" +
        "showDebug\n" +
        "enableDebug\n" +
        "drawGizmos\n" +
        "showGizmos";

    private Vector2 scroll;

    public static void OpenWindow()
    {
        DebugFlagBatchWindow window = GetWindow<DebugFlagBatchWindow>("Debug 批处理工具");
        window.minSize = new Vector2(480f, 520f);
        window.Show();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Debug 批处理工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "这个工具用于批量列出或关闭项目中的常见 Debug 开关。\n" +
            "适合场景测试后快速静音，也适合整理 Prefab。",
            MessageType.Info
        );

        EditorGUILayout.Space(8);

        includeInactive = EditorGUILayout.Toggle("包含未激活对象", includeInactive);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("要处理的字段名", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "每行一个字段名。只会处理 bool 类型字段。\n" +
            "以后项目里出现新的 debug 开关命名，往这里加就行。",
            MessageType.None
        );

        fieldNamesText = EditorGUILayout.TextArea(fieldNamesText, GUILayout.MinHeight(140f));

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("列出", EditorStyles.boldLabel);

        if (GUILayout.Button("列出当前场景已开启 Debug"))
            PrintResult(DebugFlagBatchTools.CollectEnabledFlagsInOpenScene(BuildOptions()), "当前场景");

        if (GUILayout.Button("列出当前选中对象已开启 Debug"))
            PrintResult(DebugFlagBatchTools.CollectEnabledFlagsInSelection(BuildOptions()), "当前选中对象");

        if (GUILayout.Button("列出当前 Prefab Stage 已开启 Debug"))
            PrintResult(DebugFlagBatchTools.CollectEnabledFlagsInCurrentPrefabStage(BuildOptions()), "当前 Prefab Stage");

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("关闭", EditorStyles.boldLabel);

        if (GUILayout.Button("关闭当前场景全部 Debug"))
            PrintDisableResult(DebugFlagBatchTools.DisableFlagsInOpenScene(BuildOptions()), "当前场景");

        if (GUILayout.Button("关闭当前选中对象全部 Debug"))
            PrintDisableResult(DebugFlagBatchTools.DisableFlagsInSelection(BuildOptions()), "当前选中对象");

        if (GUILayout.Button("关闭当前 Prefab Stage 全部 Debug"))
            PrintDisableResult(DebugFlagBatchTools.DisableFlagsInCurrentPrefabStage(BuildOptions()), "当前 Prefab Stage");

        if (GUILayout.Button("关闭 Project 里选中的 Prefab 资产全部 Debug"))
            PrintDisableResult(DebugFlagBatchTools.DisableFlagsInSelectedPrefabAssets(BuildOptions()), "选中的 Prefab 资产");

        EditorGUILayout.Space(10);

        if (GUILayout.Button("恢复默认字段名列表"))
            ResetToDefaultFieldNames();

        EditorGUILayout.EndScrollView();
    }

    private DebugFlagBatchTools.BatchOptions BuildOptions()
    {
        return new DebugFlagBatchTools.BatchOptions
        {
            includeInactive = includeInactive,
            fieldNames = ParseFieldNames(fieldNamesText)
        };
    }

    private string[] ParseFieldNames(string text)
    {
        return text
            .Split('\n')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToArray();
    }

    private void ResetToDefaultFieldNames()
    {
        fieldNamesText = string.Join("\n", DebugFlagBatchTools.DefaultBoolFieldNames);
    }

    private void PrintResult(DebugFlagBatchTools.BatchResult result, string targetName)
    {
        if (result.enabledLines == null || result.enabledLines.Count == 0)
        {
            Debug.Log($"[DebugFlagBatchWindow] {targetName} 没有已开启的 Debug 开关。");
            return;
        }

        Debug.Log(
            $"[DebugFlagBatchWindow] {targetName} 已开启 Debug：\n" +
            string.Join("\n", result.enabledLines)
        );
    }

    private void PrintDisableResult(DebugFlagBatchTools.BatchResult result, string targetName)
    {
        Debug.Log(
            $"[DebugFlagBatchWindow] {targetName} Debug 已关闭。扫描组件={result.scannedComponentCount}, " +
            $"命中组件={result.matchedComponentCount}, 修改组件={result.changedComponentCount}, 修改字段={result.changedFieldCount}"
        );
    }
}
