using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class StatusPickerWindow : EditorWindow
{
    public class OpenOptions
    {
        public string title = "选择状态";
        public bool accumulationOnly = false;
        public StatusDefinition current;
    }

    private static Action<StatusDefinition> onPicked;
    private static OpenOptions pendingOptions;

    private readonly List<StatusDefinition> statuses = new List<StatusDefinition>();

    private Vector2 scroll;
    private string search = "";
    private StatusDefinition selectedStatus;
    private bool accumulationOnly = false;

    private static readonly Color WindowBg = new Color(64f / 255f, 64f / 255f, 64f / 255f, 1f);
    private static readonly Color ContainerBg = new Color(0.18f, 0.18f, 0.19f, 1f);
    private static readonly Color BorderColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color HoverBg = new Color(1f, 1f, 1f, 0.04f);
    private static readonly Color SelectedBg = new Color(0.30f, 0.20f, 0.10f, 1f);
    private static readonly Color AccentColor = new Color(1.00f, 0.60f, 0.18f, 1f);

    public static void Open(Action<StatusDefinition> onSelect)
    {
        Open(new OpenOptions(), onSelect);
    }

    public static void OpenAccumulationOnly(StatusDefinition current, Action<StatusDefinition> onSelect)
    {
        Open(
            new OpenOptions
            {
                title = "选择累计型状态",
                accumulationOnly = true,
                current = current
            },
            onSelect);
    }

    public static void Open(OpenOptions options, Action<StatusDefinition> onSelect)
    {
        onPicked = onSelect;
        pendingOptions = options ?? new OpenOptions();

        StatusPickerWindow window = CreateInstance<StatusPickerWindow>();
        window.titleContent = new GUIContent(string.IsNullOrWhiteSpace(pendingOptions.title) ? "选择状态" : pendingOptions.title);
        window.minSize = new Vector2(500f, 620f);
        CenterOnMainWindow(window, 500f, 620f);
        window.ShowModalUtility();
        window.Refresh();
    }

    private static void CenterOnMainWindow(EditorWindow window, float width, float height)
    {
        Rect main = GetMainWindowRect();
        float x = main.x + (main.width - width) * 0.5f;
        float y = main.y + (main.height - height) * 0.5f;
        window.position = new Rect(x, y, width, height);
    }

    private static Rect GetMainWindowRect()
    {
        Type containerWinType = typeof(Editor).Assembly.GetType("UnityEditor.ContainerWindow");
        if (containerWinType != null)
        {
            System.Reflection.PropertyInfo showModeProp = containerWinType.GetProperty("showMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Reflection.PropertyInfo positionProp = containerWinType.GetProperty("position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(containerWinType);
            foreach (UnityEngine.Object win in windows)
            {
                if (showModeProp == null || positionProp == null)
                    continue;

                object showModeValue = showModeProp.GetValue(win, null);
                if (showModeValue is int showMode && showMode == 4)
                {
                    object pos = positionProp.GetValue(win, null);
                    if (pos is Rect rect)
                        return rect;
                }
            }
        }

        return new Rect(200f, 120f, 1280f, 720f);
    }

    private void OnEnable()
    {
        if (pendingOptions != null)
        {
            accumulationOnly = pendingOptions.accumulationOnly;
            selectedStatus = pendingOptions.current;
            if (!string.IsNullOrWhiteSpace(pendingOptions.title))
                titleContent = new GUIContent(pendingOptions.title);
        }

        Refresh();
    }

    private void Refresh()
    {
        string[] guids = AssetDatabase.FindAssets("t:StatusDefinition");
        statuses.Clear();
        statuses.AddRange(
            guids.Select(g => AssetDatabase.LoadAssetAtPath<StatusDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                 .Where(x => x != null)
                 .Where(MatchFilter)
                 .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
                 .ThenBy(x => x.statusId)
        );

        if (selectedStatus != null && !statuses.Contains(selectedStatus))
            selectedStatus = null;

        if (selectedStatus == null && statuses.Count > 0)
            selectedStatus = statuses[0];

        Repaint();
    }

    private bool MatchFilter(StatusDefinition status)
    {
        if (status == null)
            return false;

        if (accumulationOnly && status.grantMode != StatusGrantMode.ByAccumulationThreshold)
            return false;

        return true;
    }

    private void OnGUI()
    {
        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), WindowBg);

        Rect fullRect = new Rect(12f, 12f, position.width - 24f, position.height - 24f);
        float y = fullRect.y;

        Rect toolbarRect = new Rect(fullRect.x, y, fullRect.width, 20f);
        y += 26f;

        float helpHeight = accumulationOnly ? 46f : 0f;
        Rect helpRect = new Rect(fullRect.x, y, fullRect.width, helpHeight);
        y += helpHeight > 0f ? helpHeight + 6f : 0f;

        float bottomHeight = 96f;
        Rect bodyRect = new Rect(fullRect.x, y, fullRect.width, Mathf.Max(120f, fullRect.height - (y - fullRect.y) - bottomHeight - 6f));
        Rect bottomRect = new Rect(fullRect.x, bodyRect.yMax + 6f, fullRect.width, bottomHeight);

        DrawToolbar(toolbarRect);

        if (accumulationOnly)
            EditorGUI.HelpBox(helpRect, "这里只显示赋予方式为“累计”的状态，可用于属性/异常满值后的绑定。", MessageType.Info);

        DrawBody(bodyRect);
        DrawBottomButtons(bottomRect);
    }

    private void DrawToolbar(Rect rect)
    {
        const float refreshWidth = 60f;
        const float gap = 6f;

        Rect fieldRect = new Rect(rect.x, rect.y, rect.width - refreshWidth - gap, rect.height);
        Rect refreshRect = new Rect(fieldRect.xMax + gap, rect.y, refreshWidth, rect.height);

        search = EditorGUI.TextField(fieldRect, search ?? "");
        if (GUI.Button(refreshRect, "刷新"))
            Refresh();
    }

    private void DrawBody(Rect rect)
    {
        EditorGUI.DrawRect(rect, ContainerBg);
        DrawThinBorder(rect, BorderColor);

        Rect viewRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
        List<StatusDefinition> filtered = GetFilteredStatuses();

        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * 62f + 4f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        scroll = GUI.BeginScrollView(viewRect, scroll, contentRect, false, true);

        if (filtered.Count == 0)
        {
            GUI.Label(new Rect(8f, 6f, contentRect.width - 16f, 20f), "没有可选择的状态。", EditorStyles.miniLabel);
        }
        else
        {
            for (int i = 0; i < filtered.Count; i++)
            {
                Rect rowRect = new Rect(0f, i * 62f, contentRect.width, 58f);
                DrawStatusRow(rowRect, filtered[i]);
            }
        }

        GUI.EndScrollView();
    }

    private List<StatusDefinition> GetFilteredStatuses()
    {
        IEnumerable<StatusDefinition> filtered = statuses;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                (!string.IsNullOrWhiteSpace(x.displayName) && x.displayName.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrWhiteSpace(x.statusId) && x.statusId.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrWhiteSpace(x.name) && x.name.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrWhiteSpace(x.accumulationSourceKey) && x.accumulationSourceKey.ToLowerInvariant().Contains(q))
            );
        }

        return filtered.ToList();
    }

    private void DrawStatusRow(Rect rowRect, StatusDefinition status)
    {
        bool active = selectedStatus == status;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (active)
        {
            EditorGUI.DrawRect(rowRect, SelectedBg);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), AccentColor);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, HoverBg);
        }

        Rect contentRect = new Rect(rowRect.x + 10f, rowRect.y + 7f, rowRect.width - 20f, rowRect.height - 14f);
        Rect titleRect = new Rect(contentRect.x, contentRect.y, contentRect.width, 18f);
        Rect idRect = new Rect(contentRect.x, titleRect.yMax + 2f, contentRect.width, 16f);
        Rect infoRect = new Rect(contentRect.x, idRect.yMax + 1f, contentRect.width, 16f);

        string title = string.IsNullOrWhiteSpace(status.displayName) ? status.name : status.displayName;
        string id = string.IsNullOrWhiteSpace(status.statusId) ? "-" : status.statusId;
        string grantText = GetGrantModeLabel(status.grantMode);

        if (status.grantMode == StatusGrantMode.ByAccumulationThreshold && !string.IsNullOrWhiteSpace(status.accumulationSourceKey))
            grantText += " / 来源: " + status.accumulationSourceKey;

        GUI.Label(titleRect, title, EditorStyles.boldLabel);
        GUI.Label(idRect, id, EditorStyles.miniLabel);
        GUI.Label(infoRect, grantText, EditorStyles.miniLabel);

        if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
        {
            selectedStatus = status;
            Repaint();
        }

        if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && rowRect.Contains(Event.current.mousePosition))
        {
            selectedStatus = status;
            ConfirmSelection();
            Event.current.Use();
        }
    }

    private void DrawBottomButtons(Rect rect)
    {
        Rect inner = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f);

        string selectedName = selectedStatus == null
            ? "未选择"
            : (string.IsNullOrWhiteSpace(selectedStatus.displayName) ? selectedStatus.name : selectedStatus.displayName);
        string selectedId = selectedStatus == null || string.IsNullOrWhiteSpace(selectedStatus.statusId)
            ? "-"
            : selectedStatus.statusId;

        Rect labelRect = new Rect(inner.x, inner.y, inner.width, 16f);
        Rect nameRect = new Rect(inner.x, labelRect.yMax + 2f, inner.width, 18f);
        Rect idRect = new Rect(inner.x, nameRect.yMax + 1f, inner.width, 16f);
        Rect buttonRect = new Rect(inner.x, rect.yMax - 34f, inner.width, 24f);

        GUI.Label(labelRect, "当前选择", EditorStyles.miniBoldLabel);
        GUI.Label(nameRect, selectedName);
        GUI.Label(idRect, selectedId, EditorStyles.miniLabel);

        const float cancelWidth = 70f;
        const float gap = 6f;
        Rect confirmRect = new Rect(buttonRect.x, buttonRect.y, buttonRect.width - cancelWidth - gap, buttonRect.height);
        Rect cancelRect = new Rect(confirmRect.xMax + gap, buttonRect.y, cancelWidth, buttonRect.height);

        using (new EditorGUI.DisabledScope(selectedStatus == null))
        {
            if (GUI.Button(confirmRect, "确认"))
                ConfirmSelection();
        }

        if (GUI.Button(cancelRect, "取消"))
            Close();
    }

    private void ConfirmSelection()
    {
        onPicked?.Invoke(selectedStatus);
        Close();
        GUIUtility.ExitGUI();
    }

    private string GetGrantModeLabel(StatusGrantMode mode)
    {
        switch (mode)
        {
            case StatusGrantMode.Direct: return "直接赋予";
            case StatusGrantMode.ByAccumulationThreshold: return "累计";
            case StatusGrantMode.PersistentPassive: return "常驻被动";
            case StatusGrantMode.UnlockedByProgression: return "解锁常驻";
            default: return mode.ToString();
        }
    }

    private void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }
}
