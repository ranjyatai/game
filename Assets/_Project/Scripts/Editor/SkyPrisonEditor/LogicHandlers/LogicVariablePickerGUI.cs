using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "来源=变量"这个分支的共用绘制逻辑——之前8个 LogicXxxSlotHandler 各自抄了一份
/// 自由文本框(想填什么变量名全凭手打，容易打错、也不会做类型校验)，现在统一换成
/// 下拉框(只列出类型匹配的已声明变量)+"打开变量表"按钮。类型校验是靠"下拉框里
/// 压根不会出现类型不对的变量"来保证的，不是运行时再判一遍。
/// </summary>
public static class LogicVariablePickerGUI
{
    public static void DrawVariablePicker(LogicSlotValue value, LogicSlotValueType expectedType)
    {
        if (value == null)
            return;

        if (!VariableDatabase.IsSupportedVariableType(expectedType))
        {
            EditorGUILayout.HelpBox($"{VariableDatabase.GetTypeLabel(expectedType)} 类型暂不支持变量。", MessageType.Warning);
            return;
        }

        VariableDatabase database = VariableDatabase.LoadOrNull();
        List<VariableDeclaration> matches = new List<VariableDeclaration>();
        if (database != null)
            matches.AddRange(database.GetByType(expectedType));

        string[] labels = new string[matches.Count + 1];
        labels[0] = "<未指定>";
        int selected = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            labels[i + 1] = matches[i].variableName;
            if (matches[i].variableName == value.variableKey)
                selected = i + 1;
        }

        EditorGUILayout.BeginHorizontal();
        int newIndex = EditorGUILayout.Popup("变量", selected, labels);
        value.variableKey = newIndex == 0 ? "" : matches[newIndex - 1].variableName;

        if (GUILayout.Button("打开变量表", GUILayout.Width(90f)))
            SkyPrisonVariableTableWindow.Open();
        EditorGUILayout.EndHorizontal();

        if (matches.Count == 0)
            EditorGUILayout.HelpBox($"还没有 {VariableDatabase.GetTypeLabel(expectedType)} 类型的变量，先去变量表新建一个。", MessageType.Info);
    }
}
