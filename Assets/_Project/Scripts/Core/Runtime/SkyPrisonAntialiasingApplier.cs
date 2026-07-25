using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 设置里"抗锯齿"选项实际生效的地方。URP 的抗锯齿是挂在 Camera 组件上的
/// （UniversalAdditionalCameraData.antialiasing），不是全局 Volume 能覆盖的——
/// 每个场景都有自己独立的 Main Camera，所以每次场景加载完都要重新应用一次，
/// 不然只有当前这个场景生效，切图之后新相机又是默认设置。
/// 自举：SceneManager.sceneLoaded 一直订阅着，不需要挂在任何场景/预制体上。
/// </summary>
public static class SkyPrisonAntialiasingApplier
{
    public static int CurrentMode { get; private set; } = 1; // 0=关 1=FXAA 2=TAA

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToMainCamera();

    /// <summary>设置界面切换选项时调用，立即生效；同时记住这个值供下次场景加载后重新应用。</summary>
    public static void Apply(int antialiasingMode)
    {
        CurrentMode = antialiasingMode;
        ApplyToMainCamera();
    }

    private static void ApplyToMainCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;

        data.antialiasing = CurrentMode switch
        {
            1 => AntialiasingMode.FastApproximateAntialiasing,
            2 => AntialiasingMode.TemporalAntiAliasing,
            _ => AntialiasingMode.None,
        };
    }
}
