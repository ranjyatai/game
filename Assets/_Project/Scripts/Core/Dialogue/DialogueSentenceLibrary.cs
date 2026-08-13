using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 台本句子库——扁平的"句子单元"集合，不是分支对话树。每条句子有一个独立编号
/// (比如"A-1")，配好多语言文本，谁都能在任意地方按编号引用同一句(比如NPC对话
/// 选项、触发器演出)，编号本身不含跳转/顺序信息，那些编排逻辑不归这里管。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/对话/台本句子库", fileName = "DialogueSentenceLibrary")]
public class DialogueSentenceLibrary : ScriptableObject
{
    [Serializable]
    public class SentenceEntry
    {
        [Tooltip("句子编号，比如\"A-1\"——全局唯一，别的地方(NPC对话选项/触发器)按这个编号引用。")]
        public string sentenceId = "";

        [Tooltip("章节/分类，纯用来在编辑器里分组浏览，不参与运行时逻辑。")]
        public string chapter = "";

        [Tooltip("备注——给策划自己看的，不显示给玩家。")]
        public string note = "";

        [Tooltip("这句话是谁说的——从单位列表里选，不是手打名字(手打的名字跟单位定义脱节，" +
                 "改名/换角色都对不上)。留空+勾选\"匿名\"表示还没公开身份的角色，显示???。")]
        public UnitDefinition speakerUnit;

        [Tooltip("勾选后不管 speakerUnit 是谁，一律显示???——用于剧情上还没揭示身份的角色。")]
        public bool speakerAnonymous = false;

        [Tooltip("这一句显示的化名/绰号(比如玩家还不知道真名前，显示\"蓝头发的人\")——" +
                 "说话人还是同一个 speakerUnit，只是这一句台词决定显示什么名字。留空则显示" +
                 "speakerUnit 的真名。每句台词独立设置，同一个角色不同句子可以用不同化名/真名。")]
        public List<LocalizedTextEntry> speakerAliasOverride = new List<LocalizedTextEntry>();

        public List<LocalizedTextEntry> texts = new List<LocalizedTextEntry>();

        [Tooltip("这句台词的语音——按语言各配一份，播哪个跟 LocalizationRuntime.CurrentCode(玩家的文字/语音语言设置)走。")]
        public List<LocalizedVoiceEntry> voices = new List<LocalizedVoiceEntry>();
    }

    [Tooltip("这个句子库资产本身归到哪个章节分组下——纯用来在台本编辑器左栏分层显示，" +
             "不参与运行时逻辑。选项来自全局章节设置(Tools/Sky Prison/章节设置)。留空则归到\"未分组\"。")]
    public string chapterGroup = "";

    public List<SentenceEntry> entries = new List<SentenceEntry>();

    private Dictionary<string, SentenceEntry> _index;

    private void BuildIndex()
    {
        _index = new Dictionary<string, SentenceEntry>();
        foreach (var e in entries)
        {
            if (e == null || string.IsNullOrEmpty(e.sentenceId)) continue;
            _index[e.sentenceId] = e;
        }
    }

    private void OnValidate() => _index = null;

    public bool TryGet(string sentenceId, out SentenceEntry entry)
    {
        if (_index == null) BuildIndex();
        return _index.TryGetValue(sentenceId, out entry);
    }

    /// <summary>按当前语言取句子文本，找不到编号/找不到当前语言译文都退回 fallback。
    /// 文本里写了 {player} 会被替换成当前玩家角色的本地化名字，不用在每句台词里手打。</summary>
    public string GetText(string sentenceId, string fallback = "")
    {
        if (!TryGet(sentenceId, out var entry)) return fallback;
        string raw = LocalizationRuntime.Instance != null
            ? LocalizationRuntime.Instance.GetText(entry.texts, fallback)
            : fallback;
        return DialogueTextPlaceholders.Resolve(raw);
    }

    /// <summary>这句台词的说话人显示名——匿名角色一律"???"，正常角色读
    /// UnitDefinition.localizedNames(按当前文字语言)，没配说话人就退回 fallback
    /// (比如打开对话的那个NPC自己的名字，兼容没逐句配说话人的简单场景)。</summary>
    public string GetSpeakerDisplayName(string sentenceId, string fallback = "")
    {
        if (!TryGet(sentenceId, out var entry)) return fallback;
        if (entry.speakerAnonymous) return "???";

        // 化名/绰号优先——同一个说话人(speakerUnit)在不同句子可能显示不同名字，
        // 比如揭示真名前显示"蓝头发的人"，之后的句子改回真名，这个决定权在
        // 每一句台词自己，不是角色的固定属性。
        if (entry.speakerAliasOverride != null && LocalizationRuntime.Instance != null)
        {
            string alias = LocalizationRuntime.Instance.GetText(entry.speakerAliasOverride, "");
            if (!string.IsNullOrEmpty(alias)) return alias;
        }

        if (entry.speakerUnit == null) return fallback;

        if (LocalizationRuntime.Instance != null)
            return LocalizationRuntime.Instance.GetText(entry.speakerUnit.localizedNames, entry.speakerUnit.displayName);
        return string.IsNullOrEmpty(entry.speakerUnit.displayName) ? fallback : entry.speakerUnit.displayName;
    }

    /// <summary>按当前语音语言取这句台词的语音——播哪个语言的语音跟着
    /// LocalizationRuntime.CurrentVoiceCode 走，这是独立于文字语言的设置(玩家可以
    /// 文字看中文、语音听日文)。找不到当前语音语言的配音就退回任意已配的语音，
    /// 一句都没配语音则返回 null(允许只有部分句子有配音)。</summary>
    public AudioClip GetVoiceClip(string sentenceId)
    {
        if (!TryGet(sentenceId, out var entry) || entry.voices == null)
            return null;

        string code = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.CurrentVoiceCode : null;
        AudioClip clip = FindVoiceClip(entry, code);
        if (clip != null) return clip;

        foreach (var v in entry.voices)
            if (v != null && v.clip != null) return v.clip;

        return null;
    }

    private static AudioClip FindVoiceClip(SentenceEntry entry, string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) return null;
        foreach (var v in entry.voices)
            if (v != null && v.languageCode == languageCode) return v.clip;
        return null;
    }
}
