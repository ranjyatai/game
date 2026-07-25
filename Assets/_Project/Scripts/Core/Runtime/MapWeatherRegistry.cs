using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时可读的「场景 → 天气特效」注册表。
///
/// 由编辑器在 MapDefinition 保存时自动同步（见 MapWeatherRegistryBuilder），运行时
/// 通过 Resources 加载。天气类型→预制体的挑选（SkyPrisonWeatherEffectLibrary.Resolve）
/// 在编辑器构建注册表这一步就做完了，这里存的是已经解析好的具体预制体，运行时不需要
/// 引用 Editor 程序集里的 MapDefinition/SkyPrisonWeatherEffectLibrary。
/// 跟 MapBGMRegistry / MapTriggerRegistry 完全同一套模式。
/// </summary>
public sealed class MapWeatherRegistry : ScriptableObject
{
    public const string ResourceName = "MapWeatherRegistry";

    [Serializable]
    public class Entry
    {
        public string mapKey;
        public string sceneName;   // 场景文件名（无路径、无扩展名）
        public string scenePath;   // 完整场景路径（精确匹配用）
        public GameObject weatherPrefab;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        // 只有 Rain/HeavyRain 才有意义，驱动镜头雨滴特效（RaindropFX_URP）的强度，
        // 0~1。跟 weatherPrefab 是两件独立的事——weatherPrefab 目前还没有雨天粒子
        // 预制体（Resolve() 对 Rain 返回 null），但镜头湿润效果不依赖粒子预制体，
        // 该出现还是要出现。
        public float lensWetnessIntensity;
        // 暴雨才需要打雷闪烁——跟 weatherPrefab 是否解析成功无关，运行时不依赖
        // Editor 程序集里的 MapWeatherType，构建注册表时把判断结果直接存成布尔值。
        public bool isHeavyRain;
        // 雨天环境音循环（BGS）——Rain/HeavyRain各自固定一条，音量按降雨强度算好，
        // 运行时直接拿来用，不需要再依赖Editor程序集判断天气类型/强度。
        public AudioClip ambientClip;
        public float ambientVolume;
        // 只有 isHeavyRain 才有意义——打雷随机音效候选池。
        public AudioClip[] thunderClips;
    }

    public List<Entry> entries = new List<Entry>();

    private static MapWeatherRegistry _cached;
    private static bool _loaded;

    public static MapWeatherRegistry LoadOrNull()
    {
        if (_loaded)
            return _cached;

        _cached = Resources.Load<MapWeatherRegistry>(ResourceName);
        _loaded = true;
        return _cached;
    }

    public static void ClearCache()
    {
        _cached = null;
        _loaded = false;
    }

    public Entry GetForScene(string sceneName, string scenePath)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(scenePath) && e.scenePath == scenePath)
                return e;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(sceneName) && e.sceneName == sceneName)
                return e;
        }

        return null;
    }
}
