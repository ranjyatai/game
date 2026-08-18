using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位语音的一条候选台词——本身可能有好几个语言版本（跟 DialogueSentenceLibrary.
/// SentenceEntry.voices 同构，复用 LocalizedVoiceEntry），播放时按
/// LocalizationRuntime.CurrentVoiceCode（语音语言，独立于文字语言）挑对应语言的
/// 那一份；同一个事件（死亡/受击/发技）通常配好几条 UnitVoiceLine，播放时先随机选
/// 一条，再按语言选具体音频。
/// </summary>
[Serializable]
public class UnitVoiceLine
{
    public List<LocalizedVoiceEntry> clips = new List<LocalizedVoiceEntry>();
    [Range(0f, 2f)] public float volume = 1f;
}
