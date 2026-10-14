using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把「地形层 → 地表材质」登记进场景里的脚步声解析器（SkyPrisonGroundAudioSurfaceResolver）。
///
/// 背景：放置工具刷地表材质时按 surfaceId 生成 TL_&lt;surfaceId&gt; 地形层，但从来没有告诉
/// 解析器这个层对应哪个材质。解析器的兜底是按名字匹配，而层名多了 "TL_" 前缀对不上——
/// 实测 Still Vault 占地面 98% 的「马路-水泥3」「石砾」两层都解析为空，整张图走路都是
/// 默认鞋底声；新加的「金属格栅」同样如此。
///
/// 做法：写解析器的显式绑定表（terrainLayerBindings，优先级最高），同时把材质补进它的
/// surfaceDefinitions 列表。不改任何材质资产——材质上的 terrainLayer 字段语义另有用途，
/// 不在这里顺手覆盖。
///
/// 两个入口：
///   - 放置工具每次刷地表材质时自动调用 EnsureBinding（新材质一刷上去就有脚步声）；
///   - 菜单「修复当前场景地形脚步声绑定」按 TL_&lt;surfaceId&gt; 命名规则补全现有场景。
/// </summary>
public static class SkyPrisonGroundAudioBindingUtility
{
    private const string LogPrefix = "[GroundAudioBinding]";
    private const string TerrainLayerPrefix = "TL_";

    /// <returns>是否真的改了解析器。</returns>
    public static bool EnsureBinding(Terrain terrain, TerrainLayer layer, GroundSurfaceMaterialDefinition definition)
    {
        if (terrain == null || layer == null || definition == null)
            return false;

        SkyPrisonGroundAudioSurfaceResolver resolver = FindResolver(terrain);
        if (resolver == null)
            return false;

        var so = new SerializedObject(resolver);
        bool changed = false;

        SerializedProperty bindings = so.FindProperty("terrainLayerBindings");
        SerializedProperty existing = null;
        for (int i = 0; i < bindings.arraySize; i++)
        {
            SerializedProperty b = bindings.GetArrayElementAtIndex(i);
            if (b.FindPropertyRelative("terrainLayer").objectReferenceValue == layer)
            {
                existing = b;
                break;
            }
        }

        if (existing == null)
        {
            bindings.arraySize++;
            existing = bindings.GetArrayElementAtIndex(bindings.arraySize - 1);
            existing.FindPropertyRelative("terrainLayer").objectReferenceValue = layer;
            existing.FindPropertyRelative("surfaceDefinition").objectReferenceValue = null;
        }

        SerializedProperty boundDef = existing.FindPropertyRelative("surfaceDefinition");
        if (boundDef.objectReferenceValue != definition)
        {
            boundDef.objectReferenceValue = definition;
            changed = true;
        }

        SerializedProperty defs = so.FindProperty("surfaceDefinitions");
        bool listed = false;
        for (int i = 0; i < defs.arraySize; i++)
        {
            if (defs.GetArrayElementAtIndex(i).objectReferenceValue == definition)
            {
                listed = true;
                break;
            }
        }
        if (!listed)
        {
            defs.arraySize++;
            defs.GetArrayElementAtIndex(defs.arraySize - 1).objectReferenceValue = definition;
            changed = true;
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(resolver);
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(resolver.gameObject.scene);
            Debug.Log($"{LogPrefix} {layer.name} → {definition.displayName}（{definition.surfaceType}）已登记到 {resolver.gameObject.name} 的脚步声解析器。", resolver);
        }

        return changed;
    }

    [MenuItem("天空囚笼/地图/修复当前场景地形脚步声绑定", false, 117)]
    public static void RepairActiveScene()
    {
        // surfaceId → 定义。TL_<surfaceId> 就是放置工具生成地形层时用的命名规则。
        var byKey = new Dictionary<string, GroundSurfaceMaterialDefinition>();
        foreach (string guid in AssetDatabase.FindAssets("t:GroundSurfaceMaterialDefinition"))
        {
            var def = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (def == null || string.IsNullOrWhiteSpace(def.surfaceId))
                continue;
            byKey[def.surfaceId.Trim().ToLowerInvariant()] = def;
        }

        int bound = 0, unmatched = 0;
        foreach (Terrain terrain in Object.FindObjectsOfType<Terrain>())
        {
            if (terrain.terrainData == null)
                continue;

            foreach (TerrainLayer layer in terrain.terrainData.terrainLayers)
            {
                if (layer == null)
                    continue;

                string name = layer.name;
                if (!name.StartsWith(TerrainLayerPrefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    unmatched++;
                    continue;
                }

                string key = name.Substring(TerrainLayerPrefix.Length).Trim().ToLowerInvariant();
                if (!byKey.TryGetValue(key, out GroundSurfaceMaterialDefinition def))
                {
                    unmatched++;
                    continue;
                }

                if (EnsureBinding(terrain, layer, def))
                    bound++;
            }
        }

        Debug.Log($"{LogPrefix} 修复完成：新登记 {bound} 个地形层；{unmatched} 个层按 TL_<surfaceId> 规则找不到对应材质（保持原样）。记得保存场景。");
    }

    private static SkyPrisonGroundAudioSurfaceResolver FindResolver(Terrain terrain)
    {
        var resolver = terrain.GetComponent<SkyPrisonGroundAudioSurfaceResolver>();
        if (resolver != null)
            return resolver;

        resolver = Object.FindObjectOfType<SkyPrisonGroundAudioSurfaceResolver>();
        if (resolver == null)
            Debug.LogWarning($"{LogPrefix} 场景里没有 SkyPrisonGroundAudioSurfaceResolver，地形脚步声无从登记。", terrain);
        return resolver;
    }
}
