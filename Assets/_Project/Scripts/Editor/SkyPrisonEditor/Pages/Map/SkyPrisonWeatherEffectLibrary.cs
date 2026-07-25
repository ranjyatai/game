using UnityEngine;

/// <summary>
/// 天气类型 → 特效预制体的全局映射表，供地图编辑器同步天气注册表时按
/// MapDefinition.weatherType + 对应天气类型自己的强度参数（如 DustWeatherParams.intensity）
/// 自动挑选对应预制体，不用每张地图手动拖一遍。只在编辑器流程里用
/// （MapWeatherRegistryBuilder），不参与运行时——运行时读的是已经解析好的
/// MapWeatherRegistry，不直接依赖这个类。
/// </summary>
[CreateAssetMenu(menuName = "SkyPrison/Map/WeatherEffectLibrary", fileName = "SkyPrisonWeatherEffectLibrary")]
public class SkyPrisonWeatherEffectLibrary : ScriptableObject
{
    [Header("扬尘（弱/中/强，按 DustWeatherParams.intensity 三档选取；当前配的是 Falling Ash 素材）")]
    public GameObject dustLightPrefab;
    public GameObject dustMediumPrefab;
    public GameObject dustHeavyPrefab;

    [Header("雨（Rain VFX[URP]资产的World Rain三档，Rain/HeavyRain两种天气类型共用这一套）")]
    public GameObject rainLightPrefab;
    public GameObject rainMediumPrefab;
    public GameObject rainHeavyPrefab;

    [Header("雨天环境音（BGS）——Rain/HeavyRain各自固定一条环境音循环，不分强度档")]
    public AudioClip rainAmbientClip;
    public AudioClip heavyRainAmbientClip;

    [Header("暴雨打雷音效——随机挑一条播，配合闪光做\"先闪后响\"")]
    public AudioClip[] heavyRainThunderClips;

    /// <summary>按天气类型解析出对应的环境音循环（BGS），跟粒子预制体是两条独立的路。</summary>
    public AudioClip ResolveAmbient(MapWeatherType type)
    {
        switch (type)
        {
            case MapWeatherType.Rain: return rainAmbientClip;
            case MapWeatherType.HeavyRain: return heavyRainAmbientClip;
            default: return null;
        }
    }

    /// <summary>按天气类型 + 强度(0~1)解析出对应预制体，弱/中/强三档以 1/3、2/3 为界。</summary>
    public GameObject Resolve(MapWeatherType type, float intensity)
    {
        switch (type)
        {
            case MapWeatherType.Dust:
                if (intensity < 1f / 3f) return dustLightPrefab;
                if (intensity < 2f / 3f) return dustMediumPrefab;
                return dustHeavyPrefab;
            // 小雨(Rain)在Light/Medium之间选，暴雨(HeavyRain)在Medium/Heavy之间选——
            // 保证"暴雨"哪怕强度调到最低也至少是Medium档，不会比小雨还稀。
            case MapWeatherType.Rain:
                return intensity < 0.5f ? rainLightPrefab : rainMediumPrefab;
            case MapWeatherType.HeavyRain:
                return intensity < 0.5f ? rainMediumPrefab : rainHeavyPrefab;
            default:
                return null;
        }
    }
}
