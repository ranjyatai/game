using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 设置里"亮度/动态模糊/色差"这几个全局画面开关实际作用到游戏画面的地方——建一个
/// 常驻的全局 Volume，统一挂这几个 URP 内置后处理覆盖。之前动态模糊/色差这两个
/// 开关在设置窗口里只存了个布尔值，没有任何后处理效果消费它们，勾了跟没勾一个样；
/// 屏幕震动因为需要真正的相机摆动系统+接入伤害/开枪等事件源头，工作量不是"加个
/// Volume 覆盖"能解决的，留到后面单独排期，不在这里做。
/// 亮度用 Color Adjustments 的 Post Exposure 换算；跟亮度校准弹窗里那两张参考图的
/// 伽马预览是两回事，互不影响：那边是"帮你找数值"，这里是"把数值用出去"。
/// </summary>
public static class SkyPrisonBrightnessManager
{
    private static Volume _volume;
    private static ColorAdjustments _colorAdjustments;
    private static MotionBlur _motionBlur;
    private static ChromaticAberration _chromaticAberration;

    public static void Apply(float brightness)
    {
        EnsureVolume();
        // brightness 滑块范围 0.4~2.5，1.0 是不调整；换算成 Post Exposure（EV，
        // 0 是不调整），用 log2 让数值感受上跟伽马预览的"倍数"直觉一致。
        _colorAdjustments.postExposure.value = Mathf.Log(Mathf.Max(brightness, 0.01f), 2f);
    }

    public static void ApplyMotionBlur(bool enabled)
    {
        EnsureVolume();
        _motionBlur.active = enabled;
        _motionBlur.intensity.value = enabled ? 0.5f : 0f;
    }

    public static void ApplyChromaticAberration(bool enabled)
    {
        EnsureVolume();
        _chromaticAberration.active = enabled;
        _chromaticAberration.intensity.value = enabled ? 0.3f : 0f;
    }

    private static void EnsureVolume()
    {
        if (_volume != null) return;

        var go = new GameObject("[SkyPrisonBrightnessVolume]");
        Object.DontDestroyOnLoad(go);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.weight = 1f;
        _volume.priority = 100f; // 压过地图自己的 Volume，设置永远说了算

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _colorAdjustments = profile.Add<ColorAdjustments>(true);
        _colorAdjustments.postExposure.overrideState = true;

        _motionBlur = profile.Add<MotionBlur>(true);
        _motionBlur.intensity.overrideState = true;
        _motionBlur.active = false;

        _chromaticAberration = profile.Add<ChromaticAberration>(true);
        _chromaticAberration.intensity.overrideState = true;
        _chromaticAberration.active = false;

        _volume.profile = profile;
    }
}
