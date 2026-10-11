using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// RAID 局内右下角 AFP（适应系数积分）显示：仪表式 7 位滚轮数字。
    /// 「AFP」小标签在左，右边 7 位数字，前导 0 压暗、有效数字亮；数值变化时各位像里程表一样竖向滚动
    /// （个位连续滚，高位等低位走到进位才翻），获得时数字旁弹 +N，花费时反向滚并短暂偏红。
    /// 仅在 AFPRuntime.IsInSession 为 true 时可见（进本才有会话，主城不显示）。
    /// AFP 标签用 ToaHI（東亜重工）字体；数字用弹药同款 MRT-すくてらむ SDF。
    /// 自动挂载到 SkyPrisonRuntimeUIDriver.HudInstance，无需手动配置。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkyPrisonAFPHUD : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!Application.isPlaying) return;
            if (FindObjectOfType<SkyPrisonAFPHUD>() != null) return;
            var go = new GameObject("[SkyPrisonAFPHUD]");
            DontDestroyOnLoad(go);
            go.AddComponent<SkyPrisonAFPHUD>();
        }

        // 数字字体：和弹药数字相同
        private const string AmmoFontPath = "UI/HUD/MRT-すくてらむ SDF";
        // AFP 标签字体：ToaHI（東亜重工）——文件名含非 ASCII 字符，Editor 走 GUID，
        // Build 需手动将该 asset 复制到 Resources/UI/HUD/ToaHI SDF.asset。
        private const string ToaHIFontGuid        = "ea35a3e4c89493f44bb2e5cbe046505c";
        private const string ToaHIFontResourcePath = "UI/HUD/ToaHI SDF";

        // 布局：标签 + 7 个数字格。数字格宽按字体的数字宽度估一个固定值，保证标签位置永不偏移。
        private const int   Digits       = 7;
        private const float DigitFontSize = 84f;
        private const float LabelFontSize = DigitFontSize / 2.1f;   // 数字是标签的 2.1 倍（标签缩小，别抢数字）
        private const float CellW        = 52f;
        private const float CellH        = 96f;
        private const float LabelWidth   = 112f;
        // 标签和数字用同一条「基线」对齐（TMP 的 Baseline 对齐把文字基线放在文字框的垂直中心）：
        // 基线离面板底边 BaselineY；数字格内的文字框中心因此比格子中心低 (CellH/2 - BaselineY)。
        private const float BaselineY    = 18f;
        private const float BaselineShift = BaselineY - CellH * 0.5f;
        private const float PanelWidth   = LabelWidth + Digits * CellW;
        private const float PanelHeight  = CellH;
        // 位置：贴在血条区（PlayerStatusArea）右侧，水平留一点间距，底边对齐血条区底边。血条区取不到才退回屏幕下方正中偏右。
        private const float GapToStatus  = 270f;   // 血条区右边缘到数字面板左边缘的水平距离（落在血条右侧、武器格左侧之间的空档里）
        private const float BottomInset  = 22f;   // 面板底边比血条区底边高多少（数字比之前大，基线又偏下，所以整体再往下放）

        // 颜色：有效数字白色，前导 0 灰色；获得时数字短暂泛冷绿、花费时泛红，+N 弹字用冷绿
        private static readonly Color DigitColor = Color.white;
        private static readonly Color ZeroColor  = new Color(0.52f, 0.54f, 0.57f, 0.9f);   // 前导 0 的灰色
        private static readonly Color GainColor  = new Color(0.35f, 1f, 0.7f, 1f);          // 冷绿
        private static readonly Color SpendColor = new Color(1f, 0.45f, 0.45f, 1f);        // 花费时的淡红，和背包 NEW 标记同一个颜色
        private const float LabelAlpha = 1f;   // 「AFP」标签纯白，不透明

        // 动画
        private const float MinRollSpeed = 90f;        // 单位 / 秒（个位的最小滚动速度）
        private const float RollSpeedPerDiff = 3f;     // 差得越多滚得越快：约 0.3 秒滚完
        private const float PulseSeconds = 0.35f;
        private const float PopupSeconds = 0.9f, PopupRise = 38f;

        private static TMP_FontAsset _numberFontCache;
        private static bool _numberFontLoadAttempted;
        private static TMP_FontAsset _labelFontCache;
        private static bool _labelFontLoadAttempted;
        private static readonly string[] DigitStr = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

        private RectTransform _panel;
        private TMP_Text _labelText;
        private TMP_Text[] _a, _b;          // 每一位两层：当前数字 / 即将滚进来的数字
        private RectTransform[] _aRt, _bRt;
        private int[] _lastA, _lastB;

        private float _target, _shown;
        private bool _needSnap = true;
        private float _gainPulse, _spendPulse;

        private TMP_Text[] _popups; private float[] _popupT;
        private int _popupNext;

        private float _searchCooldown;
        private const float SearchInterval = 1f;

        // ── Unity 生命周期 ────────────────────────────────────────────────────

        private void OnEnable()  => AFPRuntime.OnAFPChanged += HandleAFPChanged;
        private void OnDisable() => AFPRuntime.OnAFPChanged -= HandleAFPChanged;

        private void Update()
        {
            if (_panel == null)
            {
                _searchCooldown -= Time.unscaledDeltaTime;
                if (_searchCooldown <= 0f) { TryBuildContent(); _searchCooldown = SearchInterval; }
                return;
            }
            if (!_panel.gameObject.activeSelf) return;

            float dt = Time.unscaledDeltaTime;
            _placeCooldown -= dt;                                   // 血条区会随 HUD 缩放 / 布局变化，隔半秒重新贴一次
            if (_placeCooldown <= 0f) { PlacePanel(); _placeCooldown = 0.5f; }
            bool animating = false;

            float diff = _target - _shown;
            if (Mathf.Abs(diff) > 0.001f)
            {
                _shown = Mathf.MoveTowards(_shown, _target, Mathf.Max(MinRollSpeed, Mathf.Abs(diff) * RollSpeedPerDiff) * dt);
                animating = true;
            }
            if (_gainPulse > 0f) { _gainPulse = Mathf.Max(0f, _gainPulse - dt / PulseSeconds); animating = true; }
            if (_spendPulse > 0f) { _spendPulse = Mathf.Max(0f, _spendPulse - dt / PulseSeconds); animating = true; }

            if (animating || _dirty) { ApplyDigits(_shown); _dirty = false; }
            UpdatePopups(dt);
        }
        private bool _dirty = true;

        // ── 构建 ─────────────────────────────────────────────────────────────

        private void TryBuildContent()
        {
            var driver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
            GameObject hud = driver != null ? driver.HudInstance : null;
            if (hud == null) return;

            TMP_FontAsset labelFont  = ResolveLabelFont();
            TMP_FontAsset numberFont = ResolveNumberFont();
            int uiLayer = LayerMask.NameToLayer("UI");

            var panelGo = new GameObject("AFPPanel", typeof(RectTransform));
            panelGo.transform.SetParent(hud.transform, false);
            SetLayerRecursive(panelGo, uiLayer);

            _panel = panelGo.GetComponent<RectTransform>();
            _hudRt = hud.transform as RectTransform;
            _panel.anchorMin        = new Vector2(0f, 0f);
            _panel.anchorMax        = new Vector2(0f, 0f);
            _panel.pivot            = new Vector2(0f, 0f);
            _panel.sizeDelta        = new Vector2(PanelWidth, PanelHeight);
            _statusArea = FindStatusArea(hud.transform);
            PlacePanel();

            // 描边材质：数字和标签各一份，所有位共用（不再每个文字各复制一份材质）
            Material numberMat = MakeOutlineMaterial(numberFont);
            Material labelMat  = MakeOutlineMaterial(labelFont);

            // AFP 标签：灰白小字，左侧固定宽度，垂直居中，左对齐
            _labelText = MakeTMP(panelGo.transform, "AFPLabel", labelFont, LabelFontSize,
                TextAlignmentOptions.BaselineLeft, new Color(1f, 1f, 1f, LabelAlpha), uiLayer);
            if (labelMat != null) _labelText.fontSharedMaterial = labelMat;
            _labelText.enableWordWrapping = false; _labelText.overflowMode = TextOverflowModes.Overflow;
            var lr = _labelText.rectTransform;
            lr.anchorMin        = new Vector2(0f, 0f);
            lr.anchorMax        = new Vector2(0f, 0f);
            lr.pivot            = new Vector2(0f, 0.5f);
            lr.anchoredPosition = new Vector2(0f, BaselineY);     // 文字框中心就是基线所在的高度
            lr.sizeDelta        = new Vector2(LabelWidth, 60f);
            _labelText.text = "AFP";

            // 7 个数字格：每格一个 RectMask2D 裁出一个字的高度，里面两层文字上下错开，滚动时一层滑出一层滑入。
            _a = new TMP_Text[Digits]; _b = new TMP_Text[Digits];
            _aRt = new RectTransform[Digits]; _bRt = new RectTransform[Digits];
            _lastA = new int[Digits]; _lastB = new int[Digits];
            for (int i = 0; i < Digits; i++)
            {
                _lastA[i] = _lastB[i] = -1;
                var cell = new GameObject("Digit" + i, typeof(RectTransform), typeof(RectMask2D));
                cell.transform.SetParent(panelGo.transform, false); SetLayerRecursive(cell, uiLayer);
                var cr = (RectTransform)cell.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(0f, 0.5f); cr.pivot = new Vector2(0f, 0.5f);
                cr.anchoredPosition = new Vector2(LabelWidth + (Digits - 1 - i) * CellW, 0f);   // i=0 是个位，在最右
                cr.sizeDelta = new Vector2(CellW, CellH);
                _a[i] = MakeDigit(cell.transform, "A", numberFont, numberMat, uiLayer, out _aRt[i]);
                _b[i] = MakeDigit(cell.transform, "B", numberFont, numberMat, uiLayer, out _bRt[i]);
            }

            // +N 弹出：3 个轮换使用，右对齐在数字区右端上方
            _popups = new TMP_Text[3]; _popupT = new float[3];
            for (int i = 0; i < _popups.Length; i++)
            {
                // 名字以 CleanFG_ 开头：色差模块会把它留在显示层（不进离屏 RT），+N 不会被 RT 边界裁掉，也不带色差
                var p = MakeTMP(panelGo.transform, "CleanFG_AFPPopup" + i, numberFont, 38f, TextAlignmentOptions.Right, GainColor, uiLayer);
                if (numberMat != null) p.fontSharedMaterial = numberMat;
                var pr = p.rectTransform;
                pr.anchorMin = pr.anchorMax = new Vector2(1f, 1f); pr.pivot = new Vector2(1f, 0f);
                pr.sizeDelta = new Vector2(160f, 32f); pr.anchoredPosition = new Vector2(0f, 2f);
                p.gameObject.SetActive(false); _popups[i] = p; _popupT[i] = -1f;
            }

            // 和 HP / LP 一样的色差：借血条区那个模块渲染器的源材质和混合模式，给 AFP 面板也挂一个同样的。
            // 以后调 HP/LP 的色差参数，AFP 自动跟着变。找不到就不加（AFP 仍然正常显示，只是没有色差）。
            SkyPrisonHUDModulePostProcessRenderer_V87 template = null;
            foreach (var r in hud.GetComponentsInChildren<SkyPrisonHUDModulePostProcessRenderer_V87>(true))
            {
                if (r == null || r.gameObject == panelGo || r.SourceMaterial == null) continue;
                if (template == null || r.name == "PlayerStatusArea") template = r;
                if (r.name == "PlayerStatusArea") break;
            }
            if (template != null)
                SkyPrisonHUDModulePostProcessRenderer_V87.Attach(panelGo, template.SourceMaterial, template.BlendMode);

            Refresh();
        }

        private TMP_Text MakeDigit(Transform parent, string name, TMP_FontAsset font, Material mat, int layer, out RectTransform rt)
        {
            var t = MakeTMP(parent, name, font, DigitFontSize, TextAlignmentOptions.Baseline, DigitColor, layer);
            if (mat != null) t.fontSharedMaterial = mat;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
            rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CellW, CellH); rt.anchoredPosition = new Vector2(0f, BaselineShift);
            return t;
        }

        // ── 定位：贴在血条区右侧 ───────────────────────────────────────────────

        private RectTransform _hudRt, _statusArea;
        private float _placeCooldown;

        private static RectTransform FindStatusArea(Transform hud)
        {
            foreach (var rt in hud.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == "PlayerStatusArea") return rt;
            return null;
        }

        private void PlacePanel()
        {
            if (_panel == null || _hudRt == null) return;
            if (_statusArea == null) _statusArea = FindStatusArea(_hudRt);
            Vector2 pos;
            if (_statusArea != null)
            {
                // 血条区右下角（世界坐标）→ HUD 根的本地坐标 → 以 HUD 根左下角为原点的锚点坐标
                var c = new Vector3[4]; _statusArea.GetWorldCorners(c);
                Vector3 local = _hudRt.InverseTransformPoint(c[3]);       // c[3] = 右下
                Rect r = _hudRt.rect;
                pos = new Vector2(local.x - r.xMin + GapToStatus, local.y - r.yMin + BottomInset);
            }
            else
            {   // 没找到血条区：屏幕下方正中偏右
                Rect r = _hudRt.rect;
                pos = new Vector2(r.width * 0.5f + 640f, BottomInset + 30f);
            }
            if (_panel.anchoredPosition != pos) _panel.anchoredPosition = pos;
        }

        // ── 数据 → 画面 ───────────────────────────────────────────────────────

        private void HandleAFPChanged(int value)
        {
            bool active = AFPRuntime.Instance != null && AFPRuntime.Instance.IsInSession;
            float old = _target; _target = value;
            if (!active || _needSnap)
            {
                _shown = _target; _dirty = true;      // 刚进本 / 刚显示出来：直接就位，不滚、不弹
                _needSnap = !active;
            }
            else
            {
                int delta = value - Mathf.RoundToInt(old);
                if (delta > 0) { _gainPulse = 1f; _spendPulse = 0f; SpawnPopup("+" + delta); }
                else if (delta < 0) { _spendPulse = 1f; _gainPulse = 0f; }
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_panel == null) return;

            bool active = AFPRuntime.Instance != null && AFPRuntime.Instance.IsInSession;
            if (_panel.gameObject.activeSelf != active) _panel.gameObject.SetActive(active);
            if (!active) { _needSnap = true; return; }

            if (_needSnap)
            {
                _target = AFPRuntime.Instance.CurrentAFP; _shown = _target; _needSnap = false;
            }
            _dirty = true;
        }

        private void ApplyDigits(float v)
        {
            if (_a == null) return;
            v = Mathf.Max(0f, v);
            for (int i = 0; i < Digits; i++)
            {
                float x = v / Mathf.Pow(10f, i);
                float fl = Mathf.Floor(x), fr = x - fl;
                // 个位连续滚；高位在低位快要进位时才翻（fr^6：大部分时间停着，临近进位才快速翻过去）
                // 高位只在低位真正进位的最后 1 个单位里滚；之前 Pow(frac,6) 会让整数 94 的百位卡在滚了 69% 的位置
                float pw = Mathf.Pow(10f, i);
                float f = i == 0 ? fr : Mathf.Clamp01((v % pw) - (pw - 1f));
                int cur = (int)(fl % 10f), next = (cur + 1) % 10;
                if (_lastA[i] != cur) { _a[i].text = DigitStr[cur]; _lastA[i] = cur; }
                if (_lastB[i] != next) { _b[i].text = DigitStr[next]; _lastB[i] = next; }
                _aRt[i].anchoredPosition = new Vector2(0f, BaselineShift + f * CellH);
                _bRt[i].anchoredPosition = new Vector2(0f, BaselineShift + (f - 1f) * CellH);

                // 有效数字白色，前导 0 灰色；刚要出现的新一位随着滚进来从灰逐渐变白
                Color baseCol = (i == 0 || fl > 0f) ? DigitColor : Color.Lerp(ZeroColor, DigitColor, f);
                // 获得时数字短暂泛冷绿，花费时泛红（只影响有效数字，前导 0 保持灰）
                bool significant = i == 0 || fl > 0f || f > 0.5f;
                Color flash = _gainPulse > 0f ? Color.Lerp(DigitColor, GainColor, _gainPulse)
                            : _spendPulse > 0f ? Color.Lerp(DigitColor, SpendColor, _spendPulse) : DigitColor;
                Color ca = significant ? Color.Lerp(baseCol, flash, (_gainPulse > 0f || _spendPulse > 0f) ? 1f : 0f) : baseCol;
                _a[i].color = ca;
                // B 层是即将滚进来的那个数字：亮暗按「滚完之后」的状态算
                _b[i].color = significant ? ca : ZeroColor;
            }
        }

        // ── +N 弹出 ───────────────────────────────────────────────────────────

        private void SpawnPopup(string text)
        {
            if (_popups == null) return;
            int i = _popupNext; _popupNext = (_popupNext + 1) % _popups.Length;
            _popups[i].text = text; _popups[i].gameObject.SetActive(true); _popupT[i] = 0f;
            _popups[i].rectTransform.anchoredPosition = new Vector2(0f, 2f);
            _popups[i].color = GainColor;
        }

        private void UpdatePopups(float dt)
        {
            if (_popups == null) return;
            for (int i = 0; i < _popups.Length; i++)
            {
                if (_popupT[i] < 0f) continue;
                _popupT[i] += dt;
                float k = _popupT[i] / PopupSeconds;
                if (k >= 1f) { _popups[i].gameObject.SetActive(false); _popupT[i] = -1f; continue; }
                float rise = 1f - (1f - k) * (1f - k);                 // 先快后慢往上飘
                _popups[i].rectTransform.anchoredPosition = new Vector2(0f, 2f + rise * PopupRise);
                var c = GainColor; c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f; _popups[i].color = c;   // 后 40% 淡出
            }
        }

        // ── 工具 ─────────────────────────────────────────────────────────────

        // 数字字体：MRT-すくてらむ SDF（与弹药数字相同）
        private static TMP_FontAsset ResolveNumberFont()
        {
            if (!_numberFontLoadAttempted)
            {
                _numberFontLoadAttempted = true;
                _numberFontCache = Resources.Load<TMP_FontAsset>(AmmoFontPath);
                if (_numberFontCache == null)
                    Debug.LogWarning($"[SkyPrisonAFPHUD] 找不到数字字体，期望路径：Resources/{AmmoFontPath}.asset");
            }
            return _numberFontCache;
        }

        // AFP 标签字体：ToaHI（東亜重工）
        // Editor：AssetDatabase by GUID（文件名含非 ASCII，无法用 Resources.Load 按名加载）
        // Build：需将该 asset 复制到 Resources/UI/HUD/ToaHI SDF.asset
        private static TMP_FontAsset ResolveLabelFont()
        {
            if (!_labelFontLoadAttempted)
            {
                _labelFontLoadAttempted = true;
#if UNITY_EDITOR
                string path = AssetDatabase.GUIDToAssetPath(ToaHIFontGuid);
                if (!string.IsNullOrEmpty(path))
                    _labelFontCache = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
#endif
                if (_labelFontCache == null)
                    _labelFontCache = Resources.Load<TMP_FontAsset>(ToaHIFontResourcePath);
                if (_labelFontCache == null)
                    Debug.LogWarning($"[SkyPrisonAFPHUD] 找不到 ToaHI 字体（GUID:{ToaHIFontGuid}），Build 时请将其复制到 Resources/{ToaHIFontResourcePath}.asset");
            }
            return _labelFontCache;
        }

        private static TMP_Text MakeTMP(Transform parent, string name, TMP_FontAsset font,
            float size, TextAlignmentOptions align, Color color, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SetLayerRecursive(go, layer);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize      = size;
            tmp.alignment     = align;
            tmp.color         = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        // 与 SkyPrisonWeaponSwitchHUD.ApplyAmmoTextUnderlayGlow 相同的黑描边方案，做成一份共用材质。
        // OUTLINE_ON 关键字必须显式开启，否则 shader 分支不编译，_OutlineWidth 无效。
        private static Material MakeOutlineMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;
            Material src = font.material;
            if (src == null || !src.HasProperty("_OutlineWidth")) return null;
            var mat = new Material(src);
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat("_OutlineWidth", 0.133f);
            mat.SetColor("_OutlineColor", Color.black);
            if (mat.HasProperty("_OutlineSoftness"))
                mat.SetFloat("_OutlineSoftness", 0.05f);
            return mat;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
