using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 格子级交互的公共接口——背包(SkyPrisonInventoryInteraction)和仓库
    /// (StashInventoryInteraction)各自实现一份，InventorySlotInteractor 通过这个接口
    /// 统一转发拖拽/右键/点击事件，不用关心格子具体挂在哪个窗口下面。
    /// </summary>
    public interface IInventorySlotController
    {
        void BeginDrag(int slotIndex, PointerEventData e);
        void UpdateDrag(PointerEventData e);
        void EndDrag(int slotIndex, PointerEventData e);
        void SetHoveredSlot(int slotIndex);
        void QuickSplitOne(int slotIndex);
        void ShowContextMenu(int slotIndex, PointerEventData e);
    }

    /// <summary>
    /// 挂在每个格子上：把鼠标事件转给面板级的交互控制器(背包或仓库，通过
    /// IInventorySlotController 统一解析)。自己不碰数据，只做事件管道。格子的
    /// SlotIndex 取自同物体上的 InventorySlotView。空格子的拖拽事件会转发给父层
    /// ScrollRect，保证空格区域可以拖动滚动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventorySlotInteractor : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler, IInitializePotentialDragHandler
    {
        private InventorySlotView _view;
        private IInventorySlotController _ctrl;
        private InventoryItemDetailController _detail;
        private ScrollRect _parentScroll;
        private bool _forwardingToScroll;

        private void Awake()
        {
            _view = GetComponent<InventorySlotView>();
            _parentScroll = GetComponentInParent<ScrollRect>();
        }

        public void OnInitializePotentialDrag(PointerEventData e)
        {
            if (_parentScroll != null)
                ExecuteEvents.Execute(_parentScroll.gameObject, e, ExecuteEvents.initializePotentialDrag);
        }

        public int SlotIndex => _view != null ? _view.SlotIndex : -1;

        // 背包格子挂在 SkyPrisonInventoryInteraction 下面，仓库格子挂在
        // StashInventoryInteraction 下面——两者互斥，谁先找到就是谁。
        private IInventorySlotController Ctrl()
        {
            if (_ctrl != null) return _ctrl;
            _ctrl = GetComponentInParent<SkyPrisonInventoryInteraction>();
            if (_ctrl == null) _ctrl = GetComponentInParent<StashInventoryInteraction>();
            return _ctrl;
        }

        private InventoryItemDetailController Detail()
        {
            if (_detail == null) _detail = GetComponentInParent<InventoryItemDetailController>();
            return _detail;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;

            bool hasItem = _view != null && !_view.IsEmpty;
            if (hasItem)
            {
                _forwardingToScroll = false;
                Ctrl()?.BeginDrag(SlotIndex, e);
            }
            else
            {
                _forwardingToScroll = true;
                if (_parentScroll != null)
                    ExecuteEvents.Execute(_parentScroll.gameObject, e, ExecuteEvents.beginDragHandler);
            }
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;

            if (_forwardingToScroll)
            {
                if (_parentScroll != null)
                    ExecuteEvents.Execute(_parentScroll.gameObject, e, ExecuteEvents.dragHandler);
            }
            else
            {
                Ctrl()?.UpdateDrag(e);
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;

            if (_forwardingToScroll)
            {
                if (_parentScroll != null)
                    ExecuteEvents.Execute(_parentScroll.gameObject, e, ExecuteEvents.endDragHandler);
                _forwardingToScroll = false;
            }
            else
            {
                Ctrl()?.EndDrag(SlotIndex, e);
            }
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (_view == null || _view.IsEmpty) return;
            // 把这个格子自己的 RectTransform 一起传过去——详情面板要贴在"实际悬停的
            // 这一格"旁边，不是贴在整个背包/仓库面板的边缘（面板很高的时候，悬停顶部
            // 格子却在面板垂直中点弹详情，跟鼠标位置对不上）。
            Detail()?.OnSlotHoverEnter(SlotIndex, (RectTransform)transform);
            Ctrl()?.SetHoveredSlot(SlotIndex);
        }

        public void OnPointerExit(PointerEventData e)
        {
            Detail()?.OnSlotHoverExit(SlotIndex);
            Ctrl()?.SetHoveredSlot(-1);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (_view == null || _view.IsEmpty || _view.IsDimmed) return;

            if (e.button == PointerEventData.InputButton.Right)
                Ctrl()?.QuickSplitOne(SlotIndex);   // 右键：快速拆分一个到空格
            else if (e.button == PointerEventData.InputButton.Left)
                Ctrl()?.ShowContextMenu(SlotIndex, e); // 左键：操作菜单
        }

        /// <summary>全局 raycast 找鼠标当前落在哪个格子上——背包/仓库两边的拖拽控制器
        /// 判定落点都要用这同一套逻辑，抽到这里共用一份，不各自实现一遍再各改各的。</summary>
        public static InventorySlotInteractor FindUnder(PointerEventData e)
        {
            if (EventSystem.current == null) return null;
            var results = new List<RaycastResult>();
            var ped = new PointerEventData(EventSystem.current) { position = e.position };
            EventSystem.current.RaycastAll(ped, results);
            for (int i = 0; i < results.Count; i++)
            {
                InventorySlotInteractor it = results[i].gameObject.GetComponentInParent<InventorySlotInteractor>();
                if (it != null) return it;
            }
            return null;
        }
    }
}
