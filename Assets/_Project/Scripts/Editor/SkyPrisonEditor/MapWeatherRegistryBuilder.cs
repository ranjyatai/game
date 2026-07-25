using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把所有 MapDefinition 的天气配置（启用/类型/强度）解析成具体预制体，同步进运行时
/// 可读的 MapWeatherRegistry。跟 MapBGMRegistryBuilder / MapTriggerRegistryBuilder
/// 完全同一套模式。
///
/// 触发时机：
///   - 任意 MapDefinition 资源被导入/保存时自动重建
///   - 菜单「Sky Prison/地图/重建地图天气注册表」手动重建
/// </summary>
public class MapWeatherRegistryBuilder : AssetPostprocessor
{
    private const string RegistryFolder = "Assets/_Project/Resources";
    private const string RegistryPath = RegistryFolder + "/" + MapWeatherRegistry.ResourceName + ".asset";
    private const string LibraryPath = "Assets/_Project/Data/Definitions/Custom/VFX/SkyPrisonWeatherEffectLibrary.asset";

    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool touchedMap =
            imported.Any(IsMapDefinitionPath) ||
            deleted.Any(IsMapDefinitionPath) ||
            moved.Any(IsMapDefinitionPath);

        if (touchedMap)
            Rebuild(false);
    }

    private static bool IsMapDefinitionPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset"))
            return false;
        return AssetDatabase.LoadAssetAtPath<MapDefinition>(path) != null;
    }

    [MenuItem("Sky Prison/地图/重建地图天气注册表")]
    public static void RebuildMenu()
    {
        Rebuild(true);
    }

    private static void Rebuild(bool verbose)
    {
        SkyPrisonWeatherEffectLibrary library = AssetDatabase.LoadAssetAtPath<SkyPrisonWeatherEffectLibrary>(LibraryPath);

        MapWeatherRegistry registry = LoadOrCreateRegistry();
        registry.entries.Clear();

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { "Assets/_Project" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            if (map == null)
                continue;

            if (!map.enableWeather || map.weatherType == MapWeatherType.None)
                continue;

            float intensity = ResolveIntensity(map);
            GameObject prefab = library != null ? library.Resolve(map.weatherType, intensity) : null;

            // 镜头湿润强度只有 Rain/HeavyRain 才有意义，跟 prefab 是否解析成功无关——
            // 目前 Rain 还没配粒子预制体（Resolve() 对 Rain 返回 null），但镜头雨滴
            // 效果不依赖粒子预制体，该出现还是要出现，不能因为 prefab 是 null 就跳过
            // 整条记录。
            float lensWetness = (map.weatherType == MapWeatherType.Rain || map.weatherType == MapWeatherType.HeavyRain)
                ? map.rainWeather.lensWetnessIntensity
                : 0f;

            AudioClip ambientClip = library != null ? library.ResolveAmbient(map.weatherType) : null;

            // 环境音音量跟着降雨强度走（用户明确要求），整体再压低一档——用户反馈上一版
            // (小雨0.15~0.4/暴雨0.35~0.6)整体还是偏响，这次统一往下调。
            float ambientVolume = 0f;
            if (map.weatherType == MapWeatherType.Rain)
                ambientVolume = Mathf.Lerp(0.08f, 0.22f, intensity);
            else if (map.weatherType == MapWeatherType.HeavyRain)
                ambientVolume = Mathf.Lerp(0.18f, 0.35f, intensity);

            if (prefab == null && lensWetness <= 0.001f && ambientClip == null)
                continue; // 真的什么都没有才跳过这条地图

            string sceneName = ResolveSceneName(map);

            registry.entries.Add(new MapWeatherRegistry.Entry
            {
                mapKey = map.mapKey,
                sceneName = sceneName,
                scenePath = map.scenePath,
                weatherPrefab = prefab,
                boundsCenter = map.mapBoundsCenter,
                boundsSize = map.mapBoundsSize,
                lensWetnessIntensity = lensWetness,
                isHeavyRain = map.weatherType == MapWeatherType.HeavyRain,
                ambientClip = ambientClip,
                ambientVolume = ambientVolume,
                thunderClips = (map.weatherType == MapWeatherType.HeavyRain && library != null)
                    ? library.heavyRainThunderClips
                    : null,
            });
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        if (verbose)
            Debug.Log($"[MapWeatherRegistryBuilder] 地图天气注册表已重建，共 {registry.entries.Count} 个地图条目。");
    }

    // 每种天气类型的强度存在各自独立的参数子结构里（DustWeatherParams.intensity /
    // RainWeatherParams.intensity / ...），按当前选中的天气类型取对应那一个。
    // 编辑器表单上是 0~10（给地图作者更粗的刻度好调），这里换算回 Resolve() 用的
    // 0~1，不用去改 Resolve() 内部的三档判断阈值。
    private static float ResolveIntensity(MapDefinition map)
    {
        float raw;
        switch (map.weatherType)
        {
            case MapWeatherType.Dust: raw = map.dustWeather.intensity; break;
            case MapWeatherType.Rain:
            case MapWeatherType.HeavyRain: raw = map.rainWeather.intensity; break;
            case MapWeatherType.Snow: raw = map.snowWeather.intensity; break;
            case MapWeatherType.Fog: raw = map.weatherFog.intensity; break;
            default: return 0f;
        }
        return raw / 10f;
    }

    private static string ResolveSceneName(MapDefinition map)
    {
        if (!string.IsNullOrEmpty(map.scenePath))
            return Path.GetFileNameWithoutExtension(map.scenePath);
        if (!string.IsNullOrEmpty(map.fileName))
            return map.fileName;
        return map.name;
    }

    private static MapWeatherRegistry LoadOrCreateRegistry()
    {
        MapWeatherRegistry registry = AssetDatabase.LoadAssetAtPath<MapWeatherRegistry>(RegistryPath);
        if (registry != null)
            return registry;

        if (!AssetDatabase.IsValidFolder(RegistryFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        }

        registry = ScriptableObject.CreateInstance<MapWeatherRegistry>();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();
        return registry;
    }
}
