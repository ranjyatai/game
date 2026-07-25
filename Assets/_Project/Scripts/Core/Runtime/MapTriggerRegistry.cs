using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时可读的「场景 → 触发器包」注册表。
///
/// 由编辑器在 MapDefinition 保存时自动同步（见 MapTriggerRegistryBuilder），
/// 运行时通过 Resources 加载。这样运行时无需引用 Editor 程序集里的 MapDefinition，
/// 也无需在每个场景里手动挂组件——初始加载时自动按当前场景查表生成触发器节点。
/// </summary>
public sealed class MapTriggerRegistry : ScriptableObject
{
    public const string ResourceName = "MapTriggerRegistry";

    [Serializable]
    public class Entry
    {
        public string mapKey;
        public string sceneName;   // 场景文件名（无路径、无扩展名）
        public string scenePath;   // 完整场景路径（精确匹配用）
        public List<TriggerPackage> triggerPackages = new List<TriggerPackage>();
    }

    public List<Entry> entries = new List<Entry>();

    private static MapTriggerRegistry _cached;
    private static bool _loaded;

    public static MapTriggerRegistry LoadOrNull()
    {
        if (_loaded)
            return _cached;

        _cached = Resources.Load<MapTriggerRegistry>(ResourceName);
        _loaded = true;
        return _cached;
    }

    public static void ClearCache()
    {
        _cached = null;
        _loaded = false;
    }

    public List<TriggerPackage> GetForScene(string sceneName, string scenePath)
    {
        // 优先精确路径匹配，其次场景名匹配
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(scenePath) && e.scenePath == scenePath)
                return e.triggerPackages;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(sceneName) && e.sceneName == sceneName)
                return e.triggerPackages;
        }

        return null;
    }
}
