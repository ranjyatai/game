using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 「天空囚笼/片头/商标动画列表」：编辑开头商标 / 片头的播放顺序。
/// 第 1 项、第 2 项……从上到下依次播放；可以添加、删除、拖动或点 ↑↓ 调换顺序、单项开关；
/// 点选一项可以在下方编辑它的内容，并直接预览。游戏里点一下鼠标 / 按任意键就跳过全部。
/// </summary>
public sealed class SkyPrisonIntroPlaylistWindow : EditorWindow
{
    const string AssetPath = "Assets/_Project/Resources/Intro/SkyPrisonIntroPlaylist.asset";
    const string BlackFlameDir = "Assets/_Project/Art/Intro/BlackFlame";

    SkyPrisonIntroPlaylist list;
    SerializedObject so;
    ReorderableList rl;
    int sel = -1;
    Vector2 scroll;
    System.Action pending;

    // 预览
    float pt; bool playing; double lastTime;
    RenderTexture prt; Material pmat;

    [MenuItem("天空囚笼/片头/商标动画列表", false, 899)]
    static void Open()
    {
        var w = GetWindow<SkyPrisonIntroPlaylistWindow>("商标动画列表");
        w.minSize = new Vector2(560f, 560f);
    }

    void OnEnable() { Load(); EditorApplication.update += Tick; }
    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (prt != null) { prt.Release(); DestroyImmediate(prt); prt = null; }
        if (pmat != null) { DestroyImmediate(pmat); pmat = null; }
    }

    // ───────── 资产 ─────────
    void Load()
    {
        list = AssetDatabase.LoadAssetAtPath<SkyPrisonIntroPlaylist>(AssetPath);
        if (list == null) list = CreateDefault();
        so = new SerializedObject(list);
        BuildList();
    }

    static SkyPrisonIntroPlaylist CreateDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
        var l = ScriptableObject.CreateInstance<SkyPrisonIntroPlaylist>();
        var e = new SkyPrisonIntroEntry
        {
            label = "Black Flame Works",
            type = SkyPrisonIntroType.BlackFlameShader,
            mask = LoadMask(),
            audio = AssetDatabase.LoadAssetAtPath<AudioClip>(BlackFlameDir + "/Audio/FireBurst.wav"),
            audioDelay = 0.25f, audioVolume = 1f,     // FireBurst 峰值在 0.05 秒，火焰在 0.3 秒窜出 → 延迟 0.25 秒对上
        };
        l.entries.Add(e);
        AssetDatabase.CreateAsset(l, AssetPath); AssetDatabase.SaveAssets();
        return l;
    }

    /// <summary>游戏里用 2560×1440 的遮罩（比 4K 版省内存）；确保它按"不压缩、非 sRGB、无 mip"导入。</summary>
    static Texture2D LoadMask()
    {
        string p = BlackFlameDir + "/BlackFlameWorks_Mask_2560.png";
        var ti = AssetImporter.GetAtPath(p) as TextureImporter;
        if (ti != null && (ti.sRGBTexture || ti.mipmapEnabled || ti.maxTextureSize < 4096 || ti.textureCompression != TextureImporterCompression.Uncompressed))
        {
            ti.sRGBTexture = false; ti.mipmapEnabled = false; ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    // ───────── 列表 ─────────
    void BuildList()
    {
        rl = new ReorderableList(so, so.FindProperty("entries"), true, true, true, true);
        rl.drawHeaderCallback = r => EditorGUI.LabelField(r, "播放顺序（从上到下）  ·  拖动左侧把手调换  ·  游戏里点一下 = 跳过全部");
        rl.elementHeight = EditorGUIUtility.singleLineHeight + 6f;
        rl.drawElementCallback = (r, i, active, focused) =>
        {
            var el = rl.serializedProperty.GetArrayElementAtIndex(i);
            r.y += 3f; r.height = EditorGUIUtility.singleLineHeight;
            float x = r.x;
            var en = el.FindPropertyRelative("enabled");
            en.boolValue = EditorGUI.Toggle(new Rect(x, r.y, 18f, r.height), en.boolValue); x += 20f;
            EditorGUI.LabelField(new Rect(x, r.y, 22f, r.height), (i + 1).ToString(), EditorStyles.boldLabel); x += 24f;
            float right = r.xMax;
            // 右侧：↑ ↓ ✕
            Rect rx = new Rect(right - 24f, r.y, 24f, r.height), rd = new Rect(rx.x - 24f, r.y, 24f, r.height), ru = new Rect(rd.x - 24f, r.y, 24f, r.height);
            string typeName = TypeName((SkyPrisonIntroType)el.FindPropertyRelative("type").enumValueIndex);
            float tw = 96f; Rect rt = new Rect(ru.x - tw - 4f, r.y, tw, r.height);
            EditorGUI.LabelField(rt, typeName, EditorStyles.miniLabel);
            var lab = el.FindPropertyRelative("label");
            EditorGUI.PropertyField(new Rect(x, r.y, rt.x - x - 4f, r.height), lab, GUIContent.none);
            int idx = i;
            using (new EditorGUI.DisabledScope(i == 0)) if (GUI.Button(ru, "↑")) pending = () => Move(idx, idx - 1);
            using (new EditorGUI.DisabledScope(i >= rl.count - 1)) if (GUI.Button(rd, "↓")) pending = () => Move(idx, idx + 1);
            if (GUI.Button(rx, "✕")) pending = () => Remove(idx);
        };
        rl.onSelectCallback = l => { sel = l.index; pt = 0f; playing = false; };
        rl.onAddDropdownCallback = (rect, l) =>
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent("Black Flame Works（着色器片头）"), false, () => pending = () => Add(SkyPrisonIntroType.BlackFlameShader));
            m.AddItem(new GUIContent("视频"), false, () => pending = () => Add(SkyPrisonIntroType.Video));
            m.AddItem(new GUIContent("图片（淡入淡出）"), false, () => pending = () => Add(SkyPrisonIntroType.Image));
            m.DropDown(rect);
        };
        rl.onRemoveCallback = l => { int i = l.index; if (i >= 0) pending = () => Remove(i); };
    }

    static string TypeName(SkyPrisonIntroType t)
    {
        switch (t)
        {
            case SkyPrisonIntroType.BlackFlameShader: return "着色器片头";
            case SkyPrisonIntroType.Video: return "视频";
            case SkyPrisonIntroType.Image: return "图片";
        }
        return "";
    }

    void Move(int a, int b)
    {
        if (b < 0 || b >= rl.count) return;
        so.Update(); rl.serializedProperty.MoveArrayElement(a, b); so.ApplyModifiedProperties();
        rl.index = b; sel = b;
    }

    void Remove(int i)
    {
        so.Update(); rl.serializedProperty.DeleteArrayElementAtIndex(i); so.ApplyModifiedProperties();
        sel = Mathf.Clamp(sel >= i ? sel - 1 : sel, -1, rl.count - 1); rl.index = sel;
    }

    void Add(SkyPrisonIntroType type)
    {
        Undo.RecordObject(list, "添加片头");
        var e = new SkyPrisonIntroEntry { type = type, label = type == SkyPrisonIntroType.Video ? "新视频" : type == SkyPrisonIntroType.Image ? "新图片" : "Black Flame Works" };
        if (type == SkyPrisonIntroType.BlackFlameShader)
        {
            e.mask = LoadMask();
            e.audio = AssetDatabase.LoadAssetAtPath<AudioClip>(BlackFlameDir + "/Audio/FireBurst.wav");
            e.audioDelay = 0.25f;
        }
        list.entries.Add(e);
        EditorUtility.SetDirty(list);
        so = new SerializedObject(list); BuildList();
        sel = list.entries.Count - 1; rl.index = sel;
    }

    // ───────── 界面 ─────────
    void OnGUI()
    {
        if (list == null || so == null) { Load(); if (so == null) return; }
        so.Update();

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("开头商标 / 片头", EditorStyles.boldLabel);
            if (GUILayout.Button("定位资产", GUILayout.Width(80f))) { Selection.activeObject = list; EditorGUIUtility.PingObject(list); }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button("Play 中试播", GUILayout.Width(90f))) SkyPrisonIntroPlayer.PlayThen(null);
        }
        EditorGUILayout.HelpBox("看效果：打开 MainMenu 场景（Build Settings 第一个场景）按 Play，启动时自动播一次；已经在 Play 里的话点上面的「Play 中试播」。从别的场景直接按 Play 不会播。", MessageType.None);
        EditorGUILayout.PropertyField(so.FindProperty("skipInEditor"), new GUIContent("编辑器里跳过片头", "只影响编辑器，打包后的游戏照常播放"));
        EditorGUILayout.Space(2);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        rl.DoLayoutList();

        float total = 0f; int n = 0;
        foreach (var e in list.entries) if (e != null && e.enabled && e.IsPlayable()) { total += e.Duration(); n++; }
        EditorGUILayout.HelpBox("启用且可播放的共 " + n + " 项，总时长约 " + total.ToString("0.0") + " 秒。玩家点一下鼠标 / 按任意键 / 触屏，直接跳过全部进入主界面。", MessageType.None);

        if (rl.count > 0 && sel >= 0 && sel < rl.count) DrawDetail(sel);
        else EditorGUILayout.HelpBox("点选上面的一项，在这里编辑和预览。", MessageType.Info);
        EditorGUILayout.EndScrollView();

        so.ApplyModifiedProperties();
        if (pending != null) { var p = pending; pending = null; p(); GUIUtility.ExitGUI(); }
    }

    void DrawDetail(int i)
    {
        var el = rl.serializedProperty.GetArrayElementAtIndex(i);
        var type = (SkyPrisonIntroType)el.FindPropertyRelative("type").enumValueIndex;
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("第 " + (i + 1) + " 项：" + el.FindPropertyRelative("label").stringValue, EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("type"), new GUIContent("类型"));
        type = (SkyPrisonIntroType)el.FindPropertyRelative("type").enumValueIndex;
        switch (type)
        {
            case SkyPrisonIntroType.BlackFlameShader:
                EditorGUILayout.PropertyField(el.FindPropertyRelative("mask"), new GUIContent("LOGO 遮罩"));
                EditorGUILayout.HelpBox("着色器实时渲染，不是视频，任何分辨率和宽高比都清晰。时长固定 " + SkyPrisonBlackFlameTimeline.Duration.ToString("0.0") + " 秒。", MessageType.None);
                break;
            case SkyPrisonIntroType.Video:
                EditorGUILayout.PropertyField(el.FindPropertyRelative("video"), new GUIContent("视频"));
                break;
            case SkyPrisonIntroType.Image:
                EditorGUILayout.PropertyField(el.FindPropertyRelative("image"), new GUIContent("图片"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("fadeIn"), new GUIContent("淡入（秒）"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("imageDuration"), new GUIContent("停留（秒）"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("fadeOut"), new GUIContent("淡出（秒）"));
                break;
        }
        if (type != SkyPrisonIntroType.Video)
        {
            EditorGUILayout.PropertyField(el.FindPropertyRelative("audio"), new GUIContent("音效"));
            EditorGUILayout.PropertyField(el.FindPropertyRelative("audioDelay"), new GUIContent("音效延迟（秒）"));
            EditorGUILayout.PropertyField(el.FindPropertyRelative("audioVolume"), new GUIContent("音量"));
        }

        // 预览
        EditorGUILayout.Space(4);
        var entry = list.entries[i];
        float dur = Mathf.Max(0.1f, entry.Duration());
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(playing ? "■ 暂停" : "▶ 预览", GUILayout.Width(70f))) { playing = !playing; lastTime = EditorApplication.timeSinceStartup; if (pt >= dur) pt = 0f; }
            pt = EditorGUILayout.Slider(pt, 0f, dur);
        }
        float w = Mathf.Min(position.width - 24f, 640f), h = w * 9f / 16f;
        Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
        EditorGUI.DrawRect(r, Color.black);
        if (entry.type == SkyPrisonIntroType.BlackFlameShader && entry.mask != null)
        {
            if (prt == null) { prt = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); }
            if (pmat == null) pmat = new Material(Shader.Find("SkyPrison/Intro/BlackFlame"));
            pmat.SetTexture("_Mask", entry.mask); pmat.SetFloat("_Duration", SkyPrisonBlackFlameTimeline.ShaderDuration);
            pmat.SetFloat("_T", SkyPrisonBlackFlameTimeline.Warp(pt)); pmat.SetFloat("_Aspect", 16f / 9f);
            Graphics.Blit(null, prt, pmat);
            GUI.DrawTexture(r, prt, ScaleMode.ScaleToFit, false);
        }
        else if (entry.type == SkyPrisonIntroType.Image && entry.image != null)
        {
            float a = 1f;
            if (entry.fadeIn > 0f) a = Mathf.Min(a, pt / entry.fadeIn);
            if (entry.fadeOut > 0f) a = Mathf.Min(a, (dur - pt) / entry.fadeOut);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, Mathf.Clamp01(a));
            GUI.DrawTexture(r, entry.image, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }
        else if (entry.type == SkyPrisonIntroType.Video)
            EditorGUI.LabelField(r, "视频请进 Play 里查看（窗口内不预览）", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
        else
            EditorGUI.LabelField(r, "先给这一项指定内容", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
    }

    void Tick()
    {
        if (!playing) return;
        double now = EditorApplication.timeSinceStartup; float dt = (float)(now - lastTime); lastTime = now;
        if (list != null && sel >= 0 && sel < list.entries.Count)
        {
            float dur = Mathf.Max(0.1f, list.entries[sel].Duration());
            pt += dt; if (pt >= dur) { pt = dur; playing = false; }
        }
        Repaint();
    }
}
