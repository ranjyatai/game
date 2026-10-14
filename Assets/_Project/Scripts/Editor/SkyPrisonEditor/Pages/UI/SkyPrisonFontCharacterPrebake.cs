using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从根上解决 TMP 动态字体图集在密集测试(反复触发大量 Domain Reload)下状态错位、
/// 显示错字的问题——不是靠调大图集或者清空重来（这次实测过，图集本来就是4096、
/// 多图集页也开着，清空后不重启 Unity 进程也没用，说明坑不在"空间不够"，是运行时
/// 增量加字这个操作本身跟高频 Domain Reload 不兼容）。
///
/// 真正的根治思路：把游戏里实际会用到的全部文字，一次性在编辑器里扫描收集出来，
/// 提前烘进字体资产的图集（这一步会真的保存进 .asset 文件），运行时就再也不会有
/// "遇到没见过的新字符，现场往图集里加"这个动作——没有增量修改，就没有状态跟
/// Domain Reload 打架的机会。字体仍然留在 Dynamic 模式（不需要重新生成成 Static），
/// 只是提前把已知要用的字符喂饱，动态模式的"遇到新字符还能兜底"能力还在，只是
/// 正常游玩/测试路径不会再触发它。
/// </summary>
public static class SkyPrisonFontCharacterPrebake
{
    private static readonly string[] TargetFontAssetPaths =
    {
        "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset",
    };

    [MenuItem("天空囚笼/UI/预烘焙全部游戏文本到字体图集", false, 182)]
    public static void Prebake()
    {
        HashSet<char> chars = CollectAllGameTextCharacters();
        Debug.Log($"[字体预烘焙] 从项目文本资产里收集到 {chars.Count} 个不重复字符。");

        // 常用 ASCII、数字、标点兜底——避免漏掉纯代码里手写的提示文字。
        for (char c = (char)0x20; c <= (char)0x7E; c++)
            chars.Add(c);

        var sb = new StringBuilder(chars.Count);
        foreach (char c in chars)
            sb.Append(c);
        string allChars = sb.ToString();

        foreach (string path in TargetFontAssetPaths)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null)
            {
                Debug.LogWarning($"[字体预烘焙] 找不到字体资产：{path}");
                continue;
            }

            bool ok = font.TryAddCharacters(allChars, out string missing);
            EditorUtility.SetDirty(font);

            if (string.IsNullOrEmpty(missing))
                Debug.Log($"[字体预烘焙] {font.name}：全部 {allChars.Length} 个字符（含去重）已烘入图集。");
            else
                Debug.LogWarning($"[字体预烘焙] {font.name}：有 {missing.Length} 个字符字体本身不支持，跳过：{missing}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[字体预烘焙] 完成，已保存到磁盘。这一步之后，上面这些字符运行时不会再触发图集增量修改。");
    }

    private static HashSet<char> CollectAllGameTextCharacters()
    {
        var chars = new HashSet<char>();

        CollectFromAssets<UnitDefinition>(chars, ud =>
        {
            AddString(chars, ud.displayName);
            AddString(chars, ud.note);
            AddString(chars, ud.description);
            AddEntries(chars, ud.localizedNames);
            AddEntries(chars, ud.localizedDescriptions);
        });

        CollectFromAssets<ItemDefinition>(chars, id =>
        {
            AddString(chars, id.displayName);
            AddString(chars, id.note);
            AddString(chars, id.description);
            AddEntries(chars, id.localizedNames);
            AddEntries(chars, id.localizedDescriptions);
        });

        CollectFromAssets<DialogueSentenceLibrary>(chars, lib =>
        {
            if (lib.entries == null) return;
            foreach (var entry in lib.entries)
            {
                if (entry == null) continue;
                AddString(chars, entry.sentenceId);
                AddString(chars, entry.chapter);
                AddString(chars, entry.note);
                AddEntries(chars, entry.speakerAliasOverride);
                AddEntries(chars, entry.texts);
            }
        });

        CollectFromAssets<UILocalizationTable>(chars, table =>
        {
            if (table.entries == null) return;
            foreach (var entry in table.entries)
            {
                if (entry == null) continue;
                AddString(chars, entry.key);
                AddEntries(chars, entry.texts);
            }
        });

        CollectFromAssets<NPCDialogueDefinition>(chars, def =>
        {
            AddEntries(chars, def.npcName);
        });

        return chars;
    }

    private static void CollectFromAssets<T>(HashSet<char> chars, System.Action<T> visit) where T : Object
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                visit(asset);
        }
    }

    private static void AddEntries(HashSet<char> chars, List<LocalizedTextEntry> entries)
    {
        if (entries == null) return;
        foreach (var e in entries)
            if (e != null) AddString(chars, e.text);
    }

    private static void AddString(HashSet<char> chars, string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        foreach (char c in s)
            chars.Add(c);
    }
}
