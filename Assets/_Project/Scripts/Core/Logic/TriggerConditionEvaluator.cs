using UnityEngine;

/// <summary>
/// 触发器条件句型求值器。被 TriggerPackageRuntime 调用，取代之前"永远返回true"的
/// 占位实现（2026-07-17之前 EvaluateCondition 是个没写完的TODO桩函数）。
///
/// 目前只实现了跟视野相关的两条——"cond_unit_can_see_player"/
/// "cond_any_enemy_sees_player"，其余 templateId 一律按"条件不成立"处理并打警告，
/// 不会悄悄放行（避免漏实现的条件被误判成"永远true"，那样等于没检查）。
/// </summary>
public static class TriggerConditionEvaluator
{
    public static bool Evaluate(LogicSentenceInstance sentence, bool debugLogs = false)
    {
        if (sentence == null || !sentence.enabled)
            return false;

        string tid = sentence.templateId;
        bool result;

        switch (tid)
        {
            case "cond_unit_can_see_player":
                result = EvaluateUnitCanSeePlayer(sentence);
                break;

            case "cond_any_enemy_sees_player":
                result = EvaluateAnyEnemySeesPlayer(sentence);
                break;

            default:
                if (debugLogs)
                    Debug.LogWarning($"[TriggerConditionEvaluator] 未实现的条件句型：{tid}，按不成立处理。");
                return false;
        }

        if (debugLogs)
            Debug.Log($"[TriggerConditionEvaluator] {tid} → {result}");

        return result;
    }

    private static bool EvaluateUnitCanSeePlayer(LogicSentenceInstance sentence)
    {
        GameObject subject = ResolveUnit(sentence, "subject");
        bool expected = GetBoolSlot(sentence, "expected", true);
        if (subject == null)
            return false;

        UnitPerceptionRuntime perception = subject.GetComponentInChildren<UnitPerceptionRuntime>(true);
        if (perception == null)
        {
            Debug.LogWarning($"[TriggerConditionEvaluator] 单位 {subject.name} 没有 UnitPerceptionRuntime 组件，cond_unit_can_see_player 判定为false。");
            return false == expected;
        }

        return perception.CanSeePlayer == expected;
    }

    private static bool EvaluateAnyEnemySeesPlayer(LogicSentenceInstance sentence)
    {
        bool expected = GetBoolSlot(sentence, "expected", true);
        return PlayerCombatStateRuntime.IsInCombat == expected;
    }

    // ── 工具（跟 TriggerActionExecutor 里的同名逻辑重复，故意没抽公共方法——
    // 两边各自独立演化，避免为了复用一个小helper而在两个完全不同职责的类之间
    // 建立耦合）──────────────────────────────────────────────────────────────

    private static bool GetBoolSlot(LogicSentenceInstance sentence, string slotId, bool fallback)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v?.boolValue ?? fallback;
    }

    private static GameObject ResolveUnit(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        if (v == null)
            return null;

        if (v.sourceType == LogicValueSourceType.SceneReference)
        {
            string objectId = v.sceneObjectId;
            if (string.IsNullOrWhiteSpace(objectId))
            {
                Debug.LogWarning($"[TriggerConditionEvaluator] 单位槽 {slotId}：sceneObjectId 为空");
                return null;
            }

            foreach (SkyPrisonSceneUnitMarker marker in Object.FindObjectsByType<SkyPrisonSceneUnitMarker>(FindObjectsSortMode.None))
            {
                if (marker.UnitDefinitionId == objectId || marker.SceneUnitGuid == objectId)
                    return marker.gameObject;
            }

            Debug.LogWarning($"[TriggerConditionEvaluator] 找不到 ID 为 {objectId} 的场景单位");
            return null;
        }

        Debug.LogWarning($"[TriggerConditionEvaluator] 单位槽 {slotId} 来源类型 {v.sourceType} 暂不支持");
        return null;
    }
}
