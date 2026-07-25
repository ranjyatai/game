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
    [TextArea(2, 4)] public string description;
    public List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();
    public string designerNote;
    public bool enabled = true;

    [Header("Tree")]
    public int primaryParentIndex = -1;
    public List<TechTreeSecondaryRequirement> secondaryRequirements = new List<TechTreeSecondaryRequirement>();

    [Header("Display")]
    public Sprite icon;
    public bool useCustomColor = false;
    public Color customColor = new Color(0.48f, 0.76f, 1f, 1f);

    [Header("Level")]
    public int maxLevel = 1;
    public List<TechTreeLevelData> levels = new List<TechTreeLevelData>();
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
