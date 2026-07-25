using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>独立窗口入口，与天空囚笼编辑器 Tab 共用同一套绘制逻辑。</summary>
public class SkyPrisonLootDropWindow : EditorWindow
{
    private SkyPrisonLootDropPage _page;

    [MenuItem("Tools/掉落物设置")]
    public static void Open()
    {
        var w = GetWindow<SkyPrisonLootDropWindow>("掉落物设置");
        w.minSize = new Vector2(520f, 480f);
        w.Show();
    }

    private void OnEnable()
    {
        _page = new SkyPrisonLootDropPage(null);
        _page.OnEnable();
    }

    private void OnGUI() => _page?.OnGUIRight();
}

/// <summary>
/// 掉落物设置页面：配置 LootDropModelLibrary（类别→Mesh、品级→发光色）。
/// </summary>
public class SkyPrisonLootDropPage : SkyPrisonEditorPageBase
{
    private const string LibrarySearchPath = "Assets/_Project";
    private const string LibraryCreatePath = "Assets/_Project/Data/Settings/Resources";
    private const string LibraryFileName   = "LootDropModelLibrary.asset";

    private LootDropModelLibrary _library;
    private SerializedObject     _so;
    private Vector2              _scroll;

    public SkyPrisonLootDropPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "掉落物";

    public override void OnEnable() => LoadOrCreate();

    // ── 主绘制 ────────────────────────────────────────────────────────────

    public override void OnGUIRight()
    {
        if (_library == null || _so == null)
        {
            EditorGUILayout.HelpBox("找不到 LootDropModelLibrary 资产。", MessageType.Warning);
            if (GUILayout.Button("创建", GUILayout.Height(28f)))
                LoadOrCreate();
            return;
        }

        _so.Update();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("掉落物设置", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("定位资产", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            EditorGUIUtility.PingObject(_library);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        SerializedProperty sizeProp      = _so.FindProperty("modelSizeMultiplier");
        SerializedProperty holoProp      = _so.FindProperty("hologramMaterial");
        SerializedProperty vfxPrefabProp = _so.FindProperty("beaconVFXPrefab");
        SerializedProperty vfxScaleProp  = _so.FindProperty("beaconVFXScale");
        SerializedProperty beaconMatProp = _so.FindProperty("beaconMaterial");

        EditorGUILayout.LabelField("模型外观", EditorStyles.boldLabel);
        if (sizeProp != null)
            EditorGUILayout.PropertyField(sizeProp, new GUIContent("尺寸倍率", "以当前默认大小为 1.0，填入倍率。如 1.3 = 放大 30%。"));
        if (holoProp != null)
            EditorGUILayout.PropertyField(holoProp, new GUIContent("全息材质", "Sky Prison/LootDrop Hologram shader 材质，留空保持原始材质。"));

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("光柱 VFX", EditorStyles.boldLabel);

        if (vfxPrefabProp != null)
            EditorGUILayout.PropertyField(vfxPrefabProp, new GUIContent("VFX Prefab", "商店 VFX Prefab，有值时按品级自动染色。优先于下方材质。"));

        bool hasPrefab = vfxPrefabProp != null && vfxPrefabProp.objectReferenceValue != null;
        if (hasPrefab && vfxScaleProp != null)
            EditorGUILayout.PropertyField(vfxScaleProp, new GUIContent("VFX 缩放", "X/Z 控制光柱粗细，Y 控制高度。"));

        if (!hasPrefab && beaconMatProp != null)
            EditorGUILayout.PropertyField(beaconMatProp, new GUIContent("粒子材质（自搓）", "无 Prefab 时使用。URP Particles/Unlit，Additive。"));

        if (!hasPrefab)
            EditorGUILayout.HelpBox("拖入商店 VFX Prefab 可获得更好的光柱效果，代码会自动按品级染色。", MessageType.Info);

        GUILayout.Space(8f);

        DrawSection("一般道具 Mesh", "generalEntries",
            "每种物品类别（消耗品 / 任务物品 / 凭证 / 特殊）对应的掉落模型网格。材料请用下方子类区块。");

        GUILayout.Space(8f);

        DrawMaterialSection();

        GUILayout.Space(8f);

        DrawSection("装备 Mesh", "equipmentEntries",
            "每种装备槽（武器 / 头部 / 上装 / 下装 / 手部 / 鞋子）对应的掉落模型网格。");

        GUILayout.Space(8f);

        DrawFallback();

        GUILayout.Space(6f);
        EditorGUILayout.HelpBox("发光颜色自动跟随物品等级（LV1–8 固定色，LV9 彩虹循环），无需手动配置。", MessageType.Info);

        EditorGUILayout.EndScrollView();

        _so.ApplyModifiedProperties();

        if (GUI.changed)
            EditorUtility.SetDirty(_library);
    }

    // ── 左侧留空（整个页面在右侧） ────────────────────────────────────────
    public override void OnGUILeft() { }

    // ── 各区块 ────────────────────────────────────────────────────────────

    private void DrawSection(string title, string arrayPropName, string tooltip)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(tooltip, MessageType.None);
        GUILayout.Space(2f);

        SerializedProperty arr = _so.FindProperty(arrayPropName);
        if (arr == null) return;

        for (int i = 0; i < arr.arraySize; i++)
        {
            SerializedProperty entry = arr.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginHorizontal();

            // 枚举标签（类别 / 槽位）
            SerializedProperty enumProp = entry.FindPropertyRelative("category")
                                       ?? entry.FindPropertyRelative("slot");
            if (enumProp != null)
            {
                EditorGUILayout.LabelField(
                    enumProp.enumDisplayNames[enumProp.enumValueIndex],
                    GUILayout.Width(100f));
            }

            SerializedProperty modelProp = entry.FindPropertyRelative("model");
            SerializedProperty meshProp  = entry.FindPropertyRelative("mesh");
            bool hasModel = modelProp?.objectReferenceValue != null;
            if (modelProp != null)
                EditorGUILayout.ObjectField(modelProp, typeof(GameObject), GUIContent.none);
            // 只有 model 为空时才显示 mesh 槽（两者互斥，mesh 是备选）
            if (!hasModel && meshProp != null)
                EditorGUILayout.ObjectField(meshProp, typeof(Mesh), GUIContent.none, GUILayout.Width(110f));

            EditorGUILayout.EndHorizontal();

            DrawSubFields(entry);
            GUILayout.Space(4f);
        }
    }

    private void DrawMaterialSection()
    {
        EditorGUILayout.LabelField("材料子类 Mesh", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("材料细分（零件 / 金属块 / 液体 / 杂物）各自对应的掉落模型网格。未配置子类时回退到一般道具 Mesh 的材料槽（如有）。", MessageType.None);
        GUILayout.Space(2f);

        SerializedProperty arr = _so.FindProperty("materialEntries");
        if (arr == null) return;

        for (int i = 0; i < arr.arraySize; i++)
        {
            SerializedProperty entry     = arr.GetArrayElementAtIndex(i);
            SerializedProperty enumProp  = entry.FindPropertyRelative("subCategory");
            SerializedProperty modelProp = entry.FindPropertyRelative("model");

            EditorGUILayout.BeginHorizontal();
            if (enumProp != null)
                EditorGUILayout.LabelField(enumProp.enumDisplayNames[enumProp.enumValueIndex], GUILayout.Width(100f));
            if (modelProp != null)
                EditorGUILayout.ObjectField(modelProp, typeof(GameObject), GUIContent.none);
            EditorGUILayout.EndHorizontal();

            DrawSubFields(entry);
            GUILayout.Space(4f);
        }
    }

    private static GUIStyle _subLabelStyle;
    private static GUIStyle SubLabelStyle => _subLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
    {
        normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }
    };

    private static void DrawSubFields(SerializedProperty entry)
    {
        SerializedProperty scaleProp = entry.FindPropertyRelative("scaleOverride");
        SerializedProperty rotProp   = entry.FindPropertyRelative("rotationOffset");
        if (scaleProp == null && rotProp == null) return;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(108f); // 与上方标签对齐的缩进

        if (scaleProp != null)
        {
            EditorGUILayout.LabelField("缩放", SubLabelStyle, GUILayout.Width(30f));
            scaleProp.floatValue = EditorGUILayout.FloatField(scaleProp.floatValue, GUILayout.Width(44f));
            GUILayout.Space(8f);
        }

        if (rotProp != null)
        {
            EditorGUILayout.LabelField("旋转 X", SubLabelStyle, GUILayout.Width(40f));
            Vector3 rot = rotProp.vector3Value;
            rot.x = EditorGUILayout.FloatField(rot.x, GUILayout.Width(44f));
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Y", SubLabelStyle, GUILayout.Width(12f));
            rot.y = EditorGUILayout.FloatField(rot.y, GUILayout.Width(44f));
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Z", SubLabelStyle, GUILayout.Width(12f));
            rot.z = EditorGUILayout.FloatField(rot.z, GUILayout.Width(44f));
            rotProp.vector3Value = rot;
        }

        EditorGUILayout.EndHorizontal();
    }

    private static void EnsureMaterialEntries(LootDropModelLibrary lib)
    {
        bool dirty = false;
        foreach (MaterialSubCategory sub in System.Enum.GetValues(typeof(MaterialSubCategory)))
        {
            bool found = false;
            foreach (var e in lib.materialEntries)
                if (e.subCategory == sub) { found = true; break; }
            if (!found)
            {
                lib.materialEntries.Add(new LootDropModelLibrary.MaterialEntry { subCategory = sub });
                dirty = true;
            }
        }
        if (dirty)
        {
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
        }
    }

    private void DrawFallback() { }   // fallbackMesh 已移除

    private static void EnsureVFXHueShiftShader(LootDropModelLibrary lib)
    {
        if (lib.vfxHueShiftShader != null) return;

        // 在项目里搜索 VFXHueShift.shader
        string[] guids = AssetDatabase.FindAssets("VFXHueShift t:Shader");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null)
            {
                lib.vfxHueShiftShader = shader;
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                Debug.Log($"[LootDropPage] 已自动绑定 VFX HueShift Shader：{path}");
                return;
            }
        }

        Debug.LogWarning("[LootDropPage] 找不到 VFXHueShift.shader，请手动拖入 LootDropModelLibrary 的 vfxHueShiftShader 字段。");
    }

    // ── 资产加载 / 创建 ───────────────────────────────────────────────────

    private void LoadOrCreate()
    {
        string[] guids = AssetDatabase.FindAssets("t:LootDropModelLibrary", new[] { LibrarySearchPath });
        if (guids.Length > 0)
            _library = AssetDatabase.LoadAssetAtPath<LootDropModelLibrary>(
                AssetDatabase.GUIDToAssetPath(guids[0]));

        if (_library == null)
            _library = CreateDefault();

        if (_library != null)
        {
            EnsureHologramMaterial(_library);
            EnsureVFXHueShiftShader(_library);
            EnsureMaterialEntries(_library);
        }

        _so = _library != null ? new SerializedObject(_library) : null;
    }

    private static void EnsureHologramMaterial(LootDropModelLibrary lib)
    {
        if (lib.hologramMaterial != null) return;

        const string matPath = "Assets/_Project/Art/Shaders/Custom/LootDrop/M_LootDropHologram.mat";

        // 已存在则直接绑定
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null)
        {
            lib.hologramMaterial = existing;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return;
        }

        Shader shader = Shader.Find("Sky Prison/LootDrop Hologram");
        if (shader == null)
        {
            Debug.LogWarning("[LootDropPage] 找不到 Shader 'Sky Prison/LootDrop Hologram'，请先让 Unity 编译 LootDropHologram.shader。");
            return;
        }

        var mat = new Material(shader);
        mat.name = "M_LootDropHologram";
        AssetDatabase.CreateAsset(mat, matPath);
        AssetDatabase.SaveAssets();

        lib.hologramMaterial = mat;
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LootDropPage] 已自动创建全息材质：{matPath}");
    }

    private LootDropModelLibrary CreateDefault()
    {
        if (!AssetDatabase.IsValidFolder(LibraryCreatePath))
        {
            Directory.CreateDirectory(Path.Combine(
                Application.dataPath.Replace("Assets", ""),
                LibraryCreatePath));
            AssetDatabase.Refresh();
        }

        LootDropModelLibrary lib = ScriptableObject.CreateInstance<LootDropModelLibrary>();

        // 一般道具条目（材料已移入 materialEntries）
        lib.generalEntries.Add(new LootDropModelLibrary.GeneralEntry { category = ItemCategory.Consumable });
        lib.generalEntries.Add(new LootDropModelLibrary.GeneralEntry { category = ItemCategory.Quest      });
        lib.generalEntries.Add(new LootDropModelLibrary.GeneralEntry { category = ItemCategory.Currency   });
        lib.generalEntries.Add(new LootDropModelLibrary.GeneralEntry { category = ItemCategory.Special    });

        // 材料子类条目
        lib.materialEntries.Add(new LootDropModelLibrary.MaterialEntry { subCategory = MaterialSubCategory.Part   });
        lib.materialEntries.Add(new LootDropModelLibrary.MaterialEntry { subCategory = MaterialSubCategory.Metal  });
        lib.materialEntries.Add(new LootDropModelLibrary.MaterialEntry { subCategory = MaterialSubCategory.Liquid });
        lib.materialEntries.Add(new LootDropModelLibrary.MaterialEntry { subCategory = MaterialSubCategory.Misc   });

        // 装备条目
        lib.equipmentEntries.Add(new LootDropModelLibrary.EquipmentEntry { slot = EquipmentSlotType.Weapon    });
        lib.equipmentEntries.Add(new LootDropModelLibrary.EquipmentEntry { slot = EquipmentSlotType.Head      });
        lib.equipmentEntries.Add(new LootDropModelLibrary.EquipmentEntry { slot = EquipmentSlotType.UpperBody });
        lib.equipmentEntries.Add(new LootDropModelLibrary.EquipmentEntry { slot = EquipmentSlotType.LowerBody });
        lib.equipmentEntries.Add(new LootDropModelLibrary.EquipmentEntry { slot = EquipmentSlotType.Hands    });
        lib.equipmentEntries.Add(new LootDropModelLibrary.EquipmentEntry { slot = EquipmentSlotType.Shoes    });

        string assetPath = $"{LibraryCreatePath}/{LibraryFileName}";
        AssetDatabase.CreateAsset(lib, assetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LootDropPage] 已创建 LootDropModelLibrary：{assetPath}");
        return lib;
    }
}
