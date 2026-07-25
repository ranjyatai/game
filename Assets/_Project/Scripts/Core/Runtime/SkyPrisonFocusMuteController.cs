using UnityEngine;

/// <summary>
/// 窗口失去焦点（切到别的窗口/Alt-Tab出去）时静音，拿回焦点自动恢复。
/// 用 Unity 的 AudioListener.volume 做全局最终开关——项目自己的音量控制
/// （SkyPrisonAudioGlobalSettings.masterVolume）是每个音效播放时自己乘一遍倍率，
/// 从没碰过 AudioListener.volume，两套互不干扰：失焦时这里乘 0，拿回焦点乘回 1，
/// 不会覆盖/打乱玩家自己在设置里调好的音量。
/// </summary>
public class SkyPrisonFocusMuteController : MonoBehaviour
{
    private static SkyPrisonFocusMuteController _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isPlaying)
            return;

        if (_instance != null)
            return;

        var go = new GameObject("[SkyPrisonFocusMuteController]");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<SkyPrisonFocusMuteController>();
    }

    private bool? _lastAppliedFocusState;

    private void OnApplicationFocus(bool hasFocus)
    {
        Debug.Log($"[SkyPrisonFocusMuteController] OnApplicationFocus({hasFocus}) 事件触发。");
        ApplyFocusState(hasFocus);
    }

    // OnApplicationFocus 这个回调在 Windows Standalone Build 上实测会有不触发的情况
    // （不是每次切换前台窗口都保证收到事件，是 Unity 在这个平台上的已知不可靠点）。
    // 光靠事件不够稳，改成每帧主动轮询 Application.isFocused 这个实时状态属性做兜底——
    // 就算事件真的漏掉了，下一帧轮询也能追上，不会一直卡在错误的静音/非静音状态。
    private void Update()
    {
        bool hasFocus = Application.isFocused;
        if (_lastAppliedFocusState.HasValue && _lastAppliedFocusState.Value == hasFocus)
            return;

        ApplyFocusState(hasFocus);
    }

    private void ApplyFocusState(bool hasFocus)
    {
        if (_lastAppliedFocusState.HasValue && _lastAppliedFocusState.Value == hasFocus)
            return;

        _lastAppliedFocusState = hasFocus;
        Debug.Log($"[SkyPrisonFocusMuteController] 应用焦点状态 hasFocus={hasFocus}，AudioListener.volume -> {(hasFocus ? 1f : 0f)}");
        // 2026-07-15：曾经额外加过 AudioListener.pause 当"双保险"，实测这是错的——pause
        // 会把当时正在播的声音冻结在原地，浸测机器人失焦期间还在持续触发跳跃/攻击音效，
        // 这些声音会在暂停状态下堆积，一旦拿回焦点解除pause，堆积的声音会一起爆发出来，
        // 表现为切回游戏那一下诡异的音效炸裂声。只用 volume=0 就够——声音照常正常播放/
        // 正常播完，只是静音，不会有任何堆积，拿回焦点时只有"当下正在发生"的声音才会
        // 重新听到，不会有旧声音突然冒出来。
        AudioListener.volume = hasFocus ? 1f : 0f;
    }
}
