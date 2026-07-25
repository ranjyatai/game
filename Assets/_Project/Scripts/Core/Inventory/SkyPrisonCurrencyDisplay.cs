using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 背包底部货币位：单个货币图标 + 数量。
    /// 点击图标 → 循环切换到下一种货币：图标淡出/淡入，数字滚动到该货币当前数量。
    /// 货币列表与图标来自 CurrencyDefinition（builder 注入）；数量读 CurrencyRuntime。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonCurrencyDisplay : MonoBehaviour
    {
        [SerializeField] private List<CurrencyDefinition> currencies = new List<CurrencyDefinition>();
        [SerializeField] private Image iconImage;
        [SerializeField] private Text amountText;

        private const float IconFadeSpeed = 6f;   // 图标淡入淡出速度
        private const float NumberRollSpeed = 10f; // 数字滚动跟随速度

        // 飘字：最大向上偏移（px），防止穿过上方格子区域
        private const float PopupMaxRise = 36f;
        private const float PopupDuration = 1.1f;

        private int _index;
        private CurrencyRuntime _rt;
        private bool _rtSubscribed;

        private double _shownValue;     // 当前显示的数字（滚动用）
        private float  _iconAlpha = 1f;
        private int    _swapPhase;      // 0=无 1=淡出 2=淡入
        private Sprite _queuedSprite;
        private bool   _wired;

        /// <summary>builder 构建 PF 时注入。</summary>
        public void Init(List<CurrencyDefinition> defs, Image icon, Text amount)
        {
            currencies = defs ?? new List<CurrencyDefinition>();
            iconImage = icon;
            amountText = amount;
        }

        private void OnEnable()
        {
            ResolveRuntime();
            WireClick();
            _index = Mathf.Clamp(_index, 0, Mathf.Max(0, currencies.Count - 1));
            ApplyImmediate();
        }

        private void OnDisable()
        {
            if (_rt != null && _rtSubscribed) { _rt.OnCurrencyChanged -= OnCurrencyChanged; _rtSubscribed = false; }
            if (_suspendingChromatic) { SkyPrisonInventoryChromatic.PopGlobalSuspend(); _suspendingChromatic = false; }
        }

        private void WireClick()
        {
            if (_wired || iconImage == null) return;
            var btn = iconImage.GetComponent<Button>();
            if (btn == null) btn = iconImage.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(Cycle);
            iconImage.raycastTarget = true;
            _wired = true;
        }

        private void ResolveRuntime()
        {
            if (_rt == null) _rt = CurrencyRuntime.Instance ?? FindObjectOfType<CurrencyRuntime>();
            if (_rt != null && !_rtSubscribed) { _rt.OnCurrencyChanged += OnCurrencyChanged; _rtSubscribed = true; }
        }

        private void OnCurrencyChanged(string changedId, long delta)
        {
            var cur = Current();
            if (cur == null || cur.currencyId != changedId || delta == 0) return;
            SpawnPopup(delta);
        }

        // ── 飘字 ──────────────────────────────────────────────────────────────

        private TMP_FontAsset ResolvePopupFont()
        {
            var style = GetComponentInParent<SkyPrisonUIGlobalStyleSettings_V1>();
            if (style != null && style.defaultNumberFont != null)
                return style.defaultNumberFont;
            return TMP_Settings.defaultFontAsset;
        }

        private void SpawnPopup(long delta)
        {
            if (amountText == null) return;

            GameObject go = new GameObject("CurrencyPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(amountText.transform.parent, false);
            go.transform.SetAsLastSibling();

            RectTransform rt = go.GetComponent<RectTransform>();
            RectTransform srcRt = amountText.GetComponent<RectTransform>();
            rt.anchorMin        = srcRt.anchorMin;
            rt.anchorMax        = srcRt.anchorMax;
            rt.pivot            = srcRt.pivot;
            rt.anchoredPosition = srcRt.anchoredPosition;
            rt.sizeDelta        = srcRt.sizeDelta;

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font            = ResolvePopupFont();
            tmp.fontSize        = Mathf.Max(10, amountText.fontSize - 2);
            tmp.fontStyle       = FontStyles.Bold;
            tmp.alignment       = TextAlignmentOptions.Left;
            tmp.raycastTarget   = false;
            tmp.overflowMode    = TextOverflowModes.Overflow;

            bool gain = delta > 0;
            tmp.color = gain
                ? new Color(0.45f, 1f, 0.72f, 1f)
                : new Color(1f, 0.38f, 0.38f, 1f);
            tmp.text = gain ? $"+{delta:N0}" : $"{delta:N0}";

            StartCoroutine(AnimatePopup(rt, tmp));
        }

        private IEnumerator AnimatePopup(RectTransform rt, TextMeshProUGUI tmp)
        {
            Vector2 startPos = rt.anchoredPosition;
            Color startColor = tmp.color;
            float elapsed = 0f;

            while (elapsed < PopupDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = elapsed / PopupDuration;

                float rise = PopupMaxRise * (1f - Mathf.Pow(1f - p, 2f));
                rt.anchoredPosition = startPos + new Vector2(0f, rise);

                float alpha = p < 0.5f ? 1f : 1f - (p - 0.5f) * 2f;
                var c = startColor; c.a = alpha; tmp.color = c;

                yield return null;
            }

            Destroy(rt.gameObject);
        }

        public void Cycle()
        {
            if (currencies.Count <= 1) return;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            _index = (_index + 1) % currencies.Count;
            // 启动图标淡出→换图→淡入；数字会自动滚动到新货币数量
            _queuedSprite = CurrentIcon();
            _swapPhase = 1;
        }

        private void ApplyImmediate()
        {
            ResolveRuntime();
            _shownValue = CurrentAmount();
            if (amountText != null) amountText.text = ((long)_shownValue).ToString("N0");

            _iconAlpha = 1f; _swapPhase = 0;
            if (iconImage != null)
            {
                Sprite s = CurrentIcon();
                iconImage.sprite = s;
                iconImage.enabled = s != null;
                var c = iconImage.color; c.a = 1f; iconImage.color = c;
            }
        }

        private bool _suspendingChromatic;

        private void Update()
        {
            if (currencies.Count == 0) return;
            if (_rt == null) ResolveRuntime(); // 货币运行时可能比窗口晚就位
            float dt = Time.unscaledDeltaTime;

            // 切换/滚动动画期间挂起色差抓屏，避免站立时周期性抓屏卡顿打断动画
            bool animating = _swapPhase != 0 ||
                             System.Math.Abs(_shownValue - CurrentAmount()) >= 0.5;
            if (animating && !_suspendingChromatic)
            {
                SkyPrisonInventoryChromatic.PushGlobalSuspend();
                _suspendingChromatic = true;
            }
            else if (!animating && _suspendingChromatic)
            {
                SkyPrisonInventoryChromatic.PopGlobalSuspend();
                _suspendingChromatic = false;
            }

            // 数字滚动到当前货币数量
            long target = CurrentAmount();
            if (System.Math.Abs(_shownValue - target) < 0.5) _shownValue = target;
            else _shownValue = Mathf.Lerp((float)_shownValue, target, dt * NumberRollSpeed);
            if (amountText != null) amountText.text = ((long)System.Math.Round(_shownValue)).ToString("N0");

            // 图标淡出→换图→淡入
            if (_swapPhase == 1)
            {
                _iconAlpha -= dt * IconFadeSpeed;
                if (_iconAlpha <= 0f)
                {
                    _iconAlpha = 0f;
                    if (iconImage != null) { iconImage.sprite = _queuedSprite; iconImage.enabled = _queuedSprite != null; }
                    _swapPhase = 2;
                }
            }
            else if (_swapPhase == 2)
            {
                _iconAlpha += dt * IconFadeSpeed;
                if (_iconAlpha >= 1f) { _iconAlpha = 1f; _swapPhase = 0; }
            }

            if (iconImage != null && _swapPhase != 0)
            {
                var c = iconImage.color; c.a = _iconAlpha; iconImage.color = c;
            }
        }

        private CurrencyDefinition Current()
        {
            if (_index < 0 || _index >= currencies.Count) return null;
            return currencies[_index];
        }

        private Sprite CurrentIcon() { var d = Current(); return d != null ? d.icon : null; }

        private long CurrentAmount()
        {
            var d = Current();
            if (d == null) return 0;
            return _rt != null ? _rt.Get(d.currencyId) : 0;
        }
    }
}
