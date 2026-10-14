using System;
using UnityEngine;

/// <summary>
/// 某一句台词在某个语言下对应的语音片段——跟 LocalizedTextEntry 同构，
/// 复用同一个 languageCode 集合，播放哪个语音跟着 LocalizationRuntime.CurrentCode
/// 走（跟文字语言共用同一个设置，不是独立的语音语言选项）。
/// </summary>
[Serializable]
public class LocalizedVoiceEntry
{
    public string languageCode = "";
    public AudioClip clip;
}
