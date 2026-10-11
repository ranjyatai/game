using UnityEngine;

/// <summary>
/// 大气高度雾(Boxophobic Atmospheric Height Fog)的全局参数安全默认值。
/// 角色着色器(Spine-Skeleton / SpineOcclusionComposite)会无条件采样雾：公式里有
/// (距离 - AHF_FogDistanceStart) / (AHF_FogDistanceEnd - AHF_FogDistanceStart)。
/// 如果场景里没有 HeightFogGlobal 组件去设置这些全局值（它们默认全是 0），分母为 0 → NaN →
/// 角色整个渲染成纯黑剪影。在编辑器里平时看不到：因为同一次编辑器会话里先加载过带雾的地图，
/// 全局值会残留；重新打开 Unity 直接进没有雾的地图就黑了。
/// 这里在场景加载前，如果发现雾全局值没人设置（终点距离 <= 0），就写入一组"无雾"的安全值。
/// 场景里真有 HeightFogGlobal 时，它自己会覆盖这些值，不受影响。
/// </summary>
public static class SkyPrisonFogGlobalsSafeDefaults
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        // 已经有人设置过（比如残留值或雾组件已启用）就不动
        if (Shader.GetGlobalFloat("AHF_FogDistanceEnd") > 0.0001f) return;

        Shader.SetGlobalVector("AHF_FogColorStart", new Vector4(0.5f, 0.75f, 1f, 1f));
        Shader.SetGlobalVector("AHF_FogColorEnd", new Vector4(0.75f, 1f, 1.25f, 1f));
        Shader.SetGlobalFloat("AHF_FogColorDuo", 0f);
        Shader.SetGlobalFloat("AHF_FogDistanceStart", 0f);
        Shader.SetGlobalFloat("AHF_FogDistanceEnd", 100f);
        Shader.SetGlobalFloat("AHF_FogDistanceFalloff", 1f);
        Shader.SetGlobalFloat("AHF_FogHeightStart", 0f);
        Shader.SetGlobalFloat("AHF_FogHeightEnd", 100f);
        Shader.SetGlobalFloat("AHF_FogHeightFalloff", 1f);
        Shader.SetGlobalFloat("AHF_FogIntensity", 0f);               // 强度 0 = 无雾
        Shader.SetGlobalVector("AHF_FogAxisOption", new Vector4(0f, 1f, 0f, 0f));
        Shader.SetGlobalVector("AHF_DirectionalColor", new Vector4(1f, 0.75f, 0.5f, 1f));
        Shader.SetGlobalVector("AHF_DirectionalDir", new Vector4(0.321f, 0.766f, -0.557f, 0f));
        Shader.SetGlobalFloat("AHF_DirectionalIntensity", 0f);
        Shader.SetGlobalFloat("AHF_DirectionalFalloff", 1f);
        Shader.SetGlobalFloat("AHF_FogLayersMode", 0f);
        Shader.SetGlobalFloat("AHF_NoiseMode", 0f);
        Shader.SetGlobalFloat("AHF_NoiseScale", 30f);
        Shader.SetGlobalFloat("AHF_NoiseIntensity", 0f);
        Shader.SetGlobalFloat("AHF_NoiseDistanceEnd", 200f);
        Shader.SetGlobalVector("AHF_NoiseSpeed", new Vector4(0.5f, 0f, 0.5f, 0f));
        Shader.SetGlobalFloat("AHF_NoiseMin", 0f);
        Shader.SetGlobalFloat("AHF_NoiseMax", 1f);
        Shader.SetGlobalFloat("AHF_SkyboxFogHeight", 1f);
        Shader.SetGlobalFloat("AHF_SkyboxFogOffset", 0f);
        Shader.SetGlobalFloat("AHF_SkyboxFogFalloff", 1f);
        Shader.SetGlobalFloat("AHF_SkyboxFogFill", 0f);
        Shader.SetGlobalFloat("AHF_SkyboxFogIntensity", 0f);
    }
}
