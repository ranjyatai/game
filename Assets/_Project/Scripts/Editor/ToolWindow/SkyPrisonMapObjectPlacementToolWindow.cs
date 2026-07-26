using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class SkyPrisonMapObjectPlacementToolWindow : EditorWindow
{
    private const string MenuPath = "Tools/Sky Prison/Map/地图对象放置工具";
    private const string LegacyMenuPath = "Tools/Sky Prison/Map/地形装饰物放置工具";
    private const string DefinitionSearchFilter = "t:TerrainDecorationDefinition";
    private const string GroundSurfaceMaterialSearchFilter = "t:GroundSurfaceMaterialDefinition";
    private const string TerrainLayerAssetFolder = "Assets/_Project/Data/TerrainLayers/GroundSurface";
    private const string TerrainTextureAssetFolder = "Assets/_Project/Data/TerrainLayers/GroundSurface/GeneratedTextures";
    private const string GroundStampMaterialAssetFolder = "Assets/_Project/Data/TerrainLayers/GroundSurface/GeneratedStampMaterials";
    private const string GroundStampParentPath = "WorldRoot/GroundRoot/GroundStamps";
    private const string GroundSplineParentPath = "WorldRoot/GroundRoot/GroundSplines";
    // 印花尺寸沿用之前放置工具的单位：资源里填 1，实际按旧地面刷默认单位放大。
    // 这样斑马线一类 Stamp 可以继续用 1 × 1.53 这种直观比例，而不是手填 2 × 3.06。
    private const float GroundStampLegacyUnitWorldSize = 10.0f;
    // V2: GroundStamp / RoadLine / Decal are ground visuals, not character occluders.
    // URP transparent sorting must draw them before Spine characters, otherwise they visually cover the unit.
    private const int GroundOverlaySortingPriority = -50;
    private const int GroundOverlayRenderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + GroundOverlaySortingPriority;
    // 2026-07-18：之前这里写的是-10，注释假设角色/掉落物的sortingOrder范围只有-1..+1——
    // 但角色阴影/本体实际走的是UnitSpriteSortingController那套按深度动态计算的排序
    // (orderMultiplier=1000，clamp在±300000之间，不是固定小范围)，-10这个值在角色
    // 深度算出来是负数比较大的区域会反而盖住阴影，表现就是"投影压不到贴花上面"。
    // 当时改成了-400000想"稳妥压过±300000下限"，但 Renderer/SortingGroup.sortingOrder
    // 底层是16位有符号整数(范围只有-32768..32767)，-400000 早就超出这个范围，写进去
    // 会静默按16位环绕成 -400000 mod 65536 → 实际生效值是 -6784——这个正数级别的值
    // 反而比血迹贴花(BloodVFXManager.DecalSortingOrder，早年间同一个坑已经修过一次，
    // 定在16位下限-32768)大，血迹贴花被地面贴花盖住的bug根源就是这个。改成16位范围内
    // 真正有效、且明确低于血迹贴花新值(见BloodVFXManager.cs)的一个安全值，不再假装
    // 能压过±300000这个本来就不可能用sortingOrder字段完整表示的范围。
    private const int SafeGroundDecalSortingOrder = -32767;
    private const int GroundStampSortingOrder = SafeGroundDecalSortingOrder;
    private const string GroundOverlayPreferredLayerName = "GroundVisual";
    private const string GroundOverlayFallbackLayerName = "Default";
    private const string DefaultParentPath = "WorldRoot/BackgroundRoot/StructureRoot";
    private const string PlaceSoundPath = "Assets/_Project/Audio/SE/Editor/Editor_Setting_01.wav";
    private const string DeleteSoundPath = "Assets/_Project/Audio/SE/Editor/Editor_Setting_02.wav";
    private const bool RoadLineRouteTrace = true;

    private readonly Color accent = new Color(1.00f, 0.24f, 0.08f, 1f);
    private readonly Color surfaceAccent = new Color(1.00f, 0.08f, 0.78f, 1f);
    private readonly Color panelBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color warningColor = new Color(1f, 0.72f, 0.18f, 1f);

    private const float ExpandedMinWidth = 360f;
    private const float ExpandedMinHeight = 430f;
    private const float CompactWindowHeight = 118f;
    private const float TopBarWindowHeight = 56f;

    // 展开态固定窗口尺寸：避免模块切换时窗口容器被不同页面内容撑大/压小。
    private const float ExpandedFixedWidth = 430f;
    private const float ExpandedFixedHeight = 860f;

    // 两个模块共用同一套列表高度，下面的设置区单独滚动。
    private const float ModuleListFixedHeight = 420f;
    private const float SettingsContainerMinHeight = 130f;

    private float activeSettingsButtonMaxWidth = -1f;

    private Rect lastExpandedWindowRect;
    private bool hasSavedExpandedWindowRect = false;

    private enum PlacementObjectKind
    {
        TerrainDecoration,
        GroundSurfaceMaterial,
        Unit,
        Item,
        Trigger,
        SpawnPoint,
        Effect,
        AudioArea,
    }

    private enum ToolPage
    {
        Place,
        Placed,
    }

    private enum GroundBrushShape
    {
        Circle,
        SoftCircle,
        HardCircle,
        Square,
        SoftSquare,
        Diamond,
        Hexagon,
        Star,
        SoftNoise,
        Splatter,
        Ring,
        Stripes,
    }

    private enum GroundBrushMode
    {
        ShapeAdd,
        ShapeErase,
        SurfaceMaterial,
        StampOverlay,
        SplineOverlay,
        TerrainRaise,
        TerrainLower,
        TerrainFlatten,
        TerrainPaintHole,
    }

    private enum TerrainDefaultTool
    {
        None,
        RaiseSurface,
        LowerSurface,
        FlattenSurface,
        PaintHole,
    }

    private enum GroundBrushDebugView
    {
        Normal,
        ShapeMask,
        SurfaceMaterial,
    }

    private sealed class PlacedSurfaceObject
    {
        public GameObject gameObject;
        public string kindLabel;
        public string materialName;
        public Texture preview;

        public PlacedSurfaceObject(GameObject gameObject, string kindLabel, string materialName, Texture preview)
        {
            this.gameObject = gameObject;
            this.kindLabel = kindLabel;
            this.materialName = materialName;
            this.preview = preview;
        }
    }

    /// <summary>
    /// 书签注册表：这里只定义“窗口有哪些模块入口”。
    /// 模块内部逻辑仍然留在各自分支里，不在书签绘制代码里散落 hard-code。
    /// </summary>
    private sealed class ToolBookmark
    {
        public readonly PlacementObjectKind Kind;
        public readonly string Label;
        public readonly bool Enabled;

        public ToolBookmark(PlacementObjectKind kind, string label, bool enabled)
        {
            Kind = kind;
            Label = label;
            Enabled = enabled;
        }
    }

    private static readonly ToolBookmark[] ToolBookmarkRegistry =
    {
        new ToolBookmark(PlacementObjectKind.TerrainDecoration, "地形装饰物", true),
        new ToolBookmark(PlacementObjectKind.GroundSurfaceMaterial, "地表材质", true),
        new ToolBookmark(PlacementObjectKind.Unit, "单位", true),
        new ToolBookmark(PlacementObjectKind.Item, "道具", true),
        new ToolBookmark(PlacementObjectKind.Trigger, "触发器", false),
        new ToolBookmark(PlacementObjectKind.SpawnPoint, "出生点", false),
        new ToolBookmark(PlacementObjectKind.Effect, "特效", false),
        new ToolBookmark(PlacementObjectKind.AudioArea, "声音区域", false),
    };

    private readonly List<TerrainDecorationDefinition> definitions = new List<TerrainDecorationDefinition>();
    private readonly List<GroundSurfaceMaterialDefinition> surfaceMaterials = new List<GroundSurfaceMaterialDefinition>();
    private readonly List<TerrainDecorationRuntimeBinder> placedCache = new List<TerrainDecorationRuntimeBinder>();
    private readonly List<PlacedSurfaceObject> placedSurfaceCache = new List<PlacedSurfaceObject>();

    // ── Unit placement ──────────────────────────────────────────
    private const string UnitDefinitionSearchFilter = "t:UnitDefinition";
    private const string UnitParentPath = "WorldRoot/UnitRoot";
    private readonly List<UnitDefinition> unitDefinitions = new List<UnitDefinition>();
    private UnitDefinition selectedUnitDefinition;

    private enum UnitPlacementFaction
    {
        [InspectorName("跟随单位定义")]  FollowDefinition,
        [InspectorName("玩家")]          Player,
        [InspectorName("友军")]          Ally,
        [InspectorName("敌人")]          Enemy,
        [InspectorName("精英")]          Elite,
        [InspectorName("BOSS")]          Boss,
        [InspectorName("中立无敌意")]    NeutralPassive,
        [InspectorName("中立敌意")]      NeutralHostile,
        [InspectorName("生物")]          Creature,
    }
    private UnitPlacementFaction unitPlacementFaction = UnitPlacementFaction.FollowDefinition;
    private Vector2 unitListScroll;
    private string unitSearch = "";
    private readonly List<UnitDefinitionRuntimeBinder> placedUnitCache = new List<UnitDefinitionRuntimeBinder>();
    private readonly HashSet<int> placedUnitSelectionIds = new HashSet<int>();
    private int placedUnitLastClickedIndex = -1;
    private Vector2 placedUnitScroll;
    private string placedUnitSearch = "";

    // ── Item placement ──────────────────────────────────────────
    private const string ItemDefinitionSearchFilter = "t:ItemDefinition";
    private const string ItemParentPath = "WorldRoot/LootRoot";
    private readonly List<ItemDefinition> itemDefinitions = new List<ItemDefinition>();
    private ItemDefinition selectedItemDefinition;
    private Vector2 itemListScroll;
    private string itemSearch = "";
    private readonly List<LootDropWorldObject> placedItemCache = new List<LootDropWorldObject>();
    private readonly HashSet<int> placedItemSelectionIds = new HashSet<int>();
    private Vector2 placedItemScroll;
    private string placedItemSearch = "";
    private GameObject itemPreviewInstance;
    private readonly List<Renderer> itemPreviewRenderers = new List<Renderer>();
    private bool itemPreviewCanPlace = true;
    private readonly HashSet<int> placedSelectionIds = new HashSet<int>();
    private int placedLastClickedIndex = -1;
    private bool placedLastClickedSurfaceList = false;

    private PlacementObjectKind currentKind = PlacementObjectKind.TerrainDecoration;
    private ToolPage currentPage = ToolPage.Place;

    private bool compactMode = false;
    private bool topBarMode = false;

    private Vector2 listScroll;
    private Vector2 placedScroll;
    private Vector2 placementSettingsScroll;
    private Vector2 surfaceMaterialSettingsScroll;
    private string search = "";
    private string placedSearch = "";
    private int categoryIndex = 0;
    private int subCategoryIndex = 0;
    private TerrainDecorationDefinition selectedDefinition;

    private string surfaceMaterialSearch = "";
    private int surfaceMaterialCategoryIndex = 0;
    private Vector2 surfaceMaterialListScroll;
    private GroundSurfaceMaterialDefinition selectedSurfaceMaterial;
    private TerrainDefaultTool selectedTerrainDefaultTool = TerrainDefaultTool.None;
    private float terrainDefaultToolStrength = 0.25f;
    private float terrainFlattenWorldHeight = 0f;

    // 只给 TerrainLayer 地表材质画笔使用。印花 / 贴花 / 样条不走这套笔刷图案。
    private Vector2 terrainSurfaceBrushPaletteScroll;
    private readonly Dictionary<GroundBrushShape, Texture2D> terrainSurfaceBrushPreviewTextures = new Dictionary<GroundBrushShape, Texture2D>();

    private float groundStampPlacementRotationY = 0f;
    private Vector2 groundStampPlacementScale = Vector2.one;
    private Mesh groundStampPreviewMesh;
    private Material groundStampPreviewMaterial;
    private Texture groundStampPreviewMaterialTexture;

    private GroundBrushMode groundBrushMode = GroundBrushMode.ShapeAdd;
    private GroundBrushShape groundBrushShape = GroundBrushShape.Circle;
    // 地面刷是“角色尺度的编辑笔刷”，不是用来一笔刷完整地图的离线工具。
    // 大笔刷的 CPU 成本会按面积平方增长，所以这里把日常笔刷上限收进可交互范围；
    // 大范围铺地以后应走“区域填充 / 地块填充 / 正式烘焙”，不要混进 MouseDrag。
    private const float GroundBrushDesignerMaxSize = 50f;
    private const float GroundBrushLargeSizeWarning = 20f;

    private float groundBrushSize = 2.0f;
    private float groundBrushHardness = 0.85f;
    private bool groundBrushContinuous = true;
    private bool groundBrushPreviewMask = true;
    private bool groundOverlayEraseMode = false;
    private int deprecatedGroundOverlayLayerSlot = 2; // Deprecated: spline/line now uses GroundSpline Mesh objects.
    private GroundBrushDebugView groundBrushDebugView = GroundBrushDebugView.Normal;
    private int groundBrushDebugGrid = 48;
    private Vector3 lastGroundBrushPosition;
    private bool hasValidGroundBrushPosition;
    private bool groundBrushPainting = false;
    private bool terrainPaintHoleRestoreMode = false;
    private bool hasLastGroundBrushPaintPosition = false;
    private Vector3 lastGroundBrushPaintPosition;
    private bool hasGroundOverlayStraightLineAnchor = false;
    private Vector3 groundOverlayStraightLineAnchor;
    private int groundBrushStampSeedCounter = 1;
    private bool cleanupTerrainDecorationsOnGroundErase = true;
    // 地面刷拖动时只改数据贴图，不立刻重烘焙 URP/Lit 地面贴图。
    // 否则每一笔都会触发贴图重建/Importer 检查，编辑体验会像 Unity 在反复编译。
    private bool groundBrushVisualBakeDirty = false;
    private BaseGroundBlock groundBrushVisualBakeDirtyBlock = null;
    private bool groundBrushLivePreviewEnabled = true;
    private double lastGroundBrushLivePreviewTime = 0d;
    private const double GroundBrushLivePreviewInterval = 0.035d;

    // 企业式地面绘制：鼠标拖动时只改 CPU 数据 + 局部 Lit 预览，
    // 不把 Index/Mask/Weight 这些数据贴图每个 stamp 都 Apply 到 GPU。
    // Apply 统一延迟到一笔结束，避免左键拖动时被纹理上传卡住。
    private readonly HashSet<Texture2D> deferredGroundPaintTextureUploads = new HashSet<Texture2D>();
    private bool groundBrushHasPendingPreviewBounds = false;
    private Bounds groundBrushPendingPreviewBounds;
    private bool groundEraseTouchedDuringStroke = false;
    private Bounds groundEraseStrokeWorldBounds;

    // 地面刷撤销：一次鼠标按下到松开，必须合并成一个 Undo。
    // 否则 Ctrl+Z 只撤回一小段印章，画错材质时会非常折磨。
    private bool groundBrushStrokeUndoActive = false;
    private int groundBrushStrokeUndoGroup = -1;
    private BaseGroundBlock groundBrushStrokeUndoBlock = null;
    private readonly HashSet<UnityEngine.Object> groundBrushStrokeUndoRegisteredObjects = new HashSet<UnityEngine.Object>();

    private BaseGroundBlock activeGroundBlock;
    private Terrain activeGroundTerrain;
    private float terrainSurfaceBrushOpacity = 0.35f;
    private bool terrainBrushStrokeUndoActive = false;
    private int terrainBrushStrokeUndoGroup = -1;

    // TerrainLayer 矩形填充：只给真正的地面纹理使用。
    // 与笔刷分开，按 Scene 里拖出的矩形一次性写入 alphamap。
    private bool terrainRectFillMode = false;
    private bool terrainRectFillDragging = false;
    private int terrainRectFillControlId = 0;
    private Vector3 terrainRectFillStartPosition;
    private Vector3 terrainRectFillEndPosition;
    private Vector2 terrainRectFillStartGuiPosition;
    private Vector2 terrainRectFillEndGuiPosition;

    private bool sceneGuiHookRegistered = false;
    private bool showSurfaceMaterialMapFoldout = true;
    private bool showSmartSurfaceCompositionFoldout = false;

    private bool placementMode = false;
    private bool continuousPlace = true;
    private bool snapToGrid = true;
    private bool playSoundOnPlace = true;
    private bool useLightweightPreview = false;
    private bool raycastSceneSurface = false;
    private bool requireGroundShapeForPlacement = true;
    private bool snapPlacementToGroundBlockHeight = true;
    private bool overlapCheckOnlyTerrainDecorations = true;
    private float gridSize = 1f;
    private float placementY = 0f;
    private const float TerrainDecorationPreviewRotationStep = 15f;
    private const float TerrainDecorationPreviewHeightStep = 0.10f;
    private float terrainDecorationPreviewRotationY = 0f;
    private float terrainDecorationPreviewHeightOffset = 0f;
    private Vector3 terrainDecorationPreviewBasePosition = Vector3.zero;
    private string parentPath = DefaultParentPath;

    private GameObject previewInstance;
    private TerrainDecorationPlacementResult currentPreviewResult;
    private Material validPreviewMaterial;
    private Material invalidPreviewMaterial;
    private readonly List<Renderer> previewRenderers = new List<Renderer>();
    private Vector3 lastPreviewPosition;
    private bool hasValidPreviewPosition;
    private bool canPlaceAtPreviewPosition = true;
    private bool lastAppliedPreviewValid = false;
    private bool hasAppliedPreviewMaterial = false;
    private readonly Collider[] placementOverlapBuffer = new Collider[48];
    private static readonly Color ValidPreviewColor = new Color(0.10f, 1.00f, 0.25f, 0.42f);
    private static readonly Color InvalidPreviewColor = new Color(1.00f, 0.08f, 0.04f, 0.48f);
    private static readonly Color GroundBrushAddColor = new Color(0.18f, 0.95f, 0.36f, 0.72f);
    private static readonly Color GroundBrushEraseColor = new Color(1.00f, 0.22f, 0.16f, 0.72f);
    private static readonly Color GroundBrushSurfaceColor = new Color(1.00f, 0.08f, 0.78f, 0.82f);

    private readonly string[] categoryLabels =
    {
        "全部", "普通", "箱体", "墙体", "柱体", "地面装饰", "苔藓", "残骸", "管线", "遮挡体", "机关", "自定义"
    };

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        var window = GetWindow<SkyPrisonMapObjectPlacementToolWindow>("地图对象放置工具");
        window.Show();
        window.ApplyExpandedFixedWindowSize();
    }

    [MenuItem(LegacyMenuPath)]
    public static void OpenLegacyWindow()
    {
        OpenWindow();
    }

    public static void OpenWindowWithDefinition(TerrainDecorationDefinition definition)
    {
        var window = GetWindow<SkyPrisonMapObjectPlacementToolWindow>("地图对象放置工具");
        window.Show();
        window.ApplyExpandedFixedWindowSize();
        window.currentKind = PlacementObjectKind.TerrainDecoration;
        window.currentPage = ToolPage.Place;
        window.RefreshDefinitions();
        window.SelectDefinition(definition);
        FocusSceneView();
    }

    public static void OpenWindowWithDefinitionAndEnterPlacement(TerrainDecorationDefinition definition)
    {
        var window = GetWindow<SkyPrisonMapObjectPlacementToolWindow>("地图对象放置工具");
        window.Show();
        window.ApplyExpandedFixedWindowSize();
        window.currentKind = PlacementObjectKind.TerrainDecoration;
        window.currentPage = ToolPage.Place;
        window.RefreshDefinitions();
        window.SelectDefinition(definition);
        window.SetPlacementMode(definition != null);
        FocusSceneView();
    }

    private static void FocusSceneView()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
            sceneView = GetWindow<SceneView>();

        if (sceneView != null)
        {
            sceneView.Focus();
            sceneView.Repaint();
        }
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("地图对象放置工具");
        ApplyExpandedFixedWindowSize();

        // 旧窗口实例可能缓存了旧路径。地形装饰物统一落到 StructureRoot。
        if (string.IsNullOrWhiteSpace(parentPath) || parentPath == "WorldRoot/BackgroundRoot")
            parentPath = DefaultParentPath;

        RefreshDefinitions();
        RefreshPlacedCache();
        EnsureSceneGuiHook();

        Undo.undoRedoPerformed -= OnGroundBrushUndoRedoPerformed;
        Undo.undoRedoPerformed += OnGroundBrushUndoRedoPerformed;
    }

    private void OnGroundBrushUndoRedoPerformed()
    {
        // Texture2D / BaseGroundBlock 被 Unity Undo 还原后，必须重新刷新地面预览，
        // 否则数据已经回去了但 Scene 里的 GroundVisual 还停留在上一张预览贴图。
        groundBrushPainting = false;
        hasLastGroundBrushPaintPosition = false;
        groundBrushVisualBakeDirty = false;
        groundBrushVisualBakeDirtyBlock = null;
        ResetGroundBrushStrokeUndoState();

        if (activeGroundBlock == null)
            activeGroundBlock = FindActiveGroundBlock();

        if (activeGroundBlock != null)
            activeGroundBlock.MarkGroundDataDirty(false);

        ApplyGroundVisualDisplayModeToAllBlocks();
        SceneView.RepaintAll();
        Repaint();
    }

    private void BeginGroundBrushStrokeUndo(BaseGroundBlock block)
    {
        if (block == null)
            return;

        EndGroundBrushStrokeUndo();

        groundBrushStrokeUndoActive = true;
        groundBrushStrokeUndoBlock = block;
        groundBrushStrokeUndoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(GetGroundBrushUndoLabel());
        groundBrushStrokeUndoRegisteredObjects.Clear();

        RegisterGroundBrushStrokeUndoObject(block);
    }

    private void RegisterGroundBrushStrokeUndoObject(UnityEngine.Object obj)
    {
        if (!groundBrushStrokeUndoActive || obj == null)
            return;

        if (groundBrushStrokeUndoRegisteredObjects.Contains(obj))
            return;

        groundBrushStrokeUndoRegisteredObjects.Add(obj);
        Undo.RegisterCompleteObjectUndo(obj, GetGroundBrushUndoLabel());
    }

    private void RegisterGroundBrushStrokeUndoObjects(BaseGroundBlock block)
    {
        if (block == null)
            return;

        RegisterGroundBrushStrokeUndoObject(block);
        RegisterGroundBrushStrokeUndoObject(block.groundShapeMask);
        RegisterGroundBrushStrokeUndoObject(block.surfaceMaterialIndexMap);
        RegisterGroundBrushStrokeUndoObject(block.surfaceMaterialPreviewTexture);

        if (block.surfaceMaterialWeightMaps != null)
        {
            foreach (Texture2D map in block.surfaceMaterialWeightMaps)
                RegisterGroundBrushStrokeUndoObject(map);
        }
    }

    private void EndGroundBrushStrokeUndo()
    {
        if (!groundBrushStrokeUndoActive)
            return;

        int group = groundBrushStrokeUndoGroup;
        ResetGroundBrushStrokeUndoState();

        if (group >= 0)
            Undo.CollapseUndoOperations(group);
    }

    private void ResetGroundBrushStrokeUndoState()
    {
        groundBrushStrokeUndoActive = false;
        groundBrushStrokeUndoGroup = -1;
        groundBrushStrokeUndoBlock = null;
        groundBrushStrokeUndoRegisteredObjects.Clear();
    }

    private string GetGroundBrushUndoLabel()
    {
        switch (groundBrushMode)
        {
            case GroundBrushMode.ShapeAdd: return "Paint ground shape";
            case GroundBrushMode.ShapeErase: return "Erase ground shape";
            case GroundBrushMode.SurfaceMaterial: return "Paint ground surface material";
            default: return "Paint ground";
        }
    }

    private void OnFocus()
    {
        // 地表材质经常在另一个页签中新建/修改。窗口重新获得焦点时刷新一次，避免列表不同步。
        RefreshDefinitions();
        RefreshPlacedCache();
        Repaint();
    }

    private void OnProjectChange()
    {
        // 新增 / 删除 GroundSurfaceMaterialDefinition 或地形装饰物资产后，Unity 会触发这里。
        // 这里不进烘焙，只刷新资产列表和当前选择。
        RefreshDefinitions();
        RefreshPlacedCache();
        Repaint();
    }

    private void OnDisable()
    {
        EndGroundBrushStrokeUndo();
        Undo.undoRedoPerformed -= OnGroundBrushUndoRedoPerformed;

        // 关闭工具时必须恢复所有真实地面显示，避免停留在调试模式导致 GroundVisual 被隐藏。
        RestoreGroundVisualDisplayForAllBlocks();

        SceneView.duringSceneGui -= OnSceneGUI;
        sceneGuiHookRegistered = false;
        DestroyPreview();
        DestroyPreviewMaterial();
        DestroyItemPreview();
        DestroyTerrainSurfaceBrushPreviewTextures();
    }

    private void OnDestroy()
    {
        DestroyPreview();
        DestroyPreviewMaterial();
        DestroyItemPreview();
        DestroyTerrainSurfaceBrushPreviewTextures();
    }

    private void EnsureSceneGuiHook()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        sceneGuiHookRegistered = true;
    }

    private void OnInspectorUpdate()
    {
        // 禁止轮询刷新。Scene 工具只在进入/退出模式、鼠标输入、参数变化时刷新。
        // 这里如果每帧 Repaint，会在鼠标悬停 GroundSpline / Gizmo 时形成刷新循环。
        if (placementMode && !sceneGuiHookRegistered)
            EnsureSceneGuiHook();
    }

    private void OnGUI()
    {
        if (placementMode && !sceneGuiHookRegistered)
            EnsureSceneGuiHook();

        // 顶部吸附先关闭：折叠/展开保持同一个窗口位置。

        DrawWindowHeader();
        DrawCollapseHandle();

        if (compactMode || topBarMode)
        {
            DrawCompactBody();
            return;
        }

        DrawObjectKindTabs();
        DrawToolPageTabs();

        if (currentPage == ToolPage.Place)
        {
            DrawPlacePage();
        }
        else
        {
            DrawPlacedPage();
        }
    }

    private void DrawWindowHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("地图对象放置工具", EditorStyles.boldLabel, GUILayout.Width(132f));

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44f)))
        {
            RefreshDefinitions();
            RefreshPlacedCache();
        }

        using (new EditorGUI.DisabledScope(!CanOpenCurrentKindEditor()))
        {
            if (GUILayout.Button("打开编辑器", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                OpenCurrentKindEditor();
        }

        GUILayout.FlexibleSpace();
        DrawPlacementModeToolbarButton(72f);
        EditorGUILayout.EndHorizontal();
    }

    private bool CanTogglePlacementMode()
    {
        switch (currentKind)
        {
            case PlacementObjectKind.TerrainDecoration:
                return selectedDefinition != null;
            case PlacementObjectKind.GroundSurfaceMaterial:
                return IsTerrainDefaultToolSelected() || groundBrushMode != GroundBrushMode.SurfaceMaterial || selectedSurfaceMaterial != null;
            case PlacementObjectKind.Unit:
                return selectedUnitDefinition != null && selectedUnitDefinition.prefab != null;
            case PlacementObjectKind.Item:
                return selectedItemDefinition != null;
            default:
                return false;
        }
    }

    private void DrawPlacementModeToolbarButton(float width)
    {
        using (new EditorGUI.DisabledScope(!CanTogglePlacementMode()))
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = placementMode ? new Color(0.70f, 0.16f, 0.08f, 1f) : Color.white;
            if (GUILayout.Button(placementMode ? "退出放置" : "进入放置", EditorStyles.toolbarButton, GUILayout.Width(width)))
                SetPlacementMode(!placementMode);
            GUI.backgroundColor = old;
        }
    }

    private void DrawPlacementModeLargeButton()
    {
        using (new EditorGUI.DisabledScope(!CanTogglePlacementMode()))
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = placementMode ? new Color(0.70f, 0.16f, 0.08f, 1f) : Color.white;
            if (DrawClampedButton(placementMode ? "退出 Scene 放置模式" : "进入 Scene 放置模式", 26f))
                SetPlacementMode(!placementMode);
            GUI.backgroundColor = old;
        }
    }

    private float GetActiveButtonMaxWidth()
    {
        if (activeSettingsButtonMaxWidth > 1f)
            return activeSettingsButtonMaxWidth;

        float fallback = position.width - 20f;
        return Mathf.Max(1f, fallback);
    }

    private bool DrawClampedButton(string label, float height = 22f)
    {
        float width = GetActiveButtonMaxWidth();
        Rect rowRect = GUILayoutUtility.GetRect(width, width, height, height, GUILayout.ExpandWidth(false));
        rowRect.x = 0f;
        rowRect.width = Mathf.Max(1f, width);
        return GUI.Button(rowRect, label);
    }

    private bool DrawClampedButtonPair(string leftLabel, string rightLabel, out bool leftClicked, out bool rightClicked, float height = 22f)
    {
        float width = GetActiveButtonMaxWidth();
        Rect rowRect = GUILayoutUtility.GetRect(width, width, height, height, GUILayout.ExpandWidth(false));
        rowRect.x = 0f;
        rowRect.width = Mathf.Max(1f, width);

        float gap = 6f;
        float buttonWidth = Mathf.Max(1f, (rowRect.width - gap) * 0.5f);
        Rect leftRect = new Rect(rowRect.x, rowRect.y, buttonWidth, rowRect.height);
        Rect rightRect = new Rect(leftRect.xMax + gap, rowRect.y, buttonWidth, rowRect.height);

        leftClicked = GUI.Button(leftRect, leftLabel);
        rightClicked = GUI.Button(rightRect, rightLabel);
        return leftClicked || rightClicked;
    }



    private Rect GetClampedControlRect(float height = 0f)
    {
        float width = GetActiveButtonMaxWidth();
        float h = height > 0f ? height : EditorGUIUtility.singleLineHeight;
        Rect rect = GUILayoutUtility.GetRect(width, width, h, h, GUILayout.ExpandWidth(false));
        rect.x = 0f;
        rect.width = Mathf.Max(1f, width);
        return rect;
    }

    private UnityEngine.Object DrawClampedObjectField(string label, UnityEngine.Object value, System.Type objectType, bool allowSceneObjects)
    {
        return EditorGUI.ObjectField(GetClampedControlRect(), label, value, objectType, allowSceneObjects);
    }

    private string DrawClampedTextField(string label, string value)
    {
        return EditorGUI.TextField(GetClampedControlRect(), label, value);
    }

    private float DrawClampedFloatField(string label, float value)
    {
        return EditorGUI.FloatField(GetClampedControlRect(), label, value);
    }

    private int DrawClampedPopup(string label, int selectedIndex, string[] displayedOptions)
    {
        return EditorGUI.Popup(GetClampedControlRect(), label, selectedIndex, displayedOptions);
    }

    private float DrawClampedSlider(string label, float value, float leftValue, float rightValue)
    {
        return EditorGUI.Slider(GetClampedControlRect(), label, value, leftValue, rightValue);
    }

    private int DrawClampedIntSlider(string label, int value, int leftValue, int rightValue)
    {
        return EditorGUI.IntSlider(GetClampedControlRect(), label, value, leftValue, rightValue);
    }

    private Color DrawClampedColorField(string label, Color value)
    {
        return EditorGUI.ColorField(GetClampedControlRect(), label, value);
    }

    private void DrawCollapseHandle()
    {
        Rect rect = GUILayoutUtility.GetRect(1f, 16f, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, hover ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.035f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height * 0.5f, rect.width, 1f), new Color(1f, 1f, 1f, 0.14f));

        string triangle = (compactMode || topBarMode) ? "▼" : "▲";
        string tooltip = (compactMode || topBarMode) ? "展开窗口" : "折叠窗口";
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            normal = { textColor = hover ? Color.white : new Color(0.78f, 0.78f, 0.80f, 1f) }
        };
        GUI.Label(rect, new GUIContent(triangle, tooltip), style);
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            if (compactMode || topBarMode)
                RestoreExpandedWindow();
            else
                EnterCompactWindowMode();
            e.Use();
        }
    }

    private void DetectTopEdgeAutoHide()
    {
        // 保留占位，不自动吸附，避免窗口折叠后跳到左上角。
    }

    private void DrawCompactBody()
    {
        string kind = GetKindLabel(currentKind);
        string selected = GetCurrentSelectionLabel();
        bool splineBrush = currentKind == PlacementObjectKind.GroundSurfaceMaterial && IsSelectedSurfaceMaterialSpline();

        if (topBarMode)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"{kind}：{selected}", EditorStyles.boldLabel, GUILayout.MinWidth(180f));

            DrawPlacementModeToolbarButton(64f);

            if (GUILayout.Button("已摆放", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                RestoreExpandedWindow();
                currentPage = ToolPage.Placed;
                RefreshPlacedCache();
            }

            if (GUILayout.Button("展开", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                RestoreExpandedWindow();

            EditorGUILayout.EndHorizontal();
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"{kind}  /  当前：{selected}", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (!splineBrush)
            DrawPlacementModeLargeButton();
        else
            EditorGUILayout.HelpBox("样条图案请点击上方“打开几何路径绘制器”。普通 Scene 放置模式不再用于马路线/管线等制式线条。", MessageType.None);
        if (GUILayout.Button("已摆放", GUILayout.Width(72f), GUILayout.Height(24f)))
        {
            RestoreExpandedWindow();
            currentPage = ToolPage.Placed;
            RefreshPlacedCache();
        }
        if (GUILayout.Button("展开", GUILayout.Width(64f), GUILayout.Height(24f)))
            RestoreExpandedWindow();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("折叠模式会实际压缩窗口高度，只保留当前选择与放置控制。", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void SaveExpandedWindowRect()
    {
        if (compactMode || topBarMode)
            return;

        lastExpandedWindowRect = position;
        hasSavedExpandedWindowRect = true;
    }

    private void EnterCompactWindowMode()
    {
        SaveExpandedWindowRect();
        compactMode = true;
        topBarMode = false;
        ApplyCollapsedWindowHeight(CompactWindowHeight);
        Repaint();
    }

    private void EnterTopBarWindowMode()
    {
        EnterCompactWindowMode();
    }

    private void RestoreExpandedWindow()
    {
        compactMode = false;
        topBarMode = false;
        ApplyExpandedFixedWindowSize();
        Repaint();
    }

    private void ApplyExpandedFixedWindowSize()
    {
        ApplyWindowRectKeepingCurrentPosition(
            ExpandedFixedWidth,
            ExpandedFixedHeight,
            new Vector2(ExpandedFixedWidth, ExpandedFixedHeight),
            new Vector2(ExpandedFixedWidth, ExpandedFixedHeight));
    }

    private void ApplyCollapsedWindowHeight(float height)
    {
        Rect rect = position;
        ApplyWindowRectKeepingCurrentPosition(
            Mathf.Max(rect.width, ExpandedMinWidth),
            height,
            new Vector2(ExpandedMinWidth, height),
            new Vector2(10000f, height));
    }

    private void ApplyWindowRectKeepingCurrentPosition(float width, float height, Vector2 newMinSize, Vector2 newMaxSize)
    {
        Rect rect = position;
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        float keepX = rect.x;
        float keepY = rect.y;

        // 先解除上一状态的尺寸约束，再套新状态，避免从“固定高度折叠态”
        // 切回展开态时出现 minSize / maxSize 瞬间冲突导致窗口被 Unity 推走。
        minSize = new Vector2(1f, 1f);
        maxSize = new Vector2(10000f, 10000f);

        Rect target = new Rect(keepX, keepY, width, height);
        position = target;

        minSize = newMinSize;
        maxSize = newMaxSize;
        position = target;

        // Unity EditorWindow 在 min/maxSize 变化后的当前 Layout 末尾有时会再做一次窗口约束，
        // 这里延迟一帧把左上角坐标压回去，避免折叠/展开时窗口跳位。
        EditorApplication.delayCall += () =>
        {
            if (this == null)
                return;

            Rect delayed = position;
            delayed.x = keepX;
            delayed.y = keepY;
            delayed.width = width;
            delayed.height = height;
            position = delayed;
            Repaint();
        };
    }

    private void DrawObjectKindTabs()
    {
        EditorGUILayout.BeginHorizontal("box");
        GUILayout.Label("模块书签", GUILayout.Width(70f));

        int current = GetBookmarkIndex(currentKind);
        string[] labels = ToolBookmarkRegistry
            .Select(x => x.Enabled ? x.Label : $"{x.Label}（未接入）")
            .ToArray();

        int next = EditorGUILayout.Popup(Mathf.Max(0, current), labels);
        next = Mathf.Clamp(next, 0, ToolBookmarkRegistry.Length - 1);

        ToolBookmark nextBookmark = ToolBookmarkRegistry[next];
        if (!nextBookmark.Enabled)
        {
            EditorGUILayout.EndHorizontal();
            return;
        }

        if (next != current)
            SwitchBookmark(nextBookmark.Kind);

        EditorGUILayout.EndHorizontal();
    }

    private int GetBookmarkIndex(PlacementObjectKind kind)
    {
        for (int i = 0; i < ToolBookmarkRegistry.Length; i++)
        {
            if (ToolBookmarkRegistry[i].Kind == kind)
                return i;
        }

        return 0;
    }

    private void SwitchBookmark(PlacementObjectKind nextKind)
    {
        if (currentKind == nextKind)
            return;

        PlacementObjectKind previousKind = currentKind;
        currentKind = nextKind;

        if (placementMode)
            SetPlacementMode(false);

        // 离开“地表材质/地面刷”通道时，必须立即恢复 GroundVisual。
        // 否则 ShapeMask/材质调试通道会残留，和地形装饰物放置通道串在一起。
        if (previousKind == PlacementObjectKind.GroundSurfaceMaterial && currentKind != PlacementObjectKind.GroundSurfaceMaterial)
            RestoreGroundVisualDisplayForAllBlocks();
        else if (currentKind == PlacementObjectKind.GroundSurfaceMaterial)
            ApplyGroundVisualDisplayModeToAllBlocks();

        RefreshDefinitions();

        if (currentKind == PlacementObjectKind.Unit)
        {
            LoadUnitDefinitions();
            RefreshPlacedUnitCache();
        }

        if (currentKind == PlacementObjectKind.Item)
        {
            LoadItemDefinitions();
            RefreshPlacedItemCache();
        }

        Repaint();
    }

    private void DrawKindButton(PlacementObjectKind kind, string label, bool enabled)
    {
        using (new EditorGUI.DisabledScope(!enabled))
        {
            bool selected = currentKind == kind;
            Color old = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = accent;
            if (GUILayout.Button(label, EditorStyles.miniButtonMid, GUILayout.Height(24f)))
            {
                SwitchBookmark(kind);
            }
            GUI.backgroundColor = old;
        }
    }

    private void DrawToolPageTabs()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        DrawPageTab(ToolPage.Place, "放置");
        DrawPageTab(ToolPage.Placed, "已摆放");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPageTab(ToolPage page, string label)
    {
        bool selected = currentPage == page;
        Color old = GUI.backgroundColor;
        if (selected)
            GUI.backgroundColor = accent;
        if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(72f)))
        {
            currentPage = page;
            if (page == ToolPage.Placed)
                RefreshPlacedCache();
        }
        GUI.backgroundColor = old;
    }

    private void DrawFutureKindPlaceholder()
    {
        EditorGUILayout.HelpBox($"{GetKindLabel(currentKind)} 摆放页先保留入口，后续接入。当前已实现：地形装饰物、地表材质地面刷。", MessageType.Info);
    }

    private void DrawPlacePage()
    {
        if (currentKind == PlacementObjectKind.GroundSurfaceMaterial)
        {
            DrawGroundSurfaceMaterialPlacePage();
            return;
        }

        if (currentKind == PlacementObjectKind.Unit)
        {
            DrawUnitPlacePage();
            return;
        }

        if (currentKind == PlacementObjectKind.Item)
        {
            DrawItemPlacePage();
            return;
        }

        if (currentKind != PlacementObjectKind.TerrainDecoration)
        {
            DrawFutureKindPlaceholder();
            return;
        }

        DrawFilters();
        DrawDefinitionList();
        DrawPlacementSettings();
    }

    private void DrawFilters()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("筛选", EditorStyles.boldLabel);
        search = EditorGUILayout.TextField("搜索", search);
        categoryIndex = EditorGUILayout.Popup("主分类", categoryIndex, categoryLabels);
        List<string> subCategories = GetSubCategoryOptions();
        subCategoryIndex = Mathf.Clamp(subCategoryIndex, 0, Mathf.Max(0, subCategories.Count - 1));
        subCategoryIndex = EditorGUILayout.Popup("子分类", subCategoryIndex, subCategories.ToArray());
        EditorGUILayout.EndVertical();
    }

    private void DrawDefinitionList()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, ModuleListFixedHeight, ModuleListFixedHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        List<TerrainDecorationDefinition> filtered = GetFilteredDefinitions();
        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * 58f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        listScroll = GUI.BeginScrollView(viewRect, listScroll, contentRect, false, true);
        float y = 0f;
        for (int i = 0; i < filtered.Count; i++)
        {
            Rect row = new Rect(0f, y, contentRect.width, 54f);
            DrawDefinitionRow(row, filtered[i]);
            y += 58f;
        }
        GUI.EndScrollView();
    }

    private void DrawDefinitionRow(Rect rect, TerrainDecorationDefinition definition)
    {
        bool selected = selectedDefinition == definition;
        bool hover = rect.Contains(Event.current.mousePosition);
        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.14f, 0.10f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, 42f, 42f);
        Texture icon = GetDefinitionPreview(definition);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.20f, 0.20f, 0.22f, 1f));

        Rect textRect = new Rect(iconRect.xMax + 8f, rect.y + 6f, rect.width - iconRect.width - 22f, 42f);
        GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 20f), GetDisplayName(definition), EditorStyles.boldLabel);
        GUI.Label(new Rect(textRect.x, textRect.y + 20f, textRect.width, 18f), GetCategoryLabel(definition) + " / " + definition.subCategory, EditorStyles.miniLabel);

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            SelectDefinition(definition);
    }

    private void DrawPlacementSettings()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, SettingsContainerMinHeight, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f, 1f));

        Rect viewRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
        float contentWidth = Mathf.Max(10f, viewRect.width - 18f);
        Rect contentRect = new Rect(0f, 0f, contentWidth, Mathf.Max(viewRect.height, 520f));

        placementSettingsScroll = GUI.BeginScrollView(viewRect, placementSettingsScroll, contentRect, false, true);
        activeSettingsButtonMaxWidth = contentRect.width;
        GUILayout.BeginArea(contentRect);

        EditorGUILayout.LabelField("放置设置", EditorStyles.boldLabel);
        DrawClampedObjectField("当前选择", selectedDefinition, typeof(TerrainDecorationDefinition), false);
        continuousPlace = EditorGUILayout.Toggle("连续放置", continuousPlace);
        playSoundOnPlace = EditorGUILayout.Toggle("放置时播放音效", playSoundOnPlace);
        useLightweightPreview = EditorGUILayout.Toggle("高速盒体预览（备用）", useLightweightPreview);
        raycastSceneSurface = EditorGUILayout.Toggle("检测场景表面", raycastSceneSurface);
        requireGroundShapeForPlacement = EditorGUILayout.Toggle("只允许放在有地面区域", requireGroundShapeForPlacement);
        using (new EditorGUI.DisabledScope(!requireGroundShapeForPlacement))
            snapPlacementToGroundBlockHeight = EditorGUILayout.Toggle("吸附 GroundBlock 高度", snapPlacementToGroundBlockHeight);
        using (new EditorGUI.DisabledScope(selectedDefinition == null || selectedDefinition.placementCollisionMode == TerrainDecorationPlacementCollisionMode.None || selectedDefinition.allowCollisionOverlap))
            overlapCheckOnlyTerrainDecorations = EditorGUILayout.Toggle("只检测已放置装饰物", overlapCheckOnlyTerrainDecorations);
        snapToGrid = EditorGUILayout.Toggle("吸附网格", snapToGrid);
        using (new EditorGUI.DisabledScope(!snapToGrid))
            gridSize = Mathf.Max(0.05f, DrawClampedFloatField("网格大小", gridSize));
        placementY = DrawClampedFloatField("放置高度 Y", placementY);
        parentPath = DrawClampedTextField("父节点路径", parentPath);

        if (selectedDefinition != null)
        {
            EditorGUILayout.HelpBox($"随机 PF：{(selectedDefinition.randomVariantOnPlace ? "开" : "关")} / 随机 MAT：{(selectedDefinition.randomMaterialOnPlace ? "开" : "关")} / 随机缩放：{(selectedDefinition.enableRandomScale ? "开" : "关")} / 随机视觉角度：{(selectedDefinition.enableVisualRandomRotation ? "开" : "关")}", MessageType.None);
        }

        bool splineBrush = currentKind == PlacementObjectKind.GroundSurfaceMaterial && IsSelectedSurfaceMaterialSpline();
        if (!splineBrush)
            DrawPlacementModeLargeButton();
        else
            EditorGUILayout.HelpBox("样条图案请点击上方“打开几何路径绘制器”。普通 Scene 放置模式不再用于马路线/管线等制式线条。", MessageType.None);
        EditorGUILayout.HelpBox("Scene 中：左键放置，右键 / Esc 取消。每次左键点击会按定义中的随机开关抽取 PF、MAT、缩放与视觉角度。", MessageType.Info);

        GUILayout.EndArea();
        activeSettingsButtonMaxWidth = -1f;
        GUI.EndScrollView();
    }


    private void DrawGroundSurfaceMaterialPlacePage()
    {
        DrawSurfaceMaterialFilters();
        DrawSurfaceMaterialList();
        DrawSurfaceMaterialSettings();
    }

    private void DrawSurfaceMaterialFilters()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("筛选", EditorStyles.boldLabel);
        surfaceMaterialSearch = EditorGUILayout.TextField("搜索", surfaceMaterialSearch);
        List<string> categories = GetSurfaceMaterialCategoryOptions();
        surfaceMaterialCategoryIndex = Mathf.Clamp(surfaceMaterialCategoryIndex, 0, Mathf.Max(0, categories.Count - 1));
        surfaceMaterialCategoryIndex = EditorGUILayout.Popup("分类", surfaceMaterialCategoryIndex, categories.ToArray());
        EditorGUILayout.EndVertical();
    }

    private void DrawSurfaceMaterialList()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, ModuleListFixedHeight, ModuleListFixedHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        List<GroundSurfaceMaterialDefinition> filtered = GetFilteredSurfaceMaterials();
        TerrainDefaultTool[] terrainTools = GetTerrainDefaultToolsForList();
        float contentHeight = Mathf.Max(viewRect.height, (terrainTools.Length + filtered.Count) * 64f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        surfaceMaterialListScroll = GUI.BeginScrollView(viewRect, surfaceMaterialListScroll, contentRect, false, true);
        float y = 0f;

        for (int i = 0; i < terrainTools.Length; i++)
        {
            Rect row = new Rect(0f, y, contentRect.width, 60f);
            DrawTerrainDefaultToolRow(row, terrainTools[i]);
            y += 64f;
        }

        for (int i = 0; i < filtered.Count; i++)
        {
            Rect row = new Rect(0f, y, contentRect.width, 60f);
            DrawSurfaceMaterialRow(row, filtered[i]);
            y += 64f;
        }
        GUI.EndScrollView();
    }

    private TerrainDefaultTool[] GetTerrainDefaultToolsForList()
    {
        return new[]
        {
            TerrainDefaultTool.RaiseSurface,
            TerrainDefaultTool.LowerSurface,
            TerrainDefaultTool.FlattenSurface,
            TerrainDefaultTool.PaintHole,
        };
    }

    private void DrawTerrainDefaultToolRow(Rect rect, TerrainDefaultTool tool)
    {
        bool selected = selectedTerrainDefaultTool == tool;
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.20f, 0.18f, 0.08f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 5f, rect.height), warningColor);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect iconRect = new Rect(rect.x + 10f, rect.y + 8f, 44f, 44f);
        DrawTerrainDefaultToolIcon(iconRect, tool, selected || hover);

        Rect textRect = new Rect(iconRect.xMax + 10f, rect.y + 8f, rect.width - iconRect.width - 26f, 44f);
        GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 20f), GetTerrainDefaultToolDisplayName(tool), EditorStyles.boldLabel);
        GUI.Label(new Rect(textRect.x, textRect.y + 22f, textRect.width, 18f), "Unity Terrain / Default Tool", EditorStyles.miniLabel);

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            selectedTerrainDefaultTool = tool;
            selectedSurfaceMaterial = null;
            if (tool == TerrainDefaultTool.FlattenSurface && activeGroundTerrain != null && hasValidGroundBrushPosition)
                terrainFlattenWorldHeight = SampleTerrainWorldHeight(activeGroundTerrain, lastGroundBrushPosition);
            Repaint();
        }
    }

    private void DrawTerrainDefaultToolIcon(Rect rect, TerrainDefaultTool tool, bool active)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.11f, 1f));
        Color old = Handles.color;
        Handles.BeginGUI();
        Handles.color = active ? warningColor : new Color(0.82f, 0.82f, 0.84f, 1f);

        float cx = rect.center.x;
        float cy = rect.center.y;
        switch (tool)
        {
            case TerrainDefaultTool.RaiseSurface:
                Handles.DrawAAPolyLine(3f,
                    new Vector3(rect.x + 7f, rect.yMax - 9f),
                    new Vector3(cx, rect.y + 9f),
                    new Vector3(rect.xMax - 7f, rect.yMax - 9f));
                break;
            case TerrainDefaultTool.LowerSurface:
                Handles.DrawAAPolyLine(3f,
                    new Vector3(rect.x + 7f, rect.y + 9f),
                    new Vector3(cx, rect.yMax - 9f),
                    new Vector3(rect.xMax - 7f, rect.y + 9f));
                break;
            case TerrainDefaultTool.FlattenSurface:
                Handles.DrawAAPolyLine(4f, new Vector3(rect.x + 7f, cy), new Vector3(rect.xMax - 7f, cy));
                Handles.DrawAAPolyLine(2f, new Vector3(cx, rect.y + 10f), new Vector3(cx, rect.yMax - 10f));
                break;
            case TerrainDefaultTool.PaintHole:
                Handles.DrawSolidDisc(new Vector3(cx, cy), Vector3.forward, 13f);
                Handles.color = new Color(0.10f, 0.10f, 0.11f, 1f);
                Handles.DrawSolidDisc(new Vector3(cx, cy), Vector3.forward, 7f);
                break;
        }

        Handles.EndGUI();
        Handles.color = old;
    }

    private void DrawSurfaceMaterialRow(Rect rect, GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return;

        bool selected = selectedSurfaceMaterial == material;
        bool hover = rect.Contains(Event.current.mousePosition);
        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.34f, 0.05f, 0.25f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 5f, rect.height), surfaceAccent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect iconRect = new Rect(rect.x + 10f, rect.y + 8f, 44f, 44f);
        Texture icon = GetSurfaceMaterialPreview(material);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, material.baseColor);

        Rect textRect = new Rect(iconRect.xMax + 10f, rect.y + 8f, rect.width - iconRect.width - 26f, 44f);
        GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 20f), GetDisplayName(material), EditorStyles.boldLabel);
        GUI.Label(new Rect(textRect.x, textRect.y + 22f, textRect.width, 18f), $"{material.category} / {material.surfaceType}", EditorStyles.miniLabel);

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            if (selectedSurfaceMaterial != material)
            {
                selectedTerrainDefaultTool = TerrainDefaultTool.None;
                selectedSurfaceMaterial = material;
                PushSelectedSplineMaterialToGeometryToolIfNeeded();
                Repaint();
            }
        }
    }

    private void PushSelectedSplineMaterialToGeometryToolIfNeeded()
    {
        if (selectedSurfaceMaterial == null)
            return;

        if (!IsSelectedSurfaceMaterialSpline())
            return;

        GroundSurfaceMaterialDefinition materialForTool = ReloadGroundSurfaceMaterialAsset(selectedSurfaceMaterial);
        if (materialForTool != null)
            selectedSurfaceMaterial = materialForTool;

        SkyPrisonGroundOverlaySplineGeometryTool.SyncFromPlacementFocus(
            selectedSurfaceMaterial,
            GetSelectedSplinePaintWidth(),
            selectedSurfaceMaterial != null ? Mathf.Clamp01(selectedSurfaceMaterial.splineOpacity) : terrainSurfaceBrushOpacity);
    }

    private void DrawSurfaceMaterialSettings()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, SettingsContainerMinHeight, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f, 1f));

        Rect viewRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
        float contentWidth = Mathf.Max(10f, viewRect.width - 18f);
        Rect contentRect = new Rect(0f, 0f, contentWidth, Mathf.Max(viewRect.height, 900f));

        surfaceMaterialSettingsScroll = GUI.BeginScrollView(viewRect, surfaceMaterialSettingsScroll, contentRect, false, true);
        activeSettingsButtonMaxWidth = contentRect.width;
        GUILayout.BeginArea(contentRect);

        EditorGUILayout.LabelField("地表材质设置", EditorStyles.boldLabel);
        if (IsTerrainDefaultToolSelected())
            EditorGUILayout.LabelField("当前选择", GetTerrainDefaultToolDisplayName(selectedTerrainDefaultTool));
        else
            DrawClampedObjectField("当前选择", selectedSurfaceMaterial, typeof(GroundSurfaceMaterialDefinition), false);

        if (selectedSurfaceMaterial != null)
        {
            EditorGUILayout.LabelField("Surface ID", selectedSurfaceMaterial.surfaceId);
            EditorGUILayout.LabelField("显示名", GetDisplayName(selectedSurfaceMaterial));
            EditorGUILayout.LabelField("分类", selectedSurfaceMaterial.category);
            EditorGUILayout.LabelField("地表类型", selectedSurfaceMaterial.surfaceType.ToString());
            EditorGUILayout.LabelField("摩擦系数", selectedSurfaceMaterial.friction.ToString("0.###"));
            EditorGUILayout.LabelField("脚步音声标签", selectedSurfaceMaterial.footstepAudioTag);
            EditorGUILayout.LabelField("绘制判定", IsSelectedSurfaceMaterialSpline() ? "几何路径对象 / GroundSpline Mesh" : (IsSelectedSurfaceMaterialStamp() ? "Unity Quad / MeshRenderer 贴花" : "TerrainLayer 地表"));
            EditorGUILayout.LabelField("分布模式", selectedSurfaceMaterial.textureDistributionMode.ToString());
        }
        else if (IsTerrainDefaultToolSelected())
        {
            EditorGUILayout.LabelField("工具来源", "Unity Terrain 默认工具");
            EditorGUILayout.LabelField("目标", GetTerrainDefaultToolOperationLabel(selectedTerrainDefaultTool));
        }

        GUILayout.Space(8f);
        DrawActiveGroundSurfaceMaterialMapControls();

        GUILayout.Space(8f);
        bool terrainDefaultTool = IsTerrainDefaultToolSelected();
        bool overlayBrush = !terrainDefaultTool && IsSelectedSurfaceMaterialOverlay();
        bool splineBrush = !terrainDefaultTool && IsSelectedSurfaceMaterialSpline();
        groundBrushMode = terrainDefaultTool ? GetGroundBrushModeForTerrainDefaultTool(selectedTerrainDefaultTool) : (splineBrush ? GroundBrushMode.SplineOverlay : (overlayBrush ? GroundBrushMode.StampOverlay : GroundBrushMode.SurfaceMaterial));

        EditorGUILayout.LabelField(terrainDefaultTool ? "Unity Terrain 默认工具" : (splineBrush ? "几何路径工具" : (overlayBrush ? "印花 / 贴花放置" : "Terrain 画笔")), EditorStyles.boldLabel);
        EditorGUILayout.LabelField("刷子模式", GetGroundBrushModeLabel(groundBrushMode));

        // Deprecated: GroundOverlay layer selection removed. SplinePattern now opens GroundSpline Mesh geometry tool.
        if (splineBrush)
        {
            // 不在 OnGUI / hover 阶段自动推送素材。
            // 样条素材同步只允许在“真正切换选中项”或点击“打开几何路径绘制器”时发生，
            // 否则鼠标悬停列表会反复触发几何窗口 Repaint，造成光标跳动。
            bool fixedSize = SelectedSurfaceMaterialUsesFixedOverlaySize();
            EditorGUILayout.LabelField("尺寸规则", fixedSize ? "固定素材尺寸" : "素材默认线宽");
            EditorGUILayout.LabelField("实际画线宽度", GetSelectedSplinePaintWidth().ToString("0.###") + (fixedSize ? " m（素材锁定）" : " m（读取默认线宽）"));
            EditorGUILayout.LabelField("默认线宽", selectedSurfaceMaterial != null ? selectedSurfaceMaterial.splineWorldWidth.ToString("0.###") + " m" : "-");
            EditorGUILayout.LabelField("盖印间距", selectedSurfaceMaterial != null ? selectedSurfaceMaterial.splineStampSpacing.ToString("0.###") + " m" : "-");
            EditorGUILayout.LabelField("直线辅助", "Shift + 左键：从上一落点连到当前落点");
            using (new EditorGUI.DisabledScope(selectedSurfaceMaterial == null))
            {
                if (DrawClampedButton("打开几何路径绘制器", 24f))
                {
                    // IMPORTANT: push the exact asset currently selected in this window.
                    // The spline geometry window may already be open and can otherwise keep the previous material.
                    GroundSurfaceMaterialDefinition materialForTool = ReloadGroundSurfaceMaterialAsset(selectedSurfaceMaterial);
                    if (materialForTool != null)
                        selectedSurfaceMaterial = materialForTool;

                    SkyPrisonGroundOverlaySplineGeometryTool.OpenForSurfaceMaterial(
                        materialForTool,
                        GetSelectedSplinePaintWidth(),
                        materialForTool != null ? Mathf.Clamp01(materialForTool.splineOpacity) : terrainSurfaceBrushOpacity);
                }
            }
        }
        else if (overlayBrush)
        {
            DrawGroundStampPlacementControls();
        }
        else
        {
            DrawTerrainSurfaceBrushPalette();
        }

        if (terrainDefaultTool)
        {
            terrainDefaultToolStrength = DrawClampedSlider(selectedTerrainDefaultTool == TerrainDefaultTool.PaintHole ? "洞口边缘力度" : "高度强度", terrainDefaultToolStrength, 0.01f, 5f);
            if (selectedTerrainDefaultTool == TerrainDefaultTool.FlattenSurface)
            {
                terrainFlattenWorldHeight = DrawClampedSlider("推平高度", terrainFlattenWorldHeight, 0f, GetActiveTerrainMaxWorldHeight());
                using (new EditorGUI.DisabledScope(activeGroundTerrain == null || !hasValidGroundBrushPosition))
                {
                    if (DrawClampedButton("从鼠标位置采样推平高度", 22f))
                        terrainFlattenWorldHeight = SampleTerrainWorldHeight(activeGroundTerrain, lastGroundBrushPosition);
                }
            }
        }

        if (!overlayBrush)
            groundBrushSize = DrawClampedSlider(splineBrush ? "Scene 预览范围" : "刷子尺寸", groundBrushSize, 0.25f, GroundBrushDesignerMaxSize);
        if (splineBrush && !groundOverlayEraseMode)
            EditorGUILayout.HelpBox($"当前样条图案预览按实际线宽显示：{GetSelectedSplinePaintWidth():0.###}m。上方绿色预览不再使用擦除 / 操作范围。", MessageType.None);
        if (!overlayBrush && groundBrushSize >= GroundBrushLargeSizeWarning)
        {
            EditorGUILayout.HelpBox(
                "当前是大笔刷。地面刷按像素面积计算，越大成本会平方增长；日常制作建议保持在角色尺度内。更大范围铺地后面应走区域填充，不走 MouseDrag 笔刷。",
                MessageType.None);
        }
        if (!splineBrush && !overlayBrush)
            groundBrushHardness = DrawClampedSlider("刷子硬度", groundBrushHardness, 0f, 1f);

        terrainSurfaceBrushOpacity = DrawClampedSlider(splineBrush ? "几何线不透明度" : (overlayBrush ? "贴花不透明度" : "刷子不透明度"), terrainSurfaceBrushOpacity, 0.02f, 1f);
        groundOverlayEraseMode = false;
        groundBrushContinuous = EditorGUILayout.Toggle(splineBrush ? "连续画线" : (overlayBrush ? "连续盖印" : "连续涂刷"), groundBrushContinuous);
        groundBrushPreviewMask = EditorGUILayout.Toggle("显示刷子预览", groundBrushPreviewMask);

        if (terrainDefaultTool)
            EditorGUILayout.HelpBox(selectedTerrainDefaultTool == TerrainDefaultTool.PaintHole ? "Paint Hole 会写入 TerrainData holes。Scene 中左键挖洞，按住 Shift 左键可补回洞；Ctrl+Z 支持整笔撤销。" : "高度工具会直接写入 TerrainData heightmap。Scene 中左键绘制；Ctrl+Z 支持整笔撤销。", MessageType.Info);
        else if (splineBrush)
            EditorGUILayout.HelpBox("样条图案不再写入 GroundOverlay 大贴图。请使用几何路径绘制器生成 GroundSpline Mesh 对象，材质和宽度来自当前地表材质数据。", MessageType.Info);
        else if (overlayBrush)
            EditorGUILayout.HelpBox("印花使用 Unity 默认 GameObject + MeshRenderer + Quad 生成。Scene 中会显示绿色纹理预览，左键点击放到 Terrain；生成后就是普通场景对象，可以直接用 Unity 自带 Transform 工具旋转、缩放、移动。", MessageType.Info);
        else
            EditorGUILayout.HelpBox("Terrain 地表纹理写入 TerrainLayer / alphamap。高度编辑使用 Unity Terrain 原生工具。", MessageType.Info);
        if (!splineBrush)
            DrawPlacementModeLargeButton();
        else
            EditorGUILayout.HelpBox("样条图案请点击上方“打开几何路径绘制器”。普通 Scene 放置模式不再用于马路线/管线等制式线条。", MessageType.None);
        using (new EditorGUI.DisabledScope(selectedSurfaceMaterial == null))
        {
            if (DrawClampedButton("打开地表材质编辑器", 24f))
                SkyPrisonEditorWindow.OpenWindowWithTab("地表材质", selectedSurfaceMaterial);
        }

        GUILayout.EndArea();
        activeSettingsButtonMaxWidth = -1f;
        GUI.EndScrollView();
    }

    private void DrawActiveGroundSurfaceMaterialMapControls()
    {
        activeGroundTerrain = FindActiveGroundTerrain();

        EditorGUILayout.BeginVertical("box");
        showSurfaceMaterialMapFoldout = EditorGUILayout.Foldout(showSurfaceMaterialMapFoldout, "Terrain 地表材质 / GroundRoot", true);
        if (showSurfaceMaterialMapFoldout)
        {
            EditorGUILayout.LabelField("生成位置", "WorldRoot/GroundRoot/GroundTerrain");
            EditorGUILayout.LabelField("目标 Layer", "7 : World3D");
            EditorGUILayout.ObjectField("当前 Terrain", activeGroundTerrain, typeof(Terrain), true);

            TerrainData terrainData = activeGroundTerrain != null ? activeGroundTerrain.terrainData : null;
            EditorGUILayout.ObjectField("TerrainData", terrainData, typeof(TerrainData), false);

            if (activeGroundTerrain == null || terrainData == null)
            {
                EditorGUILayout.HelpBox("当前 Scene 没有找到 WorldRoot/GroundRoot/GroundTerrain。请先在地图页面执行“生成 / 矫正 Terrain 到 MapBounds”。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("尺寸", $"{terrainData.size.x:0.##} × {terrainData.size.z:0.##} / 高度 {terrainData.size.y:0.##}");
                EditorGUILayout.LabelField("Alphamap", $"{terrainData.alphamapWidth} × {terrainData.alphamapHeight}");
                EditorGUILayout.LabelField("TerrainLayer", terrainData.terrainLayers != null ? terrainData.terrainLayers.Length.ToString() : "0");

                // 这些按钮只属于真正写入 TerrainLayer / Alphamap 的地面纹理。
                // 印花、贴花、画线、样条和 Unity Terrain 默认高度工具都不是 TerrainLayer 纹理，不能显示这些操作。
                bool terrainLayerMaterialSelected = IsTerrainLayerSurfaceMaterialSelected();

                if (terrainLayerMaterialSelected)
                {
                    if (DrawClampedButton("把当前地表材质加入 TerrainLayer", 22f))
                    {
                        EnsureSelectedSurfaceMaterialTerrainLayer(activeGroundTerrain, selectedSurfaceMaterial);
                        SceneView.RepaintAll();
                    }

                    if (DrawClampedButton("填满 Terrain 为当前材质", 22f))
                    {
                        FillActiveTerrainSurfaceMaterial(selectedSurfaceMaterial);
                        SceneView.RepaintAll();
                    }

                    Color oldBg = GUI.backgroundColor;
                    if (terrainRectFillMode)
                        GUI.backgroundColor = new Color(0.70f, 0.16f, 0.08f, 1f);
                    if (DrawClampedButton(terrainRectFillMode ? "退出矩形选区填充" : "矩形选区填充当前纹理", 22f))
                    {
                        terrainRectFillMode = !terrainRectFillMode;
                        terrainRectFillDragging = false;
                        terrainRectFillControlId = 0;
                        if (terrainRectFillMode && !placementMode)
                            SetPlacementMode(true);
                        SceneView.RepaintAll();
                        Repaint();
                    }
                    GUI.backgroundColor = oldBg;

                    if (terrainRectFillMode)
                        EditorGUILayout.HelpBox("Scene 中左键按住拖出矩形，松开后把矩形区域填充为当前地面纹理。右键或 Esc 退出 Scene 模式。", MessageType.None);
                }
                else
                {
                    terrainRectFillMode = false;
                    terrainRectFillDragging = false;
                    terrainRectFillControlId = 0;
                }
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawPlacedPage()
    {
        if (currentKind == PlacementObjectKind.TerrainDecoration)
        {
            DrawTerrainDecorationPlacedPage();
            return;
        }

        if (currentKind == PlacementObjectKind.GroundSurfaceMaterial)
        {
            DrawGroundSurfacePlacedPage();
            return;
        }

        if (currentKind == PlacementObjectKind.Unit)
        {
            DrawUnitPlacedPage();
            return;
        }

        if (currentKind == PlacementObjectKind.Item)
        {
            DrawItemPlacedPage();
            return;
        }

        DrawFutureKindPlaceholder();
    }

    private void DrawTerrainDecorationPlacedPage()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("已摆放地形装饰物", EditorStyles.boldLabel);
        if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            RefreshPlacedCache();
        EditorGUILayout.EndHorizontal();
        placedSearch = EditorGUILayout.TextField("搜索", placedSearch);
        EditorGUILayout.HelpBox("支持 Ctrl 离散多选、Shift 连续多选。多选后点击任意已选项的删除，会一次删除全部已选地形装饰物根节点。", MessageType.Info);
        EditorGUILayout.EndVertical();

        List<TerrainDecorationRuntimeBinder> list = GetFilteredPlacedBinders();
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, 220f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        float contentHeight = Mathf.Max(viewRect.height, list.Count * 72f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        placedScroll = GUI.BeginScrollView(viewRect, placedScroll, contentRect, false, true);
        float y = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            Rect row = new Rect(0f, y, contentRect.width, 68f);
            DrawPlacedRow(row, list[i], i, list);
            y += 72f;
        }
        GUI.EndScrollView();

        DrawPlacedToolbar(false);
    }

    private void DrawGroundSurfacePlacedPage()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("已摆放地表视觉物", EditorStyles.boldLabel);
        if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            RefreshPlacedCache();
        EditorGUILayout.EndHorizontal();
        placedSearch = EditorGUILayout.TextField("搜索", placedSearch);
        EditorGUILayout.HelpBox("这里记录地图上的 GroundStamp / RoadLine / GroundSpline。它们是地面视觉物，不参与角色前后遮挡。支持 Ctrl 离散多选、Shift 连续多选，多选后点击任意已选项删除可一起删除。", MessageType.Info);
        EditorGUILayout.EndVertical();

        List<PlacedSurfaceObject> list = GetFilteredPlacedSurfaceObjects();
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, 220f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        float contentHeight = Mathf.Max(viewRect.height, list.Count * 72f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        placedScroll = GUI.BeginScrollView(viewRect, placedScroll, contentRect, false, true);
        float y = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            Rect row = new Rect(0f, y, contentRect.width, 68f);
            DrawGroundSurfacePlacedRow(row, list[i], i, list);
            y += 72f;
        }
        GUI.EndScrollView();

        DrawPlacedToolbar(true);
    }

    private void DrawPlacedToolbar(bool surfaceList)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (!surfaceList)
        {
            if (GUILayout.Button("选择异常实例", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                SelectDirtyPlacedInstances();
            if (GUILayout.Button("删除异常实例", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                DeleteDirtyPlacedInstances();
        }

        int selectedCount = CountCurrentPlacedSelection(surfaceList);
        using (new EditorGUI.DisabledScope(selectedCount <= 0))
        {
            if (GUILayout.Button($"删除已选({selectedCount})", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                DeleteSelectedPlacedObjectsWithConfirm(surfaceList);
            if (GUILayout.Button("清空选择", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                ClearPlacedSelection();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPlacedRow(Rect rect, TerrainDecorationRuntimeBinder binder, int index, List<TerrainDecorationRuntimeBinder> visibleList)
    {
        if (binder == null)
            return;

        GameObject go = binder.gameObject;
        int id = go != null ? go.GetInstanceID() : 0;
        bool selected = id != 0 && placedSelectionIds.Contains(id);
        bool unitySelected = Selection.activeGameObject == go;
        bool hover = rect.Contains(Event.current.mousePosition);
        bool dirty = IsDirtyPlacedInstance(binder);

        if (selected || unitySelected)
        {
            EditorGUI.DrawRect(rect, new Color(0.25f, 0.18f, 0.12f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (dirty)
        {
            EditorGUI.DrawRect(rect, new Color(0.26f, 0.20f, 0.08f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), warningColor);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect selectionRect = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 210f), rect.height);
        HandlePlacedRowSelection(selectionRect, index, visibleList.Select(x => x != null ? x.gameObject : null).ToList(), false);

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 8f, 44f, 44f);
        Texture icon = GetDefinitionPreview(binder.definition);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.20f, 0.20f, 0.22f, 1f));

        Rect textRect = new Rect(iconRect.xMax + 8f, rect.y + 6f, Mathf.Max(80f, rect.width - 260f), 58f);
        GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 20f), go.name, EditorStyles.boldLabel);
        string defName = binder.definition != null ? GetDisplayName(binder.definition) : "Definition 丢失";
        string status = dirty ? "  ⚠ 异常：无模型或定义缺失" : "";
        GUI.Label(new Rect(textRect.x, textRect.y + 20f, textRect.width, 18f), $"{defName} / {binder.selectedVariantId}{status}", EditorStyles.miniLabel);
        Vector3 p = go.transform.position;
        GUI.Label(new Rect(textRect.x, textRect.y + 38f, textRect.width, 18f), $"位置 X {p.x:0.##} / Y {p.y:0.##} / Z {p.z:0.##}", EditorStyles.miniLabel);

        float buttonY = rect.y + 8f;
        float right = rect.xMax - 8f;
        if (GUI.Button(new Rect(right - 184f, buttonY, 54f, 22f), "选中"))
        {
            SetPlacedSelectionTo(go, false);
            EditorGUIUtility.PingObject(go);
        }
        if (GUI.Button(new Rect(right - 126f, buttonY, 54f, 22f), "定位"))
        {
            SetPlacedSelectionTo(go, false);
            FocusSceneView();
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        if (GUI.Button(new Rect(right - 68f, buttonY, 60f, 22f), "重新应用"))
        {
            TerrainDecorationRuntimeApplier applier = go.GetComponent<TerrainDecorationRuntimeApplier>();
            if (applier != null)
            {
                DisableApplierAutoApply(applier);
                applier.ApplyDefinition();
                SkyPrisonTerrainDecorationInstanceBuilder.BuildStructureFromDefinition(go, binder.definition, true);
                DisableApplierAutoApply(applier);
            }
        }
        GUI.backgroundColor = new Color(0.85f, 0.22f, 0.12f, 1f);
        if (GUI.Button(new Rect(right - 68f, buttonY + 28f, 60f, 22f), "删除"))
        {
            if (selected && CountCurrentPlacedSelection(false) > 1)
                DeleteSelectedPlacedObjectsWithConfirm(false);
            else
                DeletePlacedInstanceWithConfirm(binder);
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawGroundSurfacePlacedRow(Rect rect, PlacedSurfaceObject item, int index, List<PlacedSurfaceObject> visibleList)
    {
        if (item == null || item.gameObject == null)
            return;

        GameObject go = item.gameObject;
        int id = go.GetInstanceID();
        bool selected = placedSelectionIds.Contains(id);
        bool unitySelected = Selection.activeGameObject == go;
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected || unitySelected)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.20f, 0.27f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), surfaceAccent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect selectionRect = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 150f), rect.height);
        HandlePlacedRowSelection(selectionRect, index, visibleList.Select(x => x != null ? x.gameObject : null).ToList(), true);

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 8f, 44f, 44f);
        if (item.preview != null)
            GUI.DrawTexture(iconRect, item.preview, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.20f, 0.20f, 0.22f, 1f));

        Rect textRect = new Rect(iconRect.xMax + 8f, rect.y + 6f, Mathf.Max(80f, rect.width - 220f), 58f);
        GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 20f), go.name, EditorStyles.boldLabel);
        GUI.Label(new Rect(textRect.x, textRect.y + 20f, textRect.width, 18f), $"{item.kindLabel} / {item.materialName}", EditorStyles.miniLabel);
        Vector3 p = go.transform.position;
        GUI.Label(new Rect(textRect.x, textRect.y + 38f, textRect.width, 18f), $"位置 X {p.x:0.##} / Y {p.y:0.##} / Z {p.z:0.##}", EditorStyles.miniLabel);

        float buttonY = rect.y + 8f;
        float right = rect.xMax - 8f;
        if (GUI.Button(new Rect(right - 126f, buttonY, 54f, 22f), "选中"))
        {
            SetPlacedSelectionTo(go, true);
            EditorGUIUtility.PingObject(go);
        }
        if (GUI.Button(new Rect(right - 68f, buttonY, 60f, 22f), "定位"))
        {
            SetPlacedSelectionTo(go, true);
            FocusSceneView();
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        GUI.backgroundColor = new Color(0.85f, 0.22f, 0.12f, 1f);
        if (GUI.Button(new Rect(right - 68f, buttonY + 28f, 60f, 22f), "删除"))
        {
            if (selected && CountCurrentPlacedSelection(true) > 1)
                DeleteSelectedPlacedObjectsWithConfirm(true);
            else
                DeleteGroundSurfacePlacedObjectWithConfirm(go);
        }
        GUI.backgroundColor = Color.white;
    }

    private void SelectDefinition(TerrainDecorationDefinition definition)
    {
        if (selectedDefinition != definition)
        {
            terrainDecorationPreviewRotationY = 0f;
            terrainDecorationPreviewHeightOffset = 0f;
        }

        selectedDefinition = definition;
        if (placementMode)
            RebuildPreview();
        Repaint();
    }

    private void SetPlacementMode(bool enabled)
    {
        // SKYPRISON_NO_SPLINE_SCENE_PLACEMENT_20260514
        // 样条图案 / 画线已经改为 GroundSpline 几何路径工具。
        // 普通 Scene 放置模式不能再接管鼠标，否则鼠标悬停 GroundSpline 时会进入刷新/抢焦点循环。
        if (enabled && currentKind == PlacementObjectKind.GroundSurfaceMaterial && IsSelectedSurfaceMaterialSpline())
        {
            placementMode = false;
            groundBrushPainting = false;
            terrainPaintHoleRestoreMode = false;
            hasLastGroundBrushPaintPosition = false;
            GUIUtility.hotControl = 0;
            return;
        }
        bool canEnable = false;
        switch (currentKind)
        {
            case PlacementObjectKind.TerrainDecoration:
                canEnable = selectedDefinition != null;
                break;
            case PlacementObjectKind.GroundSurfaceMaterial:
                canEnable = IsTerrainDefaultToolSelected() || groundBrushMode != GroundBrushMode.SurfaceMaterial || selectedSurfaceMaterial != null;
                break;
            case PlacementObjectKind.Unit:
                canEnable = selectedUnitDefinition != null && selectedUnitDefinition.prefab != null;
                break;
            case PlacementObjectKind.Item:
                canEnable = selectedItemDefinition != null;
                break;
        }

        placementMode = enabled && canEnable;
        if (placementMode)
        {
            EnsureSceneGuiHook();
            if (currentKind == PlacementObjectKind.TerrainDecoration)
            {
                terrainDecorationPreviewHeightOffset = 0f;
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null && !hasValidPreviewPosition)
                {
                    lastPreviewPosition = new Vector3(sceneView.pivot.x, placementY, sceneView.pivot.z);
                    hasValidPreviewPosition = true;
                }
                RebuildPreview();
            }
            else if (currentKind == PlacementObjectKind.GroundSurfaceMaterial)
            {
                DestroyPreview();
                activeGroundBlock = FindActiveGroundBlock();
                hasValidGroundBrushPosition = false;
                hasGroundOverlayStraightLineAnchor = false;
                ResetGroundEraseStrokeTracking();
            }
            else if (currentKind == PlacementObjectKind.Item)
            {
                RebuildItemPreview();
            }
            FocusSceneView();
        }
        else
        {
            FlushDeferredGroundBrushVisualBake();
            DestroyPreview();
            DestroyItemPreview();
            hasValidGroundBrushPosition = false;
            groundBrushPainting = false;
            terrainRectFillMode = false;
            terrainRectFillDragging = false;
            terrainRectFillControlId = 0;
            hasLastGroundBrushPaintPosition = false;
            hasGroundOverlayStraightLineAnchor = false;
            ResetGroundEraseStrokeTracking();
        }

        SceneView.RepaintAll();
        Repaint();
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (currentKind == PlacementObjectKind.GroundSurfaceMaterial)
        {
            if (placementMode)
                OnGroundBrushSceneGUI(sceneView);
            return;
        }

        if (currentKind == PlacementObjectKind.Unit)
        {
            if (placementMode)
                OnUnitPlacementSceneGUI(sceneView);
            return;
        }

        if (currentKind == PlacementObjectKind.Item)
        {
            if (placementMode)
                OnItemPlacementSceneGUI(sceneView);
            return;
        }

        if (!placementMode)
            return;

        if (selectedDefinition == null)
            return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        // ScrollWheel 是“调整当前预览”的输入，不应该先触发一次射线贴地。
        // 否则高度偏移会和 GroundY / placementY 反复混合，出现上下滚都往下漂的手感。
        if (HandleTerrainDecorationPreviewHeightInput(sceneView, e))
            return;

        if (HandleTerrainDecorationPreviewRotationInput(sceneView, e))
            return;

        UpdatePreviewPosition(e.mousePosition);
        DrawSceneOverlay();

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            SetPlacementMode(false);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 1)
        {
            SetPlacementMode(false);
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDown && e.button == 0 && hasValidPreviewPosition)
        {
            if (canPlaceAtPreviewPosition)
            {
                PlaceSelectedDefinition(lastPreviewPosition);
                e.Use();
                if (!continuousPlace)
                    SetPlacementMode(false);
                else
                    RebuildPreview();
            }
            else
            {
                e.Use();
            }
        }

        // 不在 hover / repaint 阶段强制刷新预览对象。
        // 预览位置变化、放置、旋转、缩放等真实输入处会主动刷新。
    }


    private void OnGroundBrushSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;

        // SKYPRISON_NO_SPLINE_SCENE_PLACEMENT_20260514
        // 样条图案不再由地图摆放窗口直接绘制，也不应 AddDefaultControl / Repaint。
        // 只允许专用 Spline 几何路径绘制器接管 Scene 鼠标。
        if (currentKind == PlacementObjectKind.GroundSurfaceMaterial && IsSelectedSurfaceMaterialSpline())
        {
            groundBrushPainting = false;
            hasLastGroundBrushPaintPosition = false;
            if (GUIUtility.hotControl != 0)
                GUIUtility.hotControl = 0;

            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                SetPlacementMode(false);
                e.Use();
            }
            return;
        }

        if (HandleTerrainBrushUndoRedoShortcut(e))
        {
            sceneView.Repaint();
            Repaint();
            return;
        }

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (activeGroundTerrain == null)
            activeGroundTerrain = FindActiveGroundTerrain();

        UpdateTerrainBrushPosition(e.mousePosition);
        DrawTerrainBrushSceneOverlay();
        if (terrainRectFillMode && IsTerrainLayerSurfaceMaterialSelected())
            DrawTerrainRectFillPreview();
        else
            DrawTerrainBrushPreview();

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            SetPlacementMode(false);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 1)
        {
            SetPlacementMode(false);
            e.Use();
            return;
        }

        if (terrainRectFillMode && IsTerrainLayerSurfaceMaterialSelected())
        {
            HandleTerrainRectFillSceneGUI(sceneView, e);
            return;
        }

        if (IsSelectedSurfaceMaterialStamp())
        {
            HandleGroundStampPlacementSceneGUI(sceneView, e);
            return;
        }

        int brushControlId = GUIUtility.GetControlID(FocusType.Passive);
        bool paintDown = e.type == EventType.MouseDown && e.button == 0;
        bool paintDrag = (groundBrushContinuous || IsSelectedSurfaceMaterialOverlay()) && e.type == EventType.MouseDrag && groundBrushPainting;
        bool paintUp = e.type == EventType.MouseUp && groundBrushPainting;
        bool straightLineClick = paintDown && e.shift && IsSelectedSurfaceMaterialSpline();

        if (straightLineClick)
        {
            if (RoadLineRouteTrace && IsSelectedSurfaceMaterialOverlay())
                Debug.Log($"[MapPlacement RoadLineTrace] ShiftLineClick hit={hasValidGroundBrushPosition} hasAnchor={hasGroundOverlayStraightLineAnchor} pos={lastGroundBrushPosition}");

            GUIUtility.hotControl = brushControlId;
            BeginGroundSurfaceBrushStrokeUndo(activeGroundTerrain);

            if (hasValidGroundBrushPosition)
            {
                // 样条图案不再在地图对象放置工具里直接落笔。
                // 几何线条统一交给 SkyPrisonGroundOverlaySplineGeometryTool 生成 GroundSpline Mesh。
                groundOverlayStraightLineAnchor = lastGroundBrushPosition;
                hasGroundOverlayStraightLineAnchor = true;
                lastGroundBrushPaintPosition = lastGroundBrushPosition;
                hasLastGroundBrushPaintPosition = true;
            }

            EndGroundSurfaceBrushStrokeUndo(activeGroundTerrain);
            groundBrushPainting = false;
            e.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        if (paintDown)
        {
            if (RoadLineRouteTrace && IsSelectedSurfaceMaterialOverlay())
                Debug.Log($"[MapPlacement RoadLineTrace] MouseDown hit={hasValidGroundBrushPosition} mode={groundBrushMode} spline={IsSelectedSurfaceMaterialSpline()} stamp={IsSelectedSurfaceMaterialStamp()} pos={lastGroundBrushPosition}");
            GUIUtility.hotControl = brushControlId;
            BeginGroundSurfaceBrushStrokeUndo(activeGroundTerrain);
            groundBrushPainting = true;
            terrainPaintHoleRestoreMode = IsTerrainDefaultToolSelected() && selectedTerrainDefaultTool == TerrainDefaultTool.PaintHole && e.shift;
            hasLastGroundBrushPaintPosition = false;
            groundBrushStampSeedCounter++;

            if (hasValidGroundBrushPosition)
            {
                ApplyGroundSurfaceBrushStroke(lastGroundBrushPosition);
                if (IsSelectedSurfaceMaterialSpline())
                {
                    groundOverlayStraightLineAnchor = lastGroundBrushPosition;
                    hasGroundOverlayStraightLineAnchor = true;
                }
            }

            e.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        if (paintDrag)
        {
            if (RoadLineRouteTrace && IsSelectedSurfaceMaterialOverlay())
                Debug.Log($"[MapPlacement RoadLineTrace] MouseDrag hit={hasValidGroundBrushPosition} mode={groundBrushMode} spline={IsSelectedSurfaceMaterialSpline()} pos={lastGroundBrushPosition}");
            if (hasValidGroundBrushPosition)
            {
                ApplyGroundSurfaceBrushStroke(lastGroundBrushPosition);
                if (IsSelectedSurfaceMaterialSpline())
                {
                    groundOverlayStraightLineAnchor = lastGroundBrushPosition;
                    hasGroundOverlayStraightLineAnchor = true;
                }
            }

            e.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        if (paintUp)
        {
            if (GUIUtility.hotControl == brushControlId)
                GUIUtility.hotControl = 0;

            EndGroundSurfaceBrushStrokeUndo(activeGroundTerrain);
            groundBrushPainting = false;
            terrainPaintHoleRestoreMode = false;
            hasLastGroundBrushPaintPosition = false;
            e.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        // Do not force SceneView repaint on Layout/Repaint/hover.
        // Unity already repaints on real mouse movement; forcing repaint here creates a feedback loop
        // when the cursor is over generated GroundSpline meshes.
    }


    private void HandleTerrainRectFillSceneGUI(SceneView sceneView, Event e)
    {
        if (e == null)
            return;

        int controlId = terrainRectFillControlId != 0 ? terrainRectFillControlId : GUIUtility.GetControlID(FocusType.Passive);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (!hasValidGroundBrushPosition)
            {
                e.Use();
                return;
            }

            terrainRectFillControlId = controlId;
            GUIUtility.hotControl = terrainRectFillControlId;
            terrainRectFillDragging = true;
            terrainRectFillStartPosition = lastGroundBrushPosition;
            terrainRectFillEndPosition = lastGroundBrushPosition;
            terrainRectFillStartGuiPosition = e.mousePosition;
            terrainRectFillEndGuiPosition = e.mousePosition;
            e.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDrag && terrainRectFillDragging)
        {
            terrainRectFillEndGuiPosition = e.mousePosition;
            if (hasValidGroundBrushPosition)
                terrainRectFillEndPosition = lastGroundBrushPosition;

            e.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseUp && terrainRectFillDragging)
        {
            if (GUIUtility.hotControl == terrainRectFillControlId)
                GUIUtility.hotControl = 0;

            terrainRectFillEndGuiPosition = e.mousePosition;
            if (hasValidGroundBrushPosition)
                terrainRectFillEndPosition = lastGroundBrushPosition;

            FillTerrainSurfaceMaterialScreenRect(activeGroundTerrain, selectedSurfaceMaterial, terrainRectFillStartGuiPosition, terrainRectFillEndGuiPosition);
            terrainRectFillDragging = false;
            terrainRectFillControlId = 0;
            e.Use();
            sceneView.Repaint();
            Repaint();
        }
    }

    private void DrawTerrainRectFillPreview()
    {
        if (!groundBrushPreviewMask || activeGroundTerrain == null)
            return;

        if (!terrainRectFillDragging)
        {
            DrawTerrainBrushPreview();
            return;
        }

        Rect guiRect = MakePositiveGuiRect(terrainRectFillStartGuiPosition, terrainRectFillEndGuiPosition);
        if (guiRect.width <= 0.5f || guiRect.height <= 0.5f)
            return;

        Color fillColor = new Color(GroundBrushSurfaceColor.r, GroundBrushSurfaceColor.g, GroundBrushSurfaceColor.b, 0.16f);
        Color lineColor = new Color(GroundBrushSurfaceColor.r, GroundBrushSurfaceColor.g, GroundBrushSurfaceColor.b, 1f);

        Handles.BeginGUI();
        Color oldGuiColor = GUI.color;
        GUI.color = fillColor;
        GUI.DrawTexture(guiRect, EditorGUIUtility.whiteTexture);
        GUI.color = lineColor;
        GUI.DrawTexture(new Rect(guiRect.xMin, guiRect.yMin, guiRect.width, 2f), EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.xMin, guiRect.yMax - 2f, guiRect.width, 2f), EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.xMin, guiRect.yMin, 2f, guiRect.height), EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.xMax - 2f, guiRect.yMin, 2f, guiRect.height), EditorGUIUtility.whiteTexture);
        GUI.color = oldGuiColor;
        Handles.EndGUI();
    }

    private static Rect MakePositiveGuiRect(Vector2 a, Vector2 b)
    {
        float xMin = Mathf.Min(a.x, b.x);
        float xMax = Mathf.Max(a.x, b.x);
        float yMin = Mathf.Min(a.y, b.y);
        float yMax = Mathf.Max(a.y, b.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private bool TryGetTerrainLocalRectCorners(Terrain terrain, Vector3 worldA, Vector3 worldB, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
    {
        p0 = p1 = p2 = p3 = Vector3.zero;
        if (terrain == null || terrain.terrainData == null)
            return false;

        // 矩形选区的朝向不能用世界 X/Z，也不能只用 Terrain 自己的轴。
        // 实际地图可能是 MapBounds / GroundRoot 旋转，而 Terrain 本体仍然保持 0 度。
        Transform frame = GetTerrainRectFillFrameTransform(terrain);
        Vector3 localA = frame.InverseTransformPoint(worldA);
        Vector3 localB = frame.InverseTransformPoint(worldB);

        float minX = Mathf.Min(localA.x, localB.x);
        float maxX = Mathf.Max(localA.x, localB.x);
        float minZ = Mathf.Min(localA.z, localB.z);
        float maxZ = Mathf.Max(localA.z, localB.z);

        p0 = ProjectRectFrameLocalPointToTerrain(terrain, frame, minX, minZ);
        p1 = ProjectRectFrameLocalPointToTerrain(terrain, frame, maxX, minZ);
        p2 = ProjectRectFrameLocalPointToTerrain(terrain, frame, maxX, maxZ);
        p3 = ProjectRectFrameLocalPointToTerrain(terrain, frame, minX, maxZ);
        return true;
    }

    private Transform GetTerrainRectFillFrameTransform(Terrain terrain)
    {
        // 优先跟随 MapBounds。用户调整地图角度时，一般旋转的是这个地图基准物。
        GameObject mapBoundsGo = GameObject.Find("MapBounds");
        if (mapBoundsGo != null && mapBoundsGo.scene.IsValid())
            return mapBoundsGo.transform;

        Transform groundRoot = FindTransformByPath("WorldRoot/GroundRoot");
        if (groundRoot != null)
            return groundRoot;

        Transform block = FindTransformByPath("WorldRoot/GroundRoot/GroundBlock_01");
        if (block != null)
            return block;

        return terrain != null ? terrain.transform : null;
    }

    private Vector3 ProjectRectFrameLocalPointToTerrain(Terrain terrain, Transform frame, float frameLocalX, float frameLocalZ)
    {
        if (terrain == null || terrain.terrainData == null || frame == null)
            return Vector3.zero;

        Vector3 world = frame.TransformPoint(new Vector3(frameLocalX, 0f, frameLocalZ));
        if (TryWorldToTerrainUV(terrain, world, out Vector2 uv))
        {
            TerrainData data = terrain.terrainData;
            Vector3 terrainLocal = new Vector3(uv.x * data.size.x, data.GetInterpolatedHeight(uv.x, uv.y) + 0.14f, uv.y * data.size.z);
            return terrain.transform.TransformPoint(terrainLocal);
        }

        // 超出 Terrain 时仍然把预览线画在当前框架上，避免拖拽时整条线突然消失。
        world.y = terrain.transform.position.y + 0.14f;
        return world;
    }

    private Vector3 SampleTerrainPreviewLocalPoint(Terrain terrain, float localX, float localZ)
    {
        if (terrain == null || terrain.terrainData == null)
            return Vector3.zero;

        TerrainData data = terrain.terrainData;
        float u = Mathf.Clamp01(localX / Mathf.Max(0.001f, data.size.x));
        float v = Mathf.Clamp01(localZ / Mathf.Max(0.001f, data.size.z));
        Vector3 local = new Vector3(u * data.size.x, data.GetInterpolatedHeight(u, v) + 0.14f, v * data.size.z);
        return terrain.transform.TransformPoint(local);
    }

    private bool HandleTerrainBrushUndoRedoShortcut(Event e)
    {
        if (e == null || e.type != EventType.KeyDown)
            return false;

        bool actionKey = e.control || e.command;
        if (!actionKey)
            return false;

        bool undo = e.keyCode == KeyCode.Z && !e.shift;
        bool redo = e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift);
        if (!undo && !redo)
            return false;

        if (groundBrushPainting)
        {
            EndGroundSurfaceBrushStrokeUndo(activeGroundTerrain);
            groundBrushPainting = false;
            terrainPaintHoleRestoreMode = false;
            hasLastGroundBrushPaintPosition = false;
            GUIUtility.hotControl = 0;
        }

        if (undo)
            Undo.PerformUndo();
        else
            Undo.PerformRedo();

        e.Use();
        return true;
    }

    private void BeginTerrainBrushStrokeUndo(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null || terrainBrushStrokeUndoActive)
            return;

        terrainBrushStrokeUndoActive = true;
        terrainBrushStrokeUndoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Paint Terrain Surface Material");
        Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Paint Terrain Surface Material");
    }

    private void EndTerrainBrushStrokeUndo(Terrain terrain)
    {
        if (terrain != null && terrain.terrainData != null)
            EditorUtility.SetDirty(terrain.terrainData);

        if (terrainBrushStrokeUndoActive)
        {
            Undo.CollapseUndoOperations(terrainBrushStrokeUndoGroup);
            terrainBrushStrokeUndoActive = false;
            terrainBrushStrokeUndoGroup = -1;
        }
    }

    private Terrain FindActiveGroundTerrain()
    {
        Transform t = FindTransformByPath("WorldRoot/GroundRoot/GroundTerrain");
        Terrain terrain = t != null ? t.GetComponent<Terrain>() : null;
        if (terrain == null && t != null)
            terrain = t.GetComponentInChildren<Terrain>(true);
        if (terrain != null)
            return terrain;

        Terrain[] terrains = FindObjectsOfType<Terrain>(true);
        if (terrains != null && terrains.Length == 1)
            return terrains[0];

        return Terrain.activeTerrain;
    }

    private void UpdateTerrainBrushPosition(Vector2 mousePosition)
    {
        if (activeGroundTerrain == null || activeGroundTerrain.terrainData == null)
        {
            hasValidGroundBrushPosition = false;
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        TerrainCollider terrainCollider = activeGroundTerrain.GetComponent<TerrainCollider>();
        if (terrainCollider != null && terrainCollider.enabled && terrainCollider.Raycast(ray, out RaycastHit hit, 100000f))
        {
            if (TryWorldToTerrainUV(activeGroundTerrain, hit.point, out _))
            {
                lastGroundBrushPosition = hit.point;
                hasValidGroundBrushPosition = true;
                return;
            }
        }

        // 兜底：TerrainCollider 临时未刷新时，按 TerrainData 高度采样。
        Plane plane = new Plane(Vector3.up, activeGroundTerrain.transform.position);
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 p = ray.GetPoint(enter);
            if (TryWorldToTerrainUV(activeGroundTerrain, p, out Vector2 uv))
            {
                TerrainData data = activeGroundTerrain.terrainData;
                Vector3 local = new Vector3(uv.x * data.size.x, data.GetInterpolatedHeight(uv.x, uv.y), uv.y * data.size.z);
                p = activeGroundTerrain.transform.TransformPoint(local);
                lastGroundBrushPosition = p;
                hasValidGroundBrushPosition = true;
                return;
            }
        }

        hasValidGroundBrushPosition = false;
    }

    private bool TryWorldToTerrainUV(Terrain terrain, Vector3 worldPosition, out Vector2 uv)
    {
        uv = Vector2.zero;
        if (terrain == null || terrain.terrainData == null)
            return false;

        TerrainData data = terrain.terrainData;
        // Terrain 可能挂在旋转过的地图根节点下面。这里必须转到 Terrain 本地坐标，
        // 否则矩形选区会永远按世界 X/Z 对齐。
        Vector3 local = terrain.transform.InverseTransformPoint(worldPosition);
        if (data.size.x <= 0.001f || data.size.z <= 0.001f)
            return false;

        uv = new Vector2(local.x / data.size.x, local.z / data.size.z);
        return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    }


    private void DrawGroundStampPlacementControls()
    {
        if (selectedSurfaceMaterial == null)
            return;

        Texture stampTexture = GetSelectedStampTexture();
        Vector2 stampSize = GetSelectedStampPaintSize();
        bool canRotate = selectedSurfaceMaterial.stampCanRotate;
        bool canScale = selectedSurfaceMaterial.stampCanScale;

        EditorGUILayout.LabelField("印花纹理", stampTexture != null ? stampTexture.name : "未设置 stampTexture");
        if (selectedSurfaceMaterial.stampWorldSize.x > 0.001f && selectedSurfaceMaterial.stampWorldSize.y > 0.001f)
            EditorGUILayout.LabelField("资源尺寸单位", $"{selectedSurfaceMaterial.stampWorldSize.x:0.###} × {selectedSurfaceMaterial.stampWorldSize.y:0.###}  （1单位 = {GroundStampLegacyUnitWorldSize:0.###}m）");
        EditorGUILayout.LabelField("实际放置尺寸", $"{stampSize.x:0.###} × {stampSize.y:0.###} m");
        EditorGUILayout.LabelField("Scene 操作", "左键生成 / Q,E旋转 / Ctrl+滚轮缩放 / 选中后用Unity工具微调");

        using (new EditorGUI.DisabledScope(!canRotate))
        {
            groundStampPlacementRotationY = DrawClampedSlider("放置旋转 Y", groundStampPlacementRotationY, -180f, 180f);
        }

        using (new EditorGUI.DisabledScope(!canScale))
        {
            Rect scaleRect = GetClampedControlRect();
            Rect fieldRect = EditorGUI.PrefixLabel(scaleRect, new GUIContent("放置缩放"));
            float gap = 6f;
            float fieldWidth = Mathf.Max(1f, (fieldRect.width - gap) * 0.5f);
            Rect xRect = new Rect(fieldRect.x, fieldRect.y, fieldWidth, fieldRect.height);
            Rect yRect = new Rect(xRect.xMax + gap, fieldRect.y, fieldWidth, fieldRect.height);
            groundStampPlacementScale.x = Mathf.Clamp(EditorGUI.FloatField(xRect, groundStampPlacementScale.x), 0.05f, 20f);
            groundStampPlacementScale.y = Mathf.Clamp(EditorGUI.FloatField(yRect, groundStampPlacementScale.y), 0.05f, 20f);
        }

        if (!canRotate)
            groundStampPlacementRotationY = 0f;
        if (!canScale)
            groundStampPlacementScale = Vector2.one;

        Rect buttonRow = GetClampedControlRect(22f);
        buttonRow.width = Mathf.Min(buttonRow.width, 282f);
        float gapButton = 6f;
        float buttonWidth = Mathf.Max(1f, (buttonRow.width - gapButton * 2f) / 3f);
        Rect leftRect = new Rect(buttonRow.x, buttonRow.y, buttonWidth, buttonRow.height);
        Rect middleRect = new Rect(leftRect.xMax + gapButton, buttonRow.y, buttonWidth, buttonRow.height);
        Rect rightRect = new Rect(middleRect.xMax + gapButton, buttonRow.y, buttonWidth, buttonRow.height);

        using (new EditorGUI.DisabledScope(!canRotate))
        {
            if (GUI.Button(leftRect, "旋转 -90°"))
                groundStampPlacementRotationY = Mathf.Repeat(groundStampPlacementRotationY - 90f + 180f, 360f) - 180f;
            if (GUI.Button(middleRect, "旋转 +90°"))
                groundStampPlacementRotationY = Mathf.Repeat(groundStampPlacementRotationY + 90f + 180f, 360f) - 180f;
        }

        if (GUI.Button(rightRect, "重置"))
        {
            groundStampPlacementRotationY = 0f;
            groundStampPlacementScale = Vector2.one;
        }
    }

    private void HandleGroundStampPlacementSceneGUI(SceneView sceneView, Event e)
    {
        if (selectedSurfaceMaterial == null)
            return;

        bool canRotate = selectedSurfaceMaterial.stampCanRotate;
        bool canScale = selectedSurfaceMaterial.stampCanScale;

        if (e.type == EventType.KeyDown)
        {
            if (canRotate && e.keyCode == KeyCode.Q)
            {
                groundStampPlacementRotationY = Mathf.Repeat(groundStampPlacementRotationY - 5f + 180f, 360f) - 180f;
                e.Use();
                sceneView.Repaint();
                Repaint();
                return;
            }
            if (canRotate && e.keyCode == KeyCode.E)
            {
                groundStampPlacementRotationY = Mathf.Repeat(groundStampPlacementRotationY + 5f + 180f, 360f) - 180f;
                e.Use();
                sceneView.Repaint();
                Repaint();
                return;
            }
        }

        if (canScale && e.type == EventType.ScrollWheel && (e.control || e.command))
        {
            float factor = e.delta.y > 0f ? 0.95f : 1.05f;
            groundStampPlacementScale.x = Mathf.Clamp(groundStampPlacementScale.x * factor, 0.05f, 20f);
            groundStampPlacementScale.y = Mathf.Clamp(groundStampPlacementScale.y * factor, 0.05f, 20f);
            e.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (hasValidGroundBrushPosition)
                PlaceGroundStampAt(lastGroundBrushPosition);

            e.Use();
            sceneView.Repaint();
            Repaint();

            if (!groundBrushContinuous)
                SetPlacementMode(false);
            return;
        }
    }

    private void DrawGroundStampPlacementPreview()
    {
        Texture stampTexture = GetSelectedStampTexture();
        Vector2 stampSize = GetSelectedStampPaintSize();
        Vector3 center = lastGroundBrushPosition + Vector3.up * 0.055f;
        Quaternion rotation = Quaternion.Euler(0f, groundStampPlacementRotationY, 0f);
        Vector3 scale = new Vector3(stampSize.x * Mathf.Max(0.01f, groundStampPlacementScale.x), 1f, stampSize.y * Mathf.Max(0.01f, groundStampPlacementScale.y));

        Mesh mesh = GetGroundStampQuadMesh();
        Material mat = GetGroundStampPreviewMaterial(stampTexture);

        if (mesh != null && mat != null && Event.current.type == EventType.Repaint)
        {
            mat.SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix4x4.TRS(center, rotation, scale));
        }

        Color oldColor = Handles.color;
        CompareFunction oldZTest = Handles.zTest;
        Handles.zTest = CompareFunction.Always;
        Handles.color = new Color(0.18f, 1f, 0.36f, 0.35f);

        Vector3 hx = rotation * Vector3.right * scale.x * 0.5f;
        Vector3 hz = rotation * Vector3.forward * scale.z * 0.5f;
        Vector3 p0 = center - hx - hz;
        Vector3 p1 = center + hx - hz;
        Vector3 p2 = center + hx + hz;
        Vector3 p3 = center - hx + hz;
        Handles.DrawAAPolyLine(4f, p0, p1, p2, p3, p0);
        Handles.DrawLine(center - hx * 0.2f, center + hx * 0.2f);
        Handles.DrawLine(center - hz * 0.2f, center + hz * 0.2f);

        Handles.zTest = oldZTest;
        Handles.color = oldColor;
    }

    private void PlaceGroundStampAt(Vector3 worldPosition)
    {
        if (selectedSurfaceMaterial == null)
            return;

        Mesh mesh = GetGroundStampQuadMesh();
        if (mesh == null)
            return;

        Vector2 stampSize = GetSelectedStampPaintSize();
        Vector3 position = worldPosition + Vector3.up * 0.035f;
        Quaternion rotation = Quaternion.Euler(0f, groundStampPlacementRotationY, 0f);
        Vector3 scale = new Vector3(stampSize.x * Mathf.Max(0.01f, groundStampPlacementScale.x), 1f, stampSize.y * Mathf.Max(0.01f, groundStampPlacementScale.y));

        GameObject go = new GameObject(BuildGroundStampObjectName(selectedSurfaceMaterial));
        Undo.RegisterCreatedObjectUndo(go, "Place ground stamp");

        Transform parent = GetOrCreateParent(GroundStampParentPath);
        if (parent != null)
            go.transform.SetParent(parent, true);

        go.transform.position = position;
        go.transform.rotation = rotation;
        go.transform.localScale = scale;
        int world3DLayerIndex = LayerMask.NameToLayer("World3D");
        go.layer = world3DLayerIndex >= 0 ? world3DLayerIndex : ResolveGroundOverlayVisualLayer();

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sortingOrder = GroundStampSortingOrder;
        mf.sharedMesh = mesh;
        mr.sharedMaterial = GetOrCreateGroundStampMaterialAsset(selectedSurfaceMaterial);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    private string BuildGroundStampObjectName(GroundSurfaceMaterialDefinition material)
    {
        string baseName = material != null ? GetDisplayName(material) : "GroundStamp";
        return MakeUniqueSceneObjectName(SanitizeName($"GroundStamp_{baseName}_{UnityEngine.Random.Range(100000, 999999)}"));
    }

    private Texture GetSelectedStampTexture()
    {
        if (selectedSurfaceMaterial == null)
            return null;
        if (selectedSurfaceMaterial.stampTexture != null)
            return selectedSurfaceMaterial.stampTexture;
        if (selectedSurfaceMaterial.previewIcon != null)
            return selectedSurfaceMaterial.previewIcon.texture;
        if (selectedSurfaceMaterial.baseTexture != null)
            return selectedSurfaceMaterial.baseTexture;
        return null;
    }

    private Mesh GetGroundStampQuadMesh()
    {
        if (groundStampPreviewMesh != null)
            return groundStampPreviewMesh;

        groundStampPreviewMesh = new Mesh();
        groundStampPreviewMesh.name = "SkyPrison_GroundStamp_Quad";
        groundStampPreviewMesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f,  0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
        };
        groundStampPreviewMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };
        groundStampPreviewMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        groundStampPreviewMesh.RecalculateNormals();
        groundStampPreviewMesh.RecalculateBounds();
        return groundStampPreviewMesh;
    }

    private Material GetGroundStampPreviewMaterial(Texture texture)
    {
        if (groundStampPreviewMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Transparent")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Standard");
            groundStampPreviewMaterial = new Material(shader);
            groundStampPreviewMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        if (groundStampPreviewMaterialTexture != texture)
        {
            groundStampPreviewMaterialTexture = texture;
            ConfigureGroundStampMaterial(groundStampPreviewMaterial, texture, new Color(0.45f, 1f, 0.55f, 0.45f));
        }
        else
        {
            ConfigureGroundStampMaterial(groundStampPreviewMaterial, texture, new Color(0.45f, 1f, 0.55f, 0.45f));
        }

        return groundStampPreviewMaterial;
    }

    private Material GetOrCreateGroundStampMaterialAsset(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return null;

        EnsureAssetFolder(GroundStampMaterialAssetFolder);

        string key = MakeSafeAssetName(!string.IsNullOrWhiteSpace(material.surfaceId) ? material.surfaceId : material.name);
        string path = $"{GroundStampMaterialAssetFolder}/MAT_Stamp_{key}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Transparent")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }

        float opacity = Mathf.Clamp01(material.stampOpacity > 0.001f ? material.stampOpacity : terrainSurfaceBrushOpacity);
        ConfigureGroundStampMaterial(mat, GetSelectedStampTexture(), new Color(1f, 1f, 1f, opacity));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private void ConfigureGroundStampMaterial(Material mat, Texture texture, Color color)
    {
        if (mat == null)
            return;

        if (texture != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);

        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetOverrideTag("Queue", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        ApplyGroundOverlayMaterialSorting(mat);
    }

    private static int ResolveGroundOverlayVisualLayer()
    {
        int layer = LayerMask.NameToLayer(GroundOverlayPreferredLayerName);
        if (layer >= 0)
            return layer;

        layer = LayerMask.NameToLayer(GroundOverlayFallbackLayerName);
        if (layer >= 0)
            return layer;

        return 0;
    }

    private static void ApplyGroundOverlayMaterialSorting(Material mat)
    {
        if (mat == null)
            return;

        // URP/Lit and URP/Unlit expose this as Advanced Options / Sorting Priority.
        // Keeping both _QueueOffset and renderQueue makes old generated materials and runtime-created materials stable.
        if (mat.HasProperty("_QueueOffset"))
            mat.SetFloat("_QueueOffset", GroundOverlaySortingPriority);

        mat.renderQueue = GroundOverlayRenderQueue;
        mat.SetOverrideTag("Queue", "Transparent");
        mat.SetOverrideTag("RenderType", "Transparent");
    }

    [MenuItem("Tools/Sky Prison/Map/修复地面印花与画线透明排序")]
    private static void FixAllGroundOverlayTransparentSorting()
    {
        int materialCount = 0;
        int rendererCount = 0;

        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!LooksLikeGroundOverlayMaterial(path))
                continue;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                continue;

            ApplyGroundOverlayMaterialSorting(mat);
            EditorUtility.SetDirty(mat);
            materialCount++;
        }

        int sortingGroupCount = 0;
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
        {
            if (renderer == null || !LooksLikeGroundOverlayObject(renderer.gameObject))
                continue;

            // Restore layer to World3D (visible to main camera). GroundVisual is not in the camera culling mask.
            int world3DLayer = LayerMask.NameToLayer("World3D");
            if (world3DLayer >= 0 && renderer.gameObject.layer != world3DLayer)
            {
                Undo.RecordObject(renderer.gameObject, "Fix ground stamp layer");
                renderer.gameObject.layer = world3DLayer;
            }

            Material[] mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                    continue;

                ApplyGroundOverlayMaterialSorting(mats[i]);
                EditorUtility.SetDirty(mats[i]);
            }

            // Remove any SortingGroup that was incorrectly added by a previous fix run.
            SortingGroup existingSG = renderer.gameObject.GetComponent<SortingGroup>();
            if (existingSG != null)
            {
                Undo.DestroyObjectImmediate(existingSG);
                sortingGroupCount++;
            }

            // Ensure stamp draws before characters/loot drops (which use sortingOrder -1..+1).
            if (renderer.sortingOrder != GroundStampSortingOrder)
            {
                Undo.RecordObject(renderer, "Fix ground stamp sortingOrder");
                renderer.sortingOrder = GroundStampSortingOrder;
            }

            rendererCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[SkyPrison GroundOverlay Sorting Fix] materials={materialCount}, renderers={rendererCount}, newSortingGroups={sortingGroupCount}, sortingOrder={GroundStampSortingOrder}, renderQueue={GroundOverlayRenderQueue}");
    }

    private static bool LooksLikeGroundOverlayMaterial(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        string lower = assetPath.Replace('\\', '/').ToLowerInvariant();
        return lower.Contains("/generatedstampmaterials/")
            || lower.Contains("mat_stamp")
            || lower.Contains("groundstamp")
            || lower.Contains("ground_stamp")
            || lower.Contains("roadline")
            || lower.Contains("road_line")
            || lower.Contains("spline");
    }

    private static bool LooksLikeGroundOverlayObject(GameObject go)
    {
        if (go == null)
            return false;

        string name = go.name.ToLowerInvariant();
        return name.Contains("groundstamp")
            || name.Contains("ground_stamp")
            || name.Contains("roadline")
            || name.Contains("road_line")
            || name.Contains("groundspline")
            || name.Contains("ground_spline");
    }

    private float GetGroundBrushPreviewWorldSize()
    {
        if (IsSelectedSurfaceMaterialSpline() && !groundOverlayEraseMode)
            return Mathf.Max(0.05f, GetSelectedSplinePaintWidth());

        if (IsSelectedSurfaceMaterialStamp() && !groundOverlayEraseMode)
        {
            Vector2 stampSize = GetSelectedStampPaintSize();
            return Mathf.Max(0.05f, Mathf.Max(stampSize.x, stampSize.y));
        }

        return Mathf.Max(0.05f, groundBrushSize);
    }

    private void DrawTerrainBrushSceneOverlay()
    {
        Handles.BeginGUI();
        Rect rect = new Rect(12f, 12f, 430f, 106f);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.58f));
        GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f), terrainRectFillMode ? "Terrain 地表材质矩形填充" : (IsSelectedSurfaceMaterialSpline() ? "GroundSpline 几何路径" : (IsSelectedSurfaceMaterialStamp() ? "Unity Quad / 印花放置" : "Terrain 地表材质画笔")), EditorStyles.boldLabel);
        string materialName = IsTerrainDefaultToolSelected() ? GetTerrainDefaultToolDisplayName(selectedTerrainDefaultTool) : (selectedSurfaceMaterial != null ? GetDisplayName(selectedSurfaceMaterial) : "未选择地表材质");
        GUI.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 18f), $"工具：{materialName}", EditorStyles.label);
        string shape = IsSelectedSurfaceMaterialSpline() ? "方向画线" : GetGroundBrushShapeDisplayName(groundBrushShape);
        string sizeLabel = terrainRectFillMode
            ? (terrainRectFillDragging ? "正在拖拽矩形" : "左键拖拽矩形")
            : (IsSelectedSurfaceMaterialSpline()
                ? $"线宽 {GetSelectedSplinePaintWidth():0.###}m" + (groundOverlayEraseMode ? $" / 擦除 {groundBrushSize:0.##}m" : "")
                : $"尺寸 {GetGroundBrushPreviewWorldSize():0.##}m");
        string actionLabel = terrainRectFillMode ? "矩形填充" : GetGroundBrushModeLabel(groundBrushMode);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 50f, rect.width - 20f, 18f), $"{actionLabel} / {shape} / {sizeLabel} / 不透明度 {terrainSurfaceBrushOpacity:0.##}", EditorStyles.miniLabel);
        string state = activeGroundTerrain == null
            ? "没有找到 GroundTerrain"
            : (hasValidGroundBrushPosition ? $"命中 Terrain：{lastGroundBrushPosition.x:0.##}, {lastGroundBrushPosition.z:0.##}，左键" + (terrainRectFillMode ? "拖拽选区" : (IsSelectedSurfaceMaterialStamp() ? "生成印花" : "绘制")) : "已找到 Terrain，但鼠标没有命中 Terrain 范围");
        GUI.Label(new Rect(rect.x + 10f, rect.y + 72f, rect.width - 20f, 18f), state, EditorStyles.miniLabel);
        Handles.EndGUI();
    }

    private void DrawTerrainBrushPreview()
    {
        if (!groundBrushPreviewMask || activeGroundTerrain == null)
            return;

        Color brushColor = GetGroundBrushPreviewColor();

        if (!hasValidGroundBrushPosition)
        {
            Handles.BeginGUI();
            Rect warn = new Rect(12f, 124f, 400f, 28f);
            EditorGUI.DrawRect(warn, new Color(0.12f, 0.04f, 0.04f, 0.72f));
            GUI.Label(new Rect(warn.x + 10f, warn.y + 5f, warn.width - 20f, 18f), "笔刷未命中 Terrain：请把鼠标移到 GroundTerrain 范围内", EditorStyles.miniLabel);
            Handles.EndGUI();
            return;
        }

        if (IsSelectedSurfaceMaterialStamp() && !groundOverlayEraseMode)
        {
            DrawGroundStampPlacementPreview();
            return;
        }

        Color oldColor = Handles.color;
        CompareFunction oldZTest = Handles.zTest;
        Handles.zTest = CompareFunction.Always;

        Color fillColor = new Color(brushColor.r, brushColor.g, brushColor.b, 0.18f);
        Color lineColor = new Color(brushColor.r, brushColor.g, brushColor.b, 1.00f);

        Vector3 center = lastGroundBrushPosition;
        center.y += 0.12f;
        float size = GetGroundBrushPreviewWorldSize();

        Handles.color = lineColor;
        Handles.SphereHandleCap(0, center, Quaternion.identity, Mathf.Max(0.18f, size * 0.08f), EventType.Repaint);
        Handles.DrawLine(center + Vector3.left * size * 0.5f, center + Vector3.right * size * 0.5f);
        Handles.DrawLine(center + Vector3.forward * size * 0.5f, center + Vector3.back * size * 0.5f);

        DrawGroundBrushShapePreview(center, size, fillColor, lineColor);

        Handles.zTest = oldZTest;
        Handles.color = oldColor;
    }

    private void DrawTerrainSurfaceBrushPalette()
    {
        EditorGUILayout.LabelField("Terrain 笔刷", GetGroundBrushShapeDisplayName(groundBrushShape));

        const float outerPadding = 6f;
        const float itemSize = 54f;
        const float itemGap = 6f;
        const float innerPadding = 4f;
        const float verticalScrollbarReserve = 18f;

        GroundBrushShape[] shapes = GetTerrainSurfaceBrushPaletteShapes();

        // Unity Terrain 的笔刷区是“图案格子容器”，不是下拉，也不是单行横向滑条。
        // 宽度使用当前设置面板的完整可用宽度；超过容器右边界前必须换行。
        // 可见高度固定为 2 行；内容超过 2 行时只走竖向滚动条。
        float visibleRows = 2f;
        float viewHeight = outerPadding * 2f + visibleRows * itemSize + (visibleRows - 1f) * itemGap;
        float fullWidth = GetActiveButtonMaxWidth();

        Rect rect = GUILayoutUtility.GetRect(fullWidth, fullWidth, viewHeight, viewHeight, GUILayout.ExpandWidth(false));
        rect.x = 0f;
        rect.width = Mathf.Max(1f, fullWidth);

        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.08f));

        Rect viewRect = new Rect(
            rect.x + innerPadding,
            rect.y + innerPadding,
            Mathf.Max(20f, rect.width - innerPadding * 2f),
            Mathf.Max(20f, rect.height - innerPadding * 2f));

        // GUI.BeginScrollView 的竖向滚动条会吃掉右侧可视宽度。
        // 列数必须按“扣掉滚动条后的可见格子区”计算，否则最后一个格子会被塞到滚动条下面，看起来像不会换行。
        float gridVisibleWidth = Mathf.Max(itemSize, viewRect.width - verticalScrollbarReserve - outerPadding * 2f);
        int columnCount = Mathf.Max(1, Mathf.FloorToInt((gridVisibleWidth + itemGap) / (itemSize + itemGap)));
        int rowCount = Mathf.Max(1, Mathf.CeilToInt(shapes.Length / (float)columnCount));

        float contentWidth = Mathf.Max(1f, gridVisibleWidth + outerPadding * 2f);
        float contentHeight = Mathf.Max(
            viewRect.height,
            outerPadding * 2f + rowCount * itemSize + Mathf.Max(0, rowCount - 1) * itemGap);
        Rect contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

        terrainSurfaceBrushPaletteScroll = GUI.BeginScrollView(viewRect, terrainSurfaceBrushPaletteScroll, contentRect, false, true);

        for (int i = 0; i < shapes.Length; i++)
        {
            GroundBrushShape shape = shapes[i];
            int col = i % columnCount;
            int row = i / columnCount;

            float x = outerPadding + col * (itemSize + itemGap);
            float y = outerPadding + row * (itemSize + itemGap);
            Rect itemRect = new Rect(x, y, itemSize, itemSize);
            DrawTerrainSurfaceBrushPaletteItem(itemRect, shape);
        }

        GUI.EndScrollView();
    }

    private void DrawTerrainSurfaceBrushPaletteItem(Rect rect, GroundBrushShape shape)
    {
        Event e = Event.current;
        bool selected = groundBrushShape == shape;
        bool hover = rect.Contains(e.mousePosition);

        Color bg = selected
            ? new Color(0.18f, 0.34f, 0.58f, 1f)
            : (hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.04f));

        EditorGUI.DrawRect(rect, bg);
        DrawThinBorder(rect, selected ? new Color(0.62f, 0.82f, 1f, 1f) : new Color(1f, 1f, 1f, hover ? 0.22f : 0.08f));

        Texture2D tex = GetTerrainSurfaceBrushPreviewTexture(shape);
        if (tex != null)
        {
            Rect texRect = new Rect(rect.x + 5f, rect.y + 5f, rect.width - 10f, rect.height - 10f);
            GUI.DrawTexture(texRect, tex, ScaleMode.ScaleToFit, true);
        }

        if (GUI.Button(rect, new GUIContent("", GetGroundBrushShapeDisplayName(shape)), GUIStyle.none))
        {
            groundBrushShape = shape;
            GUI.changed = true;
            e.Use();
            SceneView.RepaintAll();
        }
    }

    private static GroundBrushShape[] GetTerrainSurfaceBrushPaletteShapes()
    {
        return new[]
        {
            GroundBrushShape.Circle,
            GroundBrushShape.SoftCircle,
            GroundBrushShape.HardCircle,
            GroundBrushShape.SoftNoise,
            GroundBrushShape.Splatter,
            GroundBrushShape.Ring,
            GroundBrushShape.Stripes,
            GroundBrushShape.Star,
            GroundBrushShape.Square,
            GroundBrushShape.SoftSquare,
            GroundBrushShape.Diamond,
            GroundBrushShape.Hexagon,
        };
    }

    private Texture2D GetTerrainSurfaceBrushPreviewTexture(GroundBrushShape shape)
    {
        Texture2D tex;
        if (terrainSurfaceBrushPreviewTextures.TryGetValue(shape, out tex) && tex != null)
            return tex;

        tex = CreateTerrainSurfaceBrushPreviewTexture(shape, 72);
        terrainSurfaceBrushPreviewTextures[shape] = tex;
        return tex;
    }

    private Texture2D CreateTerrainSurfaceBrushPreviewTexture(GroundBrushShape shape, int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        tex.name = "SkyPrison_TerrainSurfaceBrush_" + shape;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size) * 2f - 1f;
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                float mask = EvaluateGroundBrushPreviewMask(shape, nx, ny);
                pixels[y * size + x] = mask > 0f ? new Color(1f, 1f, 1f, mask) : new Color(0f, 0f, 0f, 0f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static float EvaluateGroundBrushPreviewMask(GroundBrushShape shape, float nx, float ny)
    {
        float ax = Mathf.Abs(nx);
        float ay = Mathf.Abs(ny);

        switch (shape)
        {
            case GroundBrushShape.Circle:
                return Mathf.Clamp01((1f - Mathf.Sqrt(nx * nx + ny * ny)) / 0.16f);
            case GroundBrushShape.SoftCircle:
                return Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
            case GroundBrushShape.HardCircle:
                return Mathf.Sqrt(nx * nx + ny * ny) <= 0.92f ? 1f : 0f;
            case GroundBrushShape.Square:
                return Mathf.Max(ax, ay) <= 0.92f ? 1f : 0f;
            case GroundBrushShape.SoftSquare:
                return Mathf.Clamp01((1f - Mathf.Max(ax, ay)) / 0.28f);
            case GroundBrushShape.Diamond:
                return Mathf.Clamp01((1f - (ax + ay)) / 0.10f);
            case GroundBrushShape.Hexagon:
                return Mathf.Clamp01((1f - Mathf.Max(ax * 0.8660254f + ay * 0.5f, ay)) / 0.09f);
            case GroundBrushShape.Star:
            {
                float angle = Mathf.Atan2(ny, nx);
                float radial = Mathf.Sqrt(nx * nx + ny * ny);
                float starRadius = Mathf.Max(0.22f, 0.70f + 0.30f * Mathf.Cos(5f * angle));
                return Mathf.Clamp01((1f - radial / starRadius) / 0.10f);
            }
            case GroundBrushShape.SoftNoise:
            {
                float radial = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx);
                float ripple = Mathf.Clamp(0.84f + 0.10f * Mathf.Sin(angle * 7f + 1.70f) + 0.06f * Mathf.Sin(angle * 13f + 0.45f), 0.62f, 1.0f);
                float edge = Mathf.Clamp01((1f - radial / ripple) / 0.22f);
                float grain = 0.80f + 0.20f * Mathf.Sin((nx * 31f + ny * 17f) * 3.14159f);
                return Mathf.Clamp01(edge * grain);
            }
            case GroundBrushShape.Splatter:
            {
                float radial = Mathf.Sqrt(nx * nx + ny * ny);
                float noise = Mathf.Sin(nx * 28.7f + ny * 11.3f) * Mathf.Sin(nx * 9.1f - ny * 31.4f);
                float holes = noise > 0.16f ? 1f : 0.55f;
                return Mathf.Clamp01((1f - radial) / 0.20f) * holes;
            }
            case GroundBrushShape.Ring:
            {
                float radial = Mathf.Sqrt(nx * nx + ny * ny);
                return Mathf.Clamp01(1f - Mathf.Abs(radial - 0.62f) / 0.16f);
            }
            case GroundBrushShape.Stripes:
            {
                float radial = Mathf.Sqrt(nx * nx + ny * ny);
                if (radial > 1f)
                    return 0f;
                float stripe = Mathf.Sin((nx + ny) * 24f) > 0f ? 1f : 0.28f;
                return Mathf.Clamp01((1f - radial) / 0.18f) * stripe;
            }
            default:
                return Mathf.Clamp01((1f - Mathf.Sqrt(nx * nx + ny * ny)) / 0.16f);
        }
    }

    private void DestroyTerrainSurfaceBrushPreviewTextures()
    {
        foreach (KeyValuePair<GroundBrushShape, Texture2D> pair in terrainSurfaceBrushPreviewTextures)
        {
            if (pair.Value != null)
                DestroyImmediate(pair.Value);
        }

        terrainSurfaceBrushPreviewTextures.Clear();
    }

    private static string GetGroundBrushShapeDisplayName(GroundBrushShape shape)
    {
        switch (shape)
        {
            case GroundBrushShape.Circle: return "圆形";
            case GroundBrushShape.SoftCircle: return "软圆";
            case GroundBrushShape.HardCircle: return "硬圆";
            case GroundBrushShape.Square: return "方形";
            case GroundBrushShape.SoftSquare: return "软方";
            case GroundBrushShape.Diamond: return "菱形";
            case GroundBrushShape.Hexagon: return "六边形";
            case GroundBrushShape.Star: return "星形";
            case GroundBrushShape.SoftNoise: return "软噪声";
            case GroundBrushShape.Splatter: return "散点";
            case GroundBrushShape.Ring: return "环形";
            case GroundBrushShape.Stripes: return "条纹";
            default: return "圆形";
        }
    }

    private bool TryEvaluateGroundBrushShape(float nx, float nz, out float normalizedDistance)
    {
        nx = Mathf.Abs(nx);
        nz = Mathf.Abs(nz);
        normalizedDistance = 0f;

        switch (groundBrushShape)
        {
            case GroundBrushShape.Circle:
            case GroundBrushShape.SoftCircle:
            case GroundBrushShape.HardCircle:
            case GroundBrushShape.SoftNoise:
            case GroundBrushShape.Splatter:
            case GroundBrushShape.Ring:
            case GroundBrushShape.Stripes:
            {
                float d = Mathf.Sqrt(nx * nx + nz * nz);
                if (d > 1f)
                    return false;
                normalizedDistance = d;
                return true;
            }
            case GroundBrushShape.Square:
            case GroundBrushShape.SoftSquare:
            {
                float d = Mathf.Max(nx, nz);
                if (d > 1f)
                    return false;
                normalizedDistance = d;
                return true;
            }
            case GroundBrushShape.Diamond:
            {
                float d = nx + nz;
                if (d > 1f)
                    return false;
                normalizedDistance = d;
                return true;
            }
            case GroundBrushShape.Hexagon:
            {
                float d = Mathf.Max(nx * 0.8660254f + nz * 0.5f, nz);
                if (d > 1f)
                    return false;
                normalizedDistance = d;
                return true;
            }
            case GroundBrushShape.Star:
            {
                float angle = Mathf.Atan2(nz, nx);
                float radial = Mathf.Sqrt(nx * nx + nz * nz);
                float starRadius = Mathf.Max(0.22f, 0.72f + 0.28f * Mathf.Cos(5f * angle));
                float d = radial / starRadius;
                if (d > 1f)
                    return false;
                normalizedDistance = d;
                return true;
            }
            default:
                normalizedDistance = Mathf.Sqrt(nx * nx + nz * nz);
                return normalizedDistance <= 1f;
        }
    }

    private void DrawGroundBrushShapePreview(Vector3 center, float size, Color fillColor, Color lineColor)
    {
        switch (groundBrushShape)
        {
            case GroundBrushShape.Circle:
            case GroundBrushShape.SoftCircle:
            case GroundBrushShape.HardCircle:
            case GroundBrushShape.SoftNoise:
            case GroundBrushShape.Splatter:
            case GroundBrushShape.Ring:
            case GroundBrushShape.Stripes:
                Handles.color = fillColor;
                Handles.DrawSolidDisc(center, Vector3.up, size * 0.5f);
                Handles.color = lineColor;
                Handles.DrawWireDisc(center, Vector3.up, size * 0.5f);
                break;
            case GroundBrushShape.Square:
            case GroundBrushShape.SoftSquare:
                DrawGroundBrushPolygonPreview(center, size, fillColor, lineColor, 4, 45f);
                break;
            case GroundBrushShape.Diamond:
                DrawGroundBrushPolygonPreview(center, size, fillColor, lineColor, 4, 0f);
                break;
            case GroundBrushShape.Hexagon:
                DrawGroundBrushPolygonPreview(center, size, fillColor, lineColor, 6, 30f);
                break;
            case GroundBrushShape.Star:
                DrawGroundBrushStarPreview(center, size, fillColor, lineColor);
                break;
            default:
                Handles.color = fillColor;
                Handles.DrawSolidDisc(center, Vector3.up, size * 0.5f);
                Handles.color = lineColor;
                Handles.DrawWireDisc(center, Vector3.up, size * 0.5f);
                break;
        }
    }

    private void DrawGroundBrushPolygonPreview(Vector3 center, float size, Color fillColor, Color lineColor, int sides, float rotationDegrees)
    {
        sides = Mathf.Max(3, sides);
        Vector3[] points = new Vector3[sides];
        float radius = size * 0.5f;
        float rotation = rotationDegrees * Mathf.Deg2Rad;
        for (int i = 0; i < sides; i++)
        {
            float a = rotation + Mathf.PI * 2f * i / sides;
            points[i] = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }

        Handles.color = fillColor;
        Handles.DrawAAConvexPolygon(points);
        Handles.color = lineColor;
        Vector3[] line = new Vector3[sides + 1];
        for (int i = 0; i < sides; i++)
            line[i] = points[i];
        line[sides] = points[0];
        Handles.DrawAAPolyLine(4f, line);
    }

    private void DrawGroundBrushStarPreview(Vector3 center, float size, Color fillColor, Color lineColor)
    {
        const int points = 10;
        Vector3[] verts = new Vector3[points];
        float outer = size * 0.5f;
        float inner = outer * 0.48f;
        float rotation = Mathf.PI * 0.5f;
        for (int i = 0; i < points; i++)
        {
            float radius = (i % 2 == 0) ? outer : inner;
            float a = rotation + Mathf.PI * 2f * i / points;
            verts[i] = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }

        Handles.color = fillColor;
        Handles.DrawAAConvexPolygon(verts);
        Handles.color = lineColor;
        Vector3[] line = new Vector3[points + 1];
        for (int i = 0; i < points; i++)
            line[i] = verts[i];
        line[points] = verts[0];
        Handles.DrawAAPolyLine(4f, line);
    }

    private bool IsSelectedSurfaceMaterialOverlay()
    {
        return IsSelectedSurfaceMaterialStamp() || IsSelectedSurfaceMaterialSpline();
    }

    private bool IsSelectedSurfaceMaterialStamp()
    {
        if (selectedSurfaceMaterial == null)
            return false;

        string mode = selectedSurfaceMaterial.textureDistributionMode.ToString();
        if (mode == "StampDecal")
            return true;

        // Compatibility guard for assets created while the menu/category was being refactored.
        // Category / name is not the source of truth long-term, but it prevents old "印章" assets from
        // accidentally falling back to TerrainLayer painting.
        string category = selectedSurfaceMaterial.category ?? string.Empty;
        string displayName = GetDisplayName(selectedSurfaceMaterial) ?? string.Empty;
        string id = selectedSurfaceMaterial.surfaceId ?? string.Empty;
        return category.Contains("印章")
            || category.Contains("贴花")
            || id.ToLowerInvariant().Contains("stamp")
            || displayName.Contains("印章")
            || displayName.Contains("贴花");
    }

    private bool IsSelectedSurfaceMaterialSpline()
    {
        if (selectedSurfaceMaterial == null)
            return false;

        string mode = selectedSurfaceMaterial.textureDistributionMode.ToString();
        if (mode == "SplinePattern")
            return true;

        // Compatibility guard for RoadLine assets created before textureDistributionMode was correctly aligned.
        string category = selectedSurfaceMaterial.category ?? string.Empty;
        string displayName = GetDisplayName(selectedSurfaceMaterial) ?? string.Empty;
        string id = selectedSurfaceMaterial.surfaceId ?? string.Empty;
        return category.Contains("样条")
            || category.Contains("画线")
            || displayName.Contains("样条")
            || displayName.Contains("画线")
            || displayName.Contains("路线")
            || id.ToLowerInvariant().Contains("spline")
            || id.ToLowerInvariant().Contains("roadline");
    }

    private bool IsTerrainDefaultToolSelected()
    {
        return currentKind == PlacementObjectKind.GroundSurfaceMaterial && selectedTerrainDefaultTool != TerrainDefaultTool.None;
    }

    private bool IsTerrainLayerSurfaceMaterialSelected()
    {
        return currentKind == PlacementObjectKind.GroundSurfaceMaterial
            && selectedSurfaceMaterial != null
            && !IsTerrainDefaultToolSelected()
            && !IsSelectedSurfaceMaterialOverlay();
    }

    private string GetTerrainDefaultToolDisplayName(TerrainDefaultTool tool)
    {
        switch (tool)
        {
            case TerrainDefaultTool.RaiseSurface: return "隆起地表";
            case TerrainDefaultTool.LowerSurface: return "凹陷地表";
            case TerrainDefaultTool.FlattenSurface: return "推平地表";
            case TerrainDefaultTool.PaintHole: return "Paint Hole 挖洞";
            default: return "Terrain 默认工具";
        }
    }

    private string GetTerrainDefaultToolOperationLabel(TerrainDefaultTool tool)
    {
        switch (tool)
        {
            case TerrainDefaultTool.RaiseSurface: return "TerrainData heightmap += 高度";
            case TerrainDefaultTool.LowerSurface: return "TerrainData heightmap -= 高度";
            case TerrainDefaultTool.FlattenSurface: return "TerrainData heightmap -> 指定高度";
            case TerrainDefaultTool.PaintHole: return "TerrainData holes：false 为洞";
            default: return "-";
        }
    }

    private GroundBrushMode GetGroundBrushModeForTerrainDefaultTool(TerrainDefaultTool tool)
    {
        switch (tool)
        {
            case TerrainDefaultTool.RaiseSurface: return GroundBrushMode.TerrainRaise;
            case TerrainDefaultTool.LowerSurface: return GroundBrushMode.TerrainLower;
            case TerrainDefaultTool.FlattenSurface: return GroundBrushMode.TerrainFlatten;
            case TerrainDefaultTool.PaintHole: return GroundBrushMode.TerrainPaintHole;
            default: return GroundBrushMode.SurfaceMaterial;
        }
    }

    private float GetActiveTerrainMaxWorldHeight()
    {
        Terrain terrain = activeGroundTerrain != null ? activeGroundTerrain : FindActiveGroundTerrain();
        if (terrain == null || terrain.terrainData == null)
            return 50f;

        return Mathf.Max(0.01f, terrain.terrainData.size.y);
    }

    private float SampleTerrainWorldHeight(Terrain terrain, Vector3 worldPosition)
    {
        if (terrain == null || terrain.terrainData == null)
            return 0f;

        Vector3 local = worldPosition - terrain.transform.position;
        return Mathf.Clamp(terrain.terrainData.GetInterpolatedHeight(
            Mathf.Clamp01(local.x / Mathf.Max(0.001f, terrain.terrainData.size.x)),
            Mathf.Clamp01(local.z / Mathf.Max(0.001f, terrain.terrainData.size.z))),
            0f,
            Mathf.Max(0.01f, terrain.terrainData.size.y));
    }

    private void BeginGroundSurfaceBrushStrokeUndo(Terrain terrain)
    {
        if (IsSelectedSurfaceMaterialOverlay())
            return;

        BeginTerrainBrushStrokeUndo(terrain);
    }

    private void EndGroundSurfaceBrushStrokeUndo(Terrain terrain)
    {
        if (IsSelectedSurfaceMaterialOverlay())
            return;

        EndTerrainBrushStrokeUndo(terrain);
    }

    private void ApplyGroundSurfaceBrushStroke(Vector3 worldPosition)
    {
        if (IsTerrainDefaultToolSelected())
        {
            ApplyTerrainDefaultToolBrushStroke(worldPosition);
            return;
        }

        if (selectedSurfaceMaterial == null)
            return;

        if (RoadLineRouteTrace)
            Debug.Log($"[MapPlacement RoadLineTrace] ApplyGroundSurfaceBrushStroke material={GetDisplayName(selectedSurfaceMaterial)} mode={groundBrushMode} spline={IsSelectedSurfaceMaterialSpline()} stamp={IsSelectedSurfaceMaterialStamp()} pos={worldPosition}");

        if (IsSelectedSurfaceMaterialSpline())
        {
            // 样条图案不再写入 GroundOverlay 大贴图。
            // 请使用“打开几何路径绘制器”生成 GroundSpline Mesh 对象。
            return;
        }

        if (IsSelectedSurfaceMaterialStamp())
        {
            // 印章 / 贴花后续接 Unity Decal / Projector；当前不再走四层 GroundOverlay。
            return;
        }

        ApplyTerrainSurfaceBrushStroke(worldPosition);
    }

    private bool SelectedSurfaceMaterialUsesFixedOverlaySize()
    {
        if (selectedSurfaceMaterial == null)
            return false;

        return selectedSurfaceMaterial.overlaySizeMode == GroundSurfaceOverlaySizeMode.FixedMaterialSize
               || selectedSurfaceMaterial.lockOverlaySize
               || selectedSurfaceMaterial.lockOverlayWorldSize
               || selectedSurfaceMaterial.lockSplineWorldWidth;
    }

    private float GetSelectedSplinePaintWidth()
    {
        if (selectedSurfaceMaterial == null)
            return Mathf.Max(0.05f, groundBrushSize);

        bool fixedSize = SelectedSurfaceMaterialUsesFixedOverlaySize();

        if (fixedSize)
        {
            float fixedWidth = selectedSurfaceMaterial.EffectiveFixedSplineWorldWidth;
            if (fixedWidth > 0.001f)
                return Mathf.Max(0.01f, fixedWidth);
        }

        // Spline / RoadLine is a material-sized asset: its default line width must drive the actual stroke.
        // The green brush remains as hit-preview / erase-range, not the RoadLine width source.
        if (selectedSurfaceMaterial.splineWorldWidth > 0.001f)
            return Mathf.Max(0.01f, selectedSurfaceMaterial.splineWorldWidth);

        return Mathf.Max(0.05f, groundBrushSize);
    }

    private Vector2 GetSelectedStampPaintSize()
    {
        if (selectedSurfaceMaterial == null)
            return new Vector2(Mathf.Max(0.05f, groundBrushSize), Mathf.Max(0.05f, groundBrushSize));

        // Stamp / 印花不是普通 Terrain 世界米数，而是沿用之前放置工具的“印花单位”。
        // 资源里填 1 × 1.53，表示 1 个旧印花单位 × 1.53 个旧印花单位；
        // 实际落地时再乘 GroundStampLegacyUnitWorldSize，恢复之前 1 的体感大小。
        if (selectedSurfaceMaterial.stampWorldSize.x > 0.001f && selectedSurfaceMaterial.stampWorldSize.y > 0.001f)
        {
            return new Vector2(
                Mathf.Max(0.01f, selectedSurfaceMaterial.stampWorldSize.x * GroundStampLegacyUnitWorldSize),
                Mathf.Max(0.01f, selectedSurfaceMaterial.stampWorldSize.y * GroundStampLegacyUnitWorldSize));
        }

        // 兼容旧数据：通用固定覆盖尺寸本来就是世界尺寸，不再额外乘旧印花单位。
        Vector2 fixedSizeValue = selectedSurfaceMaterial.EffectiveFixedOverlayWorldSize;
        if (fixedSizeValue.x > 0.001f && fixedSizeValue.y > 0.001f)
        {
            return new Vector2(
                Mathf.Max(0.01f, fixedSizeValue.x),
                Mathf.Max(0.01f, fixedSizeValue.y));
        }

        float size = Mathf.Max(0.05f, groundBrushSize);
        return new Vector2(size, size);
    }

    private void ApplyTerrainDefaultToolBrushStroke(Vector3 worldPosition)
    {
        if (activeGroundTerrain == null || activeGroundTerrain.terrainData == null)
            return;

        if (!hasLastGroundBrushPaintPosition)
        {
            ApplyTerrainDefaultToolBrush(activeGroundTerrain, worldPosition);
            lastGroundBrushPaintPosition = worldPosition;
            hasLastGroundBrushPaintPosition = true;
            return;
        }

        Vector3 from = lastGroundBrushPaintPosition;
        Vector3 to = worldPosition;
        float distance = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
        float stampSpacingFactor = groundBrushSize >= GroundBrushLargeSizeWarning ? 0.34f : 0.22f;
        float step = Mathf.Max(0.05f, groundBrushSize * stampSpacingFactor);
        int count = Mathf.Clamp(Mathf.CeilToInt(distance / step), 1, 128);

        for (int i = 1; i <= count; i++)
        {
            float t = i / (float)count;
            Vector3 p = Vector3.Lerp(from, to, t);
            ApplyTerrainDefaultToolBrush(activeGroundTerrain, p);
        }

        lastGroundBrushPaintPosition = worldPosition;
    }

    private void ApplyTerrainDefaultToolBrush(Terrain terrain, Vector3 worldPosition)
    {
        if (terrain == null || terrain.terrainData == null || selectedTerrainDefaultTool == TerrainDefaultTool.None)
            return;

        if (selectedTerrainDefaultTool == TerrainDefaultTool.PaintHole)
            PaintTerrainHole(terrain, worldPosition, terrainPaintHoleRestoreMode);
        else
            PaintTerrainHeight(terrain, worldPosition, selectedTerrainDefaultTool);
    }

    private void PaintTerrainHeight(Terrain terrain, Vector3 worldPosition, TerrainDefaultTool tool)
    {
        TerrainData data = terrain.terrainData;
        if (!TryWorldToTerrainUV(terrain, worldPosition, out Vector2 uv))
            return;

        int res = data.heightmapResolution;
        if (res <= 1)
            return;

        float radiusWorld = Mathf.Max(0.025f, groundBrushSize * 0.5f);
        int centerX = Mathf.RoundToInt(uv.x * (res - 1));
        int centerY = Mathf.RoundToInt(uv.y * (res - 1));
        int radiusX = Mathf.CeilToInt(radiusWorld / Mathf.Max(0.001f, data.size.x) * res);
        int radiusY = Mathf.CeilToInt(radiusWorld / Mathf.Max(0.001f, data.size.z) * res);

        int minX = Mathf.Clamp(centerX - radiusX, 0, res - 1);
        int maxX = Mathf.Clamp(centerX + radiusX, 0, res - 1);
        int minY = Mathf.Clamp(centerY - radiusY, 0, res - 1);
        int maxY = Mathf.Clamp(centerY + radiusY, 0, res - 1);
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width <= 0 || height <= 0)
            return;

        float[,] heights = data.GetHeights(minX, minY, width, height);
        Vector3 localCenter = worldPosition - terrain.transform.position;
        float innerRadius = radiusWorld * Mathf.Clamp01(groundBrushHardness);
        float strength01 = Mathf.Max(0.0001f, terrainDefaultToolStrength) / Mathf.Max(0.001f, data.size.y);
        float flattenTarget01 = Mathf.Clamp01(terrainFlattenWorldHeight / Mathf.Max(0.001f, data.size.y));

        for (int y = 0; y < height; y++)
        {
            int hy = minY + y;
            float wz = (hy / Mathf.Max(1f, res - 1f)) * data.size.z;
            for (int x = 0; x < width; x++)
            {
                int hx = minX + x;
                float wx = (hx / Mathf.Max(1f, res - 1f)) * data.size.x;
                float dx = wx - localCenter.x;
                float dz = wz - localCenter.z;

                float nx = Mathf.Abs(dx) / Mathf.Max(0.0001f, radiusWorld);
                float nz = Mathf.Abs(dz) / Mathf.Max(0.0001f, radiusWorld);
                if (!TryEvaluateGroundBrushShape(nx, nz, out float normalized))
                    continue;

                float distanceWorld = normalized * radiusWorld;
                float brushWeight;
                if (radiusWorld <= innerRadius + 0.0001f)
                    brushWeight = 1f;
                else if (distanceWorld <= innerRadius)
                    brushWeight = 1f;
                else
                    brushWeight = 1f - Mathf.Clamp01((distanceWorld - innerRadius) / Mathf.Max(0.0001f, radiusWorld - innerRadius));

                if (brushWeight <= 0f)
                    continue;

                float delta = strength01 * brushWeight;
                switch (tool)
                {
                    case TerrainDefaultTool.RaiseSurface:
                        heights[y, x] = Mathf.Clamp01(heights[y, x] + delta);
                        break;
                    case TerrainDefaultTool.LowerSurface:
                        heights[y, x] = Mathf.Clamp01(heights[y, x] - delta);
                        break;
                    case TerrainDefaultTool.FlattenSurface:
                        heights[y, x] = Mathf.MoveTowards(heights[y, x], flattenTarget01, delta);
                        break;
                }
            }
        }

        data.SetHeightsDelayLOD(minX, minY, heights);
        terrain.ApplyDelayedHeightmapModification();
        EditorUtility.SetDirty(data);
    }

    private void PaintTerrainHole(Terrain terrain, Vector3 worldPosition, bool restoreHole)
    {
        TerrainData data = terrain.terrainData;
        if (!TryWorldToTerrainUV(terrain, worldPosition, out Vector2 uv))
            return;

        int res = data.holesResolution;
        if (res <= 1)
            return;

        float radiusWorld = Mathf.Max(0.025f, groundBrushSize * 0.5f);
        int centerX = Mathf.RoundToInt(uv.x * (res - 1));
        int centerY = Mathf.RoundToInt(uv.y * (res - 1));
        int radiusX = Mathf.CeilToInt(radiusWorld / Mathf.Max(0.001f, data.size.x) * res);
        int radiusY = Mathf.CeilToInt(radiusWorld / Mathf.Max(0.001f, data.size.z) * res);

        int minX = Mathf.Clamp(centerX - radiusX, 0, res - 1);
        int maxX = Mathf.Clamp(centerX + radiusX, 0, res - 1);
        int minY = Mathf.Clamp(centerY - radiusY, 0, res - 1);
        int maxY = Mathf.Clamp(centerY + radiusY, 0, res - 1);
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width <= 0 || height <= 0)
            return;

        bool[,] holes = data.GetHoles(minX, minY, width, height);
        Vector3 localCenter = worldPosition - terrain.transform.position;
        float innerRadius = radiusWorld * Mathf.Clamp01(groundBrushHardness);
        float edgeThreshold = Mathf.Lerp(0.05f, 0.95f, Mathf.Clamp01(terrainDefaultToolStrength / 5f));

        for (int y = 0; y < height; y++)
        {
            int hy = minY + y;
            float wz = (hy / Mathf.Max(1f, res - 1f)) * data.size.z;
            for (int x = 0; x < width; x++)
            {
                int hx = minX + x;
                float wx = (hx / Mathf.Max(1f, res - 1f)) * data.size.x;
                float dx = wx - localCenter.x;
                float dz = wz - localCenter.z;

                float nx = Mathf.Abs(dx) / Mathf.Max(0.0001f, radiusWorld);
                float nz = Mathf.Abs(dz) / Mathf.Max(0.0001f, radiusWorld);
                if (!TryEvaluateGroundBrushShape(nx, nz, out float normalized))
                    continue;

                float distanceWorld = normalized * radiusWorld;
                float brushWeight;
                if (radiusWorld <= innerRadius + 0.0001f)
                    brushWeight = 1f;
                else if (distanceWorld <= innerRadius)
                    brushWeight = 1f;
                else
                    brushWeight = 1f - Mathf.Clamp01((distanceWorld - innerRadius) / Mathf.Max(0.0001f, radiusWorld - innerRadius));

                if (brushWeight < edgeThreshold)
                    continue;

                holes[y, x] = restoreHole;
            }
        }

        data.SetHoles(minX, minY, holes);
        EditorUtility.SetDirty(data);
    }

    private void ApplyTerrainSurfaceBrushStroke(Vector3 worldPosition)
    {
        if (activeGroundTerrain == null || activeGroundTerrain.terrainData == null || selectedSurfaceMaterial == null)
            return;

        if (!hasLastGroundBrushPaintPosition)
        {
            PaintTerrainSurfaceMaterial(activeGroundTerrain, worldPosition, selectedSurfaceMaterial);
            lastGroundBrushPaintPosition = worldPosition;
            hasLastGroundBrushPaintPosition = true;
            return;
        }

        Vector3 from = lastGroundBrushPaintPosition;
        Vector3 to = worldPosition;
        float distance = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
        float stampSpacingFactor = groundBrushSize >= GroundBrushLargeSizeWarning ? 0.34f : 0.22f;
        float step = Mathf.Max(0.05f, groundBrushSize * stampSpacingFactor);
        int count = Mathf.Clamp(Mathf.CeilToInt(distance / step), 1, 128);

        for (int i = 1; i <= count; i++)
        {
            float t = i / (float)count;
            Vector3 p = Vector3.Lerp(from, to, t);
            PaintTerrainSurfaceMaterial(activeGroundTerrain, p, selectedSurfaceMaterial);
        }

        lastGroundBrushPaintPosition = worldPosition;
    }

    private void PaintTerrainSurfaceMaterial(Terrain terrain, Vector3 worldPosition, GroundSurfaceMaterialDefinition material)
    {
        if (terrain == null || terrain.terrainData == null || material == null)
            return;

        TerrainData data = terrain.terrainData;
        int targetLayer = EnsureSelectedSurfaceMaterialTerrainLayer(terrain, material);
        if (targetLayer < 0)
            return;

        if (!TryWorldToTerrainUV(terrain, worldPosition, out Vector2 uv))
            return;

        int alphaWidth = data.alphamapWidth;
        int alphaHeight = data.alphamapHeight;
        int layers = data.alphamapLayers;
        if (alphaWidth <= 0 || alphaHeight <= 0 || layers <= targetLayer)
            return;

        float radiusWorld = Mathf.Max(0.025f, groundBrushSize * 0.5f);
        int centerX = Mathf.RoundToInt(uv.x * (alphaWidth - 1));
        int centerY = Mathf.RoundToInt(uv.y * (alphaHeight - 1));
        int radiusX = Mathf.CeilToInt(radiusWorld / Mathf.Max(0.001f, data.size.x) * alphaWidth);
        int radiusY = Mathf.CeilToInt(radiusWorld / Mathf.Max(0.001f, data.size.z) * alphaHeight);

        int minX = Mathf.Clamp(centerX - radiusX, 0, alphaWidth - 1);
        int maxX = Mathf.Clamp(centerX + radiusX, 0, alphaWidth - 1);
        int minY = Mathf.Clamp(centerY - radiusY, 0, alphaHeight - 1);
        int maxY = Mathf.Clamp(centerY + radiusY, 0, alphaHeight - 1);
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width <= 0 || height <= 0)
            return;

        float[,,] alphamaps = data.GetAlphamaps(minX, minY, width, height);
        Vector3 localCenter = worldPosition - terrain.transform.position;
        float innerRadius = radiusWorld * Mathf.Clamp01(groundBrushHardness);
        float opacity = Mathf.Clamp01(terrainSurfaceBrushOpacity);

        for (int y = 0; y < height; y++)
        {
            int ay = minY + y;
            float wz = (ay / Mathf.Max(1f, alphaHeight - 1f)) * data.size.z;
            for (int x = 0; x < width; x++)
            {
                int ax = minX + x;
                float wx = (ax / Mathf.Max(1f, alphaWidth - 1f)) * data.size.x;
                float dx = wx - localCenter.x;
                float dz = wz - localCenter.z;

                float nx = Mathf.Abs(dx) / Mathf.Max(0.0001f, radiusWorld);
                float nz = Mathf.Abs(dz) / Mathf.Max(0.0001f, radiusWorld);
                if (!TryEvaluateGroundBrushShape(nx, nz, out float normalized))
                    continue;

                float distanceWorld = normalized * radiusWorld;
                float brushWeight;
                if (radiusWorld <= innerRadius + 0.0001f)
                    brushWeight = 1f;
                else if (distanceWorld <= innerRadius)
                    brushWeight = 1f;
                else
                    brushWeight = 1f - Mathf.Clamp01((distanceWorld - innerRadius) / Mathf.Max(0.0001f, radiusWorld - innerRadius));

                float strength = Mathf.Clamp01(brushWeight * opacity);
                if (strength <= 0f)
                    continue;

                float currentTarget = alphamaps[y, x, targetLayer];
                float newTarget = Mathf.Clamp01(currentTarget + (1f - currentTarget) * strength);
                float oldOtherTotal = 0f;
                for (int l = 0; l < layers; l++)
                {
                    if (l == targetLayer)
                        continue;
                    oldOtherTotal += alphamaps[y, x, l];
                }

                alphamaps[y, x, targetLayer] = newTarget;
                float newOtherTotal = Mathf.Max(0f, 1f - newTarget);
                if (oldOtherTotal > 0.0001f)
                {
                    float scale = newOtherTotal / oldOtherTotal;
                    for (int l = 0; l < layers; l++)
                    {
                        if (l == targetLayer)
                            continue;
                        alphamaps[y, x, l] *= scale;
                    }
                }
                else
                {
                    for (int l = 0; l < layers; l++)
                    {
                        if (l != targetLayer)
                            alphamaps[y, x, l] = 0f;
                    }
                }
            }
        }

        data.SetAlphamaps(minX, minY, alphamaps);
        EditorUtility.SetDirty(data);
    }


    private void FillTerrainSurfaceMaterialScreenRect(Terrain terrain, GroundSurfaceMaterialDefinition material, Vector2 guiA, Vector2 guiB)
    {
        if (terrain == null || terrain.terrainData == null || material == null)
            return;

        Rect guiRect = MakePositiveGuiRect(guiA, guiB);
        if (guiRect.width <= 1f || guiRect.height <= 1f)
            return;

        TerrainData data = terrain.terrainData;
        int targetLayer = EnsureSelectedSurfaceMaterialTerrainLayer(terrain, material);
        if (targetLayer < 0)
            return;

        int alphaWidth = data.alphamapWidth;
        int alphaHeight = data.alphamapHeight;
        int layers = data.alphamapLayers;
        if (alphaWidth <= 0 || alphaHeight <= 0 || layers <= targetLayer)
            return;

        Undo.RegisterCompleteObjectUndo(data, "Screen Rect Fill Terrain Surface Material");

        // 这里故意不使用地图 / Terrain 的旋转轴。
        // 用户拖出来的是 SceneView 画面上的矩形，所以判断标准必须是 WorldToGUIPoint 后的屏幕矩形。
        float[,,] maps = data.GetAlphamaps(0, 0, alphaWidth, alphaHeight);
        bool changed = false;

        for (int y = 0; y < alphaHeight; y++)
        {
            float v = alphaHeight <= 1 ? 0f : y / (float)(alphaHeight - 1);

            for (int x = 0; x < alphaWidth; x++)
            {
                float u = alphaWidth <= 1 ? 0f : x / (float)(alphaWidth - 1);
                Vector3 terrainLocal = new Vector3(u * data.size.x, data.GetInterpolatedHeight(u, v) + 0.14f, v * data.size.z);
                Vector3 world = terrain.transform.TransformPoint(terrainLocal);
                Vector2 guiPoint = HandleUtility.WorldToGUIPoint(world);

                if (!guiRect.Contains(guiPoint))
                    continue;

                for (int l = 0; l < layers; l++)
                    maps[y, x, l] = l == targetLayer ? 1f : 0f;
                changed = true;
            }
        }

        if (!changed)
            return;

        data.SetAlphamaps(0, 0, maps);
        EditorUtility.SetDirty(data);
    }

    private void FillTerrainSurfaceMaterialRect(Terrain terrain, GroundSurfaceMaterialDefinition material, Vector3 worldA, Vector3 worldB)
    {
        if (terrain == null || terrain.terrainData == null || material == null)
            return;

        TerrainData data = terrain.terrainData;
        int targetLayer = EnsureSelectedSurfaceMaterialTerrainLayer(terrain, material);
        if (targetLayer < 0)
            return;

        Transform frame = GetTerrainRectFillFrameTransform(terrain);
        if (frame == null)
            return;

        Vector3 localA = frame.InverseTransformPoint(worldA);
        Vector3 localB = frame.InverseTransformPoint(worldB);
        float minFrameX = Mathf.Min(localA.x, localB.x);
        float maxFrameX = Mathf.Max(localA.x, localB.x);
        float minFrameZ = Mathf.Min(localA.z, localB.z);
        float maxFrameZ = Mathf.Max(localA.z, localB.z);

        if (Mathf.Abs(maxFrameX - minFrameX) <= 0.001f || Mathf.Abs(maxFrameZ - minFrameZ) <= 0.001f)
            return;

        int alphaWidth = data.alphamapWidth;
        int alphaHeight = data.alphamapHeight;
        int layers = data.alphamapLayers;
        if (alphaWidth <= 0 || alphaHeight <= 0 || layers <= targetLayer)
            return;

        // 先用四个角在 Terrain UV 上取一个包围盒，避免整张 alphamap 都扫。
        Vector3 c0 = frame.TransformPoint(new Vector3(minFrameX, 0f, minFrameZ));
        Vector3 c1 = frame.TransformPoint(new Vector3(maxFrameX, 0f, minFrameZ));
        Vector3 c2 = frame.TransformPoint(new Vector3(maxFrameX, 0f, maxFrameZ));
        Vector3 c3 = frame.TransformPoint(new Vector3(minFrameX, 0f, maxFrameZ));

        bool hasAnyUv = false;
        float minU = 1f;
        float maxU = 0f;
        float minV = 1f;
        float maxV = 0f;
        AccumulateTerrainUvForRectCorner(terrain, c0, ref hasAnyUv, ref minU, ref maxU, ref minV, ref maxV);
        AccumulateTerrainUvForRectCorner(terrain, c1, ref hasAnyUv, ref minU, ref maxU, ref minV, ref maxV);
        AccumulateTerrainUvForRectCorner(terrain, c2, ref hasAnyUv, ref minU, ref maxU, ref minV, ref maxV);
        AccumulateTerrainUvForRectCorner(terrain, c3, ref hasAnyUv, ref minU, ref maxU, ref minV, ref maxV);
        if (!hasAnyUv)
            return;

        int minX = Mathf.Clamp(Mathf.FloorToInt(minU * (alphaWidth - 1)) - 1, 0, alphaWidth - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(maxU * (alphaWidth - 1)) + 1, 0, alphaWidth - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(minV * (alphaHeight - 1)) - 1, 0, alphaHeight - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(maxV * (alphaHeight - 1)) + 1, 0, alphaHeight - 1);

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width <= 0 || height <= 0)
            return;

        Undo.RegisterCompleteObjectUndo(data, "Rect Fill Terrain Surface Material");
        float[,,] maps = data.GetAlphamaps(minX, minY, width, height);
        bool changed = false;

        for (int y = 0; y < height; y++)
        {
            int alphaY = minY + y;
            float v = alphaHeight <= 1 ? 0f : alphaY / (float)(alphaHeight - 1);

            for (int x = 0; x < width; x++)
            {
                int alphaX = minX + x;
                float u = alphaWidth <= 1 ? 0f : alphaX / (float)(alphaWidth - 1);

                Vector3 terrainLocal = new Vector3(u * data.size.x, data.GetInterpolatedHeight(u, v), v * data.size.z);
                Vector3 world = terrain.transform.TransformPoint(terrainLocal);
                Vector3 frameLocal = frame.InverseTransformPoint(world);

                if (frameLocal.x < minFrameX || frameLocal.x > maxFrameX || frameLocal.z < minFrameZ || frameLocal.z > maxFrameZ)
                    continue;

                for (int l = 0; l < layers; l++)
                    maps[y, x, l] = l == targetLayer ? 1f : 0f;
                changed = true;
            }
        }

        if (!changed)
            return;

        data.SetAlphamaps(minX, minY, maps);
        EditorUtility.SetDirty(data);
    }

    private void AccumulateTerrainUvForRectCorner(Terrain terrain, Vector3 world, ref bool hasAnyUv, ref float minU, ref float maxU, ref float minV, ref float maxV)
    {
        if (terrain == null || terrain.terrainData == null)
            return;

        TerrainData data = terrain.terrainData;
        Vector3 local = terrain.transform.InverseTransformPoint(world);
        if (data.size.x <= 0.001f || data.size.z <= 0.001f)
            return;

        float u = Mathf.Clamp01(local.x / data.size.x);
        float v = Mathf.Clamp01(local.z / data.size.z);
        if (!hasAnyUv)
        {
            minU = maxU = u;
            minV = maxV = v;
            hasAnyUv = true;
            return;
        }

        minU = Mathf.Min(minU, u);
        maxU = Mathf.Max(maxU, u);
        minV = Mathf.Min(minV, v);
        maxV = Mathf.Max(maxV, v);
    }

    private int EnsureSelectedSurfaceMaterialTerrainLayer(Terrain terrain, GroundSurfaceMaterialDefinition material)
    {
        if (terrain == null || terrain.terrainData == null || material == null)
            return -1;

        TerrainData data = terrain.terrainData;
        TerrainLayer layer = GetOrCreateTerrainLayerAsset(material);
        if (layer == null)
            return -1;

        TerrainLayer[] layers = data.terrainLayers ?? Array.Empty<TerrainLayer>();
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == layer)
                return i;
        }

        Undo.RecordObject(data, "Add Terrain Layer");
        Array.Resize(ref layers, layers.Length + 1);
        layers[layers.Length - 1] = layer;
        data.terrainLayers = layers;
        EditorUtility.SetDirty(data);
        return layers.Length - 1;
    }

    private TerrainLayer GetOrCreateTerrainLayerAsset(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return null;

        EnsureAssetFolder(TerrainLayerAssetFolder);
        EnsureAssetFolder(TerrainTextureAssetFolder);

        string key = MakeSafeAssetName(!string.IsNullOrWhiteSpace(material.surfaceId) ? material.surfaceId : material.name);
        string layerPath = $"{TerrainLayerAssetFolder}/TL_{key}.terrainlayer";
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, layerPath);
        }

        Texture2D diffuse = GetTerrainLayerDiffuseTexture(material, key);
        if (diffuse != null)
            layer.diffuseTexture = diffuse;
        layer.tileSize = new Vector2(4f, 4f);
        layer.tileOffset = Vector2.zero;
        layer.specular = Color.black;
        layer.metallic = 0f;
        layer.smoothness = 0.25f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    private Texture2D GetTerrainLayerDiffuseTexture(GroundSurfaceMaterialDefinition material, string key)
    {
        if (material == null)
            return null;

        if (material.baseTexture is Texture2D baseTexture)
            return baseTexture;

        if (material.baseMaterial != null)
        {
            Texture t = null;
            if (material.baseMaterial.HasProperty("_BaseMap"))
                t = material.baseMaterial.GetTexture("_BaseMap");
            if (t == null && material.baseMaterial.HasProperty("_MainTex"))
                t = material.baseMaterial.GetTexture("_MainTex");
            if (t is Texture2D tex)
                return tex;
        }

        string texturePath = $"{TerrainTextureAssetFolder}/TX_{key}.asset";
        Texture2D generated = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (generated != null)
            return generated;

        generated = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
        generated.name = $"TX_{key}";
        Color c = material.baseColor;
        Color[] pixels = Enumerable.Repeat(c, 16 * 16).ToArray();
        generated.SetPixels(pixels);
        generated.Apply(false, false);
        AssetDatabase.CreateAsset(generated, texturePath);
        return generated;
    }

    private string MakeSafeAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "surface";
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new string(value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "surface" : safe;
    }

    private void FillActiveTerrainSurfaceMaterial(GroundSurfaceMaterialDefinition material)
    {
        Terrain terrain = activeGroundTerrain != null ? activeGroundTerrain : FindActiveGroundTerrain();
        if (terrain == null || terrain.terrainData == null || material == null)
            return;

        TerrainData data = terrain.terrainData;
        int targetLayer = EnsureSelectedSurfaceMaterialTerrainLayer(terrain, material);
        if (targetLayer < 0)
            return;

        Undo.RegisterCompleteObjectUndo(data, "Fill Terrain Surface Material");
        int width = data.alphamapWidth;
        int height = data.alphamapHeight;
        int layers = data.alphamapLayers;
        float[,,] maps = new float[height, width, layers];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int l = 0; l < layers; l++)
                    maps[y, x, l] = l == targetLayer ? 1f : 0f;
            }
        }
        data.SetAlphamaps(0, 0, maps);
        EditorUtility.SetDirty(data);
    }

    private bool HandleGroundBrushUndoRedoShortcut(Event e)
    {
        if (e == null || e.type != EventType.KeyDown)
            return false;

        bool actionKey = e.control || e.command;
        if (!actionKey)
            return false;

        bool undo = e.keyCode == KeyCode.Z && !e.shift;
        bool redo = e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift);
        if (!undo && !redo)
            return false;

        if (groundBrushPainting)
        {
            groundBrushPainting = false;
            hasLastGroundBrushPaintPosition = false;
            FlushDeferredGroundBrushVisualBake();
            EndGroundBrushStrokeUndo();
            ResetGroundEraseStrokeTracking();
            GUIUtility.hotControl = 0;
        }

        if (undo)
            Undo.PerformUndo();
        else
            Undo.PerformRedo();

        e.Use();
        return true;
    }

    private void UpdateGroundBrushPosition(Vector2 mousePosition)
    {
        if (activeGroundBlock == null)
        {
            hasValidGroundBrushPosition = false;
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

        // 优先打到 GroundCollider / GroundVisual 的真实表面。
        // 这样 GroundBlock 不在 Y=0、或者后面有高度图/斜面时，笔刷也能跟着地面。
        if (TryRaycastActiveGroundBlock(ray, activeGroundBlock, out Vector3 hitOnGround))
        {
            if (activeGroundBlock.TryWorldToUV(hitOnGround, out _))
            {
                lastGroundBrushPosition = hitOnGround;
                hasValidGroundBrushPosition = true;
                return;
            }
        }

        // 兜底：打默认地面高度平面。这里用 GroundBlock 的数据中心，而不是写死世界 Y=0。
        float planeY = activeGroundBlock.mapBoundsCenter.y + activeGroundBlock.defaultGroundHeight;
        Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 hit = ray.GetPoint(enter);
            if (activeGroundBlock.TryWorldToUV(hit, out _))
            {
                lastGroundBrushPosition = hit;
                hasValidGroundBrushPosition = true;
                return;
            }
        }

        hasValidGroundBrushPosition = false;
    }

    private bool TryRaycastActiveGroundBlock(Ray ray, BaseGroundBlock block, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (block == null)
            return false;

        float bestDistance = float.PositiveInfinity;
        bool found = false;

        if (block.groundColliderRoot != null)
        {
            Collider[] colliders = block.groundColliderRoot.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in colliders)
            {
                if (c == null || !c.enabled)
                    continue;

                if (c.Raycast(ray, out RaycastHit hit, 100000f) && hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    hitPoint = hit.point;
                    found = true;
                }
            }
        }

        if (block.groundVisualRoot != null)
        {
            Collider[] visualColliders = block.groundVisualRoot.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in visualColliders)
            {
                if (c == null || !c.enabled)
                    continue;

                if (c.Raycast(ray, out RaycastHit hit, 100000f) && hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    hitPoint = hit.point;
                    found = true;
                }
            }
        }

        return found;
    }

    private void DrawGroundBrushPreview()
    {
        if (!groundBrushPreviewMask || activeGroundBlock == null)
            return;

        Color brushColor = GetGroundBrushPreviewColor();

        if (!hasValidGroundBrushPosition)
        {
            // 回调已经进入，但鼠标没有命中 GroundBlock 数据域时，仍给一个醒目的屏幕提示，
            // 避免误判为 SceneView 没有绘制笔刷。
            Handles.BeginGUI();
            Rect warn = new Rect(12f, 112f, 380f, 28f);
            EditorGUI.DrawRect(warn, new Color(0.12f, 0.04f, 0.04f, 0.72f));
            GUI.Label(new Rect(warn.x + 10f, warn.y + 5f, warn.width - 20f, 18f), "笔刷未命中 GroundBlock：请把鼠标移到地图边界内", EditorStyles.miniLabel);
            Handles.EndGUI();
            return;
        }

        if (IsSelectedSurfaceMaterialStamp() && !groundOverlayEraseMode)
        {
            DrawGroundStampPlacementPreview();
            return;
        }

        Color oldColor = Handles.color;
        CompareFunction oldZTest = Handles.zTest;
        Handles.zTest = CompareFunction.Always;

        Color fillColor = new Color(brushColor.r, brushColor.g, brushColor.b, 0.18f);
        Color lineColor = new Color(brushColor.r, brushColor.g, brushColor.b, 1.00f);

        Vector3 center = lastGroundBrushPosition;
        center.y = activeGroundBlock.mapBoundsCenter.y + activeGroundBlock.defaultGroundHeight + 0.12f;
        float size = Mathf.Max(0.05f, groundBrushSize);

        // 先画一个醒目的中心标记。即使水平圆盘被 Scene 视角/网格视觉淹掉，也能确认笔刷存在。
        Handles.color = lineColor;
        Handles.SphereHandleCap(0, center, Quaternion.identity, Mathf.Max(0.18f, size * 0.08f), EventType.Repaint);
        Handles.DrawLine(center + Vector3.left * size * 0.5f, center + Vector3.right * size * 0.5f);
        Handles.DrawLine(center + Vector3.forward * size * 0.5f, center + Vector3.back * size * 0.5f);

        if (groundBrushShape == GroundBrushShape.Circle)
        {
            Handles.color = fillColor;
            Handles.DrawSolidDisc(center, Vector3.up, size * 0.5f);
            Handles.color = lineColor;
            Handles.DrawWireDisc(center, Vector3.up, size * 0.5f);
        }
        else
        {
            Vector3 halfX = Vector3.right * size * 0.5f;
            Vector3 halfZ = Vector3.forward * size * 0.5f;
            Vector3 p0 = center - halfX - halfZ;
            Vector3 p1 = center + halfX - halfZ;
            Vector3 p2 = center + halfX + halfZ;
            Vector3 p3 = center - halfX + halfZ;
            Handles.color = fillColor;
            Handles.DrawAAConvexPolygon(p0, p1, p2, p3);
            Handles.color = lineColor;
            Handles.DrawAAPolyLine(4f, p0, p1, p2, p3, p0);
        }

        // 再叠一个屏幕空间的小圆点，防止世界空间笔刷被 Scene 视角看成一条线。
        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(center);
        Handles.BeginGUI();
        Rect dot = new Rect(guiPoint.x - 5f, guiPoint.y - 5f, 10f, 10f);
        EditorGUI.DrawRect(dot, lineColor);
        Handles.EndGUI();

        Handles.zTest = oldZTest;
        Handles.color = oldColor;
    }

    private Color GetGroundBrushPreviewColor()
    {
        switch (groundBrushMode)
        {
            case GroundBrushMode.ShapeAdd: return GroundBrushAddColor;
            case GroundBrushMode.ShapeErase: return GroundBrushEraseColor;
            case GroundBrushMode.SurfaceMaterial: return GroundBrushSurfaceColor;
            case GroundBrushMode.TerrainRaise: return new Color(0.55f, 0.85f, 1f, 0.32f);
            case GroundBrushMode.TerrainLower: return new Color(0.45f, 0.55f, 1f, 0.32f);
            case GroundBrushMode.TerrainFlatten: return new Color(1f, 0.78f, 0.20f, 0.32f);
            case GroundBrushMode.TerrainPaintHole: return new Color(0.18f, 0.18f, 0.18f, 0.38f);
            default: return GroundBrushAddColor;
        }
    }

    private void DrawGroundBrushSceneOverlay()
    {
        Handles.BeginGUI();
        Rect rect = new Rect(12f, 12f, 420f, 116f);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.58f));
        GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f), "地面刷模式 / 地表材质", EditorStyles.boldLabel);
        string materialName = IsTerrainDefaultToolSelected() ? GetTerrainDefaultToolDisplayName(selectedTerrainDefaultTool) : (selectedSurfaceMaterial != null ? GetDisplayName(selectedSurfaceMaterial) : "未选择地表材质");
        GUI.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 18f), $"工具：{materialName}", EditorStyles.label);
        string mode = GetGroundBrushModeLabel(groundBrushMode);
        string shape = GetGroundBrushShapeDisplayName(groundBrushShape);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 50f, rect.width - 20f, 18f), $"{mode} / {shape} / 尺寸 {groundBrushSize:0.##} / 硬度 {groundBrushHardness:0.##}", EditorStyles.miniLabel);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 68f, rect.width - 20f, 18f), $"显示：{GetGroundBrushDebugViewLabel(groundBrushDebugView)}", EditorStyles.miniLabel);
        string state = activeGroundBlock == null ? "没有找到 BaseGroundBlock" : (hasValidGroundBrushPosition ? $"命中 GroundBlock：{lastGroundBrushPosition.x:0.##}, {lastGroundBrushPosition.z:0.##}，左键涂刷" : "已找到 BaseGroundBlock，但鼠标没有命中地面数据域");
        GUI.Label(new Rect(rect.x + 10f, rect.y + 88f, rect.width - 20f, 18f), state, EditorStyles.miniLabel);
        Handles.EndGUI();
    }

    private string GetGroundBrushDebugViewLabel(GroundBrushDebugView view)
    {
        switch (view)
        {
            case GroundBrushDebugView.ShapeMask: return "ShapeMask 调试（柔绿=有地面 / 暗红=无地面）";
            case GroundBrushDebugView.SurfaceMaterial: return "地表材质调试";
            case GroundBrushDebugView.Normal:
            default: return "正常地面";
        }
    }

    private void DrawGroundDisplayModeSceneOverlay()
    {
        Handles.BeginGUI();
        Rect rect = new Rect(12f, 12f, 420f, 82f);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.54f));
        GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f), "地面显示调试 / 地表材质", EditorStyles.boldLabel);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 18f), $"显示：{GetGroundBrushDebugViewLabel(groundBrushDebugView)}", EditorStyles.miniLabel);

        string state = activeGroundBlock == null
            ? "没有找到 BaseGroundBlock"
            : "调试覆盖层已显示；进入 Scene 放置模式后可继续涂刷";
        GUI.Label(new Rect(rect.x + 10f, rect.y + 50f, rect.width - 20f, 18f), state, EditorStyles.miniLabel);
        Handles.EndGUI();
    }

    private void ApplyGroundVisualDisplayModeToActiveBlock()
    {
        ApplyGroundVisualDisplayModeToAllBlocks();
    }

    private void ApplyGroundVisualDisplayModeToAllBlocks()
    {
        BaseGroundBlock[] blocks = FindObjectsOfType<BaseGroundBlock>(true);
        if (blocks == null || blocks.Length == 0)
            return;

        foreach (BaseGroundBlock block in blocks)
            ApplyGroundVisualDisplayMode(block);
    }

    private void RestoreGroundVisualDisplayForAllBlocks()
    {
        BaseGroundBlock[] blocks = FindObjectsOfType<BaseGroundBlock>(true);
        if (blocks == null || blocks.Length == 0)
            return;

        foreach (BaseGroundBlock block in blocks)
        {
            if (block == null)
                continue;

            // 正常显示统一交回 BaseGroundBlock 的运行时刷新。
            // 它会根据 useUrpLitShadowSafeOutput 选择 URP/Lit 烘焙输出，避免旧 SurfacePreview/SurfaceIndexMap 路径抢写材质。
            block.RefreshGroundVisualRuntime();
        }
    }

    private void ApplyGroundVisualDisplayMode(BaseGroundBlock block)
    {
        if (block == null)
            return;

        // 不再通过 Renderer.enabled 切通道，避免误伤其它已有可视化系统。
        // 正常显示时优先显示 SurfaceMaterialPreview，让“刷地表材质”在普通视图里也有结果。
        // ShapeMask / 地表材质调试只画 Scene Overlay，不再叠第二套 GroundVisual 调试色。
        if (groundBrushDebugView == GroundBrushDebugView.Normal)
        {
            // 正常地面只走真实 GroundVisual。不要再叠编辑器调试/Overlay 材质。
            block.RefreshGroundVisualRuntime();
        }
        else
        {
            // 调试模式只影响 SceneView Overlay，不改 GroundVisual。
            // 这样 Game 视图不会被编辑器调试显示模式污染，也不会发生材质抢写闪烁。
        }
    }

    private void SetGroundVisualRenderersVisible(BaseGroundBlock block, bool visible)
    {
        // 保留空实现，避免旧调用点误关 Renderer。
        // GroundVisual 的显示不再由本工具直接开关。
    }

    private void DrawGroundBlockDebugOverlay()
    {
        if (activeGroundBlock == null || groundBrushDebugView == GroundBrushDebugView.Normal)
            return;

        Bounds bounds = activeGroundBlock.WorldBounds;
        if (bounds.size.x <= 0.01f || bounds.size.z <= 0.01f)
            return;

        int gridX = Mathf.Clamp(groundBrushDebugGrid, 8, 96);
        int gridZ = Mathf.Clamp(Mathf.RoundToInt(gridX * bounds.size.z / Mathf.Max(0.01f, bounds.size.x)), 8, 96);
        float stepX = bounds.size.x / gridX;
        float stepZ = bounds.size.z / gridZ;
        float y = activeGroundBlock.mapBoundsCenter.y + activeGroundBlock.defaultGroundHeight + 0.055f;

        CompareFunction oldZTest = Handles.zTest;
        Color oldColor = Handles.color;
        Handles.zTest = CompareFunction.Always;

        Texture2D shapeMask = activeGroundBlock.groundShapeMask;
        Texture2D materialPreview = activeGroundBlock.surfaceMaterialPreviewTexture;

        for (int iz = 0; iz < gridZ; iz++)
        {
            float z0 = bounds.min.z + stepZ * iz;
            float z1 = z0 + stepZ;
            float v = (iz + 0.5f) / gridZ;
            for (int ix = 0; ix < gridX; ix++)
            {
                float x0 = bounds.min.x + stepX * ix;
                float x1 = x0 + stepX;
                float u = (ix + 0.5f) / gridX;

                float mask = shapeMask != null ? shapeMask.GetPixelBilinear(u, v).a : 1f;
                bool solid = mask >= activeGroundBlock.groundMaskThreshold;

                Color c;
                if (groundBrushDebugView == GroundBrushDebugView.ShapeMask)
                {
                    c = solid
                        ? new Color(0.10f, 0.70f, 0.25f, 0.34f)
                        : new Color(0.78f, 0.12f, 0.10f, 0.30f);
                }
                else
                {
                    if (!solid)
                        c = new Color(0.78f, 0.12f, 0.10f, 0.30f);
                    else if (materialPreview != null)
                    {
                        c = materialPreview.GetPixelBilinear(u, v);
                        c.a = 0.38f;
                    }
                    else
                        c = new Color(0.20f, 0.65f, 0.85f, 0.34f);
                }

                Handles.color = c;
                Vector3 p0 = new Vector3(x0, y, z0);
                Vector3 p1 = new Vector3(x1, y, z0);
                Vector3 p2 = new Vector3(x1, y, z1);
                Vector3 p3 = new Vector3(x0, y, z1);
                Handles.DrawAAConvexPolygon(p0, p1, p2, p3);
            }
        }

        Handles.color = new Color(1f, 1f, 1f, 0.11f);
        Handles.DrawWireCube(new Vector3(bounds.center.x, y, bounds.center.z), new Vector3(bounds.size.x, 0f, bounds.size.z));

        Handles.zTest = oldZTest;
        Handles.color = oldColor;
    }

    private string GetGroundBrushModeLabel(GroundBrushMode mode)
    {
        switch (mode)
        {
            case GroundBrushMode.ShapeAdd: return "添加地面";
            case GroundBrushMode.ShapeErase: return "擦除地面";
            case GroundBrushMode.SurfaceMaterial: return "Terrain 地表材质";
            case GroundBrushMode.StampOverlay: return groundOverlayEraseMode ? "Overlay 擦除：印章 / 贴花" : "Overlay 印章 / 贴花";
            case GroundBrushMode.SplineOverlay: return groundOverlayEraseMode ? "Overlay 擦除：样条图案" : "Overlay 样条图案";
            case GroundBrushMode.TerrainRaise: return "隆起地表";
            case GroundBrushMode.TerrainLower: return "凹陷地表";
            case GroundBrushMode.TerrainFlatten: return "推平地表";
            case GroundBrushMode.TerrainPaintHole: return terrainPaintHoleRestoreMode ? "Paint Hole：补洞" : "Paint Hole：挖洞";
            default: return mode.ToString();
        }
    }

    private void ApplyGroundBrush(Vector3 worldPosition)
    {
        if (activeGroundBlock == null)
            return;

        switch (groundBrushMode)
        {
            case GroundBrushMode.ShapeAdd:
                PaintGroundShapeMask(activeGroundBlock, worldPosition, 1f);
                break;
            case GroundBrushMode.ShapeErase:
                PaintGroundShapeMask(activeGroundBlock, worldPosition, 0f);
                TrackGroundEraseStroke(worldPosition);
                break;
            case GroundBrushMode.SurfaceMaterial:
                PaintGroundSurfaceMaterial(activeGroundBlock, worldPosition, selectedSurfaceMaterial);
                break;
        }
    }

    private void ApplyGroundBrushStroke(Vector3 worldPosition)
    {
        if (activeGroundBlock == null)
            return;

        if (!hasLastGroundBrushPaintPosition)
        {
            ApplyGroundBrush(worldPosition);
            lastGroundBrushPaintPosition = worldPosition;
            hasLastGroundBrushPaintPosition = true;
            return;
        }

        Vector3 from = lastGroundBrushPaintPosition;
        Vector3 to = worldPosition;
        float distance = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
        // 大笔刷不需要像细笔一样密集补点，否则每个 MouseDrag 会拆成太多 stamp。
        // 这里按笔刷尺寸提高步长，避免“刷子越大越像全图烘焙”。
        float stampSpacingFactor = groundBrushSize >= GroundBrushLargeSizeWarning ? 0.34f : 0.22f;
        float step = Mathf.Max(0.05f, groundBrushSize * stampSpacingFactor);
        int count = Mathf.Clamp(Mathf.CeilToInt(distance / step), 1, 128);

        for (int i = 1; i <= count; i++)
        {
            float t = i / (float)count;
            Vector3 p = Vector3.Lerp(from, to, t);
            ApplyGroundBrush(p);
        }

        lastGroundBrushPaintPosition = worldPosition;
    }

    private Bounds GetGroundBrushWorldBounds(Vector3 worldPosition, float extraWorldPadding = 0f)
    {
        float size = Mathf.Max(0.05f, groundBrushSize) + Mathf.Max(0f, extraWorldPadding) * 2f;
        Vector3 center = new Vector3(worldPosition.x, 0f, worldPosition.z);
        Vector3 extents = new Vector3(size * 0.65f, 1000f, size * 0.65f);
        return new Bounds(center, extents * 2f);
    }

    private void ResetGroundEraseStrokeTracking()
    {
        groundEraseTouchedDuringStroke = false;
        groundEraseStrokeWorldBounds = new Bounds(Vector3.zero, Vector3.zero);
    }

    private void TrackGroundEraseStroke(Vector3 worldPosition)
    {
        Bounds brushBounds = GetGroundBrushWorldBounds(worldPosition);

        if (!groundEraseTouchedDuringStroke)
        {
            groundEraseStrokeWorldBounds = brushBounds;
            groundEraseTouchedDuringStroke = true;
        }
        else
        {
            groundEraseStrokeWorldBounds.Encapsulate(brushBounds.min);
            groundEraseStrokeWorldBounds.Encapsulate(brushBounds.max);
        }
    }

    private void CleanupTerrainDecorationsAfterGroundErase(BaseGroundBlock block, Bounds eraseBounds)
    {
        if (block == null)
            return;

        TerrainDecorationRuntimeBinder[] binders = FindObjectsOfType<TerrainDecorationRuntimeBinder>(true);
        if (binders == null || binders.Length == 0)
            return;

        List<GameObject> targets = new List<GameObject>();
        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null || binder.gameObject == null || !binder.gameObject.scene.IsValid())
                continue;

            Vector3 anchor = GetTerrainDecorationAnchorWorldPosition(binder);
            Vector3 flatAnchor = new Vector3(anchor.x, eraseBounds.center.y, anchor.z);
            if (!eraseBounds.Contains(flatAnchor))
                continue;

            if (!block.HasGroundAtWorld(anchor))
                targets.Add(binder.gameObject);
        }

        if (targets.Count == 0)
            return;

        int undoGroup = groundBrushStrokeUndoActive ? groundBrushStrokeUndoGroup : Undo.GetCurrentGroup();
        if (!groundBrushStrokeUndoActive)
            Undo.SetCurrentGroupName("Erase ground and cleanup terrain decorations");

        foreach (GameObject go in targets)
        {
            if (go == null)
                continue;
            Undo.DestroyObjectImmediate(go);
        }

        if (!groundBrushStrokeUndoActive)
            Undo.CollapseUndoOperations(undoGroup);

        // 鼠标松开后如果确实撤除了地形装饰物，播放 2 号编辑器 SE。
        PlayEditorSound(DeleteSoundPath);

        RefreshPlacedCache();
        SceneView.RepaintAll();
        Repaint();
        Debug.Log($"[GroundBrush] 地面擦除/清空后自动清理了 {targets.Count} 个落在无地面区域的地形装饰物。", block);
    }

    private Vector3 GetTerrainDecorationAnchorWorldPosition(TerrainDecorationRuntimeBinder binder)
    {
        if (binder == null)
            return Vector3.zero;

        Transform t = binder.transform;
        Transform anchor = t.Find("PlacementAnchor") ?? t.Find("Anchor") ?? t.Find("Root") ?? t;
        return anchor.position;
    }

    private bool HandleTerrainDecorationPreviewRotationInput(SceneView sceneView, Event e)
    {
        if (e == null || e.type != EventType.ScrollWheel)
            return false;

        if (!(e.control || e.command))
            return false;

        if (e.shift || e.alt)
            return false;

        if (EditorGUIUtility.editingTextField)
            return false;

        float direction = e.delta.y < 0f ? 1f : -1f;
        terrainDecorationPreviewRotationY = Mathf.Repeat(terrainDecorationPreviewRotationY + direction * TerrainDecorationPreviewRotationStep + 180f, 360f) - 180f;

        if (previewInstance != null)
            previewInstance.transform.rotation = Quaternion.Euler(GetRuleEuler(selectedDefinition, currentPreviewResult));

        e.Use();
        sceneView.Repaint();
        Repaint();
        return true;
    }

    private bool HandleTerrainDecorationPreviewHeightInput(SceneView sceneView, Event e)
    {
        if (e == null || e.type != EventType.ScrollWheel)
            return false;

        EventModifiers modifiers = e.modifiers;

        // Unity / 鼠标驱动在 Shift+滚轮时并不总是稳定把状态表现为“纯 Shift”。
        // 有些设备会把垂直滚轮转成 horizontal delta，有些 SceneView 状态会附带其它 modifier。
        // 这里的规则只排除会和其它工具冲突的 Ctrl/Command/Alt；只要 Shift 按下就作为高度输入。
        bool hasShift = e.shift || (modifiers & EventModifiers.Shift) != 0;
        bool hasCtrlOrCommand = e.control || e.command
            || (modifiers & EventModifiers.Control) != 0
            || (modifiers & EventModifiers.Command) != 0;
        bool hasAlt = e.alt || (modifiers & EventModifiers.Alt) != 0;

        if (!hasShift || hasCtrlOrCommand || hasAlt)
            return false;

        if (EditorGUIUtility.editingTextField)
            return false;

        // Shift+滚轮在部分系统里会变成横向滚动，所以优先取幅度更大的轴。
        float wheel = Mathf.Abs(e.delta.y) >= Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
        if (Mathf.Abs(wheel) < 0.0001f)
            return false;

        // Shift+滚轮只做“当前位置增量升降”，不重新射线贴地。
        // 这样不会被 placementY / GroundY / Raycast 反复拉回。
        float delta = -Mathf.Sign(wheel) * TerrainDecorationPreviewHeightStep;
        terrainDecorationPreviewHeightOffset += delta;

        Vector3 currentPosition;
        if (previewInstance != null)
            currentPosition = previewInstance.transform.position;
        else if (hasValidPreviewPosition)
            currentPosition = lastPreviewPosition;
        else
            currentPosition = terrainDecorationPreviewBasePosition;

        Vector3 finalPosition = currentPosition + Vector3.up * delta;
        lastPreviewPosition = finalPosition;
        terrainDecorationPreviewBasePosition = finalPosition - Vector3.up * terrainDecorationPreviewHeightOffset;
        hasValidPreviewPosition = true;
        canPlaceAtPreviewPosition = CanPlaceAt(finalPosition);

        if (previewInstance != null)
        {
            previewInstance.transform.position = finalPosition;
            previewInstance.transform.rotation = Quaternion.Euler(GetRuleEuler(selectedDefinition, currentPreviewResult));
            ApplyPreviewMaterial(canPlaceAtPreviewPosition);
        }

        e.Use();
        sceneView.Repaint();
        Repaint();
        return true;
    }

    private void DrawSceneOverlay()
    {
        Handles.BeginGUI();
        Rect rect = new Rect(12f, 12f, 340f, 82f);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.58f));
        GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f), "地图对象放置模式 / 地形装饰物", EditorStyles.boldLabel);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 18f), selectedDefinition != null ? GetDisplayName(selectedDefinition) : "未选择", EditorStyles.label);
        string state = hasValidPreviewPosition
            ? (canPlaceAtPreviewPosition ? "可放置：绿色" : "不可放置：红色 / 与已有碰撞重叠")
            : "未命中放置平面";
        string heightText = Mathf.Abs(terrainDecorationPreviewHeightOffset) > 0.0001f
            ? $" / 高度偏移 {terrainDecorationPreviewHeightOffset:+0.00;-0.00;0.00}"
            : "";
        GUI.Label(new Rect(rect.x + 10f, rect.y + 50f, rect.width - 20f, 18f), state + heightText + " / Ctrl+滚轮旋转 / Shift+滚轮升降 / 右键或 Esc 取消", EditorStyles.miniLabel);
        Handles.EndGUI();
    }

    private void UpdatePreviewPosition(Vector2 mousePosition)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        Vector3 hitPosition;
        if ((raycastSceneSurface && TryRaycastScene(ray, out hitPosition)) || TryRaycastPlacementPlane(ray, out hitPosition))
        {
            if (snapToGrid)
                hitPosition = Snap(hitPosition, gridSize);

            if (requireGroundShapeForPlacement && snapPlacementToGroundBlockHeight && TryGetGroundPlacementY(hitPosition, out float groundY))
                hitPosition.y = groundY;
            else
                hitPosition.y = placementY;

            terrainDecorationPreviewBasePosition = hitPosition;
            Vector3 finalPosition = terrainDecorationPreviewBasePosition + Vector3.up * terrainDecorationPreviewHeightOffset;

            lastPreviewPosition = finalPosition;
            hasValidPreviewPosition = true;
            canPlaceAtPreviewPosition = CanPlaceAt(finalPosition);

            if (previewInstance == null)
                RebuildPreview();

            if (previewInstance != null)
            {
                previewInstance.transform.position = finalPosition;
                previewInstance.transform.rotation = Quaternion.Euler(GetRuleEuler(selectedDefinition, currentPreviewResult));
                ApplyPreviewMaterial(canPlaceAtPreviewPosition);
            }
        }
        else
        {
            hasValidPreviewPosition = false;
            canPlaceAtPreviewPosition = false;
            ApplyPreviewMaterial(false);
        }
    }

    private bool TryRaycastScene(Ray ray, out Vector3 hitPosition)
    {
        // 不能直接 Physics.Raycast 取最近命中。放置预览物本身也有 Renderer/Collider，
        // 高度调整后射线很容易先打到 previewInstance，导致“向上/向下滚轮都在往下跑”。
        // 这里显式跳过当前预览实例及其子物体，并忽略 Trigger，只把真实场景表面作为放置基准。
        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            hitPosition = Vector3.zero;
            return false;
        }

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            Transform hitTransform = hit.collider.transform;
            if (previewInstance != null && hitTransform != null && hitTransform.IsChildOf(previewInstance.transform))
                continue;

            hitPosition = hit.point;
            return true;
        }

        hitPosition = Vector3.zero;
        return false;
    }

    private bool TryRaycastPlacementPlane(Ray ray, out Vector3 hitPosition)
    {
        Plane plane = new Plane(Vector3.up, new Vector3(0f, placementY, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            hitPosition = ray.GetPoint(enter);
            return true;
        }
        hitPosition = Vector3.zero;
        return false;
    }

    private Vector3 Snap(Vector3 position, float size)
    {
        if (size <= 0.0001f)
            return position;
        return new Vector3(Mathf.Round(position.x / size) * size, position.y, Mathf.Round(position.z / size) * size);
    }

    private void PlaceSelectedDefinition(Vector3 position)
    {
        TerrainDecorationPlacementResult result = currentPreviewResult != null ? currentPreviewResult : BuildPlacementResult(selectedDefinition);
        if (result.variant == null || result.variant.prefab == null)
        {
            Debug.LogWarning("[TerrainDecorationPlacement] 当前定义没有可放置的视觉 PF 变体。", selectedDefinition);
            return;
        }

        Transform parent = GetOrCreateParent(parentPath);

        Vector3 placementEuler = GetRuleEuler(selectedDefinition, result);
        Vector3 visualEuler = GetVisualLocalEuler(selectedDefinition, result);

        GameObject root = new GameObject(BuildPlacedObjectName(selectedDefinition, result.variant));
        Undo.RegisterCreatedObjectUndo(root, "Place terrain decoration");

        root.transform.SetParent(parent, false);
        root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(placementEuler);
        root.transform.localScale = Vector3.one;
        SetLayerRecursively(root, LayerMask.NameToLayer("World3D"));

        TerrainDecorationRuntimeBinder binder = root.GetComponent<TerrainDecorationRuntimeBinder>();
        if (binder == null)
            binder = root.AddComponent<TerrainDecorationRuntimeBinder>();

        binder.definition = selectedDefinition;
        binder.instanceId = GenerateInstanceId(selectedDefinition);
        binder.selectedVariantId = result.variant.variantId;
        binder.randomSeed = result.seed;
        binder.placementEuler = placementEuler;
        binder.visualLocalEuler = visualEuler;
        binder.finalScale = result.finalScale;
        TryAssignMaterialOverridesToBinder(binder, result.materialChoices);

        Transform visualRoot = EnsureChild(root.transform, "VisualRoot");
        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.Euler(visualEuler);
        visualRoot.localScale = result.finalScale;
        SetLayerRecursively(visualRoot.gameObject, LayerMask.NameToLayer("World3D"));

        // 运行时模板只提供标准容器和手动代理结构；具体视觉仍然按本次抽到的 Variant PF 重建。
        // 这不会断开 root 对 PF_TD_* 的 Prefab 连接，只会形成当前实例的 VisualRoot override。
        while (visualRoot.childCount > 0)
            Undo.DestroyObjectImmediate(visualRoot.GetChild(0).gameObject);

        GameObject visual = InstantiatePrefab(result.variant.prefab, visualRoot);
        if (visual != null)
        {
            visual.name = result.variant.prefab.name;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            StripTerrainDecorationComponentsFromVisual(visual);
            SetLayerRecursively(visual, LayerMask.NameToLayer("World3D"));
            ApplyMaterialChoicesDirect(visualRoot, visual.transform, result.materialChoices);
        }

        TerrainDecorationRuntimeApplier applier = root.GetComponent<TerrainDecorationRuntimeApplier>();
        if (applier == null)
            applier = root.AddComponent<TerrainDecorationRuntimeApplier>();

        DisableApplierAutoApply(applier);
        applier.ApplyDefinition();
        SkyPrisonTerrainDecorationInstanceBuilder.BuildStructureFromDefinition(root, selectedDefinition, true);
        DisableApplierAutoApply(applier);

        // 贴花本身的sortingOrder统一压到安全值，不用definition/源PF自带的旧数值——
        // 见SafeGroundDecalSortingOrder注释，避免地面贴花在角色深度值较大的区域
        // 反而盖住角色投影。只改visualRoot下实际可见的渲染器，不影响判定/遮挡这些
        // 用途不同的辅助节点。
        ApplySafeGroundDecalSortingOrder(visualRoot);

        // V3: 视野遮挡节点必须在“摆放确认后”按当前 Scene 实例真实模型贴合。
        // 这里不回写 Prefab，也不改 BackTrigger / FrontTrigger / MeshCollider，只补 VisionBlockerRoot。
        SyncVisionBlockerToPlacedVisualBounds(root, selectedDefinition);

        Debug.Log("[TD_PLACE_ACTIVE][MAP_WINDOW][BUILDER] 直接生成 Scene 实例，并由 Builder 按定义生成碰撞与遮挡结构。", root);

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        RefreshPlacedCache();

        if (playSoundOnPlace)
            PlayEditorSound(PlaceSoundPath);
    }



    private void SyncVisionBlockerToPlacedVisualBounds(GameObject root, TerrainDecorationDefinition definition)
    {
        if (root == null || definition == null)
            return;

        Transform ruleRoot = root.transform.Find("RuleRoot");
        if (ruleRoot == null)
        {
            if (!definition.blockVision)
                return;
            ruleRoot = EnsureChild(root.transform, "RuleRoot");
        }

        Transform visionBlockerRoot = ruleRoot.Find("VisionBlockerRoot");
        if (visionBlockerRoot == null)
        {
            if (!definition.blockVision)
                return;
            visionBlockerRoot = EnsureChild(ruleRoot, "VisionBlockerRoot");
        }

        Transform blockerBox = visionBlockerRoot.Find("Vision_Blocker_Box");
        if (blockerBox == null)
        {
            if (!definition.blockVision)
                return;
            blockerBox = EnsureChild(visionBlockerRoot, "Vision_Blocker_Box");
        }

        visionBlockerRoot.localPosition = Vector3.zero;
        visionBlockerRoot.localRotation = Quaternion.identity;
        visionBlockerRoot.localScale = Vector3.one;

        blockerBox.localPosition = Vector3.zero;
        blockerBox.localRotation = Quaternion.identity;
        blockerBox.localScale = Vector3.one;

        blockerBox.gameObject.SetActive(definition.blockVision);
        if (!definition.blockVision)
        {
            BoxCollider existingCollider = blockerBox.GetComponent<BoxCollider>();
            if (existingCollider != null)
                existingCollider.enabled = false;
            return;
        }

        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer >= 0)
        {
            visionBlockerRoot.gameObject.layer = defaultLayer;
            blockerBox.gameObject.layer = defaultLayer;
        }

        BoxCollider collider = blockerBox.GetComponent<BoxCollider>();
        if (collider == null)
            collider = blockerBox.gameObject.AddComponent<BoxCollider>();

        collider.isTrigger = true;
        collider.enabled = true;

        Bounds localBounds;
        if (!TryCalculateVisualRendererBoundsInLocal(root.transform, blockerBox, out localBounds))
        {
            // 极端情况：没有 Renderer 时仍然给一个很小的 Trigger，避免空 Collider 报错。
            localBounds = new Bounds(Vector3.zero, Vector3.one * 0.25f);
        }

        collider.center = localBounds.center;
        collider.size = SanitizeVisionBlockerSize(localBounds.size);

        Debug.Log($"[TD_PLACE_ACTIVE][VISION_BLOCKER] 已按当前摆放实例贴合 Vision_Blocker_Box。size={collider.size} center={collider.center}", root);
    }

    private static bool TryCalculateVisualRendererBoundsInLocal(Transform root, Transform targetLocalSpace, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        if (root == null || targetLocalSpace == null)
            return false;

        Transform visualRoot = root.Find("VisualRoot");
        if (visualRoot == null)
            return false;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            Bounds rendererLocalBounds;
            Matrix4x4 rendererLocalToBlockerLocal;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                rendererLocalBounds = meshFilter.sharedMesh.bounds;
                rendererLocalToBlockerLocal = targetLocalSpace.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            }
            else
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    rendererLocalBounds = skinned.localBounds;
                    rendererLocalToBlockerLocal = targetLocalSpace.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                }
                else
                {
                    // 贴花 / 特殊 Renderer 没有可靠 Mesh 本地 Bounds 时，退回世界 AABB。
                    rendererLocalBounds = renderer.bounds;
                    rendererLocalToBlockerLocal = targetLocalSpace.worldToLocalMatrix;
                }
            }

            EncapsulateTransformedBounds(rendererLocalBounds, rendererLocalToBlockerLocal, ref bounds, ref hasAny);
        }

        return hasAny;
    }

    private static void EncapsulateTransformedBounds(Bounds sourceBounds, Matrix4x4 localToTarget, ref Bounds targetBounds, ref bool hasAny)
    {
        Vector3 min = sourceBounds.min;
        Vector3 max = sourceBounds.max;

        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z)), ref targetBounds, ref hasAny);
        EncapsulatePoint(localToTarget.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z)), ref targetBounds, ref hasAny);
    }

    private static void EncapsulatePoint(Vector3 point, ref Bounds bounds, ref bool hasAny)
    {
        if (!hasAny)
        {
            bounds = new Bounds(point, Vector3.zero);
            hasAny = true;
            return;
        }

        bounds.Encapsulate(point);
    }

    private static Vector3 SanitizeVisionBlockerSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(0.05f, Mathf.Abs(size.x)),
            Mathf.Max(0.05f, Mathf.Abs(size.y)),
            Mathf.Max(0.05f, Mathf.Abs(size.z)));
    }


    private static void ScheduleDelayedButtonCorrection(GameObject root)
    {
        // Deprecated: 新地形装饰物主线禁止 delayCall 二次矫正。
        // 保留空方法只为避免旧调用点编译断裂；正式放置流程不应再调用。
    }

    // 放置工具已经在编辑阶段手动 ApplyDefinition 并校正 BackTrigger。
    // 这里把自动 Apply 关掉，避免进入 Play / OnEnable 时 RuntimeApplier 再次重建，
    // 把已经校正到背后的 BackTrigger 覆盖回前方。
    private static void DisableApplierAutoApply(TerrainDecorationRuntimeApplier applier)
    {
        if (applier == null)
            return;

        SerializedObject so = new SerializedObject(applier);
        SerializedProperty applyOnEnableProp = so.FindProperty("applyOnEnable");
        SerializedProperty applyInEditModeProp = so.FindProperty("applyInEditMode");

        if (applyOnEnableProp != null)
            applyOnEnableProp.boolValue = false;

        if (applyInEditModeProp != null)
            applyInEditModeProp.boolValue = false;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(applier);
    }

    private const float BackTriggerDepthMultiplier = 1.45f;
    private const float BackTriggerMinExtraWorldDepth = 0.35f;

    // 只修正 BackTrigger 的位置与判定深度：不改角度、不改 Applier / Binder / Definition。
    // 目标：BackTrigger 的前缘从物体规则中心附近开始，向模型背后方向延伸；
    // 变厚时保持前缘不往前顶，只把后缘往模型背后加深。
    private void CorrectBackTriggerToStartNearRuleCenter(GameObject root)
    {
        if (root == null)
            return;

        Transform backTrigger = root.transform.Find("RuleRoot/BackTrigger");
        if (backTrigger == null)
            return;

        BoxCollider backCollider = backTrigger.GetComponent<BoxCollider>();
        if (backCollider == null)
            return;

        // 规则中心用实例根节点位置。这里比 Renderer Bounds 更稳定，避免模型网格自身偏心导致判定盒漂移。
        Vector3 ruleCenter = root.transform.position;

        // ApplyDefinition 当前生成出来的位置能告诉我们“前后轴”在哪边。
        // 旧错误是它在前方；这里取反，得到背后方向。
        Vector3 currentOffset = backTrigger.position - ruleCenter;
        currentOffset.y = 0f;

        Vector3 rearDirection;
        if (currentOffset.sqrMagnitude > 0.000001f)
        {
            rearDirection = -currentOffset.normalized;
        }
        else
        {
            rearDirection = backTrigger.forward;
            rearDirection.y = 0f;
            if (rearDirection.sqrMagnitude < 0.000001f)
                rearDirection = root.transform.forward;
            rearDirection.y = 0f;
            if (rearDirection.sqrMagnitude < 0.000001f)
                rearDirection = Vector3.forward;
            rearDirection.Normalize();
        }

        Undo.RecordObject(backCollider, "Thicken BackTrigger Collider");
        ExpandBackTriggerColliderDepth(backCollider, rearDirection);

        float halfExtentAlongRear = GetBoxWorldHalfExtentAlongDirection(backCollider, rearDirection);
        if (halfExtentAlongRear <= 0.0001f)
            halfExtentAlongRear = Mathf.Max(0.1f, backCollider.size.z * Mathf.Abs(backTrigger.lossyScale.z) * 0.5f);

        // 让判定盒“前缘”贴近规则中心：中心点只偏移半个盒深。
        // 这样盒子会从模型坐标中心附近开始，向后方展开，而不是离模型很远。
        Vector3 fixedCenter = ruleCenter + rearDirection * halfExtentAlongRear;
        fixedCenter.y = backTrigger.position.y;

        Undo.RecordObject(backTrigger, "Fix BackTrigger Position");
        backTrigger.position = fixedCenter;
        EditorUtility.SetDirty(backCollider);
        EditorUtility.SetDirty(backTrigger.gameObject);
        Physics.SyncTransforms();
    }

    private static void ExpandBackTriggerColliderDepth(BoxCollider box, Vector3 rearDirection)
    {
        if (box == null)
            return;

        if (rearDirection.sqrMagnitude < 0.000001f)
            rearDirection = box.transform.forward;

        rearDirection.Normalize();

        Transform t = box.transform;
        Vector3 lossy = t.lossyScale;
        float dotX = Mathf.Abs(Vector3.Dot(rearDirection, t.right));
        float dotY = Mathf.Abs(Vector3.Dot(rearDirection, t.up));
        float dotZ = Mathf.Abs(Vector3.Dot(rearDirection, t.forward));

        Vector3 size = box.size;
        int axis = 2;
        float axisScale = Mathf.Abs(lossy.z);
        float currentLocalDepth = Mathf.Abs(size.z);

        if (dotX >= dotY && dotX >= dotZ)
        {
            axis = 0;
            axisScale = Mathf.Abs(lossy.x);
            currentLocalDepth = Mathf.Abs(size.x);
        }
        else if (dotY >= dotX && dotY >= dotZ)
        {
            axis = 1;
            axisScale = Mathf.Abs(lossy.y);
            currentLocalDepth = Mathf.Abs(size.y);
        }

        axisScale = Mathf.Max(0.0001f, axisScale);
        float minExtraLocalDepth = BackTriggerMinExtraWorldDepth / axisScale;
        float targetLocalDepth = Mathf.Max(currentLocalDepth * BackTriggerDepthMultiplier, currentLocalDepth + minExtraLocalDepth);

        if (axis == 0)
            size.x = Mathf.Max(0.01f, targetLocalDepth);
        else if (axis == 1)
            size.y = Mathf.Max(0.01f, targetLocalDepth);
        else
            size.z = Mathf.Max(0.01f, targetLocalDepth);

        box.size = size;
    }

    private static float GetBoxWorldHalfExtentAlongDirection(BoxCollider box, Vector3 worldDirection)
    {
        if (box == null)
            return 0f;

        if (worldDirection.sqrMagnitude < 0.000001f)
            return 0f;

        worldDirection.Normalize();

        Transform t = box.transform;
        Vector3 lossy = t.lossyScale;
        Vector3 half = new Vector3(
            Mathf.Abs(box.size.x * lossy.x) * 0.5f,
            Mathf.Abs(box.size.y * lossy.y) * 0.5f,
            Mathf.Abs(box.size.z * lossy.z) * 0.5f);

        float x = Mathf.Abs(Vector3.Dot(worldDirection, t.right)) * half.x;
        float y = Mathf.Abs(Vector3.Dot(worldDirection, t.up)) * half.y;
        float z = Mathf.Abs(Vector3.Dot(worldDirection, t.forward)) * half.z;
        return x + y + z;
    }

    private void RebuildPreview()
    {
        DestroyPreview();
        if (selectedDefinition == null)
            return;

        currentPreviewResult = BuildPlacementResult(selectedDefinition);
        if (currentPreviewResult.variant == null || currentPreviewResult.variant.prefab == null)
            return;

        previewInstance = new GameObject("__TerrainDecorationPreview__");
        previewInstance.hideFlags = HideFlags.HideAndDontSave;
        previewInstance.transform.position = lastPreviewPosition;
        previewInstance.transform.rotation = Quaternion.Euler(GetRuleEuler(selectedDefinition, currentPreviewResult));
        previewInstance.transform.localScale = Vector3.one;

        Transform visualRoot = new GameObject("VisualRoot").transform;
        visualRoot.SetParent(previewInstance.transform, false);
        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.Euler(GetVisualLocalEuler(selectedDefinition, currentPreviewResult));
        visualRoot.localScale = currentPreviewResult.finalScale;

        GameObject visual = InstantiatePrefab(currentPreviewResult.variant.prefab, visualRoot);
        if (visual == null)
            return;

        visual.name = currentPreviewResult.variant.prefab.name;
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        StripTerrainDecorationComponentsFromVisual(visual);
        ApplyMaterialChoicesDirect(visualRoot, visual.transform, currentPreviewResult.materialChoices);
        PreparePreviewObject(previewInstance);
        hasAppliedPreviewMaterial = false;
        ApplyPreviewMaterial(canPlaceAtPreviewPosition);
    }

    private void PreparePreviewObject(GameObject go)
    {
        previewRenderers.Clear();

        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            c.enabled = false;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            previewRenderers.Add(r);
        }
    }

    private void ApplyPreviewMaterial(bool valid)
    {
        if (previewInstance == null)
            return;

        if (hasAppliedPreviewMaterial && lastAppliedPreviewValid == valid && previewRenderers.Count > 0)
            return;

        Material mat = GetPreviewMaterial(valid);
        for (int i = previewRenderers.Count - 1; i >= 0; i--)
        {
            Renderer r = previewRenderers[i];
            if (r == null)
            {
                previewRenderers.RemoveAt(i);
                continue;
            }

            Material[] mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int j = 0; j < mats.Length; j++)
                mats[j] = mat;
            r.sharedMaterials = mats;
        }

        lastAppliedPreviewValid = valid;
        hasAppliedPreviewMaterial = true;
    }

    private Material GetPreviewMaterial(bool valid)
    {
        Material existing = valid ? validPreviewMaterial : invalidPreviewMaterial;
        if (existing != null)
            return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        mat.name = valid ? "__TerrainDecorationPreviewMaterial_Valid__" : "__TerrainDecorationPreviewMaterial_Invalid__";
        mat.hideFlags = HideFlags.HideAndDontSave;
        Color color = valid ? ValidPreviewColor : InvalidPreviewColor;
        mat.color = color;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (valid) validPreviewMaterial = mat; else invalidPreviewMaterial = mat;
        return mat;
    }

    private void DestroyPreview()
    {
        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }
        previewRenderers.Clear();
        currentPreviewResult = null;
        lastAppliedPreviewValid = false;
        hasAppliedPreviewMaterial = false;
    }

    private void DestroyPreviewMaterial()
    {
        if (validPreviewMaterial != null)
        {
            DestroyImmediate(validPreviewMaterial);
            validPreviewMaterial = null;
        }
        if (invalidPreviewMaterial != null)
        {
            DestroyImmediate(invalidPreviewMaterial);
            invalidPreviewMaterial = null;
        }
    }

    private Vector3 GetRuleEuler(TerrainDecorationDefinition definition, TerrainDecorationPlacementResult result)
    {
        if (definition == null)
            return Vector3.zero;
        Vector3 euler = definition.defaultPlacementRotation;
        euler.y += terrainDecorationPreviewRotationY;
        if (definition.visualRandomRotationAffectsRules && result != null)
            euler += result.visualLocalEuler;
        return euler;
    }

    private Vector3 GetVisualLocalEuler(TerrainDecorationDefinition definition, TerrainDecorationPlacementResult result)
    {
        if (definition == null || result == null || definition.visualRandomRotationAffectsRules)
            return Vector3.zero;
        return result.visualLocalEuler;
    }

    private bool CanPlaceOnGroundShape(Vector3 position)
    {
        if (!requireGroundShapeForPlacement)
            return true;

        BaseGroundBlock block = activeGroundBlock != null ? activeGroundBlock : FindActiveGroundBlock();
        if (block == null)
            return true; // 没有 GroundBlock 的旧场景先不阻断放置。

        return block.HasGroundAtWorld(position);
    }

    private bool TryGetGroundPlacementY(Vector3 position, out float y)
    {
        y = placementY;
        BaseGroundBlock block = activeGroundBlock != null ? activeGroundBlock : FindActiveGroundBlock();
        if (block == null)
            return false;

        if (!block.HasGroundAtWorld(position))
            return false;

        y = block.mapBoundsCenter.y + block.GetGroundYAtWorld(position);
        return true;
    }

    private bool CanPlaceAt(Vector3 position)
    {
        if (selectedDefinition == null)
            return false;

        if (!CanPlaceOnGroundShape(position))
            return false;

        if (selectedDefinition.allowCollisionOverlap || selectedDefinition.placementCollisionMode == TerrainDecorationPlacementCollisionMode.None)
            return true;

        Vector3 size;
        Vector3 offset;
        GetPlacementCollisionBox(selectedDefinition, out size, out offset);

        Vector3 scale = currentPreviewResult != null ? currentPreviewResult.finalScale : selectedDefinition.defaultScale;
        Quaternion rotation = Quaternion.Euler(GetRuleEuler(selectedDefinition, currentPreviewResult));
        Vector3 scaledSize = new Vector3(
            Mathf.Max(0.05f, Mathf.Abs(size.x * scale.x)),
            Mathf.Max(0.05f, Mathf.Abs(size.y * scale.y)),
            Mathf.Max(0.05f, Mathf.Abs(size.z * scale.z)));
        Vector3 scaledOffset = Vector3.Scale(offset, scale);
        Vector3 center = position + rotation * scaledOffset;
        Vector3 halfExtents = scaledSize * 0.5f;

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, placementOverlapBuffer, rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = placementOverlapBuffer[i];
            if (c == null)
                continue;
            if (previewInstance != null && c.transform.IsChildOf(previewInstance.transform))
                continue;

            if (ShouldIgnorePlacementOverlapCollider(c))
                continue;

            TerrainDecorationRuntimeBinder binder = c.GetComponentInParent<TerrainDecorationRuntimeBinder>();
            if (overlapCheckOnlyTerrainDecorations)
            {
                if (binder != null)
                    return false;
                continue;
            }

            return false;
        }

        return true;
    }

    private bool ShouldIgnorePlacementOverlapCollider(Collider c)
    {
        if (c == null)
            return true;

        if (previewInstance != null && c.transform.IsChildOf(previewInstance.transform))
            return true;

        if (c.isTrigger)
            return true;

        string layerName = LayerMask.LayerToName(c.gameObject.layer);
        if (layerName == "FogOfWar" || layerName == "OverheadUI" || layerName == "Ignore Raycast" || layerName == "ForegroundOccluder" || layerName == "OcclusionMask")
            return true;

        Transform t = c.transform;
        while (t != null)
        {
            string n = t.name;
            if (n.Contains("Preview") || n.Contains("EditorGizmo") || n.Contains("StencilWriter") || n.Contains("OutlineMaskProxy") || n.Contains("FrontOccluderProxy") || n.Contains("BackTrigger") || n.Contains("FrontTrigger") || n.Contains("VisionBlocker"))
                return true;
            t = t.parent;
        }

        return false;
    }

    private void GetPlacementCollisionBox(TerrainDecorationDefinition definition, out Vector3 size, out Vector3 offset)
    {
        size = Vector3.one;
        offset = new Vector3(0f, 0.5f, 0f);
        if (definition == null)
            return;

        if (definition.collisionSize.sqrMagnitude > 0.0001f)
        {
            size = definition.collisionSize;
            offset = definition.collisionOffset;
            return;
        }

        if (definition.footprintSize.x > 0.0001f || definition.footprintSize.y > 0.0001f)
        {
            size = new Vector3(Mathf.Max(0.05f, definition.footprintSize.x), 1f, Mathf.Max(0.05f, definition.footprintSize.y));
            offset = new Vector3(0f, 0.5f, 0f);
        }
    }

    private GameObject InstantiatePrefab(GameObject prefab, Transform parent)
    {
        if (prefab == null)
            return null;
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (instance == null)
            instance = Instantiate(prefab, parent);
        return instance;
    }

    private TerrainDecorationPlacementResult BuildPlacementResult(TerrainDecorationDefinition definition)
    {
        TerrainDecorationPlacementResult result = new TerrainDecorationPlacementResult();
        if (definition == null)
            return result;

        result.seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        System.Random random = new System.Random(result.seed);
        result.variant = definition.randomVariantOnPlace ? PickVariantByRandom(definition, random) : definition.GetFirstVariant();
        if (result.variant == null)
            return result;
        result.finalScale = BuildRandomScale(definition, random);
        result.visualLocalEuler = BuildRandomVisualEuler(definition, random);
        result.materialChoices = BuildMaterialChoices(definition, result.variant, random);
        return result;
    }

    private TerrainDecorationVariant PickVariantByRandom(TerrainDecorationDefinition definition, System.Random random)
    {
        List<TerrainDecorationVariant> valid = definition.variants.Where(v => v != null && v.prefab != null).ToList();
        if (valid.Count == 0)
            return null;
        if (!definition.randomVariantByWeight)
            return valid[random.Next(0, valid.Count)];
        int totalWeight = valid.Sum(v => Mathf.Max(1, v.weight));
        int value = random.Next(0, totalWeight);
        int acc = 0;
        foreach (var v in valid)
        {
            acc += Mathf.Max(1, v.weight);
            if (value < acc)
                return v;
        }
        return valid[valid.Count - 1];
    }

    private Vector3 BuildRandomScale(TerrainDecorationDefinition definition, System.Random random)
    {
        if (!definition.enableRandomScale)
            return definition.defaultScale;
        if (definition.uniformRandomScale)
        {
            float min = Mathf.Min(definition.randomScaleMin.x, definition.randomScaleMax.x);
            float max = Mathf.Max(definition.randomScaleMin.x, definition.randomScaleMax.x);
            float value = Lerp(min, max, random.NextDouble());
            return definition.defaultScale * value;
        }
        return new Vector3(
            Lerp(definition.randomScaleMin.x, definition.randomScaleMax.x, random.NextDouble()),
            Lerp(definition.randomScaleMin.y, definition.randomScaleMax.y, random.NextDouble()),
            Lerp(definition.randomScaleMin.z, definition.randomScaleMax.z, random.NextDouble()));
    }

    private Vector3 BuildRandomVisualEuler(TerrainDecorationDefinition definition, System.Random random)
    {
        if (!definition.enableVisualRandomRotation)
            return Vector3.zero;
        return new Vector3(
            Lerp(definition.visualRandomRotationMin.x, definition.visualRandomRotationMax.x, random.NextDouble()),
            Lerp(definition.visualRandomRotationMin.y, definition.visualRandomRotationMax.y, random.NextDouble()),
            Lerp(definition.visualRandomRotationMin.z, definition.visualRandomRotationMax.z, random.NextDouble()));
    }

    private float Lerp(float a, float b, double t) => Mathf.Lerp(a, b, (float)t);

    private List<MaterialChoice> BuildMaterialChoices(TerrainDecorationDefinition definition, TerrainDecorationVariant variant, System.Random random)
    {
        List<MaterialChoice> result = new List<MaterialChoice>();
        if (!definition.randomMaterialOnPlace || variant == null || variant.materialSlots == null)
            return result;

        foreach (TerrainDecorationMaterialSlot slot in variant.materialSlots)
        {
            if (slot == null || slot.allowedMaterials == null)
                continue;
            List<Material> valid = slot.allowedMaterials.Where(x => x != null).ToList();
            if (valid.Count == 0)
                continue;
            result.Add(new MaterialChoice
            {
                slotId = slot.slotId,
                rendererPath = slot.rendererPath,
                materialIndex = slot.materialIndex,
                material = valid[random.Next(0, valid.Count)]
            });
        }
        return result;
    }

    private void ApplyMaterialChoicesDirect(Transform root, Transform visualInstance, List<MaterialChoice> choices)
    {
        if (choices == null || choices.Count == 0)
            return;

        foreach (MaterialChoice choice in choices)
        {
            if (choice == null || choice.material == null)
                continue;

            Renderer renderer = FindRendererForMaterialChoice(root, visualInstance, choice);
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            if (choice.materialIndex < 0 || choice.materialIndex >= mats.Length)
                continue;
            mats[choice.materialIndex] = choice.material;
            renderer.sharedMaterials = mats;
        }
    }

    private Renderer FindRendererForMaterialChoice(Transform root, Transform visualInstance, MaterialChoice choice)
    {
        Transform target = null;
        string path = choice.rendererPath ?? "";
        if (!string.IsNullOrWhiteSpace(path))
        {
            target = root.Find(path);
            if (target == null && path.StartsWith("VisualRoot/", StringComparison.Ordinal))
                target = visualInstance.Find(path.Substring("VisualRoot/".Length));
            if (target == null)
                target = visualInstance.Find(path);
        }
        if (target == null)
            target = visualInstance;
        return target.GetComponent<Renderer>() ?? target.GetComponentInChildren<Renderer>(true);
    }

    private void TryAssignMaterialOverridesToBinder(TerrainDecorationRuntimeBinder binder, List<MaterialChoice> choices)
    {
        if (binder == null || choices == null || choices.Count == 0)
            return;

        FieldInfo field = binder.GetType().GetField("materialOverrides", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            return;

        object listObject = field.GetValue(binder);
        IList list = listObject as IList;
        if (list == null)
            return;

        list.Clear();
        Type elementType = null;
        if (field.FieldType.IsGenericType)
            elementType = field.FieldType.GetGenericArguments()[0];
        if (elementType == null)
            return;

        foreach (MaterialChoice choice in choices)
        {
            object item = Activator.CreateInstance(elementType);
            SetFieldOrProperty(item, "slotId", choice.slotId);
            SetFieldOrProperty(item, "material", choice.material);
            list.Add(item);
        }
    }

    private void SetFieldOrProperty(object target, string name, object value)
    {
        if (target == null)
            return;
        Type type = target.GetType();
        FieldInfo f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            f.SetValue(target, value);
            return;
        }
        PropertyInfo p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite)
            p.SetValue(target, value, null);
    }

    private Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create terrain decoration node");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private void StripTerrainDecorationComponentsFromVisual(GameObject visual)
    {
        if (visual == null)
            return;
        foreach (TerrainDecorationRuntimeApplier c in visual.GetComponentsInChildren<TerrainDecorationRuntimeApplier>(true))
            DestroyImmediate(c);
        foreach (TerrainDecorationRuntimeBinder c in visual.GetComponentsInChildren<TerrainDecorationRuntimeBinder>(true))
            DestroyImmediate(c);
        foreach (MonoBehaviour mb in visual.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null)
                continue;
            string n = mb.GetType().Name;
            if (n == "TerrainDecorationEnvironmentAudioEmitter")
                DestroyImmediate(mb);
        }
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null || layer < 0)
            return;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    // 见SafeGroundDecalSortingOrder注释：贴花放置到地图上的这一刻统一压到安全排序值，
    // 不依赖definition/源PF里原本写的sortingOrder（那个数值是按老的、range很小的
    // 假设定的，跟角色阴影现在实际的动态排序范围对不上）。
    private void ApplySafeGroundDecalSortingOrder(Transform visualRoot)
    {
        if (visualRoot == null)
            return;

        foreach (Renderer r in visualRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            r.sortingOrder = SafeGroundDecalSortingOrder;
        }
    }

    private Transform GetOrCreateParent(string path)
    {
        Transform parent = FindTransformByPath(path);
        if (parent != null)
            return parent;

        if (string.IsNullOrWhiteSpace(path))
        {
            GameObject fallback = GameObject.Find("TerrainDecorationPlacedRoot") ?? new GameObject("TerrainDecorationPlacedRoot");
            Undo.RegisterCreatedObjectUndo(fallback, "Create terrain decoration parent");
            return fallback.transform;
        }

        string[] parts = path.Split('/');
        Transform current = null;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (i == 0)
            {
                GameObject root = GameObject.Find(part);
                if (root == null)
                {
                    root = new GameObject(part);
                    Undo.RegisterCreatedObjectUndo(root, "Create terrain decoration parent");
                }
                current = root.transform;
            }
            else
            {
                Transform child = current.Find(part);
                if (child == null)
                {
                    GameObject go = new GameObject(part);
                    Undo.RegisterCreatedObjectUndo(go, "Create terrain decoration parent");
                    go.transform.SetParent(current, false);
                    child = go.transform;
                }
                current = child;
            }
        }
        return current;
    }

    private Transform FindTransformByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string[] parts = path.Split('/');
        GameObject root = GameObject.Find(parts[0]);
        if (root == null)
            return null;
        Transform current = root.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            current = current.Find(parts[i]);
            if (current == null)
                return null;
        }
        return current;
    }

    private string BuildPlacedObjectName(TerrainDecorationDefinition definition, TerrainDecorationVariant variant)
    {
        string baseName = definition != null && !string.IsNullOrWhiteSpace(definition.displayName) ? definition.displayName : "地形装饰物";
        string variantName = variant != null && !string.IsNullOrWhiteSpace(variant.displayName) ? variant.displayName : (variant != null ? variant.variantId : "默认");
        string raw = $"{baseName}_{variantName}_{UnityEngine.Random.Range(100000, 999999)}";
        return MakeUniqueSceneObjectName(SanitizeName(raw));
    }

    private string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "TerrainDecoration";
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private string MakeUniqueSceneObjectName(string baseName)
    {
        if (GameObject.Find(baseName) == null)
            return baseName;
        int index = 1;
        while (GameObject.Find($"{baseName}_{index:00}") != null)
            index++;
        return $"{baseName}_{index:00}";
    }

    private string GenerateInstanceId(TerrainDecorationDefinition definition)
    {
        string key = definition != null && !string.IsNullOrWhiteSpace(definition.decorationId) ? definition.decorationId : "terrain_decoration";
        return key + "_" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
    }


    private void MarkGroundVisualDataDirty(BaseGroundBlock block, bool forceBake)
    {
        if (block == null)
            return;

        // 地面刷/参数变化只标记“运行贴图过期”。
        // 不在设置页/普通参数变化时整图重建，避免 ColorPicker、数字输入、滑条拖动时出现读条。
        // 真正的运行烘焙统一由“进入 Play Mode 前自动烘焙”或手动“烘焙运行地面贴图”负责。
        block.MarkGroundDataDirty(false);

        EditorUtility.SetDirty(block);
    }

    private void MarkGroundBrushVisualDirty(BaseGroundBlock block)
    {
        MarkGroundBrushVisualDirty(block, null);
    }

    private void MarkGroundBrushVisualDirty(BaseGroundBlock block, Bounds? livePreviewBounds)
    {
        if (block == null)
            return;

        // 地面绘制只标记数据过期，不触发正式运行烘焙。
        block.MarkGroundDataDirty(false);
        groundBrushVisualBakeDirty = true;
        groundBrushVisualBakeDirtyBlock = block;
        EditorUtility.SetDirty(block);

        if (livePreviewBounds.HasValue)
            AccumulateGroundBrushPreviewBounds(livePreviewBounds.Value);

        if (groundBrushLivePreviewEnabled && livePreviewBounds.HasValue)
        {
            TryPreviewGroundBrushVisual(block, false);
        }
        else if (!block.useUrpLitShadowSafeOutput)
        {
            // 非 URP/Lit 双通道时才允许走旧显示刷新。
            ApplyGroundVisualDisplayMode(block);
            SceneView.RepaintAll();
        }
    }

    private void AccumulateGroundBrushPreviewBounds(Bounds bounds)
    {
        if (!groundBrushHasPendingPreviewBounds)
        {
            groundBrushPendingPreviewBounds = bounds;
            groundBrushHasPendingPreviewBounds = true;
            return;
        }

        groundBrushPendingPreviewBounds.Encapsulate(bounds.min);
        groundBrushPendingPreviewBounds.Encapsulate(bounds.max);
    }

    private void TryPreviewGroundBrushVisual(BaseGroundBlock block, bool force)
    {
        if (block == null || !block.useUrpLitShadowSafeOutput || !groundBrushHasPendingPreviewBounds)
            return;

        if (!SkyPrisonRenderQualityContext.AllowGroundPaintLivePreview)
            return;

        double now = EditorApplication.timeSinceStartup;
        double interval = Mathf.Max(0.001f, SkyPrisonRenderQualityContext.GetGroundPaintPreviewIntervalSeconds());
        if (!force && groundBrushPainting && now - lastGroundBrushLivePreviewTime < interval)
            return;

        Bounds bounds = groundBrushPendingPreviewBounds;
        groundBrushHasPendingPreviewBounds = false;
        lastGroundBrushLivePreviewTime = now;

        // 拖动中的真实地面纹理反馈需要让控制图的 CPU 修改进入可采样状态。
        // 这里按 30fps 左右合批 Apply，不再每个 stamp Apply，避免回到“左键读条”。
        FlushDeferredGroundPaintTextureUploadsForLivePreview();

        bool oldSuppressImporterChanges = BaseGroundBlock.SuppressAutomaticTextureImporterChanges;
        BaseGroundBlock.SuppressAutomaticTextureImporterChanges = true;
        try
        {
            // 这里调用的是 BaseGroundBlock 里的“编辑实时预览”路径：
            // 只刷本次 dirty bounds，不 SaveAssets，不 Import，不正式运行烘焙。
            block.PreviewRebakeLitGroundTextureRegion(bounds);
        }
        finally
        {
            BaseGroundBlock.SuppressAutomaticTextureImporterChanges = oldSuppressImporterChanges;
        }

        SceneView.RepaintAll();
    }

    private void MarkGroundPaintTextureForDeferredUpload(Texture2D texture)
    {
        if (texture == null)
            return;

        deferredGroundPaintTextureUploads.Add(texture);
        if (SkyPrisonRenderQualityContext.AllowAssetDatabaseDuringPreview)
            EditorUtility.SetDirty(texture);
    }

    private void FlushDeferredGroundPaintTextureUploadsForLivePreview()
    {
        if (deferredGroundPaintTextureUploads.Count == 0)
            return;

        foreach (Texture2D texture in deferredGroundPaintTextureUploads)
        {
            if (texture == null)
                continue;

            // 编辑器拖动实时预览：只把 CPU 端像素提交到 GPU，
            // 不在这里做资产保存 / Import / 完整烘焙。
            texture.Apply(false, false);
        }

        deferredGroundPaintTextureUploads.Clear();
    }

    private void FlushDeferredGroundPaintTextureUploads()
    {
        if (deferredGroundPaintTextureUploads.Count == 0)
            return;

        foreach (Texture2D texture in deferredGroundPaintTextureUploads)
        {
            if (texture == null)
                continue;
            texture.Apply(false, false);
            if (SkyPrisonRenderQualityContext.AllowAssetDatabaseDuringPreview)
                EditorUtility.SetDirty(texture);
        }

        deferredGroundPaintTextureUploads.Clear();
    }

    private void FlushDeferredGroundBrushVisualBake()
    {
        if (!groundBrushVisualBakeDirty && !groundBrushHasPendingPreviewBounds && deferredGroundPaintTextureUploads.Count == 0)
            return;

        BaseGroundBlock block = groundBrushVisualBakeDirtyBlock != null ? groundBrushVisualBakeDirtyBlock : activeGroundBlock;
        groundBrushVisualBakeDirty = false;
        groundBrushVisualBakeDirtyBlock = null;

        if (block == null)
        {
            deferredGroundPaintTextureUploads.Clear();
            groundBrushHasPendingPreviewBounds = false;
            return;
        }

        block.MarkGroundDataDirty(false);
        EditorUtility.SetDirty(block);

        // 一笔结束只补最后一次局部预览，不整图重建、不全局刷新所有 GroundBlock。
        if (block.useUrpLitShadowSafeOutput)
            TryPreviewGroundBrushVisual(block, true);
        else
            ApplyGroundVisualDisplayMode(block);

        // 数据贴图统一在一笔结束时上传，避免 MouseDrag 每个 stamp 都 Apply。
        FlushDeferredGroundPaintTextureUploads();

        SceneView.RepaintAll();
    }

    private void FillActiveGroundShapeMask(float value)
    {
        BaseGroundBlock block = FindActiveGroundBlock();
        if (block == null)
        {
            Debug.LogWarning("[GroundBrush] 当前 Scene 没有找到 BaseGroundBlock。请先同步/补齐 GroundBlock。", this);
            return;
        }

        Texture2D mask = GetOrCreateGroundShapeMask(block);
        if (mask == null)
            return;

        Undo.RegisterCompleteObjectUndo(mask, value >= 0.5f ? "Fill ground shape mask" : "Clear ground shape mask");
        Color c = new Color(value, value, value, value);
        Color[] pixels = Enumerable.Repeat(c, mask.width * mask.height).ToArray();
        mask.SetPixels(pixels);
        mask.Apply(false, false);
        EditorUtility.SetDirty(mask);
        MarkGroundVisualDataDirty(block, true);
        ApplyGroundVisualDisplayModeToAllBlocks();

        // “清空地面”是一次全图擦除。鼠标松开清理只覆盖拖刷，
        // 这里也要同步撤掉落在无地面区域里的地形装饰物。
        if (value < 0.5f && cleanupTerrainDecorationsOnGroundErase)
            CleanupTerrainDecorationsAfterGroundErase(block, block.WorldBounds);

        SceneView.RepaintAll();
    }

    private void PaintGroundShapeMask(BaseGroundBlock block, Vector3 worldPosition, float targetValue)
    {
        Texture2D mask = GetOrCreateGroundShapeMask(block);
        if (mask == null || !block.TryWorldToUV(worldPosition, out Vector2 centerUv))
            return;

        if (groundBrushStrokeUndoActive)
            RegisterGroundBrushStrokeUndoObjects(block);
        else
            Undo.RegisterCompleteObjectUndo(mask, targetValue >= 0.5f ? "Paint ground shape" : "Erase ground shape");

        PaintTextureByBrush(mask, centerUv, block.WorldBounds, (x, y, brushWeight) =>
        {
            Color old = mask.GetPixel(x, y);
            float next = Mathf.Lerp(old.a, targetValue, brushWeight);
            Color c = new Color(next, next, next, next);
            mask.SetPixel(x, y, c);
        });
        MarkGroundPaintTextureForDeferredUpload(mask);
        MarkGroundBrushVisualDirty(block, GetGroundBrushWorldBounds(worldPosition));
    }

    private int BuildGroundBrushStampSeed(Vector3 worldPosition, int paletteIndex)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + groundBrushStampSeedCounter;
            h = h * 31 + paletteIndex;
            h = h * 31 + Mathf.RoundToInt(worldPosition.x * 1000f);
            h = h * 31 + Mathf.RoundToInt(worldPosition.z * 1000f);
            h = h * 31 + Mathf.RoundToInt(groundBrushSize * 100f);
            h = h * 31 + Mathf.RoundToInt(groundBrushHardness * 1000f);
            return h;
        }
    }

    private void PaintGroundSurfaceMaterial(BaseGroundBlock block, Vector3 worldPosition, GroundSurfaceMaterialDefinition material)
    {
        if (block == null || material == null || !block.TryWorldToUV(worldPosition, out Vector2 centerUv))
            return;

        Texture2D indexMap = GetOrCreateSurfaceMaterialIndexMap(block);
        Texture2D previewMap = GetOrCreateSurfaceMaterialPreviewTexture(block);
        if (indexMap == null || previewMap == null)
            return;

        int paletteIndex = block.RegisterSurfaceMaterial(material);
        paletteIndex = Mathf.Clamp(paletteIndex, 0, 255);

        if (groundBrushStrokeUndoActive)
            RegisterGroundBrushStrokeUndoObjects(block);
        else
            Undo.RecordObject(block, "Paint ground surface material");

        // 随机散布必须以“笔触/印章”为单位记录随机种子。
        // 只改权重图会导致烘焙时只能按世界格子随机，反复平刷还是同一套纹理。
        block.AddSurfacePaintStamp(
            paletteIndex,
            worldPosition,
            groundBrushSize,
            groundBrushHardness,
            1f);

        Texture2D targetWeightMap = null;
        if (block.enableSurfaceWeightBlend)
            targetWeightMap = GetOrCreateSurfaceMaterialWeightMap(block, paletteIndex);

        if (!groundBrushStrokeUndoActive)
        {
            Undo.RegisterCompleteObjectUndo(indexMap, "Paint ground surface material index");
            Undo.RegisterCompleteObjectUndo(previewMap, "Paint ground surface material preview");
            if (targetWeightMap != null)
                RegisterUndoForAllSurfaceWeightMaps(block, "Paint ground surface material weights");
            Undo.RecordObject(block, "Register ground surface material");
        }

        float encoded = paletteIndex / 255f;
        Color indexColor = new Color(encoded, 0f, 0f, 1f);
        Color previewColor = material.baseColor;
        previewColor.a = 1f;

        PaintTextureByBrush(indexMap, centerUv, block.WorldBounds, (x, y, brushWeight) =>
        {
            if (!IsGroundShapePixelSolid(block, x, y, indexMap.width, indexMap.height))
                return;

            if (block.enableSurfaceWeightBlend && targetWeightMap != null)
            {
                PaintSurfaceWeightsAtPixel(block, x, y, indexMap.width, indexMap.height, paletteIndex, brushWeight);
                int dominant = FindDominantSurfaceWeightAtPixel(block, x, y, indexMap.width, indexMap.height, paletteIndex);
                indexMap.SetPixel(x, y, new Color(Mathf.Clamp(dominant, 0, 255) / 255f, 0f, 0f, 1f));

                Color blendedPreview = BuildWeightedPreviewColorAtPixel(block, x, y, indexMap.width, indexMap.height, previewColor);
                previewMap.SetPixel(x, y, blendedPreview);
            }
            else
            {
                if (brushWeight >= 0.50f)
                    indexMap.SetPixel(x, y, indexColor);

                Color oldPreview = previewMap.GetPixel(x, y);
                previewMap.SetPixel(x, y, Color.Lerp(oldPreview, previewColor, brushWeight));
            }
        });

        if (block.enableSurfaceWeightBlend && block.surfaceMaterialWeightMaps != null)
        {
            foreach (Texture2D weightMap in block.surfaceMaterialWeightMaps)
            {
                if (weightMap == null)
                    continue;
                MarkGroundPaintTextureForDeferredUpload(weightMap);
            }
        }

        MarkGroundPaintTextureForDeferredUpload(indexMap);
        MarkGroundPaintTextureForDeferredUpload(previewMap);
        MarkGroundBrushVisualDirty(block, GetGroundBrushWorldBounds(worldPosition));
    }

    private void RegisterUndoForAllSurfaceWeightMaps(BaseGroundBlock block, string label)
    {
        if (block == null || block.surfaceMaterialWeightMaps == null)
            return;

        foreach (Texture2D map in block.surfaceMaterialWeightMaps)
        {
            if (map != null)
                Undo.RegisterCompleteObjectUndo(map, label);
        }
    }

    private void PaintSurfaceWeightsAtPixel(BaseGroundBlock block, int x, int y, int width, int height, int targetPaletteIndex, float brushWeight)
    {
        if (block == null || block.surfaceMaterialPalette == null)
            return;

        int count = Mathf.Clamp(block.surfaceMaterialPalette.Count, 1, 256);
        float[] weights = new float[count];
        float total = 0f;

        for (int i = 0; i < count; i++)
        {
            weights[i] = GetSurfaceWeightAtPixel(block, i, x, y, width, height);
            total += weights[i];
        }

        if (total <= 0.0001f)
        {
            int currentIndex = 0;
            if (block.surfaceMaterialIndexMap != null)
            {
                Color c = block.surfaceMaterialIndexMap.GetPixel(x, y);
                currentIndex = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), 0, count - 1);
            }
            weights[currentIndex] = 1f;
            total = 1f;
        }

        for (int i = 0; i < count; i++)
            weights[i] = total > 0.0001f ? weights[i] / total : 0f;

        float w = Mathf.Clamp01(brushWeight);
        for (int i = 0; i < count; i++)
            weights[i] *= (1f - w);
        weights[targetPaletteIndex] += w;

        float newTotal = 0f;
        for (int i = 0; i < count; i++)
            newTotal += weights[i];
        if (newTotal <= 0.0001f)
        {
            weights[targetPaletteIndex] = 1f;
            newTotal = 1f;
        }

        for (int i = 0; i < count; i++)
            SetSurfaceWeightAtPixel(block, i, x, y, weights[i] / newTotal);
    }

    private int FindDominantSurfaceWeightAtPixel(BaseGroundBlock block, int x, int y, int width, int height, int fallback)
    {
        if (block == null || block.surfaceMaterialPalette == null || block.surfaceMaterialPalette.Count == 0)
            return fallback;

        int count = Mathf.Clamp(block.surfaceMaterialPalette.Count, 1, 256);
        int best = Mathf.Clamp(fallback, 0, count - 1);
        float bestWeight = -1f;
        for (int i = 0; i < count; i++)
        {
            float w = GetSurfaceWeightAtPixel(block, i, x, y, width, height);
            if (w > bestWeight)
            {
                bestWeight = w;
                best = i;
            }
        }
        return best;
    }

    private Color BuildWeightedPreviewColorAtPixel(BaseGroundBlock block, int x, int y, int width, int height, Color fallback)
    {
        if (block == null || block.surfaceMaterialPalette == null || block.surfaceMaterialPalette.Count == 0)
            return fallback;

        int count = Mathf.Clamp(block.surfaceMaterialPalette.Count, 1, 256);
        Color accum = Color.clear;
        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            float w = GetSurfaceWeightAtPixel(block, i, x, y, width, height);
            if (w <= 0.0001f)
                continue;

            GroundSurfaceMaterialDefinition mat = block.surfaceMaterialPalette[i];
            Color c = mat != null ? mat.baseColor : fallback;
            c.a = 1f;
            accum += c * w;
            total += w;
        }

        if (total <= 0.0001f)
            return fallback;
        Color result = accum / total;
        result.a = 1f;
        return result;
    }

    private float GetSurfaceWeightAtPixel(BaseGroundBlock block, int paletteIndex, int x, int y, int width, int height)
    {
        Texture2D map = GetSurfaceMaterialWeightMap(block, paletteIndex, false);
        if (map == null)
            return 0f;

        int mx = width == map.width ? x : Mathf.Clamp(Mathf.RoundToInt((x / Mathf.Max(1f, width - 1f)) * (map.width - 1)), 0, map.width - 1);
        int my = height == map.height ? y : Mathf.Clamp(Mathf.RoundToInt((y / Mathf.Max(1f, height - 1f)) * (map.height - 1)), 0, map.height - 1);
        Color c = map.GetPixel(mx, my);
        switch (paletteIndex % 4)
        {
            case 0: return Mathf.Clamp01(c.r);
            case 1: return Mathf.Clamp01(c.g);
            case 2: return Mathf.Clamp01(c.b);
            case 3: return Mathf.Clamp01(c.a);
            default: return 0f;
        }
    }

    private void SetSurfaceWeightAtPixel(BaseGroundBlock block, int paletteIndex, int x, int y, float value)
    {
        Texture2D map = GetSurfaceMaterialWeightMap(block, paletteIndex, true);
        if (map == null)
            return;

        int mx = x;
        int my = y;
        if (block.surfaceMaterialIndexMap != null && (block.surfaceMaterialIndexMap.width != map.width || block.surfaceMaterialIndexMap.height != map.height))
        {
            mx = Mathf.Clamp(Mathf.RoundToInt((x / Mathf.Max(1f, block.surfaceMaterialIndexMap.width - 1f)) * (map.width - 1)), 0, map.width - 1);
            my = Mathf.Clamp(Mathf.RoundToInt((y / Mathf.Max(1f, block.surfaceMaterialIndexMap.height - 1f)) * (map.height - 1)), 0, map.height - 1);
        }

        Color c = map.GetPixel(mx, my);
        switch (paletteIndex % 4)
        {
            case 0: c.r = Mathf.Clamp01(value); break;
            case 1: c.g = Mathf.Clamp01(value); break;
            case 2: c.b = Mathf.Clamp01(value); break;
            case 3: c.a = Mathf.Clamp01(value); break;
        }
        map.SetPixel(mx, my, c);
    }

    private Texture2D GetSurfaceMaterialWeightMap(BaseGroundBlock block, int paletteIndex, bool create)
    {
        if (block == null || paletteIndex < 0)
            return null;

        if (block.surfaceMaterialWeightMaps == null)
            block.surfaceMaterialWeightMaps = new List<Texture2D>();

        int group = paletteIndex / 4;
        while (block.surfaceMaterialWeightMaps.Count <= group)
            block.surfaceMaterialWeightMaps.Add(null);

        Texture2D map = block.surfaceMaterialWeightMaps[group];
        if (map != null)
        {
            EnsureGroundDataTextureResolution(block, map, false);
            return map;
        }
        if (!create)
            return null;

        map = CreateGroundDataTexture(block, $"SurfaceWeightMap_{group:00}", Color.clear, TextureFormat.RGBA32, FilterMode.Bilinear, "GroundMaps");
        if (map == null)
            return null;

        // 从旧的硬 ID 材质图迁移出第一版权重。这样开启“材质软边权重混合”后，
        // 旧地图不会突然全部变回默认材质。
        InitializeSurfaceWeightMapFromIndexMap(block, map, group);
        map.Apply(false, false);
        EditorUtility.SetDirty(map);

        Undo.RecordObject(block, "Assign surface material weight map");
        block.surfaceMaterialWeightMaps[group] = map;
        EditorUtility.SetDirty(block);
        return map;
    }

    private void InitializeSurfaceWeightMapFromIndexMap(BaseGroundBlock block, Texture2D weightMap, int group)
    {
        if (block == null || weightMap == null)
            return;

        int w = weightMap.width;
        int h = weightMap.height;

        if (block.surfaceMaterialIndexMap == null)
        {
            Color fill = group == 0 ? new Color(1f, 0f, 0f, 0f) : Color.clear;
            Color[] pixels = Enumerable.Repeat(fill, w * h).ToArray();
            weightMap.SetPixels(pixels);
            return;
        }

        Color[] outPixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = h <= 1 ? 0f : y / (float)(h - 1);
            for (int x = 0; x < w; x++)
            {
                float u = w <= 1 ? 0f : x / (float)(w - 1);
                int index = Mathf.Clamp(Mathf.RoundToInt(block.surfaceMaterialIndexMap.GetPixelBilinear(u, v).r * 255f), 0, 255);
                Color c = Color.clear;
                if (index / 4 == group)
                {
                    switch (index % 4)
                    {
                        case 0: c.r = 1f; break;
                        case 1: c.g = 1f; break;
                        case 2: c.b = 1f; break;
                        case 3: c.a = 1f; break;
                    }
                }
                outPixels[y * w + x] = c;
            }
        }
        weightMap.SetPixels(outPixels);
    }

    private Texture2D GetOrCreateSurfaceMaterialWeightMap(BaseGroundBlock block, int paletteIndex)
    {
        return GetSurfaceMaterialWeightMap(block, paletteIndex, true);
    }

    private delegate void PaintPixelDelegate(int x, int y, float brushWeight);

    private void PaintTextureByBrush(Texture2D texture, Vector2 centerUv, Bounds bounds, PaintPixelDelegate paintPixel)
    {
        int centerX = Mathf.RoundToInt(centerUv.x * (texture.width - 1));
        int centerY = Mathf.RoundToInt(centerUv.y * (texture.height - 1));
        float pixelsPerWorldX = texture.width / Mathf.Max(0.01f, bounds.size.x);
        float pixelsPerWorldZ = texture.height / Mathf.Max(0.01f, bounds.size.z);
        int radiusX = Mathf.CeilToInt(groundBrushSize * 0.5f * pixelsPerWorldX);
        int radiusY = Mathf.CeilToInt(groundBrushSize * 0.5f * pixelsPerWorldZ);
        radiusX = Mathf.Max(1, radiusX);
        radiusY = Mathf.Max(1, radiusY);

        int minX = Mathf.Clamp(centerX - radiusX, 0, texture.width - 1);
        int maxX = Mathf.Clamp(centerX + radiusX, 0, texture.width - 1);
        int minY = Mathf.Clamp(centerY - radiusY, 0, texture.height - 1);
        int maxY = Mathf.Clamp(centerY + radiusY, 0, texture.height - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = radiusX <= 0 ? 0f : Mathf.Abs(x - centerX) / (float)radiusX;
                float dy = radiusY <= 0 ? 0f : Mathf.Abs(y - centerY) / (float)radiusY;
                if (!TryEvaluateGroundBrushShape(dx, dy, out float normalizedDistance))
                    continue;

                float brushWeight = EvaluateGroundBrushHardnessWeight(normalizedDistance);
                if (brushWeight <= 0.0001f)
                    continue;

                paintPixel?.Invoke(x, y, brushWeight);
            }
        }
    }

    private float EvaluateGroundBrushHardnessWeight(float normalizedDistance)
    {
        float hardness = Mathf.Clamp01(groundBrushHardness);
        float d = Mathf.Clamp01(normalizedDistance);

        // Photoshop 风格硬度：
        // 硬度越高，中心实心区域越大，边缘过渡越窄；
        // 硬度越低，从中心到边缘越早衰减。
        if (hardness >= 0.999f)
            return 1f;

        float fadeStart = hardness;
        if (d <= fadeStart)
            return 1f;

        float fadeRange = Mathf.Max(0.0001f, 1f - fadeStart);
        float t = Mathf.Clamp01((d - fadeStart) / fadeRange);
        return 1f - Mathf.SmoothStep(0f, 1f, t);
    }

    private bool IsGroundShapePixelSolid(BaseGroundBlock block, int x, int y, int width, int height)
    {
        if (block == null || block.groundShapeMask == null)
            return true;

        float u = width <= 1 ? 0f : x / (float)(width - 1);
        float v = height <= 1 ? 0f : y / (float)(height - 1);
        return block.groundShapeMask.GetPixelBilinear(u, v).a >= block.groundMaskThreshold;
    }

    private Texture2D GetOrCreateGroundShapeMask(BaseGroundBlock block)
    {
        if (block == null)
            return null;
        if (block.groundShapeMask != null)
        {
            EnsureGroundDataTextureResolution(block, block.groundShapeMask, false);
            return block.groundShapeMask;
        }

        Texture2D mask = CreateGroundDataTexture(block, "GroundShapeMask", Color.white, TextureFormat.RGBA32, FilterMode.Bilinear, "GroundMasks");
        if (mask == null)
            return null;
        Undo.RecordObject(block, "Assign ground shape mask");
        block.groundShapeMask = mask;
        EditorUtility.SetDirty(block);
        return mask;
    }

    private Texture2D GetOrCreateSurfaceMaterialIndexMap(BaseGroundBlock block)
    {
        if (block == null)
            return null;
        if (block.surfaceMaterialIndexMap != null)
        {
            EnsureGroundDataTextureResolution(block, block.surfaceMaterialIndexMap, true);
            return block.surfaceMaterialIndexMap;
        }

        Texture2D map = CreateGroundDataTexture(block, "SurfaceMaterialIndexMap", Color.black, TextureFormat.RGBA32, FilterMode.Point, "GroundMaps");
        if (map == null)
            return null;
        Undo.RecordObject(block, "Assign surface material index map");
        block.surfaceMaterialIndexMap = map;
        if (block.surfaceMaterialPalette == null)
            block.surfaceMaterialPalette = new List<GroundSurfaceMaterialDefinition>();
        if (block.defaultSurfaceMaterial != null && !block.surfaceMaterialPalette.Contains(block.defaultSurfaceMaterial))
            block.surfaceMaterialPalette.Insert(0, block.defaultSurfaceMaterial);
        EditorUtility.SetDirty(block);
        return map;
    }

    private Texture2D GetOrCreateSurfaceMaterialPreviewTexture(BaseGroundBlock block)
    {
        if (block == null)
            return null;
        if (block.surfaceMaterialPreviewTexture != null)
        {
            EnsureGroundDataTextureResolution(block, block.surfaceMaterialPreviewTexture, false);
            return block.surfaceMaterialPreviewTexture;
        }

        Color baseColor = block.defaultSurfaceMaterial != null ? block.defaultSurfaceMaterial.baseColor : new Color(0.45f, 0.45f, 0.45f, 1f);
        baseColor.a = 1f;
        Texture2D map = CreateGroundDataTexture(block, "SurfaceMaterialPreview", baseColor, TextureFormat.RGBA32, FilterMode.Bilinear, "GroundMaps");
        if (map == null)
            return null;
        Undo.RecordObject(block, "Assign surface material preview texture");
        block.surfaceMaterialPreviewTexture = map;
        EditorUtility.SetDirty(block);
        ApplyGroundVisualDisplayModeToAllBlocks();
        return map;
    }


    private int GetDesiredGroundDataTextureResolution(BaseGroundBlock block)
    {
        if (block == null)
            return 2048;

        int dataResolution = Mathf.Clamp(block.groundDataTextureResolution, 512, 4096);
        int bakeResolution = Mathf.Clamp(block.litBakedTextureResolution, 128, 4096);
        return Mathf.Clamp(Mathf.Max(dataResolution, bakeResolution), 512, 4096);
    }

    private bool EnsureGroundDataTextureResolution(BaseGroundBlock block, Texture2D texture, bool preserveNearest)
    {
        if (block == null || texture == null || !block.autoUpgradeGroundDataTextures)
            return false;

        int target = GetDesiredGroundDataTextureResolution(block);
        if (texture.width >= target && texture.height >= target)
            return false;

        int oldW = texture.width;
        int oldH = texture.height;
        Color[] resized = new Color[target * target];
        for (int y = 0; y < target; y++)
        {
            float v = target <= 1 ? 0f : y / (float)(target - 1);
            for (int x = 0; x < target; x++)
            {
                float u = target <= 1 ? 0f : x / (float)(target - 1);
                Color c;
                if (preserveNearest)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt(u * (oldW - 1)), 0, oldW - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt(v * (oldH - 1)), 0, oldH - 1);
                    c = texture.GetPixel(sx, sy);
                }
                else
                {
                    c = texture.GetPixelBilinear(u, v);
                }
                resized[y * target + x] = c;
            }
        }

        texture.Reinitialize(target, target, texture.format, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(resized);
        texture.Apply(false, false);
        EditorUtility.SetDirty(texture);
        return true;
    }

    private Texture2D CreateGroundDataTexture(BaseGroundBlock block, string suffix, Color fill, TextureFormat format, FilterMode filterMode, string folderName)
    {
        int resolution = GetDesiredGroundDataTextureResolution(block);
        Texture2D tex = new Texture2D(resolution, resolution, format, false, true);
        tex.name = $"{block.gameObject.name}_{suffix}";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = filterMode;
        Color[] pixels = Enumerable.Repeat(fill, resolution * resolution).ToArray();
        tex.SetPixels(pixels);
        tex.Apply(false, false);

        string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        string folder = $"Assets/_Project/Data/Maps/{folderName}";
        if (!string.IsNullOrEmpty(scenePath))
        {
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (!string.IsNullOrWhiteSpace(sceneName))
                folder = $"Assets/_Project/Data/Maps/{folderName}/{sceneName}";
        }
        EnsureAssetFolder(folder);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{tex.name}.asset");
        AssetDatabase.CreateAsset(tex, path);
        AssetDatabase.SaveAssets();
        return tex;
    }

    private BaseGroundBlock FindActiveGroundBlock()
    {
        BaseGroundBlock selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<BaseGroundBlock>() : null;
        if (selected != null)
            return selected;

        BaseGroundBlock existing = FindObjectsOfType<BaseGroundBlock>(true)
            .FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
        if (existing != null)
            return existing;

        return EnsureBaseGroundBlockComponentInScene();
    }

    private BaseGroundBlock EnsureBaseGroundBlockComponentInScene()
    {
        Transform blockTransform = FindTransformByPath("WorldRoot/GroundRoot/GroundBlock_01");
        if (blockTransform == null)
        {
            GameObject go = GameObject.Find("GroundBlock_01");
            if (go != null && go.scene.IsValid())
                blockTransform = go.transform;
        }

        if (blockTransform == null)
            return null;

        BaseGroundBlock block = blockTransform.GetComponent<BaseGroundBlock>();
        SetLayerRecursively(blockTransform.gameObject, LayerMask.NameToLayer("World3D"));

        if (block == null)
        {
            try
            {
                block = Undo.AddComponent<BaseGroundBlock>(blockTransform.gameObject);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    "[GroundBrush] 无法把 BaseGroundBlock 挂到 GroundBlock_01。" +
                    "请确认 BaseGroundBlock.cs / GroundSurfaceType.cs / GroundSurfaceMarker.cs / GroundSurfaceMaterialDefinition.cs 都放在非 Editor 目录。\n" +
                    ex.Message,
                    blockTransform.gameObject);
                return null;
            }

            if (block == null)
            {
                Debug.LogError(
                    "[GroundBrush] AddComponent<BaseGroundBlock>() 返回 null。" +
                    "通常是 BaseGroundBlock.cs 仍然位于 Editor 文件夹，Unity 不允许把 Editor 脚本挂到 Scene 物体上。",
                    blockTransform.gameObject);
                return null;
            }

            Debug.Log($"[GroundBrush] Auto added BaseGroundBlock to {blockTransform.name}.", blockTransform.gameObject);
        }

        BindBaseGroundBlockChildren(block, blockTransform);
        SyncBaseGroundBlockBoundsFromScene(block, blockTransform);

        if (block != null)
            EditorUtility.SetDirty(block);
        return block;
    }

    private void BindBaseGroundBlockChildren(BaseGroundBlock block, Transform blockTransform)
    {
        if (block == null || blockTransform == null)
            return;

        if (block.groundVisualRoot == null)
            block.groundVisualRoot = EnsureChild(blockTransform, "GroundVisual");
        if (block.groundColliderRoot == null)
            block.groundColliderRoot = EnsureChild(blockTransform, "GroundCollider");
        if (block.groundDebugRoot == null)
            block.groundDebugRoot = EnsureChild(blockTransform, "GroundDebug");

        SetLayerRecursively(blockTransform.gameObject, LayerMask.NameToLayer("World3D"));
    }

    private void SyncBaseGroundBlockBoundsFromScene(BaseGroundBlock block, Transform blockTransform)
    {
        if (block == null || blockTransform == null)
            return;

        Transform mapBounds = null;
        GameObject mapBoundsGo = GameObject.Find("MapBounds");
        if (mapBoundsGo != null && mapBoundsGo.scene.IsValid())
            mapBounds = mapBoundsGo.transform;

        if (mapBounds != null)
        {
            block.mapBoundsCenter = mapBounds.position;
            Vector3 s = mapBounds.lossyScale;
            block.mapBoundsSize = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(s.x)),
                Mathf.Max(0.01f, Mathf.Abs(s.y)),
                Mathf.Max(0.01f, Mathf.Abs(s.z)));
            return;
        }

        block.mapBoundsCenter = blockTransform.position;
        Vector3 scale = blockTransform.lossyScale;
        block.mapBoundsSize = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(scale.x)),
            Mathf.Max(0.01f, Mathf.Abs(scale.y)),
            Mathf.Max(0.01f, Mathf.Abs(scale.z)));
    }

    private void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        if (parts.Length == 0)
            return;
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private void RefreshDefinitions()
    {
        definitions.Clear();
        foreach (string guid in AssetDatabase.FindAssets(DefinitionSearchFilter))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TerrainDecorationDefinition def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(path);
            if (def != null)
                definitions.Add(def);
        }
        definitions.Sort((a, b) => string.Compare(GetDisplayName(a), GetDisplayName(b), StringComparison.OrdinalIgnoreCase));
        if (selectedDefinition == null && definitions.Count > 0)
            selectedDefinition = definitions[0];
        RefreshSurfaceMaterials();
    }

    private void RefreshPlacedCache()
    {
        placedCache.Clear();
        TerrainDecorationRuntimeBinder[] binders = FindObjectsOfType<TerrainDecorationRuntimeBinder>(true);
        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null || binder.gameObject == null)
                continue;
            if (!binder.gameObject.scene.IsValid())
                continue;
            placedCache.Add(binder);
        }
        placedCache.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.OrdinalIgnoreCase));

        RefreshPlacedSurfaceCache();
        RefreshPlacedUnitCache();
        RefreshPlacedItemCache();
        PrunePlacedSelection();
    }

    private void RefreshPlacedUnitCache()
    {
        placedUnitCache.Clear();
        UnitDefinitionRuntimeBinder[] binders = FindObjectsOfType<UnitDefinitionRuntimeBinder>(true);
        foreach (UnitDefinitionRuntimeBinder b in binders)
        {
            if (b == null || b.gameObject == null) continue;
            if (!b.gameObject.scene.IsValid()) continue;
            placedUnitCache.Add(b);
        }
        placedUnitCache.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.OrdinalIgnoreCase));
        placedUnitSelectionIds.RemoveWhere(id => !placedUnitCache.Exists(x => x.gameObject.GetInstanceID() == id));
    }

    private void RefreshPlacedSurfaceCache()
    {
        placedSurfaceCache.Clear();

        HashSet<int> visited = new HashSet<int>();
        AddPlacedSurfaceObjectsFromParentPath(GroundStampParentPath, visited);
        AddPlacedSurfaceObjectsFromParentPath(GroundSplineParentPath, visited);

        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null || renderer.gameObject == null)
                continue;
            GameObject go = renderer.gameObject;
            if (!go.scene.IsValid())
                continue;
            if (visited.Contains(go.GetInstanceID()))
                continue;
            if (!LooksLikeGroundSurfacePlacedObject(go, renderer))
                continue;

            AddPlacedSurfaceObject(go, renderer, visited);
        }

        placedSurfaceCache.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.OrdinalIgnoreCase));
    }

    private void AddPlacedSurfaceObjectsFromParentPath(string parentPath, HashSet<int> visited)
    {
        Transform parent = FindSceneTransformByPath(parentPath);
        if (parent == null)
            return;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null || child.gameObject == null)
                continue;
            MeshRenderer renderer = child.GetComponentInChildren<MeshRenderer>(true);
            AddPlacedSurfaceObject(child.gameObject, renderer, visited);
        }
    }

    private Transform FindSceneTransformByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string[] parts = path.Split('/');
        if (parts.Length == 0)
            return null;

        GameObject root = GameObject.Find(parts[0]);
        if (root == null)
            return null;

        Transform current = root.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            if (current == null)
                return null;
            current = current.Find(parts[i]);
        }
        return current;
    }

    private bool LooksLikeGroundSurfacePlacedObject(GameObject go, MeshRenderer renderer)
    {
        if (go == null)
            return false;

        string name = go.name ?? string.Empty;
        if (name.StartsWith("GroundStamp_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.IndexOf("GroundSpline", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (name.IndexOf("RoadLine", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        Transform t = go.transform;
        while (t != null)
        {
            string n = t.name ?? string.Empty;
            if (n.Equals("GroundStamps", StringComparison.OrdinalIgnoreCase)
                || n.Equals("GroundSplines", StringComparison.OrdinalIgnoreCase)
                || n.Equals("GroundOverlays", StringComparison.OrdinalIgnoreCase)
                || n.Equals("RoadLines", StringComparison.OrdinalIgnoreCase))
                return true;
            t = t.parent;
        }

        if (renderer != null && renderer.sharedMaterial != null)
        {
            string path = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
            if (!string.IsNullOrEmpty(path)
                && path.Replace('\\', '/').IndexOf("/TerrainLayers/GroundSurface/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private void AddPlacedSurfaceObject(GameObject go, MeshRenderer renderer, HashSet<int> visited)
    {
        if (go == null)
            return;

        int id = go.GetInstanceID();
        if (!visited.Add(id))
            return;

        Texture preview = null;
        string materialName = "无材质";
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material mat = renderer.sharedMaterial;
            materialName = mat.name;
            if (mat.HasProperty("_BaseMap"))
                preview = mat.GetTexture("_BaseMap");
            if (preview == null && mat.HasProperty("_MainTex"))
                preview = mat.GetTexture("_MainTex");
        }

        placedSurfaceCache.Add(new PlacedSurfaceObject(go, GetPlacedSurfaceKindLabel(go), materialName, preview));
    }

    private string GetPlacedSurfaceKindLabel(GameObject go)
    {
        if (go == null)
            return "地表视觉物";
        string name = go.name ?? string.Empty;
        if (name.StartsWith("GroundStamp_", StringComparison.OrdinalIgnoreCase))
            return "印章 / 贴花";
        if (name.IndexOf("GroundSpline", StringComparison.OrdinalIgnoreCase) >= 0)
            return "画线 / GroundSpline";
        if (name.IndexOf("RoadLine", StringComparison.OrdinalIgnoreCase) >= 0)
            return "画线 / RoadLine";
        return "地表视觉物";
    }

    private List<TerrainDecorationRuntimeBinder> GetFilteredPlacedBinders()
    {
        string s = string.IsNullOrWhiteSpace(placedSearch) ? "" : placedSearch.Trim().ToLowerInvariant();
        return placedCache.Where(b =>
        {
            if (b == null || b.gameObject == null)
                return false;
            if (string.IsNullOrEmpty(s))
                return true;
            string def = b.definition != null ? GetDisplayName(b.definition) : "";
            return b.gameObject.name.ToLowerInvariant().Contains(s)
                   || def.ToLowerInvariant().Contains(s)
                   || (b.selectedVariantId ?? "").ToLowerInvariant().Contains(s)
                   || (b.instanceId ?? "").ToLowerInvariant().Contains(s);
        }).ToList();
    }

    private List<PlacedSurfaceObject> GetFilteredPlacedSurfaceObjects()
    {
        string s = string.IsNullOrWhiteSpace(placedSearch) ? "" : placedSearch.Trim().ToLowerInvariant();
        return placedSurfaceCache.Where(item =>
        {
            if (item == null || item.gameObject == null)
                return false;
            if (string.IsNullOrEmpty(s))
                return true;
            return item.gameObject.name.ToLowerInvariant().Contains(s)
                   || (item.kindLabel ?? "").ToLowerInvariant().Contains(s)
                   || (item.materialName ?? "").ToLowerInvariant().Contains(s);
        }).ToList();
    }

    private bool IsDirtyPlacedInstance(TerrainDecorationRuntimeBinder binder)
    {
        if (binder == null || binder.gameObject == null)
            return false;
        if (binder.definition == null)
            return true;
        return !HasRealVisualRenderer(binder.gameObject);
    }

    private bool HasRealVisualRenderer(GameObject root)
    {
        if (root == null)
            return false;
        Transform visualRoot = root.transform.Find("VisualRoot");
        if (visualRoot == null)
            return false;
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        return renderers.Any(r => r != null && r.gameObject.activeInHierarchy);
    }

    private void SelectDirtyPlacedInstances()
    {
        RefreshPlacedCache();
        List<GameObject> dirty = placedCache.Where(IsDirtyPlacedInstance).Select(x => x.gameObject).Where(x => x != null).ToList();
        Selection.objects = dirty.Cast<UnityEngine.Object>().ToArray();
        Debug.Log($"[TerrainDecorationPlacement] 已选择异常地形装饰物实例：{dirty.Count} 个。", this);
    }

    private void DeleteDirtyPlacedInstances()
    {
        RefreshPlacedCache();
        List<TerrainDecorationRuntimeBinder> dirty = placedCache.Where(IsDirtyPlacedInstance).ToList();
        if (dirty.Count == 0)
        {
            EditorUtility.DisplayDialog("清理异常实例", "没有找到异常地形装饰物实例。", "知道了");
            return;
        }
        bool ok = EditorUtility.DisplayDialog("删除异常地形装饰物实例", $"将删除 {dirty.Count} 个异常实例。这个操作只删除地形装饰物根节点。", "删除", "取消");
        if (!ok)
            return;
        int deleted = 0;
        foreach (TerrainDecorationRuntimeBinder binder in dirty)
        {
            if (binder != null && binder.gameObject != null)
            {
                Undo.DestroyObjectImmediate(binder.gameObject);
                deleted++;
            }
        }
        if (deleted > 0)
            PlayEditorSound(DeleteSoundPath);
        RefreshPlacedCache();
    }

    private void DeletePlacedInstanceWithConfirm(TerrainDecorationRuntimeBinder binder)
    {
        if (binder == null || binder.gameObject == null)
            return;
        bool ok = EditorUtility.DisplayDialog("删除地形装饰物", $"确定连根删除：\n{binder.gameObject.name}\n\n会删除它下面的 VisualRoot / RuleRoot / Collision / Mask 等所有自动节点。", "删除", "取消");
        if (!ok)
            return;
        Undo.DestroyObjectImmediate(binder.gameObject);
        PlayEditorSound(DeleteSoundPath);
        RefreshPlacedCache();
    }


    private void HandlePlacedRowSelection(Rect rect, int index, List<GameObject> visibleObjects, bool surfaceList)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0)
            return;
        if (!rect.Contains(e.mousePosition))
            return;

        if (visibleObjects == null || index < 0 || index >= visibleObjects.Count)
            return;

        GameObject clicked = visibleObjects[index];
        if (clicked == null)
            return;

        if (e.shift && placedLastClickedIndex >= 0 && placedLastClickedSurfaceList == surfaceList)
        {
            int a = Mathf.Clamp(Mathf.Min(placedLastClickedIndex, index), 0, visibleObjects.Count - 1);
            int b = Mathf.Clamp(Mathf.Max(placedLastClickedIndex, index), 0, visibleObjects.Count - 1);
            if (!e.control && !e.command)
                placedSelectionIds.Clear();
            for (int i = a; i <= b; i++)
            {
                GameObject go = visibleObjects[i];
                if (go != null)
                    placedSelectionIds.Add(go.GetInstanceID());
            }
        }
        else if (e.control || e.command)
        {
            int id = clicked.GetInstanceID();
            if (placedSelectionIds.Contains(id))
                placedSelectionIds.Remove(id);
            else
                placedSelectionIds.Add(id);
            placedLastClickedIndex = index;
            placedLastClickedSurfaceList = surfaceList;
        }
        else
        {
            placedSelectionIds.Clear();
            placedSelectionIds.Add(clicked.GetInstanceID());
            placedLastClickedIndex = index;
            placedLastClickedSurfaceList = surfaceList;
        }

        Selection.objects = placedSelectionIds
            .Select(EditorUtility.InstanceIDToObject)
            .Where(o => o != null)
            .ToArray();
        if (Selection.activeGameObject == null || !placedSelectionIds.Contains(Selection.activeGameObject.GetInstanceID()))
            Selection.activeGameObject = clicked;

        e.Use();
        Repaint();
    }

    private void SetPlacedSelectionTo(GameObject go, bool surfaceList)
    {
        placedSelectionIds.Clear();
        if (go != null)
        {
            placedSelectionIds.Add(go.GetInstanceID());
            Selection.activeGameObject = go;
        }
        placedLastClickedIndex = -1;
        placedLastClickedSurfaceList = surfaceList;
        Repaint();
    }

    private void ClearPlacedSelection()
    {
        placedSelectionIds.Clear();
        placedLastClickedIndex = -1;
        Selection.objects = Array.Empty<UnityEngine.Object>();
        Repaint();
    }

    private int CountCurrentPlacedSelection(bool surfaceList)
    {
        HashSet<int> current = new HashSet<int>(surfaceList
            ? placedSurfaceCache.Where(x => x != null && x.gameObject != null).Select(x => x.gameObject.GetInstanceID())
            : placedCache.Where(x => x != null && x.gameObject != null).Select(x => x.gameObject.GetInstanceID()));
        return placedSelectionIds.Count(id => current.Contains(id));
    }

    private List<GameObject> GetCurrentSelectedPlacedObjects(bool surfaceList)
    {
        HashSet<int> current = new HashSet<int>(surfaceList
            ? placedSurfaceCache.Where(x => x != null && x.gameObject != null).Select(x => x.gameObject.GetInstanceID())
            : placedCache.Where(x => x != null && x.gameObject != null).Select(x => x.gameObject.GetInstanceID()));

        return placedSelectionIds
            .Where(id => current.Contains(id))
            .Select(id => EditorUtility.InstanceIDToObject(id) as GameObject)
            .Where(go => go != null)
            .ToList();
    }

    private void PrunePlacedSelection()
    {
        HashSet<int> valid = new HashSet<int>();
        foreach (TerrainDecorationRuntimeBinder binder in placedCache)
        {
            if (binder != null && binder.gameObject != null)
                valid.Add(binder.gameObject.GetInstanceID());
        }
        foreach (PlacedSurfaceObject item in placedSurfaceCache)
        {
            if (item != null && item.gameObject != null)
                valid.Add(item.gameObject.GetInstanceID());
        }
        placedSelectionIds.RemoveWhere(id => !valid.Contains(id));
    }

    private void DeleteSelectedPlacedObjectsWithConfirm(bool surfaceList)
    {
        List<GameObject> targets = GetCurrentSelectedPlacedObjects(surfaceList);
        if (targets.Count == 0)
            return;

        string title = surfaceList ? "删除已选地表视觉物" : "删除已选地形装饰物";
        string message = surfaceList
            ? $"将删除 {targets.Count} 个已选 GroundStamp / RoadLine / GroundSpline 对象。"
            : $"将删除 {targets.Count} 个已选地形装饰物根节点。";
        bool ok = EditorUtility.DisplayDialog(title, message, "删除", "取消");
        if (!ok)
            return;

        int deleted = 0;
        foreach (GameObject go in targets)
        {
            if (go == null)
                continue;
            Undo.DestroyObjectImmediate(go);
            deleted++;
        }

        if (deleted > 0)
            PlayEditorSound(DeleteSoundPath);
        placedSelectionIds.Clear();
        placedLastClickedIndex = -1;
        RefreshPlacedCache();
        Repaint();
    }

    private void DeleteGroundSurfacePlacedObjectWithConfirm(GameObject go)
    {
        if (go == null)
            return;
        bool ok = EditorUtility.DisplayDialog("删除地表视觉物", $"确定删除：\n{go.name}\n\n用于删除地图上的 GroundStamp / RoadLine / GroundSpline 对象。", "删除", "取消");
        if (!ok)
            return;
        Undo.DestroyObjectImmediate(go);
        PlayEditorSound(DeleteSoundPath);
        RefreshPlacedCache();
        Repaint();
    }

    private void RefreshSurfaceMaterials()
    {
        string selectedPath = selectedSurfaceMaterial != null ? AssetDatabase.GetAssetPath(selectedSurfaceMaterial) : null;

        surfaceMaterials.Clear();
        foreach (string guid in AssetDatabase.FindAssets(GroundSurfaceMaterialSearchFilter))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GroundSurfaceMaterialDefinition def = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
            if (def != null)
                surfaceMaterials.Add(def);
        }

        surfaceMaterials.Sort((a, b) => string.Compare(GetDisplayName(a), GetDisplayName(b), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(selectedPath))
        {
            GroundSurfaceMaterialDefinition matched = surfaceMaterials.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
            {
                selectedSurfaceMaterial = matched;
                return;
            }
        }

        if (selectedSurfaceMaterial == null || !surfaceMaterials.Contains(selectedSurfaceMaterial))
            selectedSurfaceMaterial = surfaceMaterials.Count > 0 ? surfaceMaterials[0] : null;
    }

    private List<GroundSurfaceMaterialDefinition> GetFilteredSurfaceMaterials()
    {
        List<string> categories = GetSurfaceMaterialCategoryOptions();
        string selectedCategory = categories[Mathf.Clamp(surfaceMaterialCategoryIndex, 0, Mathf.Max(0, categories.Count - 1))];
        string s = string.IsNullOrWhiteSpace(surfaceMaterialSearch) ? "" : surfaceMaterialSearch.Trim().ToLowerInvariant();
        return surfaceMaterials.Where(def =>
        {
            if (def == null)
                return false;
            if (!string.IsNullOrEmpty(s)
                && !GetDisplayName(def).ToLowerInvariant().Contains(s)
                && !(def.surfaceId ?? "").ToLowerInvariant().Contains(s)
                && !(def.category ?? "").ToLowerInvariant().Contains(s))
                return false;
            if (selectedCategory != "全部" && def.category != selectedCategory)
                return false;
            return true;
        }).ToList();
    }

    private List<string> GetSurfaceMaterialCategoryOptions()
    {
        List<string> result = new List<string> { "全部" };
        result.AddRange(surfaceMaterials.Where(x => x != null && !string.IsNullOrWhiteSpace(x.category)).Select(x => x.category).Distinct().OrderBy(x => x));
        return result;
    }

    private string GetDisplayName(GroundSurfaceMaterialDefinition definition)
    {
        if (definition == null)
            return "-";
        if (!string.IsNullOrWhiteSpace(definition.displayName))
            return definition.displayName;
        if (!string.IsNullOrWhiteSpace(definition.surfaceId))
            return definition.surfaceId;
        return definition.name;
    }


    private bool IsSurfaceMaterialStamp(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return false;

        string mode = material.textureDistributionMode.ToString();
        if (mode == "StampDecal")
            return true;

        string category = material.category ?? string.Empty;
        string displayName = GetDisplayName(material) ?? string.Empty;
        string id = material.surfaceId ?? string.Empty;
        string idLower = id.ToLowerInvariant();

        return category.Contains("印章")
            || category.Contains("贴花")
            || displayName.Contains("印章")
            || displayName.Contains("贴花")
            || idLower.Contains("stamp");
    }

    private bool IsSurfaceMaterialSpline(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return false;

        string mode = material.textureDistributionMode.ToString();
        if (mode == "SplinePattern")
            return true;

        string category = material.category ?? string.Empty;
        string displayName = GetDisplayName(material) ?? string.Empty;
        string id = material.surfaceId ?? string.Empty;
        string idLower = id.ToLowerInvariant();

        return category.Contains("样条")
            || category.Contains("画线")
            || displayName.Contains("样条")
            || displayName.Contains("画线")
            || displayName.Contains("路线")
            || idLower.Contains("spline")
            || idLower.Contains("roadline");
    }

    private Texture GetSurfaceMaterialPreview(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return null;

        // 地表材质列表的缩略图必须跟“素材用途”一致。
        // Terrain 地表看 baseTexture；印章看 stampTexture；样条 / RoadLine 看 splineTexture。
        // 之前这里只读 baseTexture，所以 RoadLine / Stamp 资源会显示成灰块。
        if (material.previewIcon != null)
            return material.previewIcon.texture;

        bool spline = IsSurfaceMaterialSpline(material);
        bool stamp = IsSurfaceMaterialStamp(material);

        if (spline && material.splineTexture != null)
            return material.splineTexture;

        if (stamp && material.stampTexture != null)
            return material.stampTexture;

        // 兼容误建 / 旧资产：只要有专用 Overlay 贴图，就优先拿来当缩略图。
        if (material.splineTexture != null)
            return material.splineTexture;
        if (material.stampTexture != null)
            return material.stampTexture;

        if (material.baseTexture != null)
            return material.baseTexture;

        if (material.baseMaterial != null)
        {
            if (material.baseMaterial.HasProperty("_BaseColorMap"))
            {
                Texture t = material.baseMaterial.GetTexture("_BaseColorMap");
                if (t != null) return t;
            }
            if (material.baseMaterial.HasProperty("_UnlitColorMap"))
            {
                Texture t = material.baseMaterial.GetTexture("_UnlitColorMap");
                if (t != null) return t;
            }
            if (material.baseMaterial.HasProperty("_BaseMap"))
            {
                Texture t = material.baseMaterial.GetTexture("_BaseMap");
                if (t != null) return t;
            }
            if (material.baseMaterial.HasProperty("_MainTex"))
            {
                Texture t = material.baseMaterial.GetTexture("_MainTex");
                if (t != null) return t;
            }
            return AssetPreview.GetAssetPreview(material.baseMaterial) ?? AssetPreview.GetMiniThumbnail(material.baseMaterial);
        }

        return null;
    }

    private string GetCurrentSelectionLabel()
    {
        switch (currentKind)
        {
            case PlacementObjectKind.TerrainDecoration:
                return selectedDefinition != null ? GetDisplayName(selectedDefinition) : "未选择";
            case PlacementObjectKind.GroundSurfaceMaterial:
                return selectedSurfaceMaterial != null ? GetDisplayName(selectedSurfaceMaterial) : "未选择";
            default:
                return "未选择";
        }
    }

    private List<TerrainDecorationDefinition> GetFilteredDefinitions()
    {
        string selectedCategory = categoryLabels[Mathf.Clamp(categoryIndex, 0, categoryLabels.Length - 1)];
        List<string> subOptions = GetSubCategoryOptions();
        string selectedSub = subOptions[Mathf.Clamp(subCategoryIndex, 0, Mathf.Max(0, subOptions.Count - 1))];
        string s = string.IsNullOrWhiteSpace(search) ? "" : search.Trim().ToLowerInvariant();
        return definitions.Where(def =>
        {
            if (def == null)
                return false;
            if (!string.IsNullOrEmpty(s)
                && !GetDisplayName(def).ToLowerInvariant().Contains(s)
                && !(def.decorationId ?? "").ToLowerInvariant().Contains(s)
                && !(def.subCategory ?? "").ToLowerInvariant().Contains(s))
                return false;
            if (selectedCategory != "全部" && GetCategoryLabel(def) != selectedCategory)
                return false;
            if (selectedSub != "全部" && def.subCategory != selectedSub)
                return false;
            return true;
        }).ToList();
    }

    private List<string> GetSubCategoryOptions()
    {
        List<string> result = new List<string> { "全部" };
        result.AddRange(definitions.Where(x => x != null && !string.IsNullOrWhiteSpace(x.subCategory)).Select(x => x.subCategory).Distinct().OrderBy(x => x));
        return result;
    }

    private string GetDisplayName(TerrainDecorationDefinition definition)
    {
        if (definition == null)
            return "-";
        if (!string.IsNullOrWhiteSpace(definition.displayName))
            return definition.displayName;
        if (!string.IsNullOrWhiteSpace(definition.decorationId))
            return definition.decorationId;
        return definition.name;
    }

    private string GetCategoryLabel(TerrainDecorationDefinition definition) => definition == null ? "其他" : GetCategoryLabel(definition.category);

    private string GetCategoryLabel(TerrainDecorationCategory category)
    {
        switch (category)
        {
            case TerrainDecorationCategory.Prop: return "普通";
            case TerrainDecorationCategory.Box: return "箱体";
            case TerrainDecorationCategory.Wall: return "墙体";
            case TerrainDecorationCategory.Pillar: return "柱体";
            case TerrainDecorationCategory.FloorAttachment: return "地面装饰";
            case TerrainDecorationCategory.Moss: return "苔藓";
            case TerrainDecorationCategory.Ruin: return "残骸";
            case TerrainDecorationCategory.Pipe: return "管线";
            case TerrainDecorationCategory.Occluder: return "遮挡体";
            case TerrainDecorationCategory.Mechanism: return "机关";
            case TerrainDecorationCategory.Custom: return "自定义";
            default: return "其他";
        }
    }

    private string GetKindLabel(PlacementObjectKind kind)
    {
        ToolBookmark bookmark = ToolBookmarkRegistry.FirstOrDefault(x => x.Kind == kind);
        return bookmark != null ? bookmark.Label : "未知";
    }

    private Texture GetDefinitionPreview(TerrainDecorationDefinition definition)
    {
        if (definition == null)
            return null;
        if (definition.icon != null)
            return definition.icon.texture;
        TerrainDecorationVariant variant = definition.GetFirstVariant();
        if (variant != null)
        {
            if (variant.previewIcon != null)
                return variant.previewIcon.texture;
            if (variant.prefab != null)
                return AssetPreview.GetAssetPreview(variant.prefab) ?? AssetPreview.GetMiniThumbnail(variant.prefab);
        }
        return null;
    }

    private bool CanOpenCurrentKindEditor()
    {
        switch (currentKind)
        {
            case PlacementObjectKind.TerrainDecoration:
                return selectedDefinition != null;
            case PlacementObjectKind.GroundSurfaceMaterial:
                return selectedSurfaceMaterial != null;
            case PlacementObjectKind.Unit:
                return true;
            case PlacementObjectKind.Item:
                return true;
            default:
                return false;
        }
    }

    private void OpenCurrentKindEditor()
    {
        switch (currentKind)
        {
            case PlacementObjectKind.TerrainDecoration:
                if (selectedDefinition != null)
                    SkyPrisonEditorWindow.OpenWindowWithTab("地形装饰物", selectedDefinition);
                break;

            case PlacementObjectKind.GroundSurfaceMaterial:
                SkyPrisonEditorWindow.OpenWindowWithTab("地表材质", selectedSurfaceMaterial);
                break;

            case PlacementObjectKind.Unit:
                SkyPrisonEditorWindow.OpenWindowWithTab("单位定义", selectedUnitDefinition);
                break;

            case PlacementObjectKind.Item:
                SkyPrisonEditorWindow.OpenWindowWithTab("物品库", null);
                break;

            case PlacementObjectKind.Trigger:
                SkyPrisonEditorWindow.OpenWindowWithTab("触发器", null);
                break;

            case PlacementObjectKind.Effect:
                SkyPrisonEditorWindow.OpenWindowWithTab("特效", null);
                break;

            case PlacementObjectKind.AudioArea:
                SkyPrisonEditorWindow.OpenWindowWithTab("音声合成", null);
                break;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  ITEM PLACEMENT
    // ════════════════════════════════════════════════════════════

    private void LoadItemDefinitions()
    {
        itemDefinitions.Clear();
        string[] guids = AssetDatabase.FindAssets(ItemDefinitionSearchFilter);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (def != null)
                itemDefinitions.Add(def);
        }
        itemDefinitions.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
    }

    private List<ItemDefinition> GetFilteredItemDefinitions()
    {
        string q = itemSearch.Trim().ToLower();
        if (string.IsNullOrEmpty(q)) return itemDefinitions;
        List<ItemDefinition> result = new List<ItemDefinition>();
        foreach (ItemDefinition d in itemDefinitions)
            if (d.displayName.ToLower().Contains(q) || d.name.ToLower().Contains(q) || d.itemKey.ToLower().Contains(q))
                result.Add(d);
        return result;
    }

    private void DrawItemPlacePage()
    {
        if (itemDefinitions.Count == 0)
            LoadItemDefinitions();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("筛选", EditorStyles.boldLabel);
        itemSearch = EditorGUILayout.TextField("搜索", itemSearch);
        EditorGUILayout.EndVertical();

        List<ItemDefinition> filtered = GetFilteredItemDefinitions();
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, ModuleListFixedHeight, ModuleListFixedHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * 58f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        itemListScroll = GUI.BeginScrollView(viewRect, itemListScroll, contentRect, false, true);
        float y = 0f;
        foreach (ItemDefinition def in filtered)
        {
            Rect row = new Rect(0f, y, contentRect.width, 54f);
            DrawItemDefinitionRow(row, def);
            y += 58f;
        }
        GUI.EndScrollView();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("放置设置", EditorStyles.boldLabel);
        if (selectedItemDefinition == null)
        {
            EditorGUILayout.HelpBox("请从上方列表选择一个道具定义。", MessageType.Info);
        }
        else
        {
            ItemDefinition u = selectedItemDefinition;
            EditorGUILayout.LabelField("名称", u.displayName);
            EditorGUILayout.LabelField("分类", u.category.ToString());
            EditorGUILayout.LabelField("Key", u.itemKey);
            EditorGUILayout.LabelField("父节点", ItemParentPath);
            EditorGUILayout.HelpBox("道具以全息掉落物形式放置到场景，运行时可拾取。", MessageType.None);
        }
        EditorGUILayout.Space(4f);
        DrawPlacementModeLargeButton();
        EditorGUILayout.EndVertical();
    }

    private void DrawItemDefinitionRow(Rect rect, ItemDefinition def)
    {
        bool selected = selectedItemDefinition == def;
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.14f, 0.10f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, 42f, 42f);
        Texture icon = def.icon != null ? def.icon.texture : null;
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.20f, 0.20f, 0.22f, 1f));

        Rect textRect = new Rect(iconRect.xMax + 8f, rect.y + 6f, rect.width - iconRect.width - 22f, 42f);
        GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 20f), def.displayName, EditorStyles.boldLabel);
        GUI.Label(new Rect(textRect.x, textRect.y + 20f, textRect.width, 18f),
            $"{def.category}  |  {def.itemKey}", EditorStyles.miniLabel);

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            selectedItemDefinition = def;
            if (placementMode && currentKind == PlacementObjectKind.Item)
                SetPlacementMode(false);
            Repaint();
        }
    }

    private void DrawItemPlacedPage()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("已摆放道具", EditorStyles.boldLabel);
        if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            RefreshPlacedItemCache();
        EditorGUILayout.EndHorizontal();
        placedItemSearch = EditorGUILayout.TextField("搜索", placedItemSearch);
        EditorGUILayout.HelpBox("点击定位，Delete 删除。支持 Ctrl 多选。", MessageType.Info);
        EditorGUILayout.EndVertical();

        List<LootDropWorldObject> filtered = GetFilteredPlacedItems();
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, 220f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * 58f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        placedItemScroll = GUI.BeginScrollView(viewRect, placedItemScroll, contentRect, false, true);
        float y = 0f;
        for (int i = 0; i < filtered.Count; i++)
        {
            Rect row = new Rect(0f, y, contentRect.width, 54f);
            DrawPlacedItemRow(row, filtered[i], i);
            y += 58f;
        }
        GUI.EndScrollView();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        int selCount = placedItemSelectionIds.Count;
        using (new EditorGUI.DisabledScope(selCount <= 0))
        {
            if (GUILayout.Button($"删除已选({selCount})", EditorStyles.toolbarButton, GUILayout.Width(100f)))
            {
                if (EditorUtility.DisplayDialog("删除确认", $"删除 {selCount} 个已选道具？", "删除", "取消"))
                {
                    List<LootDropWorldObject> toDelete = new List<LootDropWorldObject>();
                    foreach (LootDropWorldObject d in placedItemCache)
                        if (d != null && placedItemSelectionIds.Contains(d.gameObject.GetInstanceID()))
                            toDelete.Add(d);
                    foreach (LootDropWorldObject d in toDelete)
                        if (d != null && d.gameObject != null)
                            Undo.DestroyObjectImmediate(d.gameObject);
                    placedItemSelectionIds.Clear();
                    RefreshPlacedItemCache();
                }
            }
            if (GUILayout.Button("清空选择", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                placedItemSelectionIds.Clear();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPlacedItemRow(Rect rect, LootDropWorldObject drop, int index)
    {
        if (drop == null || drop.gameObject == null) return;
        int id = drop.gameObject.GetInstanceID();
        bool selected = placedItemSelectionIds.Contains(id);
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.14f, 0.10f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        ItemDefinition def = drop.Item;
        Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, 42f, 42f);
        Texture icon = (def != null && def.icon != null) ? def.icon.texture : null;
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.20f, 0.20f, 0.22f, 1f));

        string itemName = def != null ? def.displayName : drop.gameObject.name;
        float textX = iconRect.xMax + 8f;
        float textW = rect.width - iconRect.width - 110f;
        GUI.Label(new Rect(textX, rect.y + 6f, textW, 20f), itemName, EditorStyles.boldLabel);
        GUI.Label(new Rect(textX, rect.y + 26f, textW, 14f),
            $"x{drop.Count}  {drop.transform.position:F1}{(def != null ? "  " + def.category : "")}", EditorStyles.miniLabel);

        Rect btnRect = new Rect(rect.xMax - 94f, rect.y + 16f, 42f, 22f);
        Rect delRect = new Rect(rect.xMax - 48f, rect.y + 16f, 42f, 22f);
        if (GUI.Button(btnRect, "定位"))
        {
            Selection.activeGameObject = drop.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        if (GUI.Button(delRect, "删除"))
        {
            Undo.DestroyObjectImmediate(drop.gameObject);
            RefreshPlacedItemCache();
            return;
        }

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            bool ctrl = Event.current.control || Event.current.command;
            if (ctrl)
            {
                if (selected) placedItemSelectionIds.Remove(id);
                else placedItemSelectionIds.Add(id);
            }
            else
            {
                placedItemSelectionIds.Clear();
                placedItemSelectionIds.Add(id);
                Selection.activeGameObject = drop.gameObject;
            }
            Repaint();
            Event.current.Use();
        }
    }

    private List<LootDropWorldObject> GetFilteredPlacedItems()
    {
        string q = placedItemSearch.Trim().ToLower();
        if (string.IsNullOrEmpty(q)) return placedItemCache;
        List<LootDropWorldObject> result = new List<LootDropWorldObject>();
        foreach (LootDropWorldObject d in placedItemCache)
        {
            if (d == null) continue;
            string n = d.Item != null ? d.Item.displayName.ToLower() : d.gameObject.name.ToLower();
            if (n.Contains(q)) result.Add(d);
        }
        return result;
    }

    private void RefreshPlacedItemCache()
    {
        placedItemCache.Clear();
        LootDropWorldObject[] drops = FindObjectsOfType<LootDropWorldObject>(true);
        foreach (LootDropWorldObject d in drops)
        {
            if (d == null || d.gameObject == null) continue;
            if (!d.gameObject.scene.IsValid()) continue;
            placedItemCache.Add(d);
        }
        placedItemCache.Sort((a, b) => string.Compare(
            a.Item != null ? a.Item.displayName : a.gameObject.name,
            b.Item != null ? b.Item.displayName : b.gameObject.name,
            StringComparison.OrdinalIgnoreCase));
        placedItemSelectionIds.RemoveWhere(id => !placedItemCache.Exists(x => x != null && x.gameObject.GetInstanceID() == id));
    }

    private void RebuildItemPreview()
    {
        DestroyItemPreview();
        if (selectedItemDefinition == null) return;

        LootDropModelLibrary lib = LootDropModelLibrary.Instance;
        if (lib == null) return;

        GameObject modelPrefab = null;
        Mesh meshOverride = null;
        float scaleOverride = 1f;
        Vector3 rotOffset = Vector3.zero;

        ItemDefinition d = selectedItemDefinition;
        if (d.category == ItemCategory.Weapon || d.category == ItemCategory.Armor || d.category == ItemCategory.Accessory)
        {
            EquipmentSlotType slot = d.category == ItemCategory.Weapon ? EquipmentSlotType.Weapon : EquipmentSlotType.UpperBody;
            modelPrefab = lib.GetModelForEquipment(slot, out scaleOverride, out rotOffset, out meshOverride);
            if (modelPrefab == null && meshOverride == null)
                modelPrefab = lib.GetModelForGeneral(d.category, out scaleOverride, out rotOffset, out meshOverride);
        }
        else if (d.category == ItemCategory.Material)
        {
            modelPrefab = lib.GetModelForMaterial(MaterialSubCategory.Part, out scaleOverride, out rotOffset, out meshOverride);
            if (modelPrefab == null && meshOverride == null)
                modelPrefab = lib.GetModelForGeneral(d.category, out scaleOverride, out rotOffset, out meshOverride);
        }
        else
        {
            modelPrefab = lib.GetModelForGeneral(d.category, out scaleOverride, out rotOffset, out meshOverride);
        }

        if (modelPrefab == null && meshOverride == null) return;

        itemPreviewInstance = new GameObject("__ItemPlacementPreview__");
        itemPreviewInstance.hideFlags = HideFlags.HideAndDontSave;
        float finalScale = lib.modelSizeMultiplier * scaleOverride;

        GameObject visual;
        if (modelPrefab != null)
        {
            visual = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(modelPrefab);
            if (visual == null) visual = UnityEngine.Object.Instantiate(modelPrefab);
        }
        else
        {
            visual = new GameObject("MeshPreview");
            MeshFilter mf = visual.AddComponent<MeshFilter>();
            mf.sharedMesh = meshOverride;
            visual.AddComponent<MeshRenderer>();
        }
        visual.hideFlags = HideFlags.HideAndDontSave;
        visual.transform.SetParent(itemPreviewInstance.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(rotOffset);
        visual.transform.localScale = Vector3.one * finalScale;

        itemPreviewRenderers.Clear();
        foreach (Collider c in itemPreviewInstance.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
        foreach (Renderer r in itemPreviewInstance.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            itemPreviewRenderers.Add(r);
        }
        ApplyItemPreviewMaterial(itemPreviewCanPlace);
    }

    private void ApplyItemPreviewMaterial(bool valid)
    {
        Material mat = GetPreviewMaterial(valid);
        foreach (Renderer r in itemPreviewRenderers)
        {
            if (r == null) continue;
            Material[] mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int j = 0; j < mats.Length; j++) mats[j] = mat;
            r.sharedMaterials = mats;
        }
    }

    private void DestroyItemPreview()
    {
        if (itemPreviewInstance != null)
        {
            DestroyImmediate(itemPreviewInstance);
            itemPreviewInstance = null;
        }
        itemPreviewRenderers.Clear();
    }

    private void OnItemPlacementSceneGUI(SceneView sceneView)
    {
        if (selectedItemDefinition == null) return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { SetPlacementMode(false); e.Use(); return; }
        if (e.type == EventType.MouseDown && e.button == 1) { SetPlacementMode(false); e.Use(); return; }

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Vector3 hitPos = Vector3.zero;
        bool hasHit = false;

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            hitPos = hit.point;
            hasHit = true;
        }
        else
        {
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter)) { hitPos = ray.GetPoint(enter); hasHit = true; }
        }

        if (hasHit)
        {
            if (itemPreviewInstance == null)
                RebuildItemPreview();

            if (itemPreviewInstance != null)
                itemPreviewInstance.transform.position = hitPos;

            Handles.color = ValidPreviewColor;
            Handles.DrawWireDisc(hitPos, Vector3.up, 0.28f);
            Handles.Label(hitPos + Vector3.up * 0.6f, selectedItemDefinition.displayName);
            sceneView.Repaint();
        }
        else
        {
            DestroyItemPreview();
        }

        if (e.type == EventType.MouseDown && e.button == 0 && hasHit)
        {
            PlaceItemAtPosition(hitPos);
            e.Use();
        }
    }

    private void PlaceItemAtPosition(Vector3 position)
    {
        if (selectedItemDefinition == null) return;
        Transform parent = GetOrCreateParent(ItemParentPath);

        GameObject go = new GameObject($"Drop_{selectedItemDefinition.displayName}_x1");
        Undo.RegisterCreatedObjectUndo(go, "Place Item");
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        LootDropWorldObject drop = go.AddComponent<LootDropWorldObject>();
        drop.SetLoot(selectedItemDefinition, 1);
        go.AddComponent<LootDropVisual>();

        // 编辑器下 Start() 不运行，手动实例化模型让场景视图可见
        BuildItemEditorVisual(go, selectedItemDefinition);

        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        PlayEditorSound(PlaceSoundPath);
        RefreshPlacedItemCache();
        Repaint();
    }

    private static void BuildItemEditorVisual(GameObject root, ItemDefinition item)
    {
        LootDropModelLibrary lib = LootDropModelLibrary.Instance;
        if (lib == null || item == null) return;

        GameObject modelPrefab;
        Mesh meshOverride;
        float scaleOverride;
        Vector3 rotOffset;

        if (item.IsEquipmentItem)
            modelPrefab = lib.GetModelForEquipment(item.equipment.slot, out scaleOverride, out rotOffset, out meshOverride);
        else if (item.category == ItemCategory.Material)
            modelPrefab = lib.GetModelForMaterial(item.materialSubCategory, out scaleOverride, out rotOffset, out meshOverride);
        else
            modelPrefab = lib.GetModelForGeneral(item.category, out scaleOverride, out rotOffset, out meshOverride);

        if (modelPrefab == null && meshOverride == null) return;

        // ModelRoot — 与运行时 LootDropVisual 层级保持一致，运行时 Start() 会重建不冲突
        GameObject modelRoot = new GameObject("ModelRoot_EditorPreview");
        modelRoot.transform.SetParent(root.transform, false);
        modelRoot.transform.localPosition = Vector3.zero;

        GameObject inst;
        if (modelPrefab != null)
            inst = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, modelRoot.transform);
        else
        {
            inst = new GameObject("MeshModel");
            inst.transform.SetParent(modelRoot.transform, false);
            inst.AddComponent<MeshFilter>().sharedMesh = meshOverride;
            inst.AddComponent<MeshRenderer>();
        }

        float modelScale = 104f;
        float sizeMultiplier = (lib.modelSizeMultiplier > 0f ? lib.modelSizeMultiplier : 1f)
                             * (scaleOverride > 0f ? scaleOverride : 1f);
        bool hasRotOffset = rotOffset != Vector3.zero;
        inst.transform.localPosition = new Vector3(0f, 0.732f, 0f);
        inst.transform.localRotation = Quaternion.Euler(hasRotOffset ? rotOffset : new Vector3(-90f, 0f, 0f));
        inst.transform.localScale    = Vector3.one * (modelScale * sizeMultiplier);

        // 套全息材质
        if (lib.hologramMaterial != null)
        {
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = lib.hologramMaterial;
                r.sharedMaterials = mats;
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  UNIT PLACEMENT
    // ════════════════════════════════════════════════════════════

    private void LoadUnitDefinitions()
    {
        unitDefinitions.Clear();
        string[] guids = AssetDatabase.FindAssets(UnitDefinitionSearchFilter);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UnitDefinition def = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);
            if (def != null)
                unitDefinitions.Add(def);
        }
        unitDefinitions.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
    }

    private List<UnitDefinition> GetFilteredUnitDefinitions()
    {
        string q = unitSearch.Trim().ToLower();
        if (string.IsNullOrEmpty(q))
            return unitDefinitions;
        List<UnitDefinition> result = new List<UnitDefinition>();
        foreach (UnitDefinition d in unitDefinitions)
        {
            if (d.displayName.ToLower().Contains(q) || d.name.ToLower().Contains(q))
                result.Add(d);
        }
        return result;
    }

    private void DrawUnitPlacePage()
    {
        if (unitDefinitions.Count == 0)
            LoadUnitDefinitions();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("筛选", EditorStyles.boldLabel);
        unitSearch = EditorGUILayout.TextField("搜索", unitSearch);
        EditorGUILayout.EndVertical();

        List<UnitDefinition> filtered = GetFilteredUnitDefinitions();
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, ModuleListFixedHeight, ModuleListFixedHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * 58f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        unitListScroll = GUI.BeginScrollView(viewRect, unitListScroll, contentRect, false, true);
        float y = 0f;
        foreach (UnitDefinition def in filtered)
        {
            Rect row = new Rect(0f, y, contentRect.width, 54f);
            DrawUnitDefinitionRow(row, def);
            y += 58f;
        }
        GUI.EndScrollView();

        // 放置设置
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("放置设置", EditorStyles.boldLabel);
        if (selectedUnitDefinition == null)
        {
            EditorGUILayout.HelpBox("请从上方列表选择一个单位定义。", MessageType.Info);
        }
        else
        {
            UnitDefinition u = selectedUnitDefinition;
            EditorGUILayout.LabelField("名称", u.displayName);
            EditorGUILayout.LabelField("类型", $"{u.defineType} / {u.characterIdentity}");
            EditorGUILayout.LabelField("Prefab", u.prefab != null ? u.prefab.name : "⚠ 未配置");
            EditorGUILayout.LabelField("父节点", UnitParentPath);
            unitPlacementFaction = (UnitPlacementFaction)EditorGUILayout.EnumPopup("放置阵营", unitPlacementFaction);
            if (unitPlacementFaction != UnitPlacementFaction.FollowDefinition)
                EditorGUILayout.HelpBox($"放置后将覆盖运行时身份为「{unitPlacementFaction}」，单位定义本身不受影响。", MessageType.Info);
            if (u.prefab == null)
                EditorGUILayout.HelpBox("该单位定义没有配置 Prefab，无法放置。", MessageType.Warning);
        }
        EditorGUILayout.Space(4f);
        DrawPlacementModeLargeButton();
        EditorGUILayout.EndVertical();
    }

    private void DrawUnitDefinitionRow(Rect rect, UnitDefinition def)
    {
        bool selected = selectedUnitDefinition == def;
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.14f, 0.10f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, 42f, 42f);
        Texture icon = def.prefab != null ? AssetPreview.GetAssetPreview(def.prefab) : null;
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.20f, 0.20f, 0.22f, 1f));

        Rect textRect = new Rect(iconRect.xMax + 8f, rect.y + 6f, rect.width - iconRect.width - 22f, 42f);
        GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 20f), def.displayName, EditorStyles.boldLabel);
        GUI.Label(new Rect(textRect.x, textRect.y + 20f, textRect.width, 18f),
            $"{def.defineType} / {def.characterIdentity}  |  {(def.prefab != null ? def.prefab.name : "⚠ 无 Prefab")}", EditorStyles.miniLabel);

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            selectedUnitDefinition = def;
            if (placementMode && currentKind == PlacementObjectKind.Unit)
                SetPlacementMode(false);
            Repaint();
        }
    }

    private void DrawUnitPlacedPage()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("已摆放单位", EditorStyles.boldLabel);
        if (GUILayout.Button("刷新", GUILayout.Width(60f)))
            RefreshPlacedUnitCache();
        EditorGUILayout.EndHorizontal();
        placedUnitSearch = EditorGUILayout.TextField("搜索", placedUnitSearch);
        EditorGUILayout.HelpBox("点击定位，Delete 删除。支持 Ctrl 多选。", MessageType.Info);
        EditorGUILayout.EndVertical();

        List<UnitDefinitionRuntimeBinder> filtered = GetFilteredPlacedUnits();
        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, 220f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, panelBg);
        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * 58f + 8f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        placedUnitScroll = GUI.BeginScrollView(viewRect, placedUnitScroll, contentRect, false, true);
        float y = 0f;
        for (int i = 0; i < filtered.Count; i++)
        {
            Rect row = new Rect(0f, y, contentRect.width, 54f);
            DrawPlacedUnitRow(row, filtered[i], i, filtered);
            y += 58f;
        }
        GUI.EndScrollView();

        // 底部工具栏
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        int selCount = placedUnitSelectionIds.Count;
        using (new EditorGUI.DisabledScope(selCount <= 0))
        {
            if (GUILayout.Button($"删除已选({selCount})", EditorStyles.toolbarButton, GUILayout.Width(100f)))
            {
                if (EditorUtility.DisplayDialog("删除确认", $"删除 {selCount} 个已选单位？", "删除", "取消"))
                {
                    List<UnitDefinitionRuntimeBinder> toDelete = new List<UnitDefinitionRuntimeBinder>();
                    foreach (UnitDefinitionRuntimeBinder b in placedUnitCache)
                        if (placedUnitSelectionIds.Contains(b.gameObject.GetInstanceID()))
                            toDelete.Add(b);
                    foreach (UnitDefinitionRuntimeBinder b in toDelete)
                        if (b != null && b.gameObject != null)
                            Undo.DestroyObjectImmediate(b.gameObject);
                    placedUnitSelectionIds.Clear();
                    RefreshPlacedUnitCache();
                }
            }
            if (GUILayout.Button("清空选择", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                placedUnitSelectionIds.Clear();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPlacedUnitRow(Rect rect, UnitDefinitionRuntimeBinder binder, int index, List<UnitDefinitionRuntimeBinder> list)
    {
        if (binder == null || binder.gameObject == null) return;
        int id = binder.gameObject.GetInstanceID();
        bool selected = placedUnitSelectionIds.Contains(id);
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.14f, 0.10f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        UnitDefinition def = binder.UnitDefinitionAsset;
        string defName = def != null ? def.displayName : "(无定义)";

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, 42f, 42f);
        Texture icon = (def != null && def.prefab != null) ? AssetPreview.GetAssetPreview(def.prefab) : null;
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(0.20f, 0.20f, 0.22f, 1f));

        float textX = iconRect.xMax + 8f;
        float textW = rect.width - iconRect.width - 110f;
        Rect nameRect = new Rect(textX, rect.y + 6f,  textW, 20f);
        Rect subRect  = new Rect(textX, rect.y + 26f, textW, 14f);
        Rect btnRect  = new Rect(rect.xMax - 94f, rect.y + 16f, 42f, 22f);
        Rect delRect  = new Rect(rect.xMax - 48f, rect.y + 16f, 42f, 22f);
        GUI.Label(nameRect, binder.gameObject.name, EditorStyles.boldLabel);
        string typeInfo = def != null ? $"{def.defineType} / {def.characterIdentity}" : "";
        GUI.Label(subRect, $"{defName}  {binder.transform.position:F1}  {typeInfo}", EditorStyles.miniLabel);

        if (GUI.Button(btnRect, "定位"))
        {
            Selection.activeGameObject = binder.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        if (GUI.Button(delRect, "删除"))
        {
            Undo.DestroyObjectImmediate(binder.gameObject);
            RefreshPlacedUnitCache();
            return;
        }

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            bool ctrl = Event.current.control || Event.current.command;
            if (ctrl)
            {
                if (selected) placedUnitSelectionIds.Remove(id);
                else placedUnitSelectionIds.Add(id);
            }
            else
            {
                placedUnitSelectionIds.Clear();
                placedUnitSelectionIds.Add(id);
                Selection.activeGameObject = binder.gameObject;
            }
            placedUnitLastClickedIndex = index;
            Repaint();
            Event.current.Use();
        }
    }

    private List<UnitDefinitionRuntimeBinder> GetFilteredPlacedUnits()
    {
        string q = placedUnitSearch.Trim().ToLower();
        if (string.IsNullOrEmpty(q)) return placedUnitCache;
        List<UnitDefinitionRuntimeBinder> result = new List<UnitDefinitionRuntimeBinder>();
        foreach (UnitDefinitionRuntimeBinder b in placedUnitCache)
        {
            if (b == null) continue;
            string defName = b.UnitDefinitionAsset != null ? b.UnitDefinitionAsset.displayName.ToLower() : "";
            if (b.gameObject.name.ToLower().Contains(q) || defName.Contains(q))
                result.Add(b);
        }
        return result;
    }

    private void OnUnitPlacementSceneGUI(SceneView sceneView)
    {
        if (selectedUnitDefinition == null || selectedUnitDefinition.prefab == null)
            return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            SetPlacementMode(false);
            e.Use();
            return;
        }
        if (e.type == EventType.MouseDown && e.button == 1)
        {
            SetPlacementMode(false);
            e.Use();
            return;
        }

        // 射线 Y 平面
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        Vector3 hitPos = Vector3.zero;
        bool hasHit = false;

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            hitPos = hit.point;
            hasHit = true;
        }
        else if (groundPlane.Raycast(ray, out float enter))
        {
            hitPos = ray.GetPoint(enter);
            hasHit = true;
        }

        if (hasHit)
        {
            Handles.color = new Color(0.3f, 1f, 0.3f, 0.9f);
            Handles.DrawWireDisc(hitPos, Vector3.up, 0.35f);
            Handles.Label(hitPos + Vector3.up * 0.6f, selectedUnitDefinition.displayName);
            sceneView.Repaint();
        }

        if (e.type == EventType.MouseDown && e.button == 0 && hasHit)
        {
            PlaceUnitAtPosition(hitPos);
            e.Use();
        }
    }

    private void PlaceUnitAtPosition(Vector3 position)
    {
        if (selectedUnitDefinition == null || selectedUnitDefinition.prefab == null) return;

        Transform parent = GetOrCreateParent(UnitParentPath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(selectedUnitDefinition.prefab, parent);
        if (instance == null) return;

        instance.name = selectedUnitDefinition.prefab.name;
        instance.transform.position = position;
        Undo.RegisterCreatedObjectUndo(instance, "Place Unit");

        // 绑定 UnitDefinition
        UnitDefinitionRuntimeBinder binder = instance.GetComponent<UnitDefinitionRuntimeBinder>();
        if (binder == null) binder = instance.GetComponentInChildren<UnitDefinitionRuntimeBinder>(true);
        if (binder != null)
            binder.SetUnitDefinitionAsset(selectedUnitDefinition, false);

        // 阵营覆盖：写入 SkyPrisonUnitRuntimeIdentity，运行时生效
        if (unitPlacementFaction != UnitPlacementFaction.FollowDefinition)
        {
            SkyPrisonUnitRuntimeIdentity runtimeIdentity =
                instance.GetComponent<SkyPrisonUnitRuntimeIdentity>()
             ?? instance.GetComponentInChildren<SkyPrisonUnitRuntimeIdentity>(true);
            if (runtimeIdentity != null)
            {
                CharacterIdentity identity = unitPlacementFaction switch
                {
                    UnitPlacementFaction.Player        => CharacterIdentity.Player,
                    UnitPlacementFaction.Ally          => CharacterIdentity.Ally,
                    UnitPlacementFaction.Enemy         => CharacterIdentity.Enemy,
                    UnitPlacementFaction.Elite         => CharacterIdentity.Elite,
                    UnitPlacementFaction.Boss          => CharacterIdentity.Boss,
                    UnitPlacementFaction.NeutralPassive=> CharacterIdentity.NeutralPassive,
                    UnitPlacementFaction.NeutralHostile=> CharacterIdentity.NeutralHostile,
                    UnitPlacementFaction.Creature      => CharacterIdentity.Creature,
                    _                                  => selectedUnitDefinition.characterIdentity,
                };
                Undo.RecordObject(runtimeIdentity, "Place Unit");
                runtimeIdentity.SetRuntimeIdentity(identity, false);
                EditorUtility.SetDirty(runtimeIdentity);
            }
        }

        EditorUtility.SetDirty(instance);
        PlayEditorSound(PlaceSoundPath);
        RefreshPlacedUnitCache();
        Repaint();
    }

    private void PlayEditorSound(string assetPath)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        if (clip == null)
            return;

        Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtilType == null)
            return;

        MethodInfo playMethod = audioUtilType.GetMethod(
            "PlayPreviewClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null);

        if (playMethod != null)
        {
            playMethod.Invoke(null, new object[] { clip, 0, false });
            return;
        }

        playMethod = audioUtilType.GetMethod(
            "PlayClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { typeof(AudioClip) },
            null);

        if (playMethod != null)
            playMethod.Invoke(null, new object[] { clip });
    }


    private static GroundSurfaceMaterialDefinition ReloadGroundSurfaceMaterialAsset(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return null;

        string path = AssetDatabase.GetAssetPath(material);
        if (string.IsNullOrEmpty(path))
            return material;

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        GroundSurfaceMaterialDefinition reloaded = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
        return reloaded != null ? reloaded : material;
    }


    private static void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private class TerrainDecorationPlacementResult
    {
        public TerrainDecorationVariant variant;
        public int seed;
        public Vector3 finalScale = Vector3.one;
        public Vector3 visualLocalEuler = Vector3.zero;
        public List<MaterialChoice> materialChoices = new List<MaterialChoice>();
    }

    private class MaterialChoice
    {
        public string slotId;
        public string rendererPath;
        public int materialIndex;
        public Material material;
    }
}


// 兼容旧调用：以后正式使用 SkyPrisonMapObjectPlacementToolWindow。
public static class SkyPrisonTerrainDecorationPlacementToolWindow
{
    public static void OpenWindow()
    {
        SkyPrisonMapObjectPlacementToolWindow.OpenWindow();
    }

    public static void OpenLegacyWindow()
    {
        SkyPrisonMapObjectPlacementToolWindow.OpenLegacyWindow();
    }

    public static void OpenWindowWithDefinition(TerrainDecorationDefinition definition)
    {
        SkyPrisonMapObjectPlacementToolWindow.OpenWindowWithDefinition(definition);
    }

    public static void OpenWindowWithDefinitionAndEnterPlacement(TerrainDecorationDefinition definition)
    {
        SkyPrisonMapObjectPlacementToolWindow.OpenWindowWithDefinitionAndEnterPlacement(definition);
    }
}
