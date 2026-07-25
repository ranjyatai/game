using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonTriggerPage : SkyPrisonEditorPageBase
{
    private const string TriggerRootFolder = "Assets/_Project/Data/Triggers";
    private const float LeftRowHeight = 24f;
    private const float LeftPadding = 8f;
    private const float RuleListWidth = 260f;
    private const float ColumnGap = 8f;

    private readonly List<TriggerPackage> packages = new List<TriggerPackage>();
    private readonly SkyPrisonAILogicPanel logicPanel = new SkyPrisonAILogicPanel(LogicTemplateContext.Trigger);

    private TriggerPackage selectedPackage;
    private SerializedObject selectedSO;
    private int selectedRuleIndex = -1;
    private Vector2 leftScroll;
    private Vector2 ruleScroll;
    private Vector2 detailScroll;
    private string search = "";

    private TriggerPackage editingPackageNameTarget;
    private string editingPackageNameBuffer = "";
    private bool editingFileName = false;
    private string fileNameBuffer = "";
    private static readonly Color TriggerSelectedFillColor = new Color(0.24f, 0.32f, 0.12f, 1f);
    private static readonly Color TriggerSelectedAccentColor = new Color(0.78f, 0.95f, 0.20f, 1f);
    private static readonly Color TriggerRuleSelectedColor = new Color(0.38f, 0.50f, 0.18f, 0.95f);

    public SkyPrisonTriggerPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "触发器";

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = GetPackagePath(selectedPackage);
        packages.Clear();

        string[] guids = AssetDatabase.FindAssets("t:TriggerPackage");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TriggerPackage pkg = AssetDatabase.LoadAssetAtPath<TriggerPackage>(path);
            if (pkg != null)
                packages.Add(pkg);
        }

        packages.Sort((a, b) => string.Compare(GetPackageLabel(a), GetPackageLabel(b), System.StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(selectedPath))
            selectedPackage = packages.FirstOrDefault(x => GetPackagePath(x) == selectedPath);

        if (selectedPackage == null && packages.Count > 0)
            selectedPackage = packages[0];

        selectedSO = selectedPackage != null ? new SerializedObject(selectedPackage) : null;
        ClampSelection();
    }

    public override bool TrySelectObject(Object obj)
    {
        if (obj is not TriggerPackage pkg)
            return false;

        Refresh();

        string targetPath = GetPackagePath(pkg);
        selectedPackage = packages.FirstOrDefault(x => GetPackagePath(x) == targetPath);
        if (selectedPackage == null)
            selectedPackage = pkg;

        selectedSO = new SerializedObject(selectedPackage);
        selectedRuleIndex = -1;
        search = string.Empty;
        FocusSelectedPackageInLeftList();
        ClampSelection();
        return true;
    }

    public override void OnGUILeft()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

        EditorGUILayout.LabelField("触发器包", EditorStyles.boldLabel);
        GUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+", GUILayout.Width(28f), GUILayout.Height(22f)))
            CreatePackage();

        using (new EditorGUI.DisabledScope(selectedPackage == null))
        {
            if (GUILayout.Button("-", GUILayout.Width(28f), GUILayout.Height(22f)))
                DeleteSelectedPackage();
        }

        if (GUILayout.Button("刷新", GUILayout.Width(56f), GUILayout.Height(22f)))
            Refresh();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        search = EditorGUILayout.TextField(search);
        GUILayout.Space(6f);

        Rect listRect = GUILayoutUtility.GetRect(10f, 100000f, 10f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(listRect, new Color(0.13f, 0.13f, 0.14f, 1f));

        Rect viewRect = new Rect(listRect.x + LeftPadding, listRect.y + LeftPadding, listRect.width - LeftPadding * 2f, listRect.height - LeftPadding * 2f);
        List<TriggerPackage> filtered = GetFilteredPackages();
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), Mathf.Max(viewRect.height, filtered.Count * LeftRowHeight));

        _leftListRect = listRect;

        leftScroll = GUI.BeginScrollView(viewRect, leftScroll, contentRect, false, true);
        for (int i = 0; i < filtered.Count; i++)
        {
            TriggerPackage pkg = filtered[i];
            Rect row = new Rect(0f, i * LeftRowHeight, contentRect.width, LeftRowHeight);
            DrawPackageRow(row, pkg);
        }
        GUI.EndScrollView();

        if (selectedPackage != null && Event.current.type == EventType.KeyDown)
            HandleListKeyboard();

        EditorGUILayout.EndVertical();
    }

    private void FocusSelectedPackageInLeftList()
    {
        if (selectedPackage == null)
            return;

        List<TriggerPackage> filtered = GetFilteredPackages();
        int index = filtered.IndexOf(selectedPackage);
        if (index < 0)
            return;

        leftScroll.y = Mathf.Max(0f, index * LeftRowHeight - LeftRowHeight * 2f);
    }

    public override void OnGUIRight()
    {
        if (selectedPackage == null)
        {
            EditorGUILayout.HelpBox("请先创建或选择一个触发器包。", MessageType.Info);
            return;
        }

        if (selectedSO == null || selectedSO.targetObject != selectedPackage)
            selectedSO = new SerializedObject(selectedPackage);

        selectedSO.Update();

        DrawHeader();
        GUILayout.Space(8f);

        EditorGUILayout.BeginHorizontal();
        DrawRuleList(GUILayout.Width(RuleListWidth));
        GUILayout.Space(ColumnGap);
        DrawRuleDetail(GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();

        selectedSO.ApplyModifiedProperties();

        if (GUI.changed)
            EditorUtility.SetDirty(selectedPackage);
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("触发器工作台", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("触发器是作用于当前地图世界的逻辑包。这里使用灵活主语版本：单位槽位可以选择进入者、指定单位、最后创建单位等。", MessageType.Info);

        string assetPath = AssetDatabase.GetAssetPath(selectedPackage);
        string currentFileName = string.IsNullOrEmpty(assetPath) ? selectedPackage.name : Path.GetFileNameWithoutExtension(assetPath);
        DrawFileNameRow(assetPath, currentFileName);

        DrawPropertyRow("触发器 ID", selectedSO.FindProperty("triggerId"));
        DrawPropertyRow("显示名称", selectedSO.FindProperty("displayName"));
        DrawPropertyRow("备注", selectedSO.FindProperty("note"), true);
        DrawPropertyRow("调试日志", selectedSO.FindProperty("debugLogs"));
        EditorGUILayout.EndVertical();
    }

    private void DrawFileNameRow(string assetPath, string currentFileName)
    {
        if (editingFileName && editingPackageNameTarget != null)
            editingFileName = false;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("文件名", GUILayout.Width(EditorGUIUtility.labelWidth));

        if (editingFileName)
        {
            fileNameBuffer = EditorGUILayout.TextField(fileNameBuffer);
            if (GUILayout.Button("确定", GUILayout.Width(48f)))
            {
                string trimmed = fileNameBuffer.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != currentFileName)
                {
                    string error = AssetDatabase.RenameAsset(assetPath, trimmed);
                    if (!string.IsNullOrEmpty(error))
                        EditorUtility.DisplayDialog("重命名失败", error, "确定");
                    else
                        AssetDatabase.SaveAssets();
                }
                editingFileName = false;
            }
            if (GUILayout.Button("取消", GUILayout.Width(48f)))
                editingFileName = false;
        }
        else
        {
            EditorGUILayout.LabelField(currentFileName, EditorStyles.textField);
            if (GUILayout.Button("重命名", GUILayout.Width(56f)))
            {
                editingFileName = true;
                fileNameBuffer = currentFileName;
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRuleList(params GUILayoutOption[] options)
    {
        EditorGUILayout.BeginVertical("box", options);
        EditorGUILayout.LabelField("规则列表", EditorStyles.boldLabel);

        SerializedProperty rules = selectedSO.FindProperty("rules");
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("新建规则", GUILayout.Height(22f)))
        {
            int idx = rules.arraySize;
            rules.InsertArrayElementAtIndex(idx);
            SerializedProperty rule = rules.GetArrayElementAtIndex(idx);
            rule.FindPropertyRelative("enabled").boolValue = true;
            rule.FindPropertyRelative("ruleName").stringValue = "新触发器规则";
            rule.FindPropertyRelative("priority").intValue = 100;
            rule.FindPropertyRelative("cooldownSeconds").floatValue = 0f;
            rule.FindPropertyRelative("requireAllConditions").boolValue = true;
            selectedRuleIndex = idx;
            logicPanel.ResetSelection();
            logicPanel.MarkCacheDirty();
        }

        using (new EditorGUI.DisabledScope(selectedRuleIndex < 0 || selectedRuleIndex >= rules.arraySize))
        {
            if (GUILayout.Button("删除", GUILayout.Width(58f), GUILayout.Height(22f)))
            {
                rules.DeleteArrayElementAtIndex(selectedRuleIndex);
                selectedRuleIndex = Mathf.Clamp(selectedRuleIndex - 1, -1, rules.arraySize - 1);
                logicPanel.ResetSelection();
                logicPanel.MarkCacheDirty();
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        ruleScroll = EditorGUILayout.BeginScrollView(ruleScroll);
        for (int i = 0; i < rules.arraySize; i++)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(i);
            string name = rule.FindPropertyRelative("ruleName").stringValue;
            bool enabled = rule.FindPropertyRelative("enabled").boolValue;
            string label = enabled ? name : $"[禁用] {name}";

            bool active = selectedRuleIndex == i;
            Rect ruleRowRect = EditorGUILayout.GetControlRect(false, 24f);

            if (active)
            {
                EditorGUI.DrawRect(ruleRowRect, TriggerRuleSelectedColor);
                EditorGUI.DrawRect(new Rect(ruleRowRect.x, ruleRowRect.y, 4f, ruleRowRect.height), TriggerSelectedAccentColor);
            }
            else if (ruleRowRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(ruleRowRect, new Color(0.78f, 0.95f, 0.20f, 0.07f));
            }

            GUI.Label(new Rect(ruleRowRect.x + 10f, ruleRowRect.y + 3f, ruleRowRect.width - 20f, 18f), label, EditorStyles.label);

            Event rowEvent = Event.current;
            if (rowEvent.type == EventType.MouseDown && ruleRowRect.Contains(rowEvent.mousePosition) && rowEvent.button == 0)
            {
                if (selectedRuleIndex != i)
                {
                    selectedRuleIndex = i;
                    logicPanel.ResetSelection();
                    logicPanel.MarkCacheDirty();
                }

                rowEvent.Use();
            }
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawRuleDetail(params GUILayoutOption[] options)
    {
        EditorGUILayout.BeginVertical("box", options);
        SerializedProperty rules = selectedSO.FindProperty("rules");
        if (selectedRuleIndex < 0 || selectedRuleIndex >= rules.arraySize)
        {
            EditorGUILayout.HelpBox("请选择或新建一条触发器规则。", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        SerializedProperty rule = rules.GetArrayElementAtIndex(selectedRuleIndex);
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

        EditorGUILayout.LabelField("规则详情", EditorStyles.boldLabel);
        DrawPropertyRow("启用", rule.FindPropertyRelative("enabled"));
        DrawPropertyRow("规则名称", rule.FindPropertyRelative("ruleName"));
        DrawPropertyRow("优先级", rule.FindPropertyRelative("priority"));
        DrawPropertyRow("冷却", rule.FindPropertyRelative("cooldownSeconds"));
        DrawPropertyRow("条件全部满足", rule.FindPropertyRelative("requireAllConditions"));
        DrawPropertyRow("备注", rule.FindPropertyRelative("note"), true);

        GUILayout.Space(8f);
        logicPanel.Draw(rule);

        // 快捷键：Ctrl/Cmd+C 复制、Ctrl/Cmd+V 粘贴、Ctrl/Cmd+X 剪切、Delete/Backspace 删除
        Event e = Event.current;
        if (logicPanel.HandleKeyboardShortcuts(rule, e))
        {
            selectedSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(selectedPackage);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawPackageRow(Rect rect, TriggerPackage pkg)
    {
        bool selected = selectedPackage == pkg;
        bool hover = rect.Contains(Event.current.mousePosition);
        Event e = Event.current;

        if (selected)
        {
            EditorGUI.DrawRect(rect, TriggerSelectedFillColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), TriggerSelectedAccentColor);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(0.78f, 0.95f, 0.20f, 0.08f));
        }

        Rect labelRect = new Rect(rect.x + 10f, rect.y + 2f, rect.width - 14f, rect.height - 4f);

        if (editingPackageNameTarget == pkg)
        {
            GUI.SetNextControlName("TriggerPackageRenameField");
            editingPackageNameBuffer = EditorGUI.TextField(labelRect, editingPackageNameBuffer);

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                CommitTriggerPackageRename();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelTriggerPackageRename();
                e.Use();
            }
            else if (e.type == EventType.MouseDown && !rect.Contains(e.mousePosition))
            {
                CommitTriggerPackageRename();
            }

            return;
        }

        GUI.Label(labelRect, GetPackageLabel(pkg), EditorStyles.label);

        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                if (selectedPackage != pkg)
                {
                    selectedPackage = pkg;
                    selectedSO = new SerializedObject(selectedPackage);
                    selectedRuleIndex = -1;
                    editingFileName = false;
                    ClampSelection();
                }

                if (e.clickCount == 2)
                    BeginTriggerPackageRename(pkg);

                e.Use();
            }
            else if (e.button == 1)
            {
                if (selectedPackage != pkg)
                {
                    selectedPackage = pkg;
                    selectedSO = new SerializedObject(selectedPackage);
                    selectedRuleIndex = -1;
                    editingFileName = false;
                    ClampSelection();
                }
                e.Use();
            }
        }
    }

    private void BeginTriggerPackageRename(TriggerPackage pkg)
    {
        if (pkg == null)
            return;

        editingPackageNameTarget = pkg;
        editingPackageNameBuffer = string.IsNullOrWhiteSpace(pkg.displayName) ? pkg.name : pkg.displayName;
        EditorGUI.FocusTextInControl("TriggerPackageRenameField");
    }

    private void CommitTriggerPackageRename()
    {
        if (editingPackageNameTarget == null)
            return;

        string newName = (editingPackageNameBuffer ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName))
            newName = editingPackageNameTarget.name;

        Undo.RecordObject(editingPackageNameTarget, "Rename Trigger Package Display Name");
        editingPackageNameTarget.displayName = newName;
        EditorUtility.SetDirty(editingPackageNameTarget);
        AssetDatabase.SaveAssets();

        editingPackageNameTarget = null;
        editingPackageNameBuffer = "";
        Refresh();
    }

    private void CancelTriggerPackageRename()
    {
        editingPackageNameTarget = null;
        editingPackageNameBuffer = "";
    }

    private List<TriggerPackage> GetFilteredPackages()
    {
        if (string.IsNullOrWhiteSpace(search))
            return packages;

        string key = search.Trim().ToLower();
        return packages.Where(x => GetPackageLabel(x).ToLower().Contains(key) || x.triggerId.ToLower().Contains(key)).ToList();
    }

    private void CreatePackage()
    {
        EnsureFolderExists(TriggerRootFolder);
        TriggerPackage pkg = ScriptableObject.CreateInstance<TriggerPackage>();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{TriggerRootFolder}/TriggerPackage.asset");
        AssetDatabase.CreateAsset(pkg, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        selectedPackage = pkg;
        selectedSO = new SerializedObject(selectedPackage);
        selectedRuleIndex = -1;
        Refresh();
        selectedPackage = pkg;
        selectedSO = new SerializedObject(selectedPackage);
    }

    private void DeleteSelectedPackage()
    {
        if (selectedPackage == null)
            return;

        string path = GetPackagePath(selectedPackage);
        if (string.IsNullOrWhiteSpace(path))
            return;

        bool ok = EditorUtility.DisplayDialog("删除触发器包", $"确定删除 {GetPackageLabel(selectedPackage)} 吗？", "删除", "取消");
        if (!ok)
            return;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        selectedPackage = null;
        selectedSO = null;
        selectedRuleIndex = -1;
        Refresh();
    }

    private TriggerPackage _clipboard;

    private void CopySelectedPackage()
    {
        _clipboard = selectedPackage;
    }

    private void PastePackage()
    {
        if (_clipboard == null) return;

        EnsureFolderExists(TriggerRootFolder);
        string srcPath = GetPackagePath(_clipboard);
        if (string.IsNullOrEmpty(srcPath)) return;

        string dstPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{TriggerRootFolder}/{Path.GetFileNameWithoutExtension(srcPath)}_Copy.asset");

        if (!AssetDatabase.CopyAsset(srcPath, dstPath))
        {
            Debug.LogWarning($"[TriggerPage] 复制失败：{srcPath}");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Refresh();
        selectedPackage = AssetDatabase.LoadAssetAtPath<TriggerPackage>(dstPath);
        if (selectedPackage != null)
            selectedSO = new SerializedObject(selectedPackage);
    }

    private Rect _leftListRect;

    private void HandleListKeyboard()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        // 有文本框正在编辑时不拦截
        if (EditorGUIUtility.editingTextField) return;
        // 鼠标不在左侧列表区域内时不拦截
        if (!_leftListRect.Contains(e.mousePosition)) return;

        bool cmd = e.command || e.control;

        if (cmd && e.keyCode == KeyCode.C)
        {
            CopySelectedPackage();
            e.Use();
            return;
        }

        if (cmd && e.keyCode == KeyCode.V)
        {
            PastePackage();
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            DeleteSelectedPackage();
            e.Use();
            return;
        }

        // 上下箭头切换选中
        List<TriggerPackage> filtered = GetFilteredPackages();
        if (filtered.Count == 0) return;

        int cur = filtered.IndexOf(selectedPackage);
        int next = cur;

        if (e.keyCode == KeyCode.UpArrow)   next = Mathf.Max(0, cur - 1);
        if (e.keyCode == KeyCode.DownArrow) next = Mathf.Min(filtered.Count - 1, cur + 1);

        if (next != cur && next >= 0)
        {
            selectedPackage = filtered[next];
            selectedSO = new SerializedObject(selectedPackage);
            selectedRuleIndex = -1;
            editingFileName = false;
            ClampSelection();
            FocusSelectedPackageInLeftList();
            e.Use();
        }
    }

    private void ClampSelection()
    {
        if (selectedSO == null)
        {
            selectedRuleIndex = -1;
            return;
        }

        SerializedProperty rules = selectedSO.FindProperty("rules");
        if (rules == null || rules.arraySize <= 0)
            selectedRuleIndex = -1;
        else
            selectedRuleIndex = Mathf.Clamp(selectedRuleIndex, 0, rules.arraySize - 1);
    }

    private static string GetPackagePath(TriggerPackage pkg)
    {
        return pkg == null ? "" : AssetDatabase.GetAssetPath(pkg);
    }

    private static string GetPackageLabel(TriggerPackage pkg)
    {
        if (pkg == null)
            return "-";
        return string.IsNullOrWhiteSpace(pkg.displayName) ? pkg.name : pkg.displayName;
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

    private static void DrawPropertyRow(string label, SerializedProperty prop, bool multiline = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(100f));

        if (prop == null)
        {
            EditorGUILayout.LabelField("字段不存在");
        }
        else if (multiline && prop.propertyType == SerializedPropertyType.String)
        {
            prop.stringValue = EditorGUILayout.TextArea(prop.stringValue, GUILayout.MinHeight(44f));
        }
        else
        {
            EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        }

        EditorGUILayout.EndHorizontal();
    }
}
