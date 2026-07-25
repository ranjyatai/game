using UnityEngine;

/// <summary>
/// 鼠标/手柄右摇杆控制角色朝向（2.5D）。
/// 挂在玩家根对象上；调用 UnitMovementController.SetFacingOverride 注入朝向覆盖，
/// SpineAnimationDriver_Current.UpdateFacing 读取后自动翻转 Spine Scale。
///
/// 鼠标模式：以角色屏幕 X 为基准，鼠标偏左 = 面左，偏右 = 面右。
///           光标显示（Confined），不锁定到屏幕中心。
/// 手柄模式：读取右摇杆 X 轴决定朝向。光标隐藏（Locked）。
/// 两种模式根据最近有效输入自动切换。
///
/// 窗口光标协议：
///   外部窗口开启时调 PushWindowCursor()，关闭时调 PopWindowCursor()，
///   使此组件暂停光标接管。SkyPrisonWindowManager / PlayerDeathReviveUI 已接入。
/// </summary>
[DisallowMultipleComponent]
public class PlayerAimFacingController : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static PlayerAimFacingController Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("鼠标瞄准")]
    [Tooltip("鼠标与角色屏幕 X 的差值（像素）小于此值时不翻转，防止鼠标正对角色时抖动。")]
    [SerializeField] private float mouseDeadZonePixels = 20f;

    [Header("手柄右摇杆")]
    [Tooltip("Unity Input Manager 中右摇杆水平轴名称。Xbox 360 默认为第 4 轴；若不存在会静默跳过。")]
    [SerializeField] private string gamepadAimAxisX = "Joystick Axis 4";
    [SerializeField] [Range(0f, 0.95f)] private float gamepadDeadZone = 0.25f;

    [Header("输入切换")]
    [Tooltip("两种输入各自最后活跃后，多少秒内仍认为该模式有效。")]
    [SerializeField] private float inputModeLinger = 0.5f;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private UnitMovementController _movement;
    private Camera _cam;

    private float _lastMouseTime   = 0f;
    private float _lastGamepadTime = -999f;
    private bool  _isMouseMode     = true;

    private int _windowCursorDepth = 0;

    // ── 生命周期 ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        _movement = GetComponent<UnitMovementController>();
        if (_movement == null) _movement = GetComponentInParent<UnitMovementController>();

        _cam = Camera.main;

        if (_movement == null)
            Debug.LogWarning("[PlayerAimFacingController] 未找到 UnitMovementController，朝向覆盖将不生效。请确认组件挂在角色根对象上。", this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _movement?.ClearFacingOverride();
    }

    private void Update()
    {
        if (_windowCursorDepth <= 0)
            ApplyGameplayCursor();
    }

    // ── 光标管理 ──────────────────────────────────────────────────────────────

    private void ApplyGameplayCursor()
    {
        // 光标是否显示交给 SkyPrisonCustomCursor 全权管理（系统硬件光标只卡热点、
        // 图案会露出/被裁切屏幕边缘，才改成整张图标跟着鼠标走的自绘方案）——这里
        // 只管"是不是要把鼠标位置卡在窗口范围内"，不再插手显示/隐藏，不然两边
        // 每帧抢着设 Cursor.visible，谁的脚本执行顺序在后面就把另一边覆盖掉。
        Cursor.lockState = CursorLockMode.Confined;
    }

    /// <summary>外部窗口打开时调用，让出光标控制权。</summary>
    public void PushWindowCursor() => _windowCursorDepth++;

    /// <summary>外部窗口关闭时调用，归还光标控制权并立即恢复游戏光标状态。</summary>
    public void PopWindowCursor()
    {
        _windowCursorDepth = Mathf.Max(0, _windowCursorDepth - 1);
        if (_windowCursorDepth <= 0)
            ApplyGameplayCursor();
    }

    public bool IsMouseMode => _isMouseMode;
}
