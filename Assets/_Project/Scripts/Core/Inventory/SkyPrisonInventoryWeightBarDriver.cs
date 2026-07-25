using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 挂在背包 WeightBar 上。
    /// 自动找场景中的 InventoryRuntime，订阅 OnInventoryChanged，
    /// 每次背包内容变化时刷新进度条颜色和数值文字。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryWeightBarDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Image fillImage;
        [SerializeField] private Text  valueText;

        [Header("Thresholds (%)")]
        [SerializeField] [Range(0f, 100f)] private float lightThreshold    = 30f;
        [SerializeField] [Range(0f, 150f)] private float overloadThreshold = 90f;

        [Header("Bar Colors")]
        [SerializeField] private Color lightColor    = new Color(0.64f, 1.00f, 0.78f, 0.95f);
        [SerializeField] private Color normalColor   = new Color(1.00f, 0.94f, 0.55f, 0.95f);
        [SerializeField] private Color overloadColor = new Color(1.00f, 0.62f, 0.58f, 0.95f);

        [Header("Animation")]
        [Tooltip("开窗时进度条从 0 向右增长到当前负重比的速度（比例/秒），越大越快。")]
        [SerializeField] private float fillLerpSpeed = 2.5f;

        private InventoryRuntime _inventory;
        private EquipmentWeightController _equipWeight;
        private float _targetRatio;
        private float _displayedRatio;
        private float _lastCurrent, _lastMax;

        // 重量单位"石英"之前是直接拼进字符串的硬编码中文，从没查过表，不管切什么
        // 语言这行文字都不会变。改成走 UILocalizationTable 的 unit_weight key。
        private UILocalizationTable _locTable;
        private bool _locResolved;

        private string L(string key, string fallback)
        {
            if (!_locResolved)
            {
                var loc = GetComponentInParent<SkyPrisonInventoryTextLocalizer>()
                       ?? GetComponentInChildren<SkyPrisonInventoryTextLocalizer>(true);
                _locTable = loc != null ? loc.Table : null;
                _locResolved = true;
            }
            return _locTable != null ? _locTable.Get(key, fallback) : fallback;
        }

        /// <summary>编辑器 PF 构建时调用，绑定填充 Image 和数值 Text。</summary>
        public void BindReferences(Image fill, Text value)
        {
            fillImage = fill;
            valueText = value;
        }

        private void OnEnable()
        {
            _inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
            if (_inventory != null)
                _inventory.OnInventoryChanged += Refresh;

            // 这条负重条之前只读 InventoryRuntime.TotalWeight（背包格子里的东西），
            // 已经装备到身上的武器/防具完全没算进来——所以装备武器后这里显示的负重
            // 反而会"变轻"（其实是东西从背包格子移出去了，重量凭空消失）。
            // 跟 EquipmentWeightController.Rebuild() 保持同一份口径，装备变化也要刷新。
            var auth = FindObjectOfType<SkyPrisonPlayerAuthority>();
            var player = auth?.CurrentPlayer;
            _equipWeight = player != null ? player.GetComponentInChildren<EquipmentWeightController>(true) : null;
            if (_equipWeight == null)
                _equipWeight = FindObjectOfType<EquipmentWeightController>();
            EquipmentRuntime.OnEquipped   += OnEquipmentChanged;
            EquipmentRuntime.OnUnequipped += OnEquipmentChanged;

            LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;

            _displayedRatio = 0f; // 开窗时从 0 向右增长
            Refresh();
            ApplyFill(_displayedRatio); // 先压到 0，再由 Update 平滑长到目标
        }

        private void OnEquipmentChanged(EquipmentSlotType _, InventoryItemEntry __) => Refresh();

        private void OnLanguageChanged(string _) => SetWeight(_lastCurrent, _lastMax);

        private void Update()
        {
            if (Mathf.Approximately(_displayedRatio, _targetRatio)) return;
            _displayedRatio = Mathf.MoveTowards(_displayedRatio, _targetRatio, fillLerpSpeed * Time.unscaledDeltaTime);
            ApplyFill(_displayedRatio);
        }

        private void OnDisable()
        {
            if (_inventory != null)
                _inventory.OnInventoryChanged -= Refresh;
            EquipmentRuntime.OnEquipped   -= OnEquipmentChanged;
            EquipmentRuntime.OnUnequipped -= OnEquipmentChanged;
            LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        }

        private void Refresh()
        {
            if (_inventory != null)
            {
                _equipWeight?.Rebuild(); // 跟 InventoryBurdenSync 一样，读之前先强制刷新，避免订阅顺序导致读到旧值
                float equippedWeight = _equipWeight != null ? _equipWeight.TotalEquipWeight : 0f;
                SetWeight(_inventory.TotalWeight + equippedWeight, _inventory.MaxWeightCapacity);
            }
            else
                SetWeight(0f, 50f);
        }

        /// <summary>可由外部直接调用强制刷新（如切换场景后重绑定）。</summary>
        public void SetWeight(float current, float max)
        {
            _lastCurrent = current; _lastMax = max;
            if (max <= 0f) max = 1f;
            float ratio = current / max; // 允许超过 1（超重）
            float pct   = ratio * 100f;

            _targetRatio = ratio; // 由 Update 把显示值平滑推到这里（开窗时从 0 向右增长）

            if (fillImage != null)
                fillImage.color = pct <= lightThreshold    ? lightColor
                                : pct >= overloadThreshold ? overloadColor
                                                           : normalColor;

            if (valueText != null)
                valueText.text = $"{current:F1} / {max:F1} {L("unit_weight", "石英")}";
        }

        // 只设进度条宽度（anchorMax.x），颜色/文字在 SetWeight 里按目标值即时设定。
        private void ApplyFill(float ratio)
        {
            if (fillImage == null) return;
            RectTransform ft = fillImage.rectTransform;
            ft.anchorMin = new Vector2(0f, 0f);
            ft.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            ft.offsetMin = Vector2.zero;
            ft.offsetMax = Vector2.zero;
        }
    }
}
