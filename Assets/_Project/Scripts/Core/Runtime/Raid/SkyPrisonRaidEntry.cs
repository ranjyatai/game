using System.Collections.Generic;

/// <summary>
/// RAID 入口的「可进入 / 不可进入」状态，由触发器行动句型 act_set_raid_entry 写入，随存档保存。
/// 入口按「RAID × 昼夜」拆开（旧工厂昼、旧工厂夜、学校昼……），塔单独一个。
/// 没被触发器设置过的入口，仍按 RaidDefinition 自己的解锁条件判断；设置过的以这里为准。
/// </summary>
public static class SkyPrisonRaidEntry
{
    /// <summary>触发器里能选的入口：value（存档用的 key，= raidId + _day / _night）, 显示名。</summary>
    public static readonly string[][] Targets =
    {
        new[] { "old_factory_day",    "旧工厂 · 昼" },
        new[] { "old_factory_night",  "旧工厂 · 夜" },
        new[] { "school_day",         "学校 · 昼" },
        new[] { "school_night",       "学校 · 夜" },
        new[] { "golden_land_day",    "黄金乡 · 昼" },
        new[] { "golden_land_night",  "黄金乡 · 夜" },
        new[] { "hospital_day",       "废病院 · 昼" },
        new[] { "hospital_night",     "废病院 · 夜" },
        new[] { "tower",              "塔" },
    };

    public static string Key(string raidId, bool night) { return raidId == "tower" ? "tower" : raidId + (night ? "_night" : "_day"); }   // 塔没有昼夜之分

    /// <summary>触发器设置过：true = 可进入，false = 不可进入；没设置过 = null（交给 RaidDefinition 自己的条件）。</summary>
    public static bool? Get(string key)
    {
        var p = SaveManager.Player;
        if (p == null || string.IsNullOrEmpty(key)) return null;
        if (p.raidEntryEnabled != null && p.raidEntryEnabled.Contains(key)) return true;
        if (p.raidEntryDisabled != null && p.raidEntryDisabled.Contains(key)) return false;
        return null;
    }

    /// <summary>夜间模式有没有出现过：任意一个夜版被触发器开放过就算（科技树「夜间模式开启」条件用）。编辑器 / Dev 的全解锁测试开关也算。</summary>
    public static bool AnyNightEnabled()
    {
        if (RaidDefinition.UnlockAllForTesting && (UnityEngine.Application.isEditor || UnityEngine.Debug.isDebugBuild)) return true;
        var p = SaveManager.Player;
        if (p == null || p.raidEntryEnabled == null) return false;
        for (int i = 0; i < p.raidEntryEnabled.Count; i++)
            if (p.raidEntryEnabled[i] != null && p.raidEntryEnabled[i].EndsWith("_night")) return true;
        return false;
    }

    public static bool Set(string key, bool enterable)
    {
        var p = SaveManager.Player;
        if (p == null || string.IsNullOrEmpty(key)) return false;
        if (p.raidEntryEnabled == null) p.raidEntryEnabled = new List<string>();
        if (p.raidEntryDisabled == null) p.raidEntryDisabled = new List<string>();
        p.raidEntryEnabled.Remove(key); p.raidEntryDisabled.Remove(key);
        (enterable ? p.raidEntryEnabled : p.raidEntryDisabled).Add(key);
        return true;
    }
}
