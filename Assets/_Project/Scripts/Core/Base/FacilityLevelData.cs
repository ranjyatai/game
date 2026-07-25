using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 某个等级的设施数据：升级所需素材 + 该等级解锁的功能。
/// 内嵌在 FacilityDefinition 里，不单独创建 asset。
/// </summary>
[Serializable]
public class FacilityLevelData
{
    [Header("等级标识")]
    [Tooltip("等级编号，从 0 开始。LV0 = 初始未解锁状态")]
    public int level;

    [Tooltip("该等级在 UI 上显示的名称，例如「简陋工房」「标准工房」")]
    public string displayName;

    [TextArea(2, 4)]
    [Tooltip("该等级的描述，显示在设施升级界面")]
    public string description;

    [Header("前置条件（所有条件同时满足才允许升级）")]
    [Tooltip("其他设施必须达到的最低等级。留空 = 无前置。")]
    public List<FacilityPrerequisite> prerequisites = new List<FacilityPrerequisite>();

    [Header("升级到此等级所需素材（LV0 无需素材，天然存在）")]
    public List<FacilityUpgradeCost> upgradeCosts = new List<FacilityUpgradeCost>();

    [Header("解锁内容（设计备注，不直接驱动逻辑）")]
    [Tooltip("解锁的功能说明，例如「解锁商店购买」「新增稀有品池」")]
    public List<string> unlockedFeatures = new List<string>();

    [Header("剧情/世界旗标")]
    [Tooltip("升级到此等级时自动设置的世界旗标 key（可留空）")]
    public string setWorldFlagOnUnlock = "";
}

[Serializable]
public class FacilityUpgradeCost
{
    [Tooltip("素材物品的 itemKey（与 ItemDefinition 的 key 对应）")]
    public string itemKey;

    [Tooltip("需要的数量")]
    [Min(1)]
    public int count = 1;
}

[Serializable]
public class FacilityPrerequisite
{
    [Tooltip("要求的设施类型")]
    public FacilityType facilityType;

    [Tooltip("该设施必须达到的最低等级")]
    [Min(1)]
    public int minimumLevel = 1;
}
