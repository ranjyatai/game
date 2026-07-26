using UnityEngine;
using UnityEngine.EventSystems;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 货架卡片鼠标聚焦时的两个反馈：数量步进+加入购物车面板显示出来，卡片描一圈
    /// 绿色高亮框（跟光标高亮同色，用户明确要求）——平时都藏起来避免货架列表太拥挤。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopShelfRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameObject _quickAddPanel;
        private GameObject _hoverOutline;
        private GameObject _addToCartBadge;

        // 售罄商品悬停时不应该再冒出"- 1 +"和加购角标（用户明确要求），由
        // ShopWindowController.ApplySoldOutVisual() 每次刷新库存状态时同步设置。
        public bool SoldOut { get; set; }

        private GameObject QuickAddPanel
        {
            get
            {
                if (_quickAddPanel == null)
                {
                    Transform t = transform.Find("QuickAddButton");
                    if (t != null) _quickAddPanel = t.gameObject;
                }
                return _quickAddPanel;
            }
        }

        private GameObject HoverOutline
        {
            get
            {
                if (_hoverOutline == null)
                {
                    Transform t = transform.Find("HoverOutline");
                    if (t != null) _hoverOutline = t.gameObject;
                }
                return _hoverOutline;
            }
        }

        // 价格色块右上角的"加入购物车"三角折角角标——嵌套在 PriceRow 下面，跟
        // QuickAddButton/HoverOutline 是直接子物体不一样，得用带路径的 Find。
        private GameObject AddToCartBadge
        {
            get
            {
                if (_addToCartBadge == null)
                {
                    Transform t = transform.Find("PriceRow/AddToCartBadge");
                    if (t != null) _addToCartBadge = t.gameObject;
                }
                return _addToCartBadge;
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => SetGamepadFocused(true);

        public void OnPointerExit(PointerEventData eventData) => SetGamepadFocused(false);

        // 手柄光标(SkyPrisonListGamepadNav焦点)落在这张卡片上时，跟鼠标悬停走同一套
        // 显隐逻辑——数量胶囊平时只在"有人在看这张卡"的时候才冒出来，手柄没有鼠标
        // 悬停这个概念，焦点落上来就等同于悬停。
        public void SetGamepadFocused(bool focused)
        {
            if (focused)
            {
                if (!SoldOut)
                {
                    QuickAddPanel?.SetActive(true);
                    AddToCartBadge?.SetActive(true);
                }
                HoverOutline?.SetActive(true);
            }
            else
            {
                QuickAddPanel?.SetActive(false);
                HoverOutline?.SetActive(false);
                AddToCartBadge?.SetActive(false);
            }
        }

        private void OnDisable()
        {
            QuickAddPanel?.SetActive(false);
            HoverOutline?.SetActive(false);
            AddToCartBadge?.SetActive(false);
        }
    }
}
