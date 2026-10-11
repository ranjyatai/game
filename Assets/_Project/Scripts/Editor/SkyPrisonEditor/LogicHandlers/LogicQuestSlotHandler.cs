using UnityEditor;
using UnityEngine;

public class LogicQuestSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.Quest;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null) return "<未指定任务>";

        if (value.sourceType == LogicValueSourceType.AssetReference)
        {
            if (value.assetReference is QuestDefinition quest)
                return QuestPickerPopupContent.GetLabel(quest, LocalizationSettingsUtility.GetOrCreateSettings());
            if (value.assetReference != null)
                return value.assetReference.name;
        }

        return "<未指定任务>";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null
            && value.sourceType == LogicValueSourceType.AssetReference
            && value.assetReference is QuestDefinition;
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null) return;

        QuestDefinition current = value.assetReference as QuestDefinition;

        EditorGUILayout.BeginVertical();
        string displayText = current != null
            ? QuestPickerPopupContent.GetLabel(current, LocalizationSettingsUtility.GetOrCreateSettings())
            : "（未选择）";
        EditorGUILayout.LabelField(displayText, EditorStyles.boldLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(2f);

        Rect btnRect = GUILayoutUtility.GetRect(new GUIContent("选择任务…"), EditorStyles.objectField, GUILayout.Height(24f), GUILayout.ExpandWidth(true));
        if (GUI.Button(btnRect, "选择任务…"))
        {
            PopupWindow.Show(btnRect, new QuestPickerPopupContent(picked =>
            {
                value.assetReference = picked;
                value.sourceType = LogicValueSourceType.AssetReference;
            }));
        }

        if (current != null && GUILayout.Button("清空", GUILayout.Height(22f)))
            value.assetReference = null;
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Quest,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = null
        };
    }
}
