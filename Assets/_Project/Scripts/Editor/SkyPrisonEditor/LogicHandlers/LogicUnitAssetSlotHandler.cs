using UnityEditor;
using UnityEngine;

/// <summary>
/// 单位定义(资产本身)的选择器——跟 LogicUnitSlotHandler(选场景里具体某个单位实例，
/// SceneReference)是两回事。选择弹窗复用 UnitPickerPopupContent(按displayName
/// 搜索)，跟对话系统选NPC、任务编辑器"讨伐目标"快捷模板选怪物是同一个体验。
/// </summary>
public class LogicUnitAssetSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.UnitAsset;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null) return "<未指定单位>";

        if (value.sourceType == LogicValueSourceType.AssetReference)
        {
            if (value.assetReference is UnitDefinition unit)
                return UnitPickerPopupContent.GetLabel(unit);
            if (value.assetReference != null)
                return value.assetReference.name;
        }

        return "<未指定单位>";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null
            && value.sourceType == LogicValueSourceType.AssetReference
            && value.assetReference is UnitDefinition;
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null) return;

        UnitDefinition current = value.assetReference as UnitDefinition;

        EditorGUILayout.LabelField(current != null ? UnitPickerPopupContent.GetLabel(current) : "（未选择）", EditorStyles.boldLabel);
        EditorGUILayout.Space(2f);

        Rect btnRect = GUILayoutUtility.GetRect(new GUIContent("选择单位…"), EditorStyles.objectField, GUILayout.Height(24f), GUILayout.ExpandWidth(true));
        if (GUI.Button(btnRect, "选择单位…"))
        {
            PopupWindow.Show(btnRect, new UnitPickerPopupContent(picked =>
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
            valueType = LogicSlotValueType.UnitAsset,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = null
        };
    }
}
