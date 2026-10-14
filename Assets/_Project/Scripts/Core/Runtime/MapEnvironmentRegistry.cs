using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时可读的「场景 → 环境设置(天空盒/环境光/雾)」注册表。
///
/// 由编辑器在 MapDefinition 保存时自动同步（见 MapEnvironmentRegistryBuilder），运行时
/// 通过 Resources 加载。跟 MapBGMRegistry / MapWeatherRegistry / MapTriggerRegistry
/// 完全同一套模式。
///
/// 为什么需要这个：ApplyEnvironmentToScene 是编辑器工具，把 MapDefinition 上配置好的
/// 环境预设值（天空盒材质、环境光、雾）写进当前打开场景的 RenderSettings——但这一步
/// 要靠人手动点按钮才会执行。选了预设、甚至连天空盒材质都手动拖对了，只要没点那个
/// 按钮，RenderSettings 里存的还是旧值，场景文件保存下来的就是这份没同步的旧数据。
/// 这类"编辑器配置改了、运行时悄悄还在用旧值"的坑今天已经踩过好几次。
///
/// 运行时用 SkyPrisonMapEnvironmentRuntimeSync 在进图时读这份注册表，跟当前
/// RenderSettings 比一遍，不一致就直接改过来，不依赖美术记得点按钮。
/// </summary>
public sealed class MapEnvironmentRegistry : ScriptableObject
{
    public const string ResourceName = "MapEnvironmentRegistry";

    [Serializable]
    public class Entry
    {
        public string mapKey;
        public string sceneName;   // 场景文件名（无路径、无扩展名）
        public string scenePath;   // 完整场景路径（精确匹配用）

        public Material skyboxMaterial;
        public Color ambientColor;
        public float reflectionIntensity;

        public bool enableSceneFog;
        public bool enableHeightFog;
        public Color sceneFogColor;
        public float fogStartDistance;
        public float fogEndDistance;
    }

    public List<Entry> entries = new List<Entry>();

    private static MapEnvironmentRegistry _cached;
    private static bool _loaded;

    public static MapEnvironmentRegistry LoadOrNull()
    {
        if (_loaded)
            return _cached;

        _cached = Resources.Load<MapEnvironmentRegistry>(ResourceName);
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
