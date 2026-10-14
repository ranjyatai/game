using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 把 SkyPrisonOccluderFootprintDepthFeature 挂到 UniversalRenderer3D 上，编辑器加载时自动执行一次。
///
/// 为什么自动做：这个 Feature 不挂上去，落地深度图就永远是空的，而空图的表现是
/// 「所有遮挡物都不遮挡」——没有任何报错，和「代码写错了」长得一模一样。今天已经
/// 因为类似的静默失败反复误判过好几次（材质没绑、FootScale 没同步、注释里承诺的
/// 同步脚本根本不存在），所以不做成菜单，避免再依赖「记得去点一下」。
///
/// 手写 YAML 挂 RendererFeature 需要同时维护 m_RendererFeatures 列表和
/// m_RendererFeatureMap，写错不会报错只会静默失效，所以走 AssetDatabase。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonOccluderFootprintFeatureInstaller
{
    private const string LogPrefix = "[SkyPrison 落地深度]";
    private const string RendererPath = "Assets/_Project/UniversalRenderer3D.asset";
    private const string ShaderPath =
        "Assets/_Project/Art/Shaders/Custom/FrontOccluder/OcclusionMask/SkyPrisonOccluderFootprintDepth.shader";
    private const string PrefsKey = "SkyPrison.OccluderFootprintFeature.Version";
    private const int CurrentVersion = 1;

    static SkyPrisonOccluderFootprintFeatureInstaller()
    {
        if (EditorPrefs.GetInt(PrefsKey, 0) >= CurrentVersion)
            return;

        EditorApplication.delayCall += () =>
        {
            EditorPrefs.SetInt(PrefsKey, CurrentVersion);
            Install();
        };
    }

    public static void Install()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null)
        {
            Debug.LogError($"{LogPrefix} 找不到 Renderer 资产：{RendererPath}");
            return;
        }

        foreach (var existing in renderer.rendererFeatures)
        {
            if (existing is SkyPrisonOccluderFootprintDepthFeature)
            {
                EnsureShaderAssigned((SkyPrisonOccluderFootprintDepthFeature)existing);
                Debug.Log($"{LogPrefix} Feature 已存在，仅校验着色器引用。");
                return;
            }
        }

        var feature = ScriptableObject.CreateInstance<SkyPrisonOccluderFootprintDepthFeature>();
        feature.name = "SkyPrison Occluder Footprint Depth";
        EnsureShaderAssigned(feature);

        AssetDatabase.AddObjectToAsset(feature, renderer);

        // rendererFeatures 是只读属性，且还有一个并行的 m_RendererFeatureMap 必须同步，
        // 否则 Unity 重新导入时会把这个 Feature 丢掉。用 SerializedObject 一起写。
        var so = new SerializedObject(renderer);
        SerializedProperty features = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");

        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = GetLocalId(feature);

        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"{LogPrefix} 已挂载到 {RendererPath}。进 Play 走到叉车后面确认：" +
                  "角色应整体被挡或整体不被挡，露出轮廓外的部分正常显示。");
    }

    private static void EnsureShaderAssigned(SkyPrisonOccluderFootprintDepthFeature feature)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} 找不到着色器：{ShaderPath}");
            return;
        }

        feature.settings.footprintShader = shader;
        feature.settings.enabled = true;
        EditorUtility.SetDirty(feature);
    }

    /// <summary>取子资产在文件里的 localFileID，m_RendererFeatureMap 存的就是这个。</summary>
    private static long GetLocalId(Object obj)
    {
        var serialized = new SerializedObject(obj);
        PropertyInfo info = typeof(SerializedObject).GetProperty(
            "inspectorMode", BindingFlags.NonPublic | BindingFlags.Instance);
        info.SetValue(serialized, InspectorMode.Debug, null);

        SerializedProperty idProp = serialized.FindProperty("m_LocalIdentfierInFile");
        return idProp.longValue;
    }
}
