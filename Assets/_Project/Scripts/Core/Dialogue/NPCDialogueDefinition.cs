using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一个NPC"怎么交互"的编排——名字 + 一组"对话变体"(每个变体=条件+开场白句子编号+
/// 选项列表)。不含句子文本本身(那部分在 DialogueSentenceLibrary 里)，也不走触发器
/// 组装——触发器只负责地图专属的一次性演出，常规的"跟NPC交互出选项"是通用重复模式，
/// 该由这份数据+运行时代码直接驱动。
///
/// 变体的条件复用触发器同一套 LogicSentenceInstance/TriggerConditionEvaluator——
/// NPC交互、任务、剧情、章节推进用的是同一套底层判定，不是给对话另开一条平行小路。
/// 按顺序取第一个"条件全部成立"的变体；没有条件(conditions为空)的变体永远成立，
/// 放在列表最后当默认兜底。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/对话/NPC对话包", fileName = "NPCDialogueDefinition")]
public class NPCDialogueDefinition : ScriptableObject
{
    [Serializable]
    public class DialogueVariant
    {
        [Tooltip("全部成立才会选中这个变体；留空(不加条件)=永远成立，适合放最后当默认兜底。")]
        public List<LogicSentenceInstance> conditions = new List<LogicSentenceInstance>();

        [Tooltip("按E交互时这个变体显示的第一句——句子库里的编号。可以配多个，运行时随机选一个" +
                 "(每次开始对话各选一次)，用来给同一个开场增加台词变化，不是必须只配一个。")]
        public List<string> greetingSentenceIds = new List<string>();

        public List<DialogueOption> options = new List<DialogueOption>();

        /// <summary>从 greetingSentenceIds 里随机选一个——配了多个就是随机开场白，" +
        /// 只配一个等价于固定开场白。</summary>
        public string PickGreetingSentenceId()
        {
            if (greetingSentenceIds == null || greetingSentenceIds.Count == 0) return "";
            if (greetingSentenceIds.Count == 1) return greetingSentenceIds[0];
            return greetingSentenceIds[UnityEngine.Random.Range(0, greetingSentenceIds.Count)];
        }
    }

    [Header("句子库")]
    [Tooltip("这个NPC的对话文本从哪个句子库里查——sentenceId 都是对这份库的编号。")]
    public DialogueSentenceLibrary sentenceLibrary;

    [Header("基本信息")]
    public List<LocalizedTextEntry> npcName = new List<LocalizedTextEntry>();

    [Header("对话变体")]
    [Tooltip("按顺序判定，取第一个条件成立的变体。放一个无条件的变体在最后当默认兜底。")]
    public List<DialogueVariant> variants = new List<DialogueVariant>();

    [Header("待机闲聊")]
    [Tooltip("玩家停在选项界面这么久没做任何操作(点选项/切换悬停)，就从下面随机念一句" +
             "闲聊台词，念完重新计时——纯粹是让NPC看起来\"活着\"的调味，不影响对话流程。" +
             "留空(闲聊句子列表为空)就没有这个效果。")]
    public float idleChatterDelaySeconds = 18f;

    [Tooltip("待机闲聊会念的句子——句子库里的编号，随机挑一句。")]
    public List<string> idleChatterSentenceIds = new List<string>();

    public string GetLocalizedName(string fallback = "")
        => LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetText(npcName, fallback) : fallback;

    public string GetSentenceText(string sentenceId, string fallback = "")
        => sentenceLibrary != null ? sentenceLibrary.GetText(sentenceId, fallback) : fallback;

    /// <summary>从闲聊句子里随机选一个，没配就返回空字符串。</summary>
    public string PickIdleChatterSentenceId()
    {
        if (idleChatterSentenceIds == null || idleChatterSentenceIds.Count == 0) return "";
        if (idleChatterSentenceIds.Count == 1) return idleChatterSentenceIds[0];
        return idleChatterSentenceIds[UnityEngine.Random.Range(0, idleChatterSentenceIds.Count)];
    }

    /// <summary>取当前应该显示的变体——按顺序找第一个条件全部成立的。</summary>
    public DialogueVariant GetActiveVariant(bool debugLogs = false)
    {
        foreach (var variant in variants)
        {
            if (variant == null) continue;

            bool allMatch = true;
            foreach (var condition in variant.conditions)
            {
                if (!TriggerConditionEvaluator.Evaluate(condition, debugLogs))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
                return variant;
        }

        return null;
    }
}
