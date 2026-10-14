using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 人物记录——某个NPC专属的一条"回忆/羁绊"进度线，跟任务是平行的两套系统，不是
/// 任务的一种(不占用主线/支线名额，不受QuestRuntime的进行中/已完成状态机管理)。
/// 阶段(stages)复用 QuestObjective——同样的"描述+完成条件(LogicSentenceInstance)"
/// 结构，任务日志的节点链进度条对任务目标和人物记录阶段是同一份绘制逻辑，不用
/// 分别维护两套数据形状。
///
/// 没有 unlockConditions/isActive 这类状态机字段——人物记录不需要"接取"，它的完成度
/// 直接由各阶段的完成条件实时算出来，玩家什么时候看都是当下的真实进度，没有"进行中
/// vs 已完成"两态要持久化，只有"完成到第几阶段了"。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/任务/人物记录", fileName = "CharacterArcDefinition")]
public class CharacterArcDefinition : ScriptableObject
{
    [Header("标识")]
    [Tooltip("唯一标识，仅用于内部索引/排查，不像任务ID那样会被当成flag用。")]
    public string arcId = "";

    [Header("归属")]
    public UnitDefinition npc;

    [Header("显示")]
    public List<LocalizedTextEntry> title = new List<LocalizedTextEntry>();

    [Header("阶段")]
    [Tooltip("按数组顺序当作节点链的步骤顺序，跟任务目标的节点链是同一套规则。")]
    public List<QuestObjective> stages = new List<QuestObjective>();

    public string GetLocalizedTitle(string fallback = "")
        => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(title, fallback) : fallback;

    public bool IsComplete()
    {
        foreach (var stage in stages)
            if (!stage.IsComplete())
                return false;
        return stages.Count > 0;
    }

    public QuestObjective GetCurrentStage()
    {
        foreach (var stage in stages)
            if (!stage.IsComplete())
                return stage;
        return null;
    }
}
