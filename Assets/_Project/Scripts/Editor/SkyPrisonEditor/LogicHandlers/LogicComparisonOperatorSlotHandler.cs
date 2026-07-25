using System;
using UnityEditor;

public class LogicComparisonOperatorSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.ComparisonOperator;

    public string GetDisplayText(LogicSlotValue value)
    {
        return LogicSentenceEditorGUI.GetComparisonOperatorSymbol(value.enumValue);
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return !string.IsNullOrWhiteSpace(value.enumValue);
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        LogicComparisonOperator current = LogicComparisonOperator.Less;
        if (!string.IsNullOrWhiteSpace(value.enumValue))
            Enum.TryParse(value.enumValue, out current);

        current = (LogicComparisonOperator)EditorGUILayout.EnumPopup("比较符", current);
        value.enumValue = current.ToString();
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.ComparisonOperator,
            sourceType = LogicValueSourceType.Constant,
            enumValue = LogicComparisonOperator.Less.ToString()
        };
    }
}
