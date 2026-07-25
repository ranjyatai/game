using System.Collections.Generic;

public static class AILogicSentenceTemplateLibrary
{
    private static List<LogicSentenceTemplate> cachedTemplates;

    public static IReadOnlyList<LogicSentenceTemplate> GetAllTemplates()
    {
        if (cachedTemplates == null)
            BuildTemplates();

        return cachedTemplates;
    }

    public static List<LogicSentenceTemplate> GetTemplatesByCategory(LogicSentenceCategory category)
    {
        if (cachedTemplates == null)
            BuildTemplates();

        List<LogicSentenceTemplate> result = new List<LogicSentenceTemplate>();
        for (int i = 0; i < cachedTemplates.Count; i++)
        {
            if (cachedTemplates[i].category == category)
                result.Add(cachedTemplates[i]);
        }

        return result;
    }

    public static LogicSentenceTemplate GetTemplateById(string templateId)
    {
        if (cachedTemplates == null)
            BuildTemplates();

        for (int i = 0; i < cachedTemplates.Count; i++)
        {
            if (cachedTemplates[i].templateId == templateId)
                return cachedTemplates[i];
        }

        return null;
    }

    public static LogicSentenceInstance CreateInstance(string templateId)
    {
        LogicSentenceTemplate template = GetTemplateById(templateId);
        if (template == null)
            return null;

        LogicSentenceInstance instance = new LogicSentenceInstance
        {
            templateId = template.templateId,
            enabled = true
        };

        for (int i = 0; i < template.slots.Count; i++)
        {
            LogicSentenceTemplate.SlotDefinition slot = template.slots[i];
            LogicSlotAssignment assignment = new LogicSlotAssignment
            {
                slotId = slot.slotId,
                value = new LogicSlotValue
                {
                    valueType = slot.valueType,
                    sourceType = slot.allowedSources != null && slot.allowedSources.Length > 0
                        ? slot.allowedSources[0]
                        : LogicValueSourceType.Constant
                }
            };

            if (slot.valueType == LogicSlotValueType.ComparisonOperator)
                assignment.value.enumValue = LogicComparisonOperator.Less.ToString();

            if (slot.valueType == LogicSlotValueType.Enum && string.IsNullOrWhiteSpace(assignment.value.enumValue))
                assignment.value.enumValue = "";

            if (slot.valueType == LogicSlotValueType.Unit &&
                assignment.value.sourceType == LogicValueSourceType.ContextReference &&
                string.IsNullOrWhiteSpace(assignment.value.contextKey))
            {
                assignment.value.contextKey = "当前目标";
            }

            instance.slotAssignments.Add(assignment);
        }

        return instance;
    }

    public static void InvalidateCache() => cachedTemplates = null;

    private static void BuildTemplates()
    {
        cachedTemplates = new List<LogicSentenceTemplate>();

        AddTemplatesUnique(AILogicSentenceTemplateLibraryCore.GetTemplates());
        AddTemplatesUnique(AILogicSentenceTemplateLibraryCombat.GetTemplates());
        AddTemplatesUnique(AILogicSentenceTemplateLibrarySystem.GetTemplates());
        AddTemplatesUnique(AILogicSentenceTemplateLibraryMacro.GetTemplates());
    }

    private static void AddTemplatesUnique(IEnumerable<LogicSentenceTemplate> templates)
    {
        if (templates == null)
            return;

        foreach (LogicSentenceTemplate template in templates)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.templateId))
                continue;

            bool exists = false;
            for (int i = 0; i < cachedTemplates.Count; i++)
            {
                if (cachedTemplates[i] != null && cachedTemplates[i].templateId == template.templateId)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                cachedTemplates.Add(template);
        }
    }
}
