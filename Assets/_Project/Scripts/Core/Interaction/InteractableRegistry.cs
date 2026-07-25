using System.Collections.Generic;

/// <summary>
/// 全局可交互对象注册表。
/// IInteractable 实现者在 OnEnable 中 Register，OnDisable 中 Unregister。
/// SkyPrisonInteractionController 从这里查询，避免每帧 FindObjectsOfType。
/// </summary>
public static class InteractableRegistry
{
    private static readonly List<IInteractable> _all = new List<IInteractable>();

    public static IReadOnlyList<IInteractable> All => _all;

    public static void Register(IInteractable it)
    {
        if (!_all.Contains(it)) _all.Add(it);
    }

    public static void Unregister(IInteractable it)
    {
        _all.Remove(it);
    }
}
