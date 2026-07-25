using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 菜单：SkyPrison → Setup Blood VFX
/// 自动创建 BloodVFXSettings 并填入 URP 飞溅预制体。
/// </summary>
public static class BloodVFXSetupTool
{
    private const string SettingsOutputPath = "Assets/Resources/BloodVFXSettings.asset";
    private const string SplashFolder       = "Assets/RVFX/BloodEffectsPack/1_URP/Blood/Splash";
    private const string DecalFolder        = "Assets/RVFX/BloodEffectsPack/1_URP/Blood/Decal_Projector";

    [MenuItem("SkyPrison/Setup Blood VFX")]
    public static void Run()
    {
        // 确保 Resources 文件夹存在
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.Refresh();
        }

        // 找或创建 Settings 资产
        BloodVFXSettings settings = AssetDatabase.LoadAssetAtPath<BloodVFXSettings>(SettingsOutputPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<BloodVFXSettings>();
            AssetDatabase.CreateAsset(settings, SettingsOutputPath);
        }

        // 找 URP 飞溅预制体（排除 Continuous / WithGut 变体，只取基础款）
        string[] guids = AssetDatabase.FindAssets("t:Prefab Blood_Splash", new[] { SplashFolder });
        var prefabs = new System.Collections.Generic.List<GameObject>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);

            // 只要基础款（不要 Continuous / WithGut）
            if (name.Contains("Continuous") || name.Contains("WithGut")) continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                prefabs.Add(prefab);
        }

        settings.splashPrefabs = prefabs.ToArray();

        // 填入 Static Decal Projector（地面血迹，随机旋转）
        string[] decalGuids = AssetDatabase.FindAssets("t:Prefab BloodDecal", new[] { DecalFolder });
        var decals = new System.Collections.Generic.List<GameObject>();
        foreach (string guid in decalGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            // 只要 Static_Projected（最轻量、无动画，适合受击贴花）
            if (!name.Contains("Static_Projected")) continue;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) decals.Add(prefab);
        }
        settings.decalPrefabs = decals.ToArray();

        // 填入 Static_Projector_URP 材质（运行时直接创建 DecalProjector 用）
        string[] matGuids = AssetDatabase.FindAssets("t:Material BloodDecal", new[] { DecalFolder });
        var mats = new System.Collections.Generic.List<Material>();
        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.Contains("Static_Projector")) continue;
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) mats.Add(mat);
        }
        settings.decalMaterials = mats.ToArray();

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BloodVFXSetupTool] 完成：Splash={prefabs.Count} Decal={decals.Count}，已写入 {SettingsOutputPath}");
        EditorUtility.DisplayDialog("Blood VFX Setup",
            $"完成！\nSplash: {prefabs.Count} 个\nDecal: {decals.Count} 个\n已保存到 Resources/BloodVFXSettings.asset", "OK");
    }
}
