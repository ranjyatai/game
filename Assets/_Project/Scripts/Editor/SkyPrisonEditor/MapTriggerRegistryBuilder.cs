using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把所有 MapDefinition 的「场景 → 触发器包」绑定同步进运行时可读的 MapTriggerRegistry。
///
/// 触发时机：
///   - 任意 MapDefinition 资源被导入/保存时自动重建
///   - 菜单「Sky Prison/触发器/重建触发器注册表」手动重建
/// </summary>
public class MapTriggerRegistryBuilder : AssetPostprocessor
{
    private const string RegistryFolder = "Assets/_Project/Resources";
    private const string RegistryPath = RegistryFolder + "/" + MapTriggerRegistry.ResourceName + ".asset";

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

    [MenuItem("Sky Prison/触发器/重建触发器注册表")]
    public static void RebuildMenu()
    {
        Rebuild(true);
    }

    private static void Rebuild(bool verbose)
    {
        MapTriggerRegistry registry = LoadOrCreateRegistry();
        registry.entries.Clear();

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { "Assets/_Project" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            if (map == null)
                continue;
            if (map.triggerPackages == null || map.triggerPackages.Count == 0)
                continue;

            string sceneName = ResolveSceneName(map);

            MapTriggerRegistry.Entry entry = new MapTriggerRegistry.Entry
            {
                mapKey = map.mapKey,
                sceneName = sceneName,
                scenePath = map.scenePath
            };
            entry.triggerPackages.AddRange(map.triggerPackages.Where(p => p != null));
            registry.entries.Add(entry);
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        if (verbose)
            Debug.Log($"[MapTriggerRegistryBuilder] 触发器注册表已重建，共 {registry.entries.Count} 个地图条目。");
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

    private static MapTriggerRegistry LoadOrCreateRegistry()
    {
        MapTriggerRegistry registry = AssetDatabase.LoadAssetAtPath<MapTriggerRegistry>(RegistryPath);
        if (registry != null)
            return registry;

        if (!AssetDatabase.IsValidFolder(RegistryFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        }

        registry = ScriptableObject.CreateInstance<MapTriggerRegistry>();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();
        return registry;
    }
}
