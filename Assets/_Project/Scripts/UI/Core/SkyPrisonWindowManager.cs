using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    [DisallowMultipleComponent]
    public sealed class SkyPrisonWindowManager_V1 : MonoBehaviour
    {
        [SerializeField] private Transform windowRoot;
        [SerializeField] private float fadeDuration = 0.15f;

        private readonly Dictionary<string, GameObject> openedWindows = new Dictionary<string, GameObject>();
        // 正在淡出的实例（等待销毁），不计入 openedWindows
        private readonly HashSet<GameObject> fadingOut = new HashSet<GameObject>();

        // 打开 Window/Popup 时隐藏战斗 HUD（避免高排序的快捷键帽/HP/LP 盖在窗口上）。
        private CanvasGroup _hudGroup;
        private Coroutine _hudFade;

        // 全局底部居中操作提示条（所有窗口共用）。
        private SkyPrisonWindowHintBar _hintBar;

        /// <summary>HUD 开始淡入/淡出时触发。true=显示，false=隐藏。</summary>
        public event System.Action<bool> OnHudVisibilityChanged;

        public GameObject Open(GameObject windowPrefab)
        {
            if (windowPrefab == null) return null;

            SkyPrisonUIPrefabMetadata_V1 metadata = windowPrefab.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
            string key = metadata != null && !string.IsNullOrWhiteSpace(metadata.uiId)
                ? metadata.uiId : windowPrefab.name;

            if (metadata != null && !metadata.allowMultipleInstances
                && openedWindows.TryGetValue(key, out GameObject existing) && existing != null)
            {
                existing.SetActive(true);
                UpdateHudForWindows();
                return existing;
            }

            Transform root = windowRoot != null ? windowRoot : transform;
            GameObject instance = Instantiate(windowPrefab, root);
            instance.name = windowPrefab.name;
            openedWindows[key] = instance;
            ApplyInputMode(metadata, true);
            UpdateHudForWindows();

            // 只对 Window / Popup / Overlay 做淡入，HUD 等直接显示
            if (ShouldFade(metadata))
            {
                CanvasGroup cg = GetOrAddCanvasGroup(instance);
                cg.alpha = 0f;
                StartCoroutine(FadeIn(cg));
            }

            return instance;
        }

        public bool IsOpen(string uiId)
        {
            if (string.IsNullOrWhiteSpace(uiId)) return false;
            return openedWindows.TryGetValue(uiId, out GameObject go) && go != null;
        }

        /// <summary>当前所有已打开窗口的 key（快照，可安全迭代后调用 Close）。</summary>
        public IReadOnlyList<string> OpenedWindowKeys
        {
            get
            {
                var list = new List<string>(openedWindows.Count);
                foreach (var kv in openedWindows)
                    if (kv.Value != null) list.Add(kv.Key);
                return list;
            }
        }

        public bool HasAnyWindowOpen()
        {
            foreach (var kv in openedWindows)
                if (kv.Value != null) return true;
            return false;
        }

        public void Close(string uiId)
        {
            if (string.IsNullOrWhiteSpace(uiId)) return;
            if (!openedWindows.TryGetValue(uiId, out GameObject instance) || instance == null) return;

            SkyPrisonUIPrefabMetadata_V1 metadata = instance.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
            ApplyInputMode(metadata, false);
            openedWindows.Remove(uiId);

            // 背包<->仓库联动关闭：仓库打开时会自动带开背包(StashWindowController.
            // OpenInventoryAlongside)，两者是搭配使用的一对——关掉任何一个都应该把
            // 另一个也收掉，不能留一个功能不完整的单独晾在那。放在 Remove(uiId) 之后
            // 才检查，这样递归调用 Close(对方) 时 IsOpen(自己) 已经是 false，不会来回
            // 无限递归。背包关闭入口不止一处(关闭按钮/B键/ESC都直接调这里)，放在这个
            // 唯一的公共出口上才能保证不管从哪条路径关，联动都生效。
            if (uiId == "inventory" && IsOpen("stash")) Close("stash");
            else if (uiId == "stash" && IsOpen("inventory")) Close("inventory");

            // 有些下拉/弹层是单独建在最外层Canvas上的（不是窗口的子物体，比如背包排序
            // 下拉），窗口自己的关闭动画完全影响不到它们——开始关闭动画之前先立即关掉，
            // 不然会在窗口收缩播放期间一直悬在原地，看起来像"窗口关了、弹层没关"。
            var sortControls = instance.GetComponentsInChildren<SkyPrisonInventorySortControls>(true);
            foreach (var sc in sortControls)
                sc.ForceCloseDropdown();

            // 只对 Window / Popup / Overlay 做关闭动画：向中心垂直压缩成一根线再消失。
            // 先把窗口加入 fadingOut，再刷新阻塞态——这样关窗动画期间仍算"菜单态"，
            // 玩法输入（闪避/跳跃等）继续被屏蔽，避免关窗那一刻漏触发。
            if (ShouldFade(metadata))
            {
                fadingOut.Add(instance);
                UpdateHudForWindows();
                StartCoroutine(CompressOut(instance, (RectTransform)instance.transform));
            }
            else
            {
                UpdateHudForWindows();
                Destroy(instance);
            }
        }

        // ── 淡入淡出协程 ────────────────────────────────────────────────

        private IEnumerator FadeIn(CanvasGroup cg)
        {
            SkyPrisonInventoryChromatic.PushGlobalSuspend(); // 开窗动画期间停色差抓屏，避免卡顿
            try
            {
                float t = 0f;
                while (t < fadeDuration)
                {
                    t += Time.unscaledDeltaTime;
                    cg.alpha = Mathf.Clamp01(t / fadeDuration);
                    yield return null;
                }
                cg.alpha = 1f;
            }
            finally
            {
                SkyPrisonInventoryChromatic.PopGlobalSuspend();
            }
        }

        private IEnumerator FadeOut(CanvasGroup cg, GameObject instance)
        {
            float t = fadeDuration;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime;
                if (cg == null) yield break; // 已被外部销毁
                cg.alpha = Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }

            fadingOut.Remove(instance);
            if (instance != null) Destroy(instance);
        }

        // 关闭：上下边往中心收成一根横线(scaleY→0)，再横向收到中心(scaleX→0)后销毁
        private IEnumerator CompressOut(GameObject instance, RectTransform rootRt)
        {
            if (rootRt == null) { if (instance != null) Destroy(instance); yield break; }

            SkyPrisonInventoryChromatic.PushGlobalSuspend(); // 关窗动画期间停色差抓屏，避免卡顿
            try
            {
                // 关窗期间挂起色差快照覆盖层，否则它会盖住正在压缩的面板（无此组件则无操作）
                var chromatic = instance.GetComponentInChildren<SkyPrisonInventoryChromatic>(true);
                if (chromatic != null) chromatic.SuspendForInteraction = true;

                // 冻结磨砂 UV 跟踪，否则它会随缩放重算采样区导致磨砂图扭曲，而非随窗口压缩
                var blurTracker = instance.GetComponentInChildren<SkyPrisonBlurUVTracker>(true);
                if (blurTracker != null) blurTracker.Frozen = true;

                // PF 根常是零尺寸锚点容器，缩它会朝屏幕角收缩。改缩真正的可视面板（最大的子节点）。
                RectTransform rt = FindVisualPanel(rootRt);
                SetPivotKeepPosition(rt, new Vector2(0.5f, 0.5f)); // 绕面板中心收缩
                Vector3 baseScale = rt.localScale;

                float yDur = fadeDuration;          // 竖压成线
                float xDur = fadeDuration * 0.6f;   // 横缩到点

                // 阶段一：上下往中心收成一根横线（ease-in，越收越快有"合拢"感）
                float t = 0f;
                while (t < yDur)
                {
                    t += Time.unscaledDeltaTime;
                    if (rt == null) yield break;
                    float p = Mathf.Clamp01(t / yDur);
                    float k = 1f - p * p;           // 1→0，缓入
                    rt.localScale = new Vector3(baseScale.x, baseScale.y * k, baseScale.z);
                    yield return null;
                }

                // 阶段二：横向从两端收到中心
                t = 0f;
                while (t < xDur)
                {
                    t += Time.unscaledDeltaTime;
                    if (rt == null) yield break;
                    float k = 1f - Mathf.Clamp01(t / xDur); // 1→0
                    rt.localScale = new Vector3(baseScale.x * k, 0f, baseScale.z);
                    yield return null;
                }
            }
            finally
            {
                SkyPrisonInventoryChromatic.PopGlobalSuspend();
            }

            fadingOut.Remove(instance);
            UpdateHudForWindows(); // 关闭动画结束，重算阻塞态（解除菜单态）
            if (instance != null) Destroy(instance);
        }

        // 取真正的可视窗口面板：根若是 Canvas 容器（rect=全屏）或零尺寸，则用面积最大的子节点（整窗面板）
        private static RectTransform FindVisualPanel(RectTransform root)
        {
            bool rootIsContainer = root.GetComponent<Canvas>() != null || root.sizeDelta == Vector2.zero;
            if (rootIsContainer)
            {
                RectTransform best = null;
                float bestArea = -1f;
                for (int i = 0; i < root.childCount; i++)
                {
                    if (!(root.GetChild(i) is RectTransform c) || !c.gameObject.activeSelf) continue;
                    float area = c.rect.width * c.rect.height;
                    if (area > bestArea) { bestArea = area; best = c; }
                }
                if (best != null) return best;
            }
            return root;
        }

        // 改 pivot 同时补偿 anchoredPosition，保持窗口视觉位置不跳
        private static void SetPivotKeepPosition(RectTransform rt, Vector2 pivot)
        {
            Vector2 size = rt.rect.size;
            Vector2 dPivot = pivot - rt.pivot;
            Vector2 dPos = new Vector2(dPivot.x * size.x * rt.localScale.x,
                                       dPivot.y * size.y * rt.localScale.y);
            rt.pivot = pivot;
            rt.anchoredPosition += dPos;
        }

        // ── 工具 ────────────────────────────────────────────────────────

        private static bool ShouldFade(SkyPrisonUIPrefabMetadata_V1 metadata)
        {
            if (metadata == null) return true; // 未知类型默认淡入淡出
            return metadata.kind == SkyPrisonUIPrefabKindV1.Window
                || metadata.kind == SkyPrisonUIPrefabKindV1.Popup
                || metadata.kind == SkyPrisonUIPrefabKindV1.Overlay;
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
        {
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            return cg != null ? cg : go.AddComponent<CanvasGroup>();
        }

        private static void ApplyInputMode(SkyPrisonUIPrefabMetadata_V1 metadata, bool opening)
        {
            if (metadata == null || !Application.isPlaying) return;

            if (metadata.showMouseCursor)
            {
                // 光标不管开没开窗口都不能离开游戏窗口——之前开窗口用 None（完全不限制，
                // 鼠标能跑出窗口外）、关窗口用 Locked（FPS式锁在屏幕中心，只读位移量，
                // 根本不是"卡在窗口边缘"），两个都不对，统一成 Confined。
                // Cursor.visible 这边不用管了，交给 SkyPrisonCustomCursor 每帧自己维护
                // （找到自定义光标图标就强制隐藏系统光标，找不到才用系统默认光标），
                // 这里再显式设一次反而会跟它打架。
                Cursor.lockState = CursorLockMode.Confined;
                if (opening)
                    PlayerAimFacingController.Instance?.PushWindowCursor();
                else
                    PlayerAimFacingController.Instance?.PopWindowCursor();
            }
        }

        // ── 战斗 HUD 隐藏 / 恢复 ─────────────────────────────────────────
        // 有 Window/Popup 打开时淡出整个战斗 HUD（含高排序的快捷键帽、HP/LP），
        // 全部关闭后淡回。用 HUD 根的 CanvasGroup，alpha 会连嵌套 Canvas 一起作用。

        /// <summary>
        /// 是否有「会进入菜单态」的窗口(Window/Popup)打开。
        /// 任何读鼠标/世界点击的玩法系统都应检查它，窗口打开时屏蔽自身功能（移动除外）。
        /// </summary>
        public static bool AnyBlockingWindowOpen => _windowsBlocking || ExternalBlock;
        private static bool _windowsBlocking;

        /// <summary>
        /// 给不走 Open()/Close() 这条 prefab 流程、纯代码搭的独立系统用（比如程序化的
        /// 暂停菜单）——这类系统自己决定阻塞玩法输入的起止，用完必须复位为 false，
        /// 否则会一直卡住后续所有窗口/攻击/移动的输入判断。
        /// </summary>
        public static bool ExternalBlock { get; set; }

        /// <summary>
        /// 给不走 Open()/Close() prefab 流程、但自己也需要"藏战斗HUD"的独立窗口用（比如
        /// 角色面板）。计数式而不是布尔，允许多个外部系统各自 Show/Hide 互不冲突。之前
        /// 角色面板自己直接抢同一个 CanvasGroup 做淡入淡出，跟这里的 SetHudHidden 各跑各的
        /// 协程互相打架——背包关闭触发的"淡回"和角色面板打开触发的"淡出"谁跑到最后算谁赢，
        /// 表现为"背包关闭后开角色面板，HUD 没被藏住"。现在统一走这一个入口，不再各自为政。
        /// </summary>
        public static int ExternalHudHideRequests { get; set; }

        /// <summary>外部系统（角色面板等）请求隐藏/恢复战斗HUD时调用，会触发跟窗口开关同一套
        /// 淡入淡出逻辑（含 PlayerHUDStatusIconBar 监听的 OnHudVisibilityChanged 事件）。</summary>
        public void RefreshHudVisibility() => UpdateHudForWindows();

        private void UpdateHudForWindows()
        {
            // 关闭动画(压缩成线)期间窗口已从 openedWindows 移除，但仍在 fadingOut。
            // 把它也算作"阻塞"，避免关窗那一刻 A/B 等键漏给玩法（如闪避）。
            bool blocking = AnyHudHidingWindowOpen() || fadingOut.Count > 0 || ExternalHudHideRequests > 0;
            _windowsBlocking = blocking;
            SetHudHidden(blocking);
            UpdateHintsForWindows();
            UpdateMovementLockForWindows();
        }

        // metadata.lockGameplayInput 之前是纯数据字段，没有任何运行时系统真正读它
        // （元数据组件自己的注释也写了"不建 UI、不装 UI、不改布局"，纯数据）——商店
        // 把它设成 true 想"冻结窗口外操作包括移动"，但从来没有代码消费这个字段，
        // 用户反馈"按wsad角色还在动"。真正决定移动是否被冻结的是
        // SkyPrisonPlayerInputRouter 里的 movementBlocking(目前只看 Time.timeScale)，
        // 这里把它跟 lockGameplayInput 接起来：任意已打开窗口的 metadata 标了
        // lockGameplayInput=true，就等同暂停菜单那种真模态，一起定住角色。
        private void UpdateMovementLockForWindows()
        {
            bool moveBlock = false;
            foreach (var kv in openedWindows)
            {
                if (kv.Value == null) continue;
                var md = kv.Value.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
                if (md != null && md.lockGameplayInput) { moveBlock = true; break; }
            }
            _movementLocked = moveBlock;
        }

        /// <summary>是否有窗口要求"冻结窗口外操作(含移动)"(metadata.lockGameplayInput=true)。
        /// 跟 AnyBlockingWindowOpen 语义不同——后者故意不挡移动(背包/角色面板要能边开边走)，
        /// 这个是真正的全模态窗口(比如商店)专用。</summary>
        public static bool AnyMovementLockingWindowOpen => _movementLocked;
        private static bool _movementLocked;

        // 收集所有打开窗口的操作提示，交给底部提示条；无窗口则清空。
        private void UpdateHintsForWindows()
        {
            // ExternalBlock 是不走 Open()/Close() prefab 流程的独立窗口（暂停菜单、
            // 设置窗口、存档选择器这类纯代码搭的全屏窗口）自己置位的——它们跟这套
            // openedWindows 完全不知道彼此存在，但共用同一个 SkyPrisonWindowHintBar
            // 实例。这个方法本来是每帧/每次窗口状态变化都在跑，之前没这个判断的话，
            // 会拿着"openedWindows 里一个都没有"算出一份空提示，把外部窗口刚设好的
            // 内容立刻清掉——之前设置界面提示条一直空白就是这个在捣鬼。外部窗口开着
            // 的时候这个方法完全不管提示条，交给外部窗口自己维护。
            if (ExternalBlock) return;

            var hints = new List<SkyPrisonWindowHint>();
            foreach (var kv in openedWindows)
            {
                if (kv.Value == null) continue;
                var provider = kv.Value.GetComponentInChildren<ISkyPrisonWindowHintProvider>(true);
                if (provider != null)
                {
                    var list = provider.GetWindowHints();
                    if (list != null) hints.AddRange(list);
                }
            }

            ResolveHintBar()?.Show(hints);
        }

        private SkyPrisonWindowHintBar ResolveHintBar()
        {
            if (_hintBar == null)
                _hintBar = gameObject.GetComponent<SkyPrisonWindowHintBar>()
                        ?? gameObject.AddComponent<SkyPrisonWindowHintBar>();
            return _hintBar;
        }

        private bool AnyHudHidingWindowOpen()
        {
            foreach (var kv in openedWindows)
            {
                if (kv.Value == null) continue;
                var meta = kv.Value.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
                if (meta == null) return true; // 未知类型按会遮挡处理
                if (meta.kind == SkyPrisonUIPrefabKindV1.Window || meta.kind == SkyPrisonUIPrefabKindV1.Popup)
                    return true;
            }
            return false;
        }

        private void SetHudHidden(bool hidden)
        {
            CanvasGroup cg = ResolveHudGroup();
            if (cg == null) return;
            OnHudVisibilityChanged?.Invoke(!hidden);

            cg.interactable = !hidden;
            cg.blocksRaycasts = !hidden;

            if (!Application.isPlaying)
            {
                cg.alpha = hidden ? 0f : 1f;
                return;
            }

            if (_hudFade != null) StopCoroutine(_hudFade);
            _hudFade = StartCoroutine(FadeHud(cg, hidden ? 0f : 1f));
        }

        private IEnumerator FadeHud(CanvasGroup cg, float target)
        {
            float start = cg.alpha;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                if (cg == null) yield break;
                cg.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / fadeDuration));
                yield return null;
            }
            if (cg != null) cg.alpha = target;
        }

        private CanvasGroup ResolveHudGroup()
        {
            if (_hudGroup != null) return _hudGroup;

            SkyPrisonRuntimeUIDriver driver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
            GameObject hud = driver != null ? driver.HudInstance : null;
            if (hud == null) hud = GameObject.Find("SkyPrisonBattleHUD_Runtime");
            if (hud == null) return null;

            _hudGroup = hud.GetComponent<CanvasGroup>();
            if (_hudGroup == null) _hudGroup = hud.AddComponent<CanvasGroup>();
            return _hudGroup;
        }
    }
}
