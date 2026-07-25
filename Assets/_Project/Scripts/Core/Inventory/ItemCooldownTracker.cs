using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物品使用冷却，按 itemKey 记，全局共享（不挂在任何窗口/UI实例上）。
/// 之前冷却状态是背包"使用"菜单那个组件（SkyPrisonInventoryInteraction）自己的实例字段，
/// 背包窗口一关就跟着销毁——同一件道具通过快捷键使用（不需要开背包）就没法跟背包菜单
/// 共享同一份冷却状态了。挪到这个静态类里，两条使用路径（背包菜单"使用"、快捷物品
/// 按键）用的是同一份冷却记录。
/// </summary>
public static class ItemCooldownTracker
{
    private static readonly Dictionary<string, float> _cooldownEnd = new Dictionary<string, float>();

    public static bool IsOnCooldown(string itemKey, out float endTime)
    {
        endTime = 0f;
        return !string.IsNullOrEmpty(itemKey) && _cooldownEnd.TryGetValue(itemKey, out endTime) && Time.time < endTime;
    }

    public static float GetRemaining(string itemKey)
    {
        if (string.IsNullOrEmpty(itemKey) || !_cooldownEnd.TryGetValue(itemKey, out float endTime)) return 0f;
        return Mathf.Max(0f, endTime - Time.time);
    }

    public static void StartCooldown(string itemKey, float durationSeconds)
    {
        if (string.IsNullOrEmpty(itemKey) || durationSeconds <= 0f) return;
        _cooldownEnd[itemKey] = Time.time + durationSeconds;
    }
}
