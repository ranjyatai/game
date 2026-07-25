using System;
using System.Collections.Generic;
using UnityEngine;

public class LocalizationRuntime : MonoBehaviour
{
    public static LocalizationRuntime Instance { get; private set; }

    /// <summary>语言切换后触发，参数为新的语言代码。</summary>
    public static event Action<string> OnLanguageChanged;

    private const string PrefsKey = "SkyPrison.Language";
    private const string ConfigResourcePath = "SkyPrison/LocalizationRuntimeConfig";

    private LocalizationProjectSettings settings;

    private string _currentCode;

    public string CurrentCode
    {
        get => _currentCode;
        private set
        {
            if (_currentCode == value) return;
            _currentCode = value;
            PlayerPrefs.SetString(PrefsKey, value);
            PlayerPrefs.Save();
            OnLanguageChanged?.Invoke(_currentCode);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[LocalizationRuntime]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LocalizationRuntime>();
        Instance.Init();
    }

    private void Init()
    {
        var config = Resources.Load<LocalizationRuntimeConfig>(ConfigResourcePath);
        if (config != null)
            settings = config.settings;

        if (settings == null)
            Debug.LogWarning("[LocalizationRuntime] 未找到 LocalizationRuntimeConfig，请在编辑器菜单 Sky Prison > 生成本地化配置 中创建。");

        string saved = PlayerPrefs.GetString(PrefsKey, "");
        _currentCode = IsValidCode(saved) ? saved : GetDefaultCode();
    }

    private void Awake()
    {
        // 防止场景里手动放置的重复实例
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Init();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetLanguage(string code)
    {
        if (IsValidCode(code))
            CurrentCode = code;
    }

    // ── 查询 ──────────────────────────────────────────────────────────

    public string GetText(List<LocalizedTextEntry> entries, string fallback = "")
    {
        if (entries == null) return fallback;
        string result = FindText(entries, _currentCode);
        if (!string.IsNullOrEmpty(result)) return result;

        // 找不到当前语言则退回默认语言
        string def = GetDefaultCode();
        if (def != _currentCode)
            result = FindText(entries, def);

        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    // 静态版本，不依赖单例（用于运行时单例未启动时的兜底）
    public static string Resolve(List<LocalizedTextEntry> entries, string languageCode, string fallback = "")
    {
        string result = FindText(entries, languageCode);
        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    /// <summary>当前语言的主字体（找不到退回默认语言字体）。供运行时 UI 取字体用。</summary>
    public Font GetCurrentFont()
    {
        if (settings == null) return null;
        foreach (var lang in settings.languages)
            if (lang != null && lang.enabled && lang.languageCode == _currentCode && lang.primaryFont != null)
                return lang.primaryFont;
        var def = settings.GetDefaultLanguage();
        return def != null ? def.primaryFont : null;
    }

    /// <summary>当前语言的 TMP 字体。供代码动态建 UI 用，Build 里也能正确打包。</summary>
    public TMPro.TMP_FontAsset GetCurrentTMPFont()
    {
        if (settings == null) return null;
        foreach (var lang in settings.languages)
            if (lang != null && lang.enabled && lang.languageCode == _currentCode && lang.primaryTMPFont != null)
                return lang.primaryTMPFont;
        var def = settings.GetDefaultLanguage();
        return def != null ? def.primaryTMPFont : null;
    }

    // ── 工具 ──────────────────────────────────────────────────────────

    private static string FindText(List<LocalizedTextEntry> entries, string code)
    {
        if (entries == null || string.IsNullOrEmpty(code)) return "";
        foreach (var e in entries)
            if (e != null && e.languageCode == code)
                return e.text ?? "";
        return "";
    }

    private string GetDefaultCode()
    {
        if (settings == null) return "zh-CN";
        var def = settings.GetDefaultLanguage();
        return def != null ? def.languageCode : "zh-CN";
    }

    private bool IsValidCode(string code)
    {
        if (string.IsNullOrEmpty(code) || settings == null) return false;
        foreach (var lang in settings.languages)
            if (lang != null && lang.enabled && lang.languageCode == code)
                return true;
        return false;
    }
}
