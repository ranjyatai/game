using UnityEditor;
using UnityEngine;

public class LogicIntSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.Int;

    public string GetDisplayText(LogicSlotValue value)
    {
        return value.sourceType switch
        {
            LogicValueSourceType.Constant => value.intValue.ToString(),
            LogicValueSourceType.RandomRange => $"随机 {value.intValue} ~ {value.intValueMax}",
            LogicValueSourceType.Variable => string.IsNullOrWhiteSpace(value.variableKey) ? "<未指定变量>" : value.variableKey,
            LogicValueSourceType.ContextReference => string.IsNullOrWhiteSpace(value.contextKey) ? "<未指定上下文>" : value.contextKey,
            LogicValueSourceType.AssetReference => value.assetReference != null ? value.assetReference.name : "<未指定资源>",
            LogicValueSourceType.Expression => value.GetDisplayText(),
            _ => "<未指定>"
        };
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value.sourceType switch
        {
            LogicValueSourceType.Constant => true,
            LogicValueSourceType.RandomRange => value.intValue <= value.intValueMax,
            LogicValueSourceType.Variable => !string.IsNullOrWhiteSpace(value.variableKey),
            LogicValueSourceType.ContextReference => !string.IsNullOrWhiteSpace(value.contextKey),
            LogicValueSourceType.AssetReference => value.assetReference != null,
            LogicValueSourceType.Expression => !value.IsEmpty(),
            _ => false
        };
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        switch (value.sourceType)
        {
            case LogicValueSourceType.Constant:
                value.intValue = EditorGUILayout.IntField("整数", value.intValue);
                break;

            case LogicValueSourceType.RandomRange:
                value.intValue    = EditorGUILayout.IntField("最小值", value.intValue);
                value.intValueMax = EditorGUILayout.IntField("最大值", value.intValueMax);
                break;

            case LogicValueSourceType.Variable:
                value.variableKey = EditorGUILayout.TextField("变量Key", value.variableKey);
                break;

            case LogicValueSourceType.ContextReference:
                value.contextKey = EditorGUILayout.TextField("上下文Key", value.contextKey);
                break;

            case LogicValueSourceType.AssetReference:
                value.assetReference = EditorGUILayout.ObjectField("资源", value.assetReference, typeof(UnityEngine.Object), false);
                break;

            case LogicValueSourceType.Expression:
                LogicExpressionSlotEditorGUI.DrawExpressionEditor(LogicSlotValueType.Int, value);
                break;
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Int,
            sourceType = slot.allowedSources != null && slot.allowedSources.Length > 0
                ? slot.allowedSources[0]
                : LogicValueSourceType.Constant
        };
    }
}
