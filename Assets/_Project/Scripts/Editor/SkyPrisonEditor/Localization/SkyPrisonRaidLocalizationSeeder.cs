using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 选关界面（RAID）所有不来自地图编辑器的文字，补进多语言字典表（UILocalizationTable）：中 / 日 / 英。
/// 只补缺的 key、只填空着的语言，已经有内容的不会覆盖（用户在字典表里改过的保持原样）。
/// 编译后自动跑一次；也可以手动点菜单「天空囚笼/本地化/补全选关界面文字」。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonRaidLocalizationSeeder
{
    const string TablePath = "Assets/_Project/Data/Resources/UILocalizationTable.asset";
    static readonly string[] Codes = { "zh-CN", "ja", "en" };

    // key, 中, 日, 英
    static readonly string[][] Rows =
    {
        new[] { "raid_hint_switch_raid",    "切换 RAID",       "RAIDを切り替え",   "Switch RAID" },
        new[] { "raid_hint_switch_daynight","切换昼夜",         "昼夜を切り替え",   "Day / Night" },
        new[] { "raid_hint_pan",            "平移",             "移動",             "Pan" },
        new[] { "raid_hint_zoom",           "缩放",             "ズーム",           "Zoom" },
        new[] { "raid_hint_orbit",          "转动视角",         "視点を回転",       "Rotate View" },
        new[] { "raid_hint_confirm",        "选定 / 出发",      "決定 / 出発",      "Select / Deploy" },
        new[] { "raid_hint_drag",           "拖拽",             "ドラッグ",         "Drag" },
        new[] { "raid_hint_wheel",          "滚轮",             "ホイール",         "Wheel" },
        new[] { "raid_hint_rclick",         "右键",             "右クリック",       "Right Click" },
        new[] { "raid_image_pending",       "影像待补",         "画像準備中",       "Image Pending" },
        new[] { "raid_depart",              "出发",             "出発",             "Deploy" },
        new[] { "raid_depart_subway",       "乘地铁出发",       "地下鉄で出発",     "Take the Subway" },
        new[] { "raid_main_output",         "主要产出",         "主な回収品",         "Main Yield" },
        new[] { "raid_day",                 "昼",               "昼",               "Day" },
        new[] { "raid_night",               "夜",               "夜",               "Night" },
        new[] { "raid_map_title",           "RAID  ·  出发地图", "RAID  ·  出撃マップ", "RAID  ·  Deployment Map" },
        new[] { "raid_subway_to",           "地铁 · 前往 ",     "地下鉄 · 行き先 ", "Subway · To " },
        new[] { "raid_new",                 "NEW",              "NEW",              "NEW" },
        new[] { "raid_deploy_prompt",       "出发 · 选择 RAID", "出発 · RAID選択",  "Deploy · Select RAID" },
        new[] { "raid_deploy_start",        "START",            "START",            "START" },
        new[] { "raid_hold_hint",           "长按出发",         "長押しで出発",     "Hold to Deploy" },
        new[] { "ui_hint_close",            "关闭",             "閉じる",           "Close" },
    };

    static SkyPrisonRaidLocalizationSeeder()
    {
        EditorApplication.delayCall += () => Seed(false);
    }

    [MenuItem("天空囚笼/本地化/补全选关界面文字", false, 2100)]
    static void Menu() { Seed(true); }

    static void Seed(bool verbose)
    {
        var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TablePath);
        if (table == null) { if (verbose) Debug.LogWarning("[RaidLoc] 找不到 " + TablePath); return; }

        var codes = new List<string>(Codes);
        int changed = 0;
        foreach (var row in Rows)
        {
            var entry = table.EnsureEntry(row[0], codes);
            for (int i = 0; i < Codes.Length; i++)
            {
                foreach (var t in entry.texts)
                {
                    if (t.languageCode != Codes[i] || !string.IsNullOrEmpty(t.text)) continue;
                    t.text = row[i + 1]; changed++;
                }
            }
        }
        if (changed > 0)
        {
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log("[RaidLoc] 选关界面文字已补进字典表，新增 " + changed + " 条译文（中/日/英）。");
        }
        else if (verbose) Debug.Log("[RaidLoc] 字典表里选关界面文字已齐全。");
    }
}
