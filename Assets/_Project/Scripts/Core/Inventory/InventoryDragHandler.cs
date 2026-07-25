using UnityEngine;
using UnityEngine.EventSystems;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 挂在背包 TitleBar 上，鼠标左键按住拖动整个面板，并确保面板不超出屏幕。
    /// 要求：TitleBar 上的 Image.raycastTarget = true，且父层级有 Canvas。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryDragHandler : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [SerializeField] private RectTransform dragTarget;

        private RectTransform _canvasRect;
        private bool _dragging;
        private Vector2 _pointerStartCanvas;
        private Vector2 _anchoredPosStart;

        private void Awake()
        {
            if (dragTarget == null)
                dragTarget = transform.parent as RectTransform;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                _canvasRect = canvas.rootCanvas.GetComponent<RectTransform>();

            SkyPrisonWindowOverlapGuard.Register(dragTarget);
        }

        private void OnDestroy() => SkyPrisonWindowOverlapGuard.Unregister(dragTarget);

        public void SetDragTarget(RectTransform target) => dragTarget = target;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (dragTarget == null || _canvasRect == null) return;

            _dragging = true;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, eventData.position, eventData.pressEventCamera,
                out _pointerStartCanvas);

            _anchoredPosStart = dragTarget.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || dragTarget == null || _canvasRect == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, eventData.position, eventData.pressEventCamera,
                    out Vector2 pointerNow))
                return;

            Vector2 desired = _anchoredPosStart + (pointerNow - _pointerStartCanvas);
            Vector2 oldPos = dragTarget.anchoredPosition;
            // 四条边不超出屏幕——跟角色信息面板共用同一套夹取算法（SkyPrisonFloatingWindowKit），
            // 不再各自维护一份。
            dragTarget.anchoredPosition = SkyPrisonWindowOverlapGuard.ClampToCanvas(dragTarget, desired, _canvasRect);

            // 跟别的悬浮窗（比如角色信息面板）重叠了就撤回这次移动，两个窗口不允许互相压着。
            var corners = new Vector3[4];
            dragTarget.GetWorldCorners(corners);
            var selfRect = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            if (SkyPrisonWindowOverlapGuard.WouldOverlapOthers(dragTarget, selfRect))
                dragTarget.anchoredPosition = oldPos;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                _dragging = false;
        }
    }
}
