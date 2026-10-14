using System;
using System.Collections.Generic;

/// <summary>
/// 任务里的一个目标。完成判定复用触发器条件语言(LogicSentenceInstance)，
/// 不另建一套"目标类型"枚举——跟设施等级、任务标记判定是同一套底层。
/// </summary>
[Serializable]
public class QuestObjective
{
    public List<LocalizedTextEntry> description = new List<LocalizedTextEntry>();

    [UnityEngine.Tooltip("全部成立才算这个目标完成。")]
    public List<LogicSentenceInstance> completionConditions = new List<LogicSentenceInstance>();

    [UnityEngine.Tooltip("可选目标：任务完成判定时会跳过未完成的可选目标（支线内允许非线性/可跳过）。")]
    public bool isOptional = false;

    [UnityEngine.Tooltip("玩家针对这个目标跟发任务人对话/交付时，要不要直接跑一整套" +
        "触发器演出(镜头/暂停/连续台词等)，而不是只显示一句台词——给主线级别的演出" +
        "用。配了这个字段，下面的npcResponseSentenceIds整个被跳过不看。运行时靠" +
        "TriggerPackageRuntime.ExecuteImmediately手动立刻执行，完全独立于场景自动" +
        "生成、按动机+条件驱动的那一套触发器(不会互相干扰，也不会被登记进" +
        "MapTriggerRegistry)。")]
    public TriggerPackage npcResponseTriggerPackage;

    [UnityEngine.Tooltip("玩家针对这个目标跟发任务人对话/交付时(NPCDialogueWindowController." +
        "CheckInWithGiverForQuest/ConfirmDeliveryPopup)，NPC按顺序连续念完的专属台词——" +
        "从台本编辑器里选，走跟普通对话台词同一套字幕+配音(DialogueSubtitleHUD." +
        "ShowLineSequence)，每一句自己的自动播放/等待按键行为复用全局设置" +
        "(SaveManager.Settings.dialogueAutoPlay)。配几句就完整播几句(不是随机抽一句，" +
        "跟开场白/闲聊池那种\"多句=随机\"是不同语义)，留空则退回用任务描述文字当反馈" +
        "(旧任务数据不受影响)。上面配了npcResponseTriggerPackage的话，这个字段整个" +
        "不会被用到。")]
    public List<string> npcResponseSentenceIds = new List<string>();

    public bool IsComplete(bool debugLogs = false)
    {
        foreach (var condition in completionConditions)
        {
            if (!TriggerConditionEvaluator.Evaluate(condition, debugLogs))
                return false;
        }
        return true;
    }
}
