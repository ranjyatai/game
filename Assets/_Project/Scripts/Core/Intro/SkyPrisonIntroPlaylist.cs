using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

public enum SkyPrisonIntroType
{
    BlackFlameShader = 0,   // Black Flame Works 片头（着色器实时渲染，不是视频）
    Video = 1,              // 视频片头
    Image = 2,              // 一张图（淡入 → 停留 → 淡出）
}

/// <summary>片头列表里的一项。</summary>
[Serializable]
public class SkyPrisonIntroEntry
{
    public bool enabled = true;
    public string label = "新片头";
    public SkyPrisonIntroType type = SkyPrisonIntroType.BlackFlameShader;

    [Tooltip("BlackFlameShader：LOGO 遮罩（R=图形 G=文字 B=柔光）")]
    public Texture2D mask;
    [Tooltip("Video：视频文件")]
    public VideoClip video;
    [Tooltip("Image：显示的图")]
    public Texture2D image;
    public float imageDuration = 2.5f;
    public float fadeIn = 0.4f;
    public float fadeOut = 0.4f;

    [Tooltip("音效（视频自带声音时不用填）")]
    public AudioClip audio;
    public float audioDelay = 0f;
    [Range(0f, 1f)] public float audioVolume = 1f;

    public bool IsPlayable()
    {
        switch (type)
        {
            case SkyPrisonIntroType.BlackFlameShader: return mask != null;
            case SkyPrisonIntroType.Video: return video != null;
            case SkyPrisonIntroType.Image: return image != null;
        }
        return false;
    }

    public float Duration()
    {
        switch (type)
        {
            case SkyPrisonIntroType.BlackFlameShader: return SkyPrisonBlackFlameTimeline.Duration;
            case SkyPrisonIntroType.Video: return video != null ? (float)video.length : 0f;
            case SkyPrisonIntroType.Image: return fadeIn + imageDuration + fadeOut;
        }
        return 0f;
    }
}

/// <summary>开头商标 / 片头播放列表：按从上到下的顺序依次播放，玩家点一下直接跳过全部。放在 Resources/Intro 下。</summary>
public class SkyPrisonIntroPlaylist : ScriptableObject
{
    public const string ResourcePath = "Intro/SkyPrisonIntroPlaylist";

    public List<SkyPrisonIntroEntry> entries = new List<SkyPrisonIntroEntry>();

    [Tooltip("勾上后在编辑器里直接跳过片头（打包出来的游戏不受影响），省得每次进 Play 都等一遍。")]
    public bool skipInEditor = false;
}

/// <summary>Black Flame Works 片头的时间线：真实时间 → 着色器内部时钟。离线渲染和游戏内播放共用，保证一致。</summary>
public static class SkyPrisonBlackFlameTimeline
{
    public const float Duration = 4.0f;          // 实际片长（秒）
    public const float ShaderDuration = 14f;     // 着色器内部时钟总长

    // 0–0.3s：纯黑里一粒火种在闪（着色器 0.3→0.95）；0.3s 火焰窜出，0.3–0.77s 匀速长成大火（着色器 0.95→7），0.77s 爆炸；
    // 爆炸之后加快 2.2 倍。LeadIn 之前画面是黑的，火焰不会一上来就有。
    public const float LeadIn = 0.3f;
    public static float Warp(float t)
    {
        if (t < LeadIn) return 0.3f + t * (0.65f / LeadIn);
        if (t < 0.77f) return 0.95f + (t - LeadIn) * (6.05f / 0.47f);
        return Mathf.Min(ShaderDuration, 7f + (t - 0.77f) * 2.2f);
    }
}
