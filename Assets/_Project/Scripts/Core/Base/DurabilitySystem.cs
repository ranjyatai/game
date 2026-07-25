using System;
using UnityEngine;

/// <summary>
/// 耐久度工具类。静态方法，无状态。
/// 磨损由战斗系统调用 Wear()，修复由工房 UI 调用 Repair()。
/// </summary>
public static class DurabilitySystem
{
    // ── 耐久状态枚举 ──────────────────────────────────────────────────────
    public enum DurabilityState
    {
        Full,       // 100%
        Good,       // 60–99%
        Worn,       // 30–59%  轻微性能下降
        Damaged,    // 10–29%  明显性能下降
        Broken,     // 1–9%   几乎无法使用
        Destroyed,  // 0      完全失效
    }

    // 破损阈值（百分比）
    private const float ThresholdGood    = 0.60f;
    private const float ThresholdWorn    = 0.30f;
    private const float ThresholdDamaged = 0.10f;
    private const float ThresholdBroken  = 0.01f;

    // ── 事件 ──────────────────────────────────────────────────────────────
    /// <summary>装备耐久度变化时触发（entry, oldDur, newDur）。</summary>
    public static event Action<InventoryItemEntry, int, int> OnDurabilityChanged;

    // ── 磨损 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 对装备施加耐久磨损。
    /// 消耗品或 maxDurability=0 的装备直接返回 false。
    /// </summary>
    /// <returns>磨损后是否变为 Destroyed。</returns>
    public static bool Wear(InventoryItemEntry entry, int amount = -1)
    {
        if (!HasDurability(entry)) return false;

        int loss = amount >= 0
            ? amount
            : entry.definition.equipment.durabilityLossPerHit;

        int oldDur = entry.currentDurability;
        entry.currentDurability = Mathf.Max(0, entry.currentDurability - loss);

        OnDurabilityChanged?.Invoke(entry, oldDur, entry.currentDurability);
        return entry.currentDurability == 0;
    }

    // ── 修复（工房）──────────────────────────────────────────────────────

    /// <summary>计算将装备修复到满耐久所需的代币数量。</summary>
    public static int CalcRepairCost(InventoryItemEntry entry)
    {
        if (!HasDurability(entry)) return 0;
        int missing = entry.definition.equipment.maxDurability - entry.currentDurability;
        if (missing <= 0) return 0;

        // 基础公式：每点耐久 1 代币，破损越严重单价越贵
        float stateMultiplier = GetState(entry) switch
        {
            DurabilityState.Destroyed => 3.0f,
            DurabilityState.Broken    => 2.0f,
            DurabilityState.Damaged   => 1.5f,
            _                         => 1.0f,
        };
        return Mathf.CeilToInt(missing * stateMultiplier);
    }

    /// <summary>
    /// 执行修复（工房 UI 扣完代币后调用）。
    /// </summary>
    public static void Repair(InventoryItemEntry entry)
    {
        if (!HasDurability(entry)) return;
        int oldDur = entry.currentDurability;
        entry.currentDurability = entry.definition.equipment.maxDurability;
        OnDurabilityChanged?.Invoke(entry, oldDur, entry.currentDurability);
    }

    // ── 状态查询 ──────────────────────────────────────────────────────────

    public static DurabilityState GetState(InventoryItemEntry entry)
    {
        if (!HasDurability(entry)) return DurabilityState.Full;
        int max = entry.definition.equipment.maxDurability;
        if (max <= 0) return DurabilityState.Full;
        float ratio = (float)entry.currentDurability / max;
        return GetStateFromRatio(ratio);
    }

    public static DurabilityState GetStateFromRatio(float ratio)
    {
        if (ratio >= 1f)              return DurabilityState.Full;
        if (ratio >= ThresholdGood)   return DurabilityState.Good;
        if (ratio >= ThresholdWorn)   return DurabilityState.Worn;
        if (ratio >= ThresholdDamaged)return DurabilityState.Damaged;
        if (ratio > 0f)               return DurabilityState.Broken;
        return DurabilityState.Destroyed;
    }

    /// <summary>返回 0~1 的耐久比例，无耐久系统返回 1。</summary>
    public static float GetRatio(InventoryItemEntry entry)
    {
        if (!HasDurability(entry)) return 1f;
        int max = entry.definition.equipment.maxDurability;
        return max > 0 ? Mathf.Clamp01((float)entry.currentDurability / max) : 1f;
    }

    /// <summary>是否完全损毁（无法使用）。</summary>
    public static bool IsDestroyed(InventoryItemEntry entry)
        => HasDurability(entry) && entry.currentDurability <= 0;

    /// <summary>根据耐久状态返回 UI 颜色（绿→黄→红）。</summary>
    public static UnityEngine.Color GetStateColor(DurabilityState state) => state switch
    {
        DurabilityState.Full     => new UnityEngine.Color(0.60f, 0.90f, 0.50f),
        DurabilityState.Good     => new UnityEngine.Color(0.80f, 0.95f, 0.30f),
        DurabilityState.Worn     => new UnityEngine.Color(0.95f, 0.80f, 0.20f),
        DurabilityState.Damaged  => new UnityEngine.Color(0.95f, 0.50f, 0.10f),
        DurabilityState.Broken   => new UnityEngine.Color(0.90f, 0.20f, 0.10f),
        DurabilityState.Destroyed=> new UnityEngine.Color(0.50f, 0.50f, 0.50f),
        _                        => UnityEngine.Color.white,
    };

    /// <summary>根据耐久状态返回本地化显示文字。</summary>
    public static string GetStateLabel(DurabilityState state) => state switch
    {
        DurabilityState.Full      => "完好",
        DurabilityState.Good      => "良好",
        DurabilityState.Worn      => "磨损",
        DurabilityState.Damaged   => "损坏",
        DurabilityState.Broken    => "破损",
        DurabilityState.Destroyed => "报废",
        _                         => "",
    };

    // ── 内部 ──────────────────────────────────────────────────────────────

    private static bool HasDurability(InventoryItemEntry entry)
    {
        return entry != null
            && entry.definition?.equipment != null
            && entry.definition.equipment.maxDurability > 0
            && entry.currentDurability >= 0;
    }
}
