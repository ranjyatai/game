using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把所有 MapDefinition 对应的场景名无条件同步进运行时可读的 MapSceneRegistry——
/// 跟 MapBGMRegistryBuilder 同一套模式，区别是不筛选"有没有配内容"，只要存在
/// MapDefinition 就收录，供 SkyPrisonRuntimeBootCoordinator 判断"当前场景是不是该走
/// 完整读条流程的地图场景"。
///
/// 触发时机：
///   - 任意 MapDefinition 资源被导入/保存时自动重建
///   - 菜单「Sky Prison/地图/重建地图场景名单」手动重建
/// </summary>
public class MapSceneRegistryBuilder : AssetPostprocessor
{
    private const string RegistryFolder = "Assets/_Project/Resources";
    private const string RegistryPath = RegistryFolder + "/" + MapSceneRegistry.ResourceName + ".asset";

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

    [MenuItem("Sky Prison/地图/重建地图场景名单")]
    public static void RebuildMenu()
    {
        Rebuild(true);
    }

    private static void Rebuild(bool verbose)
    {
        MapSceneRegistry registry = LoadOrCreateRegistry();
        registry.entries.Clear();

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { "Assets/_Project" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            if (map == null)
                continue;

            registry.entries.Add(new MapSceneRegistry.Entry
            {
                mapKey = map.mapKey,
                sceneName = ResolveSceneName(map),
                scenePath = map.scenePath,
            });
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        if (verbose)
            Debug.Log($"[MapSceneRegistryBuilder] 地图场景名单已重建，共 {registry.entries.Count} 个地图条目。");
    }

    private static string ResolveSceneName(MapDefinition map)
    {
        if (!string.IsNullOrEmpty(map.scenePath))
            return Path.GetFileNameWithoutExtension(map.scenePath);
        if (!string.IsNullOrEmpty(map.fileName))
            return map.fileName;
        return map.name;
    }

    private static MapSceneRegistry LoadOrCreateRegistry()
    {
        MapSceneRegistry registry = AssetDatabase.LoadAssetAtPath<MapSceneRegistry>(RegistryPath);
        if (registry != null)
            return registry;

        if (!AssetDatabase.IsValidFolder(RegistryFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        }

        registry = ScriptableObject.CreateInstance<MapSceneRegistry>();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();
        return registry;
    }
}
