#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

static class CharacterPresenceFeatureCleanup
{
    [MenuItem("SkyPrison/Rendering/清理重复的 CharacterPresenceFeature")]
    public static void CleanupDuplicates()
    {
        string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        bool dirty = false;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) continue;

            var toRemove = new System.Collections.Generic.List<ScriptableRendererFeature>();
            foreach (var f in data.rendererFeatures)
                if (f is CharacterPresenceFeature) toRemove.Add(f);

            if (toRemove.Count == 0) continue;

            foreach (var f in toRemove)
            {
                data.rendererFeatures.Remove(f);
                // 从 asset 里删除子对象
                if (AssetDatabase.IsSubAsset(f))
                    AssetDatabase.RemoveObjectFromAsset(f);
                Object.DestroyImmediate(f, true);
            }

            EditorUtility.SetDirty(data);
            dirty = true;
            Debug.Log($"[Cleanup] 已从 {path} 移除 {toRemove.Count} 个 CharacterPresenceFeature");
        }

        if (dirty) AssetDatabase.SaveAssets();
        Debug.Log("[Cleanup] 完成。请重启 Unity，再用菜单重新添加一次。");
    }
}
#endif
