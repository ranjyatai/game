#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 通过菜单把 CharacterPresenceFeature 挂到所有 UniversalRendererData。
/// 已存在则跳过，不会重复添加。
/// </summary>
static class CharacterPresenceFeatureAutoSetup
{
    [MenuItem("SkyPrison/Rendering/确保 CharacterPresenceFeature 已注册")]
    public static void EnsureFeatureOnAllRenderers()
    {
        string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        bool dirty = false;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) continue;

            bool found = false;
            foreach (var f in data.rendererFeatures)
            {
                if (f is CharacterPresenceFeature) { found = true; break; }
            }
            if (found) continue;

            var feature = ScriptableObject.CreateInstance<CharacterPresenceFeature>();
            feature.name = "CharacterPresenceFeature";

            // 作为子资产保存到 renderer data 文件里
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            EditorUtility.SetDirty(data);

            dirty = true;
            Debug.Log($"[CharacterPresenceFeature] 已自动添加到：{path}");
        }

        if (dirty) AssetDatabase.SaveAssets();
    }
}
#endif
