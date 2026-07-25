using UnityEditor;
using UnityEngine;

/// <summary>
/// 自动维护 GameAssetManifest。
/// 触发时机：
///   1. 菜单 Sky Prison → 重建 Asset Manifest（手动）
///   2. AssetPostprocessor：每次 asset 导入/删除/移动后自动重建
/// </summary>
public class GameAssetManifestBuilder : AssetPostprocessor
{
    private const string ManifestResourcePath = "Assets/_Project/Data/Resources/GameAssetManifest.asset";

    // ── 菜单入口 ──────────────────────────────────────────────────────────

    [MenuItem("Sky Prison/重建 Asset Manifest", priority = 50)]
    public static void RebuildManifest()
    {
        var manifest = GetOrCreateManifest();
        Fill(manifest);
        EditorUtility.SetDirty(manifest);
        AssetDatabase.SaveAssets();
        Debug.Log("[GameAssetManifest] 重建完成。");
    }

    // ── AssetPostprocessor 自动触发 ───────────────────────────────────────

    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool relevant = false;
        foreach (var p in imported) if (IsRelevant(p)) { relevant = true; break; }
        if (!relevant) foreach (var p in deleted) if (IsRelevant(p)) { relevant = true; break; }
        if (!relevant) foreach (var p in moved)   if (IsRelevant(p)) { relevant = true; break; }
        if (!relevant) return;

        // 延迟一帧，等 AssetDatabase 刷新完毕
        EditorApplication.delayCall += RebuildManifest;
    }

    private static bool IsRelevant(string path)
    {
        return path.EndsWith(".asset") &&
               (path.Contains("ItemDefinition") ||
                path.Contains("ItemRegistry")   ||
                path.Contains("TechTree"));
    }

    // ── 核心填充逻辑 ──────────────────────────────────────────────────────

    private static void Fill(GameAssetManifest manifest)
    {
        // ItemRegistry：取第一个找到的
        manifest.itemRegistry = FindFirst<ItemRegistry>("t:ItemRegistry");

        // TechTreeGraphAsset：取第一个找到的
        manifest.techTreeGraph = FindFirst<TechTreeGraphAsset>("t:TechTreeGraphAsset");

        // ── 后续新增 asset 类型在这里加一行 FindFirst ──
    }

    // ── 工具 ──────────────────────────────────────────────────────────────

    private static T FindFirst<T>(string filter) where T : Object
    {
        string[] guids = AssetDatabase.FindAssets(filter);
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
        }
        return null;
    }

    private static GameAssetManifest GetOrCreateManifest()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameAssetManifest>(ManifestResourcePath);
        if (existing != null) return existing;

        // 确保 Resources 目录存在
        const string dir = "Assets/_Project/Data/Resources";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets/_Project/Data", "Resources");
            AssetDatabase.Refresh();
        }

        var manifest = ScriptableObject.CreateInstance<GameAssetManifest>();
        AssetDatabase.CreateAsset(manifest, ManifestResourcePath);
        AssetDatabase.SaveAssets();
        return manifest;
    }
}
