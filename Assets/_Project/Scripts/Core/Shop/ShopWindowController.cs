using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SkyPrison.Runtime.UI;

/// <summary>
/// 商店窗口。继承 SkyPrisonBaseWindowController，绑定 ShopDefinition 后驱动货架 + 购物车 UI。
/// 视觉结构（由 Unity 编辑器搭建）：
///   ┌──────────────────────────────────────────┐
///   │  [货架区] 左侧：物品卡片自带图标/名称/库存/单价/数量步进/加入购物车 │
///   │  [详情区] 右上：选中物品的大图 + 描述文字（不重复摆购买控件） │
///   │  [购物车] 右下：购物车条目列表 + 各币种合计         │
///   │  [结账]  右下角：结账按钮                         │
///   └──────────────────────────────────────────┘
/// </summary>
public class ShopWindowController : SkyPrisonBaseWindowController
{
    [Header("商店数据")]
    [SerializeField] private ShopDefinition shopDefinition;
    [SerializeField] private Sprite         defaultCurrencyIcon;   // 演示商店只用单一货币，直接烤这个图标
    [SerializeField] private Text           titleLabel;            // 标题栏文字——按 shopDefinition.displayNameKey 查表动态填

    [Header("货架区")]
    [SerializeField] private Transform shelfContent;      // 货架行的父节点（ScrollRect content）
    [SerializeField] private GameObject shelfRowPrefab;   // 货架行预制体
    // 售罄卡片图标要变黑白——生成/加载材质要用到 UnityEditor.AssetDatabase，运行时
    // Build里不能调，所以两份材质在编辑器生成的时候就烤进这两个字段，运行时只做
    // 引用切换，不现场生成。
    [SerializeField] private Material  normalIconMaterial;
    [SerializeField] private Material  grayscaleIconMaterial;

    // 详情区只保留大图+名字+描述——库存/货币/单价/数量步进/加入购物车都在左侧
    // 货架卡片自己身上（BindShelfRow），不在这里重复一份。
    [Header("详情区")]
    [SerializeField] private Image     detailIcon;
    [SerializeField] private Text      detailName;
    [SerializeField] private Text      detailTag;  // "分类  Lv.N" 一行，跟背包详情面板同款格式
    [SerializeField] private Text      detailDesc;

    [Header("购物车区")]
    [SerializeField] private Transform cartContent;       // 购物车行的父节点
    [SerializeField] private GameObject cartRowPrefab;    // 购物车行预制体
    // 所需/所持/结账后——三行右对齐堆叠，方便一眼比对差额（用户明确要求）。
    // 演示商店只有单一货币，按 shopDefinition.defaultCurrencyId 取余额；如果购物车
    // 里出现了别的货币，退化成把该货币也一起列进"所需"里（多货币场景以后再细化）。
    [SerializeField] private Text      cartCostText;
    [SerializeField] private Text      cartWalletText;
    [SerializeField] private Text      cartAfterText;
    [SerializeField] private Button    checkoutButton;
    [SerializeField] private Text      checkoutLabel;

    // ── 2026-07-24 新版布局：购物区/结账区二选一显示，标题栏购物车按钮切换 ──────
    [Header("购物区/结账区切换（新版布局）")]
    [SerializeField] private GameObject shoppingViewRoot;  // 货架+详情，整个一起显隐
    [SerializeField] private GameObject checkoutViewRoot;  // 结账列表整个一起显隐
    [SerializeField] private Button     cartToggleButton;  // 标题栏购物车按钮
    [SerializeField] private Text       cartCountText;     // 购物车按钮上的数量角标
    [SerializeField] private Button     goToCheckoutButton; // 购物区右下角的"结账"大按钮，跟标题栏购物车按钮做同一件事
    [SerializeField] private Text       goToCheckoutLabel;  // 上面那个按钮自己的"结账"文字
    [SerializeField] private Button     backToShopButton;   // 结账区左下角的"返回购物"按钮
    [SerializeField] private Text       backToShopLabel;    // "返回购物"文字
    [SerializeField] private Text       costLabel;          // "所需"
    [SerializeField] private Text       walletLabel;        // "所持"
    [SerializeField] private Text       afterLabel;         // "结账后"
    private bool _showingCheckout;

    // ── 运行时 ──────────────────────────────────────────────────────────
    private ShoppingCart  _cart;
    private ShopItemEntry _selected;

    private readonly List<GameObject> _shelfRows = new List<GameObject>();
    // 跟 _shelfRows 一一对应——商店等级解锁上线后 BuildShelf() 会跳过未解锁的
    // ShopItemEntry，_shelfRows.Count 不再等于 shopDefinition.items.Count，
    // RefreshShelfStock() 不能再假设"第 i 行对应第 i 个 item"，得存一份实际配对。
    private readonly List<ShopItemEntry> _shelfEntries = new List<ShopItemEntry>();
    private readonly List<GameObject> _cartRows  = new List<GameObject>();
    // 当前上架展示的商品——Fixed模式下就是shopDefinition.items全部；RandomPool模式下
    // 是每次刷新从解锁物品池里随机抽出来的那一批。只在"该刷新库存"的时候重新抽一次，
    // 不能每次BuildShelf()都重抽（切语言/选中商品也会调BuildShelf()，抽选品不该跟着变）。
    private readonly List<ShopItemEntry> _displayedPool = new List<ShopItemEntry>();
    private SkyPrisonListGamepadNav _gamepadNav;

    // 必须跟生成器里 SkyPrisonUIPrefabMetadata_V1.uiId("shop")完全一致——
    // SkyPrisonWindowManager_V1.Open() 是按 metadata.uiId 注册实例的，这里之前用
    // "shop_"+shopId 拼出来的字符串对不上，导致 Close(WindowId) 在字典里永远查不到，
    // 关闭按钮/ESC 全部静默失效（不报错，纯粹字符串不匹配）。
    protected override string WindowId => "shop";

    // 商店里所有面向玩家的文字统一走这个查表（用户明确要求"所有文字接入字典表，
    // 支持语言切换"）——跟 BuildHints() 原本就在用的 Resources.Load<UILocalizationTable>
    // 是同一张表、同一套 key 机制，查不到 key 就退回中文兜底文案，不会显示空白。
    private static string L(string key, string fallback)
    {
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        return locTable != null ? locTable.Get(key, fallback) : fallback;
    }

    // 手柄确认走 SkyPrisonListGamepadNav 里固定的 A 键，手动配对图标（原因见
    // WorldMapWindowController 同款注释）。
    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        return new[]
        {
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "选择", label = L("ui_hint_select_goods", "选择商品") },
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "选择", label = L("ui_hint_add_cart_checkout", "加入购物车/结账") },
            SkyPrisonWindowHint.Icon("keyboard/esc", "Esc", L("ui_hint_close", "关闭")),
        };
    }

    // ── 生命周期 ─────────────────────────────────────────────────────────

    protected override void OnWindowOpen()
    {
        if (shopDefinition == null) return;
        if (_gamepadNav == null) _gamepadNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();

        RefreshTitle();
        LocalizationRuntime.OnLanguageChanged += OnLanguageChangedRefreshShopTexts;

        // 初始化运行时库存
        bool firstTimeOpen = shopDefinition.items.Count > 0 && shopDefinition.items[0].remainingStock < 0;
        foreach (var entry in shopDefinition.items)
            if (entry.remainingStock < 0 || shopDefinition.refreshStockOnChapterStart)
                entry.remainingStock = entry.stock;

        // 随机商店模式下，"该不该重新抽一批上架"跟"该不该重置库存"是同一个触发条件——
        // 都是"新的一次进货"，不用另开一条规则。
        if (firstTimeOpen || shopDefinition.refreshStockOnChapterStart)
            RerollDisplayPool();

        _cart = new ShoppingCart(shopDefinition.defaultCurrencyId);
        _cart.OnCartChanged += RefreshCartUI;
        if (CurrencyRuntime.Instance != null)
            CurrencyRuntime.Instance.OnCurrencyChanged += OnCurrencyChangedRefreshSummary;

        if (goToCheckoutLabel != null) goToCheckoutLabel.text = L("shop_checkout_button", "结账");
        if (backToShopLabel   != null) backToShopLabel.text   = L("shop_back_to_shop", "返回购物");
        if (costLabel   != null) costLabel.text   = L("shop_cost_label", "所需");
        if (walletLabel != null) walletLabel.text = L("shop_wallet_label", "所持");
        if (afterLabel  != null) afterLabel.text  = L("shop_after_label", "结账后");

        BuildShelf();
        SelectEntry(_displayedPool.Count > 0 ? _displayedPool[0] : null);
        RefreshCartUI();
        RefreshCheckoutButton();

        _showingCheckout = false;
        ApplyViewVisibility();
        if (cartToggleButton != null)
        {
            cartToggleButton.onClick.RemoveAllListeners();
            cartToggleButton.onClick.AddListener(ToggleCheckoutView);
        }
        if (goToCheckoutButton != null)
        {
            goToCheckoutButton.onClick.RemoveAllListeners();
            goToCheckoutButton.onClick.AddListener(ToggleCheckoutView);
        }
        if (backToShopButton != null)
        {
            backToShopButton.onClick.RemoveAllListeners();
            backToShopButton.onClick.AddListener(ToggleCheckoutView);
        }

        RefreshGamepadTargets();
    }

    // 标题栏购物车按钮：购物区/结账区二选一显示，不是同时铺开两块。
    private void ToggleCheckoutView()
    {
        _showingCheckout = !_showingCheckout;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        ApplyViewVisibility();
        RefreshGamepadTargets();
    }

    private void ApplyViewVisibility()
    {
        if (shoppingViewRoot != null) shoppingViewRoot.SetActive(!_showingCheckout);
        if (checkoutViewRoot != null) checkoutViewRoot.SetActive(_showingCheckout);
    }

    // 货架行/购物车行/加购/结账按钮随时会因为选品、结账重建，重建后都要重喂一次。
    private void RefreshGamepadTargets()
    {
        if (_gamepadNav == null) return;
        var targets = new List<Button>();
        foreach (var row in _shelfRows)
        {
            if (row == null) continue;
            // 之前每张卡片喂了两个手柄目标(卡片本体 + PriceRow)，5列卡片正好填满
            // 一整行的时候，纵向按一下会先落到"自己的PriceRow"这个视觉上不明显的
            // second stop，再按一次才是真正到头——表现为"光标好像跑去了看不见的
            // 第六个格子"。手柄用户本来就摸不到只在鼠标hover时才显示的QuickAddButton
            // 数量面板，PriceRow这个额外纵向停靠点没有实际功能意义，直接去掉，
            // 每张卡片只留一个手柄目标，上下到头就该老老实实停住。
            var btn = row.GetComponent<Button>() ?? row.GetComponentInChildren<Button>();
            if (btn != null) targets.Add(btn);
        }
        foreach (var row in _cartRows)
        {
            if (row == null) continue;
            // RemoveButton 已经被数量步进取代（减到0直接移除），手柄目标改成喂
            // QtyMinus/QtyPlus 这两个按钮。
            var minusBtn = FindChildButton(row, "QtyGroup/QtyMinus");
            var plusBtn  = FindChildButton(row, "QtyGroup/QtyPlus");
            if (minusBtn != null) targets.Add(minusBtn);
            if (plusBtn != null) targets.Add(plusBtn);
        }
        // 之前手柄目标列表里完全没有"结账"入口按钮和"返回购物"按钮——手柄用户
        // 选完商品、加进购物车之后，压根没有路径能用手柄切到结账视图，只能靠
        // 鼠标点。按当前显示的是哪个视图，把对应能看见的那个按钮加进去。
        if (!_showingCheckout && goToCheckoutButton != null && goToCheckoutButton.gameObject.activeInHierarchy)
            targets.Add(goToCheckoutButton);
        if (_showingCheckout && backToShopButton != null && backToShopButton.gameObject.activeInHierarchy)
            targets.Add(backToShopButton);
        if (checkoutButton != null && checkoutButton.gameObject.activeInHierarchy) targets.Add(checkoutButton);
        _gamepadNav.SetTargets(targets);
    }

    protected override void OnWindowClose()
    {
        if (CurrencyRuntime.Instance != null)
            CurrencyRuntime.Instance.OnCurrencyChanged -= OnCurrencyChangedRefreshSummary;
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChangedRefreshShopTexts;
        // 按ESC关窗口的时候，如果 EventSystem 当前选中的对象正好是商店里的一个
        // InputField(比如数量输入框，之前WASD那个bug就是同一类问题)，这个对象马上
        // 就要跟着窗口一起被销毁——EventSystem 不会自动清空 currentSelectedGameObject，
        // 留着一个指向"已销毁对象"的陈旧引用，会导致关窗后场景里的鼠标点击/交互
        // 判断跟着一起失灵。关窗前主动清空选中，不留陈旧引用。
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _cart?.Clear();
        _cart = null;
        _selected = null;
    }

    // 玩家在窗口开着的时候通过别的途径拿到/花掉货币（比如触发器掉落），所需/所持/
    // 结账后三行也要跟着刷新，不能只在购物车变化时才更新。
    private void OnCurrencyChangedRefreshSummary(string currencyId, long delta)
    {
        RefreshPaymentSummary();
        RefreshCheckoutButton();
    }

    private void RefreshTitle()
    {
        if (titleLabel == null || shopDefinition == null) return;
        string fallback = !string.IsNullOrEmpty(shopDefinition.displayName) ? shopDefinition.displayName : shopDefinition.shopId;
        titleLabel.text = !string.IsNullOrEmpty(shopDefinition.displayNameKey)
            ? L(shopDefinition.displayNameKey, fallback)
            : fallback;
    }

    // 语言切换时窗口开着不重建整个UI，只重新查一遍所有文字——标题、结账/返回购物/
    // 所需所持结账后这几个静态标签、货架上每张卡片的"免费"字样和分类标签、结账
    // 按钮当前状态文字，全部重新刷一遍（用户明确要求"所有文字接入字典表，支持
    // 语言切换"，不能只是接入了查表机制但窗口开着的时候切语言没反应）。
    private void OnLanguageChangedRefreshShopTexts(string _)
    {
        RefreshTitle();
        if (goToCheckoutLabel != null) goToCheckoutLabel.text = L("shop_checkout_button", "结账");
        if (backToShopLabel   != null) backToShopLabel.text   = L("shop_back_to_shop", "返回购物");
        if (costLabel   != null) costLabel.text   = L("shop_cost_label", "所需");
        if (walletLabel != null) walletLabel.text = L("shop_wallet_label", "所持");
        if (afterLabel  != null) afterLabel.text  = L("shop_after_label", "结账后");
        BuildShelf();
        SelectEntry(_selected);
        RefreshCheckoutButton();
    }

    // ── 货架 ─────────────────────────────────────────────────────────────

    // 决定这次"该卖什么"——Fixed模式就是全部items(还是要过一遍等级过滤)；RandomPool
    // 模式先按等级筛出"当前买得到"的候选池，再洗牌抽 randomDisplayCount 件，同时给
    // 抽中的每一件按各自的随机价格区间(如果配了的话)单独抽一个价格。只在"该重新
    // 进货"的时候调用一次，不能每次刷新UI都重抽（不然选中商品、切语言都会导致
    // 货架商品跟着变，体验很怪）。
    private void RerollDisplayPool()
    {
        _displayedPool.Clear();
        foreach (var entry in shopDefinition.items) entry.rolledPrice = -1;

        if (shopDefinition.stockMode == ShopStockMode.Fixed)
        {
            foreach (var entry in shopDefinition.items)
                if (entry.unlockLevel <= shopDefinition.currentLevel)
                    _displayedPool.Add(entry);
            return;
        }

        var eligible = new List<ShopItemEntry>();
        foreach (var entry in shopDefinition.items)
            if (entry.unlockLevel <= shopDefinition.currentLevel)
                eligible.Add(entry);

        // Fisher-Yates 洗牌，取前 randomDisplayCount 个。
        for (int i = eligible.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (eligible[i], eligible[j]) = (eligible[j], eligible[i]);
        }

        int count = Mathf.Min(Mathf.Max(0, shopDefinition.randomDisplayCount), eligible.Count);
        for (int i = 0; i < count; i++)
        {
            var entry = eligible[i];
            _displayedPool.Add(entry);
            entry.rolledPrice = entry.HasRandomPriceRange
                ? Random.Range(entry.randomPriceMin, entry.randomPriceMax + 1)
                : -1;
        }
    }

    private void BuildShelf()
    {
        foreach (var go in _shelfRows) if (go) Destroy(go);
        _shelfRows.Clear();
        _shelfEntries.Clear();
        if (shelfContent == null || shelfRowPrefab == null) return;

        foreach (var entry in _displayedPool)
        {
            var row = Instantiate(shelfRowPrefab, shelfContent);
            _shelfRows.Add(row);
            _shelfEntries.Add(entry);
            BindShelfRow(row, entry);
        }
    }

    private void BindShelfRow(GameObject row, ShopItemEntry entry)
    {
        // 货架行 UI 绑定（预制体结构由编辑器决定，这里按名字找子节点）
        var nameText = FindChildText(row, "ItemName");
        if (nameText != null)
        {
            nameText.text  = entry.item != null ? entry.item.GetLocalizedDisplayName() : "—";
            nameText.color = entry.item != null ? QualityColor(entry.item.itemLevel) : Color.white;
        }

        // 图标之前一直没绑——货架卡片一直显示白块，就是漏了这一行。
        var iconTf = row.transform.Find("Icon");
        if (iconTf != null)
        {
            var iconImg = iconTf.GetComponent<Image>();
            if (iconImg != null) iconImg.sprite = entry.item != null ? entry.item.icon : null;
        }

        string cid   = entry.ResolveCurrencyId(shopDefinition.defaultCurrencyId);
        int    unitPrice = entry.ResolvePrice(cid);
        var priceText = FindChildText(row, "PriceRow/ItemPrice");
        if (priceText != null) priceText.text = unitPrice > 0 ? unitPrice.ToString() : L("shop_free", "免费");
        var priceIconTf = row.transform.Find("PriceRow/PriceIcon");
        if (priceIconTf != null)
        {
            var priceIconImg = priceIconTf.GetComponent<Image>();
            if (priceIconImg != null)
            {
                priceIconImg.sprite = defaultCurrencyIcon;
                priceIconImg.enabled = unitPrice > 0 && defaultCurrencyIcon != null;
            }
        }
        SetChildText(row, "StockText", entry.stock < 0 ? "∞" : $"{entry.remainingStock}/{entry.stock}");

        // 数量步进 + 加入购物车，常驻显示在卡片底部。数量框改成了真正能打字输入的
        // InputField（用户明确要求完善"-"/"+"和输入数字的交互，不能只靠点格子）。
        // Transform.Find不带"/"的时候只查直接子物体，不会递归——QAMinus/QAQty/QAPlus
        // 现在藏在 QuickAddButton/QtyCapsule 下面三层深，之前用裸名字查一直是null，
        // "-/+点了没反应"就是这个原因（onClick压根没挂上去）。改成带路径查。
        var qaQtyInput = FindChildInputField(row, "QuickAddButton/QtyCapsule/QAQty");
        var qaMinus   = FindChildButton(row, "QuickAddButton/QtyCapsule/QAMinus");
        var qaPlus    = FindChildButton(row, "QuickAddButton/QtyCapsule/QAPlus");
        var priceBarBtn = FindChildButton(row, "PriceRow"); // 用户要求点价格直接加入购物车，独立的"加购"按钮已删掉

        int[] qtyBox  = { 1 };
        // 之前 maxQty 只按 entry.remainingStock 算一次性的静态上限，完全不看购物车
        // 里已经放了多少——用户发现"单次最多10个，但可以重复点好几次10个"：库存
        // 显示10/10全程不变(结账前不会真的扣库存)，步进器每次都还能拉到10，
        // ShoppingCart.AddOrIncrement 内部虽然会按"库存-购物车已有量"correctly夹到0
        // 不让真的超卖，但 DoAddToCart 那边不管有没有真加成功都照样播确认音效、
        // 数量框照样清空重置成1，看起来就像"又成功加了一份"，容易让人怀疑总量
        // 超过了库存。改成动态算"库存 - 购物车里这件商品已有的量"，步进器到头
        // 就是到头，加不进去的时候给拒绝反馈而不是假装成功。
        int EffectiveMaxQty()
        {
            if (entry.stock < 0) return 99;
            int already = _cart?.GetQuantity(entry) ?? 0;
            return Mathf.Max(0, entry.remainingStock - already);
        }

        // 加不进购物车的时候（库存被购物车占满）角标改灰色，不再是冷绿色，直观
        // 告诉玩家这个操作现在点了也没用（用户明确要求）。
        var addBadgeBg  = row.transform.Find("PriceRow/AddToCartBadge")?.GetComponent<Image>();
        var addBadgeIcon = row.transform.Find("PriceRow/AddToCartBadge/Icon")?.GetComponent<Image>();
        void RefreshAddBadgeState()
        {
            bool canAdd = EffectiveMaxQty() > 0;
            Color bg = canAdd ? SkyPrisonUIPalette.ColdGreen : new Color(0.45f, 0.45f, 0.47f, 1f);
            Color icon = canAdd ? new Color(0.08f, 0.1f, 0.09f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
            if (addBadgeBg != null) addBadgeBg.color = bg;
            if (addBadgeIcon != null) addBadgeIcon.color = icon;
        }

        // 数量变化时价格也要跟着乘上去显示（用户明确要求），不能只显示单价。
        void RefreshQaQty()
        {
            if (qaQtyInput != null) qaQtyInput.SetTextWithoutNotify(qtyBox[0].ToString());
            if (priceText != null) priceText.text = unitPrice > 0 ? (unitPrice * qtyBox[0]).ToString() : L("shop_free", "免费");
            RefreshAddBadgeState();
        }
        RefreshQaQty();
        if (qaMinus != null)
        {
            qaMinus.onClick.RemoveAllListeners();
            qaMinus.onClick.AddListener(() => { qtyBox[0] = Mathf.Max(1, qtyBox[0] - 1); RefreshQaQty(); });
        }
        if (qaPlus != null)
        {
            qaPlus.onClick.RemoveAllListeners();
            qaPlus.onClick.AddListener(() => { qtyBox[0] = Mathf.Max(1, Mathf.Min(EffectiveMaxQty(), qtyBox[0] + 1)); RefreshQaQty(); });
        }
        if (qaQtyInput != null)
        {
            // 打字过程中就实时把价格乘上去（用户明确要求），但不夹数值范围/不改
            // qtyBox——半个数字（比如"1"后面还没打完"12"）此时夹范围会打断输入体验；
            // 真正提交(失焦/回车)的时候再校验夹到[1,maxQty]。
            qaQtyInput.onValueChanged.RemoveAllListeners();
            qaQtyInput.onValueChanged.AddListener(text =>
            {
                if (priceText == null) return;
                if (!int.TryParse(text, out int typed) || typed <= 0) return;
                priceText.text = unitPrice > 0 ? (unitPrice * typed).ToString() : L("shop_free", "免费");
            });

            qaQtyInput.onEndEdit.RemoveAllListeners();
            qaQtyInput.onEndEdit.AddListener(text =>
            {
                if (!int.TryParse(text, out int typed)) typed = qtyBox[0];
                qtyBox[0] = Mathf.Clamp(typed, 1, Mathf.Max(1, EffectiveMaxQty()));
                RefreshQaQty();
            });
        }

        var capturedForCart = entry;
        void DoAddToCart()
        {
            int before = _cart?.GetQuantity(capturedForCart) ?? 0;
            _cart?.AddOrIncrement(capturedForCart, qtyBox[0]);
            int after = _cart?.GetQuantity(capturedForCart) ?? 0;
            // 库存已经被购物车里已有的量占满时，AddOrIncrement 内部会把实际能加的
            // 数量夹到0、什么都不做——之前这里不管有没有真的加进去都照样播确认音效
            // +清空数量框，看起来像"又成功加了一份"。改成按实际是否真的增加了来
            // 决定播哪个音效，加不进去就明确给拒绝反馈。
            SkyPrisonSystemSEPlayer.Play(after > before ? SkyPrisonSystemSEType.Confirm : SkyPrisonSystemSEType.Forbidden);
            qtyBox[0] = Mathf.Max(1, Mathf.Min(qtyBox[0], EffectiveMaxQty()));
            RefreshQaQty();
        }
        if (priceBarBtn != null)
        {
            priceBarBtn.onClick.RemoveAllListeners();
            priceBarBtn.onClick.AddListener(DoAddToCart);
        }

        // 点击卡片 → 选中该商品；已经选中的情况下再按一次直接加入购物车（手柄专用
        // 路径——之前手柄靠额外喂一个PriceRow纵向导航目标来够到加购按钮，5列卡片
        // 正好填满一整行时会多出一个不明显的纵向停靠点，表现为"光标跑去看不见的
        // 第六格"，改成只喂卡片这一个手柄目标后，鼠标用户仍然点PriceRow加购，
        // 手柄用户靠"选中→再按一次A"达到同样效果，不用再靠那个多余的纵向停靠点）。
        var btn = row.GetComponent<Button>() ?? row.GetComponentInChildren<Button>();
        if (btn != null)
        {
            var captured = entry;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                if (_selected == captured) DoAddToCart();
                else SelectEntry(captured);
            });
        }

        ApplySoldOutVisual(row, entry);
    }

    // 售罄——图标变黑白 + 整卡调暗 + 居中"售罄"文字（用户明确要求，之前这里找的
    // CanvasGroup 组件卡片prefab上根本没挂，这段代码一直是死代码，从没生效过）。
    private void ApplySoldOutVisual(GameObject row, ShopItemEntry entry)
    {
        bool outOfStock = entry.IsOutOfStock;

        var cg = row.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = outOfStock ? 0.22f : 1f; // 用户反馈0.4不够暗，压得更低一点

        var iconTf = row.transform.Find("Icon");
        var iconImg = iconTf != null ? iconTf.GetComponent<Image>() : null;
        if (iconImg != null)
            iconImg.material = outOfStock ? grayscaleIconMaterial : normalIconMaterial;

        var soldOutTf = row.transform.Find("SoldOutText");
        if (soldOutTf != null)
        {
            soldOutTf.gameObject.SetActive(outOfStock);
            var soldOutText = soldOutTf.GetComponent<Text>();
            if (soldOutText != null) soldOutText.text = L("shop_sold_out", "售罄");
        }

        var hover = row.GetComponent<ShopShelfRowHover>();
        if (hover != null) hover.SoldOut = outOfStock;
    }

    // ── 详情区 ───────────────────────────────────────────────────────────

    private void SelectEntry(ShopItemEntry entry)
    {
        // 切换选中商品之前一直是静音的——其它窗口(背包等)切换选中项都会给一个
        // "光标移动"提示音，商店漏了这个反馈，用户反馈"光标移动怎么没有音效"。
        // 只在真的换了商品时才播，跟 InventoryWindowController 的判断方式一致，
        // 避免同一个商品重复点击/首次 BuildShelf 自动选中时也叮一下。
        if (entry != _selected)
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        _selected = entry;

        if (entry?.item == null)
        {
            SetActive(detailIcon, false);
            SetActive(detailName, false);
            return;
        }

        SetActive(detailIcon, true);
        SetActive(detailName, true);

        if (detailIcon  != null) detailIcon.sprite = entry.item.icon;
        if (detailName  != null)
        {
            detailName.text  = entry.item.GetLocalizedDisplayName();
            detailName.color = QualityColor(entry.item.itemLevel);
        }
        if (detailTag != null) detailTag.text = BuildItemTagLine(entry.item);
        if (detailDesc  != null) detailDesc.text   = entry.item.GetLocalizedDescription();

        RefreshGamepadTargets();
    }

    // ── 购物车区 ─────────────────────────────────────────────────────────

    private void RefreshCartUI()
    {
        foreach (var go in _cartRows) if (go) Destroy(go);
        _cartRows.Clear();

        if (_cart == null) return;

        int lineIndex = 0;
        foreach (var line in _cart.Lines)
        {
            if (cartContent == null || cartRowPrefab == null) break;
            var row = Instantiate(cartRowPrefab, cartContent);
            _cartRows.Add(row);

            // 一深一浅交替填充，方便区分行（用户明确要求）。
            var rowBg = row.GetComponent<Image>();
            if (rowBg != null)
                rowBg.color = lineIndex % 2 == 0 ? new Color(1f, 1f, 1f, 0.04f) : new Color(1f, 1f, 1f, 0.09f);
            lineIndex++;

            var iconTf = row.transform.Find("Icon");
            if (iconTf != null)
            {
                var iconImg = iconTf.GetComponent<Image>();
                if (iconImg != null) iconImg.sprite = line.shopEntry.item != null ? line.shopEntry.item.icon : null;
            }

            SetChildText(row, "CartItemName",  line.shopEntry.item.GetLocalizedDisplayName());
            var cartNameText = row.transform.Find("CartItemName")?.GetComponent<Text>();
            if (cartNameText != null) cartNameText.color = QualityColor(line.shopEntry.item.itemLevel);
            // 之前这里查"CartItemQty"没带路径前缀，Transform.Find不带"/"只查直接
            // 子物体——CartItemQty实际嵌在QtyGroup下面一层，一直找不到，数字从来
            // 没被设过("看不见"不是显示问题，是压根没赋值)。现在CartItemQty也换成
            // InputField了，直接用SetTextWithoutNotify设，不走SetChildText那条
            // 只认Text组件的路径。
            var qtyInput = FindChildInputField(row, "QtyGroup/CartItemQty");
            if (qtyInput != null) qtyInput.SetTextWithoutNotify(line.quantity.ToString());
            // 货币用图标表示，不再显示"token"这种货币ID文字（用户明确要求）。
            SetChildText(row, "TotalGroup/CartItemTotal", line.lineTotal.ToString());
            var totalIconTf = row.transform.Find("TotalGroup/TotalIcon");
            if (totalIconTf != null)
            {
                var totalIconImg = totalIconTf.GetComponent<Image>();
                if (totalIconImg != null) totalIconImg.sprite = defaultCurrencyIcon;
            }

            // 数量步进直接改购物车数量（用户明确要求）——减到0时 ShoppingCart.
            // SetQuantity() 内部会把这一行从购物车里移除并触发 OnCartChanged，
            // 不用再单独找一个"删除"按钮。
            var capturedEntry = line.shopEntry;
            var minusBtn = FindChildButton(row, "QtyGroup/QtyMinus");
            var plusBtn  = FindChildButton(row, "QtyGroup/QtyPlus");
            if (minusBtn != null)
            {
                minusBtn.onClick.AddListener(() =>
                {
                    int newQty = _cart.GetQuantity(capturedEntry) - 1;
                    _cart.SetQuantity(capturedEntry, newQty);
                    SkyPrisonSystemSEPlayer.Play(newQty <= 0 ? SkyPrisonSystemSEType.Cancel : SkyPrisonSystemSEType.Switch);
                });
            }
            if (plusBtn != null)
            {
                plusBtn.onClick.AddListener(() =>
                {
                    int newQty = _cart.GetQuantity(capturedEntry) + 1;
                    _cart.SetQuantity(capturedEntry, newQty);
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                });
            }
            if (qtyInput != null)
            {
                // 直接打字改数量（用户明确要求"没法输入"）——SetQuantity内部已经会
                // 按库存夹范围、传0或负数会把这一行从购物车里整个移除，不用在这里
                // 再单独判断。
                qtyInput.onEndEdit.AddListener(text =>
                {
                    if (!int.TryParse(text, out int typed)) typed = _cart.GetQuantity(capturedEntry);
                    _cart.SetQuantity(capturedEntry, typed);
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                });
            }
        }

        RefreshPaymentSummary();

        if (cartCountText != null)
        {
            int totalQty = 0;
            foreach (var line in _cart.Lines) totalQty += line.quantity;
            cartCountText.text = totalQty > 0 ? totalQty.ToString() : "";
            // cartCountText 只是圆形角标里的数字文字，之前只隐藏它，圆形背景(父物体
            // "Count")还留在原地——用户反馈"没东西的时候右上角不需要有这个圆形"，
            // 得连圆形背景一起隐藏，所以隐藏它的父物体而不是它自己。
            Transform badgeRoot = cartCountText.transform.parent;
            if (badgeRoot != null) badgeRoot.gameObject.SetActive(totalQty > 0);
        }

        RefreshCheckoutButton();
        RefreshGamepadTargets();
    }

    private void RefreshCheckoutButton()
    {
        if (checkoutButton == null) return;

        bool canCheckout = _cart != null && !_cart.IsEmpty
                           && _cart.CanAfford(CurrencyRuntime.Instance)
                           && InventoryRuntimeBootstrap.Instance?.Inventory != null;

        // interactable 故意保持 true（不设成 false）——用户明确要求"点击是拒绝"，
        // 如果 interactable=false，Button.onClick 根本不会触发，OnCheckout() 里的
        // Forbidden 反馈音永远播不出来，点了跟没点一样。真正的"不可用"完全靠下面
        // 的置灰视觉 + OnCheckout() 内部的条件判断来体现。
        //
        // 框的描边不是 UnityEngine.UI.Outline 组件——SkyPrisonFloatingWindowKit.
        // AddOutline() 实际是拼了 OT/OB/OL/OR 四条独立的 Image 当边框（跟角落括号
        // 同一套做法），之前用 GetComponent<Outline>() 找，永远是 null，置灰从来没
        // 生效过（框一直是生成时烘焙的固定绿色）。改成直接找这四个子物体重新上色。
        Color frameColor = canCheckout ? SkyPrisonUIPalette.ColdGreen : new Color(0.5f, 0.5f, 0.5f, 0.6f);
        SetFrameColor(checkoutButton.transform, frameColor);

        if (checkoutLabel != null)
        {
            checkoutLabel.text = canCheckout
                ? L("shop_checkout_button", "结账")
                : (_cart == null || _cart.IsEmpty ? L("shop_cart_empty", "购物车为空") : L("shop_insufficient_funds", "余额不足"));
            checkoutLabel.color = canCheckout ? SkyPrisonUIPalette.ColdGreen : new Color(0.6f, 0.6f, 0.6f, 1f);
        }

        checkoutButton.onClick.RemoveAllListeners();
        checkoutButton.onClick.AddListener(OnCheckout);
    }

    // 淡红色——付不起的时候"结账后"显示负数用这个颜色，跟正常的冷绿区分开。
    private static readonly Color InsufficientFundsColor = new Color(1f, 0.4f, 0.4f, 1f);

    // 所需/所持/结账后三行，全部右对齐（用户明确要求）。演示商店只有单一货币，
    // 按 shopDefinition.defaultCurrencyId 取值；购物车里出现别的货币的情况暂不处理
    // （目前项目里只有这一种演示货币）。
    private void RefreshPaymentSummary()
    {
        if (cartCostText == null && cartWalletText == null && cartAfterText == null) return;
        if (_cart == null || shopDefinition == null) return;

        string cid = shopDefinition.defaultCurrencyId;
        var totals = _cart.GetTotals();
        long cost = totals.TryGetValue(cid, out long c) ? c : 0;
        long wallet = CurrencyRuntime.Instance != null ? CurrencyRuntime.Instance.Get(cid) : 0;
        long after = wallet - cost;

        // 标签("所需"/"所持"/"结账后")是编辑器生成时烘焙好的静态文字，这几个字段
        // 只是数字部分——数字要带千分位逗号（用户明确要求），用 "N0" 格式化。
        if (cartCostText != null) cartCostText.text = cost.ToString("N0");
        if (cartWalletText != null) cartWalletText.text = wallet.ToString("N0");
        if (cartAfterText != null)
        {
            cartAfterText.text = after.ToString("N0");
            cartAfterText.color = after < 0 ? InsufficientFundsColor : SkyPrisonUIPalette.ColdGreen;
        }
    }

    private void OnCheckout()
    {
        if (_cart == null || InventoryRuntimeBootstrap.Instance?.Inventory == null) return;

        // 购物车为空时不算"余额不足"/"背包满了"这两种 ShoppingCart.Checkout() 已知
        // 的失败结果（空购物车 CanAfford 对着空 totals 循环直接放行，会被当成
        // "结账成功"空跑一次）——按钮平时按钮保持 interactable=true（不然用户点了
        // 灰按钮没有任何反馈），这里单独拦一下空购物车，给个拒绝反馈。
        if (_cart.IsEmpty)
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return;
        }

        var result = _cart.Checkout(CurrencyRuntime.Instance, InventoryRuntimeBootstrap.Instance?.Inventory);
        switch (result)
        {
            case ShoppingCart.CheckoutResult.Success:
                // 结账成功用专门的"购买/支付"音效，不跟通用Confirm共用——需要在
                // SkyPrisonSystemSETable（Tools/音声设置）里给 Purchase 这个类型配
                // clip，没配的话播放静默不报错（走的是SkyPrisonSystemSEPlayer已有
                // 的"没配clip就跳过"逻辑）。
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Purchase);
                RefreshShelfStock();
                // 结账成功后自动切回购物视图，不用玩家自己点"返回购物"——购物车这时
                // 已经清空，停在结账页没什么意义，用户确认过这个方向。
                if (_showingCheckout) ToggleCheckoutView();
                break;
            case ShoppingCart.CheckoutResult.InsufficientFunds:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                break;
            case ShoppingCart.CheckoutResult.InventoryFull:
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                break;
        }
    }

    // 结账后刷新货架行的库存显示
    private void RefreshShelfStock()
    {
        for (int i = 0; i < _shelfRows.Count && i < _shelfEntries.Count; i++)
        {
            var entry = _shelfEntries[i];
            SetChildText(_shelfRows[i], "StockText",
                entry.stock < 0 ? "∞" : $"{entry.remainingStock}/{entry.stock}");

            ApplySoldOutVisual(_shelfRows[i], entry);
        }
    }

    // ── 小工具 ──────────────────────────────────────────────────────────

    private static void SetChildText(GameObject root, string childName, string text)
    {
        if (root == null) return;
        var t = root.transform.Find(childName);
        if (t == null) return;
        var tx = t.GetComponent<Text>();
        if (tx != null) tx.text = text;
    }

    private static Button FindChildButton(GameObject root, string childName)
    {
        if (root == null) return null;
        var t = root.transform.Find(childName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    private static Text FindChildText(GameObject root, string childName)
    {
        if (root == null) return null;
        var t = root.transform.Find(childName);
        return t != null ? t.GetComponent<Text>() : null;
    }

    private static InputField FindChildInputField(GameObject root, string childName)
    {
        if (root == null) return null;
        var t = root.transform.Find(childName);
        return t != null ? t.GetComponent<InputField>() : null;
    }

    // "分类  Lv.N" 一行——跟 InventoryItemDetailPanel.BuildTagLine 是同一套文案
    // 格式(品级用富文本按品质上色)，那边几个辅助方法是 private，没法直接复用，
    // 这里按同样的映射规则各自留一份小的（用户明确要求商店详情区也要显示品级/类型）。
    private static string BuildItemTagLine(ItemDefinition item)
    {
        if (item == null) return "";
        string cat = item.majorCategory == ItemMajorCategory.Equipment
            ? EquipmentCategoryLabel(item.equipment)
            : CategoryLabel(item.category);
        string lv = $"<color=#{QualityHex(item.itemLevel)}>Lv.{item.itemLevel}</color>";
        // 两个空格用户反馈还是挤在一起——一行里塞更多空格比调行间距靠谱(这行大多数
        // 情况下根本不会换行，之前调lineSpacing对这种单行情况没用)。
        return $"{cat}      {lv}";
    }

    // key跟 InventoryItemDetailPanel 那边用的是同一批("item_cat_xxx")，两处共用
    // 一张本地化表条目，不用各自另起一套翻译。
    private static string EquipmentCategoryLabel(ItemEquipmentExtension eq)
    {
        if (eq == null) return L("item_cat_equipment", "装备");
        return eq.slot switch
        {
            EquipmentSlotType.Weapon          => L("item_cat_weapon", "武器"),
            EquipmentSlotType.WeaponSecondary => L("item_cat_weapon", "武器"),
            EquipmentSlotType.Head            => L("item_cat_armor", "防具"),
            EquipmentSlotType.UpperBody       => L("item_cat_armor", "防具"),
            EquipmentSlotType.LowerBody       => L("item_cat_armor", "防具"),
            EquipmentSlotType.Hands           => L("item_cat_armor", "防具"),
            EquipmentSlotType.Shoes           => L("item_cat_armor", "防具"),
            _                                 => L("item_cat_equipment", "装备")
        };
    }

    private static string CategoryLabel(ItemCategory c) => c switch
    {
        ItemCategory.Consumable => L("item_cat_consumable", "消耗品"),
        ItemCategory.Material   => L("item_cat_material", "材料"),
        ItemCategory.Quest      => L("item_cat_quest", "任务道具"),
        ItemCategory.Currency   => L("item_cat_currency", "凭证"),
        ItemCategory.Special    => L("item_cat_special", "特殊"),
        _                       => L("item_cat_general", "道具")
    };

    // 跟 InventoryItemDetailPanel.QualityHex 保持同一套品质颜色映射，不能自己另起一份。
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

    // 用户反馈商店里的物品名字没跟着品级上色——之前用的是 SkyPrisonUIPalette.
    // GetRarityColor(item.rarity)，但项目里其它地方(背包详情面板)物品名字实际
    // 按 itemLevel 走 QualityHex 这套配色，rarity 字段不是真正在用的那一套，
    // 名字颜色跟下面"Lv.N"标签的颜色对不上。改成同一个 QualityHex 来源。
    private static Color QualityColor(int lv)
    {
        return ColorUtility.TryParseHtmlString("#" + QualityHex(lv), out Color c) ? c : Color.white;
    }

    // AddOutline() 拼出来的四条边框子物体固定叫 OT/OB/OL/OR。
    private static void SetFrameColor(Transform root, Color c)
    {
        foreach (string n in new[] { "OT", "OB", "OL", "OR" })
        {
            var t = root.Find(n);
            var img = t != null ? t.GetComponent<Image>() : null;
            if (img != null) img.color = c;
        }
    }

    private static void SetActive(Component c, bool active)
    {
        if (c != null) c.gameObject.SetActive(active);
    }
}
