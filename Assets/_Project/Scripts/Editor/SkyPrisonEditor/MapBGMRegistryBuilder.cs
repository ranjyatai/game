using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把所有 MapDefinition 的「场景 → BGM配置」绑定同步进运行时可读的 MapBGMRegistry。
/// 跟 MapTriggerRegistryBuilder 完全同一套模式。
///
/// 触发时机：
///   - 任意 MapDefinition 资源被导入/保存时自动重建
///   - 菜单「Sky Prison/音频/重建地图BGM注册表」手动重建
/// </summary>
public class MapBGMRegistryBuilder : AssetPostprocessor
{
    private const string RegistryFolder = "Assets/_Project/Resources";
    private const string RegistryPath = RegistryFolder + "/" + MapBGMRegistry.ResourceName + ".asset";

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

    [MenuItem("Sky Prison/音频/重建地图BGM注册表")]
    public static void RebuildMenu()
    {
        Rebuild(true);
    }

    private static void Rebuild(bool verbose)
    {
        MapBGMRegistry registry = LoadOrCreateRegistry();
        registry.entries.Clear();

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { "Assets/_Project" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            if (map == null)
                continue;

            bool hasExplore = map.exploreBgmClips != null && map.exploreBgmClips.Any(c => c != null);
            bool hasCombat = map.combatBgmClips != null && map.combatBgmClips.Any(c => c != null);
            if (!hasExplore && !hasCombat)
                continue;

            string sceneName = ResolveSceneName(map);

            MapBGMRegistry.Entry entry = new MapBGMRegistry.Entry
            {
                mapKey = map.mapKey,
                sceneName = sceneName,
                scenePath = map.scenePath,
                sequentialPlayMode = map.bgmPlayMode == MapBGMPlayMode.Sequential,
                crossfadeDuration = map.bgmCrossfadeDuration,
                volume = map.bgmVolume,
            };
            if (map.exploreBgmClips != null)
                entry.exploreClips.AddRange(map.exploreBgmClips.Where(c => c != null));
            if (map.combatBgmClips != null)
                entry.combatClips.AddRange(map.combatBgmClips.Where(c => c != null));

            registry.entries.Add(entry);
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        if (verbose)
            Debug.Log($"[MapBGMRegistryBuilder] 地图BGM注册表已重建，共 {registry.entries.Count} 个地图条目。");
    }

    private static string ResolveSceneName(MapDefinition map)
    {
        if (!string.IsNullOrEmpty(map.scenePath))
            return Path.GetFileNameWithoutExtension(map.scenePath);
        if (!string.IsNullOrEmpty(map.fileName))
            return map.fileName;
        return map.name;
    }

    private static MapBGMRegistry LoadOrCreateRegistry()
    {
        MapBGMRegistry registry = AssetDatabase.LoadAssetAtPath<MapBGMRegistry>(RegistryPath);
        if (registry != null)
            return registry;

        if (!AssetDatabase.IsValidFolder(RegistryFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        }

        registry = ScriptableObject.CreateInstance<MapBGMRegistry>();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();
        return registry;
    }
}
