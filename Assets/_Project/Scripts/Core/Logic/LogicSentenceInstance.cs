using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LogicSentenceInstance
{
    public string templateId = "";
    public bool enabled = true;

    public List<LogicSlotAssignment> slotAssignments = new List<LogicSlotAssignment>();

    // 结构节点分区（自引用）。用 [SerializeReference] 引用式序列化，避免内联递归套娃
    // 触发打包时的「object composition depth limit 10」中止错误。
    [SerializeReference] public List<LogicSentenceInstance> conditionChildren = new List<LogicSentenceInstance>();
    [SerializeReference] public List<LogicSentenceInstance> thenChildren = new List<LogicSentenceInstance>();
    [SerializeReference] public List<LogicSentenceInstance> elseChildren = new List<LogicSentenceInstance>();
    [SerializeReference] public List<LogicSentenceInstance> bodyChildren = new List<LogicSentenceInstance>();

    public LogicSlotAssignment GetAssignment(string slotId)
    {
        for (int i = 0; i < slotAssignments.Count; i++)
        {
            if (slotAssignments[i] != null && slotAssignments[i].slotId == slotId)
                return slotAssignments[i];
        }

        return null;
    }

    public LogicSlotAssignment GetOrCreateAssignment(string slotId)
    {
        LogicSlotAssignment found = GetAssignment(slotId);
        if (found != null)
            return found;

        LogicSlotAssignment created = new LogicSlotAssignment
        {
            slotId = slotId,
            value = new LogicSlotValue()
        };

        slotAssignments.Add(created);
        return created;
    }
}
