using System.Collections;
using System.Collections.Generic;
using Effekseer;
using UnityEngine;

/// <summary>
/// 物品使用效果执行器。
/// 调用方：SkyPrisonInventoryInteraction.UseItem() / QuickSlotUseController.TryUseSlot()——
/// 背包和快捷道具栏两条独立的使用入口，最终都汇合到这个方法，是唯一的公共汇合点。
/// 依赖：UnitHealthController（HP）、UnitStatusController（状态）、
///        TriggerPackageRuntime（触发器）、CurrencyRuntime（货币）。
/// LP 系统接入后在 ApplyResourceImmediate 中补充 UnitLPController 分支。
/// </summary>
public static class ItemEffectExecutor
{
    /// <summary>sourceItem 传了才会播放它身上配的 useVFX/useSE——不是所有调用场景都有
    /// 明确的"来源道具"概念（effects列表本身也能被其它系统复用），留空就跳过播放，
    /// 不影响原有的效果执行。</summary>
    public static void Execute(List<ItemEffectEntry> effects, GameObject user, ItemDefinition sourceItem = null)
    {
        if (effects == null || effects.Count == 0 || user == null) return;

        UnitHealthController health = user.GetComponentInParent<UnitHealthController>();
        UnitStatusController status = user.GetComponentInParent<UnitStatusController>();
        TriggerPackageRuntime triggers = null;

        foreach (ItemEffectEntry e in effects)
        {
            if (e == null) continue;
            switch (e.effectType)
            {
                case ItemEffectType.ResourceImmediate:
                    ApplyResourceImmediate(e, health, user);
                    break;

                case ItemEffectType.ResourceOverTime:
                    MonoBehaviour host = user.GetComponent<MonoBehaviour>()
                                        ?? user.GetComponentInParent<MonoBehaviour>();
                    if (host != null)
                        host.StartCoroutine(ResourceOverTimeRoutine(e, health, user));
                    else
                        Debug.LogWarning("[ItemEffect] 找不到 MonoBehaviour 宿主，无法开启 DoT 协程", user);
                    break;

                case ItemEffectType.ApplyStatus:
                    ApplyStatusEffect(e, status, user);
                    break;

                case ItemEffectType.RemoveStatus:
                    RemoveStatusEffect(e, status, user);
                    break;

                case ItemEffectType.ExecuteTrigger:
                    ExecuteTriggerEffect(e, triggers, user);
                    break;

                case ItemEffectType.DropRateBoost:
                    DropRateBoostRuntime.Activate(e.dropRateMultiplier, e.dropRateBoostDuration);
                    break;
            }
        }

        PlayUseVfxAndSe(sourceItem, user.transform.position);
    }

    // 跟 UnitActionModuleRuntime.PlaySkillSE 同一套惯例：不用 AudioSource.PlayClipAtPoint
    // （3D空间音源带距离衰减，角色移动几步声音就变轻），手动建一个 spatialBlend=0 的
    // 2D音源，音量只取决于算出来的倍率，不随位置/距离变化。
    private static void PlayUseVfxAndSe(ItemDefinition item, Vector3 worldPos)
    {
        if (item == null) return;

        if (item.useVFX != null)
            EffekseerSystem.PlayEffect(item.useVFX, worldPos);

        if (item.useSE != null && item.useSE.Length > 0)
        {
            SkillSoundEntry entry = item.useSE[Random.Range(0, item.useSE.Length)];
            if (entry != null && entry.clip != null)
            {
                var gs = SkyPrisonAudioGlobalSettings.Instance;
                float vol = (gs != null ? gs.masterVolume * gs.seVolume : 1f)
                            * Mathf.Max(0f, item.useSEVolume) * Mathf.Max(0f, entry.volume);

                GameObject go = new GameObject("ItemUseSE_OneShot");
                go.transform.position = worldPos;
                AudioSource source = go.AddComponent<AudioSource>();
                source.clip = entry.clip;
                source.volume = vol;
                source.spatialBlend = 0f;
                source.Play();
                Object.Destroy(go, entry.clip.length + 0.1f);
            }
        }
    }

    // ── 使用前置检查（返回 null = 可用；返回字符串 = 拦截原因）────────────────────

    /// <summary>
    /// 检查效果列表是否因资源已满而应拦截使用。
    /// 仅当道具的所有正向资源回复效果目标均已满时才拦截。
    /// 含非资源效果（施加状态/触发器等）的道具不受此限制。
    /// </summary>
    public static string GetBlockReason(List<ItemEffectEntry> effects, GameObject user)
    {
        if (effects == null || effects.Count == 0 || user == null) return null;

        UnitHealthController health = user.GetComponentInParent<UnitHealthController>();

        bool hasNonResourceEffect = false;
        bool hasPositiveHP = false;
        bool hasPositiveLP = false;

        foreach (ItemEffectEntry e in effects)
        {
            if (e == null) continue;
            bool isResource = e.effectType == ItemEffectType.ResourceImmediate
                           || e.effectType == ItemEffectType.ResourceOverTime;
            if (!isResource) { hasNonResourceEffect = true; continue; }

            float val = e.value;
            if (e.effectType == ItemEffectType.ResourceImmediate && e.valueMode == ItemEffectValueMode.Percent && health != null)
                val = health.MaxHealth * e.value * 0.01f;

            if (val <= 0f) continue; // 负值（伤害）不算正向恢复

            if (e.resourceTarget == ItemEffectResourceTarget.HP) hasPositiveHP = true;
            if (e.resourceTarget == ItemEffectResourceTarget.LP)  hasPositiveLP = true;
        }

        if (hasNonResourceEffect) return null;

        if (hasPositiveHP && health != null && health.CurrentHealth >= health.MaxHealth)
            return "HP 已满";

        if (hasPositiveLP)
            return "LP 已满";

        return null;
    }

    // ── 资源立即 ──────────────────────────────────────────────────────────────

    private static void ApplyResourceImmediate(ItemEffectEntry e, UnitHealthController health, GameObject user)
    {
        switch (e.resourceTarget)
        {
            case ItemEffectResourceTarget.HP:
                if (health == null)
                {
                    Debug.LogWarning("[ItemEffect] 找不到 UnitHealthController，HP 效果无法应用", user);
                    return;
                }
                float hpAmount = e.valueMode == ItemEffectValueMode.Percent
                    ? health.MaxHealth * e.value * 0.01f
                    : e.value;
                if (hpAmount >= 0f) health.ApplyHeal(hpAmount);
                else                health.ApplyDamage(-hpAmount);
                break;

            case ItemEffectResourceTarget.LP:
                // LP 系统占位：接入 UnitLPController 后在此补充。
                Debug.LogWarning("[ItemEffect] LP 系统尚未接入，LP 效果跳过", user);
                break;

            case ItemEffectResourceTarget.Currency:
                if (CurrencyRuntime.Instance == null || string.IsNullOrEmpty(e.currencyId)) return;
                CurrencyRuntime.Instance.Add(e.currencyId, Mathf.RoundToInt(e.value));
                break;
        }
    }

    // ── 资源持续（DoT / HoT）──────────────────────────────────────────────────

    private static IEnumerator ResourceOverTimeRoutine(ItemEffectEntry e, UnitHealthController health, GameObject user)
    {
        float interval = Mathf.Max(0.1f, e.dotInterval);
        float elapsed = 0f;
        while (elapsed < e.dotDuration)
        {
            ApplyResourceImmediate(e, health, user);
            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }
    }

    // ── 施加状态 ───────────────────────────────────────────────────────────────

    private static void ApplyStatusEffect(ItemEffectEntry e, UnitStatusController status, GameObject user)
    {
        if (e.statusToApply == null)
        {
            Debug.LogWarning("[ItemEffect] ApplyStatus：statusToApply 未设置", user);
            return;
        }
        if (status == null)
        {
            Debug.LogWarning("[ItemEffect] 找不到 UnitStatusController，状态无法施加", user);
            return;
        }
        status.ApplyStatus(e.statusToApply, user, e.statusStacks, e.statusDurationOverride);
    }

    // ── 移除状态 ───────────────────────────────────────────────────────────────

    private static void RemoveStatusEffect(ItemEffectEntry e, UnitStatusController status, GameObject user)
    {
        if (status == null)
        {
            Debug.LogWarning("[ItemEffect] 找不到 UnitStatusController，状态无法移除", user);
            return;
        }

        if (e.statusToRemove != null)
        {
            status.RemoveStatus(e.statusToRemove.statusId);
            return;
        }


        if (!e.removeAllDebuffs && !e.removeAllBuffs) return;

        var toRemove = new List<string>();
        foreach (RuntimeStatusEntry active in status.ActiveStatuses)
        {
            if (active?.definition == null) continue;
            bool isBuff = active.definition.isBuff;
            if ((e.removeAllDebuffs && !isBuff) || (e.removeAllBuffs && isBuff))
                toRemove.Add(active.definition.statusId);
        }
        foreach (string id in toRemove)
            status.RemoveStatus(id);
    }

    // ── 执行触发器 ─────────────────────────────────────────────────────────────

    private static void ExecuteTriggerEffect(ItemEffectEntry e, TriggerPackageRuntime _, GameObject user)
    {
        if (e.triggerPackage == null)
        {
            Debug.LogWarning("[ItemEffect] ExecuteTrigger：triggerPackage 未设置", user);
            return;
        }

        TriggerPackage pkg = e.triggerPackage;
        bool debug = pkg.debugLogs;

        foreach (TriggerPackage.TriggerRule rule in pkg.rules)
        {
            if (rule == null || !rule.enabled) continue;

            foreach (LogicSentenceInstance action in rule.sentenceRoot?.actionGroup?.items ?? new System.Collections.Generic.List<LogicSentenceInstance>())
            {
                if (action == null || !action.enabled) continue;
                TriggerActionExecutor.Execute(action, debug);
            }

            if (debug)
                Debug.Log($"[ItemEffect] 触发器包 \"{pkg.displayName}\" 规则 \"{rule.ruleName}\" 已执行", user);
        }
    }
}
