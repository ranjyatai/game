using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 基地设施定义。每个设施对应一个 asset。
/// 创建路径：Sky Prison / Base / Facility Definition
/// </summary>
[CreateAssetMenu(
    fileName = "FacilityDef_",
    menuName  = "Sky Prison/Base/Facility Definition",
    order     = 500)]
public class FacilityDefinition : ScriptableObject
{
    [Header("基本信息")]
    public FacilityType facilityType;

    [Tooltip("设施显示名称，例如「工房」")]
    public string displayName;

    [TextArea(2, 3)]
    public string summary;

    [Header("等级数据（index = 等级编号）")]
    [Tooltip("levels[0] = LV0（初始状态），levels[1] = LV1（第一次升级后）…")]
    public List<FacilityLevelData> levels = new List<FacilityLevelData>();

    // ── 查询接口 ──────────────────────────────────────────────────────────

    public int MaxLevel => levels.Count - 1;

    /// <summary>获取指定等级数据，超出范围返回 null。</summary>
    public FacilityLevelData GetLevel(int level)
    {
        if (level < 0 || level >= levels.Count) return null;
        return levels[level];
    }

    /// <summary>获取升级到 targetLevel 所需的素材列表，null = 已是最高或无定义。</summary>
    public List<FacilityUpgradeCost> GetUpgradeCosts(int targetLevel)
    {
        var data = GetLevel(targetLevel);
        return data?.upgradeCosts;
    }

    /// <summary>是否已是最高等级。</summary>
    public bool IsMaxLevel(int currentLevel) => currentLevel >= MaxLevel;
}
