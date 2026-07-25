using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 工房窗口：修复耐久 + 安装/卸载改装组件。
/// UI 结构（编辑器搭建）：
///   左侧：当前装备武器列表（从 EquipmentRuntime 读）
///   中间：选中武器的改装槽位列表 + 耐久条
///   右侧：背包中的组件物品列表（可筛选兼容槽位）
/// </summary>
public class WorkshopWindowController : SkyPrisonBaseWindowController
{
    [Header("修复")]
    [SerializeField] private Text   durabilityText;
    [SerializeField] private Button repairButton;
    [SerializeField] private Text   repairCostText;
    [SerializeField] private string repairCurrencyId = "token";
    [SerializeField] private int    repairCostPerPoint = 5;

    [Header("改装槽位列表")]
    [SerializeField] private Transform modSlotContent;
    [SerializeField] private GameObject modSlotRowPrefab;

    [Header("背包组件列表")]
    [SerializeField] private Transform modItemContent;
    [SerializeField] private GameObject modItemRowPrefab;

    private InventoryItemEntry _selectedWeapon;
    private string             _selectedSlotKey;
    private InventoryItemEntry _selectedModItem;

    private readonly List<GameObject> _slotRows    = new List<GameObject>();
    private readonly List<GameObject> _modItemRows = new List<GameObject>();
    private SkyPrisonListGamepadNav _gamepadNav;

    protected override string WindowId => "workshop";

    // 手柄确认走 SkyPrisonListGamepadNav 里固定的 A 键，跟 Interact 绑定动作无关，
    // 手动配对图标（原因见 WorldMapWindowController 同款注释）。
    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;

        return new[]
        {
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "选择", label = L("ui_hint_select_slot_mod", "选择槽位/组件") },
            SkyPrisonWindowHint.Icon("keyboard/esc", "Esc", L("ui_hint_close", "关闭")),
        };
    }

    protected override void OnWindowOpen()
    {
        if (_gamepadNav == null) _gamepadNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();
        // 默认选中当前装备的武器
        _selectedWeapon  = EquipmentRuntime.Instance?.GetWeapon();
        _selectedSlotKey = null;
        _selectedModItem = null;
        RefreshAll();
    }

    // 槽位/组件/修复按钮随时会因为切换武器、装卸组件而重建，每次 RefreshAll 之后都要
    // 重新喂一次手柄可选目标。
    private void RefreshGamepadTargets()
    {
        if (_gamepadNav == null) return;
        var targets = new List<Button>();
        if (repairButton != null && repairButton.gameObject.activeInHierarchy) targets.Add(repairButton);
        foreach (var row in _slotRows)
        {
            if (row == null) continue;
            var btn = row.GetComponent<Button>() ?? FindChildButton(row, "SlotButton");
            if (btn != null) targets.Add(btn);
            var removeBtn = FindChildButton(row, "RemoveButton");
            if (removeBtn != null && removeBtn.interactable) targets.Add(removeBtn);
        }
        foreach (var row in _modItemRows)
        {
            if (row == null) continue;
            var btn = row.GetComponent<Button>() ?? FindChildButton(row, "ModItemButton");
            if (btn != null) targets.Add(btn);
        }
        _gamepadNav.SetTargets(targets);
    }

    protected override void OnWindowClose()
    {
        _selectedWeapon  = null;
        _selectedSlotKey = null;
        _selectedModItem = null;
    }

    // ── 刷新 ─────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        RefreshDurability();
        RefreshModSlots();
        RefreshModItemList();
        RefreshGamepadTargets();
    }

    private void RefreshDurability()
    {
        var w = _selectedWeapon;
        if (w?.definition?.equipment == null)
        {
            if (durabilityText != null) durabilityText.text = "—";
            if (repairButton   != null) repairButton.interactable = false;
            return;
        }

        int cur = w.currentDurability;
        int max = w.definition.equipment.maxDurability;
        if (durabilityText != null)
            durabilityText.text = max > 0 ? $"耐久  {cur} / {max}" : "无耐久";

        int cost = WorkshopRuntime.GetRepairCost(w, repairCurrencyId, repairCostPerPoint);
        if (repairCostText != null)
            repairCostText.text = cost > 0 ? $"修复费用  {cost} {repairCurrencyId}" : "无需修复";

        if (repairButton != null)
        {
            repairButton.interactable = cur < max && max > 0;
            repairButton.onClick.RemoveAllListeners();
            repairButton.onClick.AddListener(OnRepair);
        }
    }

    private void RefreshModSlots()
    {
        foreach (var go in _slotRows) if (go) Destroy(go);
        _slotRows.Clear();

        var w = _selectedWeapon;
        if (w?.definition?.equipment == null || modSlotContent == null || modSlotRowPrefab == null) return;

        foreach (var slotDef in w.definition.equipment.modSlots)
        {
            var row = Instantiate(modSlotRowPrefab, modSlotContent);
            _slotRows.Add(row);

            var installed = w.GetInstalledMod(slotDef.slotKey);
            bool occupied = installed != null;

            SetChildText(row, "SlotName",  slotDef.displayName);
            SetChildText(row, "SlotState", occupied
                ? (installed.isIdentified ? installed.modItemKey : "??? （未鉴定）")
                : "空置");

            // 卸载按钮
            var removeBtn = FindChildButton(row, "RemoveButton");
            if (removeBtn != null)
            {
                removeBtn.interactable = occupied;
                if (occupied)
                {
                    var capturedSlot = slotDef.slotKey;
                    removeBtn.onClick.AddListener(() => OnRemoveMod(capturedSlot));
                }
            }

            // 点击槽位 → 选中，高亮兼容组件
            var selectBtn = row.GetComponent<Button>() ?? FindChildButton(row, "SlotButton");
            if (selectBtn != null)
            {
                var capturedSlot = slotDef.slotKey;
                selectBtn.onClick.AddListener(() =>
                {
                    _selectedSlotKey = capturedSlot;
                    RefreshModItemList();
                    RefreshGamepadTargets();
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                });
            }
        }
    }

    private void RefreshModItemList()
    {
        foreach (var go in _modItemRows) if (go) Destroy(go);
        _modItemRows.Clear();

        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inventory == null || modItemContent == null || modItemRowPrefab == null) return;

        // 找出背包里所有 mod 类型物品，若已选槽位则筛选兼容类型
        string slotTypeKey = null;
        if (_selectedWeapon != null && _selectedSlotKey != null)
        {
            var slotDef = WorkshopRuntime.FindSlot(_selectedWeapon, _selectedSlotKey);
            slotTypeKey = slotDef?.slotTypeKey;
        }

        for (int i = 0; i < inventory.Slots.Count; i++)
        {
            var entry = inventory.Slots[i];
            if (entry?.definition?.mod == null) continue;
            if (entry.definition.mod.bonusPool.Count == 0 &&
                entry.definition.mod.compatibleSlotTypeKeys.Length == 0) continue;

            // 槽位兼容过滤
            if (slotTypeKey != null && !entry.definition.mod.IsCompatibleWith(slotTypeKey))
                continue;

            var row = Instantiate(modItemRowPrefab, modItemContent);
            _modItemRows.Add(row);

            SetChildText(row, "ModName", entry.isIdentified
                ? entry.definition.displayName
                : entry.definition.mod.unidentifiedDisplayName);

            SetChildText(row, "ModBonus", entry.isIdentified
                ? FormatBonuses(entry.rolledBonuses)
                : "（需先鉴定）");

            var btn = row.GetComponent<Button>() ?? FindChildButton(row, "ModItemButton");
            if (btn != null)
            {
                var capturedEntry = entry;
                btn.onClick.AddListener(() =>
                {
                    _selectedModItem = capturedEntry;
                    OnInstallMod();
                });
            }
        }
    }

    // ── 操作 ─────────────────────────────────────────────────────────────

    private void OnRepair()
    {
        if (_selectedWeapon == null) return;
        var result = WorkshopRuntime.Repair(_selectedWeapon, repairCurrencyId, repairCostPerPoint);
        switch (result)
        {
            case WorkshopRuntime.RepairResult.Success:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
                RefreshDurability();
                break;
            case WorkshopRuntime.RepairResult.InsufficientFunds:
            case WorkshopRuntime.RepairResult.AlreadyFull:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                break;
        }
    }

    private void OnInstallMod()
    {
        if (_selectedWeapon == null || _selectedSlotKey == null || _selectedModItem == null) return;

        var result = WorkshopRuntime.Install(_selectedWeapon, _selectedSlotKey, _selectedModItem);
        if (result == WorkshopRuntime.InstallResult.Success)
        {
            // 从背包消耗掉这个组件物品
            InventoryRuntimeBootstrap.Instance?.Inventory
                .RemoveItem(_selectedModItem.definition, 1);

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            _selectedModItem = null;

            // 触发属性重算（如果已装备该武器）
            NotifyEquipmentChanged();
            RefreshAll();
        }
        else
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        }
    }

    private void OnRemoveMod(string slotKey)
    {
        if (_selectedWeapon == null) return;

        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        var registry  = GameAssetLoader.ItemRegistry;
        if (inventory == null || registry == null) return;

        var result = WorkshopRuntime.Remove(_selectedWeapon, slotKey, inventory, registry);
        if (result == WorkshopRuntime.RemoveResult.Success)
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            NotifyEquipmentChanged();
            RefreshAll();
        }
        else
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        }
    }

    // 如果这把武器当前装备着，触发 EquipmentRuntime 事件让 EquipmentStatApplier 重算属性
    private void NotifyEquipmentChanged()
    {
        var eq = EquipmentRuntime.Instance;
        if (eq == null || _selectedWeapon == null) return;
        var equipped = eq.GetWeapon();
        if (equipped == _selectedWeapon)
        {
            // 重新 Equip 同一把武器触发 OnEquipped 事件
            eq.Unequip(EquipmentSlotType.Weapon);
            eq.Equip(_selectedWeapon);
        }
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private static string FormatBonuses(System.Collections.Generic.List<RolledModBonus> bonuses)
    {
        if (bonuses == null || bonuses.Count == 0) return "无词条";
        var sb = new System.Text.StringBuilder();
        foreach (var b in bonuses)
            sb.Append($"{b.displayName} +{(b.isPercent ? $"{b.value:P0}" : b.value.ToString("F0"))}  ");
        return sb.ToString().TrimEnd();
    }

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
