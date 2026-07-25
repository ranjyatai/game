using System;
using UnityEngine;

public enum ItemEffectType
{
    [InspectorName("资源-立即")]  ResourceImmediate = 0,
    [InspectorName("资源-持续")]  ResourceOverTime  = 1,
    [InspectorName("施加状态")]   ApplyStatus       = 2,
    [InspectorName("移除状态")]   RemoveStatus      = 3,
    [InspectorName("执行触发器")] ExecuteTrigger    = 4,
    [InspectorName("掉落率加成")] DropRateBoost     = 5,
}

public enum ItemEffectResourceTarget
{
    [InspectorName("HP（生命值）")] HP       = 0,
    [InspectorName("LP（耐力）")]   LP       = 1,
    [InspectorName("货币")]         Currency = 2,
}

public enum ItemEffectValueMode
{
    [InspectorName("固定值")]           Flat    = 0,
    [InspectorName("百分比（基于最大值）")] Percent = 1,
}

[Serializable]
public class ItemEffectEntry
{
    public ItemEffectType effectType = ItemEffectType.ResourceImmediate;

    // ── 资源立即 / 资源持续 ────────────────────────────────────────────────
    public ItemEffectResourceTarget resourceTarget = ItemEffectResourceTarget.HP;
    public ItemEffectValueMode valueMode = ItemEffectValueMode.Flat;
    [Tooltip("正值=恢复，负值=扣除")]
    public float value = 0f;
    [Tooltip("仅 resourceTarget=货币 时有效")]
    public string currencyId = "";

    // ── 资源持续（DoT / HoT）─────────────────────────────────────────────
    [Tooltip("每次触发间隔（秒）")]
    public float dotInterval = 1f;
    [Tooltip("持续总时长（秒）")]
    public float dotDuration = 5f;

    // ── 施加状态 ───────────────────────────────────────────────────────────
    public StatusDefinition statusToApply;
    [Tooltip("施加层数")]
    public int statusStacks = 1;
    [Tooltip("-1 使用 StatusDefinition 默认持续时间")]
    public float statusDurationOverride = -1f;

    // ── 移除状态 ───────────────────────────────────────────────────────────
    [Tooltip("指定移除某个状态；为空则按下方选项批量移除")]
    public StatusDefinition statusToRemove;
    [Tooltip("移除所有负面状态（isBuff=false）")]
    public bool removeAllDebuffs = false;
    [Tooltip("移除所有正面状态（isBuff=true）")]
    public bool removeAllBuffs = false;

    // ── 执行触发器 ─────────────────────────────────────────────────────────
    public TriggerPackage triggerPackage;

    // ── 掉落率加成 ─────────────────────────────────────────────────────────
    [Tooltip("掉落率乘数，例如 2 = 双倍掉落率")]
    [Min(1f)]
    public float dropRateMultiplier = 2f;
    [Tooltip("加成持续时间（秒）")]
    [Min(1f)]
    public float dropRateBoostDuration = 60f;
}
