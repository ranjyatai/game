#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Editor-side installer/synchronizer for the Sky Prison 2.5D orthographic distance blur.
/// It does not alter the camera stack. It only ensures the Base renderer has the renderer feature.
/// </summary>
public static class SkyPrisonOrthographicDistanceBlurEditorUtility
{
    private const string FeatureName = "SkyPrison 2.5D Distance Blur";
    private const string ShaderName = "Hidden/SkyPrison/OrthographicDistanceBlur";

    public static void AutoFixCurrentScene(MapDefinition map)
    {
        EnsureRendererFeature(map, true, out string report);
        EditorUtility.DisplayDialog("2.5D 远景虚化", report, "确定");
    }

    public static void SyncCurrentScene(MapDefinition map)
    {
        EnsureRendererFeature(map, false, out string report);
        Debug.Log(report);
    }

    public static void InspectCurrentScene(MapDefinition map)
    {
        string report = BuildReport(map);
        EditorUtility.DisplayDialog("2.5D 远景虚化结构检查", report, "确定");
    }

    private static SkyPrisonOrthographicDistanceBlurRendererFeature EnsureRendererFeature(
        MapDefinition map,
        bool createIfMissing,
        out string report)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("2.5D 远景虚化同步结果：");
        sb.AppendLine();

        UniversalRendererData rendererData = FindWorldRendererData();
        if (rendererData == null)
        {
            report = "未找到 UniversalRendererData。请确认项目使用 URP，并且存在 UniversalRenderer3D Renderer Data。";
            return null;
        }

        sb.AppendLine($"目标 Renderer Data：{rendererData.name}");

        SkyPrisonOrthographicDistanceBlurRendererFeature feature = FindFeature(rendererData);
        if (feature == null && createIfMissing)
        {
            feature = ScriptableObject.CreateInstance<SkyPrisonOrthographicDistanceBlurRendererFeature>();
            feature.name = FeatureName;
            feature.settings.shader = Shader.Find(ShaderName);

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            sb.AppendLine("Renderer Feature：已创建");
        }
        else
        {
            sb.AppendLine($"Renderer Feature：{(feature != null ? "已存在" : "缺失")}");
        }

        if (feature != null)
        {
            Undo.RecordObject(feature, "Sync Sky Prison 2.5D Distance Blur");

            if (feature.settings.shader == null)
                feature.settings.shader = Shader.Find(ShaderName);

            bool enabled = map != null && map.enableDepthOfField && !SkyPrisonRenderQualityContext.IsSafe;
            float strength = map != null ? Mathf.Clamp01(map.blurStrength) : 0.4f;
            float focus = map != null ? Mathf.Max(0.1f, map.focusDistance) : 35f;

            // In orthographic 2.5D, the useful focus value is usually much larger than lens DOF's value.
            // If the map still has the old default 8, keep it but widen the range so a visible effect is possible.
            feature.settings.enabled = enabled;
            feature.settings.focusDistance = focus;
            feature.settings.blurRange = Mathf.Lerp(10f, 42f, strength);
            feature.settings.maxRadius = Mathf.Lerp(0f, SkyPrisonRenderQualityContext.IsFinal ? 12f : 8f, strength);
            feature.settings.intensity = strength;
            feature.settings.applyToSceneView = false;
            feature.settings.targetMode = SkyPrisonOrthographicDistanceBlurRendererFeature.BlurTargetMode.BaseCameraFinalAfterStack;
            feature.settings.afterOverlayCameraName = "GamePlayCamera";
            feature.settings.maskMode = SkyPrisonOrthographicDistanceBlurRendererFeature.BlurMaskMode.ScreenY;
            // Use an intentionally visible default for orthographic 2.5D.
            // The designer can raise the start Y later after confirming the effect is active.
            feature.settings.screenBlurStartY = 0.35f;
            feature.settings.screenBlurEndY = 1.0f;

            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);

            sb.AppendLine($"状态：{(enabled ? "已开启" : "已关闭")}");
            sb.AppendLine($"焦点距离：{feature.settings.focusDistance:0.##}");
            sb.AppendLine($"模糊范围：{feature.settings.blurRange:0.##}");
            sb.AppendLine($"最大半径：{feature.settings.maxRadius:0.##}");
            sb.AppendLine();
            sb.AppendLine("提示：当前模式会作为 Base Camera 的最终镜头效果执行，用来让 World3D 与角色 Overlay 进入同一远景虚化链。请在 Game 视图查看。 ");
        }

        report = sb.ToString();
        return feature;
    }

    private static string BuildReport(MapDefinition map)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("2.5D 远景虚化结构检查");
        sb.AppendLine();

        UniversalRendererData rendererData = FindWorldRendererData();
        if (rendererData == null)
        {
            sb.AppendLine("Renderer Data：缺失");
            sb.AppendLine("请先确认 URP Renderer Data 是否存在。 ");
            return sb.ToString();
        }

        sb.AppendLine($"Renderer Data：{rendererData.name}");
        SkyPrisonOrthographicDistanceBlurRendererFeature feature = FindFeature(rendererData);
        if (feature == null)
        {
            sb.AppendLine("Renderer Feature：缺失");
            sb.AppendLine("请点击“自动补齐2.5D远景虚化”。");
            return sb.ToString();
        }

        sb.AppendLine("Renderer Feature：存在");
        sb.AppendLine($"Feature Enabled：{feature.settings.enabled}");
        sb.AppendLine($"Shader：{(feature.settings.shader != null ? feature.settings.shader.name : "缺失")}");
        sb.AppendLine($"Focus Distance：{feature.settings.focusDistance:0.##}");
        sb.AppendLine($"Blur Range：{feature.settings.blurRange:0.##}");
        sb.AppendLine($"Max Radius：{feature.settings.maxRadius:0.##}");
        sb.AppendLine($"Intensity：{feature.settings.intensity:0.##}");
        sb.AppendLine($"目标模式：{feature.settings.targetMode}");
        sb.AppendLine($"叠加后执行相机：{feature.settings.afterOverlayCameraName}");
        sb.AppendLine($"遮罩模式：{feature.settings.maskMode}");
        sb.AppendLine($"屏幕Y起点：{feature.settings.screenBlurStartY:0.##}");
        sb.AppendLine($"屏幕Y终点：{feature.settings.screenBlurEndY:0.##}");
        sb.AppendLine();
        sb.AppendLine($"地图开启景深：{(map != null && map.enableDepthOfField ? "是" : "否")}");
        sb.AppendLine($"当前渲染档：{SkyPrisonRenderQualityContext.CurrentTier}");
        return sb.ToString();
    }

    private static SkyPrisonOrthographicDistanceBlurRendererFeature FindFeature(UniversalRendererData rendererData)
    {
        if (rendererData == null || rendererData.rendererFeatures == null)
            return null;

        foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
        {
            if (feature is SkyPrisonOrthographicDistanceBlurRendererFeature typed)
                return typed;
        }

        return null;
    }

    private static UniversalRendererData FindWorldRendererData()
    {
        List<UniversalRendererData> all = new List<UniversalRendererData>();
        string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UniversalRendererData data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data != null)
                all.Add(data);
        }

        if (all.Count == 0)
            return null;

        UniversalRendererData preferred = all.FirstOrDefault(x => x.name.Contains("UniversalRenderer3D"));
        if (preferred != null)
            return preferred;

        preferred = all.FirstOrDefault(x => x.name.Contains("3D") || x.name.Contains("World"));
        if (preferred != null)
            return preferred;

        return all[0];
    }
}
#endif
