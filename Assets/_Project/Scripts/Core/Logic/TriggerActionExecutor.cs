using UnityEngine;

/// <summary>
/// 触发器行动句型执行器。
/// 被 TriggerPackageRuntime 调用；未来也可以被其他系统复用。
/// </summary>
public static class TriggerActionExecutor
{
    public static bool Execute(LogicSentenceInstance sentence, bool debugLogs = false)
    {
        if (sentence == null || !sentence.enabled) return false;

        string tid = sentence.templateId;
        bool result;

        switch (tid)
        {
            // ── 物品 ──────────────────────────────────────────────────────────
            case "act_add_item_to_bag":
                result = ExecuteAddItem(sentence);
                break;

            case "act_remove_item_from_bag":
                result = ExecuteRemoveItem(sentence);
                break;

            // ── 货币 ──────────────────────────────────────────────────────────
            case "act_add_currency":
                result = ExecuteAddCurrency(sentence);
                break;

            // ── 音效 ──────────────────────────────────────────────────────────
            case "act_play_se":
                result = ExecutePlaySE(sentence);
                break;

            case "act_play_custom_se":
                result = ExecutePlayCustomSE(sentence);
                break;

            case "act_set_bgm_layer":
                result = ExecuteSetBGMLayer(sentence);
                break;

            case "act_play_bgm_track_from_start":
                result = ExecutePlayBGMTrackFromStart(sentence);
                break;

            case "act_play_bgm_track_at_time":
                result = ExecutePlayBGMTrackAtTime(sentence);
                break;

            // ── 单位生命 ──────────────────────────────────────────────────────
            case "act_set_unit_current_health":
                result = ExecuteSetUnitHealth(sentence);
                break;

            case "act_set_unit_current_health_percent":
                result = ExecuteSetUnitHealthPercent(sentence);
                break;

            case "act_force_unit_revive_with_health":
                result = ExecuteForceReviveWithHealth(sentence);
                break;

            case "act_force_unit_revive_with_health_percent":
                result = ExecuteForceReviveWithHealthPercent(sentence);
                break;

            case "act_force_unit_death":
                result = ExecuteForceUnitDeath(sentence);
                break;

            // ── 单位负荷（LP）────────────────────────────────────────────────
            case "act_set_unit_current_load":
                result = ExecuteSetUnitLoad(sentence);
                break;

            // ── 系统 ──────────────────────────────────────────────────────────
            case "act_pause_game":
                Time.timeScale = 0f;
                result = true;
                break;

            case "act_resume_game":
                Time.timeScale = 1f;
                result = true;
                break;

            case "act_trigger_autosave":
                SaveManager.AutoSave();
                result = true;
                break;

            // ── 单位行动/移动 ────────────────────────────────────────────────
            case "act_play_unit_animation":
                result = ExecutePlayUnitAnimation(sentence);
                break;

            case "act_set_unit_move_speed":
                result = ExecuteSetUnitMoveSpeed(sentence);
                break;

            case "act_set_unit_facing":
                result = ExecuteSetUnitFacing(sentence);
                break;

            case "act_set_unit_collision_state":
                result = ExecuteSetUnitCollisionState(sentence);
                break;

            case "act_deal_damage":
                result = ExecuteDealDamage(sentence);
                break;

            default:
                if (debugLogs)
                    Debug.LogWarning($"[TriggerActionExecutor] 未实现的行动句型：{tid}");
                return false;
        }

        if (debugLogs)
            Debug.Log($"[TriggerActionExecutor] {tid} → {(result ? "成功" : "失败")}");

        return result;
    }

    // ── 物品 ──────────────────────────────────────────────────────────────────

    private static bool ExecuteAddItem(LogicSentenceInstance sentence)
    {
        ItemDefinition def = GetItemSlot(sentence, "itemId");
        if (def == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_add_item_to_bag：itemId 槽位未设置或不是 ItemDefinition");
            return false;
        }

        int amount = GetIntSlot(sentence, "amount", 1);
        InventoryRuntime inv = ResolveInventory();
        if (inv == null) return false;

        inv.AddItem(def, amount);
        return true;
    }

    private static bool ExecuteRemoveItem(LogicSentenceInstance sentence)
    {
        ItemDefinition def = GetItemSlot(sentence, "itemId");
        if (def == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_remove_item_from_bag：itemId 槽位未设置或不是 ItemDefinition");
            return false;
        }

        int amount = GetIntSlot(sentence, "amount", 1);
        InventoryRuntime inv = ResolveInventory();
        if (inv == null) return false;

        return inv.RemoveItem(def, amount);
    }

    // ── 货币 ──────────────────────────────────────────────────────────────────

    private static bool ExecuteAddCurrency(LogicSentenceInstance sentence)
    {
        CurrencyDefinition def = GetSlotValue(sentence, "currencyId")?.assetReference as CurrencyDefinition;
        if (def == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_add_currency：currencyId 槽位未设置或不是 CurrencyDefinition");
            return false;
        }

        int amount = GetIntSlot(sentence, "amount", 1);
        if (CurrencyRuntime.Instance == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] 找不到 CurrencyRuntime，请确认场景中存在货币运行时。");
            return false;
        }

        CurrencyRuntime.Instance.Add(def.currencyId, amount);
        return true;
    }

    // ── 音效 ──────────────────────────────────────────────────────────────────

    private static bool ExecutePlaySE(LogicSentenceInstance sentence)
    {
        LogicSlotValue v = GetSlotValue(sentence, "seType");
        string typeName = v != null && !string.IsNullOrWhiteSpace(v.enumValue) ? v.enumValue : v?.stringValue;

        if (string.IsNullOrWhiteSpace(typeName))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_play_se：seType 未设置");
            return false;
        }

        if (!System.Enum.TryParse(typeName, true, out SkyPrisonSystemSEType seType) || seType == SkyPrisonSystemSEType.None)
        {
            Debug.LogWarning($"[TriggerActionExecutor] act_play_se：未知 SE 类型 \"{typeName}\"");
            return false;
        }

        SkyPrisonSystemSEPlayer.Play(seType);
        return true;
    }

    private static bool ExecutePlayCustomSE(LogicSentenceInstance sentence)
    {
        LogicSlotValue clipSlot = GetSlotValue(sentence, "clip");
        if (clipSlot?.assetReference is not UnityEngine.AudioClip clip)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_play_custom_se：clip 未设置或不是 AudioClip");
            return false;
        }

        float volume = GetFloatSlot(sentence, "volume", 1f);
        float pitch  = GetFloatSlot(sentence, "pitch",  1f);
        if (Mathf.Approximately(volume, 0f)) volume = 1f;
        if (Mathf.Approximately(pitch,  0f)) pitch  = 1f;
        SkyPrisonSystemSEPlayer.PlayClip(clip, volume, pitch);
        return true;
    }

    private static bool ExecuteSetBGMLayer(LogicSentenceInstance sentence)
    {
        LogicSlotValue v = GetSlotValue(sentence, "layer");
        string layer = v != null && !string.IsNullOrWhiteSpace(v.enumValue) ? v.enumValue : v?.stringValue;
        if (string.IsNullOrWhiteSpace(layer))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_bgm_layer：layer 未设置");
            return false;
        }

        if (MapBGMController.Instance == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_bgm_layer：场景里没有找到 MapBGMController。");
            return false;
        }

        MapBGMController.Instance.SetLayer(layer);
        return true;
    }

    private static bool ExecutePlayBGMTrackFromStart(LogicSentenceInstance sentence)
    {
        LogicSlotValue clipSlot = GetSlotValue(sentence, "clip");
        if (clipSlot?.assetReference is not UnityEngine.AudioClip clip)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_play_bgm_track_from_start：clip 未设置或不是 AudioClip");
            return false;
        }

        if (MapBGMController.Instance == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_play_bgm_track_from_start：场景里没有找到 MapBGMController。");
            return false;
        }

        MapBGMController.Instance.PlaySpecificClip(clip, 0f);
        return true;
    }

    private static bool ExecutePlayBGMTrackAtTime(LogicSentenceInstance sentence)
    {
        LogicSlotValue clipSlot = GetSlotValue(sentence, "clip");
        if (clipSlot?.assetReference is not UnityEngine.AudioClip clip)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_play_bgm_track_at_time：clip 未设置或不是 AudioClip");
            return false;
        }

        if (MapBGMController.Instance == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_play_bgm_track_at_time：场景里没有找到 MapBGMController。");
            return false;
        }

        int minutes = GetIntSlot(sentence, "minutes", 0);
        int seconds = GetIntSlot(sentence, "seconds", 0);
        float startTime = Mathf.Max(0, minutes) * 60f + Mathf.Max(0, seconds);

        MapBGMController.Instance.PlaySpecificClip(clip, startTime);
        return true;
    }

    // ── 单位生命 ──────────────────────────────────────────────────────────────

    private static bool ExecuteSetUnitHealth(LogicSentenceInstance sentence)
    {
        UnitHealthController health = ResolveUnitHealth(sentence, "unit");
        if (health == null) return false;
        int value = GetIntSlot(sentence, "value", 0);
        health.SetCurrentHealth(value);
        return true;
    }

    private static bool ExecuteSetUnitHealthPercent(LogicSentenceInstance sentence)
    {
        UnitHealthController health = ResolveUnitHealth(sentence, "unit");
        if (health == null) return false;
        float pct = GetFloatSlot(sentence, "percent", 1f) / 100f;
        health.SetCurrentHealth(health.MaxHealth * Mathf.Clamp01(pct));
        return true;
    }

    private static bool ExecuteForceReviveWithHealth(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        int value = GetIntSlot(sentence, "health", 1);
        UnitDeathController death = go.GetComponent<UnitDeathController>();
        UnitHealthController health = go.GetComponent<UnitHealthController>();

        if (death != null && death.IsWaitingForRespawn)
            death.Respawn();

        if (health != null)
            health.SetCurrentHealth(value);

        return true;
    }

    private static bool ExecuteForceReviveWithHealthPercent(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        float pct = GetFloatSlot(sentence, "percent", 1f) / 100f;
        UnitDeathController death = go.GetComponent<UnitDeathController>();
        UnitHealthController health = go.GetComponent<UnitHealthController>();

        if (death != null && death.IsWaitingForRespawn)
            death.Respawn();

        if (health != null)
            health.SetCurrentHealth(health.MaxHealth * Mathf.Clamp01(pct));

        return true;
    }

    private static bool ExecuteForceUnitDeath(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        UnitDeathController death = go.GetComponent<UnitDeathController>();
        if (death == null || !death.IsAlive) return false;
        death.Kill();
        return true;
    }

    // ── 单位负荷（LP）────────────────────────────────────────────────────────

    private static bool ExecuteSetUnitLoad(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        UnitLoadRuntime load = go.GetComponent<UnitLoadRuntime>();
        if (load == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_unit_current_load：单位没有 UnitLoadRuntime 组件");
            return false;
        }

        int value = GetIntSlot(sentence, "value", 0);
        load.SetCurrentLoad(value);
        return true;
    }

    // ── 单位行动/移动 ────────────────────────────────────────────────────────

    private static bool ExecutePlayUnitAnimation(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        LogicSlotValue v = GetSlotValue(sentence, "animation");
        string trigger = v != null && !string.IsNullOrWhiteSpace(v.enumValue) ? v.enumValue : v?.stringValue;
        if (string.IsNullOrWhiteSpace(trigger))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_play_unit_animation：animation 未设置");
            return false;
        }

        Animator animator = go.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            Debug.LogWarning($"[TriggerActionExecutor] 单位 {go.name} 没有 Animator 组件");
            return false;
        }

        animator.SetTrigger(trigger);
        return true;
    }

    // 真正驱动移动的是 TerrainGroundMotorV5（UnitMovementController 只算不落地那条死路径，
    // 见项目脚注"Movement Active Path"），改速度必须改这个组件的字段才会真的生效。
    private static bool ExecuteSetUnitMoveSpeed(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        TerrainGroundMotorV5 motor = go.GetComponentInChildren<TerrainGroundMotorV5>(true);
        if (motor == null)
        {
            Debug.LogWarning($"[TriggerActionExecutor] 单位 {go.name} 没有 TerrainGroundMotorV5 组件");
            return false;
        }

        float value = GetFloatSlot(sentence, "value", motor.moveSpeed);
        motor.moveSpeed = Mathf.Max(0f, value);
        return true;
    }

    private static bool ExecuteSetUnitFacing(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        float angle = GetFloatSlot(sentence, "angle", 0f);
        go.transform.rotation = Quaternion.Euler(0f, angle, 0f);
        return true;
    }

    private static bool ExecuteSetUnitCollisionState(LogicSentenceInstance sentence)
    {
        GameObject go = ResolveUnit(sentence, "unit");
        if (go == null) return false;

        LogicSlotValue v = GetSlotValue(sentence, "enabled");
        bool enabled = v != null && v.boolValue;

        Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            Debug.LogWarning($"[TriggerActionExecutor] 单位 {go.name} 没有任何 Collider 组件");
            return false;
        }

        foreach (Collider c in colliders)
            c.enabled = enabled;
        return true;
    }

    private static bool ExecuteDealDamage(LogicSentenceInstance sentence)
    {
        GameObject target = ResolveUnit(sentence, "target");
        if (target == null) return false;

        UnitHealthController health = target.GetComponentInChildren<UnitHealthController>(true);
        if (health == null)
        {
            Debug.LogWarning($"[TriggerActionExecutor] 单位 {target.name} 没有 UnitHealthController 组件");
            return false;
        }

        float damage = GetFloatSlot(sentence, "damage", 0f);
        if (damage <= 0f) return false;

        // 走正常受伤管线（ApplyDamage），会触发受击反馈/死亡判定，
        // 跟"更改当前生命值"那种直接赋值不是一回事。
        health.ApplyDamage(damage);
        return true;
    }

    // ── 工具 ──────────────────────────────────────────────────────────────────

    private static InventoryRuntime ResolveInventory()
    {
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv == null)
            Debug.LogWarning("[TriggerActionExecutor] 找不到 InventoryRuntime，请确认场景中存在 InventoryRuntimeBootstrap。");
        return inv;
    }

    private static ItemDefinition GetItemSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = GetSlotValue(sentence, slotId);
        return v?.assetReference as ItemDefinition;
    }

    private static int GetIntSlot(LogicSentenceInstance sentence, string slotId, int fallback)
    {
        LogicSlotValue v = GetSlotValue(sentence, slotId);
        if (v == null) return fallback;
        return v.EvaluateInt();
    }

    private static float GetFloatSlot(LogicSentenceInstance sentence, string slotId, float fallback)
    {
        LogicSlotValue v = GetSlotValue(sentence, slotId);
        if (v == null) return fallback;
        return v.EvaluateFloat();
    }

    private static LogicSlotValue GetSlotValue(LogicSentenceInstance sentence, string slotId)
    {
        return sentence.GetAssignment(slotId)?.value;
    }

    // ── 单位解析 ──────────────────────────────────────────────────────────────

    private static UnitHealthController ResolveUnitHealth(LogicSentenceInstance sentence, string slotId)
    {
        GameObject go = ResolveUnit(sentence, slotId);
        if (go == null) return null;

        UnitHealthController health = go.GetComponent<UnitHealthController>();
        if (health == null)
            Debug.LogWarning($"[TriggerActionExecutor] 单位 {go.name} 没有 UnitHealthController 组件");

        return health;
    }

    private static GameObject ResolveUnit(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = GetSlotValue(sentence, slotId);
        if (v == null) return null;

        if (v.sourceType == LogicValueSourceType.SceneReference)
        {
            string objectId = v.sceneObjectId;
            if (string.IsNullOrWhiteSpace(objectId))
            {
                Debug.LogWarning($"[TriggerActionExecutor] 单位槽 {slotId}：sceneObjectId 为空");
                return null;
            }

            // 优先匹配 UnitDefinitionId，其次匹配 SceneUnitGuid
            foreach (SkyPrisonSceneUnitMarker marker in Object.FindObjectsByType<SkyPrisonSceneUnitMarker>(FindObjectsSortMode.None))
            {
                if (marker.UnitDefinitionId == objectId || marker.SceneUnitGuid == objectId)
                    return marker.gameObject;
            }

            Debug.LogWarning($"[TriggerActionExecutor] 找不到 ID 为 {objectId} 的场景单位");
            return null;
        }

        Debug.LogWarning($"[TriggerActionExecutor] 单位槽 {slotId} 来源类型 {v.sourceType} 暂不支持");
        return null;
    }
}
