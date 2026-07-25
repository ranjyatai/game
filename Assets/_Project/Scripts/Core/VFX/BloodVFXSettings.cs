using UnityEngine;

/// <summary>
/// 血液特效全局配置。放在 Resources/BloodVFXSettings.asset，Manager 自动读取。
/// 在 Project 窗口右键 → Create → SkyPrison → BloodVFXSettings 创建，
/// 把 URP 飞溅预制体拖进列表，保存到 Assets/Resources/BloodVFXSettings.asset。
/// </summary>
[CreateAssetMenu(menuName = "SkyPrison/BloodVFXSettings", fileName = "BloodVFXSettings")]
public class BloodVFXSettings : ScriptableObject
{
    [Header("Normal / DarkBlood")]
    [Tooltip("URP 飞溅粒子预制体（Splash/）")]
    public GameObject[] splashPrefabs;

    [Tooltip("URP 地面贴花预制体（Decal_Projector/ Static 或 SpriteSheet）")]
    public GameObject[] decalPrefabs;

    [Tooltip("Static_Projector_URP 材质列表（直接创建 DecalProjector，不依赖 RVFX 生命周期）")]
    public Material[] decalMaterials;

    [Header("MetalSpark（机械单位）")]
    [Tooltip("金属火花粒子预制体。为空时回退到 Normal 飞溅。")]
    public GameObject[] metalSparkPrefabs;

    [Header("EnergyBurst（魔法/精灵单位）")]
    [Tooltip("能量爆散粒子预制体。为空时回退到 Normal 飞溅。")]
    public GameObject[] energyBurstPrefabs;
}
