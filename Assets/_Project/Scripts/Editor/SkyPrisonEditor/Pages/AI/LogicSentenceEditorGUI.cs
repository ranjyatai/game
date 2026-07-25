using UnityEditor;
using UnityEngine;

public static class LogicSentenceEditorGUI
{
    public static Color ValidColor => new Color(0.35f, 0.85f, 1f, 1f);
    public static Color ErrorColor => new Color(1f, 0.35f, 0.35f, 1f);
    public static Color DisabledColor => new Color(0.6f, 0.6f, 0.6f, 1f);
    public static Color KeywordColor => new Color(0.75f, 0.9f, 0.75f, 1f);

    public static bool IsSentenceValid(SerializedProperty sentenceProp)
    {
        if (sentenceProp == null) return false;

        SerializedProperty templateIdProp = sentenceProp.FindPropertyRelative("templateId");
        if (templateIdProp == null || string.IsNullOrWhiteSpace(templateIdProp.stringValue))
            return false;

        LogicSentenceTemplate template = AILogicSentenceTemplateLibrary.GetTemplateById(templateIdProp.stringValue);
        if (template == null) return false;

        SerializedProperty assignmentsProp = sentenceProp.FindPropertyRelative("slotAssignments");
        for (int i = 0; i < template.slots.Count; i++)
        {
            var slot = template.slots[i];
            if (!slot.required) continue;

            SerializedProperty assignmentProp = FindAssignment(assignmentsProp, slot.slotId);
            if (assignmentProp == null) return false;

            if (!IsAssignmentValid(slot, assignmentProp))
                return false;
        }

        return true;
    }

    public static bool IsAssignmentValid(LogicSentenceTemplate.SlotDefinition slot, SerializedProperty assignmentProp)
    {
        if (slot == null || assignmentProp == null)
            return false;

        SerializedProperty valueProp = assignmentProp.FindPropertyRelative("value");
        if (valueProp == null)
            return false;

        LogicSlotValue value = ReadLogicSlotValue(valueProp);

        if (value.valueType != slot.valueType)
            return false;

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slot.valueType);
        if (handler == null)
            return false;

        return handler.IsValid(slot, value);
    }

    public static string GetSentenceText(SerializedProperty sentenceProp)
    {
        if (sentenceProp == null) return "<空句子>";

        SerializedProperty templateIdProp = sentenceProp.FindPropertyRelative("templateId");
        LogicSentenceTemplate template = AILogicSentenceTemplateLibrary.GetTemplateById(templateIdProp.stringValue);
        if (template == null) return "<未知句型>";

        SerializedProperty assignmentsProp = sentenceProp.FindPropertyRelative("slotAssignments");

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < template.tokens.Count; i++)
        {
            var token = template.tokens[i];
            if (!token.isSlot)
            {
                sb.Append(token.text);
            }
            else
            {
                SerializedProperty assignmentProp = FindAssignment(assignmentsProp, token.slotId);
                LogicSentenceTemplate.SlotDefinition slotDef = template.slots?.Find(s => s.slotId == token.slotId);
                sb.Append(GetSlotDisplayText(assignmentProp, slotDef));
            }
        }

        return sb.ToString();
    }

    public static string GetSlotDisplayText(SerializedProperty assignmentProp, LogicSentenceTemplate.SlotDefinition slotDef = null)
    {
        if (assignmentProp == null) return "<未指定>";

        SerializedProperty valueProp = assignmentProp.FindPropertyRelative("value");
        if (valueProp == null) return "<未指定>";

        LogicSlotValue value = ReadLogicSlotValue(valueProp);

        // 旧数据 enumDisplayName 为空时，从模板 enumOptions 兜底查找中文显示名
        if (value.valueType == LogicSlotValueType.Enum
            && string.IsNullOrWhiteSpace(value.enumDisplayName)
            && !string.IsNullOrWhiteSpace(value.enumValue)
            && slotDef?.enumOptions != null)
        {
            foreach (var opt in slotDef.enumOptions)
            {
                if (opt != null && opt.value == value.enumValue && !string.IsNullOrWhiteSpace(opt.displayName))
                {
                    value.enumDisplayName = opt.displayName;
                    break;
                }
            }
        }

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(value.valueType);
        if (handler == null)
            return "<未注册处理器>";

        return handler.GetDisplayText(value);
    }

    public static string GetComparisonOperatorSymbol(string enumValue)
    {
        if (string.IsNullOrWhiteSpace(enumValue))
            return "<比较符>";

        if (!System.Enum.TryParse(enumValue, out LogicComparisonOperator op))
            return "<比较符>";

        return op switch
        {
            LogicComparisonOperator.Greater => ">",
            LogicComparisonOperator.Less => "<",
            LogicComparisonOperator.Equal => "==",
            LogicComparisonOperator.NotEqual => "!=",
            LogicComparisonOperator.GreaterOrEqual => ">=",
            LogicComparisonOperator.LessOrEqual => "<=",
            LogicComparisonOperator.StrictEqual => "===",
            _ => "<比较符>"
        };
    }

    public static SerializedProperty FindAssignment(SerializedProperty assignmentsProp, string slotId)
    {
        if (assignmentsProp == null) return null;

        for (int i = 0; i < assignmentsProp.arraySize; i++)
        {
            SerializedProperty a = assignmentsProp.GetArrayElementAtIndex(i);
            if (a.FindPropertyRelative("slotId").stringValue == slotId)
                return a;
        }

        return null;
    }

    public static LogicSlotValue ReadLogicSlotValue(SerializedProperty valueProp)
    {
        if (valueProp == null)
            return new LogicSlotValue();

        return new LogicSlotValue
        {
            valueType = (LogicSlotValueType)valueProp.FindPropertyRelative("valueType").enumValueIndex,
            sourceType = (LogicValueSourceType)valueProp.FindPropertyRelative("sourceType").enumValueIndex,
            boolValue = valueProp.FindPropertyRelative("boolValue").boolValue,
            intValue = valueProp.FindPropertyRelative("intValue").intValue,
            floatValue = valueProp.FindPropertyRelative("floatValue").floatValue,
            stringValue = valueProp.FindPropertyRelative("stringValue").stringValue,
            enumValue = valueProp.FindPropertyRelative("enumValue").stringValue,
            enumDisplayName = valueProp.FindPropertyRelative("enumDisplayName")?.stringValue ?? "",
            variableKey = valueProp.FindPropertyRelative("variableKey").stringValue,
            contextKey = valueProp.FindPropertyRelative("contextKey").stringValue,
            assetReference = valueProp.FindPropertyRelative("assetReference").objectReferenceValue,
            sceneObjectId = valueProp.FindPropertyRelative("sceneObjectId").stringValue,
            sceneObjectName = valueProp.FindPropertyRelative("sceneObjectName").stringValue
        };
    }

}
