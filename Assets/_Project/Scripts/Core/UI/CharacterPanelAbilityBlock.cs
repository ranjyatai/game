using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 角色信息面板里的「战局能力」区块：列出玩家这一局通过 AFP 加点获得的能力(特效力场 / 生命保险…)，
/// 每行图标 + 名字 + 右侧状态(生效中显示效果，冷却/恢复中显示倒计时)。没有任何能力时整块不显示。
/// 独立挂在 Box 上，不跟属性行一起销毁重建；自己每 0.2 秒读一次玩家身上的组件，数量变了才重建行，倒计时每帧刷新。
/// 以后要换到第二页(TAB)时，只需要把它挂到别的父物体上，内容不用改。
/// </summary>
public sealed class CharacterPanelAbilityBlock : MonoBehaviour
{
    struct Entry
    {
        public string name, effect;
        public int icon;
        public bool cooling;
        public float cooldownLeft;
        public string id;
    }

    static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);

    TMP_FontAsset _font;
    RectTransform _root;
    float _rowHeight, _headerHeight, _width, _scale;
    float _nextPoll;
    string _signature = "";
    readonly List<Entry> _entries = new List<Entry>();
    readonly List<TMP_Text> _timerTexts = new List<TMP_Text>();
    readonly List<int> _timerEntryIndex = new List<int>();

    /// <summary>当前有没有能力可显示(没有的话整块隐藏)；页面切换时用它决定第一页要不要显示这块。</summary>
    public bool HasEntries => _entries.Count > 0;

    public static CharacterPanelAbilityBlock Create(RectTransform box, TMP_FontAsset font, Vector2 anchoredPos, float width, float scale)
    {
        var go = new GameObject("AbilityBlock", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(box, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(width, 10f);
        var b = go.AddComponent<CharacterPanelAbilityBlock>();
        b._font = font; b._root = rt; b._width = width; b._scale = scale;
        b._rowHeight = 30f * scale;
        b._headerHeight = 22f * scale;
        return b;
    }

    void Update()
    {
        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + 0.2f;
            Collect();
            string sig = Signature();
            if (sig != _signature) { _signature = sig; Rebuild(); }
        }
        for (int i = 0; i < _timerTexts.Count; i++)          // 倒计时每帧读，不重建
        {
            if (_timerTexts[i] == null) continue;
            var e = _entries.Count > _timerEntryIndex[i] ? _entries[_timerEntryIndex[i]] : default;
            if (e.cooling) _timerTexts[i].text = FormatTimer(e.cooldownLeft);
        }
    }

    static string FormatTimer(float s) { int t = Mathf.CeilToInt(Mathf.Max(0f, s)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in _entries) sb.Append(e.id).Append('|').Append(e.name).Append('|').Append(e.cooling ? '1' : '0').Append(';');
        return sb.ToString();
    }

    static string Loc(string key, string fallback) => DoorText.Get(key, fallback);

    void Collect()
    {
        // 倒计时用的 cooldownLeft 每次轮询都更新；签名只看「有哪些能力 + 是否在冷却」，所以倒计时变化不会触发重建
        var list = new List<Entry>();
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player != null)
        {
            foreach (var s in player.GetComponentsInChildren<SkyPrisonAfpAbsorbShield>(true))
            {
                int tier = s.Tier;
                if (tier <= 0 || s.Capacity <= 0f) continue;
                string dmg = s.DamageKey == "heat" ? Loc("ui_afp_dmg_heat", "灼热") : Loc("ui_afp_dmg_shock", "电磁");
                list.Add(new Entry
                {
                    id = "field_" + s.DamageKey,
                    name = string.Format(Loc("ui_afp_ab_field_fmt", "特效力场：{0}"), dmg) + " " + SkyPrisonAfpAbsorbShield.TierName(tier),
                    effect = Loc("charpanel_ability_exempt", "豁免 1 次"),
                    icon = SkyPrisonAfpIcons.Shield,
                    cooling = s.RechargeLeft > 0f,
                    cooldownLeft = s.RechargeLeft,
                });
            }
            var ins = SkyPrisonAfpLifeInsurance.FindOnPlayer();
            if (ins != null && ins.Active)
            {
                list.Add(new Entry
                {
                    id = "insurance",
                    name = Loc("ui_afp_ab_insurance", "生命保险"),
                    effect = Loc("charpanel_ability_ready", "就绪"),
                    icon = SkyPrisonAfpIcons.Insurance,
                    cooling = ins.CooldownLeft > 0f,
                    cooldownLeft = ins.CooldownLeft,
                });
            }
        }
        _entries.Clear(); _entries.AddRange(list);
    }

    void Rebuild()
    {
        for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
        _timerTexts.Clear(); _timerEntryIndex.Clear();
        if (_entries.Count == 0) return;

        float total = _headerHeight + 8f * _scale + _entries.Count * _rowHeight;
        _root.sizeDelta = new Vector2(_width, total);

        // 小标题 + 细线(跟右边「武器/装备/快捷物品」的标题条同一风格)
        var header = MakeRect("Header", _root, new Vector2(0f, 1f), new Vector2(1f, 1f));
        header.pivot = new Vector2(0f, 1f); header.anchoredPosition = Vector2.zero; header.sizeDelta = new Vector2(0f, _headerHeight);
        var ht = SkyPrisonFloatingWindowKit.MakeText(header, "Label", Loc("charpanel_group_abilities_v2", "适应能力") + " · " + _entries.Count,
            SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.68f, FontStyles.Bold, _font);
        ht.alignment = TextAlignmentOptions.MidlineLeft; ht.color = new Color(1f, 1f, 1f, 0.7f); ht.raycastTarget = false;
        var line = MakeRect("Line", header, new Vector2(0f, 0f), new Vector2(1f, 0f));
        line.pivot = new Vector2(0f, 0f); line.sizeDelta = new Vector2(0f, 1.4f * _scale / 1.4f);
        var li = line.gameObject.AddComponent<Image>(); li.color = new Color(1f, 1f, 1f, 0.25f); li.raycastTarget = false;

        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            float y = _headerHeight + 8f * _scale + i * _rowHeight;
            var row = MakeRect("Row" + i, _root, new Vector2(0f, 1f), new Vector2(1f, 1f));
            row.pivot = new Vector2(0f, 1f); row.anchoredPosition = new Vector2(0f, -y); row.sizeDelta = new Vector2(0f, _rowHeight);
            float alpha = e.cooling ? 0.45f : 1f;

            float iconSize = _rowHeight * 0.74f;
            var iconRt = MakeRect("Icon", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            iconRt.pivot = new Vector2(0f, 0.5f); iconRt.sizeDelta = new Vector2(iconSize, iconSize); iconRt.anchoredPosition = new Vector2(0f, 0f);
            var img = iconRt.gameObject.AddComponent<Image>();
            img.sprite = SkyPrisonAfpIcons.GetSprite(e.icon); img.raycastTarget = false;
            img.color = e.cooling ? new Color(1f, 1f, 1f, 0.35f) : ColdGreen;

            var nameT = SkyPrisonFloatingWindowKit.MakeText(row, "Name", e.name, SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.8f, FontStyles.Normal, _font);
            var nrt = nameT.rectTransform; nrt.anchorMin = Vector2.zero; nrt.anchorMax = Vector2.one;
            nrt.offsetMin = new Vector2(iconSize + 10f * _scale, 0f); nrt.offsetMax = new Vector2(-_width * 0.34f, 0f);
            nameT.alignment = TextAlignmentOptions.MidlineLeft; nameT.color = new Color(1f, 1f, 1f, alpha); nameT.raycastTarget = false;
            nameT.enableWordWrapping = false; nameT.overflowMode = TextOverflowModes.Ellipsis;

            var stateT = SkyPrisonFloatingWindowKit.MakeText(row, "State", e.cooling ? FormatTimer(e.cooldownLeft) : e.effect,
                SkyPrisonFloatingWindowKit.DecorativeFontSize, FontStyles.Normal, _font);
            var srt = stateT.rectTransform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(_width * 0.62f, 0f); srt.offsetMax = Vector2.zero;
            stateT.alignment = TextAlignmentOptions.MidlineRight; stateT.raycastTarget = false;
            stateT.color = e.cooling ? new Color(1f, 1f, 1f, 0.6f) : new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.95f);
            stateT.enableWordWrapping = false;
            if (e.cooling) { _timerTexts.Add(stateT); _timerEntryIndex.Add(i); }
        }
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }
}
