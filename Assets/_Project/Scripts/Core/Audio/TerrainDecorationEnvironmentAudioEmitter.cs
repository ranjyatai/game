using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地形装饰物环境音运行时播放器。
/// 绑定 SkyPrisonAudioPackage，要求 packageType = Ambience。
/// 第一版目标：让地图装饰物能携带 3D 环境音，距离越远越衰减。
/// </summary>
[DisallowMultipleComponent]
public class TerrainDecorationEnvironmentAudioEmitter : MonoBehaviour
{
    [Header("Audio Package")]
    public SkyPrisonAudioPackage audioPackage;

    [Header("3D Range")]
    [Min(0f)] public float minDistance = 1f;
    [Min(0.1f)] public float maxDistance = 12f;
    [Range(0f, 2f)] public float volume = 1f;
    public bool loop = true;
    public bool playOnEnable = true;

    private readonly List<AudioSource> runtimeSources = new List<AudioSource>();

    private void OnEnable()
    {
        if (Application.isPlaying && playOnEnable)
            Play();
    }

    private void OnDisable()
    {
        Stop();
    }

    public void ApplyFromDefinition(TerrainDecorationDefinition definition)
    {
        if (definition == null || !definition.enableEnvironmentAudio)
        {
            audioPackage = null;
            return;
        }

        audioPackage = definition.environmentAudioPackage;
        minDistance = Mathf.Max(0f, definition.environmentAudioMinDistance);
        maxDistance = Mathf.Max(minDistance + 0.01f, definition.environmentAudioMaxDistance);
        volume = Mathf.Clamp(definition.environmentAudioVolume, 0f, 2f);
        loop = definition.environmentAudioLoop;
    }

    public void Play()
    {
        Stop();

        if (audioPackage == null)
            return;

        audioPackage.EnsureValid();

        if (audioPackage.packageType != SkyPrisonAudioPackageType.Ambience)
        {
            Debug.LogWarning($"[TerrainDecorationEnvironmentAudioEmitter] 音声包不是环境音类型：{audioPackage.name}", this);
            return;
        }

        List<SkyPrisonAudioSegment> segments = CollectPlayableSegments(audioPackage);
        if (segments.Count == 0)
            return;

        if (audioPackage.playMode == SkyPrisonAudioPackagePlayMode.RandomTrack)
        {
            SkyPrisonAudioSegment selected = PickRandomSegment(segments);
            if (selected != null)
                CreateAndPlaySource(selected, audioPackage.masterVolume);
            return;
        }

        // 环境音默认按分层同时播放。SequentialTrack 在环境音里也先退化为首段播放，避免一次性刷太多音源。
        if (audioPackage.playMode == SkyPrisonAudioPackagePlayMode.SequentialTrack)
        {
            CreateAndPlaySource(segments[0], audioPackage.masterVolume);
            return;
        }

        for (int i = 0; i < segments.Count; i++)
            CreateAndPlaySource(segments[i], audioPackage.masterVolume);
    }

    public void Stop()
    {
        for (int i = runtimeSources.Count - 1; i >= 0; i--)
        {
            AudioSource source = runtimeSources[i];
            if (source == null)
                continue;

            if (Application.isPlaying)
                Destroy(source);
            else
                DestroyImmediate(source);
        }
        runtimeSources.Clear();
    }

    private List<SkyPrisonAudioSegment> CollectPlayableSegments(SkyPrisonAudioPackage package)
    {
        List<SkyPrisonAudioSegment> result = new List<SkyPrisonAudioSegment>();
        if (package == null || package.tracks == null)
            return result;

        for (int i = 0; i < package.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = package.tracks[i];
            if (track == null || track.mute || track.segments == null)
                continue;

            SkyPrisonMixerChannel channel = package.FindMixerChannel(track.mixerChannelId);
            if (channel != null && channel.mute)
                continue;

            for (int j = 0; j < track.segments.Count; j++)
            {
                SkyPrisonAudioSegment segment = track.segments[j];
                if (segment != null && segment.sourceClip != null)
                    result.Add(segment);
            }
        }

        return result;
    }

    private SkyPrisonAudioSegment PickRandomSegment(List<SkyPrisonAudioSegment> segments)
    {
        if (segments == null || segments.Count == 0)
            return null;

        float total = 0f;
        for (int i = 0; i < segments.Count; i++)
            total += Mathf.Max(0.001f, segments[i].randomWeight);

        float value = Random.value * total;
        float acc = 0f;
        for (int i = 0; i < segments.Count; i++)
        {
            acc += Mathf.Max(0.001f, segments[i].randomWeight);
            if (value <= acc)
                return segments[i];
        }

        return segments[segments.Count - 1];
    }

    private void CreateAndPlaySource(SkyPrisonAudioSegment segment, float packageMasterVolume)
    {
        if (segment == null || segment.sourceClip == null)
            return;

        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = segment.sourceClip;
        source.volume = Mathf.Clamp01(packageMasterVolume * segment.volume * volume);
        source.pitch = Mathf.Clamp(segment.pitch, 0.1f, 3f);
        source.panStereo = Mathf.Clamp(segment.pan, -1f, 1f);
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = Mathf.Max(0f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, maxDistance);
        runtimeSources.Add(source);
        source.Play();
    }
}
