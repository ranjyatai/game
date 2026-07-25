using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 挂在窗口的"标题栏/可拖拽区域"上，让整个窗口（target）跟着鼠标拖动。
/// 通用组件，不是角色面板专属——以后别的窗口要支持拖动直接复用这个。
/// </summary>
public class SkyPrisonUIWindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _target;
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private readonly Vector3[] _corners = new Vector3[4];

    // 锁定期间完全不响应拖拽——用于"这个窗口呼出了一个子级弹窗，子级弹窗允许跟
    // 它重叠显示，此时不能再让这个窗口被拖动"的场景（比如角色面板呼出背包选装备
    // 期间）。不能靠 this.enabled=false 做这件事：Unity UI 事件系统调用
    // IBeginDragHandler/IDragHandler 不看 MonoBehaviour.enabled，禁用组件本身不会
    // 阻止这两个回调被调用，必须显式判断。
    private bool _locked;

    // 2026-07-23：_target/_canvas/_canvasRect 都不是 [SerializeField]——对于代码里
    // 直接 AddComponent+Init() 的窗口(角色面板)这没问题，Init 就是在真正的运行时对象
    // 上调的。但仓库是"编辑器时用脚本搭好存成 prefab 资产"这条路径：BuildTitleBar 里
    // 的 Init() 调用发生在编辑器临时物体上，存盘后这几个字段不会被序列化进 prefab，
    // 真正运行时 Instantiate 出来的实例上这几个字段全是 null——导致 OnDrag 直接
    // 提前 return（仓库本身完全拖不动），且从没调用过 Register()，仓库也就没进
    // "已注册悬浮窗"名单，别的窗口（比如背包）拖过来时压根查不到仓库这个障碍物。
    // Awake 时自愈一次，跟 InventoryDragHandler 已有的"没显式指定就退到 transform.parent"
    // 兜底逻辑同一个思路——不管是被 Init() 显式调用过，还是纯粹从 prefab 实例化出来
    // 从没被调用过，Awake 都能保证进入 OnDrag 之前这几个字段是配置好的。
    private void Awake()
    {
        if (_target == null)
            Init(transform.parent as RectTransform);
    }

    public void Init(RectTransform target)
    {
        _target = target;
        _canvas = GetComponentInParent<Canvas>();
        _canvasRect = _canvas != null ? _canvas.GetComponent<RectTransform>() : null;
        SkyPrisonWindowOverlapGuard.Register(_target);
    }

    public void SetLocked(bool locked) => _locked = locked;

    private void OnDestroy() => SkyPrisonWindowOverlapGuard.Unregister(_target);

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (_locked || _target == null) return;

        // ScaleWithScreenSize 下屏幕像素位移要除以画布缩放系数，换算成 UI 本地单位，
        // 不然分辨率越高拖动手感越"飘"（同样的鼠标移动量在高分屏下挪动距离偏大）。
        float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
        Vector2 oldPos = _target.anchoredPosition;
        Vector2 newPos = oldPos + eventData.delta / scale;

        // 窗口四条边都不能拖出屏幕——跟背包用的是同一套夹取算法。
        newPos = SkyPrisonWindowOverlapGuard.ClampToCanvas(_target, newPos, _canvasRect);

        _target.anchoredPosition = newPos;

        // 拖动之后如果跟别的悬浮窗（比如背包）重叠了，直接撤回这次移动——
        // 两个窗口不允许互相压在一起。
        _target.GetWorldCorners(_corners);
        var selfRect = new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y);
        if (SkyPrisonWindowOverlapGuard.WouldOverlapOthers(_target, selfRect))
            _target.anchoredPosition = oldPos;
    }
}
