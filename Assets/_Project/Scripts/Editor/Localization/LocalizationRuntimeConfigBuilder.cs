#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class LocalizationRuntimeConfigBuilder
{
    private const string ResourcesFolder = "Assets/_Project/Resources/SkyPrison";
    private const string AssetPath       = ResourcesFolder + "/LocalizationRuntimeConfig.asset";

    [MenuItem("Sky Prison/生成本地化运行时配置")]
    public static void Build()
    {
        // 确保 Resources/SkyPrison 文件夹存在
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources"))
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets/_Project/Resources", "SkyPrison");

        // 加载或创建 config asset
        var config = AssetDatabase.LoadAssetAtPath<LocalizationRuntimeConfig>(AssetPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<LocalizationRuntimeConfig>();
            AssetDatabase.CreateAsset(config, AssetPath);
        }

        // 自动填入 settings 引用
        config.settings = LocalizationSettingsUtility.GetOrCreateSettings();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[LocalizationRuntime] 配置已生成：" + AssetPath);
        Selection.activeObject = config;
    }
}
#endif
