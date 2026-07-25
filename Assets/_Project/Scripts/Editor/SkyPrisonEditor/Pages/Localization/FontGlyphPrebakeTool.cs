using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using TMPro;

namespace SkyPrison.Editor.Localization
{
    /// <summary>
    /// Build 里出现部分中文/日文字变方块（不是完全没字体，是缺字）——
    /// ZhouFangRiMingTi-2 SDF 这份字体资产是 Dynamic 图集模式，运行时按需现造字形；
    /// Editor 里能正常按需生成，但打包后动态生字有时候会失败（IL2CPP/Job System 相关
    /// 的已知坑），已经生成过的字不受影响，缺的字就变方块。
    ///
    /// 解法：不依赖运行时动态生字，把 UILocalizationTable 里用到的所有字符提前在
    /// 编辑器里一次性烤进图集（静态预生成），这些字在 Build 里就不需要再走那条
    /// 会失败的动态路径了。
    /// </summary>
    public static class FontGlyphPrebakeTool
    {
        private const string TablePath = "Assets/_Project/Data/Resources/UILocalizationTable.asset";
        private static readonly string[] FontPaths =
        {
            "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset",
            "Assets/_Project/Resources/Fonts & Materials/ZhouFangRiMingTi-2 SDF.asset",
        };

        // 上一版工具误把 atlasPopulationMode 直接扳成了 Static，结果比缺字更糟——
        // Build 里连菜单文字全变方块。这个菜单项专门用来把已经被那次误操作改坏的
        // 两份字体资产改回 Dynamic（脚本改枚举值这条路对切换到 Static 不可靠，但
        // 切回 Dynamic 是安全的，因为 Dynamic 本来就是这两份资产创建时的原始状态）。
        [MenuItem("Tools/Sky Prison/Localization/撤销字体 Static 误操作（改回 Dynamic）")]
        public static void RevertToDynamic()
        {
            int fixedCount = 0;
            foreach (string path in FontPaths)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null) continue;
                if (font.atlasPopulationMode == AtlasPopulationMode.Static)
                {
                    font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    EditorUtility.SetDirty(font);
                    fixedCount++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FontGlyphPrebakeTool] 撤销完成：{fixedCount} 份字体资产改回 Dynamic 模式。" +
                      "改完记得重新跑一次上面那个预烤工具，再重新打包测。");
        }

        [MenuItem("Tools/Sky Prison/Localization/预烤字体字形（修 Build 方块字）")]
        public static void PrebakeGlyphs()
        {
            var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TablePath);
            if (table == null)
            {
                Debug.LogError($"[FontGlyphPrebakeTool] 找不到本地化表：{TablePath}");
                return;
            }

            var charSet = new HashSet<char>();
            foreach (var entry in table.entries)
            {
                if (entry?.texts == null) continue;
                foreach (var t in entry.texts)
                {
                    if (string.IsNullOrEmpty(t?.text)) continue;
                    foreach (char c in t.text)
                        if (!char.IsControl(c)) charSet.Add(c);
                }
            }

            // 常用符号/数字/英文兜底，避免遗漏纯代码里拼的字符（Lv.、×、%、← → 等）。
            const string extra = "0123456789.,:;!?%×+-/()（）【】[]<>←→↑↓_ ";
            foreach (char c in extra) charSet.Add(c);

            if (charSet.Count == 0)
            {
                Debug.LogWarning("[FontGlyphPrebakeTool] 本地化表里没扫到任何字符，没什么可烤的。");
                return;
            }

            var sb = new StringBuilder();
            foreach (char c in charSet) sb.Append(c);
            string charString = sb.ToString();

            int totalFonts = 0, totalMissing = 0;
            foreach (string path in FontPaths)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null)
                {
                    Debug.LogWarning($"[FontGlyphPrebakeTool] 找不到字体资产：{path}，跳过。");
                    continue;
                }

                bool success = font.TryAddCharacters(charString, out string missingChars);
                totalFonts++;
                if (!string.IsNullOrEmpty(missingChars))
                {
                    totalMissing += missingChars.Length;
                    Debug.LogWarning($"[FontGlyphPrebakeTool] {font.name}：{missingChars.Length} 个字符没能加进图集" +
                                      $"（可能是图集已满，需要在 Font Asset 面板把 Atlas Width/Height 调大后重试）：{missingChars}");
                }

                // 之前这里试过直接把 atlasPopulationMode 扳成 Static，结果情况更糟——
                // 原来至少部分字能显示，扳完之后 Build 里连菜单文字全变方块了。说明
                // "脚本里直接改枚举值切 Static"这条路根本不可靠，Unity 官方走这条路
                // 是要经过完整的 Font Asset Creator 重新生成流程的，不是翻个字段就行。
                // 已经撤销，保持 Dynamic 模式，只做 TryAddCharacters 预烤这一步。

                EditorUtility.SetDirty(font);
                if (font.atlasTexture != null) EditorUtility.SetDirty(font.atlasTexture);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[FontGlyphPrebakeTool] 完成：扫到 {charSet.Count} 个不同字符，" +
                      $"处理了 {totalFonts} 份字体资产，" +
                      $"{(totalMissing > 0 ? $"{totalMissing} 个字符没烤进去（看上面警告）" : "全部成功烤入图集")}。");
        }
    }
}
