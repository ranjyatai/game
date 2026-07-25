using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 单个背包格子的视图层。
    /// 由 Workbench builder 在构建 PF 时创建并 Init() 注入引用，
    /// 运行时由 InventoryGridView 驱动 Bind / SetEmpty（容量外整格由 GridView 隐藏）。
    /// 自己不碰数据层，只负责显示。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image      iconImage;
        [SerializeField] private TMP_Text   countText;
        [SerializeField] private GameObject newBadge;
        [SerializeField] private Image      cellBg;
        [SerializeField] private int        slotIndex;
        // 右上角"绑在快捷栏几号"角标——跟左上角 NewBadge 错开，不挤在一起。
        // 由 Editor/PatchInventoryQuickSlotBadge.cs 批量打进 prefab。
        [SerializeField] private GameObject quickSlotBadge;
        [SerializeField] private TMP_Text   quickSlotBadgeText;

        private static readonly Color CountNormal = new Color(0.92f, 0.92f, 0.94f, 1f);
        private static readonly Color CountFull   = new Color(1.00f, 0.62f, 0.22f, 1f); // 堆叠满=橙
        private static readonly Color IconWhite   = Color.white;
        private static readonly Color IconDimmed  = new Color(1f, 1f, 1f, 0.28f);

        // NEW 文字缓慢闪烁参数（淡红文字呼吸，无填充）
        private const float BadgePulseSpeed = 3.2f;   // 角速度，约 2 秒一个呼吸周期
        private const float BadgeAlphaMin   = 0.35f;  // 最淡
        private const float BadgeAlphaMax   = 1.00f;  // 最亮
        private static readonly Color BadgeTextColor = new Color(1f, 0.45f, 0.45f, 1f); // 淡红

        private CanvasGroup _badgeCg;
        private bool _badgeStyled;
        private bool _countFontEnsured;
        private InventoryItemEntry _entry;

        // 旧版 UI Text 缺字体就不渲染（Unity 2022+ 移除了内置 Arial）。统一兜底取内置字体。
        private static Font _uiFont;
        private static Font UiFont
        {
            get
            {
                if (_uiFont == null)
                    _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                           ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _uiFont;
            }
        }

        public int SlotIndex { get => slotIndex; set => slotIndex = value; }
        public ItemDefinition CurrentDef { get; private set; }
        public bool IsEmpty => CurrentDef == null;
        public bool IsDimmed { get; private set; } // 当前被筛选调暗（不响应点击交互）

        /// <summary>编辑器构建 PF 时调用，注入格子内各子节点引用。</summary>
        public void Init(Image icon, TMP_Text count, GameObject newBadgeGo, Image bg, int index)
        {
            iconImage = icon;
            countText = count;
            newBadge  = newBadgeGo;
            cellBg    = bg;
            slotIndex = index;
        }

        /// <summary>容量内、有物品：显示图标 + 数量 + 堆叠满橙字 + 新物品 NEW 徽标。</summary>
        public void Bind(InventoryItemEntry entry)
        {
            if (entry == null || entry.definition == null) { SetEmpty(); return; }
            CurrentDef = entry.definition;
            _entry = entry;

            if (iconImage != null)
            {
                iconImage.sprite  = entry.definition.icon;
                iconImage.enabled = entry.definition.icon != null;
                iconImage.color   = IconWhite;
            }

            if (countText != null)
            {
                if (!_countFontEnsured)
                {
                    countText.fontStyle   = FontStyles.Bold;
                    countText.fontSize    = 36;
                    countText.richText    = true;
                    countText.enableWordWrapping = false;
                    countText.overflowMode = TextOverflowModes.Overflow;
                    countText.alignment   = TextAlignmentOptions.BottomRight;
                    _countFontEnsured = true;
                }
                bool showCount = entry.count > 1;
                // ×号小一号并下沉，与数字底部对齐
                countText.text  = showCount ? $"<voffset=-0.06em><size=62%>×</size></voffset>{entry.count}" : "";
                countText.color = entry.IsStackFull ? CountFull : CountNormal;
            }

            SetBadgeVisible(entry.isNew);
            SetQuickSlotBadge(entry.definition);
        }

        // 这个物品种类当前绑在哪些快捷槽（0~3）——同一种物品可以同时绑给好几个快捷槽
        // （比如1号2号都指定成同一种消耗品），之前只返回"第一个匹配的"，导致后面绑的
        // 槽位角标显示不出来，看着像是"被吃掉了"。改成把所有匹配的槽位号都拼出来。
        private static string BuildQuickSlotBadgeText(ItemDefinition def)
        {
            var runtime = QuickSlotRuntime.Instance;
            if (runtime == null || def == null) return null;

            string text = null;
            for (int i = 0; i < QuickSlotRuntime.SlotCount; i++)
            {
                if (runtime.GetSlot(i) != def) continue;
                text = text == null ? (i + 1).ToString() : $"{text},{i + 1}";
            }
            return text;
        }

        private void SetQuickSlotBadge(ItemDefinition def)
        {
            if (quickSlotBadge == null) return;
            string text = BuildQuickSlotBadgeText(def);
            bool show = text != null;
            if (quickSlotBadge.activeSelf != show) quickSlotBadge.SetActive(show);
            if (show && quickSlotBadgeText != null) quickSlotBadgeText.text = text;
        }

        /// <summary>容量内、无物品：空格。</summary>
        public void SetEmpty()
        {
            CurrentDef = null;
            _entry = null;
            if (iconImage != null) { iconImage.sprite = null; iconImage.enabled = false; }
            if (countText != null) countText.text = "";
            SetBadgeVisible(false);
            SetQuickSlotBadge(null);
        }

        /// <summary>筛选调暗：不属于当前分类的物品图标变暗（空格不动）。</summary>
        public void SetDimmed(bool dim)
        {
            IsDimmed = dim && CurrentDef != null;
            if (iconImage == null || CurrentDef == null) return;
            iconImage.color = dim ? IconDimmed : IconWhite;
        }

        // 手柄按住R2批量选择发往仓库时的标记色——琥珀色，跟手柄抓取态(GrabColor)同色系，
        // 让"这几个已经被选中要一起送走"跟"当前正在拿着的这一个"视觉上是同一种语义。
        private static readonly Color BatchMarkedColor = new Color(1.00f, 0.85f, 0.30f, 0.55f);
        private Color? _cellBgOriginalColor;

        /// <summary>手柄批量选择模式下标记/取消标记这一格，用于发往仓库前的多选视觉反馈。</summary>
        public void SetBatchMarked(bool marked)
        {
            if (cellBg == null) return;
            if (_cellBgOriginalColor == null) _cellBgOriginalColor = cellBg.color;
            cellBg.color = marked ? BatchMarkedColor : _cellBgOriginalColor.Value;
        }

        /// <summary>鼠标悬停看过后调用：消除 NEW 标记并持久化到数据条目（同一局内不再提示）。</summary>
        public void MarkSeen()
        {
            if (_entry != null) _entry.isNew = false;
            SetBadgeVisible(false);
        }

        private void SetBadgeVisible(bool visible)
        {
            if (newBadge == null) return;
            if (newBadge.activeSelf != visible)
                newBadge.SetActive(visible);

            if (visible)
            {
                EnsureBadgeStyle();
                if (_badgeCg == null)
                {
                    _badgeCg = newBadge.GetComponent<CanvasGroup>();
                    if (_badgeCg == null) _badgeCg = newBadge.AddComponent<CanvasGroup>();
                }
            }
        }

        // 把徽标改成「无填充 + 淡红 NEW 文字」：去掉红色底图，文字染淡红。
        private void EnsureBadgeStyle()
        {
            if (_badgeStyled || newBadge == null) return;
            _badgeStyled = true;

            Image bg = newBadge.GetComponent<Image>();
            if (bg != null) bg.enabled = false; // 去掉红色填充

            Text label = newBadge.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                if (UiFont != null) label.font = UiFont; // 同样强制替换失效的内置 Arial
                label.color = BadgeTextColor;
            }
        }

        private void Update()
        {
            if (newBadge == null || !newBadge.activeSelf || _badgeCg == null) return;

            float t = (Mathf.Sin(Time.unscaledTime * BadgePulseSpeed) + 1f) * 0.5f;
            _badgeCg.alpha = Mathf.Lerp(BadgeAlphaMin, BadgeAlphaMax, t);
        }
    }
}
