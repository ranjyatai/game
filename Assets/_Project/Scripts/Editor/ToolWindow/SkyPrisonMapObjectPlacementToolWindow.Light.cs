using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地图放置工具的「光源」模块：从 Assets/_Project/Prefabs/Lights 里挑预制体，在 Scene 视图点击摆放。
///
/// 光源不走地形装饰物那套（那套带遮挡、碰撞、模型代理），就是普通预制体实例，统一挂在
/// WorldRoot/LightRoot 下。摆放高度单独填（灯一般挂在天花板，不是贴地）。
/// 预制体都按项目约定做成「不投影」，角色投影只来自常驻的那盏平行光。
/// </summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private const string LightPrefabFolder = "Assets/_Project/Prefabs/Lights";
    private const string LightParentPath = "WorldRoot/LightRoot";

    private readonly List<GameObject> lightPrefabs = new List<GameObject>();
    private GameObject selectedLightPrefab;
    private string lightSearch = "";
    // 0 = 全部，1 = 纯照明（只有灯光），2 = 光束演出（灯光 + 体积光束，光束只是视觉效果）。
    private int lightCategory;
    private static readonly string[] LightCategoryLabels = { "全部", "纯照明", "光束演出" };

    private static bool HasLightBeam(GameObject prefab)
    {
        return prefab.GetComponentInChildren<VLB.VolumetricLightBeamSD>(true) != null;
    }
    private Vector2 lightListScroll;
    private Vector2 placedLightScroll;
    private float lightPlaceHeight = 3f;
    private float lightPreviewScale = 1f;
    private float lightPreviewYaw;

    private const float LightHeightStep = 0.25f;
    private const float LightYawStep = 15f;
    private const float LightScaleStep = 1.1f;

    // 预制体名 → 中文名。新增光源预制体后在这里补一行，没写的就显示预制体名。
    private static readonly Dictionary<string, string> LightDisplayNames = new Dictionary<string, string>
    {
        { "PF_Light_Point_Warm", "暖色点光源" },
        { "PF_Light_Point_Cool", "冷色点光源" },
        { "PF_Light_Spot_Beam", "聚光灯（光柱）" },
        { "PF_Light_Spot_Beam_Dust", "聚光灯（光柱+飞尘）" },
        { "PF_Light_Spot_Beam_Flicker", "聚光灯（光柱+闪烁）" },
        { "PF_Light_Point_Red", "红色警示点光源" },
        { "PF_Light_Point_Amber", "琥珀色点光源" },
        { "PF_Light_Point_Green", "绿色终端点光源" },
        { "PF_Light_Point_Fill", "暗淡补光（大范围）" },
        { "PF_Light_Point_Small", "小范围暖光" },
        { "PF_Light_Spot_Plain", "聚光灯（无光柱）" },
        { "PF_Light_Spot_Narrow", "窄聚光灯（无光柱）" },
        { "PF_Light_Spot_Wide", "宽聚光灯（无光柱）" },
        { "PF_Light_Spot_Big", "大范围聚光灯（无光柱）" },
        { "PF_Light_Spot_Cool", "冷白聚光灯（无光柱）" },
        { "PF_Light_Spot_Red", "红色警示聚光灯（无光柱）" },
        { "PF_Light_Spot_Narrow_Beam", "窄聚光灯（光柱）" },
        { "PF_Light_Spot_Wide_Beam", "宽聚光灯（光柱）" },
        { "PF_Light_Spot_Beam_Cool", "冷白聚光灯（光柱）" },
        { "PF_Light_Spot_Beam_Red", "红色警示聚光灯（光柱）" },
        { "PF_Light_Stage_Rainbow", "舞台彩虹灯" },
        { "PF_Light_Stage_Rainbow_Tilt", "舞台彩虹灯（倾斜）" },
        { "PF_Light_Stage_Rainbow_Skew", "舞台彩虹灯（偏斜）" },
    };

    private static string GetLightDisplayName(string objectName)
    {
        string key = objectName.Replace("(Clone)", "").Trim();
        int sp = key.IndexOf(" (", System.StringComparison.Ordinal); // Unity 重名后缀 "Name (1)"
        string baseKey = sp > 0 ? key.Substring(0, sp) : key;
        return LightDisplayNames.TryGetValue(baseKey, out string cn) ? cn + key.Substring(baseKey.Length) : key;
    }

    // 灯预制体没有网格，AssetPreview 只能给出通用方块图标。这里按灯的类型 / 颜色 / 附加效果
    // 画一张示意图：点光源 = 发光球，聚光灯 = 灯头 + 向下的光锥，飞尘 = 光锥里的点，闪烁 = 光锥断层。
    private static readonly Dictionary<GameObject, Texture2D> LightIconCache = new Dictionary<GameObject, Texture2D>();

    private static Texture2D GetLightIcon(GameObject prefab)
    {
        if (LightIconCache.TryGetValue(prefab, out Texture2D cached) && cached != null)
            return cached;

        const int S = 64;
        var l = prefab.GetComponentInChildren<Light>();
        Color c = l != null ? l.color : Color.white;
        bool spot = l != null && l.type == LightType.Spot;
        bool dust = prefab.GetComponentInChildren<VLB.VolumetricDustParticles>() != null;
        bool flicker = prefab.GetComponentInChildren<VLB.EffectFlicker>() != null;
        // 渐变色光束（彩虹灯）：光锥颜色按距离取渐变，不再是单色。
        var beamSD = prefab.GetComponentInChildren<VLB.VolumetricLightBeamSD>();
        Gradient rainbow = beamSD != null && beamSD.colorMode == VLB.ColorMode.Gradient ? beamSD.colorGradient : null;

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        Color bg = new Color(0.13f, 0.14f, 0.16f, 1f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            Color o = bg;
            if (!spot)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.5f, S * 0.5f)) / (S * 0.5f);
                float glow = Mathf.Clamp01(1f - d);
                o = Color.Lerp(bg, c, glow * glow * 0.9f);
                if (d < 0.16f) o = Color.Lerp(o, Color.white, 0.85f);
            }
            else
            {
                // 灯头在上方，光锥向下展开（y 向上，所以 y 大是画面上方）。
                float headY = S * 0.84f;
                float t01 = Mathf.Clamp01((headY - y) / (S * 0.78f));
                float halfW = Mathf.Lerp(2f, S * 0.42f, t01);
                float dx = Mathf.Abs(x - S * 0.5f);
                if (y < headY && dx < halfW)
                {
                    float a = (1f - t01) * 0.85f * (1f - dx / halfW * 0.6f);
                    if (flicker && ((int)(t01 * 9f) % 2 == 1)) a *= 0.3f;
                    o = Color.Lerp(bg, rainbow != null ? rainbow.Evaluate(t01) : c, a);
                    if (dust && ((x * 7 + y * 13) % 17 == 0)) o = Color.Lerp(o, Color.white, 0.8f);
                }
                if (y >= headY - 1f && y < headY + 7f && dx < 9f) o = new Color(0.62f, 0.64f, 0.68f, 1f);
            }
            px[y * S + x] = o;
        }
        tex.SetPixels(px);
        tex.Apply();
        LightIconCache[prefab] = tex;
        return tex;
    }

    private void LoadLightPrefabs()
    {
        lightPrefabs.Clear();
        if (AssetDatabase.IsValidFolder(LightPrefabFolder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { LightPrefabFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null)
                    lightPrefabs.Add(go);
            }
            lightPrefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        if (selectedLightPrefab == null || !lightPrefabs.Contains(selectedLightPrefab))
            selectedLightPrefab = lightPrefabs.FirstOrDefault();
    }

    private void DrawLightPlacePage()
    {
        if (lightPrefabs.Count == 0)
            LoadLightPrefabs();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("筛选", EditorStyles.boldLabel);
        lightSearch = EditorGUILayout.TextField("搜索", lightSearch);
        lightCategory = GUILayout.Toolbar(lightCategory, LightCategoryLabels);
        lightPlaceHeight = EditorGUILayout.FloatField("摆放高度（离地）", lightPlaceHeight);
        EditorGUILayout.LabelField("父节点", LightParentPath);
        EditorGUILayout.EndVertical();

        List<GameObject> filtered = string.IsNullOrWhiteSpace(lightSearch)
            ? lightPrefabs
            : lightPrefabs.Where(p => p.name.IndexOf(lightSearch, System.StringComparison.OrdinalIgnoreCase) >= 0
                                   || GetLightDisplayName(p.name).IndexOf(lightSearch, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (lightCategory != 0)
            filtered = filtered.Where(p => HasLightBeam(p) == (lightCategory == 2)).ToList();

        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, ModuleListFixedHeight, ModuleListFixedHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * 58f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        lightListScroll = GUI.BeginScrollView(viewRect, lightListScroll, contentRect, false, true);
        float y = 0f;
        foreach (GameObject prefab in filtered)
        {
            Rect row = new Rect(0f, y, contentRect.width, 54f);
            bool selected = prefab == selectedLightPrefab;
            if (selected)
                EditorGUI.DrawRect(row, new Color(accent.r, accent.g, accent.b, 0.35f));

            GUI.DrawTexture(new Rect(row.x + 4f, row.y + 4f, 46f, 46f), GetLightIcon(prefab), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(row.x + 58f, row.y + 6f, row.width - 62f, 20f), new GUIContent(GetLightDisplayName(prefab.name), prefab.name), EditorStyles.boldLabel);
            GUI.Label(new Rect(row.x + 58f, row.y + 28f, row.width - 62f, 20f), DescribeLightPrefab(prefab), EditorStyles.miniLabel);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                selectedLightPrefab = prefab;
                if (placementMode)
                    DestroyPreview();
                Event.current.Use();
                Repaint();
            }
            y += 58f;
        }
        GUI.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        lightPreviewScale = Mathf.Clamp(EditorGUILayout.FloatField("缩放（范围倍率）", lightPreviewScale), 0.1f, 20f);
        lightPreviewYaw = EditorGUILayout.FloatField("旋转 Y", lightPreviewYaw);
        if (GUILayout.Button("重置", GUILayout.Width(48f)))
        {
            lightPreviewScale = 1f;
            lightPreviewYaw = 0f;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("选中预制体后点上方「放置」进入摆放模式：Scene 视图里左键点击摆放，右键或 Esc 退出。\n"
            + "滚轮 = 缩放（灯的范围 / 光束长度跟着变）；Shift+滚轮 = 升降；Ctrl+滚轮 = 旋转。\n"
            + "预制体都是不投影的，装饰灯不要自己打开投影。", MessageType.Info);
    }

    private static string DescribeLightPrefab(GameObject prefab)
    {
        var l = prefab.GetComponentInChildren<Light>();
        if (l == null)
            return "（没有 Light 组件）";
        string beam = prefab.GetComponentInChildren<VLB.VolumetricLightBeamSD>() != null ? " + 光束" : "";
        if (prefab.GetComponentInChildren<VLB.VolumetricLightBeamSD>() is VLB.VolumetricLightBeamSD sd && sd.colorMode == VLB.ColorMode.Gradient)
            beam = " + 渐变光束";
        return $"{(l.type == LightType.Spot ? "聚光灯" : l.type == LightType.Point ? "点光源" : l.type.ToString())}  强度 {l.intensity:0.#}  范围 {l.range:0.#}{beam}";
    }

    private void OnLightPlacementSceneGUI(SceneView sceneView)
    {
        if (selectedLightPrefab == null)
            return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) || (e.type == EventType.MouseDown && e.button == 1))
        {
            SetPlacementMode(enabled: false);
            e.Use();
            return;
        }

        if (HandleLightPreviewWheel(sceneView, e))
            return;

        if (!TryRaycastGround(e.mousePosition, out Vector3 hit))
            return;

        Vector3 pos = hit + Vector3.up * lightPlaceHeight;
        DrawLightPlacementPreview(pos, hit);

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            PlaceLight(pos);
            e.Use();
        }

        sceneView.Repaint();
    }

    /// <summary>滚轮：无修饰键 = 缩放，Shift = 升降，Ctrl/Command = 旋转（和地形装饰物放置的手感一致）。</summary>
    private bool HandleLightPreviewWheel(SceneView sceneView, Event e)
    {
        if (e.type != EventType.ScrollWheel || e.alt || EditorGUIUtility.editingTextField)
            return false;

        // Shift+滚轮在部分系统里会变成横向滚动，取幅度更大的轴。
        float wheel = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
        if (Mathf.Abs(wheel) < 0.0001f)
            return false;
        float dir = wheel < 0f ? 1f : -1f;

        if (e.control || e.command)
            lightPreviewYaw = Mathf.Repeat(lightPreviewYaw + dir * LightYawStep + 180f, 360f) - 180f;
        else if (e.shift)
            lightPlaceHeight = Mathf.Max(0f, lightPlaceHeight + dir * LightHeightStep);
        else
            lightPreviewScale = Mathf.Clamp(lightPreviewScale * (dir > 0f ? LightScaleStep : 1f / LightScaleStep), 0.1f, 20f);

        e.Use();
        sceneView.Repaint();
        Repaint();
        return true;
    }

    private void DrawLightPlacementPreview(Vector3 pos, Vector3 ground)
    {
        var l = selectedLightPrefab.GetComponentInChildren<Light>();
        Handles.color = ValidPreviewColor;
        Handles.DrawLine(pos, ground);
        Handles.DrawWireDisc(ground, Vector3.up, 0.3f);
        Handles.SphereHandleCap(0, pos, Quaternion.identity, 0.3f, EventType.Repaint);
        if (l == null)
            return;

        // 画灯真正的照射范围，不被摆放高度封顶（以前聚光灯只画地面光斑，高度 < 范围时
        // 圈的大小只由高度决定，缩放完全看不出变化）：
        //   点光源 = 三个方向的圈组成的球，半径 = 范围
        //   聚光灯 = 从灯头出发的光锥，长度 = 范围；光锥碰到地面时另用黄色圈标出光斑
        float range = l.range * lightPreviewScale;
        if (l.type == LightType.Spot)
        {
            Vector3 dir = Quaternion.Euler(0f, lightPreviewYaw, 0f) * (selectedLightPrefab.transform.rotation * Vector3.forward);
            Vector3 end = pos + dir * range;
            float half = l.spotAngle * 0.5f * Mathf.Deg2Rad;
            float endRadius = range * Mathf.Tan(half);
            Vector3 right = Vector3.Cross(dir, Vector3.up).sqrMagnitude > 0.001f
                ? Vector3.Cross(dir, Vector3.up).normalized
                : Vector3.right;
            Vector3 up = Vector3.Cross(right, dir).normalized;
            Handles.DrawWireDisc(end, dir, endRadius);
            Handles.DrawLine(pos, end + right * endRadius);
            Handles.DrawLine(pos, end - right * endRadius);
            Handles.DrawLine(pos, end + up * endRadius);
            Handles.DrawLine(pos, end - up * endRadius);
            if (dir.y < -0.99f && lightPlaceHeight < range)
            {
                Handles.color = new Color(1f, 0.9f, 0.2f, 0.9f);
                Handles.DrawWireDisc(ground, Vector3.up, lightPlaceHeight * Mathf.Tan(half));
                Handles.color = ValidPreviewColor;
            }
        }
        else
        {
            Handles.DrawWireDisc(pos, Vector3.up, range);
            Handles.DrawWireDisc(pos, Vector3.right, range);
            Handles.DrawWireDisc(pos, Vector3.forward, range);
        }
        float gh = Mathf.Max(0.01f, lightPlaceHeight);
        float gi = SkyPrisonLightMarkerOverlay.EstimateGroundIrradiance(l.intensity, range, l.spotAngle, l.type == LightType.Spot, gh, 0f);
        Handles.Label(pos + Vector3.up * 0.5f,
            $"范围 {range:0.#} m  ×{lightPreviewScale:0.##}  高 {lightPlaceHeight:0.##} m  地面亮度≈{gi:0.00}" + (gi < 0.1f ? "  ⚠ 太暗" : ""));
        // 朝向箭头：旋转预览用，聚光灯 / 光束的横向偏转（倾斜、偏斜）也看这个方向。
        Vector3 fwd = Quaternion.Euler(0f, lightPreviewYaw, 0f) * Vector3.forward;
        Handles.DrawLine(pos, pos + fwd * 0.8f);
        Handles.DrawWireDisc(pos + fwd * 0.8f, Vector3.up, 0.08f);
    }

    private void PlaceLight(Vector3 position)
    {
        Transform parent = GetOrCreateParent(LightParentPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(selectedLightPrefab, parent.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(instance, "Place Light");
        instance.transform.SetParent(parent, true);
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.Euler(0f, lightPreviewYaw, 0f) * instance.transform.rotation;
        ApplyLightScale(instance, lightPreviewScale);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(instance.scene);
    }

    /// <summary>
    /// Light.range 不跟 Transform 缩放走，所以「缩放」直接改范围：所有 Light 的 range × 倍率，
    /// 自带参数的独立光束（比如彩虹灯，光束和 Light 不在同一个物体上）的衰减终点也 × 倍率。
    /// 跟 Light 同物体的光束会自动跟着灯走，不用再改。改的是实例上的覆盖值，预制体本身不动。
    /// </summary>
    private static void ApplyLightScale(GameObject instance, float scale)
    {
        if (Mathf.Approximately(scale, 1f))
            return;

        foreach (Light l in instance.GetComponentsInChildren<Light>(true))
        {
            Undo.RecordObject(l, "Scale Light");
            l.range *= scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(l);
        }

        foreach (var beam in instance.GetComponentsInChildren<VLB.VolumetricLightBeamSD>(true))
        {
            if (beam.GetComponent<Light>() != null)
                continue;
            Undo.RecordObject(beam, "Scale Beam");
            beam.fallOffEnd *= scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(beam);
        }
    }

    private void DrawLightPlacedPage()
    {
        Transform root = FindTransformByPath(LightParentPath);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("已摆放光源", EditorStyles.boldLabel);
        DrawPlacedPickToggle();
        DrawPlacedGroupExpandButtons(lightPlacedGroups);
        EditorGUILayout.EndHorizontal();
        if (root == null || root.childCount == 0)
        {
            EditorGUILayout.HelpBox("还没有摆放光源（放下第一个时自动创建 " + LightParentPath + "）。", MessageType.Info);
            return;
        }

        var children = new List<Transform>();
        for (int i = 0; i < root.childCount; i++)
            children.Add(root.GetChild(i));

        // 光源页没有多选集合；组头的「选中全部」直接走 Unity Selection，用一个临时集合给组头算已选数。
        var selected = new HashSet<int>(Selection.gameObjects.Select(g => g.GetInstanceID()));
        pendingLightDelete = null;
        DrawGroupedPlacedList(
            children,
            lightPlacedGroups,
            false,
            ref placedLightScroll,
            54f,
            t => t.gameObject,
            t =>
            {
                Light l = t.GetComponentInChildren<Light>();
                string type = l != null ? l.type.ToString() : "zzz";
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                return (type, l != null ? type : "其他",
                        source != null ? source.GetInstanceID().ToString() : "name:" + t.name,
                        GetLightDisplayName(source != null ? source.name : t.name));
            },
            selected,
            gos => Selection.objects = gos.ToArray(),
            gos =>
            {
                if (EditorUtility.DisplayDialog("删除确认", $"删除 {gos.Count} 个光源？", "删除", "取消"))
                {
                    foreach (GameObject g in gos)
                        if (g != null)
                            Undo.DestroyObjectImmediate(g);
                }
            },
            DrawPlacedLightRow,
            ModuleListFixedHeight);

        if (pendingLightDelete != null)
            Undo.DestroyObjectImmediate(pendingLightDelete.gameObject);
    }

    private Transform pendingLightDelete;

    private void DrawPlacedLightRow(Rect row, Transform child, int index, List<Transform> list)
    {
        var l = child.GetComponentInChildren<Light>();
        bool shadowWarn = l != null && l.shadows != LightShadows.None;
        // 实例对应的源预制体（图标和已摆放前的列表用同一张）；断开预制体连接的实例就没有图标。
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
        if (source != null)
            GUI.DrawTexture(new Rect(row.x + 4f, row.y + 2f, 46f, 46f), GetLightIcon(source), ScaleMode.ScaleToFit);
        GUI.Label(new Rect(row.x + 58f, row.y + 15f, row.width - 190f, 20f),
            shadowWarn ? GetLightDisplayName(child.name) + "  ⚠ 开了投影" : GetLightDisplayName(child.name), EditorStyles.boldLabel);
        if (GUI.Button(new Rect(row.xMax - 122f, row.y + 14f, 58f, 22f), "选中"))
        {
            Selection.activeTransform = child;
            SceneView.lastActiveSceneView?.Frame(new Bounds(child.position, Vector3.one * 4f), false);
        }
        if (GUI.Button(new Rect(row.xMax - 60f, row.y + 14f, 58f, 22f), "删除"))
            pendingLightDelete = child;
    }
}
