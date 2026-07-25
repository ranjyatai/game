#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性补：PauseMenuController 的提示条已经在调 L("ui_pause_close", "关闭菜单")
    /// 查表本地化了，机制本身没问题（同一行"W 移动/E 点击"两条能正确显示日语，
    /// 证明缓存/查表流程是通的）——纯粹是这个key在UILocalizationTable里从来没被
    /// 建过，查不到就一直落到中文fallback，日/英环境下也是中文。
    /// </summary>
    public static class SkyPrisonEnsurePauseCloseLocKey
    {
        private const string TablePath = "Assets/_Project/Data/Resources/UILocalizationTable.asset";

        [MenuItem("Tools/Sky Prison/UI/补齐暂停菜单关闭提示本地化")]
        public static void Ensure()
        {
            var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TablePath);
            if (table == null)
            {
                Debug.LogError($"[SkyPrisonEnsurePauseCloseLocKey] 找不到本地化表：{TablePath}");
                return;
            }

            var entry = table.EnsureEntry("ui_pause_close", new List<string> { "zh-CN", "ja", "en" });
            SetLangText(entry, "zh-CN", "关闭菜单");
            SetLangText(entry, "ja", "メニューを閉じる");
            SetLangText(entry, "en", "Close Menu");

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log("[SkyPrisonEnsurePauseCloseLocKey] ui_pause_close 已补齐 zh-CN/ja/en 三个语言的翻译。");
        }

        private static void SetLangText(UILocalizationEntry entry, string languageCode, string text)
        {
            foreach (var t in entry.texts)
            {
                if (t.languageCode != languageCode) continue;
                if (string.IsNullOrEmpty(t.text)) t.text = text;
                return;
            }
        }
    }
}
#endif
