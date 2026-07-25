using System;

/// <summary>
/// 单个设施的持久化状态。存在 PlayerSaveData.facilities 里。
/// </summary>
[Serializable]
public class FacilitySaveData
{
    public FacilityType facilityType;

    /// <summary>当前等级。0 = 初始未激活。</summary>
    public int currentLevel = 0;

    /// <summary>是否已被玩家发现（用于首次抵达基地时的引导逻辑）。</summary>
    public bool discovered = false;
}
