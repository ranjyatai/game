using UnityEngine;
using SkyPrison.Runtime.UI;

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

            // ── 任务标记 ──────────────────────────────────────────────────────
            case "act_set_quest_flag":
                result = ExecuteSetQuestFlag(sentence);
                break;

            case "act_increment_counter":
                result = ExecuteIncrementCounter(sentence);
                break;

            // ── 自定义变量 ────────────────────────────────────────────────────
            case "act_set_variable_bool":
                result = ExecuteSetVariableBool(sentence);
                break;

            case "act_set_variable_int":
                result = ExecuteSetVariableInt(sentence);
                break;

            case "act_set_variable_float":
                result = ExecuteSetVariableFloat(sentence);
                break;

            case "act_set_variable_string":
                result = ExecuteSetVariableString(sentence);
                break;

            // ── 台词字幕 ──────────────────────────────────────────────────────
            case "act_show_dialogue_line":
                result = ExecuteShowDialogueLine(sentence);
                break;

            // ── 商店 ──────────────────────────────────────────────────────────
            case "act_open_shop":
                result = ExecuteOpenShop(sentence);
                break;

            // ── 装备 ──────────────────────────────────────────────────────────
            case "act_equip_item":
                result = ExecuteEquipItem(sentence);
                break;

            case "act_unequip_item":
                result = ExecuteUnequipItem(sentence);
                break;

            case "act_cycle_active_weapon":
                result = ExecuteCycleActiveWeapon();
                break;

            // ── 货币 ──────────────────────────────────────────────────────────
            case "act_spend_currency":
                result = ExecuteSpendCurrency(sentence);
                break;

            // ── 音频 ──────────────────────────────────────────────────────────
            case "act_stop_looping_se":
                SkyPrisonSystemSEPlayer.StopLoop();
                result = true;
                break;

            // ── 本地化 ────────────────────────────────────────────────────────
            case "act_set_language":
                result = ExecuteSetLanguage(sentence);
                break;

            case "act_set_voice_language":
                result = ExecuteSetVoiceLanguage(sentence);
                break;

            // ── 镜头 ──────────────────────────────────────────────────────────
            case "act_camera_shake":
                result = ExecuteCameraShake(sentence);
                break;

            // ── 天气 ──────────────────────────────────────────────────────────
            case "act_set_weather_active":
                result = ExecuteSetWeatherActive(sentence);
                break;

            // ── RAID 入口 ─────────────────────────────────────────────────────
            case "act_set_raid_entry":
                result = ExecuteSetRaidEntry(sentence);
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
        bool enabled = v != null && v.EvaluateBool();

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

    // ── 任务标记 ──────────────────────────────────────────────────────────────

    private static bool ExecuteSetQuestFlag(LogicSentenceInstance sentence)
    {
        string flag = GetSlotValue(sentence, "flag")?.EvaluateString();
        if (string.IsNullOrWhiteSpace(flag))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_quest_flag：flag 槽位为空");
            return false;
        }

        if (SaveManager.Player == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_quest_flag：SaveManager.Player 尚未初始化");
            return false;
        }

        SaveManager.Player.SetQuestFlag(flag);
        return true;
    }

    private static bool ExecuteIncrementCounter(LogicSentenceInstance sentence)
    {
        string counterId = GetSlotValue(sentence, "counterId")?.EvaluateString();
        if (string.IsNullOrWhiteSpace(counterId))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_increment_counter：counterId 槽位为空");
            return false;
        }

        if (SaveManager.Player == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_increment_counter：SaveManager.Player 尚未初始化");
            return false;
        }

        int amount = GetIntSlot(sentence, "amount", 1);
        SaveManager.Player.IncrementCounter(counterId, amount);
        return true;
    }

    // ── 自定义变量 ────────────────────────────────────────────────────────────
    // "variable" 槽位存的是目标变量名(variableKey)，"value" 槽位才是真正求值的
    // 那一个——两者不是同一件事，variable槽位本身的value.EvaluateXxx()没有意义
    // (它的来源被限死成Variable，读出来的还是它自己指向的那个变量当前值，不是
    // "要写入的名字")，所以这里直接读 variableKey，不走 Evaluate。

    private static bool ExecuteSetVariableBool(LogicSentenceInstance sentence)
    {
        string variableName = GetSlotValue(sentence, "variable")?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_variable_bool：variable 槽位未选择变量");
            return false;
        }

        bool value = GetSlotValue(sentence, "value")?.EvaluateBool() ?? false;
        SkyPrisonVariableRuntime.SetBool(variableName, value);
        return true;
    }

    private static bool ExecuteSetVariableInt(LogicSentenceInstance sentence)
    {
        string variableName = GetSlotValue(sentence, "variable")?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_variable_int：variable 槽位未选择变量");
            return false;
        }

        int value = GetIntSlot(sentence, "value", 0);
        SkyPrisonVariableRuntime.SetInt(variableName, value);
        return true;
    }

    private static bool ExecuteSetVariableFloat(LogicSentenceInstance sentence)
    {
        string variableName = GetSlotValue(sentence, "variable")?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_variable_float：variable 槽位未选择变量");
            return false;
        }

        float value = GetFloatSlot(sentence, "value", 0f);
        SkyPrisonVariableRuntime.SetFloat(variableName, value);
        return true;
    }

    private static bool ExecuteSetVariableString(LogicSentenceInstance sentence)
    {
        string variableName = GetSlotValue(sentence, "variable")?.variableKey;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_variable_string：variable 槽位未选择变量");
            return false;
        }

        string value = GetSlotValue(sentence, "value")?.EvaluateString() ?? "";
        SkyPrisonVariableRuntime.SetString(variableName, value);
        return true;
    }

    // ── 台词字幕 ──────────────────────────────────────────────────────────────

    private static bool ExecuteShowDialogueLine(LogicSentenceInstance sentence)
    {
        LogicSlotValue v = GetSlotValue(sentence, "line");
        DialogueSentenceLibrary library = v?.assetReference as DialogueSentenceLibrary;
        if (library == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_show_dialogue_line：line 槽位未设置句子库");
            return false;
        }

        string sentenceId = v.EvaluateString();
        if (string.IsNullOrWhiteSpace(sentenceId))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_show_dialogue_line：line 槽位未选择句子");
            return false;
        }

        DialogueSubtitleHUD.Instance?.ShowLine(library, sentenceId);
        return true;
    }

    // ── 商店 ──────────────────────────────────────────────────────────────────
    // 跟 DialogueOption(actionType=打开窗口) 走的是同一条路径(SkyPrisonWindowManager_V1.
    // Open + IDialogueWindowPayloadReceiver.ReceiveDialoguePayload)，只是窗口Prefab
    // 这里直接写死加载 Resources/UI/Window/PF_SkyPrisonShop——项目里商店窗口只有
    // 这一份Prefab，不用像对话选项那样另外配一个"选哪个窗口"的字段。

    private static bool ExecuteOpenShop(LogicSentenceInstance sentence)
    {
        ShopDefinition shop = GetSlotValue(sentence, "shop")?.assetReference as ShopDefinition;
        if (shop == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_open_shop：shop 槽位未设置或不是 ShopDefinition");
            return false;
        }

        GameObject prefab = Resources.Load<GameObject>("UI/Window/PF_SkyPrisonShop");
        if (prefab == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_open_shop：找不到 Resources/UI/Window/PF_SkyPrisonShop");
            return false;
        }

        SkyPrisonWindowManager_V1 manager = Object.FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (manager == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_open_shop：场景里没有找到 SkyPrisonWindowManager_V1");
            return false;
        }

        GameObject opened = manager.Open(prefab);
        opened?.GetComponent<IDialogueWindowPayloadReceiver>()?.ReceiveDialoguePayload(shop);
        return opened != null;
    }

    // ── 装备 ──────────────────────────────────────────────────────────────────
    // EquipmentRuntime 是玩家专属单例，不需要单位槽位。

    private static bool ExecuteEquipItem(LogicSentenceInstance sentence)
    {
        ItemDefinition item = GetItemSlot(sentence, "item");
        if (item == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_equip_item：item 槽位未设置或不是 ItemDefinition");
            return false;
        }

        InventoryRuntime inv = ResolveInventory();
        if (inv == null || EquipmentRuntime.Instance == null) return false;

        InventoryItemEntry entry = null;
        foreach (InventoryItemEntry e in inv.Slots)
        {
            if (e != null && !e.IsEmpty && e.definition == item) { entry = e; break; }
        }

        if (entry == null)
        {
            Debug.LogWarning($"[TriggerActionExecutor] act_equip_item：背包里没有「{item.itemKey}」");
            return false;
        }

        return EquipmentRuntime.Instance.TryEquipFromInventory(inv, entry);
    }

    private static bool ExecuteUnequipItem(LogicSentenceInstance sentence)
    {
        if (EquipmentRuntime.Instance == null) return false;

        InventoryRuntime inv = ResolveInventory();
        if (inv == null) return false;

        EquipmentSlotType slot = GetEquipmentSlotSlot(sentence, "slot");
        return EquipmentRuntime.Instance.TryUnequipToInventory(inv, slot);
    }

    private static bool ExecuteCycleActiveWeapon()
    {
        if (EquipmentRuntime.Instance == null) return false;
        EquipmentRuntime.Instance.CycleActiveWeapon();
        return true;
    }

    // ── 货币 ──────────────────────────────────────────────────────────────────

    private static bool ExecuteSpendCurrency(LogicSentenceInstance sentence)
    {
        CurrencyDefinition currency = GetSlotValue(sentence, "currency")?.assetReference as CurrencyDefinition;
        if (currency == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_spend_currency：currency 槽位未设置或不是 CurrencyDefinition");
            return false;
        }

        if (CurrencyRuntime.Instance == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] 找不到 CurrencyRuntime，请确认场景中存在货币运行时。");
            return false;
        }

        int amount = GetIntSlot(sentence, "amount", 0);
        return CurrencyRuntime.Instance.Spend(currency.currencyId, amount);
    }

    // ── 本地化 ────────────────────────────────────────────────────────────────

    private static bool ExecuteSetLanguage(LogicSentenceInstance sentence)
    {
        string code = GetSlotValue(sentence, "languageCode")?.EvaluateString();
        if (string.IsNullOrWhiteSpace(code) || LocalizationRuntime.Instance == null) return false;
        LocalizationRuntime.Instance.SetLanguage(code);
        return true;
    }

    private static bool ExecuteSetVoiceLanguage(LogicSentenceInstance sentence)
    {
        string code = GetSlotValue(sentence, "languageCode")?.EvaluateString();
        if (string.IsNullOrWhiteSpace(code) || LocalizationRuntime.Instance == null) return false;
        LocalizationRuntime.Instance.SetVoiceLanguage(code);
        return true;
    }

    // ── 镜头 ──────────────────────────────────────────────────────────────────

    private static bool ExecuteCameraShake(LogicSentenceInstance sentence)
    {
        float intensity = GetFloatSlot(sentence, "intensity", 0.3f);
        float duration = GetFloatSlot(sentence, "duration", 0.3f);
        SkyPrisonScreenShake.Shake(intensity, duration);
        return true;
    }

    // ── 天气 ──────────────────────────────────────────────────────────────────

    private static bool ExecuteSetWeatherActive(LogicSentenceInstance sentence)
    {
        if (MapWeatherController.Instance == null)
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_weather_active：场景里没有找到 MapWeatherController。");
            return false;
        }

        bool active = GetSlotValue(sentence, "active")?.EvaluateBool() ?? true;
        MapWeatherController.Instance.SetActive(active);
        return true;
    }

    private static bool ExecuteSetRaidEntry(LogicSentenceInstance sentence)
    {
        string target = GetSlotValue(sentence, "target")?.EvaluateString();
        string state = GetSlotValue(sentence, "state")?.EvaluateString();
        if (string.IsNullOrWhiteSpace(target))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_raid_entry：target 槽位为空");
            return false;
        }
        if (!SkyPrisonRaidEntry.Set(target, state != "disabled"))
        {
            Debug.LogWarning("[TriggerActionExecutor] act_set_raid_entry：SaveManager.Player 尚未初始化");
            return false;
        }
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

    private static EquipmentSlotType GetEquipmentSlotSlot(LogicSentenceInstance sentence, string slotId)
    {
        LogicSlotValue v = GetSlotValue(sentence, slotId);
        if (v != null && !string.IsNullOrWhiteSpace(v.enumValue) && System.Enum.TryParse(v.enumValue, out EquipmentSlotType slot))
            return slot;
        return EquipmentSlotType.Weapon;
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
