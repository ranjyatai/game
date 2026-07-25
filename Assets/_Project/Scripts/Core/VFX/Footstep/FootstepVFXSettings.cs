using UnityEngine;

/// <summary>
/// 奔跑/跳跃扬尘特效全局配置。放在 Resources/FootstepVFXSettings.asset，
/// FootstepVFXManager 自动读取。素材来自 Stylized Smoke and Dust。
/// </summary>
[CreateAssetMenu(menuName = "SkyPrison/FootstepVFXSettings", fileName = "FootstepVFXSettings")]
public class FootstepVFXSettings : ScriptableObject
{
    [Header("奔跑扬尘（每步触发）")]
    public GameObject[] runDustPrefabs;

    [Header("起跳扬尘")]
    public GameObject[] jumpDustPrefabs;

    [Header("落地扬尘")]
    public GameObject[] landDustPrefabs;

    [Header("闪避扬尘")]
    public GameObject[] dodgeDustPrefabs;

    [Header("蓄力冲刺起手扬尘（角色定格蓄力那一刻，不是冲刺瞬间）")]
    public GameObject[] chargeDustPrefabs;

    [Header("空手攻击扬尘（只在未装备武器时触发）")]
    public GameObject[] unarmedAttackDustPrefabs;
}
