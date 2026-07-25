using UnityEngine;

/// <summary>
/// V10 - 2026-05-29 - retired Build footstep playback safety; package distances are authoritative
/// Project-wide audio tuning asset.
///
/// This asset is intentionally separated from individual AudioPackage assets and unit emitters.
/// AudioPackage: what clips/tracks a sound uses.
/// UnitFootstepAudioEmitter: where/how a unit emits footsteps.
/// SkyPrisonAudioGlobalSettings: global mix and gameplay-audio balance such as Master / SE / Footstep volume.
/// </summary>
[CreateAssetMenu(
    fileName = "SkyPrisonAudioGlobalSettings",
    menuName = "Sky Prison/Audio/Global Audio Settings",
    order = 1300)]
public class SkyPrisonAudioGlobalSettings : ScriptableObject
{
    public const string ResourcesAssetName = "SkyPrisonAudioGlobalSettings";
    public const string DefaultAssetPath = "Assets/_Project/Data/Settings/Resources/SkyPrisonAudioGlobalSettings.asset";

    private static SkyPrisonAudioGlobalSettings cachedInstance;

    public static SkyPrisonAudioGlobalSettings Instance
    {
        get
        {
            if (cachedInstance != null)
                return cachedInstance;

#if UNITY_EDITOR
            // Editor and Editor Play must always use the canonical asset edited by Tools/音声设置.
            // The runtime catalog can exist in the project with stale values while you are tuning audio;
            // letting Editor preview read that catalog is exactly how Editor/Build loudness drift starts.
            cachedInstance = FindOrCreateEditorAsset();
            return cachedInstance;
#else
            // Player Build uses the asset reference collected into the runtime catalog by the build preprocessor.
            SkyPrisonRuntimeAudioCatalog catalog = SkyPrisonRuntimeAudioCatalog.Instance;
            if (catalog != null && catalog.globalSettings != null)
            {
                cachedInstance = catalog.globalSettings;
                return cachedInstance;
            }

            // Fallback for development builds where the catalog has not been generated yet.
            cachedInstance = Resources.Load<SkyPrisonAudioGlobalSettings>(ResourcesAssetName);
            return cachedInstance;
#endif
        }
    }

    public static void SetRuntimeInstanceForEditorPreview(SkyPrisonAudioGlobalSettings settings)
    {
        if (settings != null)
            cachedInstance = settings;
    }

    public static void ClearCachedInstance()
    {
        cachedInstance = null;
    }

    [Header("Main Mix")]
    [Range(0f, 2f)] public float masterVolume = 1f;
    [Range(0f, 2f)] public float seVolume = 1f;
    [Range(0f, 2f)] public float meVolume = 1f;
    [Range(0f, 2f)] public float bgmVolume = 1f;
    [Range(0f, 2f)] public float voiceVolume = 1f;
    [Range(0f, 2f)] public float ambienceVolume = 1f;
    [Range(0f, 2f)] public float uiVolume = 1f;

    [Header("Footstep Master")]
    [Tooltip("开启后，UnitFootstepAudioEmitter 使用本资产里的全局脚步声参数，不再需要每个 Player/Enemy 组件逐个调。")]
    public bool useGlobalFootstepSettings = true;

    [Tooltip("脚步声整体播放响度。最终播放音量 = Master × SE × Footstep Volume × 包/轨/片段/动作/地表倍率。")]
    [Range(0f, 3f)] public float footstepVolumeMultiplier = 1f;

    [Tooltip("脚步声整体 AI 听觉强度倍率。")]
    [Range(0f, 3f)] public float footstepNoiseMultiplier = 1f;

    [Tooltip("开启后，AI 听觉强度也吃 Master × SE × Footstep Volume，确保玩家听感和敌人听觉标准一致。")]
    public bool applyOutputVolumeToFootstepNoise = true;

    [Header("Footstep Motion Loudness")]
    public bool overrideEmitterMotionLoudness = true;
    [Range(0f, 2f)] public float walkVolumeMultiplier = 0.70f;
    [Range(0f, 2f)] public float runVolumeMultiplier = 1.00f;
    [Range(0f, 2f)] public float sneakVolumeMultiplier = 0.15f;
    [Range(0f, 3f)] public float jumpUpVolumeMultiplier = 1.00f;
    [Range(0f, 3f)] public float landVolumeMultiplier = 1.35f;

    [Header("Footstep Ground Surface Mix")]
    public bool overrideEmitterSurfaceDucking = true;

    [Tooltip("脚下存在草地/水泥/泥地等地表音声层时，鞋底层播放音量倍率。")]
    [Range(0f, 1f)] public float baseFootwearVolumeMultiplierWhenGroundSurfaceActive = 0.40f;

    [Tooltip("脚下存在地表音声层时，鞋底层 AI 噪声倍率。")]
    [Range(0f, 1f)] public float baseFootwearNoiseMultiplierWhenGroundSurfaceActive = 0.40f;

    public bool overrideEmitterGroundSurfaceMix = true;

    [Tooltip("所有地表音声层的全局播放响度倍率。")]
    [Range(0f, 3f)] public float groundSurfaceLayerVolumeMultiplier = 1.00f;

    [Tooltip("所有地表音声层的全局 AI 噪声倍率。")]
    [Range(0f, 3f)] public float groundSurfaceLayerNoiseMultiplier = 1.00f;


    [Header("Dodge SE")]
    [Tooltip("闪避时播放的音效片段，可填多个随机挑选。")]
    public AudioClip[] dodgeSFXClips;
    [Range(0f, 2f)] public float dodgeSFXVolume = 0.85f;

    [Header("Build Footstep Playback Safety")]
    [Tooltip("Build 运行时启用脚步声播放端保底：保持 AI 听觉仍用权威 3D 距离，但玩家听到的 AudioSource 不再因为 2.5D 摄像机/AudioListener 距离过远而完全静音。")]
    public bool useBuildAudibleFootstepPlaybackSafety = false;

    [Tooltip("在 Editor Play 模式也应用同样保底，方便直接预览 Build 听感。关闭时只在非 Editor Build 生效。")]
    public bool applyBuildAudibleFootstepPlaybackSafetyInEditor = false;

    [Tooltip("保底模式下脚步声播放端的 3D 混合度。0=完全2D，1=完全3D。AI 听觉不受此项影响。")]
    [Range(0f, 1f)] public float buildAudibleFootstepSpatialBlend = 1f;

    [Tooltip("保底模式下脚步声 AudioSource 的最小听距下限。只影响玩家听到的播放端，不影响 AI 听觉距离。")]
    [Min(0.01f)] public float buildAudibleFootstepMinDistanceFloor = 1f;

    [Tooltip("保底模式下脚步声 AudioSource 的最大听距下限。只影响玩家听到的播放端，不影响 AI 听觉距离。")]
    [Min(0.01f)] public float buildAudibleFootstepMaxDistanceFloor = 8f;

    public float GetFootstepOutputMultiplier(float emitterLocalGlobalVolume)
    {
        return Mathf.Max(0f, emitterLocalGlobalVolume) *
               Mathf.Max(0f, masterVolume) *
               Mathf.Max(0f, seVolume) *
               Mathf.Max(0f, footstepVolumeMultiplier);
    }

    public float GetFootstepNoiseMultiplier(float emitterLocalGlobalVolume)
    {
        float outputMultiplier = applyOutputVolumeToFootstepNoise
            ? Mathf.Max(0f, masterVolume) * Mathf.Max(0f, seVolume) * Mathf.Max(0f, footstepVolumeMultiplier)
            : 1f;

        return Mathf.Max(0f, emitterLocalGlobalVolume) *
               outputMultiplier *
               Mathf.Max(0f, footstepNoiseMultiplier);
    }

    public string BuildDebugSummary()
    {
        return
            $"master={masterVolume:0.###}, se={seVolume:0.###}, footstep={footstepVolumeMultiplier:0.###}, " +
            $"noise={footstepNoiseMultiplier:0.###}, useGlobalFootstep={useGlobalFootstepSettings}, " +
            $"walk={walkVolumeMultiplier:0.###}, run={runVolumeMultiplier:0.###}, sneak={sneakVolumeMultiplier:0.###}, " +
            $"surfaceVol={groundSurfaceLayerVolumeMultiplier:0.###}, baseDuck={baseFootwearVolumeMultiplierWhenGroundSurfaceActive:0.###}, " +
            $"buildSafety=retired, packageDistanceAuthoritative=True";
    }

    public void ResetToDefaults()
    {
        masterVolume = 1f;
        seVolume = 1f;
        meVolume = 1f;
        bgmVolume = 1f;
        voiceVolume = 1f;
        ambienceVolume = 1f;
        uiVolume = 1f;

        useGlobalFootstepSettings = true;
        footstepVolumeMultiplier = 1f;
        footstepNoiseMultiplier = 1f;
        applyOutputVolumeToFootstepNoise = true;

        overrideEmitterMotionLoudness = true;
        walkVolumeMultiplier = 0.70f;
        runVolumeMultiplier = 1.00f;
        sneakVolumeMultiplier = 0.15f;
        jumpUpVolumeMultiplier = 1.00f;
        landVolumeMultiplier = 1.35f;

        overrideEmitterSurfaceDucking = true;
        baseFootwearVolumeMultiplierWhenGroundSurfaceActive = 0.40f;
        baseFootwearNoiseMultiplierWhenGroundSurfaceActive = 0.40f;

        overrideEmitterGroundSurfaceMix = true;
        groundSurfaceLayerVolumeMultiplier = 1.00f;
        groundSurfaceLayerNoiseMultiplier = 1.00f;

        dodgeSFXVolume = 0.85f;

        useBuildAudibleFootstepPlaybackSafety = false;
        applyBuildAudibleFootstepPlaybackSafetyInEditor = false;
        buildAudibleFootstepSpatialBlend = 1f;
        buildAudibleFootstepMinDistanceFloor = 1f;
        buildAudibleFootstepMaxDistanceFloor = 8f;

    }

#if UNITY_EDITOR
    public static SkyPrisonAudioGlobalSettings FindOrCreateEditorAsset()
    {
        if (cachedInstance != null)
            return cachedInstance;

        // First choice is the canonical Resources asset.  The Tools/音声设置 window edits this asset,
        // and Build/runtime can load the same one.  Do not let AssetDatabase.FindAssets pick an
        // arbitrary duplicate from another folder.
        SkyPrisonAudioGlobalSettings canonical = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonAudioGlobalSettings>(DefaultAssetPath);
        if (canonical != null)
        {
            cachedInstance = canonical;
            return cachedInstance;
        }

        // Second choice: any Resources copy with the expected file name.
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SkyPrisonAudioGlobalSettings");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || !path.Replace('\\', '/').Contains("/Resources/"))
                continue;

            SkyPrisonAudioGlobalSettings asset = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonAudioGlobalSettings>(path);
            if (asset != null)
            {
                cachedInstance = asset;
                return cachedInstance;
            }
        }

        // Last fallback: existing project copy.  We copy its serialized values into the canonical
        // Resources asset so the window and Build will use the same data from now on.
        SkyPrisonAudioGlobalSettings source = null;
        string[] allGuids = UnityEditor.AssetDatabase.FindAssets("t:SkyPrisonAudioGlobalSettings");
        for (int i = 0; i < allGuids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(allGuids[i]);
            SkyPrisonAudioGlobalSettings asset = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonAudioGlobalSettings>(path);
            if (asset != null)
            {
                source = asset;
                break;
            }
        }

        EnsureEditorFolder("Assets/_Project");
        EnsureEditorFolder("Assets/_Project/Data");
        EnsureEditorFolder("Assets/_Project/Data/Settings");
        EnsureEditorFolder("Assets/_Project/Data/Settings/Resources");

        SkyPrisonAudioGlobalSettings created = CreateInstance<SkyPrisonAudioGlobalSettings>();
        if (source != null)
        {
            UnityEditor.EditorUtility.CopySerialized(source, created);
            created.name = "SkyPrisonAudioGlobalSettings";
        }

        UnityEditor.AssetDatabase.CreateAsset(created, DefaultAssetPath);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();

        cachedInstance = created;
        return cachedInstance;
    }

    private static void EnsureEditorFolder(string folderPath)
    {
        if (UnityEditor.AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!UnityEditor.AssetDatabase.IsValidFolder(next))
                UnityEditor.AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
#endif
}

#if UNITY_EDITOR
public class SkyPrisonAudioGlobalSettingsWindow : UnityEditor.EditorWindow
{
    // ── 标签页 ────────────────────────────────────────────────────────────────
    private static readonly string[] TabLabels = { "主混音", "系统SE音效", "物品SE音效" };
    private int _tab;

    // ── 主混音 ────────────────────────────────────────────────────────────────
    private UnityEditor.SerializedObject _serializedSettings;

    // ── 系统SE ────────────────────────────────────────────────────────────────
    private UnityEditor.SerializedObject _serializedSETable;
    private SkyPrisonSystemSETable       _seTable;
    private bool[]                       _seEntryFoldouts;

    // ── 物品SE ────────────────────────────────────────────────────────────────
    private UnityEditor.SerializedObject  _serializedItemSETable;
    private SkyPrisonItemMaterialSoundTable _itemSETable;
    private bool[]                          _itemSEFoldouts;
    private Vector2                      _scroll;

    // SE 类型中文名
    private static readonly System.Collections.Generic.Dictionary<SkyPrisonSystemSEType, string> SeTypeNames
        = new System.Collections.Generic.Dictionary<SkyPrisonSystemSEType, string>
    {
        { SkyPrisonSystemSEType.Confirm,   "确认 / 使用" },
        { SkyPrisonSystemSEType.Cancel,    "取消 / 返回" },
        { SkyPrisonSystemSEType.Forbidden, "禁止 / 不可用" },
        { SkyPrisonSystemSEType.Switch,    "切换 / 导航" },
        { SkyPrisonSystemSEType.Open,      "窗口打开" },
        { SkyPrisonSystemSEType.Close,     "窗口关闭" },
        { SkyPrisonSystemSEType.Pickup,    "拾取 / 获得" },
        { SkyPrisonSystemSEType.DropToGround, "丢弃 / 落地" },
        { SkyPrisonSystemSEType.EquipArmor,       "装备防具" },
        { SkyPrisonSystemSEType.UnequipArmor,     "卸下防具" },
        { SkyPrisonSystemSEType.EquipWeapon,      "装备武器" },
        { SkyPrisonSystemSEType.UnequipWeapon,    "卸下武器" },
        { SkyPrisonSystemSEType.QuickSlotAssign,  "指定为快捷物品" },
        { SkyPrisonSystemSEType.UnassignQuickSlot,"取消快捷物品指定" },
        { SkyPrisonSystemSEType.TitlePressAnyKey, "开场按任意键" },
        { SkyPrisonSystemSEType.Notice,    "提示 / 通知" },
        { SkyPrisonSystemSEType.Error,     "错误 / 失败" },
        { SkyPrisonSystemSEType.Revive,    "复活" },
        { SkyPrisonSystemSEType.Tidy,      "整理 / 背包排序" },
        { SkyPrisonSystemSEType.NearDeath, "濒死 / 脉冲警报" },
        { SkyPrisonSystemSEType.Purchase,  "购买 / 支付" },
        { SkyPrisonSystemSEType.WeaponSwitch, "武器切换（滚轮）" }, // 之前漏了这个类型，音声设置窗口列表里一直显示英文原名
    };

    [UnityEditor.MenuItem("Tools/音声设置")]
    public static void Open()
    {
        var w = GetWindow<SkyPrisonAudioGlobalSettingsWindow>("音声设置");
        w.minSize = new Vector2(760f, 560f);
        w.Show();
    }

    private void OnEnable()
    {
        BindSettings();
        BindSETable();
        BindItemSETable();
    }

    private void BindSettings()
    {
        var s = SkyPrisonAudioGlobalSettings.FindOrCreateEditorAsset();
        _serializedSettings = s != null ? new UnityEditor.SerializedObject(s) : null;
    }

    private void BindSETable()
    {
        // 尝试从资产数据库找 SETable（不依赖 Resources 文件夹位置）
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SkyPrisonSystemSETable");
        if (guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            _seTable = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonSystemSETable>(path);
        }
        _serializedSETable = _seTable != null ? new UnityEditor.SerializedObject(_seTable) : null;
        _seEntryFoldouts   = null;
    }

    private void OnGUI()
    {
        // ── 顶部工具栏 ────────────────────────────────────────────────────────
        UnityEditor.EditorGUILayout.BeginHorizontal(UnityEditor.EditorStyles.toolbar);
        GUILayout.Label("音声设置", UnityEditor.EditorStyles.boldLabel, GUILayout.Width(120f));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("定位资产", UnityEditor.EditorStyles.toolbarButton, GUILayout.Width(72f)))
        {
            if (_tab == 0 && _serializedSettings != null)
                UnityEditor.EditorGUIUtility.PingObject(_serializedSettings.targetObject);
            else if (_tab == 1 && _seTable != null)
                UnityEditor.EditorGUIUtility.PingObject(_seTable);
            else if (_tab == 2 && _itemSETable != null)
                UnityEditor.EditorGUIUtility.PingObject(_itemSETable);
        }
        if (_tab == 0 && GUILayout.Button("恢复默认", UnityEditor.EditorStyles.toolbarButton, GUILayout.Width(72f)))
        {
            if (UnityEditor.EditorUtility.DisplayDialog("恢复默认音声设置", "确定恢复为默认值？", "恢复", "取消"))
            {
                UnityEditor.Undo.RecordObject(_serializedSettings.targetObject, "恢复默认音声设置");
                ((SkyPrisonAudioGlobalSettings)_serializedSettings.targetObject).ResetToDefaults();
                SkyPrisonAudioGlobalSettings.SetRuntimeInstanceForEditorPreview(
                    (SkyPrisonAudioGlobalSettings)_serializedSettings.targetObject);
                UnityEditor.EditorUtility.SetDirty(_serializedSettings.targetObject);
                UnityEditor.AssetDatabase.SaveAssets();
            }
        }
        UnityEditor.EditorGUILayout.EndHorizontal();

        // ── 标签页 ────────────────────────────────────────────────────────────
        _tab = GUILayout.Toolbar(_tab, TabLabels, GUILayout.Height(24f));

        _scroll = UnityEditor.EditorGUILayout.BeginScrollView(_scroll);

        if (_tab == 0)
            DrawMixTab();
        else if (_tab == 1)
            DrawSETab();
        else
            DrawItemSETab();

        UnityEditor.EditorGUILayout.EndScrollView();
    }

    // ── 主混音标签页 ──────────────────────────────────────────────────────────

    private void DrawMixTab()
    {
        if (_serializedSettings == null || _serializedSettings.targetObject == null) BindSettings();
        if (_serializedSettings == null) { UnityEditor.EditorGUILayout.HelpBox("无法读取全局音声设置。", UnityEditor.MessageType.Error); return; }

        _serializedSettings.Update();

        UnityEditor.EditorGUILayout.Space(8f);
        UnityEditor.EditorGUILayout.HelpBox(
            "主音量、各分类音量（SE/ME/BGM/语音/环境/界面），以及脚步声整体响度和地表混合参数。",
            UnityEditor.MessageType.Info);

        DrawSection("主混音", "所有大类音量的总入口。", _serializedSettings, new[]
        {
            Row("masterVolume",   "主音量",     "全局最终输出音量倍率。"),
            Row("seVolume",       "音效音量",   "战斗、脚步、交互等 SE 音效总音量。"),
            Row("meVolume",       "演出音乐",   "ME / 演出用音乐或短乐句音量。"),
            Row("bgmVolume",      "背景音乐",   "BGM 音量。"),
            Row("voiceVolume",    "语音音量",   "角色语音、旁白等。"),
            Row("ambienceVolume", "环境音量",   "风声、机器声、场景底噪等。"),
            Row("uiVolume",       "界面音量",   "系统 SE、按钮、窗口等 UI 音量。"),
        });

        DrawSection("脚步声总控", "脚步声整体响度和 AI 听觉强度的统一入口。", _serializedSettings, new[]
        {
            Row("useGlobalFootstepSettings",      "使用全局脚步声设置", "开启后，发射器优先使用这里的统一参数。"),
            Row("footstepVolumeMultiplier",       "脚步声整体音量倍率", "所有脚步声播放音量的总倍率。"),
            Row("footstepNoiseMultiplier",        "脚步声 AI 噪声倍率", "传给敌人听觉系统的总倍率。"),
            Row("applyOutputVolumeToFootstepNoise","AI 噪声跟随输出音量","开启后 AI 听觉也吃主/音效/脚步声倍率。"),
        });

        DrawSection("脚步动作响度", "按动作状态统一控制脚步声强弱。", _serializedSettings, new[]
        {
            Row("overrideEmitterMotionLoudness", "覆盖单位动作响度", "开启后覆盖各单位本地参数。"),
            Row("walkVolumeMultiplier",  "行走音量倍率", "建议 0.6~0.75。"),
            Row("runVolumeMultiplier",   "奔跑音量倍率", "通常作为 1 倍基准。"),
            Row("sneakVolumeMultiplier", "潜行音量倍率", "建议 0.1~0.2。"),
            Row("jumpUpVolumeMultiplier","起跳音量倍率", ""),
            Row("landVolumeMultiplier",  "落地音量倍率", ""),
        });

        DrawSection("地表混合", "脚下有草地/水泥/泥地等音声层时的混合关系。", _serializedSettings, new[]
        {
            Row("overrideEmitterSurfaceDucking",                        "覆盖鞋底压低倍率",       ""),
            Row("baseFootwearVolumeMultiplierWhenGroundSurfaceActive",   "有地表时鞋底音量倍率",   ""),
            Row("baseFootwearNoiseMultiplierWhenGroundSurfaceActive",    "有地表时鞋底 AI 噪声倍率",""),
            Row("overrideEmitterGroundSurfaceMix",                      "覆盖地表层倍率",         ""),
            Row("groundSurfaceLayerVolumeMultiplier",                   "地表层音量倍率",         ""),
            Row("groundSurfaceLayerNoiseMultiplier",                    "地表层 AI 噪声倍率",     ""),
        });

        // ── 闪避音效 ──────────────────────────────────────────────────────────
        UnityEditor.EditorGUILayout.Space(12f);
        UnityEditor.EditorGUILayout.LabelField("闪避音效", UnityEditor.EditorStyles.boldLabel);
        UnityEditor.EditorGUILayout.BeginVertical("box");
        {
            var noteStyle = new GUIStyle(UnityEditor.EditorStyles.wordWrappedMiniLabel)
                { normal = { textColor = new Color(0.72f, 0.72f, 0.72f) } };
            UnityEditor.EditorGUILayout.LabelField("玩家闪避时播放的音效，支持多个片段随机选取。", noteStyle);
            UnityEditor.EditorGUILayout.Space(4f);
            float old2 = UnityEditor.EditorGUIUtility.labelWidth;
            UnityEditor.EditorGUIUtility.labelWidth = 230f;
            var clipsProp = _serializedSettings.FindProperty("dodgeSFXClips");
            var volProp   = _serializedSettings.FindProperty("dodgeSFXVolume");
            if (clipsProp != null) UnityEditor.EditorGUILayout.PropertyField(clipsProp, new GUIContent("闪避音效片段（可多个）"), true);
            if (volProp   != null) UnityEditor.EditorGUILayout.PropertyField(volProp,   new GUIContent("闪避音效音量倍率"));
            UnityEditor.EditorGUIUtility.labelWidth = old2;
        }
        UnityEditor.EditorGUILayout.EndVertical();

        bool changed = _serializedSettings.ApplyModifiedProperties();
        if (changed || GUI.changed)
        {
            var s = _serializedSettings.targetObject as SkyPrisonAudioGlobalSettings;
            if (s != null) SkyPrisonAudioGlobalSettings.SetRuntimeInstanceForEditorPreview(s);
            UnityEditor.EditorUtility.SetDirty(_serializedSettings.targetObject);
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }

    // ── 系统SE标签页 ──────────────────────────────────────────────────────────

    private void DrawSETab()
    {
        UnityEditor.EditorGUILayout.Space(8f);

        // 没找到 SETable → 自动创建
        if (_seTable == null)
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "未找到 SkyPrisonSystemSETable 资产，点击下方按钮自动创建。",
                UnityEditor.MessageType.Warning);
            if (GUILayout.Button("自动创建系统SE音效表", GUILayout.Height(32f)))
            {
                EnsureSETableFolder();
                const string assetPath = "Assets/_Project/Data/Audio/Resources/Data/Audio/SkyPrisonSystemSETable.asset";
                var newTable = UnityEditor.Editor.CreateInstance<SkyPrisonSystemSETable>();
                UnityEditor.AssetDatabase.CreateAsset(newTable, assetPath);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
                BindSETable();
            }
            if (GUILayout.Button("刷新（已手动创建后点此）", GUILayout.Height(24f)))
                BindSETable();
            return;
        }

        if (_serializedSETable == null || _serializedSETable.targetObject == null)
            BindSETable();

        _serializedSETable.Update();

        UnityEditor.EditorGUILayout.HelpBox(
            "为每种系统操作音效类型指定音频片段（Clip）。同一类型可放多个 Clip 随机播放。\n" +
            "音量最终 = 条目音量 × 界面音量（uiVolume）× 主音量（masterVolume）。",
            UnityEditor.MessageType.Info);

        var entriesProp = _serializedSETable.FindProperty("entries");
        if (entriesProp == null) { UnityEditor.EditorGUILayout.LabelField("entries 字段未找到"); return; }

        // 确保每个枚举类型都有一条 Entry（自动补齐）
        EnsureAllSETypeEntries(entriesProp);

        // 初始化折叠状态数组
        if (_seEntryFoldouts == null || _seEntryFoldouts.Length != entriesProp.arraySize)
            _seEntryFoldouts = new bool[entriesProp.arraySize];

        float oldLW = UnityEditor.EditorGUIUtility.labelWidth;
        UnityEditor.EditorGUIUtility.labelWidth = 160f;

        for (int i = 0; i < entriesProp.arraySize; i++)
        {
            var entry  = entriesProp.GetArrayElementAtIndex(i);
            var typeProp  = entry.FindPropertyRelative("type");
            var clipsProp = entry.FindPropertyRelative("clips");
            var volProp   = entry.FindPropertyRelative("volume");
            var pitchProp = entry.FindPropertyRelative("pitch");
            var varProp   = entry.FindPropertyRelative("pitchVariance");

            var seType = (SkyPrisonSystemSEType)typeProp.enumValueIndex;
            string typeName = SeTypeNames.TryGetValue(seType, out string n) ? n : seType.ToString();

            // 折叠标题行（显示类型名 + 首个 clip 名作预览）
            string preview = "";
            if (clipsProp.arraySize > 0)
            {
                var firstClip = clipsProp.GetArrayElementAtIndex(0).objectReferenceValue;
                preview = firstClip != null ? $"  [{firstClip.name}]" : "  [未绑定]";
            }
            else preview = "  [未绑定]";

            UnityEditor.EditorGUILayout.BeginVertical("box");
            _seEntryFoldouts[i] = UnityEditor.EditorGUILayout.Foldout(
                _seEntryFoldouts[i],
                $"{typeName}{preview}",
                true, UnityEditor.EditorStyles.foldoutHeader);

            if (_seEntryFoldouts[i])
            {
                UnityEditor.EditorGUILayout.Space(2f);
                UnityEditor.EditorGUILayout.PropertyField(clipsProp,   new GUIContent("音频片段（可多个）"), true);
                UnityEditor.EditorGUILayout.PropertyField(volProp,     new GUIContent("音量倍率"));
                UnityEditor.EditorGUILayout.PropertyField(pitchProp,   new GUIContent("基础音高"));
                UnityEditor.EditorGUILayout.PropertyField(varProp,     new GUIContent("音高随机幅度"));
                UnityEditor.EditorGUILayout.Space(2f);
            }
            UnityEditor.EditorGUILayout.EndVertical();
        }

        UnityEditor.EditorGUIUtility.labelWidth = oldLW;

        if (_serializedSETable.ApplyModifiedProperties())
        {
            UnityEditor.EditorUtility.SetDirty(_seTable);
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }

    // 保证 entries 里每个 SkyPrisonSystemSEType（除 None）都有一条记录
    private static void EnsureAllSETypeEntries(UnityEditor.SerializedProperty entriesProp)
    {
        var allTypes = (SkyPrisonSystemSEType[])System.Enum.GetValues(typeof(SkyPrisonSystemSEType));
        foreach (var t in allTypes)
        {
            if (t == SkyPrisonSystemSEType.None) continue;
            bool found = false;
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var e = entriesProp.GetArrayElementAtIndex(i);
                if ((SkyPrisonSystemSEType)e.FindPropertyRelative("type").enumValueIndex == t)
                { found = true; break; }
            }
            if (!found)
            {
                entriesProp.InsertArrayElementAtIndex(entriesProp.arraySize);
                var newE = entriesProp.GetArrayElementAtIndex(entriesProp.arraySize - 1);
                newE.FindPropertyRelative("type").enumValueIndex = (int)t;
                newE.FindPropertyRelative("volume").floatValue   = 1f;
                newE.FindPropertyRelative("pitch").floatValue    = 1f;
                newE.FindPropertyRelative("pitchVariance").floatValue = 0f;
                newE.FindPropertyRelative("clips").ClearArray();
            }
        }
    }

    private void BindItemSETable()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SkyPrisonItemMaterialSoundTable");
        if (guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            _itemSETable = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonItemMaterialSoundTable>(path);
        }
        _serializedItemSETable = _itemSETable != null ? new UnityEditor.SerializedObject(_itemSETable) : null;
        _itemSEFoldouts = null;
    }

    private static readonly System.Collections.Generic.Dictionary<ItemSoundMaterial, string> ItemMatNames
        = new System.Collections.Generic.Dictionary<ItemSoundMaterial, string>
    {
        { ItemSoundMaterial.Default, "默认" },
        { ItemSoundMaterial.Metal,   "金属" },
        { ItemSoundMaterial.Powder,  "粉末 / 颗粒" },
        { ItemSoundMaterial.Fabric,  "布料 / 皮革" },
        { ItemSoundMaterial.Liquid,  "液体容器" },
        { ItemSoundMaterial.Paper,   "纸张 / 书册" },
        { ItemSoundMaterial.Plastic, "塑料 / 包装袋" },
        { ItemSoundMaterial.Bone,    "骨质 / 骨制品" },
        { ItemSoundMaterial.Tech,    "科技物 / 电子元件" },
    };

    private void DrawItemSETab()
    {
        UnityEditor.EditorGUILayout.Space(8f);

        if (_itemSETable == null)
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "未找到 SkyPrisonItemMaterialSoundTable 资产，点击下方按钮自动创建。",
                UnityEditor.MessageType.Warning);
            if (GUILayout.Button("自动创建物品材质音效表", GUILayout.Height(32f)))
            {
                EnsureSETableFolder();
                const string assetPath = "Assets/_Project/Data/Audio/Resources/Data/Audio/SkyPrisonItemMaterialSoundTable.asset";
                var newTable = UnityEditor.Editor.CreateInstance<SkyPrisonItemMaterialSoundTable>();
                UnityEditor.AssetDatabase.CreateAsset(newTable, assetPath);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
                BindItemSETable();
            }
            if (GUILayout.Button("刷新（已手动创建后点此）", GUILayout.Height(24f)))
                BindItemSETable();
            return;
        }

        if (_serializedItemSETable == null || _serializedItemSETable.targetObject == null)
            BindItemSETable();

        _serializedItemSETable.Update();

        UnityEditor.EditorGUILayout.HelpBox(
            "为每种物品材质类型配置拿起（Pickup）和放下（Drop）音效片段。\n" +
            "同一类型可放多个 Clip 随机播放。在物品库的「一般」页签可为每件物品选择材质类型。",
            UnityEditor.MessageType.Info);

        // 全局总控
        var globalVolProp = _serializedItemSETable.FindProperty("globalVolumeMultiplier");
        if (globalVolProp != null)
        {
            UnityEditor.EditorGUILayout.BeginVertical("box");
            UnityEditor.EditorGUILayout.PropertyField(globalVolProp, new GUIContent("全局音量总控", "所有物品拾取/放下音效的统一音量倍率，不影响各条目的独立音量设置。"));
            UnityEditor.EditorGUILayout.EndVertical();
            UnityEditor.EditorGUILayout.Space(6f);
        }

        var entriesProp = _serializedItemSETable.FindProperty("entries");
        if (entriesProp == null) { UnityEditor.EditorGUILayout.LabelField("entries 字段未找到"); return; }

        EnsureAllItemMatEntries(entriesProp);

        if (_itemSEFoldouts == null || _itemSEFoldouts.Length != entriesProp.arraySize)
            _itemSEFoldouts = new bool[entriesProp.arraySize];

        float oldLW = UnityEditor.EditorGUIUtility.labelWidth;
        UnityEditor.EditorGUIUtility.labelWidth = 160f;

        for (int i = 0; i < entriesProp.arraySize; i++)
        {
            var entry       = entriesProp.GetArrayElementAtIndex(i);
            var matProp     = entry.FindPropertyRelative("material");
            var pickupProp  = entry.FindPropertyRelative("pickupClips");
            var dropProp    = entry.FindPropertyRelative("dropClips");
            var volProp     = entry.FindPropertyRelative("volume");
            var pitchProp   = entry.FindPropertyRelative("pitch");
            var varProp     = entry.FindPropertyRelative("pitchVariance");

            var mat = (ItemSoundMaterial)matProp.enumValueIndex;
            string matName = ItemMatNames.TryGetValue(mat, out string n) ? n : mat.ToString();

            // 预览首个 clip 名
            string pickPreview = pickupProp.arraySize > 0 && pickupProp.GetArrayElementAtIndex(0).objectReferenceValue != null
                ? pickupProp.GetArrayElementAtIndex(0).objectReferenceValue.name : "未绑定";

            UnityEditor.EditorGUILayout.BeginVertical("box");
            _itemSEFoldouts[i] = UnityEditor.EditorGUILayout.Foldout(
                _itemSEFoldouts[i], $"{matName}  [{pickPreview}]",
                true, UnityEditor.EditorStyles.foldoutHeader);

            if (_itemSEFoldouts[i])
            {
                UnityEditor.EditorGUILayout.Space(2f);
                UnityEditor.EditorGUILayout.PropertyField(pickupProp, new GUIContent("拿起音效（可多个）"), true);
                UnityEditor.EditorGUILayout.PropertyField(dropProp,   new GUIContent("放下音效（可多个）"), true);
                UnityEditor.EditorGUILayout.PropertyField(volProp,    new GUIContent("音量倍率"));
                UnityEditor.EditorGUILayout.PropertyField(pitchProp,  new GUIContent("基础音高"));
                UnityEditor.EditorGUILayout.PropertyField(varProp,    new GUIContent("音高随机幅度"));
                UnityEditor.EditorGUILayout.Space(2f);
            }
            UnityEditor.EditorGUILayout.EndVertical();
        }

        UnityEditor.EditorGUIUtility.labelWidth = oldLW;

        if (_serializedItemSETable.ApplyModifiedProperties())
        {
            UnityEditor.EditorUtility.SetDirty(_itemSETable);
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }

    private static void EnsureAllItemMatEntries(UnityEditor.SerializedProperty entriesProp)
    {
        var allMats = (ItemSoundMaterial[])System.Enum.GetValues(typeof(ItemSoundMaterial));
        foreach (var m in allMats)
        {
            bool found = false;
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                if ((ItemSoundMaterial)entriesProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("material").enumValueIndex == m)
                { found = true; break; }
            }
            if (!found)
            {
                entriesProp.InsertArrayElementAtIndex(entriesProp.arraySize);
                var e = entriesProp.GetArrayElementAtIndex(entriesProp.arraySize - 1);
                e.FindPropertyRelative("material").enumValueIndex = (int)m;
                e.FindPropertyRelative("volume").floatValue       = 1f;
                e.FindPropertyRelative("pitch").floatValue        = 1f;
                e.FindPropertyRelative("pitchVariance").floatValue = 0.05f;
                e.FindPropertyRelative("pickupClips").ClearArray();
                e.FindPropertyRelative("dropClips").ClearArray();
            }
        }
    }

    private static void EnsureSETableFolder()
    {
        string[] parts = "Assets/_Project/Data/Audio/Resources/Data/Audio".Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!UnityEditor.AssetDatabase.IsValidFolder(next))
                UnityEditor.AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    // ── 通用工具 ──────────────────────────────────────────────────────────────

    private void DrawSection(string title, string note, UnityEditor.SerializedObject so, SettingRow[] rows)
    {
        UnityEditor.EditorGUILayout.Space(12f);
        UnityEditor.EditorGUILayout.LabelField(title, UnityEditor.EditorStyles.boldLabel);
        UnityEditor.EditorGUILayout.BeginVertical("box");

        if (!string.IsNullOrWhiteSpace(note))
        {
            var noteStyle = new GUIStyle(UnityEditor.EditorStyles.wordWrappedMiniLabel)
                { normal = { textColor = new Color(0.72f, 0.72f, 0.72f) } };
            UnityEditor.EditorGUILayout.LabelField(note, noteStyle);
            UnityEditor.EditorGUILayout.Space(4f);
        }

        float old = UnityEditor.EditorGUIUtility.labelWidth;
        UnityEditor.EditorGUIUtility.labelWidth = 230f;
        foreach (var r in rows)
        {
            var prop = so.FindProperty(r.propertyName);
            if (prop != null)
                UnityEditor.EditorGUILayout.PropertyField(prop, new GUIContent(r.label, r.tooltip), true);
        }
        UnityEditor.EditorGUIUtility.labelWidth = old;
        UnityEditor.EditorGUILayout.EndVertical();
    }

    private static SettingRow Row(string p, string l, string t) => new SettingRow(p, l, t);

    private struct SettingRow
    {
        public readonly string propertyName, label, tooltip;
        public SettingRow(string p, string l, string t) { propertyName = p; label = l; tooltip = t; }
    }
}
#endif
