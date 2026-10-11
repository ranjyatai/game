using System;
using System.Collections.Generic;

/// <summary>
/// 战术研修里的节点「按主线显现」：节点带一个显现组(TechTreeNodeData.revealGroup)，
/// 主线触发器(act_reveal_tech_group)把组写进存档后，组里的节点才会出现在窗口里。
///   · 显现组留空 = 一开始就可见；
///   · 已点亮的节点永远可见(旧存档兼容)；
///   · 主前置节点没出现时，子节点也不出现——树只会从根上一层层长出来。
/// </summary>
public static class TechTreeReveal
{
    /// <summary>触发器里能选的显现组：组 ID(存档用), 显示名。新增一批节点就在这里加一行，再把节点的 revealGroup 填上。</summary>
    public static readonly string[][] Groups =
    {
        new[] { "seeker_2",     "探索者 · 第二批" },
        new[] { "seeker_3",     "探索者 · 第三批" },
        new[] { "strategist_2", "战略家 · 第二批" },
        new[] { "strategist_3", "战略家 · 第三批" },
    };

    public static event Action OnRevealChanged;

    public static bool IsGroupRevealed(string group)
    {
        if (string.IsNullOrEmpty(group)) return true;
        if (SkyPrisonAfpAbsorbShield.DebugForceUnlock) return true;          // F7 测试时一并放开
        var list = SaveManager.Player?.techTree?.revealedGroups;
        return list != null && list.Contains(group);
    }

    public static bool Reveal(string group)
    {
        var data = SaveManager.Player?.techTree;
        if (data == null || string.IsNullOrEmpty(group)) return false;
        if (data.revealedGroups == null) data.revealedGroups = new List<string>();
        if (!data.revealedGroups.Contains(group))
        {
            data.revealedGroups.Add(group);
            SaveManager.Save();
            OnRevealChanged?.Invoke();
        }
        return true;
    }

    /// <summary>节点左上角要不要显示 NEW：只有靠显现组出现的节点才有；已经看过(悬停 / 选中过)或已点亮的不再显示。</summary>
    public static bool IsNodeNew(TechTreeGraphAsset graph, int index)
    {
        if (graph == null || index < 0 || index >= graph.nodes.Count) return false;
        var n = graph.nodes[index];
        if (string.IsNullOrEmpty(n.revealGroup)) return false;
        if (!IsNodeVisible(graph, index)) return false;
        if (TechTreeRuntime.Instance != null && TechTreeRuntime.Instance.GetNodeLevel(n.nodeId) > 0) return false;
        var seen = SaveManager.Player?.techTree?.seenNodes;
        return seen == null || !seen.Contains(n.nodeId);
    }

    /// <summary>玩家看过这个节点了：记进存档，NEW 不再出现。返回 true = 这次是新记的。</summary>
    public static bool MarkSeen(string nodeId)
    {
        var data = SaveManager.Player?.techTree;
        if (data == null || string.IsNullOrEmpty(nodeId)) return false;
        if (data.seenNodes == null) data.seenNodes = new List<string>();
        if (data.seenNodes.Contains(nodeId)) return false;
        data.seenNodes.Add(nodeId);
        SaveManager.Save();
        return true;
    }

    /// <summary>这个节点现在要不要画出来(窗口用)：点亮过的一定画；其余看自己的显现组 + 主前置有没有出现。</summary>
    public static bool IsNodeVisible(TechTreeGraphAsset graph, int index)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            if (graph == null || index < 0 || index >= graph.nodes.Count) return true;
            var n = graph.nodes[index];
            if (TechTreeRuntime.Instance != null && TechTreeRuntime.Instance.GetNodeLevel(n.nodeId) > 0) return true;
            if (!IsGroupRevealed(n.revealGroup)) return false;
            index = n.primaryParentIndex;           // 往上查到根；每一层都得出现
        }
        return true;
    }
}
