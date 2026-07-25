using System.Collections.Generic;

[System.Serializable]
public class LogicSentenceTemplate
{
    [System.Serializable]
    public class TextToken
    {
        public bool isSlot = false;
        public string text = "";
        public string slotId = "";
    }

    [System.Serializable]
    public class EnumOption
    {
        public string value = "";
        public string displayName = "";

        public EnumOption() { }

        public EnumOption(string value, string displayName)
        {
            this.value = value;
            this.displayName = displayName;
        }
    }

    [System.Serializable]
    public class SlotDefinition
    {
        public string slotId = "slot";
        public string displayName = "参数";
        public LogicSlotValueType valueType = LogicSlotValueType.Float;
        public bool required = true;

        public LogicValueSourceType[] allowedSources =
        {
            LogicValueSourceType.Constant
        };

        // Float 槽位的默认值（0 以外的默认值在此设置）。
        public float defaultFloatValue = 0f;

        // 仅当 valueType == LogicSlotValueType.Enum 时使用。
        public List<EnumOption> enumOptions = new List<EnumOption>();
    }

    public string templateId = "new_template";
    public string displayName = "新句型";
    public LogicSentenceCategory category = LogicSentenceCategory.Condition;
    public LogicNodeKind nodeKind = LogicNodeKind.Leaf;

    public List<LogicTemplateTag> tags = new List<LogicTemplateTag>();
    public List<TextToken> tokens = new List<TextToken>();
    public List<SlotDefinition> slots = new List<SlotDefinition>();
}
