using UnityEngine;

/// <summary>
/// 运行时全局 asset 访问入口。
/// 优先从 GameAssetManifest 读取（已烘焙进 Resources）；
/// 编辑器下若 Manifest 为空则回退到 AssetDatabase 扫描（开发期兜底）。
/// </summary>
public static class GameAssetLoader
{
    private static GameAssetManifest _manifest;

    private static GameAssetManifest Manifest
    {
        get
        {
            if (_manifest != null) return _manifest;
            _manifest = Resources.Load<GameAssetManifest>("GameAssetManifest");
            if (_manifest == null)
                Debug.LogError("[GameAssetLoader] 找不到 GameAssetManifest。" +
                               "请在编辑器菜单 Sky Prison → 重建 Asset Manifest 执行一次。");
            return _manifest;
        }
    }

    // ── 公开访问属性 ──────────────────────────────────────────────────────

    public static ItemRegistry ItemRegistry
    {
        get
        {
            var r = Manifest?.itemRegistry;
#if UNITY_EDITOR
            if (r == null) r = EditorFallback<ItemRegistry>("t:ItemRegistry");
#endif
            return r;
        }
    }

    public static TechTreeGraphAsset TechTree
    {
        get
        {
            var r = Manifest?.techTreeGraph;
#if UNITY_EDITOR
            if (r == null) r = EditorFallback<TechTreeGraphAsset>("t:TechTreeGraphAsset");
#endif
            return r;
        }
    }

    // ── 编辑器回退（仅 Editor 编译）──────────────────────────────────────
#if UNITY_EDITOR
    private static T EditorFallback<T>(string filter) where T : UnityEngine.Object
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets(filter);
        if (guids.Length == 0) return null;
        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(
            UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
    }
#endif

    /// <summary>场景切换时清空缓存，强制重新从 Resources 加载。</summary>
    public static void ClearCache() => _manifest = null;
}
