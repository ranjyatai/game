using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时可读的「场景 → 地图BGM配置」注册表。
///
/// 由编辑器在 MapDefinition 保存时自动同步（见 MapBGMRegistryBuilder），运行时通过
/// Resources 加载。这样运行时无需引用 Editor 程序集里的 MapDefinition，也无需在每个
/// 场景里手动挂 MapBGMController——初始加载时自动按当前场景查表生成。
/// 跟 MapTriggerRegistry 是完全同一套模式，两者独立维护互不影响。
/// </summary>
public sealed class MapBGMRegistry : ScriptableObject
{
    public const string ResourceName = "MapBGMRegistry";

    [Serializable]
    public class Entry
    {
        public string mapKey;
        public string sceneName;   // 场景文件名（无路径、无扩展名）
        public string scenePath;   // 完整场景路径（精确匹配用）
        public List<AudioClip> exploreClips = new List<AudioClip>();
        public List<AudioClip> combatClips = new List<AudioClip>();
        public bool sequentialPlayMode;
        public float crossfadeDuration = 1.5f;
        public float volume = 1f;
    }

    public List<Entry> entries = new List<Entry>();

    private static MapBGMRegistry _cached;
    private static bool _loaded;

    public static MapBGMRegistry LoadOrNull()
    {
        if (_loaded)
            return _cached;

        _cached = Resources.Load<MapBGMRegistry>(ResourceName);
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
