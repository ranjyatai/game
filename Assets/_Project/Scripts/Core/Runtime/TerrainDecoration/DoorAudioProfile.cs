using System;
using UnityEngine;

/// <summary>
/// 门的音效配置（一份可以被很多扇门共用的资源）。门组件上引用它；留空就是没有声音。
/// 创建：Project 里右键 Create → 天空囚笼 → 门音效配置。
/// 命名约定沿用项目：素材放 Assets/_Project/Data/Audio/SFX/Door/，命名 SFX_Door_<动作><变体>。
/// </summary>
[CreateAssetMenu(fileName = "DoorAudio_New", menuName = "天空囚笼/门音效配置")]
public sealed class DoorAudioProfile : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public AudioClip clip;
        [Range(0f, 2f)] public float volume = 1f;
        [Tooltip("音高随机范围（±），0 = 不随机。")]
        [Range(0f, 0.3f)] public float pitchJitter = 0.04f;
    }

    [Header("一次性音效（多条会随机选一条）")]
    [Tooltip("开始开门的那一刻。")]
    public Entry[] open;
    [Tooltip("开始关门的那一刻。")]
    public Entry[] close;
    [Tooltip("门板滑到位、撞到头的那一刻（开到位和关到位都播）。")]
    public Entry[] stop;
    [Tooltip("锁着、条件不满足时，玩家按交互键。")]
    public Entry[] denied;
    [Tooltip("门自己的交互反馈（按下交互键的瞬间，开关门之前）。")]
    public Entry[] interact;

    [Header("滑动中循环")]
    [Tooltip("门板移动期间循环播放，到位后停止。留空则不循环。")]
    public Entry moveLoop;

    [Header("空间感")]
    [Tooltip("勾上 = 3D 音源，随玩家离门远近衰减；不勾 = 2D，音量固定。")]
    public bool spatial = true;
    public float minDistance = 4f;
    public float maxDistance = 40f;
    [Range(0f, 2f)] public float masterVolume = 1f;
}
