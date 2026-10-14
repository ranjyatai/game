using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把所有 MapDefinition 上配置好的环境设置（天空盒/环境光/雾）同步进运行时可读的
/// MapEnvironmentRegistry。跟 MapWeatherRegistryBuilder / MapBGMRegistryBuilder
/// 完全同一套模式。
///
/// 触发时机：
///   - 任意 MapDefinition 资源被导入/保存时自动重建
///   - 菜单「Sky Prison/地图/重建地图环境注册表」手动重建
/// </summary>
public class MapEnvironmentRegistryBuilder : AssetPostprocessor
{
    private const string RegistryFolder = "Assets/_Project/Resources";
    private const string RegistryPath = RegistryFolder + "/" + MapEnvironmentRegistry.ResourceName + ".asset";

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

    [MenuItem("天空囚笼/重建注册表/重建地图环境注册表", false, 163)]
    public static void RebuildMenu()
    {
        Rebuild(true);
    }

    private static void Rebuild(bool verbose)
    {
        MapEnvironmentRegistry registry = LoadOrCreateRegistry();
        registry.entries.Clear();

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { "Assets/_Project" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            if (map == null)
                continue;

            string sceneName = ResolveSceneName(map);

            registry.entries.Add(new MapEnvironmentRegistry.Entry
            {
                mapKey = map.mapKey,
                sceneName = sceneName,
                scenePath = map.scenePath,
                skyboxMaterial = map.skyboxMaterial,
                ambientColor = map.ambientColor,
                reflectionIntensity = map.skyboxMaterial != null
                    ? Mathf.Clamp01(map.environmentReflectionIntensity)
                    : 0f, // 跟 ApplyEnvironmentToScene 里那条注释同一个理由：
                          // 没有天空盒却留着反射强度没有任何意义。
                enableSceneFog = map.enableSceneFog && !map.enableHeightFog, // 同上，两套雾不能同时开。
                enableHeightFog = map.enableHeightFog,
                sceneFogColor = map.sceneFogColor,
                fogStartDistance = map.fogStartDistance,
                fogEndDistance = map.fogEndDistance,
            });
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        if (verbose)
            Debug.Log($"[MapEnvironmentRegistryBuilder] 地图环境注册表已重建，共 {registry.entries.Count} 个地图条目。");
    }

    private static string ResolveSceneName(MapDefinition map)
    {
        if (!string.IsNullOrEmpty(map.scenePath))
            return Path.GetFileNameWithoutExtension(map.scenePath);
        if (!string.IsNullOrEmpty(map.fileName))
            return map.fileName;
        return map.name;
    }

    private static MapEnvironmentRegistry LoadOrCreateRegistry()
    {
        MapEnvironmentRegistry registry = AssetDatabase.LoadAssetAtPath<MapEnvironmentRegistry>(RegistryPath);
        if (registry != null)
            return registry;

        if (!AssetDatabase.IsValidFolder(RegistryFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        }

        registry = ScriptableObject.CreateInstance<MapEnvironmentRegistry>();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();
        return registry;
    }
}
