using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音声合成器运行时接口段。
/// 目标：先让脚步声、攻击音效、地编、AI 等系统可以“查询音声包有哪些可激活层”，
/// 后续再把真正的播放实现接到 Runtime Audio Manager。
/// </summary>
public interface ISkyPrisonLayeredAudioPlayer
{
    void PlayLayeredPackage(SkyPrisonLayeredAudioPlayRequest request);
}

public interface ISkyPrisonAudioNoiseEmitter
{
    void EmitAudioNoise(SkyPrisonAudioNoiseEvent noiseEvent);
}

[Serializable]
public class SkyPrisonLayeredAudioPlayRequest
{
    public SkyPrisonAudioPackage package;
    public Vector3 worldPosition;

    /// <summary>
    /// 外部系统希望开启的音轨层。
    /// 例如脚步声规则表检测到浅水：base_impact + surface_water。
    /// </summary>
    public List<string> enabledLayerKeys = new List<string>();

    public SkyPrisonAudioFootSide footSide = SkyPrisonAudioFootSide.None;

    public string surfaceKey = "";
    public string footwearKey = "";
    public string movementStateKey = "";

    public float volumeMultiplier = 1f;
    public float pitchMultiplier = 1f;
    public float noiseMultiplier = 1f;

    public bool ignoreMute = false;
    public bool ignoreSolo = false;
}

[Serializable]
public class SkyPrisonAudioNoiseEvent
{
    public Vector3 worldPosition;
    public string sourceKey = "";
    public string surfaceKey = "";
    public string footwearKey = "";
    public string movementStateKey = "";

    public float radius = 0f;
    public float strength = 0f;
}

public struct SkyPrisonAudioTrackRuntimeMatch
{
    public int trackIndex;
    public SkyPrisonAudioTrack track;
    public SkyPrisonMixerChannel mixerChannel;

    public string runtimeLayerKey;
    public float volumeMultiplier;
    public float pan;
}

/// <summary>
/// 音声包运行时查询工具。
/// 注意：这里不负责播放，只负责把“上下文条件”解析成“哪些 Track 应该参与播放”。
/// </summary>
public static class SkyPrisonAudioPackageRuntimeQuery
{
    public static bool HasRuntimeLayer(SkyPrisonAudioPackage package, string runtimeLayerKey)
    {
        if (package == null || package.tracks == null || string.IsNullOrWhiteSpace(runtimeLayerKey))
            return false;

        for (int i = 0; i < package.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = package.tracks[i];
            if (track != null && track.runtimeLayerKey == runtimeLayerKey)
                return true;
        }

        return false;
    }

    public static List<string> CollectRuntimeLayerKeys(SkyPrisonAudioPackage package)
    {
        List<string> result = new List<string>();

        if (package == null || package.tracks == null)
            return result;

        for (int i = 0; i < package.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = package.tracks[i];
            if (track == null || string.IsNullOrWhiteSpace(track.runtimeLayerKey))
                continue;

            if (!result.Contains(track.runtimeLayerKey))
                result.Add(track.runtimeLayerKey);
        }

        return result;
    }

    public static List<SkyPrisonAudioTrackRuntimeMatch> FindPlayableTracks(SkyPrisonLayeredAudioPlayRequest request)
    {
        List<SkyPrisonAudioTrackRuntimeMatch> result = new List<SkyPrisonAudioTrackRuntimeMatch>();

        if (request == null || request.package == null || request.package.tracks == null)
            return result;

        SkyPrisonAudioPackage package = request.package;
        bool anySolo = !request.ignoreSolo && HasAnySolo(package);

        for (int i = 0; i < package.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = package.tracks[i];
            if (track == null)
                continue;

            SkyPrisonMixerChannel channel = package.FindMixerChannel(track.mixerChannelId);

            if (!request.ignoreMute)
            {
                bool muted = track.mute || (channel != null && channel.mute);
                if (muted)
                    continue;
            }

            if (anySolo)
            {
                bool solo = track.solo || (channel != null && channel.solo);
                if (!solo)
                    continue;
            }

            if (!LayerEnabledByRequest(track.runtimeLayerKey, request.enabledLayerKeys))
                continue;

            if (!FootSideMatches(track.footSideCondition, request.footSide))
                continue;

            result.Add(new SkyPrisonAudioTrackRuntimeMatch
            {
                trackIndex = i,
                track = track,
                mixerChannel = channel,
                runtimeLayerKey = track.runtimeLayerKey,
                volumeMultiplier = request.volumeMultiplier * (channel != null ? Mathf.Max(0f, channel.volume) : 1f),
                pan = channel != null ? Mathf.Clamp(channel.pan, -1f, 1f) : 0f
            });
        }

        return result;
    }

    public static bool LayerEnabledByRequest(string runtimeLayerKey, List<string> enabledLayerKeys)
    {
        if (string.IsNullOrWhiteSpace(runtimeLayerKey))
            return false;

        if (enabledLayerKeys == null || enabledLayerKeys.Count == 0)
            return false;

        return enabledLayerKeys.Contains(runtimeLayerKey);
    }

    public static bool FootSideMatches(SkyPrisonAudioFootSideCondition condition, SkyPrisonAudioFootSide footSide)
    {
        switch (condition)
        {
            case SkyPrisonAudioFootSideCondition.Any:
                return true;

            case SkyPrisonAudioFootSideCondition.LeftOnly:
                return footSide == SkyPrisonAudioFootSide.Left;

            case SkyPrisonAudioFootSideCondition.RightOnly:
                return footSide == SkyPrisonAudioFootSide.Right;

            default:
                return true;
        }
    }

    private static bool HasAnySolo(SkyPrisonAudioPackage package)
    {
        if (package == null || package.tracks == null)
            return false;

        for (int i = 0; i < package.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = package.tracks[i];
            if (track == null)
                continue;

            SkyPrisonMixerChannel channel = package.FindMixerChannel(track.mixerChannelId);
            if (track.solo || (channel != null && channel.solo))
                return true;
        }

        return false;
    }
}
