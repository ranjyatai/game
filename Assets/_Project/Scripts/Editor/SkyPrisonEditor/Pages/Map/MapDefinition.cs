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
