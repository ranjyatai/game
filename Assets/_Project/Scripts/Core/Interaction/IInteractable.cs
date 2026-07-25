using UnityEngine;

/// <summary>
/// 所有世界中可交互对象实现此接口。
/// 挂载该接口的 MonoBehaviour 会被 SkyPrisonInteractionController 自动检测。
/// </summary>
public interface IInteractable
{
    /// <summary>交互点的世界坐标（用于排序和提示条定位）。</summary>
    Vector3 InteractPosition { get; }

    /// <summary>显示在提示条上的动作文字，例如"进入工房"、"与 NPC 交谈"。</summary>
    string InteractLabel { get; }

    /// <summary>玩家按下交互键时调用。</summary>
    void Interact();

    /// <summary>是否当前可交互（false = 提示变灰，按键无效）。</summary>
    bool CanInteract { get; }
}
