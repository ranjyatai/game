using UnityEditor;
using UnityEngine;

/// <summary>
/// 一键把 Volumetric Light Beam 配置成 URP。
///
/// 这个插件的着色器是按渲染管线动态生成的（Shaders/shadergen_includes_urp.txt 等），
/// 装完不配置的话生成出来的还是内置管线版本，光柱要么不显示要么穿帮。
/// 官方流程是打开 Config 资产手动选管线，这里做成一个菜单项，顺便把
/// 「创建 Config 资产 → 设管线 → 刷新着色器 → 写 scripting define」这几步串起来，
/// 少一步都可能出现"看着配好了但着色器没重新生成"。
///
/// 这个插件原生支持正交相机（着色器里用 unity_OrthoParams.w 区分透视/正交，
/// 光线起点和方向分别推导），所以适用于本项目的 2.5D 正交视角。
/// </summary>
public static class SkyPrisonVolumetricLightBeamSetup
{
    /// <summary>VLB 自己没提供"打开配置"的菜单项，配置又是个藏在 Resources 里的
    /// 资产（Assets/Resources/VLBConfigOverride.asset），每次都要去 Project 里翻。
    /// 加一个入口，选中并 ping 出来，直接在 Inspector 里改。</summary>
    [MenuItem("天空囚笼/渲染/打开 Volumetric Light Beam 配置", false, 196)]
    public static void OpenConfig()
    {
        VLB.Config config = VLB.Config.Instance; // 不存在时会自动创建
        if (config == null)
        {
            EditorUtility.DisplayDialog("Volumetric Light Beam",
                "拿不到 VLB Config 实例。确认插件已经通过 Installer 安装完成、并且重启过 Unity。", "确定");
            return;
        }

        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
        Debug.Log($"[VLB] 配置资产：{AssetDatabase.GetAssetPath(config)}\n" +
                  $"当前渲染管线={config.renderPipeline}，光柱几何体层={config.geometryLayerID}" +
                  $"（{LayerMask.LayerToName(config.geometryLayerID)}）");
    }

    [MenuItem("天空囚笼/渲染/配置 Volumetric Light Beam 为 URP", false, 195)]
    public static void ConfigureForURP()
    {
        VLB.Config config = VLB.Config.Instance; // 不存在时会自动创建 VLBConfigOverride 资产
        if (config == null)
        {
            EditorUtility.DisplayDialog("Volumetric Light Beam",
                "拿不到 VLB Config 实例。确认插件已经通过 Installer 安装完成、并且重启过 Unity。", "确定");
            return;
        }

        VLB.RenderPipeline before = config.renderPipeline;
        config.renderPipeline = VLB.RenderPipeline.URP;
        config.RefreshShaders(VLB.Config.RefreshShaderFlags.All);
        config.SetScriptingDefineSymbolsForCurrentRenderPipeline();

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[VLB] 渲染管线：{before} → {config.renderPipeline}，已刷新着色器并写入 scripting define。" +
                  "如果 Console 出现着色器相关报错，重启一次 Unity。");

        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);

        EditorUtility.DisplayDialog("Volumetric Light Beam",
            $"已配置为 URP（原来是 {before}）。\n\n" +
            "Config 资产已经选中，可以在 Inspector 里进一步调。\n" +
            "建议重启一次 Unity，避免残留的着色器编译缓存报错。", "确定");
    }
}
