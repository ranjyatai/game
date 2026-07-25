using UnityEditor;
using UnityEngine;

public class LogicCurrencySlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.Currency;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null) return "<未指定货币>";

        if (value.sourceType == LogicValueSourceType.AssetReference)
        {
            if (value.assetReference is CurrencyDefinition cur)
                return string.IsNullOrWhiteSpace(cur.displayName) ? cur.name : cur.displayName;
            if (value.assetReference != null)
                return value.assetReference.name;
        }

        return "<未指定货币>";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null
            && value.sourceType == LogicValueSourceType.AssetReference
            && value.assetReference is CurrencyDefinition;
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null) return;

        CurrencyDefinition current = value.assetReference as CurrencyDefinition;

        EditorGUILayout.BeginHorizontal();

        Texture2D icon = current != null && current.icon != null ? current.icon.texture : null;
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
            EditorGUILayout.LabelField(current.currencyId, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);

        if (GUILayout.Button("选择货币…", GUILayout.Height(24f)))
        {
            SkyPrisonItemPickerPopup.Open(
                current,
                picked =>
                {
                    if (picked is CurrencyDefinition curDef)
                    {
                        value.assetReference = curDef;
                        value.sourceType = LogicValueSourceType.AssetReference;
                    }
                },
                nameof(CurrencyDefinition));
        }

        if (current != null && GUILayout.Button("清空", GUILayout.Height(22f)))
            value.assetReference = null;
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Currency,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = null
        };
    }
}
