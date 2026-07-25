using System;
using System.Collections.Generic;

/// <summary>
/// 玩家永久存档数据。写入 save_auto.json。
/// </summary>
[Serializable]
public class PlayerSaveData
{
    // ── 元信息 ─────────────────────────────────────────────────────────────
    public int    saveVersion   = 1;
    public string lastSavedTime = "";   // yyyy-MM-dd HH:mm:ss，用于 UI 显示
    public string playTimeText  = "";   // 格式化游玩时长，如 "12h 34m"，由 SaveManager.Save() 计算写入
    public float  totalPlayTimeSeconds = 0f;   // 累计游玩秒数（真实来源，playTimeText 由它格式化而来）

    // ── 背包与仓库 ─────────────────────────────────────────────────────────
    /// <summary>玩家随身背包，进本时带着，死亡时清空。</summary>
    public List<SavedItemEntry> backpack = new List<SavedItemEntry>();

    /// <summary>基地仓库，按页存储。index 对应页码（0起）。</summary>
    public List<StashPageSaveData> stashPages = new List<StashPageSaveData>();

    /// <summary>快捷物品槽绑定，按 itemKey 存（QuickSlotRuntime.SlotCount 个，null=未绑定）。</summary>
    public string[] quickSlots;

    /// <summary>2026-07-21新增：各装备槽(武器/副武器/头部/上装/下装/手部/鞋子)当前穿着的物品。
    /// 之前完全没存这个，读档后 EquipmentRuntime 永远是空的，表现是"背包/HUD以为还装备着
    /// 上次的武器，实际角色是空手模组"。</summary>
    public List<SavedEquipmentSlotEntry> equippedSlots = new List<SavedEquipmentSlotEntry>();

    /// <summary>存档时哪把武器是"生效中"（主/副），不存的话读档后永远回到主武器槽。</summary>
    public EquipmentSlotType activeWeaponSlot = EquipmentSlotType.Weapon;

    // ── 当前章节会话（为 null 表示玩家在基地）──────────────────────────────
    public ChapterSessionData activeSession;

    // ── 科技树 ─────────────────────────────────────────────────────────────
    public TechTreeSaveData techTree = new TechTreeSaveData();

    // ── 角色外观 ───────────────────────────────────────────────────────────
    public AppearanceSaveData appearance = new AppearanceSaveData();

    // ── 基地设施 ───────────────────────────────────────────────────────────
    /// <summary>每个设施的当前等级与发现状态。</summary>
    public List<FacilitySaveData> facilities = new List<FacilitySaveData>();

    // ── 任务进度 ───────────────────────────────────────────────────────────
    /// <summary>已完成/已触发的任务标记集合。</summary>
    public List<string> completedQuestFlags = new List<string>();

    // ── 世界永久状态 ───────────────────────────────────────────────────────
    /// <summary>
    /// 各本的永久世界状态标记（如"钥匙开的门"）。
    /// Key = chapterId，Value = 已触发的标记列表。
    /// </summary>
    public SerializableDictionary<string, List<string>> worldFlags
        = new SerializableDictionary<string, List<string>>();

    // ── 便捷方法 ───────────────────────────────────────────────────────────

    public bool IsInChapter => activeSession != null;

    public void SetQuestFlag(string flag)
    {
        if (!completedQuestFlags.Contains(flag))
            completedQuestFlags.Add(flag);
    }

    public bool HasQuestFlag(string flag) => completedQuestFlags.Contains(flag);

    public void SetWorldFlag(string chapterId, string flag)
    {
        if (!worldFlags.ContainsKey(chapterId))
            worldFlags[chapterId] = new List<string>();
        if (!worldFlags[chapterId].Contains(flag))
            worldFlags[chapterId].Add(flag);
    }

    public bool HasWorldFlag(string chapterId, string flag)
    {
        return worldFlags.TryGetValue(chapterId, out var flags) && flags.Contains(flag);
    }
}
