#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SkyPrisonInputSettingsWindow : EditorWindow
{
    private const float WindowWidth = 980f;
    private const float WindowHeight = 920f;

    private SkyPrisonInputSettings settings;
    private SerializedObject settingsSO;
    private Vector2 tableScroll;

    private bool foldMovement = true;
    private bool foldCombat = true;
    private bool foldQuickItems = true;
    private bool foldSystem = true;
    private bool foldAdvanced = true;

    private GUIStyle titleStyle;
    private GUIStyle sectionTitleStyle;
    private GUIStyle subTitleStyle;
    private GUIStyle darkContainerStyle;
    private GUIStyle tableHeaderStyle;
    private GUIStyle tableCellStyle;
    private GUIStyle centeredMiniLabelStyle;
    private GUIStyle infoStyle;

    private Texture2D darkBgTex;
    private Texture2D darkerBgTex;
    private Texture2D rowOddTex;
    private Texture2D rowEvenTex;
    private Texture2D headerTex;

    private struct KeyOption
    {
        public KeyCode key;
        public string label;

        public KeyOption(KeyCode key, string label)
        {
            this.key = key;
            this.label = label;
        }
    }

    private static readonly KeyOption[] KeyboardKeyOptions =
    {
        new KeyOption(KeyCode.None, "无"),

        new KeyOption(KeyCode.Mouse0, "鼠标左键"),
        new KeyOption(KeyCode.Mouse1, "鼠标右键"),
        new KeyOption(KeyCode.Mouse2, "鼠标中键"),
        new KeyOption(KeyCode.Mouse3, "鼠标侧键 1"),
        new KeyOption(KeyCode.Mouse4, "鼠标侧键 2"),
        new KeyOption(KeyCode.Mouse5, "鼠标侧键 3"),
        new KeyOption(KeyCode.Mouse6, "鼠标侧键 4"),

        new KeyOption(KeyCode.W, "W"),
        new KeyOption(KeyCode.A, "A"),
        new KeyOption(KeyCode.S, "S"),
        new KeyOption(KeyCode.D, "D"),
        new KeyOption(KeyCode.Q, "Q"),
        new KeyOption(KeyCode.E, "E"),
        new KeyOption(KeyCode.R, "R"),
        new KeyOption(KeyCode.T, "T"),
        new KeyOption(KeyCode.Y, "Y"),
        new KeyOption(KeyCode.U, "U"),
        new KeyOption(KeyCode.I, "I"),
        new KeyOption(KeyCode.O, "O"),
        new KeyOption(KeyCode.P, "P"),
        new KeyOption(KeyCode.F, "F"),
        new KeyOption(KeyCode.G, "G"),
        new KeyOption(KeyCode.H, "H"),
        new KeyOption(KeyCode.J, "J"),
        new KeyOption(KeyCode.K, "K"),
        new KeyOption(KeyCode.L, "L"),
        new KeyOption(KeyCode.Z, "Z"),
        new KeyOption(KeyCode.X, "X"),
        new KeyOption(KeyCode.C, "C"),
        new KeyOption(KeyCode.V, "V"),
        new KeyOption(KeyCode.B, "B"),
        new KeyOption(KeyCode.N, "N"),
        new KeyOption(KeyCode.M, "M"),

        new KeyOption(KeyCode.Alpha1, "数字 1"),
        new KeyOption(KeyCode.Alpha2, "数字 2"),
        new KeyOption(KeyCode.Alpha3, "数字 3"),
        new KeyOption(KeyCode.Alpha4, "数字 4"),
        new KeyOption(KeyCode.Alpha5, "数字 5"),
        new KeyOption(KeyCode.Alpha6, "数字 6"),
        new KeyOption(KeyCode.Alpha7, "数字 7"),
        new KeyOption(KeyCode.Alpha8, "数字 8"),
        new KeyOption(KeyCode.Alpha9, "数字 9"),
        new KeyOption(KeyCode.Alpha0, "数字 0"),

        new KeyOption(KeyCode.Space, "空格"),
        new KeyOption(KeyCode.LeftShift, "左 Shift"),
        new KeyOption(KeyCode.RightShift, "右 Shift"),
        new KeyOption(KeyCode.LeftControl, "左 Ctrl"),
        new KeyOption(KeyCode.RightControl, "右 Ctrl"),
        new KeyOption(KeyCode.LeftAlt, "左 Alt"),
        new KeyOption(KeyCode.RightAlt, "右 Alt"),
        new KeyOption(KeyCode.Tab, "Tab"),
        new KeyOption(KeyCode.Return, "回车"),
        new KeyOption(KeyCode.Escape, "Esc"),
        new KeyOption(KeyCode.BackQuote, "~"),
        new KeyOption(KeyCode.Backspace, "退格"),

        new KeyOption(KeyCode.UpArrow, "上方向键"),
        new KeyOption(KeyCode.DownArrow, "下方向键"),
        new KeyOption(KeyCode.LeftArrow, "左方向键"),
        new KeyOption(KeyCode.RightArrow, "右方向键"),

        new KeyOption(KeyCode.F1, "F1"),
        new KeyOption(KeyCode.F2, "F2"),
        new KeyOption(KeyCode.F3, "F3"),
        new KeyOption(KeyCode.F4, "F4"),
        new KeyOption(KeyCode.F5, "F5"),
        new KeyOption(KeyCode.F6, "F6"),
        new KeyOption(KeyCode.F7, "F7"),
        new KeyOption(KeyCode.F8, "F8"),
        new KeyOption(KeyCode.F9, "F9"),
        new KeyOption(KeyCode.F10, "F10"),
        new KeyOption(KeyCode.F11, "F11"),
        new KeyOption(KeyCode.F12, "F12"),
    };

    private static readonly KeyOption[] GamepadKeyOptions =
    {
        new KeyOption(KeyCode.None, "无"),
        new KeyOption(KeyCode.JoystickButton0, "手柄按键 0"),
        new KeyOption(KeyCode.JoystickButton1, "手柄按键 1"),
        new KeyOption(KeyCode.JoystickButton2, "手柄按键 2"),
        new KeyOption(KeyCode.JoystickButton3, "手柄按键 3"),
        new KeyOption(KeyCode.JoystickButton4, "手柄按键 4"),
        new KeyOption(KeyCode.JoystickButton5, "手柄按键 5"),
        new KeyOption(KeyCode.JoystickButton6, "手柄按键 6"),
        new KeyOption(KeyCode.JoystickButton7, "手柄按键 7"),
        new KeyOption(KeyCode.JoystickButton8, "手柄按键 8"),
        new KeyOption(KeyCode.JoystickButton9, "手柄按键 9"),
        new KeyOption(KeyCode.JoystickButton10, "手柄按键 10"),
        new KeyOption(KeyCode.JoystickButton11, "手柄按键 11"),
        new KeyOption(KeyCode.JoystickButton12, "手柄按键 12"),
        new KeyOption(KeyCode.JoystickButton13, "手柄按键 13"),
        new KeyOption(KeyCode.JoystickButton14, "手柄按键 14"),
        new KeyOption(KeyCode.JoystickButton15, "手柄按键 15"),
        new KeyOption(KeyCode.JoystickButton16, "手柄按键 16"),
        new KeyOption(KeyCode.JoystickButton17, "手柄按键 17"),
        new KeyOption(KeyCode.JoystickButton18, "手柄按键 18"),
        new KeyOption(KeyCode.JoystickButton19, "手柄按键 19"),
    };

    private static readonly string[] TriggerModeLabels = { "按住", "按下", "松开" };

    [MenuItem("Tools/按键设置")]
    public static void Open()
    {
        SkyPrisonInputSettingsWindow window = GetWindow<SkyPrisonInputSettingsWindow>("输入设置");
        window.minSize = new Vector2(WindowWidth, WindowHeight);
        window.maxSize = new Vector2(WindowWidth, WindowHeight);
        window.Show();
        window.Focus();
    }

    private void OnEnable()
    {
        InitializeStyles();
        LoadOrCreateSettings();
        minSize = new Vector2(WindowWidth, WindowHeight);
        maxSize = new Vector2(WindowWidth, WindowHeight);
    }

    private void InitializeStyles()
    {
        if (titleStyle != null)
            return;

        darkBgTex = MakeTex(new Color(0.13f, 0.14f, 0.16f));
        darkerBgTex = MakeTex(new Color(0.10f, 0.11f, 0.13f));
        rowOddTex = MakeTex(new Color(0.16f, 0.17f, 0.19f));
        rowEvenTex = MakeTex(new Color(0.14f, 0.15f, 0.17f));
        headerTex = MakeTex(new Color(0.19f, 0.20f, 0.22f));

        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
        sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        subTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

        darkContainerStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 10, 10),
            margin = new RectOffset(0, 0, 6, 6),
            normal = { background = darkBgTex }
        };

        tableHeaderStyle = new GUIStyle(EditorStyles.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.88f, 0.88f, 0.88f) }
        };

        tableCellStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(6, 6, 0, 0)
        };

        centeredMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.78f, 0.78f, 0.78f) }
        };

        infoStyle = new GUIStyle(EditorStyles.helpBox)
        {
            wordWrap = true,
            fontSize = 11
        };
    }

    private void LoadOrCreateSettings()
    {
        settings = AssetDatabase.LoadAssetAtPath<SkyPrisonInputSettings>(SkyPrisonInputSettings.DefaultAssetPath);
        if (settings == null)
        {
            EnsureFolderExists("Assets/_Project/Data/Settings");
            settings = CreateInstance<SkyPrisonInputSettings>();
            settings.EnsureDefaults();
            AssetDatabase.CreateAsset(settings, SkyPrisonInputSettings.DefaultAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        settings.EnsureDefaults();
        EditorUtility.SetDirty(settings);
        settingsSO = new SerializedObject(settings);
    }

    private void OnGUI()
    {
        if (titleStyle == null)
            InitializeStyles();

        if (settings == null || settingsSO == null)
            LoadOrCreateSettings();

        settingsSO.Update();

        DrawHeader();
        GUILayout.Space(8f);
        DrawActionButtons();
        GUILayout.Space(10f);
        DrawMovementSection();
        GUILayout.Space(10f);
        DrawDoubleTapSection();
        GUILayout.Space(8f);
        DrawConflictWarnings();
        GUILayout.Space(8f);
        DrawBindingSection();

        settingsSO.ApplyModifiedProperties();

        if (GUI.changed)
        {
            settings.EnsureDefaults();
            settings.RebuildLookup();
            EditorUtility.SetDirty(settings);
        }
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("天空囚笼 输入设置", titleStyle);
        EditorGUILayout.HelpBox("动作绑定从这里读取。V6 默认：Space 跳跃，鼠标右键闪避；轻攻击为鼠标左键按下，重攻击暂定为长按鼠标左键；快捷物品 1~4 对应数字键 1~4。", MessageType.Info);
    }

    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("重置 / 补全默认绑定", GUILayout.Width(170f), GUILayout.Height(26f)))
        {
            Undo.RecordObject(settings, "Reset Input Settings");
            settings.bindings.Clear();
            settings.EnsureDefaults();
            EditorUtility.SetDirty(settings);
            settingsSO.Update();
        }

        if (GUILayout.Button("应用 V5 默认键位", GUILayout.Width(145f), GUILayout.Height(26f)))
        {
            Undo.RecordObject(settings, "Apply V5 Default Input Scheme");
            settings.ApplyV5DefaultKeyboardScheme();
            EditorUtility.SetDirty(settings);
            settingsSO.Update();
        }

        if (GUILayout.Button("定位配置资产", GUILayout.Width(140f), GUILayout.Height(26f)))
        {
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMovementSection()
    {
        EditorGUILayout.BeginVertical(darkContainerStyle);
        EditorGUILayout.LabelField("移动输入", sectionTitleStyle);
        GUILayout.Space(4f);

        SerializedProperty normalizeDigitalMove = settingsSO.FindProperty("normalizeDigitalMove");
        SerializedProperty enableGamepadAxes = settingsSO.FindProperty("enableGamepadAxes");
        SerializedProperty gamepadHorizontalAxis = settingsSO.FindProperty("gamepadHorizontalAxis");
        SerializedProperty gamepadVerticalAxis = settingsSO.FindProperty("gamepadVerticalAxis");
        SerializedProperty gamepadDeadZone = settingsSO.FindProperty("gamepadDeadZone");

        EditorGUILayout.LabelField("键盘移动", subTitleStyle);
        normalizeDigitalMove.boolValue = EditorGUILayout.ToggleLeft("数字移动归一化", normalizeDigitalMove.boolValue);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("手柄轴", subTitleStyle);
        enableGamepadAxes.boolValue = EditorGUILayout.ToggleLeft("启用手柄摇杆轴", enableGamepadAxes.boolValue);

        using (new EditorGUI.DisabledScope(!enableGamepadAxes.boolValue))
        {
            DrawLabeledTextField("水平轴", gamepadHorizontalAxis, 120f);
            DrawLabeledTextField("垂直轴", gamepadVerticalAxis, 120f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("摇杆死区", GUILayout.Width(120f));
            gamepadDeadZone.floatValue = EditorGUILayout.Slider(gamepadDeadZone.floatValue, 0f, 0.95f);
            GUILayout.Label(gamepadDeadZone.floatValue.ToString("0.00"), centeredMiniLabelStyle, GUILayout.Width(42f));
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawDoubleTapSection()
    {
        SerializedProperty enableSprintDoubleTapDodge = settingsSO.FindProperty("enableSprintDoubleTapDodge");
        SerializedProperty sprintDoubleTapWindow = settingsSO.FindProperty("sprintDoubleTapWindow");
        SerializedProperty sprintDoubleTapMinReleaseTime = settingsSO.FindProperty("sprintDoubleTapMinReleaseTime");
        SerializedProperty runSuppressAfterDoubleTapDodge = settingsSO.FindProperty("runSuppressAfterDoubleTapDodge");
        SerializedProperty noMoveInputDodgeForward = settingsSO.FindProperty("noMoveInputDodgeForward");
        SerializedProperty directDodgeKeyStillAllowed = settingsSO.FindProperty("directDodgeKeyStillAllowed");

        if (enableSprintDoubleTapDodge == null)
        {
            EditorGUILayout.HelpBox("当前 SkyPrisonInputSettings 版本还没有双击奔跑键闪避参数。请先替换 SkyPrisonInputSettings_V3。", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginVertical(darkContainerStyle);
        foldAdvanced = EditorGUILayout.Foldout(foldAdvanced, "高级输入规则", true);
        if (foldAdvanced)
        {
            enableSprintDoubleTapDodge.boolValue = EditorGUILayout.ToggleLeft("启用：双击奔跑键触发闪避", enableSprintDoubleTapDodge.boolValue);

            using (new EditorGUI.DisabledScope(!enableSprintDoubleTapDodge.boolValue))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("双击判定窗口", GUILayout.Width(130f));
                sprintDoubleTapWindow.floatValue = EditorGUILayout.Slider(sprintDoubleTapWindow.floatValue, 0.08f, 0.6f);
                GUILayout.Label(sprintDoubleTapWindow.floatValue.ToString("0.00") + "s", centeredMiniLabelStyle, GUILayout.Width(48f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("最小松开时间", GUILayout.Width(130f));
                sprintDoubleTapMinReleaseTime.floatValue = EditorGUILayout.Slider(sprintDoubleTapMinReleaseTime.floatValue, 0f, 0.2f);
                GUILayout.Label(sprintDoubleTapMinReleaseTime.floatValue.ToString("0.00") + "s", centeredMiniLabelStyle, GUILayout.Width(48f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("闪避后奔跑压制", GUILayout.Width(130f));
                runSuppressAfterDoubleTapDodge.floatValue = EditorGUILayout.Slider(runSuppressAfterDoubleTapDodge.floatValue, 0f, 0.25f);
                GUILayout.Label(runSuppressAfterDoubleTapDodge.floatValue.ToString("0.00") + "s", centeredMiniLabelStyle, GUILayout.Width(48f));
                EditorGUILayout.EndHorizontal();

                noMoveInputDodgeForward.boolValue = EditorGUILayout.ToggleLeft("无方向输入时向前闪避；关闭则默认后撤", noMoveInputDodgeForward.boolValue);
            }

            directDodgeKeyStillAllowed.boolValue = EditorGUILayout.ToggleLeft("保留独立闪避键绑定（V5 默认开启；默认闪避=鼠标右键）", directDodgeKeyStillAllowed.boolValue);
            EditorGUILayout.HelpBox("V5 默认不再依赖双击奔跑键闪避。Dodge 行是正式闪避绑定，默认鼠标右键；双击奔跑键闪避只作为可选高级规则保留。", MessageType.None);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawLabeledTextField(string label, SerializedProperty property, float labelWidth)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        property.stringValue = EditorGUILayout.TextField(property.stringValue);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawConflictWarnings()
    {
        List<string> conflicts = BuildConflictWarnings();
        if (conflicts.Count == 0)
            return;

        string message = string.Join("\n", conflicts);
        EditorGUILayout.HelpBox(message, MessageType.Warning);
    }

    private List<string> BuildConflictWarnings()
    {
        List<string> warnings = new List<string>();
        if (settings == null || settings.bindings == null)
            return warnings;

        Dictionary<KeyCode, InputConflictOwner> keyboardOwner = new Dictionary<KeyCode, InputConflictOwner>();
        Dictionary<KeyCode, InputConflictOwner> gamepadOwner = new Dictionary<KeyCode, InputConflictOwner>();

        for (int i = 0; i < settings.bindings.Count; i++)
        {
            SkyPrisonInputBinding binding = settings.bindings[i];
            if (binding == null)
                continue;

            string name = string.IsNullOrWhiteSpace(binding.displayName) ? GetActionLabel(binding.action) : binding.displayName;
            CheckKeyConflict(binding.primaryKey, name + " 主键", binding, keyboardOwner, warnings);
            CheckKeyConflict(binding.secondaryKey, name + " 备用键", binding, keyboardOwner, warnings);
            CheckKeyConflict(binding.gamepadKey, name + " 手柄", binding, gamepadOwner, warnings);
        }

        SkyPrisonInputBinding jump = FindBinding(SkyPrisonInputAction.Jump);
        SkyPrisonInputBinding dodge = FindBinding(SkyPrisonInputAction.Dodge);
        SkyPrisonInputBinding sprint = FindBinding(SkyPrisonInputAction.Sprint);

        if (jump != null && dodge != null && SharesKeyboardKey(jump, dodge))
            warnings.Add("跳跃和闪避存在键位重叠：这会导致 Space/闪避冲突。建议 Jump=Space，Dodge=Mouse1。");

        if (sprint != null && jump != null && SharesKeyboardKey(sprint, jump))
            warnings.Add("奔跑和跳跃存在键位重叠：双击奔跑键闪避会和跳跃抢输入。");

        return warnings;
    }

    private SkyPrisonInputBinding FindBinding(SkyPrisonInputAction action)
    {
        if (settings == null || settings.bindings == null)
            return null;

        for (int i = 0; i < settings.bindings.Count; i++)
        {
            if (settings.bindings[i] != null && settings.bindings[i].action == action)
                return settings.bindings[i];
        }

        return null;
    }

    private struct InputConflictOwner
    {
        public string label;
        public SkyPrisonInputAction action;
        public SkyPrisonInputTriggerMode triggerMode;
    }

    private static void CheckKeyConflict(KeyCode key, string owner, SkyPrisonInputBinding binding, Dictionary<KeyCode, InputConflictOwner> map, List<string> warnings)
    {
        if (key == KeyCode.None || binding == null)
            return;

        if (map.TryGetValue(key, out InputConflictOwner existing))
        {
            if (IsAllowedTapHoldPair(existing.action, existing.triggerMode, binding.action, binding.triggerMode))
                return;

            warnings.Add($"键位冲突：{GetFallbackKeyLabel(key)} 同时绑定给 {existing.label} 和 {owner}");
            return;
        }

        map[key] = new InputConflictOwner
        {
            label = owner,
            action = binding.action,
            triggerMode = binding.triggerMode
        };
    }

    private static bool IsAllowedTapHoldPair(
        SkyPrisonInputAction a,
        SkyPrisonInputTriggerMode aMode,
        SkyPrisonInputAction b,
        SkyPrisonInputTriggerMode bMode)
    {
        bool isLightHeavyPair =
            (a == SkyPrisonInputAction.LightAttack && b == SkyPrisonInputAction.HeavyAttack) ||
            (a == SkyPrisonInputAction.HeavyAttack && b == SkyPrisonInputAction.LightAttack);

        if (!isLightHeavyPair)
            return false;

        return (aMode == SkyPrisonInputTriggerMode.Press && bMode == SkyPrisonInputTriggerMode.Hold) ||
               (aMode == SkyPrisonInputTriggerMode.Hold && bMode == SkyPrisonInputTriggerMode.Press);
    }

    private static bool SharesKeyboardKey(SkyPrisonInputBinding a, SkyPrisonInputBinding b)
    {
        if (a == null || b == null)
            return false;

        return KeyOverlap(a.primaryKey, b.primaryKey)
            || KeyOverlap(a.primaryKey, b.secondaryKey)
            || KeyOverlap(a.secondaryKey, b.primaryKey)
            || KeyOverlap(a.secondaryKey, b.secondaryKey);
    }

    private static bool KeyOverlap(KeyCode a, KeyCode b)
    {
        return a != KeyCode.None && a == b;
    }

    private void DrawBindingSection()
    {
        SerializedProperty bindings = settingsSO.FindProperty("bindings");
        if (bindings == null)
            return;

        EditorGUILayout.LabelField("动作绑定", sectionTitleStyle);

        Rect containerRect = GUILayoutUtility.GetRect(0f, 10000f, 0f, Mathf.Max(360f, position.height - 435f));
        GUI.Box(containerRect, GUIContent.none, darkContainerStyle);

        Rect innerRect = new Rect(containerRect.x + 8f, containerRect.y + 8f, containerRect.width - 16f, containerRect.height - 16f);
        float contentHeight = CalculateTableContentHeight(bindings);
        Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, contentHeight);

        tableScroll = GUI.BeginScrollView(innerRect, tableScroll, viewRect);

        float y = 0f;
        DrawTableHeader(new Rect(0f, y, viewRect.width, 28f));
        y += 30f;

        DrawGroup(bindings, ref y, "移动", ref foldMovement, IsMovementAction);
        DrawGroup(bindings, ref y, "战斗 / 技能", ref foldCombat, IsCombatAction);
        DrawGroup(bindings, ref y, "快捷物品", ref foldQuickItems, IsQuickItemAction);
        DrawGroup(bindings, ref y, "交互 / 界面", ref foldSystem, IsSystemAction);

        GUI.EndScrollView();
    }

    private float CalculateTableContentHeight(SerializedProperty bindings)
    {
        float height = 30f;
        height += CalculateGroupHeight(bindings, foldMovement, IsMovementAction);
        height += CalculateGroupHeight(bindings, foldCombat, IsCombatAction);
        height += CalculateGroupHeight(bindings, foldQuickItems, IsQuickItemAction);
        height += CalculateGroupHeight(bindings, foldSystem, IsSystemAction);
        return Mathf.Max(height + 8f, 420f);
    }

    private float CalculateGroupHeight(SerializedProperty bindings, bool expanded, Func<SkyPrisonInputAction, bool> predicate)
    {
        float height = 28f;
        if (!expanded)
            return height;

        for (int i = 0; i < bindings.arraySize; i++)
        {
            SerializedProperty item = bindings.GetArrayElementAtIndex(i);
            SerializedProperty action = item?.FindPropertyRelative("action");
            if (action == null)
                continue;

            SkyPrisonInputAction actionValue = (SkyPrisonInputAction)action.intValue;
            if (predicate(actionValue))
                height += 32f;
        }

        return height;
    }

    private void DrawGroup(SerializedProperty bindings, ref float y, string title, ref bool expanded, Func<SkyPrisonInputAction, bool> predicate)
    {
        Rect headerRect = new Rect(0f, y, 835f, 24f);
        DrawGroupHeader(headerRect, title, ref expanded);
        y += 28f;

        if (!expanded)
            return;

        int rowIndex = 0;
        for (int i = 0; i < bindings.arraySize; i++)
        {
            SerializedProperty item = bindings.GetArrayElementAtIndex(i);
            if (item == null)
                continue;

            SerializedProperty action = item.FindPropertyRelative("action");
            if (action == null)
                continue;

            SkyPrisonInputAction actionValue = (SkyPrisonInputAction)action.intValue;
            if (!predicate(actionValue))
                continue;

            Rect rowRect = new Rect(0f, y, 835f, 30f);
            DrawBindingRow(item, rowIndex, rowRect);
            y += 32f;
            rowIndex++;
        }
    }

    private void DrawGroupHeader(Rect rect, string title, ref bool expanded)
    {
        EditorGUI.DrawRect(rect, new Color(0.11f, 0.12f, 0.14f, 1f));

        Rect arrowRect = new Rect(rect.x + 8f, rect.y, 18f, rect.height);
        Rect labelRect = new Rect(rect.x + 28f, rect.y, rect.width - 36f, rect.height);

        GUI.Label(arrowRect, expanded ? "▼" : "▶", centeredMiniLabelStyle);
        GUI.Label(labelRect, title, tableHeaderStyle);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button == 0)
        {
            expanded = !expanded;
            e.Use();
            Repaint();
        }
    }

    private static bool IsMovementAction(SkyPrisonInputAction action)
    {
        return action == SkyPrisonInputAction.MoveUp
            || action == SkyPrisonInputAction.MoveDown
            || action == SkyPrisonInputAction.MoveLeft
            || action == SkyPrisonInputAction.MoveRight
            || action == SkyPrisonInputAction.Sprint
            || action == SkyPrisonInputAction.Sneak
            || action == SkyPrisonInputAction.Jump
            || action == SkyPrisonInputAction.Dodge;
    }

    private static bool IsCombatAction(SkyPrisonInputAction action)
    {
        return action == SkyPrisonInputAction.LightAttack
            || action == SkyPrisonInputAction.HeavyAttack
            || action == SkyPrisonInputAction.Skill1
            || action == SkyPrisonInputAction.Skill2
            || action == SkyPrisonInputAction.Skill3;
    }

    private static bool IsQuickItemAction(SkyPrisonInputAction action)
    {
        return action == SkyPrisonInputAction.QuickItem1
            || action == SkyPrisonInputAction.QuickItem2
            || action == SkyPrisonInputAction.QuickItem3
            || action == SkyPrisonInputAction.QuickItem4;
    }

    private static bool IsSystemAction(SkyPrisonInputAction action)
    {
        return action == SkyPrisonInputAction.Interact
            || action == SkyPrisonInputAction.Inventory
            || action == SkyPrisonInputAction.Map
            || action == SkyPrisonInputAction.Menu
            || action == SkyPrisonInputAction.CharacterPanel;
    }

    private void DrawTableHeader(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.19f, 0.20f, 0.22f));

        float x = rect.x;
        DrawHeaderCell(new Rect(x, rect.y, 120f, rect.height), "动作"); x += 120f;
        DrawHeaderCell(new Rect(x, rect.y, 150f, rect.height), "显示名"); x += 150f;
        DrawHeaderCell(new Rect(x, rect.y, 100f, rect.height), "触发"); x += 100f;
        DrawHeaderCell(new Rect(x, rect.y, 155f, rect.height), "主键"); x += 155f;
        DrawHeaderCell(new Rect(x, rect.y, 155f, rect.height), "备用键"); x += 155f;
        DrawHeaderCell(new Rect(x, rect.y, 155f, rect.height), "手柄");
    }

    private void DrawHeaderCell(Rect rect, string text)
    {
        GUI.Label(rect, text, tableHeaderStyle);
    }

    private void DrawBindingRow(SerializedProperty item, int index, Rect rect)
    {
        SerializedProperty action = item.FindPropertyRelative("action");
        SerializedProperty displayName = item.FindPropertyRelative("displayName");
        SerializedProperty triggerMode = item.FindPropertyRelative("triggerMode");
        SerializedProperty primaryKey = item.FindPropertyRelative("primaryKey");
        SerializedProperty secondaryKey = item.FindPropertyRelative("secondaryKey");
        SerializedProperty gamepadKey = item.FindPropertyRelative("gamepadKey");

        EditorGUI.DrawRect(rect, (index % 2 == 0) ? new Color(0.16f, 0.17f, 0.19f) : new Color(0.14f, 0.15f, 0.17f));

        Rect padded = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);
        float x = padded.x;

        Rect actionRect = new Rect(x, padded.y, 120f, 18f); x += 120f;
        Rect displayRect = new Rect(x, padded.y, 150f, 18f); x += 150f;
        Rect triggerRect = new Rect(x, padded.y, 100f, 18f); x += 100f;
        Rect primaryRect = new Rect(x, padded.y, 155f, 18f); x += 155f;
        Rect secondaryRect = new Rect(x, padded.y, 155f, 18f); x += 155f;
        Rect gamepadRect = new Rect(x, padded.y, 155f, 18f);

        SkyPrisonInputAction actionValue = (SkyPrisonInputAction)action.intValue;
        EditorGUI.LabelField(actionRect, GetActionLabel(actionValue), tableCellStyle);

        displayName.stringValue = EditorGUI.TextField(displayRect, displayName.stringValue ?? string.Empty);

        SkyPrisonInputTriggerMode triggerValue = (SkyPrisonInputTriggerMode)triggerMode.intValue;
        triggerMode.intValue = (int)(SkyPrisonInputTriggerMode)EditorGUI.Popup(triggerRect, (int)triggerValue, TriggerModeLabels);

        primaryKey.intValue = (int)DrawKeyPopup(primaryRect, (KeyCode)primaryKey.intValue, KeyboardKeyOptions);
        secondaryKey.intValue = (int)DrawKeyPopup(secondaryRect, (KeyCode)secondaryKey.intValue, KeyboardKeyOptions);
        gamepadKey.intValue = (int)DrawKeyPopup(gamepadRect, (KeyCode)gamepadKey.intValue, GamepadKeyOptions);
    }

    private static KeyCode DrawKeyPopup(Rect rect, KeyCode current, KeyOption[] options)
    {
        int currentIndex = FindKeyIndex(current, options);
        string[] labels = BuildLabels(options, current, ref currentIndex);
        int newIndex = EditorGUI.Popup(rect, currentIndex, labels);

        if (newIndex < 0 || newIndex >= labels.Length)
            return current;

        if (currentIndex >= options.Length && newIndex == currentIndex)
            return current;

        if (newIndex >= options.Length)
            return current;

        return options[newIndex].key;
    }

    private static int FindKeyIndex(KeyCode value, KeyOption[] options)
    {
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].key == value)
                return i;
        }

        return -1;
    }

    private static string[] BuildLabels(KeyOption[] options, KeyCode current, ref int currentIndex)
    {
        if (currentIndex >= 0)
        {
            string[] labels = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
                labels[i] = options[i].label;
            return labels;
        }

        string[] extended = new string[options.Length + 1];
        for (int i = 0; i < options.Length; i++)
            extended[i] = options[i].label;

        extended[options.Length] = GetFallbackKeyLabel(current);
        currentIndex = options.Length;
        return extended;
    }

    private static string GetFallbackKeyLabel(KeyCode key)
    {
        if (key == KeyCode.None)
            return "无";

        if (key.ToString().StartsWith("JoystickButton", StringComparison.Ordinal))
            return key.ToString().Replace("JoystickButton", "手柄按键 ");

        return ObjectNames.NicifyVariableName(key.ToString());
    }

    private static string GetActionLabel(SkyPrisonInputAction action)
    {
        switch (action)
        {
            case SkyPrisonInputAction.MoveUp: return "上移动";
            case SkyPrisonInputAction.MoveDown: return "下移动";
            case SkyPrisonInputAction.MoveLeft: return "左移动";
            case SkyPrisonInputAction.MoveRight: return "右移动";
            case SkyPrisonInputAction.Sprint: return "奔跑";
            case SkyPrisonInputAction.Sneak: return "潜行";
            case SkyPrisonInputAction.Jump: return "跳跃";
            case SkyPrisonInputAction.Dodge: return "闪避";
            case SkyPrisonInputAction.Interact: return "交互";
            case SkyPrisonInputAction.LightAttack: return "轻攻击";
            case SkyPrisonInputAction.HeavyAttack: return "重攻击";
            case SkyPrisonInputAction.Skill1: return "技能 1";
            case SkyPrisonInputAction.Skill2: return "技能 2";
            case SkyPrisonInputAction.Skill3: return "技能 3";
            case SkyPrisonInputAction.Reload: return "换弹";
            case SkyPrisonInputAction.Inventory: return "背包";
            case SkyPrisonInputAction.Map: return "地图";
            case SkyPrisonInputAction.Menu: return "菜单";
            case SkyPrisonInputAction.CharacterPanel: return "角色面板";
            case SkyPrisonInputAction.QuickItem1: return "快捷物品 1";
            case SkyPrisonInputAction.QuickItem2: return "快捷物品 2";
            case SkyPrisonInputAction.QuickItem3: return "快捷物品 3";
            case SkyPrisonInputAction.QuickItem4: return "快捷物品 4";
            default: return action.ToString();
        }
    }

    private static Texture2D MakeTex(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
