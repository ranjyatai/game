using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 通用的"一串按钮，手柄能选"导航器——给那些只有 Button.onClick（纯鼠标点击）
    /// 的列表/网格窗口（世界地图节点、商店货架、改装槽位、装备槽、古董街物品）补手柄
    /// 操作，不用像背包那套 SkyPrisonInventoryGamepad 那么复杂（那个是格子+抓取+菜单，
    /// 这个只是"移动光标 + 按 A 点击"）。
    ///
    /// 用法：挂在窗口面板上，调 SetTargets(按钮列表) 喂目标（列表变了随时可以再调一次），
    /// A 键触发当前焦点按钮的 onClick，D-pad/左摇杆按最近邻规则移动焦点，高亮框自动
    /// 跟着挂到焦点按钮上。B 键默认不处理（各窗口自己的关闭逻辑走 SkyPrisonBaseWindowController）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonListGamepadNav : MonoBehaviour
    {
        private const KeyCode BtnConfirm = KeyCode.JoystickButton0; // A / ×
        // 纯键盘玩家没有手柄A键——Horizontal/Vertical这两个轴本来就吃方向键/WASD
        // (InputManager.asset里altNegativeButton/altPositiveButton配的)，键盘导航
        // 已经是"顺带能用"，唯独确认动作原来只认手柄A，键盘用户挪到目标上却点不下去。
        // 回车/小键盘回车补一个键盘等价确认键，不影响手柄本身的行为。
        private static readonly KeyCode[] KeyboardConfirmKeys = { KeyCode.Return, KeyCode.KeypadEnter };
        private const float NavThreshold = 0.5f;
        private const float NavRepeatDelay = 0.18f;

        private static readonly Color FocusColor = new Color(0.42f, 0.92f, 0.68f, 1f);

        private readonly List<Button> _targets = new();
        private int _focus = -1;
        private float _navCooldown;
        private bool _navAtRest = true;
        private bool _gamepadMode;
        private Vector3 _lastMousePos;
        private bool _drawHighlight = true;

        private RectTransform _highlight;
        private Image[] _highlightEdges;
        private int _hlFocus = -1;

        /// <summary>关掉这个之后不再画自带的四边高亮框——窗口自己已经有一套更贴合的
        /// 聚焦视觉(比如NPC对话框的文字变色+方块跟随)时用，避免两套高亮同时出现。
        /// 手柄导航/确认逻辑不受影响，只是不再自己画框。</summary>
        public bool DrawHighlight
        {
            get => _drawHighlight;
            set { _drawHighlight = value; if (!value) HideHighlight(); }
        }

        /// <summary>当前手柄焦点落在哪个按钮上——外部窗口想在"光标停在某一项上"时接入
        /// 额外的快捷键（比如商店用 L2/R2 直接调焦点项的数量，不用先把光标挪到具体的
        /// "-"/"+"按钮上）时用这个查询，没有焦点时返回 null。</summary>
        public Button CurrentFocusedButton => (_focus >= 0 && _focus < _targets.Count) ? _targets[_focus] : null;

        /// <summary>当前是不是键盘/手柄在驱动这个光标(而不是纯鼠标)——_focus在鼠标模式下
        /// 也会停在一个默认值(比如0)，CurrentFocusedButton不受这个状态影响照样返回
        /// 一个非null值。窗口如果想用CurrentFocusedButton画自己的聚焦视觉(DrawHighlight
        /// 关掉时)，必须先查这个，不然纯鼠标玩家从没碰过键盘/手柄，也会莫名其妙看到
        /// 第一项被"聚焦"高亮。</summary>
        public bool IsGamepadMode => _gamepadMode;

        /// <summary>喂目标按钮列表——窗口每次重建按钮（比如切换分类）后都要重新调一次。</summary>
        public void SetTargets(IReadOnlyList<Button> buttons)
        {
            _targets.Clear();
            if (buttons != null)
                foreach (var b in buttons)
                    if (b != null && b.gameObject.activeInHierarchy) _targets.Add(b);

            _focus = _targets.Count > 0 ? Mathf.Clamp(_focus, 0, _targets.Count - 1) : -1;
        }

        private void OnEnable()
        {
            _lastMousePos = Input.mousePosition;
        }

        private void OnDisable()
        {
            HideHighlight();
        }

        private void Update()
        {
            Vector3 curMouse = Input.mousePosition;
            if ((curMouse - _lastMousePos).sqrMagnitude > 0.5f || Input.GetMouseButtonDown(0))
                _gamepadMode = false;
            _lastMousePos = curMouse;

            if (_targets.Count == 0) { HideHighlight(); return; }
            if (_focus < 0) _focus = 0;

            if (AnyGamepadInput()) _gamepadMode = true;

            if (TryGetNavStep(out int dirX, out int dirY))
            {
                int next = FindNeighbor(dirX, dirY);
                if (next >= 0 && next != _focus)
                {
                    _focus = next;
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                }
            }

            if (IsConfirmPressed() && _focus >= 0 && _focus < _targets.Count)
            {
                var btn = _targets[_focus];
                if (btn != null && btn.interactable) btn.onClick.Invoke();
            }

            if (_gamepadMode && _drawHighlight) UpdateHighlight(); else HideHighlight();
        }

        private bool TryGetNavStep(out int dirX, out int dirY)
        {
            dirX = 0; dirY = 0;

            float h = SafeAxis("DPadHorizontal") + SafeAxis("Horizontal");
            float v = SafeAxis("DPadVertical") + SafeAxis("Vertical");

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

        // 按屏幕坐标找指定方向上最近的相邻按钮，不依赖列数——跟背包那套 FindNeighbor 同一个思路。
        private int FindNeighbor(int dirX, int dirY)
        {
            if (_focus < 0 || _focus >= _targets.Count || _targets[_focus] == null) return -1;
            Vector2 cur = ((RectTransform)_targets[_focus].transform).position;

            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (i == _focus || _targets[i] == null) continue;
                Vector2 p = ((RectTransform)_targets[i].transform).position;
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

        private static bool IsConfirmPressed()
        {
            if (Input.GetKeyDown(BtnConfirm)) return true;
            foreach (KeyCode key in KeyboardConfirmKeys)
                if (Input.GetKeyDown(key)) return true;
            return false;
        }

        private static bool AnyGamepadInput()
        {
            if (IsConfirmPressed()) return true;
            float h = SafeAxis("DPadHorizontal") + SafeAxis("Horizontal");
            float v = SafeAxis("DPadVertical") + SafeAxis("Vertical");
            return Mathf.Abs(h) >= NavThreshold || Mathf.Abs(v) >= NavThreshold;
        }

        private static bool _axisMissingLogged;
        private static float SafeAxis(string axisName)
        {
            try { return Input.GetAxisRaw(axisName); }
            catch
            {
                if (!_axisMissingLogged)
                {
                    Debug.LogWarning($"[SkyPrisonListGamepadNav] 未找到输入轴 \"{axisName}\"，请确认 InputManager 已配置。");
                    _axisMissingLogged = true;
                }
                return 0f;
            }
        }

        private void UpdateHighlight()
        {
            EnsureHighlight();
            if (_focus < 0 || _focus >= _targets.Count || _targets[_focus] == null) { HideHighlight(); _hlFocus = -1; return; }

            if (_focus != _hlFocus)
            {
                RectTransform cell = (RectTransform)_targets[_focus].transform;
                _highlight.SetParent(cell, false);
                _highlight.anchorMin = Vector2.zero; _highlight.anchorMax = Vector2.one;
                _highlight.offsetMin = Vector2.zero; _highlight.offsetMax = Vector2.zero;
                _highlight.SetAsLastSibling();
                _highlight.gameObject.SetActive(true);
                _hlFocus = _focus;
            }
        }

        private void HideHighlight()
        {
            if (_highlight != null) _highlight.gameObject.SetActive(false);
            _hlFocus = -1;
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
