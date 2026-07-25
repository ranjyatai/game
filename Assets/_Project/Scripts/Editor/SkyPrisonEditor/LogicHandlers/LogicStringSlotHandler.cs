
using UnityEditor;

public class LogicStringSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.String;

    public string GetDisplayText(LogicSlotValue value)
    {
        return value.sourceType switch
        {
            LogicValueSourceType.Constant => string.IsNullOrWhiteSpace(value.stringValue) ? "\"\"" : value.stringValue,
            LogicValueSourceType.Variable => string.IsNullOrWhiteSpace(value.variableKey) ? "<未指定变量>" : value.variableKey,
            LogicValueSourceType.ContextReference => string.IsNullOrWhiteSpace(value.contextKey) ? "<未指定上下文>" : value.contextKey,
            LogicValueSourceType.AssetReference => value.assetReference != null ? value.assetReference.name : "<未指定资源>",
            _ => "<未指定>"
        };
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value.sourceType switch
        {
            LogicValueSourceType.Constant => !string.IsNullOrWhiteSpace(value.stringValue),
            LogicValueSourceType.Variable => !string.IsNullOrWhiteSpace(value.variableKey),
            LogicValueSourceType.ContextReference => !string.IsNullOrWhiteSpace(value.contextKey),
            LogicValueSourceType.AssetReference => value.assetReference != null,
            _ => false
        };
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        switch (value.sourceType)
        {
            case LogicValueSourceType.Constant:
                value.stringValue = EditorGUILayout.TextField("字符串", value.stringValue);
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
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.String,
            sourceType = slot.allowedSources != null && slot.allowedSources.Length > 0
                ? slot.allowedSources[0]
                : LogicValueSourceType.Constant
        };
    }
}
