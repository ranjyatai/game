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
    // ── 任务目标"起点快照"上下文 ──────────────────────────────────────────
    // 击杀数/对话次数这类计数器是从游戏一开始永久累加、不分任务的，任务目标判定
    // 时不能直接看绝对值(接任务这个动作本身，或者接任务之前发生的事，都不该被
    // 算进"这个目标推进了多少")。QuestDefinition.AreRequiredObjectivesComplete()
    // 判定这一批目标之前会把questId设进这个上下文，判定完再清掉——同一帧内的
    // 同步调用，不涉及多线程，不用担心并发。只有走这个上下文判定的计数器类条件
    // 才会扣起点快照，其它调用方(触发器/解锁条件/商店条件等，不传这个上下文)
    // 完全不受影响，行为跟以前一样看绝对值。
    private static string _activeQuestBaselineContext;

    public static void BeginQuestObjectiveContext(string questId) => _activeQuestBaselineContext = questId;
    public static void EndQuestObjectiveContext() => _activeQuestBaselineContext = null;

    private static int GetCounterSinceBaseline(string counterId)
    {
        int current = SaveManager.Player.GetCounter(counterId);
        if (_activeQuestBaselineContext == null) return current;
        return current - SaveManager.Player.GetCounterBaseline(_activeQuestBaselineContext, counterId);
    }

    /// <summary>某个条件句是不是"计数器型"、以及它读的是哪个计数器key——
    /// QuestRuntime.StartQuest() 用这个给任务目标依赖的计数器记起点快照，不用
    /// 针对每种模板各写一遍"怎么算counterKey"。</summary>
    public static string GetCounterKeyForBaseline(LogicSentenceInstance sentence)
    {
        if (sentence == null) return null;
        switch (sentence.templateId)
        {
            case "cond_counter_at_least":
                return GetStringSlot(sentence, "counterId");
            case "cond_unit_kill_count_at_least":
            {
                UnitDefinition unit = sentence.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
                return unit != null && !string.IsNullOrWhiteSpace(unit.unitId) ? $"kill_unit_{unit.unitId}" : null;
            }
            case "cond_has_talked_to_npc":
            {
                UnitDefinition unit = sentence.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
                return unit != null && !string.IsNullOrWhiteSpace(unit.unitId) ? $"talked_npc_{unit.unitId}" : null;
            }
            case "cond_has_delivered_item_to_npc":
                return GetDeliveredItemCounterKey(sentence);
            default:
                return null;
        }
    }

    /// <summary>交付计数器key由"物品+交付对象"这两个真实资产引用现拼，不用额外加一个
    /// 自由文本的counterId槽位手动填——跟kill_unit_{unitId}/talked_npc_{unitId}
    /// 同一个道理，只要这两个引用没换过，key天然稳定唯一。物品/NPC没配全时返回
    /// null(等于"这条条件不是计数器型"，QuestRuntime.StartQuest不会给它记起点快照，
    /// EvaluateHasDeliveredItemToNpc自己也会因为拿不到counterId直接判不成立)。</summary>
    private static string GetDeliveredItemCounterKey(LogicSentenceInstance sentence)
    {
        ItemDefinition item = sentence.GetAssignment("item")?.value?.assetReference as ItemDefinition;
        UnitDefinition npc = sentence.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
        if (item == null) return null; // itemId是int(有默认值)，不是string，没有"没填"这个状态，只判空引用
        if (npc == null || string.IsNullOrWhiteSpace(npc.unitId)) return null;
        return $"delivered_item_{item.itemId}_to_{npc.unitId}";
    }

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

            case "cond_has_quest_flag":
                result = EvaluateHasQuestFlag(sentence);
                break;

            case "cond_counter_at_least":
                result = EvaluateCounterAtLeast(sentence);
                break;

            case "cond_has_item_count_at_least":
                result = EvaluateHasItemCountAtLeast(sentence);
                break;

            case "cond_player_near_scene_object":
                result = EvaluatePlayerNearSceneObject(sentence);
                break;

            case "cond_player_in_region":
                result = EvaluatePlayerInRegion(sentence);
                break;

            case "cond_variable_bool_equals":
                result = EvaluateVariableBoolEquals(sentence);
                break;

            case "cond_variable_int_compare":
                result = EvaluateVariableIntCompare(sentence);
                break;

            case "cond_variable_float_compare":
                result = EvaluateVariableFloatCompare(sentence);
                break;

            case "cond_variable_string_equals":
                result = EvaluateVariableStringEquals(sentence);
                break;

            case "cond_shop_has_stock":
                result = EvaluateShopHasStock(sentence);
                break;

            case "cond_quest_state_is":
                result = EvaluateQuestStateIs(sentence);
                break;

            case "cond_unit_kill_count_at_least":
                result = EvaluateUnitKillCountAtLeast(sentence);
                break;

            case "cond_has_talked_to_npc":
                result = EvaluateHasTalkedToNpc(sentence);
                break;

            case "cond_has_delivered_item_to_npc":
                result = EvaluateHasDeliveredItemToNpc(sentence);
                break;

            case "cond_unit_status_stack_count_compare":
                result = EvaluateUnitStatusStackCountCompare(sentence);
                break;

            case "cond_unit_status_remaining_duration_compare":
                result = EvaluateUnitStatusRemainingDurationCompare(sentence);
                break;

            case "cond_has_equipped_slot":
                result = EvaluateHasEquippedSlot(sentence);
                break;

            case "cond_currency_amount_compare":
                result = EvaluateCurrencyAmountCompare(sentence);
                break;

            case "cond_save_slot_exists":
                result = EvaluateSaveSlotExists(sentence);
                break;

            case "cond_current_chapter_is":
                result = EvaluateCurrentChapterIs(sentence);
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

    private static bool EvaluateHasQuestFlag(LogicSentenceInstance sentence)
    {
        string flag = GetStringSlot(sentence, "flag");
        bool expected = GetBoolSlot(sentence, "expected", true);

        if (string.IsNullOrWhiteSpace(flag))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_has_quest_flag：flag 槽位为空");
            return false;
        }

        bool actual = SaveManager.Player != null && SaveManager.Player.HasQuestFlag(flag);
        return actual == expected;
    }

    private static bool EvaluateCounterAtLeast(LogicSentenceInstance sentence)
    {
        string counterId = GetStringSlot(sentence, "counterId");
        int count = GetIntSlot(sentence, "count");

        if (string.IsNullOrWhiteSpace(counterId))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_counter_at_least：counterId 槽位为空");
            return false;
        }

        if (SaveManager.Player == null)
            return false;

        return GetCounterSinceBaseline(counterId) >= count;
    }

    private static bool EvaluateHasItemCountAtLeast(LogicSentenceInstance sentence)
    {
        ItemDefinition item = GetItemSlot(sentence, "item");
        int count = GetIntSlot(sentence, "count");

        if (item == null)
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_has_item_count_at_least：item 槽位为空");
            return false;
        }

        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        return inv != null && inv.HasItem(item, count);
    }

    private static bool EvaluatePlayerNearSceneObject(LogicSentenceInstance sentence)
    {
        GameObject target = ResolveUnit(sentence, "target");
        float radius = GetFloatSlot(sentence, "radius", 2f);

        if (target == null)
            return false;

        GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        if (playerGo == null)
            return false;

        return Vector3.Distance(playerGo.transform.position, target.transform.position) <= radius;
    }

    private static bool EvaluatePlayerInRegion(LogicSentenceInstance sentence)
    {
        SkyPrisonSceneRegionMarker region = ResolveRegion(sentence, "region");
        bool expected = GetBoolSlot(sentence, "expected", true);

        if (region == null)
            return false == expected;

        GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        bool actual = playerGo != null && region.ContainsPoint(playerGo.transform.position);
        return actual == expected;
    }

    private static SkyPrisonSceneRegionMarker ResolveRegion(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        if (v == null || string.IsNullOrWhiteSpace(v.sceneObjectId))
            return null;

        foreach (SkyPrisonSceneRegionMarker marker in Object.FindObjectsByType<SkyPrisonSceneRegionMarker>(FindObjectsSortMode.None))
        {
            if (marker.SceneRegionGuid == v.sceneObjectId)
                return marker;
        }

        Debug.LogWarning($"[TriggerConditionEvaluator] 找不到 ID 为 {v.sceneObjectId} 的区域");
        return null;
    }

    private static bool EvaluateVariableBoolEquals(LogicSentenceInstance sentence)
    {
        string variableName = sentence.GetAssignment("variable")?.value?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_variable_bool_equals：variable 槽位未选择变量");
            return false;
        }

        bool expected = GetBoolSlot(sentence, "expected", true);
        return SkyPrisonVariableRuntime.GetBool(variableName) == expected;
    }

    private static bool EvaluateVariableIntCompare(LogicSentenceInstance sentence)
    {
        string variableName = sentence.GetAssignment("variable")?.value?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_variable_int_compare：variable 槽位未选择变量");
            return false;
        }

        LogicComparisonOperator op = GetComparisonOperatorSlot(sentence, "op");
        int value = GetIntSlot(sentence, "value");
        return EvaluateComparison(op, SkyPrisonVariableRuntime.GetInt(variableName), value);
    }

    private static bool EvaluateVariableFloatCompare(LogicSentenceInstance sentence)
    {
        string variableName = sentence.GetAssignment("variable")?.value?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_variable_float_compare：variable 槽位未选择变量");
            return false;
        }

        LogicComparisonOperator op = GetComparisonOperatorSlot(sentence, "op");
        float value = GetFloatSlot(sentence, "value", 0f);
        return EvaluateComparison(op, SkyPrisonVariableRuntime.GetFloat(variableName), value);
    }

    private static bool EvaluateVariableStringEquals(LogicSentenceInstance sentence)
    {
        string variableName = sentence.GetAssignment("variable")?.value?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_variable_string_equals：variable 槽位未选择变量");
            return false;
        }

        string expected = GetStringSlot(sentence, "value") ?? "";
        return SkyPrisonVariableRuntime.GetString(variableName) == expected;
    }

    private static bool EvaluateShopHasStock(LogicSentenceInstance sentence)
    {
        ShopDefinition shop = GetShopSlot(sentence, "shop");
        ItemDefinition item = GetItemSlot(sentence, "item");

        if (shop == null || item == null)
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_shop_has_stock：shop 或 item 槽位为空");
            return false;
        }

        foreach (ShopItemEntry entry in shop.items)
            if (entry != null && entry.item == item)
                return !entry.IsOutOfStock;

        return false; // 这家店压根不卖这件东西
    }

    private static bool EvaluateUnitKillCountAtLeast(LogicSentenceInstance sentence)
    {
        UnitDefinition unit = sentence.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
        if (unit == null || string.IsNullOrWhiteSpace(unit.unitId))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_unit_kill_count_at_least：unit 槽位为空或没有unitId");
            return false;
        }

        int count = GetIntSlot(sentence, "count");
        if (SaveManager.Player == null)
            return false;

        return GetCounterSinceBaseline($"kill_unit_{unit.unitId}") >= count;
    }

    private static bool EvaluateHasTalkedToNpc(LogicSentenceInstance sentence)
    {
        UnitDefinition unit = sentence.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
        if (unit == null || string.IsNullOrWhiteSpace(unit.unitId))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_has_talked_to_npc：unit 槽位为空或没有unitId");
            return false;
        }

        if (SaveManager.Player == null)
            return false;

        // 跟 kill_unit_ 是同一套"NPCDialogueInteractable.Interact() 打开对话时自增"的
        // 计数器约定，只看有没有发生过(>=1)，不需要"对话次数"这种概念。
        return GetCounterSinceBaseline($"talked_npc_{unit.unitId}") >= 1;
    }

    private static bool EvaluateHasDeliveredItemToNpc(LogicSentenceInstance sentence)
    {
        string counterKey = GetDeliveredItemCounterKey(sentence);
        int count = GetIntSlot(sentence, "count");

        if (counterKey == null)
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_has_delivered_item_to_npc：item/npc 槽位为空或缺少id。");
            return false;
        }

        if (SaveManager.Player == null)
            return false;

        // 计数器由 NPCDialogueWindowController 的交付确认窗在玩家点"确认交付"那一刻
        // 手动加count(见ConfirmDeliveryPopup)，不是背包数量的实时快照——交付是"给
        // 出去"这个动作本身，给完之后哪怕背包又通过其它途径重新有了这个物品，也不该
        // 让这个目标变回未完成(跟"收集"类目标那种活的、随身上数量涨跌的判定不是
        // 同一回事)。
        return GetCounterSinceBaseline(counterKey) >= count;
    }

    private static bool EvaluateQuestStateIs(LogicSentenceInstance sentence)
    {
        QuestDefinition quest = GetQuestSlot(sentence, "quest");
        if (quest == null || string.IsNullOrWhiteSpace(quest.questId))
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_quest_state_is：quest 槽位为空");
            return false;
        }

        if (SaveManager.Player == null)
            return false;

        bool completed = SaveManager.Player.completedQuestIds.Contains(quest.questId);
        bool active = !completed && SaveManager.Player.activeQuestIds.Contains(quest.questId);
        string state = sentence.GetAssignment("state")?.value?.enumValue;

        return state switch
        {
            "Completed" => completed,
            "Active" => active,
            "NotStarted" => !completed && !active,
            _ => false
        };
    }

    private static bool EvaluateUnitStatusStackCountCompare(LogicSentenceInstance sentence)
    {
        GameObject unit = ResolveUnit(sentence, "target");
        string statusId = GetStringSlot(sentence, "statusId");
        if (unit == null || string.IsNullOrWhiteSpace(statusId))
            return false;

        UnitStatusController status = unit.GetComponentInChildren<UnitStatusController>(true);
        int stacks = status?.GetStatus(statusId)?.stackCount ?? 0;

        LogicComparisonOperator op = GetComparisonOperatorSlot(sentence, "op");
        int value = GetIntSlot(sentence, "value");
        return EvaluateComparison(op, stacks, value);
    }

    private static bool EvaluateUnitStatusRemainingDurationCompare(LogicSentenceInstance sentence)
    {
        GameObject unit = ResolveUnit(sentence, "target");
        string statusId = GetStringSlot(sentence, "statusId");
        if (unit == null || string.IsNullOrWhiteSpace(statusId))
            return false;

        UnitStatusController status = unit.GetComponentInChildren<UnitStatusController>(true);
        RuntimeStatusEntry entry = status?.GetStatus(statusId);
        float remaining = entry != null ? entry.remainingDuration : 0f;

        LogicComparisonOperator op = GetComparisonOperatorSlot(sentence, "op");
        float value = GetFloatSlot(sentence, "value", 0f);
        return EvaluateComparison(op, remaining, value);
    }

    private static bool EvaluateHasEquippedSlot(LogicSentenceInstance sentence)
    {
        EquipmentSlotType slot = GetEquipmentSlotSlot(sentence, "slot");
        bool expected = GetBoolSlot(sentence, "expected", true);

        bool actual = EquipmentRuntime.Instance != null && EquipmentRuntime.Instance.IsEquipped(slot);
        return actual == expected;
    }

    private static bool EvaluateCurrencyAmountCompare(LogicSentenceInstance sentence)
    {
        CurrencyDefinition currency = GetCurrencySlot(sentence, "currency");
        if (currency == null)
        {
            Debug.LogWarning("[TriggerConditionEvaluator] cond_currency_amount_compare：currency 槽位为空");
            return false;
        }

        long amount = CurrencyRuntime.Instance != null ? CurrencyRuntime.Instance.Get(currency.currencyId) : 0;
        LogicComparisonOperator op = GetComparisonOperatorSlot(sentence, "op");
        int value = GetIntSlot(sentence, "value");
        return EvaluateComparison(op, amount, value);
    }

    private static bool EvaluateSaveSlotExists(LogicSentenceInstance sentence)
    {
        int slot = GetIntSlot(sentence, "slot");
        return SaveManager.HasSave(slot);
    }

    private static bool EvaluateCurrentChapterIs(LogicSentenceInstance sentence)
    {
        string chapterId = GetStringSlot(sentence, "chapterId");
        return SaveManager.Player?.activeSession?.chapterId == chapterId;
    }

    private static LogicComparisonOperator GetComparisonOperatorSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        if (v != null && !string.IsNullOrWhiteSpace(v.enumValue) && System.Enum.TryParse(v.enumValue, out LogicComparisonOperator op))
            return op;
        return LogicComparisonOperator.Equal;
    }

    private static bool EvaluateComparison(LogicComparisonOperator op, float a, float b)
    {
        return op switch
        {
            LogicComparisonOperator.Greater => a > b,
            LogicComparisonOperator.Less => a < b,
            LogicComparisonOperator.Equal => Mathf.Approximately(a, b),
            LogicComparisonOperator.NotEqual => !Mathf.Approximately(a, b),
            LogicComparisonOperator.GreaterOrEqual => a >= b,
            LogicComparisonOperator.LessOrEqual => a <= b,
            LogicComparisonOperator.StrictEqual => a == b,
            _ => false
        };
    }

    // ── 工具（跟 TriggerActionExecutor 里的同名逻辑重复，故意没抽公共方法——
    // 两边各自独立演化，避免为了复用一个小helper而在两个完全不同职责的类之间
    // 建立耦合）──────────────────────────────────────────────────────────────

    private static bool GetBoolSlot(LogicSentenceInstance sentence, string slotId, bool fallback)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v != null ? v.EvaluateBool() : fallback;
    }

    private static string GetStringSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v?.EvaluateString();
    }

    private static int GetIntSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v?.EvaluateInt() ?? 0;
    }

    private static float GetFloatSlot(LogicSentenceInstance sentence, string slotId, float fallback)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v != null ? v.EvaluateFloat() : fallback;
    }

    private static ItemDefinition GetItemSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v?.assetReference as ItemDefinition;
    }

    private static ShopDefinition GetShopSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v?.assetReference as ShopDefinition;
    }

    private static QuestDefinition GetQuestSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v?.assetReference as QuestDefinition;
    }

    private static CurrencyDefinition GetCurrencySlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        return v?.assetReference as CurrencyDefinition;
    }

    private static EquipmentSlotType GetEquipmentSlotSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = sentence.GetAssignment(slotId)?.value;
        if (v != null && !string.IsNullOrWhiteSpace(v.enumValue) && System.Enum.TryParse(v.enumValue, out EquipmentSlotType slot))
            return slot;
        return EquipmentSlotType.Weapon;
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
