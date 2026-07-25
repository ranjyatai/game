using UnityEditor;
using UnityEngine;

public class LogicAIPackageSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.AIBehaviorPackage;

    public string GetDisplayText(LogicSlotValue value)
    {
        return value.sourceType switch
        {
            LogicValueSourceType.AssetReference => value.assetReference != null ? value.assetReference.name : "<未指定AI包>",
            LogicValueSourceType.Variable => string.IsNullOrWhiteSpace(value.variableKey) ? "<未指定变量>" : value.variableKey,
            LogicValueSourceType.ContextReference => string.IsNullOrWhiteSpace(value.contextKey) ? "<未指定上下文>" : value.contextKey,
            _ => "<未指定AI包>"
        };
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value.sourceType switch
        {
            LogicValueSourceType.AssetReference => value.assetReference != null,
            LogicValueSourceType.Variable => !string.IsNullOrWhiteSpace(value.variableKey),
            LogicValueSourceType.ContextReference => !string.IsNullOrWhiteSpace(value.contextKey),
            _ => false
        };
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        switch (value.sourceType)
        {
            case LogicValueSourceType.AssetReference:
                value.assetReference = EditorGUILayout.ObjectField("AI包", value.assetReference, typeof(AIBehaviorPackage), false);
                break;

            case LogicValueSourceType.Variable:
                value.variableKey = EditorGUILayout.TextField("变量Key", value.variableKey);
                break;

            case LogicValueSourceType.ContextReference:
                value.contextKey = EditorGUILayout.TextField("上下文Key", value.contextKey);
                break;

            default:
                EditorGUILayout.HelpBox("AI包槽位通常使用资源引用。", MessageType.Info);
                break;
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.AIBehaviorPackage,
            sourceType = slot.allowedSources != null && slot.allowedSources.Length > 0
                ? slot.allowedSources[0]
                : LogicValueSourceType.AssetReference
        };
    }
}
