using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>地图编辑器里「某种资源箱这一把最多出几个」的一行配置（编辑器的 MapDefinition 和运行时注册表共用）。</summary>
[Serializable]
public class MapLootBoxConfig
{
    [Tooltip("箱子的单位定义（比如物资箱 A）。")]
    public UnitDefinition box;
    [Tooltip("白天版本这一把最多出几个。默认 0 = 不出。候选点位比这个多时随机抽这么多；点位不够就有多少出多少。（旧数据的「最多」会当作昼的数量。）")]
    [UnityEngine.Serialization.FormerlySerializedAs("maxCount")] [Min(0)] public int dayCount = 0;
    [Tooltip("夜间版本这一把最多出几个。默认 0 = 不出。想做「只在夜里才出的箱子」：昼填 0、夜填数量。")]
    [Min(0)] public int nightCount = 0;

    /// <summary>当前这一把（昼 / 夜）该出几个。</summary>
    public int CountFor(bool night) { return night ? nightCount : dayCount; }
    [Tooltip("同一种箱子被抽中的点位之间至少隔多少米，防止扎堆。0 = 不限制。")]
    [Min(0f)] public float minSpacing = 12f;
}

/// <summary>
/// 运行时可读的「场景 → 资源箱配置」注册表。跟 MapLocalizationRegistry 同一套模式：
/// MapDefinition 在 Editor 文件夹里、运行时不存在，所以由编辑器在地图保存时同步到这份 Resources 资源。
/// </summary>
public sealed class MapLootRegistry : ScriptableObject
{
    public const string ResourceName = "MapLootRegistry";

    [Serializable]
    public class Entry
    {
        public string sceneName;
        public List<MapLootBoxConfig> boxes = new List<MapLootBoxConfig>();
    }

    public List<Entry> entries = new List<Entry>();

    static MapLootRegistry cached; static bool loaded;
    public static MapLootRegistry LoadOrNull()
    {
        if (loaded) return cached;
        cached = Resources.Load<MapLootRegistry>(ResourceName); loaded = true; return cached;
    }
    public static void ClearCache() { cached = null; loaded = false; }

    public Entry FindByScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        foreach (var e in entries) if (e != null && e.sceneName == sceneName) return e;
        return null;
    }
}
