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

    /// <summary>已被主线触发器「显现」的研修组 ID(见 TechTreeReveal)。没写进来的组里的节点，玩家在战术研修里看不到。</summary>
    public List<string> revealedGroups = new List<string>();

    /// <summary>玩家已经「看过」的被显现节点 ID(鼠标移上去或选中过)。没在里面的新显现节点，窗口里左上角显示 NEW。</summary>
    public List<string> seenNodes = new List<string>();
}
