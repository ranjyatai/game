using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 商店货架卡片数量输入框——没有聚焦（没在打字）的时候背景填充透明，一点进去
    /// 输入才亮出来（用户明确要求），跟Unity默认InputField没有"未选中透明"这个状态
    /// 不一样，得自己接 ISelectHandler/IDeselectHandler 手动切。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopQtyInputFocusHighlight : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private Image background;
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0f);
        [SerializeField] private Color focusedColor = new Color(1f, 1f, 1f, 0.12f);

        private void Awake()
        {
            if (background == null) background = GetComponent<Image>();
            if (background != null) background.color = idleColor;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (background != null) background.color = focusedColor;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (background != null) background.color = idleColor;
        }
    }
}
