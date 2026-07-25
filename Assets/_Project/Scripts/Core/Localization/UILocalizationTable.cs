using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class UILocalizationEntry
{
    public string key = "";
    public List<LocalizedTextEntry> texts = new List<LocalizedTextEntry>();
}

[CreateAssetMenu(menuName = "Sky Prison/Localization/UI Localization Table", fileName = "UILocalizationTable")]
public class UILocalizationTable : ScriptableObject
{
    public List<UILocalizationEntry> entries = new List<UILocalizationEntry>();

    // 运行时查询 — 通过 LocalizationRuntime 当前语言
    public string Get(string key, string fallback = "")
    {
        var entry = Find(key);
        if (entry == null) return fallback;

        if (LocalizationRuntime.Instance != null)
            return LocalizationRuntime.Instance.GetText(entry.texts, fallback);

        return fallback;
    }

    // 编辑器/静态查询 — 指定语言 code
    public string GetForCode(string key, string languageCode, string fallback = "")
    {
        var entry = Find(key);
        if (entry == null) return fallback;
        return LocalizationRuntime.Resolve(entry.texts, languageCode, fallback);
    }

    // 确保 key 存在，并同步语言列表（编辑器调用）
    public UILocalizationEntry EnsureEntry(string key, List<string> languageCodes)
    {
        var entry = Find(key);
        if (entry == null)
        {
            entry = new UILocalizationEntry { key = key };
            entries.Add(entry);
        }

        foreach (string code in languageCodes)
        {
            bool found = false;
            foreach (var t in entry.texts)
                if (t.languageCode == code) { found = true; break; }
            if (!found)
                entry.texts.Add(new LocalizedTextEntry { languageCode = code, text = "" });
        }

        return entry;
    }

    private UILocalizationEntry Find(string key)
    {
        foreach (var e in entries)
            if (e != null && e.key == key) return e;
        return null;
    }

#if UNITY_EDITOR
    // 预置 UI 文字 key 列表，编辑器可一键同步
    public static readonly string[] StandardUIKeys =
    {
        "ui_inventory_title",
        "ui_filter_all",
        "ui_filter_consumable",
        "ui_filter_material",
        "ui_filter_equipment",
        "ui_filter_quest",
        "ui_filter_keyitem",
        "ui_sort_label",
        "ui_sort_time",
        "ui_sort_level",
        "ui_sort_value",
        "ui_sort_weight",
        "ui_sort_category",
        "ui_sort_name",
        "ui_weight_label",
        "ui_tidy_button",
        "ui_use",
        // 背包窗口底部按键提示条——之前是 static readonly 数组硬编码中文，
        // static 初始化时没有实例上下文能查表，改成运行时按当前语言现查。
        "ui_hint_move_merge",
        "ui_hint_split",
        "ui_hint_discard",
        "ui_hint_move",
        "ui_hint_grab_drop",
        "ui_hint_menu",
        "ui_hint_detail",
        "ui_hint_prev_category",
        "ui_hint_next_category",
        "ui_hint_sort_field",
        "ui_hint_sort_order",
        "ui_hint_close",
        "ui_discard_title",
        "ui_discard_confirm",
        "ui_discard_cancel",
        "ui_split_title",
        "ui_split_confirm",
        // 货币
        "currency_token_name",
        "currency_token_desc",
        "currency_hero_cert_name",
        "currency_hero_cert_desc",
        // 单位
        "unit_weight",          // 石英
        "unit_hp",              // HP 单位（如"点"）
        "unit_lp",              // LP 单位
        // 角色属性名
        "stat_hp_name",         // 生命值
        "stat_lp_name",         // 精力值 / LP
        "stat_weight_name",     // 负重
        "stat_attack_name",     // 攻击
        "stat_defense_name",    // 防御
        "stat_speed_name",      // 速度
        // 物品分类名（用于筛选栏、物品详情等）
        "item_cat_consumable",  // 消耗品
        "item_cat_material",    // 材料
        "item_cat_equipment",   // 装备
        "item_cat_quest",       // 任务物品
        "item_cat_keyitem",     // 重要物品
        "item_cat_currency",    // 凭证
        "item_cat_special",     // 特殊
        "item_cat_general",     // 道具（兜底分类）
        "ui_item_usable",       // 可使用（物品详情标签行）
        // 战斗相关术语
        "term_light_attack",    // 轻攻击
        "term_heavy_attack",    // 重攻击
        "term_dodge",           // 闪避
        "term_tech_tree",       // 科技树
        // 死亡/复活弹窗
        "ui_death_title",       // 生命维持警告
        "ui_death_warning",     // 死亡将会造成背包所持物品的损失
        "ui_death_use_item",    // 使用该道具
        "ui_death_on_cooldown", // （冷却中）
        "ui_death_return_base", // 返回据点
        // 世界掉落物拾取提示
        "ui_pickup_action",     // 拾取
        "ui_pickup_cycle",      // 切换目标
        "ui_pickup_full",       // 背包已满
        // 物品详情面板
        "ui_item_stack_limit",  // 组上限
        // 主界面
        "ui_menu_continue",     // 继续游戏
        "ui_menu_new_game",     // 新游戏
        "ui_menu_settings",     // 游戏设置
        "ui_menu_steam_store",  // Steam 商店
        "ui_menu_quit",         // 退出游戏
        "ui_menu_press_any",    // 按任意键开始
        // 存档选择窗口
        "ui_saveslot_title_continue",           // 选择存档
        "ui_saveslot_title_new",                // 选择存档位
        "ui_saveslot_auto",                     // 自动存档
        "ui_saveslot_slot_format",               // 存档 {0}
        "ui_saveslot_no_data",                   // NO DATA
        "ui_saveslot_return",                    // 返回
        "ui_saveslot_click_token",                // 点击
        "ui_saveslot_select_hint",                // 选择存档（底部提示条）
        "ui_saveslot_move_hint",                   // 移动（底部提示条）
        "ui_saveslot_overwrite_title_format",      // 覆盖 {0}？
        "ui_saveslot_overwrite_confirm",           // 覆盖并开始
        "ui_saveslot_delete",                      // 删除存档（行内小按钮文字）
        "ui_saveslot_delete_hint",                  // 删除存档（底部提示条）
        "ui_saveslot_delete_title_format",          // 删除 {0}？
        "ui_saveslot_delete_body",                  // 删除后无法恢复，确定要删除这份存档吗？
        "ui_saveslot_delete_confirm",                // 确认删除
        // 自动存档提示（右上角转圈+文字）
        "ui_autosave_saving",                      // 自动保存中，请勿退出游戏...
        "ui_autosave_done",                        // 自动保存完毕
        "ui_autosave_failed",                      // 自动保存失败
        // 关卡内暂停菜单
        "ui_pause_title",           // 暂停
        "ui_pause_resume",          // 继续游戏
        "ui_pause_return_menu",     // 返回主菜单
        "ui_pause_settings_wip",    // 设置界面施工中…
        "ui_pause_return_confirm_title",   // 返回主菜单？
        "ui_pause_return_confirm_body",    // 本次战斗进度将会丢失，确定要返回吗？
        "ui_pause_return_confirm_yes",     // 确认返回
        // 设置窗口
        "ui_settings_tab_display",   // 显示
        "ui_settings_tab_graphics",  // 画面
        "ui_settings_tab_audio",     // 音频
        "ui_settings_tab_language",  // 语言
        "ui_settings_tab_keymouse",  // 键鼠
        "ui_settings_tab_gamepad",   // 手柄
        "ui_settings_tab_controls",  // 操作（旧字段，保留避免历史数据丢失，已不在新框架使用）
        "ui_settings_tab_gameplay",  // 玩法
        "ui_settings_content_wip",   // 内容开发中…
        "ui_settings_title",         // 设置/設定/Option
        "ui_settings_tab_hint",      // 切换分类（底部提示条）
        "ui_settings_brightness",    // 亮度
        "ui_settings_brightness_enter",   // 亮度设置（进入弹窗按钮）
        "ui_settings_brightness_confirm", // 确认返回（弹窗内按钮）
        "ui_settings_brightness_cancel",  // 取消（弹窗内按钮）
        "ui_settings_brightness_hint",    // 调整亮度，直到右侧标志隐约可见。
        // 显示 Tab
        "ui_settings_fullscreen",    // 全屏（旧字段，兼容用）
        "ui_settings_windowmode",           // 窗口模式
        "ui_settings_windowmode_windowed",   // 窗口
        "ui_settings_windowmode_fullscreen", // 全屏
        "ui_settings_windowmode_borderless", // 无边框全屏
        "ui_settings_vsync",         // 垂直同步
        "ui_settings_resolution",    // 分辨率
        "ui_settings_framerate",     // 帧率上限
        "ui_settings_framerate_unlimited", // 无限制
        "ui_settings_show_fps",      // 显示FPS
        // 画面 Tab
        "ui_settings_quality",       // 画质预设
        "ui_quality_verylow",        // 极低
        "ui_quality_low",            // 低
        "ui_quality_mid",            // 中
        "ui_quality_high",           // 高
        "ui_quality_veryhigh",       // 极高
        "ui_quality_ultra",          // 极致
        "ui_settings_antialiasing",  // 抗锯齿
        "ui_settings_aa_off",        // 关闭（抗锯齿选项之一）
        "ui_settings_motion_blur",   // 动态模糊
        "ui_settings_chromatic",     // 色差
        "ui_settings_screenshake",   // 屏幕震动
        // 音频 Tab
        "ui_settings_volume_master", // 主音量
        "ui_settings_volume_music",  // 音乐
        "ui_settings_volume_sfx",    // 音效
        "ui_settings_volume_voice",  // 语音
        // 语言 Tab
        "ui_settings_language",      // 语言
        // 键鼠 Tab
        "ui_settings_mouse_sensitivity", // 鼠标灵敏度
        "ui_settings_invert_y",      // Y轴反转
        "ui_settings_keybinds",      // 按键绑定
        "ui_settings_keybinds_enter", // 查看/修改
        // 手柄 Tab
        "ui_settings_gamepad_deadzone",  // 摇杆死区
        "ui_settings_gamepad_vibration", // 震动强度
        "ui_settings_gamepad_keybinds",  // 手柄按键绑定
        // 玩法 Tab
        "ui_settings_damage_numbers",   // 伤害数字显示
        "ui_settings_autosave",          // 自动保存
        "ui_settings_gore_effects",     // 流血效果（原"和谐模式"改名，语义反了——ON=显示流血）
        "ui_settings_clear_cache",      // 清除缓存
        "ui_settings_clear_cache_action", // 清除
        "ui_settings_clear_cache_title",       // 清除缓存？
        "ui_settings_clear_cache_body_format", // 当前缓存占用 {0}，清除后素材需要重新生成，确定要清除吗？
        // 手柄 Tab 图例——按 action 显示的功能名，之前直接拿 binding.displayName
        // 原始中文当 L() 的 key 用，永远查不到表、只会原样显示中文，跟这里没关系
        // 但功能上是同一批文字，一起列进来方便以后用编辑器同步工具核对。
        "ui_settings_keybind_gamepad_key",       // 手柄键（手柄按键绑定弹窗表头）
        "ui_settings_keybind_press_any_gamepad", // 请按手柄任意键…
        "ui_settings_keybind_hint_move",    // 移动光标（弹窗提示条）
        "ui_settings_keybind_hint_select",  // 选择键位重新绑定（弹窗提示条）
        "ui_settings_keybind_hint_cancel",  // 取消捕获 / 返回（弹窗提示条）
        "ui_fatal_error_title_format",     // 发生错误（{0}）——统一致命报错弹窗标题，{0}=错误编号
        "ui_fatal_error_log_hint",         // 详细记录已写入 logs/error_log.txt……
        "ui_fatal_error_confirm",          // 返回主菜单（致命报错弹窗按钮）
        "ui_settings_keybind_reset",       // 恢复默认按键
        "ui_settings_keybind_cancel",      // 返回（不保存）
        "ui_settings_keybind_save",        // 保存并返回
        "ui_settings_keybind_primary",     // 主键
        "ui_settings_keybind_secondary",   // 副键
        "ui_settings_keybind_press_any",   // 请按任意键…
        "ui_settings_action_moveup",
        "ui_settings_action_movedown",
        "ui_settings_action_moveleft",
        "ui_settings_action_moveright",
        "ui_settings_action_interact",
        "ui_settings_action_cyclepickup",
        "ui_settings_action_sprint",
        "ui_settings_action_sneak",
        "ui_settings_action_dodge",
        "ui_settings_action_jump",
        "ui_settings_action_light_attack",
        "ui_settings_action_heavy_attack",
        "ui_settings_action_skill1",
        "ui_settings_action_skill2",
        "ui_settings_action_skill3",
        "ui_settings_action_inventory",
        "ui_settings_action_map",
        "ui_settings_action_menu",
        "ui_settings_action_quickitem1",
        "ui_settings_action_quickitem2",
        "ui_settings_action_quickitem3",
        "ui_settings_action_quickitem4",
        // 角色信息面板——注意这几个 key 专属这个面板，别跟别处的 stat_xxx_name 混用，
        // 那些是"生命值"这种全称，这里要的是"HP"这种终端读数缩写。
        "charpanel_title",
        "charpanel_stat_hp",
        "charpanel_stat_lp",
        "charpanel_stat_atk",
        "charpanel_stat_def",
        "charpanel_stat_atkspeed",
        "charpanel_stat_critrate",
        "charpanel_stat_critmult",
        "charpanel_stat_negcritrate",
        "charpanel_stat_negcritmult",
        "charpanel_stat_slashresist",
        "charpanel_stat_strikeresist",
        "charpanel_stat_impactresist",
        "charpanel_stat_heatdamage",
        "charpanel_stat_shockdamage",
        "charpanel_stat_corrosiondamage",
        "charpanel_stat_freezedamage",
        "charpanel_stat_heatresist",
        "charpanel_stat_shockresist",
        "charpanel_stat_corrosionresist",
        "charpanel_stat_freezeresist",
        // 装备区分组标题 + 槽位名 + 底部提示条——之前这些是硬编码中文，L()调了但表里
        // 没有这些key的翻译条目，非中文语言下会读到空/兜底文字，看着像"没接入字典表"。
        "charpanel_group_weapon",
        "charpanel_group_equipment",
        "charpanel_group_quickitem",
        "charpanel_slot_weapon1",
        "charpanel_slot_weapon2",
        "charpanel_slot_head",
        "charpanel_slot_upperbody",
        "charpanel_slot_lowerbody",
        "charpanel_slot_hands",
        "charpanel_slot_shoes",
        "ui_hint_open_equip",
        // 商店窗口——用户明确要求商店所有文字都接入字典表支持语言切换。
        "ui_hint_select_goods",
        "ui_hint_add_cart_checkout",
        "shop_checkout_button",
        "shop_back_to_shop",
        "shop_cost_label",
        "shop_wallet_label",
        "shop_after_label",
        "shop_cart_empty",
        "shop_insufficient_funds",
        "shop_free",
        "shop_sold_out",
        // 物品分类名——跟背包详情面板(InventoryItemDetailPanel)共用同一批key，
        // item_cat_consumable等几个上面已经有了，这里补齐武器/防具两个新用到的。
        "item_cat_weapon",
        "item_cat_armor",
    };
#endif
}
