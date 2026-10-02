using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把所有遮挡合成材质切到「场景深度」判定。
///
/// 着色器的 Properties 默认值改了，已经存在的材质不会跟着变——材质里存的是序列化后
/// 的旧值。这一点今天已经踩过一次（头顶任务图标的 iconSize 改了默认值，
/// 场景里已有的实例还是旧的 18）。
///
/// 为什么把 GPU 深度定为默认：
///   CPU 三角面   开销 = 渲染体数 × 三角面数 × 采样点 × 每帧   随地图复杂度线性增长
///   GPU 深度     开销 = 一次纹理采样                        与场景里有多少东西无关
/// 实测一台叉车单帧吃掉 15.9ms、占 21 个遮挡物总开销的 90%，纯粹是几何复杂度。
/// 而地图内容只会继续增加，CPU 那条路数学上就走不通。
///
/// 精度也不是妥协——逐像素比较深度比按采样点插值的射线求交更准。
///
/// 旧路径保留在着色器里，F9 仍可临时切回做对比，只是不再是默认。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonSceneDepthOcclusionDefault
{
    private const string LogPrefix = "[SkyPrison 深度遮挡]";
    private const string PropertyName = "_SkyPrison_UseSceneDepthOcclusion";
    private const string FootScaleName = "_SkyPrison_SceneDepthFootScale";

    // 一次性迁移标记，不是持续强制。
    //
    // 之前 SkyPrisonDebugLogFlagsOff 用 [InitializeOnLoad] 每次重载都把 debugLogs
    // 摁回 false，结果和「特意打开调试」这种合理场景打架，最后被删掉。这里不重蹈：
    // 版本号写进 EditorPrefs，只要这次修复跑过一次就不再自动执行，
    // 之后谁想把某个材质的 FootScale 调成别的值（比如特殊美术效果）不会被自动改回去。
    private const string MigrationVersionKey = "SkyPrison.SceneDepthFootScaleMigration.v1";

    static SkyPrisonSceneDepthOcclusionDefault()
    {
        if (EditorPrefs.GetBool(MigrationVersionKey, false))
            return;

        EditorPrefs.SetBool(MigrationVersionKey, true);
        EditorApplication.delayCall += RunSilently;
    }

    /// <summary>
    /// 深度路径下脚部补偿必须为 0。
    ///
    /// 着色器里这一项的默认值已经改成 0，但改默认值救不了已经存在的材质——材质资产里
    /// 存的是序列化后的旧值 0.7。同一个坑今天踩到第三次了（头顶图标 iconSize、
    /// 深度开关本身、现在是这个），所以一律走菜单同步，不靠记得去手动改 Inspector。
    ///
    /// 为什么必须是 0：深度路径传进着色器的是像素真实的 worldPos，高度已经在里面，
    /// 视矩阵会自动得出「头比脚离俯视相机近」。再减一次 relativeY 是重复扣减，
    /// 会把整个上半身判成「在场景前面」，导致全场遮挡物一起失效。
    /// </summary>
    private const float FootScaleForDepthPath = 0f;

    private static void RunSilently() => Run(logEvenIfNothingChanged: false);

    public static void Run() => Run(logEvenIfNothingChanged: true);

    private static void Run(bool logEvenIfNothingChanged)
    {
        var changed = new List<string>();
        var alreadyOn = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null)
                continue;

            // 只认真正声明了这个属性的着色器，避免给无关材质写脏数据。
            if (!mat.HasProperty(PropertyName))
                continue;

            bool needsToggle = mat.GetFloat(PropertyName) <= 0.5f;
            bool needsFootScale = mat.HasProperty(FootScaleName)
                && !Mathf.Approximately(mat.GetFloat(FootScaleName), FootScaleForDepthPath);

            if (!needsToggle && !needsFootScale)
            {
                alreadyOn.Add(System.IO.Path.GetFileName(path));
                continue;
            }

            Undo.RecordObject(mat, "Use Scene Depth Occlusion");

            if (needsToggle)
                mat.SetFloat(PropertyName, 1f);

            if (needsFootScale)
                mat.SetFloat(FootScaleName, FootScaleForDepthPath);

            EditorUtility.SetDirty(mat);
            changed.Add(System.IO.Path.GetFileName(path)
                + (needsFootScale ? "（含脚部补偿归零）" : string.Empty));
        }

        AssetDatabase.SaveAssets();

        if (changed.Count == 0 && alreadyOn.Count == 0)
        {
            if (logEvenIfNothingChanged)
            {
                Debug.LogWarning(
                    $"{LogPrefix} 没有找到任何声明了 {PropertyName} 的材质。" +
                    "着色器可能还没重新编译，切回 Unity 等编译完再跑一次。");
            }
            return;
        }

        if (changed.Count == 0 && !logEvenIfNothingChanged)
            return;

        Debug.Log(
            $"{LogPrefix} 已切到场景深度判定。\n" +
            $"  本次改动（{changed.Count}）：{string.Join("、", changed)}\n" +
            $"  之前已开启（{alreadyOn.Count}）：{string.Join("、", alreadyOn)}\n" +
            "进 Play 走到遮挡物后面确认效果。F9 仍可临时切回 CPU 三角面做对比。");
    }
}
