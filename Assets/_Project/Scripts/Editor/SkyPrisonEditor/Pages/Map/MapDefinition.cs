using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(
    fileName = "MapDefinition",
    menuName = "Sky Prison/Map Definition",
    order = 1350)]
public class MapDefinition : ScriptableObject
{
    [Header("Identity")]
    public string mapKey = "new_map";
    public string fileName = "NewMap";
    public string displayName = "新地图";
    [TextArea(2, 4)]
    public string description = "";

    [Header("Localization")]
    public List<LocalizedTextEntry> localizedNames = new List<LocalizedTextEntry>();
    public List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();

    [Header("Scene Binding")]
    public string scenePath = "";
    public string sceneGuid = "";

    [Header("Map Bounds")]
    public Vector3 mapBoundsCenter = Vector3.zero;
    public Vector3 mapBoundsSize = new Vector3(64f, 6f, 64f);

    [Header("Physical Map Bounds")]
    public bool enablePhysicalMapBounds = true;
    [Min(0.1f)] public float mapBoundsWallThickness = 2f;
    [Min(0.1f)] public float mapBoundsWallHeight = 12f;
    public bool mapBoundsUseCeiling = false;
    [Min(0.1f)] public float mapBoundsCeilingThickness = 1f;

    [Header("Fog Of War")]
    public bool enableFogOfWar = true;
    [Range(0f, 1f)] public float fogStrength = 0.72f;
    [Min(0.1f)] public float fogSoftEdgeWidth = 3.5f;

    [Header("Environment")]
    public MapEnvironmentPreset environmentPreset = MapEnvironmentPreset.Day;
    public GameObject skyRenderModel;
    public Material skyboxMaterial;

    [Tooltip("RenderSettings Ambient 的颜色，也是默认环境 Area Light 的颜色。")]
    public Color ambientColor = new Color(0.72f, 0.70f, 0.67f, 1f);

    [Tooltip("天空盒环境反射强度。地下场景必须调到 0——否则就算环境光归零、灯全删光，" +
        "所有 PBR 材质仍然会从天空盒吃到一层反射亮度，画面永远黑不下来。" +
        "Skybox 材质留空时这个值会被强制当 0 处理。")]
    [Range(0f, 1f)] public float environmentReflectionIntensity = 1f;

    [Tooltip("地图默认环境补光。只能作为柔光使用，不再默认生成 Point Light。")]
    public Color environmentAreaLightColor = new Color(0.72f, 0.70f, 0.67f, 1f);
    [Min(0f)] public float environmentAreaLightIntensity = 0.45f;
    [Min(0.1f)] public float environmentAreaLightSize = 16f;
    public Vector3 environmentAreaLightPosition = new Vector3(0f, 8f, 0f);
    public Vector3 environmentAreaLightEuler = new Vector3(90f, 0f, 0f);

    public Color mainLightColor = Color.white;
    [Min(0f)] public float mainLightIntensity = 0.6f;
    public Vector3 mainLightEuler = new Vector3(50f, -30f, 0f);

    public bool enableSceneFog = false;
    public Color sceneFogColor = new Color(0.55f, 0.58f, 0.62f, 1f);
    [Min(0f)] public float fogStartDistance = 20f;
    [Min(0.1f)] public float fogEndDistance = 80f;

    // ── 体积高度雾（Atmospheric Height Fog）────────────────────────────────
    // Unity 自带的 RenderSettings 线性雾只有"离相机越远越白"这一个维度，没有高度
    // 分层、没有颜色渐变、不吃光照方向，地下/室内看起来非常假。这一组接的是
    // BOXOPHOBIC 的 Atmospheric Height Fog，它支持正交相机(FogCameraMode.Orthographic)，
    // 这一点对本项目是硬需求——大部分体积雾方案只做透视。
    //
    // 开启时会自动关掉 RenderSettings 那套线性雾，两套同时开会叠成一片糊。

    // 这里不加 [Header]：地图编辑器面板用 DrawRow 把标签和字段排成两列，
    // PropertyField 会把 Header 画进值列里、再把勾选框顶到下一行。分组标题由
    // SkyPrisonMapInspectorPanel 手绘，和其它分组的做法一致。
    [Tooltip("启用后接管场景雾，上面那套 RenderSettings 线性雾会被自动关闭。")]
    public bool enableHeightFog = false;

    [Tooltip("近处的雾色。地下建议用接近环境色的深蓝黑，不要用灰白——灰白会让暗部发灰、失去黑。")]
    public Color heightFogColorStart = new Color(0.05f, 0.06f, 0.08f, 1f);
    [Tooltip("远处的雾色。跟近处拉开一点色相差能做出纵深，但别差太多，否则像贴了张渐变纸。")]
    public Color heightFogColorEnd = new Color(0.10f, 0.12f, 0.16f, 1f);

    [Range(0f, 1f)] public float heightFogIntensity = 1f;

    [Tooltip("两种雾色的混合权重。0=几乎只用近处色，1=沿距离在两色之间完整过渡。" +
             "官方样本用 1，是双色渐变能看出来的前提；留 0 的话远处雾色基本不起作用。")]
    [Range(0f, 1f)] public float heightFogColorDuo = 1f;

    [Tooltip("多远开始起雾。可以为负——负值表示雾从相机所在位置就已经存在，" +
             "官方样本用 -60。填正数会在近处切出一圈完全没有雾的清晰区，边界很硬。")]
    public float heightFogDistanceStart = -60f;
    [Tooltip("多远完全被雾吞掉")]
    [Min(0.1f)] public float heightFogDistanceEnd = 60f;
    [Tooltip("距离衰减曲线。1=线性（官方样本值），>1 更集中在远处，<1 近处就开始明显")]
    [Range(0.1f, 4f)] public float heightFogDistanceFalloff = 1f;

    [Tooltip("雾的底面高度（世界 Y）")]
    public float heightFogHeightStart = 0f;
    [Tooltip("雾的顶面高度（世界 Y）。地下空间不高，这个值别照抄室外的 100")]
    public float heightFogHeightEnd = 15f;
    [Tooltip("高度衰减曲线。1=线性（官方样本值）")]
    [Range(0.1f, 4f)] public float heightFogHeightFalloff = 1f;

    [Tooltip("噪声流动速度。官方样本是 (0.5, 0.5, 0)，组件默认的 (0.5, 0, 0.5) 是水平飘，" +
             "样本那组带 Y 分量，雾会有缓慢的上下翻涌感。")]
    public Vector3 heightFogNoiseSpeed = new Vector3(0.5f, 0.5f, 0f);
    [Tooltip("噪声的作用距离上限。组件默认 200 对室外大场景，样本收到 30——" +
             "地下空间近，30 才能在可视范围内看出噪声的流动。")]
    [Min(1f)] public float heightFogNoiseDistanceEnd = 30f;

    public VolumeProfile postProcessProfile;
    public GameObject environmentFxPrefab;
    public bool enableDayNightCycle = false;
    [Range(0f, 24f)] public float startTimeOfDay = 12f;

    [Header("Weather")]
    public bool enableWeather = false;
    public MapWeatherType weatherType = MapWeatherType.None;

    // 每种天气类型的专属参数各自独立成一个子结构——不同天气需要调的东西本来就不一样
    // （扬尘只有强度，雨天还要控制镜头湿润度，以后雪/雾各自也会加自己专属的参数），
    // 摊平成一堆共用字段的话，选了扬尘还得看到一个跟扬尘毫无关系的"镜头湿润强度"，
    // 一堆参数混在一起容易应该配哪个都记混。
    public DustWeatherParams dustWeather = new DustWeatherParams();
    public RainWeatherParams rainWeather = new RainWeatherParams();
    public SnowWeatherParams snowWeather = new SnowWeatherParams();
    public FogWeatherParams weatherFog = new FogWeatherParams();

    [Header("Camera")]
    public bool enableDepthOfField = false;
    [Range(0.1f, 50f)] public float focusDistance = 8f;
    [Range(0f, 1f)] public float blurStrength = 0.4f;

    [Header("Trigger Packages")]
    public List<TriggerPackage> triggerPackages = new List<TriggerPackage>();

    [Header("BGM")]
    [Tooltip("探索状态下轮播的曲目，可以放多首。")]
    public List<AudioClip> exploreBgmClips = new List<AudioClip>();
    [Tooltip("战斗状态下轮播的曲目（任意敌人看见玩家时切过来），可以放多首。留空时进入战斗维持探索曲目不切。")]
    public List<AudioClip> combatBgmClips = new List<AudioClip>();
    public MapBGMPlayMode bgmPlayMode = MapBGMPlayMode.Random;
    [Min(0.1f)] public float bgmCrossfadeDuration = 3f;
    [Range(0f, 2f)] public float bgmVolume = 1f;
}

public enum MapBGMPlayMode
{
    [InspectorName("随机")] Random = 0,
    [InspectorName("顺序")] Sequential = 1,
}

public enum MapEnvironmentPreset
{
    Custom = 0,
    Day = 1,
    Morning = 2,
    Dusk = 3,
    Night = 4,
    InteriorCold = 5,
    Underground = 6,
    AlertRed = 7,
    PollutedFog = 8,
}

public enum MapWeatherType
{
    [InspectorName("无")] None = 0,
    [InspectorName("小雨")] Rain = 1,
    [InspectorName("暴雨")] HeavyRain = 2,
    [InspectorName("雾")] Fog = 3,
    [InspectorName("雪")] Snow = 4,
    [InspectorName("扬尘")] Dust = 5,
}

[System.Serializable]
public class DustWeatherParams
{
    [Range(0f, 10f)] public float intensity = 5f;
}

[System.Serializable]
public class RainWeatherParams
{
    [Range(0f, 10f)] public float intensity = 5f;
    [Range(0f, 1f)] public float lensWetnessIntensity = 0f;
}

[System.Serializable]
public class SnowWeatherParams
{
    [Range(0f, 10f)] public float intensity = 5f;
}

[System.Serializable]
public class FogWeatherParams
{
    [Range(0f, 10f)] public float intensity = 5f;
}
