using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位语音（死亡/受击/发技）的统一播放入口——UnitDeathController、
/// UnitActionModuleRuntime 这些调用方不用各自重复"随机选一条 → 按 CurrentVoiceCode
/// 选语言 → 建 AudioSource 播放"这一整套逻辑。
///
/// 音量走 voiceVolume（跟战斗 SE 用的 seVolume 是分开的两个滑条），2D 播放（spatialBlend
/// =0，不随距离衰减）——原因跟 UnitActionModuleRuntime.PlaySkillSE 一样：角色位移快的
/// 技能会让固定发声点迅速远离监听者，3D 空间音源会被距离衰减削弱，听感上像"忽然变小声"，
/// 战斗反馈类音效不该受这个影响。
/// </summary>
public static class UnitVoicePlayback
{
    public static void Play(List<UnitVoiceLine> lines, Vector3 worldPos)
    {
        if (lines == null || lines.Count == 0)
            return;

        UnitVoiceLine line = lines[Random.Range(0, lines.Count)];
        if (line?.clips == null || line.clips.Count == 0)
            return;

        AudioClip clip = ResolveClip(line.clips);
        if (clip == null)
            return;

        var gs = SkyPrisonAudioGlobalSettings.Instance;
        float vol = (gs != null ? gs.masterVolume * gs.voiceVolume : 1f) * Mathf.Max(0f, line.volume);
        if (vol <= 0f)
            return;

        GameObject go = new GameObject("UnitVoice_OneShot");
        go.transform.position = worldPos;
        AudioSource source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = vol;
        source.spatialBlend = 0f;
        source.Play();
        Object.Destroy(go, clip.length + 0.1f);
    }

    /// <summary>按当前语音语言（CurrentVoiceCode，独立于文字语言）找这条台词对应的
    /// 音频；找不到当前语言的版本就退回任意已配的语言，一个语言都没配则返回 null
    /// （允许只配部分语言）。</summary>
    private static AudioClip ResolveClip(List<LocalizedVoiceEntry> clips)
    {
        string code = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.CurrentVoiceCode : null;
        if (!string.IsNullOrEmpty(code))
        {
            foreach (var c in clips)
                if (c != null && c.languageCode == code && c.clip != null)
                    return c.clip;
        }

        foreach (var c in clips)
            if (c != null && c.clip != null)
                return c.clip;

        return null;
    }
}
