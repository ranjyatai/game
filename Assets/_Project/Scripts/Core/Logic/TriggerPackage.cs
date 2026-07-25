using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SkyPrison/Trigger/Trigger Package", fileName = "TriggerPackage")]
public class TriggerPackage : ScriptableObject
{
    public enum TriggerRuleTreeNodeType
    {
        Folder = 0,
        Rule = 1
    }

    [System.Serializable]
    public class ConditionSentenceGroup
    {
        public List<LogicSentenceInstance> items = new List<LogicSentenceInstance>();
    }

    [System.Serializable]
    public class MotiveSentenceGroup
    {
        public List<LogicSentenceInstance> items = new List<LogicSentenceInstance>();
    }

    [System.Serializable]
    public class ActionSentenceGroup
    {
        public List<LogicSentenceInstance> items = new List<LogicSentenceInstance>();
    }

    [System.Serializable]
    public class LogicSentenceRoot
    {
        public ConditionSentenceGroup conditionGroup = new ConditionSentenceGroup();
        public MotiveSentenceGroup motiveGroup = new MotiveSentenceGroup();
        public ActionSentenceGroup actionGroup = new ActionSentenceGroup();
    }

    [System.Serializable]
    public class TriggerRule
    {
        public bool enabled = true;
        public string ruleName = "新触发器规则";
        [TextArea(1, 3)] public string note;

        [Header("语法控制")]
        public int priority = 100;
        public float cooldownSeconds = 0f;
        public bool requireAllConditions = true;

        [Header("逻辑句型")]
        public LogicSentenceRoot sentenceRoot = new LogicSentenceRoot();
    }

    [System.Serializable]
    public class TriggerRuleTreeNode
    {
        public TriggerRuleTreeNodeType nodeType = TriggerRuleTreeNodeType.Rule;
        public string displayName = "新节点";
        public bool expanded = true;
        public bool enabled = true;

        [TextArea(1, 3)] public string note;

        public TriggerRule ruleData = new TriggerRule();
        // 自引用树结构：引用式序列化，避免内联递归触发打包深度限制。
        [SerializeReference] public List<TriggerRuleTreeNode> children = new List<TriggerRuleTreeNode>();

        public static TriggerRuleTreeNode CreateRuleNode()
        {
            return new TriggerRuleTreeNode
            {
                nodeType = TriggerRuleTreeNodeType.Rule,
                displayName = "新规则",
                expanded = true,
                enabled = true,
                ruleData = new TriggerRule()
            };
        }

        public static TriggerRuleTreeNode CreateFolderNode()
        {
            return new TriggerRuleTreeNode
            {
                nodeType = TriggerRuleTreeNodeType.Folder,
                displayName = "新文件夹",
                expanded = true,
                enabled = true,
                ruleData = null,
                children = new List<TriggerRuleTreeNode>()
            };
        }
    }

    [Header("基础信息")]
    public string triggerId = "new_trigger_package";
    public string displayName = "新触发器包";

    [TextArea(2, 5)]
    public string note;

    [Header("调试")]
    public bool debugLogs = false;

    [Header("规则列表")]
    public List<TriggerRule> rules = new List<TriggerRule>();

    [Header("规则树（预留）")]
    public List<TriggerRuleTreeNode> ruleTree = new List<TriggerRuleTreeNode>();
}
