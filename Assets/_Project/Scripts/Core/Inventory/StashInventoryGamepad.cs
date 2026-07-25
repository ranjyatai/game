using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 仓库手柄操作（挂在 StashPanel 上，与 StashInventoryInteraction 同处）。
    /// 跟背包 SkyPrisonInventoryGamepad 同一套输入方案，但按键分工不一样——仓库比
    /// 背包多一组"翻页(1234)"，跟"筛选分类"抢同一对 LB/RB，用户确认过的分配方案：
    /// - 左摇杆/十字键：移动焦点格（冷绿高亮）；十字键左右移到格子横向边界、再往同一
    ///   方向按一次，切换筛选分类（而不是原地播拒绝音效）
    /// - A：抓取焦点格物品 / 放下
    /// - B：关闭窗口（抓取中则取消抓取）
    /// - X：整理
    /// - L2/R2：循环切换排序字段 / 切换升降序
    /// - LB/RB：上一页/下一页（1234），跟背包的"LB/RB=切筛选"刻意不同
    /// 仅在仓库打开（CanvasGroup.alpha≈1）时响应。鼠标操作不受影响，可共存。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StashInventoryGamepad : MonoBehaviour
    {
        // 0-5 在 Xbox / PS4 原生 HID 两种模式下一致
        private const KeyCode BtnConfirm = KeyCode.JoystickButton0; // A / ×
        private const KeyCode BtnMenu    = KeyCode.JoystickButton1; // B / ○
        private const KeyCode BtnX       = KeyCode.JoystickButton2; // X / □ → 整理
        private const KeyCode BtnLB      = KeyCode.JoystickButton4; // LB / L1 → 上一页
        private const KeyCode BtnRB      = KeyCode.JoystickButton5; // RB / R1 → 下一页

        // 一键"直接送回背包"——跟背包那边共用同一个物理键(原角色面板键，已经腾出来)。
        private const KeyCode BtnQuickSendToInventory = KeyCode.JoystickButton10;

        // 6+ 在 Xbox 和 PS4 原生 HID 不同，运行时动态决定
        private KeyCode _btnBack;
        private KeyCode _btnL2Btn;
        private KeyCode _btnR2Btn;
        private bool    _ps4Native;

        private const string AxisL2 = "Axis 9";
        private const string AxisR2 = "Axis 10";
        private const float  TriggerThreshold = 0.5f;
        private bool _l2WasDown, _r2WasDown;

        private static bool IsPS4NativeHID()
        {
            foreach (string n in Input.GetJoystickNames())
                if (!string.IsNullOrEmpty(n) &&
                    (n.IndexOf("Wireless Controller", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     n.IndexOf("DualShock",           System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     n.IndexOf("PS4",                 System.StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            return false;
        }

        private void ResolveControllerLayout()
        {
            _ps4Native = IsPS4NativeHID();
            if (_ps4Native)
            {
                _btnBack  = KeyCode.JoystickButton8;
                _btnL2Btn = KeyCode.JoystickButton6;
                _btnR2Btn = KeyCode.JoystickButton7;
            }
            else
            {
                _btnBack  = KeyCode.JoystickButton6;
                _btnL2Btn = KeyCode.None;
                _btnR2Btn = KeyCode.None;
            }
        }

        private bool L2Down()
        {
            if (_ps4Native) return Input.GetKeyDown(_btnL2Btn);
            float v = ReadAxisRaw(AxisL2);
            bool down = v > TriggerThreshold;
            bool result = down && !_l2WasDown;
            _l2WasDown = down;
            return result;
        }

        private bool R2Down()
        {
            if (_ps4Native) return Input.GetKeyDown(_btnR2Btn);
            float v = ReadAxisRaw(AxisR2);
            bool down = v > TriggerThreshold;
            bool result = down && !_r2WasDown;
            _r2WasDown = down;
            return result;
        }

        private bool R2Raw() => _ps4Native ? Input.GetKey(_btnR2Btn) : ReadAxisRaw(AxisR2) > TriggerThreshold;

        // 跟背包那边同一套：按住R2挪格子=批量标记，松开时有标记就一次性送回背包；
        // 全程没挪动过就当成"轻点R2=切换升/降序"。
        private readonly HashSet<int> _batchMarked = new HashSet<int>();
        private bool _r2WasHeldLastFrame;

        private void HandleR2HoldBatchSelect()
        {
            bool held = R2Raw();

            if (held && _focus >= 0 && _focus < _cells.Count && _cells[_focus] != null)
            {
                if (_batchMarked.Add(_focus))
                {
                    _cells[_focus].SetBatchMarked(true);
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); // 用户要求：标记音效跟普通光标移动一样就好，不用特殊音效
                }
            }

            if (_r2WasHeldLastFrame && !held)
            {
                if (_batchMarked.Count > 0)
                    TransferBatchMarkedToInventory();
                else
                {
                    if (_sortControls == null) _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                    _sortControls?.GamepadToggleOrder();
                }
            }

            _r2WasHeldLastFrame = held;
        }

        // 一键送回背包：有标记送标记的一批，没标记就只送焦点这一件。
        private void QuickSendToInventory()
        {
            if (_batchMarked.Count == 0)
            {
                if (_focus < 0 || _focus >= _cells.Count || _cells[_focus] == null)
                {
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                    return;
                }
                _batchMarked.Add(_focus);
            }
            TransferBatchMarkedToInventory();
        }

        private void TransferBatchMarkedToInventory()
        {
            InventoryRuntime srcInv = Inv;
            InventoryRuntime dstInv = InventoryRuntimeBootstrap.Instance != null
                ? InventoryRuntimeBootstrap.Instance.Inventory : null;

            int moved = 0;
            if (srcInv != null && dstInv != null)
            {
                foreach (int idx in _batchMarked)
                {
                    if (idx < 0 || idx >= srcInv.Slots.Count) continue;
                    InventoryItemEntry entry = srcInv.Slots[idx];
                    if (entry == null || entry.definition == null) continue;

                    int targetIndex = FindMergeOrEmptyIndex(dstInv, entry.definition);
                    if (targetIndex < 0) continue; // 背包满了，跳过这一件，其它继续

                    if (srcInv.TransferSlotTo(idx, dstInv, targetIndex))
                    {
                        moved++;
                        SkyPrisonItemMaterialSoundTable.PlayDrop(entry.definition.general.soundMaterial);
                    }
                }
            }

            SkyPrisonSystemSEPlayer.Play(moved > 0 ? SkyPrisonSystemSEType.Confirm : SkyPrisonSystemSEType.Forbidden);
            ClearBatchMarks();
        }

        private static int FindMergeOrEmptyIndex(InventoryRuntime target, ItemDefinition def)
        {
            if (def.maxStackCount > 1)
            {
                for (int i = 0; i < target.Slots.Count; i++)
                {
                    InventoryItemEntry e = target.Slots[i];
                    if (e != null && e.definition == def && !e.IsStackFull) return i;
                }
            }
            for (int i = 0; i < target.Slots.Count; i++)
                if (target.Slots[i] == null) return i;
            return -1;
        }

        private void ClearBatchMarks()
        {
            foreach (int idx in _batchMarked)
                if (idx >= 0 && idx < _cells.Count) _cells[idx]?.SetBatchMarked(false);
            _batchMarked.Clear();
        }

        private const float NavThreshold = 0.5f;
        private const float NavRepeatDelay = 0.18f;

        private static readonly Color FocusColor = new Color(0.42f, 0.92f, 0.68f, 1f);
        private static readonly Color GrabColor  = new Color(1.00f, 0.85f, 0.30f, 1f);

        private readonly List<InventorySlotView> _cells = new List<InventorySlotView>();
        private int _focus = -1;
        private int _grab = -1;
        private int _hlFocus = -1;
        private int _hlGrab = -2;
        private float _navCooldown;
        private bool _navAtRest = true;

        private CanvasGroup _cg;
        private RectTransform _highlight;
        private Image[] _highlightEdges;
        private StashWindowController _window;
        private StashInventoryInteraction _interaction;
        private SkyPrisonInventorySortControls _sortControls;

        private bool _gamepadMode;
        private Vector3 _lastMousePos;
        private bool _wasGamepadFocusActive;

        private InventoryRuntime Inv => StashRuntime.Instance?.GetPage(_window != null ? _window.CurrentPage : 0);

        public int     FocusIndex => _focus;
        public KeyCode BtnBack    => _btnBack;
        public bool    IsFocusActive { get; private set; } = true;

        private void OnEnable()
        {
            _cg = GetComponentInParent<CanvasGroup>();
            _window = GetComponentInParent<StashWindowController>();
            _interaction = GetComponent<StashInventoryInteraction>();
            _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
            ResolveControllerLayout();
            _focus = -1; _grab = -1;
            _lastMousePos = Input.mousePosition;
        }

        private InventoryWindowController _inventoryWindow;

        private bool IsOtherWindowOpen()
        {
            if (_inventoryWindow == null) _inventoryWindow = FindObjectOfType<InventoryWindowController>();
            if (_inventoryWindow == null) return false;
            var cg = _inventoryWindow.GetComponentInChildren<CanvasGroup>(true);
            return cg != null && cg.alpha > 0.9f;
        }

        private const int GamepadModeGraceFrames = 12;
        private int _gamepadModeGrace;

        private void Update()
        {
            Vector3 curMouse = Input.mousePosition;
            bool mouseMoved = (curMouse - _lastMousePos).sqrMagnitude > 0.5f || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (mouseMoved && _gamepadModeGrace <= 0)
                _gamepadMode = false;
            if (_gamepadModeGrace > 0) _gamepadModeGrace--;
            _lastMousePos = curMouse;

            if (_cg == null) _cg = GetComponentInParent<CanvasGroup>();
            bool open = _cg == null || _cg.alpha > 0.9f;
            if (!open) { HideHighlight(); _grab = -1; return; }

            // 背包/仓库可以同时打开，两边手柄输入之前各管各的——R3切换焦点，见
            // SkyPrisonGamepadWindowFocus。
            bool inventoryOpen = IsOtherWindowOpen();
            bool isActiveNow = SkyPrisonGamepadWindowFocus.IsActive(SkyPrisonGamepadWindowFocus.Target.Stash, inventoryOpen);
            IsFocusActive = isActiveNow;
            if (isActiveNow && !_wasGamepadFocusActive)
            {
                // 刚从"非活跃"切回"活跃"——直接判定手柄模式，并把焦点重置到第一格；
                // 再给几帧宽限期，防止鼠标极小抖动马上把手柄模式冲掉，导致光标框
                // 忽隐忽现。
                _gamepadMode = true;
                _focus = 0;
                _gamepadModeGrace = GamepadModeGraceFrames;
                // UpdateHighlight()只有 _focus != _hlFocus 时才会重新挂框+激活，切回来
                // 强制_focus=0，如果_hlFocus上次缓存的也刚好是0就会被判定"没变"，框
                // 永远停在隐藏状态——强制清成-1保证一定重新走一次激活逻辑。
                _hlFocus = -1;
            }
            _wasGamepadFocusActive = isActiveNow;

            if (!isActiveNow)
            {
                HideHighlight();
                return;
            }

            ResolveCells();
            if (_cells.Count == 0) { HideHighlight(); return; }

            if (_focus < 0) _focus = 0;

            if (AnyGamepadInput() || IsGamepadConnected()) _gamepadMode = true;

            if (_interaction != null && _interaction.IsAmountPopupOpen)
            {
                HandlePopupInput();
                HideHighlight();
                return;
            }

            if (_interaction != null && _interaction.IsContextMenuOpen)
            {
                HandleMenuInput();
                if (_gamepadMode) UpdateHighlight(); else HideHighlight();
                return;
            }

            HandleNavigation();
            HandleButtons();
            if (_gamepadMode) UpdateHighlight(); else HideHighlight();
        }

        private void HandleMenuInput()
        {
            if (TryGetNavStep(out int dirX, out int dirY) && dirY != 0)
                _interaction.MenuNavigate(dirY > 0 ? -1 : 1);

            if (Input.GetKeyDown(BtnConfirm)) _interaction.MenuConfirm();
            if (Input.GetKeyDown(BtnMenu))    _interaction.MenuCancel();
        }

        private void HandlePopupInput()
        {
            if (TryGetNavStep(out int dirX, out int dirY) && dirX != 0)
                _interaction.AmountPopupStep(dirX);

            if (Input.GetKeyDown(BtnConfirm)) _interaction.AmountPopupConfirm();
            if (Input.GetKeyDown(BtnMenu))    _interaction.AmountPopupCancel();
        }

        // ── 导航 ──────────────────────────────────────────────────────────────

        private void HandleNavigation()
        {
            if (!TryGetNavStep(out int dirX, out int dirY)) return;
            int prev = _focus;
            int next = FindNeighbor(dirX, dirY);
            if (next >= 0)
            {
                _focus = next;
                if (_focus != prev)
                {
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                    GetComponent<InventoryItemDetailController>()?.OnGamepadFocusChanged(_focus);
                }
                return;
            }

            // 横向到边界找不到邻居——切换筛选分类，而不是原地播拒绝音（用户确认过的方案：
            // LB/RB 留给翻页，十字键左右到头再按一下切分类）。
            if (dirX != 0 && _window != null)
            {
                int idx = (int)_window.CurrentFilter;
                int nextIdx = Mathf.Clamp(idx + dirX, 0, 5);
                if (nextIdx != idx)
                {
                    _window.SelectFilterTab(nextIdx);
                }
                else
                {
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                }
                return;
            }

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        }

        private bool TryGetNavStep(out int dirX, out int dirY)
        {
            dirX = 0; dirY = 0;

            float h = ReadAxisRaw("DPadHorizontal");
            float v = ReadAxisRaw("DPadVertical");
            if (Input.GetKey(KeyCode.RightArrow)) h = 1f;
            else if (Input.GetKey(KeyCode.LeftArrow)) h = -1f;
            if (Input.GetKey(KeyCode.UpArrow)) v = 1f;
            else if (Input.GetKey(KeyCode.DownArrow)) v = -1f;

            if (Mathf.Abs(h) < NavThreshold && Mathf.Abs(v) < NavThreshold)
            {
                _navAtRest = true;
                _navCooldown = 0f;
                return false;
            }

            if (!_navAtRest && _navCooldown > 0f)
            {
                _navCooldown -= Time.unscaledDeltaTime;
                return false;
            }
            _navAtRest = false;
            _navCooldown = NavRepeatDelay;

            if (Mathf.Abs(h) >= Mathf.Abs(v)) dirX = h > 0 ? 1 : -1;
            else                              dirY = v > 0 ? 1 : -1;
            return true;
        }

        private int FindNeighbor(int dirX, int dirY)
        {
            if (_focus < 0 || _focus >= _cells.Count) return -1;
            Vector2 cur = ((RectTransform)_cells[_focus].transform).anchoredPosition;

            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _cells.Count; i++)
            {
                if (i == _focus || _cells[i] == null) continue;
                Vector2 p = ((RectTransform)_cells[i].transform).anchoredPosition;
                Vector2 d = p - cur;

                float score;
                if (dirX != 0)
                {
                    if (Mathf.Sign(d.x) != dirX || Mathf.Abs(d.x) < 0.5f) continue;
                    score = Mathf.Abs(d.x) + Mathf.Abs(d.y) * 4f;
                }
                else
                {
                    if (Mathf.Sign(d.y) != dirY || Mathf.Abs(d.y) < 0.5f) continue;
                    score = Mathf.Abs(d.y) + Mathf.Abs(d.x) * 4f;
                }
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private static bool IsGamepadConnected()
        {
            foreach (string name in Input.GetJoystickNames())
                if (!string.IsNullOrEmpty(name)) return true;
            return false;
        }

        private static bool AnyGamepadInput()
        {
            if (Input.GetKeyDown(BtnConfirm) || Input.GetKeyDown(BtnMenu) ||
                Input.GetKeyDown(BtnLB)      || Input.GetKeyDown(BtnRB))
                return true;
            float h = ReadAxisRaw("DPadHorizontal");
            float v = ReadAxisRaw("DPadVertical");
            return Mathf.Abs(h) >= NavThreshold || Mathf.Abs(v) >= NavThreshold;
        }

        private static bool _axisMissingLogged;
        private static float ReadAxisRaw(string axisName)
        {
            try { return Input.GetAxisRaw(axisName); }
            catch
            {
                if (!_axisMissingLogged)
                {
                    Debug.LogWarning($"[StashGamepad] 未找到输入轴 \"{axisName}\"，请确认 InputManager 已加入 D-pad 轴（重启/刷新工程）。");
                    _axisMissingLogged = true;
                }
                return 0f;
            }
        }

        private void HandleButtons()
        {
            if (Input.GetKeyDown(BtnConfirm)) GrabOrDrop();

            if (Input.GetKeyDown(BtnMenu))
            {
                if (_grab >= 0) _grab = -1;
                else OpenContextMenuAtFocus();
            }

            if (Input.GetKeyDown(BtnX))
            {
                if (_sortControls == null)
                    _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                _sortControls?.GamepadTidy();
            }

            if (L2Down())
            {
                if (_sortControls == null)
                    _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                _sortControls?.GamepadCycleSortField();
            }

            // 没开背包就没有送回去的目的地，R2退回原本"轻点=切换升/降序"的老行为。
            bool inventoryOpenForBatch = IsOtherWindowOpen();
            if (inventoryOpenForBatch)
            {
                HandleR2HoldBatchSelect(); // 按住R2=批量选择送回背包，轻点=切换升/降序

                if (Input.GetKeyDown(BtnQuickSendToInventory)) // 一键直接送回背包
                    QuickSendToInventory();
            }
            else if (R2Down())
            {
                if (_sortControls == null)
                    _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                _sortControls?.GamepadToggleOrder();
            }

            if (_window != null) // LB/RB 翻页（仓库跟背包刻意不同的分工）
            {
                if (Input.GetKeyDown(BtnRB)) _window.NextPage();
                if (Input.GetKeyDown(BtnLB)) _window.PreviousPage();
            }
        }

        private void OpenContextMenuAtFocus()
        {
            if (_interaction == null || _focus < 0 || _focus >= _cells.Count) return;
            InventoryRuntime inv = Inv;
            if (inv == null || _focus >= inv.Slots.Count || inv.Slots[_focus] == null)
            { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

            RectTransform cell = (RectTransform)_cells[_focus].transform;
            var corners = new Vector3[4];
            cell.GetWorldCorners(corners);
            float cellH = corners[1].y - corners[0].y;
            Vector2 menuAnchor = new Vector2(
                (corners[0].x + corners[3].x) * 0.5f,
                corners[1].y - cellH * 0.4f);
            _interaction.ShowContextMenuAt(_focus, menuAnchor);
        }

        private void GrabOrDrop()
        {
            InventoryRuntime inv = Inv;
            if (inv == null || _focus < 0 || _focus >= inv.Slots.Count) return;

            if (_grab < 0)
            {
                if (inv.Slots[_focus] != null)
                {
                    _grab = _focus;
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Pickup);
                }
                else SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                return;
            }

            int src = _grab, dst = _focus;
            _grab = -1;
            if (src == dst) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel); return; }

            InventoryItemEntry s = inv.Slots[src];
            InventoryItemEntry d = inv.Slots[dst];
            if (s == null) return;

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            if (d != null && s.definition == d.definition && s.definition.maxStackCount > 1 && !d.IsStackFull)
                inv.MergeSlots(src, dst);
            else
                inv.MoveSlot(src, dst);
        }

        // ── 焦点格收集 ──────────────────────────────────────────────────────────

        private void ResolveCells()
        {
            if (_cells.Count > 0 && _cells[0] != null) return;

            _cells.Clear();
            var found = GetComponentsInChildren<InventorySlotView>(false);
            _cells.AddRange(found);
            _cells.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        }

        // ── 高亮 ──────────────────────────────────────────────────────────────

        private void UpdateHighlight()
        {
            EnsureHighlight();
            if (_focus < 0 || _focus >= _cells.Count || _cells[_focus] == null) { HideHighlight(); _hlFocus = -1; return; }

            if (_focus != _hlFocus)
            {
                _cells[_focus].MarkSeen();
                RectTransform cell = (RectTransform)_cells[_focus].transform;
                _highlight.SetParent(cell, false);
                _highlight.anchorMin = Vector2.zero; _highlight.anchorMax = Vector2.one;
                _highlight.offsetMin = Vector2.zero; _highlight.offsetMax = Vector2.zero;
                _highlight.SetAsLastSibling();
                _highlight.gameObject.SetActive(true);
                _hlFocus = _focus;
            }

            int grabState = _grab >= 0 ? 1 : 0;
            if (grabState != _hlGrab)
            {
                Color c = _grab >= 0 ? GrabColor : FocusColor;
                for (int i = 0; i < _highlightEdges.Length; i++)
                    if (_highlightEdges[i] != null) _highlightEdges[i].color = c;
                _hlGrab = grabState;
            }
        }

        private void HideHighlight()
        {
            if (_highlight != null) _highlight.gameObject.SetActive(false);
        }

        private void EnsureHighlight()
        {
            if (_highlight != null) return;

            var go = new GameObject("GamepadFocus", typeof(RectTransform));
            _highlight = (RectTransform)go.transform;
            _highlight.SetParent(transform, false);
            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false; cg.interactable = false;

            _highlightEdges = new Image[4];
            _highlightEdges[0] = MakeEdge("T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 3));
            _highlightEdges[1] = MakeEdge("B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 3));
            _highlightEdges[2] = MakeEdge("L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(3, 0));
            _highlightEdges[3] = MakeEdge("R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(3, 0));
            go.SetActive(false);
        }

        private Image MakeEdge(string name, Vector2 aMin, Vector2 aMax, Vector2 size)
        {
            var go = new GameObject("Edge_" + name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_highlight, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = FocusColor; img.raycastTarget = false;
            return img;
        }
    }
}
