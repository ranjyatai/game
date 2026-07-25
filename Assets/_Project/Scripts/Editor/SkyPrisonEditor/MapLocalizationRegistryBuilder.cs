using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把所有 MapDefinition 的「场景 → 多语言名称」同步进运行时可读的 MapLocalizationRegistry。
/// 跟 MapTriggerRegistryBuilder 完全同一套模式。
///
/// 触发时机：
///   - 任意 MapDefinition 资源被导入/保存时自动重建
///   - 菜单「Sky Prison/本地化/重建地图名称注册表」手动重建
/// </summary>
public class MapLocalizationRegistryBuilder : AssetPostprocessor
{
    private const string RegistryFolder = "Assets/_Project/Resources";
    private const string RegistryPath = RegistryFolder + "/" + MapLocalizationRegistry.ResourceName + ".asset";

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

    [MenuItem("Sky Prison/本地化/重建地图名称注册表")]
    public static void RebuildMenu()
    {
        Rebuild(true);
    }

    private static void Rebuild(bool verbose)
    {
        MapLocalizationRegistry registry = LoadOrCreateRegistry();
        registry.entries.Clear();

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { "Assets/_Project" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            if (map == null)
                continue;

            string sceneName = ResolveSceneName(map);

            MapLocalizationRegistry.Entry entry = new MapLocalizationRegistry.Entry
            {
                mapKey = map.mapKey,
                sceneName = sceneName,
                scenePath = map.scenePath
            };
            entry.localizedNames.AddRange(map.localizedNames);
            registry.entries.Add(entry);
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        MapLocalizationRegistry.ClearCache();

        if (verbose)
            Debug.Log($"[MapLocalizationRegistryBuilder] 地图名称注册表已重建，共 {registry.entries.Count} 个地图条目。");
    }

    private static string ResolveSceneName(MapDefinition map)
    {
        // 优先用 scenePath 推导场景文件名，其次用 fileName 字段
        if (!string.IsNullOrEmpty(map.scenePath))
            return Path.GetFileNameWithoutExtension(map.scenePath);
        if (!string.IsNullOrEmpty(map.fileName))
            return map.fileName;
        return map.name;
    }

    private static MapLocalizationRegistry LoadOrCreateRegistry()
    {
        MapLocalizationRegistry registry = AssetDatabase.LoadAssetAtPath<MapLocalizationRegistry>(RegistryPath);
        if (registry != null)
            return registry;

        if (!AssetDatabase.IsValidFolder(RegistryFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        }

        registry = ScriptableObject.CreateInstance<MapLocalizationRegistry>();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();
        return registry;
    }
}
