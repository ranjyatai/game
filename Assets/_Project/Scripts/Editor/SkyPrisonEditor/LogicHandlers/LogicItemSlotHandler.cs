using UnityEditor;
using UnityEngine;

public class LogicItemSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.Item;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null) return "<未指定物品>";

        if (value.sourceType == LogicValueSourceType.AssetReference)
        {
            if (value.assetReference is ItemDefinition item)
                return string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
            if (value.assetReference != null)
                return value.assetReference.name;
        }

        return "<未指定物品>";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null
            && value.sourceType == LogicValueSourceType.AssetReference
            && value.assetReference is ItemDefinition;
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null) return;

        ItemDefinition current = value.assetReference as ItemDefinition;

        EditorGUILayout.BeginHorizontal();

        // 图标预览
        Texture2D icon = GetItemIcon(current);
        Rect iconRect = GUILayoutUtility.GetRect(32f, 32f, GUILayout.Width(32f), GUILayout.Height(32f));
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.08f));

        EditorGUILayout.BeginVertical();
        string displayText = current != null
            ? (string.IsNullOrWhiteSpace(current.displayName) ? current.name : current.displayName)
            : "（未选择）";
        EditorGUILayout.LabelField(displayText, EditorStyles.boldLabel);
        if (current != null)
            EditorGUILayout.LabelField(current.itemKey, EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);

        if (GUILayout.Button("选择物品…", GUILayout.Height(24f)))
        {
            SkyPrisonItemPickerPopup.Open(
                current,
                picked =>
                {
                    if (picked is ItemDefinition itemDef)
                    {
                        value.assetReference = itemDef;
                        value.sourceType = LogicValueSourceType.AssetReference;
                    }
                },
                nameof(ItemDefinition));
        }

        if (current != null)
        {
            if (GUILayout.Button("清空", GUILayout.Height(22f)))
            {
                value.assetReference = null;
            }
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Item,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = null
        };
    }

    private static Texture2D GetItemIcon(ItemDefinition item)
    {
        if (item == null) return null;
        if (item.icon != null && item.icon.texture != null)
            return item.icon.texture;
        return null;
    }
}
