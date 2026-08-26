using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 战斗HUD右下角武器切换条——鼠标滚轮切换主/副武器（逻辑早就在
    /// EquipmentRuntime.CycleActiveWeapon 里，这里只负责视觉表现）。
    ///
    /// 布局：上面一个大格子显示当前生效武器剪影+弹药数，下面一个小格子（半透明）
    /// 显示另一把武器，跟物品快捷栏同一套"四角方块+两侧竖线、内部透明"的边框语言，
    /// 只是尺寸更大。滚轮切换时两个格子互换位置（大变小、小变大，同时挪动到对方
    /// 位置），做成来回滚动的观感。
    ///
    /// 不走 SkyPrisonPlayerHUDBuilder 那套 StyleProfile/LayoutProfile 数据驱动管线——
    /// 那套是给HP/LP/QuickSlot这些美术已经定稿、需要美术反复调参的模块用的，这里
    /// 是全新功能，先用自包含的运行时程序化UI跑起来，等美术真正定稿风格数值后
    /// 再考虑要不要迁移进那套管线，不要本末倒置。
    ///
    /// 自动创建：不需要手动挂在任何 Prefab 上，SkyPrisonRuntimeSystemsBootstrapper
    /// 之外单独用 [RuntimeInitializeOnLoadMethod] 自举，模式照抄 SkyPrisonCustomCursor/
    /// PlayerHUDStatusIconBar 这类"自己找HUD、自己挂内容"的运行时单例写法。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkyPrisonWeaponSwitchHUD : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!Application.isPlaying) return;
            EnsureInScene();
        }

        public static SkyPrisonWeaponSwitchHUD EnsureInScene()
        {
            if (FindObjectOfType<SkyPrisonWeaponSwitchHUD>() is { } existing) return existing;
            var go = new GameObject("[SkyPrisonWeaponSwitchHUD_Runtime]");
            DontDestroyOnLoad(go);
            return go.AddComponent<SkyPrisonWeaponSwitchHUD>();
        }

        // 武器框固定宽高比 616:340（美术画布比例，剪影图按这个比例画，Image用
        // PreserveAspect 显示，不会拉伸变形）。
        private const float FrameAspect = 616f / 340f;

        [Header("尺寸（大格子；小格子按比例缩小）")]
        [SerializeField] private float mainWidth = 294f; // 210 * 1.4
        // 整体先放大1.4倍（mainWidth已经是放大后的值），高度按616:340算完之后，
        // 宽度再单独多拉30%——故意让实际显示比例比原图宽一些，不是等比缩放。
        [SerializeField] private float extraWidthStretch = 1.3f;
        [SerializeField] private float secondaryScale = 0.68f;
        [SerializeField] private float slotGap = 10f;
        [SerializeField] private float marginRight = 40f;
        [SerializeField] private float marginBottom = 40f;
        // 副武器格子平时（没在切换动画里）完全透明——不是"半透明常驻显示"，只有
        // 滚轮触发切换的那一瞬间跟着大格子互换位置/透明度时才会短暂可见，切换完
        // 落到新的空位后又变回0（因为新的"副武器格子"复用的就是这套0起点逻辑）。
        [SerializeField] private float secondaryAlpha = 0f;
        [SerializeField] private float swapDuration = 0.22f;

        [Header("耐久度警示")]
        [Tooltip("剩余耐久比例（当前/最大）低于这个值时，剪影变淡红色提醒该修/换了。")]
        [Range(0f, 1f)]
        [SerializeField] private float lowDurabilityThreshold = 0.2f;
        [SerializeField] private Color lowDurabilityTint = new Color(1f, 0.5f, 0.5f, 0.92f);
        private static readonly Color NormalSilhouetteColor = new Color(1f, 1f, 1f, 0.92f);

        // 徒手（没装备武器）时显示的默认剪影——全局唯一一份，不属于任何具体武器，
        // 所以不放在 ItemEquipmentExtension 里，走 Resources 固定路径加载。
        // 素材放 Assets/Resources/UI/HUD/Default_hand.png（要在 Resources 文件夹
        // 下面才能被 Resources.Load 找到，放 Icon/Equipment 那些非Resources目录
        // 加载不到）。
        private const string DefaultHandResourcesPath = "UI/HUD/Default_hand";
        private static Sprite _defaultHandSilhouetteCache;
        private static bool _defaultHandLoadAttempted;

        private static Sprite ResolveDefaultHandSilhouette()
        {
            if (!_defaultHandLoadAttempted)
            {
                _defaultHandLoadAttempted  = true;
                _defaultHandSilhouetteCache = Resources.Load<Sprite>(DefaultHandResourcesPath);
                if (_defaultHandSilhouetteCache == null)
                    Debug.LogWarning($"[SkyPrisonWeaponSwitchHUD] 找不到默认空手剪影，期望路径：Resources/{DefaultHandResourcesPath}.png");
            }
            return _defaultHandSilhouetteCache;
        }

        // 弹药数字字体要跟战斗掉血数字同一款——伤害数字实际用的字体来自
        // OBS_NewStyle.asset(OverheadBarStyleAsset).damageNumberFontAsset，指向
        // "MRT-すくてらむ SDF"。这个字体原本放在 UIUX/Fonts/TMP 下（普通资产目录，
        // Editor 用序列化引用直接拖，不走 Resources.Load），这个 HUD 是纯运行时代码
        // 生成、没有 Inspector 可以拖引用——照抄上面 DefaultHandSilhouette 那套
        // Resources 固定路径方案，把这份字体挪到 Resources/UI/HUD 下（同一个GUID，
        // 挪动不影响 OBS_NewStyle.asset 那边已经序列化好的引用，Unity 按GUID解析
        // 不按路径）。
        private const string AmmoFontResourcesPath = "UI/HUD/MRT-すくてらむ SDF";
        private static TMP_FontAsset _ammoFontCache;
        private static bool _ammoFontLoadAttempted;

        private static TMP_FontAsset ResolveAmmoFont()
        {
            if (!_ammoFontLoadAttempted)
            {
                _ammoFontLoadAttempted = true;
                _ammoFontCache = Resources.Load<TMP_FontAsset>(AmmoFontResourcesPath);
                if (_ammoFontCache == null)
                    Debug.LogWarning($"[SkyPrisonWeaponSwitchHUD] 找不到伤害数字同款字体，期望路径：Resources/{AmmoFontResourcesPath}.asset");
            }
            return _ammoFontCache;
        }

        private RectTransform _contentRoot;
        private WeaponSlotCard _cardA;
        private WeaponSlotCard _cardB;
        private Coroutine _swapRoutine;

        private float _searchCooldown;
        private const float SearchInterval = 1.5f;

        // 之前只订阅了"换装备"/"武器耐久变化"/"开枪换弹"这几个事件——买/捡弹药走的是
        // InventoryRuntime.AddItem，只会触发 InventoryRuntime.OnInventoryChanged，
        // 这个HUD完全没听这个事件，所以"背包弹药"这半个数字买了之后不会立刻刷新，
        // 得等下次开枪/换弹这些别的事件顺带把它带出来才会更新。InventoryRuntime是
        // 实例事件（不是静态的），且这个HUD在OnEnable那一刻背包系统不一定已经初始化
        // 完，没法直接订阅，改成跟_contentRoot一样的"每帧检查一次直到拿到实例"惰性
        // 订阅模式。
        private InventoryRuntime _subscribedInventory;

        private class WeaponSlotCard
        {
            public RectTransform root;
            public Image frame;
            public Image silhouette;
            public TMP_Text ammoText;
            public CanvasGroup canvasGroup;
        }

        private void OnEnable()
        {
            EquipmentRuntime.OnEquipped         += HandleEquipmentChanged;
            EquipmentRuntime.OnUnequipped       += HandleEquipmentChanged;
            EquipmentRuntime.OnActiveWeaponChanged += HandleActiveWeaponChanged;
            // 武器耐久在战斗中随时会磨损（UnitCombatHitbox命中时调用
            // DurabilitySystem.Wear），不订阅这个事件的话，剪影的低耐久变色只会在
            // 下次装备/切换武器时才刷新，磨损当下不会立刻变红。
            DurabilitySystem.OnDurabilityChanged += HandleDurabilityChanged;
            // 弹匣消耗(开火)/换弹补充都不会触发上面那几个"换装备"事件——同一把枪连续
            // 开火/换弹时xx/xx数字必须实时跟着变，靠这个专门的弹药变化事件刷新。
            UnitActionModuleRuntime.OnWeaponAmmoChanged += RefreshContentImmediate;
        }

        private void OnDisable()
        {
            EquipmentRuntime.OnEquipped         -= HandleEquipmentChanged;
            EquipmentRuntime.OnUnequipped       -= HandleEquipmentChanged;
            EquipmentRuntime.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
            DurabilitySystem.OnDurabilityChanged -= HandleDurabilityChanged;
            UnitActionModuleRuntime.OnWeaponAmmoChanged -= RefreshContentImmediate;

            if (_subscribedInventory != null)
            {
                _subscribedInventory.OnInventoryChanged -= RefreshContentImmediate;
                _subscribedInventory = null;
            }
        }

        private void Update()
        {
            if (_subscribedInventory == null)
            {
                InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
                if (inv != null)
                {
                    inv.OnInventoryChanged += RefreshContentImmediate;
                    _subscribedInventory = inv;
                }
            }

            if (_contentRoot != null) return;

            _searchCooldown -= Time.unscaledDeltaTime;
            if (_searchCooldown <= 0f)
            {
                TryBuildContent();
                _searchCooldown = SearchInterval;
            }
        }

        private void HandleEquipmentChanged(EquipmentSlotType slot, InventoryItemEntry entry)
        {
            if (slot != EquipmentSlotType.Weapon && slot != EquipmentSlotType.WeaponSecondary) return;
            RefreshContentImmediate();
        }

        private void HandleActiveWeaponChanged(EquipmentSlotType newActiveSlot)
        {
            if (_contentRoot == null) return;
            if (_swapRoutine != null) StopCoroutine(_swapRoutine);
            _swapRoutine = StartCoroutine(SwapRoutine());
        }

        private void HandleDurabilityChanged(InventoryItemEntry entry, int oldDurability, int newDurability)
        {
            // 不管磨损的是不是当前显示中的这把武器都直接整体刷新一次——两个格子最多
            // 显示两把武器，判断"是不是这一把"反而比直接重新读一遍更麻烦，这个方法
            // 开销也小，不用省这点。
            RefreshContentImmediate();
        }

        // ── 挂到 HUD 上 ───────────────────────────────────────────────────────

        private void TryBuildContent()
        {
            var driver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
            GameObject hud = driver != null ? driver.HudInstance : null;
            if (hud == null) return;

            var rootGo = new GameObject("WeaponSwitchHUD", typeof(RectTransform));
            rootGo.transform.SetParent(hud.transform, false);
            SetLayerRecursive(rootGo, LayerMask.NameToLayer("UI"));

            _contentRoot = rootGo.GetComponent<RectTransform>();
            // 屏幕右下角——跟PlayerStatusArea（左下角HP/LP）完全独立，不共用锚点。
            _contentRoot.anchorMin = new Vector2(1f, 0f);
            _contentRoot.anchorMax = new Vector2(1f, 0f);
            _contentRoot.pivot     = new Vector2(1f, 0f);

            // 高度先按未拉宽前的宽度算比例，宽度最后再单独乘 extraWidthStretch——
            // 这样"宽度多拉30%"只影响宽度，不会连带把高度也一起拉高。
            float mainHeight   = mainWidth / FrameAspect;
            float secHeight    = mainHeight * secondaryScale;
            float mainWidthUI  = mainWidth * extraWidthStretch;
            float secWidthUI   = mainWidthUI * secondaryScale;
            float totalHeight = mainHeight + slotGap + secHeight;
            float totalWidth  = Mathf.Max(mainWidthUI, secWidthUI);

            _contentRoot.anchoredPosition = new Vector2(-marginRight, marginBottom);
            _contentRoot.sizeDelta        = new Vector2(totalWidth, totalHeight);

            // 大格子在上（生效武器），小格子在下（备用武器）——都以 _contentRoot
            // 右下角为锚点对齐，宽度不同时右边缘对齐（贴合HUD惯用的靠右对齐习惯）。
            Vector2 mainAnchoredPos = new Vector2(0f, secHeight + slotGap);
            Vector2 secAnchoredPos  = new Vector2(0f, 0f);

            _cardA = BuildCard(_contentRoot, "MainSlot", new Vector2(mainWidthUI, mainHeight), mainAnchoredPos, 1f);
            _cardB = BuildCard(_contentRoot, "SecondarySlot", new Vector2(secWidthUI, secHeight), secAnchoredPos, secondaryAlpha);

            RefreshContentImmediate();
        }

        private WeaponSlotCard BuildCard(Transform parent, string name, Vector2 size, Vector2 anchoredPos, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SetLayerRecursive(go, LayerMask.NameToLayer("UI"));

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot     = new Vector2(1f, 0f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta        = size;

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = alpha;

            // 内部透明填充——只留边框语言（四角方块+两侧竖线），跟物品快捷栏一致，
            // 不挡住底下战场画面。
            Image frame = go.AddComponent<Image>();
            frame.color = new Color(0f, 0f, 0f, 0f);
            frame.raycastTarget = false;

            BuildCornerMarkers(rect);
            BuildSideTicks(rect);

            var silGo = new GameObject("Silhouette", typeof(RectTransform));
            silGo.transform.SetParent(rect, false);
            SetLayerRecursive(silGo, LayerMask.NameToLayer("UI"));
            Image silhouette = silGo.AddComponent<Image>();
            silhouette.raycastTarget  = false;
            silhouette.preserveAspect = true;
            silhouette.color = new Color(1f, 1f, 1f, 0.92f);
            RectTransform silRect = silGo.GetComponent<RectTransform>();
            // 不加任何内边距——剪影图在画布里的构图（位置/大小/留白）是美术自己
            // 统一按616:340画布画好的，格子本身就是照这个比例做的，直接铺满整个
            // 格子照图显示即可，不该由代码这边再额外拉伸/收缩/加边距去"二次构图"。
            SetStretch(silRect, Vector2.zero, Vector2.zero);

            var ammoGo = new GameObject("AmmoText", typeof(RectTransform));
            ammoGo.transform.SetParent(rect, false);
            SetLayerRecursive(ammoGo, LayerMask.NameToLayer("UI"));
            TMP_Text ammoText = ammoGo.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset ammoFont = ResolveAmmoFont();
            if (ammoFont != null)
                ammoText.font = ammoFont;
            // 之前贴在右下角，正好压在枪身上——枪剪影是照616:340整张画布画的，下半张
            // 塞满了枪身/弹匣，右下角躲不开重叠。画布上方留白明显更多，挪到右上角
            // 基本能完全让开枪身轮廓。
            ammoText.alignment = TextAlignmentOptions.TopRight;
            ammoText.color     = Color.white;
            ammoText.raycastTarget = false;
            ApplyAmmoTextUnderlayGlow(ammoText);
            RectTransform ammoRect = ammoGo.GetComponent<RectTransform>();
            // 改成固定锚点+固定宽度（不再是横向拉伸满宽），"往左挪"才能真的通过
            // anchoredPosition.x 生效——横向拉伸满宽时 sizeDelta.x=0 会导致锚点
            // 始终精确贴在格子右边缘，anchoredPosition.x 那个偏移量根本不起作用。
            ammoRect.anchorMin = new Vector2(1f, 1f);
            ammoRect.anchorMax = new Vector2(1f, 1f);
            ammoRect.pivot     = new Vector2(1f, 1f);

            var card = new WeaponSlotCard
            {
                root        = rect,
                frame       = frame,
                silhouette  = silhouette,
                ammoText    = ammoText,
                canvasGroup = group
            };
            ApplyAmmoLayout(card, size);
            return card;
        }

        // 弹药数字的框/字号是按"当前格子实际大小"算的比例，不是写死的像素值——
        // 之前只在建卡片那一刻算过一次，切换动画把 card.root.sizeDelta 从大格子
        // 动画到小格子（或反过来）之后，弹药文字的框/字号却没跟着一起变，导致主/
        // 副武器来回切换后数字忽大忽小、位置也跟着偏，被你发现了。现在每次格子
        // 尺寸变化（建卡片时、切换动画每一帧）都重新套用一次，两边永远保持同一套
        // 跟随格子大小的比例关系。
        private static void ApplyAmmoLayout(WeaponSlotCard card, Vector2 cardSize)
        {
            float ammoBoxHeight = cardSize.y * 0.35f;
            float ammoBoxWidth  = cardSize.x * 0.6f;
            RectTransform ammoRect = card.ammoText.rectTransform;
            // 锚点/pivot都是(1,1)——anchoredPosition.y 从卡片顶边往上量。整个数字框
            // 的高度(ammoBoxHeight)全部让到卡片顶边之外，再加一点间隙(gapAboveCard)，
            // 保证数字完全悬在武器槽上方，跟槽内的剪影/边框完全不重叠。
            const float gapAboveCard = 4f;
            ammoRect.anchoredPosition = new Vector2(0f, ammoBoxHeight + gapAboveCard);
            ammoRect.sizeDelta        = new Vector2(ammoBoxWidth, ammoBoxHeight);
            card.ammoText.fontSize = Mathf.Min(Mathf.Max(14f, cardSize.y * 0.22f) * 2.3f, ammoBoxHeight * 0.9f);
        }

        // 四角小方块——跟快捷物品栏 BuildQuickSlotCornerMarkers 同一套视觉语言。
        private static void BuildCornerMarkers(Transform parent)
        {
            const float cornerSize = 6f;
            Color cornerColor = new Color(1f, 1f, 1f, 0.5f);
            AddCorner(parent, new Vector2(0f, 0f), cornerSize, cornerColor);
            AddCorner(parent, new Vector2(1f, 0f), cornerSize, cornerColor);
            AddCorner(parent, new Vector2(0f, 1f), cornerSize, cornerColor);
            AddCorner(parent, new Vector2(1f, 1f), cornerSize, cornerColor);
        }

        private static void AddCorner(Transform parent, Vector2 anchor, float size, Color color)
        {
            var go = new GameObject("Corner", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SetLayerRecursive(go, LayerMask.NameToLayer("UI"));
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot     = anchor;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);
        }

        // 左右两侧竖线——照物品快捷栏截图里那种上下贯穿的细竖线提示。
        private static void BuildSideTicks(Transform parent)
        {
            const float tickWidth = 2f;
            Color tickColor = new Color(1f, 1f, 1f, 0.35f);
            AddSideTick(parent, 0f, tickWidth, tickColor);
            AddSideTick(parent, 1f, tickWidth, tickColor);
        }

        private static void AddSideTick(Transform parent, float xAnchor, float width, Color color)
        {
            var go = new GameObject("SideTick", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SetLayerRecursive(go, LayerMask.NameToLayer("UI"));
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(xAnchor, 0.15f);
            rect.anchorMax = new Vector2(xAnchor, 0.85f);
            rect.pivot     = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, 0f);
        }

        // ── 内容刷新 ─────────────────────────────────────────────────────────

        private void RefreshContentImmediate()
        {
            if (_contentRoot == null || _cardA == null || _cardB == null) return;

            var eq = EquipmentRuntime.Instance;
            EquipmentSlotType activeSlot = eq != null ? eq.ActiveWeaponSlot : EquipmentSlotType.Weapon;
            EquipmentSlotType otherSlot  = activeSlot == EquipmentSlotType.Weapon
                ? EquipmentSlotType.WeaponSecondary
                : EquipmentSlotType.Weapon;

            ApplyWeaponToCard(_cardA, eq?.GetEquipped(activeSlot));
            ApplyWeaponToCard(_cardB, eq?.GetEquipped(otherSlot));
        }

        private void ApplyWeaponToCard(WeaponSlotCard card, InventoryItemEntry entry)
        {
            if (card == null) return;

            ItemEquipmentExtension ext = entry?.definition?.equipment;

            // 没装备武器（ext==null）才显示默认空手剪影——不能用"weaponSilhouette
            // 是不是空"来判断，那样会把"真没装备武器"和"装备了武器但美术剪影还没配"
            // 这两种完全不同的情况混成一样的显示（角色明明拿着武器，HUD却显示空手，
            // 看着像没生效）。武器已装备但剪影缺失时应该留空（不显示），提醒该把
            // 这把武器的剪影图配上，而不是冒充"空手"。
            Sprite silhouetteSprite = ext != null ? ext.weaponSilhouette : ResolveDefaultHandSilhouette();

            card.silhouette.sprite  = silhouetteSprite;
            card.silhouette.enabled = silhouetteSprite != null;

            // 耐久度快见底时剪影变淡红色提醒——只对真正有耐久系统的武器生效
            // （maxDurability<=0 的装备本来就不参与耐久，比如饰品；空手更没有耐久
            // 可言，都保持正常白色）。
            card.silhouette.color = IsDurabilityLow(entry, ext) ? lowDurabilityTint : NormalSilhouetteColor;

            if (ext == null)
            {
                card.ammoText.text = "∞"; // 徒手也算"近战"，不消耗弹药
                return;
            }

            if (!ext.usesAmmo)
            {
                card.ammoText.text = "∞"; // 近战/不吃弹药武器显示无穷符号
                return;
            }

            // 2026-07-21：热武器改成"弹匣/备用"两段式显示（xx/xx，经典FPS弹药条格式）——
            // 弹匣数是这把武器实例自己的 currentMagazineAmmo（真正参与攻击消耗的数值），
            // 备用数是背包里这个口径的剩余弹药（换弹时从这里补充），两个数字来源不同，
            // 不能只显示其中一个。
            int loaded = entry.currentMagazineAmmo >= 0 ? entry.currentMagazineAmmo : 0;
            var inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
            int reserve = inventory != null ? inventory.GetAmmoCount(ext.ammoCaliber) : 0;
            card.ammoText.text = $"{loaded}/{reserve}";
        }

        // 复用 DurabilitySystem.GetRatio——跟工房/装备详情面板显示耐久用的是同一套
        // 口径（HasDurability 内部已经处理了 maxDurability<=0 / currentDurability<0
        // 这些"不参与耐久系统"的情况，返回1，不会被误判成低耐久），不用自己重复算。
        private bool IsDurabilityLow(InventoryItemEntry entry, ItemEquipmentExtension ext)
        {
            if (entry == null || ext == null || ext.maxDurability <= 0) return false;
            return DurabilitySystem.GetRatio(entry) <= lowDurabilityThreshold;
        }

        // ── 滚轮切换动画：两个格子互换位置+大小+透明度，做成滚动交换的观感 ─────

        private IEnumerator SwapRoutine()
        {
            // _cardA 在动画开始前永远是"当前大格子"，本来就显示着"切换前生效的那把
            // 武器"——切换之后它会缩小变成小格子，而这正好就是小格子该显示的内容
            // （切换前生效的武器变成了"备用"），完全不用碰它的内容。
            // 真正需要换内容的只有 _cardB（当前小格子，即将放大变成新的大格子）——
            // 把"切换后生效的武器"提前塞给它，让内容跟位置动画同步到位，不会出现
            // "格子先放大、图标一拍之后才换"的割裂感。
            var eq = EquipmentRuntime.Instance;
            EquipmentSlotType newActiveSlot = eq != null ? eq.ActiveWeaponSlot : EquipmentSlotType.Weapon;
            ApplyWeaponToCard(_cardB, eq?.GetEquipped(newActiveSlot));

            Vector2 posA0 = _cardA.root.anchoredPosition, sizeA0 = _cardA.root.sizeDelta;
            float alphaA0 = _cardA.canvasGroup.alpha;
            Vector2 posB0 = _cardB.root.anchoredPosition, sizeB0 = _cardB.root.sizeDelta;
            float alphaB0 = _cardB.canvasGroup.alpha;

            // 目标：A、B 互换彼此当前的位置/大小/透明度（大格子<->小格子对调）。
            Vector2 posA1 = posB0, sizeA1 = sizeB0; float alphaA1 = alphaB0;
            Vector2 posB1 = posA0, sizeB1 = sizeA0; float alphaB1 = alphaA0;

            float t = 0f;
            while (t < swapDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / swapDuration));

                _cardA.root.anchoredPosition = Vector2.Lerp(posA0, posA1, k);
                _cardA.root.sizeDelta        = Vector2.Lerp(sizeA0, sizeA1, k);
                _cardA.canvasGroup.alpha      = Mathf.Lerp(alphaA0, alphaA1, k);
                ApplyAmmoLayout(_cardA, _cardA.root.sizeDelta);

                _cardB.root.anchoredPosition = Vector2.Lerp(posB0, posB1, k);
                _cardB.root.sizeDelta        = Vector2.Lerp(sizeB0, sizeB1, k);
                _cardB.canvasGroup.alpha      = Mathf.Lerp(alphaB0, alphaB1, k);
                ApplyAmmoLayout(_cardB, _cardB.root.sizeDelta);

                yield return null;
            }

            _cardA.root.anchoredPosition = posA1; _cardA.root.sizeDelta = sizeA1; _cardA.canvasGroup.alpha = alphaA1;
            _cardB.root.anchoredPosition = posB1; _cardB.root.sizeDelta = sizeB1; _cardB.canvasGroup.alpha = alphaB1;
            ApplyAmmoLayout(_cardA, sizeA1);
            ApplyAmmoLayout(_cardB, sizeB1);

            // 动画结束后 A/B 两个物理卡片已经"占据了对方原来的格子"——交换引用，
            // 让 _cardA 永远代表"当前显示在大格子里的那张卡"，下次刷新/动画时
            // 位置计算不用关心上一次到底谁是谁。
            (_cardA, _cardB) = (_cardB, _cardA);
            _swapRoutine = null;
        }

        // ── 小工具 ───────────────────────────────────────────────────────────

        private static void SetStretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot     = new Vector2(0.5f, 0.5f);
            rect.offsetMin = min;
            rect.offsetMax = -max;
        }

        /// <summary>
        /// 给弹药数文字加一层贴着字形轮廓的模糊暗影（TMP 自带的 Underlay 功能，不是
        /// 磨砂窗口那套截屏模糊——Underlay 是 SDF 字体 shader 里专门做"贴合字形的软阴影/
        /// 发光"用的，参数直接控制在字形周围晕开的柔度，天生就是贴着轮廓的模糊，不用
        /// 额外拿一份贴图去做高斯模糊。
        ///
        /// 不能直接改 ammoText.fontSharedMaterial——那是整个项目默认字体共用的同一份
        /// 材质实例，改了会让所有没单独设置材质的 TMP 文本一起被拖上这层暗影。改成
        /// 读 fontMaterial（TMP 在这个属性第一次被访问时会自动复制一份材质实例出来），
        /// 只影响这一个文字组件自己。
        /// </summary>
        // 色收差shader（Sky Prison/UI/HUD TMP Chromatic V83）挂到这个动态生成的弹药
        // 文字上会花屏，参数对不对都一样——项目里这个shader真正的使用路径是
        // SkyPrisonPlayerHUDBuilder，作用对象是烤好的HUD prefab里静态的TMP节点，那边的
        // 字体atlas在编辑期就已经固定；这里的AmmoText是运行时new出来的动态TMP文本，用的
        // 是运行时动态SDF atlas，字形随时可能重新打包/换页，这个shader按静态atlas假设
        // 写的UV采样跟动态atlas对不上，才会花。换个稳的方案：黑描边（跟快捷栏1/2/3/4
        // 数字同一套 PatchQuickSlotsHUDCorners.ApplyBlackOutline），保证不出问题。
        private static void ApplyAmmoTextUnderlayGlow(TMP_Text ammoText)
        {
            Material sharedMat = ammoText.fontSharedMaterial;
            if (sharedMat == null)
                return;

            if (!sharedMat.HasProperty("_OutlineWidth"))
                return;

            Material mat = new Material(sharedMat);
            // TMP 的 SDF shader 描边是 shader_feature 开关控制的分支(OUTLINE_ON)，只设
            // _OutlineWidth 数值、不开这个关键字，着色器里那条分支根本没编译进去，数值
            // 白设——这大概率就是黑描边"设了但看不见"的真正原因。
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat("_OutlineWidth", 0.133f);
            mat.SetColor("_OutlineColor", Color.black);
            if (mat.HasProperty("_OutlineSoftness"))
                mat.SetFloat("_OutlineSoftness", 0.05f);

            // 同上一版色收差踩过的坑：只设 fontSharedMaterial 可能被 TMP 自己后续的
            // 初始化流程用 fontAsset 默认材质覆盖掉，三处一起设保证不管初始化顺序如何
            // 最终生效的都是这份材质。
            ammoText.fontSharedMaterial = mat;
            ammoText.fontMaterial = mat;
            ammoText.material = mat;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0) return;
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
