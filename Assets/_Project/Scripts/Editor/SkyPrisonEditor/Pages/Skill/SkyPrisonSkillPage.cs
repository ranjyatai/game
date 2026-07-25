using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonSkillPage : SkyPrisonEditorPageBase
{
    public const string SkillRootFolder          = "Assets/_Project/Data/Definitions";
    public const string DefaultSkillCreateFolder = "Assets/_Project/Data/Definitions/Custom/Skills";
    public const string DefaultModuleCreateFolder= "Assets/_Project/Data/Definitions/Custom/ActionModules";

    // 芥末色
    private readonly Color accentMustard      = new Color(0.70f, 0.68f, 0.22f, 1f);
    private readonly Color selectedRowMustard = new Color(0.55f, 0.52f, 0.12f, 0.28f);
    private readonly Color leftTopBg          = new Color(0.13f, 0.13f, 0.14f, 1f);

    private readonly SkyPrisonSkillAssetListPanel        skillListPanel;
    private readonly SkyPrisonSkillInspectorPanel        skillInspectorPanel;
    private readonly SkyPrisonActionModuleListPanel      moduleListPanel;
    private readonly SkyPrisonActionModuleInspectorPanel moduleInspectorPanel;

    // Skill 数据
    private List<SkillDefinition> skillDefinitions = new List<SkillDefinition>();
    private SkillDefinition       selectedSkill;
    private SerializedObject      selectedSkillSO;

    // ActionModule 数据
    private List<WeaponCombatModule> moduleDefinitions = new List<WeaponCombatModule>();
    private WeaponCombatModule       selectedModule;
    private SerializedObject             selectedModuleSO;

    // Tab
    private enum SubTab { Skills, ActionModules }
    private SubTab _tab = SubTab.Skills;

    public SkyPrisonSkillPage(SkyPrisonEditorContext context) : base(context)
    {
        skillListPanel        = new SkyPrisonSkillAssetListPanel(this);
        skillInspectorPanel   = new SkyPrisonSkillInspectorPanel(this);
        moduleListPanel       = new SkyPrisonActionModuleListPanel(this);
        moduleInspectorPanel  = new SkyPrisonActionModuleInspectorPanel(this);
    }

    public override string TabName => "技能";

    // ── 颜色 ─────────────────────────────────────────────────────────────
    public Color AccentMustard      => accentMustard;
    public Color SelectedRowMustard => selectedRowMustard;
    public Color LeftTopBg          => leftTopBg;

    // ── Skill 访问器 ─────────────────────────────────────────────────────
    public List<SkillDefinition> SkillDefinitions => skillDefinitions;
    public SkillDefinition       SelectedSkill     => selectedSkill;
    public SerializedObject      SelectedSO        => selectedSkillSO;

    // ── ActionModule 访问器 ───────────────────────────────────────────────
    public List<WeaponCombatModule> ModuleDefinitions => moduleDefinitions;
    public WeaponCombatModule       SelectedModule     => selectedModule;
    public SerializedObject             SelectedModuleSO   => selectedModuleSO;

    public override void OnEnable() => Refresh();

    public override void Refresh()
    {
        RefreshSkills();
        RefreshModules();
    }

    private void RefreshSkills()
    {
        string prevPath = selectedSkill != null ? AssetDatabase.GetAssetPath(selectedSkill) : "";
        skillDefinitions = AssetDatabase.FindAssets("t:SkillDefinition")
            .Select(g => AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name).ToList();

        if (!string.IsNullOrEmpty(prevPath))
        {
            var match = skillDefinitions.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == prevPath);
            if (match != null) selectedSkill = match;
        }
        if (selectedSkill == null && skillDefinitions.Count > 0)
            SelectSkill(skillDefinitions[0]);

        skillListPanel.OnRefresh();
    }

    private void RefreshModules()
    {
        string prevPath = selectedModule != null ? AssetDatabase.GetAssetPath(selectedModule) : "";
        moduleDefinitions = AssetDatabase.FindAssets("t:WeaponCombatModule")
            .Select(g => AssetDatabase.LoadAssetAtPath<WeaponCombatModule>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name).ToList();

        if (!string.IsNullOrEmpty(prevPath))
        {
            var match = moduleDefinitions.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == prevPath);
            if (match != null) selectedModule = match;
        }
        if (selectedModule == null && moduleDefinitions.Count > 0)
            SelectModule(moduleDefinitions[0]);

        moduleListPanel.OnRefresh();
    }

    // ── 快捷键 ────────────────────────────────────────────────────────────

    public override void HandleGlobalShortcuts()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown) return;
        if (EditorGUIUtility.editingTextField) return;
        if (!string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())) return;

        bool cmd = e.control || e.command;

        if (_tab == SubTab.Skills)
        {
            if (cmd && e.keyCode == KeyCode.C && selectedSkill != null)
            { skillListPanel.CopySkill(selectedSkill); e.Use(); }
            else if (cmd && e.keyCode == KeyCode.X && selectedSkill != null)
            { skillListPanel.CutSkill(selectedSkill); e.Use(); }
            else if (cmd && e.keyCode == KeyCode.V)
            { if (skillListPanel.TryPaste()) e.Use(); }
            else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedSkill != null)
            { if (skillListPanel.DeleteSkill(selectedSkill)) e.Use(); }
        }
        else
        {
            if (cmd && e.keyCode == KeyCode.C && selectedModule != null)
            { moduleListPanel.CopyModule(selectedModule); e.Use(); }
            else if (cmd && e.keyCode == KeyCode.X && selectedModule != null)
            { moduleListPanel.CutModule(selectedModule); e.Use(); }
            else if (cmd && e.keyCode == KeyCode.V)
            { if (moduleListPanel.TryPaste()) e.Use(); }
            else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedModule != null)
            { if (moduleListPanel.DeleteModule(selectedModule)) e.Use(); }
        }
    }

    // ── GUI ──────────────────────────────────────────────────────────────

    public override void OnGUILeft()
    {
        DrawSubTabBar();
        if (_tab == SubTab.Skills)       skillListPanel.Draw();
        else                              moduleListPanel.Draw();
    }

    public override void OnGUIRight()
    {
        if (_tab == SubTab.Skills)
        {
            if (selectedSkill == null) { EditorGUILayout.HelpBox("请在左侧选择一个技能定义。", MessageType.Info); return; }
            EnsureSkillSO();
            selectedSkillSO.Update();
            EditorGUI.BeginChangeCheck();
            skillInspectorPanel.Draw();
            bool skillChanged = EditorGUI.EndChangeCheck();
            if (skillChanged)
            {
                selectedSkillSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(selectedSkill);
                AssetDatabase.SaveAssets();
            }
        }
        else
        {
            if (selectedModule == null) { EditorGUILayout.HelpBox("请在左侧选择一个战斗模组。", MessageType.Info); return; }
            EnsureModuleSO();
            selectedModuleSO.Update();
            EditorGUI.BeginChangeCheck();
            moduleInspectorPanel.Draw();
            bool moduleChanged = EditorGUI.EndChangeCheck();
            if (moduleChanged)
            {
                selectedModuleSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(selectedModule);
                AssetDatabase.SaveAssets();
            }
        }
    }

    public override void HandlePostGUI()
    {
        if (_tab == SubTab.Skills) skillListPanel.HandlePostGUI();
        else                        moduleListPanel.HandlePostGUI();
    }

    private void DrawSubTabBar()
    {
        Rect bar = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
        float half = bar.width * 0.5f;

        DrawTabButton(new Rect(bar.x,        bar.y, half - 1f, bar.height), "技能",   SubTab.Skills);
        DrawTabButton(new Rect(bar.x + half, bar.y, half,      bar.height), "战斗模组", SubTab.ActionModules);

        EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), new Color(1,1,1,0.08f));
        GUILayout.Space(6);
    }

    private void DrawTabButton(Rect r, string label, SubTab target)
    {
        bool active = _tab == target;
        Color bg = active ? new Color(0.70f, 0.68f, 0.22f, 0.18f) : new Color(1,1,1,0.03f);
        EditorGUI.DrawRect(r, bg);

        if (active)
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), accentMustard);

        var style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
            normal    = { textColor = active ? accentMustard : new Color(0.80f, 0.80f, 0.80f) }
        };
        GUI.Label(r, label, style);

        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            _tab = target;
            GUI.FocusControl(null);
            Context.RightScroll = Vector2.zero;
            Context.Repaint();
            Event.current.Use();
        }
    }

    // ── 选择 ─────────────────────────────────────────────────────────────

    public void SelectSkill(SkillDefinition skill)
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;
        FlushSkillSO();
        selectedSkill = skill;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void ClearSkillSelection()
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;
        FlushSkillSO();
        selectedSkill = null;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void SelectModule(WeaponCombatModule module)
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;
        FlushModuleSO();
        selectedModule = module;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void ClearModuleSelection()
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;
        FlushModuleSO();
        selectedModule = null;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    // ── 内部工具 ─────────────────────────────────────────────────────────

    public void EnsureSkillSO()
    {
        if (selectedSkill == null) return;
        if (selectedSkillSO == null || selectedSkillSO.targetObject != selectedSkill)
        { GUI.FocusControl(null); EditorGUIUtility.editingTextField = false; selectedSkillSO = new SerializedObject(selectedSkill); }
    }

    public void EnsureModuleSO()
    {
        if (selectedModule == null) return;
        if (selectedModuleSO == null || selectedModuleSO.targetObject != selectedModule)
        { GUI.FocusControl(null); EditorGUIUtility.editingTextField = false; selectedModuleSO = new SerializedObject(selectedModule); }
    }

    private void FlushSkillSO()  { if (selectedSkillSO  != null) { selectedSkillSO.ApplyModifiedProperties();  selectedSkillSO  = null; } }
    private void FlushModuleSO() { if (selectedModuleSO != null) { selectedModuleSO.ApplyModifiedProperties(); selectedModuleSO = null; } }

    // ── 共用 helpers ─────────────────────────────────────────────────────

    public void DrawThinBorder(Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
    }

    public GUIStyle CenteredToolbarStyle() => new GUIStyle(EditorStyles.boldLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        fontSize  = 11,
        normal    = { textColor = new Color(0.92f, 0.92f, 0.94f) }
    };

    public string GenerateUniqueSkillKey(string baseKey)
    {
        string safe = Sanitize(string.IsNullOrWhiteSpace(baseKey) ? "skill" : baseKey);
        if (string.IsNullOrWhiteSpace(safe)) safe = "skill";
        var ex = new HashSet<string>(skillDefinitions.Where(x => x != null).Select(x => x.skillKey));
        if (!ex.Contains(safe)) return safe;
        int i = 1; while (true) { string c = $"{safe}_{i:000}"; if (!ex.Contains(c)) return c; i++; }
    }

    public string GenerateUniqueModuleKey(string baseKey)
    {
        string safe = Sanitize(string.IsNullOrWhiteSpace(baseKey) ? "module" : baseKey);
        if (string.IsNullOrWhiteSpace(safe)) safe = "module";
        var ex = new HashSet<string>(moduleDefinitions.Where(x => x != null).Select(x => x.moduleKey));
        if (!ex.Contains(safe)) return safe;
        int i = 1; while (true) { string c = $"{safe}_{i:000}"; if (!ex.Contains(c)) return c; i++; }
    }

    public string Sanitize(string v)
    {
        string s = (v ?? "").Trim().ToLower().Replace(" ", "_");
        return new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
    }

    public void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        string[] parts = folderPath.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
