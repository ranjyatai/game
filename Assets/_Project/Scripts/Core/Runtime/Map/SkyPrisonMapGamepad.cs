using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

/// <summary>
/// 战术地图的手柄输入。用 Input System 的 Gamepad 接口(选关界面同一套做法)，不走旧的 JoystickButton 数字键位：
/// 数字键位在 Xbox / PS4 原生 / DS4Windows 下各不相同，而且战斗键位已经占满，地图键不想再和它们撞。
///   · 打开 / 关闭：PS4 = 触摸板按下；Xbox = View(⧉，左边那个小键)；其它手柄 = Select
///   · 窗口里：LS 平移、RS 转动、LT / RT 缩放、Y 切正交 / 透视、A 在准星处放置 / 撤销标记、LB / RB 换槽位、X 清除当前槽、B 关闭
/// </summary>
public static class SkyPrisonMapGamepad
{
    const float DeadZone = 0.2f;

    /// <summary>这一帧是否按下了「地图键」(PS4 触摸板 / Xbox View)。</summary>
    public static bool MapButtonPressed()
    {
        var gp = Gamepad.current;
        if (gp == null) return false;
        if (gp is DualShockGamepad ds) return ds.touchpadButton.wasPressedThisFrame;
        return gp.selectButton.wasPressedThisFrame;
    }

    /// <summary>摇杆去死区并重新映射到 0~1，推到底仍是 1。</summary>
    public static Vector2 Stick(Vector2 v)
    {
        float m = v.magnitude;
        if (m < DeadZone) return Vector2.zero;
        return v * ((m - DeadZone) / ((1f - DeadZone) * m));
    }
}
