using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 装备面板窗口。显示 6 个装备槽位当前穿戴的物品。
/// 鼠标悬停槽位 → 复用 InventoryItemDetailPanel 显示属性。
/// 点击已装备的槽位 → 卸装还回背包。
/// UI 结构（编辑器搭建）：
///   6 个 EquipmentSlotView 子节点，每个对应一个 EquipmentSlotType。
/// </summary>
public class EquipmentPanelWindowController : SkyPrisonBaseWindowController
{
    [System.Serializable]
    public class SlotView
    {
        public EquipmentSlotType slotType;
        public GameObject        root;       // 槽位根节点
        public Image             itemIcon;   // 物品图标
        public Image             emptyIcon;  // 空槽占位图标
        public Text              slotLabel;  // 槽位名称（头部/武器…）
    }

    [Header("装备槽位")]
    [SerializeField] private List<SlotView> slots = new List<SlotView>();

    [Header("面板引用（用于详情面板定位）")]
    [SerializeField] private RectTransform panelRect;

    // 详情面板（和背包共用同一个 Panel 类，程序化创建）
    private InventoryItemDetailPanel _detailPanel;

    private SkyPrisonListGamepadNav _gamepadNav;

    protected override string WindowId => "equipment_panel";

    // 手柄确认走 SkyPrisonListGamepadNav 里固定的 A 键，手动配对图标（原因见
    // WorldMapWindowController 同款注释）。
    // 之前 label 是写死的中文，不管切什么语言这条提示条都只显示中文——跟
    // PauseMenuController 同一个套路查表（Resources.Load 直接拿表，不依赖背包那套
    // SkyPrisonInventoryTextLocalizer，这几个窗口跟背包无关）。
    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;

        return new[]
        {
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "选择", label = L("ui_hint_unequip", "卸下装备") },
            SkyPrisonWindowHint.Icon("keyboard/esc", "Esc", L("ui_hint_close", "关闭")),
        };
    }

    // ── 生命周期 ─────────────────────────────────────────────────────────

    private void Awake()
    {
        // 程序化创建详情面板，挂在窗口同级节点下（避免被 RectMask2D 裁剪）
        var go = new GameObject("EquipDetailPanel", typeof(RectTransform));
        go.transform.SetParent(transform.parent, false);
        _detailPanel = go.AddComponent<InventoryItemDetailPanel>();
    }

    private void OnDestroy()
    {
        if (_detailPanel != null) Destroy(_detailPanel.gameObject);
    }

    protected override void OnWindowOpen()
    {
        if (_gamepadNav == null) _gamepadNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();
        EquipmentRuntime.OnEquipped   += HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped += HandleEquipmentChanged;
        RefreshAll();
        SetupSlotEvents();
        RefreshGamepadTargets();
    }

    // EventTrigger.PointerClick 是纯鼠标事件，手柄按不到——每个槽位额外挂一个 Button
    // 组件（跟 EventTrigger 共存不冲突），onClick 走同一个卸装逻辑，喂给通用手柄导航器。
    private void RefreshGamepadTargets()
    {
        if (_gamepadNav == null) return;
        var targets = new List<Button>();
        var eq = EquipmentRuntime.Instance;
        foreach (var slot in slots)
        {
            if (slot.root == null) continue;
            var btn = slot.root.GetComponent<Button>() ?? slot.root.AddComponent<Button>();
            bool hasItem = eq?.GetEquipped(slot.slotType)?.definition != null;
            btn.interactable = hasItem;
            var capturedType = slot.slotType;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => TryUnequip(capturedType));
            targets.Add(btn);
        }
        _gamepadNav.SetTargets(targets);
    }

    protected override void OnWindowClose()
    {
        EquipmentRuntime.OnEquipped   -= HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped -= HandleEquipmentChanged;
        _detailPanel?.Hide();
    }

    private void HandleEquipmentChanged(EquipmentSlotType _, InventoryItemEntry __)
    {
        RefreshAll();
        RefreshGamepadTargets();
    }

    // ── 刷新所有槽位图标 ─────────────────────────────────────────────────

    private void RefreshAll()
    {
        var eq = EquipmentRuntime.Instance;
        foreach (var slot in slots)
        {
            if (slot.root == null) continue;
            var entry = eq?.GetEquipped(slot.slotType);
            bool hasItem = entry?.definition != null;

            if (slot.itemIcon  != null)
            {
                slot.itemIcon.gameObject.SetActive(hasItem);
                if (hasItem) slot.itemIcon.sprite = entry.definition.icon;
            }
            if (slot.emptyIcon != null)
                slot.emptyIcon.gameObject.SetActive(!hasItem);
            if (slot.slotLabel != null)
                slot.slotLabel.text = SlotDisplayName(slot.slotType);
        }
    }

    // ── 槽位交互（hover 详情 + 点击卸装）────────────────────────────────

    private void SetupSlotEvents()
    {
        foreach (var slot in slots)
        {
            if (slot.root == null) continue;

            // 确保有 EventTrigger
            var trigger = slot.root.GetComponent<EventTrigger>()
                          ?? slot.root.AddComponent<EventTrigger>();
            trigger.triggers.Clear();

            var capturedSlot = slot;

            // PointerEnter → 显示详情
            AddTriggerEntry(trigger, EventTriggerType.PointerEnter, _ =>
            {
                var entry = EquipmentRuntime.Instance?.GetEquipped(capturedSlot.slotType);
                if (entry?.definition != null)
                    _detailPanel?.Show(entry.definition, entry, panelRect ?? (RectTransform)transform);
            });

            // PointerExit → 隐藏详情
            AddTriggerEntry(trigger, EventTriggerType.PointerExit, _ =>
                _detailPanel?.Hide());

            // PointerClick → 卸装
            AddTriggerEntry(trigger, EventTriggerType.PointerClick, _ =>
                TryUnequip(capturedSlot.slotType));
        }
    }

    private void TryUnequip(EquipmentSlotType slotType)
    {
        var eq = EquipmentRuntime.Instance;
        if (eq == null) return;

        var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inventory == null) return;

        // 之前这里是手写的 inventory.AddItem(entry.definition, entry.count) +
        // eq.Unequip(slotType) 两步——AddItem 会新建一个全新的 entry（或者合并数量
        // 进背包里已有的同款堆叠），不是把这一件具体实例原样放回去，等于"删掉这把
        // 装备、背包里凭空多一件同属性的新的"，跟角色面板/背包自己的卸装入口（走
        // TryUnequipToInventory，用 AddExactEntry 原样放回）是两套不一致的逻辑，
        // 这正是背包卸装后武器"变多了"的原因。统一改成走同一个方法，跟其它入口
        // 保持一致：装备了的东西一定在背包里腾出/占用一个格子，卸下来就是原样放回
        // 这一件，不会凭空复制。
        if (!eq.TryUnequipToInventory(inventory, slotType))
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return; // 背包满，或者这个槽本来就没装备
        }

        _detailPanel?.Hide();
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private static string SlotDisplayName(EquipmentSlotType t) => t switch
    {
        EquipmentSlotType.Weapon    => "武器",
        EquipmentSlotType.Head      => "头部",
        EquipmentSlotType.UpperBody => "上装",
        EquipmentSlotType.LowerBody => "下装",
        EquipmentSlotType.Hands     => "手部",
        EquipmentSlotType.Shoes     => "鞋子",
        _                           => t.ToString(),
    };

    private static void AddTriggerEntry(
        EventTrigger trigger,
        EventTriggerType type,
        UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }
}
