using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public class SkyPrisonCreateMapWindow : EditorWindow
{
    private struct SizePreset
    {
        public string label;
        public Vector2 size;

        public SizePreset(string label, float x, float y)
        {
            this.label = label;
            this.size = new Vector2(x, y);
        }
    }

    private static readonly SizePreset[] SizePresets =
    {
        new SizePreset("32×32", 32f, 32f),
        new SizePreset("64×64", 64f, 64f),
        new SizePreset("96×96", 96f, 96f),
        new SizePreset("128×128", 128f, 128f),
        new SizePreset("192×192", 192f, 192f),
        new SizePreset("自定义", -1f, -1f),
    };

    private string fileName = "NewMap";
    private string mapName = "新地图";
    private string mapDescription = "";
    private Vector2 mapSizeXZ = new Vector2(64f, 64f);
    private bool enableFogOfWar = true;
    private bool enableDayNightCycle = false;
    private bool enableWeather = false;
    private MapWeatherType weatherType = MapWeatherType.None;

    private readonly List<LocalizedTextEntry> localizedNames = new List<LocalizedTextEntry>();
    private readonly List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();
    private Vector2 scroll;
    private int selectedSizePresetIndex = 1;

    private const float BoundaryPreviewWidth = 300f;
    private const float BoundaryPreviewHeight = 180f;

    private Action<CreateMapResult> onCreate;

    public static void Open(Action<CreateMapResult> onCreateCallback)
    {
        SkyPrisonCreateMapWindow window = CreateInstance<SkyPrisonCreateMapWindow>();
        window.titleContent = new GUIContent("新建地图");
        window.minSize = new Vector2(640f, 620f);
        window.maxSize = new Vector2(640f, 760f);
        window.position = new Rect(220f, 120f, 640f, 620f);
        window.onCreate = onCreateCallback;
        window.InitializeLocalizationFields();
        // 新建地图窗口使用模态窗口：创建期间禁止操作其它编辑器内容。
        // 富文本编辑器由本窗口内部按钮打开，仍然允许作为子弹窗使用。
        window.ShowModalUtility();
        window.Focus();
    }

    private void InitializeLocalizationFields()
    {
        localizedNames.Clear();
        localizedDescriptions.Clear();

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = orderedLanguages[i];
            localizedNames.Add(new LocalizedTextEntry
            {
                languageCode = lang.languageCode,
                text = lang.isDefault ? mapName : ""
            });

            localizedDescriptions.Add(new LocalizedTextEntry
            {
                languageCode = lang.languageCode,
                text = lang.languageCode == defaultLanguageCode ? mapDescription : ""
            });
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("新建地图", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("地图在真正创建前，先填写基础信息。创建完成后会自动生成资源包、Scene 以及基础节点骨架。", MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        GUILayout.Space(6f);

        DrawFileNameSection();
        GUILayout.Space(8f);
        DrawLocalizedNameSection();
        GUILayout.Space(8f);
        DrawSizePresetSection();
        GUILayout.Space(8f);
        DrawMapBoundsPreviewSection();
        GUILayout.Space(8f);
        DrawLocalizedDescriptionSection();
        GUILayout.Space(8f);
        DrawRuleSection();

        EditorGUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        DrawBottomButtons();
    }

    private void DrawFileNameSection()
    {
        EditorGUILayout.LabelField("文件名称", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("文件名称只用于生成地图包文件夹、MapDefinition 资源和 Scene 文件名。多语言名称只负责游戏内显示，不参与文件命名。建议使用英文、数字和下划线，例如 OldFactory_01。", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("文件名称", GUILayout.Width(140f));
        string newFileName = EditorGUILayout.TextField(fileName ?? "");
        if (newFileName != fileName)
            fileName = SanitizeFileNamePreview(newFileName);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("地图 Key", GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(GenerateMapKeyPreview(), GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLocalizedNameSection()
    {
        EditorGUILayout.LabelField("地图名称", EditorStyles.boldLabel);
        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        EnsureLocalizedEntries(localizedNames, orderedLanguages);
        EnsureLocalizedEntries(localizedDescriptions, orderedLanguages);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = orderedLanguages[i];
            LocalizedTextEntry entry = FindLocalizedEntry(localizedNames, lang.languageCode);
            if (entry == null)
                continue;

            string label = string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName;
            if (lang.isDefault)
                label += "（默认）";

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));
            string newValue = EditorGUILayout.TextField(entry.text ?? "");
            if (newValue != entry.text)
            {
                entry.text = newValue;
                if (lang.languageCode == defaultLanguageCode)
                    mapName = newValue ?? "";
            }
            EditorGUILayout.EndHorizontal();
        }

        mapName = GetLocalizedText(localizedNames, defaultLanguageCode);
    }

    private void DrawSizePresetSection()
    {
        EditorGUILayout.LabelField("地图尺寸", EditorStyles.boldLabel);

        string[] presetLabels = new string[SizePresets.Length];
        for (int i = 0; i < SizePresets.Length; i++)
            presetLabels[i] = SizePresets[i].label;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("尺寸预设", GUILayout.Width(140f));
        int newPresetIndex = EditorGUILayout.Popup(selectedSizePresetIndex, presetLabels);
        EditorGUILayout.EndHorizontal();

        if (newPresetIndex != selectedSizePresetIndex)
        {
            selectedSizePresetIndex = newPresetIndex;
            if (selectedSizePresetIndex >= 0 && selectedSizePresetIndex < SizePresets.Length - 1)
                mapSizeXZ = SizePresets[selectedSizePresetIndex].size;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("尺寸（XZ）", GUILayout.Width(140f));
        float x = EditorGUILayout.FloatField("X", Mathf.Max(8f, mapSizeXZ.x));
        float y = EditorGUILayout.FloatField("Y", Mathf.Max(8f, mapSizeXZ.y));
        mapSizeXZ = new Vector2(Mathf.Max(8f, x), Mathf.Max(8f, y));
        EditorGUILayout.EndHorizontal();

        if (selectedSizePresetIndex < SizePresets.Length - 1)
        {
            Vector2 preset = SizePresets[selectedSizePresetIndex].size;
            if (!Mathf.Approximately(mapSizeXZ.x, preset.x) || !Mathf.Approximately(mapSizeXZ.y, preset.y))
                selectedSizePresetIndex = SizePresets.Length - 1;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("当前尺寸", GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel($"{mapSizeXZ.x:0.#} × {mapSizeXZ.y:0.#}", GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMapBoundsPreviewSection()
    {
        EditorGUILayout.LabelField("地图边界预览", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("当前阶段这里只预览创建后 MapBounds 的 XZ 边界比例。后续真正小地图会使用专用美术素材生成。", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("预览", GUILayout.Width(140f));
        Rect rect = GUILayoutUtility.GetRect(
            BoundaryPreviewWidth,
            BoundaryPreviewWidth,
            BoundaryPreviewHeight,
            BoundaryPreviewHeight,
            GUILayout.Width(BoundaryPreviewWidth),
            GUILayout.Height(BoundaryPreviewHeight));
        DrawBoundaryPreview(rect, new Vector3(mapSizeXZ.x, 6f, mapSizeXZ.y));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("边界尺寸", GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel($"X {mapSizeXZ.x:0.#} / Z {mapSizeXZ.y:0.#}", GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBoundaryPreview(Rect rect, Vector3 size)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.13f, 1f));
        DrawRectBorder(rect, new Color(1f, 1f, 1f, 0.12f));

        float safeX = Mathf.Max(1f, size.x);
        float safeZ = Mathf.Max(1f, size.z);
        float maxW = Mathf.Max(10f, rect.width - 44f);
        float maxH = Mathf.Max(10f, rect.height - 32f);
        float scale = Mathf.Min(maxW / safeX, maxH / safeZ);
        Rect mapRect = new Rect(rect.center.x - safeX * scale * 0.5f, rect.center.y - safeZ * scale * 0.5f, safeX * scale, safeZ * scale);

        EditorGUI.DrawRect(mapRect, new Color(0.33f, 0.70f, 0.52f, 0.18f));
        DrawRectBorder(mapRect, new Color(0.33f, 0.90f, 0.68f, 0.95f));

        EditorGUI.DrawRect(new Rect(mapRect.center.x - 12f, mapRect.center.y, 24f, 1f), new Color(1f, 1f, 1f, 0.28f));
        EditorGUI.DrawRect(new Rect(mapRect.center.x, mapRect.center.y - 12f, 1f, 24f), new Color(1f, 1f, 1f, 0.28f));

        GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.88f, 0.92f, 0.90f, 1f) }
        };
        GUI.Label(rect, $"MapBounds  X {safeX:0.#} / Z {safeZ:0.#}", style);
    }

    private void DrawRectBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void DrawLocalizedDescriptionSection()
    {
        EditorGUILayout.LabelField("地图描述", EditorStyles.boldLabel);

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        EnsureLocalizedEntries(localizedDescriptions, orderedLanguages);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = orderedLanguages[i];
            LocalizedTextEntry entry = FindLocalizedEntry(localizedDescriptions, lang.languageCode);
            if (entry == null)
                continue;

            string label = string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName;
            if (lang.isDefault)
                label += "（默认）";

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));
            bool requestOpenRichText = GUILayout.Button("打开富文本编辑器", GUILayout.Width(140f), GUILayout.Height(24f));
            EditorGUILayout.EndHorizontal();

            string previewText = !string.IsNullOrWhiteSpace(entry.text) ? entry.text : "（暂无描述）";
            EditorGUILayout.SelectableLabel(previewText, GUILayout.MinHeight(54f));
            EditorGUILayout.EndVertical();
            GUILayout.Space(2f);

            if (requestOpenRichText)
            {
                string openLang = lang.languageCode;
                string openLabel = label;
                string openCurrent = entry.text ?? "";
                EditorApplication.delayCall += () =>
                {
                    SkyPrisonRichTextEditorWindow.Open(
                        openLabel,
                        openCurrent,
                        updated =>
                        {
                            LocalizedTextEntry target = FindLocalizedEntry(localizedDescriptions, openLang);
                            if (target != null)
                                target.text = updated ?? "";

                            if (openLang == defaultLanguageCode)
                                mapDescription = updated ?? "";

                            Repaint();
                        },
                        "map");
                };
            }
        }

        mapDescription = GetLocalizedText(localizedDescriptions, defaultLanguageCode);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("主语言描述", GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(mapDescription) ? "-" : mapDescription, GUILayout.MinHeight(54f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRuleSection()
    {
        EditorGUILayout.LabelField("地图规则", EditorStyles.boldLabel);

        enableFogOfWar = EditorGUILayout.Toggle("开启战争迷雾", enableFogOfWar);
        enableDayNightCycle = EditorGUILayout.Toggle("昼夜交替", enableDayNightCycle);
        enableWeather = EditorGUILayout.Toggle("开启天气", enableWeather);

        using (new EditorGUI.DisabledScope(!enableWeather))
            weatherType = (MapWeatherType)EditorGUILayout.EnumPopup("天气类型", weatherType);
    }

    private void DrawBottomButtons()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("取消", GUILayout.Height(30f)))
        {
            Close();
            GUIUtility.ExitGUI();
        }

        using (new EditorGUI.DisabledScope(!CanCreate()))
        {
            if (GUILayout.Button("创建地图", GUILayout.Height(30f)))
            {
                string defaultLanguageCode = GetDefaultLanguageCode(LocalizationSettingsUtility.GetOrCreateSettings());
                mapName = GetLocalizedText(localizedNames, defaultLanguageCode);
                mapDescription = GetLocalizedText(localizedDescriptions, defaultLanguageCode);

                CreateMapResult payload = new CreateMapResult
                {
                    fileName = GetSafeFileName(),
                    mapName = string.IsNullOrWhiteSpace(mapName) ? "新地图" : mapName.Trim(),
                    mapKey = GenerateMapKeyPreview(),
                    mapDescription = mapDescription ?? "",
                    localizedNames = CloneLocalizedList(localizedNames),
                    localizedDescriptions = CloneLocalizedList(localizedDescriptions),
                    mapSizeXZ = new Vector2(Mathf.Max(8f, mapSizeXZ.x), Mathf.Max(8f, mapSizeXZ.y)),
                    enableFogOfWar = enableFogOfWar,
                    enableDayNightCycle = enableDayNightCycle,
                    enableWeather = enableWeather,
                    weatherType = weatherType,
                };

                Close();
                EditorApplication.delayCall += () => onCreate?.Invoke(payload);
                GUIUtility.ExitGUI();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private bool CanCreate()
    {
        string defaultLanguageCode = GetDefaultLanguageCode(LocalizationSettingsUtility.GetOrCreateSettings());
        string defaultName = GetLocalizedText(localizedNames, defaultLanguageCode);
        return !string.IsNullOrWhiteSpace(GetSafeFileName()) && !string.IsNullOrWhiteSpace(defaultName) && mapSizeXZ.x >= 8f && mapSizeXZ.y >= 8f;
    }

    private string GenerateMapKeyPreview()
    {
        string raw = GetSafeFileName().Trim().ToLowerInvariant();

        StringBuilder sb = new StringBuilder(raw.Length);
        bool lastUnderscore = false;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            bool valid = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (valid)
            {
                sb.Append(c);
                lastUnderscore = false;
            }
            else if (!lastUnderscore)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }

        string key = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(key) ? "new_map" : key;
    }

    private string GetSafeFileName()
    {
        string value = SanitizeFileNamePreview(fileName);
        return string.IsNullOrWhiteSpace(value) ? "NewMap" : value;
    }

    private string SanitizeFileNamePreview(string raw)
    {
        string value = string.IsNullOrWhiteSpace(raw) ? "" : raw.Trim();
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            value = value.Replace(invalid[i].ToString(), "_");

        return value.Replace("/", "_").Replace("\\", "_");
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetOrderedLanguages(LocalizationProjectSettings settings)
    {
        List<LocalizationProjectSettings.LanguageEntry> result = new List<LocalizationProjectSettings.LanguageEntry>();
        if (settings == null || settings.languages == null)
            return result;

        LocalizationProjectSettings.LanguageEntry defaultLang = null;

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang == null || !lang.enabled)
                continue;

            if (lang.isDefault)
                defaultLang = lang;
        }

        if (defaultLang != null)
            result.Add(defaultLang);

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang == null || !lang.enabled)
                continue;

            if (defaultLang != null && lang.languageCode == defaultLang.languageCode)
                continue;

            result.Add(lang);
        }

        return result;
    }

    private string GetDefaultLanguageCode(LocalizationProjectSettings settings)
    {
        if (settings == null || settings.languages == null || settings.languages.Count == 0)
            return "zh-CN";

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang != null && lang.enabled && lang.isDefault)
                return lang.languageCode;
        }

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang != null && lang.enabled)
                return lang.languageCode;
        }

        return "zh-CN";
    }

    private void EnsureLocalizedEntries(List<LocalizedTextEntry> list, List<LocalizationProjectSettings.LanguageEntry> orderedLanguages)
    {
        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = orderedLanguages[i];
            if (FindLocalizedEntry(list, lang.languageCode) == null)
            {
                list.Add(new LocalizedTextEntry
                {
                    languageCode = lang.languageCode,
                    text = ""
                });
            }
        }
    }

    private LocalizedTextEntry FindLocalizedEntry(List<LocalizedTextEntry> list, string languageCode)
    {
        if (list == null)
            return null;

        for (int i = 0; i < list.Count; i++)
        {
            LocalizedTextEntry entry = list[i];
            if (entry != null && entry.languageCode == languageCode)
                return entry;
        }

        return null;
    }

    private string GetLocalizedText(List<LocalizedTextEntry> list, string languageCode)
    {
        LocalizedTextEntry entry = FindLocalizedEntry(list, languageCode);
        return entry != null ? (entry.text ?? "") : "";
    }

    private List<LocalizedTextEntry> CloneLocalizedList(List<LocalizedTextEntry> source)
    {
        List<LocalizedTextEntry> result = new List<LocalizedTextEntry>();
        if (source == null)
            return result;

        for (int i = 0; i < source.Count; i++)
        {
            LocalizedTextEntry entry = source[i];
            if (entry == null)
                continue;

            result.Add(new LocalizedTextEntry
            {
                languageCode = entry.languageCode,
                text = entry.text ?? ""
            });
        }

        return result;
    }

    public struct CreateMapResult
    {
        public string fileName;
        public string mapName;
        public string mapKey;
        public string mapDescription;
        public List<LocalizedTextEntry> localizedNames;
        public List<LocalizedTextEntry> localizedDescriptions;
        public Vector2 mapSizeXZ;
        public bool enableFogOfWar;
        public bool enableDayNightCycle;
        public bool enableWeather;
        public MapWeatherType weatherType;
    }
}
