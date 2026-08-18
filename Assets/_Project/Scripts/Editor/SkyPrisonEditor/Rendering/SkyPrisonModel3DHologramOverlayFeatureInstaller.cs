using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 把 SkyPrisonModel3DHologramOverlayFeature 挂到 UniversalRenderer3D 上，编辑器加载时
/// 自动执行一次。不装这个 Feature，3D 通道场景物材质里的 "HologramOverlay3D" Pass 永远
/// 不会被调度——它跟 "NormalBody" 共用 LightMode="UniversalForward"，URP 标准前向渲染
/// 每个物体每个 LightMode 只挑一条 Pass 画，材质、判定逻辑再对也没用。表现是"箱子被
/// 挡住时完全不显示全息"，没有任何报错，跟"判定算错了"长得一模一样——参照
/// SkyPrisonOccluderFootprintFeatureInstaller 同样的静默失败教训，不做成菜单。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonModel3DHologramOverlayFeatureInstaller
{
    private const string LogPrefix = "[SkyPrison 3D全息覆盖]";
    private const string RendererPath = "Assets/_Project/UniversalRenderer3D.asset";
    private const string PrefsKey = "SkyPrison.Model3DHologramOverlayFeature.Version";
    private const int CurrentVersion = 1;

    static SkyPrisonModel3DHologramOverlayFeatureInstaller()
    {
        if (EditorPrefs.GetInt(PrefsKey, 0) >= CurrentVersion)
            return;

        EditorApplication.delayCall += () =>
        {
            EditorPrefs.SetInt(PrefsKey, CurrentVersion);
            Install();
        };
    }

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/安装 3D 全息覆盖 Feature")]
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
            if (existing is SkyPrisonModel3DHologramOverlayFeature)
            {
                Debug.Log($"{LogPrefix} Feature 已存在，跳过。");
                return;
            }
        }

        var feature = ScriptableObject.CreateInstance<SkyPrisonModel3DHologramOverlayFeature>();
        feature.name = "SkyPrison Model3D Hologram Overlay";

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

        Debug.Log($"{LogPrefix} 已挂载到 {RendererPath}。进 Play 把 3D 通道场景物挡住确认全息是否出现。");
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
