using System.Collections.Generic;

public static class AILogicSentenceTemplateLibraryCore
{
    public static List<LogicSentenceTemplate> GetTemplates()
    {
        return new List<LogicSentenceTemplate>
        {
            // 事件
            BuildEventInitialize(),
            BuildEventEverySeconds(),
            // 条件
            BuildConditionUnitToUnitDistanceCompare(),
            BuildConditionUnitNoCurrentTarget(),
            BuildConditionUnitCurrentTargetExists(),
            BuildConditionUnitCanSeeUnit(),
            BuildConditionUnitCanSeePerceptionTarget(),
            BuildConditionUnitCanHearPerceptionTarget(),
            BuildConditionUnitHasDetectedPerceptionTarget(),
            BuildConditionUnitIsSuspicious(),
            BuildConditionUnitIsAlert(),
            BuildConditionUnitHasLastHeardPosition(),
            BuildConditionUnitHasLastKnownPosition(),
            BuildConditionUnitToCurrentTargetDistanceCompare(),
            BuildConditionUnitBehaviorTypeIs(),
            BuildConditionUniversalCompare(),

            // 基础动作
            BuildActionSetCurrentTarget(),
            BuildActionClearCurrentTarget(),
            BuildActionSetBehaviorType(),
            BuildActionMoveToXY(),
            BuildActionMoveToTargetWithMoveMode(),
            BuildActionMoveToXYWithMoveMode(),
            BuildActionMoveAwayFromTarget(),
            BuildActionRandomWanderInRadius(),
            BuildActionMoveToLastHeardPosition(),
            BuildActionMoveToLastKnownPosition(),
            BuildActionMoveToLastPerceptionPosition(),
            BuildActionStopMove(),
            BuildActionWaitSeconds(),
            BuildActionSwitchAIPackage(),

            // 控制流 / 结构
            BuildIfThenAction(),
            BuildIfThenElseAction(),
            BuildWhileLoop(),
            BuildDoWhileLoop(),
            BuildLoopBlock(),
            BuildBreakLoop(),
            BuildDisableCurrentTrigger(),
            BuildCommentNode(),
            BuildCodeNode()
        };
    }

    // =========================
    // 事件
    // =========================

    private static LogicSentenceTemplate BuildEventInitialize()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_initialize",
            displayName = "初始化",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Time },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "初始化进程" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildEventEverySeconds()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_every_seconds",
            displayName = "每秒触发",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Time, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "每 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "seconds" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 秒" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildFloatSlot("seconds", "秒数")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventUnitSeeUnit()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_unit_see_unit",
            displayName = "单位看见单位",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 看见 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "target" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildUnitSlot("target", "目标单位", true, true)
            }
        };
    }

    // =========================
    // 条件
    // =========================

    private static LogicSentenceTemplate BuildConditionUnitToUnitDistanceCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_to_unit_distance_compare",
            displayName = "单位与单位的距离比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Point, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unitA" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 与 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unitB" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的距离 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "distance" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unitA", "单位A", true, true),
                BuildUnitSlot("unitB", "单位B", true, true),
                BuildComparisonSlot("op", "比较符"),
                BuildFloatSlot("distance", "距离")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitNoCurrentTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_no_current_target",
            displayName = "单位当前无目标为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前无目标 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildBoolSlot("expected", "是否无目标")
            }
        };
    }


    private static LogicSentenceTemplate BuildConditionUnitCurrentTargetExists()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_current_target_exists",
            displayName = "单位当前目标存在为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前目标存在 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildBoolSlot("expected", "是否存在")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeUnit()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_see_unit",
            displayName = "单位可以看见单位为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 可以看见 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "观察单位", true, true),
                BuildUnitSlot("target", "目标单位", true, true),
                BuildBoolSlot("expected", "是否看得见")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeePlayer()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_see_player",
            displayName = "单位可以看见玩家为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 可以看见 玩家 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "观察单位", true, true),
                BuildBoolSlot("expected", "是否看得见")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeCurrentTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_see_current_target",
            displayName = "单位可以看见当前目标为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 可以看见 当前目标 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "观察单位", true, true),
                BuildBoolSlot("expected", "是否看得见")
            }
        };
    }


    private static LogicSentenceTemplate BuildPerceptionBoolCondition(string templateId, string displayName, string middleText)
    {
        return new LogicSentenceTemplate
        {
            templateId = templateId,
            displayName = displayName,
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = middleText },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "判断单位", true, true),
                BuildBoolSlot("expected", "是否成立")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitIsSuspicious()
    {
        return BuildPerceptionBoolCondition("cond_unit_is_suspicious", "单位处于怀疑状态为", " 处于 怀疑状态 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitIsAlert()
    {
        return BuildPerceptionBoolCondition("cond_unit_is_alert", "单位处于警戒状态为", " 处于 警戒状态 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitHasDetectedPlayer()
    {
        return BuildPerceptionBoolCondition("cond_unit_has_detected_player", "单位已发现玩家为", " 已发现 玩家 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanHearPlayer()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_hear_player", "单位可以听见玩家为", " 可以听见 玩家 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanHearHostileUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_hear_hostile_unit", "单位可以听见敌对单位为", " 可以听见 敌对单位 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanHearFriendlyUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_hear_friendly_unit", "单位可以听见友军为", " 可以听见 友军 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanHearNeutralUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_hear_neutral_unit", "单位可以听见中立单位为", " 可以听见 中立单位 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanHearCreatureUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_hear_creature_unit", "单位可以听见生物为", " 可以听见 生物 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitHasLastHeardPosition()
    {
        return BuildPerceptionBoolCondition("cond_unit_has_last_heard_position", "单位有最后听到位置为", " 有 最后听到位置 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitHasLastKnownPosition()
    {
        return BuildPerceptionBoolCondition("cond_unit_has_last_known_position", "单位有最后感知位置为", " 有 最后感知位置 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeHostileUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_see_hostile_unit", "单位可以看见敌对单位为", " 可以看见 敌对单位 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitHasDetectedHostileUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_has_detected_hostile_unit", "单位已发现敌对单位为", " 已发现 敌对单位 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeFriendlyUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_see_friendly_unit", "单位可以看见友军为", " 可以看见 友军 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeNeutralUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_see_neutral_unit", "单位可以看见中立单位为", " 可以看见 中立单位 为 ");
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeCreatureUnit()
    {
        return BuildPerceptionBoolCondition("cond_unit_can_see_creature_unit", "单位可以看见生物为", " 可以看见 生物 为 ");
    }


    private static LogicSentenceTemplate BuildConditionUnitCanSeePerceptionTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_see_perception_target",
            displayName = "单位可以看见对象为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 可以看见 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "targetKind" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildPerceptionTargetKindSlot("targetKind", "对象"),
                BuildBoolSlot("expected", "是否成立")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitCanHearPerceptionTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_hear_perception_target",
            displayName = "单位可以听见对象为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 可以听见 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "targetKind" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildPerceptionTargetKindSlot("targetKind", "对象"),
                BuildBoolSlot("expected", "是否成立")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitHasDetectedPerceptionTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_has_detected_perception_target",
            displayName = "单位已发现对象为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 已发现 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "targetKind" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildPerceptionTargetKindSlot("targetKind", "对象"),
                BuildBoolSlot("expected", "是否成立")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitToCurrentTargetDistanceCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_to_current_target_distance_compare",
            displayName = "单位与当前目标的距离比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 与 当前目标 的距离 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "distance" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildComparisonSlot("op", "比较符"),
                BuildFloatSlot("distance", "距离")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitBehaviorTypeIs()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_behavior_type_is",
            displayName = "单位行为类型是",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的行为类型 是 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "behaviorType" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "主体单位", true, true),
                BuildBehaviorTypeSlot("behaviorType", "行为类型")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUniversalCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_universal_compare",
            displayName = "通用比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "left" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "right" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildAnyValueSlot("left", "左值"),
                BuildComparisonSlot("op", "比较符"),
                BuildAnyValueSlot("right", "右值")
            }
        };
    }

    // =========================
    // 基础动作
    // =========================


    private static LogicSentenceTemplate BuildActionSetCurrentTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_current_target",
            displayName = "设置当前目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前目标 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "targetKind" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildEnumSlot("targetKind", "目标类型", new List<LogicSentenceTemplate.EnumOption>
                {
                    new LogicSentenceTemplate.EnumOption("Player",       "玩家"),
                    new LogicSentenceTemplate.EnumOption("LastDetected", "最后发现目标"),
                    new LogicSentenceTemplate.EnumOption("LastPerceived","最后感知目标"),
                    new LogicSentenceTemplate.EnumOption("LastHearing",  "最后听到目标"),
                    new LogicSentenceTemplate.EnumOption("LastHostile",  "最后敌对目标"),
                })
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetCurrentTargetToPlayer()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_current_target_to_player",
            displayName = "设置当前目标为玩家",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前目标 为 玩家" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetCurrentTargetToLastDetectedTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_current_target_to_last_detected_target",
            displayName = "设置当前目标为最后发现目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前目标 为 最后发现目标" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetCurrentTargetToLastPerceivedTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_current_target_to_last_perceived_target",
            displayName = "设置当前目标为最后感知目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前目标 为 最后感知目标" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetCurrentTargetToLastHearingTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_current_target_to_last_hearing_target",
            displayName = "设置当前目标为最后听到目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前目标 为 最后听到目标" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetCurrentTargetToLastHostileTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_current_target_to_last_hostile_target",
            displayName = "设置当前目标为最后敌对目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前目标 为 最后敌对目标" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionClearCurrentTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_clear_current_target",
            displayName = "清空当前目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "清空 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前目标" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetBehaviorType()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_behavior_type",
            displayName = "更改行为类型",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的行为类型为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "behaviorType" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildBehaviorTypeSlot("behaviorType", "行为类型")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionMoveToTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_to_target",
            displayName = "移动到目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "移动到 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "target" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("target", "目标", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionMoveToXY()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_to_xy",
            displayName = "移动到坐标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Point, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "移动到坐标为（" },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "x" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = ", " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "y" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "）的地方" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildFloatSlot("x", "X"),
                BuildFloatSlot("y", "Y")
            }
        };
    }


    private static LogicSentenceTemplate BuildActionMoveToTargetWithMoveMode()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_to_target_with_move_mode",
            displayName = "移动到目标（移动方式）",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI, LogicTemplateTag.Point },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "移动 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 到 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的位置，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildUnitSlot("target", "目标单位", true, true),
                BuildMoveModeSlot("moveMode", "移动方式")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionMoveToXYWithMoveMode()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_to_xy_with_move_mode",
            displayName = "移动到坐标（移动方式）",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Point, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "移动 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 到坐标为（" },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "x" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = ", " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "y" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "）的地方，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildFloatSlot("x", "X"),
                BuildFloatSlot("y", "Y"),
                BuildMoveModeSlot("moveMode", "移动方式")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionMoveAwayFromTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_away_from_target",
            displayName = "远离目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI, LogicTemplateTag.Point },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "让 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 远离 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "，距离为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "distance" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildUnitSlot("target", "远离对象", true, true),
                BuildFloatSlot("distance", "距离"),
                BuildMoveModeSlot("moveMode", "移动方式")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionRandomWanderInRadius()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_random_in_radius",
            displayName = "随机徘徊",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI, LogicTemplateTag.Point, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "让 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 在半径 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "radius" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 内随机徘徊，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildFloatSlot("radius", "半径"),
                BuildMoveModeSlot("moveMode", "移动方式")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionMoveToLastHeardPosition()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_to_last_heard_position",
            displayName = "移动到最后听到位置",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI, LogicTemplateTag.Point },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "让 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 移动到最后听到位置，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildMoveModeSlot("moveMode", "移动方式")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionMoveToLastKnownPosition()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_to_last_known_position",
            displayName = "移动到最后感知位置",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI, LogicTemplateTag.Point },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "让 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 移动到最后感知位置，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildMoveModeSlot("moveMode", "移动方式")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionMoveToLastPerceptionPosition()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_move_unit_to_last_perception_position",
            displayName = "移动到最后感知/听到位置",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI, LogicTemplateTag.Point },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "让 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 移动到最后感知/听到位置，移动方式为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "moveMode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true),
                BuildMoveModeSlot("moveMode", "移动方式")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionStopMove()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_stop_unit_move",
            displayName = "停止移动",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "停止 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "subject" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 移动" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("subject", "执行单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionWaitSeconds()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_wait_seconds",
            displayName = "等待秒数",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Time, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "等待 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "seconds" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 秒" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildFloatSlot("seconds", "秒数")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSwitchAIPackage()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_switch_ai_package",
            displayName = "切换 AI 包",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "切换到 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "package" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "package",
                    displayName = "AI包",
                    valueType = LogicSlotValueType.AIBehaviorPackage,
                    required = true,
                    allowedSources = new[]
                    {
                        LogicValueSourceType.AssetReference
                    }
                }
            }
        };
    }

    // =========================
    // 控制流 / 结构
    // =========================

    private static LogicSentenceTemplate BuildIfThenAction()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_if_then",
            displayName = "If / Then",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Branch,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
        {
            new LogicSentenceTemplate.TextToken { isSlot = false, text = "If / then" }
        },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildIfThenElseAction()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_if_then_else",
            displayName = "If / Then / Else",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Branch,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
        {
            new LogicSentenceTemplate.TextToken { isSlot = false, text = "If / then / else" }
        },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildWhileLoop()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_while",
            displayName = "While",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Loop,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
        {
            new LogicSentenceTemplate.TextToken { isSlot = false, text = "While" }
        },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildDoWhileLoop()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_do_while",
            displayName = "Do While",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Loop,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
        {
            new LogicSentenceTemplate.TextToken { isSlot = false, text = "Do While" }
        },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildLoopBlock()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_loop",
            displayName = "Loop",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Loop,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
        {
            new LogicSentenceTemplate.TextToken { isSlot = false, text = "Loop" }
        },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }


    private static LogicSentenceTemplate BuildBreakLoop()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_break_loop",
            displayName = "中断当前循环",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "中断当前循环" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildDisableCurrentTrigger()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_disable_trigger",
            displayName = "关闭当前触发器",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "关闭当前触发器" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildCommentNode()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_comment",
            displayName = "注释",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Comment,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "comment" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "comment",
                    displayName = "注释",
                    valueType = LogicSlotValueType.String,
                    required = false,
                    allowedSources = new[] { LogicValueSourceType.Constant }
                }
            }
        };
    }

    private static LogicSentenceTemplate BuildCodeNode()
    {
        return new LogicSentenceTemplate
        {
            templateId = "flow_code",
            displayName = "代码",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Code,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "code" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "code",
                    displayName = "代码",
                    valueType = LogicSlotValueType.String,
                    required = false,
                    allowedSources = new[] { LogicValueSourceType.Constant }
                }
            }
        };
    }

    // =========================
    // Helpers
    // =========================

    private static LogicSentenceTemplate.SlotDefinition BuildUnitSlot(string slotId, string displayName, bool allowContext, bool allowScene)
    {
        List<LogicValueSourceType> allowed = new List<LogicValueSourceType>();
        if (allowContext)
            allowed.Add(LogicValueSourceType.ContextReference);
        if (allowScene)
            allowed.Add(LogicValueSourceType.SceneReference);

        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Unit,
            required = true,
            allowedSources = allowed.ToArray()
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildBoolSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Bool,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant,
                LogicValueSourceType.Variable,
                LogicValueSourceType.ContextReference
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildFloatSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Float,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant,
                LogicValueSourceType.Variable
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildComparisonSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.ComparisonOperator,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildAnyValueSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Any,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant,
                LogicValueSourceType.Variable,
                LogicValueSourceType.ContextReference,
                LogicValueSourceType.SceneReference
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildEnumSlot(
        string slotId,
        string displayName,
        List<LogicSentenceTemplate.EnumOption> enumOptions)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Enum,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant
            },
            enumOptions = enumOptions ?? new List<LogicSentenceTemplate.EnumOption>()
        };
    }


    private static LogicSentenceTemplate.SlotDefinition BuildPerceptionTargetKindSlot(string slotId, string displayName)
    {
        return BuildEnumSlot(
            slotId,
            displayName,
            new List<LogicSentenceTemplate.EnumOption>
            {
                new LogicSentenceTemplate.EnumOption("玩家", "玩家"),
                new LogicSentenceTemplate.EnumOption("当前目标", "当前目标"),
                new LogicSentenceTemplate.EnumOption("敌对单位", "敌对单位"),
                new LogicSentenceTemplate.EnumOption("友军", "友军"),
                new LogicSentenceTemplate.EnumOption("中立单位", "中立单位"),
                new LogicSentenceTemplate.EnumOption("生物", "生物")
            });
    }

    private static LogicSentenceTemplate.SlotDefinition BuildMoveModeSlot(string slotId, string displayName)
    {
        return BuildEnumSlot(
            slotId,
            displayName,
            new List<LogicSentenceTemplate.EnumOption>
            {
                new LogicSentenceTemplate.EnumOption("行走", "行走"),
                new LogicSentenceTemplate.EnumOption("奔跑", "奔跑"),
                new LogicSentenceTemplate.EnumOption("潜行", "潜行")
            });
    }

    private static LogicSentenceTemplate.SlotDefinition BuildBehaviorTypeSlot(string slotId, string displayName)
    {
        return BuildEnumSlot(
            slotId,
            displayName,
            new List<LogicSentenceTemplate.EnumOption>
            {
                new LogicSentenceTemplate.EnumOption("待机", "待机"),
                new LogicSentenceTemplate.EnumOption("怀疑", "怀疑"),
                new LogicSentenceTemplate.EnumOption("警戒", "警戒"),
                new LogicSentenceTemplate.EnumOption("发现", "发现"),
                new LogicSentenceTemplate.EnumOption("徘徊", "徘徊"),
                new LogicSentenceTemplate.EnumOption("逃跑", "逃跑"),
                new LogicSentenceTemplate.EnumOption("停止", "停止"),
                new LogicSentenceTemplate.EnumOption("追击", "追击"),
                new LogicSentenceTemplate.EnumOption("攻击", "攻击"),
                new LogicSentenceTemplate.EnumOption("返回", "返回")
            });
    }

    private static LogicSentenceTemplate.SlotDefinition BuildOptionalConditionReferenceSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.ConditionReference,
            required = false,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant
            }
        };
    }
}
