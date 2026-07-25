using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class LogicEnumSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.Enum;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null)
            return "<未指定枚举值>";

        return value.sourceType switch
        {
            LogicValueSourceType.Constant => GetConstantDisplayText(value),
            LogicValueSourceType.Variable => string.IsNullOrWhiteSpace(value.variableKey) ? "<未指定变量>" : value.variableKey,
            LogicValueSourceType.ContextReference => string.IsNullOrWhiteSpace(value.contextKey) ? "<未指定上下文>" : value.contextKey,
            _ => "<未指定枚举值>"
        };
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null)
            return false;

        return value.sourceType switch
        {
            LogicValueSourceType.Constant => IsValidConstant(slot, value),
            LogicValueSourceType.Variable => !string.IsNullOrWhiteSpace(value.variableKey),
            LogicValueSourceType.ContextReference => !string.IsNullOrWhiteSpace(value.contextKey),
            _ => false
        };
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null)
            return;

        value.valueType = LogicSlotValueType.Enum;

        switch (value.sourceType)
        {
            case LogicValueSourceType.Constant:
                DrawConstantEnum(slot, value);
                break;

            case LogicValueSourceType.Variable:
                value.variableKey = EditorGUILayout.TextField("变量Key", value.variableKey);
                break;

            case LogicValueSourceType.ContextReference:
                value.contextKey = EditorGUILayout.TextField("上下文Key", value.contextKey);
                break;

            default:
                EditorGUILayout.HelpBox("枚举槽位通常使用 Constant。", MessageType.Info);
                break;
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        LogicValueSourceType sourceType =
            slot.allowedSources != null && slot.allowedSources.Length > 0
                ? slot.allowedSources[0]
                : LogicValueSourceType.Constant;

        LogicSlotValue value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Enum,
            sourceType = sourceType
        };

        if (sourceType == LogicValueSourceType.Constant)
            ApplyFirstOptionIfNeeded(slot, value);

        return value;
    }

    private void DrawConstantEnum(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        List<LogicSentenceTemplate.EnumOption> options = slot.enumOptions;

        if (options == null || options.Count == 0)
        {
            EditorGUILayout.HelpBox("该 Enum 槽位没有配置 enumOptions。", MessageType.Warning);
            value.enumValue = "";
            value.enumDisplayName = "";
            return;
        }

        int currentIndex = FindOptionIndex(options, value.enumValue);

        if (currentIndex < 0)
        {
            currentIndex = 0;
            ApplyOption(value, options[currentIndex]);
        }

        string[] labels = new string[options.Count];
        for (int i = 0; i < options.Count; i++)
        {
            string displayName = options[i].displayName;
            string rawValue = options[i].value;

            if (!string.IsNullOrWhiteSpace(displayName))
                labels[i] = displayName;
            else if (!string.IsNullOrWhiteSpace(rawValue))
                labels[i] = rawValue;
            else
                labels[i] = $"选项 {i}";
        }

        int nextIndex = EditorGUILayout.Popup(slot.displayName, currentIndex, labels);

        if (nextIndex != currentIndex && nextIndex >= 0 && nextIndex < options.Count)
            ApplyOption(value, options[nextIndex]);

        EditorGUILayout.LabelField("内部值", string.IsNullOrWhiteSpace(value.enumValue) ? "-" : value.enumValue);
    }

    private bool IsValidConstant(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (string.IsNullOrWhiteSpace(value.enumValue))
            return false;

        List<LogicSentenceTemplate.EnumOption> options = slot.enumOptions;

        // 没有配置选项时，不强行判非法，避免旧数据完全不可用。
        if (options == null || options.Count == 0)
            return true;

        return FindOptionIndex(options, value.enumValue) >= 0;
    }

    private string GetConstantDisplayText(LogicSlotValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.enumDisplayName))
            return value.enumDisplayName;

        if (!string.IsNullOrWhiteSpace(value.enumValue))
            return value.enumValue;

        return "<未指定枚举值>";
    }

    private void ApplyFirstOptionIfNeeded(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.enumValue))
            return;

        if (slot.enumOptions == null || slot.enumOptions.Count == 0)
            return;

        ApplyOption(value, slot.enumOptions[0]);
    }

    private void ApplyOption(LogicSlotValue value, LogicSentenceTemplate.EnumOption option)
    {
        if (option == null)
            return;

        value.enumValue = option.value;
        value.enumDisplayName = string.IsNullOrWhiteSpace(option.displayName)
            ? option.value
            : option.displayName;
    }

    private int FindOptionIndex(List<LogicSentenceTemplate.EnumOption> options, string enumValue)
    {
        if (options == null || string.IsNullOrWhiteSpace(enumValue))
            return -1;

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] != null && options[i].value == enumValue)
                return i;
        }

        return -1;
    }
}
