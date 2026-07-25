using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SkyPrison/AI/Behavior Package", fileName = "AIBehaviorPackage")]
public class AIBehaviorPackage : ScriptableObject
{
    public enum AIConditionType
    {
        SeeTarget = 0,
        LoseTarget = 1,
        TookDamage = 2,
        NoCurrentTarget = 3,
        HealthBelowPercent = 4,
        DistanceToTargetLessThan = 5,
        DistanceToTargetGreaterThan = 6
    }

    public enum AIMotiveType
    {
        Idle = 0,
        Patrol = 1,
        Chase = 2,
        KeepDistance = 3,
        Escape = 4,
        Guard = 5
    }

    public enum AIActionType
    {
        MoveToTarget = 0,
        BasicAttack = 1,
        StepBack = 2,
        FaceTarget = 3,
        StopMoving = 4,
        WaitSeconds = 5,
        SwitchAIPackage = 6
    }

    public enum RuleTreeNodeType
    {
        Folder = 0,
        Rule = 1
    }

    [System.Serializable]
    public class AIConditionUnit
    {
        public bool enabled = true;
        public AIConditionType conditionType = AIConditionType.SeeTarget;
        public float valueA = 0f;
        [TextArea(1, 2)] public string note;
    }

    [System.Serializable]
    public class AIActionUnit
    {
        public bool enabled = true;
        public AIActionType actionType = AIActionType.MoveToTarget;
        public float valueA = 0f;
        public AIBehaviorPackage nextAIPackage;
        [TextArea(1, 2)] public string note;
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
    public class AIRule
    {
        public bool enabled = true;
        public string ruleName = "新规则";
        [TextArea(1, 3)] public string note;

        [Header("语法控制")]
        public int priority = 100;
        public float cooldownSeconds = 0f;
        public bool requireAllConditions = true;

        [Header("旧结构（保留迁移）")]
        public List<AIConditionUnit> conditions = new List<AIConditionUnit>();
        public AIMotiveType motive = AIMotiveType.Chase;
        public List<AIActionUnit> actions = new List<AIActionUnit>();

        [Header("新句型结构（最终版）")]
        public LogicSentenceRoot sentenceRoot = new LogicSentenceRoot();
    }

    [System.Serializable]
    public class AIRuleTreeNode
    {
        public RuleTreeNodeType nodeType = RuleTreeNodeType.Rule;
        public string displayName = "新节点";
        public bool expanded = true;
        public bool enabled = true;

        [TextArea(1, 3)]
        public string note;

        public AIRule ruleData = new AIRule();
        public List<AIRuleTreeNode> children = new List<AIRuleTreeNode>();

        public static AIRuleTreeNode CreateRuleNode()
        {
            return new AIRuleTreeNode
            {
                nodeType = RuleTreeNodeType.Rule,
                displayName = "新规则",
                expanded = true,
                enabled = true,
                ruleData = new AIRule()
            };
        }

        public static AIRuleTreeNode CreateFolderNode()
        {
            return new AIRuleTreeNode
            {
                nodeType = RuleTreeNodeType.Folder,
                displayName = "新文件夹",
                expanded = true,
                enabled = true,
                ruleData = null,
                children = new List<AIRuleTreeNode>()
            };
        }
    }

    [Header("基础信息")]
    public string aiId = "new_ai";
    public string displayName = "新AI行为包";

    [TextArea(2, 5)]
    public string note;

    [Header("调试")]
    public bool debugLogs = false;

    [Header("模块开关")]
    public bool enablePerceptionModule = true;
    public bool enableDecisionModule = true;
    public bool enableActionModule = true;

    [Header("旧规则列表（当前编辑器仍使用）")]
    public List<AIRule> rules = new List<AIRule>();

    [Header("新规则树（预留）")]
    public List<AIRuleTreeNode> ruleTree = new List<AIRuleTreeNode>();
}
