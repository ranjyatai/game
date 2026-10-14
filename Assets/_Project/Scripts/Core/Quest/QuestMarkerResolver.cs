using System.Collections.Generic;

/// <summary>
/// NPC 头顶 / 任务窗口的任务标记种类。顺序即优先级——主线 &gt; 人物 &gt; 支线。
/// </summary>
public enum QuestMarkerKind
{
    None = 0,
    MainStory = 1,
    Person = 2,
    SubStory = 3,
}

/// <summary>
/// 一个任务标记的完整状态：画哪个图标、要不要压暗。
/// </summary>
public struct QuestMarker
{
    public QuestMarkerKind kind;

    /// <summary>true = 用「可提交」(✓) 图标，false = 用「可接取」(!) 图标。</summary>
    public bool report;

    /// <summary>进行中但必做目标还没做完——图标压暗。</summary>
    public bool dimmed;

    public bool HasValue => kind != QuestMarkerKind.None;

    public static readonly QuestMarker None = new QuestMarker { kind = QuestMarkerKind.None };
}

/// <summary>
/// 把「这个 NPC 身上有什么任务」归纳成一个可以直接画的标记。
///
/// NPC 头顶和任务窗口共用这里，避免两处各判一套、判出不同结果——图标是给玩家做
/// 决策用的，两个界面对同一个 NPC 说法不一致比没有图标更糟。
///
/// 优先级按 QuestMarkerKind 的枚举顺序：主线 &gt; 人物 &gt; 支线。这是按「类型」排的，
/// 不是按「紧急程度」——即使支线已经可以交、主线还差得远，显示的仍然是主线。
/// 这是刻意的：玩家应该先被引导到主线。
///
/// 人物记录（CharacterArc）和任务是平行的两套系统，它没有「接取 / 提交」状态机，
/// 完成度实时算，所以只有「未完成 = 有内容」和「已完成 = 不显示」两态，
/// 不会产生 report / dimmed。
/// </summary>
public static class QuestMarkerResolver
{
    public static QuestMarker ResolveForNpc(UnitDefinition npc)
    {
        if (npc == null)
            return QuestMarker.None;

        QuestMarker main = ResolveQuestMarker(npc, QuestCategory.Mainline, QuestMarkerKind.MainStory);
        if (main.HasValue)
            return main;

        QuestMarker person = ResolvePersonMarker(npc);
        if (person.HasValue)
            return person;

        return ResolveQuestMarker(npc, QuestCategory.Side, QuestMarkerKind.SubStory);
    }

    /// <summary>
    /// 任务窗口用：单个任务自己的标记，不看 NPC。
    /// 已完成的任务仍然给出标记（用 report 且不压暗），窗口里需要画完成过的条目。
    /// </summary>
    public static QuestMarker ResolveForQuest(QuestDefinition quest)
    {
        if (quest == null)
            return QuestMarker.None;

        QuestMarkerKind kind = quest.category == QuestCategory.Mainline
            ? QuestMarkerKind.MainStory
            : QuestMarkerKind.SubStory;

        QuestRuntime runtime = QuestRuntime.Instance;
        if (runtime == null)
            return new QuestMarker { kind = kind, report = false, dimmed = true };

        if (runtime.IsCompleted(quest.questId))
            return new QuestMarker { kind = kind, report = true, dimmed = false };

        if (runtime.IsActive(quest.questId))
        {
            // 进行中但还没做完 —— 用亮的感叹号，不是压暗的对勾。
            // 对勾的含义是「可以交了」，没做完就画对勾（哪怕压暗）会让玩家误以为
            // 已经能提交、跑回去发现不行。感叹号的含义是「这里有你的事」，更准确。
            bool ready = quest.AreRequiredObjectivesComplete();
            return new QuestMarker { kind = kind, report = ready, dimmed = false };
        }

        return new QuestMarker { kind = kind, report = false, dimmed = false };
    }

    /// <summary>任务窗口用：人物记录条目的标记。</summary>
    public static QuestMarker ResolveForArc(CharacterArcDefinition arc)
    {
        if (arc == null)
            return QuestMarker.None;

        return new QuestMarker
        {
            kind = QuestMarkerKind.Person,
            report = arc.IsComplete(),
            dimmed = false,
        };
    }

    private static QuestMarker ResolveQuestMarker(UnitDefinition npc, QuestCategory category, QuestMarkerKind kind)
    {
        QuestRuntime runtime = QuestRuntime.Instance;
        if (runtime == null)
            return QuestMarker.None;

        // 可提交优先于可接取：玩家手上已经有的任务比再拿一个新的更值得先处理。
        // 这个次序只在同一类型内部生效，跨类型仍然是主线 > 人物 > 支线。
        bool anyActive = false;

        List<QuestDefinition> active = runtime.GetActiveQuestsForNpc(npc);
        for (int i = 0; i < active.Count; i++)
        {
            QuestDefinition quest = active[i];
            if (quest == null || quest.category != category)
                continue;

            anyActive = true;
            if (quest.AreRequiredObjectivesComplete())
                return new QuestMarker { kind = kind, report = true, dimmed = false };
        }

        List<QuestDefinition> acceptable = runtime.GetAcceptableQuestsForNpc(npc);
        for (int i = 0; i < acceptable.Count; i++)
        {
            QuestDefinition quest = acceptable[i];
            if (quest != null && quest.category == category)
                return new QuestMarker { kind = kind, report = false, dimmed = false };
        }

        // 有进行中的、但没有一个能在这个 NPC 这里推进 —— 画压暗的感叹号。
        //
        // 压暗表达的是「有你的事，但此刻在这儿做不了」，不是「快要能交了」。
        // 这里用感叹号而不是对勾：对勾意味着可以提交，没做完就画对勾会误导玩家
        // 白跑一趟。亮度才是「能不能现在动」的信号。
        if (anyActive)
            return new QuestMarker { kind = kind, report = false, dimmed = true };

        return QuestMarker.None;
    }

    private static QuestMarker ResolvePersonMarker(UnitDefinition npc)
    {
        List<CharacterArcDefinition> arcs = CharacterArcRuntime.GetAll();
        for (int i = 0; i < arcs.Count; i++)
        {
            CharacterArcDefinition arc = arcs[i];
            if (arc == null || arc.npc != npc)
                continue;

            // 已完成的人物记录不再提示——它不是待办，是已经看过的回忆。
            if (arc.IsComplete())
                continue;

            return new QuestMarker { kind = QuestMarkerKind.Person, report = false, dimmed = false };
        }

        return QuestMarker.None;
    }
}
