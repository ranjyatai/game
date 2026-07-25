using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;

public class UnitUIStyleEditorWindow : EditorWindow
{
    private const float LeftPanelWidth = 500f;
    private const string DefaultSaveFolder = "Assets/_Project/UIUX/HUD/OverheadBarStyles";

    private OverheadBarStyleAsset selectedStyle;
    private Vector2 leftScroll;
    private Vector2 rightScroll;

    private float previewCurrentPercent = 1f;
    private float previewTargetPercent = 1f;
    private float previewDamageReferencePercent = 1f;
    private float previewDamageReferenceHoldTimer = 0f;
    private float previewChangePercent = 0.1f;

    private bool previewLoopDamage;
    private bool previewLoopHeal;
    private int previewStatusCount = 8;

    private string assetFileName = "";
    private int selectedBgLayerIndex = 0;

    private void NotifyRealtimeStylePreview()
    {
        if (selectedStyle == null)
            return;

        EditorUtility.SetDirty(selectedStyle);
        OverheadBarStyleAsset.NotifyStyleChanged(selectedStyle);
        Repaint();
        SceneView.RepaintAll();
    }

    public static void Open(OverheadBarStyleAsset style = null)
    {
        UnitUIStyleEditorWindow window = GetWindow<UnitUIStyleEditorWindow>("单位UI样式编辑器");
        window.minSize = new Vector2(1240f, 780f);
        window.selectedStyle = style;
        window.SyncAssetFileName();
        window.Repaint();
    }

    [MenuItem("Tools/Sky Prison/Debug/单位UI样式编辑器")]
    private static void OpenFromMenu()
    {
        Open();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        SyncAssetFileName();
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        float dt = 1f / 60f;

        if (previewLoopDamage)
        {
            ApplyDamage(previewChangePercent * dt);
            if (previewTargetPercent <= 0f)
                previewLoopDamage = false;
        }

        if (previewLoopHeal)
        {
            ApplyHeal(previewChangePercent * dt);
            if (previewTargetPercent >= 1f)
                previewLoopHeal = false;
        }

        float fillSpeed = selectedStyle != null ? Mathf.Max(0.01f, selectedStyle.fillLerpSpeed) : 5f;
        previewCurrentPercent = Mathf.MoveTowards(previewCurrentPercent, previewTargetPercent, fillSpeed * dt);

        if (previewDamageReferenceHoldTimer > 0f)
        {
            previewDamageReferenceHoldTimer -= dt;
        }
        else
        {
            float refFadeSpeed = selectedStyle != null ? Mathf.Max(0.01f, selectedStyle.damageReferenceFadeSpeed) : 2.5f;
            previewDamageReferencePercent = Mathf.MoveTowards(previewDamageReferencePercent, previewCurrentPercent, refFadeSpeed * dt);
        }

        Repaint();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawPreviewPanel();
            DrawRightPanel();
        }
    }

    private void DrawPreviewPanel()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftPanelWidth)))
        {
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("左侧只看效果。右侧负责样式参数与调试控制。", MessageType.None);

            Rect previewRect = GUILayoutUtility.GetRect(LeftPanelWidth - 28f, 620f, GUILayout.ExpandWidth(true));
            GUI.Box(previewRect, GUIContent.none);
            DrawPreviewCanvas(previewRect);

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawRightPanel()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

            EditorGUILayout.Space(8f);
            DrawResourceSection();

            EditorGUILayout.Space(10f);

            if (selectedStyle == null)
            {
                EditorGUILayout.HelpBox("请先选择一个 OverheadBarStyleAsset。", MessageType.Info);
            }
            else
            {
                SerializedObject so = new SerializedObject(selectedStyle);
                so.Update();

                EditorGUI.BeginChangeCheck();

                DrawBaseSection(so);
                DrawNameSection(so);
                DrawBackgroundLayerSection(so);
                DrawBarSection(so);
                DrawSizeSection(so);
                DrawStatusAreaSection(so);
                DrawDamageNumberSection(so);
                DrawDisplaySection(so);
                DrawDynamicSection(so);

                bool changed = EditorGUI.EndChangeCheck();
                so.ApplyModifiedProperties();

                if (changed)
                    NotifyRealtimeStylePreview();
            }

            EditorGUILayout.Space(12f);
            DrawDebugControls();

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawResourceSection()
    {
        DrawBoxSection("样式资源", () =>
        {
            EditorGUI.BeginChangeCheck();
            selectedStyle = (OverheadBarStyleAsset)EditorGUILayout.ObjectField("样式包", selectedStyle, typeof(OverheadBarStyleAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                SyncAssetFileName();
                NotifyRealtimeStylePreview();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新建样式包", GUILayout.Height(24f)))
                    CreateStyleAsset();

                if (GUILayout.Button("读取当前选择", GUILayout.Height(24f)))
                    ReadFromSelection();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开当前资源", GUILayout.Height(24f)))
                    PingSelectedStyle();

                if (GUILayout.Button("打开所在文件夹", GUILayout.Height(24f)))
                    RevealStyleFolder();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("删除当前文件", GUILayout.Height(24f)))
                    DeleteCurrentStyleWithConfirm();
            }

            EditorGUILayout.Space(4f);
            DrawTextFieldRow("文件名", ref assetFileName);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(120f);
                if (GUILayout.Button("应用文件名", GUILayout.Height(24f)))
                    ApplyAssetFileName();
            }
        });
    }

    private void DrawBaseSection(SerializedObject so)
    {
        DrawBoxSection("基础", () =>
        {
            DrawPropertyRow(so.FindProperty("styleKey"), "样式Key");
            DrawPropertyRow(so.FindProperty("displayName"), "显示名称");
        });
    }

    private void DrawNameSection(SerializedObject so)
    {
        DrawBoxSection("名字", () =>
        {
            DrawPropertyRow(so.FindProperty("nameFontAsset"), "字体资源");
            DrawPropertyRow(so.FindProperty("nameFontSize"), "字号");
            DrawPropertyRow(so.FindProperty("nameColor"), "文字颜色");
        });
    }

    private void DrawBackgroundLayerSection(SerializedObject so)
    {
        SerializedProperty layersProp = so.FindProperty("backgroundLayers");

        DrawBoxSection("BG图层", () =>
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 添加BG图层", GUILayout.Height(24f)))
                {
                    int newIndex = layersProp.arraySize;
                    layersProp.InsertArrayElementAtIndex(newIndex);
                    SerializedProperty newLayer = layersProp.GetArrayElementAtIndex(newIndex);
                    newLayer.FindPropertyRelative("layerName").stringValue = $"BG Layer {newIndex + 1}";
                    newLayer.FindPropertyRelative("useTint").boolValue = true;
                    newLayer.FindPropertyRelative("tint").colorValue = Color.white;
                    newLayer.FindPropertyRelative("offset").vector2Value = Vector2.zero;
                    newLayer.FindPropertyRelative("sizeOverride").vector2Value = Vector2.zero;
                    newLayer.FindPropertyRelative("rotationZ").floatValue = 0f;
                    selectedBgLayerIndex = newIndex;
                }

                using (new EditorGUI.DisabledScope(layersProp.arraySize == 0))
                {
                    if (GUILayout.Button("- 删除BG图层", GUILayout.Height(24f)))
                    {
                        if (selectedBgLayerIndex >= 0 && selectedBgLayerIndex < layersProp.arraySize)
                        {
                            layersProp.DeleteArrayElementAtIndex(selectedBgLayerIndex);
                            selectedBgLayerIndex = Mathf.Clamp(selectedBgLayerIndex - 1, 0, Mathf.Max(0, layersProp.arraySize - 1));
                        }
                    }
                }
            }

            if (layersProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("当前没有 BG 图层。至少建议保留 1 层底板。", MessageType.Info);
                return;
            }

            string[] names = new string[layersProp.arraySize];
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                SerializedProperty layer = layersProp.GetArrayElementAtIndex(i);
                string layerName = layer.FindPropertyRelative("layerName").stringValue;
                names[i] = string.IsNullOrWhiteSpace(layerName) ? $"BG Layer {i + 1}" : layerName;
            }

            selectedBgLayerIndex = Mathf.Clamp(selectedBgLayerIndex, 0, layersProp.arraySize - 1);
            selectedBgLayerIndex = EditorGUILayout.Popup("当前图层", selectedBgLayerIndex, names);

            SerializedProperty currentLayer = layersProp.GetArrayElementAtIndex(selectedBgLayerIndex);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                DrawSubPropertyRow(currentLayer.FindPropertyRelative("layerName"), "图层名称");
                DrawSubPropertyRow(currentLayer.FindPropertyRelative("texture"), "图层图片");
                DrawSubPropertyRow(currentLayer.FindPropertyRelative("useTint"), "启用染色");
                using (new EditorGUI.DisabledScope(!currentLayer.FindPropertyRelative("useTint").boolValue))
                {
                    DrawSubPropertyRow(currentLayer.FindPropertyRelative("tint"), "图层颜色");
                }
                DrawSubPropertyRow(currentLayer.FindPropertyRelative("offset"), "图层偏移");
                DrawSubPropertyRow(currentLayer.FindPropertyRelative("sizeOverride"), "尺寸覆盖");
                DrawSubPropertyRow(currentLayer.FindPropertyRelative("rotationZ"), "图层旋转");
            }
        });
    }

    private void DrawBarSection(SerializedObject so)
    {
        DrawBoxSection("条体", () =>
        {
            DrawPropertyRow(so.FindProperty("fillTexture"), "填充图");
            DrawPropertyRow(so.FindProperty("damageReferenceTexture"), "扣血参考图");
            DrawPropertyRow(so.FindProperty("fillColor"), "填充颜色");
            DrawPropertyRow(so.FindProperty("damageReferenceColor"), "扣血参考颜色");
        });
    }

    private void DrawSizeSection(SerializedObject so)
    {
        DrawBoxSection("尺寸", () =>
        {
            DrawPropertyRow(so.FindProperty("barSize"), "条尺寸");
            DrawPropertyRow(so.FindProperty("hpBarOffset"), "条偏移");
            DrawPropertyRow(so.FindProperty("nameOffset"), "名字偏移");
            DrawPropertyRow(so.FindProperty("rotationZ"), "整体旋转");
            DrawPropertyRow(so.FindProperty("paddingLeft"), "左边距");
            DrawPropertyRow(so.FindProperty("paddingRight"), "右边距");
            DrawPropertyRow(so.FindProperty("paddingTop"), "上边距");
            DrawPropertyRow(so.FindProperty("paddingBottom"), "下边距");
        });
    }

    private void DrawStatusAreaSection(SerializedObject so)
    {
        DrawBoxSection("状态区域（容器）", () =>
        {
            DrawPropertyRow(so.FindProperty("enableStatusArea"), "启用状态区域");
            DrawPropertyRow(so.FindProperty("statusDisplayKey"), "状态显示Key");
            DrawPropertyRow(so.FindProperty("statusAreaOffset"), "区域偏移");
            DrawPropertyRow(so.FindProperty("statusAreaSize"), "区域尺寸");
            DrawPropertyRow(so.FindProperty("statusIconSize"), "图标尺寸");
            DrawPropertyRow(so.FindProperty("statusIconSpacing"), "图标间距");
            DrawPropertyRow(so.FindProperty("statusAreaAlignment"), "对齐方式");

            if (selectedStyle != null && selectedStyle.enableStatusArea)
            {
                int perLine = CalculateStatusPerLine(selectedStyle.statusAreaSize, selectedStyle.statusIconSize, selectedStyle.statusIconSpacing);
                int maxRows = CalculateStatusMaxRows(selectedStyle.statusAreaSize, selectedStyle.statusIconSize, selectedStyle.statusIconSpacing);
                int maxVisible = perLine * maxRows;
                EditorGUILayout.HelpBox($"当前单行容量(自动推导): {perLine}    最多可见: {maxVisible}", MessageType.None);
            }
        });
    }


    private void DrawDamageNumberSection(SerializedObject so)
    {
        DrawBoxSection("伤害跳字", () =>
        {
            SerializedProperty enableProp = so.FindProperty("enableDamageNumbers");
            SerializedProperty fontProp = so.FindProperty("damageNumberFontAsset");

            DrawPropertyRow(enableProp, "启用跳字");

            using (new EditorGUI.DisabledScope(enableProp == null || !enableProp.boolValue))
            {
                DrawDamageNumberFontPicker(fontProp);
            }

            EditorGUILayout.HelpBox("这里只预留总开关和字体。具体跳字动画、位置与节奏先统一走运行时默认规则，后面再按需要扩展。", MessageType.None);
        });
    }

    private void DrawDamageNumberFontPicker(SerializedProperty fontProp)
    {
        if (fontProp == null)
            return;

        TMP_FontAsset[] fonts = FindAllTmpFonts();
        string[] options = new string[fonts.Length + 1];
        options[0] = "(未指定)";
        int selectedIndex = 0;

        TMP_FontAsset current = fontProp.objectReferenceValue as TMP_FontAsset;
        for (int i = 0; i < fonts.Length; i++)
        {
            string path = AssetDatabase.GetAssetPath(fonts[i]);
            options[i + 1] = string.IsNullOrEmpty(path)
                ? fonts[i].name
                : $"{fonts[i].name}   [{path}]";

            if (fonts[i] == current)
                selectedIndex = i + 1;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("跳字字体", GUILayout.Width(120f));
            int nextIndex = EditorGUILayout.Popup(selectedIndex, options);
            if (nextIndex != selectedIndex)
            {
                fontProp.objectReferenceValue = nextIndex <= 0 ? null : fonts[nextIndex - 1];
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("字体资源对象", GUILayout.Width(120f));
            EditorGUILayout.ObjectField(current, typeof(TMP_FontAsset), false);
        }

        EditorGUILayout.Space(4f);
        DrawDamageNumberFontInfo(current, fonts.Length);
    }

    private TMP_FontAsset[] FindAllTmpFonts()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
        System.Collections.Generic.List<TMP_FontAsset> result = new System.Collections.Generic.List<TMP_FontAsset>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                continue;

            if (!path.StartsWith("Assets/_Project/UIUX/Fonts")
                && !path.StartsWith("Assets/_Project"))
                continue;

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null)
                result.Add(font);
        }

        result.Sort((a, b) =>
        {
            string pa = AssetDatabase.GetAssetPath(a);
            string pb = AssetDatabase.GetAssetPath(b);
            int cmp = string.Compare(pa, pb, System.StringComparison.OrdinalIgnoreCase);
            if (cmp != 0)
                return cmp;
            return string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
        });

        return result.ToArray();
    }

    private void DrawDamageNumberFontInfo(TMP_FontAsset font, int scannedCount)
    {
        EditorGUILayout.LabelField("字体预览", EditorStyles.boldLabel);

        Rect outerRect = EditorGUILayout.GetControlRect(false, 150f);
        GUI.Box(outerRect, GUIContent.none);

        Rect atlasRect = new Rect(outerRect.x + 10f, outerRect.y + 10f, 96f, 96f);
        Rect infoRect = new Rect(
            atlasRect.xMax + 12f,
            outerRect.y + 10f,
            outerRect.width - atlasRect.width - 32f,
            96f);

        Texture atlas = null;
        string path = "-";
        string fontName = "(未指定)";
        string digits = "0123456789+-";

        if (font != null)
        {
            if (font.atlasTextures != null && font.atlasTextures.Length > 0)
                atlas = font.atlasTextures[0];
            path = AssetDatabase.GetAssetPath(font);
            fontName = font.name;
        }

        if (atlas != null)
            GUI.DrawTexture(atlasRect, atlas, ScaleMode.ScaleToFit, false);
        else
            EditorGUI.DrawRect(atlasRect, new Color(0f, 0f, 0f, 0.35f));

        GUI.Label(
            infoRect,
            $"名称: {fontName}\n路径: {path}\n\n数字字符: {digits}\n已扫描到 TMP 字体资源: {scannedCount} 个",
            EditorStyles.label);

        Rect noteRect = new Rect(
            outerRect.x + 10f,
            outerRect.y + 112f,
            outerRect.width - 20f,
            28f);

        EditorGUI.HelpBox(
            noteRect,
            "这里仅显示字体资源信息与图集缩略图。数字动态预览已移除，避免与说明重叠。",
            MessageType.None);
    }

    private void DrawDisplaySection(SerializedObject so)
    {
        DrawBoxSection("显示规则", () =>
        {
            DrawPropertyRow(so.FindProperty("hideWhenFull"), "满血隐藏");
            DrawPropertyRow(so.FindProperty("fullHpThreshold"), "满血阈值");
            DrawPropertyRow(so.FindProperty("fadeSpeed"), "淡入淡出速度");
        });
    }

    private void DrawDynamicSection(SerializedObject so)
    {
        DrawBoxSection("动态", () =>
        {
            DrawPropertyRow(so.FindProperty("fillLerpSpeed"), "主条变化速度");
            DrawPropertyRow(so.FindProperty("damageReferenceHoldTime"), "扣血参考停留时间");
            DrawPropertyRow(so.FindProperty("damageReferenceFadeSpeed"), "扣血参考追赶速度");
        });
    }

    private void DrawBoxSection(string title, System.Action drawContent)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);
            drawContent?.Invoke();
        }

        EditorGUILayout.Space(6f);
    }

    private void DrawPropertyRow(SerializedProperty property, string label)
    {
        if (property == null)
            return;

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(120f));
            EditorGUILayout.PropertyField(property, GUIContent.none, true);
        }
    }

    private void DrawSubPropertyRow(SerializedProperty property, string label)
    {
        if (property == null)
            return;

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(110f));
            EditorGUILayout.PropertyField(property, GUIContent.none, true);
        }
    }

    private void DrawTextFieldRow(string label, ref string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(120f));
            value = EditorGUILayout.TextField(value);
        }
    }

    private void DrawDebugControls()
    {
        DrawBoxSection("调试预览", () =>
        {
            previewCurrentPercent = EditorGUILayout.Slider("当前百分比", previewTargetPercent, 0f, 1f);
            previewTargetPercent = previewCurrentPercent;
            previewDamageReferencePercent = Mathf.Max(previewDamageReferencePercent, previewCurrentPercent);

            previewChangePercent = EditorGUILayout.Slider("变化百分比", previewChangePercent, 0.01f, 1f);

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("扣除", GUILayout.Height(28f)))
                    ApplyDamage(previewChangePercent);

                if (GUILayout.Button("增加", GUILayout.Height(28f)))
                    ApplyHeal(previewChangePercent);
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("回满", GUILayout.Height(24f)))
                {
                    previewTargetPercent = 1f;
                    previewLoopDamage = false;
                    previewLoopHeal = false;
                }

                if (GUILayout.Button("归零", GUILayout.Height(24f)))
                {
                    ApplyDamage(1f);
                    previewLoopDamage = false;
                    previewLoopHeal = false;
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("循环演示", EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                previewLoopDamage = GUILayout.Toggle(previewLoopDamage, "循环扣除", "Button", GUILayout.Height(24f));
                previewLoopHeal = GUILayout.Toggle(previewLoopHeal, "循环增加", "Button", GUILayout.Height(24f));

                if (GUILayout.Button("停止", GUILayout.Width(80f), GUILayout.Height(24f)))
                {
                    previewLoopDamage = false;
                    previewLoopHeal = false;
                }
            }
        });
    }

    private void ApplyDamage(float delta)
    {
        float oldTarget = previewTargetPercent;
        previewTargetPercent = Mathf.Clamp01(previewTargetPercent - delta);

        if (previewTargetPercent < oldTarget)
        {
            previewDamageReferencePercent = Mathf.Max(previewDamageReferencePercent, oldTarget);
            previewDamageReferenceHoldTimer = selectedStyle != null ? Mathf.Max(0f, selectedStyle.damageReferenceHoldTime) : 0.35f;
        }
    }

    private void ApplyHeal(float delta)
    {
        previewTargetPercent = Mathf.Clamp01(previewTargetPercent + delta);
    }

    private void DrawPreviewCanvas(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.10f));
        DrawCrossGuides(rect);

        Vector2 barSize = selectedStyle != null ? selectedStyle.barSize : new Vector2(150f, 14f);
        Vector2 barOffset = selectedStyle != null ? selectedStyle.hpBarOffset : Vector2.zero;
        Vector2 nameOffset = selectedStyle != null ? selectedStyle.nameOffset : new Vector2(0f, 16f);

        Vector2 anchor = new Vector2(rect.center.x, rect.y + rect.height * 0.72f);

        Rect barRect = new Rect(
            anchor.x - barSize.x * 0.5f + barOffset.x,
            anchor.y - barSize.y * 0.5f - barOffset.y,
            barSize.x,
            barSize.y);

        Rect nameRect = new Rect(
            anchor.x - 120f + nameOffset.x,
            barRect.y - 30f - nameOffset.y,
            240f,
            24f);

        GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = selectedStyle != null ? Mathf.Max(8, selectedStyle.nameFontSize) : 13,
            normal = { textColor = selectedStyle != null ? selectedStyle.nameColor : Color.white }
        };
        GUI.Label(nameRect, "NPC Name", nameStyle);

        bool hideWhenFull = selectedStyle != null && selectedStyle.hideWhenFull;
        float threshold = selectedStyle != null ? selectedStyle.fullHpThreshold : 0.999f;
        float alpha = (!hideWhenFull || previewCurrentPercent < threshold) ? 1f : 0f;

        DrawStyledBar(barRect, previewCurrentPercent, previewDamageReferencePercent, alpha);
        DrawStatusAreaPreview(anchor, barRect);

        Rect infoRect = new Rect(rect.x + 16f, rect.yMax - 96f, rect.width - 32f, 76f);
        GUI.Box(infoRect, GUIContent.none);
        GUI.Label(new Rect(infoRect.x + 10f, infoRect.y + 8f, infoRect.width - 20f, 18f),
            $"当前值: {Mathf.RoundToInt(previewCurrentPercent * 100f)} / 100",
            EditorStyles.whiteLabel);
        GUI.Label(new Rect(infoRect.x + 10f, infoRect.y + 28f, infoRect.width - 20f, 18f),
            $"参考层: {Mathf.RoundToInt(previewDamageReferencePercent * 100f)}%    变化: {Mathf.RoundToInt(previewChangePercent * 100f)}%",
            EditorStyles.miniLabel);
        GUI.Label(new Rect(infoRect.x + 10f, infoRect.y + 46f, infoRect.width - 20f, 18f),
            $"状态预览数: {previewStatusCount}    单行容量(自动推导): {GetPreviewStatusPerLine()}",
            EditorStyles.miniLabel);
        GUI.Label(new Rect(infoRect.x + 10f, infoRect.y + 62f, infoRect.width - 20f, 18f),
            "绿色线框表示状态绘制范围。",
            EditorStyles.miniLabel);
    }

    private void DrawStatusAreaPreview(Vector2 anchor, Rect barRect)
    {
        if (selectedStyle == null || !selectedStyle.enableStatusArea)
            return;

        Vector2 areaSize = selectedStyle.statusAreaSize;
        Vector2 areaOffset = selectedStyle.statusAreaOffset;

        Rect areaRect = new Rect(
            anchor.x - areaSize.x * 0.5f + areaOffset.x,
            barRect.yMax + 8f - areaOffset.y,
            areaSize.x,
            areaSize.y);

        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = new Color(0.2f, 1f, 0.35f, 0.95f);
        Handles.DrawAAPolyLine(2f,
            new Vector3(areaRect.xMin, areaRect.yMin),
            new Vector3(areaRect.xMax, areaRect.yMin),
            new Vector3(areaRect.xMax, areaRect.yMax),
            new Vector3(areaRect.xMin, areaRect.yMax),
            new Vector3(areaRect.xMin, areaRect.yMin));
        Handles.color = old;
        Handles.EndGUI();

        DrawStatusPlaceholders(areaRect);
    }

    private void DrawStatusPlaceholders(Rect areaRect)
    {
        if (selectedStyle == null || !selectedStyle.enableStatusArea || previewStatusCount <= 0)
            return;

        Vector2 iconSize = selectedStyle.statusIconSize;
        Vector2 spacing = selectedStyle.statusIconSpacing;
        int perLine = CalculateStatusPerLine(selectedStyle.statusAreaSize, iconSize, spacing);
        int maxRows = CalculateStatusMaxRows(selectedStyle.statusAreaSize, iconSize, spacing);
        if (perLine <= 0 || maxRows <= 0)
            return;

        int maxVisible = perLine * maxRows;
        int drawCount = Mathf.Min(previewStatusCount, maxVisible);

        float contentWidth = perLine * iconSize.x + Mathf.Max(0, perLine - 1) * spacing.x;
        float startX = areaRect.xMin;

        switch (selectedStyle.statusAreaAlignment)
        {
            case TextAnchor.UpperCenter:
            case TextAnchor.MiddleCenter:
            case TextAnchor.LowerCenter:
                startX = areaRect.center.x - contentWidth * 0.5f;
                break;
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                startX = areaRect.xMax - contentWidth;
                break;
        }

        float startY = areaRect.yMin + 2f;

        for (int i = 0; i < drawCount; i++)
        {
            int row = i / perLine;
            int col = i % perLine;

            Rect iconRect = new Rect(
                startX + col * (iconSize.x + spacing.x),
                startY + row * (iconSize.y + spacing.y),
                iconSize.x,
                iconSize.y);

            EditorGUI.DrawRect(iconRect, new Color(0.2f, 1f, 0.35f, 0.16f));
            DrawRectOutline(iconRect, new Color(0.2f, 1f, 0.35f, 0.95f));
        }
    }

    private int GetPreviewStatusPerLine()
    {
        if (selectedStyle == null || !selectedStyle.enableStatusArea)
            return 0;

        return CalculateStatusPerLine(selectedStyle.statusAreaSize, selectedStyle.statusIconSize, selectedStyle.statusIconSpacing);
    }

    private int CalculateStatusPerLine(Vector2 areaSize, Vector2 iconSize, Vector2 spacing)
    {
        float step = iconSize.x + Mathf.Max(0f, spacing.x);
        if (step <= 0.001f)
            return 0;

        return Mathf.Max(1, Mathf.FloorToInt((areaSize.x + Mathf.Max(0f, spacing.x)) / step));
    }

    private int CalculateStatusMaxRows(Vector2 areaSize, Vector2 iconSize, Vector2 spacing)
    {
        float step = iconSize.y + Mathf.Max(0f, spacing.y);
        if (step <= 0.001f)
            return 0;

        return Mathf.Max(1, Mathf.FloorToInt((areaSize.y + Mathf.Max(0f, spacing.y)) / step));
    }

    private void DrawRectOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
    }

    private void DrawStyledBar(Rect barRect, float currentPercent, float referencePercent, float alpha)
    {
        if (alpha <= 0.001f)
            return;

        float left = selectedStyle != null ? selectedStyle.paddingLeft : 0f;
        float right = selectedStyle != null ? selectedStyle.paddingRight : 0f;
        float top = selectedStyle != null ? selectedStyle.paddingTop : 0f;
        float bottom = selectedStyle != null ? selectedStyle.paddingBottom : 0f;

        Rect innerRect = new Rect(
            barRect.x + left,
            barRect.y + top,
            Mathf.Max(0f, barRect.width - left - right),
            Mathf.Max(0f, barRect.height - top - bottom));

        Matrix4x4 oldMatrix = GUI.matrix;
        float rotation = selectedStyle != null ? selectedStyle.rotationZ : 0f;
        if (Mathf.Abs(rotation) > 0.001f)
            GUIUtility.RotateAroundPivot(rotation, barRect.center);

        if (selectedStyle != null)
        {
            for (int i = 0; i < selectedStyle.backgroundLayers.Count; i++)
            {
                OverheadBarBackgroundLayer layer = selectedStyle.backgroundLayers[i];
                if (layer == null)
                    continue;

                Vector2 size = layer.sizeOverride == Vector2.zero ? barRect.size : layer.sizeOverride;
                Rect layerRect = new Rect(
                    barRect.center.x - size.x * 0.5f + layer.offset.x,
                    barRect.center.y - size.y * 0.5f - layer.offset.y,
                    size.x,
                    size.y);

                Matrix4x4 layerOldMatrix = GUI.matrix;
                if (Mathf.Abs(layer.rotationZ) > 0.001f)
                    GUIUtility.RotateAroundPivot(layer.rotationZ, layerRect.center);

                Color tint = layer.useTint ? layer.tint : Color.white;
                DrawTextureOrRect(layerRect, layer.texture, MultiplyAlpha(tint, alpha));
                GUI.matrix = layerOldMatrix;
            }
        }

        Texture2D damageRef = selectedStyle != null ? selectedStyle.damageReferenceTexture : null;
        Texture2D fill = selectedStyle != null ? selectedStyle.fillTexture : null;

        Color damageColor = selectedStyle != null ? selectedStyle.damageReferenceColor : new Color(0.42f, 0.12f, 0.12f, 1f);
        Color fillColor = selectedStyle != null ? selectedStyle.fillColor : Color.white;

        Rect refRect = innerRect;
        refRect.width *= Mathf.Clamp01(referencePercent);
        DrawTextureOrRect(refRect, damageRef, MultiplyAlpha(damageColor, alpha));

        Rect fillRect = innerRect;
        fillRect.width *= Mathf.Clamp01(currentPercent);
        DrawTextureOrRect(fillRect, fill, MultiplyAlpha(fillColor, alpha));

        GUI.matrix = oldMatrix;
    }

    private Color MultiplyAlpha(Color color, float alpha)
    {
        color.a *= alpha;
        return color;
    }

    private void DrawTextureOrRect(Rect rect, Texture2D texture, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        Color old = GUI.color;
        GUI.color = tint;

        if (texture != null)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }
        else
        {
            EditorGUI.DrawRect(rect, tint);
        }

        GUI.color = old;
    }

    private void DrawCrossGuides(Rect rect)
    {
        Color line = new Color(1f, 1f, 1f, 0.08f);
        EditorGUI.DrawRect(new Rect(rect.center.x, rect.y, 1f, rect.height), line);
        EditorGUI.DrawRect(new Rect(rect.x, rect.center.y, rect.width, 1f), line);
    }

    private void CreateStyleAsset()
    {
        EnsureFolderExists(DefaultSaveFolder);

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(DefaultSaveFolder + "/OBS_NewStyle.asset");
        OverheadBarStyleAsset asset = CreateInstance<OverheadBarStyleAsset>();
        ApplyDefaultPreset(asset);

        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedStyle = asset;
        SyncAssetFileName();
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private void ApplyDefaultPreset(OverheadBarStyleAsset asset)
    {
        asset.styleKey = "Default";
        asset.displayName = "默认样式";
        asset.nameFontSize = 13;
        asset.nameColor = Color.white;
        asset.fillColor = new Color(0.85f, 0.12f, 0.12f, 1f);
        asset.damageReferenceColor = new Color(0.42f, 0.12f, 0.12f, 1f);
        asset.barSize = new Vector2(150f, 14f);
        asset.nameOffset = new Vector2(0f, 16f);
        asset.fillLerpSpeed = 5f;
        asset.damageReferenceHoldTime = 0.35f;
        asset.damageReferenceFadeSpeed = 2.5f;
        asset.enableStatusArea = true;
        asset.statusDisplayKey = "default_status_display";
        asset.statusAreaOffset = new Vector2(0f, -22f);
        asset.statusAreaSize = new Vector2(120f, 20f);
        asset.statusIconSize = new Vector2(16f, 16f);
        asset.statusIconSpacing = new Vector2(2f, 2f);
        asset.statusAreaAlignment = TextAnchor.MiddleCenter;
        asset.backgroundLayers.Clear();

        OverheadBarBackgroundLayer baseLayer = new OverheadBarBackgroundLayer
        {
            layerName = "底板",
            useTint = true,
            tint = new Color(0.18f, 0.18f, 0.18f, 1f)
        };
        asset.backgroundLayers.Add(baseLayer);

        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Project/UIUX/HUD" });
        if (texGuids != null && texGuids.Length > 0)
        {
            string texPath = AssetDatabase.GUIDToAssetPath(texGuids[0]);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            baseLayer.texture = tex;
            asset.fillTexture = tex;
            asset.damageReferenceTexture = tex;
        }
    }

    private void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folderName = Path.GetFileName(folderPath);

        if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolderExists(parent);

        if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(folderName))
            AssetDatabase.CreateFolder(parent, folderName);
    }

    private void ReadFromSelection()
    {
        if (Selection.activeObject is OverheadBarStyleAsset style)
        {
            selectedStyle = style;
            SyncAssetFileName();
            Repaint();
        }
    }

    private void PingSelectedStyle()
    {
        if (selectedStyle == null)
            return;

        Selection.activeObject = selectedStyle;
        EditorGUIUtility.PingObject(selectedStyle);
    }

    private void RevealStyleFolder()
    {
        EnsureFolderExists(DefaultSaveFolder);

        string path = selectedStyle != null ? AssetDatabase.GetAssetPath(selectedStyle) : DefaultSaveFolder;
        if (string.IsNullOrWhiteSpace(path))
            path = DefaultSaveFolder;

        string absolutePath = Path.GetFullPath(path);
        if (File.Exists(absolutePath))
            absolutePath = Path.GetDirectoryName(absolutePath);

        if (!string.IsNullOrWhiteSpace(absolutePath))
            EditorUtility.RevealInFinder(absolutePath);
    }

    private void DeleteCurrentStyleWithConfirm()
    {
        if (selectedStyle == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(selectedStyle);
        string displayName = string.IsNullOrWhiteSpace(selectedStyle.displayName) ? selectedStyle.name : selectedStyle.displayName;

        bool confirmed = EditorUtility.DisplayDialog(
            "删除当前样式",
            $"确定要删除当前样式文件吗？\n\n{displayName}\n{assetPath}",
            "删除",
            "取消");

        if (!confirmed)
            return;

        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedStyle = null;
        assetFileName = "";
    }

    private void SyncAssetFileName()
    {
        if (selectedStyle == null)
        {
            assetFileName = "";
            return;
        }

        string path = AssetDatabase.GetAssetPath(selectedStyle);
        assetFileName = Path.GetFileNameWithoutExtension(path);
    }

    private void ApplyAssetFileName()
    {
        if (selectedStyle == null || string.IsNullOrWhiteSpace(assetFileName))
            return;

        string assetPath = AssetDatabase.GetAssetPath(selectedStyle);
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        string error = AssetDatabase.RenameAsset(assetPath, assetFileName);
        if (!string.IsNullOrWhiteSpace(error))
        {
            EditorUtility.DisplayDialog("重命名失败", error, "确定");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        SyncAssetFileName();
        EditorGUIUtility.PingObject(selectedStyle);
    }
}