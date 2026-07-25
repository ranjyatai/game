using System.Collections.Generic;

public static class AILogicSentenceTemplateLibraryCombat
{
    public static List<LogicSentenceTemplate> GetTemplates()
    {
        return new List<LogicSentenceTemplate>
        {
            // 事件
            BuildEventUnitDeath(),
            BuildEventUnitTookDamage(),
            BuildEventUnitChangedFaction(),
            BuildEventUnitLoadBelowValue(),
            BuildEventUnitIsStaggered(),
            BuildEventUnitUsedSkill(),
            BuildEventUnitEnterArea(),
            BuildEventUnitLeaveArea(),

            // 条件
            // 视野条件：放在条件列表最前面，避免在下拉框里找不到
            BuildConditionUnitCanSeeUnit(),
            BuildConditionUnitCanSeePlayer(),
            BuildConditionAnyEnemySeesPlayer(),
            BuildConditionUnitCanSeeCurrentTarget(),
            BuildConditionUnitHealthCompareValue(),
            BuildConditionUnitLoadCompareValue(),
            BuildConditionUnitIsDefineType(),
            BuildConditionUnitIsCharacterIdentity(),
            BuildConditionUnitHasTarget(),
            BuildConditionUnitHasStatus(),
            BuildConditionUnitHasNoStatus(),

            // 战斗状态判断
            BuildConditionUnitIsAttacking(),
            BuildConditionUnitCanAttack(),

            // 行动状态
            BuildActionEnterPatrolState(),
            BuildActionEnterChaseState(),
            BuildActionEnterEscapeState(),

            // 一般战斗动作
            BuildActionBasicAttack(),
            BuildActionHeavyAttack(),
            BuildActionForceUnitDeath(),
            BuildActionForceUnitReviveWithHealth(),
            BuildActionForceUnitReviveWithHealthPercent(),
            BuildActionSetUnitCurrentHealth(),
            BuildActionSetUnitCurrentHealthPercent(),
            BuildActionSetUnitCurrentLoad(),
            BuildActionSetUnitIdentity(),
            BuildActionSetUnitActionType(),
            BuildActionSetUnitMoveSpeed(),
            BuildActionSetUnitReasonMoveSpeed(),
            BuildActionSetUnitCollisionState(),
            BuildActionSetUnitFacing(),
            BuildActionPlayUnitAnimation(),
            BuildActionDealDamage(),
            BuildActionForceLoseTarget(),
            BuildActionForceFollowUnit(),
            BuildActionAddStatus(),
            BuildActionRemoveStatus(),
            BuildActionSetStatusDuration()
        };
    }

    // =========================
    // 事件
    // =========================

    private static LogicSentenceTemplate BuildEventUnitDeath()
    {
        return BuildUnitLeafEvent(
            "event_unit_death",
            "单位死亡",
            " 死亡"
        );
    }

    private static LogicSentenceTemplate BuildEventUnitTookDamage()
    {
        return BuildUnitLeafEvent(
            "event_unit_took_damage",
            "单位被攻击",
            " 被攻击"
        );
    }

    private static LogicSentenceTemplate BuildEventUnitChangedFaction()
    {
        return BuildUnitLeafEvent(
            "event_unit_changed_faction",
            "单位改变阵营",
            " 改变阵营"
        );
    }

    private static LogicSentenceTemplate BuildEventUnitLoadBelowValue()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_unit_load_below_value",
            displayName = "单位当前负荷值低于数值",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前负荷值低于 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildIntSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventUnitIsStaggered()
    {
        return BuildUnitLeafEvent(
            "event_unit_is_staggered",
            "单位处于失衡",
            " 处于失衡"
        );
    }

    private static LogicSentenceTemplate BuildEventUnitUsedSkill()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_unit_used_skill",
            displayName = "单位使用技能",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 使用了 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "skillId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("skillId", "技能")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventUnitEnterArea()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_unit_enter_area",
            displayName = "单位进入区域",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Point, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 进入 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "area" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildAreaSlot("area", "区域")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventUnitLeaveArea()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_unit_leave_area",
            displayName = "单位离开区域",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Point, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 离开区域 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "area" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildAreaSlot("area", "区域")
            }
        };
    }

    // =========================
    // 条件
    // =========================

    private static LogicSentenceTemplate BuildConditionUnitHealthCompareValue()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_health_compare_value",
            displayName = "单位当前生命值比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前生命值 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildComparisonSlot("op", "比较符"),
                BuildIntSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitLoadCompareValue()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_load_compare_value",
            displayName = "单位当前负荷值比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前负荷值 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildComparisonSlot("op", "比较符"),
                BuildIntSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitIsDefineType()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_is_define_type",
            displayName = "单位是定义类型",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 是定义类型 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "defineType" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildEnumSlot("defineType", "定义类型")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitIsCharacterIdentity()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_is_character_identity",
            displayName = "单位是人物身份",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 是人物身份 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "identity" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildEnumSlot("identity", "人物身份")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitHasTarget()
    {
        return BuildUnitLeafCondition(
            "cond_unit_has_target",
            "单位当前有目标",
            " 当前有目标"
        );
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeUnit()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_see_unit",
            displayName = "视野：单位可以看见单位为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
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
            displayName = "视野：单位可以看见玩家为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
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

    // 2026-07-17：跟 cond_unit_can_see_player 的区别——那条要求指定一个具体单位
    // (subject) 当"观察者"，地图里敌人可能有好几个、还会动态生成/死亡，一个个配
    // 太麻烦。这条不需要指定subject，只要地图里"任意"敌对阵营(Enemy/Elite/Boss)
    // 单位当前看得见玩家就成立——给地图BGM战斗/探索切换这类"全局有没有人盯上玩家"
    // 场景用。实际判定复用 PlayerCombatStateRuntime 已经在维护的同一份信号
    // （UnitPerceptionRuntime.ActiveInstances 里任意敌对单位的 CanSeePlayer）。
    private static LogicSentenceTemplate BuildConditionAnyEnemySeesPlayer()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_any_enemy_sees_player",
            displayName = "视野：任意敌人可以看见玩家为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "任意敌人 可以看见 玩家 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildBoolSlot("expected", "是否看得见")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitCanSeeCurrentTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_see_current_target",
            displayName = "视野：单位可以看见当前目标为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
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

    private static LogicSentenceTemplate BuildConditionUnitHasStatus()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_has_status",
            displayName = "单位拥有状态",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 拥有状态 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "statusId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("statusId", "状态")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitHasNoStatus()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_has_no_status",
            displayName = "单位没有状态",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 没有状态 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "statusId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("statusId", "状态")
            }
        };
    }

    // =========================
    // 行动状态
    // =========================

    private static LogicSentenceTemplate BuildActionEnterPatrolState()
    {
        return BuildUnitActionState(
            "act_enter_patrol_state",
            "切换到巡逻状态",
            " 切换到巡逻状态"
        );
    }

    private static LogicSentenceTemplate BuildActionEnterChaseState()
    {
        return BuildUnitActionState(
            "act_enter_chase_state",
            "切换到追击状态",
            " 切换到追击状态"
        );
    }

    private static LogicSentenceTemplate BuildActionEnterEscapeState()
    {
        return BuildUnitActionState(
            "act_enter_escape_state",
            "切换到逃跑状态",
            " 切换到逃跑状态"
        );
    }

    private static LogicSentenceTemplate BuildUnitActionState(string id, string displayName, string suffix)
    {
        return new LogicSentenceTemplate
        {
            templateId = id,
            displayName = displayName,
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = suffix }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true)
            }
        };
    }

    // =========================
    // 动作
    // =========================

    private static LogicSentenceTemplate BuildConditionUnitIsAttacking()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_is_attacking",
            displayName = "单位正在攻击",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "单位正在攻击 是=" },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildBoolSlot("expected", "期望值")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitCanAttack()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_can_attack",
            displayName = "单位可以发起攻击",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "单位可以发起攻击 是=" },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildBoolSlot("expected", "期望值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionBasicAttack()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_basic_attack",
            displayName = "普通攻击",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "普通攻击" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildActionHeavyAttack()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_heavy_attack",
            displayName = "重攻击",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "重攻击" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildActionForceUnitDeath()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_force_unit_death",
            displayName = "强制单位死亡",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "强制 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 死亡" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionForceUnitReviveWithHealth()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_force_unit_revive_with_health",
            displayName = "强制单位复活并指定生命值",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "强制 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 复活，当前生命值为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "health" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildIntSlot("health", "自然整数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionForceUnitReviveWithHealthPercent()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_force_unit_revive_with_health_percent",
            displayName = "强制单位复活并指定生命百分比",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "强制 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 复活，当前生命值为最大生命值的 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "percent" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "%" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildFloatSlot("percent", "百分比")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitCurrentHealth()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_current_health",
            displayName = "更改单位当前生命值",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前生命值为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildIntSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitCurrentHealthPercent()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_current_health_percent",
            displayName = "更改单位当前生命百分比",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前生命值为最大生命值的 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "percent" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "%" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildFloatSlot("percent", "百分比")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitCurrentLoad()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_current_load",
            displayName = "更改单位当前负荷值",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前负荷值为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildIntSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitIdentity()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_identity",
            displayName = "更改单位身份",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 身份为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "identity" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildEnumSlot("identity", "人物身份")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitActionType()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_action_type",
            displayName = "更改单位行动类型",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的行动类型为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "actionType" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("actionType", "行动类型")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitMoveSpeed()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_move_speed",
            displayName = "更改单位移动速度",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的移动速度为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildFloatSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitReasonMoveSpeed()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_reason_move_speed",
            displayName = "更改单位理性移动速度",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的理性移动速度为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildFloatSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitCollisionState()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_collision_state",
            displayName = "更改单位碰撞状态",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 碰撞状态为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "enabled" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildBoolSlot("enabled", "布尔值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetUnitFacing()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_unit_facing",
            displayName = "修改单位当前朝向",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "修改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的当前朝向为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "angle" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 度" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildFloatSlot("angle", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionPlayUnitAnimation()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_play_unit_animation",
            displayName = "播放单位动画",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Time },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "播放 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "animation" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 动作，持续 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "seconds" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 秒" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("animation", "动画"),
                BuildFloatSlot("seconds", "浮点数")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionDealDamage()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_deal_damage",
            displayName = "对单位造成伤害",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "对 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 造成 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "damage" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 点 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "damageType" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 伤害，伤害来源为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "source" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("target", "单位", true, true),
                BuildFloatSlot("damage", "数值"),
                BuildStringSlot("damageType", "伤害类型"),
                BuildUnitSlot("source", "伤害来源", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionForceLoseTarget()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_force_lose_target",
            displayName = "强制单位丢失目标",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "强制 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 丢失目标" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionForceFollowUnit()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_force_follow_unit",
            displayName = "强制单位跟随单位",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "强制 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "follower" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 跟随 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "target" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("follower", "单位", true, true),
                BuildUnitSlot("target", "目标单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildActionAddStatus()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_add_status",
            displayName = "给单位增加状态",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "给 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 增加 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "statusId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("statusId", "状态")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionRemoveStatus()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_remove_status",
            displayName = "给单位删除状态",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "给 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 删除 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "statusId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("statusId", "状态")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetStatusDuration()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_status_duration",
            displayName = "更改单位状态持续时间",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.Time },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "更改 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "statusId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 持续时间为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "seconds" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 秒" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true),
                BuildStringSlot("statusId", "状态"),
                BuildFloatSlot("seconds", "数值")
            }
        };
    }

    // =========================
    // Helpers
    // =========================

    private static LogicSentenceTemplate BuildUnitLeafEvent(string id, string displayName, string suffix)
    {
        return new LogicSentenceTemplate
        {
            templateId = id,
            displayName = displayName,
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = suffix }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true)
            }
        };
    }

    private static LogicSentenceTemplate BuildUnitLeafCondition(string id, string displayName, string suffix)
    {
        return new LogicSentenceTemplate
        {
            templateId = id,
            displayName = displayName,
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Unit, LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = suffix }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitSlot("unit", "单位", true, true)
            }
        };
    }

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

    private static LogicSentenceTemplate.SlotDefinition BuildAreaSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Area,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.SceneReference,
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

    private static LogicSentenceTemplate.SlotDefinition BuildIntSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Int,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant,
                LogicValueSourceType.Variable
            }
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
                LogicValueSourceType.Variable
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildStringSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.String,
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

    private static LogicSentenceTemplate.SlotDefinition BuildEnumSlot(string slotId, string displayName)
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
            }
        };
    }
}
