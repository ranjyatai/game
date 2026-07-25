using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    [DisallowMultipleComponent]
    public sealed class SkyPrisonPlayerHUDView_V4_StyleDriven : MonoBehaviour
    {
        [Header("HP - Masked Fill")]
        [SerializeField] private RectTransform hpFillMask;
        [SerializeField] private RectTransform hpDamageLagMask;
        [SerializeField] private Image hpFillImage;
        [SerializeField] private Image hpDamageLagImage;
        [SerializeField] private TMP_Text hpLabel;
        [SerializeField] private float hpFillMaxWidth = 600f;

        [Header("HP - Animation")]
        [SerializeField] private bool enableHpDamageLag = true;
        [SerializeField] private float hpFillChangeDuration = 0.16f;
        [SerializeField] private float hpDamageLagHoldDuration = 2.35f;
        [SerializeField] private float hpDamageLagCatchupDuration = 2.5f;

        [Header("LP - Masked Fill")]
        [SerializeField] private RectTransform lpFillMask;
        [SerializeField] private RectTransform lpGhostMask;
        [SerializeField] private Image lpFillImage;
        [SerializeField] private Image lpGhostImage;
        [SerializeField] private TMP_Text lpLabel;
        [SerializeField] private float lpFillMaxWidth = 600f;

        [Header("LP - Animation")]
        [SerializeField] private bool enableLpGhostFill = false;
        [SerializeField] private float lpFillChangeDuration = 0.08f;

        [Header("Time")]
        [SerializeField] private bool useUnscaledTimeForBars = false;

        [Header("Load - Segmented")]
        [SerializeField] private TMP_Text loadPrefixText;
        [SerializeField] private TMP_Text loadNumberText;
        [SerializeField] private TMP_Text loadPercentText;
        [SerializeField] private TMP_Text loadStatusText;

        [Header("Text Aspect")]
        [SerializeField] private Vector2 hpLabelAspect = Vector2.one;
        [SerializeField] private Vector2 lpLabelAspect = Vector2.one;
        [SerializeField] private Vector2 loadLabelAspect = Vector2.one;
        [SerializeField] private Vector2 loadNumberAspect = Vector2.one;
        [SerializeField] private Vector2 loadStatusAspect = Vector2.one;

        [SerializeField] private string loadPrefixValue = "LD";
        [SerializeField] private string loadPercentSymbol = "%";
        [SerializeField] private string loadLightStatusText = "轻便";
        [SerializeField] private string loadNormalStatusText = "负重";
        [SerializeField] private string loadOverweightStatusText = "超重";

        [Header("Load - Status Colors")]
        [SerializeField] private Color loadLightStatusColor = new Color(0.64f, 1.00f, 0.78f, 0.95f);
        [SerializeField] private Color loadNormalStatusColor = new Color(1.00f, 0.94f, 0.55f, 0.95f);
        [SerializeField] private Color loadOverweightStatusColor = new Color(1.00f, 0.62f, 0.58f, 0.95f);

        [Header("Quick Slots")]
        [SerializeField] private Image[] quickSlotIcons = new Image[4];
        // 冷却遮罩：Image.Type.Filled（Radial360或Vertical，看prefab里怎么摆），
        // fillAmount 1→0 表示"还剩多少冷却"，0 时代表冷却结束、完全不挡图标。
        [SerializeField] private Image[] quickSlotCooldownFills = new Image[4];

        [Header("Display")]
        [SerializeField] private bool compactLabels = true;
        [SerializeField] private string hpLabelTextValue = "HP";
        [SerializeField] private string lpLabelTextValue = "LP";

        private float hpTargetRatio = 1f;
        private float hpVisualRatio = 1f;
        private float hpDamageLagRatio = 1f;
        private float hpDamageLagHoldTimer;

        [Header("HP Lag Runtime Diagnostic - Read Only")]
        [SerializeField] private float runtimeHpTargetRatio;
        [SerializeField] private float runtimeHpVisualRatio;
        [SerializeField] private float runtimeHpDamageLagRatio;
        [SerializeField] private float runtimeHpDamageLagHoldTimer;
        [SerializeField] private string runtimeHpLagBranch = "Idle";
        [SerializeField] private int runtimeHpSetCalls;
        [SerializeField] private int runtimeHpDamageDetectedCalls;
        [SerializeField] private int runtimeHpPreviewCalls;
        [SerializeField] private int runtimeHpSnapCalls;
        [SerializeField] private int runtimeHpTickCalls;
        [SerializeField] private int runtimeHpHoldTicks;
        [SerializeField] private int runtimeHpCatchupTicks;

        [Header("LP Penalty Runtime - Compatibility")]
        [SerializeField] private bool lpPenaltyActiveRuntime;

        [Header("LP Penalty Flash")]
        [SerializeField] private bool enableLpPenaltyFlash = true;
        [SerializeField] private bool useLpEmptyPenaltyFallback = true;
        [SerializeField] private Image lpSlotBaseImage;
        [SerializeField] private Image lpBackImage;
        [SerializeField] private Image lpPenaltyFlashImage;
        [SerializeField] private string lpPenaltyRuntimeStatus = "Not evaluated";
        [SerializeField] private bool lpPenaltyRuntimeEffective;
        [SerializeField] private float lpPenaltyRuntimeRatio;
        [SerializeField] private Color lpPenaltyFlashColor = new Color(1.00f, 0.18f, 0.12f, 1.0f);
        [SerializeField, Min(0.1f)] private float lpPenaltyFlashSpeed = 7.0f;
        [SerializeField, Range(0f, 1f)] private float lpPenaltyFlashMinMix = 0.25f;
        [SerializeField, Range(0f, 1f)] private float lpPenaltyFlashMaxMix = 0.85f;

        private bool lpPenaltyBaseColorsCaptured;
        private bool lpPenaltyExternalActive;
        private Color lpPenaltyBaseSlotBaseColor = Color.white;
        private Color lpPenaltyBaseBackColor = Color.white;
        private Color lpPenaltyBaseFillColor = Color.white;
        private Color lpPenaltyBaseGhostColor = Color.white;
        private Color lpPenaltyBaseLabelColor = Color.white;

        // 负重状态：overweight 时 LP 条颜色偏橙
        private static readonly Color LpOverweightTint = new Color(1f, 0.55f, 0.15f, 1f);
        private Color _lpDefaultFillColor = Color.white;
        private bool _lpDefaultFillColorCaptured;

        private float lpTargetRatio = 1f;
        private float lpVisualRatio = 1f;
        private float lpGhostRatio = 1f;

        private SkyPrisonHUDCombatVisibilityController ResolveCombatVisibilityController()
        {
            SkyPrisonHUDCombatVisibilityController controller = GetComponent<SkyPrisonHUDCombatVisibilityController>();
            if (controller == null)
                controller = GetComponentInParent<SkyPrisonHUDCombatVisibilityController>();
            return controller;
        }

        private void NotifyCombatVisibilityHp(float current, float max)
        {
            SkyPrisonHUDCombatVisibilityController controller = ResolveCombatVisibilityController();
            if (controller != null)
                controller.SetHp(current, max);
        }

        private void NotifyCombatVisibilityLp(float current, float max)
        {
            SkyPrisonHUDCombatVisibilityController controller = ResolveCombatVisibilityController();
            if (controller != null)
                controller.SetLp(current, max);
        }

        public void Bind(
            RectTransform newHpFillMask,
            RectTransform newHpDamageLagMask,
            TMP_Text newHpLabel,
            RectTransform newLpFillMask,
            TMP_Text newLpLabel,
            TMP_Text legacyLoadText,
            Image[] newQuickSlotIcons)
        {
            hpFillMask = newHpFillMask;
            hpDamageLagMask = newHpDamageLagMask;
            hpLabel = newHpLabel;
            lpFillMask = newLpFillMask;
            lpLabel = newLpLabel;
            quickSlotIcons = newQuickSlotIcons;

            ResolveFillImagesFromCurrentMasks();

            if (legacyLoadText != null && loadStatusText == null)
                loadStatusText = legacyLoadText;

            if (hpFillMask != null)
                hpFillMaxWidth = Mathf.Max(1f, hpFillMask.sizeDelta.x);

            if (lpFillMask != null)
                lpFillMaxWidth = Mathf.Max(1f, lpFillMask.sizeDelta.x);

            NormalizeOverheadStyleHpLagTiming();
            EnforceHpLayerOrder();
            EnforceLpLayerOrder();
            CaptureLpPenaltyBaseColors(true);
            ApplyHpVisual(true);
            ApplyLpVisual(true);
            ApplyLpPenaltyFlashVisual(true);
        }

        public void BindLoadSegments(TMP_Text prefix, TMP_Text number, TMP_Text percent, TMP_Text status)
        {
            loadPrefixText = prefix;
            loadNumberText = number;
            loadPercentText = percent;
            loadStatusText = status;
            ApplyLoadText(51, GetLoadStatusText(51));
        }

        public void ApplyStyleTiming(SkyPrisonHUDStyleProfile_V1 style)
        {
            if (style == null)
                return;

            enableHpDamageLag = style.enableHpDamageLag;
            hpFillChangeDuration = Mathf.Max(0f, style.hpFillChangeDuration);
            hpDamageLagHoldDuration = Mathf.Max(0f, style.hpDamageLagHoldDuration);
            hpDamageLagCatchupDuration = Mathf.Max(0f, style.hpDamageLagCatchupDuration);
            enableLpGhostFill = style.enableLpGhostFill;
            lpFillChangeDuration = Mathf.Max(0f, style.lpFillChangeDuration);
            useUnscaledTimeForBars = style.useUnscaledTimeForBars;
            NormalizeOverheadStyleHpLagTiming();
            hpLabelTextValue = style.ResolveHpLabelText();
            lpLabelTextValue = style.ResolveLpLabelText();
            if (hpLabel != null)
                hpLabel.text = hpLabelTextValue;
            if (lpLabel != null)
                lpLabel.text = lpLabelTextValue;

            loadPrefixValue = style.ResolveLoadPrefixText();
            loadPercentSymbol = style.ResolveLoadPercentSymbolText();
            loadLightStatusText = style.ResolveLoadLightText();
            loadNormalStatusText = style.ResolveLoadNormalText();
            loadOverweightStatusText = style.ResolveLoadOverweightText();
            loadLightStatusColor = style.loadLightStatusColor;
            loadNormalStatusColor = style.loadNormalStatusColor;
            loadOverweightStatusColor = style.loadOverweightStatusColor;

            if (loadPrefixText != null)
                loadPrefixText.text = loadPrefixValue;
            if (loadPercentText != null)
                loadPercentText.text = loadPercentSymbol;

            hpLabelAspect = style.ResolveHpLabelAspect();
            lpLabelAspect = style.ResolveLpLabelAspect();
            loadLabelAspect = style.ResolveLoadLabelAspect();
            loadNumberAspect = style.ResolveLoadNumberAspect();
            loadStatusAspect = style.ResolveLoadStatusAspect();
            ApplyTextAspect(hpLabel, hpLabelAspect);
            ApplyTextAspect(lpLabel, lpLabelAspect);
            ApplyTextAspect(loadPrefixText, loadLabelAspect);
            ApplyTextAspect(loadNumberText, loadNumberAspect);
            ApplyTextAspect(loadPercentText, loadNumberAspect);
            ApplyTextAspect(loadStatusText, loadStatusAspect);

            if (lpGhostMask != null)
                lpGhostMask.gameObject.SetActive(enableLpGhostFill);

            if (hpDamageLagMask != null)
                hpDamageLagMask.gameObject.SetActive(enableHpDamageLag);

            CaptureLpPenaltyBaseColors(true);
            ApplyLpPenaltyFlashVisual(true);
        }

        public void BindLpGhost(RectTransform newLpGhostMask)
        {
            lpGhostMask = newLpGhostMask;
            if (lpGhostMask != null)
                lpGhostMask.gameObject.SetActive(enableLpGhostFill);
        }


        public bool EnsurePreviewBindingsFrom(Transform root)
        {
            if (root == null)
                return hpFillMask != null && lpFillMask != null;

            RectTransform resolvedHpFillMask = FindRectRecursive(root, "HpFillMaskRoot");
            RectTransform resolvedHpDamageLagMask = FindRectRecursive(root, "HpDamageLagMaskRoot");
            RectTransform resolvedLpFillMask = FindRectRecursive(root, "LpFillMaskRoot");
            RectTransform resolvedLpGhostMask = FindRectRecursive(root, "LpGhostMaskRoot");
            Image resolvedHpFillImage = FindImageRecursive(root, "HpFill");
            Image resolvedHpDamageLagImage = FindImageRecursive(root, "HpDamageLagFill");
            Image resolvedLpFillImage = FindImageRecursive(root, "LpFill");
            Image resolvedLpGhostImage = FindImageRecursive(root, "LpGhostFill");
            Image resolvedLpSlotBaseImage = FindImageRecursive(root, "LpSlotBase");
            Image resolvedLpBackImage = FindImageRecursive(root, "LpBack");

            if (resolvedHpFillImage != null)
                hpFillImage = resolvedHpFillImage;
            if (resolvedHpDamageLagImage != null)
                hpDamageLagImage = resolvedHpDamageLagImage;
            if (resolvedLpFillImage != null)
                lpFillImage = resolvedLpFillImage;
            if (resolvedLpGhostImage != null)
                lpGhostImage = resolvedLpGhostImage;
            if (resolvedLpSlotBaseImage != null)
                lpSlotBaseImage = resolvedLpSlotBaseImage;
            if (resolvedLpBackImage != null)
                lpBackImage = resolvedLpBackImage;

            if (resolvedHpFillMask != null)
                hpFillMask = resolvedHpFillMask;
            if (resolvedHpDamageLagMask != null)
                hpDamageLagMask = resolvedHpDamageLagMask;
            if (resolvedLpFillMask != null)
                lpFillMask = resolvedLpFillMask;
            if (resolvedLpGhostMask != null)
                lpGhostMask = resolvedLpGhostMask;

            TMP_Text resolvedHpLabel = FindTMPRecursive(root, "HpLabel");
            TMP_Text resolvedLpLabel = FindTMPRecursive(root, "LpLabel");
            TMP_Text resolvedLoadPrefix = FindTMPRecursive(root, "LoadPrefixText");
            TMP_Text resolvedLoadNumber = FindTMPRecursive(root, "LoadNumberText");
            TMP_Text resolvedLoadPercent = FindTMPRecursive(root, "LoadPercentText");
            TMP_Text resolvedLoadStatus = FindTMPRecursive(root, "LoadStatusText");

            if (resolvedHpLabel != null)
                hpLabel = resolvedHpLabel;
            if (resolvedLpLabel != null)
                lpLabel = resolvedLpLabel;
            if (resolvedLoadPrefix != null)
                loadPrefixText = resolvedLoadPrefix;
            if (resolvedLoadNumber != null)
                loadNumberText = resolvedLoadNumber;
            if (resolvedLoadPercent != null)
                loadPercentText = resolvedLoadPercent;
            if (resolvedLoadStatus != null)
                loadStatusText = resolvedLoadStatus;

            hpFillMaxWidth = ResolveCanonicalBarMaxWidth(root, "HpBack", hpFillMask, hpFillMaxWidth);
            lpFillMaxWidth = ResolveCanonicalBarMaxWidth(root, "LpBack", lpFillMask, lpFillMaxWidth);

            NormalizeOverheadStyleHpLagTiming();
            EnforceHpLayerOrder();
            EnforceLpLayerOrder();

            SkyPrisonPlayerStatusLocalStyleSettings localSettings =
                GetComponent<SkyPrisonPlayerStatusLocalStyleSettings>();
            if (localSettings == null)
                localSettings = root.GetComponentInChildren<SkyPrisonPlayerStatusLocalStyleSettings>(true);
            if (localSettings != null)
                ApplyLocalPlayerStatusStyleSettings(localSettings);

            ApplyHpVisual(true);
            ApplyLpVisual(true);

            return hpFillMask != null && lpFillMask != null;
        }

        private static float ResolveCanonicalBarMaxWidth(Transform root, string backName, RectTransform mask, float fallback)
        {
            RectTransform back = FindRectRecursive(root, backName);
            if (back != null)
            {
                float width = Mathf.Max(Mathf.Abs(back.sizeDelta.x), back.rect.width);
                if (width > 1f)
                    return width;
            }

            if (mask != null)
            {
                float width = Mathf.Max(Mathf.Abs(mask.sizeDelta.x), mask.rect.width);
                if (width > 1f)
                    return width;
            }

            return Mathf.Max(1f, fallback);
        }

        private static RectTransform FindRectRecursive(Transform root, string name)
        {
            Transform found = FindTransformRecursive(root, name);
            return found != null ? found as RectTransform : null;
        }

        private static TMP_Text FindTMPRecursive(Transform root, string name)
        {
            Transform found = FindTransformRecursive(root, name);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }

        private static Image FindImageRecursive(Transform root, string name)
        {
            Transform found = FindTransformRecursive(root, name);
            return found != null ? found.GetComponent<Image>() : null;
        }

        private void ResolveFillImagesFromCurrentMasks()
        {
            if (hpFillImage == null && hpFillMask != null)
                hpFillImage = FindImageRecursive(hpFillMask, "HpFill");
            if (hpDamageLagImage == null && hpDamageLagMask != null)
                hpDamageLagImage = FindImageRecursive(hpDamageLagMask, "HpDamageLagFill");
            if (lpFillImage == null && lpFillMask != null)
                lpFillImage = FindImageRecursive(lpFillMask, "LpFill");
            if (lpGhostImage == null && lpGhostMask != null)
                lpGhostImage = FindImageRecursive(lpGhostMask, "LpGhostFill");

            // LP penalty should flash the whole LP strip, not only the "LP" label.
            // The visible empty strip is often LpSlotBase / LpBack, not LpFill because fill width is 0.
            Transform searchRoot = lpFillMask != null ? lpFillMask.root : transform.root;
            if (lpSlotBaseImage == null)
                lpSlotBaseImage = FindImageRecursive(searchRoot, "LpSlotBase");
            if (lpBackImage == null)
                lpBackImage = FindImageRecursive(searchRoot, "LpBack");
        }

        private static Transform FindTransformRecursive(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransformRecursive(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void NormalizeOverheadStyleHpLagTiming()
        {
            // Migration guard: older HUD builds stored 0.18/0.45/0.90 style values.
            // The project standard is the UnitOverheadUIView timing: hold time 2.35s, reference chase speed 2.5 ratio/sec.
            // This keeps existing saved PF/style assets from silently overriding the confirmed overhead UI behavior.
            if (hpDamageLagHoldDuration < 1.0f)
                hpDamageLagHoldDuration = 2.35f;

            if (hpDamageLagCatchupDuration < 1.0f)
                hpDamageLagCatchupDuration = 2.5f;

            useUnscaledTimeForBars = false;
        }

        public void SetHp(float current, float max)
        {
            NotifyCombatVisibilityHp(current, max);
            float next = SafeRatio(current, max);
            float oldTarget = hpTargetRatio;

            hpTargetRatio = next;
            runtimeHpSetCalls++;

            if (enableHpDamageLag && next < oldTarget - 0.0001f)
            {
                // Exact UnitOverheadUIView semantics:
                // the lag/reference bar stays at the previous target value and only refreshes hold.
                hpDamageLagRatio = Mathf.Max(hpDamageLagRatio, oldTarget);
                hpDamageLagHoldTimer = Mathf.Max(0f, hpDamageLagHoldDuration);
                runtimeHpDamageDetectedCalls++;
                runtimeHpLagBranch = "SetHpDamageHold";
            }
            else if (next > hpDamageLagRatio)
            {
                // Heal/correction should not leave a stale damage reference ahead of the real HP.
                hpDamageLagRatio = next;
                if (hpDamageLagHoldTimer > 0f)
                    hpDamageLagHoldTimer = 0f;
                runtimeHpLagBranch = "SetHpHealOrCorrection";
            }
            else
            {
                runtimeHpLagBranch = "SetHpNoDamage";
            }

            UpdateHpRuntimeDiagnostics();

            if (hpLabel != null)
                hpLabel.text = compactLabels ? hpLabelTextValue : $"{hpLabelTextValue} {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        public void SetHpDelay(float current, float max)
        {
            hpDamageLagRatio = SafeRatio(current, max);
            ApplyHpVisual(false);
        }

        /// <summary>
        /// Editor/workbench event preview only.
        /// Static HP sliders should use SnapHp; this method is only for a real damage event:
        /// the current HP fill moves to the new value while the damage-lag fill stays at the old value,
        /// then catches up after hpDamageLagHoldDuration.
        /// </summary>
        public void PreviewHpDamageEvent(float previousCurrent, float nextCurrent, float max)
        {
            // Workbench/debug compatibility only. Runtime Binder must use SetHp(current,max).
            // Feed the same single SetHp state machine instead of keeping a second HP-lag path.
            float previous = SafeRatio(previousCurrent, max);

            hpVisualRatio = previous;
            hpTargetRatio = previous;
            hpDamageLagRatio = Mathf.Max(hpDamageLagRatio, previous);
            hpDamageLagHoldTimer = 0f;
            runtimeHpPreviewCalls++;

            SetHp(nextCurrent, max);
            ApplyHpVisual(true);

            if (hpLabel != null)
                hpLabel.text = compactLabels ? hpLabelTextValue : $"{hpLabelTextValue} {Mathf.CeilToInt(nextCurrent)} / {Mathf.CeilToInt(max)}";
        }

        public void SetLp(float current, float max)
        {
            NotifyCombatVisibilityLp(current, max);
            float next = SafeRatio(current, max);
            if (enableLpGhostFill && next < lpVisualRatio && lpGhostRatio < lpVisualRatio)
                lpGhostRatio = lpVisualRatio;
            else if (next > lpGhostRatio)
                lpGhostRatio = next;

            lpTargetRatio = next;
            ApplyLpEmptyPenaltyFallback(next);

            if (lpLabel != null)
                lpLabel.text = compactLabels ? lpLabelTextValue : $"{lpLabelTextValue} {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        public void SetLoad(float normalizedLoad, string statusText = null)
        {
            // Load is allowed to exceed 100%. In this project, 90%+ is already overweight,
            // and preview/runtime values such as 120% are valid instead of being clamped to 100%.
            normalizedLoad = Mathf.Max(0f, normalizedLoad);
            int percent = Mathf.RoundToInt(normalizedLoad * 100f);
            if (string.IsNullOrWhiteSpace(statusText))
                statusText = GetLoadStatusText(percent);

            ApplyLoadText(percent, statusText);
            ApplyLpLoadTint(percent);
        }

        private void ApplyLpLoadTint(int percent)
        {
            if (lpFillImage == null) return;
            if (!_lpDefaultFillColorCaptured)
            {
                _lpDefaultFillColor = lpFillImage.color;
                _lpDefaultFillColorCaptured = true;
            }
            Color target = percent >= 90 ? LpOverweightTint : _lpDefaultFillColor;
            if (lpFillImage.color != target)
            {
                lpFillImage.color = target;
                lpPenaltyBaseFillColor = target;
                RefreshLpPenaltyEffectiveState(lpTargetRatio, true);
            }
        }

        // 负重数字滚动状态
        private int _loadTargetPercent;
        private float _loadShownPercent;
        private bool _loadInited;
        private const float LoadRollSpeed = 12f;

        private void ApplyLoadText(int percent, string statusText)
        {
            if (loadPrefixText != null)
                loadPrefixText.text = loadPrefixValue;

            // 数字改为滚动：设目标值，由 Update 渐变；首次直接吸附
            _loadTargetPercent = percent;
            if (!_loadInited)
            {
                _loadShownPercent = percent;
                _loadInited = true;
                if (loadNumberText != null) loadNumberText.text = percent.ToString();
            }

            if (loadPercentText != null)
                loadPercentText.text = loadPercentSymbol;
            if (loadStatusText != null)
            {
                loadStatusText.text = string.IsNullOrWhiteSpace(statusText) ? GetLoadStatusText(percent) : statusText;
                loadStatusText.color = GetLoadStatusColor(percent);
            }
        }

        // 负重数字朝目标滚动（状态/颜色已按真实值即时更新，只滚动数字）
        private void TickLoadRoll(float dt)
        {
            if (loadNumberText == null || !_loadInited || dt <= 0f) return;
            if (Mathf.Abs(_loadShownPercent - _loadTargetPercent) < 0.5f)
            {
                if (Mathf.RoundToInt(_loadShownPercent) != _loadTargetPercent)
                {
                    _loadShownPercent = _loadTargetPercent;
                    loadNumberText.text = _loadTargetPercent.ToString();
                }
                return;
            }
            _loadShownPercent = Mathf.Lerp(_loadShownPercent, _loadTargetPercent, Mathf.Clamp01(dt * LoadRollSpeed));
            loadNumberText.text = Mathf.RoundToInt(_loadShownPercent).ToString();
        }

        private string GetLoadStatusText(int percent)
        {
            if (percent < 30)
                return string.IsNullOrWhiteSpace(loadLightStatusText) ? "轻便" : loadLightStatusText;
            if (percent < 90)
                return string.IsNullOrWhiteSpace(loadNormalStatusText) ? "负重" : loadNormalStatusText;
            return string.IsNullOrWhiteSpace(loadOverweightStatusText) ? "超重" : loadOverweightStatusText;
        }

        private Color GetLoadStatusColor(int percent)
        {
            if (percent < 30)
                return loadLightStatusColor;
            if (percent < 90)
                return loadNormalStatusColor;
            return loadOverweightStatusColor;
        }

        public void SetLpPenaltyActive(bool active)
        {
            // V9:
            // External gameplay state is an additive source, not a permanent override.
            // Some runtime drivers may call false by default in Build; that must not suppress
            // the LP-empty fallback when LP is actually 0.
            lpPenaltyExternalActive = active;
            RefreshLpPenaltyEffectiveState(lpTargetRatio, true);
        }

        public void ApplyLocalPlayerStatusStyleSettings(SkyPrisonPlayerStatusLocalStyleSettings settings)
        {
            if (settings == null)
                return;

            enableLpPenaltyFlash = settings.enableLpEmptyPenaltyFlash;
            lpPenaltyFlashColor = settings.lpEmptyPenaltyFlashColor;
            lpPenaltyFlashSpeed = settings.lpEmptyPenaltyFlashSpeed;
            lpPenaltyFlashMinMix = settings.lpEmptyPenaltyFlashMin;
            lpPenaltyFlashMaxMix = settings.lpEmptyPenaltyFlashMax;

            if (!enableLpPenaltyFlash)
            {
                lpPenaltyExternalActive = false;
                lpPenaltyActiveRuntime = false;
            }

            RefreshLpPenaltyEffectiveState(lpTargetRatio, true);
        }

        private void ApplyLpEmptyPenaltyFallback(float ratio)
        {
            RefreshLpPenaltyEffectiveState(ratio, true);
        }

        private void RefreshLpPenaltyEffectiveState(float ratio, bool forceVisual)
        {
            ratio = Mathf.Clamp01(ratio);
            lpPenaltyRuntimeRatio = ratio;

            bool emptyFallbackActive = useLpEmptyPenaltyFallback && ratio <= 0.0001f;
            bool nextActive = enableLpPenaltyFlash && (lpPenaltyExternalActive || emptyFallbackActive);

            lpPenaltyRuntimeEffective = nextActive;
            lpPenaltyRuntimeStatus = $"effective={nextActive}, external={lpPenaltyExternalActive}, emptyFallback={emptyFallbackActive}, ratio={ratio:0.000}";

            if (lpPenaltyActiveRuntime == nextActive)
            {
                if (forceVisual)
                    ApplyLpPenaltyFlashVisual(true);
                return;
            }

            lpPenaltyActiveRuntime = nextActive;
            ApplyLpPenaltyFlashVisual(true);
        }

        public void SetQuickSlotIcon(int index, Sprite sprite, bool visible)
        {
            if (quickSlotIcons == null || index < 0 || index >= quickSlotIcons.Length)
            {
                Debug.LogWarning($"[SkyPrisonPlayerHUDView] SetQuickSlotIcon({index})：quickSlotIcons 是 null 或者 index 越界（数组长度={(quickSlotIcons != null ? quickSlotIcons.Length : -1)}）。");
                return;
            }

            Image icon = quickSlotIcons[index];
            if (icon == null)
            {
                Debug.LogWarning($"[SkyPrisonPlayerHUDView] SetQuickSlotIcon({index})：quickSlotIcons[{index}] 引用是 null，说明 Inspector 里这个数组元素没接上。");
                return;
            }

            icon.sprite = sprite;
            icon.enabled = visible && sprite != null;
            Debug.Log($"[SkyPrisonPlayerHUDView] SetQuickSlotIcon({index}) 成功：icon={icon.name} path={GetHierarchyPath(icon.transform)} sprite={(sprite != null ? sprite.name : "null")} enabled={icon.enabled} activeInHierarchy={icon.gameObject.activeInHierarchy}");
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "(null)";
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        /// <summary>ratio: 1=刚用、冷却刚开始，0=冷却结束。遮罩自己收缩/消失就行，
        /// 不用外部再额外判断"是不是0就隐藏"——fillAmount=0 的 Filled Image 本来就
        /// 不会画出任何东西。</summary>
        public void SetQuickSlotCooldown(int index, float ratio)
        {
            if (quickSlotCooldownFills == null || index < 0 || index >= quickSlotCooldownFills.Length)
                return;

            Image fill = quickSlotCooldownFills[index];
            if (fill == null)
                return;

            fill.fillAmount = Mathf.Clamp01(ratio);
        }

        public void SnapHp(float current, float max)
        {
            NotifyCombatVisibilityHp(current, max);
            float ratio = SafeRatio(current, max);
            hpTargetRatio = ratio;
            hpVisualRatio = ratio;
            hpDamageLagRatio = ratio;
            hpDamageLagHoldTimer = 0f;
            runtimeHpSnapCalls++;
            runtimeHpLagBranch = "SnapHp";
            UpdateHpRuntimeDiagnostics();
            ApplyHpVisual(true);

            if (hpLabel != null)
                hpLabel.text = compactLabels ? hpLabelTextValue : $"{hpLabelTextValue} {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        public void SnapLp(float current, float max)
        {
            NotifyCombatVisibilityLp(current, max);
            float ratio = SafeRatio(current, max);
            lpTargetRatio = ratio;
            lpVisualRatio = ratio;
            lpGhostRatio = ratio;
            ApplyLpEmptyPenaltyFallback(ratio);
            ApplyLpVisual(true);

            if (lpLabel != null)
                lpLabel.text = compactLabels ? lpLabelTextValue : $"{lpLabelTextValue} {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        public bool PreviewTick(float dt)
        {
            return TickBars(Mathf.Max(0f, dt));
        }

        private void Update()
        {
            float dt = useUnscaledTimeForBars ? Time.unscaledDeltaTime : Time.deltaTime;
            TickBars(dt);
            TickLoadRoll(dt);
        }

        private bool TickBars(float dt)
        {
            if (dt <= 0f)
                return false;

            runtimeHpTickCalls++;
            bool hpChanged = MoveRatio(ref hpVisualRatio, hpTargetRatio, hpFillChangeDuration, dt);

            if (enableHpDamageLag)
            {
                if (hpDamageLagHoldTimer > 0f)
                {
                    hpDamageLagHoldTimer = Mathf.Max(0f, hpDamageLagHoldTimer - dt);
                    runtimeHpLagBranch = "Hold";
                    runtimeHpHoldTicks++;
                    hpChanged = true;
                }
                else
                {
                    // Exact UnitOverheadUIView timing: damage reference catches up by speed, not by full-bar duration.
                    hpChanged |= MoveRatioBySpeed(ref hpDamageLagRatio, hpVisualRatio, Mathf.Max(0.01f, hpDamageLagCatchupDuration), dt);
                    runtimeHpLagBranch = "Catchup";
                    runtimeHpCatchupTicks++;
                }

                if (hpDamageLagRatio < hpVisualRatio)
                    hpDamageLagRatio = hpVisualRatio;
            }
            else
            {
                hpDamageLagRatio = hpVisualRatio;
                runtimeHpLagBranch = "Disabled";
            }

            UpdateHpRuntimeDiagnostics();

            bool lpChanged = MoveRatio(ref lpVisualRatio, lpTargetRatio, lpFillChangeDuration, dt);

            if (enableLpGhostFill)
            {
                lpChanged |= MoveRatio(ref lpGhostRatio, lpTargetRatio, Mathf.Max(0.01f, lpFillChangeDuration * 2.5f), dt);
                if (lpGhostRatio < lpVisualRatio)
                    lpGhostRatio = lpVisualRatio;
            }
            else
            {
                lpGhostRatio = lpVisualRatio;
            }

            if (hpChanged)
                ApplyHpVisual(false);
            if (lpChanged)
                ApplyLpVisual(false);

            // V10 build safety:
            // Re-evaluate every frame. In Build, LP may already be 0 before a visible SetLp call reaches
            // this view, or an external binder may keep sending a default false penalty state.
            RefreshLpPenaltyEffectiveState(lpTargetRatio, false);
            bool lpPenaltyFlashChanged = ApplyLpPenaltyFlashVisual(false);

            return hpChanged || lpChanged || lpPenaltyFlashChanged;
        }

        private void UpdateHpRuntimeDiagnostics()
        {
            runtimeHpTargetRatio = hpTargetRatio;
            runtimeHpVisualRatio = hpVisualRatio;
            runtimeHpDamageLagRatio = hpDamageLagRatio;
            runtimeHpDamageLagHoldTimer = hpDamageLagHoldTimer;
        }

        private void EnforceHpLayerOrder()
        {
            if (hpDamageLagMask != null && hpFillMask != null && hpDamageLagMask.parent == hpFillMask.parent)
            {
                hpDamageLagMask.SetAsLastSibling();
                hpFillMask.SetAsLastSibling();
            }
        }

        private void EnforceLpLayerOrder()
        {
            if (lpGhostMask != null && lpFillMask != null && lpGhostMask.parent == lpFillMask.parent)
            {
                lpGhostMask.SetAsLastSibling();
                lpFillMask.SetAsLastSibling();
            }
        }

        private void ApplyHpVisual(bool force)
        {
            EnforceHpLayerOrder();
            ResolveFillImagesFromCurrentMasks();

            ApplyBarVisual(hpFillMask, hpFillImage, hpFillMaxWidth, hpVisualRatio, true);

            if (hpDamageLagMask != null)
            {
                bool visible = enableHpDamageLag;
                hpDamageLagMask.gameObject.SetActive(visible);
                ApplyBarVisual(hpDamageLagMask, hpDamageLagImage, hpFillMaxWidth, visible ? hpDamageLagRatio : hpVisualRatio, visible);
            }
        }

        private void ApplyLpVisual(bool force)
        {
            EnforceLpLayerOrder();
            ResolveFillImagesFromCurrentMasks();

            ApplyBarVisual(lpFillMask, lpFillImage, lpFillMaxWidth, lpVisualRatio, true);

            if (lpGhostMask != null)
            {
                bool visible = enableLpGhostFill;
                lpGhostMask.gameObject.SetActive(visible);
                ApplyBarVisual(lpGhostMask, lpGhostImage, lpFillMaxWidth, visible ? lpGhostRatio : lpVisualRatio, visible);
            }

            ApplyLpPenaltyFlashVisual(force);
        }

        private void CaptureLpPenaltyBaseColors(bool force)
        {
            ResolveFillImagesFromCurrentMasks();

            if (!force && lpPenaltyBaseColorsCaptured)
                return;

            if (lpSlotBaseImage != null)
                lpPenaltyBaseSlotBaseColor = lpSlotBaseImage.color;
            if (lpBackImage != null)
                lpPenaltyBaseBackColor = lpBackImage.color;
            if (lpFillImage != null)
                lpPenaltyBaseFillColor = lpFillImage.color;
            if (lpGhostImage != null)
                lpPenaltyBaseGhostColor = lpGhostImage.color;
            if (lpLabel != null)
                lpPenaltyBaseLabelColor = lpLabel.color;

            lpPenaltyBaseColorsCaptured = true;
        }

        private bool ApplyLpPenaltyFlashVisual(bool force)
        {
            ResolveFillImagesFromCurrentMasks();

            if (!lpPenaltyBaseColorsCaptured)
                CaptureLpPenaltyBaseColors(false);

            float pulseMix = 0f;
            if (enableLpPenaltyFlash && lpPenaltyActiveRuntime)
            {
                float t = useUnscaledTimeForBars ? Time.unscaledTime : Time.time;
                float wave = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Max(0.1f, lpPenaltyFlashSpeed));
                pulseMix = Mathf.Lerp(lpPenaltyFlashMinMix, lpPenaltyFlashMaxMix, wave);
            }

            // 惩罚激活时只用 overlay 叠加红色，不改 slotBase/back/fill/ghost/label 颜色
            // 避免因为颜色混合导致背景变黑
            Color slotBaseTarget = lpPenaltyBaseSlotBaseColor;
            Color backTarget     = lpPenaltyBaseBackColor;
            Color fillTarget     = lpPenaltyBaseFillColor;
            Color ghostTarget    = lpPenaltyBaseGhostColor;
            Color labelTarget    = lpPenaltyBaseLabelColor;

            EnsureLpPenaltyFlashOverlay();

            Color overlayTarget = lpPenaltyFlashColor;
            overlayTarget.a = lpPenaltyActiveRuntime ? Mathf.Clamp01(pulseMix * lpPenaltyFlashColor.a) : 0f;

            bool changed = false;
            changed |= SetGraphicColorIfChanged(lpSlotBaseImage, slotBaseTarget, force);
            changed |= SetGraphicColorIfChanged(lpBackImage, backTarget, force);
            changed |= SetGraphicColorIfChanged(lpFillImage, fillTarget, force);
            changed |= SetGraphicColorIfChanged(lpGhostImage, ghostTarget, force);
            changed |= SetGraphicColorIfChanged(lpPenaltyFlashImage, overlayTarget, force);
            changed |= SetTMPColorIfChanged(lpLabel, labelTarget, force);

            if (lpPenaltyFlashImage != null)
                lpPenaltyFlashImage.enabled = lpPenaltyActiveRuntime && overlayTarget.a > 0.001f;

            return changed;
        }

        private void EnsureLpPenaltyFlashOverlay()
        {
            if (lpPenaltyFlashImage != null)
                return;

            ResolveFillImagesFromCurrentMasks();

            // 优先用 lpBackImage（条轨道）而非 lpSlotBaseImage（槽容器），
            // 防止 overlay 比 LP 填充条更大而露出多余色块。
            RectTransform referenceRect = null;
            if (lpBackImage != null)
                referenceRect = lpBackImage.rectTransform;
            else if (lpFillMask != null)
                referenceRect = lpFillMask.parent as RectTransform;
            else if (lpSlotBaseImage != null)
                referenceRect = lpSlotBaseImage.rectTransform;

            if (referenceRect == null || referenceRect.parent == null)
                return;

            Transform existing = referenceRect.parent.Find("__LPPenaltyFlashOverlay");
            bool isNew = existing == null;
            GameObject overlayObject = isNew
                ? new GameObject("__LPPenaltyFlashOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                : existing.gameObject;

            overlayObject.transform.SetParent(referenceRect.parent, false);
            overlayObject.layer = referenceRect.gameObject.layer;

            // 每次都强制对齐 referenceRect，防止旧尺寸残留
            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.anchorMin = referenceRect.anchorMin;
            overlayRect.anchorMax = referenceRect.anchorMax;
            overlayRect.pivot = referenceRect.pivot;
            overlayRect.anchoredPosition = referenceRect.anchoredPosition;
            overlayRect.sizeDelta = referenceRect.sizeDelta;
            overlayRect.localRotation = referenceRect.localRotation;
            overlayRect.localScale = referenceRect.localScale;

            lpPenaltyFlashImage = overlayObject.GetComponent<Image>();
            if (lpPenaltyFlashImage == null)
                lpPenaltyFlashImage = overlayObject.AddComponent<Image>();

            lpPenaltyFlashImage.raycastTarget = false;
            lpPenaltyFlashImage.color = new Color(lpPenaltyFlashColor.r, lpPenaltyFlashColor.g, lpPenaltyFlashColor.b, 0f);
            lpPenaltyFlashImage.enabled = false;

            overlayObject.transform.SetAsLastSibling();
        }

        private Color BlendPenaltyColor(Color baseColor, float mix)
        {
            Color target = Color.Lerp(baseColor, lpPenaltyFlashColor, Mathf.Clamp01(mix));
            target.a = baseColor.a;
            return target;
        }

        private static bool SetGraphicColorIfChanged(Graphic graphic, Color color, bool force)
        {
            if (graphic == null)
                return false;

            if (!force && ApproximatelyColor(graphic.color, color))
                return false;

            graphic.color = color;
            return true;
        }

        private static bool SetTMPColorIfChanged(TMP_Text text, Color color, bool force)
        {
            if (text == null)
                return false;

            if (!force && ApproximatelyColor(text.color, color))
                return false;

            text.color = color;
            return true;
        }

        private static bool ApproximatelyColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.002f
                && Mathf.Abs(a.g - b.g) < 0.002f
                && Mathf.Abs(a.b - b.b) < 0.002f
                && Mathf.Abs(a.a - b.a) < 0.002f;
        }

        private static bool MoveRatioBySpeed(ref float current, float target, float speed, float dt)
        {
            target = Mathf.Clamp01(target);
            if (Mathf.Approximately(current, target))
            {
                current = target;
                return false;
            }

            current = Mathf.MoveTowards(current, target, Mathf.Max(0.01f, speed) * Mathf.Max(0f, dt));
            return true;
        }

        private static bool MoveRatio(ref float current, float target, float duration, float dt)
        {
            target = Mathf.Clamp01(target);
            if (Mathf.Approximately(current, target))
            {
                current = target;
                return false;
            }

            if (duration <= 0.0001f)
            {
                current = target;
                return true;
            }

            float step = dt / duration;
            current = Mathf.MoveTowards(current, target, step);
            return true;
        }

        private static float SafeRatio(float current, float max)
        {
            if (max <= 0.0001f)
                return 0f;

            return Mathf.Clamp01(current / max);
        }

        private static void ApplyTextAspect(TMP_Text text, Vector2 aspect)
        {
            if (text == null)
                return;

            aspect.x = Mathf.Clamp(aspect.x <= 0f ? 1f : aspect.x, 0.35f, 2.00f);
            aspect.y = Mathf.Clamp(aspect.y <= 0f ? 1f : aspect.y, 0.35f, 2.00f);

            // Do not scale the RectTransform. We want Illustrator-like glyph deformation
            // while keeping the text box, anchor and coordinates stable.
            text.rectTransform.localScale = Vector3.one;

            SkyPrisonTMPGlyphAspect glyphAspect = text.GetComponent<SkyPrisonTMPGlyphAspect>();
            if (glyphAspect == null)
                glyphAspect = text.gameObject.AddComponent<SkyPrisonTMPGlyphAspect>();

            glyphAspect.SetAspect(aspect);
        }

        private static void ApplyBarVisual(RectTransform mask, Image fillImage, float maxWidth, float ratio, bool visible)
        {
            ratio = Mathf.Clamp01(ratio);
            maxWidth = Mathf.Max(1f, maxWidth);

            if (mask != null)
            {
                if (mask.gameObject.activeSelf != visible)
                    mask.gameObject.SetActive(visible);

                SetRectWidth(mask, maxWidth * ratio);
            }

            if (fillImage == null)
                return;

            if (fillImage.gameObject.activeSelf != visible)
                fillImage.gameObject.SetActive(visible);

            fillImage.enabled = visible;

            if (fillImage.type == Image.Type.Filled)
            {
                fillImage.fillAmount = ratio;
            }
            else
            {
                RectTransform fillRect = fillImage.rectTransform;
                if (fillRect != null)
                {
                    // Canonical rule:
                    // - Mask root width is the crop window.
                    // - Fill image inside a mask is always the full authored Back width.
                    // - Legacy sibling fill is directly cropped by width.
                    bool fillIsInsideMask = mask != null && fillRect.parent == mask;
                    SetRectWidth(fillRect, fillIsInsideMask ? maxWidth : maxWidth * ratio);
                }
            }
        }

        private static void SetRectWidth(RectTransform target, float width)
        {
            if (target == null)
                return;

            Vector2 size = target.sizeDelta;
            size.x = Mathf.Max(0f, width);
            target.sizeDelta = size;
        }
    }
}
