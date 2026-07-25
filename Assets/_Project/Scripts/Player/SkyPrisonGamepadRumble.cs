using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 手柄震动的全局唯一入口。自举单例，DontDestroyOnLoad，不需要挂在任何场景/预制体上。
/// Strength 由设置界面「震动强度」滑块驱动（0 = 完全关闭）。
/// </summary>
public sealed class SkyPrisonGamepadRumble : MonoBehaviour
{
    /// <summary>0~1，来自 SettingsData.vibrationStrength，SaveManager 启动时同步。</summary>
    public static float Strength = 1f;

    private static SkyPrisonGamepadRumble _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[GamepadRumble]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SkyPrisonGamepadRumble>();
    }

    private Coroutine _routine;

    /// <summary>角色受击时的短促震动脉冲。</summary>
    public static void PulseOnHit()
    {
        if (_instance == null) return;
        _instance.Pulse(lowFreq: 0.25f, highFreq: 0.55f, duration: 0.18f);
    }

    private void Pulse(float lowFreq, float highFreq, float duration)
    {
        if (Strength <= 0f) return;
        var pad = Gamepad.current;
        if (pad == null) return;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(PulseRoutine(pad, lowFreq * Strength, highFreq * Strength, duration));
    }

    private IEnumerator PulseRoutine(Gamepad pad, float low, float high, float duration)
    {
        pad.SetMotorSpeeds(low, high);
        yield return new WaitForSecondsRealtime(duration);
        pad.SetMotorSpeeds(0f, 0f);
        _routine = null;
    }

    private void OnDestroy()      => Gamepad.current?.SetMotorSpeeds(0f, 0f);
    private void OnApplicationQuit() => Gamepad.current?.SetMotorSpeeds(0f, 0f);
}
