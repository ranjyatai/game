using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 挂在背包筛选标签上：鼠标悬停时，把（未选中的）标签文字染成半透明冷绿，作为「可点击」预览；
    /// 移开恢复常态色。选中态由控制器统一管理（白字 + 辉光），hover 不覆盖选中项。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryTabHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private TextMeshProUGUI _label;
        private Color _normalColor;
        private Color _hoverColor;
        private System.Func<int, bool> _isSelected;
        private int _index;

        /// <summary>2026-07-23：原本只服务背包(InventoryWindowController)，仓库要复用同一套
        /// hover 效果，把"选中态查询"从具体类型换成通用委托，不用为仓库另抄一份 hover 组件。</summary>
        public void Configure(System.Func<int, bool> isSelected, int index, TextMeshProUGUI label, Color normalColor, Color hoverColor)
        {
            _isSelected  = isSelected;
            _index       = index;
            _label       = label;
            _normalColor = normalColor;
            _hoverColor  = hoverColor;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_label == null || IsSelected()) return;
            _label.color = _hoverColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_label == null || IsSelected()) return;
            _label.color = _normalColor;
        }

        private bool IsSelected() => _isSelected != null && _isSelected(_index);
    }
}
