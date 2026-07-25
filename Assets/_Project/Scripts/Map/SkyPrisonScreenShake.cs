using UnityEngine;

/// <summary>
/// 屏幕震动的全局唯一入口。自举单例，DontDestroyOnLoad。
/// 不直接摆动 Main Camera 的 transform——CameraFollowProxy 每帧 LateUpdate 会
/// 覆写 transform.position，直接改相机会被它立刻冲掉。改成暴露一个衰减中的
/// CurrentOffset，由 CameraFollowProxy 自己在算完基础位置后加上去。
/// Enabled 由设置界面「屏幕震动」开关驱动，关掉时 Shake() 直接忽略。
/// 用法：任何地方调用 SkyPrisonScreenShake.Shake(intensity, duration) 即可，
/// 不需要关心当前是否已经在震——新的一次会覆盖旧的衰减进度。
/// </summary>
public sealed class SkyPrisonScreenShake : MonoBehaviour
{
    public static bool Enabled = true;

    /// <summary>当前这一帧应叠加到相机位置上的偏移量，CameraFollowProxy 读这个。</summary>
    public static Vector3 CurrentOffset { get; private set; } = Vector3.zero;

    private static SkyPrisonScreenShake _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[ScreenShake]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SkyPrisonScreenShake>();
    }

    private float _duration;
    private float _elapsed;
    private float _intensity;

    /// <summary>触发一次震动。intensity 是最大偏移量（世界单位），duration 是衰减时长（秒）。</summary>
    public static void Shake(float intensity, float duration)
    {
        if (_instance == null || !Enabled || intensity <= 0f || duration <= 0f) return;
        _instance._intensity = intensity;
        _instance._duration  = duration;
        _instance._elapsed   = 0f;
    }

    private void LateUpdate()
    {
        if (_elapsed >= _duration)
        {
            CurrentOffset = Vector3.zero;
            return;
        }

        _elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        float falloff = 1f - t; // 线性衰减，越到后面抖动幅度越小

        float x = (Mathf.PerlinNoise(Time.unscaledTime * 25f, 0f) - 0.5f) * 2f;
        float z = (Mathf.PerlinNoise(0f, Time.unscaledTime * 25f) - 0.5f) * 2f;
        CurrentOffset = new Vector3(x, 0f, z) * _intensity * falloff;
    }
}
