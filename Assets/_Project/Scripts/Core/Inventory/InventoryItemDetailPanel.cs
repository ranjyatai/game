using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 物品详情面板：动态构建 UI 子树，黑白磨砂背景，展开/收缩动画，
    /// 贴在背包面板右侧，高度与背包面板一致。
    /// 由 InventoryItemDetailController 驱动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryItemDetailPanel : MonoBehaviour
    {
        private const float PanelW   = 640f; // 原560——日文换行太多显得文案很长，加宽减少换行次数
        // 英文文案通常比中文长不少（同样的意思要用更多单词）——固定高度不管调多大，
        // 总会有某条描述文字更长，还是会溢出（描述文字 verticalOverflow=Overflow，
        // 不裁切，超出的部分会一路往下画，盖到底部"剩N"库存数字上）。改成动态：
        // 每次 Populate 时按实际描述文字量测出所需高度，这个常量降级成"最小高度"
        // （中文这种短描述的下限），实际用的高度取 MinPanelH 和量出来的高度中更大的那个，
        // 见 _dynamicPanelH。
        private const float MinPanelH = 720f;
        private const float MaxPanelH = 1200f; // 极端长文的兜底上限，避免面板顶穿屏幕
        private float _dynamicPanelH = MinPanelH;
        private const float PadX     = 30f;
        private const float PadY     = 30f;
        private const float IconSize = 170f; // 原148——面板加宽到640后图标显得偏小，按比例放大
        private const float HGap     = 22f;
        private const float VGap     = 19f;
        private const float AnimDur  = 0.14f;
        private const float RightGap = 6f;

        private const int MaxPriceRows = 3;
        private const float PriceRowH   = 36f;
        private const float PriceIconSz = 28f;
        private const float PriceAreaH  = MaxPriceRows * PriceRowH + VGap;
        private static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);

        private RectTransform _box;
        private RawImage      _blur;
        private Image         _iconImg;
        private TMP_Text      _nameText;
        private TMP_Text      _tagText;
        private Text          _descText; // 用 Legacy Text + OS 字体，避免 TMP Static Atlas 缺字

        // 价格行：图标 + 数字
        private readonly Image[]    _priceIcons = new Image[MaxPriceRows];
        private readonly TMP_Text[] _priceTexts = new TMP_Text[MaxPriceRows];
        private readonly GameObject[] _priceRows = new GameObject[MaxPriceRows];

        // 组上限
        private TMP_Text _stackLimitText;

        private bool      _built;
        private bool      _open;
        private Coroutine _anim;

        private RectTransform _inventoryPanel;
        private RectTransform _contentRt;
        private TMP_FontAsset _font;

        public bool IsOpen => _open;

        // ── 公开接口 ──────────────────────────────────────────────────────

        public void Show(ItemDefinition def, InventoryItemEntry entry, RectTransform inventoryPanel)
        {
            if (def == null) { Hide(); return; }
            _inventoryPanel = inventoryPanel;
            EnsurePanel();
            Populate(def, entry);
            PositionPanel();
            if (_open) return;
            _open = true;
            StopAnim();
            _box.gameObject.SetActive(true);
            _box.localScale = new Vector3(1f, 0f, 1f);
            _anim = StartCoroutine(ExpandIn());
        }

        public void Hide()
        {
            if (!_open || _box == null) return;
            _open = false;
            StopAnim();
            _anim = StartCoroutine(CompressOut());
        }

        public void UpdateContent(ItemDefinition def, InventoryItemEntry entry)
        {
            if (!_open || !_built) return;
            Populate(def, entry);
        }

        // ── 每帧跟随背包面板位置 ──────────────────────────────────────────

        private void LateUpdate()
        {
            if (_open && _box != null && _inventoryPanel != null)
                PositionPanel();
        }

        // ── 定位 ──────────────────────────────────────────────────────────

        private void PositionPanel()
        {
            if (_box == null || _inventoryPanel == null) return;

            var corners = new Vector3[4];
            _inventoryPanel.GetWorldCorners(corners);
            // Overlay Canvas: 世界坐标=屏幕像素  0=左下 1=左上 2=右上 3=右下
            float rightEdge  = corners[2].x;
            float leftEdge   = corners[0].x;
            float centerY    = (corners[0].y + corners[1].y) * 0.5f; // 背包垂直中心对齐

            // PanelW 是画布本地单位（给 sizeDelta 用的），leftEdge/rightEdge/Screen.width
            // 是真实屏幕像素——两者只有 scaleFactor=1 时才刚好相等。之前直接拿 PanelW
            // 当像素用，往右展开那条分支凑巧没用到 PanelW 算位置所以看不出问题，往左
            // 展开这条用 PanelW 算目标点，scaleFactor 不是 1 时算出来的位置就偏了一大截
            // （偏移量 = PanelW*(1-scaleFactor)），表现为"往左弹出时跟背包窗口隔老远"。
            RectTransform parent = (RectTransform)_box.parent;
            Canvas panelCanvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            float scaleFactor = panelCanvas != null && panelCanvas.scaleFactor > 0f ? panelCanvas.scaleFactor : 1f;
            float panelWPixels = PanelW * scaleFactor;
            float panelHPixels = _dynamicPanelH * scaleFactor;

            // 2026-07-23：原本只判断"右边到屏幕右边缘还有没有空间"——这对贴着屏幕右侧
            // 摆的背包成立(右边永远不够，稳定回退到左边)，但仓库贴着屏幕左侧摆，右边到
            // 屏幕右边缘明明还有一大截空间，算法会选择往右展开——可那片"空间"往往正是
            // 背包窗口自己占的地方，两个窗口的物品详情面板因此会糊到背包窗口上，跟正在
            // 悬停的仓库格子完全对不上。改成同时查一下候选位置会不会跟"其它已注册的
            // 悬浮窗"(拖拽防重叠用的同一张登记表)重叠，不只是查会不会超出屏幕。
            Rect rightCandidate = new Rect(rightEdge + RightGap, centerY - panelHPixels * 0.5f, panelWPixels, panelHPixels);
            Rect leftCandidate  = new Rect(leftEdge - RightGap - panelWPixels, centerY - panelHPixels * 0.5f, panelWPixels, panelHPixels);

            bool rightFitsScreen = rightEdge + RightGap + panelWPixels <= Screen.width;
            bool leftFitsScreen  = leftEdge - RightGap - panelWPixels >= 0f;
            bool rightOverlaps = SkyPrisonWindowOverlapGuard.WouldOverlapOthers(_box, rightCandidate);
            bool leftOverlaps  = SkyPrisonWindowOverlapGuard.WouldOverlapOthers(_box, leftCandidate);

            bool useRight = rightFitsScreen && !rightOverlaps;
            bool useLeft  = !useRight && leftFitsScreen && !leftOverlaps;
            // 两边都不干净(比如两个悬浮窗中间的空隙塞不下)：退回原来的"哪边更靠屏幕内"
            // 判断，好歹保证面板完整可见，允许跟别的窗口有一点重叠好过整个被裁掉。
            bool fitRight = useRight || (!useLeft && rightFitsScreen);

            // pivot.y = 0.5，所以锚点对应面板垂直中心
            Vector2 screenAnchor = fitRight
                ? new Vector2(rightEdge + RightGap, centerY)
                : new Vector2(leftEdge - RightGap - panelWPixels, centerY);

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screenAnchor, null, out local);

            _box.sizeDelta        = new Vector2(PanelW, _dynamicPanelH);
            _box.anchoredPosition = local;

            UpdateBlur();
        }

        // ── 动画 ──────────────────────────────────────────────────────────

        private IEnumerator ExpandIn()
        {
            float t = 0f;
            while (t < AnimDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / AnimDur);
                float ease = k * (2f - k); // ease-out
                _box.localScale = new Vector3(1f, ease, 1f);
                UpdateBlur();
                yield return null;
            }
            _box.localScale = Vector3.one;
            UpdateBlur();
        }

        private IEnumerator CompressOut()
        {
            float t = AnimDur;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / AnimDur);
                _box.localScale = new Vector3(1f, k * k, 1f); // ease-in
                UpdateBlur();
                yield return null;
            }
            _box.localScale = Vector3.zero;
            _box.gameObject.SetActive(false);
        }

        private void StopAnim()
        {
            if (_anim != null) { StopCoroutine(_anim); _anim = null; }
        }

        // ── 内容填充 ──────────────────────────────────────────────────────

        private void Populate(ItemDefinition def, InventoryItemEntry entry)
        {
            if (_iconImg != null)
            {
                _iconImg.sprite  = def.icon;
                _iconImg.enabled = def.icon != null;
            }
            if (_nameText != null)
            {
                string name = string.IsNullOrEmpty(def.displayName) ? def.itemKey : def.GetLocalizedDisplayName();
                if (def.itemLevel == 9)
                {
                    // LV9：纯文本，由 RainbowTextEffect 组件逐帧着色
                    _nameText.text  = name;
                    _nameText.color = Color.white;
                    var rainbow = _nameText.GetComponent<RainbowTextEffect>();
                    if (rainbow == null) rainbow = _nameText.gameObject.AddComponent<RainbowTextEffect>();
                    rainbow.enabled = true;
                }
                else
                {
                    // 关闭彩虹效果（如果之前是 LV9 切换过来的）
                    var rainbow = _nameText.GetComponent<RainbowTextEffect>();
                    if (rainbow != null) rainbow.enabled = false;
                    _nameText.color = Color.white;
                    string nameHex = QualityHex(def.itemLevel);
                    _nameText.text = $"<color=#{nameHex}>{name}</color>";
                }
                // TMP 内置的 enableAutoSizing 在这套"运行时现造 UI、Populate 时机不走
                // 标准布局流程"的场景下试了三轮都没有实际生效（在 Inspector 里能看到
                // Auto Size 是勾着的，但字号一直卡在 Max 不往下降）。不再依赖它，
                // 改成手动量：当前字号下量出这行字的实际宽度，超出可用宽度就一档一档
                // 往下降字号，直到测量结果真正塞进去为止——直接用测量结果驱动，
                // 不经过 TMP 自动缩放那条不知道卡在哪的内部路径，一定能生效。
                ShrinkNameToFit(_nameText.rectTransform.rect.width);
            }
            if (_tagText != null)
            {
                _tagText.color = Color.white;
                if (def.itemLevel == 9)
                {
                    // "材料  Lv.9  可使用"：可见字符顺序 材(0)料(1)L(2)v(3).(4)9(5)可(6)使(7)用(8)
                    // 只对 Lv.9 对应的可见字符 2-5 上色
                    string tagLine = BuildTagLine(def);
                    _tagText.text = tagLine;
                    var rb = _tagText.GetComponent<RainbowTextEffect>();
                    if (rb == null) rb = _tagText.gameObject.AddComponent<RainbowTextEffect>();
                    rb.SetVisibleRange(2, 5, new UnityEngine.Color32(180, 178, 169, 255));
                    rb.enabled = true;
                }
                else
                {
                    _tagText.text = BuildTagLine(def);
                    var rb = _tagText.GetComponent<RainbowTextEffect>();
                    if (rb != null) rb.enabled = false;
                }
            }
            if (_descText != null)
            {
                _descText.text = string.IsNullOrEmpty(def.description) ? "暂无描述。" : def.GetLocalizedDescription();
                UpdateDynamicPanelHeight();
            }

            PopulateStackLimit(def);
            PopulatePrices(def);
            PopulateEquipmentInfo(def, entry);
            UpdateDyeSwatches(def, entry);
            ApplyDynamicPanelHeight();
        }

        private void PopulateStackLimit(ItemDefinition def)
        {
            if (_stackLimitText == null) return;
            string label = GetLocalized("ui_item_stack_limit", "组上限");
            _stackLimitText.text = $"{label}  <color=#42EB8E>{def.maxStackCount}</color>";
        }

        // ── 装备专属信息（耐久 / 改装槽 / 词条）────────────────────────────
        // 不再另开一个悬浮在主面板下方的独立窗口——直接嵌进主面板内容区，紧贴在
        // 描述文字下方。不加底色、不加边框、不加彩色——一条白色横线分割，文字统一
        // 白色，只有耐久度快耗尽/耗尽时才用淡红色提醒，其它一律不特殊上色。

        private GameObject _equipSectionRoot;   // 整个装备信息区的根节点，每次 Populate 整块重建
        private float      _equipSectionH;      // 供 ApplyDynamicPanelHeight 计算总面板高度

        private const float EquipLineH        = 34f; // 配合下面 27pt 字号（跟描述文字同尺寸）调高的行距
        private const float EquipFontSize     = 27f; // 跟 _descText（暂无描述那行）同字号
        private const float EquipPadX         = 0f;
        private const float EquipSectionPadV  = 10f;
        private const float EquipSectionGap   = 16f; // 跟描述文字之间的间距
        private static readonly Color LowDurabilityRed = new Color(1f, 0.55f, 0.55f, 1f);

        private void PopulateEquipmentInfo(ItemDefinition def, InventoryItemEntry entry)
        {
            if (_equipSectionRoot != null) { Destroy(_equipSectionRoot); _equipSectionRoot = null; }
            _equipSectionH = 0f;

            if (def == null || !def.IsEquipmentItem || _contentRt == null || def.equipment == null)
                return;

            var eq = def.equipment;

            // 每行拆成"标签"+"数值"两列（不再是拼成一整串的单个文本），数值列统一
            // 右对齐——跟角色面板的属性行是同一套排版语言。value=null 的行（改装槽的
            // 分组标题/词条名）只有左边一列文字，没有右对齐的数值。
            var rows = new List<(string label, string value, Color color)>();

            // 耐久度——正常白色，快耗尽（<30%）或耗尽时才用淡红色提醒
            if (eq.maxDurability > 0)
            {
                int cur = entry?.currentDurability ?? eq.maxDurability;
                Color durColor = cur <= 0 || cur < eq.maxDurability * 0.3f
                    ? LowDurabilityRed : Color.white;
                rows.Add((GetLocalized("ui_item_durability", "耐久"), $"{cur} / {eq.maxDurability}", durColor));

                // 耐久是装备的损耗状态，不是这件装备带来的收益——跟下面的属性加成
                // 隔开一点距离，别让人以为它跟攻击力/暴击率是同一类"加成"信息。
                bool hasMoreRows = (eq.statBonuses != null && eq.statBonuses.Exists(b => b != null && !string.IsNullOrEmpty(b.parameterKey) && b.value != 0f))
                    || (eq.modSlots != null && eq.modSlots.Count > 0);
                if (hasMoreRows)
                    rows.Add(("", null, Color.white));
            }

            // 属性加成——跟角色核心属性共用同一套 BattleParameterDatabase key，显示名
            // 复用角色面板已有的本地化条目（GetStatBonusLabel），保证跟角色面板里
            // 看到的属性名字（"攻击"/"暴击率"/"负暴击率"……）是同一套说法，不会出现
            // 装备详情里写"atk"、角色面板里写"攻击"这种对不上的情况。
            if (eq.statBonuses != null)
            {
                foreach (var bonus in eq.statBonuses)
                {
                    if (bonus == null || string.IsNullOrEmpty(bonus.parameterKey) || bonus.value == 0f)
                        continue;
                    string label = GetStatBonusLabel(bonus.parameterKey);
                    string sign = bonus.value > 0f ? "+" : "";
                    rows.Add((label, $"{sign}{bonus.value:F0}", Color.white));
                }
            }

            // 改装槽位
            if (eq.modSlots != null && eq.modSlots.Count > 0)
            {
                rows.Add((GetLocalized("ui_item_mod_slots_header", "── 改装槽 ──"), null, Color.white));
                string emptyLabel        = GetLocalized("ui_item_mod_slot_empty", "空置");
                string unidentifiedLabel = GetLocalized("ui_item_mod_unidentified", "??? 未鉴定");

                foreach (var slot in eq.modSlots)
                {
                    InstalledModEntry installed = entry?.GetInstalledMod(slot.slotKey);
                    if (installed == null)
                    {
                        rows.Add(($"[{slot.displayName}]", emptyLabel, Color.white));
                    }
                    else if (!installed.isIdentified)
                    {
                        rows.Add(($"[{slot.displayName}]", unidentifiedLabel, Color.white));
                    }
                    else
                    {
                        rows.Add(($"[{slot.displayName}]", installed.modItemKey, Color.white));
                        foreach (var bonus in installed.bonuses)
                        {
                            string val = bonus.isPercent ? $"+{bonus.value:P0}" : $"+{bonus.value:F0}";
                            rows.Add(($"    {bonus.displayName}", val, Color.white));
                        }
                    }
                }
            }

            const float dividerH = 1f;
            float sectionH = dividerH + rows.Count * EquipLineH + EquipSectionPadV * 2f;
            _equipSectionH = sectionH;

            float startY = -(IconSize + VGap * 2f + 1f) - _measuredDescHeight - EquipSectionGap;

            _equipSectionRoot = new GameObject("EquipSection", typeof(RectTransform));
            _equipSectionRoot.transform.SetParent(_contentRt, false);
            var rootRt = (RectTransform)_equipSectionRoot.transform;
            rootRt.anchorMin        = new Vector2(0f, 1f);
            rootRt.anchorMax        = new Vector2(1f, 1f);
            rootRt.pivot            = new Vector2(0f, 1f);
            rootRt.sizeDelta        = new Vector2(0f, sectionH);
            rootRt.anchoredPosition = new Vector2(0f, startY);

            // 顶部白色分割线——跟主面板图标下方那条分割线同一套配色，不额外加底色/边框
            var divGo = new GameObject("Divider", typeof(RectTransform));
            divGo.transform.SetParent(_equipSectionRoot.transform, false);
            var divRt = (RectTransform)divGo.transform;
            divRt.anchorMin        = new Vector2(0f, 1f);
            divRt.anchorMax        = new Vector2(1f, 1f);
            divRt.pivot            = new Vector2(0.5f, 1f);
            divRt.sizeDelta        = new Vector2(0f, dividerH);
            divRt.anchoredPosition = Vector2.zero;
            var divImg = divGo.AddComponent<Image>();
            divImg.color = new Color(1f, 1f, 1f, 0.35f);
            divImg.raycastTarget = false;

            // 数值行——紧跟分割线，不再有单独的"武器信息"标题行。标签靠左、数值靠右
            // 分两个文本对象（不是拼成一整串），数值列右对齐才能真正对齐成一列。
            for (int i = 0; i < rows.Count; i++)
            {
                float y = -(dividerH + EquipSectionPadV + i * EquipLineH);

                var labelTmp = MakeText($"EL_{i}_Label", (RectTransform)_equipSectionRoot.transform, EquipFontSize, rows[i].color, FontStyles.Normal);
                labelTmp.text = rows[i].label;
                labelTmp.enableWordWrapping = false;
                labelTmp.overflowMode = TextOverflowModes.Ellipsis;
                labelTmp.alignment = TextAlignmentOptions.TopLeft;
                var labelRt = labelTmp.rectTransform;
                labelRt.anchorMin        = new Vector2(0f, 1f);
                labelRt.anchorMax        = new Vector2(1f, 1f);
                labelRt.pivot            = new Vector2(0f, 1f);
                labelRt.sizeDelta        = new Vector2(0f, EquipLineH);
                labelRt.anchoredPosition = new Vector2(EquipPadX, y);

                if (string.IsNullOrEmpty(rows[i].value))
                    continue;

                var valueTmp = MakeText($"EL_{i}_Value", (RectTransform)_equipSectionRoot.transform, EquipFontSize, rows[i].color, FontStyles.Normal);
                valueTmp.text = rows[i].value;
                valueTmp.enableWordWrapping = false;
                valueTmp.overflowMode = TextOverflowModes.Ellipsis;
                valueTmp.alignment = TextAlignmentOptions.TopRight;
                var valueRt = valueTmp.rectTransform;
                valueRt.anchorMin        = new Vector2(0f, 1f);
                valueRt.anchorMax        = new Vector2(1f, 1f);
                valueRt.pivot            = new Vector2(0f, 1f);
                valueRt.sizeDelta        = new Vector2(0f, EquipLineH);
                valueRt.anchoredPosition = new Vector2(EquipPadX, y);
            }
        }

        // 装备属性加成显示名——复用角色面板已经在用的本地化条目，保证跟角色面板里的
        // 属性名字说法一致（不会一边写"atk"一边写"攻击"）。BattleParameterDatabase
        // 里没在这张表里的 key 就直接显示原始 key 兜底，不会崩，只是不好看，
        // 后续遇到新 key 再补对应本地化条目。
        private static string GetStatBonusLabel(string key) => key switch
        {
            "atk"                          => GetLocalized("stat_attack_name", "攻击"),
            "def"                           => GetLocalized("stat_defense_name", "防御"),
            "critRate"                      => GetLocalized("charpanel_stat_critrate", "暴击率"),
            "critDamageMultiplier"          => GetLocalized("charpanel_stat_critmult", "暴击伤害"),
            "negativeCritRate"              => GetLocalized("charpanel_stat_negcritrate", "负暴击率"),
            "negativeCritDamageMultiplier"  => GetLocalized("charpanel_stat_negcritmult", "负暴击伤害"),
            "heatDamage"                    => GetLocalized("charpanel_stat_heatdamage", "灼热伤害"),
            "shockDamage"                   => GetLocalized("charpanel_stat_shockdamage", "电磁伤害"),
            "corrosionDamage"               => GetLocalized("charpanel_stat_corrosiondamage", "腐蚀伤害"),
            "freezeDamage"                  => GetLocalized("charpanel_stat_freezedamage", "冻结伤害"),
            "slashResist"                   => GetLocalized("charpanel_stat_slashresist", "斩击抗性"),
            "strikeResist"                  => GetLocalized("charpanel_stat_strikeresist", "打击抗性"),
            "impactResist"                  => GetLocalized("charpanel_stat_impactresist", "冲击抗性"),
            "heatResist"                    => GetLocalized("charpanel_stat_heatresist", "灼热抗性"),
            "shockResist"                   => GetLocalized("charpanel_stat_shockresist", "电磁抗性"),
            "corrosionResist"               => GetLocalized("charpanel_stat_corrosionresist", "腐蚀抗性"),
            "freezeResist"                  => GetLocalized("charpanel_stat_freezeresist", "冻结抗性"),
            _                               => key
        };

        // 染色色块：贴在"武器 Lv.1"那一行（_tagText）最右侧，不是耐久/改装槽那个
        // 嵌入式装备信息区——这3个色块是常驻的（EnsurePanel 建一次），Populate 时只更新
        // 颜色和显隐，不跟 EquipSection 那套"每次 Populate 整块重建"的逻辑混在一起。
        private const float DyeSwatchSize = 16f;
        private const float DyeSwatchGap  = 6f;
        private readonly Image[] _dyeSwatches = new Image[3];

        private void UpdateDyeSwatches(ItemDefinition def, InventoryItemEntry entry)
        {
            if (_dyeSwatches[0] == null) return;

            var eq = def?.equipment;
            bool show = eq != null && eq.defaultDyeColorSchemes != null && eq.defaultDyeColorSchemes.Count > 0;

            Color[] dye = null;
            if (show)
            {
                dye = entry?.dyeColors != null && entry.dyeColors.Length == 3
                    ? entry.dyeColors
                    : eq.GetRandomDefaultDyeColors();
            }

            for (int i = 0; i < 3; i++)
            {
                _dyeSwatches[i].gameObject.SetActive(show);
                if (!show) continue;
                Color c = i < dye.Length ? dye[i] : Color.white;
                c.a = 1f;
                _dyeSwatches[i].color = c;
            }
        }

        private static string GetLocalized(string key, string fallback)
        {
            var table = Resources.Load<UILocalizationTable>("UILocalizationTable");
            return table != null ? table.Get(key, fallback) : fallback;
        }

        private void PopulatePrices(ItemDefinition def)
        {
            // 从场景中的货币展示组件获取货币定义列表（已加载，无额外开销）
            var currencyDisplay = Object.FindObjectOfType<SkyPrisonCurrencyDisplay>();
            var knownCurrencies = currencyDisplay != null
                ? GetCurrencies(currencyDisplay)
                : null;

            var prices = def.currencyPrices;
            for (int i = 0; i < MaxPriceRows; i++)
            {
                if (_priceRows[i] == null) continue;
                bool show = prices != null && i < prices.Count && prices[i].price > 0;
                _priceRows[i].SetActive(show);
                if (!show) continue;

                var entry = prices[i];
                if (_priceTexts[i] != null)
                    _priceTexts[i].text = entry.price.ToString("N0");

                if (_priceIcons[i] != null)
                {
                    Sprite icon = FindCurrencyIcon(knownCurrencies, entry.currencyId);
                    _priceIcons[i].sprite  = icon;
                    _priceIcons[i].enabled = icon != null;
                }
            }
        }

        private static List<CurrencyDefinition> GetCurrencies(SkyPrisonCurrencyDisplay display)
        {
            // 通过反射读取 currencies 字段（private serialized field）
            var field = typeof(SkyPrisonCurrencyDisplay)
                .GetField("currencies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(display) as List<CurrencyDefinition>;
        }

        private static Sprite FindCurrencyIcon(List<CurrencyDefinition> list, string currencyId)
        {
            if (list == null || string.IsNullOrEmpty(currencyId)) return null;
            foreach (var cd in list)
                if (cd != null && cd.currencyId == currencyId) return cd.icon;
            return null;
        }

        private static string BuildTagLine(ItemDefinition def)
        {
            string cat = def.majorCategory switch
            {
                ItemMajorCategory.Equipment => EquipmentCategoryLabel(def.equipment),
                _                           => CategoryLabel(def.category)
            };
            string usable = def.IsUsable ? "  " + GetLocalized("ui_item_usable", "可使用") : "";
            string lvColored = QualityLvLabel(def.itemLevel);
            return $"{cat}  {lvColored}{usable}";
        }

        // 装备的分类标签比"装备"更具体——按装备槽位分武器/防具，跟编辑器左侧列表的
        // 自动分类（SkyPrisonItemDefinitionAssetListPanel.GetCategoryLabel）用同一套判断。
        private static string EquipmentCategoryLabel(ItemEquipmentExtension eq)
        {
            if (eq == null) return GetLocalized("item_cat_equipment", "装备");
            return eq.slot switch
            {
                EquipmentSlotType.Weapon          => GetLocalized("item_cat_weapon", "武器"),
                EquipmentSlotType.WeaponSecondary => GetLocalized("item_cat_weapon", "武器"),
                EquipmentSlotType.Head            => GetLocalized("item_cat_armor", "防具"),
                EquipmentSlotType.UpperBody       => GetLocalized("item_cat_armor", "防具"),
                EquipmentSlotType.LowerBody       => GetLocalized("item_cat_armor", "防具"),
                EquipmentSlotType.Hands           => GetLocalized("item_cat_armor", "防具"),
                EquipmentSlotType.Shoes           => GetLocalized("item_cat_armor", "防具"),
                _                                 => GetLocalized("item_cat_equipment", "装备")
            };
        }

        // LV9 纯文本，由 RainbowTextEffect 在整个 _tagText 上逐字符着色
        private static string QualityLvLabel(int lv)
        {
            if (lv == 9) return "Lv.9";
            return $"<color=#{QualityHex(lv)}>Lv.{lv}</color>";
        }

        // 从 private 改成 internal——CharacterPanelController 的装备栏也要显示同一套
        // 按物品等级(itemLevel)决定的品质颜色，不能另外维护一份可能跟这边不同步的映射表。
        internal static string QualityHex(int lv) => lv switch
        {
            1 => "B4B2A9",
            2 => "97C459",
            3 => "ED93B1",
            4 => "85B7EB",
            5 => "AFA9EC",
            6 => "1D9E75",
            7 => "D85A30",
            8 => "E24B4A",
            9 => "AFA9EC",
            _ => "B4B2A9"
        };

        private static string CategoryLabel(ItemCategory c) => c switch
        {
            ItemCategory.Consumable => GetLocalized("item_cat_consumable", "消耗品"),
            ItemCategory.Material   => GetLocalized("item_cat_material", "材料"),
            ItemCategory.Quest      => GetLocalized("item_cat_quest", "任务道具"),
            ItemCategory.Currency   => GetLocalized("item_cat_currency", "凭证"),
            ItemCategory.Special    => GetLocalized("item_cat_special", "特殊"),
            _                       => GetLocalized("item_cat_general", "道具")
        };

        // ── 动态面板高度 ──────────────────────────────────────────────────
        // 描述文字长短不定（同一个意思英文常常比中文长很多），固定高度无论调多大总有
        // 更长的文案会溢出。之前靠手动调用 cachedTextGenerator.GetPreferredHeight
        // 自己算一遍高度，跟Unity实际渲染这段文字用的是两套不同的计算路径——Build里
        // 这两套计算结果不一致，量出来的高度比实际渲染的偏小，导致下面的装备信息区
        // 往上贴、跟描述文字尾部重叠。现在 Desc 物体上挂了 ContentSizeFitter
        // （verticalFit=PreferredSize），交给Unity自己的布局系统算，强制一次
        // LayoutRebuilder 之后直接读它算出来的 rect.height——这就是Unity实际渲染时
        // 真正会用的高度，不存在"量出来的值跟实际渲染不一致"这个问题了。
        // 装备信息区（PopulateEquipmentInfo）要用这个值算自己该贴在哪个 Y 位置，所以
        // 要在装备区生成之前先测出来。真正的总面板高度要等装备区也生成完、知道它占多高
        // 之后才能定，见 ApplyDynamicPanelHeight。
        private float _measuredDescHeight;

        private void UpdateDynamicPanelHeight()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_descText.rectTransform);
            _measuredDescHeight = _descText.rectTransform.rect.height;
        }

        // 描述文字高度 + 装备信息区高度（如果有）都测完之后，统一算总面板高度。
        private void ApplyDynamicPanelHeight()
        {
            float fixedChrome = (IconSize + VGap * 2f + 1f) + PriceAreaH + PadY * 2f;
            float equipExtra  = _equipSectionH > 0f ? _equipSectionH + EquipSectionGap : 0f;
            _dynamicPanelH = Mathf.Clamp(fixedChrome + _measuredDescHeight + equipExtra, MinPanelH, MaxPanelH);
        }

        // ── 模糊 UV ───────────────────────────────────────────────────────

        private void UpdateBlur()
        {
            if (_blur == null || _box == null) return;
            Canvas.ForceUpdateCanvases();
            var c = new Vector3[4];
            _box.GetWorldCorners(c);
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            _blur.uvRect = new Rect(
                c[0].x / sw, c[0].y / sh,
                (c[2].x - c[0].x) / sw, (c[2].y - c[0].y) / sh);
        }

        // ── 构建 UI 子树 ──────────────────────────────────────────────────

        private void EnsurePanel()
        {
            if (_built) return;
            _built = true;

            // 物品详情面板本来是背包这个 Canvas（sortingOrder 1100）下面的普通子物体，
            // 跟背包共用同一个排序层——角色信息面板这类新窗口自己的 Canvas
            // sortingOrder 是 31900，两个窗口叠在一起的时候详情面板必然被压在下面，
            // 看不见。加一个 override Canvas，给一个高于目前所有悬浮窗的排序值，
            // 保证详情面板永远显示在最上层，不被其它悬浮窗遮挡。
            var overrideCanvas = gameObject.GetComponent<Canvas>();
            if (overrideCanvas == null) overrideCanvas = gameObject.AddComponent<Canvas>();
            overrideCanvas.overrideSorting = true;
            overrideCanvas.sortingOrder = 32000;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            // 从场景中的背包 prefab 取 GlobalStyleSettings → 中文字体
            var style = Object.FindObjectOfType<SkyPrison.Runtime.UI.SkyPrisonUIGlobalStyleSettings_V1>();
            _font = style?.defaultTextFont;

            // 找背包面板的磨砂 RawImage（共享模糊纹理）
            RawImage panelBlur = FindPanelBlurImage();

            // box: pivot=(0,1) 左上角，LateUpdate 每帧更新 anchoredPosition
            var boxGo = new GameObject("ItemDetailBox", typeof(RectTransform));
            boxGo.transform.SetParent(transform, false);
            _box = (RectTransform)boxGo.transform;
            _box.pivot      = new Vector2(0f, 0.5f);
            _box.anchorMin  = _box.anchorMax = new Vector2(0.5f, 0.5f);
            _box.sizeDelta  = new Vector2(PanelW, 300f);
            _box.gameObject.SetActive(false);
            _box.gameObject.AddComponent<RectMask2D>();

            // 磨砂背景
            if (panelBlur != null && panelBlur.texture != null)
            {
                var blurGo = NewStretch("Blur", _box);
                _blur = blurGo.gameObject.AddComponent<RawImage>();
                _blur.texture  = panelBlur.texture;
                _blur.material = panelBlur.material;
                _blur.color    = panelBlur.color;
                _blur.raycastTarget = false;
            }
            else
            {
                var fb = _box.gameObject.AddComponent<Image>();
                fb.color = new Color(0.08f, 0.09f, 0.10f, 1f);
            }


            // 左侧白色描边
            var edgeGo = new GameObject("Edge_L", typeof(RectTransform));
            edgeGo.transform.SetParent(_box, false);
            var edgeRt = (RectTransform)edgeGo.transform;
            edgeRt.anchorMin = new Vector2(0f, 0f); edgeRt.anchorMax = new Vector2(0f, 1f);
            edgeRt.offsetMin = Vector2.zero;         edgeRt.offsetMax = Vector2.zero;
            edgeRt.sizeDelta = new Vector2(2f, 0f);
            var edgeImg = edgeGo.AddComponent<Image>();
            edgeImg.color = new Color(1f, 1f, 1f, 0.35f);
            edgeImg.raycastTarget = false;

            // ── 内容区（padding 内缩）────────────────────────────────────
            var contentRt = NewStretch("Content", _box,
                new Vector2(PadX, PadY), new Vector2(-PadX, -PadY));
            _contentRt = contentRt;

            // 图标（左上角）
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(contentRt, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin        = new Vector2(0f, 1f);
            iconRt.anchorMax        = new Vector2(0f, 1f);
            iconRt.pivot            = new Vector2(0f, 1f);
            iconRt.sizeDelta        = new Vector2(IconSize, IconSize);
            iconRt.anchoredPosition = Vector2.zero;
            _iconImg = iconGo.AddComponent<Image>();
            _iconImg.preserveAspect = true;

            // 物品名（图标右侧，靠顶）
            _nameText = MakeText("Name", contentRt, 38f, Color.white, FontStyles.Bold);
            var nameRt = _nameText.rectTransform;
            // 真正的根因在这——之前 offsetMin/offsetMax 定好宽度之后，又紧接着设了
            // sizeDelta/anchoredPosition，对于左右锚点不同（横向拉伸）的 RectTransform，
            // 设置 sizeDelta.x 会按"锚点隐含宽度 + sizeDelta.x"重新算一遍横向范围——
            // sizeDelta.x=0 意味着"就是锚点撑满的宽度，没有任何收缩"，直接把前面
            // offsetMin/offsetMax 收进来的宽度全部作废，变回 contentRt 的整个宽度。
            // 这就是为什么日志测出来的可用宽度(500)其实是 contentRt 的宽度，根本没扣掉
            // 图标+右边距——不是字号计算错，是这个矩形的宽度从一开始就没被真正收窄过。
            // 横向拉伸的矩形只用 offsetMin/offsetMax 表达就够了，纵向（上锚点固定、
            // 不拉伸）改用同一套 offset 语义表达高度，不再混用 sizeDelta/anchoredPosition。
            nameRt.anchorMin        = new Vector2(0f, 1f);
            nameRt.anchorMax        = new Vector2(1f, 1f);
            nameRt.pivot            = new Vector2(0f, 1f);
            nameRt.offsetMin        = new Vector2(IconSize + HGap, -53f); // 高度48：顶部锚点往下53到底边
            nameRt.offsetMax        = new Vector2(-12f, -5f);              // 顶部锚点往下5到顶边
            // 名字太长（尤其日语/英语翻译比中文长得多）之前会被裁掉一截，看不全。
            // 不用 TMP 内置的 enableAutoSizing——试了三轮在这套现造 UI 的场景下都没有
            // 实际生效，改成 Populate() 里手动量宽度缩字号（见 ShrinkNameToFit）。
            _nameText.enableWordWrapping = false;

            // 分类行（图标右侧，名称下方）
            _tagText = MakeText("Tag", contentRt, 23f, new Color(0.58f, 0.64f, 0.68f, 1f), FontStyles.Normal);
            var tagRt = _tagText.rectTransform;
            tagRt.anchorMin        = new Vector2(0f, 1f);
            tagRt.anchorMax        = new Vector2(1f, 1f);
            tagRt.pivot            = new Vector2(0f, 1f);
            tagRt.sizeDelta        = new Vector2(0f, 32f);
            tagRt.anchoredPosition = new Vector2(IconSize + HGap, -58f);

            // 染色色块：贴在分类行（"武器 Lv.1"这行）最右侧，跟 Tag 同一条水平线，
            // 竖直居中对齐 Tag 那 32 高的行。常驻创建、Populate 时只切换颜色/显隐。
            float dyeRowCenterY = -58f - 32f * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                var swGo = new GameObject($"DyeSwatch_{i}", typeof(RectTransform));
                swGo.transform.SetParent(contentRt, false);
                var swRt = (RectTransform)swGo.transform;
                swRt.anchorMin        = new Vector2(1f, 1f);
                swRt.anchorMax        = new Vector2(1f, 1f);
                swRt.pivot             = new Vector2(1f, 0.5f);
                swRt.sizeDelta         = new Vector2(DyeSwatchSize, DyeSwatchSize);
                // 染色区域1/2/3要从左到右显示，跟编辑器里"方案1"那一行的3个色块顺序
                // 保持一致——这组整体贴右边界，所以要反过来算：i=2(通道3)贴最右
                // （偏移0），i=0(通道1)偏移最大，排在最左边。
                swRt.anchoredPosition  = new Vector2(-(2 - i) * (DyeSwatchSize + DyeSwatchGap), dyeRowCenterY);
                _dyeSwatches[i] = swGo.AddComponent<Image>();
                _dyeSwatches[i].raycastTarget = false;
                swGo.SetActive(false);
            }

            // 组上限（右上角，Tag 与分割线之间）
            _stackLimitText = MakeText("StackLimit", contentRt, 20f,
                new Color(0.58f, 0.64f, 0.68f, 1f), FontStyles.Normal);
            _stackLimitText.alignment = TextAlignmentOptions.MidlineRight;
            _stackLimitText.enableWordWrapping = false;
            var slRt = _stackLimitText.rectTransform;
            slRt.anchorMin        = new Vector2(1f, 1f);
            slRt.anchorMax        = new Vector2(1f, 1f);
            slRt.pivot            = new Vector2(1f, 1f);
            slRt.sizeDelta        = new Vector2(200f, 28f);
            slRt.anchoredPosition = new Vector2(0f, -128f);

            // 分割线（图标高度以下）
            var divGo = new GameObject("Divider", typeof(RectTransform));
            divGo.transform.SetParent(contentRt, false);
            var divRt = (RectTransform)divGo.transform;
            divRt.anchorMin        = new Vector2(0f, 1f);
            divRt.anchorMax        = new Vector2(1f, 1f);
            divRt.pivot            = new Vector2(0.5f, 1f);
            divRt.sizeDelta        = new Vector2(0f, 1f);
            divRt.anchoredPosition = new Vector2(0f, -(IconSize + VGap));
            var divImg = divGo.AddComponent<Image>();
            divImg.color = new Color(1f, 1f, 1f, 0.10f);
            divImg.raycastTarget = false;

            // ── DATA 装饰字（左下角，大号半透明，数据感）────────────────────
            var dataText = MakeText("DataLabel", contentRt, 112f,
                new Color(1f, 1f, 1f, 0.028f), FontStyles.Bold);
            dataText.text = "DATA";
            dataText.alignment = TextAlignmentOptions.BottomLeft;
            dataText.enableWordWrapping = false;
            dataText.overflowMode = TextOverflowModes.Overflow;
            dataText.characterSpacing = -8f; // 字间距收紧
            dataText.raycastTarget = false;
            // 优先用乱码字体（含 ع 字符），找不到退回 bahnschrift
            var dataFont = LoadFontByKeyword("ع") ?? LoadFont("bahnschrift SDF") ?? _font;
            if (dataFont != null) dataText.font = dataFont;
            var dataRt = dataText.rectTransform;
            dataRt.anchorMin  = new Vector2(0f, 0f);
            dataRt.anchorMax  = new Vector2(1f, 0f);
            dataRt.pivot      = new Vector2(0f, 0f);
            dataRt.sizeDelta  = new Vector2(0f, 120f);
            dataRt.anchoredPosition = new Vector2(-16f, -10f); // 往左下出血

            // 描述（分割线以下）——PriceRowH/PriceIconSz/PriceAreaH 挪到类级别常量了
            // （Populate 算动态高度时也要用同一份口径）。价格行自己锚定在面板底部，
            // 不受描述文字这边用 ContentSizeFitter 自动往下长高度影响。

            // 描述用 Legacy Text + 动态字体，完全绕过 TMP Static Atlas 缺字问题
            var descGo = new GameObject("Desc", typeof(RectTransform));
            descGo.transform.SetParent(contentRt, false);
            _descText = descGo.AddComponent<Text>();
            _descText.fontSize    = 27;
            _descText.color       = new Color(0.76f, 0.78f, 0.80f, 1f);
            _descText.alignment   = TextAnchor.UpperLeft;
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _descText.verticalOverflow   = VerticalWrapMode.Overflow;
            _descText.supportRichText    = true;
            _descText.raycastTarget      = false;
            // 优先从 Resources 加载项目内字体（Build 安全）；找不到退回 OS 字体
            Font descFont = Resources.Load<Font>("Fonts/msyh")
                         ?? Resources.Load<Font>("Fonts/msyhbd")
                         ?? Font.CreateDynamicFontFromOSFont(
                                new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 19);
            if (descFont != null) _descText.font = descFont;
            var descRt = (RectTransform)descGo.transform;
            // 顶部固定点锚点（不是上下都拉伸），横向仍然铺满——这样才能配合下面的
            // ContentSizeFitter 让Unity自己算出文字实际渲染高度、自动写回sizeDelta.y，
            // 不用我们手动用cachedTextGenerator再算一遍高度（今天Build里文字重叠
            // 就是那份手动测量跟Unity实际渲染结果不一致导致的，让Unity自己算才是
            // 唯一跟实际渲染结果保证一致的办法）。
            descRt.anchorMin  = new Vector2(0f, 1f);
            descRt.anchorMax  = new Vector2(1f, 1f);
            descRt.pivot      = new Vector2(0f, 1f);
            descRt.anchoredPosition = new Vector2(0f, -(IconSize + VGap * 2f + 1f));
            descRt.sizeDelta  = new Vector2(0f, 0f);
            var descFitter = descGo.AddComponent<ContentSizeFitter>();
            descFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            descFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── 价格行（右下角，从下往上叠）────────────────────────────────
            for (int i = 0; i < MaxPriceRows; i++)
            {
                float rowY = i * PriceRowH; // 从底部往上

                // 整行铺满内容区宽度，内部：数字靠右，图标在数字左侧紧贴
                var rowGo = new GameObject($"PriceRow_{i}", typeof(RectTransform));
                rowGo.transform.SetParent(contentRt, false);
                var rowRt = (RectTransform)rowGo.transform;
                rowRt.anchorMin        = new Vector2(0f, 0f);
                rowRt.anchorMax        = new Vector2(1f, 0f);
                rowRt.pivot            = new Vector2(0.5f, 0f);
                rowRt.sizeDelta        = new Vector2(0f, PriceRowH - 4f);
                rowRt.anchoredPosition = new Vector2(0f, rowY);
                _priceRows[i] = rowGo;
                rowGo.SetActive(false);

                // 数字（最右侧，固定宽度）
                const float NumW = 90f;
                var txt = MakeText($"PriceTxt_{i}", rowRt, 22f, ColdGreen, FontStyles.Bold);
                txt.alignment = TextAlignmentOptions.MidlineRight;
                var tRt = txt.rectTransform;
                tRt.anchorMin        = new Vector2(1f, 0f);
                tRt.anchorMax        = new Vector2(1f, 1f);
                tRt.pivot            = new Vector2(1f, 0.5f);
                tRt.sizeDelta        = new Vector2(NumW, 0f);
                tRt.anchoredPosition = new Vector2(0f, 0f);

                // 货币图标（数字左侧紧贴）
                var iconGo2 = new GameObject("CurrIcon", typeof(RectTransform));
                iconGo2.transform.SetParent(rowRt, false);
                var iRt = (RectTransform)iconGo2.transform;
                iRt.anchorMin        = new Vector2(1f, 0.5f);
                iRt.anchorMax        = new Vector2(1f, 0.5f);
                iRt.pivot            = new Vector2(1f, 0.5f);
                iRt.sizeDelta        = new Vector2(PriceIconSz, PriceIconSz);
                iRt.anchoredPosition = new Vector2(-(NumW + 6f), 0f);
                _priceIcons[i] = iconGo2.AddComponent<Image>();
                _priceIcons[i].preserveAspect = true;
                _priceIcons[i].raycastTarget = false;
                _priceTexts[i] = txt;
            }
        }

        // ── 构建辅助 ──────────────────────────────────────────────────────

        private static RectTransform NewStretch(string name, RectTransform parent,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;    rt.offsetMax = offsetMax;
            return rt;
        }

        private const float NameFontMax = 38f;
        private const float NameFontMin = 18f;

        // 手动缩字号，不依赖 TMP 的 enableAutoSizing（在这个场景下试了三轮都没有实际
        // 生效）。从最大字号开始量宽度，超了就降一档，直到测量结果不超或者已经降到
        // 下限为止。GetPreferredValues 是即时同步测量，不存在"等下一帧布局"的问题。
        private void ShrinkNameToFit(float availableWidth)
        {
            if (_nameText == null || availableWidth <= 0f) return;

            float size = NameFontMax;
            _nameText.fontSize = size;
            _nameText.ForceMeshUpdate();

            for (int i = 0; i < 20 && size > NameFontMin; i++)
            {
                float width = _nameText.GetPreferredValues(_nameText.text, 0f, 0f).x;
                if (width <= availableWidth) break;

                size -= 2f;
                if (size < NameFontMin) size = NameFontMin;
                _nameText.fontSize = size;
                _nameText.ForceMeshUpdate();
            }
        }

        private TMP_Text MakeText(string name, RectTransform parent,
            float size, Color color, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize    = size;
            tmp.color       = color;
            tmp.fontStyle   = style;
            tmp.raycastTarget = false;
            if (_font != null) tmp.font = _font;
            return tmp;
        }


        private static TMP_FontAsset LoadFont(string assetName)
        {
#if UNITY_EDITOR
            string path = $"Assets/_Project/UIUX/Fonts/TMP/{assetName}.asset";
            var fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa != null) return fa;
            // 文件名含特殊字符时用 FindAssets 模糊搜索
            string[] guids = UnityEditor.AssetDatabase.FindAssets(assetName + " t:TMP_FontAsset");
            if (guids.Length > 0)
            {
                fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                if (fa != null) return fa;
            }
#endif
            return Resources.Load<TMP_FontAsset>($"Fonts & Materials/{assetName}");
        }

        // 按文件名关键词在字体目录中查找（用于名称含乱码的字体）
        private static TMP_FontAsset LoadFontByKeyword(string keyword)
        {
#if UNITY_EDITOR
            string dir = "Assets/_Project/UIUX/Fonts/TMP/";
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { dir }))
            {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (p.Contains(keyword))
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
            }
#endif
            return null;
        }

        private RawImage FindPanelBlurImage()
        {
            // 从 transform.parent 往上找最近的 Canvas，再往下找 BlurBackground——不能从
            // 自己（transform）开始找：EnsurePanel 里在调用这个方法之前，已经给详情面板
            // 自己加了一个 override Canvas（为了保证显示在最上层），GetComponentInParent
            // 会优先命中自己身上这个新Canvas，导致往下搜索的起点变成详情面板自己的空子树，
            // 背包窗口里真正的 BlurBackground 永远搜不到，掉进黑色填充的兜底分支。
            Transform root = transform.parent != null
                ? (transform.parent.GetComponentInParent<Canvas>()?.transform ?? transform.parent)
                : null;
            // 背包这个手搭的旧 prefab 里磨砂节点叫 "BlurBackground"；仓库/角色面板这些
            // 走 SkyPrisonFloatingWindowKit.BuildBlurBackground 建的新窗口，节点名是
            // "Blur"（Kit 自己内部叫这个名字，跟背包的旧命名从来没统一过）。之前只找
            // "BlurBackground"，仓库这边永远找不到，详情面板背景一直是黑色兜底、不是
            // 黑白磨砂——两个名字都试一遍。
            Transform found = FindDeep(root, "BlurBackground") ?? FindDeep(root, "Blur");
            return found != null ? found.GetComponent<RawImage>() : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform f = FindDeep(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
