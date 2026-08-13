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
public static class SkyPrisonSceneDepthOcclusionDefault
{
    private const string LogPrefix = "[SkyPrison 深度遮挡]";
    private const string PropertyName = "_SkyPrison_UseSceneDepthOcclusion";

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/把材质切到场景深度判定")]
    public static void Run()
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

            if (mat.GetFloat(PropertyName) > 0.5f)
            {
                alreadyOn.Add(System.IO.Path.GetFileName(path));
                continue;
            }

            Undo.RecordObject(mat, "Use Scene Depth Occlusion");
            mat.SetFloat(PropertyName, 1f);
            EditorUtility.SetDirty(mat);
            changed.Add(System.IO.Path.GetFileName(path));
        }

        AssetDatabase.SaveAssets();

        if (changed.Count == 0 && alreadyOn.Count == 0)
        {
            Debug.LogWarning(
                $"{LogPrefix} 没有找到任何声明了 {PropertyName} 的材质。" +
                "着色器可能还没重新编译，切回 Unity 等编译完再跑一次。");
            return;
        }

        Debug.Log(
            $"{LogPrefix} 已切到场景深度判定。\n" +
            $"  本次改动（{changed.Count}）：{string.Join("、", changed)}\n" +
            $"  之前已开启（{alreadyOn.Count}）：{string.Join("、", alreadyOn)}\n" +
            "进 Play 走到遮挡物后面确认效果。F9 仍可临时切回 CPU 三角面做对比。");
    }
}
