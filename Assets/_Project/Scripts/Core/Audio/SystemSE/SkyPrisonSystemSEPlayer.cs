using UnityEngine;

/// <summary>
/// 系统 UI SE 播放器。
/// 首次调用 Play() 时自动在场景里创建单例（DontDestroyOnLoad）。
/// 音量走 SkyPrisonAudioGlobalSettings.uiVolume × masterVolume。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonSystemSEPlayer : MonoBehaviour
{
    // ── 单例 ─────────────────────────────────────────────────────────────────

    private static SkyPrisonSystemSEPlayer s_instance;

    public static SkyPrisonSystemSEPlayer Instance
    {
        get
        {
            if (s_instance == null) Bootstrap();
            return s_instance;
        }
    }

    // ── 字段 ─────────────────────────────────────────────────────────────────

    private AudioSource        _source;
    private AudioSource        _loopSource;
    private SkyPrisonSystemSETable _table;

    // ── 公开 API ──────────────────────────────────────────────────────────────

    /// <summary>播放指定类型的系统 SE（静态入口，无需手持引用）。</summary>
    public static void Play(SkyPrisonSystemSEType type)
    {
        if (type == SkyPrisonSystemSEType.None) return;
        Instance?.PlayInternal(type);
    }

    /// <summary>开始循环播放指定类型 SE（用于濒死等持续状态音）。</summary>
    public static void PlayLoop(SkyPrisonSystemSEType type)
    {
        if (type == SkyPrisonSystemSEType.None) return;
        Instance?.PlayLoopInternal(type);
    }

    /// <summary>停止循环 SE。</summary>
    public static void StopLoop()
    {
        var inst = Instance;
        if (inst?._loopSource != null && inst._loopSource.isPlaying)
            inst._loopSource.Stop();
    }

    /// <summary>濒死脉冲：主层正常播放 + sub-bass 层压低音高叠加低频感。</summary>
    public static void PlayNearDeathPulse(float subPitch, float subVolMul)
        => Instance?.PlayNearDeathPulseInternal(subPitch, subVolMul);

    /// <summary>直接播放指定 Clip（供物品材质音效表调用）。</summary>
    public static void PlayClip(AudioClip clip, float volume, float pitch)
    {
        var inst = Instance;
        if (inst == null || inst._source == null || clip == null) return;
        inst._source.pitch = pitch;
        inst._source.PlayOneShot(clip, Mathf.Max(0f, volume));
    }

    // ── 初始化 ────────────────────────────────────────────────────────────────

    private static void Bootstrap()
    {
        var go = new GameObject("[SystemSEPlayer]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<SkyPrisonSystemSEPlayer>();
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
        s_instance = this;

        // 2D AudioSource（UI 音效不需要空间定位）
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake   = false;
        _source.loop          = false;
        _source.spatialBlend  = 0f;
        _source.reverbZoneMix = 0f;

        _loopSource = gameObject.AddComponent<AudioSource>();
        _loopSource.playOnAwake   = false;
        _loopSource.loop          = true;
        _loopSource.spatialBlend  = 0f;
        _loopSource.reverbZoneMix = 0f;

        _table = Resources.Load<SkyPrisonSystemSETable>(SkyPrisonSystemSETable.ResourcesPath);
        if (_table == null)
            Debug.LogWarning("[SystemSEPlayer] 找不到 SE 音效表，请在 " +
                             SkyPrisonSystemSETable.ResourcesPath + " 创建并挂上 clip。");
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    // ── 播放 ──────────────────────────────────────────────────────────────────

    private void PlayInternal(SkyPrisonSystemSEType type)
    {
        if (_table == null || _source == null) return;
        if (!_table.TryGet(type, out var entry)) return;

        AudioClip clip = entry.clips == null || entry.clips.Length == 0 ? null : entry.clips[Random.Range(0, entry.clips.Length)];
        if (clip == null) return;

        // 音量：全局 uiVolume × masterVolume × 条目本地音量
        float vol = entry.volume;
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        if (gs != null) vol *= gs.uiVolume * gs.masterVolume;

        // 音高：基础 + 随机偏移
        float pitch = entry.pitch + Random.Range(-entry.pitchVariance, entry.pitchVariance);

        _source.pitch  = pitch;
        _source.volume = Mathf.Max(0f, vol);
        _source.PlayOneShot(clip);
    }

    private void PlayNearDeathPulseInternal(float subPitch, float subVolMul)
    {
        if (_table == null || _source == null) return;
        if (!_table.TryGet(SkyPrisonSystemSEType.NearDeath, out var entry)) return;
        if (entry.clips == null || entry.clips.Length == 0) return;

        AudioClip clip = entry.clips[Random.Range(0, entry.clips.Length)];
        if (clip == null) return;

        float vol = entry.volume;
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        if (gs != null) vol *= gs.uiVolume * gs.masterVolume;

        float pitch = entry.pitch + Random.Range(-entry.pitchVariance, entry.pitchVariance);

        // 主层
        _source.pitch = pitch;
        _source.PlayOneShot(clip, Mathf.Max(0f, vol));

        // sub-bass 层：同 clip 压低音高，叠出低频轰鸣
        _source.pitch = subPitch;
        _source.PlayOneShot(clip, Mathf.Max(0f, vol * subVolMul));

        _source.pitch = 1f; // 还原，避免影响后续 PlayOneShot
    }

    private void PlayLoopInternal(SkyPrisonSystemSEType type)
    {
        if (_table == null || _loopSource == null) return;
        if (!_table.TryGet(type, out var entry)) return;

        AudioClip clip = entry.clips[Random.Range(0, entry.clips.Length)];
        if (clip == null) return;

        float vol = entry.volume;
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        if (gs != null) vol *= gs.uiVolume * gs.masterVolume;

        _loopSource.clip   = clip;
        _loopSource.pitch  = entry.pitch;
        _loopSource.volume = Mathf.Max(0f, vol);
        _loopSource.Play();
    }
}
