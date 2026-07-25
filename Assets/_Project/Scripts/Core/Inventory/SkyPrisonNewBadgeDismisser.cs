using UnityEngine;
using UnityEngine.EventSystems;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 挂在背包格子根节点上。
    /// 鼠标悬停时消除左上角 NEW 徽章，并持久化（同一局内不再显示）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonNewBadgeDismisser : MonoBehaviour, IPointerEnterHandler
    {
        private GameObject _badge;
        private InventorySlotView _slotView;

        public void SetBadge(GameObject badge) => _badge = badge;

        public void ShowBadge()
        {
            if (_badge != null) _badge.SetActive(true);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // 通过 SlotView 清除，既隐藏徽标也把 isNew 持久化到数据条目（重刷后不再出现）。
            if (_slotView == null) _slotView = GetComponent<InventorySlotView>();
            if (_slotView != null)
            {
                _slotView.MarkSeen();
                return;
            }

            // 兜底：拿不到 SlotView 时仅隐藏当前徽标。
            if (_badge != null && _badge.activeSelf)
                _badge.SetActive(false);
        }
    }
}
