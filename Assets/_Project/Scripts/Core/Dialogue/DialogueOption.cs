using System;
using System.Collections.Generic;
using UnityEngine;

public enum DialogueOptionActionType
{
    [InspectorName("显示句子(留在对话里)")]
    ShowSentence,

    [InspectorName("打开窗口(商店、工房等)")]
    OpenWindow,

    [InspectorName("设置任务标记(推进剧情)")]
    SetQuestFlag,

    [InspectorName("接受任务")]
    AcceptQuest,

    [InspectorName("显示任务列表")]
    ShowQuestList,

    [InspectorName("关闭对话")]
    Close,
}

/// <summary>
/// 一个可点击的对话选项——从 NPCDialogueDefinition 抽出来的独立类型，
/// 让触发器以后也能复用同一套"选项"数据形状，不用两边各写一份。
/// </summary>
[Serializable]
public class DialogueOption
{
    public List<LocalizedTextEntry> label = new List<LocalizedTextEntry>();
    public DialogueOptionActionType actionType = DialogueOptionActionType.Close;

    [Tooltip("这个选项要不要显示——全部成立才显示，留空(不加条件)=永远显示。用来做" +
             "\"有任务才显示'有事情找你'\"这种按条件出现/消失的选项。")]
    public List<LogicSentenceInstance> visibilityConditions = new List<LogicSentenceInstance>();

    [Tooltip("actionType=显示句子/设置任务标记 时：要跳到句子库里的哪个编号。可以配多个，" +
             "运行时随机选一个(每次点这个选项各选一次)，给回应台词增加变化。")]
    public List<string> targetSentenceIds = new List<string>();

    /// <summary>从 targetSentenceIds 里随机选一个——配了多个就是随机回应，只配一个等价于固定。</summary>
    public string PickTargetSentenceId()
    {
        if (targetSentenceIds == null || targetSentenceIds.Count == 0) return "";
        if (targetSentenceIds.Count == 1) return targetSentenceIds[0];
        return targetSentenceIds[UnityEngine.Random.Range(0, targetSentenceIds.Count)];
    }

    [Tooltip("actionType=显示句子 时：这句讲完后要不要直接关闭对话(告别语用)，" +
             "不勾选=讲完停留在对话里、重新显示选项(默认行为)。")]
    public bool closeAfterShow = false;

    [Tooltip("actionType=打开窗口 时：要打开哪个窗口Prefab(跟FacilityInteractable" +
             "的windowPrefab同一套——SkyPrisonWindowManager_V1.Open()直接吃这个)。")]
    public GameObject targetWindowPrefab;

    [Tooltip("actionType=打开窗口 时：玩家关掉这个子窗口后，要不要自动弹回当前这个" +
             "NPC对话(留在原地继续对话)，不勾选=像现在这样直接结束对话(默认行为)。")]
    public bool returnToDialogueAfterWindowClose = false;

    [Tooltip("actionType=打开窗口 时：这个窗口具体要打开哪份数据——比如同样是\"商店\"" +
             "窗口，供给站/古董店/工坊商店是不同的ShopDefinition，这里指定用哪个。" +
             "窗口controller要实现 IDialogueWindowPayloadReceiver 才认得这份数据；" +
             "留空则用窗口prefab自己原本绑定的数据(不区分场景)。")]
    public UnityEngine.Object targetWindowPayload;

    [Tooltip("actionType=设置任务标记 时：要设置哪个任务标记(SaveManager.Player.SetQuestFlag)。")]
    public string questFlag = "";

    [Tooltip("actionType=接受任务 时：点这个选项要接取哪个任务(QuestRuntime.TryAcceptQuest)。" +
             "只对有归属NPC(giverNpc!=null)的任务有意义——那类任务不会被自动扫描开始，" +
             "必须通过这个选项显式接取。任务本身的解锁条件依然要满足，不满足则接取无效果。")]
    public QuestDefinition questToAccept;

    [Tooltip("点这个选项之后，要不要换成另一套选项(而不是回到当前这一层原来的选项)——" +
             "用来做多层对话分支，比如\"闲谈\"这个选项配一套自己的子选项，聊完再手动" +
             "\"返回\"回上一层。留空(没有子选项)=保持现在这层的选项不变，行为跟以前一样。" +
             "运行时会自动在子选项列表最后补一个\"返回\"按钮，不用自己在子选项里加。")]
    public List<DialogueOption> subOptions = new List<DialogueOption>();
}
