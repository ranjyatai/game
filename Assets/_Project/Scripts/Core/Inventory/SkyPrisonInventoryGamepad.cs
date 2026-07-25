using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 背包手柄操作（挂在背包面板上，与 SkyPrisonInventoryInteraction 同处）。
    /// - 左摇杆/十字键：移动焦点格（冷绿高亮）
    /// - A：抓取焦点格物品 / 放下（空位=移动、同类=合并、他物=交换）
    /// - B：关闭窗口（抓取中则取消抓取）
    /// - LB / RB：切换筛选标签页
    /// 仅在背包打开（CanvasGroup.alpha≈1）时响应。鼠标操作不受影响，可共存。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryGamepad : MonoBehaviour
    {
        // 0-5 在 Xbox / PS4 原生 HID 两种模式下一致
        private const KeyCode BtnConfirm = KeyCode.JoystickButton0; // A / ×
        private const KeyCode BtnMenu    = KeyCode.JoystickButton1; // B / ○
        private const KeyCode BtnX       = KeyCode.JoystickButton2; // X / □  → 整理
        private const KeyCode BtnLB      = KeyCode.JoystickButton4; // LB / L1
        private const KeyCode BtnRB      = KeyCode.JoystickButton5; // RB / R1

        // 一键"直接送到仓库"（不用像R2那样按住+挪动）——原本是角色面板的键，用户已经
        // 要求把那个手柄绑定取消腾出来，这里复用同一个物理键。
        private const KeyCode BtnQuickSendToStash = KeyCode.JoystickButton10;

        // 6+ 在 Xbox 和 PS4 原生 HID 不同，运行时动态决定
        // Xbox/DS4Windows: Back=6 Start=7 L3=8  R3=9  L2/R2=轴
        // PS4 原生 HID:    L2=6  R2=7  Share=8 Options=9 L3=10 R3=11
        private KeyCode _btnBack;    // Back(Xbox) / Share(PS4) → 详情面板
        private KeyCode _btnL2Btn;   // PS4 L2 数字键（Xbox 无对应按键，用轴检测）
        private KeyCode _btnR2Btn;   // PS4 R2 数字键
        private bool    _ps4Native;

        // Xbox L2/R2 触发器轴名（Unity 默认映射，需 InputManager 中存在）
        private const string AxisL2 = "Axis 9";
        private const string AxisR2 = "Axis 10";
        private const float  TriggerThreshold = 0.5f;

        // L2/R2 的单次触发检测（模拟按下，不持续触发）
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
                _btnBack  = KeyCode.JoystickButton8;  // Share
                _btnL2Btn = KeyCode.JoystickButton6;  // L2（数字）
                _btnR2Btn = KeyCode.JoystickButton7;  // R2（数字）
            }
            else
            {
                _btnBack  = KeyCode.JoystickButton6;  // Back/View
                _btnL2Btn = KeyCode.None;             // Xbox L2 为轴，单独检测
                _btnR2Btn = KeyCode.None;
            }
        }

        // 返回 L2/R2 本帧是否刚按下（跨平台统一接口）
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

        // 持续按住检测（跟 R2Down 的单帧边缘检测不一样）——PS4原生走按键，Xbox走扳机轴。
        private bool R2Raw() => _ps4Native ? Input.GetKey(_btnR2Btn) : ReadAxisRaw(AxisR2) > TriggerThreshold;

        // 批量选择送仓库：按住R2期间，光标移动到哪一格就把哪一格标记进选择集合（琥珀色描边），
        // 松开R2时如果标记过至少一格，就把这些格子的物品一次性送到仓库当前页；如果全程
        // 没挪动过（标记集合是空的），当成原来"轻点R2=切换升/降序"处理，两种手感不冲突。
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
                    TransferBatchMarkedToStash();
                else
                {
                    if (_sortControls == null) _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                    _sortControls?.GamepadToggleOrder();
                }
            }

            _r2WasHeldLastFrame = held;
        }

        private StashWindowController _batchStashWindow;

        // 一键送仓库：已经用R2标记过东西就送那一批；没标记过就只送当前焦点这一件——
        // 复用同一套转移逻辑，临时把焦点塞进标记集合再调用，用完照常清空。
        private void QuickSendToStash()
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
            TransferBatchMarkedToStash();
        }

        private void TransferBatchMarkedToStash()
        {
            if (_batchStashWindow == null) _batchStashWindow = FindObjectOfType<StashWindowController>();
            InventoryRuntime srcInv = Inv;
            InventoryRuntime dstInv = _batchStashWindow != null
                ? StashRuntime.Instance?.GetPage(_batchStashWindow.CurrentPage) : null;

            int moved = 0;
            if (srcInv != null && dstInv != null)
            {
                foreach (int idx in _batchMarked)
                {
                    if (idx < 0 || idx >= srcInv.Slots.Count) continue;
                    InventoryItemEntry entry = srcInv.Slots[idx];
                    if (entry == null || entry.definition == null) continue;

                    int targetIndex = FindMergeOrEmptyIndex(dstInv, entry.definition);
                    if (targetIndex < 0) continue; // 仓库满了，这个跳过，剩下的照样继续尝试

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

        // 跟 InventoryRuntime.AddItem() 内部找格子的逻辑一样：优先合并进同种未满堆叠，
        // 否则找第一个空格。TransferSlotTo 需要一个明确的目标格，这里现算一个。
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

        private static readonly Color FocusColor = new Color(0.42f, 0.92f, 0.68f, 1f);   // 冷绿
        private static readonly Color GrabColor  = new Color(1.00f, 0.85f, 0.30f, 1f);   // 抓取中=琥珀

        private readonly List<InventorySlotView> _cells = new List<InventorySlotView>();
        private bool _navLogged;
        private int _focus = -1;
        private int _grab = -1;
        private int _hlFocus = -1;
        private int _hlGrab = -2;
        private float _navCooldown;
        private bool _navAtRest = true;

        private CanvasGroup _cg;
        private RectTransform _highlight;
        private Image[] _highlightEdges;
        private InventoryWindowController _window;
        private SkyPrisonInventoryInteraction _interaction;
        private SkyPrisonInventorySortControls _sortControls;

        // 输入设备检测：鼠标移动/点击→隐藏光标，手柄输入→显示
        private bool _gamepadMode;
        private Vector3 _lastMousePos;
        private bool _wasGamepadFocusActive;

        private InventoryRuntime Inv =>
            InventoryRuntimeBootstrap.Instance != null ? InventoryRuntimeBootstrap.Instance.Inventory : null;

        private StashWindowController _stashWindow;

        private void OnEnable()
        {
            _cg = GetComponentInParent<CanvasGroup>();
            _window = GetComponentInParent<InventoryWindowController>();
            _interaction = GetComponent<SkyPrisonInventoryInteraction>();
            _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
            ResolveControllerLayout();
            _focus = -1; _grab = -1;
            _lastMousePos = Input.mousePosition;
        }

        private bool IsOtherWindowOpen()
        {
            if (_stashWindow == null) _stashWindow = FindObjectOfType<StashWindowController>();
            if (_stashWindow == null) return false;
            var cg = _stashWindow.GetComponentInChildren<CanvasGroup>(true);
            return cg != null && cg.alpha > 0.9f;
        }

        // 切换焦点后的一小段"宽限期"内，忽略鼠标位移判定——鼠标本身的极小抖动
        // （系统指针平滑/无线鼠标底噪，哪怕完全没碰）经常刚好压过阈值，导致切过去
        // 强制置的 _gamepadMode=true 下一帧又被鼠标检测悄悄改回false，光标框忽隐
        // 忽现。给几帧的宽限期，让"这是手柄切过来的"这个状态能稳定住。
        private const int GamepadModeGraceFrames = 12;
        private int _gamepadModeGrace;

        private void Update()
        {
            // 设备检测：鼠标移动或点击→键鼠模式（隐藏手柄光标）；手柄按键/轴→手柄模式
            Vector3 curMouse = Input.mousePosition;
            bool mouseMoved = (curMouse - _lastMousePos).sqrMagnitude > 0.5f || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (mouseMoved && _gamepadModeGrace <= 0)
                _gamepadMode = false;
            if (_gamepadModeGrace > 0) _gamepadModeGrace--;
            _lastMousePos = curMouse;

            // 仅在窗口完全可见时响应
            if (_cg == null) _cg = GetComponentInParent<CanvasGroup>();
            bool open = _cg == null || _cg.alpha > 0.9f;
            if (!open) { HideHighlight(); _grab = -1; return; }

            // 背包/仓库可以同时打开，两边手柄输入之前各管各的，会同时响应同一次十字键/A/B。
            // R3切换"当前手柄操作哪一边"，只有一边在没开仓库、或者被选中时才处理导航/操作。
            bool stashOpen = IsOtherWindowOpen();
            bool isActiveNow = SkyPrisonGamepadWindowFocus.IsActive(SkyPrisonGamepadWindowFocus.Target.Inventory, stashOpen);
            IsFocusActive = isActiveNow;
            if (isActiveNow && !_wasGamepadFocusActive)
            {
                // 刚从"非活跃"切回"活跃"——R3本身不算在 AnyGamepadInput() 的按键清单里，
                // 之前要等切过来之后再挪一下十字键才会被判定成"手柄模式"、光标框才出现，
                // 用户反馈"切换后框没有第一时间出现"。既然是靠手柄按键切过来的，直接
                // 判定为手柄模式；同时把焦点重置到第一格，而不是停在上次离开时的位置；
                // 再给几帧宽限期防止鼠标抖动马上把这个状态冲掉。
                _gamepadMode = true;
                _focus = 0;
                _gamepadModeGrace = GamepadModeGraceFrames;
                // 真正的bug在这——UpdateHighlight()只有 _focus != _hlFocus 时才会把
                // 光标框重新挂到格子上并 SetActive(true)。切走时一直在HideHighlight()，
                // 框被隐藏；切回来强制_focus=0，但如果上次缓存的_hlFocus也刚好是0，
                // "没变"这个判断成立，激活那段代码根本不会跑，框就一直隐着。强制清成
                // -1，保证切回来这一帧一定会重新走一次激活逻辑。
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

            if (_focus < 0) _focus = 0; // 首次打开聚焦第一格

            // 任何手柄按键/轴切换为手柄模式；背包打开时若有手柄接入则始终保持手柄模式显示光标
            if (AnyGamepadInput() || IsGamepadConnected()) _gamepadMode = true;

            // 数量弹窗（拆分/丢弃）打开时优先：D-pad 左右调数量、×确认、○取消
            if (_interaction != null && _interaction.IsAmountPopupOpen)
            {
                HandlePopupInput();
                HideHighlight();
                return;
            }

            // 操作菜单打开时：D-pad 导航菜单、×确认、○取消；网格操作暂停
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
                _interaction.MenuNavigate(dirY > 0 ? -1 : 1); // 上(dirY=1)=上一项

            if (Input.GetKeyDown(BtnConfirm)) _interaction.MenuConfirm();
            if (Input.GetKeyDown(BtnMenu))    _interaction.MenuCancel();
        }

        // 数量弹窗：左右调数量（右=+，左=-），×确认、○取消
        private void HandlePopupInput()
        {
            if (TryGetNavStep(out int dirX, out int dirY) && dirX != 0)
                _interaction.AmountPopupStep(dirX);

            if (Input.GetKeyDown(BtnConfirm)) _interaction.AmountPopupConfirm();
            if (Input.GetKeyDown(BtnMenu))    _interaction.AmountPopupCancel();
        }

        // ── 导航 ──────────────────────────────────────────────────────────────

        public int     FocusIndex => _focus;
        public KeyCode BtnBack    => _btnBack; // 供 InventoryItemDetailController 读取（跨平台适配）
        // 供 InventoryItemDetailController 判断：背包/仓库双开时，焦点切到另一边后，
        // 详情面板不该继续挂在这一边——之前只隐藏了光标高亮框，详情面板没跟着关。
        public bool    IsFocusActive { get; private set; } = true;

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
            }
            else
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            }
        }

        // 读 D-pad（带按下重复节流），返回是否产生一次离散方向步进及方向
        private bool TryGetNavStep(out int dirX, out int dirY)
        {
            dirX = 0; dirY = 0;

            // 只用十字键(D-pad)，不用左摇杆(L3)；键盘方向键作桌面调试
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

            // 方向：v>0=上, v<0=下, h>0=右, h<0=左（DPadVertical 已 invert 对齐）
            if (Mathf.Abs(h) >= Mathf.Abs(v)) dirX = h > 0 ? 1 : -1;
            else                              dirY = v > 0 ? 1 : -1;
            return true;
        }

        // 按格子实际坐标找指定方向上最近的相邻格（不依赖列数，稳健）
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
                    score = Mathf.Abs(d.x) + Mathf.Abs(d.y) * 4f; // 同行优先
                }
                else
                {
                    // anchoredPosition.y 向上为正（顶行更大）；dirY=1=上 → 找 d.y>0
                    if (Mathf.Sign(d.y) != dirY || Mathf.Abs(d.y) < 0.5f) continue;
                    score = Mathf.Abs(d.y) + Mathf.Abs(d.x) * 4f; // 同列优先
                }
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        // 检测本帧是否有任何手柄输入（按键按下或 D-pad 超阈值）
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

        // 安全读轴：轴未在 InputManager 定义时返回 0（不抛异常）
        private static bool _axisMissingLogged;
        private static float ReadAxisRaw(string axisName)
        {
            try { return Input.GetAxisRaw(axisName); }
            catch
            {
                if (!_axisMissingLogged)
                {
                    Debug.LogWarning($"[InventoryGamepad] 未找到输入轴 \"{axisName}\"，请确认 InputManager 已加入 D-pad 轴（重启/刷新工程）。");
                    _axisMissingLogged = true;
                }
                return 0f;
            }
        }

        private void HandleButtons()
        {
            if (Input.GetKeyDown(BtnConfirm)) GrabOrDrop();      // A 抓取/放下

            if (Input.GetKeyDown(BtnMenu))                       // B
            {
                if (_grab >= 0) _grab = -1;                      // 抓取中 → 取消抓取
                else OpenContextMenuAtFocus();                  // 否则 → 打开操作菜单
            }

            if (Input.GetKeyDown(BtnX))                          // X → 整理
            {
                if (_sortControls == null)
                    _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                _sortControls?.GamepadTidy();
            }

            if (L2Down())                                        // L2 → 循环切换排序字段
            {
                if (_sortControls == null)
                    _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                _sortControls?.GamepadCycleSortField();
            }

            // 批量选择/一键送仓库这两个功能都要求仓库确实开着——没开仓库时没有
            // 送去的目的地，R2就退回原本"轻点=切换升/降序"的老行为，不进入批量模式；
            // 一键送仓库的键直接不响应（不会莫名其妙标记东西又送不出去）。
            bool stashOpenForBatch = IsOtherWindowOpen();
            if (stashOpenForBatch)
            {
                HandleR2HoldBatchSelect();                       // 按住R2=批量选择送仓库，轻点=切换升/降序

                if (Input.GetKeyDown(BtnQuickSendToStash))       // 一键直接送仓库（焦点这一件，或者已经批量标记的这几件）
                    QuickSendToStash();
            }
            else if (R2Down())
            {
                if (_sortControls == null)
                    _sortControls = GetComponentInParent<SkyPrisonInventorySortControls>();
                _sortControls?.GamepadToggleOrder();
            }

            if (_window != null)                                 // LB/RB 切换分类页
            {
                int idx = (int)_window.CurrentFilter;
                if (Input.GetKeyDown(BtnRB))
                {
                    int next = Mathf.Min(idx + 1, 5);
                    SkyPrisonSystemSEPlayer.Play(next != idx ? SkyPrisonSystemSEType.Switch : SkyPrisonSystemSEType.Forbidden);
                    _window.SelectTab(next);
                }
                if (Input.GetKeyDown(BtnLB))
                {
                    int next = Mathf.Max(idx - 1, 0);
                    SkyPrisonSystemSEPlayer.Play(next != idx ? SkyPrisonSystemSEType.Switch : SkyPrisonSystemSEType.Forbidden);
                    _window.SelectTab(next);
                }
            }
        }

        // 在焦点格打开操作菜单（使用/拆分/丢弃）；空格不开
        private void OpenContextMenuAtFocus()
        {
            if (_interaction == null || _focus < 0 || _focus >= _cells.Count) return;
            InventoryRuntime inv = Inv;
            if (inv == null || _focus >= inv.Slots.Count || inv.Slots[_focus] == null)
            { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

            // 手柄菜单从焦点格中上部弹出，与格子形成交叠
            RectTransform cell = (RectTransform)_cells[_focus].transform;
            var corners = new Vector3[4];
            cell.GetWorldCorners(corners); // 0=左下 1=左上 2=右上 3=右下
            float cellH = corners[1].y - corners[0].y;
            Vector2 menuAnchor = new Vector2(
                (corners[0].x + corners[3].x) * 0.5f,  // 格子水平中心
                corners[1].y - cellH * 0.4f);           // 顶边下移 40% 格高
            _interaction.ShowContextMenuAt(_focus, menuAnchor);
        }

        // 抓取/放下：空位=移动、同类可堆叠=合并、否则=交换
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
            if (_cells.Count > 0 && _cells[0] != null) return; // 已解析（格子是常驻的）

            _cells.Clear();
            var found = GetComponentsInChildren<InventorySlotView>(false); // 仅激活（容量内）
            _cells.AddRange(found);
            _cells.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        }

        // ── 高亮 ──────────────────────────────────────────────────────────────

        private void UpdateHighlight()
        {
            EnsureHighlight();
            if (_focus < 0 || _focus >= _cells.Count || _cells[_focus] == null) { HideHighlight(); _hlFocus = -1; return; }

            // 仅在焦点变化时重挂到焦点格（避免每帧 SetParent 触发布局）
            if (_focus != _hlFocus)
            {
                _cells[_focus].MarkSeen(); // 手柄焦点落到物品上即清除 NEW（与鼠标悬停一致）
                RectTransform cell = (RectTransform)_cells[_focus].transform;
                _highlight.SetParent(cell, false);
                _highlight.anchorMin = Vector2.zero; _highlight.anchorMax = Vector2.one;
                _highlight.offsetMin = Vector2.zero; _highlight.offsetMax = Vector2.zero;
                _highlight.SetAsLastSibling();
                _highlight.gameObject.SetActive(true);
                _hlFocus = _focus;
            }

            // 抓取状态变化时换色（焦点=冷绿，抓取中=琥珀）
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
