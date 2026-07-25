using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 挂在背包面板根节点上。
    /// 职责：
    ///   1. 语言切换时，从 LocalizationProjectSettings 读取当前语言字体，
    ///      并应用到面板内所有 Text/TMP_Text 组件（全局字体适配）。
    ///   2. 从 UILocalizationTable 读取当前语言的多语言文字，
    ///      更新面板内带 locKey 标记的 Text/TMP_Text。
    ///
    /// 使用方式：
    ///   - 工作台构建 PF 时通过 RegisterTextNode()/RegisterTMPTextNode() 注册每个
    ///     文字组件及其 key。这两套是分开的——背包窗口里筛选 Tab 这类元素实际用的是
    ///     TextMeshProUGUI，不是旧版 UI.Text，之前只有 RegisterTextNode()（认
    ///     UnityEngine.UI.Text）时，Workbench 那边 GetComponentInChildren&lt;Text&gt;()
    ///     永远查不到这些 TMP 组件、注册直接被跳过，不管重建多少次预制体都没用，
    ///     必须有专门认 TMP_Text 的一套。
    ///   - 运行时调用 ApplyCurrentLanguage() 或监听 LocalizationRuntime.OnLanguageChanged 事件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryTextLocalizer : MonoBehaviour
    {
        [System.Serializable]
        public struct LocalizedTextNode
        {
            public Text text;
            public string locKey;   // UILocalizationTable 里的 key，空字符串 = 只换字体不换内容
        }

        [System.Serializable]
        public struct LocalizedTMPTextNode
        {
            public TMP_Text text;
            public string locKey;
        }

        [SerializeField] private LocalizationProjectSettings localizationSettings;
        [SerializeField] private UILocalizationTable localizationTable;
        [SerializeField] private List<LocalizedTextNode> nodes = new List<LocalizedTextNode>();
        [SerializeField] private List<LocalizedTMPTextNode> tmpNodes = new List<LocalizedTMPTextNode>();

        // 不在 locKey 列表里、但仍需随语言切换字体的 Text（如动态数值）
        [SerializeField] private List<Text> fontOnlyTexts = new List<Text>();
        [SerializeField] private List<TMP_Text> fontOnlyTMPTexts = new List<TMP_Text>();

        private void OnEnable()
        {
            LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;
            ApplyCurrentLanguage();
        }

        private void OnDisable()
        {
            LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(string _) => ApplyCurrentLanguage();

        /// <summary>编辑器构建 PF 时调用，注册 Text 和对应本地化 key。</summary>
        public void RegisterTextNode(Text text, string locKey)
        {
            if (text == null) return;
            nodes.Add(new LocalizedTextNode { text = text, locKey = locKey ?? "" });
        }

        /// <summary>注册只需要换字体、不换文字内容的 Text（如数值文字）。</summary>
        public void RegisterFontOnlyText(Text text)
        {
            if (text == null) return;
            fontOnlyTexts.Add(text);
        }

        /// <summary>编辑器构建 PF 时调用，注册 TMP_Text 和对应本地化 key。</summary>
        public void RegisterTMPTextNode(TMP_Text text, string locKey)
        {
            if (text == null) return;
            tmpNodes.Add(new LocalizedTMPTextNode { text = text, locKey = locKey ?? "" });
        }

        /// <summary>注册只需要换字体、不换文字内容的 TMP_Text。</summary>
        public void RegisterFontOnlyTMPText(TMP_Text text)
        {
            if (text == null) return;
            fontOnlyTMPTexts.Add(text);
        }

        public void SetSources(LocalizationProjectSettings settings, UILocalizationTable table)
        {
            localizationSettings = settings;
            localizationTable    = table;
        }

        /// <summary>暴露字典表给同面板其他运行时组件（如交互弹窗）按 key 取词。</summary>
        public UILocalizationTable Table => localizationTable;

        /// <summary>应用当前语言的字体与文字。可在语言切换后由外部调用。</summary>
        [ContextMenu("Apply Current Language")]
        public void ApplyCurrentLanguage()
        {
            Font font = ResolveCurrentFont();

            foreach (LocalizedTextNode node in nodes)
            {
                if (node.text == null) continue;

                if (font != null)
                    node.text.font = font;

                if (!string.IsNullOrEmpty(node.locKey) && localizationTable != null)
                {
                    string localized = localizationTable.Get(node.locKey, node.text.text);
                    if (!string.IsNullOrEmpty(localized))
                        node.text.text = localized;
                }
            }

            if (font != null)
            {
                foreach (Text t in fontOnlyTexts)
                    if (t != null) t.font = font;
            }

            TMP_FontAsset tmpFont = ResolveCurrentTMPFont();

            foreach (LocalizedTMPTextNode node in tmpNodes)
            {
                if (node.text == null) continue;

                if (tmpFont != null)
                    node.text.font = tmpFont;

                if (!string.IsNullOrEmpty(node.locKey) && localizationTable != null)
                {
                    string localized = localizationTable.Get(node.locKey, node.text.text);
                    if (!string.IsNullOrEmpty(localized))
                        node.text.text = localized;
                }
            }

            if (tmpFont != null)
            {
                foreach (TMP_Text t in fontOnlyTMPTexts)
                    if (t != null) t.font = tmpFont;
            }
        }

        private Font ResolveCurrentFont()
        {
            if (localizationSettings == null) return null;

            string code = LocalizationRuntime.Instance != null
                ? LocalizationRuntime.Instance.CurrentCode
                : "";

            foreach (var lang in localizationSettings.languages)
            {
                if (lang == null || !lang.enabled) continue;
                if (lang.languageCode == code && lang.primaryFont != null)
                    return lang.primaryFont;
            }

            // fallback：默认语言字体
            var def = localizationSettings.GetDefaultLanguage();
            return def?.primaryFont;
        }

        private TMP_FontAsset ResolveCurrentTMPFont()
        {
            if (localizationSettings == null) return null;

            string code = LocalizationRuntime.Instance != null
                ? LocalizationRuntime.Instance.CurrentCode
                : "";

            foreach (var lang in localizationSettings.languages)
            {
                if (lang == null || !lang.enabled) continue;
                if (lang.languageCode == code && lang.primaryTMPFont != null)
                    return lang.primaryTMPFont;
            }

            var def = localizationSettings.GetDefaultLanguage();
            return def?.primaryTMPFont;
        }
    }
}
