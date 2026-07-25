using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时可读的「这是一张真实地图场景」名单。由编辑器在 MapDefinition 保存时无条件
/// 同步（见 MapSceneRegistryBuilder）——不像 MapBGMRegistry/MapWeatherRegistry 那样
/// 要求"配了内容才收录"，只要存在对应的 MapDefinition 就收录，用来判断"当前场景是不是
/// 该走完整读条流程的地图场景"（主菜单、LoadingScene 等不会出现在这张名单里）。
/// 跟 MapBGMRegistry 同一套模式。
/// </summary>
public sealed class MapSceneRegistry : ScriptableObject
{
    public const string ResourceName = "MapSceneRegistry";

    [Serializable]
    public class Entry
    {
        public string mapKey;
        public string sceneName;
        public string scenePath;
    }

    public List<Entry> entries = new List<Entry>();

    private static MapSceneRegistry _cached;
    private static bool _loaded;

    public static MapSceneRegistry LoadOrNull()
    {
        if (_loaded)
            return _cached;

        _cached = Resources.Load<MapSceneRegistry>(ResourceName);
        _loaded = true;
        return _cached;
    }

    public static void ClearCache()
    {
        _cached = null;
        _loaded = false;
    }

    public bool ContainsScene(string sceneName, string scenePath)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(scenePath) && e.scenePath == scenePath)
                return true;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null) continue;
            if (!string.IsNullOrEmpty(sceneName) && e.sceneName == sceneName)
                return true;
        }

        return false;
    }
}
