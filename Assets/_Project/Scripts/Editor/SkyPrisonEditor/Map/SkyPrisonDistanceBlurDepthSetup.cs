using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 把 2.5D 距离模糊设成「Base 相机 + 屏幕上缘渐进」。
///
/// 为什么用屏幕 Y 而不是场景深度：
///   相机俯角固定，屏幕越靠上就是越远，两者在观感上等价；而屏幕 Y 不依赖深度纹理，
///   绕开了两个真实存在的坑 ——
///     1. pass 原本跑在 GamePlayCamera（Overlay，cullingMask 只有 UI + FogOfWar）上，
///        那台相机从没画过世界几何体，SceneDepth 遮罩读到的是无效值；
///     2. 着色器里的深度换算原本用 LinearEyeDepth，那是透视公式，本项目相机是正交的。
///   两个坑都修过之后深度模式能用，但对固定俯角的 2.5D 来说是多余的复杂度。
///
/// 真正让「怎么调都不像景深」的其实是合成方式：原来是 lerp(清晰, 模糊, m) 按比例混合，
/// 且 m 被 intensity 卡在 0.449，最远处仍叠着 55% 的清晰图 —— 出来是发灰带重影，
/// 不是散焦。着色器已改成沿模糊程度串联，清晰图会完全退出。
///
/// targetMode 用 World3DOnlyBeforeOverlays：跑在 Base 相机、Overlay 画 UI 之前，
/// 所以 UI 不会跟着一起糊。
///
/// 做成菜单而不是直接改资产文本：Unity 开着时磁盘改动经常被内存里的版本覆盖，
/// 这个项目已经在这上面吃过亏。走 SerializedObject 才是可靠的写入方式。
/// </summary>
public static class SkyPrisonDistanceBlurDepthSetup
{
    private const string LogPrefix = "[SkyPrison 距离模糊]";
    private const string RendererPath = "Assets/_Project/UniversalRenderer3D.asset";

    // 屏幕纵向渐变的起止位置。相机俯角固定，屏幕越靠上就是越远，
    // 所以「上缘渐进模糊」和「按深度模糊」在观感上等价。
    private const float BlurStartY = 0.62f;   // 从这个高度开始出现模糊
    private const float BlurEndY = 1.0f;      // 到屏幕顶端达到最大模糊

    [MenuItem("天空囚笼/地图/距离模糊：上缘渐进（推荐）", false, 119)]
    public static void Run()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"{LogPrefix} 找不到 {RendererPath}");
            return;
        }

        foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
        {
            if (feature is not SkyPrisonOrthographicDistanceBlurRendererFeature blur)
                continue;

            var so = new SerializedObject(blur);
            SerializedProperty s = so.FindProperty("settings");

            s.FindPropertyRelative("enabled").boolValue = true;
            // 0 = World3DOnlyBeforeOverlays（Base 相机，在 Overlay 画 UI 之前 → UI 不会跟着糊）
            s.FindPropertyRelative("targetMode").enumValueIndex = 0;
            // 1 = ScreenY，屏幕纵向渐变
            s.FindPropertyRelative("maskMode").enumValueIndex = 1;
            s.FindPropertyRelative("screenBlurStartY").floatValue = BlurStartY;
            s.FindPropertyRelative("screenBlurEndY").floatValue = BlurEndY;
            // 让遮罩能到满值。0.449 会让最远处仍叠着 55% 的清晰图 —— 那是「发灰有重影」
            // 而不是模糊，也是之前怎么调都不像景深的主因。
            s.FindPropertyRelative("intensity").floatValue = 1f;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blur);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"{LogPrefix} 已设为上缘渐进模糊。起点 Y={BlurStartY}，顶端={BlurEndY}。\n" +
                "调法：Screen Blur Start Y 调小 → 模糊范围往下扩；Max Radius 调大 → 糊得更散。\n" +
                "注意 Focus Distance / Blur Range 在 ScreenY 模式下不参与计算，改了没反应。");
            return;
        }

        Debug.LogError($"{LogPrefix} 渲染器里没找到 SkyPrisonOrthographicDistanceBlurRendererFeature");
    }

    // 2026-08-14：相机从正交切成真透视之后加的。
    //
    // 上面那个「上缘渐进」菜单是按「相机俯角固定、屏幕越靠上就越远」的正交假设写的，
    // 切透视后这个假设不成立——镜头会跟着角色移动，同一个屏幕 Y 坐标不再对应固定的
    // 远近关系，模糊范围因此和画面对不上（用户实测反馈：糊的位置跟画面对不上）。
    //
    // SceneDepth 模式才是几何正确的做法，着色器里的深度换算本来就有正交/透视双路径
    // （按 unity_OrthoParams.w 分流，早上写落地深度遮挡时就顺手做好了），现在正好用上。
    // targetMode 继续用 World3DOnlyBeforeOverlays（Base 相机，Overlay 画 UI 之前），
    // 这台相机本来就渲染世界几何体、有正确深度，不需要再绕开哪个坑。
    // 2026-08-14 二次校准：切透视之后相机跟着玩家平移，"相机到玩家距离"不再是
    // 固定值——之前这里没写 focusDistance/blurRange，资产里一直存着切透视之前
    // 量出来的旧值 70，跟当前实测的 55.1 差了 15，导致玩家周围大片区域被卡在
    // "完全清晰"那一段，模糊看起来像是"消失了"。这两个常量不是永久精确值，
    // 只是当前实测的校准起点，后续如果相机跟随的偏移量再调，要重新实测。
    private const float FocusDistance = 50f; // 略小于实测的55.1，确保玩家自身不被糊到
    private const float BlurRange = 45f;

    [MenuItem("天空囚笼/地图/距离模糊：场景深度（透视用）", false, 120)]
    public static void RunSceneDepth()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"{LogPrefix} 找不到 {RendererPath}");
            return;
        }

        foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
        {
            if (feature is not SkyPrisonOrthographicDistanceBlurRendererFeature blur)
                continue;

            var so = new SerializedObject(blur);
            SerializedProperty s = so.FindProperty("settings");

            s.FindPropertyRelative("enabled").boolValue = true;
            s.FindPropertyRelative("targetMode").enumValueIndex = 0; // World3DOnlyBeforeOverlays
            s.FindPropertyRelative("maskMode").enumValueIndex = 0;   // SceneDepth
            s.FindPropertyRelative("intensity").floatValue = 1f;
            s.FindPropertyRelative("focusDistance").floatValue = FocusDistance;
            s.FindPropertyRelative("blurRange").floatValue = BlurRange;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blur);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"{LogPrefix} 已切到场景深度模式，并按实测距离(55.1)重新校准。" +
                $"Focus Distance={FocusDistance}，Blur Range={BlurRange}。\n" +
                "如果相机跟随玩家的偏移量之后又调整过，这两个数字要重新用[BlurGate]诊断" +
                "实测校准，不是一次写死永久有效的。\n" +
                "Screen Blur Start Y / End Y 那几项在这个模式下不参与计算，改了没反应。");
            return;
        }

        Debug.LogError($"{LogPrefix} 渲染器里没找到 SkyPrisonOrthographicDistanceBlurRendererFeature");
    }
}
