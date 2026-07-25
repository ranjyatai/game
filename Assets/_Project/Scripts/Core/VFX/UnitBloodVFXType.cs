/// <summary>
/// 单位受击出血演出类型。在 UnitDefinition 里配置，驱动 BloodVFXManager 行为。
/// </summary>
public enum UnitBloodVFXType
{
    [UnityEngine.InspectorName("普通血液")]
    Normal = 0,
    [UnityEngine.InspectorName("无出血")]
    None = 1,
    [UnityEngine.InspectorName("暗色血液")]
    DarkBlood = 2,
    [UnityEngine.InspectorName("金属火花")]
    MetalSpark = 3,
    [UnityEngine.InspectorName("能量爆散")]
    EnergyBurst = 4,
}
