using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 音声设置子面板。
/// 读写 SaveManager.Settings 音量字段，并同步到 AudioMixer。
/// </summary>
public class SettingsAudioPanel : MonoBehaviour
{
    [Tooltip("项目的主 AudioMixer，留空则只改 Settings 不改 Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    // AudioMixer 中对应的 Exposed Parameter 名称
    private const string MixerMaster = "MasterVolume";
    private const string MixerMusic  = "MusicVolume";
    private const string MixerSFX    = "SFXVolume";
    private const string MixerVoice  = "VoiceVolume";

    private float _master, _music, _sfx, _voice;

    public void SetVisible(bool v) => gameObject.SetActive(v);

    public void Refresh()
    {
        var s = SaveManager.Settings;
        if (s == null) return;

        _master = s.masterVolume;
        _music  = s.musicVolume;
        _sfx    = s.sfxVolume;
        _voice  = s.voiceVolume;

        RefreshUI();
    }

    public void Apply()
    {
        var s = SaveManager.Settings;
        if (s == null) return;

        s.masterVolume = _master;
        s.musicVolume  = _music;
        s.sfxVolume    = _sfx;
        s.voiceVolume  = _voice;

        ApplyToMixer();
    }

    // ── 供 UI Slider 调用 ─────────────────────────────────────────────────

    public void SetMaster(float v) { _master = Mathf.Clamp01(v); ApplyToMixer(); }
    public void SetMusic (float v) { _music  = Mathf.Clamp01(v); ApplyToMixer(); }
    public void SetSFX   (float v) { _sfx    = Mathf.Clamp01(v); ApplyToMixer(); }
    public void SetVoice (float v) { _voice  = Mathf.Clamp01(v); ApplyToMixer(); }

    // ── 内部 ──────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        // TODO: Slider 控件绑定后在这里 push 值
    }

    private void ApplyToMixer()
    {
        if (audioMixer == null) return;
        // AudioMixer 用 dB，0.0001 防止 log(0)
        audioMixer.SetFloat(MixerMaster, LinearToDb(_master));
        audioMixer.SetFloat(MixerMusic,  LinearToDb(_music));
        audioMixer.SetFloat(MixerSFX,    LinearToDb(_sfx));
        audioMixer.SetFloat(MixerVoice,  LinearToDb(_voice));
    }

    private static float LinearToDb(float linear)
        => Mathf.Log10(Mathf.Max(linear, 0.0001f)) * 20f;
}
