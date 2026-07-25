using System;
using System.Collections.Generic;

[Serializable]
public class TechTreeNodeLevel
{
    public string key;
    public int    value;
}

/// <summary>
/// 科技树存档数据。存在 PlayerSaveData.techTree 里。
/// 按节点 ID 记录当前等级（0 = 未解锁）。
/// </summary>
[Serializable]
public class TechTreeSaveData
{
    /// <summary>当前可用科技点（未花费）。</summary>
    public int availablePoints = 0;

    /// <summary>历史累计获得的总科技点。</summary>
    public int totalPointsEarned = 0;

    /// <summary>各节点当前等级。JsonUtility 不支持 Dictionary，用 List 代替。</summary>
    public List<TechTreeNodeLevel> nodeLevels = new List<TechTreeNodeLevel>();
}
