using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 界面设置编辑器窗口（主界面配置 + 读条设置）。
/// 菜单：Tools → 界面设置
/// </summary>
public class MainMenuSettingsWindow : EditorWindow
{
    private const string MainMenuAssetPath  = "Assets/_Project/Resources/MainMenuSettings.asset";
    private const string LoadingAssetPath   = "Assets/_Project/Resources/LoadingScreenSettings.asset";
    // 之前这个路径写错了(Config目录下从来没有过这份资产)，实际资产在Data/Resources
    // 下——UILocalizationTableEditor/单位定义那边的多语言编辑功能之所以正常，是因为
    // 它们各自另外单独查表，没受这个写错的常量牵连；这个窗口里所有依赖LocTablePath
    // 的功能（主菜单本地化同步、商店名字多语言）全部因为这一个路径一直读不到表。
    private const string LocTablePath       = "Assets/_Project/Data/Resources/UILocalizationTable.asset";
    private const string SidebarIconAssetPath = "Assets/_Project/Resources/SettingsSidebarIconSettings.asset";

    // ── 状态 ─────────────────────────────────────────────────────────────
    private int _tab;
    private static readonly string[] TabLabels = { "主界面配置", "读条设置", "设置界面书签", "商店" };

    // 商店
    private readonly List<ShopDefinition> _shops = new List<ShopDefinition>();
    private ShopDefinition _selectedShop;
    private Vector2 _shopListScroll;
    private Vector2 _shopDetailScroll;
    private const string ShopAssetFolder = "Assets/_Project/Data/Definitions/Custom/Shop";
    private readonly List<CurrencyDefinition> _currencies = new List<CurrencyDefinition>();

    // 设置界面书签
    private SettingsSidebarIconSettings _sidebarIconSettings;
    private Vector2 _sidebarIconScroll;

    // 主界面
    private MainMenuSettings  _mainSettings;
    private Editor            _mainEditor;
    private Vector2           _mainScroll;

    // 读条
    private LoadingScreenSettings _loadingSettings;
    private Vector2               _loadingScroll;
    private int                   _tipFoldoutIndex = -1;

    // 已知语言列表（从本地化表读，或使用默认）
    private List<string> _langCodes;

    // ── 场景列表缓存（Build Settings）────────────────────────────────────
    private string[] _sceneNames  = System.Array.Empty<string>();
    private string[] _scenePaths  = System.Array.Empty<string>();
    private int      _sceneIndex  = -1;   // 当前选中的下拉索引

    // ── Scene View 拾取模式 ───────────────────────────────────────────────
    private bool _picking = false;

    [MenuItem("Tools/界面设置")]
    public static void Open()
    {
        var win = GetWindow<MainMenuSettingsWindow>("界面设置");
        win.minSize = new Vector2(480f, 560f);
        win.LoadAll();
    }

    private void OnEnable()
    {
        LoadAll();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        _picking = false;
    }

    private void LoadAll()
    {
        LoadMainMenu();
        LoadLoading();
        LoadSidebarIcons();
        LoadLangCodes();
        RefreshSceneList();
        LoadCurrencies();
        LoadShops();
    }

    private void LoadCurrencies()
    {
        _currencies.Clear();
        string[] guids = AssetDatabase.FindAssets("t:CurrencyDefinition");
        foreach (string guid in guids)
        {
            var c = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (c != null) _currencies.Add(c);
        }
        _currencies.Sort((a, b) => string.Compare(a.currencyId, b.currencyId, System.StringComparison.OrdinalIgnoreCase));
    }

    // ── 设置界面书签 ──────────────────────────────────────────────────────

    private void LoadSidebarIcons()
    {
        _sidebarIconSettings = AssetDatabase.LoadAssetAtPath<SettingsSidebarIconSettings>(SidebarIconAssetPath);
        if (_sidebarIconSettings == null)
        {
            EnsureFolder("Assets/_Project/Resources");
            _sidebarIconSettings = CreateInstance<SettingsSidebarIconSettings>();
            AssetDatabase.CreateAsset(_sidebarIconSettings, SidebarIconAssetPath);
            AssetDatabase.SaveAssets();
        }

        // 数组长度跟着 SkyPrisonSettingsTabDefinitions 走——万一以后分类数量改了，
        // 这里自动补齐/截断，不用手动去改这个资产。
        int expected = SkyPrisonSettingsTabDefinitions.Count;
        if (_sidebarIconSettings.tabIcons == null || _sidebarIconSettings.tabIcons.Length != expected)
        {
            var resized = new Texture2D[expected];
            if (_sidebarIconSettings.tabIcons != null)
            {
                int copyLen = Mathf.Min(expected, _sidebarIconSettings.tabIcons.Length);
                for (int i = 0; i < copyLen; i++) resized[i] = _sidebarIconSettings.tabIcons[i];
            }
            _sidebarIconSettings.tabIcons = resized;
            EditorUtility.SetDirty(_sidebarIconSettings);
            AssetDatabase.SaveAssets();
        }
    }

    private void DrawSidebarIconTab()
    {
        if (_sidebarIconSettings == null) { LoadSidebarIcons(); return; }

        EditorGUILayout.LabelField("设置界面书签", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "给设置窗口左侧每个分类配一个图标（可选）。留空的分类不显示图标、也不占位置；" +
            "配了图标的分类，文字会自动让出一个图标的位置。",
            MessageType.Info);
        EditorGUILayout.Space(4);

        _sidebarIconScroll = EditorGUILayout.BeginScrollView(_sidebarIconScroll);
        EditorGUI.BeginChangeCheck();
        for (int i = 0; i < SkyPrisonSettingsTabDefinitions.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(SkyPrisonSettingsTabDefinitions.FallbackLabels[i], GUILayout.Width(80));
            _sidebarIconSettings.tabIcons[i] = (Texture2D)EditorGUILayout.ObjectField(
                _sidebarIconSettings.tabIcons[i], typeof(Texture2D), false);
            EditorGUILayout.EndHorizontal();
        }
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_sidebarIconSettings);
            AssetDatabase.SaveAssets();
        }
        EditorGUILayout.EndScrollView();
    }

    // ── Tab 3：商店 ───────────────────────────────────────────────────────
    // 左侧商店包列表 + 右侧编辑详情（用户明确要求的布局）。商店名字走
    // UILocalizationTable 的 key 机制，跟主界面/读条那两个 Tab 是同一套本地化系统，
    // 不是 ItemDefinition 那种资产自带 localizedNames 列表——一个 Key 对应
    // UILocalizationTable 里一条按语言分的文字，编辑器这里直接读写那个表。

    private void LoadShops()
    {
        _shops.Clear();
        string[] guids = AssetDatabase.FindAssets("t:ShopDefinition");
        foreach (string guid in guids)
        {
            var shop = AssetDatabase.LoadAssetAtPath<ShopDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (shop != null) _shops.Add(shop);
        }
        _shops.Sort((a, b) => string.Compare(a.shopId, b.shopId, System.StringComparison.OrdinalIgnoreCase));

        if (_selectedShop != null && !_shops.Contains(_selectedShop))
            _selectedShop = null;
    }

    private void DrawShopTab()
    {
        EditorGUILayout.LabelField("商店", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "左侧管理商店包（ShopDefinition 资产），右侧编辑选中商店的名字/货币/等级和货架商品。" +
            "商店名字是多语言的，实际文字存在 UILocalizationTable 里，切换语言会自动生效。",
            MessageType.Info);
        EditorGUILayout.Space(4);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawShopList();
            GUILayout.Space(6);
            DrawShopDetail();
        }
    }

    private void DrawShopList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
        {
            EditorGUILayout.LabelField("商店包列表", EditorStyles.miniBoldLabel);

            _shopListScroll = EditorGUILayout.BeginScrollView(_shopListScroll, "box", GUILayout.ExpandHeight(true));
            foreach (var shop in _shops)
            {
                if (shop == null) continue;
                bool selected = shop == _selectedShop;
                GUI.backgroundColor = selected ? new Color(0.35f, 0.65f, 1f) : Color.white;
                string label = string.IsNullOrWhiteSpace(shop.displayName) ? shop.shopId : $"{shop.displayName} ({shop.shopId})";
                if (GUILayout.Button(label, GUILayout.Height(24)))
                    _selectedShop = shop;
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("＋ 新建商店包", GUILayout.Height(26)))
                CreateNewShop();

            using (new EditorGUI.DisabledScope(_selectedShop == null))
            {
                if (GUILayout.Button("－ 删除选中商店包", GUILayout.Height(22)))
                    DeleteSelectedShop();
            }
        }
    }

    private void CreateNewShop()
    {
        EnsureFolder(ShopAssetFolder);
        string baseId = "new_shop";
        string id = baseId;
        int suffix = 1;
        while (_shops.Exists(s => s != null && s.shopId == id))
            id = $"{baseId}_{suffix++}";

        string path = AssetDatabase.GenerateUniqueAssetPath($"{ShopAssetFolder}/SD_{id}.asset");
        var shop = CreateInstance<ShopDefinition>();
        shop.shopId = id;
        shop.displayName = "新商店";
        shop.displayNameKey = "shop_name_" + id;
        AssetDatabase.CreateAsset(shop, path);
        AssetDatabase.SaveAssets();

        LoadShops();
        _selectedShop = shop;
    }

    private void DeleteSelectedShop()
    {
        if (_selectedShop == null) return;
        string path = AssetDatabase.GetAssetPath(_selectedShop);
        if (!EditorUtility.DisplayDialog("删除商店包", $"确定要删除商店包「{_selectedShop.shopId}」吗？此操作无法撤销。", "删除", "取消"))
            return;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        _selectedShop = null;
        LoadShops();
    }

    private void DrawShopDetail()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            if (_selectedShop == null)
            {
                EditorGUILayout.HelpBox("请先在左侧选择或新建一个商店包。", MessageType.Info);
                return;
            }

            ShopDefinition shop = _selectedShop;
            _shopDetailScroll = EditorGUILayout.BeginScrollView(_shopDetailScroll);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("基本信息", EditorStyles.miniBoldLabel);
            string newShopId = EditorGUILayout.TextField("商店 ID", shop.shopId);
            if (newShopId != shop.shopId)
            {
                shop.shopId = newShopId;
                // Key 跟着 shopId 走，改 ID 就重新派生一次 Key——旧 Key 下的翻译不会
                // 自动带过来（等于换了个新商店身份），这是当前最简单可预期的行为。
                shop.displayNameKey = "shop_name_" + newShopId;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("商店名字（多语言，Key：" + shop.displayNameKey + "）", EditorStyles.miniBoldLabel);
            DrawShopNameLocalization(shop);

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("默认货币", GUILayout.Width(140));
            shop.defaultCurrencyId = DrawCurrencyPopup(shop.defaultCurrencyId, false);
            EditorGUILayout.EndHorizontal();
            shop.currentLevel = Mathf.Max(0, EditorGUILayout.IntField(
                new GUIContent("商店当前等级", "货架只展示 商品解锁等级 <= 这个值 的商品；以后会由中枢等级等外部系统驱动提升。"),
                shop.currentLevel));
            shop.refreshStockOnChapterStart = EditorGUILayout.Toggle("章节开始时重置库存", shop.refreshStockOnChapterStart);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("货架商品", EditorStyles.miniBoldLabel);
            DrawShopItemsList(shop);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(shop);
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存", GUILayout.Height(28)))
                {
                    EditorUtility.SetDirty(shop);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("选中资产", GUILayout.Height(28)))
                    Selection.activeObject = shop;
            }
        }
    }

    // 商店名字的多语言文字——不走 ItemDefinition 那种资产自带列表，直接读写
    // UILocalizationTable，跟 UILocalizationTableEditor.DrawEntryLanguageFields
    // 同一套做法，这样商店名字跟其它 UI 文字共用一张表、切语言统一生效。
    private void DrawShopNameLocalization(ShopDefinition shop)
    {
        var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(LocTablePath);
        if (table == null)
        {
            EditorGUILayout.HelpBox("找不到 UILocalizationTable，无法编辑多语言名字。", MessageType.Warning);
            return;
        }

        var settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
        {
            EditorGUILayout.HelpBox("找不到 LocalizationProjectSettings。", MessageType.Warning);
            return;
        }

        var codes = new List<string>();
        foreach (var lang in settings.languages)
            if (lang != null && lang.enabled) codes.Add(lang.languageCode);
        if (codes.Count == 0) codes.Add("zh-CN");

        var entry = table.EnsureEntry(shop.displayNameKey, codes);

        // 默认语言优先显示
        foreach (var lang in settings.languages)
        {
            if (lang == null || !lang.enabled || !lang.isDefault) continue;
            DrawShopNameLangRow(entry, lang, shop);
        }
        foreach (var lang in settings.languages)
        {
            if (lang == null || !lang.enabled || lang.isDefault) continue;
            DrawShopNameLangRow(entry, lang, shop);
        }

        EditorUtility.SetDirty(table);
    }

    private void DrawShopNameLangRow(UILocalizationEntry entry, LocalizationProjectSettings.LanguageEntry lang, ShopDefinition shop)
    {
        var textEntry = entry.texts.Find(t => t.languageCode == lang.languageCode);
        if (textEntry == null) return;

        string label = (string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName)
                       + (lang.isDefault ? "（默认）" : "");
        string newText = EditorGUILayout.TextField(label, textEntry.text);
        if (newText != textEntry.text)
        {
            textEntry.text = newText;
            if (lang.isDefault) shop.displayName = newText; // 默认语言同步进 displayName 兜底字段
        }
    }

    // 货币改成选项式（用户明确要求，参考现有 CurrencyDefinition 资产列表选，不再手打
    // 字符串 ID）——allowEmpty=true 用于"空=用商店默认货币"这种可留空的覆盖字段，
    // false 用于商店自己的默认货币（必须选一个）。资产库里一个货币都没有时退化成
    // 文本框兜底，不然 Popup 空数组会出问题、还会把人卡在这个页面出不去。
    private string DrawCurrencyPopup(string currentId, bool allowEmpty, params GUILayoutOption[] options)
    {
        if (_currencies.Count == 0)
            return EditorGUILayout.TextField(currentId, options);

        var labels = new List<string>();
        var ids = new List<string>();
        if (allowEmpty) { labels.Add("(默认)"); ids.Add(""); }
        foreach (var c in _currencies)
        {
            labels.Add(string.IsNullOrWhiteSpace(c.displayName) ? c.currencyId : $"{c.displayName}({c.currencyId})");
            ids.Add(c.currencyId);
        }

        int selected = ids.IndexOf(currentId ?? "");
        if (selected < 0) selected = 0;
        int newSelected = EditorGUILayout.Popup(selected, labels.ToArray(), options);
        newSelected = Mathf.Clamp(newSelected, 0, ids.Count - 1);
        return ids[newSelected];
    }

    private void DrawShopItemsList(ShopDefinition shop)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.miniBoldLabel);
        GUILayout.Label("物品", GUILayout.Width(140));
        GUILayout.Label("货币(空=默认)", GUILayout.Width(90));
        GUILayout.Label("价格(0=按物品)", GUILayout.Width(90));
        GUILayout.Label("库存(0=无限)", GUILayout.Width(80));
        GUILayout.Label("解锁等级", GUILayout.Width(60));
        GUILayout.Label("", GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < shop.items.Count; i++)
        {
            ShopItemEntry entry = shop.items[i];
            EditorGUILayout.BeginHorizontal();

            // 用户明确要求跟"物品池"一样用带图标网格的选择窗口，不要Unity默认那个
            // 纯文字列表的 ObjectField 弹窗——直接复用科技树那边已有的
            // SkyPrisonItemPickerPopup，同一套选择体验，不用重新做一个。
            string itemButtonLabel = entry.item != null ? entry.item.name : "（点击选择物品）";
            if (GUILayout.Button(itemButtonLabel, GUILayout.Width(140)))
            {
                ShopItemEntry capturedEntry = entry;
                SkyPrisonItemPickerPopup.Open(entry.item, picked =>
                {
                    capturedEntry.item = picked as ItemDefinition;
                    EditorUtility.SetDirty(shop);
                }, "ItemDefinition");
            }
            entry.currencyOverride = DrawCurrencyPopup(entry.currencyOverride, true, GUILayout.Width(90));
            entry.priceOverride = Mathf.Max(0, EditorGUILayout.IntField(entry.priceOverride, GUILayout.Width(90)));

            // 界面上 0 表示无限库存，跟 ShopItemEntry.stock 内部约定的 -1 不是同一个数——
            // 运行时那套(IsOutOfStock/ResolvePrice等)全都认 -1，这里只在编辑器输入层做转换，
            // 不改运行时约定，省得牵连一大片已有逻辑。
            int displayStock = entry.stock < 0 ? 0 : entry.stock;
            int newDisplayStock = Mathf.Max(0, EditorGUILayout.IntField(displayStock, GUILayout.Width(80)));
            entry.stock = newDisplayStock <= 0 ? -1 : newDisplayStock;

            entry.unlockLevel = Mathf.Max(0, EditorGUILayout.IntField(entry.unlockLevel, GUILayout.Width(60)));

            if (GUILayout.Button("删除", GUILayout.Width(40)))
            {
                shop.items.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("＋ 添加商品", GUILayout.Height(24)))
            shop.items.Add(new ShopItemEntry());
    }

    // ── Build Settings 场景列表 ───────────────────────────────────────────

    private void RefreshSceneList()
    {
        var scenes = EditorBuildSettings.scenes;
        var names  = new List<string>();
        var paths  = new List<string>();
        foreach (var s in scenes)
        {
            if (!s.enabled) continue;
            string n = System.IO.Path.GetFileNameWithoutExtension(s.path);
            names.Add(n);
            paths.Add(s.path);
        }
        _sceneNames = names.ToArray();
        _scenePaths = paths.ToArray();

        // 同步当前选中
        if (_mainSettings != null)
            _sceneIndex = System.Array.IndexOf(_sceneNames, _mainSettings.newGameScene);
    }

    // ── Scene View 拾取 ───────────────────────────────────────────────────

    private void OnSceneGUI(SceneView sv)
    {
        if (!_picking || _mainSettings == null) return;

        // 整个 Scene View 消耗鼠标事件，防止 Unity 默认行为
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        Event e = Event.current;

        // 绘制提示文字
        Handles.BeginGUI();
        GUI.Box(new Rect(10, sv.position.height - 60, 340, 36),
            "点击地面设定出生坐标 | ESC 取消", EditorStyles.helpBox);
        Handles.EndGUI();

        // 绘制当前位置标记
        Handles.color = new Color(0.3f, 1f, 0.5f, 0.9f);
        Handles.DrawWireDisc(_mainSettings.newGameSpawnPosition, Vector3.up, 0.5f);
        Handles.DrawLine(_mainSettings.newGameSpawnPosition,
                         _mainSettings.newGameSpawnPosition + Vector3.up * 2f);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            // 从鼠标位置向场景做射线检测
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                _mainSettings.newGameSpawnPosition = hit.point;
                // 朝向保持不变，用户可自行填写
            }
            else
            {
                // 没有碰撞体时，落在 Y=0 平面
                float t = ray.origin.y / -ray.direction.y;
                if (t > 0)
                    _mainSettings.newGameSpawnPosition = ray.origin + ray.direction * t;
            }

            EditorUtility.SetDirty(_mainSettings);
            AssetDatabase.SaveAssets();
            _picking = false;
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            _picking = false;
            Repaint();
            e.Use();
        }

        sv.Repaint();
    }

    // ── 主界面 ────────────────────────────────────────────────────────────

    private void LoadMainMenu()
    {
        _mainSettings = AssetDatabase.LoadAssetAtPath<MainMenuSettings>(MainMenuAssetPath);
        if (_mainSettings == null)
        {
            EnsureFolder("Assets/_Project/Resources");
            _mainSettings = CreateInstance<MainMenuSettings>();
            var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(LocTablePath);
            if (table != null) _mainSettings.localizationTable = table;
            AssetDatabase.CreateAsset(_mainSettings, MainMenuAssetPath);
            AssetDatabase.SaveAssets();
        }
        _mainEditor = Editor.CreateEditor(_mainSettings);
    }

    // ── 读条设置 ──────────────────────────────────────────────────────────

    private void LoadLoading()
    {
        _loadingSettings = AssetDatabase.LoadAssetAtPath<LoadingScreenSettings>(LoadingAssetPath);
        if (_loadingSettings == null)
        {
            EnsureFolder("Assets/_Project/Resources");
            _loadingSettings = CreateInstance<LoadingScreenSettings>();
            // 默认填一条示例 tip
            var tip = new LoadingTip { tipName = "示例 Tip" };
            tip.texts.Add(new LocalizedTipText { languageCode = "zh-CN", richText = "死亡后背包内所有物品将永久丢失，请谨慎行事。" });
            tip.texts.Add(new LocalizedTipText { languageCode = "en",    richText = "All inventory items are lost permanently upon death." });
            _loadingSettings.tips.Add(tip);
            AssetDatabase.CreateAsset(_loadingSettings, LoadingAssetPath);
            AssetDatabase.SaveAssets();
        }
    }

    private void LoadLangCodes()
    {
        _langCodes = new List<string> { "zh-CN", "en", "ja" };
        var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(LocTablePath);
        if (table == null) return;
        var found = new List<string>();
        foreach (var e in table.entries)
            foreach (var t in e.texts)
                if (!found.Contains(t.languageCode))
                    found.Add(t.languageCode);
        if (found.Count > 0) _langCodes = found;
    }

    // ── OnGUI ────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        _tab = GUILayout.Toolbar(_tab, TabLabels, GUILayout.Height(28));
        EditorGUILayout.Space(4);

        if (_tab == 0)      DrawMainMenuTab();
        else if (_tab == 1) DrawLoadingTab();
        else if (_tab == 2) DrawSidebarIconTab();
        else                DrawShopTab();
    }

    // ── Tab 0：主界面配置 ─────────────────────────────────────────────────

    private void DrawMainMenuTab()
    {
        if (_mainSettings == null) { LoadMainMenu(); return; }

        EditorGUILayout.LabelField("主界面配置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "修改后点击「保存」，主界面运行时会自动读取此配置。\n" +
            "LOGO 图片需先 Import 为 Sprite 类型再拖入。",
            MessageType.Info);
        EditorGUILayout.Space(4);

        _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);
        EditorGUI.BeginChangeCheck();
        _mainEditor.OnInspectorGUI();
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_mainSettings);
            AssetDatabase.SaveAssets();
        }
        EditorGUILayout.EndScrollView();

        // ── 新游戏起点 ────────────────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("新游戏起点", EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope(1))
        {
            // 场景下拉
            RefreshSceneList();
            int newIdx = EditorGUILayout.Popup("目标场景", _sceneIndex, _sceneNames);
            if (newIdx != _sceneIndex && newIdx >= 0 && newIdx < _sceneNames.Length)
            {
                _sceneIndex = newIdx;
                _mainSettings.newGameScene = _sceneNames[newIdx];
                EditorUtility.SetDirty(_mainSettings);
            }
            // 允许手填（场景不在 Build Settings 时兜底）
            string typed = EditorGUILayout.TextField("  或手动输入场景名", _mainSettings.newGameScene);
            if (typed != _mainSettings.newGameScene)
            {
                _mainSettings.newGameScene = typed;
                _sceneIndex = System.Array.IndexOf(_sceneNames, typed);
                EditorUtility.SetDirty(_mainSettings);
            }

            EditorGUILayout.Space(4);

            // 坐标显示（只读预览 + 直接编辑）
            _mainSettings.newGameSpawnPosition  = EditorGUILayout.Vector3Field("出生坐标", _mainSettings.newGameSpawnPosition);
            _mainSettings.newGameSpawnRotationY = EditorGUILayout.FloatField("初始朝向 Y（度）", _mainSettings.newGameSpawnRotationY);

            EditorGUILayout.Space(4);

            // 拾取按钮
            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = _picking ? new Color(0.4f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button(_picking ? "▶ 在 Scene View 中点击地面来设定出生点…（再次点击取消）"
                                          : "在地图中拾取出生坐标", GUILayout.Height(26)))
            {
                _picking = !_picking;
                if (_picking)
                {
                    // 切换到已打开的场景视图，让用户能看到场景
                    SceneView.lastActiveSceneView?.Focus();
                }
            }
            GUI.backgroundColor = prevColor;

            if (_picking)
                EditorGUILayout.HelpBox("切换到 Scene View，在地图上点击任意位置即可设定出生坐标。", MessageType.Info);
        }

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("保存", GUILayout.Height(32)))
            {
                EditorUtility.SetDirty(_mainSettings);
                AssetDatabase.SaveAssets();
            }
            if (GUILayout.Button("同步主菜单本地化 Key", GUILayout.Height(32)))
                SyncLocalizationKeys();
            if (GUILayout.Button("选中资产", GUILayout.Height(32)))
                Selection.activeObject = _mainSettings;
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("▶  打开主菜单 Scene", GUILayout.Height(28)))
        {
            string path = null;
            foreach (var s in EditorBuildSettings.scenes)
            {
                string n = System.IO.Path.GetFileNameWithoutExtension(s.path);
                if (n == "MainMenu") { path = s.path; break; }
            }
            if (path != null)
            {
                if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
            }
            else
                EditorUtility.DisplayDialog("找不到", "Build Settings 里没有名为 MainMenu 的场景。", "OK");
        }
        EditorGUILayout.Space(4);
    }

    // ── Tab 1：读条设置 ───────────────────────────────────────────────────

    private void DrawLoadingTab()
    {
        if (_loadingSettings == null) { LoadLoading(); return; }

        _loadingScroll = EditorGUILayout.BeginScrollView(_loadingScroll);
        EditorGUI.BeginChangeCheck();

        // ── 背景 / 模型 ──────────────────────────────────────────────────
        EditorGUILayout.LabelField("背景 / 模型", EditorStyles.boldLabel);
        _loadingSettings.backgroundTexture = (Texture2D)EditorGUILayout.ObjectField(
            "背景底图", _loadingSettings.backgroundTexture, typeof(Texture2D), false);
        _loadingSettings.cornerOverlayTexture = (Texture2D)EditorGUILayout.ObjectField(
            "角标图层", _loadingSettings.cornerOverlayTexture, typeof(Texture2D), false);
        EditorGUILayout.HelpBox(
            "直接拖入 PNG 即可，无需修改 Import 设置。角标图层保持宽高比填满屏幕，适应各种分辨率。",
            MessageType.None);
        _loadingSettings.modelPrefab = (GameObject)EditorGUILayout.ObjectField(
            "全息模型 Prefab", _loadingSettings.modelPrefab, typeof(GameObject), false);

        EditorGUILayout.Space(10);

        // ── Tips ─────────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Tips（随机播放）", EditorStyles.boldLabel);

        var tips = _loadingSettings.tips;
        for (int i = 0; i < tips.Count; i++)
        {
            var tip = tips[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                bool open = _tipFoldoutIndex == i;
                bool next = EditorGUILayout.Foldout(open, tip.tipName, true);
                if (next != open) _tipFoldoutIndex = next ? i : -1;

                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    tips.RemoveAt(i);
                    if (_tipFoldoutIndex >= tips.Count) _tipFoldoutIndex = -1;
                    break;
                }
            }

            if (_tipFoldoutIndex == i)
            {
                EditorGUI.indentLevel++;
                tip.tipName = EditorGUILayout.TextField("名称", tip.tipName);

                EditorGUILayout.LabelField("多语言内容", EditorStyles.miniBoldLabel);
                EnsureLangEntries(tip);

                foreach (var lt in tip.texts)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(lt.languageCode, GUILayout.Width(56));
                        // 单行预览
                        string preview = string.IsNullOrEmpty(lt.richText) ? "（空）" : lt.richText;
                        if (preview.Length > 48) preview = preview.Substring(0, 48) + "…";
                        EditorGUILayout.LabelField(preview, EditorStyles.miniLabel);

                        string capturedCode = lt.languageCode;
                        int capturedIdx = i;
                        if (GUILayout.Button("编辑", GUILayout.Width(44)))
                        {
                            SkyPrisonRichTextEditorWindow.Open(
                                $"Tip · {tip.tipName} [{capturedCode}]",
                                lt.richText,
                                result =>
                                {
                                    _loadingSettings.tips[capturedIdx]
                                        .texts.Find(x => x.languageCode == capturedCode)
                                        .richText = result;
                                    EditorUtility.SetDirty(_loadingSettings);
                                    AssetDatabase.SaveAssets();
                                });
                        }
                    }
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("＋ 添加 Tip", GUILayout.Width(120), GUILayout.Height(24)))
            {
                var newTip = new LoadingTip { tipName = $"Tip {tips.Count + 1}" };
                EnsureLangEntries(newTip);
                tips.Add(newTip);
                _tipFoldoutIndex = tips.Count - 1;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_loadingSettings);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("保存", GUILayout.Height(32)))
            {
                EditorUtility.SetDirty(_loadingSettings);
                AssetDatabase.SaveAssets();
            }
            if (GUILayout.Button("选中资产", GUILayout.Height(32)))
                Selection.activeObject = _loadingSettings;
        }
        EditorGUILayout.Space(4);
    }

    private void EnsureLangEntries(LoadingTip tip)
    {
        if (_langCodes == null) LoadLangCodes();
        foreach (var code in _langCodes)
        {
            if (tip.texts.Find(x => x.languageCode == code) == null)
                tip.texts.Add(new LocalizedTipText { languageCode = code, richText = "" });
        }
    }

    // ── 本地化同步（主界面）──────────────────────────────────────────────

    private void SyncLocalizationKeys()
    {
        var table = _mainSettings.localizationTable
                    ?? AssetDatabase.LoadAssetAtPath<UILocalizationTable>(LocTablePath);
        if (table == null)
        {
            EditorUtility.DisplayDialog("提示", "未找到 UILocalizationTable。", "OK");
            return;
        }

        var existingCodes = new List<string>();
        foreach (var e in table.entries)
            foreach (var t in e.texts)
                if (!existingCodes.Contains(t.languageCode))
                    existingCodes.Add(t.languageCode);
        if (existingCodes.Count == 0)
            existingCodes.AddRange(new[] { "zh-CN", "en", "ja" });

        var defaults = new Dictionary<string, (string zh, string en, string ja)>
        {
            ["ui_menu_continue"]    = ("继续游戏",     "Continue",         "続ける"),
            ["ui_menu_new_game"]    = ("新游戏",       "New Game",         "新しいゲーム"),
            ["ui_menu_settings"]    = ("游戏设置",     "Settings",         "設定"),
            ["ui_menu_steam_store"] = ("Steam 商店",   "Steam Store",      "Steam ストア"),
            ["ui_menu_quit"]        = ("退出游戏",     "Quit",             "終了"),
            ["ui_menu_press_any"]   = ("按任意键开始", "PRESS ANY BUTTON", "何かボタンを押してください"),
            ["ui_loading_continue"] = ("继续",         "Continue",         "続ける"),
        };

        foreach (var kv in defaults)
        {
            var entry = table.EnsureEntry(kv.Key, existingCodes);
            foreach (var t in entry.texts)
            {
                if (!string.IsNullOrEmpty(t.text)) continue;
                if (t.languageCode == "zh-CN" || t.languageCode == "zh")      t.text = kv.Value.zh;
                else if (t.languageCode.StartsWith("en"))                      t.text = kv.Value.en;
                else if (t.languageCode.StartsWith("ja"))                      t.text = kv.Value.ja;
            }
        }

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("完成", "主菜单 Key 已写入本地化表。", "OK");
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
