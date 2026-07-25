using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 一条操作提示。动作型(Action)用当前绑定键的键帽图标(live)；
    /// 字面/手势型(Icon)直接给 iconKey（如 mouse/left、mouse/right）。两者都带文字兜底。
    /// </summary>
    public struct SkyPrisonWindowHint
    {
        public string label;          // 描述，如 "拆分"
        public SkyPrisonInputAction action;
        public bool hasAction;
        public string iconKey;        // 键鼠家族的图标 key（字面/手势型）
        public string gamepadIconKey; // 手柄家族对应 key（无则手柄模式下隐藏，避免混合）
        public string fallbackText;   // 无图标时显示的文字 token

        public static SkyPrisonWindowHint Action(SkyPrisonInputAction action, string label)
            => new SkyPrisonWindowHint { action = action, hasAction = true, label = label };

        public static SkyPrisonWindowHint Icon(string iconKey, string fallbackText, string label)
            => new SkyPrisonWindowHint { iconKey = iconKey, fallbackText = fallbackText, hasAction = false, label = label };

        // 仅手柄模式显示（键鼠模式自动隐藏）：用手柄家族图标 key。
        public static SkyPrisonWindowHint GamepadIcon(string gamepadIconKey, string fallbackText, string label)
            => new SkyPrisonWindowHint { gamepadIconKey = gamepadIconKey, fallbackText = fallbackText, hasAction = false, label = label };
    }

    /// <summary>窗口提供自己的操作提示列表。WindowManager 在窗口打开时收集并交给提示条。</summary>
    public interface ISkyPrisonWindowHintProvider
    {
        IReadOnlyList<SkyPrisonWindowHint> GetWindowHints();
    }

    /// <summary>
    /// 全局窗口操作提示条：屏幕底部居中、按内容自动撑宽（抗分辨率不重叠）。
    /// 键帽图标复用 SkyPrisonQuickItemPromptStrip 缓存的 InputPromptIconDatabase（同战斗 HUD）；
    /// 无图标时退回文字 token。背景为左右渐变淡出的半透明条。由 WindowManager 驱动 Show/Clear。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonWindowHintBar : MonoBehaviour
    {
        private const int FontSize = 40;
        private const int IconSize = 68;
        // 之前是 1150——远低于 PauseMenuController(32100)/SettingsWindowUI(32200) 这些
        // 全屏窗口自己的 Canvas，提示条其实一直在渲染，只是被这些窗口的不透明背景死死
        // 盖在下面，表现为"提示条完全不显示"。这几个窗口专门把自己的 sortingOrder 提到
        // 三万多就是为了盖过所有正常玩法内容，提示条作为一个理应永远在最上层的常驻
        // UI，必须比它们全部都高。
        //
        // 关键教训（这个项目里已经不是第一次踩）：Canvas.sortingOrder 公开的类型是
        // int，但内部按 16 位有符号整数存（范围 -32768~32767），超过 32767 会直接绕成
        // 负数——之前改成 32900 就是这样，32900-65536=-32636，排到了全场最底下，
        // 表现跟"完全没渲染"一模一样，肉眼根本看不出来是溢出。这里的 32760 已经是
        // 逼近上限但仍在合法范围内、同时压过项目里已知最高的 FPS 计数器(32700) 的值，
        // 以后如果要再加更高优先级的常驻 UI，数值必须先核对没有超过 32767。
        private const int SortOrder = 32760;

        private RectTransform _bar;
        private Font _font;
        private SkyPrisonInputSettings _input;
        private static Sprite _fadeSprite;
        private const float EdgeMargin = 48f; // 距屏幕上/下边的像素
        private static RectTransform s_barRect;
        private static bool s_atTop;
        private static SkyPrisonWindowHintBar s_shared;

        /// <summary>
        /// 拿到全项目共用的那一条提示条——不经过 SkyPrisonWindowManager_V1 的窗口
        /// （PauseMenuController / SettingsWindowUI / SaveSlotSelectorUI 这类纯手写的
        /// 全屏窗口）也用这个，不要再各自重写一遍图标解析/设备切换那一整套逻辑，
        /// 以前每个窗口都自己写一份，出的 bug 也是各写各的、要修好几遍。
        /// </summary>
        public static SkyPrisonWindowHintBar GetOrCreate()
        {
            if (s_shared != null) return s_shared;
            s_shared = FindObjectOfType<SkyPrisonWindowHintBar>();
            if (s_shared == null)
            {
                var go = new GameObject("[SharedWindowHintBar]") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                s_shared = go.AddComponent<SkyPrisonWindowHintBar>();
                // 这个组件是 DontDestroyOnLoad 的，内容会跨场景一直留着。窗口正常关闭时
                // 自己会调 Clear()，但只要有一条路径漏调（比如暂停中直接退章节回主菜单、
                // 场景在窗口 Close() 真正跑完前就把它的 GameObject 一起清掉了），上一次
                // 显示的内容就会一直挂在屏幕上，跟着漏到下一个场景/读条画面里——
                // 这里加一道场景切换自动清空兜底，不依赖每个调用方都记得清干净。
                SceneManager.sceneLoaded += (_, __) => s_shared.Clear();
            }
            return s_shared;
        }

        /// <summary>窗口盖住底部时把提示条移到顶部，拖开移回底部。幂等。</summary>
        public static void SetEdgeTop(bool top)
        {
            if (s_barRect == null || s_atTop == top) return;
            s_atTop = top;
            if (top)
            {
                s_barRect.anchorMin = s_barRect.anchorMax = new Vector2(0.5f, 1f);
                s_barRect.pivot = new Vector2(0.5f, 1f);
                s_barRect.anchoredPosition = new Vector2(0f, -EdgeMargin);
            }
            else
            {
                s_barRect.anchorMin = s_barRect.anchorMax = new Vector2(0.5f, 0f);
                s_barRect.pivot = new Vector2(0.5f, 0f);
                s_barRect.anchoredPosition = new Vector2(0f, EdgeMargin);
            }
        }

        /// <summary>提示条「底部位置」的固定屏幕矩形（不论它当前在上还是在下），用于稳定的重叠判定。</summary>
        public static bool TryGetBottomScreenRect(out Rect rect)
        {
            rect = default;
            if (s_barRect == null || !s_barRect.gameObject.activeInHierarchy) return false;
            Rect r = s_barRect.rect;
            if (r.width <= 0f || r.height <= 0f) return false;
            rect = new Rect((Screen.width - r.width) * 0.5f, EdgeMargin, r.width, r.height);
            return true;
        }

        // ── 通用上下文提示覆盖 ────────────────────────────────────────────
        // 任何子上下文（操作菜单 / 数量弹窗等）激活时调 PushContext 切到自己的提示，
        // 关闭时 PopContext 还原到窗口基础提示。规则通用，新弹窗照此即可。
        private static SkyPrisonWindowHintBar s_instance;
        private IReadOnlyList<SkyPrisonWindowHint> _baseHints;
        private bool _contextActive;

        public static void PushContext(IReadOnlyList<SkyPrisonWindowHint> hints)
        {
            if (s_instance == null) return;
            s_instance._contextActive = true;
            s_instance.Render(hints);
        }

        public static void PopContext()
        {
            if (s_instance == null) return;
            s_instance._contextActive = false;
            s_instance.Render(s_instance._baseHints);
        }

        // Show() 是 SkyPrisonWindowManager_V1 每帧调的（收集所有窗口的提示）。之前
        // Render() 没有任何"内容没变就跳过"的判断，只要 !_contextActive 就无条件把
        // 整条提示条的子物体全部 Destroy 再重建——每秒 60 次。调用方如果传的是同一份
        // 缓存数组引用（现在大部分窗口都是），这里用引用相等 + 设备状态没变来判断
        // "这一帧其实什么都没变"，直接跳过，不再逐帧销毁重建。
        private IReadOnlyList<SkyPrisonWindowHint> _lastRenderedHints;
        private bool _lastRenderedGamepad;

        public void Show(IReadOnlyList<SkyPrisonWindowHint> hints)
        {
            s_instance = this;
            _baseHints = hints;
            if (hints == null || hints.Count == 0)
            {
                _contextActive = false;
                UnsubscribeDeviceTracker();
                Clear();
                return;
            }
            SubscribeDeviceTracker();
            if (!_contextActive) Render(hints);
        }

        // 设备切换时自动重绘当前提示条
        private void SubscribeDeviceTracker()
        {
            SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged -= OnDeviceChanged;
            SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged += OnDeviceChanged;
        }
        private void UnsubscribeDeviceTracker()
        {
            SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged -= OnDeviceChanged;
        }
        private void OnDeviceChanged(SkyPrisonInputDeviceTracker.DeviceFamily _, SkyPrisonInputDeviceTracker.DeviceFamily __)
        {
            // 重绘：上下文活跃时刷上下文提示，否则刷基础提示
            if (_contextActive) return; // 上下文层由调用方自行刷新
            if (_baseHints != null && _baseHints.Count > 0) Render(_baseHints);
        }


        private void Render(IReadOnlyList<SkyPrisonWindowHint> hints)
        {
            if (hints == null || hints.Count == 0) { Clear(); return; }
            EnsureBar();

            // 以 DeviceTracker 的实际最近使用设备为准（接了手柄但在用键盘 → 显示键盘图标）
            bool gamepad = SkyPrisonInputDeviceTracker.Current == SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;

            // 内容（同一份数组引用）、设备状态、可见性都没变 → 这一帧其实什么都没变，
            // 跳过销毁重建。调用方每帧都会调 Show()，之前这里没有这道判断，等于每秒
            // 60 次无条件整条重建，足够的 GC/帧间抖动会干扰别的系统（比如背包窗口的
            // 帧间面板矩形对比运动检测）。
            if (ReferenceEquals(hints, _lastRenderedHints) && gamepad == _lastRenderedGamepad
                && _bar.gameObject.activeSelf && _bar.childCount > 0)
                return;
            _lastRenderedHints = hints;
            _lastRenderedGamepad = gamepad;

            for (int i = _bar.childCount - 1; i >= 0; i--)
                Destroy(_bar.GetChild(i).gameObject);

            SkyPrisonInputPromptIconDatabase db = ResolveIconDatabase();
            SkyPrisonInputSettings input = ResolveInput();

            int added = 0;
            foreach (SkyPrisonWindowHint h in hints)
            {
                Sprite icon = null;
                string token;

                if (h.hasAction)
                {
                    token = ResolveKeyText(input, h.action);
                    string ik = null;
                    if (db != null && input != null)
                        // 这里必须传当前"实际在用"的设备(gamepad)，不能传"是否插着手柄"。
                        // TryResolveActionIcon 的 preferGamepadWhenConnected 只看手柄是否
                        // 插着——插着但正在用键鼠时也会解析出手柄图标，下面又因为
                        // DeviceTracker 判断是键鼠而把这个手柄图标清空，最终只剩文字兜底，
                        // 表现就是"图标一直不显示，只要插着手柄，键鼠模式下的提示也变文字"。
                        SkyPrisonInputPromptResolver.TryResolveActionIcon(
                            input, db, h.action, gamepad,
                            SkyPrisonInputPromptDeviceStyle.GamepadXbox,
                            out icon, out ik, out _);

                    bool iconIsGamepad = ik != null && ik.StartsWith("gamepad");
                    if (gamepad && !iconIsGamepad) continue;   // 手柄模式下该动作无手柄绑定 → 隐藏，不混入键鼠
                    if (!gamepad && iconIsGamepad) icon = null; // 键鼠模式不显示手柄图标
                }
                else
                {
                    token = h.fallbackText;
                    string key = gamepad ? h.gamepadIconKey : h.iconKey;
                    if (string.IsNullOrEmpty(key)) continue; // 当前模式无对应图标 → 隐藏（键鼠手势/仅手柄提示互不混入）
                    if (db != null) db.TryGetSprite(key, out icon);
                }

                AddChip(icon, token, h.label);
                added++;
            }

            if (added == 0) { Clear(); return; }
            _bar.gameObject.SetActive(true);
        }

        public void Clear()
        {
            // 之前这里只隐藏了 GameObject，_baseHints 和设备切换订阅都没清——窗口
            // Close() 普遍直接调 Clear()（不是 Show(null)），于是提示条虽然被藏起来了，
            // 但只要之后设备家族一变（比如关掉设置窗口后手随便动了下鼠标，或摇杆有
            // 一点漂移），OnDeviceChanged 照样会拿着这份没清掉的 _baseHints 重新
            // Render 一次，把已经关闭的窗口的提示又弹回来——表现就是读条画面/正常
            // 玩法画面下面突然冒出上一个窗口的按键提示。Clear() 必须做完整的复位，
            // 不能只隐藏。
            _baseHints = null;
            _contextActive = false;
            UnsubscribeDeviceTracker();
            if (_bar != null) _bar.gameObject.SetActive(false);
        }

        // ── 构建 ──────────────────────────────────────────────────────────
        private void EnsureBar()
        {
            if (_bar != null) return;

            _font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;

            var canvasGo = new GameObject("[WindowHintBar]") { hideFlags = HideFlags.HideAndDontSave };
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Canvas.sortingOrder 是 16 位有符号整数（-32768~32767），超范围会静默绕成
            // 负数、排到最底下，肉眼跟"完全没渲染"没区别，之前在这吃过亏。这里显式校验，
            // 以后谁改了 SortOrder 改出界，一进 Play 模式立刻在 Console 报错，不会
            // 再变成一次要靠加日志才能查出来的"提示条莫名其妙消失"排查。
            if (SortOrder < short.MinValue || SortOrder > short.MaxValue)
                Debug.LogError($"[SkyPrisonWindowHintBar] SortOrder={SortOrder} 超出 Canvas.sortingOrder 的合法范围（16位有符号整数，-32768~32767），会静默溢出变成负数，提示条会排到最底下完全不可见。");
            canvas.sortingOrder = SortOrder;
            canvasGo.AddComponent<GraphicRaycaster>();

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var barGo = new GameObject("Bar", typeof(RectTransform));
            _bar = (RectTransform)barGo.transform;
            _bar.SetParent(canvasGo.transform, false);
            _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0f);
            _bar.pivot = new Vector2(0.5f, 0f);
            _bar.anchoredPosition = new Vector2(0f, EdgeMargin);
            s_barRect = _bar;
            s_atTop = false;

            // 左右渐变淡出的半透明底（更透）。
            var bg = barGo.AddComponent<Image>();
            bg.sprite = FadeSprite();
            bg.type = Image.Type.Simple;
            bg.color = new Color(0.06f, 0.07f, 0.09f, 0.55f);
            bg.raycastTarget = false;

            var layout = barGo.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 60f;
            layout.padding = new RectOffset(80, 80, 16, 16); // 左右多留 padding 让渐变区不压字
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = barGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _bar.gameObject.SetActive(false);
        }

        // 一个 chip：[图标 或 文字token] + 描述
        private void AddChip(Sprite icon, string token, string label)
        {
            var chip = new GameObject("Hint", typeof(RectTransform));
            chip.transform.SetParent(_bar, false);

            var hl = chip.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.spacing = 16f;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            if (icon != null)
            {
                var ig = new GameObject("Icon", typeof(RectTransform));
                ig.transform.SetParent(chip.transform, false);
                var im = ig.AddComponent<Image>();
                im.sprite = icon;
                im.preserveAspect = true;
                im.raycastTarget = false;
                var le = ig.AddComponent<LayoutElement>();
                le.preferredWidth = IconSize;
                le.preferredHeight = IconSize;
            }
            else
            {
                Text tk = NewText("Token", chip.transform);
                tk.text = $"[{token}]";
                tk.color = new Color(0.56f, 0.84f, 1f, 1f); // 青蓝
            }

            Text lb = NewText("Label", chip.transform);
            lb.text = label;
            lb.color = new Color(0.90f, 0.92f, 0.96f, 1f);
        }

        private Text NewText(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            if (_font != null) t.font = _font;
            t.fontSize = FontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private SkyPrisonInputPromptIconDatabase _iconDb;
        // RuntimeDatabase 只在玩法内 HUD 的 SkyPrisonQuickItemPromptStrip 建过一次之后才会
        // 有值——从主菜单/暂停/设置这些不经过玩法 HUD 的入口直接测试时它是 null，图标
        // 解析整段直接跳过，永远只显示文字兜底。这里照 ResolveInput() 同样的模式加一个
        // Resources 兜底，不依赖玩法 HUD 是否已经初始化过。
        private SkyPrisonInputPromptIconDatabase ResolveIconDatabase()
        {
            if (SkyPrisonQuickItemPromptStrip.RuntimeDatabase != null)
                return SkyPrisonQuickItemPromptStrip.RuntimeDatabase;
            if (_iconDb == null)
                // 资产实际文件名是 InputPromptIconDatabase（没有 SkyPrison 前缀），之前
                // 这两行的 Resources.Load 路径名字就没对上，永远返回 null，跟资产没被
                // 放进 Resources 目录是同一个 bug 的两个表现。数据库源文件在
                // UIUX/Data/InputPromptIconDatabase.asset，现在照 SkyPrisonInputSettings
                // 的先例在 _Project/Resources 下放了一份同名副本（各自独立 GUID）。
                _iconDb = Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");
            return _iconDb;
        }

        private SkyPrisonInputSettings ResolveInput()
        {
            if (SkyPrisonQuickItemPromptStrip.RuntimeInputSettings != null)
                return SkyPrisonQuickItemPromptStrip.RuntimeInputSettings;
            if (_input == null)
                _input = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings")
                      ?? Resources.Load<SkyPrisonInputSettings>("Settings/SkyPrisonInputSettings");
            if (_input != null) _input.EnsureDefaults();
            return _input;
        }

        private string ResolveKeyText(SkyPrisonInputSettings input, SkyPrisonInputAction action)
        {
            if (input != null)
            {
                var b = input.GetBinding(action);
                if (b != null && b.primaryKey != KeyCode.None)
                    return b.primaryKey.ToString();
            }
            return action.ToString();
        }

        // 横向渐变淡出 sprite：两端 alpha→0，中间满。RGB 白(由 Image.color 染色)。
        private static Sprite FadeSprite()
        {
            if (_fadeSprite != null) return _fadeSprite;

            const int w = 256, h = 8;
            const float edge = 0.22f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                float a = u < edge ? u / edge : (u > 1f - edge ? (1f - u) / edge : 1f);
                a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a));
                var col = new Color(1f, 1f, 1f, a);
                for (int y = 0; y < h; y++) tex.SetPixel(x, y, col);
            }
            tex.Apply();
            _fadeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            _fadeSprite.hideFlags = HideFlags.HideAndDontSave;
            return _fadeSprite;
        }

        private void OnDestroy()
        {
            UnsubscribeDeviceTracker();
            if (_bar != null) Destroy(_bar.transform.root.gameObject);
        }
    }
}
