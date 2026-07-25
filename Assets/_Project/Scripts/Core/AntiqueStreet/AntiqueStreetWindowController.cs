using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 古物街窗口：鉴定未识别的组件物品 + 商店（复用 ShopWindowController 通过 FacilityInteractable 配置）。
/// 本窗口只负责鉴定服务。
/// UI 结构（编辑器搭建）：
///   左侧：背包中未鉴定组件列表
///   右侧：选中组件详情 + 费用 + 鉴定按钮 / 全部鉴定按钮
/// </summary>
public class AntiqueStreetWindowController : SkyPrisonBaseWindowController
{
    [Header("鉴定")]
    [SerializeField] private string identifyCurrencyId = "token";

    [Header("列表")]
    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject listRowPrefab;

    [Header("详情")]
    [SerializeField] private Text   selectedName;
    [SerializeField] private Text   selectedCost;
    [SerializeField] private Button identifyButton;
    [SerializeField] private Button identifyAllButton;
    [SerializeField] private Text   identifyAllCostText;

    private InventoryItemEntry _selected;
    private readonly List<GameObject> _rows = new List<GameObject>();
    private SkyPrisonListGamepadNav _gamepadNav;

    protected override string WindowId => "antique_street";

    // 手柄确认走 SkyPrisonListGamepadNav 里固定的 A 键，手动配对图标（原因见
    // WorldMapWindowController 同款注释）。
    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;

        return new[]
        {
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "选择", label = L("ui_hint_select_item", "选择物品") },
            SkyPrisonWindowHint.Icon("keyboard/esc", "Esc", L("ui_hint_close", "关闭")),
        };
    }

    protected override void OnWindowOpen()
    {
        if (_gamepadNav == null) _gamepadNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();
        _selected = null;
        RefreshList();
        RefreshIdentifyAllButton();
        RefreshGamepadTargets();
    }

    private void RefreshGamepadTargets()
    {
        if (_gamepadNav == null) return;
        var targets = new List<Button>();
        foreach (var row in _rows)
        {
            if (row == null) continue;
            var btn = row.GetComponent<Button>() ?? FindChildButton(row, "RowButton");
            if (btn != null) targets.Add(btn);
        }
        if (identifyButton != null && identifyButton.gameObject.activeInHierarchy) targets.Add(identifyButton);
        if (identifyAllButton != null && identifyAllButton.gameObject.activeInHierarchy) targets.Add(identifyAllButton);
        _gamepadNav.SetTargets(targets);
    }

    protected override void OnWindowClose()
    {
        _selected = null;
    }

    // ── 列表 ─────────────────────────────────────────────────────────────

    private void RefreshList()
    {
        foreach (var go in _rows) if (go) Destroy(go);
        _rows.Clear();

        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inventory == null || listContent == null || listRowPrefab == null) return;

        foreach (var slot in inventory.Slots)
        {
            if (slot == null || slot.isIdentified) continue;
            if (slot.definition?.mod == null) continue;

            var row = Instantiate(listRowPrefab, listContent);
            _rows.Add(row);

            SetChildText(row, "ItemName", slot.definition.mod.unidentifiedDisplayName);
            int cost = IdentificationRuntime.GetCost(slot);
            SetChildText(row, "CostText", cost > 0 ? $"{cost} {identifyCurrencyId}" : "免费");

            var btn = row.GetComponent<Button>() ?? FindChildButton(row, "RowButton");
            if (btn != null)
            {
                var captured = slot;
                btn.onClick.AddListener(() => SelectEntry(captured));
            }
        }
    }

    private void SelectEntry(InventoryItemEntry entry)
    {
        _selected = entry;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        if (selectedName != null)
            selectedName.text = entry.definition.mod.unidentifiedDisplayName;

        int cost = IdentificationRuntime.GetCost(entry);
        if (selectedCost != null)
            selectedCost.text = cost > 0 ? $"费用  {cost} {identifyCurrencyId}" : "免费";

        if (identifyButton != null)
        {
            bool canAfford = cost <= 0 || (CurrencyRuntime.Instance?.Get(identifyCurrencyId) >= cost);
            identifyButton.interactable = canAfford;
            identifyButton.onClick.RemoveAllListeners();
            identifyButton.onClick.AddListener(OnIdentify);
        }
    }

    // ── 操作 ─────────────────────────────────────────────────────────────

    private void OnIdentify()
    {
        if (_selected == null) return;
        var result = IdentificationRuntime.Identify(_selected, identifyCurrencyId);
        switch (result)
        {
            case IdentificationRuntime.IdentifyResult.Success:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
                _selected = null;
                RefreshList();
                RefreshIdentifyAllButton();
                RefreshGamepadTargets();
                break;
            case IdentificationRuntime.IdentifyResult.InsufficientFunds:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                break;
        }
    }

    private void RefreshIdentifyAllButton()
    {
        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inventory == null) return;

        long totalCost = 0;
        int  count     = 0;
        foreach (var slot in inventory.Slots)
        {
            if (slot == null || slot.isIdentified || slot.definition?.mod == null) continue;
            totalCost += IdentificationRuntime.GetCost(slot);
            count++;
        }

        if (identifyAllCostText != null)
            identifyAllCostText.text = count > 0
                ? $"全部鉴定（{count} 件）  {totalCost} {identifyCurrencyId}"
                : "无未鉴定物品";

        if (identifyAllButton != null)
        {
            bool canAfford = count > 0 && (totalCost <= 0 || CurrencyRuntime.Instance?.Get(identifyCurrencyId) >= totalCost);
            identifyAllButton.interactable = canAfford;
            identifyAllButton.onClick.RemoveAllListeners();
            identifyAllButton.onClick.AddListener(OnIdentifyAll);
        }
    }

    private void OnIdentifyAll()
    {
        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        int done = IdentificationRuntime.IdentifyAll(inventory, identifyCurrencyId);
        if (done > 0)
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            _selected = null;
            RefreshList();
            RefreshIdentifyAllButton();
            RefreshGamepadTargets();
        }
        else
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        }
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private static void SetChildText(GameObject root, string childName, string text)
    {
        if (root == null) return;
        var t = root.transform.Find(childName);
        if (t != null) { var tx = t.GetComponent<Text>(); if (tx) tx.text = text; }
    }

    private static Button FindChildButton(GameObject root, string childName)
    {
        if (root == null) return null;
        var t = root.transform.Find(childName);
        return t != null ? t.GetComponent<Button>() : null;
    }
}
