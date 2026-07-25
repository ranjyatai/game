using System;
using System.Collections.Generic;
using UnityEngine;

public enum SkyPrisonAudioPackageType
{
    [InspectorName("通用")]
    Generic,

    [InspectorName("脚步声")]
    Footstep,

    [InspectorName("战斗音效")]
    Combat,

    [InspectorName("界面音效")]
    UI,

    [InspectorName("环境音")]
    Ambience,

    [InspectorName("BGM")]
    BGM,

    [InspectorName("语音")]
    Voice
}

public enum SkyPrisonAudioPackagePlayMode
{
    [InspectorName("分层同时播放")]
    Layered,

    [InspectorName("随机音轨")]
    RandomTrack,

    [InspectorName("顺序音轨")]
    SequentialTrack
}

public enum SkyPrisonAudioFootSide
{
    [InspectorName("未指定")]
    None,

    [InspectorName("左脚")]
    Left,

    [InspectorName("右脚")]
    Right
}

public enum SkyPrisonAudioFootSideCondition
{
    [InspectorName("任意")]
    Any,

    [InspectorName("仅左脚")]
    LeftOnly,

    [InspectorName("仅右脚")]
    RightOnly
}

[Serializable]
public class SkyPrisonAudioSegment
{
    public string segmentId = Guid.NewGuid().ToString("N");
    public string displayName = "Segment";

    public AudioClip sourceClip;

    [Min(0f)] public float timelineStart = 0f;
    [Min(0f)] public float sourceStart = 0f;
    [Min(0f)] public float sourceEnd = 1f;

    [Range(0f, 2f)] public float volume = 1f;
    [Range(0.25f, 3f)] public float pitch = 1f;
    [Range(-1f, 1f)] public float pan = 0f;

    [Min(0f)] public float fadeIn = 0f;
    [Min(0f)] public float fadeOut = 0f;

    [Min(0f)] public float randomWeight = 1f;
    public string tag = "";

    public float Duration
    {
        get
        {
            if (sourceClip == null)
                return Mathf.Max(0f, sourceEnd - sourceStart);

            float start = Mathf.Clamp(sourceStart, 0f, sourceClip.length);
            float end = Mathf.Clamp(sourceEnd, start, sourceClip.length);
            return Mathf.Max(0f, end - start);
        }
    }

    public void ClampToClip()
    {
        if (sourceClip == null)
        {
            sourceStart = Mathf.Max(0f, sourceStart);
            sourceEnd = Mathf.Max(sourceStart, sourceEnd);
            return;
        }

        sourceStart = Mathf.Clamp(sourceStart, 0f, sourceClip.length);
        sourceEnd = Mathf.Clamp(sourceEnd, sourceStart, sourceClip.length);

        if (sourceEnd <= sourceStart)
            sourceEnd = Mathf.Min(sourceClip.length, sourceStart + 0.1f);
    }
}

[Serializable]
public class SkyPrisonAudioTrack
{
    public string trackId = Guid.NewGuid().ToString("N");
    public string displayName = "Audio Track";

    // 运行时层 Key：给脚步声、攻击音效、环境音等控制器按条件激活音轨使用。
    // 例如：base_impact / surface_water / gear_jingle。
    public string runtimeLayerKey = "";

    // 脚侧条件：用于脚步声。非脚步声包可以保持 Any。
    public SkyPrisonAudioFootSideCondition footSideCondition = SkyPrisonAudioFootSideCondition.Any;

    public Color color = new Color(0.95f, 0.35f, 0.80f, 1f);

    public bool mute = false;
    public bool solo = false;
    public bool locked = false;

    public string mixerChannelId = "";
    public List<SkyPrisonAudioSegment> segments = new List<SkyPrisonAudioSegment>();
}

[Serializable]
public class SkyPrisonAudioEffectSlot
{
    public bool enabled = true;
    public string effectName = "Empty";
}

[Serializable]
public class SkyPrisonMixerChannel
{
    public string channelId = Guid.NewGuid().ToString("N");
    public string displayName = "Channel";

    [Range(0f, 2f)] public float volume = 1f;
    [Range(-1f, 1f)] public float pan = 0f;

    public bool mute = false;
    public bool solo = false;

    [Range(0f, 1f)] public float editorMeterPreview = 0f;

    public List<SkyPrisonAudioEffectSlot> effects = new List<SkyPrisonAudioEffectSlot>();
}

[CreateAssetMenu(
    fileName = "SAP_NewAudioPackage",
    menuName = "Sky Prison/Audio/Audio Package",
    order = 1700)]
public class SkyPrisonAudioPackage : ScriptableObject
{
    public string packageKey = "new_audio_package";
    public string displayName = "新音声包";
    public SkyPrisonAudioPackageType packageType = SkyPrisonAudioPackageType.Generic;
    public SkyPrisonAudioPackagePlayMode playMode = SkyPrisonAudioPackagePlayMode.Layered;

    [Header("全局播放参数")]
    [Min(0.1f)] public float timelineLengthSeconds = 8f;

    [Range(0f, 2f)] public float masterVolume = 1f;
    public Vector2 randomVolumeRange = new Vector2(0.95f, 1.05f);
    public Vector2 randomPitchRange = new Vector2(0.96f, 1.04f);

    [Header("空间音频")]
    public bool spatial3D = true;
    public float minDistance = 1f;
    public float maxDistance = 12f;

    [Header("AI 噪声参数")]
    public float noiseRadius = 5f;
    public float noiseStrength = 1f;

    [Header("时间线 / 调音台")]
    public List<SkyPrisonAudioTrack> tracks = new List<SkyPrisonAudioTrack>();
    public List<SkyPrisonMixerChannel> mixerChannels = new List<SkyPrisonMixerChannel>();

    public void EnsureValid()
    {
        if (tracks == null)
            tracks = new List<SkyPrisonAudioTrack>();

        if (mixerChannels == null)
            mixerChannels = new List<SkyPrisonMixerChannel>();

        if (string.IsNullOrWhiteSpace(packageKey))
            packageKey = name;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = name;

        timelineLengthSeconds = Mathf.Max(0.1f, timelineLengthSeconds);

        for (int i = 0; i < tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = tracks[i];
            if (track == null)
                continue;

            if (string.IsNullOrWhiteSpace(track.trackId))
                track.trackId = Guid.NewGuid().ToString("N");

            if (string.IsNullOrWhiteSpace(track.displayName))
                track.displayName = "Track " + (i + 1);

            if (track.runtimeLayerKey == null)
                track.runtimeLayerKey = "";

            if (string.IsNullOrWhiteSpace(track.mixerChannelId))
            {
                SkyPrisonMixerChannel channel = CreateMixerChannelForTrack(track);
                mixerChannels.Add(channel);
            }
            else if (FindMixerChannel(track.mixerChannelId) == null)
            {
                SkyPrisonMixerChannel channel = CreateMixerChannelForTrack(track);
                channel.channelId = track.mixerChannelId;
                mixerChannels.Add(channel);
            }

            if (track.segments == null)
                track.segments = new List<SkyPrisonAudioSegment>();

            for (int j = 0; j < track.segments.Count; j++)
                track.segments[j]?.ClampToClip();
        }

        RemoveOrphanMixerChannels();
    }

    public SkyPrisonAudioTrack AddTrack(string name = null)
    {
        if (tracks == null)
            tracks = new List<SkyPrisonAudioTrack>();
        if (mixerChannels == null)
            mixerChannels = new List<SkyPrisonMixerChannel>();

        int index = tracks.Count + 1;
        SkyPrisonAudioTrack track = new SkyPrisonAudioTrack
        {
            displayName = string.IsNullOrWhiteSpace(name) ? "Track " + index : name,
            color = PickTrackColor(index)
        };

        SkyPrisonMixerChannel channel = CreateMixerChannelForTrack(track);
        tracks.Add(track);
        mixerChannels.Add(channel);

        return track;
    }

    public void RemoveTrackAt(int index)
    {
        if (tracks == null || index < 0 || index >= tracks.Count)
            return;

        string channelId = tracks[index].mixerChannelId;
        tracks.RemoveAt(index);

        if (!string.IsNullOrWhiteSpace(channelId) && mixerChannels != null)
            mixerChannels.RemoveAll(x => x != null && x.channelId == channelId);
    }

    public SkyPrisonMixerChannel FindMixerChannel(string channelId)
    {
        if (mixerChannels == null || string.IsNullOrWhiteSpace(channelId))
            return null;

        for (int i = 0; i < mixerChannels.Count; i++)
        {
            SkyPrisonMixerChannel channel = mixerChannels[i];
            if (channel != null && channel.channelId == channelId)
                return channel;
        }

        return null;
    }

    private SkyPrisonMixerChannel CreateMixerChannelForTrack(SkyPrisonAudioTrack track)
    {
        SkyPrisonMixerChannel channel = new SkyPrisonMixerChannel
        {
            displayName = track != null ? track.displayName : "Channel"
        };

        if (track != null)
            track.mixerChannelId = channel.channelId;

        return channel;
    }

    private void RemoveOrphanMixerChannels()
    {
        if (mixerChannels == null || tracks == null)
            return;

        mixerChannels.RemoveAll(channel =>
        {
            if (channel == null)
                return true;

            for (int i = 0; i < tracks.Count; i++)
            {
                SkyPrisonAudioTrack track = tracks[i];
                if (track != null && track.mixerChannelId == channel.channelId)
                    return false;
            }

            return true;
        });
    }

    private Color PickTrackColor(int index)
    {
        Color[] colors =
        {
            new Color(0.95f, 0.35f, 0.80f, 1f),
            new Color(0.40f, 0.85f, 0.55f, 1f),
            new Color(0.95f, 0.55f, 0.20f, 1f),
            new Color(0.45f, 0.60f, 1.00f, 1f),
            new Color(0.75f, 0.45f, 1.00f, 1f),
            new Color(1.00f, 0.80f, 0.25f, 1f),
        };

        return colors[Mathf.Abs(index - 1) % colors.Length];
    }
}
