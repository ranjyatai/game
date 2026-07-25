using System.Collections.Generic;

public static class AILogicSentenceTemplateLibraryMacro
{
    public static List<LogicSentenceTemplate> GetTemplates()
    {
        return new List<LogicSentenceTemplate>
        {
            BuildActionExecuteMacro(),
            BuildConditionReadyToAttackTarget(),
            BuildActionMoveToSpawnPosition(),
        };
    }

    private static LogicSentenceTemplate BuildConditionReadyToAttackTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_ready_to_attack_target",
            displayName = "可以攻击当前目标",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "已进入有效攻击范围" },
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildActionMoveToSpawnPosition()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_to_spawn_position",
            displayName = "移动到出生位置",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "移动 该单位 回到出生位置，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" },
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "moveMode",
                    displayName = "移动方式",
                    valueType = LogicSlotValueType.Enum,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.Constant },
                    enumOptions = new List<LogicSentenceTemplate.EnumOption>
                    {
                        new LogicSentenceTemplate.EnumOption("行走", "行走"),
                        new LogicSentenceTemplate.EnumOption("奔跑", "奔跑"),
                        new LogicSentenceTemplate.EnumOption("潜行", "潜行"),
                    }
                }
            }
        };
    }

    private static LogicSentenceTemplate BuildActionExecuteMacro()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_execute_macro",
            displayName = "执行宏",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "执行宏 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "macro" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "macro",
                    displayName = "宏指令",
                    valueType = LogicSlotValueType.String,
                    required = true,
                    allowedSources = new[]
                    {
                        LogicValueSourceType.Constant,
                        LogicValueSourceType.Variable
                    }
                }
            }
        };
    }
}
