using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "SkyPrisonInputSettings",
    menuName = "Sky Prison/Input Settings",
    order = 1600)]
public class SkyPrisonInputSettings : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/_Project/Data/Settings/SkyPrisonInputSettings.asset";

    [Header("Version")]
    [SerializeField] private string settingsVersion = "V6 - 2026-06-13 - quick item prompt bindings";

    [Header("Keyboard Movement")]
    public bool normalizeDigitalMove = true;

    [Header("Gamepad Axes")]
    public bool enableGamepadAxes = true;
    public string gamepadHorizontalAxis = "Horizontal";
    public string gamepadVerticalAxis = "Vertical";
    [Range(0f, 0.95f)] public float gamepadDeadZone = 0.2f;

    [Header("Sprint Double Tap Dodge")]
    [Tooltip("开启后，双击当前 Sprint 绑定键触发闪避。玩家改奔跑键后，双击闪避会自动跟随新的 Sprint 绑定。")]
    public bool enableSprintDoubleTapDodge = false;

    [Tooltip("两次按下 Sprint 之间允许的最大间隔。")]
    [Range(0.08f, 0.6f)] public float sprintDoubleTapWindow = 0.26f;

    [Tooltip("第二次按下前，必须至少松开 Sprint 这么久，避免长按奔跑被误判成双击。")]
    [Range(0f, 0.2f)] public float sprintDoubleTapMinReleaseTime = 0.03f;

    [Tooltip("闪避触发后短暂压制奔跑输入，避免同一帧既闪避又继续奔跑。")]
    [Range(0f, 0.25f)] public float runSuppressAfterDoubleTapDodge = 0.08f;

    [Tooltip("没有方向输入时，双击 Sprint 是否向前闪避。关闭则默认后撤。")]
    public bool noMoveInputDodgeForward = false;

    [Tooltip("是否保留独立 Dodge 绑定键。默认 LeftAlt 作为直接闪避兜底。")]
    public bool directDodgeKeyStillAllowed = true;

    [Header("Bindings")]
    public List<SkyPrisonInputBinding> bindings = new List<SkyPrisonInputBinding>();

    private Dictionary<SkyPrisonInputAction, SkyPrisonInputBinding> lookup;

    public string SettingsVersion => settingsVersion;

    public void EnsureDefaults()
    {
        settingsVersion = "V6 - 2026-06-13 - quick item prompt bindings";

        if (bindings == null)
            bindings = new List<SkyPrisonInputBinding>();

        EnsureBinding(SkyPrisonInputAction.MoveUp, "上移动", KeyCode.W, KeyCode.UpArrow);
        EnsureBinding(SkyPrisonInputAction.MoveDown, "下移动", KeyCode.S, KeyCode.DownArrow);
        EnsureBinding(SkyPrisonInputAction.MoveLeft, "左移动", KeyCode.A, KeyCode.LeftArrow);
        EnsureBinding(SkyPrisonInputAction.MoveRight, "右移动", KeyCode.D, KeyCode.RightArrow);

        EnsureBinding(SkyPrisonInputAction.Sprint, "奔跑", KeyCode.LeftShift, KeyCode.RightShift, KeyCode.JoystickButton8);
        EnsureBinding(SkyPrisonInputAction.Sneak, "潜行", KeyCode.LeftControl, KeyCode.None, KeyCode.JoystickButton9);

        EnsureBinding(SkyPrisonInputAction.Jump, "跳跃", KeyCode.Space, KeyCode.None, KeyCode.JoystickButton0, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.Dodge, "闪避", KeyCode.Mouse1, KeyCode.None, KeyCode.JoystickButton1, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Interact, "交互/拾取/使用", KeyCode.E, KeyCode.Return, KeyCode.None, SkyPrisonInputTriggerMode.Press);

        EnsureBinding(SkyPrisonInputAction.LightAttack, "轻攻击", KeyCode.Mouse0, KeyCode.J, KeyCode.JoystickButton2, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.HeavyAttack, "重攻击（长按轻攻击）", KeyCode.Mouse0, KeyCode.K, KeyCode.JoystickButton2, SkyPrisonInputTriggerMode.Hold);
        EnsureBinding(SkyPrisonInputAction.Skill1, "技能 1", KeyCode.Q, KeyCode.U, KeyCode.JoystickButton4, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.Skill2, "技能 2", KeyCode.F, KeyCode.I, KeyCode.JoystickButton5, SkyPrisonInputTriggerMode.Press);
        // 2026-07-21：原默认键 R 让给了下面新增的换弹（Reload），改用 V 避免两个动作
        // 同时绑在同一个物理键上。
        EnsureBinding(SkyPrisonInputAction.Skill3, "技能 3", KeyCode.V, KeyCode.O, KeyCode.JoystickButton6, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.Reload, "换弹", KeyCode.R, KeyCode.None, KeyCode.JoystickButton11, SkyPrisonInputTriggerMode.Press);

        SetBinding(SkyPrisonInputAction.CyclePickup, "切换拾取目标", KeyCode.G, KeyCode.None, KeyCode.None, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.Inventory, "背包", KeyCode.B, KeyCode.Tab, KeyCode.JoystickButton3, SkyPrisonInputTriggerMode.Press);
        // 手柄绑定都取消——PS4原生模式下 R2 实际读的就是 JoystickButton7，跟这两个
        // 动作撞在同一个键上，按R2会连带触发"菜单"把窗口关掉。键盘不受影响。
        EnsureBinding(SkyPrisonInputAction.Map, "地图", KeyCode.M, KeyCode.None, KeyCode.None, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.Menu, "菜单", KeyCode.Escape, KeyCode.None, KeyCode.None, SkyPrisonInputTriggerMode.Press);
        // 手柄按键取消——用户反馈这个键实测在他的手柄上跟L3物理按键冲突（当时"LT空闲"
        // 这条注释是按理论按键布局猜的，跟用户实测的真实硬件映射对不上）。用户打算
        // 以后单独做一套面向手柄的独立菜单面板，这个键先空出来，键盘(C键)不受影响。
        EnsureBinding(SkyPrisonInputAction.CharacterPanel, "角色面板", KeyCode.C, KeyCode.None, KeyCode.None, SkyPrisonInputTriggerMode.Press);

        EnsureBinding(SkyPrisonInputAction.QuickItem1, "快捷物品 1", KeyCode.Alpha1, KeyCode.None, KeyCode.JoystickButton12, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.QuickItem2, "快捷物品 2", KeyCode.Alpha2, KeyCode.None, KeyCode.JoystickButton13, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.QuickItem3, "快捷物品 3", KeyCode.Alpha3, KeyCode.None, KeyCode.JoystickButton14, SkyPrisonInputTriggerMode.Press);
        EnsureBinding(SkyPrisonInputAction.QuickItem4, "快捷物品 4", KeyCode.Alpha4, KeyCode.None, KeyCode.JoystickButton15, SkyPrisonInputTriggerMode.Press);

        MigrateDefaultCombatInputToV6();
        RebuildLookup();
    }

    [ContextMenu("Apply V4 Default Keyboard Scheme")]
    public void ApplyV5DefaultKeyboardSchemeFromContextMenu()
    {
        ApplyV5DefaultKeyboardScheme();
    }

    // Kept for old editor buttons / context references. It now applies the current V5 scheme.
    public void ApplyV4DefaultKeyboardScheme()
    {
        ApplyV5DefaultKeyboardScheme();
    }

    public void ApplyV5DefaultKeyboardScheme()
    {
        if (bindings == null)
            bindings = new List<SkyPrisonInputBinding>();

        SetBinding(SkyPrisonInputAction.MoveUp, "上移动", KeyCode.W, KeyCode.UpArrow, KeyCode.None, SkyPrisonInputTriggerMode.Hold);
        SetBinding(SkyPrisonInputAction.MoveDown, "下移动", KeyCode.S, KeyCode.DownArrow, KeyCode.None, SkyPrisonInputTriggerMode.Hold);
        SetBinding(SkyPrisonInputAction.MoveLeft, "左移动", KeyCode.A, KeyCode.LeftArrow, KeyCode.None, SkyPrisonInputTriggerMode.Hold);
        SetBinding(SkyPrisonInputAction.MoveRight, "右移动", KeyCode.D, KeyCode.RightArrow, KeyCode.None, SkyPrisonInputTriggerMode.Hold);

        SetBinding(SkyPrisonInputAction.Sprint, "奔跑", KeyCode.LeftShift, KeyCode.RightShift, KeyCode.JoystickButton8, SkyPrisonInputTriggerMode.Hold);
        SetBinding(SkyPrisonInputAction.Sneak, "潜行", KeyCode.LeftControl, KeyCode.None, KeyCode.JoystickButton9, SkyPrisonInputTriggerMode.Hold);
        SetBinding(SkyPrisonInputAction.Jump, "跳跃", KeyCode.Space, KeyCode.None, KeyCode.JoystickButton0, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Dodge, "闪避", KeyCode.Mouse1, KeyCode.None, KeyCode.JoystickButton1, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Interact, "交互/拾取/使用", KeyCode.E, KeyCode.Return, KeyCode.None, SkyPrisonInputTriggerMode.Press);

        SetBinding(SkyPrisonInputAction.LightAttack, "轻攻击", KeyCode.Mouse0, KeyCode.J, KeyCode.JoystickButton2, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.HeavyAttack, "重攻击（长按轻攻击）", KeyCode.Mouse0, KeyCode.K, KeyCode.JoystickButton2, SkyPrisonInputTriggerMode.Hold);
        SetBinding(SkyPrisonInputAction.Skill1, "技能 1", KeyCode.Q, KeyCode.U, KeyCode.JoystickButton4, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Skill2, "技能 2", KeyCode.F, KeyCode.I, KeyCode.JoystickButton5, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Skill3, "技能 3", KeyCode.V, KeyCode.O, KeyCode.JoystickButton6, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Reload, "换弹", KeyCode.R, KeyCode.None, KeyCode.JoystickButton11, SkyPrisonInputTriggerMode.Press);

        SetBinding(SkyPrisonInputAction.Inventory, "背包", KeyCode.B, KeyCode.Tab, KeyCode.JoystickButton3, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Map, "地图", KeyCode.M, KeyCode.None, KeyCode.None, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.Menu, "菜单", KeyCode.Escape, KeyCode.None, KeyCode.None, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.CharacterPanel, "角色面板", KeyCode.C, KeyCode.None, KeyCode.None, SkyPrisonInputTriggerMode.Press);

        SetBinding(SkyPrisonInputAction.QuickItem1, "快捷物品 1", KeyCode.Alpha1, KeyCode.None, KeyCode.JoystickButton12, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.QuickItem2, "快捷物品 2", KeyCode.Alpha2, KeyCode.None, KeyCode.JoystickButton13, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.QuickItem3, "快捷物品 3", KeyCode.Alpha3, KeyCode.None, KeyCode.JoystickButton14, SkyPrisonInputTriggerMode.Press);
        SetBinding(SkyPrisonInputAction.QuickItem4, "快捷物品 4", KeyCode.Alpha4, KeyCode.None, KeyCode.JoystickButton15, SkyPrisonInputTriggerMode.Press);

        enableSprintDoubleTapDodge = false;
        sprintDoubleTapWindow = 0.26f;
        sprintDoubleTapMinReleaseTime = 0.03f;
        runSuppressAfterDoubleTapDodge = 0.08f;
        noMoveInputDodgeForward = false;
        directDodgeKeyStillAllowed = true;

        settingsVersion = "V6 - 2026-06-13 - quick item prompt bindings";
        RebuildLookup();
    }

    public void RebuildLookup()
    {
        if (lookup == null)
            lookup = new Dictionary<SkyPrisonInputAction, SkyPrisonInputBinding>();
        else
            lookup.Clear();

        if (bindings == null)
            return;

        for (int i = 0; i < bindings.Count; i++)
        {
            SkyPrisonInputBinding binding = bindings[i];
            if (binding == null)
                continue;

            lookup[binding.action] = binding;
        }
    }

    public SkyPrisonInputBinding GetBinding(SkyPrisonInputAction action)
    {
        if (lookup == null || lookup.Count == 0)
            RebuildLookup();

        if (lookup != null && lookup.TryGetValue(action, out SkyPrisonInputBinding binding))
            return binding;

        return null;
    }

    public bool GetAction(SkyPrisonInputAction action)
    {
        SkyPrisonInputBinding binding = GetBinding(action);
        if (binding == null)
            return false;

        return GetKeyAny(binding.primaryKey) || GetKeyAny(binding.secondaryKey) || GetKeyAny(binding.gamepadKey);
    }

    /// <summary>只读键盘绑定（primaryKey / secondaryKey），忽略手柄键。窗口打开时用此方法避免手柄按键穿透。</summary>
    public bool GetActionKeyboardOnly(SkyPrisonInputAction action)
    {
        SkyPrisonInputBinding binding = GetBinding(action);
        if (binding == null) return false;
        return GetKeyAny(binding.primaryKey) || GetKeyAny(binding.secondaryKey);
    }

    public bool GetActionDown(SkyPrisonInputAction action)
    {
        SkyPrisonInputBinding binding = GetBinding(action);
        if (binding == null)
            return false;

        return GetKeyDownAny(binding.primaryKey) || GetKeyDownAny(binding.secondaryKey) || GetKeyDownAny(binding.gamepadKey);
    }

    public bool GetActionUp(SkyPrisonInputAction action)
    {
        SkyPrisonInputBinding binding = GetBinding(action);
        if (binding == null)
            return false;

        return GetKeyUpAny(binding.primaryKey) || GetKeyUpAny(binding.secondaryKey) || GetKeyUpAny(binding.gamepadKey);
    }

    public Vector2 GetMoveVector()
    {
        float x = 0f;
        float y = 0f;

        if (GetAction(SkyPrisonInputAction.MoveLeft))
            x -= 1f;
        if (GetAction(SkyPrisonInputAction.MoveRight))
            x += 1f;
        if (GetAction(SkyPrisonInputAction.MoveDown))
            y -= 1f;
        if (GetAction(SkyPrisonInputAction.MoveUp))
            y += 1f;

        Vector2 digital = new Vector2(x, y);
        if (normalizeDigitalMove && digital.sqrMagnitude > 1f)
            digital.Normalize();

        if (!enableGamepadAxes)
            return digital;

        Vector2 analog = Vector2.zero;
        analog.x = SafeGetAxisRaw(gamepadHorizontalAxis);
        analog.y = SafeGetAxisRaw(gamepadVerticalAxis);

        if (Mathf.Abs(analog.x) < gamepadDeadZone)
            analog.x = 0f;
        if (Mathf.Abs(analog.y) < gamepadDeadZone)
            analog.y = 0f;

        if (analog.sqrMagnitude > 1f)
            analog.Normalize();

        return analog.sqrMagnitude > digital.sqrMagnitude ? analog : digital;
    }

    private void EnsureBinding(
        SkyPrisonInputAction action,
        string displayName,
        KeyCode primary,
        KeyCode secondary = KeyCode.None,
        KeyCode gamepad = KeyCode.None,
        SkyPrisonInputTriggerMode trigger = SkyPrisonInputTriggerMode.Hold)
    {
        SkyPrisonInputBinding binding = FindBinding(action);

        if (binding == null)
        {
            bindings.Add(new SkyPrisonInputBinding(action, displayName, primary, secondary, gamepad, trigger));
            return;
        }

        if (string.IsNullOrWhiteSpace(binding.displayName))
            binding.displayName = displayName;
    }

    private void SetBinding(
        SkyPrisonInputAction action,
        string displayName,
        KeyCode primary,
        KeyCode secondary,
        KeyCode gamepad,
        SkyPrisonInputTriggerMode trigger)
    {
        SkyPrisonInputBinding binding = FindBinding(action);

        if (binding == null)
        {
            bindings.Add(new SkyPrisonInputBinding(action, displayName, primary, secondary, gamepad, trigger));
            return;
        }

        binding.displayName = displayName;
        binding.primaryKey = primary;
        binding.secondaryKey = secondary;
        binding.gamepadKey = gamepad;
        binding.triggerMode = trigger;
    }

    private SkyPrisonInputBinding FindBinding(SkyPrisonInputAction action)
    {
        if (bindings == null)
            return null;

        for (int i = 0; i < bindings.Count; i++)
        {
            if (bindings[i] != null && bindings[i].action == action)
                return bindings[i];
        }

        return null;
    }

    private void MigrateDefaultCombatInputToV6()
    {
        SkyPrisonInputBinding jump = FindBinding(SkyPrisonInputAction.Jump);
        if (jump != null)
        {
            if (jump.primaryKey == KeyCode.None)
                jump.primaryKey = KeyCode.Space;
            if (string.IsNullOrWhiteSpace(jump.displayName))
                jump.displayName = "跳跃";
            jump.triggerMode = SkyPrisonInputTriggerMode.Press;
        }

        // V5 default: dodge is an explicit RMB action. Sprint double-tap dodge is no longer the default,
        // because the combat model is intentionally less combo-heavy and more strategy / survival oriented.
        SkyPrisonInputBinding dodge = FindBinding(SkyPrisonInputAction.Dodge);
        if (dodge != null)
        {
            bool looksLikeOldDefault =
                dodge.primaryKey == KeyCode.None ||
                dodge.primaryKey == KeyCode.Space ||
                dodge.primaryKey == KeyCode.LeftAlt ||
                dodge.primaryKey == KeyCode.Mouse1 ||
                (dodge.displayName != null && (dodge.displayName.Contains("独立键禁用") || dodge.displayName.Contains("双击奔跑")));

            if (looksLikeOldDefault)
            {
                dodge.displayName = "闪避";
                dodge.primaryKey = KeyCode.Mouse1;
                dodge.secondaryKey = KeyCode.None;
                dodge.gamepadKey = KeyCode.JoystickButton1;
                dodge.triggerMode = SkyPrisonInputTriggerMode.Press;
            }
        }

        // V5 default: heavy attack shares LMB with light attack, but uses Hold.
        // The combat consumer must interpret LightAttack Press as tap and HeavyAttack Hold as long press / charge.
        SkyPrisonInputBinding heavy = FindBinding(SkyPrisonInputAction.HeavyAttack);
        if (heavy != null)
        {
            bool looksLikeOldHeavyDefault =
                heavy.primaryKey == KeyCode.Mouse1 ||
                heavy.primaryKey == KeyCode.Mouse0 ||
                string.IsNullOrWhiteSpace(heavy.displayName) ||
                heavy.displayName == "重攻击";

            if (looksLikeOldHeavyDefault)
            {
                heavy.displayName = "重攻击（长按轻攻击）";
                heavy.primaryKey = KeyCode.Mouse0;
                heavy.secondaryKey = KeyCode.K;
                heavy.gamepadKey = KeyCode.JoystickButton2;
                heavy.triggerMode = SkyPrisonInputTriggerMode.Hold;
            }
        }

        SkyPrisonInputBinding interact = FindBinding(SkyPrisonInputAction.Interact);
        if (interact != null && interact.gamepadKey == KeyCode.JoystickButton0)
            interact.gamepadKey = KeyCode.None;

        EnsureQuickItemDefault(SkyPrisonInputAction.QuickItem1, "快捷物品 1", KeyCode.Alpha1, KeyCode.JoystickButton12);
        EnsureQuickItemDefault(SkyPrisonInputAction.QuickItem2, "快捷物品 2", KeyCode.Alpha2, KeyCode.JoystickButton13);
        EnsureQuickItemDefault(SkyPrisonInputAction.QuickItem3, "快捷物品 3", KeyCode.Alpha3, KeyCode.JoystickButton14);
        EnsureQuickItemDefault(SkyPrisonInputAction.QuickItem4, "快捷物品 4", KeyCode.Alpha4, KeyCode.JoystickButton15);

        enableSprintDoubleTapDodge = false;
        directDodgeKeyStillAllowed = true;
    }

    private void EnsureQuickItemDefault(SkyPrisonInputAction action, string displayName, KeyCode primaryKey, KeyCode gamepadKey)
    {
        SkyPrisonInputBinding binding = FindBinding(action);
        if (binding == null)
        {
            bindings.Add(new SkyPrisonInputBinding(action, displayName, primaryKey, KeyCode.None, gamepadKey, SkyPrisonInputTriggerMode.Press));
            return;
        }

        if (string.IsNullOrWhiteSpace(binding.displayName))
            binding.displayName = displayName;

        if (binding.primaryKey == KeyCode.None)
            binding.primaryKey = primaryKey;

        if (binding.gamepadKey == KeyCode.None)
            binding.gamepadKey = gamepadKey;

        binding.triggerMode = SkyPrisonInputTriggerMode.Press;
    }

    private static bool GetKeyAny(KeyCode key)
    {
        return key != KeyCode.None && Input.GetKey(key);
    }

    private static bool GetKeyDownAny(KeyCode key)
    {
        return key != KeyCode.None && Input.GetKeyDown(key);
    }

    private static bool GetKeyUpAny(KeyCode key)
    {
        return key != KeyCode.None && Input.GetKeyUp(key);
    }

    private static float SafeGetAxisRaw(string axisName)
    {
        if (string.IsNullOrWhiteSpace(axisName))
            return 0f;

        try
        {
            return Input.GetAxisRaw(axisName);
        }
        catch
        {
            return 0f;
        }
    }
}
