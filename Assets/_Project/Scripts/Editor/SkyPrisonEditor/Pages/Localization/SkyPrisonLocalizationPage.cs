#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 天空囚笼编辑器 — 字典表页面。
/// 左栏：语言管理（增删启用、字体设置）。
/// 右栏：UI 文字条目编辑（每个 Key 在各语言下的译文）。
/// </summary>
public class SkyPrisonLocalizationPage : SkyPrisonEditorPageBase
{
    public override string TabName => "字典表";

    // ── 资产引用 ────────────────────────────────────────────────────────
    private LocalizationProjectSettings _settings;
    private UILocalizationTable         _table;

    // ── 左栏状态 ────────────────────────────────────────────────────────
    private Vector2 _leftScroll;
    private bool    _showAddLanguage;
    private string  _newLangCode    = "";
    private string  _newLangDisplay = "";

    // ── 右栏状态 ────────────────────────────────────────────────────────
    private Vector2 _rightScroll;
    private string  _searchKey = "";
    private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

    // ── 样式 ────────────────────────────────────────────────────────────
    private static readonly Color HeaderBg    = new Color(0.16f, 0.16f, 0.18f, 1f);
    private static readonly Color RowEvenBg   = new Color(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Color RowOddBg    = new Color(0.15f, 0.15f, 0.17f, 1f);
    private static readonly Color AccentColor = new Color(0.35f, 0.75f, 1.00f, 1f);

    private const string TableDefaultPath   = "Assets/_Project/Data/Resources/UILocalizationTable.asset";
    private const string TableDefaultFolder = "Assets/_Project/Data/Resources";

    public SkyPrisonLocalizationPage(SkyPrisonEditorContext context) : base(context) { }

    public override void OnEnable() => ReloadAssets();

    public override void Refresh() => ReloadAssets();

    // ════════════════════════════════════════════════════════════════════
    // 左栏 — 语言管理
    // ════════════════════════════════════════════════════════════════════
    public override void OnGUILeft()
    {
        if (_settings == null) ReloadAssets();

        _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

        // 标题
        DrawSectionHeader("语言管理");

        if (_settings == null)
        {
            EditorGUILayout.HelpBox("未找到 LocalizationProjectSettings。", MessageType.Warning);
            if (GUILayout.Button("创建默认配置"))
            {
                _settings = LocalizationSettingsUtility.GetOrCreateSettings();
            }
            EditorGUILayout.EndScrollView();
            return;
        }

        // ── 语言列表 ──────────────────────────────────────────────────
        List<LocalizationProjectSettings.LanguageEntry> langs = _settings.languages;
        bool dirty = false;

        for (int i = 0; i < langs.Count; i++)
        {
            var lang = langs[i];
            if (lang == null) continue;

            Rect rowRect = EditorGUILayout.BeginVertical("box");
            GUILayout.Space(2f);

            // 第一行：启用复选框 + 显示名称 + 默认标签
            EditorGUILayout.BeginHorizontal();

            bool wasEnabled = lang.enabled;
            lang.enabled = EditorGUILayout.Toggle(lang.enabled, GUILayout.Width(18f));
            if (lang.enabled != wasEnabled) dirty = true;

            GUIStyle nameStyle = lang.isDefault
                ? new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = AccentColor } }
                : EditorStyles.boldLabel;
            EditorGUILayout.LabelField(lang.displayName, nameStyle);

            if (lang.isDefault)
            {
                GUILayout.Label("默认", EditorStyles.miniLabel, GUILayout.Width(30f));
            }
            else if (GUILayout.Button("设为默认", EditorStyles.miniButton, GUILayout.Width(58f)))
            {
                foreach (var l in langs) if (l != null) l.isDefault = false;
                lang.isDefault = true;
                dirty = true;
            }

            GUI.enabled = !lang.isDefault; // 默认语言不允许删除
            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f)))
            {
                langs.RemoveAt(i);
                dirty = true;
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // 第二行：语言代码（只读显示）
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("语言代码", EditorStyles.miniLabel, GUILayout.Width(56f));
            EditorGUILayout.LabelField(lang.languageCode, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // 第三行：主字体
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("主字体", EditorStyles.miniLabel, GUILayout.Width(56f));
            Font newFont = (Font)EditorGUILayout.ObjectField(
                lang.primaryFont, typeof(Font), false);
            if (newFont != lang.primaryFont)
            {
                lang.primaryFont = newFont;
                dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            // 第四行：备用字体列表（折叠）
            if (lang.fallbackFonts != null && lang.fallbackFonts.Count > 0)
            {
                EditorGUI.indentLevel++;
                for (int fi = 0; fi < lang.fallbackFonts.Count; fi++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"备用 {fi + 1}", EditorStyles.miniLabel, GUILayout.Width(56f));
                    Font fb = (Font)EditorGUILayout.ObjectField(lang.fallbackFonts[fi], typeof(Font), false);
                    if (fb != lang.fallbackFonts[fi]) { lang.fallbackFonts[fi] = fb; dirty = true; }
                    if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(20f)))
                    {
                        lang.fallbackFonts.RemoveAt(fi);
                        dirty = true;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            // 添加备用字体按钮
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(60f);
            if (GUILayout.Button("＋ 添加备用字体", EditorStyles.miniButton))
            {
                lang.fallbackFonts.Add(null);
                dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2f);
            EditorGUILayout.EndVertical();
            GUILayout.Space(2f);
        }

        // ── 添加语言 ──────────────────────────────────────────────────
        GUILayout.Space(4f);
        _showAddLanguage = EditorGUILayout.Foldout(_showAddLanguage, "＋ 添加语言", true);
        if (_showAddLanguage)
        {
            EditorGUI.indentLevel++;
            _newLangCode    = EditorGUILayout.TextField("语言代码", _newLangCode);
            _newLangDisplay = EditorGUILayout.TextField("显示名称", _newLangDisplay);
            EditorGUILayout.HelpBox("代码示例：zh-CN / en-US / ja-JP", MessageType.None);

            GUI.enabled = !string.IsNullOrWhiteSpace(_newLangCode) &&
                          !string.IsNullOrWhiteSpace(_newLangDisplay);
            if (GUILayout.Button("添加"))
            {
                string code = _newLangCode.Trim();
                if (!_settings.HasLanguageCode(code))
                {
                    langs.Add(new LocalizationProjectSettings.LanguageEntry
                    {
                        enabled     = true,
                        isDefault   = false,
                        languageCode = code,
                        displayName  = _newLangDisplay.Trim()
                    });
                    _newLangCode    = "";
                    _newLangDisplay = "";
                    _showAddLanguage = false;
                    dirty = true;
                }
                else
                {
                    EditorUtility.DisplayDialog("重复", $"语言代码 {code} 已存在。", "确定");
                }
            }
            GUI.enabled = true;
            EditorGUI.indentLevel--;
        }

        if (dirty)
        {
            _settings.EnsureSingleDefault();
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndScrollView();
    }

    // ════════════════════════════════════════════════════════════════════
    // 右栏 — 文字条目编辑
    // ════════════════════════════════════════════════════════════════════
    public override void OnGUIRight()
    {
        // ── 顶部工具栏 ────────────────────────────────────────────────
        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("UI 文字字典");

        EditorGUILayout.BeginHorizontal();
        _searchKey = EditorGUILayout.TextField("搜索", _searchKey, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("×", GUILayout.Width(22f))) _searchKey = "";
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        if (_table == null)
        {
            if (GUILayout.Button("创建字典表 Asset"))
                CreateTableAsset();
        }
        else
        {
            if (GUILayout.Button("同步标准 Key"))
                SyncStandardKeys();
            if (GUILayout.Button("同步语言列表"))
                SyncLanguagesToEntries();
            if (GUILayout.Button("填入默认译文"))
                FillDefaultTexts();
            if (GUILayout.Button("在 Inspector 中打开"))
                Selection.activeObject = _table;
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        if (_table == null)
        {
            EditorGUILayout.HelpBox("未找到 UILocalizationTable。请点击[创建字典表 Asset]。", MessageType.Info);
            return;
        }

        if (_settings == null)
        {
            EditorGUILayout.HelpBox("请先在左侧配置语言。", MessageType.Warning);
            return;
        }

        List<LocalizationProjectSettings.LanguageEntry> langs = GetEnabledLanguages();
        if (langs.Count == 0)
        {
            EditorGUILayout.HelpBox("没有启用的语言，请在左侧添加至少一种语言。", MessageType.Warning);
            return;
        }

        // ── 条目列表 ──────────────────────────────────────────────────
        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        bool tableDirty = false;
        int rowIndex = 0;

        foreach (var entry in _table.entries)
        {
            if (entry == null) continue;
            if (!string.IsNullOrEmpty(_searchKey) &&
                !entry.key.Contains(_searchKey, System.StringComparison.OrdinalIgnoreCase))
                continue;

            // 交替行底色
            Rect rowRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rowRect, rowIndex % 2 == 0 ? RowEvenBg : RowOddBg);
            rowIndex++;

            // Key 折叠头
            if (!_foldouts.ContainsKey(entry.key))
                _foldouts[entry.key] = false;

            EditorGUILayout.BeginHorizontal();
            _foldouts[entry.key] = EditorGUILayout.Foldout(
                _foldouts[entry.key], entry.key, true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (_foldouts[entry.key])
            {
                // 确保条目包含所有启用语言
                EnsureEntryHasAllLanguages(entry, langs);

                EditorGUI.indentLevel++;
                foreach (var lang in langs)
                {
                    string label = lang.isDefault
                        ? $"{lang.displayName} ★"
                        : lang.displayName;

                    foreach (var t in entry.texts)
                    {
                        if (t.languageCode != lang.languageCode) continue;

                        EditorGUILayout.BeginHorizontal();

                        // 语言标签（带字体预览色）
                        GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
                        if (lang.isDefault) labelStyle.normal.textColor = AccentColor;
                        GUILayout.Label(label, labelStyle, GUILayout.Width(120f));

                        // 文字输入框
                        string newText = EditorGUILayout.TextField(t.text);
                        if (newText != t.text)
                        {
                            t.text   = newText;
                            tableDirty = true;
                        }

                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                }
                EditorGUI.indentLevel--;
                GUILayout.Space(2f);
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        if (tableDirty)
        {
            EditorUtility.SetDirty(_table);
            AssetDatabase.SaveAssets();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // 工具方法
    // ════════════════════════════════════════════════════════════════════

    // ────────────────────────────────────────────────────────────────────
    // 默认译文表  key → (zh-CN, ja-JP, en-US)
    // ────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, (string zh, string ja, string en)> DefaultTexts
        = new Dictionary<string, (string, string, string)>
    {
        // 背包界面
        { "ui_inventory_title",   ("背包",       "アイテムパック", "Inventory")         },
        { "ui_filter_all",        ("全部",       "すべて",         "All")               },
        { "ui_filter_consumable", ("消耗品",     "消耗品",         "Consumables")        },
        { "ui_filter_material",   ("材料",       "素材",           "Materials")          },
        { "ui_filter_equipment",  ("装备",       "装備",           "Equipment")          },
        { "ui_filter_quest",      ("任务",       "クエスト",       "Quest")              },
        { "ui_filter_keyitem",    ("重要物品",   "貴重品",         "Key Items")          },
        { "ui_sort_label",        ("排序",       "並び替え",       "Sort")              },
        { "ui_sort_time",         ("获得时间",   "入手時間",       "Acquired Time")      },
        { "ui_sort_weight",       ("重量",       "重量",           "Weight")            },
        { "ui_sort_category",     ("类别",       "種別",           "Category")          },
        { "ui_sort_name",         ("名称",       "名前",           "Name")              },
        { "ui_weight_label",      ("负重",       "負荷",           "Weight")            },
        { "ui_tidy_button",       ("整理",       "整理",           "Tidy")              },
        { "ui_discard_title",     ("丢弃物品",   "アイテムを捨てる", "Discard Item")    },
        { "ui_discard_confirm",   ("确认丢弃",   "捨てる",         "Discard")           },
        { "ui_discard_cancel",    ("取消",       "キャンセル",     "Cancel")            },
        { "ui_split_title",       ("分割物品",   "アイテムを分割", "Split Item")        },
        { "ui_split_confirm",     ("确认",       "確認",           "Confirm")           },
        // 货币
        { "currency_token_name",      ("代币",       "コイン",         "Token")             },
        { "currency_token_desc",      ("流通于天空囚笼的通用货币。", "天空囚籠で流通する通貨。", "Common currency of Sky Prison.") },
        { "currency_hero_cert_name",  ("英雄证书",   "英雄証明書",     "Hero Certificate")  },
        { "currency_hero_cert_desc",  ("证明英雄资格的稀有凭证。",  "英雄の資格を証明する証書。", "Rare proof of hero status.") },
        // 单位
        { "unit_weight", ("石英", "石英", "Quartz") },
        { "unit_hp",     ("点",   "ポイント", "pt")   },
        { "unit_lp",     ("点",   "ポイント", "pt")   },
        // 角色属性名
        { "stat_hp_name",      ("生命值", "HP",   "HP")       },
        { "stat_lp_name",      ("精力值", "LP",   "LP")       },
        { "stat_weight_name",  ("负重",   "負荷", "Weight")   },
        { "stat_attack_name",  ("攻击",   "攻撃", "ATK")      },
        { "stat_defense_name", ("防御",   "防御", "DEF")      },
        { "stat_speed_name",   ("速度",   "速度", "SPD")      },
        // 物品分类名
        { "item_cat_consumable", ("消耗品", "消耗品",       "Consumable") },
        { "item_cat_material",   ("材料",   "素材",         "Material")   },
        { "item_cat_equipment",  ("装备",   "装備",         "Equipment")  },
        { "item_cat_quest",      ("任务",   "クエスト",     "Quest")      },
        { "item_cat_keyitem",    ("重要物品", "貴重品",      "Key Item")   },
        // 战斗术语
        { "term_light_attack", ("轻攻击", "軽攻撃", "Light Attack") },
        { "term_heavy_attack", ("重攻击", "重攻撃", "Heavy Attack") },
        { "term_dodge",        ("闪避",   "回避",   "Dodge")        },
        { "term_tech_tree",    ("科技树", "スキルツリー", "Tech Tree") },
        // 世界掉落物拾取提示
        { "ui_pickup_action", ("拾取",     "拾う",       "Pick Up")      },
        { "ui_pickup_cycle",  ("切换目标", "対象切り替え", "Switch Target") },
        { "ui_pickup_full",   ("背包已满", "所持アイテムがいっぱい", "Bag Full") },
        // 物品详情面板
        { "ui_item_stack_limit", ("组上限", "スタック上限", "Stack Limit") },
        // 死亡弹窗
        { "ui_death_title",       ("生命维持警告",             "生命維持警告",               "Life Support Warning")      },
        { "ui_death_warning",     ("死亡将会造成背包物品损失", "死亡するとアイテムを失います", "Death will cost your items") },
        { "ui_death_use_item",    ("使用该道具",               "このアイテムを使用",          "Use Item")                  },
        { "ui_death_on_cooldown", ("（冷却中）",               "（クールダウン中）",           "(Cooldown)")                },
        { "ui_death_return_base", ("返回据点",                 "拠点に戻る",                  "Return to Base")            },
        // 主界面
        { "ui_menu_continue",    ("继续游戏",     "続ける",                       "Continue")          },
        { "ui_menu_new_game",    ("新游戏",       "新しいゲーム",                 "New Game")          },
        { "ui_menu_settings",    ("游戏设置",     "設定",                         "Settings")          },
        { "ui_menu_steam_store", ("Steam 商店",   "Steam ストア",                 "Steam Store")       },
        { "ui_menu_quit",        ("退出游戏",     "終了",                         "Quit")              },
        { "ui_menu_press_any",   ("按任意键开始", "何かボタンを押してください",   "PRESS ANY BUTTON")  },
    };

    private void FillDefaultTexts()
    {
        if (_table == null || _settings == null) return;

        bool dirty = false;
        foreach (var kv in DefaultTexts)
        {
            var entry = _table.entries.Find(e => e != null && e.key == kv.Key);
            if (entry == null) continue;

            foreach (var t in entry.texts)
            {
                if (!string.IsNullOrEmpty(t.text)) continue; // 已有内容不覆盖

                string fill = "";
                if (t.languageCode.StartsWith("zh")) fill = kv.Value.zh;
                else if (t.languageCode.StartsWith("ja")) fill = kv.Value.ja;
                else if (t.languageCode.StartsWith("en")) fill = kv.Value.en;

                if (!string.IsNullOrEmpty(fill))
                {
                    t.text = fill;
                    dirty = true;
                }
            }
        }

        if (dirty)
        {
            EditorUtility.SetDirty(_table);
            AssetDatabase.SaveAssets();
        }
    }

    private void ReloadAssets()
    {
        _settings = LocalizationSettingsUtility.GetOrCreateSettings();
        _table    = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TableDefaultPath);
    }

    private void CreateTableAsset()
    {
        LocalizationSettingsUtility.GetOrCreateSettings(); // 确保文件夹存在

        if (!AssetDatabase.IsValidFolder(TableDefaultFolder))
        {
            string[] parts = TableDefaultFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        _table = ScriptableObject.CreateInstance<UILocalizationTable>();
        AssetDatabase.CreateAsset(_table, TableDefaultPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        SyncStandardKeys();
    }

    private void SyncStandardKeys()
    {
        if (_table == null || _settings == null) return;

        List<string> codes = new List<string>();
        foreach (var l in _settings.languages)
            if (l != null && l.enabled) codes.Add(l.languageCode);

        foreach (string key in UILocalizationTable.StandardUIKeys)
            _table.EnsureEntry(key, codes);

        EditorUtility.SetDirty(_table);
        AssetDatabase.SaveAssets();
    }

    private void SyncLanguagesToEntries()
    {
        if (_table == null || _settings == null) return;

        List<string> codes = new List<string>();
        foreach (var l in _settings.languages)
            if (l != null && l.enabled) codes.Add(l.languageCode);

        foreach (var entry in _table.entries)
            if (entry != null) _table.EnsureEntry(entry.key, codes);

        EditorUtility.SetDirty(_table);
        AssetDatabase.SaveAssets();
    }

    private void EnsureEntryHasAllLanguages(UILocalizationEntry entry,
        List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        foreach (var lang in langs)
        {
            bool found = false;
            foreach (var t in entry.texts)
                if (t.languageCode == lang.languageCode) { found = true; break; }
            if (!found)
                entry.texts.Add(new LocalizedTextEntry { languageCode = lang.languageCode, text = "" });
        }
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetEnabledLanguages()
    {
        var result = new List<LocalizationProjectSettings.LanguageEntry>();
        if (_settings == null) return result;

        // 默认语言排首位
        foreach (var l in _settings.languages)
            if (l != null && l.enabled && l.isDefault)  result.Add(l);
        foreach (var l in _settings.languages)
            if (l != null && l.enabled && !l.isDefault) result.Add(l);

        return result;
    }

    private static void DrawSectionHeader(string title)
    {
        Rect r = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, HeaderBg);
        GUI.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 8f, r.height),
                  title, EditorStyles.boldLabel);
        GUILayout.Space(4f);
    }
}
#endif
