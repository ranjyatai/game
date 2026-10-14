using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 工房修理窗口——列出身上所有需要修理的装备(图标/名字/耐久/费用/个别修理按钮)，
/// 底部还有"一键修理"一次性修完全部并只扣一次总费用。跟武器强化/改装那套UI
/// (还没搭)完全分开，这里只管耐久修复。
/// </summary>
public class WorkshopRepairPopupController : SkyPrisonBaseWindowController
{
    [Header("标题/文案")]
    [SerializeField] private Text   titleText;
    [SerializeField] private Text   messageText;
    [SerializeField] private Text   costLabel;   // "所需"
    [SerializeField] private Text   walletLabel; // "所持"

    [Header("装备列表")]
    [SerializeField] private Transform  itemListContent;
    [SerializeField] private GameObject itemRowPrefab;

    [Header("总费用")]
    [SerializeField] private Image  currencyIcon;
    [SerializeField] private Text   costText;
    [SerializeField] private Text   walletText;
    [SerializeField] private GameObject summaryRoot; // "所需/所持"两行整体，跟emptyText互斥显隐
    [SerializeField] private Text   emptyText; // 身上没有需要修的装备时显示

    [Header("按钮")]
    [SerializeField] private Button confirmButton; // 一键修理
    [SerializeField] private Text   confirmLabel;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Text   cancelLabel;

    [Header("修复参数")]
    [SerializeField] private string repairCurrencyId = "token";
    [SerializeField] private int    repairCostPerPoint = 5;

    private readonly List<GameObject> _itemRows = new List<GameObject>();
    private SkyPrisonListGamepadNav _gamepadNav;

    protected override string WindowId => "workshop_repair";

    private static string L(string key, string fallback)
    {
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        return locTable != null ? locTable.Get(key, fallback) : fallback;
    }

    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        return new[]
        {
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "选择", label = L("ui_hint_confirm_repair", "确认修理") },
            SkyPrisonWindowHint.Icon("keyboard/esc", "Esc", L("ui_hint_close", "关闭")),
        };
    }

    protected override void OnWindowOpen()
    {
        if (_gamepadNav == null) _gamepadNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();

        if (titleText    != null) titleText.text    = L("workshop_repair_title", "装备修理");
        if (messageText  != null) messageText.text  = L("workshop_repair_message", "是否消耗以下费用，修理身上所有装备？");
        if (costLabel    != null) costLabel.text    = L("shop_cost_label", "所需");
        if (walletLabel  != null) walletLabel.text  = L("shop_wallet_label", "所持");
        if (confirmLabel != null) confirmLabel.text = L("workshop_repair_confirm_all", "一键修理");
        if (cancelLabel  != null) cancelLabel.text  = L("workshop_repair_cancel", "取消");
        if (emptyText    != null) emptyText.text    = L("workshop_repair_empty", "身上没有需要修理的装备");

        RefreshAll();

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirmRepairAll);
        }
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(Close);
        }
    }

    protected override void OnWindowClose()
    {
        foreach (var go in _itemRows) if (go) Destroy(go);
        _itemRows.Clear();
    }

    // 冷绿=可点；灰=点了也没用——跟商店结账按钮同一套做法(SetFrameColor)。
    // interactable 故意保持 true，不设 false：设 false 会让 Button.onClick 整个不
    // 触发，拒绝反馈音就播不出来，点了跟没点一样，用户明确反馈过"按不了的时候要
    // 拒绝音效+灰色"，两者都要有，不能只做视觉一半。
    private static readonly Color EnabledColor  = new Color(0.42f, 0.92f, 0.68f, 1f);
    private static readonly Color DisabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

    private void RefreshAll()
    {
        RefreshItemList();
        RefreshTotals();
        RefreshGamepadTargets();
    }

    private void RefreshItemList()
    {
        foreach (var go in _itemRows) if (go) Destroy(go);
        _itemRows.Clear();

        if (itemListContent == null || itemRowPrefab == null) return;

        int lineIndex = 0;
        foreach (var item in WorkshopRuntime.GetAllEquippedRepairable())
        {
            int missing = item.definition.equipment.maxDurability - item.currentDurability;
            if (missing <= 0) continue; // 满耐久的不用列出来占地方

            var row = Instantiate(itemRowPrefab, itemListContent);
            _itemRows.Add(row);

            // 一深一浅交替填充，跟商店购物车行同一套做法，方便区分行。
            var rowBg = row.GetComponent<Image>();
            if (rowBg != null)
                rowBg.color = lineIndex % 2 == 0 ? new Color(1f, 1f, 1f, 0.04f) : new Color(1f, 1f, 1f, 0.09f);
            lineIndex++;

            BindItemRow(row, item);
        }
    }

    // 耐久归零(报废)用淡红色——跟商店"余额不足"同一个色值，颜色语义保持一致。
    private static readonly Color ZeroDurabilityColor = new Color(1f, 0.4f, 0.4f, 1f);
    private static readonly Color NormalDurabilityColor = new Color(0.75f, 0.78f, 0.8f, 1f);

    private void BindItemRow(GameObject row, InventoryItemEntry item)
    {
        var iconTf = row.transform.Find("Icon");
        if (iconTf != null)
        {
            var iconImg = iconTf.GetComponent<Image>();
            if (iconImg != null) iconImg.sprite = item.definition.icon;
        }

        SetChildText(row, "ItemName", item.definition.GetLocalizedDisplayName());
        var nameTf = row.transform.Find("ItemName");
        var nameText = nameTf != null ? nameTf.GetComponent<Text>() : null;
        if (nameText != null) nameText.color = QualityColor(item.definition.itemLevel);

        int max = item.definition.equipment.maxDurability;
        SetChildText(row, "Durability", $"{item.currentDurability} / {max}");
        var durTf = row.transform.Find("Durability");
        var durText = durTf != null ? durTf.GetComponent<Text>() : null;
        if (durText != null) durText.color = item.currentDurability <= 0 ? ZeroDurabilityColor : NormalDurabilityColor;

        int cost = WorkshopRuntime.GetRepairCost(item, repairCurrencyId, repairCostPerPoint);
        SetChildText(row, "TotalGroup/CostText", cost.ToString("N0"));
        var iconGroupTf = row.transform.Find("TotalGroup/Icon");
        if (iconGroupTf != null)
        {
            var img = iconGroupTf.GetComponent<Image>();
            var tokenDef = FindCurrencyIcon();
            if (img != null && tokenDef != null) img.sprite = tokenDef;
        }

        var repairBtn = FindChildButton(row, "RepairButton");
        if (repairBtn != null)
        {
            var repairLabelTf = repairBtn.transform.Find("Label");
            var repairLabel = repairLabelTf != null ? repairLabelTf.GetComponent<Text>() : null;
            if (repairLabel != null) repairLabel.text = L("workshop_repair_item_action", "修理");

            bool canAfford = CurrencyRuntime.Instance != null && CurrencyRuntime.Instance.Get(repairCurrencyId) >= cost;
            ApplyButtonState(repairBtn, repairLabel, canAfford ? EnabledColor : DisabledColor);

            var captured = item;
            repairBtn.onClick.RemoveAllListeners();
            repairBtn.onClick.AddListener(() => OnConfirmRepairSingle(captured));
        }
    }

    private Sprite _cachedCurrencyIcon;
    private bool   _cachedCurrencyIconLookedUp;
    private Sprite FindCurrencyIcon()
    {
        if (_cachedCurrencyIconLookedUp) return _cachedCurrencyIcon;
        _cachedCurrencyIconLookedUp = true;
        if (currencyIcon != null) _cachedCurrencyIcon = currencyIcon.sprite;
        return _cachedCurrencyIcon;
    }

    private void RefreshTotals()
    {
        int cost = WorkshopRuntime.GetRepairAllCost(repairCurrencyId, repairCostPerPoint);
        bool needsRepair = cost > 0;
        bool canAfford = CurrencyRuntime.Instance != null && CurrencyRuntime.Instance.Get(repairCurrencyId) >= cost;
        bool canRepair = needsRepair && canAfford;

        if (messageText != null)
            messageText.gameObject.SetActive(needsRepair);
        if (summaryRoot != null)
            summaryRoot.SetActive(needsRepair);
        if (emptyText != null)
            emptyText.gameObject.SetActive(!needsRepair);

        if (costText != null)
        {
            costText.text = cost.ToString("N0");
            costText.color = needsRepair && !canAfford ? new Color(1f, 0.4f, 0.4f, 1f) : EnabledColor;
        }
        if (walletText != null)
        {
            long wallet = CurrencyRuntime.Instance != null ? CurrencyRuntime.Instance.Get(repairCurrencyId) : 0;
            walletText.text = wallet.ToString("N0");
        }

        if (confirmButton != null)
            ApplyButtonState(confirmButton, confirmLabel, canRepair ? EnabledColor : DisabledColor);
    }

    // 货架行/装备行/按钮随时会因为修理重建，重建后都要重喂一次手柄目标。
    private void RefreshGamepadTargets()
    {
        if (_gamepadNav == null) return;
        var targets = new List<Button>();
        foreach (var row in _itemRows)
        {
            if (row == null) continue;
            var btn = FindChildButton(row, "RepairButton");
            if (btn != null) targets.Add(btn);
        }
        if (confirmButton != null && confirmButton.gameObject.activeInHierarchy) targets.Add(confirmButton);
        if (cancelButton  != null && cancelButton.gameObject.activeInHierarchy) targets.Add(cancelButton);
        _gamepadNav.SetTargets(targets);
    }

    // AddOutline() 拼出来的四条边框子物体固定叫 Outline_T/B/L/R(见
    // CreateWorkshopRepairPopupPrefab.MakeButton)。设完描边+文字颜色后还要让
    // SkyPrisonUIButtonFeedback 重新采样一次"当前颜色"当新的常态基准——它自己的
    // _normals 只在 Start() 那一刻(也就是窗口刚打开、还没跑过一次真正的可修复
    // 判定)采样过一次，之后不会再更新。玩家点完"一键修理"之后鼠标通常还悬停在
    // 按钮上，松手瞬间连带触发的点击闪光结束后会用这份陈旧的_normals收尾——不
    // 刷新的话，修完之后按钮明明该变灰，摸着鼠标不挪开的话看起来却还是绿的
    // (用户实测复现的正是这个)。
    private static void ApplyButtonState(Button btn, Text label, Color c)
    {
        if (btn == null) return;
        foreach (string n in new[] { "Outline_T", "Outline_B", "Outline_L", "Outline_R" })
        {
            var t = btn.transform.Find(n);
            var img = t != null ? t.GetComponent<Image>() : null;
            if (img != null) img.color = c;
        }
        if (label != null) label.color = c;

        var feedback = btn.GetComponent<SkyPrisonUIButtonFeedback>();
        if (feedback != null)
            feedback.Configure(btn.GetComponentsInChildren<Graphic>(true));
    }

    private void OnConfirmRepairSingle(InventoryItemEntry item)
    {
        var result = WorkshopRuntime.Repair(item, repairCurrencyId, repairCostPerPoint);
        switch (result)
        {
            case WorkshopRuntime.RepairResult.Success:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Repair);
                RefreshAll();
                break;
            case WorkshopRuntime.RepairResult.InsufficientFunds:
            case WorkshopRuntime.RepairResult.AlreadyFull:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                break;
        }
    }

    private void OnConfirmRepairAll()
    {
        var result = WorkshopRuntime.RepairAll(repairCurrencyId, repairCostPerPoint);
        switch (result)
        {
            case WorkshopRuntime.RepairResult.Success:
                // 专门的"修理成功"音效通道——没配clip的话SkyPrisonSystemSEPlayer会
                // 静默跳过(已有的通用兜底逻辑)，不会因为没配而报错或者哑火。
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Repair);
                RefreshAll();
                break;
            case WorkshopRuntime.RepairResult.InsufficientFunds:
            case WorkshopRuntime.RepairResult.AlreadyFull:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                break;
        }
    }

    // 跟 ShopWindowController.QualityColor/QualityHex、InventoryItemDetailPanel
    // 保持同一套按itemLevel映射的品质配色，不能自己另起一份——用户明确要求过
    // 物品名字应该按这个统一来源上色，不是rarity字段那套没人在用的配色。
    private static string QualityHex(int lv) => lv switch
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

    private static Color QualityColor(int lv)
        => ColorUtility.TryParseHtmlString("#" + QualityHex(lv), out Color c) ? c : Color.white;

    // ── 小工具 ──────────────────────────────────────────────────────────
    private static void SetChildText(GameObject root, string childPath, string text)
    {
        if (root == null) return;
        var t = root.transform.Find(childPath);
        if (t == null) return;
        var tx = t.GetComponent<Text>();
        if (tx != null) tx.text = text;
    }

    private static Button FindChildButton(GameObject root, string childPath)
    {
        if (root == null) return null;
        var t = root.transform.Find(childPath);
        return t != null ? t.GetComponent<Button>() : null;
    }
}
