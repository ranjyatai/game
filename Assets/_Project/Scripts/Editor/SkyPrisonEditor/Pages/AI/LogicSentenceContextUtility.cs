using System.Collections.Generic;

[System.Flags]
public enum LogicTemplateContext
{
    AI = 1 << 0,
    Trigger = 1 << 1,
    Cutscene = 1 << 2,
    Debug = 1 << 3,
}

public enum LogicSubjectPolicy
{
    FixedSelf = 0,
    FlexibleUnit = 1,
    Global = 2,
}

public static class LogicSentenceContextUtility
{
    private static readonly HashSet<string> AIAllowedTemplateIds = new HashSet<string>
    {
        "event_initialize", "event_every_seconds", "event_unit_see_unit",
        "cond_unit_to_unit_distance_compare", "cond_unit_no_current_target", "cond_universal_compare",
        "cond_unit_can_see_unit",
        "cond_unit_can_see_perception_target", "cond_unit_can_hear_perception_target", "cond_unit_has_detected_perception_target",
        "cond_unit_is_suspicious", "cond_unit_is_alert",
        "cond_unit_is_attacking", "cond_unit_can_attack",
        "cond_unit_has_detected_player",
        "cond_unit_has_last_heard_position", "cond_unit_has_last_known_position",
        "act_move_to_target", "act_move_to_xy",
        "act_move_unit_to_target_with_move_mode", "act_move_unit_to_xy_with_move_mode",
        "act_move_unit_away_from_target", "act_move_unit_random_in_radius",
        "act_move_unit_to_last_heard_position", "act_move_unit_to_last_known_position", "act_move_unit_to_last_perception_position",
        "act_set_current_target",
        // 以下旧 id 保留用于兼容存量数据，不在选择器中显示
        "act_set_current_target_to_player", "act_set_current_target_to_last_detected_target", "act_set_current_target_to_last_perceived_target",
        "act_set_current_target_to_last_hearing_target", "act_set_current_target_to_last_hostile_target", "act_clear_current_target",
        "act_stop_unit_move",
        "act_wait_seconds", "act_switch_ai_package",
        "flow_if_then", "flow_if_then_else", "flow_while", "flow_do_while", "flow_loop", "flow_break_loop", "flow_comment", "flow_code",
        "act_execute_macro",
        "event_unit_load_below_value", "event_unit_used_skill", "event_unit_death", "event_unit_took_damage", "event_unit_changed_faction", "event_unit_is_staggered",
        "cond_unit_health_compare_value", "cond_unit_load_compare_value", "cond_unit_is_define_type", "cond_unit_is_character_identity", "cond_unit_has_status", "cond_unit_has_no_status",
        "act_basic_attack", "act_heavy_attack", "act_force_unit_death", "act_force_unit_revive_with_health", "act_force_unit_revive_with_health_percent",
        "act_set_unit_current_health", "act_set_unit_current_health_percent", "act_set_unit_current_load", "act_set_unit_identity",
        "act_set_unit_action_type", "act_set_unit_move_speed", "act_set_unit_reason_move_speed", "act_set_unit_collision_state", "act_set_unit_facing", "act_play_unit_animation",
        "act_deal_damage", "act_force_lose_target", "act_force_follow_unit", "act_add_status", "act_remove_status", "act_set_status_duration",
        "cond_ready_to_attack_target",
        "act_move_unit_to_spawn_position",
        "cond_unit_to_current_target_distance_compare", "cond_unit_current_target_exists",
        "cond_unit_has_detected_hostile_unit", "cond_unit_can_see_hostile_unit",
        "cond_unit_behavior_type_is", "cond_unit_has_move_target", "cond_unit_move_failed_by_stuck",
    };

    // 触发器中隐藏的 AI 专属词条（感知/移动/AI 状态机/AI 目标管理）
    private static readonly HashSet<string> TriggerBlockedTemplateIds = new HashSet<string>
    {
        // AI 初始化与周期事件（触发器有自己的事件系统）
        "event_initialize",

        // AI 感知条件
        "cond_unit_can_see_unit",
        "cond_unit_can_see_perception_target",
        "cond_unit_can_hear_perception_target",
        "cond_unit_has_detected_perception_target",
        "cond_unit_is_suspicious",
        "cond_unit_is_alert",
        "cond_unit_has_last_heard_position",
        "cond_unit_has_last_known_position",
        "cond_unit_to_current_target_distance_compare",
        "cond_unit_no_current_target",
        "cond_unit_current_target_exists",
        "cond_unit_behavior_type_is",

        // AI 事件（感知/视野类）
        "event_unit_see_unit",

        // AI 移动动作
        "act_move_to_target",
        "act_move_to_xy",
        "act_move_unit_to_target_with_move_mode",
        "act_move_unit_to_xy_with_move_mode",
        "act_move_unit_away_from_target",
        "act_move_unit_random_in_radius",
        "act_move_unit_to_last_heard_position",
        "act_move_unit_to_last_known_position",
        "act_move_unit_to_last_perception_position",
        "act_stop_unit_move",

        // AI 目标管理
        "act_set_current_target_to_player",
        "act_set_current_target_to_last_detected_target",
        "act_set_current_target_to_last_perceived_target",
        "act_set_current_target_to_last_hearing_target",
        "act_set_current_target_to_last_hostile_target",
        "act_clear_current_target",
        "act_force_lose_target",
        "act_force_follow_unit",

        // AI 行为状态机
        "act_set_behavior_type",
        "act_enter_patrol_state",
        "act_enter_chase_state",
        "act_enter_escape_state",

        // AI 攻击状态条件
        "cond_unit_is_attacking",
        "cond_unit_can_attack",

        // AI 专属动作
        "act_switch_ai_package",
        "act_execute_macro",
        "act_heavy_attack",
        "act_basic_attack",
    };

    public static bool IsTemplateAllowed(LogicTemplateContext context, LogicSentenceTemplate template)
    {
        if (template == null || string.IsNullOrWhiteSpace(template.templateId))
            return false;

        if (context == LogicTemplateContext.AI)
            return AIAllowedTemplateIds.Contains(template.templateId);

        if (context == LogicTemplateContext.Trigger)
            return !TriggerBlockedTemplateIds.Contains(template.templateId);

        return true;
    }

    public static string GetContextDisplayName(LogicTemplateContext context)
    {
        return context switch
        {
            LogicTemplateContext.AI => "AI",
            LogicTemplateContext.Trigger => "触发器",
            LogicTemplateContext.Cutscene => "剧情",
            LogicTemplateContext.Debug => "调试",
            _ => "逻辑"
        };
    }

    public static bool IsFixedSelfUnitSlot(LogicTemplateContext context, LogicSentenceTemplate.SlotDefinition slot)
    {
        if (context != LogicTemplateContext.AI || slot == null)
            return false;

        if (slot.valueType != LogicSlotValueType.Unit)
            return false;

        string id = slot.slotId ?? "";
        return id == "subject" || id == "unit" || id == "unitA";
    }

    public static bool IsCurrentTargetUnitSlot(LogicTemplateContext context, LogicSentenceTemplate.SlotDefinition slot)
    {
        if (context != LogicTemplateContext.AI || slot == null)
            return false;

        if (slot.valueType != LogicSlotValueType.Unit)
            return false;

        // AI 中只锁定“执行者/主语”，不锁定目标槽。
        // target / unitB / source 应保持可编辑，默认可由用户选择“当前目标、最后看见单位、最后攻击来源”等上下文对象。
        return false;
    }

    public static LogicSlotValue BuildFixedSelfUnitValue()
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Unit,
            sourceType = LogicValueSourceType.ContextReference,
            contextKey = "self",
            sceneObjectName = "该单位"
        };
    }

    public static LogicSlotValue BuildCurrentTargetUnitValue()
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Unit,
            sourceType = LogicValueSourceType.ContextReference,
            contextKey = "current_target",
            sceneObjectName = "当前目标"
        };
    }

    public static string GetFixedSlotDisplayText(LogicTemplateContext context, LogicSentenceTemplate.SlotDefinition slot)
    {
        if (IsFixedSelfUnitSlot(context, slot))
            return "该单位";

        if (IsCurrentTargetUnitSlot(context, slot))
            return "当前目标";

        return null;
    }
}
