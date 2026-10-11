using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TT_NewGraph", menuName = "SkyPrison/Tech Tree Graph")]
public class TechTreeGraphAsset : ScriptableObject
{
    public enum LayoutMode
    {
        Vertical = 0,
        Horizontal = 1,
        RadialOutward = 2,
        RadialInward = 3,
    }

    [Header("Graph")]
    public string graphId;
    public string displayName;
    [TextArea(2, 4)] public string note;
    public LayoutMode layoutMode = LayoutMode.Vertical;

    [Header("Nodes")]
    public List<TechTreeNodeData> nodes = new List<TechTreeNodeData>();
}

[Serializable]
public class TechTreeNodeData
{
    public string nodeId = "node_001";
    public string nodeName = "新节点";
    public List<LocalizedTextEntry> localizedNames = new List<LocalizedTextEntry>();      // 多语言节点名(和物品 / 单位同一套)；某语言没填就用 nodeName
    [TextArea(2, 4)] public string description;
    public List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();
    public string designerNote;
    public bool enabled = true;

    [Header("Tree")]
    public int primaryParentIndex = -1;
    public List<TechTreeSecondaryRequirement> secondaryRequirements = new List<TechTreeSecondaryRequirement>();
    [Tooltip("游戏进度类的外部条件，满足了这个节点才能点（和前置节点的条件同时要满足）。")]
    public TechTreeExternalCondition externalCondition = TechTreeExternalCondition.None;

    [Tooltip("显现组：留空 = 一开始就可见；填了组 ID，要等主线触发器「显现研修组」后玩家才看得到这个节点(父节点没出现时也看不到)。已点亮的节点永远可见。")]
    public string revealGroup;

    [Header("Display")]
    public Sprite icon;
    public bool useCustomColor = false;
    public Color customColor = new Color(0.48f, 0.76f, 1f, 1f);

    [Header("Level")]
    public int maxLevel = 1;
    public List<TechTreeLevelData> levels = new List<TechTreeLevelData>();

    /// <summary>当前语言的节点名 / 描述(研修窗口用)。没有多语言数据或该语言没填时退回中文字段。</summary>
    public string GetLocalizedName() => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(localizedNames, nodeName) : nodeName;
    public string GetLocalizedDescription() => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(localizedDescriptions, description) : description;
}

public enum TechTreeExternalCondition
{
    None = 0,
    /// <summary>夜间模式已出现（任意夜版被开放过）。</summary>
    NightModeAppeared = 1,
}

[Serializable]
public class TechTreeSecondaryRequirement
{
    public string targetNodeId;
    public RequirementType requirementType = RequirementType.NodeLevelAtLeast;
    public int requiredLevel = 1;

    public enum RequirementType
    {
        NodeLevelAtLeast = 0,
    }
}

[Serializable]
public class TechTreeLevelData
{
    public int level = 1;
    public List<TechTreeCostData> costs = new List<TechTreeCostData>();
    public List<TechTreeRewardData> rewards = new List<TechTreeRewardData>();
    [TextArea(1, 3)] public string note;
    public List<LocalizedTextEntry> localizedNotes = new List<LocalizedTextEntry>();      // 多语言等级说明；没填就用 note

    public string GetLocalizedNote() => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(localizedNotes, note) : note;
}

[Serializable]
public class TechTreeCostData
{
    public UnityEngine.Object item;
    public int amount = 1;
}

[Serializable]
public class TechTreeRewardData
{
    public string key;
    public string displayName;
    public float value;
    public bool isPercent;
    public string note;
}
