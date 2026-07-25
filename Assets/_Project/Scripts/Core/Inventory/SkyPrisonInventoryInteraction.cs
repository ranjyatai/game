using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 背包面板级交互控制器（挂在 InventoryPanel 上，由 Workbench builder 添加）。
    /// 负责：拖拽幽灵、落点判定（同种合并 / 否则换位 / 拖出窗口丢弃）、右键拆分弹窗、丢弃弹窗。
    /// 数据操作全部走 InventoryRuntime（MergeSlots/MoveSlot/SplitSlot/DiscardSlot）；
    /// 改完数据后 OnInventoryChanged → InventoryGridView 自动刷新显示。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryInteraction : MonoBehaviour, IInventorySlotController
    {
        private InventoryRuntime Inv =>
            InventoryRuntimeBootstrap.Instance != null ? InventoryRuntimeBootstrap.Instance.Inventory : null;

        private RectTransform PanelRect => (RectTransform)transform;

        // ── 悬浮格子（装备对比预览用）──────────────────────────────────────
        // CharacterPanelController 用装备槽打开背包时，每帧读这个属性算"如果换上这件
        // 悬浮中的装备会怎么变化"，不用额外接事件——跟角色面板那边已有的轮询式耦合
        // （_equipInventoryCurrentSlot）是同一个思路。
        public InventoryItemEntry HoveredEntry { get; private set; }

        public void SetHoveredSlot(int slotIndex)
        {
            InventoryRuntime inv = Inv;
            HoveredEntry = (inv != null && slotIndex >= 0 && slotIndex < inv.Slots.Count)
                ? inv.Slots[slotIndex]
                : null;
        }

        // ── 物品冷却 ──────────────────────────────────────────────────────
        // 冷却数据本身挪到 ItemCooldownTracker（全局静态，不随这个窗口销毁），这个类
        // 只留菜单展示要用的 itemKey，方便定位当前菜单是不是正显示着这一项的冷却倒计时。

        // ── 拖拽幽灵 ──────────────────────────────────────────────────────
        private Canvas _ghostCanvas;
        private Image _ghost;
        private RectTransform _ghostRt;
        private int _srcIndex = -1;
        private bool _dragging;

        private void Awake()
        {
            if (GetComponent<SkyPrisonInventoryGamepad>() == null)
                gameObject.AddComponent<SkyPrisonInventoryGamepad>();
            if (GetComponent<InventoryItemDetailController>() == null)
                gameObject.AddComponent<InventoryItemDetailController>();
        }

        public void BeginDrag(int srcIndex, PointerEventData e)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || srcIndex < 0 || srcIndex >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[srcIndex];
            if (entry == null || entry.definition == null) return;

            _dragging = true;
            _srcIndex = srcIndex;

            EnsureGhost();
            _ghost.sprite = entry.definition.icon;
            _ghost.enabled = entry.definition.icon != null;
            _ghostCanvas.gameObject.SetActive(true);
            UpdateDrag(e);

            // 拿起手感：拖动期间源格视觉清空（数据不动，落点决定后再刷新还原/更新）
            FindSlotView(srcIndex)?.SetEmpty();
            SkyPrisonItemMaterialSoundTable.PlayPickup(entry.definition.general.soundMaterial);

            // 拖动期间挂起色差静止快照，避免旧快照把物品留在原位形成残影
            SetChromaticSuspended(true);
        }

        public void UpdateDrag(PointerEventData e)
        {
            if (!_dragging || _ghostRt == null) return;
            _ghostRt.position = e.position;
        }

        public void EndDrag(int srcIndex, PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_ghostCanvas != null) _ghostCanvas.gameObject.SetActive(false);

            InventoryRuntime inv = Inv;
            if (inv == null) return;

            InventorySlotInteractor target = InventorySlotInteractor.FindUnder(e);
            StashGridView stashTarget = target != null ? target.GetComponentInParent<StashGridView>() : null;
            if (stashTarget != null)
            {
                // 落在仓库格子上 → 送进仓库当前页对应格，不当成"拖出窗口"处理，
                // 不会走到下面的丢弃分支。
                HandleDropToStash(srcIndex, target.SlotIndex, stashTarget);
            }
            else if (target != null && target.SlotIndex != srcIndex)
            {
                HandleDrop(srcIndex, target.SlotIndex);
            }
            else
            {
                // 落在窗口之外 → 丢弃；窗口内空白 / 原格 → 无操作回弹。仓库窗口是屏幕上
                // 另一块独立区域，不属于背包自己的 PanelRect，但落在那上面（哪怕没精确
                // 命中某个格子，比如缝隙/还没解锁的格子）也不该被当成"拖到虚空里"丢弃。
                bool insidePanel = RectTransformUtility.RectangleContainsScreenPoint(
                    PanelRect, e.position, e.pressEventCamera) || IsInsideStashPanel(e);
                if (!insidePanel)
                    RequestDiscard(srcIndex);
            }

            // 无论结果如何都刷新一次：数据改了由 OnInventoryChanged 刷，没改（回弹）也要还原源格显示
            RefreshGrid();

            // 恢复色差静止快照
            SetChromaticSuspended(false);
        }

        private InventoryGridView _grid;
        private SkyPrisonInventoryChromatic _chromatic;
        private bool _chromaticResolved;

        private void SetChromaticSuspended(bool suspended)
        {
            if (!_chromaticResolved)
            {
                _chromatic = GetComponentInChildren<SkyPrisonInventoryChromatic>(true)
                          ?? GetComponentInParent<SkyPrisonInventoryChromatic>();
                _chromaticResolved = true;
            }
            if (_chromatic != null) _chromatic.SuspendForInteraction = suspended;
        }

        private InventorySlotView FindSlotView(int index)
        {
            if (_grid == null) _grid = GetComponentInChildren<InventoryGridView>(true);
            var views = GetComponentsInChildren<InventorySlotView>(true);
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].SlotIndex == index) return views[i];
            return null;
        }

        private void RefreshGrid()
        {
            if (_grid == null) _grid = GetComponentInChildren<InventoryGridView>(true);
            _grid?.Refresh();
        }

        // 落点为空 → 移过去；落点同种可堆叠 → 合并；否则 → 两格交换
        private void HandleDrop(int src, int dst)
        {
            InventoryRuntime inv = Inv;
            if (inv == null) return;

            IReadOnlyList<InventoryItemEntry> slots = inv.Slots;
            if (src < 0 || src >= slots.Count || dst < 0 || dst >= slots.Count) return;

            InventoryItemEntry s = slots[src];
            InventoryItemEntry d = slots[dst];
            if (s == null) return;

            if (d != null && s.definition == d.definition && s.definition.maxStackCount > 1 && !d.IsStackFull)
                inv.MergeSlots(src, dst);
            else
                inv.MoveSlot(src, dst); // 空位=移动过去；有物=交换

            SkyPrisonItemMaterialSoundTable.PlayDrop(s.definition.general.soundMaterial);
        }

        // 落点是仓库格子 → 跨 InventoryRuntime 转移到仓库当前页，不是背包内部换位/合并。
        // MoveSlot/MergeSlots 只能在同一个 InventoryRuntime 内部操作，所以走单独的
        // InventoryRuntime.TransferSlotTo。
        private void HandleDropToStash(int src, int dst, StashGridView stashGrid)
        {
            InventoryRuntime inv = Inv;
            InventoryRuntime stashInv = stashGrid.BoundInventory;
            if (inv == null || stashInv == null) return;
            if (src < 0 || src >= inv.Slots.Count) return;

            InventoryItemEntry s = inv.Slots[src];
            if (s == null) return;

            if (inv.TransferSlotTo(src, stashInv, dst))
                SkyPrisonItemMaterialSoundTable.PlayDrop(s.definition.general.soundMaterial);
        }

        /// <summary>落点是否在仓库窗口面板范围内——用来避免"拖到仓库窗口里但没精确
        /// 命中格子"被误判成"拖出窗口丢弃"。StashInventoryInteraction 就挂在
        /// StashPanel 上，它自己的 transform 就是仓库面板的 RectTransform。</summary>
        private static bool IsInsideStashPanel(PointerEventData e)
        {
            var stashInteraction = UnityEngine.Object.FindObjectOfType<StashInventoryInteraction>();
            if (stashInteraction == null) return false;
            var stashPanelRect = (RectTransform)stashInteraction.transform;
            return RectTransformUtility.RectangleContainsScreenPoint(stashPanelRect, e.position, e.pressEventCamera);
        }

        private void EnsureGhost()
        {
            if (_ghostCanvas != null) return;

            var go = new GameObject("DragGhost", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _ghostCanvas = go.AddComponent<Canvas>();
            _ghostCanvas.overrideSorting = true;
            _ghostCanvas.sortingOrder = 1200; // 高于背包(1100)及其色差层(1101)
            go.AddComponent<GraphicRaycaster>();

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            _ghostRt = (RectTransform)iconGo.transform;
            _ghostRt.sizeDelta = new Vector2(120f, 120f);
            _ghost = iconGo.AddComponent<Image>();
            _ghost.raycastTarget = false;         // 不挡落点 raycast
            _ghost.preserveAspect = true;
            var c = _ghost.color; c.a = 0.85f; _ghost.color = c;

            go.SetActive(false);
        }

        // ── 右键/单击操作菜单 ──────────────────────────────────────────────
        private GameObject _menuRoot;
        private RectTransform _menuBox;
        private RawImage _menuBlur;
        private CanvasGroup _panelCg;
        private RectTransform _menuUnderline;
        private Image _menuUnderlineImg;

        private readonly List<GameObject> _menuItems = new List<GameObject>();
        private readonly List<Text> _menuRowTexts = new List<Text>();
        private readonly List<bool> _menuRowEnabled = new List<bool>();
        private readonly List<Action> _menuRowActions = new List<Action>();

        public bool IsContextMenuOpen => _menuRoot != null && _menuRoot.activeSelf;

        private int   _hoveredRow = -1;
        private float _menuOpenT;      // 展开动画进度 0..1
        private int   _cdMenuRow = -1;       // 冷却行 index（-1=无）
        private string _cdMenuItemKey = "";  // 对应物品 itemKey
        private bool  _menuOpening;
        private float _menuTargetH;    // 完全展开高度
        private float _menuFlash;      // 点击闪烁 1..0
        private int   _flashRow = -1;

        private const float MenuWidth = 180f;
        private const float MenuRowH  = 46f;
        private const float MenuPadV  = 8f;
        private const float MenuOpenDur    = 0.14f;
        private const float MenuThinH      = 4f;
        private const float MenuColorSpeed = 18f;
        private const float MenuSlideSpeed = 22f;
        private const float MenuFlashDur   = 0.18f;
        private static readonly Color MenuTextNormal     = Color.white;
        private static readonly Color MenuTextDisabled   = new Color(1f, 1f, 1f, 0.30f);
        private static readonly Color MenuHoverGreen     = new Color(0.42f, 0.92f, 0.68f, 0.85f); // 半透明冷绿
        private static readonly Color MenuUnderlineGreen = new Color(0.42f, 0.92f, 0.68f, 1f);
        private static readonly Color MenuFlashGreen     = new Color(0.70f, 1f, 0.85f, 1f);

        // 上下文提示（底部提示条按当前设备自适应；键鼠/手柄各显示对应条目）
        private static readonly SkyPrisonWindowHint[] MenuHints =
        {
            SkyPrisonWindowHint.Icon("mouse/left", "点击", "选择"),
            SkyPrisonWindowHint.GamepadIcon("gamepad/up",     "↕", "选择"),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/a", "A", "确认"),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/b", "B", "取消"),
        };
        private static readonly SkyPrisonWindowHint[] PopupHints =
        {
            SkyPrisonWindowHint.Icon("mouse/left", "点击", "调整/确认"),
            SkyPrisonWindowHint.GamepadIcon("gamepad/left",   "←", "减少"),
            SkyPrisonWindowHint.GamepadIcon("gamepad/right",  "→", "增加"),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/a", "A", "确认"),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/b", "B", "取消"),
        };

        private void Update()
        {
            if (_menuRoot == null || !_menuRoot.activeSelf) return;

            // 窗口淡出时自动收起菜单
            if (_panelCg == null) _panelCg = GetComponentInParent<CanvasGroup>();
            if (_panelCg != null && _panelCg.alpha < 0.99f) { HideMenu(); return; }

            float dt = Time.unscaledDeltaTime;

            // 1) 展开动画：从细线高度缓动到完整高度（配合 RectMask2D 逐行揭示）
            if (_menuOpening)
            {
                _menuOpenT += dt / Mathf.Max(0.01f, MenuOpenDur);
                float t = Mathf.Clamp01(_menuOpenT);
                float e = 1f - (1f - t) * (1f - t); // ease-out
                _menuBox.sizeDelta = new Vector2(MenuWidth, Mathf.Lerp(MenuThinH, _menuTargetH, e));
                UpdateBlurUv(_menuBlur, _menuBox);
                if (t >= 1f) _menuOpening = false;
            }

            // 2) 点击闪烁衰减
            if (_menuFlash > 0f) { _menuFlash -= dt / MenuFlashDur; if (_menuFlash < 0f) _menuFlash = 0f; }

            // 3a) 冷却行倒计时文字实时刷新
            if (_cdMenuRow >= 0 && _cdMenuRow < _menuRowTexts.Count
                && !string.IsNullOrEmpty(_cdMenuItemKey)
                && _menuRowTexts[_cdMenuRow] != null)
            {
                if (ItemCooldownTracker.IsOnCooldown(_cdMenuItemKey, out float cdEnd))
                {
                    _menuRowTexts[_cdMenuRow].text =
                        string.Format("{0}（{1:F1}s）", L("ui_use", "使用"), cdEnd - Time.time);
                }
                else
                {
                    // 冷却结束：恢复文字 + 启用行（颜色、raycast、点击）
                    int r = _cdMenuRow;
                    _menuRowTexts[r].text = L("ui_use", "使用");
                    if (r < _menuRowEnabled.Count) _menuRowEnabled[r] = true;
                    if (r < _menuItems.Count && _menuItems[r] != null)
                    {
                        Image rowImg = _menuItems[r].GetComponent<Image>();
                        if (rowImg != null) rowImg.raycastTarget = true;

                        if (_menuItems[r].GetComponent<EventTrigger>() == null)
                        {
                            int capturedRow = r;
                            Action capturedAction = r < _menuRowActions.Count ? _menuRowActions[r] : null;
                            var trig = _menuItems[r].AddComponent<EventTrigger>();
                            AddTrig(trig, EventTriggerType.PointerEnter, () => _hoveredRow = capturedRow);
                            AddTrig(trig, EventTriggerType.PointerExit,  () => { if (_hoveredRow == capturedRow) _hoveredRow = -1; });
                            AddTrig(trig, EventTriggerType.PointerClick, () => OnMenuItemClicked(capturedRow, capturedAction));
                        }
                    }
                    _cdMenuRow = -1;
                    _cdMenuItemKey = "";
                }
            }

            // 3b) 文字颜色：悬停项半透明冷绿，其余白/灰；点击项叠加亮闪
            for (int i = 0; i < _menuRowTexts.Count; i++)
            {
                Text tx = _menuRowTexts[i];
                if (tx == null) continue;
                bool en = i < _menuRowEnabled.Count && _menuRowEnabled[i];
                Color target = !en ? MenuTextDisabled : (i == _hoveredRow ? MenuHoverGreen : MenuTextNormal);
                if (_menuFlash > 0f && i == _flashRow) target = Color.Lerp(target, MenuFlashGreen, _menuFlash);
                tx.color = Color.Lerp(tx.color, target, dt * MenuColorSpeed);
            }

            // 4) 下划线：滑向悬停项底边，连续移动 + 显隐淡入淡出
            if (_menuUnderlineImg != null)
            {
                bool show = _hoveredRow >= 0 && _hoveredRow < _menuRowEnabled.Count && _menuRowEnabled[_hoveredRow];
                Color baseCol = (_menuFlash > 0f && _flashRow == _hoveredRow) ? MenuFlashGreen : MenuUnderlineGreen;
                Color cur = _menuUnderlineImg.color;
                float aTarget = show ? baseCol.a : 0f;
                Color col = baseCol; col.a = Mathf.Lerp(cur.a, aTarget, dt * MenuColorSpeed);
                _menuUnderlineImg.color = col;

                if (show)
                {
                    float targetY = -(MenuPadV + (_hoveredRow + 1) * MenuRowH);
                    float y = _menuUnderline.anchoredPosition.y;
                    // 首次出现(几乎透明)直接吸附；已可见则连续滑动
                    y = cur.a < 0.05f ? targetY : Mathf.Lerp(y, targetY, dt * MenuSlideSpeed);
                    _menuUnderline.anchoredPosition = new Vector2(0f, y);
                }
            }
        }

        public void ShowContextMenu(int index, PointerEventData e)
            => ShowContextMenuCore(index, e.position, e.pressEventCamera, false);

        // 手柄：在指定屏幕位置（焦点格）打开菜单，并默认聚焦第一个可用项
        public void ShowContextMenuAt(int index, Vector2 screenPos)
            => ShowContextMenuCore(index, screenPos, null, true);

        private void ShowContextMenuCore(int index, Vector2 screenPos, Camera cam, bool focusFirst)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null) return;

            ItemDefinition def = entry.definition;
            float _cdEnd = 0f;
            bool onCooldown = def.cooldown > 0f && ItemCooldownTracker.IsOnCooldown(def.itemKey, out _cdEnd);

            // 复活道具：仅在濒死决策阶段可用
            GameObject _menuPlayerGo = SkyPrisonPlayerAuthority.Instance != null
                ? SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject ?? gameObject
                : gameObject;
            bool reviveOnlyBlocked = def.general.isReviveItem &&
                (_menuPlayerGo.GetComponentInParent<PlayerDeathDecisionController>() is PlayerDeathDecisionController _pdc
                    ? !_pdc.IsWaitingForChoice : true);

            // 满 HP/LP 拦截
            string fullBlockReason = ItemEffectExecutor.GetBlockReason(def.effects, _menuPlayerGo);

            bool canUse     = def.IsUsable && !onCooldown && !reviveOnlyBlocked && fullBlockReason == null;
            bool canSplit   = def.maxStackCount > 1 && entry.count > 1 && inv.UsedSlots < inv.Capacity;
            bool canDiscard = entry.CanDiscard;

            string useLabel = onCooldown
                ? string.Format("{0}（{1:F1}s）", L("ui_use", "使用"), _cdEnd - Time.time)
                : fullBlockReason != null ? fullBlockReason
                : reviveOnlyBlocked ? "濒死专用"
                : L("ui_use", "使用");

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            EnsureMenu();
            ClearMenuItems();

            _cdMenuRow     = onCooldown ? 0 : -1;
            _cdMenuItemKey = onCooldown ? def.itemKey : "";

            // 装备/卸装判断——按"这一件具体实例是不是已经装备的那件"判断，不能只按
            // definition比对：背包里如果还留着一把跟已装备武器同款（同ItemDefinition）
            // 的备用/备份武器，按definition比只会把这把备用武器也误判成"已装备"，
            // 导致它在背包里被错误地调暗、"装备"选项被禁用，实际上它是完全独立的一件，
            // 应该能正常装备上去换掉当前那把（换装换耐久之类场景）。已装备的物品本来
            // 就已经从 inv.Slots 里移除了，背包网格里能枚举到的条目不可能是"当前
            // 已装备的那一个实例"，直接按引用比对entry本身即可。
            var eqRuntime = EquipmentRuntime.Instance;
            bool isEquipItem  = def.IsEquipmentItem && eqRuntime != null;
            bool alreadyEquipped = isEquipItem && eqRuntime.IsEquipped(def.equipment.slot)
                                   && eqRuntime.GetEquipped(def.equipment.slot) == entry;
            bool canEquip   = isEquipItem && !alreadyEquipped;
            bool canUnequip = isEquipItem && alreadyEquipped;
            if (isEquipItem)
                Debug.Log($"[SkyPrisonInventoryInteraction] ShowMenu 装备判断：item={def.itemKey}, " +
                    $"slot={def.equipment.slot}, IsEquipped(slot)={eqRuntime.IsEquipped(def.equipment.slot)}, " +
                    $"GetEquipped(slot)==entry: {eqRuntime.GetEquipped(def.equipment.slot) == entry}, " +
                    $"alreadyEquipped={alreadyEquipped}, canEquip={canEquip}");

            // 角色面板点了某个快捷物品行呼出背包时，_grid 会带着这个强制过滤的槽位序号——
            // 这时候点物品弹出的菜单额外多一行"指定为快捷物品"，跟装备的"装备"行是同一个
            // 位置角色不同功能，复用同一套菜单，不用为快捷物品单独再做一套UI。
            // _grid 是懒加载字段（跟 FindSlotView/RefreshGrid 一样），这里必须先确保
            // 已经解析过，不然菜单打开时机可能早于任何触发过懒加载的调用，_grid 还是
            // null，导致这一行永远不出现——刚测出来的就是这个问题。
            if (_grid == null) _grid = GetComponentInChildren<InventoryGridView>(true);
            int? forcedQuickSlot = _grid != null ? _grid.ForcedQuickSlotIndex : null;
            // def.category==Consumable 单看不够——那是枚举默认值(0)，装备类物品的
            // category 字段从没被归一化过，随便一把没手动改过 category 的武器都会顶着
            // 这个默认值，必须先确认是一般道具（IsGeneralItem）才能再看 category。
            bool canAssignQuickSlot = forcedQuickSlot.HasValue
                && def.IsGeneralItem && def.category == ItemCategory.Consumable && !def.general.isReviveItem;

            // 角色面板点了某个装备槽（武器/防具）呼出背包时同理——只保留"装备"这一个目的，
            // 跟快捷物品行呼出背包时只保留"指定为快捷物品"是同一套思路。
            EquipmentSlotType? forcedEquipSlot = _grid != null ? _grid.ForcedEquipSlot : null;
            bool canAssignEquipSlot = forcedEquipSlot.HasValue && canEquip;

            int rows = 0;
            if (forcedQuickSlot.HasValue)
            {
                // 背包是被快捷物品行呼出来的：菜单只保留"指定为快捷物品"这一个目的，
                // 使用/装备/拆分/丢弃这些跟当前操作无关，不显示，减少误操作。
                int capturedSlot = forcedQuickSlot.Value;
                ItemDefinition capturedDef = def;
                rows += AddMenuItem(L("ui_assign_quickslot", "指定为快捷物品"), canAssignQuickSlot, rows,
                    () => AssignQuickSlot(capturedSlot, capturedDef));
            }
            else if (forcedEquipSlot.HasValue)
            {
                // 背包是被装备槽呼出来的：菜单只保留"装备"这一个目的，使用/卸装/拆分/丢弃
                // 都跟当前操作无关，不显示。
                int capturedIndex = index;
                rows += AddMenuItem(L("ui_equip", "装备"), canAssignEquipSlot, rows, () => EquipItem(capturedIndex));
            }
            else
            {
                rows += AddMenuItem(useLabel,                                    canUse,      rows, () => UseItem(index));
                if (isEquipItem)
                {
                    // 装备/卸装其实是同一个位置的两种互斥状态（跟快捷物品那个单一目的的
                    // "指定为快捷物品"行是一个道理），不用摆两行——已装备就显示"卸装"，
                    // 没装备就显示"装备"，点哪个都只对应当前唯一合理的那个动作。
                    int capturedIndex = index;
                    string toggleLabel = alreadyEquipped ? L("ui_unequip", "卸装") : L("ui_equip", "装备");
                    bool canToggle = alreadyEquipped ? canUnequip : canEquip;
                    rows += AddMenuItem(toggleLabel, canToggle, rows, () =>
                    {
                        if (alreadyEquipped) UnequipItem(def.equipment.slot);
                        else EquipItem(capturedIndex);
                    });
                }
                rows += AddMenuItem(L("ui_split_title", "拆分"),  canSplit,   rows, () => RequestSplit(index));
                rows += AddMenuItem(L("ui_discard_title", "丢弃"), canDiscard, rows, () => RequestDiscard(index));
            }

            float boxH = rows * MenuRowH + MenuPadV * 2f;

            // 展开动画初始化：从细线高度开始，Update 缓动到完整高度
            _menuTargetH = boxH;
            _menuOpenT = 0f; _menuOpening = true;
            _hoveredRow = focusFirst ? FirstEnabledRow() : -1; // 手柄默认聚焦首个可用项
            _menuFlash = 0f; _flashRow = -1;
            _menuBox.sizeDelta = new Vector2(MenuWidth, MenuThinH);
            if (_menuUnderlineImg != null) _menuUnderlineImg.color = new Color(MenuUnderlineGreen.r, MenuUnderlineGreen.g, MenuUnderlineGreen.b, 0f);
            if (_menuUnderline != null) _menuUnderline.SetAsLastSibling(); // 浮在选项之上

            PositionMenuAt(screenPos, cam, boxH);
            _menuRoot.SetActive(true);
            _menuRoot.transform.SetAsLastSibling();
            UpdateBlurUv(_menuBlur, _menuBox);
            SkyPrisonWindowHintBar.PushContext(MenuHints);
        }

        // 把菜单左上角放在屏幕点处，并夹回窗口内
        private void PositionMenuAt(Vector2 screenPos, Camera cam, float boxH)
        {
            // 用菜单根矩形（pivot 0.5、铺满面板）换算，避免面板自身 pivot 非 0.5 造成偏移
            RectTransform refRect = (RectTransform)_menuBox.parent;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                refRect, screenPos, cam, out local);

            float halfW = refRect.rect.width  * 0.5f;
            float halfH = refRect.rect.height * 0.5f;

            float x = Mathf.Clamp(local.x, -halfW, halfW - MenuWidth);
            float y = Mathf.Clamp(local.y, -halfH + boxH, halfH);

            _menuBox.anchoredPosition = new Vector2(x, y);
        }

        private int FirstEnabledRow()
        {
            for (int i = 0; i < _menuRowEnabled.Count; i++)
                if (_menuRowEnabled[i]) return i;
            return -1;
        }

        // ── 手柄菜单导航 ──────────────────────────────────────────────────────
        public void MenuNavigate(int delta)
        {
            if (!IsContextMenuOpen || _menuRowEnabled.Count == 0 || delta == 0) return;
            int n = _menuRowEnabled.Count;
            int i = _hoveredRow;
            while (true)
            {
                int ni = i + delta;
                if (ni < 0 || ni >= n) break;
                i = ni;
                if (_menuRowEnabled[i]) { _hoveredRow = i; SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); return; }
            }
            // 到边界：禁止音效
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        }

        public void MenuConfirm()
        {
            if (!IsContextMenuOpen) return;
            if (_hoveredRow < 0 || _hoveredRow >= _menuRowActions.Count) return;
            if (!_menuRowEnabled[_hoveredRow]) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            OnMenuItemClicked(_hoveredRow, _menuRowActions[_hoveredRow]);
        }

        public void MenuCancel()
        {
            if (!IsContextMenuOpen) return;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
            HideMenu();
        }

        private int AddMenuItem(string label, bool enabled, int row, Action onClick)
        {
            Font font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;
            int rowIndex = _menuRowTexts.Count; // 实际行号（与 lists 对齐）

            RectTransform rt = NewRect("Item_" + label, _menuBox,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(MenuPadV + row * MenuRowH)), new Vector2(0f, MenuRowH));
            rt.pivot = new Vector2(0.5f, 1f);

            // 透明底：只接收点击，不显示填充色
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = enabled;

            Text t = NewText("Label", rt, font, 22, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            t.text = label;
            t.color = enabled ? MenuTextNormal : MenuTextDisabled;

            if (enabled)
            {
                // 悬停 + 点击全用 EventTrigger，避免父层 Button（closeBtn）拦截 onClick 事件
                var trig = rt.gameObject.AddComponent<EventTrigger>();
                AddTrig(trig, EventTriggerType.PointerEnter, () => _hoveredRow = rowIndex);
                AddTrig(trig, EventTriggerType.PointerExit,  () => { if (_hoveredRow == rowIndex) _hoveredRow = -1; });
                AddTrig(trig, EventTriggerType.PointerClick, () => OnMenuItemClicked(rowIndex, onClick));
            }

            _menuItems.Add(rt.gameObject);
            _menuRowTexts.Add(t);
            _menuRowEnabled.Add(enabled);
            _menuRowActions.Add(onClick); // 始终存，disabled 行冷却结束后也能绑定
            return 1;
        }

        // _menuRoot 上的透明遮罩被点击时：悬停行有效则执行，否则仅关闭菜单
        private void OnMenuRootClick()
        {
            int hitRow = HitTestMenuRow(Input.mousePosition);
            if (hitRow >= 0 && hitRow < _menuRowEnabled.Count)
            {
                bool enabled = _menuRowEnabled[hitRow];
                SkyPrisonSystemSEPlayer.Play(enabled ? SkyPrisonSystemSEType.Confirm : SkyPrisonSystemSEType.Forbidden);
                if (enabled)
                {
                    Action act = hitRow < _menuRowActions.Count ? _menuRowActions[hitRow] : null;
                    OnMenuItemClicked(hitRow, act);
                    return;
                }
            }
            HideMenu();
        }

        // 用鼠标屏幕坐标逐行比对 RectTransform，绕过 RectMask2D 对事件的裁剪
        private int HitTestMenuRow(Vector2 screenPos)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            for (int i = 0; i < _menuItems.Count; i++)
            {
                if (_menuItems[i] == null) continue;
                var rt = _menuItems[i].GetComponent<RectTransform>();
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam))
                    return i;
            }
            return -1;
        }

        // 点击：先亮闪一下（激活感）再关闭并执行
        private void OnMenuItemClicked(int row, Action onClick)
        {
            bool enabled = row < _menuRowEnabled.Count && _menuRowEnabled[row];
            SkyPrisonSystemSEPlayer.Play(enabled ? SkyPrisonSystemSEType.Confirm : SkyPrisonSystemSEType.Forbidden);
            // 之前这里播完"禁止"音效之后没有 return，还是照样往下执行 onClick——禁用
            // 只是摆样子（灰字+拒绝音效），点击动作从来没有被真正拦住过。
            if (!enabled) return;
            _menuFlash = 1f; _flashRow = row;
            StartCoroutine(FlashThenInvoke(onClick));
        }

        private IEnumerator FlashThenInvoke(Action onClick)
        {
            yield return new WaitForSecondsRealtime(0.12f);
            HideMenu();
            onClick?.Invoke();
        }

        private static void AddTrig(EventTrigger trig, EventTriggerType type, Action cb)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => cb());
            trig.triggers.Add(entry);
        }

        private void ClearMenuItems()
        {
            for (int i = 0; i < _menuItems.Count; i++)
                if (_menuItems[i] != null) Destroy(_menuItems[i]);
            _menuItems.Clear();
            _menuRowTexts.Clear();
            _menuRowEnabled.Clear();
            _menuRowActions.Clear();
            _hoveredRow = -1;
            _cdMenuRow = -1;
            _cdMenuItemKey = "";
        }

        private void HideMenu()
        {
            if (_menuRoot != null) _menuRoot.SetActive(false);
            SkyPrisonWindowHintBar.PopContext();
        }

        private void EnsureMenu()
        {
            if (_menuRoot != null) return;

            // 全面板透明遮罩：点菜单外即关闭
            RectTransform root = NewRect("ContextMenu", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _menuRoot = root.gameObject;
            var backdrop = _menuRoot.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0f); // 透明但可挡 raycast
            backdrop.raycastTarget = true;
            var closeBtn = _menuRoot.AddComponent<Button>();
            closeBtn.transition = Selectable.Transition.None;
            closeBtn.onClick.AddListener(OnMenuRootClick);

            // 菜单框（锚定面板中心，pivot 左上）：磨砂背板 + 四角角标，与丢弃弹窗一致
            _menuBox = NewRect("Box", _menuRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MenuWidth, MenuRowH * 3f));
            _menuBox.pivot = new Vector2(0f, 1f);
            _menuBlur = BuildFrostedBackground(_menuBox);
            AddEdge(_menuBox, "Edge_L", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, 0f)); // 左侧白色描边
            _menuBox.gameObject.AddComponent<RectMask2D>(); // 展开动画时裁剪未露出的选项

            // 共享下划线：滑向悬停项，连续移动（不每项一条）
            var ulGo = new GameObject("Underline", typeof(RectTransform));
            ulGo.transform.SetParent(_menuBox, false);
            _menuUnderline = (RectTransform)ulGo.transform;
            _menuUnderline.anchorMin = new Vector2(0f, 1f); _menuUnderline.anchorMax = new Vector2(1f, 1f);
            _menuUnderline.pivot = new Vector2(0.5f, 1f);
            _menuUnderline.anchoredPosition = Vector2.zero;
            _menuUnderline.sizeDelta = new Vector2(0f, 2f);
            _menuUnderlineImg = ulGo.AddComponent<Image>();
            _menuUnderlineImg.color = new Color(MenuUnderlineGreen.r, MenuUnderlineGreen.g, MenuUnderlineGreen.b, 0f);
            _menuUnderlineImg.raycastTarget = false;

            _menuRoot.SetActive(false);
        }

        private void UseItem(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null || !entry.definition.IsUsable) return;

            GameObject playerGo = SkyPrisonPlayerAuthority.Instance != null
                ? SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject ?? gameObject
                : gameObject;

            // 复活道具只在濒死决策阶段可用
            if (entry.definition.general.isReviveItem)
            {
                PlayerDeathDecisionController dc = playerGo.GetComponentInParent<PlayerDeathDecisionController>();
                if (dc == null || !dc.IsWaitingForChoice)
                {
                    Debug.Log($"[Inventory] {entry.definition.displayName} 只能在濒死状态使用");
                    return;
                }
            }

            // 满 HP / LP 时拦截纯回复道具
            string blockReason = ItemEffectExecutor.GetBlockReason(entry.definition.effects, playerGo);
            if (blockReason != null)
            {
                Debug.Log($"[Inventory] {entry.definition.displayName} 无法使用：{blockReason}");
                return;
            }

            // 冷却检查
            string key = entry.definition.itemKey;
            if (entry.definition.cooldown > 0f)
            {
                if (ItemCooldownTracker.IsOnCooldown(key, out float endTime))
                {
                    float remaining = endTime - Time.time;
                    Debug.Log($"[Inventory] {entry.definition.displayName} 冷却中（剩余 {remaining:F1} 秒）");
                    return;
                }
                ItemCooldownTracker.StartCooldown(key, entry.definition.cooldown);
            }
            ItemEffectExecutor.Execute(entry.definition.effects, playerGo, entry.definition);
            if (entry.definition.general.consumeOnUse)
                inv.DiscardSlot(index, 1);
        }

        // ── 拆分 ──────────────────────────────────────────────────────────
        // 右键快速拆分：直接拆 1 个到第一个空格，不弹数量弹窗
        private void EquipItem(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count)
            {
                Debug.LogWarning($"[SkyPrisonInventoryInteraction] EquipItem 拒绝：inv={inv != null}, index={index}");
                return;
            }

            InventoryItemEntry entry = inv.Slots[index];
            var eqRuntime = EquipmentRuntime.Instance;
            if (eqRuntime == null)
            {
                Debug.LogWarning("[SkyPrisonInventoryInteraction] EquipItem 拒绝：EquipmentRuntime.Instance 为空。");
                return;
            }
            Debug.Log($"[SkyPrisonInventoryInteraction] EquipItem 被调用：index={index}, item={entry?.definition?.itemKey}");

            // 角色面板点"副武器"槽呼出背包时 _grid.ForcedEquipSlot 会带着
            // EquipmentSlotType.WeaponSecondary——必须原样传给 TryEquipFromInventory，
            // 不传的话武器永远只会装进它自己 equipment.slot 写死的那个槽（比如铲子
            // 固定是 Weapon），没法把同一款武器分别塞到主/副武器槽。
            if (_grid == null) _grid = GetComponentInChildren<InventoryGridView>(true);
            EquipmentSlotType? targetSlotOverride = _grid != null ? _grid.ForcedEquipSlot : null;

            if (!eqRuntime.TryEquipFromInventory(inv, entry, targetSlotOverride))
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                return;
            }

            HideMenu();
            // 武器/防具各自专属音效，不共用一个通用 Confirm——武器上手需要更有分量感的
            // 独立反馈，防具穿上也该是自己的质感，不是同一个"确认"音效糊弄过去。
            bool isWeaponSlot = entry.definition?.equipment != null
                && (entry.definition.equipment.slot == EquipmentSlotType.Weapon
                    || entry.definition.equipment.slot == EquipmentSlotType.WeaponSecondary);
            SkyPrisonSystemSEPlayer.Play(isWeaponSlot ? SkyPrisonSystemSEType.EquipWeapon : SkyPrisonSystemSEType.EquipArmor);

            // 从角色面板点某个具体装备槽呼出背包时（ForcedEquipSlot 有值），这一步就是
            // 唯一目的——选完直接关背包回角色面板，跟快捷物品那个单一目的的流程一样。
            // 从背包自己（全部/装备 Tab）直接装备时不关——那种场景经常要连着挑好几件。
            if (_grid == null) _grid = GetComponentInChildren<InventoryGridView>(true);
            if (_grid != null && _grid.ForcedEquipSlot.HasValue)
            {
                var windowController = GetComponentInParent<InventoryWindowController>();
                windowController?.CloseWindow();
            }
        }

        // 快捷物品绑定的是 ItemDefinition（物品种类），不是这一个具体堆叠——物品本身
        // 留在背包里不动，跟装备（物理从背包挪走）是两回事，见 QuickSlotRuntime 头注释。
        private void AssignQuickSlot(int slotIndex, ItemDefinition definition)
        {
            if (QuickSlotRuntime.Instance == null || definition == null) return;
            QuickSlotRuntime.Instance.AssignSlot(slotIndex, definition);
            HideMenu();
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.QuickSlotAssign);

            // 指定完直接关背包，回到角色面板——跟装备不一样，装备之后玩家经常还要继续
            // 挑下一件装备，快捷物品这边一次只选一个槽，选完这一步操作就完整结束了。
            var windowController = GetComponentInParent<InventoryWindowController>();
            windowController?.CloseWindow();
        }

        private void UnequipItem(EquipmentSlotType slot)
        {
            var eqRuntime = EquipmentRuntime.Instance;
            if (eqRuntime == null) return;

            InventoryRuntime inv = Inv;
            if (!eqRuntime.TryUnequipToInventory(inv, slot))
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                return;
            }

            HideMenu();
            bool isWeaponSlot = slot == EquipmentSlotType.Weapon || slot == EquipmentSlotType.WeaponSecondary;
            SkyPrisonSystemSEPlayer.Play(isWeaponSlot ? SkyPrisonSystemSEType.UnequipWeapon : SkyPrisonSystemSEType.UnequipArmor);
        }

        public void QuickSplitOne(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;
            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null || entry.definition.maxStackCount <= 1 || entry.count <= 1
                || inv.UsedSlots >= inv.Capacity)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                return;
            }
            inv.SplitSlot(index, 1);
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            SkyPrisonItemMaterialSoundTable.PlayPickup(entry.definition.general.soundMaterial);
        }

        public void RequestSplit(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null) return;
            if (entry.definition.maxStackCount <= 1 || entry.count <= 1) return; // 不可拆
            if (inv.UsedSlots >= inv.Capacity) return;                           // 没空格放拆出的

            int max = entry.count - 1;
            ShowAmountPopup(L("ui_split_title", "拆分"), entry.definition.GetLocalizedDisplayName(), max, Mathf.Max(1, max / 2),
                L("ui_split_confirm", "确认"), amt => inv.SplitSlot(index, amt));
        }

        // ── 本地化取词（字典表 UILocalizationTable）─────────────────────────
        private UILocalizationTable _locTable;
        private bool _locResolved;

        private string L(string key, string fallback)
        {
            if (!_locResolved)
            {
                var loc = GetComponentInParent<SkyPrisonInventoryTextLocalizer>()
                       ?? GetComponentInChildren<SkyPrisonInventoryTextLocalizer>(true);
                _locTable = loc != null ? loc.Table : null;
                _locResolved = true;
            }
            return _locTable != null ? _locTable.Get(key, fallback) : fallback;
        }

        // ── 丢弃 ──────────────────────────────────────────────────────────
        public void RequestDiscard(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null) return;
            if (!entry.CanDiscard) return; // 重要 / 不可丢弃物品直接拦截

            ShowAmountPopup(L("ui_discard_title", "丢弃"), entry.definition.GetLocalizedDisplayName(), entry.count, entry.count,
                L("ui_discard_confirm", "确认"), amt =>
                {
                    ItemDefinition def = entry.definition;
                    inv.DiscardSlot(index, amt);
                    Vector3 dropPos  = GetPlayerDropPosition();
                    var     dropped  = LootDropWorldObject.SpawnDrop(def, amt, dropPos);
                    if (dropped != null)
                    {
                        // 从角色腰部高度抛向落点
                        GameObject unit  = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
                        Vector3    tossOrigin = unit != null
                            ? unit.transform.position + Vector3.up * 0.9f
                            : dropPos + Vector3.up * 0.9f;
                        // 落点用 SpawnDrop 实际设置的位置（含随机偏移）
                        LootDropTossEffect.Apply(dropped.gameObject, tossOrigin, dropped.transform.position);
                    }
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.DropToGround);
                });
        }

        // ── 丢弃位置 ─────────────────────────────────────────────────────────
        private static Vector3 GetPlayerDropPosition()
        {
            GameObject unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
            if (unit == null) return Vector3.zero;
            Vector3 forward = unit.transform.forward;
            forward.y = 0f;
            return unit.transform.position + forward.normalized * 0.8f;
        }

        // ── 数量弹窗（拆分 / 丢弃共用）────────────────────────────────────
        private GameObject _popupRoot;
        private RectTransform _popupBox;
        private RawImage _popupBlur;
        private Text _popupTitle, _popupName, _popupAmount, _popupConfirmText, _popupCancelText;
        private int _popupMin, _popupMax, _popupValue;
        private Action<int> _popupConfirm;

        private void ShowAmountPopup(string title, string itemName, int max, int initial, string confirmText, Action<int> onConfirm)
        {
            EnsurePopup();
            _popupMin = 1;
            _popupMax = Mathf.Max(1, max);
            _popupValue = Mathf.Clamp(initial, _popupMin, _popupMax);
            _popupConfirm = onConfirm;
            _popupTitle.text = title;
            _popupName.text = itemName;
            if (_popupConfirmText != null) _popupConfirmText.text = confirmText;
            if (_popupCancelText != null)  _popupCancelText.text  = L("ui_discard_cancel", "取消");
            RefreshPopupAmount();
            _popupRoot.SetActive(true);
            _popupRoot.transform.SetAsLastSibling();
            UpdateBlurUv(_popupBlur, _popupBox);
            SkyPrisonWindowHintBar.PushContext(PopupHints); // 按盒子当前屏幕矩形采样磨砂，对齐背后场景
        }

        // 磨砂背板：复用面板那张 BlurBackground RawImage（同纹理/材质），按盒子屏幕矩形采样 → 遮住背后重叠内容
        // 返回磨砂 RawImage（供调用方在显示时更新 uv）。
        private RawImage BuildFrostedBackground(RectTransform box)
        {
            RawImage blur = null;
            RawImage panelBlur = FindPanelBlurImage();
            if (panelBlur != null && panelBlur.texture != null)
            {
                var blurGo = new GameObject("Blur", typeof(RectTransform));
                blurGo.transform.SetParent(box, false);
                RectTransform brt = (RectTransform)blurGo.transform;
                brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
                brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
                blur = blurGo.AddComponent<RawImage>();
                blur.texture = panelBlur.texture;
                blur.material = panelBlur.material;
                blur.color = new Color(panelBlur.color.r, panelBlur.color.g, panelBlur.color.b, 1f); // alpha=1 遮住格子
                blur.raycastTarget = true; // 吸收盒子内空白点击
            }
            else
            {
                var fb = box.gameObject.AddComponent<Image>(); // 无磨砂纹理时深色兜底，仍遮住格子
                fb.color = new Color(0.08f, 0.09f, 0.10f, 1f);
            }

            // 极淡冷调叠层，与面板气质一致
            var tintGo = new GameObject("Tint", typeof(RectTransform));
            tintGo.transform.SetParent(box, false);
            RectTransform trt = (RectTransform)tintGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tint = tintGo.AddComponent<Image>();
            tint.color = new Color(0.06f, 0.08f, 0.10f, 0.18f);
            tint.raycastTarget = false;

            return blur;
        }

        private void UpdateBlurUv(RawImage blur, RectTransform box)
        {
            if (blur == null || box == null) return;
            Canvas.ForceUpdateCanvases();
            var c = new Vector3[4];
            box.GetWorldCorners(c); // Overlay 下世界角点即屏幕像素：0=左下 2=右上
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            blur.uvRect = new Rect(
                c[0].x / sw, c[0].y / sh,
                (c[2].x - c[0].x) / sw, (c[2].y - c[0].y) / sh);
        }

        private RawImage FindPanelBlurImage()
        {
            Transform t = FindDeep(transform, "BlurBackground");
            return t != null ? t.GetComponent<RawImage>() : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void RefreshPopupAmount() => _popupAmount.text = $"{_popupValue} / {_popupMax}";

        // ── 手柄：数量弹窗（拆分/丢弃）接口 ────────────────────────────────
        public bool IsAmountPopupOpen => _popupRoot != null && _popupRoot.activeSelf;
        public void AmountPopupStep(int delta)
        {
            int before = _popupValue;
            StepPopup(delta);
            if (_popupValue != before) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            else                       SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        }
        public void AmountPopupConfirm()
        {
            if (!IsAmountPopupOpen) return;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            ConfirmPopup();
        }
        public void AmountPopupCancel()
        {
            if (!IsAmountPopupOpen) return;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
            HidePopup();
        }

        private void StepPopup(int delta)
        {
            _popupValue = Mathf.Clamp(_popupValue + delta, _popupMin, _popupMax);
            RefreshPopupAmount();
        }

        private void ConfirmPopup()
        {
            Action<int> cb = _popupConfirm;
            int v = _popupValue;
            HidePopup();
            cb?.Invoke(v);
        }

        private void HidePopup()
        {
            _popupConfirm = null;
            if (_popupRoot != null) _popupRoot.SetActive(false);
            SkyPrisonWindowHintBar.PopContext();
        }

        private void EnsurePopup()
        {
            if (_popupRoot != null) return;

            Font font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;

            // 全面板遮罩（挡交互）
            RectTransform popupRect = NewRect("AmountPopup", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _popupRoot = popupRect.gameObject;
            var backdrop = _popupRoot.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.55f);
            backdrop.raycastTarget = true;

            // 中间小框：纯黑白灰、矩形（无圆角）
            RectTransform box = NewRect("Box", _popupRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 240f));
            _popupBox = box;
            _popupBlur = BuildFrostedBackground(box);            // 磨砂背板：复用面板磨砂纹理遮住背后重叠内容
            AddCornerBrackets(box);                               // 四角角标

            _popupTitle = NewText("Title", box, font, 26, TextAnchor.MiddleCenter,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -16f), new Vector2(-24f, 36f));
            _popupTitle.color = Color.white;

            _popupName = NewText("Name", box, font, 20, TextAnchor.MiddleCenter,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -56f), new Vector2(-24f, 28f));
            _popupName.color = new Color(0.70f, 0.70f, 0.72f, 1f); // 灰

            _popupAmount = NewText("Amount", box, font, 30, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(160f, 44f));
            _popupAmount.color = Color.white;

            MakeButton("-", box, font, new Vector2(0.5f, 0.5f), new Vector2(-120f, 6f), () => StepPopup(-1));
            MakeButton("+", box, font, new Vector2(0.5f, 0.5f), new Vector2(120f, 6f), () => StepPopup(1));
            _popupCancelText  = MakeButton("取消", box, font, new Vector2(0.5f, 0f), new Vector2(-90f, 36f), HidePopup);
            _popupConfirmText = MakeButton("确认", box, font, new Vector2(0.5f, 0f), new Vector2(90f, 36f), ConfirmPopup);

            _popupRoot.SetActive(false);
        }

        // ── UI 工具 ───────────────────────────────────────────────────────
        private static RectTransform NewRect(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            return rt;
        }

        private static Text NewText(string name, Transform parent, Font font, int size, TextAnchor align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            RectTransform rt = NewRect(name, parent, anchorMin, anchorMax, anchoredPos, sizeDelta);
            var t = rt.gameObject.AddComponent<Text>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // 返回按钮上的文字组件（供调用方后续按语言更新文案）
        // 风格遵循物品窗口：透明内部（不显示重叠）+ 白色外框，悬停半透明冷绿、点击连框闪烁。
        private Text MakeButton(string label, RectTransform parent, Font font, Vector2 anchor, Vector2 anchoredPos, Action onClick)
        {
            RectTransform rt = NewRect("Btn_" + label, parent, anchor, anchor, anchoredPos, new Vector2(120f, 44f));

            // 透明底：负责接收点击，但 alpha=0 → 反馈组件会跳过它，不被染绿、不挡背景
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = true;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None; // 视觉反馈交给 SkyPrisonUIButtonFeedback
            btn.onClick.AddListener(() => onClick());

            AddWhiteFrame(rt, 1f); // 白色外框（细）

            Text t = NewText("Label", rt, font, 22, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            t.text = label;                                    // 之前漏了赋值 → 按钮一直空白
            t.color = Color.white;

            // 悬停冷绿半透明 + 点击亮闪（连白框带文字一起），与背包激活交互一致
            SkyPrison.Runtime.UI.SkyPrisonUIButtonFeedback.Attach(rt.gameObject);
            return t;
        }

        // 4 条白线组成的矩形外框（内部透明）
        private static void AddWhiteFrame(RectTransform parent, float thickness = 2f)
        {
            AddEdge(parent, "Frame_T", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness));
            AddEdge(parent, "Frame_B", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness));
            AddEdge(parent, "Frame_L", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f));
            AddEdge(parent, "Frame_R", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f));
        }

        private static void AddEdge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            // 水平边设宽度为 0（横向拉满）、给高度；竖直边相反
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.85f); // 白框
            img.raycastTarget = false;
        }

        // 四角 L 形角标：每角一条横臂 + 一条竖臂，从角点向内延伸
        private static void AddCornerBrackets(RectTransform parent, float arm = 26f, float thickness = 2f)
        {
            Vector2[] corners = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            foreach (var c in corners)
            {
                AddCornerArm(parent, "Corner_H", c, new Vector2(arm, thickness));
                AddCornerArm(parent, "Corner_V", c, new Vector2(thickness, arm));
            }
        }

        // anchor=pivot=角点 → 臂自动从该角向内生长
        private static void AddCornerArm(RectTransform parent, string name, Vector2 corner, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = corner; rt.anchorMax = corner; rt.pivot = corner;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.9f);
            img.raycastTarget = false;
        }
    }
}
