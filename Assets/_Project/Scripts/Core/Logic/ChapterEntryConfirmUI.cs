using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 章节进入前确认窗口（程序化构建，后续可替换为 Prefab）。
/// 显示：章节名称、目标列表、当前装备概览、进入/取消按钮。
/// 调用：ChapterEntryConfirmUI.Show(config, onConfirm)
/// </summary>
public class ChapterEntryConfirmUI : MonoBehaviour
{
    public class ChapterConfig
    {
        public string chapterDisplayName = "未知章节";
        public string chapterSceneName   = "";
        public List<string> objectives   = new List<string>();
    }

    private static ChapterEntryConfirmUI _instance;

    private CanvasGroup _group;
    private TMP_Text    _titleText;
    private Transform   _objectiveContainer;
    private Transform   _equipmentContainer;
    private Button      _confirmButton;
    private Button      _cancelButton;

    private Action _onConfirm;

    // ── 静态入口 ──────────────────────────────────────────────────────────

    public static void Show(ChapterConfig config, Action onConfirm = null)
    {
        if (_instance == null) _instance = Create();
        _instance._onConfirm = onConfirm;
        _instance.gameObject.SetActive(true);
        _instance.Populate(config);
    }

    public static void Hide()
    {
        if (_instance != null) _instance.gameObject.SetActive(false);
    }

    private static ChapterEntryConfirmUI Create()
    {
        var go = new GameObject("[ChapterEntryConfirmUI]");
        DontDestroyOnLoad(go);
        return go.AddComponent<ChapterEntryConfirmUI>();
    }

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUI();
        gameObject.SetActive(false);
    }

    // ── 填充数据 ─────────────────────────────────────────────────────────

    private void Populate(ChapterConfig config)
    {
        if (_titleText != null)
            _titleText.text = config.chapterDisplayName;

        // 目标列表
        if (_objectiveContainer != null)
        {
            foreach (Transform c in _objectiveContainer) Destroy(c.gameObject);
            foreach (var obj in config.objectives)
            {
                var label = CreateTMPInContainer("Obj", _objectiveContainer, 18, Color.white);
                if (label != null) label.text = $"◆  {obj}";
            }
        }

        // 当前装备概览
        if (_equipmentContainer != null)
        {
            foreach (Transform c in _equipmentContainer) Destroy(c.gameObject);
            var eq = EquipmentRuntime.Instance;
            if (eq != null)
            {
                AppendEquipLine("武器", eq.GetEquipped(EquipmentSlotType.Weapon));
                AppendEquipLine("头部", eq.GetEquipped(EquipmentSlotType.Head));
                AppendEquipLine("上装", eq.GetEquipped(EquipmentSlotType.UpperBody));
                AppendEquipLine("下装", eq.GetEquipped(EquipmentSlotType.LowerBody));
                AppendEquipLine("手部", eq.GetEquipped(EquipmentSlotType.Hands));
                AppendEquipLine("鞋子", eq.GetEquipped(EquipmentSlotType.Shoes));
            }
        }

        if (_confirmButton != null)
        {
            _confirmButton.onClick.RemoveAllListeners();
            _confirmButton.onClick.AddListener(OnConfirm);
        }
        if (_cancelButton != null)
        {
            _cancelButton.onClick.RemoveAllListeners();
            _cancelButton.onClick.AddListener(OnCancel);
        }
    }

    private void AppendEquipLine(string slotLabel, InventoryItemEntry entry)
    {
        if (_equipmentContainer == null) return;
        string name = entry?.definition != null ? entry.definition.displayName : "（空）";
        var label = CreateTMPInContainer($"Eq_{slotLabel}", _equipmentContainer, 16,
            entry != null ? Color.white : new Color(1f, 1f, 1f, 0.35f));
        if (label != null) label.text = $"{slotLabel}　{name}";
    }

    // ── 按钮回调 ─────────────────────────────────────────────────────────

    private void OnConfirm()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
        gameObject.SetActive(false);
        _onConfirm?.Invoke();
    }

    private void OnCancel()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
        gameObject.SetActive(false);
    }

    // ── UI 构建 ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9998;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();

        var bg = new GameObject("BG").AddComponent<RectTransform>();
        bg.SetParent(transform, false);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var panel = MakeRect("Panel", transform,
            new Vector2(0.15f, 0.1f), new Vector2(0.85f, 0.9f));

        _titleText = CreateTMPAt("Title", panel, new Vector2(0.5f, 0.92f), 28, Color.white);

        // 目标区
        CreateTMPAt("ObjHeader", panel, new Vector2(0.25f, 0.82f), 18, new Color(0.4f, 0.9f, 0.65f))
            .text = "任务目标";
        var objRect = MakeRect("ObjList", panel, new Vector2(0.05f, 0.5f), new Vector2(0.48f, 0.80f));
        _objectiveContainer = objRect;

        // 装备区
        CreateTMPAt("EqHeader", panel, new Vector2(0.73f, 0.82f), 18, new Color(0.4f, 0.9f, 0.65f))
            .text = "当前装备";
        var eqRect = MakeRect("EqList", panel, new Vector2(0.52f, 0.5f), new Vector2(0.95f, 0.80f));
        _equipmentContainer = eqRect;

        // 确认/取消
        _confirmButton = MakeButton("ConfirmBtn", panel,
            new Vector2(0.3f, 0.05f), new Vector2(0.68f, 0.16f),
            "进入关卡", new Color(0.3f, 0.8f, 0.55f, 0.9f), Color.black);

        _cancelButton = MakeButton("CancelBtn", panel,
            new Vector2(0.7f, 0.05f), new Vector2(0.95f, 0.16f),
            "取消", new Color(0.25f, 0.25f, 0.25f, 0.9f), Color.white);
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static TMP_Text CreateTMPAt(string name, RectTransform parent, Vector2 anchor, float size, Color color)
    {
        var rt = MakeRect(name, parent, anchor, anchor);
        rt.sizeDelta = new Vector2(600f, 40f);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size; tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    private static TMP_Text CreateTMPInContainer(string name, Transform container, float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(container, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size; tmp.color = color;
        return tmp;
    }

    private static Button MakeButton(string name, RectTransform parent,
        Vector2 anchorMin, Vector2 anchorMax, string label, Color bgColor, Color textColor)
    {
        var rt = MakeRect(name, parent, anchorMin, anchorMax);
        rt.gameObject.AddComponent<Image>().color = bgColor;
        var btn = rt.gameObject.AddComponent<Button>();
        var txt = CreateTMPAt("Label", rt, new Vector2(0.5f, 0.5f), 20, textColor);
        if (txt != null) txt.text = label;
        return btn;
    }
}
