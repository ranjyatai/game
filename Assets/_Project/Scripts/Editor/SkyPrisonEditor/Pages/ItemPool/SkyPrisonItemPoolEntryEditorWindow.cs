using System;
using UnityEditor;
using UnityEngine;

public class SkyPrisonItemPoolEntryEditorWindow : EditorWindow
{
    [Serializable]
    public class EntryEditModel
    {
        public bool enabled = true;
        public UnityEngine.Object itemDefinition;
        public float dropChance = 100f;
        public float weight = 1f;
        public int minCount = 1;
        public int maxCount = 1;
        public string note = "";
    }

    private const string PrefKeyX = "SkyPrison.ItemPoolEntryEditorWindow.X";
    private const string PrefKeyY = "SkyPrison.ItemPoolEntryEditorWindow.Y";
    private const string PrefKeyHasRect = "SkyPrison.ItemPoolEntryEditorWindow.HasRect";

    private const float FixedWindowWidth = 520f;
    private const float FixedWindowHeight = 460f;

    private readonly Color accentGreen = new Color(0.42f, 0.82f, 0.52f, 1f);
    private readonly Color subtleGreen = new Color(0.28f, 0.62f, 0.36f, 0.12f);

    private EntryEditModel model;
    private DropPoolMode poolMode;
    private Func<EntryEditModel, string> extraValidator;
    private Action<EntryEditModel> onConfirm;

    private Vector2 scroll;
    private string validationMessage = "";
    private bool shouldFocus = true;

    public static void OpenCreate(DropPoolMode poolMode, Func<EntryEditModel, string> extraValidator, Action<EntryEditModel> onConfirm)
    {
        EntryEditModel model = new EntryEditModel();
        if (poolMode == DropPoolMode.IndependentRolls)
            model.dropChance = 100f;
        else
            model.weight = 1f;

        OpenInternal("新增物品条目", model, poolMode, extraValidator, onConfirm);
    }

    public static void OpenEdit(EntryEditModel source, DropPoolMode poolMode, Func<EntryEditModel, string> extraValidator, Action<EntryEditModel> onConfirm)
    {
        EntryEditModel copy = new EntryEditModel
        {
            enabled = source.enabled,
            itemDefinition = source.itemDefinition,
            dropChance = source.dropChance,
            weight = source.weight,
            minCount = source.minCount,
            maxCount = source.maxCount,
            note = source.note
        };

        OpenInternal("编辑物品条目", copy, poolMode, extraValidator, onConfirm);
    }

    private static void OpenInternal(string title, EntryEditModel model, DropPoolMode poolMode, Func<EntryEditModel, string> extraValidator, Action<EntryEditModel> onConfirm)
    {
        SkyPrisonItemPoolEntryEditorWindow window = CreateInstance<SkyPrisonItemPoolEntryEditorWindow>();
        window.titleContent = new GUIContent(title);
        window.model = model;
        window.poolMode = poolMode;
        window.extraValidator = extraValidator;
        window.onConfirm = onConfirm;

        window.minSize = new Vector2(FixedWindowWidth, FixedWindowHeight);
        window.maxSize = new Vector2(FixedWindowWidth, FixedWindowHeight);
        window.position = window.GetInitialRect();
        window.ShowUtility();
    }

    private Rect GetInitialRect()
    {
        if (EditorPrefs.GetBool(PrefKeyHasRect, false))
        {
            return new Rect(
                EditorPrefs.GetFloat(PrefKeyX, 200f),
                EditorPrefs.GetFloat(PrefKeyY, 120f),
                FixedWindowWidth,
                FixedWindowHeight
            );
        }

        Rect main = EditorGUIUtility.GetMainWindowPosition();
        return new Rect(
            main.x + (main.width - FixedWindowWidth) * 0.5f,
            main.y + (main.height - FixedWindowHeight) * 0.5f,
            FixedWindowWidth,
            FixedWindowHeight
        );
    }

    private void OnDisable()
    {
        EditorPrefs.SetBool(PrefKeyHasRect, true);
        EditorPrefs.SetFloat(PrefKeyX, position.x);
        EditorPrefs.SetFloat(PrefKeyY, position.y);
    }

    private void OnGUI()
    {
        HandleEscClose();

        if (model == null)
        {
            EditorGUILayout.HelpBox("编辑数据为空。", MessageType.Warning);
            return;
        }

        DrawWorkspaceHeader();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();

        DrawItemPreviewPanel();

        GUILayout.Space(12f);

        EditorGUILayout.BeginVertical();

        model.enabled = EditorGUILayout.Toggle("启用", model.enabled);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("物品定义");
        string btnLabel = model.itemDefinition != null ? model.itemDefinition.name : "（点击选择物品）";
        if (GUILayout.Button(btnLabel, EditorStyles.objectField))
            SkyPrisonItemPickerPopup.Open(model.itemDefinition, picked => { model.itemDefinition = picked; Repaint(); }, "ItemDefinition");
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.TextField("名称预览", GetItemDisplayName(model.itemDefinition));

        GUILayout.Space(6f);

        if (poolMode == DropPoolMode.IndependentRolls)
        {
            model.dropChance = EditorGUILayout.FloatField("掉落率", model.dropChance);
            model.dropChance = DrawPlainSlider(model.dropChance, 0f, 100f);
        }
        else
        {
            model.weight = EditorGUILayout.FloatField("抽选权重", model.weight);
            model.weight = DrawPlainSlider(model.weight, 0f, 100f);
        }

        model.minCount = EditorGUILayout.IntField("最小数量", model.minCount);
        model.maxCount = EditorGUILayout.IntField("最大数量", model.maxCount);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("备注");
        model.note = EditorGUILayout.TextArea(model.note ?? "", GUILayout.MinHeight(92f));

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            GUILayout.Space(8f);
            EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
        }

        EditorGUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        DrawBottomButtons();

        if (shouldFocus)
        {
            shouldFocus = false;
            Focus();
        }
    }

    private void DrawWorkspaceHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("物品条目编辑", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(titleContent.text, EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(4f);

        string modeText = poolMode == DropPoolMode.IndependentRolls ? "独立掉落" : "权重抽取";
        EditorGUILayout.LabelField($"当前模式：{modeText}", EditorStyles.miniLabel);

        Rect lineRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(lineRect, subtleGreen);

        EditorGUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    private void HandleEscClose()
    {
        Event e = Event.current;
        if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            Close();
            e.Use();
        }
    }

    private void DrawItemPreviewPanel()
    {
        const float panelWidth = 150f;
        const float iconOffsetX = 14f;
        const float textOffsetX = 8f;

        EditorGUILayout.BeginVertical("box", GUILayout.Width(panelWidth));

        GUILayout.Label("物品预览", EditorStyles.miniBoldLabel);

        Rect layoutRect = GUILayoutUtility.GetRect(110f, 96f, GUILayout.Width(panelWidth - 8f), GUILayout.Height(96f));
        Rect previewRect = new Rect(layoutRect.x + iconOffsetX, layoutRect.y, 96f, 96f);

        EditorGUI.DrawRect(previewRect, new Color(1f, 1f, 1f, 0.05f));

        Texture2D icon = GetItemIconTexture(model.itemDefinition);
        if (icon != null)
            GUI.DrawTexture(previewRect, icon, ScaleMode.ScaleToFit, true);

        GUILayout.Space(6f);

        Rect nameRect = GUILayoutUtility.GetRect(panelWidth, 18f, GUILayout.Width(panelWidth));
        nameRect.x += textOffsetX;
        nameRect.width -= textOffsetX;
        EditorGUI.LabelField(nameRect, GetItemDisplayName(model.itemDefinition), EditorStyles.miniBoldLabel);

        EditorGUILayout.EndVertical();
    }

    private float DrawPlainSlider(float value, float min, float max)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        rect.xMin += EditorGUIUtility.labelWidth;
        return GUI.HorizontalSlider(rect, value, min, max);
    }

    private void DrawBottomButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("取消", GUILayout.Height(30f)))
        {
            Close();
            return;
        }

        Color oldBg = GUI.backgroundColor;
        GUI.backgroundColor = accentGreen;

        if (GUILayout.Button("确定", GUILayout.Height(30f)))
        {
            if (ValidateModel(out string error))
            {
                onConfirm?.Invoke(CloneModel(model));
                Close();
            }
            else
            {
                validationMessage = error;
            }
        }

        GUI.backgroundColor = oldBg;

        EditorGUILayout.EndHorizontal();
        GUILayout.Space(6f);
    }

    private bool ValidateModel(out string error)
    {
        error = "";

        if (model == null)
        {
            error = "数据为空。";
            return false;
        }

        if (model.itemDefinition == null)
        {
            error = "请选择物品定义。";
            return false;
        }

        if (poolMode == DropPoolMode.IndependentRolls && model.dropChance < 0f)
        {
            error = "掉落率不能小于 0。";
            return false;
        }

        if (poolMode == DropPoolMode.WeightedPick && model.weight < 0f)
        {
            error = "抽选权重不能小于 0。";
            return false;
        }

        if (model.minCount < 0 || model.maxCount < 0)
        {
            error = "数量不能为负数。";
            return false;
        }

        if (model.minCount > model.maxCount)
        {
            error = "最小数量不能大于最大数量。";
            return false;
        }

        if (extraValidator != null)
        {
            string extra = extraValidator(model);
            if (!string.IsNullOrWhiteSpace(extra))
            {
                error = extra;
                return false;
            }
        }

        return true;
    }

    private EntryEditModel CloneModel(EntryEditModel src)
    {
        return new EntryEditModel
        {
            enabled = src.enabled,
            itemDefinition = src.itemDefinition,
            dropChance = src.dropChance,
            weight = src.weight,
            minCount = src.minCount,
            maxCount = src.maxCount,
            note = src.note
        };
    }

    private string GetItemDisplayName(UnityEngine.Object itemObj)
    {
        if (itemObj == null)
            return "未绑定物品";

        try
        {
            SerializedObject itemSO = new SerializedObject(itemObj);
            SerializedProperty displayNameProp = itemSO.FindProperty("displayName");
            if (displayNameProp != null && !string.IsNullOrWhiteSpace(displayNameProp.stringValue))
                return displayNameProp.stringValue;
        }
        catch
        {
        }

        return itemObj.name;
    }

    private Texture2D GetItemIconTexture(UnityEngine.Object itemObj)
    {
        if (itemObj == null)
            return null;

        try
        {
            SerializedObject itemSO = new SerializedObject(itemObj);
            SerializedProperty iconProp = itemSO.FindProperty("icon");
            if (iconProp != null && iconProp.objectReferenceValue != null)
            {
                if (iconProp.objectReferenceValue is Sprite sprite && sprite != null)
                    return sprite.texture;

                if (iconProp.objectReferenceValue is Texture2D tex && tex != null)
                    return tex;
            }
        }
        catch
        {
        }

        return AssetPreview.GetAssetPreview(itemObj) ?? AssetPreview.GetMiniThumbnail(itemObj);
    }
}