using UnityEditor;
using UnityEngine;

/// <summary>
/// 把遮挡合成材质里的 _SkyPrison_SceneDepthFootScale 同步为 0，编辑器加载时自动执行一次。
///
/// 为什么必须是 0：深度路径传进着色器的是像素真实的 worldPos，高度已经在里面，
/// 视矩阵会自动得出「头比脚离俯视相机更近」。再减一次 relativeY 是同一个修正做两遍，
/// 会把整个上半身的 charEye 压到场景前面，diff 恒负 → 任何遮挡物都挡不住任何东西。
///
/// 为什么要自动跑：改着色器 Properties 的默认值救不了已经存在的材质——材质资产里存的是
/// 序列化后的旧值 0.7。同一类「改默认值以为就生效了」的坑今天踩了三次
/// （头顶图标 iconSize、深度开关本身、这个 FootScale）。更糟的是 SkyPrisonOcclusionMode
/// 的注释曾声称本脚本已存在并会自动同步，实际上从来没有被写出来，于是遮挡一直没修好
/// 却以为已经修好了——所以这里不做成菜单，避免再次依赖「记得去点一下」。
///
/// 版本号守卫保证只跑一次，不会每次 Domain Reload 都扫全部材质。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonSceneDepthFootScaleSync
{
    private const string LogPrefix = "[SkyPrison 脚部补偿同步]";
    private const string PropertyName = "_SkyPrison_SceneDepthFootScale";
    private const string PrefsKey = "SkyPrison.SceneDepthFootScaleSync.Version";
    private const int CurrentVersion = 1;
    private const float TargetValue = 0f;

    static SkyPrisonSceneDepthFootScaleSync()
    {
        if (EditorPrefs.GetInt(PrefsKey, 0) >= CurrentVersion)
            return;

        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        EditorPrefs.SetInt(PrefsKey, CurrentVersion);
        Run();
    }

    public static void Run()
    {
        int changed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null || mat.shader == null || !mat.HasProperty(PropertyName))
                continue;

            if (Mathf.Approximately(mat.GetFloat(PropertyName), TargetValue))
                continue;

            mat.SetFloat(PropertyName, TargetValue);
            EditorUtility.SetDirty(mat);
            changed++;
        }

        if (changed > 0)
            AssetDatabase.SaveAssets();

        Debug.Log($"{LogPrefix} 已把 {changed} 个材质的 {PropertyName} 同步为 {TargetValue}。" +
                  (changed > 0 ? "进 Play 走到遮挡物后面确认身体被挡住。" : "本来就已经是 0，无需改动。"));
    }
}
