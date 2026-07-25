using UnityEngine;

public interface ILogicSlotHandler
{
    LogicSlotValueType ValueType { get; }

    string GetDisplayText(LogicSlotValue value);

    bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value);

    void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value);

    LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot);
}