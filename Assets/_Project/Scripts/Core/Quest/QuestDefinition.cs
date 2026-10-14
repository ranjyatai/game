using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 任务分类——用下拉枚举而不是bool，以后要加"日常"/"隐藏任务"这类新分类不用把
/// 现有字段从bool改成别的类型再动一遍所有调用点。
/// </summary>
public enum QuestCategory
{
    [InspectorName("主线")]
    Mainline,
    [InspectorName("支线")]
    Side,
}

/// <summary>
/// 一个任务的定义——解锁条件、目标列表、完成时执行的动作，全部复用触发器的
/// LogicSentenceInstance 条件/动作语言，跟NPC对话变体、地图触发器是同一套底层。
///
/// 主线之间靠 unlockConditions 串成线性(比如要求上一条主线任务的questId已完成)；
/// 支线不要求顺序，各自的 unlockConditions 独立判断，要有前置条件就在
/// unlockConditions 里加一条 cond_has_quest_flag 引用前置任务/阶段的flag即可，
/// 不需要另外一套"支线前置系统"。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/任务/任务定义", fileName = "QuestDefinition")]
public class QuestDefinition : ScriptableObject
{
    [Header("标识")]
    [Tooltip("唯一标识，也是任务完成后设置的 quest flag 名——对话/触发器条件直接判定这个questId。")]
    public string questId = "";

    [Header("显示")]
    public List<LocalizedTextEntry> title = new List<LocalizedTextEntry>();
    public List<LocalizedTextEntry> description = new List<LocalizedTextEntry>();

    [Tooltip("任务日志里大卡片(目前只有主线任务用大卡片)背景插画。留空显示占位色块，" +
             "不影响任务功能——纯美术资源，随时可以后补。")]
    public Sprite heroImage;

    [Tooltip("任务日志显示的\"区域\"字段——纯展示用的地点名字，不影响任何判定逻辑" +
             "(玩家实际所在区域由地图系统另外判断)。留空则不显示这一行。")]
    public List<LocalizedTextEntry> regionDisplayName = new List<LocalizedTextEntry>();

    [Tooltip("危险等级(HAZARD RANK)——纯展示用的1~10分级，不影响任何判定/掉落/难度逻辑，" +
             "策划凭感觉给个数字标一下这个任务大概多危险即可。本篇任务大致用1~5，" +
             "DLC/higher-tier内容用6~8，9~10留给以后更狠的内容。")]
    [Range(1, 10)]
    public int hazardRank = 1;

    [Tooltip("主线=线性推进的一环；支线=允许并行、非线性，可选自带前置条件。")]
    public QuestCategory category = QuestCategory.Side;

    [Tooltip("这个任务归属哪个NPC——NPC对话的\"显示任务列表\"选项按这个字段筛选" +
             "\"哪些进行中的任务是这个NPC给的\"。留空=不归属任何NPC，不会出现在" +
             "任何NPC的任务列表里(仍然可以被其他方式解锁/完成)。")]
    public UnitDefinition giverNpc;

    [Tooltip("giverNpc具体是地图上放置的哪一个实例——记录它所在的场景名/路径 +" +
             "SkyPrisonSceneUnitMarker的GUID，留给以后\"引导玩家去找这个NPC\"/" +
             "\"NPC不在当前地图时提示去哪张图能找到\"这类功能用。只是多记一份" +
             "\"在哪\"的信息，留空不影响现有的按giverNpc匹配任务列表的逻辑。")]
    public string giverNpcSceneName = "";
    public string giverNpcScenePath = "";
    public string giverNpcSceneUnitGuid = "";

    [Header("解锁条件")]
    [Tooltip("全部成立时任务自动开始(进入进行中)，不需要玩家手动接取。勾选下面\"需要" +
             "手动接取\"的话，这里改成只是\"允许被接取的前提\"，不会自动开始。")]
    public List<LogicSentenceInstance> unlockConditions = new List<LogicSentenceInstance>();

    [Header("目标")]
    public List<QuestObjective> objectives = new List<QuestObjective>();

    [Header("完成时执行")]
    [Tooltip("任务所有必做目标达成时执行——可以加 act_set_quest_flag 推进阶段/解锁其他内容。")]
    public List<LogicSentenceInstance> onCompleteActions = new List<LogicSentenceInstance>();

    public string GetLocalizedTitle(string fallback = "")
        => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(title, fallback) : fallback;

    public string GetLocalizedDescription(string fallback = "")
        => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(description, fallback) : fallback;

    public string GetLocalizedRegionDisplayName(string fallback = "")
        => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(regionDisplayName, fallback) : fallback;

    public bool AreUnlockConditionsMet(bool debugLogs = false)
    {
        foreach (var condition in unlockConditions)
        {
            if (!TriggerConditionEvaluator.Evaluate(condition, debugLogs))
                return false;
        }
        return true;
    }

    /// <summary>只看必做目标——可选目标(isOptional)不参与完成判定。判定期间把questId
    /// 设进 TriggerConditionEvaluator 的起点快照上下文，让计数器型目标(击杀/对话次数)
    /// 只看"任务开始之后新发生的"，不把接任务之前(包括接任务这个动作本身)发生的事
    /// 算进去——不然"跟发任务的NPC对话"这种目标，光是接任务这次对话就已经满足了。</summary>
    public bool AreRequiredObjectivesComplete(bool debugLogs = false)
    {
        TriggerConditionEvaluator.BeginQuestObjectiveContext(questId);
        try
        {
            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjective objective = objectives[i];
                if (objective.isOptional) continue;
                if (!IsObjectiveEffectivelyComplete(questId, i, objective, debugLogs))
                    return false;
            }
            return true;
        }
        finally
        {
            TriggerConditionEvaluator.EndQuestObjectiveContext();
        }
    }

    /// <summary>目标列表里第一个还没完成的——objectives 本身没有显式的"顺序"字段，
    /// 这里把数组顺序当作步骤顺序用(任务日志的节点链进度条按这个顺序连点)。全部
    /// 完成时返回 null。</summary>
    public QuestObjective GetCurrentObjective()
    {
        TriggerConditionEvaluator.BeginQuestObjectiveContext(questId);
        try
        {
            for (int i = 0; i < objectives.Count; i++)
                if (!IsObjectiveEffectivelyComplete(questId, i, objectives[i], false))
                    return objectives[i];
            return null;
        }
        finally
        {
            TriggerConditionEvaluator.EndQuestObjectiveContext();
        }
    }

    /// <summary>目标一旦被判定完成过一次，就"锁存"住(SaveManager.Player.LatchObjective)，
    /// 以后哪怕它自己的完成条件又变回不满足，也一律当作已完成——用户明确反馈过
    /// "收集×N"后面接一条"交付×N给NPC"这种链式目标，交付会把背包里那N个物品扣掉，
    /// 如果每次都重新查背包余量，"收集"那一步会在交付之后又变回未完成，任务永远
    /// 卡死没法结算。已经锁存的直接返回true不用重新判定；没锁存过的照常判定真实
    /// 条件，判定为true就顺手锁存下来，供以后调用直接命中。
    ///
    /// 公开成static——QuestLogController.BuildNodeChain(节点链UI)判定"这个节点算不算
    /// 已完成"要跟这里完全同一套规则，不能自己另外调QuestObjective.IsComplete()，
    /// 不然UI上的勾选状态会在交付之后又跳回未完成，跟真实的任务完成判定对不上。
    /// questId传null(角色剧情节点BuildNodeChain(arc.stages)那个调用点)时不锁存，
    /// 原样退回纯粹判定当前条件，跟人物记录阶段一直以来的行为保持不变。</summary>
    public static bool IsObjectiveEffectivelyComplete(string questId, int index, QuestObjective objective, bool debugLogs = false)
    {
        bool hasQuestContext = !string.IsNullOrWhiteSpace(questId);

        if (hasQuestContext && SaveManager.Player != null && SaveManager.Player.IsObjectiveLatched(questId, index))
            return true;

        if (!objective.IsComplete(debugLogs))
            return false;

        if (hasQuestContext) SaveManager.Player?.LatchObjective(questId, index);
        return true;
    }

    /// <summary>任务完成奖励的货币——直接从 onCompleteActions 里找 act_add_currency
    /// 这个动作类型读，不额外维护一份"奖励"字段。奖励本来就是"完成时执行的动作"
    /// 之一，单独搞一份重复数据只会让"改了奖励忘记改预览"这种事再发生一次。</summary>
    public List<(CurrencyDefinition currency, int amount)> GetRewardCurrencies()
    {
        var result = new List<(CurrencyDefinition, int)>();
        foreach (var action in onCompleteActions)
        {
            if (action == null || action.templateId != "act_add_currency") continue;
            CurrencyDefinition currency = action.GetAssignment("currencyId")?.value?.assetReference as CurrencyDefinition;
            if (currency == null) continue;
            int amount = action.GetAssignment("amount")?.value?.EvaluateInt() ?? 0;
            result.Add((currency, amount));
        }
        return result;
    }

    /// <summary>任务完成奖励的物品——同上，从 onCompleteActions 里找 act_add_item_to_bag。</summary>
    public List<(ItemDefinition item, int amount)> GetRewardItems()
    {
        var result = new List<(ItemDefinition, int)>();
        foreach (var action in onCompleteActions)
        {
            if (action == null || action.templateId != "act_add_item_to_bag") continue;
            ItemDefinition item = action.GetAssignment("itemId")?.value?.assetReference as ItemDefinition;
            if (item == null) continue;
            int amount = action.GetAssignment("amount")?.value?.EvaluateInt() ?? 0;
            result.Add((item, amount));
        }
        return result;
    }
}
