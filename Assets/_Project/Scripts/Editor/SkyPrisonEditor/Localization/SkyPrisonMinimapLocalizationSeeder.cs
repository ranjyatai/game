using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 战术地图窗口(SkyPrisonMinimapWindow)的所有文字，补进多语言字典表（UILocalizationTable）：中 / 日 / 英。
/// 只补缺的 key、只填空着的语言，已经有内容的不会覆盖（用户在字典表里改过的保持原样）。
/// 区域标注(SkyPrisonMapZone)的名字是地图里自己填的：填的如果是字典 key，窗口里就按语言显示译文，不是 key 就原样显示。
/// 编译后自动跑一次；也可以手动点菜单「天空囚笼/本地化/补全战术地图文字」。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonMinimapLocalizationSeeder
{
    const string TablePath = "Assets/_Project/Data/Resources/UILocalizationTable.asset";
    static readonly string[] Codes = { "zh-CN", "ja", "en" };

    // key, 中, 日, 英
    static readonly string[][] Rows =
    {
        new[] { "minimap_title",          "战术地图",                      "戦術マップ",                          "Tactical Map" },
        new[] { "minimap_markers",        "标记",                          "マーカー",                            "Markers" },
        new[] { "minimap_unplaced",       "未放置",                        "未設置",                              "Not Placed" },
        new[] { "minimap_zones",          "区域",                          "エリア",                              "Zones" },
        new[] { "minimap_legend_extract", "撤离点",                        "撤収地点",                            "Extraction Point" },
        new[] { "minimap_legend_afp",     "AFP强化",                       "AFP強化",                             "AFP Upgrade" },
        new[] { "minimap_legend_crate",   "箱子",                          "コンテナ",                            "Crate" },
        new[] { "minimap_legend_npc",     "NPC",                           "NPC",                                 "NPC" },
        new[] { "minimap_legend_quest",   "任务目标",                      "任務目標",                            "Objective" },
        new[] { "minimap_legend_you",    "玩家",                          "プレイヤー",                          "You" },
        new[] { "minimap_info",           "战术信息",                      "戦術情報",                            "Tactical Info" },
        new[] { "minimap_explored",       "探索度",                        "探索率",                              "Explored" },
        new[] { "minimap_position",       "当前位置",                      "現在位置",                            "Position" },
        new[] { "minimap_raid_time",      "入局时长",                      "滞在時間",                            "Time in Raid" },
        new[] { "minimap_extract",        "撤离",                          "撤収",                                "Extract" },
        new[] { "minimap_marker_n",       "标记 {0}",                      "マーカー {0}",                        "Marker {0}" },
        new[] { "minimap_hint_middle",    "中键",                          "中ボタン",                            "Middle Click" },
        new[] { "minimap_hint_place",     "放置 / 撤销标记",               "マーカーを設置 / 撤去",               "Place / Remove Marker" },
        new[] { "minimap_hint_place_pan", "放置 / 撤销标记 (拖动平移)",    "マーカーを設置 / 撤去 (ドラッグで移動)", "Place / Remove Marker (Drag to Pan)" },
        new[] { "minimap_hint_right",     "右键",                          "右クリック",                          "Right Click" },
        new[] { "minimap_hint_rotate",    "拖动旋转",                      "ドラッグで回転",                      "Drag to Rotate" },
        new[] { "minimap_hint_wheel",     "滚轮",                          "ホイール",                            "Wheel" },
        new[] { "minimap_hint_zoom",      "缩放",                          "ズーム",                              "Zoom" },
        new[] { "minimap_hint_view",      "切换 2D / 3D",                  "2D / 3D 切り替え",                    "Switch 2D / 3D" },
        new[] { "minimap_hint_ortho",     "正交俯视 / 透视",               "正投影の俯瞰 / 透視",                 "Top-down / Perspective" },
        new[] { "minimap_hint_cursor",    "移动准星 (推到边缘平移)",       "照準を移動 (端で押し続けるとスクロール)", "Move Crosshair (Push at Edge to Pan)" },
        new[] { "minimap_legend_enemy",   "敌人",                          "敵",                                  "Enemy" },
        new[] { "minimap_hint_color",     "标记换色",                      "マーカーの色を変更",                  "Marker Color" },
        new[] { "minimap_hint_slot",    "切换标记槽位",                  "マーカー枠を切り替え",                "Switch Marker Slot" },
        new[] { "ui_hint_close",         "关闭",                          "閉じる",                              "Close" },
    };

    static SkyPrisonMinimapLocalizationSeeder()
    {
        EditorApplication.delayCall += () => Seed(false);
    }

    [MenuItem("天空囚笼/本地化/补全战术地图文字", false, 2110)]
    static void Menu() { Seed(true); }

    static void Seed(bool verbose)
    {
        var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TablePath);
        if (table == null) { if (verbose) Debug.LogWarning("[MinimapLoc] 找不到 " + TablePath); return; }

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
            Debug.Log("[MinimapLoc] 战术地图文字已补进字典表，新增 " + changed + " 条译文（中/日/英）。");
        }
        else if (verbose) Debug.Log("[MinimapLoc] 字典表里战术地图文字已齐全。");
    }
}
