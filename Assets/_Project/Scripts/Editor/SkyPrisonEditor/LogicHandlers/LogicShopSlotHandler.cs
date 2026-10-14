using UnityEditor;
using UnityEngine;

public class LogicShopSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.Shop;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null) return "<未指定商店>";

        if (value.sourceType == LogicValueSourceType.AssetReference)
        {
            if (value.assetReference is ShopDefinition shop)
                return string.IsNullOrWhiteSpace(shop.displayName) ? shop.name : shop.displayName;
            if (value.assetReference != null)
                return value.assetReference.name;
        }

        return "<未指定商店>";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null
            && value.sourceType == LogicValueSourceType.AssetReference
            && value.assetReference is ShopDefinition;
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null) return;

        ShopDefinition current = value.assetReference as ShopDefinition;

        EditorGUILayout.BeginVertical();
        string displayText = current != null
            ? (string.IsNullOrWhiteSpace(current.displayName) ? current.name : current.displayName)
            : "（未选择）";
        EditorGUILayout.LabelField(displayText, EditorStyles.boldLabel);
        if (current != null)
            EditorGUILayout.LabelField(current.shopId, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(2f);

        if (GUILayout.Button("选择商店…", GUILayout.Height(24f)))
        {
            SkyPrisonItemPickerPopup.Open(
                current,
                picked =>
                {
                    if (picked is ShopDefinition shopDef)
                    {
                        value.assetReference = shopDef;
                        value.sourceType = LogicValueSourceType.AssetReference;
                    }
                },
                nameof(ShopDefinition));
        }

        if (current != null && GUILayout.Button("清空", GUILayout.Height(22f)))
            value.assetReference = null;
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Shop,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = null
        };
    }
}
