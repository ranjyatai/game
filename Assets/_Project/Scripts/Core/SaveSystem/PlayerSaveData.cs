using System;
using System.Collections.Generic;

[Serializable]
public class SavedCurrencyEntry
{
    public string currencyId;
    public long amount;
}

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

    /// <summary>玩家当前血量百分比（0~1）。换场景 / 读档后用它还原血量，医疗站才有意义。-1 = 没有记录（当作满血）。
    /// 由 PlayerHealthCarryover 定时写入。</summary>
    public float playerHealthPercent = -1f;

    /// <summary>各种货币的持有量（背包窗口底部那一排）。之前完全没存，换图 / 重开游戏都会归零。</summary>
    public List<SavedCurrencyEntry> currencies = new List<SavedCurrencyEntry>();

    // ── 物品持有记录 ──────────────────────────────────────
    /// <summary>曾经持有过的物品 itemKey。选关界面的「主要产出」里，没持有过的物品显示成「?」。</summary>
    public List<string> discoveredItemKeys = new List<string>();

    // ── RAID 入口状态（触发器 act_set_raid_entry 写入）──────────
    /// <summary>被触发器设为「可进入」的入口 key（raidId_day / raidId_night / tower）。</summary>
    public List<string> raidEntryEnabled = new List<string>();
    /// <summary>被触发器设为「不可进入」的入口 key。</summary>
    public List<string> raidEntryDisabled = new List<string>();

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

    /// <summary>QuestRuntime 记账用：进行中的任务questId列表。</summary>
    public List<string> activeQuestIds = new List<string>();

    /// <summary>QuestRuntime 记账用：已完成的任务questId列表。</summary>
    public List<string> completedQuestIds = new List<string>();

    /// <summary>玩家标记的"追踪中任务"——同一时间只有一条，供以后地图画面显示追踪
    /// 用。留空="没有设置追踪中任务"。只是一个记账用的questId，具体这条任务是否
    /// 还有效(进行中/存在)由QuestRuntime查询时自己判断，这里不做校验。</summary>
    public string trackedQuestId = "";

    /// <summary>任务目标用的通用计数器（击杀数/探索点数之类累加型进度）。跟
    /// completedQuestFlags 那种"有没有"的flag不是一回事——这个是能持续累加、
    /// 也会被查询比较大小的数字，key由触发器/任务作者自己定，不是固定枚举。</summary>
    public SerializableDictionary<string, int> questCounters
        = new SerializableDictionary<string, int>();

    /// <summary>任务开始那一刻，给它依赖的计数器(击杀数/对话次数等)记的"起点快照"——
    /// key = "{questId}::{counterKey}"。计数器本身是从游戏一开始永久累加、不分任务的
    /// (比如"跟琪亚拉对话过"这个计数器，接任务这次对话本身也会让它+1)，任务目标判定时
    /// 只看"现在的值 - 起点快照"是否达标，不看绝对值，这样任务开始之前(包括接任务
    /// 这个动作本身)发生的事不会被误判成"已经推进了目标"。</summary>
    public SerializableDictionary<string, int> questCounterBaselines
        = new SerializableDictionary<string, int>();

    /// <summary>任务目标一旦被判定为"完成"过一次，就永久锁存在这里——即使这个目标
    /// 自己的完成条件后来又变回不满足(典型场景："收集×N"后面接一条"交付×N给NPC"，
    /// 交付会把背包里那N个物品扣掉，如果每次都重新查背包余量，"收集"那一步会在
    /// 交付之后又变回未完成，任务永远卡死没法结算)，也不会退回"未完成"。
    /// key = "{questId}::{目标在objectives里的下标}"，任务开始/放弃/完成时清空
    /// (见QuestRuntime.StartQuest/AbandonQuest/CompleteQuest)。</summary>
    public SerializableDictionary<string, bool> questObjectiveLatches
        = new SerializableDictionary<string, bool>();

    // ── 自定义变量 ─────────────────────────────────────────────────────────
    /// <summary>触发器/任务用的自定义变量——只存标量类型(Bool/Int/Float/String)，
    /// 永久持久化。Unit/Item类型的变量绑的是当前会话的具体实例/引用，回基地后那个
    /// 实例本身就不存在了，不写存档，运行时另外存一份(见 SkyPrisonVariableRuntime)。</summary>
    public SerializableDictionary<string, bool> boolVariables
        = new SerializableDictionary<string, bool>();
    public SerializableDictionary<string, int> intVariables
        = new SerializableDictionary<string, int>();
    public SerializableDictionary<string, float> floatVariables
        = new SerializableDictionary<string, float>();
    public SerializableDictionary<string, string> stringVariables
        = new SerializableDictionary<string, string>();

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

    public int GetCounter(string counterId)
    {
        return questCounters.TryGetValue(counterId, out int value) ? value : 0;
    }

    public void IncrementCounter(string counterId, int amount)
    {
        questCounters[counterId] = GetCounter(counterId) + amount;
    }

    private static string CounterBaselineKey(string questId, string counterId) => $"{questId}::{counterId}";

    public int GetCounterBaseline(string questId, string counterId)
    {
        return questCounterBaselines.TryGetValue(CounterBaselineKey(questId, counterId), out int value) ? value : 0;
    }

    public void SetCounterBaseline(string questId, string counterId, int value)
    {
        questCounterBaselines[CounterBaselineKey(questId, counterId)] = value;
    }

    /// <summary>任务完成/放弃后清掉它名下的起点快照——避免这个字典随着玩过的任务数量
    /// 无限增长，快照本身也只在任务"进行中"这段时间有意义。</summary>
    public void ClearCounterBaselines(string questId)
    {
        string prefix = questId + "::";
        var toRemove = new List<string>();
        foreach (string key in questCounterBaselines.Keys)
            if (key.StartsWith(prefix)) toRemove.Add(key);
        foreach (string key in toRemove)
            questCounterBaselines.Remove(key);
    }

    private static string ObjectiveLatchKey(string questId, int objectiveIndex) => $"{questId}::{objectiveIndex}";

    public bool IsObjectiveLatched(string questId, int objectiveIndex)
        => questObjectiveLatches.TryGetValue(ObjectiveLatchKey(questId, objectiveIndex), out bool value) && value;

    public void LatchObjective(string questId, int objectiveIndex)
        => questObjectiveLatches[ObjectiveLatchKey(questId, objectiveIndex)] = true;

    /// <summary>任务开始/放弃时清掉它名下的目标锁存——不能在"完成"时也清：任务日志
    /// "已完成"分页复用的还是同一套实时判定(IsObjectiveEffectivelyComplete)，没有
    /// 锁存记忆的话，"收集"这类看背包即时数量的目标会因为道具已经交付/用掉了(背包
    /// 里现在是0个)重新判定成"未完成"，节点链显示成卡在半路，明明整条任务早就
    /// 结算过了。锁存只应该在"放弃任务"(真的要回退到最初状态)时清，任务开始时
    /// 顺手清一次是防御性的(正常情况下上一轮早清过，不应该有残留)。</summary>
    public void ClearObjectiveLatches(string questId)
    {
        string prefix = questId + "::";
        var toRemove = new List<string>();
        foreach (string key in questObjectiveLatches.Keys)
            if (key.StartsWith(prefix)) toRemove.Add(key);
        foreach (string key in toRemove)
            questObjectiveLatches.Remove(key);
    }
}
