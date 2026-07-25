using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时可读的「场景 → 多语言名称」注册表。
///
/// 地图编辑器里配置的多语言名称存在 MapDefinition 上，但那个类放在 Scripts/Editor/
/// 文件夹里——Editor 文件夹下的脚本只会编译进编辑器程序集，正式打包的游戏运行时
/// 根本不存在这个类型。这份表由编辑器在 MapDefinition 保存时自动同步
/// （见 MapLocalizationRegistryBuilder），运行时通过 Resources 加载，
/// 跟 MapTriggerRegistry 完全同一套模式。
/// </summary>
public sealed class MapLocalizationRegistry : ScriptableObject
{
    public const string ResourceName = "MapLocalizationRegistry";

    [Serializable]
    public class Entry
    {
        public string mapKey;
        public string sceneName;   // 场景文件名（无路径、无扩展名），匹配存档里的 mapId/chapterId
        public string scenePath;   // 完整场景路径（精确匹配用）
        public List<LocalizedTextEntry> localizedNames = new List<LocalizedTextEntry>();
    }

    public List<Entry> entries = new List<Entry>();

    private static MapLocalizationRegistry _cached;
    private static bool _loaded;

    public static MapLocalizationRegistry LoadOrNull()
    {
        if (_loaded)
            return _cached;

        _cached = Resources.Load<MapLocalizationRegistry>(ResourceName);
        _loaded = true;
        return _cached;
    }

    public static void ClearCache()
    {
        _cached = null;
        _loaded = false;
    }

    /// <summary>按场景名查询当前语言下的显示名称，找不到就返回 fallback。</summary>
    public string GetDisplayName(string sceneName, string fallback)
    {
        if (string.IsNullOrEmpty(sceneName)) return fallback;

        foreach (var e in entries)
        {
            if (e == null || e.sceneName != sceneName) continue;

            string result = LocalizationRuntime.Instance != null
                ? LocalizationRuntime.Instance.GetText(e.localizedNames, fallback)
                : LocalizationRuntime.Resolve(e.localizedNames, "zh-CN", fallback);
            return string.IsNullOrEmpty(result) ? fallback : result;
        }

        return fallback;
    }
}
