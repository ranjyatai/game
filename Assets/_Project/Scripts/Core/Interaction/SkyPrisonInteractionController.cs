using System.Collections.Generic;
using UnityEngine;
using SkyPrison.Runtime.UI;

/// <summary>
/// 挂在玩家身上（与 SkyPrisonItemPickupController 同级）。
/// 扫描附近所有 IInteractable，选中最近的一个，按 Interact 键触发交互。
/// 窗口打开期间自动隐藏提示，不响应输入。
/// </summary>
[DefaultExecutionOrder(9001)]
[DisallowMultipleComponent]
public sealed class SkyPrisonInteractionController : MonoBehaviour
{
    [SerializeField] private float interactRadius = 2.8f;
    [SerializeField] private float promptHeight   = 2.4f;

    private SkyPrisonInputSettings    _input;
    private SkyPrisonWindowManager_V1 _windows;

    private readonly List<IInteractable> _inRange = new List<IInteractable>();
    private IInteractable _current;

    // ── 提示 UI（与 ItemPickupController 同规格）────────────────────────
    private Canvas        _canvas;
    private RectTransform _panel;
    // 提示内容由 InteractionPromptUI 组件负责构建，保持本文件精简
    private InteractionPromptUI _promptUI;

    private Camera _cam;

    private void Update()
    {
        GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        if (playerGo == null) { Clear(); return; }

        // HasAnyWindowOpen() 只认走 windowManager.Open() 注册过的"prefab 流程"窗口——
        // 设置窗口/暂停菜单是纯手写的全屏窗口，走的是另一套机制（只设 ExternalBlock 静态
        // 标记，从没注册进 openedWindows），漏了这一条会导致设置/按键绑定弹窗开着的时候
        // 按 E 照样能触发场景里的交互（挖到过一次：弹窗开着按 E 直接触发了撤离终端）。
        if ((Windows() != null && _windows.HasAnyWindowOpen()) || SkyPrisonWindowManager_V1.AnyBlockingWindowOpen) { Clear(); return; }

        RefreshInRange(playerGo.transform.position);

        if (_inRange.Count == 0) { Clear(); return; }

        // 选最近的可交互对象
        IInteractable nearest = _inRange[0];
        if (nearest != _current)
            _current = nearest;

        EnsurePromptUI();
        _promptUI?.Show(_current, GetCamera());

        // 按交互键
        SkyPrisonInputSettings input = GetInput();
        if (input != null && input.GetActionDown(SkyPrisonInputAction.Interact))
        {
            if (_current.CanInteract)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
                _current.Interact();
            }
            else
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            }
        }
    }

    private void Clear()
    {
        _current = null;
        _promptUI?.Hide();
    }

    // ── 范围检测 ──────────────────────────────────────────────────────────

    private void RefreshInRange(Vector3 pos)
    {
        _inRange.Clear();
        float r2 = interactRadius * interactRadius;

        // 扫描场景中所有已注册的 IInteractable
        var all = InteractableRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            IInteractable it = all[i];
            if (it == null) continue;
            if ((it.InteractPosition - pos).sqrMagnitude <= r2)
                _inRange.Add(it);
        }

        // 按距离排序，最近的排首位
        Vector3 refPos = pos;
        _inRange.Sort((a, b) =>
            (a.InteractPosition - refPos).sqrMagnitude
            .CompareTo((b.InteractPosition - refPos).sqrMagnitude));
    }

    // ── 懒加载工具 ────────────────────────────────────────────────────────

    private SkyPrisonWindowManager_V1 Windows()
    {
        if (_windows == null) _windows = FindObjectOfType<SkyPrisonWindowManager_V1>();
        return _windows;
    }

    private SkyPrisonInputSettings GetInput()
    {
        if (_input == null) _input = FindObjectOfType<SkyPrisonInputSettings>();
        return _input;
    }

    private Camera GetCamera()
    {
        if (_cam == null) _cam = Camera.main;
        return _cam;
    }

    private void EnsurePromptUI()
    {
        if (_promptUI != null) return;
        _promptUI = GetComponent<InteractionPromptUI>() ?? gameObject.AddComponent<InteractionPromptUI>();
        _promptUI.Configure(promptHeight);
    }
}
