using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>把所有 MapDefinition 的「资源箱配置」同步进运行时可读的 MapLootRegistry（和 MapLocalizationRegistryBuilder 同一套模式）。</summary>
public class MapLootRegistryBuilder : AssetPostprocessor
{
    private const string RegistryFolder = "Assets/_Project/Resources";
    private const string RegistryPath = RegistryFolder + "/" + MapLootRegistry.ResourceName + ".asset";

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Any(IsMap) || deleted.Any(IsMap) || moved.Any(IsMap)) Rebuild(false);
    }

    private static bool IsMap(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset")) return false;
        return AssetDatabase.LoadAssetAtPath<MapDefinition>(path) != null;
    }

    [MenuItem("天空囚笼/重建注册表/重建资源箱注册表", false, 167)]
    public static void RebuildMenu() { Rebuild(true); }

    private static void Rebuild(bool verbose)
    {
        var registry = AssetDatabase.LoadAssetAtPath<MapLootRegistry>(RegistryPath);
        if (registry == null)
        {
            registry = ScriptableObject.CreateInstance<MapLootRegistry>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }
        registry.entries.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:MapDefinition", new[] { "Assets/_Project" }))
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (map == null || map.lootBoxes == null || map.lootBoxes.Count == 0) continue;
            string sceneName = !string.IsNullOrEmpty(map.scenePath) ? Path.GetFileNameWithoutExtension(map.scenePath) : (!string.IsNullOrEmpty(map.fileName) ? map.fileName : map.name);
            var e = new MapLootRegistry.Entry { sceneName = sceneName };
            foreach (var c in map.lootBoxes) if (c != null && c.box != null) e.boxes.Add(new MapLootBoxConfig { box = c.box, dayCount = c.dayCount, nightCount = c.nightCount, minSpacing = c.minSpacing });
            if (e.boxes.Count > 0) registry.entries.Add(e);
        }
        EditorUtility.SetDirty(registry); AssetDatabase.SaveAssets(); MapLootRegistry.ClearCache();
        if (verbose) Debug.Log($"[MapLootRegistryBuilder] 资源箱注册表已重建，共 {registry.entries.Count} 张地图有配置。");
    }
}
