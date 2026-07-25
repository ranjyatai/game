using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 基地管理器。单例，DontDestroyOnLoad。
/// 负责：设施等级查询、升级素材检查、交付升级、旗标触发。
/// 不持有背包引用，升级时由外部（UI）先从仓库扣除素材，再调用 Upgrade()。
/// </summary>
public class BaseManager : MonoBehaviour
{
    public static BaseManager Instance { get; private set; }

    // ── 事件 ──────────────────────────────────────────────────────────────
    /// <summary>设施升级后触发。参数为设施类型和新等级。</summary>
    public static event Action<FacilityType, int> OnFacilityUpgraded;

    // ── 设施定义库（Inspector 绑定所有 FacilityDefinition asset）─────────
    [SerializeField] private List<FacilityDefinition> facilityDefinitions = new List<FacilityDefinition>();

    private readonly Dictionary<FacilityType, FacilityDefinition> _defLookup
        = new Dictionary<FacilityType, FacilityDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        foreach (var def in facilityDefinitions)
            if (def != null) _defLookup[def.facilityType] = def;
    }

    // ── 查询接口 ──────────────────────────────────────────────────────────

    /// <summary>获取某设施当前等级（从存档读）。</summary>
    public int GetLevel(FacilityType type)
    {
        var data = GetSaveData(type);
        return data?.currentLevel ?? 0;
    }

    /// <summary>设施是否已解锁（等级 >= 1）。</summary>
    public bool IsUnlocked(FacilityType type) => GetLevel(type) >= 1;

    /// <summary>是否已是最高等级。</summary>
    public bool IsMaxLevel(FacilityType type)
    {
        if (!_defLookup.TryGetValue(type, out var def)) return true;
        return def.IsMaxLevel(GetLevel(type));
    }

    /// <summary>获取升级到下一级所需素材列表。null = 已满级或无定义。</summary>
    public List<FacilityUpgradeCost> GetNextUpgradeCosts(FacilityType type)
    {
        if (!_defLookup.TryGetValue(type, out var def)) return null;
        int nextLevel = GetLevel(type) + 1;
        return def.GetUpgradeCosts(nextLevel);
    }

    /// <summary>
    /// 检查升级到下一级的前置条件是否全部满足。
    /// 返回 true = 可以升级；false 时 unmetList 包含未达标的条件说明。
    /// </summary>
    public bool CheckPrerequisites(FacilityType type, out List<string> unmetList)
    {
        unmetList = new List<string>();

        if (!_defLookup.TryGetValue(type, out var def))
            return true; // 无定义视为无前置

        int nextLevel = GetLevel(type) + 1;
        var levelData = def.GetLevel(nextLevel);
        if (levelData == null || levelData.prerequisites == null || levelData.prerequisites.Count == 0)
            return true;

        foreach (var req in levelData.prerequisites)
        {
            int current = GetLevel(req.facilityType);
            if (current < req.minimumLevel)
            {
                var reqDef = GetDefinition(req.facilityType);
                string reqName = reqDef?.displayName ?? req.facilityType.ToString();
                unmetList.Add($"{reqName} 需达到 LV{req.minimumLevel}（当前 LV{current}）");
            }
        }

        return unmetList.Count == 0;
    }

    /// <summary>获取设施定义。</summary>
    public FacilityDefinition GetDefinition(FacilityType type)
    {
        _defLookup.TryGetValue(type, out var def);
        return def;
    }

    /// <summary>获取设施当前等级的显示数据。</summary>
    public FacilityLevelData GetCurrentLevelData(FacilityType type)
    {
        if (!_defLookup.TryGetValue(type, out var def)) return null;
        return def.GetLevel(GetLevel(type));
    }

    // ── 升级接口 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 执行设施升级。
    /// 调用前应由 UI 层确认玩家仓库中素材充足并已扣除。
    /// </summary>
    /// <returns>升级是否成功。</returns>
    public bool Upgrade(FacilityType type)
    {
        if (!_defLookup.TryGetValue(type, out var def))
        {
            Debug.LogWarning($"[BaseManager] 找不到设施定义：{type}");
            return false;
        }

        int currentLevel = GetLevel(type);
        int nextLevel    = currentLevel + 1;

        if (nextLevel > def.MaxLevel)
        {
            Debug.LogWarning($"[BaseManager] {type} 已是最高等级 {currentLevel}。");
            return false;
        }

        if (!CheckPrerequisites(type, out var unmet))
        {
            Debug.LogWarning($"[BaseManager] {type} 升级前置条件未满足：{string.Join("，", unmet)}");
            return false;
        }

        // 写入存档
        var saveData = GetOrCreateSaveData(type);
        saveData.currentLevel = nextLevel;
        saveData.discovered   = true;

        // 世界旗标
        var levelData = def.GetLevel(nextLevel);
        if (levelData != null && !string.IsNullOrEmpty(levelData.setWorldFlagOnUnlock))
            SaveManager.Player?.SetWorldFlag("base", levelData.setWorldFlagOnUnlock);

        SaveManager.Save();

        OnFacilityUpgraded?.Invoke(type, nextLevel);
        return true;
    }

    /// <summary>标记设施已被发现（首次进入基地时调用）。</summary>
    public void MarkDiscovered(FacilityType type)
    {
        var data = GetOrCreateSaveData(type);
        if (!data.discovered)
        {
            data.discovered = true;
            SaveManager.Save();
        }
    }

    // ── 内部 ──────────────────────────────────────────────────────────────

    private FacilitySaveData GetSaveData(FacilityType type)
    {
        var facilities = SaveManager.Player?.facilities;
        if (facilities == null) return null;
        foreach (var f in facilities)
            if (f.facilityType == type) return f;
        return null;
    }

    private FacilitySaveData GetOrCreateSaveData(FacilityType type)
    {
        var existing = GetSaveData(type);
        if (existing != null) return existing;

        var newData = new FacilitySaveData { facilityType = type };
        SaveManager.Player?.facilities.Add(newData);
        return newData;
    }
}
