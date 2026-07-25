using System;
using UnityEngine;

/// <summary>
/// 追踪玩家最近实际使用的输入设备（键鼠 vs 手柄），在设备切换时发出事件。
/// 不只检测手柄是否接入，而是检测哪个设备最近有实际输入，更贴近玩家当前操作习惯。
/// 单例 MonoBehaviour，挂在 DontDestroyOnLoad GO 上。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonInputDeviceTracker : MonoBehaviour
{
    public enum DeviceFamily { KeyboardMouse, Gamepad }

    // ── 单例 ─────────────────────────────────────────────────────────────────
    private static SkyPrisonInputDeviceTracker s_instance;
    public  static SkyPrisonInputDeviceTracker Instance => s_instance;

    // 之前只由 SkyPrisonRuntimeUIDriver.Awake() 里的 EnsureInputDeviceTracker() 创建，
    // 而那个 Driver 只挂在玩法 HUD 场景里——从主菜单/暂停/设置这些不经过玩法场景的
    // 入口打开提示条时，这个 tracker 从来没被实例化过，Current 永远停在默认值
    // KeyboardMouse，Update() 也从没跑起来过，手柄插不插、按不按都不会变。这不是
    // "检测手柄失败"，是"检测这个人根本没在跑"。改成应用启动时无条件自举一份，
    // 不再依赖某个特定场景的 Driver 来创建它。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;
        var go = new GameObject("[InputDeviceTracker]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<SkyPrisonInputDeviceTracker>();
    }

    /// <summary>设备家族切换时触发（旧→新）。</summary>
    public static event Action<DeviceFamily, DeviceFamily> OnDeviceFamilyChanged;

    /// <summary>当前生效的设备家族。</summary>
    public static DeviceFamily Current { get; private set; } = DeviceFamily.KeyboardMouse;

    // ── 轮询轴（检测摇杆/DPad 是否有输入）────────────────────────────────────
    private static readonly string[] GamepadAxes =
    {
        "Horizontal", "Vertical",
        "DPadHorizontal", "DPadVertical", // 这个项目的 D-pad 实际走这两个虚拟轴，不是按钮
        "3rd Axis", "4th Axis", "5th Axis", "6th Axis",
    };

    private const float AxisThreshold  = 0.25f;
    private const float PollInterval   = 0.1f;   // 每 0.1s 轮询一次，不必每帧
    private float _nextPoll;

    // ── 生命周期 ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
        s_instance = this;
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollInterval;

        DeviceFamily detected = DetectActiveFamily();
        if (detected == Current) return;

        var prev = Current;
        Current = detected;
        OnDeviceFamilyChanged?.Invoke(prev, detected);
    }

    // ── 检测逻辑 ──────────────────────────────────────────────────────────────
    private static DeviceFamily DetectActiveFamily()
    {
        // 有任意键盘/鼠标输入 → 键鼠优先（防止接了手柄但在用键盘时乱切）。
        // 注意：不能用 Input.anyKey——它在 Unity 里手柄按键按下时也会变 true（不是只有
        // 键盘鼠标），之前这行导致只要按了任意手柄按键就被误判成"正在用键鼠"，手柄
        // 提示永远切不过去，只有纯摇杆/D-pad 轴输入侥幸绕过了这个判断。
        if (AnyKeyboardMouseInput()) return DeviceFamily.KeyboardMouse;

        // 手柄按钮/轴信号只有在系统里真的报了至少一个非空手柄设备名时才采信——
        // "Horizontal"/"Vertical" 这两个轴在 Input Manager 里同时挂了键盘和摇杆两条
        // 绑定（同名轴取值会合并），且 Windows 上常有幽灵/虚拟手柄设备（Steam 手柄
        // 输入、某些手柄驱动的常驻虚拟设备）即使没有真实手柄插着也会被系统列出、
        // 偶尔冒出噪声抖动——一旦被单帧噪声误判成 Gamepad，DetectActiveFamily 末尾
        // "无明确输入维持当前状态"这条又会让它一直锁死在 Gamepad，直到玩家真的碰
        // 键盘/鼠标才能切回来，表现就是"根本没插手柄，提示却一直是手柄按键"。
        if (!HasAnyRealJoystick()) return DeviceFamily.KeyboardMouse;

        // 检测任意手柄按钮（JoystickButton0~19）——(int)KeyCode.JoystickButton0 是 330，
        // 不是 350（350 是 Joystick1Button0，per-device 专属那个范围，这个项目实际收到
        // 的按键事件走的是不分设备的通用 JoystickButton0~19）。之前这里检测的整个范围
        // 都是错的，手柄按键从来没被识别成"正在用手柄"过。
        for (int i = 0; i < 20; i++)
            if (Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0 + i))) return DeviceFamily.Gamepad;

        // 检测摇杆/DPad 轴
        foreach (string axis in GamepadAxes)
        {
            try
            {
                if (Mathf.Abs(Input.GetAxisRaw(axis)) > AxisThreshold)
                    return DeviceFamily.Gamepad;
            }
            catch { /* 轴不存在时忽略 */ }
        }

        // 无明确输入 → 维持当前状态
        return Current;
    }

    private static bool HasAnyRealJoystick()
    {
        string[] names = Input.GetJoystickNames();
        if (names == null) return false;
        for (int i = 0; i < names.Length; i++)
            if (!string.IsNullOrWhiteSpace(names[i])) return true;
        return false;
    }

    // 只查真正的键盘/鼠标范围（KeyCode.Backspace ~ Mouse6），不碰 JoystickButton 那段——
    // Input.anyKey 会把手柄按键也算进去，不能用。
    private static bool AnyKeyboardMouseInput()
    {
        if (Input.GetAxis("Mouse X") != 0f || Input.GetAxis("Mouse Y") != 0f) return true;
        for (int i = (int)KeyCode.Backspace; i < (int)KeyCode.JoystickButton0; i++)
            if (Input.GetKey((KeyCode)i)) return true;
        return false;
    }

    /// <summary>手动触发一次设备重检（如手柄拔插后立即刷新）。</summary>
    public static void ForceRefresh()
    {
        if (s_instance == null) return;
        s_instance._nextPoll = 0f;
    }
}
