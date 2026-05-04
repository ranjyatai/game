using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class SkyPrisonAnimationPreviewPanel
{
    private readonly SkyPrisonAnimationWorkbenchState state;

    private const string IconFolder = "Assets/_Project/Icon/Editor/";
    private static readonly Dictionary<int, Texture2D> IconCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
    private static Texture2D CircleTextureCache;
    private static Material MeshDeformTextureMaterial;
    private static Material PreviewBlendMaterial;
    private static Material PreviewCompositorMaterial;
    private static Material PreviewLayerCopyMaterial;
    private static Material PreviewLayerAlphaMaskMaterial;
    private static readonly Dictionary<Shader, Material> PreviewLayerEffectMaterials = new Dictionary<Shader, Material>();

    // 图层合成预览 Step 1：只在 PreviewPanel 内部使用的 RT 合成器。
    // 目标是让 PSB 图层合成方式开始按“下方已合成颜色 + 当前层颜色”的真实公式计算，
    // 不改检查器、不改数据模型、不改时间线、不接运行时材质。
    private RenderTexture previewBlendAccumRT;
    private RenderTexture previewBlendLayerRT;
    private RenderTexture previewBlendNextRT;
    private Rect previewBlendCanvasRect = Rect.zero;
    private bool previewBlendCompositorActive = false;
    private Vector2 previewBlendGroupOffset = Vector2.zero;

    // 镜像只属于最终可视化层：不改 PSB 坐标、不改绑定、不交换左右。
    private bool visualMirrorEnabled;
    private Vector2 visualMirrorPivot;
    private Rect currentPreviewClipRect = Rect.zero;
    private Vector2 currentPreviewWindowOffset = Vector2.zero;

    // 真视口预览：PSB 普通图层不再直接用 IMGUI 矩形 Quad 画到窗口上。
    // 先收集图层绘制命令，再统一以 Sprite Mesh 写入整张预览视口 RenderTexture。
    // 这样可以同时获得真正的视口裁切，以及避免旋转后的矩形 Sprite Quad 斜边暴露。
    private bool modelViewportCollecting = false;
    private readonly List<ModelViewportSpriteCommand> modelViewportSpriteCommands = new List<ModelViewportSpriteCommand>();
    private readonly List<ModelViewportDrawCommand> modelViewportDrawCommands = new List<ModelViewportDrawCommand>();
    private RenderTexture modelViewportRT;
    private RenderTexture modelViewportMaskRT;
    private RenderTexture modelViewportLayerRT;
    private RenderTexture modelViewportNextRT;
    private float lastModelViewportRtZoom = -999f;
    private Vector2 lastModelViewportRtPan = new Vector2(float.NaN, float.NaN);
    private bool lastModelViewportRtMirrored = false;
    private static Material ModelViewportSpriteMaterial;
    private static Material ModelViewportMaskedSpriteMaterial;
    private int currentModelViewportWidth = 1;
    private int currentModelViewportHeight = 1;

    // 曲面三角网格缓存：
    // 卡顿的关键不是 GL 画几个三角形，而是每个曲面图层、每次 Repaint 都 new List -> Add -> ToArray。
    // 网格拓扑和 UV 在 columns/rows/uv 不变时是稳定的，只需要复用；顶点位置才随拖拽实时变化。
    private readonly Dictionary<string, int[]> modelViewportMeshIndexCache = new Dictionary<string, int[]>();
    private readonly Dictionary<string, Vector2[]> modelViewportMeshUvCache = new Dictionary<string, Vector2[]>();

    // 模型视口颜色管理。
    // 不再用“整体乘亮度”修画面；那会造成暗部仍暗、亮部却被压/冲得不自然。
    // 这里保持 1:1 输出，把明暗关系交给正确的 sRGB RT 写入和 Unlit 材质路径处理。
    private const float ModelViewportOutputBrightness = 1.00f;
    private const float MeshDeformViewportBrightnessMultiplier = 1.00f;

    private struct ModelViewportDrawCommand
    {
        public bool isMesh;
        public ModelViewportSpriteCommand sprite;
        public ModelViewportMeshCommand mesh;
    }

    private struct ModelViewportSpriteCommand
    {
        public Sprite sprite;
        public Vector2 center;
        public Vector2 size;
        public float angle;
        public Color color;
        public bool mirrored;
        public bool hasMask;
        public ModelViewportMaskSpriteCommand mask;
        public string blendMode;
        public Shader layerEffectShader;
        public SkyPrisonAnimationRigRow sourceRow;
    }

    private struct ModelViewportMaskSpriteCommand
    {
        // 普通参照图层遮罩。
        public Sprite sprite;
        public Vector2 center;
        public Vector2 size;
        public float angle;
        public Color color;
        public bool mirrored;

        // 当参照图层本身带 MeshDeformer 时，遮罩不能再用未变形的 sprite。
        // 这里保存“已经被参照曲面压缩/拉伸后的遮罩网格”，写入 maskRT 时直接画这个网格。
        public bool useMeshMask;
        public Texture texture;
        public Vector2[] vertices;
        public Vector2[] uvs;
        public int[] indices;

        // 继承式蒙版变形：例如眼白被曲面压缩时，眼黑/瞳孔除了被 Alpha Mask 裁切，
        // 自己的绘制网格也要按眼白的同一套曲面场同步压缩。
        public bool inheritDeformer;
        public Vector2[,] deformerPoints;
        public int deformerColumns;
        public int deformerRows;
        public Vector2 deformerBaseCenter;
        public Vector2 deformerBaseSize;
        public float deformerBaseAngle;
    }

    private struct ModelViewportMeshCommand
    {
        public Texture texture;
        public Vector2[] vertices;
        public Vector2[] uvs;
        public int[] indices;
        public Color color;
        public bool hasMask;
        public ModelViewportMaskSpriteCommand mask;
        public string blendMode;
        public Shader layerEffectShader;
        public SkyPrisonAnimationRigRow sourceRow;
    }

    private readonly Dictionary<string, Rect> lastPsbPreviewRects = new Dictionary<string, Rect>();
    private readonly List<string> lastPsbPreviewPickOrder = new List<string>();
    private readonly List<PhysicsOscillatorDebugDrawEntry> physicsOscillatorDebugEntries = new List<PhysicsOscillatorDebugDrawEntry>();
    private readonly Dictionary<string, PhysicsRuntimeState> physicsRuntimeStates = new Dictionary<string, PhysicsRuntimeState>();
    private string hoveredPsbLayerKey = string.Empty;
    private bool creatingCustomBoneLine = false;
    private Vector2 creatingCustomBoneRootLocal = Vector2.zero;
    private Vector2 creatingCustomBoneHeadLocal = Vector2.zero;

    // 曲面变形控制点拖拽状态。
    // offset 以“未缩放的预览单位”保存，绘制时乘 PreviewZoom；这样缩放窗口不会改变变形量。
    private string draggingMeshDeformerKey = string.Empty;
    private int draggingMeshPointX = -1;
    private int draggingMeshPointY = -1;
    private Vector2 draggingMeshStartMouse = Vector2.zero;
    private Vector2 draggingMeshStartOffset = Vector2.zero;
    private string draggingMeshHandleKind = "anchor";
    private bool draggingMeshPointActive = false;

    // 曲面内部整体拖拽状态：鼠标落在真实网格 cell 内部空白处时，显示十字箭头并整体移动曲面。
    private bool draggingMeshSurfaceActive = false;
    private Vector2 draggingMeshSurfaceStartMouse = Vector2.zero;
    private readonly Dictionary<string, Vector2> draggingMeshSurfaceStartAnchorOffsets = new Dictionary<string, Vector2>();

    // 曲面变形红色外框拖拽状态：外框负责整体拉伸 / 旋转，不改变节点父子关系。
    private bool draggingMeshOuterActive = false;
    private string draggingMeshOuterKind = string.Empty;
    private Vector2 draggingMeshOuterStartMouse = Vector2.zero;
    private Vector2 draggingMeshOuterStartCenter = Vector2.zero;
    private Vector2 draggingMeshOuterStartVector = Vector2.zero;
    private Rect draggingMeshOuterStartBounds = Rect.zero;
    private Vector2 draggingMeshOuterStartXAxis = Vector2.right;
    private Vector2 draggingMeshOuterStartYAxis = Vector2.down;
    private Vector2 draggingMeshOuterStartTL = Vector2.zero;
    private Vector2 draggingMeshOuterStartTR = Vector2.zero;
    private Vector2 draggingMeshOuterStartBR = Vector2.zero;
    private Vector2 draggingMeshOuterStartBL = Vector2.zero;
    private readonly Dictionary<string, Vector2> draggingMeshOuterStartAnchors = new Dictionary<string, Vector2>();
    private readonly Dictionary<string, Vector2> draggingMeshOuterStartHandles = new Dictionary<string, Vector2>();

    // 曲面变形选区：Shift + 左键点击主控制点进行多选。
    // 选区只保存预览面板内的临时编辑状态，不污染数据结构；真正变形仍写入 meshDeformPoints。
    private string selectedMeshAnchorDeformerKey = string.Empty;
    private readonly HashSet<string> selectedMeshAnchorKeys = new HashSet<string>();
    // 点击空白处可隐藏红色外框；再次点控制点/方向柄/Shift 选点时恢复。
    private bool meshOuterFrameHidden = false;
    private string meshOuterFrameHiddenDeformerKey = string.Empty;

    // 选区红框的方向需要稳定：只有拖红色旋转点时才改变，普通拖动点位不能让红框自动歪掉。
    private string meshSelectionFrameAxesDeformerKey = string.Empty;
    private bool meshSelectionFrameAxesValid = false;
    private Vector2 meshSelectionFrameXAxis = Vector2.right;
    private Vector2 meshSelectionFrameYAxis = Vector2.down;

    // 多选点位拖拽：拖动任意一个已选主控制点时，整组已选点一起平移。
    private readonly Dictionary<string, Vector2> draggingMeshSelectedAnchorStartOffsets = new Dictionary<string, Vector2>();
    // 曲面拖拽期间不要每一帧都 Clone / 写 Timeline Key。
    // 实时预览直接读 row.meshDeformPoints，鼠标松开时再提交到当前帧关键帧。
    private bool meshDeformerLiveEditingDirty = false;

    // 曲面预览点缓存：一次绘制里 Bezier / 方向柄 / 命中检测会反复读取同一批点。
    // 不缓存会在高密网格、多选拖拽时反复 EvaluateTimelineMeshDeformPoints，手感会发黏。
    private string meshPreviewPointCacheDeformerKey = string.Empty;
    private int meshPreviewPointCacheColumns = 0;
    private int meshPreviewPointCacheRows = 0;
    private List<SkyPrisonMeshDeformPoint> meshPreviewPointCache = null;

    // Onion Skin / 上一帧残影必须是“上一帧整个人的快照”。
    private struct MeshDeformerScreenFrame
    {
        public bool valid;
        public Vector2 center;
        public Vector2 right;
        public Vector2 down;
        public float width;
        public float height;
        public float zoom;
    }

    // 曲面变形点位数据保存为“图层局部坐标偏移”。
    // 绘制和拖拽时必须乘上当前 PSB / 父骨骼旋转，否则贴图旋转后红框、方向柄和实际变形会脱节。
    private MeshDeformerScreenFrame currentMeshDeformerScreenFrame;
    private bool currentMeshDeformerScreenFrameValid = false;

    // 绘制残影时不能读取当前拖拽中的 row.runtime / row.meshDeformPoints，
    // 否则用户拖这一帧的骨骼或曲面时，上一帧也会跟着局部变形。
    private bool drawingOnionSkinSnapshot = false;

    // 曲面拖拽实时预览节流：拖动时必须能看到结果，但不能让 Inspector / Timeline / Structure 每个 MouseDrag 都跟着全量刷新。
    // 这里把可视刷新压到约 60fps；鼠标松开时仍然强制提交一次。
    private const double MeshLivePreviewRepaintInterval = 1.0 / 60.0;
    private double lastMeshLivePreviewRepaintTime = 0.0;

    // 预览画布世界原点：窗口尺寸变化不能改变工作台内物体的位置。
    // 注意：它是“预览区域局部坐标”里的固定画布原点，不再每帧取 localView.center。
    private bool previewCanvasOriginInitialized = false;
    private Vector2 previewCanvasOrigin = Vector2.zero;

    // 骨骼编辑拖拽状态。
    // 注意：这里存的是去缩放后的手动偏移，不存屏幕像素，避免预览缩放后骨架走样。
    private string draggingManualRigKey = string.Empty;
    private Vector2 draggingManualRigStartMouse = Vector2.zero;
    private Vector2 draggingManualRigStartOffset = Vector2.zero;
    private Vector2 draggingManualRigStartLayerOffset = Vector2.zero;
    private Vector2 draggingManualRigStartWorld = Vector2.zero;
    private Vector2 draggingManualRigParentStartWorld = Vector2.zero;
    private float draggingManualRigParentLength = 0f;
    private bool draggingManualRigLockParentLength = false;
    private bool draggingRigRootMove = false;
    private readonly Dictionary<string, Vector2> draggingRootStartSetupOffsets = new Dictionary<string, Vector2>();
    private readonly HashSet<string> draggingRootStartSetupEnabled = new HashSet<string>();

    // 新规则：每条骨骼线拥有独立 Root/Head 端点，不再把 Chest/Neck 这种关节点强行共享给相邻骨骼线。
    private string draggingBoneSegmentKey = string.Empty;
    private bool draggingBoneRootHandle = false;
    private int previewKeyboardControlId = 0;
    private Vector2 draggingBoneStartRootWorld = Vector2.zero;
    private Vector2 draggingBoneStartHeadWorld = Vector2.zero;
    private float draggingBoneStartLength = 0f;
    private Vector2 draggingBoneStartBaseVector = Vector2.zero;
    private float draggingBoneStartInheritedAngle = 0f;
    private Vector2 draggingBoneStartSetupRootOffset = Vector2.zero;
    private Vector2 draggingBoneStartSetupHeadOffset = Vector2.zero;
    private Vector2 draggingBoneStartRuntimeRootOffset = Vector2.zero;
    private Vector2 draggingBoneStartRuntimeHeadOffset = Vector2.zero;

    // Shift + Root 拖动轴向约束。
    private bool draggingRootShiftGuideVisible = false;
    private bool draggingRootShiftGuideHasAxis = false;
    private bool draggingRootShiftGuideHorizontal = false;
    private Vector2 draggingRootShiftGuideOrigin = Vector2.zero;

    private bool draggingMotionVisualOffset = false;
    private Vector2 draggingMotionStartMouse = Vector2.zero;
    private Vector2 draggingMotionStartOffset = Vector2.zero;

    private struct HumanPose
    {
        public Vector2 body, head, armL, armR, legL, legR, core;
        public bool showAttackHitbox;
        public Rect hitbox;
    }

    public SkyPrisonAnimationPreviewPanel(SkyPrisonAnimationWorkbenchState state)
    {
        this.state = state;
    }

    public void Draw(Rect rect)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        Rect view = new Rect(rect.x + 12f, rect.y + 32f, rect.width - 24f, rect.height - 44f);
        if (view.width <= 1f || view.height <= 1f)
            return;

        if (state.IsActionGroupSelected())
        {
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 420f, 18f), "实时预览 / 动作组：" + state.CurrentActionGroupDisplayName(), EditorStyles.boldLabel);
            DrawGroupSelectedPreview(view);
            return;
        }

        SkyPrisonAnimationActionRow action = state.CurrentAction();
        GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 360f, 18f), "实时预览 / " + action.name + " [" + action.key + "]", EditorStyles.boldLabel);

        CapturePreviewKeyboardFocus(view);
        HandlePreviewUndoShortcutRequest();
        HandlePreviewInput(view);

        EditorGUI.DrawRect(view, new Color(0.08f, 0.08f, 0.09f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawGrid(view, Mathf.Max(8f, 24f * state.PreviewZoom), new Color(1f, 1f, 1f, 0.035f));

        GUI.BeginGroup(view);
        Rect localView = new Rect(0f, 0f, view.width, view.height);
        currentPreviewClipRect = localView;
        currentPreviewWindowOffset = view.position;
        GUI.BeginClip(localView);

        Vector2 center = GetPreviewCanvasOrigin(localView) + state.PreviewPan;
        float z = Mathf.Clamp(state.PreviewZoom, 0.1f, 5f);
        if (!state.ShowRigEdit)
            center += state.EvaluateMotionVisualOffset() * z;
        float duration = Mathf.Max(0.01f, action.duration);
        float normalizedTime = Mathf.Clamp01(state.CurrentTime / duration);
        float phase = normalizedTime * Mathf.PI * 2f;

        HumanPose pose = EvaluateHumanPose(action.key, center, z, normalizedTime, phase);

        // 企业级镜像原则：镜像只发生在最终可视化输出层。
        // 不使用全局 GUI.matrix 翻转，因为那会把文字、热区、后续骨架推导也一起污染。
        // 每个绘制函数在最后一步单独调用 VisualPoint / VisualRect。
        visualMirrorEnabled = state.PreviewMirrored;
        visualMirrorPivot = center;

        // 图像层顺序：上一帧残影在当前模型下面，当前帧在上面。
        // 注意：上一帧只取用户真正编辑过的时间线关键帧，不取 currentFrame - 1 这种补帧。
        if (state.ShowOnionSkinPrevious)
            DrawPreviousFrameOnionSkin(action, center, localView, z);

        DrawBoundPsbSprites(pose, center, localView, z);

        // Overlay 层：轨迹线、物理辅助线、重心线、骨架线、控制点、判定框都不进 RT。
        // 它们继续使用原来的预览坐标画在 RT 图像上方，鼠标拾取和拖拽逻辑不被迁移污染。
        if (state.ShowFormulaPath)
            DrawActionPreviewPath(action.key, center, z);

        DrawMotionVisualOffsetPath(action.key, GetPreviewCanvasOrigin(localView) + state.PreviewPan, z);

        if (state.ShowPhysicsPreview && state.ShowPhysicsOscillatorDebug)
            DrawPhysicsOscillatorDebugOverlay(localView);

        if (state.ShowCenterOfGravityLine)
            DrawCenterOfGravityLine(localView);

        if (state.ShowRigLines)
            DrawEnterpriseRigOverlay(pose, center, localView, z);

        // “部位”只控制 PSB 图层矩形/点击选择，不再控制骨骼端点拖拽。
        // 关掉部位后，用户应该能更干净地拖动/旋转骨骼线。
        if (state.ShowRigLines || state.ShowRigEdit)
            DrawEnterpriseRigNodes(pose, center, localView, z);

        if (state.ShowHitbox && pose.showAttackHitbox)
        {
            Rect visualHitbox = VisualRect(pose.hitbox);
            EditorGUI.DrawRect(visualHitbox, new Color(1f, 0.25f, 0.18f, 0.22f));
            SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(visualHitbox, new Color(1f, 0.25f, 0.18f, 0.85f));
            GUI.Label(new Rect(visualHitbox.x + 4f, visualHitbox.y + 4f, 80f, 18f), "Hitbox", EditorStyles.miniLabel);
        }

        visualMirrorEnabled = false;

        GUI.EndClip();
        currentPreviewClipRect = Rect.zero;
        currentPreviewWindowOffset = Vector2.zero;
        GUI.EndGroup();

        // PSB 图层拾取必须放在当前帧 PSB 矩形刷新之后。
        // 之前在绘制前使用上一帧的 lastPsbPreviewRects，缩放/平移后会出现“看得到但点不中”。
        HandleCurrentFramePsbLayerSelection(view);

        DrawRigEditRoundButton(view);
        DrawPreviewToggles(view);
        DrawPreviewZoomControls(view);
    }

    private Vector2 GetPreviewCanvasOrigin(Rect localView)
    {
        if (!previewCanvasOriginInitialized || !IsFinite(previewCanvasOrigin))
        {
            previewCanvasOrigin = localView.center + new Vector2(0f, 12f);
            previewCanvasOriginInitialized = true;
        }

        return previewCanvasOrigin;
    }

    private void ResetPreviewCanvasOrigin(Rect view)
    {
        previewCanvasOrigin = new Rect(0f, 0f, view.width, view.height).center + new Vector2(0f, 12f);
        previewCanvasOriginInitialized = true;
    }

    private bool IsFinite(Vector2 v)
    {
        return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);
    }

    private HumanPose EvaluateHumanPose(string actionKey, Vector2 center, float z, float t, float phase)
    {
        // 人形模板不再内置任何写死的预览动作。
        // 呼吸 / 行走 / 奔跑 / 攻击等动作都必须来自时间线关键帧或左侧动作参数，
        // 否则切换动作时会被隐藏公式偷偷叠加，导致用户看到的姿势和轨道数据不一致。
        return BuildBasePose(center, z);
    }

    private HumanPose BuildBasePose(Vector2 body, float z)
    {
        HumanPose p = new HumanPose();
        p.body = body;
        p.head = body + new Vector2(0f, -54f * z);
        p.armL = body + new Vector2(-48f * z, -10f * z);
        p.armR = body + new Vector2(48f * z, -10f * z);
        p.legL = body + new Vector2(-28f * z, 54f * z);
        p.legR = body + new Vector2(28f * z, 54f * z);
        p.core = body + new Vector2(0f, 4f * z);
        return p;
    }

    private HumanPose EvaluateIdlePose(Vector2 center, float z, float t, float phase)
    {
        // 纯预览兜底待机：下半身锚定，只让上半身呼吸。
        // 这里不能再移动 BuildBasePose 的 center，否则会出现整个人上下跳。
        float inhale = Mathf.Sin(phase);
        float spineLift = -inhale * 0.7f * z;
        float headLift = -inhale * 1.15f * z;
        float armFollow = -inhale * 0.45f * z;

        HumanPose p = BuildBasePose(center, z);
        p.body += new Vector2(0f, spineLift);
        p.core += new Vector2(0f, spineLift);
        p.head += new Vector2(0f, headLift);
        p.armL += new Vector2(0f, armFollow);
        p.armR += new Vector2(0f, armFollow);
        p.legL = center + new Vector2(-28f * z, 54f * z);
        p.legR = center + new Vector2(28f * z, 54f * z);
        return p;
    }

    private HumanPose EvaluateMovePose(Vector2 center, float z, float phase, float strideMul, float bobMul)
    {
        float bob = Mathf.Abs(Mathf.Sin(phase)) * 20f * bobMul * z;
        float armSwing = Mathf.Sin(phase) * 18f * strideMul * z;
        float legSwing = Mathf.Sin(phase) * 20f * strideMul * z;
        HumanPose p = BuildBasePose(center + new Vector2(0f, -bob), z);
        p.armL += new Vector2(armSwing, Mathf.Abs(Mathf.Sin(phase + Mathf.PI)) * 4f * z);
        p.armR += new Vector2(-armSwing, Mathf.Abs(Mathf.Sin(phase)) * 4f * z);
        p.legL += new Vector2(-legSwing, -Mathf.Max(0f, Mathf.Sin(phase)) * 9f * z);
        p.legR += new Vector2(legSwing, -Mathf.Max(0f, Mathf.Sin(phase + Mathf.PI)) * 9f * z);
        p.core = p.body + state.EvaluateInfinity(phase) * 90f * z;
        return p;
    }

    private HumanPose EvaluateSneakPose(Vector2 center, float z, float phase)
    {
        HumanPose p = EvaluateMovePose(center + new Vector2(0f, 14f * z), z, phase, 0.45f, 0.25f);
        p.head += new Vector2(8f * z, 4f * z);
        p.armL += new Vector2(10f * z, 12f * z);
        p.armR += new Vector2(-10f * z, 10f * z);
        p.core = p.body + new Vector2(Mathf.Sin(phase) * 5f * z, 2f * z);
        return p;
    }

    private HumanPose EvaluateAttackPose(Vector2 center, float z, float t)
    {
        float windup = 1f - Smooth01(Mathf.InverseLerp(0.00f, 0.28f, t));
        float swing = Smooth01(Mathf.InverseLerp(0.22f, 0.52f, t));
        float recover = Smooth01(Mathf.InverseLerp(0.52f, 1.00f, t));
        float attackPower = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        HumanPose p = BuildBasePose(center + new Vector2(attackPower * 10f * z, -attackPower * 4f * z), z);
        p.head += new Vector2(attackPower * 8f * z, 2f * z);
        p.armL += new Vector2(-10f * z + attackPower * 12f * z, 14f * z);
        p.armR += new Vector2((-38f * windup + 62f * swing - 24f * recover) * z, (-32f * windup - 8f * swing + 12f * recover) * z);
        p.legL += new Vector2(-10f * z, 8f * z);
        p.legR += new Vector2(18f * z, -4f * z);
        p.core = p.body + new Vector2(attackPower * 16f * z, 0f);
        p.showAttackHitbox = t >= 0.32f && t <= 0.58f;
        p.hitbox = new Rect(p.body.x + 38f * z, p.body.y - 30f * z, 92f * z, 54f * z);
        return p;
    }

    private HumanPose EvaluateHurtPose(Vector2 center, float z, float t)
    {
        float impact = 1f - Smooth01(t);
        float sCurve = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        HumanPose p = BuildBasePose(center + new Vector2(-34f * sCurve * z, 12f * sCurve * z), z);
        p.head += new Vector2(-16f * sCurve * z, 6f * sCurve * z);
        p.armL += new Vector2(-24f * sCurve * z, -18f * impact * z);
        p.armR += new Vector2(18f * sCurve * z, -14f * impact * z);
        p.legL += new Vector2(-8f * sCurve * z, 10f * sCurve * z);
        p.legR += new Vector2(14f * sCurve * z, -6f * sCurve * z);
        p.core = p.body + new Vector2(-18f * sCurve * z, 5f * sCurve * z);
        return p;
    }

    private HumanPose EvaluateJumpPose(Vector2 center, float z, float t)
    {
        float arc = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        HumanPose p = BuildBasePose(center + new Vector2(0f, -58f * arc * z), z);
        p.armL += new Vector2(-8f * z, -22f * arc * z);
        p.armR += new Vector2(8f * z, -22f * arc * z);
        p.legL += new Vector2(-12f * z, -18f * arc * z);
        p.legR += new Vector2(12f * z, -18f * arc * z);
        p.core = p.body + new Vector2(0f, -10f * arc * z);
        return p;
    }

    private HumanPose EvaluateDeathPose(Vector2 center, float z, float t)
    {
        float fall = Smooth01(Mathf.Clamp01(t));
        HumanPose p = BuildBasePose(center + new Vector2(30f * fall * z, 42f * fall * z), z);
        p.head += new Vector2(28f * fall * z, 30f * fall * z);
        p.armL += new Vector2(-20f * fall * z, 34f * fall * z);
        p.armR += new Vector2(26f * fall * z, 26f * fall * z);
        p.legL += new Vector2(-14f * fall * z, 18f * fall * z);
        p.legR += new Vector2(18f * fall * z, 22f * fall * z);
        p.core = p.body + new Vector2(10f * fall * z, 8f * fall * z);
        return p;
    }

    private Vector2 VisualPoint(Vector2 p)
    {
        if (!visualMirrorEnabled)
            return p;

        return new Vector2(visualMirrorPivot.x * 2f - p.x, p.y);
    }

    private Rect VisualRect(Rect r)
    {
        if (!visualMirrorEnabled)
            return r;

        return new Rect(visualMirrorPivot.x * 2f - r.xMax, r.y, r.width, r.height);
    }

    private Rect GetPreviewFinalVisualRect(Rect r)
    {
        if (state == null || !state.PreviewMirrored)
            return r;

        return new Rect(visualMirrorPivot.x * 2f - r.xMax, r.y, r.width, r.height);
    }

    private float Smooth01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }


    private sealed class PsbSpriteLayout
    {
        public Sprite sprite;
        public Vector2 localCenter;
        public Vector2 localSize;
        public int hierarchyOrder;
        public int sortingOrder;
        public string sortingLayerName;
        public float prefabLayerWeight;
    }

    private sealed class PsbPrefabLayout
    {
        public bool valid;
        public Hash128 assetHash;
        public Rect bounds;
        public readonly Dictionary<string, PsbSpriteLayout> bySpriteName = new Dictionary<string, PsbSpriteLayout>();
        public readonly Dictionary<string, PsbSpriteLayout> byObjectName = new Dictionary<string, PsbSpriteLayout>();
    }

    private sealed class PsbSpriteDrawState
    {
        public Vector2 center;
        public Vector2 size;
        public float angle;
        public Rect rect;
    }

    private sealed class PhysicsOscillatorDebugDrawEntry
    {
        public string rowKey;
        public string sourceKey;
        public Vector2 root;
        public Vector2 direction;
        public Vector2 perpendicular;
        public float physicsAngle;
        public float offsetAmount;
        public SkyPrisonPhysicsPreset preset;
    }

    private sealed class PhysicsRuntimeState
    {
        public bool initialized;
        public double lastEditorTime;
        public float angle;
        public float velocity;
        public float lastInputAngle;
    }

    private struct RigBoneSegment
    {
        public string segmentKey;
        public string rootKey;
        public string headKey;
        public Vector2 root;
        public Vector2 head;
    }

    private static readonly Dictionary<string, PsbPrefabLayout> PsbPrefabLayoutCache = new Dictionary<string, PsbPrefabLayout>();

    private void DrawBoundPsbSprites(HumanPose pose, Vector2 center, Rect localView, float z, bool onionSkin = false, float onionAlpha = 1f)
    {
        if (!onionSkin)
        {
            lastPsbPreviewRects.Clear();
            lastPsbPreviewPickOrder.Clear();
            physicsOscillatorDebugEntries.Clear();
            if (state.PhysicsOscillatorStatuses != null) state.PhysicsOscillatorStatuses.Clear();
        }

        if (state.PsbRows == null || state.PsbRows.Count == 0)
            return;

        string psbAssetPath = GetCurrentPsbAssetPath();
        PsbPrefabLayout primaryLayout = LoadPsbPrefabLayout(psbAssetPath);

        if (primaryLayout == null || !primaryLayout.valid || primaryLayout.bySpriteName.Count == 0 || primaryLayout.bounds.width <= 0.0001f || primaryLayout.bounds.height <= 0.0001f)
        {
            if (!onionSkin)
                DrawPsbLayoutWarning(localView);
            return;
        }

        // 服装/武器 PSB 可以来自不同资源。不能只用当前角色 SourcePsdAssetPath 的 layout，
        // 否则装配模拟里读到了衣物树，预览里却找不到对应 SpriteRenderer。
        Dictionary<string, PsbPrefabLayout> layoutCacheForDraw = new Dictionary<string, PsbPrefabLayout>(StringComparer.OrdinalIgnoreCase);
        layoutCacheForDraw[psbAssetPath ?? string.Empty] = primaryLayout;

        // PSB 贴图变形必须和屏幕上显示的骨骼线使用同一套锚点。
        // 不能再用纯模板 BuildRigAnchorMap，否则编辑模式改过骨长后，
        // 预览骨骼线和贴图旋转会各算各的，头部尤其容易出现反向/不同步。
        HumanPose restPose = BuildBasePose(center, z);

        List<SkyPrisonAnimationRigRow> drawable = new List<SkyPrisonAnimationRigRow>();
        Dictionary<SkyPrisonAnimationRigRow, PsbSpriteLayout> rowLayout = new Dictionary<SkyPrisonAnimationRigRow, PsbSpriteLayout>();
        Dictionary<SkyPrisonAnimationRigRow, PsbPrefabLayout> rowLayoutOwner = new Dictionary<SkyPrisonAnimationRigRow, PsbPrefabLayout>();

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null || row.isFolder || !IsPsbLayerEffectivelyVisible(row) || IsPsbDyeMaskLayer(row))
                continue;

            string rowAssetPath = !string.IsNullOrEmpty(row.sourceAssetPath) ? row.sourceAssetPath : psbAssetPath;
            PsbPrefabLayout layout = GetPsbLayoutForDraw(rowAssetPath, layoutCacheForDraw);
            if (layout == null || !layout.valid)
                continue;

            PsbSpriteLayout item = FindPsbSpriteLayout(layout, row);
            if (item == null || item.sprite == null || item.sprite.texture == null)
                continue;

            drawable.Add(row);
            rowLayout[row] = item;
            rowLayoutOwner[row] = layout;
        }

        if (drawable.Count == 0)
        {
            if (!onionSkin)
                DrawPsbLayoutWarning(localView);
            return;
        }

        drawable.Sort((a, b) =>
        {
            PsbSpriteLayout itemA = rowLayout.ContainsKey(a) ? rowLayout[a] : null;
            PsbSpriteLayout itemB = rowLayout.ContainsKey(b) ? rowLayout[b] : null;

            // 大层级必须由语义决定；SpriteRenderer sortingOrder 只负责同一语义层内部。
            // 否则服装 PSB 和身体 PSB 来自不同资源时，二者 sortingOrder 空间不一致，衣服会读到了但画不到正确层。
            int semantic = GetPsbDrawOrder(a).CompareTo(GetPsbDrawOrder(b));
            if (semantic != 0)
                return semantic;

            float weightA = GetEffectivePreviewLayerWeight(a, itemA);
            float weightB = GetEffectivePreviewLayerWeight(b, itemB);
            int weightCompare = weightA.CompareTo(weightB);
            if (weightCompare != 0)
                return weightCompare;

            int orderA = itemA != null ? itemA.hierarchyOrder : 0;
            int orderB = itemB != null ? itemB.hierarchyOrder : 0;
            return orderA.CompareTo(orderB);
        });

        // 关键修正：不要使用 sprite.textureRect 当“画布坐标”。
        // textureRect 是图集/纹理里的裁剪位置，Mosaic/打包后会完全失真。
        // 这里改为读取 PSB Importer 生成的 Prefab / Character Rig 层级 Transform，
        // 每个 SpriteRenderer 的 localPosition 才是该图层在角色画布里的真实位置。
        float fitScale = Mathf.Min((localView.width * 0.72f) / Mathf.Max(0.0001f, primaryLayout.bounds.width),
                                   (localView.height * 0.88f) / Mathf.Max(0.0001f, primaryLayout.bounds.height));
        fitScale = Mathf.Clamp(fitScale, 4f, 900f) * Mathf.Clamp(state.PreviewZoom, 0.1f, 5f);

        Dictionary<SkyPrisonAnimationRigRow, PsbSpriteDrawState> drawStates = new Dictionary<SkyPrisonAnimationRigRow, PsbSpriteDrawState>();
        Dictionary<SkyPrisonAnimationRigRow, Rect> finalRects = new Dictionary<SkyPrisonAnimationRigRow, Rect>();

        // 关键：PSB 绘制和绿色骨骼线共用同一套“PSB校准锚点”。
        // rest/current 都吃编辑模式下的 Setup 修正；只有非编辑模式的旋转拖拽进入 current。
        // 这样编辑模式调整骨长后，退出编辑时不会回旧骨长，头部旋转角度也会立刻按新骨段刷新。
        // 注意：独立骨骼线端点不能先把 manualRigOffset / manualRigLayerOffset 烧进共享锚点，
        // 再在 BuildRigBoneSegments 里加一次 Root/Head 偏移；否则拖 Root 会出现长度变化和角度倾斜。
        // 所以：
        // - fallback 点位锚点仍可带旧偏移；
        // - 真正用于贴图旋转的骨骼线段，必须从“未偏移基准锚点”开始，再只加本线段自己的端点偏移。
        Dictionary<string, Vector2> restRigAnchors = BuildDisplayRigAnchorMap(restPose, center, localView, z, true, false);
        Dictionary<string, Vector2> currentRigAnchors = BuildDisplayRigAnchorMap(pose, center, localView, z, true, !state.ShowRigEdit);

        Dictionary<string, Vector2> restSegmentBaseAnchors = BuildDisplayRigAnchorMap(restPose, center, localView, z, false, false);
        Dictionary<string, Vector2> currentSegmentBaseAnchors = BuildDisplayRigAnchorMap(pose, center, localView, z, false, false);
        Dictionary<string, RigBoneSegment> restRigSegments = BuildRigBoneSegments(restSegmentBaseAnchors, z, true, false);
        Dictionary<string, RigBoneSegment> currentRigSegments = BuildRigBoneSegments(currentSegmentBaseAnchors, z, true, !state.ShowRigEdit);

        for (int i = 0; i < drawable.Count; i++)
        {
            SkyPrisonAnimationRigRow row = drawable[i];
            PsbSpriteLayout item = rowLayout[row];
            PsbPrefabLayout ownerLayout = rowLayoutOwner.ContainsKey(row) ? rowLayoutOwner[row] : primaryLayout;

            // 所有外貌/服装 PSB 必须落在“角色主 PSB 坐标系”里。
            // 不能用每个服装资源自己的 bounds 重新居中，否则外套、武器会各自以自身包围盒对齐，
            // 看起来就是坐标漂移、后片压到裸模前面。
            Vector2 originalCenter = PsbLocalToPreview(item.localCenter, primaryLayout.bounds, center, fitScale);
            Vector2 drawSize = item.localSize * fitScale;

            PsbSpriteDrawState drawState = BuildPsbSpriteDrawState(
                row,
                originalCenter,
                drawSize,
                restRigAnchors,
                currentRigAnchors,
                restRigSegments,
                currentRigSegments,
                !onionSkin && state.ShowPhysicsPreview);
            drawStates[row] = drawState;
            finalRects[row] = drawState.rect;
            if (!onionSkin && row != null && !string.IsNullOrEmpty(row.key))
            {
                // 拾取矩形保存最终视觉位置。
                // 镜像是整张模型视口的最终视觉翻转，不改变 row / 绑定 / L-R 语义；
                // 因此命中热区要跟随最终画面，而不是继续停在未镜像坐标。
                lastPsbPreviewRects[row.key] = GetPreviewFinalVisualRect(drawState.rect);
                lastPsbPreviewPickOrder.Add(row.key);
            }
        }

        // 关键：只有真正需要“逐层 RT 合成”的图层才启用 Preview Compositor。
        // 全部都是“正常 + 无图层Shader”的普通 PSB 预览，必须走原本 IMGUI 绘制路径。
        // 否则 RT 写入路径会因为 GUIClip / Matrix / PreviewZoom 的坐标空间换算，导致部分 PSB 图层在缩放时产生位置偏移。
        // 合成方式 / 图层 Shader 已迁入模型视口 RT 管线。
        // 这里不能再启用旧 PreviewBlendCompositor，否则会把一张空的旧合成 RT 盖到新模型视口上，
        // 也会把旧 IMGUI / Graphics.DrawTexture 裁切问题重新带回来。
        bool usePreviewCompositor = false;
        // 真正的模型视口：普通 PSB 图层统一收集后写入整张 RT。
        // 注意：这里只对当前帧普通预览启用，洋葱皮/特殊 Shader/Mask 仍走旧路径，避免一次性破坏其它功能。
        // 普通当前帧和上一帧残影都走模型视口 RT。
        // 注意：这里不是单图层 RT，而是整张 localView 一个 RT；坐标、缩放、骨架点仍沿用原预览坐标。
        bool useModelViewport = Event.current != null && Event.current.type == EventType.Repaint;

        if (usePreviewCompositor)
            BeginPreviewBlendCompositor(localView);

        bool oldVisualMirrorForModelViewport = visualMirrorEnabled;
        if (useModelViewport)
        {
            BeginModelViewportSpriteCollection();

            // 镜像翻转必须是“整张角色画面”的最终翻转。
            // 不能在每个 PSB 局部图层里分别翻，否则头发、眼睛、曲面层会各自围绕自己的中心翻，
            // 最终变成局部散开的假镜像。这里收集模型 RT 时强制使用未镜像坐标，
            // 最后在 EndModelViewportSpriteCollectionAndDraw 里把整张 RT 一次性镜像。
            visualMirrorEnabled = false;
        }

        for (int i = 0; i < drawable.Count; i++)
        {
            SkyPrisonAnimationRigRow row = drawable[i];
            PsbSpriteLayout item = rowLayout[row];
            PsbSpriteDrawState drawState = drawStates[row];
            Rect maskRect;
            PsbPrefabLayout ownerLayout = rowLayoutOwner.ContainsKey(row) ? rowLayoutOwner[row] : primaryLayout;
            bool hasMask = TryGetMaskPreviewRect(row, rowLayout, ownerLayout, center, fitScale, finalRects, out maskRect);
            ModelViewportMaskSpriteCommand viewportMaskCommand = default;
            bool suppressMeshDeformEffects = ShouldSuppressMeshDeformerPreviewEffects();
            bool hasViewportMask = !onionSkin && !suppressMeshDeformEffects && TryBuildModelViewportMaskCommand(row, rowLayout, drawStates, out viewportMaskCommand);
            float focusAlpha = onionSkin ? 1f : GetPreviewFocusAlphaForPsbRow(row);
            float alpha = GetEffectivePsbLayerOpacity(row) * focusAlpha * (onionSkin ? Mathf.Clamp01(onionAlpha) : 1f);
            string blendMode = GetEffectiveBlendMode(row);
            Shader layerEffectShader = row != null ? row.renderShader : null;

            SkyPrisonAnimationRigRow meshDeformer = null;
            if (!onionSkin && !suppressMeshDeformEffects)
                meshDeformer = FindMeshDeformerForPsbRow(row);

            if (meshDeformer != null && DrawSpriteWithMeshDeformer(item.sprite, drawState, meshDeformer, alpha, blendMode, hasMask, maskRect, layerEffectShader, row, hasViewportMask, viewportMaskCommand))
            {
                // 已经按曲面网格绘制。
            }
            else
            {
                DrawSpriteWithLayout(item.sprite, drawState.center, drawState.size, drawState.angle, alpha, blendMode, hasMask, maskRect, layerEffectShader, row, hasViewportMask, viewportMaskCommand);
            }
        }

        if (useModelViewport)
        {
            EndModelViewportSpriteCollectionAndDraw(localView);
            visualMirrorEnabled = oldVisualMirrorForModelViewport;
        }

        if (usePreviewCompositor)
            EndPreviewBlendCompositor(localView);

        if (!onionSkin)
        {
            DrawSelectedMeshDeformerGrid(finalRects, drawStates);
            DrawPsbLayerSelectionOverlay(finalRects);
        }
    }

    private void DrawMotionVisualOffsetPath(string actionKey, Vector2 baseCenter, float z)
    {
        if (state == null || state.MotionKeyframes == null || state.MotionKeyframes.Count == 0 || string.IsNullOrEmpty(actionKey))
            return;

        Vector2 last = Vector2.zero;
        bool hasLast = false;
        for (int i = 0; i < state.MotionKeyframes.Count; i++)
        {
            SkyPrisonAnimationMotionKeyframe k = state.MotionKeyframes[i];
            if (k == null || !string.Equals(k.actionKey, actionKey, System.StringComparison.OrdinalIgnoreCase))
                continue;

            Vector2 p = baseCenter + k.visualOffset * z;
            if (hasLast)
                SkyPrisonAnimationWorkbenchStyle.DrawLine(VisualPoint(last), VisualPoint(p), new Color(0.35f, 0.68f, 1f, 0.70f), 2f);
            hasLast = true;
            last = p;

            Rect r = new Rect(p.x - 5f, p.y - 5f, 10f, 10f);
            EditorGUI.DrawRect(VisualRect(r), new Color(0.35f, 0.68f, 1f, 0.95f));
        }

        if (state.IsMotionTimelineTrack(state.ActiveTimelineTrackKey))
            GUI.Label(new Rect(8f, 8f, 220f, 18f), "Motion轨道：拖动角色整体位移", EditorStyles.miniBoldLabel);
    }

    private void DrawPreviousFrameOnionSkin(SkyPrisonAnimationActionRow action, Vector2 center, Rect localView, float z)
    {
        if (action == null || state == null)
            return;

        int currentFrame = state.TimelineCurrentFrame;
        int previousFrame = FindPreviousAuthoredKeyframe(action, currentFrame);

        // 注意：这里绝对不回退到 currentFrame - 1。
        // “上一帧”在这个按钮的语义里等于“上一个用户打过的关键帧”，
        // 不是自动补间出来的前一帧。否则拖当前帧时，残影会显示一张没有制作意义的 -1 帧。
        if (previousFrame < 0 || previousFrame == currentFrame)
            return;

        float oldTime = state.CurrentTime;
        bool oldOnionSnapshot = drawingOnionSkinSnapshot;
        Vector2 oldMirrorPivot = visualMirrorPivot;

        // center 参数在普通预览里已经包含“当前帧 Motion Visual Offset”。
        // 上一关键帧残影不能继续沿用当前帧 Motion，否则跪下、跳跃、受击位移这类动作会对不齐。
        // 这里先记录当前帧 Motion，再切到上一关键帧取上一关键帧 Motion，最后只把二者差值补到残影中心。
        Vector2 currentMotionOffset = (!state.ShowRigEdit) ? state.EvaluateMotionVisualOffset() : Vector2.zero;

        try
        {
            drawingOnionSkinSnapshot = true;
            state.CurrentTime = state.FrameToSeconds(previousFrame);

            Vector2 previousMotionOffset = (!state.ShowRigEdit) ? state.EvaluateMotionVisualOffset() : Vector2.zero;
            Vector2 previousCenter = center + (previousMotionOffset - currentMotionOffset) * z;

            // 镜像的轴心也要跟随上一关键帧的整体 Motion，否则残影位置正确但镜像拾取/绘制会绕当前帧中心翻。
            visualMirrorPivot = previousCenter;

            float duration = Mathf.Max(0.01f, action.duration);
            float normalizedTime = Mathf.Clamp01(state.CurrentTime / duration);
            float phase = normalizedTime * Mathf.PI * 2f;
            HumanPose previousPose = EvaluateHumanPose(action.key, previousCenter, z, normalizedTime, phase);
            DrawBoundPsbSprites(previousPose, previousCenter, localView, z, true, 0.46f);
        }
        finally
        {
            visualMirrorPivot = oldMirrorPivot;
            drawingOnionSkinSnapshot = oldOnionSnapshot;
            state.CurrentTime = oldTime;
        }
    }

    private int FindPreviousAuthoredKeyframe(SkyPrisonAnimationActionRow action, int currentFrame)
    {
        if (action == null || state == null)
            return -1;

        string actionKey = state.CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey))
            actionKey = action.key;

        int previousFrame = -1;

        // 1. 普通时间线关键帧：骨骼、PSB、曲面、脚步声等。
        if (state.TimelineKeyframes != null)
        {
            for (int i = 0; i < state.TimelineKeyframes.Count; i++)
            {
                SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
                if (k == null || k.frame >= currentFrame)
                    continue;
                if (!IsSameActionKeyForOnion(k.actionKey, actionKey))
                    continue;
                if (k.frame > previousFrame)
                    previousFrame = k.frame;
            }
        }

        // 2. Motion 轨道关键帧也算用户制作过的关键帧。
        //    这样只有 Motion 位移关键帧时，上一关键帧残影也能正常定位。
        if (state.MotionKeyframes != null)
        {
            for (int i = 0; i < state.MotionKeyframes.Count; i++)
            {
                SkyPrisonAnimationMotionKeyframe k = state.MotionKeyframes[i];
                if (k == null || k.frame >= currentFrame)
                    continue;
                if (!IsSameActionKeyForOnion(k.actionKey, actionKey))
                    continue;
                if (k.frame > previousFrame)
                    previousFrame = k.frame;
            }
        }

        // 3. 循环动作在 0 帧时允许找动作末尾最后一个关键帧。
        //    但仍然只找“真实关键帧”，不使用 totalFrame - 1。
        if (previousFrame < 0 && currentFrame == 0 && action.loop)
        {
            int totalFrame = state.TimelineTotalFrames;

            if (state.TimelineKeyframes != null)
            {
                for (int i = 0; i < state.TimelineKeyframes.Count; i++)
                {
                    SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
                    if (k == null || k.frame <= 0 || k.frame > totalFrame)
                        continue;
                    if (!IsSameActionKeyForOnion(k.actionKey, actionKey))
                        continue;
                    if (k.frame > previousFrame)
                        previousFrame = k.frame;
                }
            }

            if (state.MotionKeyframes != null)
            {
                for (int i = 0; i < state.MotionKeyframes.Count; i++)
                {
                    SkyPrisonAnimationMotionKeyframe k = state.MotionKeyframes[i];
                    if (k == null || k.frame <= 0 || k.frame > totalFrame)
                        continue;
                    if (!IsSameActionKeyForOnion(k.actionKey, actionKey))
                        continue;
                    if (k.frame > previousFrame)
                        previousFrame = k.frame;
                }
            }
        }

        return previousFrame;
    }

    private bool IsSameActionKeyForOnion(string key, string currentActionKey)
    {
        if (string.IsNullOrEmpty(currentActionKey))
            return string.IsNullOrEmpty(key);

        // 旧文件里有些关键帧 actionKey 为空，视为当前动作兼容。
        return string.IsNullOrEmpty(key) || string.Equals(key, currentActionKey, System.StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetRigAngleForPoseSnapshot(string segmentKey, out float angleDeg)
    {
        angleDeg = 0f;
        if (state == null || string.IsNullOrEmpty(segmentKey))
            return false;

        // Onion Skin / 上一关键帧残影必须是完整时间线快照。
        // 拖动当前帧骨骼 Head 时，state.LiveManualBoneAngleKeys 会保存当前鼠标输入角度；
        // 如果残影也走 TryGetEffectiveManualBoneAngle，就会先读到这个 Live 值，导致上一关键帧跟着当前帧旋转。
        // Snapshot 模式下只允许读取当前 state.CurrentTime 指向的时间线 RigAngle，不允许读取实时编辑缓存。
        if (drawingOnionSkinSnapshot)
            return state.TryEvaluateTimelineManualBoneAngle(segmentKey, out angleDeg);

        return state.TryGetEffectiveManualBoneAngle(segmentKey, out angleDeg);
    }


    private PsbSpriteDrawState BuildPsbSpriteDrawState(SkyPrisonAnimationRigRow row, Vector2 originalCenter, Vector2 drawSize, Dictionary<string, Vector2> restAnchors, Dictionary<string, Vector2> currentAnchors, Dictionary<string, RigBoneSegment> restSegments, Dictionary<string, RigBoneSegment> currentSegments, bool applyPhysics)
    {
        string headKey = row != null ? row.boundRigKey : string.Empty;
        PsbSpriteDrawState drawState = new PsbSpriteDrawState
        {
            center = originalCenter,
            size = drawSize,
            angle = 0f,
            rect = GetSpriteDrawRect(originalCenter, drawSize)
        };

        if (string.IsNullOrEmpty(headKey))
            return drawState;

        // 自定义骨骼不会出现在 humanoid anchor 字典里，必须先尝试按骨骼段驱动。
        // 否则绑定成功了但贴图不会跟着自定义骨骼旋转/移动。
        string earlyDriverSegmentKey = ResolveDriverSegmentKeyForBoundRig(headKey);
        if (TryGetSegmentPair(earlyDriverSegmentKey, restSegments, currentSegments, out RigBoneSegment earlyRestSeg, out RigBoneSegment earlyCurrentSeg))
        {
            PsbSpriteDrawState segmentState = BuildPsbSpriteDrawStateOnSegment(
                originalCenter,
                drawSize,
                earlyRestSeg.root,
                earlyRestSeg.head,
                earlyCurrentSeg.root,
                earlyCurrentSeg.head);
            return ApplyPhysicsPreviewToDrawState(row, segmentState, restSegments, currentSegments, applyPhysics);
        }

        // 新建节点可以只是“挂点/空节点”，本身没有骨架线。
        // 但绑定到它的 PSB 图层必须继承父级骨骼段变换，否则发饰/耳饰/飘带根会变成死图层。
        if (TryBuildPsbSpriteDrawStateFromTransformOnlyRigNode(
            headKey,
            originalCenter,
            drawSize,
            restSegments,
            currentSegments,
            out PsbSpriteDrawState transformOnlyState))
        {
            return ApplyPhysicsPreviewToDrawState(row, transformOnlyState, restSegments, currentSegments, applyPhysics);
        }

        if (restAnchors == null || currentAnchors == null)
            return drawState;

        if (!restAnchors.TryGetValue(headKey, out Vector2 restHead) || !currentAnchors.TryGetValue(headKey, out Vector2 currentHead))
            return drawState;

        // 躯干按 Spine 父子链拆成三条中轴骨骼线：
        // lower: Pelvis 骨骼 = Pelvis -> Spine。
        // upper: Spine 骨骼 = Spine -> Chest。
        // chest: Chest 骨骼 = Chest -> Neck。
        // 这样上半身不再和下半身硬粘成一整块。
        if (IsUpperTorsoRig(row, headKey)
            && TryGetSegmentPair("Spine", restSegments, currentSegments, out RigBoneSegment restUpper, out RigBoneSegment currentUpper))
        {
            PsbSpriteDrawState upperState = BuildPsbSpriteDrawStateOnSegment(
                originalCenter,
                drawSize,
                restUpper.root,
                restUpper.head,
                currentUpper.root,
                currentUpper.head);
            return ApplyPhysicsPreviewToDrawState(row, upperState, restSegments, currentSegments, applyPhysics);
        }

        if (IsLowerTorsoRig(row, headKey)
            && TryGetSegmentPair("Pelvis", restSegments, currentSegments, out RigBoneSegment restLower, out RigBoneSegment currentLower))
        {
            PsbSpriteDrawState lowerState = BuildPsbSpriteDrawStateOnSegment(
                originalCenter,
                drawSize,
                restLower.root,
                restLower.head,
                currentLower.root,
                currentLower.head);
            return ApplyPhysicsPreviewToDrawState(row, lowerState, restSegments, currentSegments, applyPhysics);
        }

        // 普通部位：把 boundRigKey 解析成“驱动骨骼段”。
        // 注意：Foot_L / HandEnd_L 这种是端点，不是骨骼段 key。
        // Foot_L 图层应该跟随 Ankle_L -> Foot_L 这一段；
        // HandEnd_L 图层应该跟随 Wrist_L -> HandEnd_L 这一段。
        // 如果直接 TryGetSegmentPair("Foot_L") 会找不到段，于是退回点位平移，表现就是脚图层没有真正绑定/旋转。
        string driverSegmentKey = ResolveDriverSegmentKeyForBoundRig(headKey);
        if (TryGetSegmentPair(driverSegmentKey, restSegments, currentSegments, out RigBoneSegment restSeg, out RigBoneSegment currentSeg))
        {
            PsbSpriteDrawState driverState = BuildPsbSpriteDrawStateOnSegment(
                originalCenter,
                drawSize,
                restSeg.root,
                restSeg.head,
                currentSeg.root,
                currentSeg.head);
            return ApplyPhysicsPreviewToDrawState(row, driverState, restSegments, currentSegments, applyPhysics);
        }

        Vector2 pointDelta = currentHead - restHead;
        drawState.center = originalCenter + pointDelta;
        drawState.rect = GetSpriteDrawRect(drawState.center, drawSize);
        return ApplyPhysicsPreviewToDrawState(row, drawState, restSegments, currentSegments, applyPhysics);
    }
    private PsbSpriteDrawState ApplyPhysicsPreviewToDrawState(
        SkyPrisonAnimationRigRow psbRow,
        PsbSpriteDrawState drawState,
        Dictionary<string, RigBoneSegment> restSegments,
        Dictionary<string, RigBoneSegment> currentSegments,
        bool applyPhysics)
    {
        if (!applyPhysics || state == null || psbRow == null)
            return drawState;

        SkyPrisonAnimationRigRow physicsSource = ResolvePhysicsSourceRow(psbRow);
        if (physicsSource == null || !physicsSource.usePhysicsInfluence)
            return drawState;

        SkyPrisonPhysicsPreset preset = state.FindPhysicsPreset(physicsSource.physicsPresetKey);
        if (preset == null)
            return drawState;

        preset.EnsureOscillatorCount();

        RigBoneSegment restDriver;
        RigBoneSegment currentDriver;
        bool hasDriver = TryResolvePhysicsDriverSegment(psbRow, physicsSource, restSegments, currentSegments, out restDriver, out currentDriver);

        float driverAngle = 0f;
        float driverMotion = 0f;
        Vector2 driverDir = Vector2.down;
        Vector2 driverPivot = drawState.center;

        if (hasDriver)
        {
            Vector2 restVector = restDriver.head - restDriver.root;
            Vector2 currentVector = currentDriver.head - currentDriver.root;
            if (restVector.sqrMagnitude > 0.0001f && currentVector.sqrMagnitude > 0.0001f)
            {
                driverAngle = Vector2.SignedAngle(restVector, currentVector);
                driverDir = currentVector.normalized;
            }

            Vector2 rootDelta = currentDriver.root - restDriver.root;
            Vector2 headDelta = currentDriver.head - restDriver.head;
            driverMotion = (rootDelta.magnitude + headDelta.magnitude) * 0.5f;

            // 关键：物理摆动必须围绕父级挂点，而不是围绕图层自身中心旋转。
            // 头发/飘带如果只在自身中心旋转，看起来会非常硬，几乎像没有物理。
            driverPivot = currentDriver.head;
        }

        // 无骨骼头发节点常常没有自己的骨骼段；这时也要吃父节点旋转。
        // 如果没有可用 driver，就用当前绘制角作为输入，至少保证“打开物理”后有可见测试反馈。
        if (!hasDriver && Mathf.Abs(drawState.angle) > 0.0001f)
            driverAngle = drawState.angle;

        float strength = Mathf.Clamp01(physicsSource.physicsInfluenceStrength)
            * Mathf.Clamp01(preset.defaultBlend)
            * Mathf.Max(0f, physicsSource.physicsLocalSwingMultiplier);
        if (strength <= 0.0001f)
            return drawState;

        float avgReaction = 0f;
        float avgReturn = 0f;
        float avgDamping = 0f;
        float avgSway = 0f;
        float totalWeight = 0f;
        float totalLength = 0f;
        int count = preset.oscillators != null ? preset.oscillators.Count : 0;

        for (int i = 0; i < count; i++)
        {
            SkyPrisonPhysicsOscillator osc = preset.oscillators[i];
            if (osc == null) continue;

            float w = Mathf.Max(0.0001f, osc.weight);
            avgReaction += Mathf.Max(0.05f, osc.reactionSpeed) * w;
            avgReturn += Mathf.Max(0.05f, osc.returnSpeed) * w;
            avgDamping += Mathf.Clamp01(osc.damping) * w;
            avgSway += Mathf.Clamp01(osc.swayEase) * w;
            totalLength += Mathf.Max(0f, osc.length) * w;
            totalWeight += w;
        }

        if (totalWeight <= 0.0001f)
            return drawState;

        avgReaction /= totalWeight;
        avgReturn /= totalWeight;
        avgDamping /= totalWeight;
        avgSway /= totalWeight;
        totalLength /= totalWeight;

        string runtimeKey = (physicsSource.key ?? string.Empty) + "|" + (psbRow.key ?? string.Empty);
        PhysicsRuntimeState runtime;
        if (!physicsRuntimeStates.TryGetValue(runtimeKey, out runtime) || runtime == null)
        {
            runtime = new PhysicsRuntimeState();
            physicsRuntimeStates[runtimeKey] = runtime;
        }

        double now = EditorApplication.timeSinceStartup;
        float dt = runtime.initialized ? Mathf.Clamp((float)(now - runtime.lastEditorTime), 0.001f, 0.05f) : 1f / 60f;
        runtime.lastEditorTime = now;
        runtime.initialized = true;

        float inputKick = driverAngle * Mathf.Max(0f, preset.velocityInfluence);
        inputKick += driverMotion * 0.10f * Mathf.Sign(Mathf.Approximately(driverAngle, 0f) ? 1f : driverAngle);
        inputKick += (inputKick - runtime.lastInputAngle) * 0.55f;
        runtime.lastInputAngle = inputKick;

        // 这里故意让第一版预览更“看得见”：
        // 输入 5~10 度的头部旋转，头发应能出现明确滞后，而不是只有 1~2 度的硬摆。
        float targetAngle = Mathf.Clamp(-inputKick * (0.72f + avgSway * 1.15f), -55f, 55f);
        float spring = Mathf.Lerp(10f, 36f, Mathf.Clamp01(avgReaction / 2.5f));
        float returnMul = Mathf.Lerp(0.75f, 2.1f, Mathf.Clamp01(avgReturn / 2.5f));
        float dampingMul = Mathf.Lerp(1.0f, 8f, avgDamping);

        float accel = (targetAngle - runtime.angle) * spring;
        runtime.velocity += accel * dt;
        runtime.velocity *= 1f / (1f + dampingMul * dt);
        runtime.angle += runtime.velocity * dt * returnMul;

        // 手动编辑头部角度时，如果没有播放，输入变化也要立刻可见；否则用户会以为物理没有工作。
        float directPreview = targetAngle * 0.72f;
        float physicsAngle = Mathf.Clamp(runtime.angle + directPreview, -55f, 55f) * strength;

        Vector2 perp = new Vector2(-driverDir.y, driverDir.x);
        if (perp.sqrMagnitude < 0.0001f)
            perp = Vector2.right;

        float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
        float scaledLength = Mathf.Max(2f, totalLength) * Mathf.Max(0.1f, preset.globalScale) * zoom;
        float offsetAmount = Mathf.Clamp(Mathf.Sin(physicsAngle * Mathf.Deg2Rad) * scaledLength * 4.0f, -64f, 64f)
            * strength;

        // 真正接通图层：
        // 1) 先让图层中心围绕父级挂点旋转，形成“从根部甩出去”的感觉；
        // 2) 再附加少量横向位移，放大短发/小饰品的可见反馈；
        // 3) 最后图层自身角度也叠加物理角。
        Vector2 fromPivot = drawState.center - driverPivot;
        if (fromPivot.sqrMagnitude > 0.0001f)
            drawState.center = driverPivot + RotateVector(fromPivot, physicsAngle);
        drawState.center += perp.normalized * offsetAmount;
        drawState.angle += physicsAngle;
        drawState.rect = GetSpriteDrawRect(drawState.center, drawState.size);

        List<Vector2> statusPoints = null;
        if ((state.PhysicsOscillatorStatuses != null) || (state.ShowPhysicsOscillatorDebug && physicsOscillatorDebugEntries != null))
        {
            statusPoints = BuildPhysicsOscillatorStatusPoints(driverPivot, driverDir, physicsAngle, preset, strength);
        }

        if (state.PhysicsOscillatorStatuses != null)
        {
            SkyPrisonPhysicsOscillatorStatus status = new SkyPrisonPhysicsOscillatorStatus
            {
                rowKey = psbRow.key ?? string.Empty,
                sourceKey = physicsSource.key ?? string.Empty,
                presetName = string.IsNullOrWhiteSpace(preset.displayName) ? preset.presetKey : preset.displayName,
                active = true,
                inputAngle = driverAngle,
                outputAngle = physicsAngle,
                offsetAmount = offsetAmount
            };
            if (statusPoints != null)
            {
                for (int i = 0; i < statusPoints.Count; i++)
                    status.points.Add(statusPoints[i]);
            }
            state.PhysicsOscillatorStatuses.Add(status);
        }

        if (state.ShowPhysicsOscillatorDebug && physicsOscillatorDebugEntries != null)
        {
            physicsOscillatorDebugEntries.Add(new PhysicsOscillatorDebugDrawEntry
            {
                rowKey = psbRow.key,
                sourceKey = physicsSource.key,
                root = driverPivot,
                direction = driverDir.sqrMagnitude > 0.0001f ? driverDir.normalized : Vector2.down,
                perpendicular = perp.normalized,
                physicsAngle = physicsAngle,
                offsetAmount = offsetAmount,
                preset = preset
            });
        }

        return drawState;
    }

    private List<Vector2> BuildPhysicsOscillatorStatusPoints(Vector2 root, Vector2 direction, float physicsAngle, SkyPrisonPhysicsPreset preset, float strength)
    {
        List<Vector2> points = new List<Vector2>();
        points.Add(root);
        if (preset == null || preset.oscillators == null)
            return points;

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        float accumAngle = physicsAngle;
        Vector2 p = root;
        for (int i = 0; i < preset.oscillators.Count; i++)
        {
            SkyPrisonPhysicsOscillator osc = preset.oscillators[i];
            if (osc == null) continue;
            float section = (i + 1f) / Mathf.Max(1f, preset.oscillators.Count);
            Vector3 rotatedDir3 = Quaternion.Euler(0f, 0f, accumAngle * section) * new Vector3(dir.x, dir.y, 0f);
            Vector2 sectionDir = new Vector2(rotatedDir3.x, rotatedDir3.y);
            float length = Mathf.Max(3f, osc.length * Mathf.Max(0.1f, preset.globalScale) * Mathf.Lerp(0.75f, 1.15f, strength));
            p += sectionDir.normalized * length;
            points.Add(p);
        }
        return points;
    }


    private SkyPrisonAnimationRigRow ResolvePhysicsSourceRow(SkyPrisonAnimationRigRow psbRow)
    {
        if (psbRow == null)
            return null;

        if (psbRow.usePhysicsInfluence)
            return psbRow;

        if (!string.IsNullOrEmpty(psbRow.boundRigKey) && state != null)
        {
            SkyPrisonAnimationRigRow boundRig = state.FindRigRow(psbRow.boundRigKey);
            if (boundRig != null && boundRig.usePhysicsInfluence)
                return boundRig;
        }

        return null;
    }

    private bool TryResolvePhysicsDriverSegment(
        SkyPrisonAnimationRigRow psbRow,
        SkyPrisonAnimationRigRow physicsSource,
        Dictionary<string, RigBoneSegment> restSegments,
        Dictionary<string, RigBoneSegment> currentSegments,
        out RigBoneSegment restDriver,
        out RigBoneSegment currentDriver)
    {
        restDriver = new RigBoneSegment();
        currentDriver = new RigBoneSegment();
        if (restSegments == null || currentSegments == null)
            return false;

        string key = physicsSource != null ? physicsSource.key : string.Empty;
        if (!string.IsNullOrEmpty(key) && TryGetSegmentPair(ResolveDriverSegmentKeyForBoundRig(key), restSegments, currentSegments, out restDriver, out currentDriver))
            return true;

        if (physicsSource != null)
        {
            string parentSegment = ResolveTransformOnlyRigParentSegmentKey(physicsSource, currentSegments);
            if (!string.IsNullOrEmpty(parentSegment) && TryGetSegmentPair(parentSegment, restSegments, currentSegments, out restDriver, out currentDriver))
                return true;
        }

        if (psbRow != null && !string.IsNullOrEmpty(psbRow.boundRigKey))
        {
            string boundSegment = ResolveDriverSegmentKeyForBoundRig(psbRow.boundRigKey);
            if (TryGetSegmentPair(boundSegment, restSegments, currentSegments, out restDriver, out currentDriver))
                return true;

            SkyPrisonAnimationRigRow boundRig = state != null ? state.FindRigRow(psbRow.boundRigKey) : null;
            if (boundRig != null)
            {
                string parentSegment = ResolveTransformOnlyRigParentSegmentKey(boundRig, currentSegments);
                if (!string.IsNullOrEmpty(parentSegment) && TryGetSegmentPair(parentSegment, restSegments, currentSegments, out restDriver, out currentDriver))
                    return true;
            }
        }

        return false;
    }

    private float StableHash01(string text)
    {
        unchecked
        {
            int hash = 23;
            if (!string.IsNullOrEmpty(text))
            {
                for (int i = 0; i < text.Length; i++)
                    hash = hash * 31 + text[i];
            }
            hash &= 0x7fffffff;
            return (hash % 10000) / 10000f;
        }
    }

    private bool TryBuildPsbSpriteDrawStateFromTransformOnlyRigNode(
        string boundRigKey,
        Vector2 originalCenter,
        Vector2 drawSize,
        Dictionary<string, RigBoneSegment> restSegments,
        Dictionary<string, RigBoneSegment> currentSegments,
        out PsbSpriteDrawState drawState)
    {
        drawState = new PsbSpriteDrawState
        {
            center = originalCenter,
            size = drawSize,
            angle = 0f,
            rect = GetSpriteDrawRect(originalCenter, drawSize)
        };

        if (string.IsNullOrEmpty(boundRigKey) || state == null || restSegments == null || currentSegments == null)
            return false;

        SkyPrisonAnimationRigRow rigNode = state.FindRigRow(boundRigKey);
        if (rigNode == null)
            return false;

        // 有自己骨架线/骨骼段的节点前面已经处理过。这里仅处理无骨架线的空节点/挂点。
        if (IsCustomRigSegmentRow(rigNode) || TryGetSegmentPair(boundRigKey, restSegments, currentSegments, out _, out _))
            return false;

        string parentSegmentKey = ResolveTransformOnlyRigParentSegmentKey(rigNode, currentSegments);
        if (string.IsNullOrEmpty(parentSegmentKey))
            return false;

        if (!TryGetSegmentPair(parentSegmentKey, restSegments, currentSegments, out RigBoneSegment restParent, out RigBoneSegment currentParent))
            return false;

        Vector2 restVector = restParent.head - restParent.root;
        Vector2 currentVector = currentParent.head - currentParent.root;
        if (restVector.sqrMagnitude < 0.0001f || currentVector.sqrMagnitude < 0.0001f)
            return false;

        drawState.center = TransformPointByParentSegment(originalCenter, restParent, currentParent);
        drawState.angle = Vector2.SignedAngle(restVector, currentVector);
        drawState.rect = GetSpriteDrawRect(drawState.center, drawSize);
        return true;
    }

    private string ResolveTransformOnlyRigParentSegmentKey(SkyPrisonAnimationRigRow rigNode, Dictionary<string, RigBoneSegment> segments)
    {
        if (rigNode == null || segments == null)
            return string.Empty;

        string key = rigNode.parentKey;
        HashSet<string> guard = new HashSet<string>();

        while (!string.IsNullOrEmpty(key) && key != rigNode.key && guard.Add(key))
        {
            if (segments.ContainsKey(key))
                return key;

            string incoming = GetIncomingRigSegmentKeyForEndpoint(key);
            if (!string.IsNullOrEmpty(incoming) && segments.ContainsKey(incoming))
                return incoming;

            SkyPrisonAnimationRigRow parentRow = state.FindRigRow(key);
            if (parentRow == null)
                break;

            if (!string.IsNullOrEmpty(parentRow.boundRigKey))
            {
                if (segments.ContainsKey(parentRow.boundRigKey))
                    return parentRow.boundRigKey;

                incoming = GetIncomingRigSegmentKeyForEndpoint(parentRow.boundRigKey);
                if (!string.IsNullOrEmpty(incoming) && segments.ContainsKey(incoming))
                    return incoming;
            }

            key = parentRow.parentKey;
        }

        return string.Empty;
    }

    private PsbSpriteDrawState BuildPsbSpriteDrawStateOnSegment(
        Vector2 originalCenter,
        Vector2 drawSize,
        Vector2 restHead,
        Vector2 restTail,
        Vector2 currentHead,
        Vector2 currentTail)
    {
        PsbSpriteDrawState drawState = new PsbSpriteDrawState
        {
            center = originalCenter,
            size = drawSize,
            angle = 0f,
            rect = GetSpriteDrawRect(originalCenter, drawSize)
        };

        Vector2 restVector = restTail - restHead;
        Vector2 currentVector = currentTail - currentHead;
        float restLength = restVector.magnitude;
        float currentLength = currentVector.magnitude;

        if (restLength < 0.0001f || currentLength < 0.0001f)
        {
            Vector2 fallbackDelta = Vector2.Lerp(currentHead - restHead, currentTail - restTail, 0.55f);
            drawState.center = originalCenter + fallbackDelta;
            drawState.rect = GetSpriteDrawRect(drawState.center, drawSize);
            return drawState;
        }

        Vector2 restDir = restVector / restLength;
        Vector2 currentDir = currentVector / currentLength;
        Vector2 restToCenter = originalCenter - restHead;

        float along = Vector2.Dot(restToCenter, restDir);
        float side = Cross(restDir, restToCenter);
        Vector2 currentPerp = new Vector2(-currentDir.y, currentDir.x);

        // 只做刚体式平移 + 旋转，不做长度缩放。
        // 骨段长短变化只改变控制轴，不把贴图当橡皮筋拉伸。
        drawState.center = currentHead + currentDir * along + currentPerp * side;
        drawState.angle = Vector2.SignedAngle(restVector, currentVector);
        drawState.rect = GetSpriteDrawRect(drawState.center, drawSize);
        return drawState;
    }

    private bool IsUpperTorsoRigKey(string rigKey)
    {
        return rigKey == "Spine"
            || rigKey == "TorsoUpper"
            || rigKey == "UpperTorso"
            || rigKey == "torso_upper"
            || rigKey == "BodyUpper"
            || rigKey == "UpperBody";
    }

    private bool IsLowerTorsoRigKey(string rigKey)
    {
        return rigKey == "Pelvis"
            || rigKey == "TorsoLower"
            || rigKey == "LowerTorso"
            || rigKey == "torso_lower"
            || rigKey == "BodyLower"
            || rigKey == "LowerBody"
            || rigKey == "Abdomen"
            || rigKey == "Waist";
    }

    private bool IsUpperTorsoRig(SkyPrisonAnimationRigRow row, string rigKey)
    {
        if (IsUpperTorsoRigKey(rigKey))
            return true;

        string token = GetRigLayerIdentityToken(row);
        return token.Contains("torso_upper")
            || token.Contains("torsoupper")
            || token.Contains("upper_torso")
            || token.Contains("uppertorso")
            || token.Contains("spine_upper")
            || token.Contains("upperbody")
            || token.Contains("bodyupper");
    }

    private bool IsLowerTorsoRig(SkyPrisonAnimationRigRow row, string rigKey)
    {
        if (IsLowerTorsoRigKey(rigKey))
            return true;

        string token = GetRigLayerIdentityToken(row);
        return token.Contains("torso_lower")
            || token.Contains("torsolower")
            || token.Contains("lower_torso")
            || token.Contains("lowertorso")
            || token.Contains("pelvis")
            || token.Contains("waist")
            || token.Contains("abdomen")
            || token.Contains("lowerbody")
            || token.Contains("bodylower");
    }

    private string GetRigLayerIdentityToken(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return string.Empty;

        return ((row.key ?? string.Empty) + " "
            + (row.name ?? string.Empty) + " "
            + (row.semantic ?? string.Empty) + " "
            + (row.sourceSpriteName ?? string.Empty) + " "
            + (row.sourceLayerPath ?? string.Empty) + " "
            + (row.boundRigKey ?? string.Empty) + " "
            + (row.boundRigName ?? string.Empty)).ToLowerInvariant();
    }

    private float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }


    private bool IsPsbLayerEffectivelyVisible(SkyPrisonAnimationRigRow psbRow)
    {
        if (psbRow == null || !psbRow.visible)
            return false;

        if (string.IsNullOrEmpty(psbRow.boundRigKey))
            return true;

        // Rig 被删除时，PSB 图层不能跟着消失；只解除驱动关系，图像仍然可见。
        SkyPrisonAnimationRigRow rig = state.FindRigRow(psbRow.boundRigKey);
        if (rig == null)
            return true;

        return IsRigRowEffectivelyVisible(psbRow.boundRigKey);
    }


    private bool IsPsbDyeMaskLayer(SkyPrisonAnimationRigRow psbRow)
    {
        if (psbRow == null)
            return false;

        // *_dyeMask 是服装染色通道区域，不是角色可见图层。
        // 它应该被染色系统读取 RGB 通道：B=强调色、R=主色、G=副色；
        // 但不能进入普通预览绘制队列，否则会污染排序、点击框、包围盒，甚至看起来像袖子被其它层穿插。
        string identity = string.Join("/", new string[]
        {
            psbRow.key ?? string.Empty,
            psbRow.name ?? string.Empty,
            psbRow.semantic ?? string.Empty,
            psbRow.sourceSpriteName ?? string.Empty,
            psbRow.sourceLayerPath ?? string.Empty,
            psbRow.appearanceLayerKey ?? string.Empty,
            psbRow.appearanceSlotKey ?? string.Empty,
        });

        string lower = identity.ToLowerInvariant();
        string compact = CompactLayerText(identity);

        return lower.Contains("_dyemask") ||
               lower.Contains("dye_mask") ||
               compact.Contains("dyemask") ||
               compact.Contains("dyecolorregion") ||
               compact.Contains("colorregionmask") ||
               identity.Contains("染色遮罩") ||
               identity.Contains("染色マスク");
    }

    private float GetEffectivePsbLayerOpacity(SkyPrisonAnimationRigRow psbRow)
    {
        if (psbRow == null)
            return 1f;

        float alpha = Mathf.Clamp01(state.EvaluateTimelineOpacity(psbRow.key, psbRow.opacity));

        if (!string.IsNullOrEmpty(psbRow.boundRigKey))
        {
            SkyPrisonAnimationRigRow rig = state.FindRigRow(psbRow.boundRigKey);
            if (rig != null)
                alpha *= Mathf.Clamp01(state.EvaluateTimelineOpacity(rig.key, rig.opacity));
        }

        return Mathf.Clamp01(alpha);
    }

    private bool IsRigRowEffectivelyVisible(string rigKey)
    {
        SkyPrisonAnimationRigRow rig = state.FindRigRow(rigKey);
        if (rig == null)
            return false;

        int guard = 0;
        while (rig != null && guard++ < 256)
        {
            if (!rig.visible)
                return false;

            if (string.IsNullOrEmpty(rig.parentKey))
                break;

            rig = state.FindRigRow(rig.parentKey);
        }

        return true;
    }

    private bool IsRigSegmentVisible(string fromKey, string toKey)
    {
        return IsRigRowEffectivelyVisible(fromKey) && IsRigRowEffectivelyVisible(toKey);
    }

    private bool IsPreviewFocusModeActive()
    {
        return state != null
            && !state.ShowRigEdit
            && state.TimelineTrackLockEnabled
            && !string.IsNullOrEmpty(state.ActiveTimelineTrackKey);
    }

    private string GetPreviewFocusRigKey()
    {
        if (!IsPreviewFocusModeActive())
            return string.Empty;

        return state.ResolveActivePreviewFocusRigKey();
    }

    private bool IsPreviewFocusTarget(string key)
    {
        if (!IsPreviewFocusModeActive() || string.IsNullOrEmpty(key))
            return true;

        string focusKey = GetPreviewFocusRigKey();
        if (string.IsNullOrEmpty(focusKey))
            return true;

        if (key == focusKey || key == state.ActiveTimelineTrackKey)
            return true;

        SkyPrisonAnimationRigRow row = state.FindAnyStructureRow(key);
        return row != null && row.boundRigKey == focusKey;
    }

    private float GetPreviewFocusAlphaForPsbRow(SkyPrisonAnimationRigRow row)
    {
        // 聚焦模式只淡化骨架线，不淡化 PSB 图像。
        // PSB 是角色实际外观，淡掉以后反而看不出当前姿态是否自然。
        return 1f;
    }

    private bool IsPreviewFocusSegment(RigBoneSegment seg)
    {
        if (!IsPreviewFocusModeActive())
            return true;

        HashSet<string> focusSegments = GetPreviewEditableSegmentKeys();
        if (focusSegments == null || focusSegments.Count == 0)
            return true;

        // 焦点判断只允许命中“当前实际可编辑的骨骼段”。
        // 不能再把 rootKey / headKey 也算作焦点，否则父节点、共享关节连接的父骨骼线也会一起变亮。
        return focusSegments.Contains(seg.segmentKey);
    }

    private HashSet<string> GetPreviewEditableSegmentKeys()
    {
        HashSet<string> keys = new HashSet<string>();

        if (!IsPreviewFocusModeActive())
            return keys;

        // 只保留“当前节点自己”和它解析出来的实际可编辑骨骼段。
        // 不递归父节点 / 子节点，也不因为共享 root/head 让相邻线发亮。
        string activeKey = state != null ? state.ActiveTimelineTrackKey : string.Empty;
        if (!string.IsNullOrEmpty(activeKey))
            keys.Add(activeKey);

        string onlySegmentKey = GetPreviewOnlyEditableSegmentKey();
        if (!string.IsNullOrEmpty(onlySegmentKey))
            keys.Add(onlySegmentKey);

        return keys;
    }

    private string GetPreviewOnlyEditableSegmentKey()
    {
        if (!IsPreviewFocusModeActive())
            return string.Empty;

        // 视觉聚焦必须和真正可拖拽/可写入的轨道锁保持一致：
        // 只亮当前活动轨道对应的“单条骨骼段”。
        // 不再把父节点、子节点、共享 root/head 的相邻骨骼段一起加入焦点，
        // 否则会出现父子节点都发亮，用户仍然分不清当前能编辑哪条线。
        string activeKey = state != null ? state.ActiveTimelineTrackKey : string.Empty;
        if (string.IsNullOrEmpty(activeKey))
            return string.Empty;

        SkyPrisonAnimationRigRow activeRow = state.FindAnyStructureRow(activeKey);
        if (activeRow != null)
        {
            // 如果轨道来自 PSB/Socket 等绑定行，只解析到它真正驱动的 Rig 段。
            // 注意这里也只返回一条，不继续扩散到父/子。
            if (!string.IsNullOrEmpty(activeRow.boundRigKey))
                return ResolveDriverSegmentKeyForBoundRig(activeRow.boundRigKey);

            // 普通 Rig 行直接使用自身 key。
            // HeadTop / Foot / HandEnd 这种端点行需要解析到真正可编辑的段。
            return ResolveDriverSegmentKeyForBoundRig(activeRow.key);
        }

        return ResolveDriverSegmentKeyForBoundRig(activeKey);
    }

    private Color ApplyPreviewFocusColor(Color color, bool focused)
    {
        if (!IsPreviewFocusModeActive() || focused)
            return color;

        Color faded = Color.Lerp(color, Color.white, 0.88f);
        faded.a = Mathf.Min(color.a, 0.16f);
        return faded;
    }

    private string GetCurrentPsbAssetPath()
    {
        if (!string.IsNullOrEmpty(state.SourcePsdAssetPath))
            return state.SourcePsdAssetPath;

        if (state.PsbRows != null)
        {
            for (int i = 0; i < state.PsbRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.PsbRows[i];
                if (row != null && !string.IsNullOrEmpty(row.sourceAssetPath))
                    return row.sourceAssetPath;
            }
        }

        return string.Empty;
    }

    private PsbSpriteLayout FindPsbSpriteLayout(PsbPrefabLayout layout, SkyPrisonAnimationRigRow row)
    {
        if (layout == null || row == null)
            return null;

        if (!string.IsNullOrEmpty(row.sourceSpriteName) && layout.bySpriteName.TryGetValue(row.sourceSpriteName, out PsbSpriteLayout bySprite))
            return bySprite;

        if (!string.IsNullOrEmpty(row.name) && layout.byObjectName.TryGetValue(row.name, out PsbSpriteLayout byObject))
            return byObject;

        return null;
    }

    private PsbPrefabLayout GetPsbLayoutForDraw(string assetPath, Dictionary<string, PsbPrefabLayout> cache)
    {
        if (string.IsNullOrEmpty(assetPath))
            assetPath = GetCurrentPsbAssetPath();

        if (cache != null && cache.TryGetValue(assetPath ?? string.Empty, out PsbPrefabLayout cached))
            return cached;

        PsbPrefabLayout layout = LoadPsbPrefabLayout(assetPath);
        if (cache != null)
            cache[assetPath ?? string.Empty] = layout;
        return layout;
    }

    private PsbPrefabLayout LoadPsbPrefabLayout(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;

        Hash128 assetHash = AssetDatabase.GetAssetDependencyHash(assetPath);
        if (PsbPrefabLayoutCache.TryGetValue(assetPath, out PsbPrefabLayout cached) && cached != null && cached.assetHash == assetHash)
            return cached;

        PsbPrefabLayout layout = new PsbPrefabLayout { assetHash = assetHash };
        PsbPrefabLayoutCache[assetPath] = layout;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        GameObject bestRoot = null;
        SpriteRenderer[] bestRenderers = null;

        for (int i = 0; i < assets.Length; i++)
        {
            GameObject go = assets[i] as GameObject;
            if (go == null)
                continue;

            SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers == null || renderers.Length == 0)
                continue;

            if (bestRenderers == null || renderers.Length > bestRenderers.Length)
            {
                bestRoot = go;
                bestRenderers = renderers;
            }
        }

        if (bestRoot == null || bestRenderers == null || bestRenderers.Length == 0)
        {
            layout.valid = false;
            return layout;
        }

        bool hasBounds = false;
        Rect bounds = new Rect();

        for (int i = 0; i < bestRenderers.Length; i++)
        {
            SpriteRenderer sr = bestRenderers[i];
            if (sr == null || sr.sprite == null)
                continue;

            Sprite sprite = sr.sprite;
            Vector3 local = bestRoot.transform.InverseTransformPoint(sr.transform.position);
            Vector3 lossy = sr.transform.lossyScale;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);

            Vector2 size = new Vector2(
                Mathf.Max(0.0001f, sprite.rect.width / ppu * Mathf.Abs(lossy.x)),
                Mathf.Max(0.0001f, sprite.rect.height / ppu * Mathf.Abs(lossy.y)));

            Vector2 center = new Vector2(local.x, local.y);
            Rect r = Rect.MinMaxRect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, center.x + size.x * 0.5f, center.y + size.y * 0.5f);
            bounds = hasBounds ? Union(bounds, r) : r;
            hasBounds = true;

            int hierarchyOrder = GetHierarchyOrder(sr.transform);
            PsbSpriteLayout item = new PsbSpriteLayout
            {
                sprite = sprite,
                localCenter = center,
                localSize = size,
                hierarchyOrder = hierarchyOrder,
                sortingOrder = sr.sortingOrder,
                sortingLayerName = sr.sortingLayerName,
                prefabLayerWeight = sr.sortingOrder * 100000f + hierarchyOrder
            };

            layout.bySpriteName[sprite.name] = item;
            layout.byObjectName[sr.gameObject.name] = item;
        }

        layout.bounds = bounds;
        layout.valid = hasBounds && layout.bySpriteName.Count > 0;
        return layout;
    }

    private int GetHierarchyOrder(Transform t)
    {
        int order = 0;
        int mul = 1;
        Transform cur = t;
        while (cur != null)
        {
            order += cur.GetSiblingIndex() * mul;
            mul *= 100;
            cur = cur.parent;
        }
        return order;
    }

    private void DrawPsbLayoutWarning(Rect localView)
    {
        Rect warningRect = new Rect(localView.x + 10f, localView.y + 10f, Mathf.Min(520f, localView.width - 20f), 58f);
        EditorGUI.HelpBox(
            warningRect,
            "没有读取到 PSB 的 Character Rig / Prefab 图层坐标。\n请在 PSB Importer 中开启 Character Rig / Use Layer Group，并 Apply 后重新拖入。",
            MessageType.Warning);
    }

    private Rect Union(Rect a, Rect b)
    {
        float xMin = Mathf.Min(a.xMin, b.xMin);
        float yMin = Mathf.Min(a.yMin, b.yMin);
        float xMax = Mathf.Max(a.xMax, b.xMax);
        float yMax = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private Vector2 PsbLocalToPreview(Vector2 localCenter, Rect psbBounds, Vector2 previewCenter, float scale)
    {
        Vector2 psbCenter = psbBounds.center;
        float x = (localCenter.x - psbCenter.x) * scale;
        float y = -(localCenter.y - psbCenter.y) * scale;
        return previewCenter + new Vector2(x, y);
    }

    private Dictionary<string, Vector2> BuildRigAnchorMap(HumanPose pose, float z)
    {
        Dictionary<string, Vector2> map = new Dictionary<string, Vector2>();

        Vector2 pelvis = pose.body + new Vector2(0f, 22f * z);
        Vector2 spine = pose.body + new Vector2(0f, -8f * z);
        Vector2 chest = pose.body + new Vector2(0f, -34f * z);
        Vector2 neck = Vector2.Lerp(chest, pose.head, 0.60f);
        Vector2 headTop = pose.head + new Vector2(0f, -34f * z);

        map["Root"] = pose.body;
        map["Pelvis"] = pelvis;
        map["Spine"] = spine;
        map["Chest"] = chest;
        map["Neck"] = neck;
        map["Head"] = pose.head;
        map["HeadTop"] = headTop;

        AddHumanoidV1LimbAnchors(map, "L", neck, pose.armL, pelvis, pose.legL, z);
        AddHumanoidV1LimbAnchors(map, "R", neck, pose.armR, pelvis, pose.legR, z);

        map["Body"] = pose.body;
        map["Core"] = pose.core;
        return map;
    }

    private void AddHumanoidV1LimbAnchors(Dictionary<string, Vector2> map, string suffix, Vector2 neck, Vector2 handEnd, Vector2 pelvis, Vector2 foot, float z)
    {
        bool left = suffix == "L";
        // L/R 使用“角色自身左右”。角色正面朝屏幕时：L 在画面右侧，R 在画面左侧。
        float side = left ? 1f : -1f;

        Vector2 shoulderFallback = neck + new Vector2(side * 38f * z, 18f * z);
        Vector2 elbow = Vector2.Lerp(shoulderFallback, handEnd, 0.48f);
        Vector2 wrist = Vector2.Lerp(shoulderFallback, handEnd, 0.82f);

        Vector2 hip = pelvis + new Vector2(side * 18f * z, 10f * z);
        Vector2 knee = Vector2.Lerp(hip, foot, 0.48f);
        Vector2 ankle = Vector2.Lerp(hip, foot, 0.84f);

        map["Shoulder_" + suffix] = shoulderFallback;
        map["Elbow_" + suffix] = elbow;
        map["Wrist_" + suffix] = wrist;
        map["HandEnd_" + suffix] = handEnd;
        map["Hip_" + suffix] = hip;
        map["Knee_" + suffix] = knee;
        map["Ankle_" + suffix] = ankle;
        map["Foot_" + suffix] = foot;
    }



    private sealed class PsbRigSample
    {
        public string rigKey;
        public string name;
        public string searchName;
        public Vector2 center;
        public Vector2 size;
        public Rect rect;
        public Sprite sprite;
    }

    private struct LimbAlphaBand
    {
        public Vector2 center;
        public float leftX;
        public float rightX;
        public float width;
        public bool valid;
    }

    private struct LimbAxisMeasure
    {
        public bool valid;
        public Vector2 near;
        public Vector2 far;
        public Vector2 center;
        public Vector2 dir;
        public float length;
        public int count;
    }

    private Dictionary<string, Vector2> BuildDisplayRigAnchorMap(HumanPose pose, Vector2 center, Rect localView, float z)
    {
        return BuildDisplayRigAnchorMap(pose, center, localView, z, true, !state.ShowRigEdit);
    }

    private Dictionary<string, Vector2> BuildDisplayRigAnchorMap(HumanPose pose, Vector2 center, Rect localView, float z, bool includeSetupOffsets, bool includeRuntimeOffsets)
    {
        Dictionary<string, Vector2> generated = BuildRigAnchorMap(pose, z);
        generated["__PreviewCenter"] = center;

        string psbAssetPath = GetCurrentPsbAssetPath();
        PsbPrefabLayout layout = LoadPsbPrefabLayout(psbAssetPath);
        if (layout == null || !layout.valid || layout.bounds.width <= 0.0001f || layout.bounds.height <= 0.0001f)
            return ApplyRigOffsetsForContext(generated, z, includeSetupOffsets, includeRuntimeOffsets);

        HumanPose restPose = BuildBasePose(center, z);
        Dictionary<string, Vector2> currentAnchors = BuildRigAnchorMap(pose, z);
        Dictionary<string, Vector2> restAnchors = BuildRigAnchorMap(restPose, z);

        float fitScale = Mathf.Min((localView.width * 0.72f) / Mathf.Max(0.0001f, layout.bounds.width),
                                   (localView.height * 0.88f) / Mathf.Max(0.0001f, layout.bounds.height));
        fitScale = Mathf.Clamp(fitScale, 4f, 900f) * Mathf.Clamp(state.PreviewZoom, 0.1f, 5f);

        Vector2 previewA = PsbLocalToPreview(new Vector2(layout.bounds.xMin, layout.bounds.yMin), layout.bounds, center, fitScale);
        Vector2 previewB = PsbLocalToPreview(new Vector2(layout.bounds.xMax, layout.bounds.yMax), layout.bounds, center, fitScale);

        float topY = Mathf.Min(previewA.y, previewB.y);
        float bottomY = Mathf.Max(previewA.y, previewB.y);
        float leftX = Mathf.Min(previewA.x, previewB.x);
        float rightX = Mathf.Max(previewA.x, previewB.x);
        float characterHeight = Mathf.Max(1f, bottomY - topY);
        float characterWidth = Mathf.Max(1f, rightX - leftX);
        float centerX = (leftX + rightX) * 0.5f;

        Dictionary<string, List<PsbRigSample>> samples = CollectPsbRigSamples(layout, center, fitScale);
        Dictionary<string, Vector2> result = new Dictionary<string, Vector2>(generated);
        result["__PreviewCenter"] = center;

        System.Func<string, Vector2> motion = key =>
        {
            if (currentAnchors.TryGetValue(key, out Vector2 cur) && restAnchors.TryGetValue(key, out Vector2 rest))
                return cur - rest;
            return Vector2.zero;
        };

        // 中轴骨点不再只按固定百分比套模板。
        // 这里先测量角色实际的头 / 躯干 / 下半身范围，再用可调参数生成骨点。
        // 目标：不同身高、不同头身比的角色导入时，Head / Neck / Chest / Spine / Pelvis 更贴合图层。
        Rect bodyRect;
        Rect headRect;
        Rect neckRect;
        Rect chestRect;
        Rect pelvisRect;

        bool hasBody = TryUnionSampleRect(samples, out bodyRect, "Spine", "Chest", "Pelvis");
        bool hasHead = TryUnionSampleRect(samples, out headRect, "Head");
        bool hasNeck = TryUnionSampleRect(samples, out neckRect, "Neck");
        bool hasChest = TryUnionSampleRect(samples, out chestRect, "Chest");
        bool hasPelvis = TryUnionSampleRect(samples, out pelvisRect, "Pelvis");

        float spineX = centerX;
        if (hasBody)
            spineX = Mathf.Lerp(spineX, bodyRect.center.x, 0.72f);
        else if (hasHead)
            spineX = Mathf.Lerp(spineX, headRect.center.x, 0.48f);

        spineX = Mathf.Clamp(spineX, leftX + characterWidth * 0.30f, rightX - characterWidth * 0.30f);

        // --- 可调参数区 -----------------------------------------------------
        // 所有参数都是“测量后再套比例”，不是 AXIA 固定像素。
        // 头部长度：HeadTop -> Head。通常 Q 版头大，所以 Head 点放在头部偏下。
        const float HeadTopInHeadRect = 0.080f;
        const float HeadPointInHeadRect = 0.520f;

        // 脖子长度：Head -> Neck。不能再用 HeadRect 底部硬推，否则会变成大长脖子。
        const float NeckLengthByHead = 0.115f;
        const float NeckLengthByCharacter = 0.040f;

        // 身体分段：Neck -> Chest -> Spine -> Pelvis。
        // 这些比例基于 bodyRect 的实际高度，bodyRect 不可靠时再用角色总高度兜底。
        const float ChestFromBodyTop = 0.185f;
        const float SpineFromBodyTop = 0.485f;
        const float PelvisFromBodyBottom = 0.000f;

        float headH = hasHead ? Mathf.Max(1f, headRect.height) : characterHeight * 0.32f;
        float bodyTop = hasBody ? bodyRect.yMin : topY + characterHeight * 0.365f;
        float bodyBottom = hasBody ? bodyRect.yMax : bottomY - characterHeight * 0.035f;
        float bodyH = Mathf.Max(1f, bodyBottom - bodyTop);

        Vector2 headTop = hasHead
            ? new Vector2(spineX, headRect.yMin + headH * HeadTopInHeadRect)
            : new Vector2(spineX, topY + characterHeight * 0.055f);

        Vector2 head = hasHead
            ? new Vector2(spineX, headRect.yMin + headH * HeadPointInHeadRect)
            : new Vector2(spineX, topY + characterHeight * 0.285f);

        float measuredNeckLength = Mathf.Min(headH * NeckLengthByHead, characterHeight * NeckLengthByCharacter);
        measuredNeckLength = Mathf.Max(measuredNeckLength, 5f * z);

        Vector2 neck = new Vector2(spineX, head.y + measuredNeckLength);
        if (hasNeck)
            neck.y = Mathf.Lerp(neck.y, neckRect.center.y, 0.58f);
        else if (hasHead)
        {
            // neck 允许靠近头部底边，但不要被 headRect 的长发 / 耳发拖到太下面。
            float maxNeckByHead = headRect.yMin + headH * 0.835f;
            neck.y = Mathf.Min(neck.y, maxNeckByHead);
        }

        Vector2 chest = new Vector2(spineX, bodyTop + bodyH * ChestFromBodyTop);
        if (hasChest)
            chest.y = Mathf.Lerp(chest.y, chestRect.center.y, 0.62f);
        chest.y = Mathf.Max(chest.y, neck.y + Mathf.Max(6f * z, characterHeight * 0.030f));

        Vector2 spine = new Vector2(spineX, bodyTop + bodyH * SpineFromBodyTop);
        spine.y = Mathf.Max(spine.y, chest.y + Mathf.Max(10f * z, bodyH * 0.185f));

        Vector2 pelvis = new Vector2(spineX, bodyBottom - bodyH * PelvisFromBodyBottom);
        if (hasPelvis)
        {
            // Pelvis 语义是下半身末端/髋部基准，不取中心，优先贴近下端。
            float pelvisBottomPoint = pelvisRect.yMax;
            pelvis.y = Mathf.Lerp(pelvis.y, pelvisBottomPoint, 0.92f);
        }
        pelvis.y = Mathf.Max(pelvis.y, spine.y + Mathf.Max(12f * z, bodyH * 0.180f));
        pelvis.y = Mathf.Min(pelvis.y, bottomY);

        // 保证中轴顺序和最小骨长。这里用“参数化最小长度”，避免小体型角色被固定像素撑坏。
        float minHead = Mathf.Max(8f * z, characterHeight * 0.050f);
        float minNeck = Mathf.Max(4f * z, characterHeight * 0.022f);
        float minChest = Mathf.Max(8f * z, characterHeight * 0.055f);
        float minSpine = Mathf.Max(10f * z, characterHeight * 0.075f);
        float minPelvis = Mathf.Max(10f * z, characterHeight * 0.080f);

        head.y = Mathf.Max(head.y, headTop.y + minHead);
        neck.y = Mathf.Max(neck.y, head.y + minNeck);
        chest.y = Mathf.Max(chest.y, neck.y + minChest);
        spine.y = Mathf.Max(spine.y, chest.y + minSpine);
        pelvis.y = Mathf.Max(pelvis.y, spine.y + minPelvis);
        pelvis.y = Mathf.Min(pelvis.y, bottomY);

        headTop.x = head.x = neck.x = chest.x = spine.x = pelvis.x = spineX;

        result["Root"] = pelvis + motion("Root") * 0.10f;
        result["Pelvis"] = pelvis + motion("Pelvis") * 0.12f;
        result["Spine"] = spine + motion("Spine") * 0.12f;
        result["Chest"] = chest + motion("Chest") * 0.12f;
        result["Neck"] = neck + motion("Neck") * 0.10f;
        result["Head"] = head + motion("Head") * 0.10f;
        result["HeadTop"] = headTop + motion("HeadTop") * 0.08f;

        // 先给腿保留稳定占位，避免旧坐标把画面拉乱。
        // 然后覆盖双手：手臂分支从 Chest/肩颈空间长出，优先读取上臂图层的上沿作为 Shoulder。
        PinLimbPlaceholdersToCore(result, characterWidth, characterHeight);

        // L/R 使用角色自身左右：角色正面朝屏幕时，L 在画面右侧，R 在画面左侧。
        BuildStableArm(result, samples, generated, motion, "L", 1f, characterWidth, characterHeight);
        BuildStableArm(result, samples, generated, motion, "R", -1f, characterWidth, characterHeight);

        // 双腿分支：从 Pelvis / 下半身两侧生长。
        // L/R 仍然按角色自身左右：正面角色的 L 在画面右侧，R 在画面左侧。
        // 有大腿/小腿/脚图层时优先用 Bounds 测量；否则退回骨盆宽度参数。
        BuildStableLeg(result, samples, generated, motion, "L", 1f, characterWidth, characterHeight);
        BuildStableLeg(result, samples, generated, motion, "R", -1f, characterWidth, characterHeight);

        return ApplyRigOffsetsForContext(result, z, includeSetupOffsets, includeRuntimeOffsets);
    }

    private bool TryUnionSampleRect(Dictionary<string, List<PsbRigSample>> samples, out Rect rect, params string[] rigKeys)
    {
        rect = new Rect();
        bool has = false;
        if (samples == null || rigKeys == null)
            return false;

        for (int k = 0; k < rigKeys.Length; k++)
        {
            if (string.IsNullOrEmpty(rigKeys[k]))
                continue;
            if (!samples.TryGetValue(rigKeys[k], out List<PsbRigSample> list) || list == null)
                continue;

            for (int i = 0; i < list.Count; i++)
            {
                PsbRigSample s = list[i];
                if (s == null)
                    continue;
                rect = has ? Union(rect, s.rect) : s.rect;
                has = true;
            }
        }

        return has;
    }

    private void PinLimbPlaceholdersToCore(Dictionary<string, Vector2> result, float characterWidth, float characterHeight)
    {
        if (result == null || !result.ContainsKey("Neck") || !result.ContainsKey("Pelvis"))
            return;

        Vector2 neck = result["Neck"];
        Vector2 chest = result.ContainsKey("Chest") ? result["Chest"] : neck + new Vector2(0f, characterHeight * 0.10f);
        Vector2 spine = result.ContainsKey("Spine") ? result["Spine"] : chest + new Vector2(0f, characterHeight * 0.11f);
        Vector2 pelvis = result["Pelvis"];

        SetLimbPlaceholder(result, "L", 1f, neck, chest, pelvis, characterWidth, characterHeight);
        SetLimbPlaceholder(result, "R", -1f, neck, chest, pelvis, characterWidth, characterHeight);
    }

    private void SetLimbPlaceholder(Dictionary<string, Vector2> result, string suffix, float side, Vector2 neck, Vector2 chest, Vector2 pelvis, float characterWidth, float characterHeight)
    {
        Vector2 shoulder = Vector2.Lerp(neck, chest, 0.35f) + new Vector2(side * characterWidth * 0.135f, characterHeight * 0.010f);
        Vector2 hand = shoulder + new Vector2(side * characterWidth * 0.100f, characterHeight * 0.230f);
        result["Shoulder_" + suffix] = shoulder;
        result["Elbow_" + suffix] = Vector2.Lerp(shoulder, hand, 0.48f);
        result["Wrist_" + suffix] = Vector2.Lerp(shoulder, hand, 0.82f);
        result["HandEnd_" + suffix] = hand;

        Vector2 hip = pelvis + new Vector2(side * characterWidth * 0.060f, characterHeight * 0.018f);
        Vector2 foot = hip + new Vector2(side * characterWidth * 0.018f, characterHeight * 0.285f);
        result["Hip_" + suffix] = hip;
        result["Knee_" + suffix] = Vector2.Lerp(hip, foot, 0.48f);
        result["Ankle_" + suffix] = Vector2.Lerp(hip, foot, 0.84f);
        result["Foot_" + suffix] = foot;
    }

    private Dictionary<string, List<PsbRigSample>> CollectPsbRigSamples(PsbPrefabLayout layout, Vector2 center, float fitScale)
    {
        Dictionary<string, List<PsbRigSample>> samples = new Dictionary<string, List<PsbRigSample>>();
        if (state.PsbRows == null)
            return samples;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.boundRigKey))
                continue;

            PsbSpriteLayout item = FindPsbSpriteLayout(layout, row);
            if (item == null)
                continue;

            Vector2 p = PsbLocalToPreview(item.localCenter, layout.bounds, center, fitScale);
            Vector2 size = new Vector2(Mathf.Abs(item.localSize.x * fitScale), Mathf.Abs(item.localSize.y * fitScale));
            Rect r = new Rect(p.x - size.x * 0.5f, p.y - size.y * 0.5f, size.x, size.y);

            PsbRigSample sample = new PsbRigSample
            {
                rigKey = row.boundRigKey,
                name = row.name ?? string.Empty,
                searchName = string.Join("/", new string[]
                {
                    row.name ?? string.Empty,
                    row.sourceSpriteName ?? string.Empty,
                    row.sourceLayerPath ?? string.Empty,
                    row.boundRigName ?? string.Empty,
                    row.boundRigKey ?? string.Empty
                }),
                center = p,
                size = size,
                rect = r,
                sprite = item.sprite
            };

            if (!samples.TryGetValue(sample.rigKey, out List<PsbRigSample> list))
            {
                list = new List<PsbRigSample>();
                samples[sample.rigKey] = list;
            }
            list.Add(sample);
        }

        return samples;
    }

    private Vector2 ResolveMainAnchor(Dictionary<string, List<PsbRigSample>> samples, string[] keys, Vector2 fallback, Vector2 motionDelta)
    {
        if (TryAverageSampleCenter(samples, keys, out Vector2 p))
            return p + motionDelta;
        return fallback;
    }

    private bool TryAverageSampleCenter(Dictionary<string, List<PsbRigSample>> samples, string[] keys, out Vector2 center)
    {
        center = Vector2.zero;
        int count = 0;

        for (int k = 0; k < keys.Length; k++)
        {
            if (!samples.TryGetValue(keys[k], out List<PsbRigSample> list))
                continue;

            for (int i = 0; i < list.Count; i++)
            {
                center += list[i].center;
                count++;
            }
        }

        if (count <= 0)
            return false;

        center /= count;
        return true;
    }

    private PsbRigSample FirstSample(Dictionary<string, List<PsbRigSample>> samples, string key)
    {
        if (!samples.TryGetValue(key, out List<PsbRigSample> list) || list == null || list.Count == 0)
            return null;

        // 同一个骨架节点可能被多个 PSB 小图层绑定。
        // 用面积最大的那一块作为主样本，比直接取第 0 个更稳定。
        PsbRigSample best = list[0];
        float bestArea = Mathf.Abs(best.rect.width * best.rect.height);
        for (int i = 1; i < list.Count; i++)
        {
            PsbRigSample s = list[i];
            if (s == null)
                continue;

            float area = Mathf.Abs(s.rect.width * s.rect.height);
            if (area > bestArea)
            {
                best = s;
                bestArea = area;
            }
        }
        return best;
    }


    private string NormalizeLayerBindName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        text = text.Replace('\\', '/');
        text = text.Replace('-', '_');
        text = text.Replace(' ', '_');
        return text.ToLowerInvariant();
    }

    private bool ContainsAny(string text, params string[] needles)
    {
        if (string.IsNullOrEmpty(text) || needles == null)
            return false;

        for (int i = 0; i < needles.Length; i++)
        {
            string needle = needles[i];
            if (string.IsNullOrEmpty(needle))
                continue;

            if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }


    private PsbRigSample ExactLegLayerSample(Dictionary<string, List<PsbRigSample>> samples, string suffix, string part)
    {
        if (samples == null)
            return null;

        string sideLower = (suffix ?? string.Empty).ToLowerInvariant();
        bool isLeft = sideLower == "l";
        bool isRight = sideLower == "r";

        PsbRigSample best = null;
        float bestScore = float.NegativeInfinity;

        foreach (KeyValuePair<string, List<PsbRigSample>> kv in samples)
        {
            List<PsbRigSample> list = kv.Value;
            if (list == null)
                continue;

            for (int i = 0; i < list.Count; i++)
            {
                PsbRigSample sample = list[i];
                if (sample == null)
                    continue;

                string raw = !string.IsNullOrEmpty(sample.searchName) ? sample.searchName : sample.name;
                string n = NormalizeLayerBindName(raw);
                string leaf = n;
                int slash = leaf.LastIndexOf('/');
                if (slash >= 0 && slash + 1 < leaf.Length)
                    leaf = leaf.Substring(slash + 1);

                float score = 0f;

                // 腿部优先依赖图层名：leg_L_upper / leg_L_lower / leg_L_foot。
                // 文件夹 leg_L 只做分组，不参与 PCA 主样本。
                string exactA = "leg_" + sideLower + "_" + part;
                string exactB = "leg" + sideLower + "_" + part;
                string exactC = part + "_leg_" + sideLower;
                string exactD = part + "_" + sideLower;

                if (leaf == exactA || leaf == exactB || leaf == exactC || leaf == exactD)
                    score += 1000f;
                if (n.EndsWith("/" + exactA, StringComparison.Ordinal) || n.Contains("/leg_" + sideLower + "/" + exactA))
                    score += 900f;

                bool hasLeg = ContainsAny(n, "leg", "腿", "thigh", "shin", "calf", "foot", "toe", "shoe", "sock", "大腿", "小腿", "脚", "足", "鞋", "袜");
                bool hasArm = ContainsAny(n, "arm", "hand", "elbow", "wrist", "shoulder", "腕", "手", "肘", "肩");

                bool hasSide = false;
                if (isLeft)
                    hasSide = ContainsAny(n, "_l", "-l", "/l", "left", "左");
                else if (isRight)
                    hasSide = ContainsAny(n, "_r", "-r", "/r", "right", "右");

                bool hasOppositeSide = false;
                if (isLeft)
                    hasOppositeSide = ContainsAny(n, "_r", "-r", "/r", "right", "右");
                else if (isRight)
                    hasOppositeSide = ContainsAny(n, "_l", "-l", "/l", "left", "左");

                bool partMatch = false;
                if (part == "upper")
                    partMatch = ContainsAny(n, "upper", "thigh", "大腿", "上腿", "股");
                else if (part == "lower")
                    partMatch = ContainsAny(n, "lower", "shin", "calf", "小腿", "下腿", "胫");
                else if (part == "ankle")
                    partMatch = ContainsAny(n, "ankle", "heel", "脚踝", "足首", "踝", "跟");
                else if (part == "foot")
                    partMatch = ContainsAny(n, "foot", "toe", "shoe", "sock", "脚", "足", "靴", "鞋", "袜");

                if (hasLeg) score += 35f;
                if (hasSide) score += 90f;
                if (partMatch) score += 120f;
                if (hasOppositeSide) score -= 260f;
                if (hasArm) score -= 320f;

                if (leaf == "leg_" + sideLower || leaf == "leg" + sideLower)
                    score -= 180f;

                float area = Mathf.Max(1f, Mathf.Abs(sample.rect.width * sample.rect.height));
                float tall = sample.rect.height / Mathf.Max(1f, sample.rect.width);
                float wide = sample.rect.width / Mathf.Max(1f, sample.rect.height);
                score += Mathf.Log(area + 1f) * 0.01f;
                if (part == "upper" || part == "lower")
                    score += Mathf.Clamp(tall, 0f, 5f) * 4f;
                else if (part == "foot")
                    score += Mathf.Clamp(wide, 0f, 5f) * 4f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = sample;
                }
            }
        }

        return bestScore >= 160f ? best : null;
    }


    private PsbRigSample BestLegSample(Dictionary<string, List<PsbRigSample>> samples, string key, string part, string suffix, float pelvisX, float side, float characterWidth)
    {
        if (!samples.TryGetValue(key, out List<PsbRigSample> list) || list == null || list.Count == 0)
            return null;

        PsbRigSample best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < list.Count; i++)
        {
            PsbRigSample s = list[i];
            if (s == null)
                continue;

            string n = NormalizeLayerBindName(s.name ?? string.Empty);
            float area = Mathf.Max(1f, Mathf.Abs(s.rect.width * s.rect.height));
            float aspectTall = s.rect.height / Mathf.Max(1f, s.rect.width);
            float aspectWide = s.rect.width / Mathf.Max(1f, s.rect.height);
            float score = Mathf.Log(area + 1f) * 0.05f;

            // L/R 只按角色自身左右。正面角色：L 在画面右侧，R 在画面左侧。
            float sideDelta = (s.center.x - pelvisX) * side;
            if (sideDelta < -characterWidth * 0.018f)
                score -= 80f;
            else if (sideDelta > characterWidth * 0.010f)
                score += 18f;

            bool explicitUpper = ContainsAny(n, "upper_leg", "leg_upper", "thigh", "大腿", "上腿", "股");
            bool explicitLower = ContainsAny(n, "lower_leg", "leg_lower", "shin", "calf", "knee", "小腿", "下腿", "膝");
            bool explicitAnkle = ContainsAny(n, "ankle", "heel", "脚踝", "踝", "跟");
            bool explicitFoot = ContainsAny(n, "foot", "feet", "toe", "shoe", "sock", "脚尖", "脚", "足", "靴", "鞋", "袜");
            bool genericLeg = ContainsAny(n, "leg", "腿") && !explicitUpper && !explicitLower && !explicitAnkle && !explicitFoot;

            if (part == "upper")
            {
                if (explicitUpper) score += 180f;
                if (genericLeg) score += 8f;
                if (explicitLower || explicitAnkle || explicitFoot) score -= 160f;
                score += Mathf.Clamp(aspectTall, 0f, 4f) * 10f;
                // 大腿主图层通常不会非常靠下。
                score -= Mathf.Max(0f, s.center.y - (s.rect.yMin + s.rect.height * 0.72f)) * 0.001f;
            }
            else if (part == "lower")
            {
                if (explicitLower) score += 180f;
                if (genericLeg) score += 6f;
                if (explicitUpper || explicitAnkle || explicitFoot) score -= 160f;
                score += Mathf.Clamp(aspectTall, 0f, 5f) * 12f;
            }
            else if (part == "ankle")
            {
                if (explicitAnkle) score += 190f;
                if (explicitFoot) score -= 35f; // 没有脚踝层时外面会把脚层降级成 foot，用这里不要优先吞脚。
                if (explicitUpper || explicitLower) score -= 110f;
            }
            else // foot
            {
                if (explicitFoot) score += 190f;
                if (explicitUpper || explicitLower || explicitAnkle) score -= 140f;
                score += Mathf.Clamp(aspectWide, 0f, 4f) * 8f;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }

        return best != null ? best : FirstSample(samples, key);
    }

    private bool IsExplicitAnkleLayer(PsbRigSample sample)
    {
        if (sample == null) return false;
        string n = (sample.name ?? string.Empty).ToLowerInvariant();
        return n.Contains("ankle") || n.Contains("脚踝") || n.Contains("足首");
    }

    private bool TryGetSampleRect(Dictionary<string, List<PsbRigSample>> samples, string key, out Rect rect)
    {
        rect = new Rect();
        if (!samples.TryGetValue(key, out List<PsbRigSample> list) || list == null || list.Count == 0)
            return false;

        bool has = false;
        for (int i = 0; i < list.Count; i++)
        {
            PsbRigSample s = list[i];
            if (s == null)
                continue;

            rect = has ? Union(rect, s.rect) : s.rect;
            has = true;
        }
        return has;
    }

    private float MidY(float a, float b)
    {
        return (a + b) * 0.5f;
    }

    private float ClampBetween(float value, float a, float b)
    {
        float min = Mathf.Min(a, b);
        float max = Mathf.Max(a, b);
        return Mathf.Clamp(value, min, max);
    }

    private Vector2 SampleCenterOr(Dictionary<string, List<PsbRigSample>> samples, string key, Vector2 fallback, Vector2 motionDelta)
    {
        PsbRigSample s = FirstSample(samples, key);
        return s != null ? s.center + motionDelta : fallback;
    }

    private float InferScreenSideFromSamples(Dictionary<string, List<PsbRigSample>> samples, string[] keys, float originX, float fallbackSide)
    {
        float x = 0f;
        int count = 0;

        for (int k = 0; k < keys.Length; k++)
        {
            if (!samples.TryGetValue(keys[k], out List<PsbRigSample> list) || list == null)
                continue;

            for (int i = 0; i < list.Count; i++)
            {
                x += list[i].center.x;
                count++;
            }
        }

        if (count <= 0)
            return Mathf.Abs(fallbackSide) < 0.001f ? 1f : Mathf.Sign(fallbackSide);

        float dx = x / count - originX;
        if (Mathf.Abs(dx) < 0.5f)
            return Mathf.Abs(fallbackSide) < 0.001f ? 1f : Mathf.Sign(fallbackSide);

        return Mathf.Sign(dx);
    }

    private Vector2 ClosestPointOnRect(Rect rect, Vector2 target)
    {
        return new Vector2(
            Mathf.Clamp(target.x, rect.xMin, rect.xMax),
            Mathf.Clamp(target.y, rect.yMin, rect.yMax));
    }

    private float InnerEdgeX(Rect rect, float screenSide)
    {
        // screenSide < 0 表示该肢体在画面左侧，靠身体的一侧是右边缘；反之是左边缘。
        return screenSide < 0f ? rect.xMax : rect.xMin;
    }

    private float OuterEdgeX(Rect rect, float screenSide)
    {
        return screenSide < 0f ? rect.xMin : rect.xMax;
    }

    private Vector2 ClosestPointOnRectPreferTop(Rect rect, Vector2 target)
    {
        Vector2 p = ClosestPointOnRect(rect, target);
        float topBlend = Mathf.InverseLerp(rect.yMax, rect.yMin, target.y);
        p.y = Mathf.Lerp(p.y, rect.yMin, Mathf.Clamp01(topBlend) * 0.45f);
        return p;
    }

    private Vector2 TopEdgePointClosestTo(Rect rect, Vector2 target, float yInset)
    {
        return new Vector2(Mathf.Clamp(target.x, rect.xMin, rect.xMax), rect.yMin + Mathf.Max(0f, yInset));
    }

    private Vector2 BottomEdgePointClosestTo(Rect rect, Vector2 target, float yInset)
    {
        return new Vector2(Mathf.Clamp(target.x, rect.xMin, rect.xMax), rect.yMax - Mathf.Max(0f, yInset));
    }

    private float FarthestHorizontalEdgeX(Rect rect, float fromX)
    {
        return Mathf.Abs(rect.xMin - fromX) >= Mathf.Abs(rect.xMax - fromX) ? rect.xMin : rect.xMax;
    }

    private void BuildStableArm(Dictionary<string, Vector2> result, Dictionary<string, List<PsbRigSample>> samples, Dictionary<string, Vector2> generated, System.Func<string, Vector2> motion, string suffix, float side, float characterWidth, float characterHeight)
    {
        string shoulderKey = "Shoulder_" + suffix;
        string elbowKey = "Elbow_" + suffix;
        string wristKey = "Wrist_" + suffix;
        string handEndKey = "HandEnd_" + suffix;

        Vector2 neck = result.ContainsKey("Neck") ? result["Neck"] : result["Chest"];
        Vector2 chest = result.ContainsKey("Chest") ? result["Chest"] : neck + new Vector2(0f, characterHeight * 0.10f);

        // L/R 是角色自身左右：正面角色的 L 在画面右侧，R 在画面左侧。
        // 肩根第一优先级不再用固定肩宽，而是用“上臂图层上沿 + 靠身体的内侧边”。
        // 这样肩宽会跟着实际角色/实际手臂图层走，避免手臂骨骼从身体中线或过外侧长出来。
        const float ShoulderWidthByCharacter = 0.145f;
        const float ShoulderRootFromNeckToChest = 0.34f;
        Vector2 shoulderBase = Vector2.Lerp(neck, chest, ShoulderRootFromNeckToChest);

        Vector2 shoulderFallback = generated.ContainsKey(shoulderKey)
            ? generated[shoulderKey]
            : shoulderBase + new Vector2(side * characterWidth * ShoulderWidthByCharacter, characterHeight * 0.010f);
        Vector2 handFallback = generated.ContainsKey(handEndKey)
            ? generated[handEndKey]
            : shoulderFallback + new Vector2(side * characterWidth * 0.15f, characterHeight * 0.22f);

        PsbRigSample upper = FirstSample(samples, shoulderKey);
        PsbRigSample lower = FirstSample(samples, elbowKey);
        PsbRigSample hand = FirstSample(samples, wristKey);
        PsbRigSample handEnd = FirstSample(samples, handEndKey);

        Vector2 shoulder = shoulderFallback + motion(shoulderKey) * 0.16f;
        if (upper != null)
        {
            // 上臂图层存在时，肩根使用上臂顶部靠身体的一侧：
            // side > 0（角色L/画面右）取 rect.xMin；side < 0（角色R/画面左）取 rect.xMax。
            float innerX = InnerEdgeX(upper.rect, side);
            float yInset = Mathf.Min(upper.rect.height * 0.025f, characterHeight * 0.010f);
            Vector2 topInner = new Vector2(innerX, upper.rect.yMin + yInset);

            // 轻微向图层内部收一点，防止点贴在裁切边缘上不好点选。
            float inward = Mathf.Min(upper.rect.width * 0.08f, characterWidth * 0.012f);
            topInner.x += side > 0f ? inward : -inward;

            shoulder = Vector2.Lerp(shoulder, topInner + motion(shoulderKey) * 0.10f, 0.94f);
        }

        Vector2 wrist = generated.ContainsKey(wristKey) ? generated[wristKey] : Vector2.Lerp(shoulder, handFallback, 0.82f);
        if (lower != null && hand != null)
        {
            Vector2 a = ClosestPointOnRect(lower.rect, hand.center);
            Vector2 b = ClosestPointOnRect(hand.rect, lower.center);
            wrist = Vector2.Lerp(a, b, 0.58f) + motion(wristKey) * 0.16f;
        }
        else if (hand != null)
        {
            wrist = ClosestPointOnRectPreferTop(hand.rect, lower != null ? lower.center : shoulder) + motion(wristKey) * 0.16f;
        }
        else if (lower != null)
        {
            wrist = BottomEdgePointClosestTo(lower.rect, shoulder, Mathf.Min(lower.rect.height * 0.04f, characterHeight * 0.012f)) + motion(wristKey) * 0.12f;
        }

        Vector2 elbow = generated.ContainsKey(elbowKey) ? generated[elbowKey] : Vector2.Lerp(shoulder, wrist, 0.50f);
        if (upper != null && lower != null)
        {
            Vector2 a = ClosestPointOnRect(upper.rect, lower.center);
            Vector2 b = ClosestPointOnRect(lower.rect, upper.center);
            Vector2 seam = Vector2.Lerp(a, b, 0.50f);
            elbow = Vector2.Lerp(elbow, seam + motion(elbowKey) * 0.14f, 0.70f);
        }
        else if (lower != null)
        {
            Vector2 top = TopEdgePointClosestTo(lower.rect, shoulder, Mathf.Min(lower.rect.height * 0.04f, characterHeight * 0.012f));
            elbow = Vector2.Lerp(elbow, top + motion(elbowKey) * 0.12f, 0.55f);
        }
        else if (upper != null)
        {
            Vector2 bottom = BottomEdgePointClosestTo(upper.rect, wrist, Mathf.Min(upper.rect.height * 0.04f, characterHeight * 0.012f));
            elbow = Vector2.Lerp(elbow, bottom + motion(elbowKey) * 0.12f, 0.48f);
        }

        Vector2 handEndPos = handFallback + motion(handEndKey) * 0.10f;
        if (handEnd != null)
        {
            handEndPos = handEnd.center + motion(handEndKey) * 0.10f;
        }
        else if (hand != null)
        {
            // 手部末端不是横向最外边，而是手图层底部端点。
            // 这样手掌/拳头不会被画成一根横向短骨，而是从手腕连到手的底部。
            float yInset = Mathf.Min(hand.rect.height * 0.025f, characterHeight * 0.010f);
            Vector2 bottom = BottomEdgePointClosestTo(hand.rect, wrist, yInset);
            handEndPos = bottom + motion(handEndKey) * 0.08f;
        }
        else
        {
            Vector2 dir = wrist - elbow;
            if (dir.sqrMagnitude < 0.001f) dir = new Vector2(side, 0.15f);
            handEndPos = wrist + dir.normalized * Mathf.Min(characterWidth * 0.055f, characterHeight * 0.040f) + motion(handEndKey) * 0.08f;
        }

        result[shoulderKey] = shoulder;
        result[elbowKey] = elbow;
        result[wristKey] = wrist;
        result[handEndKey] = handEndPos;
    }

    private float CharacterScreenSide(string suffix)
    {
        // L/R 是角色身体自己的左右，不是用来判断脚尖朝向的画面左右。
        // 这里仍只用于髋/膝/踝的左右分区兜底；Foot 朝向必须走 GetHeadFacingSign。
        return string.Equals(suffix, "L", StringComparison.OrdinalIgnoreCase) ? 1f : -1f;
    }

    private float GetHeadFacingSign(Dictionary<string, Vector2> anchors)
    {
        // 角色面朝方向以 Head -> HeadTop 的横向偏移为准。
        // 这是全局朝向，不按 L/R 分脚单独反猜；两只脚板应该同向指向脚尖。
        if (anchors != null && anchors.TryGetValue("Head", out Vector2 head) && anchors.TryGetValue("HeadTop", out Vector2 headTop))
        {
            float dx = headTop.x - head.x;
            if (Mathf.Abs(dx) > 0.75f)
                return dx >= 0f ? 1f : -1f;
        }

        // 当前母版通常面向画面左侧；没有头部横向信息时，不再从 Foot 图层模糊猜。
        return -1f;
    }

    private Vector2 SoftKeepPointOnCharacterSide(Vector2 p, float originX, float side, float minOffset, float strength)
    {
        if (!IsFinite(p))
            return p;

        float targetX = originX + side * Mathf.Abs(minOffset);
        bool wrongSide = side > 0f ? p.x < targetX : p.x > targetX;
        if (wrongSide)
            p.x = Mathf.Lerp(p.x, targetX, Mathf.Clamp01(strength));
        return p;
    }

    private void BuildStableLeg(Dictionary<string, Vector2> result, Dictionary<string, List<PsbRigSample>> samples, Dictionary<string, Vector2> generated, System.Func<string, Vector2> motion, string suffix, float side, float characterWidth, float characterHeight)
    {
        // L/R 永远按角色自身左右：正面角色 L 在画面右侧，R 在画面左侧。
        // v13：只要存在 leg_X_upper / leg_X_lower / leg_X_foot，就进入“精确命名腿模式”。
        // 精确命名模式会直接 return，不再经过 PCA、Bounds、旧模板、侧向保护等兜底路径。
        side = CharacterScreenSide(suffix);

        string hipKey = "Hip_" + suffix;
        string kneeKey = "Knee_" + suffix;
        string ankleKey = "Ankle_" + suffix;
        string footKey = "Foot_" + suffix;

        Vector2 pelvis = result["Pelvis"];
        Vector2 hipFallback = generated.ContainsKey(hipKey) ? generated[hipKey] : pelvis + new Vector2(side * characterWidth * 0.075f, characterHeight * 0.040f);
        Vector2 footFallback = generated.ContainsKey(footKey) ? generated[footKey] : hipFallback + new Vector2(side * characterWidth * 0.018f, characterHeight * 0.42f);

        PsbRigSample exactUpper = ExactLegLayerSample(samples, suffix, "upper");
        PsbRigSample exactLower = ExactLegLayerSample(samples, suffix, "lower");
        PsbRigSample exactFoot = ExactLegLayerSample(samples, suffix, "foot");

        if (exactUpper != null && exactLower != null && exactFoot != null)
        {
            Vector2 upperTop;
            Vector2 upperBottom;
            Vector2 lowerTop;
            Vector2 lowerBottom;
            Vector2 footTop;
            float w;

            bool hasUpperTop = TryGetAlphaBandPoint(exactUpper, 0.020f, 0.120f, out upperTop, out w);
            bool hasUpperBottom = TryGetAlphaBandPoint(exactUpper, 0.900f, 0.995f, out upperBottom, out w);
            bool hasLowerTop = TryGetAlphaBandPoint(exactLower, 0.000f, 0.110f, out lowerTop, out w);
            bool hasLowerBottom = TryGetAlphaBandPoint(exactLower, 0.940f, 0.998f, out lowerBottom, out w);
            bool hasFootTop = TryGetAlphaBandPoint(exactFoot, 0.000f, 0.130f, out footTop, out w);

            Vector2 hip;
            if (hasUpperTop)
            {
                // 大腿根不能直接取大腿图层顶部 alpha 的中心。
                // 正面 2.5D 人形里，大腿图层顶部经常被裤裆/身体遮挡，alpha 中心会偏到外侧，
                // 导致 Hip -> Knee 这条骨骼看起来从大腿边缘长出来。
                // 这里改成“顶部 alpha 中心 + 靠骨盆的顶部边缘”混合，让大腿根更贴近胯部。
                Vector2 topTowardPelvis = TopEdgePointClosestTo(
                    exactUpper.rect,
                    pelvis,
                    Mathf.Min(exactUpper.rect.height * 0.030f, characterHeight * 0.008f));
                hip = Vector2.Lerp(upperTop, topTowardPelvis, 0.72f);
            }
            else
            {
                hip = TopEdgePointClosestTo(exactUpper.rect, pelvis, Mathf.Min(exactUpper.rect.height * 0.030f, characterHeight * 0.008f));
            }

            Vector2 knee;
            if (hasUpperBottom && hasLowerTop)
                knee = Vector2.Lerp(upperBottom, lowerTop, 0.50f);
            else if (hasUpperBottom)
                knee = upperBottom;
            else if (hasLowerTop)
                knee = lowerTop;
            else
                knee = Vector2.Lerp(
                    BottomEdgePointClosestTo(exactUpper.rect, exactLower.rect.center, Mathf.Min(exactUpper.rect.height * 0.018f, characterHeight * 0.006f)),
                    TopEdgePointClosestTo(exactLower.rect, exactUpper.rect.center, Mathf.Min(exactLower.rect.height * 0.018f, characterHeight * 0.006f)),
                    0.50f);

            // v23：脚尖本来是对的，真正的问题是“脚后跟 / 脚踝 pivot”被小腿底部中心牵住，
            // 导致脚掌骨骼的 Root 落在脚掌中段。这里改成：
            // - Ankle / Heel pivot 按脚图层矩形的脚后跟侧取点；
            // - Foot 端点继续取脚尖，不再往脚后跟回收。
            float headFacingSign = GetHeadFacingSign(result);

            Vector2 lowerAnkleCandidate;
            if (hasLowerBottom)
                lowerAnkleCandidate = lowerBottom;
            else if (hasFootTop)
                lowerAnkleCandidate = footTop;
            else
                lowerAnkleCandidate = BottomEdgePointClosestTo(
                    exactLower.rect,
                    exactFoot.rect.center,
                    Mathf.Min(exactLower.rect.height * 0.016f, characterHeight * 0.006f));

            Vector2 anklePos = FootHeelPivotFromRect(exactFoot, lowerAnkleCandidate, headFacingSign);

            Vector2 footPos;
            // Foot 是脚尖端点：保持脚尖识别，不再把 Foot 点往脚后跟方向偏移。
            if (TryGetAlphaFootTipColumnInDirection(exactFoot, anklePos, headFacingSign, out Vector2 footTip))
                footPos = ClampPointToRect(footTip, exactFoot.rect);
            else if (TryGetAlphaFootToeHardPointInDirection(exactFoot, anklePos, headFacingSign, out footTip))
                footPos = ClampPointToRect(footTip, exactFoot.rect);
            else
            {
                float footX = headFacingSign >= 0f ? exactFoot.rect.xMax : exactFoot.rect.xMin;
                float footY = Mathf.Lerp(exactFoot.rect.yMin, exactFoot.rect.yMax, 0.62f);
                footPos = new Vector2(footX, footY);
            }

            hip += motion(hipKey) * 0.08f;
            knee += motion(kneeKey) * 0.08f;
            anklePos += motion(ankleKey) * 0.07f;
            footPos += motion(footKey) * 0.05f;

            float minGap = characterHeight * 0.006f;
            if (knee.y < hip.y + minGap)
                knee.y = hip.y + minGap;
            if (anklePos.y < knee.y + minGap)
                anklePos.y = knee.y + minGap;
            if (footPos.y < anklePos.y + characterHeight * 0.001f)
                footPos.y = anklePos.y + characterHeight * 0.001f;

            result[hipKey] = hip;
            result[kneeKey] = knee;
            result[ankleKey] = anklePos;
            result[footKey] = footPos;
            return;
        }

        // 以下仅为兜底路径：没有完整精确腿层时才允许使用旧的自动推断。
        PsbRigSample exactAnkle = ExactLegLayerSample(samples, suffix, "ankle");
        PsbRigSample upper = exactUpper ?? BestLegSample(samples, hipKey, "upper", suffix, pelvis.x, side, characterWidth);
        PsbRigSample lower = exactLower ?? BestLegSample(samples, kneeKey, "lower", suffix, pelvis.x, side, characterWidth);
        PsbRigSample ankle = exactAnkle ?? BestLegSample(samples, ankleKey, "ankle", suffix, pelvis.x, side, characterWidth);
        PsbRigSample foot = exactFoot ?? BestLegSample(samples, footKey, "foot", suffix, pelvis.x, side, characterWidth);

        bool ankleSampleIsFootLayer = false;
        if (foot == null && ankle != null && !IsExplicitAnkleLayer(ankle))
        {
            foot = ankle;
            ankle = null;
            ankleSampleIsFootLayer = true;
        }

        Rect upperRect = upper != null ? upper.rect : new Rect();
        Rect lowerRect = lower != null ? lower.rect : new Rect();
        Rect footRect = foot != null ? foot.rect : new Rect();
        bool hasUpperRect = upper != null;
        bool hasLowerRect = lower != null;
        bool hasFootRect = foot != null || ankleSampleIsFootLayer;

        Vector2 hip2 = hipFallback + motion(hipKey) * 0.08f;
        Vector2 knee2 = generated.ContainsKey(kneeKey) ? generated[kneeKey] : Vector2.Lerp(hipFallback, footFallback, 0.42f);
        Vector2 ankle2 = generated.ContainsKey(ankleKey) ? generated[ankleKey] : Vector2.Lerp(hipFallback, footFallback, 0.74f);
        Vector2 foot2 = footFallback + motion(footKey) * 0.05f;

        bool footLooksLikeWholeLeg = hasFootRect
            && footRect.height > footRect.width * 1.35f
            && footRect.height > characterHeight * 0.18f;

        bool hasUpperAxis = TryGetAlphaAxisMeasure(upper, pelvis, out LimbAxisMeasure upperAxis);
        bool hasLowerAxis = TryGetAlphaAxisMeasure(lower, hasUpperAxis ? upperAxis.far : hipFallback, out LimbAxisMeasure lowerAxis);
        bool hasFootAxis = TryGetAlphaAxisMeasure(foot, hasLowerAxis ? lowerAxis.far : footFallback, out LimbAxisMeasure footAxis);

        if (hasUpperAxis)
        {
            hip2 = upperAxis.near + motion(hipKey) * 0.08f;
            Vector2 toPelvis = pelvis - hip2;
            if (toPelvis.sqrMagnitude > 0.01f)
                hip2 += toPelvis.normalized * Mathf.Min(characterWidth * 0.010f, upperAxis.length * 0.035f);
        }
        else if (TryGetAlphaBandPoint(upper, 0.04f, 0.14f, out Vector2 upperTop, out float upperTopWidth))
        {
            hip2 = Vector2.Lerp(hip2, upperTop + motion(hipKey) * 0.08f, 0.80f);
        }
        else if (upper != null && hasUpperRect)
        {
            Vector2 top = TopEdgePointClosestTo(upperRect, pelvis, Mathf.Min(upperRect.height * 0.035f, characterHeight * 0.010f));
            top.x = Mathf.Clamp(top.x, hipFallback.x - characterWidth * 0.060f, hipFallback.x + characterWidth * 0.060f);
            hip2 = Vector2.Lerp(hip2, top + motion(hipKey) * 0.08f, 0.58f);
        }

        if (hasUpperAxis && hasLowerAxis && !footLooksLikeWholeLeg)
            knee2 = Vector2.Lerp(upperAxis.far, lowerAxis.near, 0.50f) + motion(kneeKey) * 0.08f;
        else if (hasUpperAxis)
            knee2 = upperAxis.far + motion(kneeKey) * 0.08f;
        else if (hasLowerAxis)
            knee2 = lowerAxis.near + motion(kneeKey) * 0.08f;
        else if (TryGetAlphaBandPoint(upper, 0.950f, 0.998f, out Vector2 upperBottomAlpha, out float upperBottomWidth)
              && TryGetAlphaBandPoint(lower, 0.000f, 0.080f, out Vector2 lowerTopAlpha, out float lowerTopWidth)
              && !footLooksLikeWholeLeg)
            knee2 = Vector2.Lerp(upperBottomAlpha, lowerTopAlpha, 0.50f) + motion(kneeKey) * 0.08f;
        else if (hasUpperRect && hasLowerRect && !footLooksLikeWholeLeg)
            knee2 = Vector2.Lerp(BottomEdgePointClosestTo(upperRect, lowerRect.center, Mathf.Min(upperRect.height * 0.018f, characterHeight * 0.008f)), TopEdgePointClosestTo(lowerRect, upperRect.center, Mathf.Min(lowerRect.height * 0.018f, characterHeight * 0.008f)), 0.50f) + motion(kneeKey) * 0.08f;
        if (hasLowerAxis && hasFootAxis && !footLooksLikeWholeLeg)
            ankle2 = Vector2.Lerp(lowerAxis.far, footAxis.near, 0.45f);
        else if (hasLowerAxis)
            ankle2 = lowerAxis.far;
        else if (hasFootAxis && !footLooksLikeWholeLeg)
            ankle2 = footAxis.near;
        else if (TryGetAlphaBandPoint(lower, 0.955f, 0.998f, out Vector2 lowerBottomAlpha, out float lowerBottomWidth)
              && !footLooksLikeWholeLeg)
            ankle2 = lowerBottomAlpha;
        else if (hasLowerRect && hasFootRect && !footLooksLikeWholeLeg)
            ankle2 = BottomEdgePointClosestTo(lowerRect, footRect.center, Mathf.Min(lowerRect.height * 0.016f, characterHeight * 0.006f));

        float fallbackHeadFacingSign = GetHeadFacingSign(result);
        if (hasFootRect && !footLooksLikeWholeLeg)
            ankle2 = FootHeelPivotFromRect(foot, ankle2, fallbackHeadFacingSign);
        ankle2 += motion(ankleKey) * 0.07f;

        if (TryGetAlphaFootTipColumnInDirection(foot, ankle2, fallbackHeadFacingSign, out Vector2 alphaFootPoint) && !footLooksLikeWholeLeg)
            foot2 = ClampPointToRect(alphaFootPoint, footRect) + motion(footKey) * 0.05f;
        else if (TryGetAlphaFootToeHardPointInDirection(foot, ankle2, fallbackHeadFacingSign, out alphaFootPoint) && !footLooksLikeWholeLeg)
            foot2 = ClampPointToRect(alphaFootPoint, footRect) + motion(footKey) * 0.05f;
        else if (hasFootRect && !footLooksLikeWholeLeg)
        {
            float footX = fallbackHeadFacingSign >= 0f ? footRect.xMax : footRect.xMin;
            float footY = Mathf.Lerp(footRect.yMin, footRect.yMax, 0.62f);
            foot2 = new Vector2(footX, footY) + motion(footKey) * 0.05f;
        }


        float sideMin = characterWidth * 0.006f;
        hip2 = SoftKeepPointOnCharacterSide(hip2, pelvis.x, side, sideMin * 1.1f, 0.75f);
        knee2 = SoftKeepPointOnCharacterSide(knee2, pelvis.x, side, sideMin * 0.8f, 0.50f);
        ankle2 = SoftKeepPointOnCharacterSide(ankle2, pelvis.x, side, sideMin * 0.5f, 0.42f);
        foot2 = SoftKeepPointOnCharacterSide(foot2, pelvis.x, side, sideMin * 0.3f, 0.35f);

        float fallbackMinGap = characterHeight * 0.010f;
        if (knee2.y < hip2.y + fallbackMinGap)
            knee2.y = hip2.y + fallbackMinGap;
        if (ankle2.y < knee2.y + fallbackMinGap)
            ankle2.y = knee2.y + fallbackMinGap;
        if (foot2.y < ankle2.y + characterHeight * 0.002f)
            foot2.y = ankle2.y + characterHeight * 0.002f;

        result[hipKey] = hip2;
        result[kneeKey] = knee2;
        result[ankleKey] = ankle2;
        result[footKey] = foot2;
    }

    private Vector2 ClampPointToRect(Vector2 p, Rect r)
    {
        if (!IsFinite(p) || r.width <= 0.5f || r.height <= 0.5f)
            return p;

        return new Vector2(
            Mathf.Clamp(p.x, r.xMin, r.xMax),
            Mathf.Clamp(p.y, r.yMin, r.yMax));
    }

    private Vector2 FootHeelPivotFromRect(PsbRigSample footSample, Vector2 lowerAnkleCandidate, float facingSign)
    {
        if (footSample == null || footSample.rect.width <= 0.5f || footSample.rect.height <= 0.5f)
            return lowerAnkleCandidate;

        Rect r = footSample.rect;
        float sign = facingSign >= 0f ? 1f : -1f;

        // 脚尖侧仍由 Foot 端点负责；这里专门计算脚后跟 / 脚踝 pivot。
        // 画面朝左时：toe = xMin，heel = xMax；画面朝右时反过来。
        float toeX = sign >= 0f ? r.xMax : r.xMin;
        float heelX = sign >= 0f ? r.xMin : r.xMax;

        // 不贴死矩形边缘，稍微从后跟往脚尖收一点，落在脚后跟肉里。
        float heelPivotX = Mathf.Lerp(heelX, toeX, 0.16f);

        // 脚后跟节点通常在脚掌中上部，不应该沉到脚底，也不应该跑到小腿太高处。
        float heelPivotY = Mathf.Lerp(r.yMin, r.yMax, 0.46f);
        Vector2 rectHeelPivot = new Vector2(heelPivotX, heelPivotY);

        // 保留一点小腿末端信息，但主要以脚图层矩形后跟为准。
        Vector2 p = Vector2.Lerp(lowerAnkleCandidate, rectHeelPivot, 0.82f);
        p.x = Mathf.Clamp(p.x, r.xMin, r.xMax);
        p.y = Mathf.Clamp(p.y, r.yMin, r.yMax);
        return p;
    }


    private bool TryGetAlphaAxisMeasure(PsbRigSample sample, Vector2 preferredNearTarget, out LimbAxisMeasure measure)
    {
        measure = new LimbAxisMeasure { valid = false };
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        try
        {
            Texture2D tex = sample.sprite.texture;
            Rect tr = sample.sprite.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax), 0, tex.height - 1);

            int xStep = Mathf.Max(1, Mathf.CeilToInt((x1 - x0 + 1) / 96f));
            int yStep = Mathf.Max(1, Mathf.CeilToInt((y1 - y0 + 1) / 120f));
            const float alphaThreshold = 0.08f;

            List<Vector2> points = new List<Vector2>();
            points.Capacity = 14000;

            for (int y = y0; y <= y1; y += yStep)
            {
                for (int x = x0; x <= x1; x += xStep)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.a <= alphaThreshold)
                        continue;

                    float x01 = Mathf.Clamp01(((float)x + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
                    float y01FromBottom = Mathf.Clamp01(((float)y + 0.5f - tr.yMin) / Mathf.Max(1f, tr.height));
                    float y01FromTop = 1f - y01FromBottom;

                    Vector2 p = new Vector2(
                        Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, x01),
                        Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, y01FromTop));

                    if (IsFinite(p))
                        points.Add(p);
                }
            }

            if (points.Count < 12)
                return false;

            Vector2 mean = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
                mean += points[i];
            mean /= points.Count;

            float xx = 0f;
            float xy = 0f;
            float yy = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 d = points[i] - mean;
                xx += d.x * d.x;
                xy += d.x * d.y;
                yy += d.y * d.y;
            }
            xx /= points.Count;
            xy /= points.Count;
            yy /= points.Count;

            float trace = xx + yy;
            float diff = xx - yy;
            float root = Mathf.Sqrt(Mathf.Max(0f, diff * diff + 4f * xy * xy));
            float lambda = (trace + root) * 0.5f;

            Vector2 dir = new Vector2(xy, lambda - xx);
            if (dir.sqrMagnitude < 0.000001f)
                dir = new Vector2(lambda - yy, xy);
            if (dir.sqrMagnitude < 0.000001f)
                dir = sample.rect.height >= sample.rect.width ? Vector2.down : Vector2.right;
            dir.Normalize();

            float minProj = float.PositiveInfinity;
            float maxProj = float.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float p = Vector2.Dot(points[i] - mean, dir);
                if (p < minProj) minProj = p;
                if (p > maxProj) maxProj = p;
            }

            if (float.IsInfinity(minProj) || float.IsInfinity(maxProj) || maxProj - minProj < 0.5f)
                return false;

            Vector2 a = mean + dir * minProj;
            Vector2 b = mean + dir * maxProj;

            Vector2 near = (a - preferredNearTarget).sqrMagnitude <= (b - preferredNearTarget).sqrMagnitude ? a : b;
            Vector2 far = near == a ? b : a;
            Vector2 finalDir = far - near;
            float length = finalDir.magnitude;
            if (length < 1f || !IsFinite(near) || !IsFinite(far))
                return false;

            measure.valid = true;
            measure.near = near;
            measure.far = far;
            measure.center = mean;
            measure.dir = finalDir / Mathf.Max(0.0001f, length);
            measure.length = length;
            measure.count = points.Count;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetAlphaBandPoint(PsbRigSample sample, float top01, float bottom01, out Vector2 point, out float width)
    {
        point = Vector2.zero;
        width = 0f;
        if (TryGetAlphaBand(sample, top01, bottom01, out LimbAlphaBand band))
        {
            point = band.center;
            width = band.width;
            return true;
        }
        return false;
    }

    private bool TryGetAlphaBand(PsbRigSample sample, float top01, float bottom01, out LimbAlphaBand band)
    {
        band = new LimbAlphaBand { valid = false };
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        top01 = Mathf.Clamp01(top01);
        bottom01 = Mathf.Clamp01(bottom01);
        if (bottom01 < top01)
        {
            float t = top01;
            top01 = bottom01;
            bottom01 = t;
        }
        if (bottom01 - top01 < 0.01f)
            bottom01 = Mathf.Min(1f, top01 + 0.01f);

        try
        {
            Texture2D tex = sample.sprite.texture;
            Rect tr = sample.sprite.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax), 0, tex.width - 1);
            int yTop = Mathf.Clamp(Mathf.FloorToInt(tr.yMax - bottom01 * tr.height), 0, tex.height - 1);
            int yBottom = Mathf.Clamp(Mathf.CeilToInt(tr.yMax - top01 * tr.height), 0, tex.height - 1);

            int xStep = Mathf.Max(1, Mathf.CeilToInt((x1 - x0 + 1) / 90f));
            int yStep = Mathf.Max(1, Mathf.CeilToInt((yBottom - yTop + 1) / 42f));
            bool any = false;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            long sumY = 0;
            int countY = 0;
            const float alphaThreshold = 0.08f;

            for (int y = yTop; y <= yBottom; y += yStep)
            {
                bool rowAny = false;
                int rowMin = int.MaxValue;
                int rowMax = int.MinValue;
                for (int x = x0; x <= x1; x += xStep)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.a <= alphaThreshold)
                        continue;
                    rowAny = true;
                    rowMin = Mathf.Min(rowMin, x);
                    rowMax = Mathf.Max(rowMax, x);
                }

                if (!rowAny)
                    continue;

                any = true;
                minX = Mathf.Min(minX, rowMin);
                maxX = Mathf.Max(maxX, rowMax);
                sumY += y;
                countY++;
            }

            if (!any || minX == int.MaxValue || maxX == int.MinValue || countY <= 0)
                return false;

            float left01 = Mathf.Clamp01(((float)minX + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
            float right01 = Mathf.Clamp01(((float)maxX + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
            float y01FromBottom = Mathf.Clamp01(((float)sumY / countY + 0.5f - tr.yMin) / Mathf.Max(1f, tr.height));
            float y01FromTop = 1f - y01FromBottom;

            float leftX = Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, left01);
            float rightX = Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, right01);
            float yPreview = Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, y01FromTop);

            band.leftX = leftX;
            band.rightX = rightX;
            band.width = Mathf.Abs(rightX - leftX);
            band.center = new Vector2((leftX + rightX) * 0.5f, yPreview);
            band.valid = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private struct AlphaFootColumnMeasure
    {
        public bool valid;
        public float worldX;
        public float centerYFromTop01;
        public float minYFromTop01;
        public float maxYFromTop01;
        public float height01;
        public float dxFromAnkle;
        public float score;
    }

    private bool TryGetAlphaFootTipColumnInDirection(PsbRigSample sample, Vector2 ankleWorld, float facingSign, out Vector2 point)
    {
        point = Vector2.zero;
        if (sample == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        facingSign = facingSign >= 0f ? 1f : -1f;
        if (!TryGetAlphaFootTipColumnLockedFromAnkle(sample, ankleWorld, out Vector2 candidate))
            return TryGetAlphaFootTipColumnStrictSide(sample, ankleWorld, facingSign, out point);

        float dx = candidate.x - ankleWorld.x;
        if (Mathf.Abs(dx) > 0.001f && Mathf.Sign(dx) == facingSign)
        {
            point = candidate;
            return true;
        }

        return TryGetAlphaFootTipColumnStrictSide(sample, ankleWorld, facingSign, out point);
    }

    private bool TryGetAlphaFootTipColumnStrictSide(PsbRigSample sample, Vector2 ankleWorld, float facingSign, out Vector2 point)
    {
        point = Vector2.zero;
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        try
        {
            Texture2D tex = sample.sprite.texture;
            Rect tr = sample.sprite.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax), 0, tex.height - 1);
            int xStep = Mathf.Max(1, Mathf.CeilToInt((x1 - x0 + 1) / 160f));
            int yStep = Mathf.Max(1, Mathf.CeilToInt((y1 - y0 + 1) / 120f));
            const float alphaThreshold = 0.08f;

            bool any = false;
            float bestScore = float.NegativeInfinity;
            float bestWorldX = ankleWorld.x;
            float bestTop01 = 0.70f;
            float sign = facingSign >= 0f ? 1f : -1f;

            for (int x = x0; x <= x1; x += xStep)
            {
                int count = 0;
                float sumTop01 = 0f;
                float minTop01 = 1f;
                float maxTop01 = 0f;

                float x01 = Mathf.Clamp01(((float)x + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
                float worldX = Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, x01);
                float dx = worldX - ankleWorld.x;
                if (Mathf.Abs(dx) < 0.001f || Mathf.Sign(dx) != sign)
                    continue;

                for (int y = y0; y <= y1; y += yStep)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.a <= alphaThreshold)
                        continue;

                    float y01FromBottom = Mathf.Clamp01(((float)y + 0.5f - tr.yMin) / Mathf.Max(1f, tr.height));
                    float y01FromTop = 1f - y01FromBottom;
                    count++;
                    sumTop01 += y01FromTop;
                    minTop01 = Mathf.Min(minTop01, y01FromTop);
                    maxTop01 = Mathf.Max(maxTop01, y01FromTop);
                }

                if (count <= 0)
                    continue;

                float height01 = Mathf.Clamp01(Mathf.Abs(maxTop01 - minTop01));
                float horizontal01 = Mathf.Clamp01(Mathf.Abs(dx) / Mathf.Max(1f, sample.rect.width));
                float centerTop01 = Mathf.Clamp01(sumTop01 / Mathf.Max(1, count));
                float yWorld = Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, centerTop01);
                float nearAnkleY01 = 1f - Mathf.Clamp01(Mathf.Abs(yWorld - ankleWorld.y) / Mathf.Max(1f, sample.rect.height));
                float score = horizontal01 * 0.86f + Mathf.Clamp01(height01 * 3f) * 0.10f + nearAnkleY01 * 0.04f;

                if (!any || score > bestScore)
                {
                    any = true;
                    bestScore = score;
                    bestWorldX = worldX;
                    bestTop01 = centerTop01;
                }
            }

            if (!any)
                return false;

            float tipY = Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, bestTop01);
            float yMin = Mathf.Max(sample.rect.yMin, ankleWorld.y - sample.rect.height * 0.22f);
            float yMax = Mathf.Min(sample.rect.yMax, ankleWorld.y + sample.rect.height * 0.62f);
            if (yMax < yMin)
            {
                yMin = sample.rect.yMin;
                yMax = sample.rect.yMax;
            }

            point = new Vector2(Mathf.Clamp(bestWorldX, sample.rect.xMin, sample.rect.xMax), Mathf.Clamp(tipY, yMin, yMax));
            return IsFinite(point);
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetAlphaFootTipColumnLockedFromAnkle(PsbRigSample sample, Vector2 ankleWorld, out Vector2 point)
    {
        point = Vector2.zero;
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        try
        {
            Texture2D tex = sample.sprite.texture;
            Rect tr = sample.sprite.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax), 0, tex.height - 1);

            int xStep = Mathf.Max(1, Mathf.CeilToInt((x1 - x0 + 1) / 160f));
            int yStep = Mathf.Max(1, Mathf.CeilToInt((y1 - y0 + 1) / 120f));
            const float alphaThreshold = 0.08f;

            bool any = false;
            AlphaFootColumnMeasure best = new AlphaFootColumnMeasure { valid = false, score = float.NegativeInfinity };
            float maxHorizontalReference = Mathf.Max(1f, sample.rect.width);

            for (int x = x0; x <= x1; x += xStep)
            {
                int count = 0;
                float sumYFromTop01 = 0f;
                float minYFromTop01 = 1f;
                float maxYFromTop01 = 0f;

                for (int y = y0; y <= y1; y += yStep)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.a <= alphaThreshold)
                        continue;

                    float y01FromBottom = Mathf.Clamp01(((float)y + 0.5f - tr.yMin) / Mathf.Max(1f, tr.height));
                    float y01FromTop = 1f - y01FromBottom;

                    count++;
                    sumYFromTop01 += y01FromTop;
                    minYFromTop01 = Mathf.Min(minYFromTop01, y01FromTop);
                    maxYFromTop01 = Mathf.Max(maxYFromTop01, y01FromTop);
                }

                if (count <= 0)
                    continue;

                any = true;

                float x01 = Mathf.Clamp01(((float)x + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
                float worldX = Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, x01);
                float dx = worldX - ankleWorld.x;
                float horizontal01 = Mathf.Clamp01(Mathf.Abs(dx) / maxHorizontalReference);
                float centerYFromTop01 = Mathf.Clamp01(sumYFromTop01 / Mathf.Max(1, count));
                float height01 = Mathf.Clamp01(Mathf.Abs(maxYFromTop01 - minYFromTop01));

                // 端点锁定重点：水平离 ankle 越远越像脚尖。
                // 但要避免单个噪点，所以要求该列有一定 alpha 高度；Y 只作为弱约束，不再强行找最低点。
                float yWorld = Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, centerYFromTop01);
                float nearAnkleY01 = 1f - Mathf.Clamp01(Mathf.Abs(yWorld - ankleWorld.y) / Mathf.Max(1f, sample.rect.height));
                float score = horizontal01 * 0.82f + Mathf.Clamp01(height01 * 3.0f) * 0.12f + nearAnkleY01 * 0.06f;

                if (!best.valid || score > best.score)
                {
                    best.valid = true;
                    best.worldX = worldX;
                    best.centerYFromTop01 = centerYFromTop01;
                    best.minYFromTop01 = minYFromTop01;
                    best.maxYFromTop01 = maxYFromTop01;
                    best.height01 = height01;
                    best.dxFromAnkle = dx;
                    best.score = score;
                }
            }

            if (!any || !best.valid)
                return false;

            float minUsefulLength = Mathf.Min(sample.rect.width * 0.18f, Mathf.Max(1f, sample.rect.height * 0.40f));
            if (Mathf.Abs(best.dxFromAnkle) < minUsefulLength)
                return false;

            float tipY = Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, best.centerYFromTop01);

            // 脚尖可以略高/略低于 ankle，但不能被贴图顶部或底部噪点拖飞。
            float yMin = Mathf.Max(sample.rect.yMin, ankleWorld.y - sample.rect.height * 0.22f);
            float yMax = Mathf.Min(sample.rect.yMax, ankleWorld.y + sample.rect.height * 0.62f);
            if (yMax < yMin)
            {
                yMin = sample.rect.yMin;
                yMax = sample.rect.yMax;
            }

            point = new Vector2(
                Mathf.Clamp(best.worldX, sample.rect.xMin, sample.rect.xMax),
                Mathf.Clamp(tipY, yMin, yMax));

            return IsFinite(point);
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetAlphaFootToeHardPointInDirection(PsbRigSample sample, Vector2 ankleWorld, float facingSign, out Vector2 point)
    {
        point = Vector2.zero;
        if (!TryGetAlphaFootToeHardPoint(sample, ankleWorld, out Vector2 candidate))
            return false;

        float sign = facingSign >= 0f ? 1f : -1f;
        float dx = candidate.x - ankleWorld.x;
        if (Mathf.Abs(dx) > 0.001f && Mathf.Sign(dx) == sign)
        {
            point = candidate;
            return true;
        }

        return TryGetAlphaFootTipColumnStrictSide(sample, ankleWorld, sign, out point);
    }

    private bool TryGetAlphaFootToeHardPoint(PsbRigSample sample, Vector2 ankleWorld, out Vector2 point)
    {
        point = Vector2.zero;
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        try
        {
            Texture2D tex = sample.sprite.texture;
            Rect tr = sample.sprite.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax), 0, tex.height - 1);
            int xStep = Mathf.Max(1, Mathf.CeilToInt((x1 - x0 + 1) / 120f));
            int yStep = Mathf.Max(1, Mathf.CeilToInt((y1 - y0 + 1) / 96f));
            const float alphaThreshold = 0.08f;

            bool hasLeft = false;
            bool hasRight = false;
            float bestLeftScore = float.NegativeInfinity;
            float bestRightScore = float.NegativeInfinity;
            Vector2 bestLeft = Vector2.zero;
            Vector2 bestRight = Vector2.zero;
            float bestLeftDx = 0f;
            float bestRightDx = 0f;

            float maxDxRef = Mathf.Max(sample.rect.width, 1f);

            for (int y = y0; y <= y1; y += yStep)
            {
                float y01FromBottom = Mathf.Clamp01(((float)y + 0.5f - tr.yMin) / Mathf.Max(1f, tr.height));
                float y01FromTop = 1f - y01FromBottom;

                // 只看脚的中下部，避免脚踝附近竖向轮廓把结果吸回去。
                if (y01FromBottom < 0.38f)
                    continue;

                for (int x = x0; x <= x1; x += xStep)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.a <= alphaThreshold)
                        continue;

                    float x01 = Mathf.Clamp01(((float)x + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
                    Vector2 candidate = new Vector2(
                        Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, x01),
                        Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, y01FromTop));

                    float dx = candidate.x - ankleWorld.x;
                    float dy = candidate.y - ankleWorld.y;
                    float horiz01 = Mathf.Clamp01(Mathf.Abs(dx) / maxDxRef);
                    float down01 = Mathf.Clamp01(y01FromBottom);
                    float dist01 = Mathf.Clamp01(Vector2.Distance(candidate, ankleWorld) / Mathf.Max(1f, sample.rect.size.magnitude));
                    float score = horiz01 * 0.64f + dist01 * 0.26f + down01 * 0.10f;

                    if (dx <= -0.0001f)
                    {
                        if (!hasLeft || score > bestLeftScore)
                        {
                            hasLeft = true;
                            bestLeftScore = score;
                            bestLeft = candidate;
                            bestLeftDx = -dx;
                        }
                    }
                    else if (dx >= 0.0001f)
                    {
                        if (!hasRight || score > bestRightScore)
                        {
                            hasRight = true;
                            bestRightScore = score;
                            bestRight = candidate;
                            bestRightDx = dx;
                        }
                    }
                }
            }

            if (!hasLeft && !hasRight)
                return false;

            if (hasLeft && hasRight)
            {
                // 更硬：哪边离 ankle 的水平延伸更长，就认哪边是脚尖。
                // 只有在两边几乎一样时，才用综合分数决胜。
                if (Mathf.Abs(bestLeftDx - bestRightDx) > sample.rect.width * 0.03f)
                    point = bestLeftDx > bestRightDx ? bestLeft : bestRight;
                else
                    point = bestLeftScore >= bestRightScore ? bestLeft : bestRight;
            }
            else
            {
                point = hasLeft ? bestLeft : bestRight;
            }

            return IsFinite(point);
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetAlphaFootTipByPcaEndpoint(PsbRigSample sample, Vector2 ankleWorld, out Vector2 point)
    {
        point = Vector2.zero;
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        if (!TryGetAlphaAxisMeasure(sample, ankleWorld, out LimbAxisMeasure axis) || !axis.valid)
            return false;

        Vector2 a = axis.near;
        Vector2 b = axis.far;
        Vector2 candidate = (a - ankleWorld).sqrMagnitude >= (b - ankleWorld).sqrMagnitude ? a : b;

        if (!IsFinite(candidate))
            return false;

        // 轻微朝脚掌远端再推出一点，避免端点落在脚背中段。
        Vector2 dir = candidate - ankleWorld;
        float len = dir.magnitude;
        if (len > 0.001f)
        {
            dir /= len;
            float push = Mathf.Min(sample.rect.width, sample.rect.height) * 0.04f;
            candidate += dir * push;
        }

        candidate.x = Mathf.Clamp(candidate.x, sample.rect.xMin, sample.rect.xMax);
        candidate.y = Mathf.Clamp(candidate.y, sample.rect.yMin, sample.rect.yMax);
        point = candidate;
        return IsFinite(point);
    }

    private bool TryGetAlphaFootTipFromAnkle(PsbRigSample sample, float side, Vector2 ankleWorld, out Vector2 point)
    {
        point = Vector2.zero;
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        try
        {
            Texture2D tex = sample.sprite.texture;
            Rect tr = sample.sprite.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax), 0, tex.height - 1);
            int xStep = Mathf.Max(1, Mathf.CeilToInt((x1 - x0 + 1) / 110f));
            int yStep = Mathf.Max(1, Mathf.CeilToInt((y1 - y0 + 1) / 88f));
            const float alphaThreshold = 0.08f;

            float maxDistance = Mathf.Max(1f, sample.rect.size.magnitude);
            bool any = false;
            float bestScore = float.NegativeInfinity;
            float bestX01 = 0.5f;
            float bestTop01 = 0.85f;

            for (int y = y0; y <= y1; y += yStep)
            {
                float y01FromBottom = ((float)y + 0.5f - tr.yMin) / Mathf.Max(1f, tr.height);
                float y01FromTop = 1f - Mathf.Clamp01(y01FromBottom);
                float down01 = Mathf.Clamp01(y01FromBottom);

                for (int x = x0; x <= x1; x += xStep)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.a <= alphaThreshold)
                        continue;

                    float x01 = Mathf.Clamp01(((float)x + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
                    Vector2 candidate = new Vector2(
                        Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, x01),
                        Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, y01FromTop));

                    float dist01 = Mathf.Clamp01(Vector2.Distance(candidate, ankleWorld) / maxDistance);
                    float lowerThanAnkle01 = candidate.y >= ankleWorld.y - sample.rect.height * 0.08f ? 1f : 0f;
                    float outside01 = side > 0f ? x01 : 1f - x01;

                    // v16：脚踝只取小腿末端中心；脚尖由脚图层轮廓决定。
                    // 这里不再用 L/R 外侧作为主方向，避免脚骨线被拉向外侧边缘。
                    // 主标准是：从脚踝出发最远，其次更靠下；L/R 只做极弱的平局项。
                    float score = dist01 * 0.68f + down01 * 0.26f + lowerThanAnkle01 * 0.05f + outside01 * 0.01f;
                    if (!any || score > bestScore)
                    {
                        any = true;
                        bestScore = score;
                        bestX01 = x01;
                        bestTop01 = y01FromTop;
                    }
                }
            }

            if (!any)
                return false;

            point = new Vector2(
                Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, bestX01),
                Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, bestTop01));
            return IsFinite(point);
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetAlphaFootExtremePoint(PsbRigSample sample, float side, out Vector2 point)
    {
        point = Vector2.zero;
        if (sample == null || sample.sprite == null || sample.sprite.texture == null || sample.rect.width <= 0.5f || sample.rect.height <= 0.5f)
            return false;

        try
        {
            Texture2D tex = sample.sprite.texture;
            Rect tr = sample.sprite.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax), 0, tex.height - 1);
            int xStep = Mathf.Max(1, Mathf.CeilToInt((x1 - x0 + 1) / 96f));
            int yStep = Mathf.Max(1, Mathf.CeilToInt((y1 - y0 + 1) / 72f));
            const float alphaThreshold = 0.08f;
            bool any = false;
            float bestScore = -999f;
            float bestX01 = 0.5f;
            float bestTop01 = 0.85f;

            for (int y = y0; y <= y1; y += yStep)
            {
                float y01FromBottom = ((float)y + 0.5f - tr.yMin) / Mathf.Max(1f, tr.height);
                float y01FromTop = 1f - Mathf.Clamp01(y01FromBottom);
                float down01 = Mathf.Clamp01(y01FromBottom);

                for (int x = x0; x <= x1; x += xStep)
                {
                    Color c = tex.GetPixel(x, y);
                    if (c.a <= alphaThreshold)
                        continue;

                    float x01 = Mathf.Clamp01(((float)x + 0.5f - tr.xMin) / Mathf.Max(1f, tr.width));
                    float outside01 = side > 0f ? x01 : 1f - x01;
                    float score = down01 * 0.56f + outside01 * 0.44f;
                    if (!any || score > bestScore)
                    {
                        any = true;
                        bestScore = score;
                        bestX01 = x01;
                        bestTop01 = y01FromTop;
                    }
                }
            }

            if (!any)
                return false;

            point = new Vector2(
                Mathf.Lerp(sample.rect.xMin, sample.rect.xMax, bestX01),
                Mathf.Lerp(sample.rect.yMin, sample.rect.yMax, bestTop01)
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void BuildStableHair(Dictionary<string, Vector2> result, Dictionary<string, List<PsbRigSample>> samples, System.Func<string, Vector2> motion, float characterWidth, float characterHeight)
    {
        Vector2 head = result.ContainsKey("Head") ? result["Head"] : Vector2.zero;
        result["HairBack"] = SampleCenterOr(samples, "HairBack", head + new Vector2(0f, -characterHeight * 0.065f), motion("HairBack"));
        result["HairFront"] = SampleCenterOr(samples, "HairFront", head + new Vector2(0f, -characterHeight * 0.075f), motion("HairFront"));
        result["HairSide_L"] = SampleCenterOr(samples, "HairSide_L", head + new Vector2(-characterWidth * 0.145f, characterHeight * 0.015f), motion("HairSide_L"));
        result["HairSide_R"] = SampleCenterOr(samples, "HairSide_R", head + new Vector2(characterWidth * 0.145f, characterHeight * 0.015f), motion("HairSide_R"));
        result["Braid_L"] = SampleCenterOr(samples, "Braid_L", result["HairSide_L"] + new Vector2(-characterWidth * 0.04f, characterHeight * 0.13f), motion("Braid_L"));
        result["Braid_R"] = SampleCenterOr(samples, "Braid_R", result["HairSide_R"] + new Vector2(characterWidth * 0.04f, characterHeight * 0.13f), motion("Braid_R"));
    }

    private float GetEffectivePreviewLayerWeight(SkyPrisonAnimationRigRow row, PsbSpriteLayout item)
    {
        if (row == null) return 0f;
        float prefabWeight = item != null ? item.prefabLayerWeight : 0f;
        float baseWeight = row.usePsbLayerWeight ? row.psbLayerWeight : 0f;

        // 兼容旧数据：如果之前导入的行还没有写入 psbLayerWeight，
        // 就直接使用 PSB Prefab / SpriteRenderer 的排序权重。
        if (row.usePsbLayerWeight && Mathf.Abs(baseWeight) < 0.0001f && Mathf.Abs(prefabWeight) > 0.0001f)
            baseWeight = prefabWeight;

        float fallback = state.EvaluateTimelineLayerWeightForPsb(row.key, row.boundRigKey, baseWeight + row.manualLayerWeightOffset);
        return state.EvaluateLayerOrderKeyframeWeight(row.key, state.CurrentAction().key, state.CurrentTime, fallback);
    }

    private int GetPsbDrawOrder(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return 0;

        string key = row.boundRigKey ?? string.Empty;
        string semantic = row.semantic ?? string.Empty;
        string rawName = row.name ?? string.Empty;
        string source = row.sourceLayerPath ?? string.Empty;
        string slot = row.slotKey ?? string.Empty;
        string visualSlot = row.visualSlotKey ?? string.Empty;
        string boundRigHierarchyText = BuildBoundRigHierarchyText(key);

        string combinedRaw = string.Join("/", new string[]
        {
            rawName,
            source,
            semantic,
            key,
            boundRigHierarchyText,
            slot,
            visualSlot,
            row.appearanceSlotKey ?? string.Empty,
            row.appearanceLayerKey ?? string.Empty
        });

        string name = combinedRaw.ToLowerInvariant();
        string keyLower = (key + " " + boundRigHierarchyText).ToLowerInvariant();
        string semanticLower = (semantic + " " + boundRigHierarchyText).ToLowerInvariant();
        string compactName = CompactLayerText(combinedRaw);
        string compactSemantic = CompactLayerText(semantic + " " + boundRigHierarchyText);
        string sideProbe = keyLower + " " + semanticLower + " " + name;

        // 新绘制系统：先算“大结构前后层”，再算结构内部偏移。
        // 不再把所有部位丢进一串固定值里，否则想把整条左臂/左腿调到身体前方时，肩、胯、大腿根很容易漏出去。
        // 数值越小越先画，越大越后画，也就越压在上面。
        bool isBackNamed = ContainsLayerToken(name, "behind", "back", "rear") ||
                           rawName.Contains("背后") || rawName.Contains("后置") || rawName.Contains("後置") || rawName.Contains("后片") || rawName.Contains("後片");
        bool isFrontNamed = ContainsLayerToken(name, "front", "fore") || rawName.Contains("前置") || rawName.Contains("前片");

        bool isOutfitLayer = IsPsbOutfitLayer(row, name);

        bool isHairBack = ContainsLayerToken(name, "hair_back", "back_hair", "rear_hair", "braid", "ponytail", "tail_hair") ||
                          compactName.Contains("hairback") || compactSemantic.Contains("hairback") || compactSemantic.Contains("braid") ||
                          combinedRaw.Contains("后发") || combinedRaw.Contains("後髪") || combinedRaw.Contains("後ろ髪") || combinedRaw.Contains("辫") || combinedRaw.Contains("辮");

        bool isHairFront = ContainsLayerToken(name, "hair_front", "front_hair", "hairfront", "fronthair", "bang", "bangs", "fringe") ||
                           compactName.Contains("hairfront") || compactName.Contains("fronthair") || compactSemantic.Contains("hairfront") || compactSemantic.Contains("fronthair") || compactSemantic.Contains("fringe") || compactSemantic.Contains("bang") ||
                           combinedRaw.Contains("前发") || combinedRaw.Contains("前髪") || combinedRaw.Contains("前髮") || combinedRaw.Contains("刘海") || combinedRaw.Contains("劉海") || combinedRaw.Contains("まえがみ");

        bool isHairSide = ContainsLayerToken(name, "hair_side", "side_hair", "sidehair", "hairside", "sidehair_l", "sidehair_r", "hairside_l", "hairside_r") ||
                          compactName.Contains("hairside") || compactName.Contains("sidehair") || compactSemantic.Contains("hairside") || compactSemantic.Contains("sidehair") ||
                          combinedRaw.Contains("侧发") || combinedRaw.Contains("側髪") || combinedRaw.Contains("横髪") || combinedRaw.Contains("サイドヘア");

        bool isBrow = ContainsLayerToken(name, "brow", "brows", "eyebrow", "eyebrows", "eye_brow", "mayu", "mayuge", "brow_l", "brow_r", "l_brow", "r_brow", "eyebrow_l", "eyebrow_r") ||
                      compactName.Contains("eyebrow") || compactName.Contains("eyebrowl") || compactName.Contains("eyebrowr") ||
                      compactName.Contains("browl") || compactName.Contains("browr") ||
                      compactSemantic.Contains("eyebrow") || compactSemantic.Contains("brow") ||
                      combinedRaw.Contains("眉毛") || combinedRaw.Contains("眉") || combinedRaw.Contains("まゆ") || combinedRaw.Contains("マユ");

        bool isHeadAccessory = ContainsLayerToken(name, "hat", "cap", "head_accessory", "headaccessory", "hair_accessory", "hairaccessory", "accessory_head", "ribbon_head") ||
                               compactName.Contains("headaccessory") || compactName.Contains("hairaccessory") ||
                               rawName.Contains("帽") || rawName.Contains("头饰") || rawName.Contains("頭飾") || rawName.Contains("发饰") || rawName.Contains("髪飾");

        bool isWeapon = ContainsLayerToken(name, "weapon", "spade", "sword", "blade", "hammer", "gun", "staff", "spear", "axe") || semanticLower.Contains("weapon") || rawName.Contains("武器");

        // 武器柄只认显式命名：*_handle。
        // 这样 weapon_back / hair_back / 普通 back 不会再和“柄”语义混在一起。
        bool isWeaponHandle = isWeapon && IsExplicitWeaponHandleLayer(row, combinedRaw);

        // 独立 Back 层：辫子 / 后发 / *_back / back / behind / rear 都必须先进入低层，
        // 不能再被“头发最高层”或“上衣/左臂夹层”抢走。
        // 武器除外：武器本体始终走最前景；只有 *_handle 进入手下方的柄层。
        bool isDedicatedBackLayer = !isWeapon && (isBackNamed || isHairBack);

        bool isCollar = ContainsLayerToken(name, "collar", "neck_cloth", "neckcloth", "neckwear") || rawName.Contains("领") || rawName.Contains("領") || rawName.Contains("襟");

        bool isArmClothNamed = ContainsLayerToken(name,
            "jacket_l_upper", "jacket_l_lower", "jacket_r_upper", "jacket_r_lower",
            "jacket_left_upper", "jacket_left_lower", "jacket_right_upper", "jacket_right_lower",
            "left_jacket_upper", "left_jacket_lower", "right_jacket_upper", "right_jacket_lower",
            "coat_l_upper", "coat_l_lower", "coat_r_upper", "coat_r_lower",
            "shirt_l_upper", "shirt_l_lower", "shirt_r_upper", "shirt_r_lower",
            "sleeve", "cuff", "glove",
            "袖", "上袖", "下袖", "袖口", "手套");

        bool looksLikeArm = isArmClothNamed ||
                            IsRigKeyAny(key, "Shoulder_L", "Elbow_L", "Wrist_L", "HandEnd_L", "Shoulder_R", "Elbow_R", "Wrist_R", "HandEnd_R") ||
                            ContainsLayerToken(name, "arm", "hand", "sleeve", "glove", "wrist", "shoulder", "upper_arm", "lower_arm", "forearm", "elbow") ||
                            rawName.Contains("臂") || rawName.Contains("腕") || rawName.Contains("手") || rawName.Contains("袖") || rawName.Contains("肩");

        bool looksLikeLeg = !isArmClothNamed &&
                            (IsRigKeyAny(key, "Hip_L", "Knee_L", "Ankle_L", "Foot_L", "Hip_R", "Knee_R", "Ankle_R", "Foot_R") ||
                             ContainsLayerToken(name, "leg", "foot", "shoe", "sock", "boot", "thigh", "calf", "hip", "knee", "ankle") ||
                             rawName.Contains("腿") || rawName.Contains("脚") || rawName.Contains("足") || rawName.Contains("鞋") || rawName.Contains("袜") || rawName.Contains("胯") || rawName.Contains("膝"));

        bool leftSide = IsLikelyLeftSide(name, sideProbe);
        bool rightSide = IsLikelyRightSide(name, sideProbe);

        bool isLeftArm = IsRigKeyAny(key, "Shoulder_L", "Elbow_L", "Wrist_L", "HandEnd_L") ||
                         keyLower.Contains("shoulder_l") || keyLower.Contains("elbow_l") || keyLower.Contains("wrist_l") || keyLower.Contains("handend_l") ||
                         keyLower.Contains("hand_l") || keyLower.Contains("arm_l") ||
                         semanticLower.Contains("shoulder_l") || semanticLower.Contains("elbow_l") || semanticLower.Contains("wrist_l") || semanticLower.Contains("handend_l") ||
                         (looksLikeArm && leftSide);

        bool isRightArm = IsRigKeyAny(key, "Shoulder_R", "Elbow_R", "Wrist_R", "HandEnd_R") ||
                          keyLower.Contains("shoulder_r") || keyLower.Contains("elbow_r") || keyLower.Contains("wrist_r") || keyLower.Contains("handend_r") ||
                          keyLower.Contains("hand_r") || keyLower.Contains("arm_r") ||
                          semanticLower.Contains("shoulder_r") || semanticLower.Contains("elbow_r") || semanticLower.Contains("wrist_r") || semanticLower.Contains("handend_r") ||
                          (looksLikeArm && rightSide);

        bool isLeftLeg = IsRigKeyAny(key, "Hip_L", "Knee_L", "Ankle_L", "Foot_L") ||
                         keyLower.Contains("hip_l") || keyLower.Contains("knee_l") || keyLower.Contains("ankle_l") || keyLower.Contains("foot_l") ||
                         semanticLower.Contains("hip_l") || semanticLower.Contains("knee_l") || semanticLower.Contains("ankle_l") || semanticLower.Contains("foot_l") ||
                         (looksLikeLeg && leftSide);

        bool isRightLeg = IsRigKeyAny(key, "Hip_R", "Knee_R", "Ankle_R", "Foot_R") ||
                          keyLower.Contains("hip_r") || keyLower.Contains("knee_r") || keyLower.Contains("ankle_r") || keyLower.Contains("foot_r") ||
                          semanticLower.Contains("hip_r") || semanticLower.Contains("knee_r") || semanticLower.Contains("ankle_r") || semanticLower.Contains("foot_r") ||
                          (looksLikeLeg && rightSide);

        // 关键修正：四肢归属必须强于 Body / Top / Lower / Chest / Pelvis 父路径。
        // combinedRaw 里包含 sourceLayerPath 和绑定父链，如果不在这里硬锁，
        // 左肩、左大腿这种靠近躯干的叶子会因为父路径里的 Body/Lower 被吞回身体层。
        if (IsLeftArmRigKey(key))
        {
            isLeftArm = true;
            isRightArm = false;
        }

        if (IsRightArmRigKey(key))
        {
            isRightArm = true;
            isLeftArm = false;
        }

        if (IsLeftLegRigKey(key))
        {
            isLeftLeg = true;
            isRightLeg = false;
        }

        if (IsRightLegRigKey(key))
        {
            isRightLeg = true;
            isLeftLeg = false;
        }

        bool confirmedLimb = isLeftArm || isRightArm || isLeftLeg || isRightLeg;

        // 鞋袜必须用“本图层自己的身份”来判断，不能用 combinedRaw。
        // combinedRaw 会混入父路径、绑定父链、slot，右鞋可能因为父链里出现左腿/左袜词而被误判。
        string localLayerIdentity = BuildLocalLayerIdentity(row).ToLowerInvariant();
        int footWearSide = ResolveFootWearSide(row, key, localLayerIdentity, leftSide, rightSide);

        bool isSock = ContainsLayerToken(localLayerIdentity, "sock", "socks", "stocking", "stockings", "tights") || rawName.Contains("袜") || rawName.Contains("靴下");
        bool isShoe = !isSock && (ContainsLayerToken(localLayerIdentity, "shoe", "shoes", "boot", "boots", "heel", "heels") || rawName.Contains("鞋") || rawName.Contains("靴"));
        bool isLegCloth = !isArmClothNamed && (ContainsLayerToken(name, "leg", "pants", "skirt", "shorts", "bottom", "lower", "shoe", "sock", "boot", "tights") || rawName.Contains("腿") || rawName.Contains("裤") || rawName.Contains("褲") || rawName.Contains("裙") || rawName.Contains("鞋") || rawName.Contains("袜"));

        // 衣装专用：下装必须作为“覆盖裸腿的衣服层”，不能因为绑定到 Hip/Knee/Ankle/Foot 就被当成裸腿本体。
        // 裸模层级保持不变；这里只把 Outfit 里的裤子/裙子/下装片抬到双腿之上、左臂之下。
        bool isLowerOutfitCloth = isOutfitLayer &&
                                  !isArmClothNamed &&
                                  !isSock &&
                                  !isShoe &&
                                  (ContainsLayerToken(name,
                                       "pants", "skirt", "shorts", "bottom", "lower_cloth", "lowerbody_cloth",
                                       "lower_body_cloth", "waist_cloth", "hip_cloth", "pelvis_cloth") ||
                                   rawName.Contains("裤") || rawName.Contains("褲") || rawName.Contains("裙") ||
                                   rawName.Contains("下装") || rawName.Contains("下裝") || rawName.Contains("下身衣") ||
                                   rawName.Contains("腰布"));

        // 上衣也走独立夹层。
        // 右袖/右手衣片 < 上衣躯干 < 左袖/左手衣片 < 领子 < 头/头饰。
        // 这样不会改裸模的右臂/右腿/左臂前后关系，只把 Outfit 的上衣从裸身体层里单独抽出来。
        bool isUpperOutfitBodyCloth = isOutfitLayer &&
                                      !confirmedLimb &&
                                      !isLowerOutfitCloth &&
                                      !isCollar &&
                                      !isHeadAccessory &&
                                      !isHairBack &&
                                      !isHairFront &&
                                      !isHairSide &&
                                      (IsRigKeyAny(key, "Spine", "Chest", "Body", "Neck") ||
                                       ContainsLayerToken(name,
                                           "top", "upper", "jacket", "coat", "shirt", "torso", "body", "chest", "suit", "armor",
                                           "upper_cloth", "torso_cloth", "body_cloth", "chest_cloth") ||
                                       rawName.Contains("上衣") || rawName.Contains("上半身") ||
                                       rawName.Contains("衣") || rawName.Contains("服") ||
                                       rawName.Contains("胸") || rawName.Contains("身体") || rawName.Contains("身體"));
        // 身体层不能反向吞掉四肢。
        // sourceLayerPath / 父层级里经常带 Body / Top / Lower，
        // 如果先按身体关键词归类，Shoulder_L / Hip_L 这种已经绑定到左侧骨骼的整条肢体，
        // 会被重新压回上身/下身层。这里先排除已确认的四肢。
        bool isLowerBody = !confirmedLimb &&
                           !isArmClothNamed &&
                           !looksLikeArm &&
                           !(looksLikeLeg && (leftSide || rightSide)) &&
                           (IsRigKeyAny(key, "Pelvis") ||
                            ContainsLayerToken(name, "bottom", "lower", "skirt", "pants", "shorts", "pelvis") ||
                            rawName.Contains("裙") || rawName.Contains("裤") || rawName.Contains("褲") || rawName.Contains("下装") || rawName.Contains("下裝") || rawName.Contains("下身"));
        bool isUpperBody = !confirmedLimb &&
                           !isArmClothNamed &&
                           !looksLikeArm &&
                           !(looksLikeLeg && (leftSide || rightSide)) &&
                           (IsRigKeyAny(key, "Pelvis", "Spine", "Chest", "Body", "Neck") ||
                            ContainsLayerToken(name, "top", "upper", "jacket", "coat", "shirt", "torso", "body", "chest", "suit", "armor") ||
                            rawName.Contains("上衣") || rawName.Contains("上半身") || rawName.Contains("胸") || rawName.Contains("身体") || rawName.Contains("身體"));
        bool isEye = IsRigKeyAny(key, "Eye_L", "Eye_R") ||
                     keyLower.Contains("eye_l") || keyLower.Contains("eye_r") || semanticLower.Contains("eye_l") || semanticLower.Contains("eye_r") ||
                     ContainsLayerToken(name, "eye", "eyes", "eyeball", "eye_white", "white_eye", "iris", "pupil", "hitomi", "瞳", "眼", "目", "眼白", "瞳孔");
        bool isMouth = IsRigKeyAny(key, "Mouth") || keyLower.Contains("mouth") || semanticLower.Contains("mouth") || ContainsLayerToken(name, "mouth", "lip", "lips", "口", "嘴");
        bool isHead = IsRigKeyAny(key, "Head", "HeadTop") || semanticLower.Contains("head") || ContainsLayerToken(name, "head", "face") || rawName == "头" || rawName == "頭" || rawName.Contains("脸") || rawName.Contains("顔") || rawName.Contains("顔");

        int order;

        // Back 层是最低优先级夹层，必须先于头发/头饰/衣服/四肢判断。
        // 否则 braid / hair_back / *_back 会因为“头发高层”规则被抬到最前面。
        if (isDedicatedBackLayer)
            order = -80 + Mathf.Clamp(state.GetSemanticIndex(row.semantic), 0, 20);
        // 武器层必须先于头/手/衣服判断。
        // 之前普通武器放在 looksLikeArm / isLeftArm / isRightArm 之后，
        // 绑定到 Hand_L / Hand_R 的武器头会先被手臂规则吃掉，导致画在手下面。
        // *_handle 仍然是例外：它是“柄”，必须显示在对应手下面。
        else if (isWeaponHandle)
            order = GetWeaponHandleDrawOrder(key, name, leftSide, rightSide);
        else if (isWeapon)
            order = GetWeaponFrontDrawOrder(key, name);
        // 头/领子是最高优先级夹层，必须先于上衣、左臂、身体判断。
        // 否则它们会因为 sourceLayerPath / boundRigHierarchyText 里带有 Body/Chest/Shoulder，
        // 被误吃进上衣躯干或左臂层，导致“领子没有压到左手上面、头没有压到领子上面”。
        else if (isHeadAccessory)
            order = 238;
        else if (isBrow)
            order = 236;
        else if (isHairFront)
            order = 232;
        else if (isHairSide)
            order = 228;
        else if (isEye)
            order = 226;
        else if (isMouth)
            order = 224;
        else if (isHead)
            order = isOutfitLayer ? 222 : 220;
        else if (isHairBack)
            order = 218;
        else if (isCollar)
            order = GetCollarDrawOrder(key, name);
        else if (isBackNamed && !isWeapon)
            order = 8;
        else if (isLowerOutfitCloth)
            order = GetLowerOutfitDrawOrder(key, name);
        else if (isSock)
            order = GetSockDrawOrder(footWearSide);
        else if (isShoe)
            order = GetShoeDrawOrder(footWearSide);
        else if (isRightLeg)
            order = GetRightLegDrawOrder(key, name, isOutfitLayer);
        else if (isRightArm)
            // 裸右臂仍保持在右腿之下；但右袖/右手衣片进入“上衣夹层”的最低层。
            order = GetRightArmDrawOrder(key, name, isOutfitLayer);
        else if (isUpperOutfitBodyCloth)
            // 上衣躯干必须高于下装，也高于右袖/右手衣片。
            order = GetUpperOutfitBodyDrawOrder(key, name);
        else if (isLeftLeg)
            // 角色自身左腿：整条链路 Hip_L -> Knee_L -> Ankle_L -> Foot_L 都必须高于上身/下身。
            order = GetLeftLegDrawOrder(key, name, isOutfitLayer);
        else if (isLeftArm)
            // 左臂/左手在上衣躯干之上，领子之下。
            order = GetLeftArmDrawOrder(key, name, isOutfitLayer);
        else if ((isLowerBody || isLegCloth || isSock || isShoe) && !isLeftLeg && !isRightLeg)
            order = 58 + GetBodyPartDrawOffset(key, name, false, isOutfitLayer);
        else if (isUpperBody && !isCollar && !isHead && !isEye && !isMouth)
            order = 68 + GetBodyPartDrawOffset(key, name, true, isOutfitLayer);
        else if (looksLikeLeg)
            order = isFrontNamed ? 82 + GetLegSegmentDrawOffset(key, name) + (isOutfitLayer ? 8 : 0) : 58 + GetBodyPartDrawOffset(key, name, false, isOutfitLayer);
        else if (looksLikeArm)
            order = GetNeutralArmDrawOrder(key, name, isOutfitLayer, isBackNamed, isFrontNamed);
        else
            order = (isOutfitLayer ? 76 : 72) + state.GetSemanticIndex(row.semantic);

        // 鞋袜已经使用“右袜 < 右鞋 < 左袜 < 左鞋”的硬分层。
        // 这里不能再吃 _front / _back 的通用偏移，否则 right_shoe_front 会从 108 被抬到 111，
        // 直接越过 left_sock(109)，表现为“右腿鞋子压到左腿袜子上面”。
        // front/back 只适合身体、头发、衣片的小范围微调，不适合跨左右腿组的足部夹层。
        bool lockFootWearFrontBackBias = isSock || isShoe;
        bool lockHeadFrontBackBias = isDedicatedBackLayer || isBrow || isHairFront || isHairSide || isHairBack || isHeadAccessory || isEye || isMouth;
        if (!lockHeadFrontBackBias && !lockFootWearFrontBackBias)
        {
            if (isFrontNamed && !isWeapon)
                order += 3;
            if (isBackNamed && order > 8 && !isWeapon)
                order -= 3;
        }

        return order;
    }

    private int GetLowerOutfitDrawOrder(string rigKey, string normalizedName)
    {
        // 下装衣物独立夹层：
        // 现在它要高于右手/右袖夹层，但仍低于上衣躯干、左手、领子和头。
        // 目标大层级：右手/右袖 < 下装 < 上衣躯干 < 左手/左袖 < 领子 < 头/武器。
        string n = normalizedName ?? string.Empty;

        if (ContainsLayerToken(n, "belt", "waist", "waistband", "腰带", "腰帶", "腰"))
            return 133;
        if (ContainsLayerToken(n, "skirt", "dress_hem", "hem", "裙", "裙摆", "裙擺"))
            return 132;
        if (ContainsLayerToken(n, "pants", "shorts", "bottom", "lower_cloth", "裤", "褲", "下装", "下裝"))
            return 131;

        return 131;
    }

    private string BuildLocalLayerIdentity(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return string.Empty;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
        AppendIdentityPart(sb, row.name);
        AppendIdentityPart(sb, GetLayerPathLeaf(row.sourceLayerPath));
        AppendIdentityPart(sb, row.slotKey);
        AppendIdentityPart(sb, row.visualSlotKey);
        AppendIdentityPart(sb, row.appearanceSlotKey);
        AppendIdentityPart(sb, row.appearanceLayerKey);
        return sb.ToString();
    }

    private void AppendIdentityPart(System.Text.StringBuilder sb, string value)
    {
        if (sb == null || string.IsNullOrWhiteSpace(value))
            return;

        if (sb.Length > 0)
            sb.Append('/');
        sb.Append(value);
    }

    private string GetLayerPathLeaf(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        string p = path.Replace('\\', '/');
        int index = p.LastIndexOf('/');
        if (index >= 0 && index + 1 < p.Length)
            return p.Substring(index + 1);

        return p;
    }

    private int ResolveFootWearSide(SkyPrisonAnimationRigRow row, string rigKey, string localIdentity, bool broadLeftSide, bool broadRightSide)
    {
        // 返回：-1 = 左腿 / 前景腿，1 = 右腿 / 后景腿，0 = 未知。
        // 鞋袜层级的根因不是 front/back 偏移，而是“侧别判断使用了 combinedRaw”。
        // combinedRaw 会把 sourceLayerPath、绑定父链、slot 都混在一起；一个右鞋图层可能同时带到 left/right 词。
        // 所以这里的优先级必须是：绑定骨骼 Key > 图层自身叶子名/slot > 严格左右兜底。
        string k = (rigKey ?? string.Empty).ToLowerInvariant();

        if (IsLeftLegRigKey(rigKey) ||
            k.Contains("hip_l") || k.Contains("knee_l") || k.Contains("ankle_l") || k.Contains("foot_l") ||
            k.Contains("leg_l"))
            return -1;

        if (IsRightLegRigKey(rigKey) ||
            k.Contains("hip_r") || k.Contains("knee_r") || k.Contains("ankle_r") || k.Contains("foot_r") ||
            k.Contains("leg_r"))
            return 1;

        string local = localIdentity ?? string.Empty;
        bool localLeft = IsLikelyLeftSide(local, string.Empty);
        bool localRight = IsLikelyRightSide(local, string.Empty);

        if (localLeft && !localRight)
            return -1;
        if (localRight && !localLeft)
            return 1;

        // 再兜一次更硬的 compact 叶子规则，避免 shoeL / sockR 这类无分隔命名漏掉。
        string compact = CompactLayerText(local);
        bool compactLeft = compact.Contains("lshoe") || compact.Contains("shoel") || compact.Contains("lsock") || compact.Contains("sockl") ||
                           compact.Contains("lboot") || compact.Contains("bootl") || compact.Contains("lheel") || compact.Contains("heell") ||
                           compact.Contains("leftshoe") || compact.Contains("leftsock") || compact.Contains("leftboot") || compact.Contains("leftheel");
        bool compactRight = compact.Contains("rshoe") || compact.Contains("shoer") || compact.Contains("rsock") || compact.Contains("sockr") ||
                            compact.Contains("rboot") || compact.Contains("bootr") || compact.Contains("rheel") || compact.Contains("heelr") ||
                            compact.Contains("rightshoe") || compact.Contains("rightsock") || compact.Contains("rightboot") || compact.Contains("rightheel");

        if (compactLeft && !compactRight)
            return -1;
        if (compactRight && !compactLeft)
            return 1;

        // broadLeftSide / broadRightSide 来自旧 combinedRaw，只能在单边非常明确时兜底。
        if (broadLeftSide && !broadRightSide)
            return -1;
        if (broadRightSide && !broadLeftSide)
            return 1;

        return 0;
    }

    private int GetSockDrawOrder(int side)
    {
        // 足部衣物硬分层：右袜 < 右鞋 < 左袜 < 左鞋。
        // 未知侧默认按右腿/后景腿处理，避免误压前景左腿。
        return side < 0 ? 109 : 106;
    }

    private int GetShoeDrawOrder(int side)
    {
        // 同腿内部 shoe > sock；整体仍然左腿组 > 右腿组。
        return side < 0 ? 111 : 108;
    }

    private int GetUpperOutfitBodyDrawOrder(string rigKey, string normalizedName)
    {
        // 上衣躯干夹层：高于下装，高于右袖/右手，低于左臂与领子。
        // 右上衣肢体约 112..130，下装现在是 131..133；这里使用 136..140。
        string k = (rigKey ?? string.Empty).ToLowerInvariant();
        string n = normalizedName ?? string.Empty;

        if (ContainsLayerToken(n, "strap", "belt", "band", "胸带", "胸帶", "绑带", "綁帶"))
            return 140;
        if (ContainsLayerToken(n, "chest", "front", "torso", "body", "胸", "前片", "身体", "身體"))
            return 138;
        if (k.Contains("chest") || k.Contains("body") || k.Contains("spine"))
            return 136;

        return 136;
    }

    private int GetCollarDrawOrder(string rigKey, string normalizedName)
    {
        // 领子是上衣最高层：必须高于左臂与上衣躯干，但低于头、脸、刘海、头饰。
        return 210;
    }

    private int GetWeaponFrontDrawOrder(string rigKey, string normalizedName)
    {
        // 武器本体永远是最前景层。
        // 这样 blade / axe / gun / spade 不再被 _back 或父层级误压到角色后面。
        return 248;
    }

    private int GetWeaponHandleDrawOrder(string rigKey, string normalizedName, bool leftSide, bool rightSide)
    {
        // 武器柄使用 *_handle 语义，显示在对应手的下面。
        // 左手前景层从 144 起，因此左手柄放在 143；
        // 右手后景层从 1 起，因此右手柄放在 0。
        string k = (rigKey ?? string.Empty).ToLowerInvariant();
        string n = normalizedName ?? string.Empty;

        bool right = rightSide || IsRightArmRigKey(rigKey) || k.Contains("_r") || ContainsLayerToken(n, "_r", "right", "right_hand", "hand_r");
        bool left = leftSide || IsLeftArmRigKey(rigKey) || k.Contains("_l") || ContainsLayerToken(n, "_l", "left", "left_hand", "hand_l");

        if (right && !left)
            return 0;

        return 143;
    }

    private bool IsExplicitWeaponHandleLayer(SkyPrisonAnimationRigRow row, string combinedRaw)
    {
        // 只认 *_handle，避免和 Back 层、控制点 handle、普通 hilt/grip 等词发生语义混淆。
        // 这里故意不认单独的 "handle"，必须有下划线。
        if (row == null && string.IsNullOrEmpty(combinedRaw))
            return false;

        return HasUnderscoreHandle(row != null ? row.name : null) ||
               HasUnderscoreHandle(row != null ? row.sourceLayerPath : null) ||
               HasUnderscoreHandle(row != null ? row.slotKey : null) ||
               HasUnderscoreHandle(row != null ? row.visualSlotKey : null) ||
               HasUnderscoreHandle(row != null ? row.appearanceSlotKey : null) ||
               HasUnderscoreHandle(row != null ? row.appearanceLayerKey : null) ||
               HasUnderscoreHandle(combinedRaw);
    }

    private bool HasUnderscoreHandle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        string t = text.ToLowerInvariant();
        return t.Contains("_handle");
    }

    private int GetBodyPartDrawOffset(string rigKey, string normalizedName, bool upperBody, bool isOutfitLayer)
    {
        // 身体内部也给一层细分，方便以后整体调 Body band，而不用挨个改常量。
        string k = (rigKey ?? string.Empty).ToLowerInvariant();
        string n = normalizedName ?? string.Empty;

        if (!upperBody)
        {
            if (ContainsLayerToken(n, "skirt", "pants", "shorts", "bottom", "lower_cloth") || n.Contains("裙") || n.Contains("裤") || n.Contains("褲") || n.Contains("下装") || n.Contains("下裝"))
                return isOutfitLayer ? 8 : 6;
            if (k.Contains("pelvis") || ContainsLayerToken(n, "pelvis", "hip", "waist") || n.Contains("胯") || n.Contains("腰"))
                return isOutfitLayer ? 6 : 2;
            return isOutfitLayer ? 8 : 0;
        }

        if (ContainsLayerToken(n, "jacket", "coat", "shirt", "top", "upper_cloth", "torso_cloth") || n.Contains("上衣") || n.Contains("衣") || n.Contains("服"))
            return isOutfitLayer ? 8 : 6;
        if (k.Contains("chest") || ContainsLayerToken(n, "chest", "torso", "body") || n.Contains("胸") || n.Contains("身体") || n.Contains("身體"))
            return isOutfitLayer ? 6 : 2;
        return isOutfitLayer ? 8 : 0;
    }

    private int GetOutfitGroupOffset(bool isOutfitLayer)
    {
        // 旧版把“服装整体 +10”，然后 hand / wrist 又被放到 segment 4，
        // 结果只要裸手被误判为服装层，就会直接冲到袖子上面。
        // 手臂现在不再用这个函数决定内部层级；保留给腿部旧逻辑调用。
        return isOutfitLayer ? 10 : 0;
    }

    private enum ArmDrawPart
    {
        HandSkin = 0,
        WristSkin = 1,
        UpperArmSkin = 2,
        LowerArmSkin = 3,
        UpperSleeve = 12,
        LowerSleeve = 14,
        Cuff = 16,
        Glove = 18,
    }

    private int GetArmPartDrawOffset(string rigKey, string normalizedName, bool isOutfitLayer)
    {
        // 手臂内部这次按“部位语义”硬拆，不再把 hand / wrist 和 glove 合并。
        // 视觉目标：袖子 / 袖口压住手腕与手；只有真正手套才压在最上面。
        // 从下到上：手 -> 手腕 -> 裸上/下臂 -> 上袖 -> 下袖 -> 袖口 -> 手套。
        string k = (rigKey ?? string.Empty).ToLowerInvariant();
        string n = normalizedName ?? string.Empty;

        bool explicitGlove = ContainsLayerToken(n,
            "glove", "gloves", "mitten", "mittens",
            "l_glove", "r_glove", "glove_l", "glove_r", "left_glove", "right_glove",
            "手套");

        bool explicitCuff = ContainsLayerToken(n,
            "cuff", "cuffs", "sleeve_cuff", "cuff_l", "cuff_r", "l_cuff", "r_cuff",
            "wristband", "bracelet",
            "袖口", "腕饰", "腕飾", "护腕", "護腕");

        bool explicitUpperSleeve = ContainsLayerToken(n,
            "upper_sleeve", "sleeve_upper", "uppersleeve",
            "upper_arm_sleeve", "sleeve_l_upper", "sleeve_r_upper",
            "jacket_l_upper", "jacket_r_upper",
            "上袖", "上臂袖", "大臂袖");

        bool explicitLowerSleeve = ContainsLayerToken(n,
            "lower_sleeve", "sleeve_lower", "lowersleeve",
            "forearm_sleeve", "sleeve_l_lower", "sleeve_r_lower",
            "jacket_l_lower", "jacket_r_lower",
            "下袖", "下臂袖", "前臂袖");

        bool explicitAnySleeve = ContainsLayerToken(n,
            "sleeve", "l_sleeve", "r_sleeve", "sleeve_l", "sleeve_r",
            "left_sleeve", "right_sleeve", "袖");

        bool explicitUpperArmSkin = ContainsLayerToken(n,
            "upper_arm", "arm_upper", "upperarm", "shoulder", "shoulder_l", "shoulder_r", "arm_l_upper", "arm_r_upper",
            "l_upper_arm", "r_upper_arm", "left_upper_arm", "right_upper_arm", "l_shoulder", "r_shoulder", "left_shoulder", "right_shoulder",
            "上臂", "大臂", "肩");

        bool explicitLowerArmSkin = ContainsLayerToken(n,
            "lower_arm", "arm_lower", "forearm", "lowerarm", "arm_l_lower", "arm_r_lower",
            "l_lower_arm", "r_lower_arm", "left_lower_arm", "right_lower_arm",
            "下臂", "前臂");

        bool explicitWristSkin = ContainsLayerToken(n,
            "wrist", "l_wrist", "r_wrist", "wrist_l", "wrist_r", "left_wrist", "right_wrist",
            "腕");

        bool explicitHandSkin = ContainsLayerToken(n,
            "hand", "l_hand", "r_hand", "hand_l", "hand_r", "left_hand", "right_hand",
            "手");

        // 名字永远优先于绑定骨骼：袖口/袖子经常绑定到 Wrist / HandEnd。
        if (explicitGlove)
            return (int)ArmDrawPart.Glove;
        if (explicitCuff)
            return (int)ArmDrawPart.Cuff;
        if (explicitUpperSleeve)
            return (int)ArmDrawPart.UpperSleeve;
        if (explicitLowerSleeve)
            return (int)ArmDrawPart.LowerSleeve;
        if (explicitAnySleeve)
            return (int)ArmDrawPart.LowerSleeve;

        // 关键修正第二版：
        // hand / wrist 这个名字本身不能直接等于“裸手”。
        // 在基础 Body 槽里，它通常确实是裸手 / 裸手腕，必须压到袖子下面。
        // 但在外观 / 服装 PSB 里，很多图层会沿用绑定骨骼名 Hand/Wrist，
        // 实际内容却是袖口、袖端、手部服装片。上一版把所有 hand/wrist 都压成裸手层，
        // 结果服装袖端也被压低，裸模手依旧会穿出来。
        // 所以：Body 手 = 裸手低层；Outfit 手/腕 = 袖端/袖口高层；真正手套已在上面 explicitGlove 吃掉。
        if (explicitHandSkin)
            return isOutfitLayer ? (int)ArmDrawPart.Cuff : (int)ArmDrawPart.HandSkin;
        if (explicitWristSkin)
            return isOutfitLayer ? (int)ArmDrawPart.Cuff : (int)ArmDrawPart.WristSkin;

        if (explicitUpperArmSkin)
            return isOutfitLayer ? (int)ArmDrawPart.UpperSleeve : (int)ArmDrawPart.UpperArmSkin;
        if (explicitLowerArmSkin)
            return isOutfitLayer ? (int)ArmDrawPart.LowerSleeve : (int)ArmDrawPart.LowerArmSkin;

        if (k.Contains("handend")) return (int)ArmDrawPart.HandSkin;
        if (k.Contains("wrist")) return (int)ArmDrawPart.WristSkin;
        if (k.Contains("shoulder")) return isOutfitLayer ? (int)ArmDrawPart.UpperSleeve : (int)ArmDrawPart.UpperArmSkin;
        if (k.Contains("elbow")) return isOutfitLayer ? (int)ArmDrawPart.LowerSleeve : (int)ArmDrawPart.LowerArmSkin;

        return isOutfitLayer ? (int)ArmDrawPart.LowerSleeve : (int)ArmDrawPart.LowerArmSkin;
    }

    private int GetLegSegmentDrawOffset(string rigKey, string normalizedName)
    {
        // 同一腿部内部：大腿/髋 -> 小腿/膝 -> 脚/鞋/袜。
        string k = (rigKey ?? string.Empty).ToLowerInvariant();
        if (k.Contains("hip") || ContainsLayerToken(normalizedName, "upper_leg", "leg_upper", "thigh", "大腿", "上腿")) return 0;
        if (k.Contains("knee") || ContainsLayerToken(normalizedName, "lower_leg", "leg_lower", "shin", "calf", "小腿", "下腿", "膝")) return 2;
        if (k.Contains("ankle") || k.Contains("foot") || ContainsLayerToken(normalizedName, "foot", "toe", "shoe", "sock", "boot", "脚", "足", "鞋", "袜", "靴")) return 4;
        return 1;
    }

    private int GetNeutralArmDrawOrder(string rigKey, string normalizedName, bool isOutfitLayer, bool isBackNamed, bool isFrontNamed)
    {
        // 兜底处理“名字像手/袖子，但没有成功绑定到 L/R 骨骼”的 PSB 叶子。
        // 裸手默认压低到身体后侧，避免未绑定裸模手冲到袖子/身体最前。
        // 服装袖子默认走前侧，保证衣服读到了就能盖住裸模；真正左右关系仍优先由 boundRigKey / L/R 名字决定。
        int part = GetArmPartDrawOffset(rigKey, normalizedName, isOutfitLayer);

        bool explicitClothArm = ContainsLayerToken(normalizedName,
            "sleeve", "cuff", "glove", "jacket", "coat", "shirt",
            "袖", "袖口", "手套", "护腕", "護腕");

        if (isBackNamed)
            return 42 + part;
        if (isFrontNamed)
            return 82 + part;

        if (explicitClothArm || isOutfitLayer)
            return 82 + part;

        return 42 + part;
    }

    private int GetRightArmDrawOrder(string rigKey, string normalizedName, bool isOutfitLayer)
    {
        int segment = GetArmPartDrawOffset(rigKey, normalizedName, isOutfitLayer);

        if (isOutfitLayer)
        {
            // 上衣夹层最低层：右袖/右手衣片。
            // 它要高于下装，但低于上衣躯干。范围 112..130。
            return 112 + segment;
        }

        // 裸右臂/右手仍放到右腿之下。
        // 右腿当前从 20 起跳；右臂内部最大偏移是 18，所以这里从 1 起跳，范围 1..19。
        return 1 + segment;
    }

    private int GetLeftArmDrawOrder(string rigKey, string normalizedName, bool isOutfitLayer)
    {
        // 左臂/左手整条肢体在上衣躯干之上，领子之下。
        // 这里不区分裸模/衣装：裸手、袖子、袖口都不能被上衣胸腹片盖住。范围 144..162。
        return 144 + GetArmPartDrawOffset(rigKey, normalizedName, isOutfitLayer);
    }

    private int GetRightLegDrawOrder(string rigKey, string normalizedName, bool isOutfitLayer)
    {
        // 角色右脚/右腿在下身后。
        return 20 + GetLegSegmentDrawOffset(rigKey, normalizedName) + GetOutfitGroupOffset(isOutfitLayer);
    }

    private int GetLeftLegDrawOrder(string rigKey, string normalizedName, bool isOutfitLayer)
    {
        // 角色自身左腿是前侧腿。
        // 之前只保证它压过下身(38/44)，但仍低于上身/上衣(64/70)，
        // 所以“左大腿根”会被上身重新盖住。这里把整条角色左腿组抬到身体前侧。
        return 90 + GetLegSegmentDrawOffset(rigKey, normalizedName) + GetOutfitGroupOffset(isOutfitLayer);
    }

    private int GetLowerBodyDrawOrder(SkyPrisonAnimationRigRow row, string rigKey, string normalizedName, bool isOutfitLayer, bool isLeftLeg, bool isRightLeg)
    {
        // 视觉前后关系按用户要求收束为：角色左脚/左腿 > 下身 > 角色右脚/右腿。
        // 代码是从后往前画，所以实际数值：右脚组 < 下身组 < 左脚组。
        int segment = GetLegSegmentDrawOffset(rigKey, normalizedName);

        if (isRightLeg || IsLikelyRightSide(normalizedName, (rigKey ?? string.Empty).ToLowerInvariant()))
            return GetRightLegDrawOrder(rigKey, normalizedName, isOutfitLayer);

        if (isLeftLeg || IsLikelyLeftSide(normalizedName, (rigKey ?? string.Empty).ToLowerInvariant()))
            return GetLeftLegDrawOrder(rigKey, normalizedName, isOutfitLayer);

        // 不带左右的裙子/裤子/下装作为中间层；下装衣物压住裸下身，但不压到角色左腿前层。
        return (isOutfitLayer ? 44 : 38) + Mathf.Min(segment, 2);
    }

    private bool IsLeftArmRigKey(string key)
    {
        return IsRigKeyAny(key, "Shoulder_L", "Elbow_L", "Wrist_L", "HandEnd_L");
    }

    private bool IsRightArmRigKey(string key)
    {
        return IsRigKeyAny(key, "Shoulder_R", "Elbow_R", "Wrist_R", "HandEnd_R");
    }

    private bool IsLeftLegRigKey(string key)
    {
        return IsRigKeyAny(key, "Hip_L", "Knee_L", "Ankle_L", "Foot_L");
    }

    private bool IsRightLegRigKey(string key)
    {
        return IsRigKeyAny(key, "Hip_R", "Knee_R", "Ankle_R", "Foot_R");
    }

    private bool IsRigKeyAny(string rigKey, params string[] keys)
    {
        if (string.IsNullOrEmpty(rigKey) || keys == null)
            return false;

        for (int i = 0; i < keys.Length; i++)
        {
            if (string.Equals(rigKey, keys[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool IsLikelyLeftSide(string normalizedName, string keyLower)
    {
        string source = (normalizedName ?? string.Empty) + " " + (keyLower ?? string.Empty);
        string compact = CompactLayerText(source);

        return (keyLower ?? string.Empty).EndsWith("_l", StringComparison.OrdinalIgnoreCase) ||
               ContainsLayerToken(source,
                   "_l", "-l", ".l", "left",
                   "l_", "l-", "l.", "left_", "left-",
                   "左") ||
               compact.Contains("left") ||
               compact.Contains("左") ||
               compact.Contains("lhand") || compact.Contains("larm") || compact.Contains("lwrist") || compact.Contains("lelbow") || compact.Contains("lshoulder") ||
               compact.Contains("handl") || compact.Contains("arml") || compact.Contains("wristl") || compact.Contains("elbowl") || compact.Contains("shoulderl") ||
               compact.Contains("lleg") || compact.Contains("lfoot") || compact.Contains("lshoe") || compact.Contains("lsock") || compact.Contains("lhip") || compact.Contains("lknee") || compact.Contains("lankle") ||
               compact.Contains("legl") || compact.Contains("footl") || compact.Contains("shoel") || compact.Contains("sockl") || compact.Contains("hipl") || compact.Contains("kneel") || compact.Contains("anklel");
    }

    private bool IsLikelyRightSide(string normalizedName, string keyLower)
    {
        string source = (normalizedName ?? string.Empty) + " " + (keyLower ?? string.Empty);
        string compact = CompactLayerText(source);

        return (keyLower ?? string.Empty).EndsWith("_r", StringComparison.OrdinalIgnoreCase) ||
               ContainsLayerToken(source,
                   "_r", "-r", ".r", "right",
                   "r_", "r-", "r.", "right_", "right-",
                   "右") ||
               compact.Contains("right") ||
               compact.Contains("右") ||
               compact.Contains("rhand") || compact.Contains("rarm") || compact.Contains("rwrist") || compact.Contains("relbow") || compact.Contains("rshoulder") ||
               compact.Contains("handr") || compact.Contains("armr") || compact.Contains("wristr") || compact.Contains("elbowr") || compact.Contains("shoulderr") ||
               compact.Contains("rleg") || compact.Contains("rfoot") || compact.Contains("rshoe") || compact.Contains("rsock") || compact.Contains("rhip") || compact.Contains("rknee") || compact.Contains("rankle") ||
               compact.Contains("legr") || compact.Contains("footr") || compact.Contains("shoer") || compact.Contains("sockr") || compact.Contains("hipr") || compact.Contains("kneer") || compact.Contains("ankler");
    }

    private bool IsPsbOutfitLayer(SkyPrisonAnimationRigRow row, string normalizedCombinedName)
    {
        if (row == null)
            return false;

        if (row.fromAppearanceSlot)
            return true;

        if (!string.IsNullOrEmpty(row.appearanceSlotKey) || !string.IsNullOrEmpty(row.appearanceLayerKey))
            return true;

        if (!string.IsNullOrEmpty(row.boundEquipmentKey) || !string.IsNullOrEmpty(row.equipmentSourceKey))
            return true;

        if (!string.IsNullOrEmpty(row.slotKey) && !string.Equals(row.slotKey, "Body", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(row.visualSlotKey) && !string.Equals(row.visualSlotKey, "Body", StringComparison.OrdinalIgnoreCase))
            return true;

        // 兜底：旧缓存行可能没有 fromAppearanceSlot，但名字/路径里已经能看出是服装层。
        return ContainsLayerToken(
            normalizedCombinedName,
            "cloth", "clothes", "costume", "outfit", "dress", "jacket", "coat", "shirt",
            "pants", "skirt", "sleeve", "glove", "shoe", "sock", "wear", "armor",
            "head_accessory", "hair_accessory", "accessory", "weapon") ||
            normalizedCombinedName.Contains("衣") ||
            normalizedCombinedName.Contains("服") ||
            normalizedCombinedName.Contains("装") ||
            normalizedCombinedName.Contains("裝") ||
            normalizedCombinedName.Contains("裤") ||
            normalizedCombinedName.Contains("褲") ||
            normalizedCombinedName.Contains("裙") ||
            normalizedCombinedName.Contains("袖") ||
            normalizedCombinedName.Contains("手套") ||
            normalizedCombinedName.Contains("鞋") ||
            normalizedCombinedName.Contains("袜") ||
            normalizedCombinedName.Contains("飾") ||
            normalizedCombinedName.Contains("饰") ||
            normalizedCombinedName.Contains("武器");
    }

    private string CompactLayerText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        string lower = text.ToLowerInvariant();
        System.Text.StringBuilder sb = new System.Text.StringBuilder(lower.Length);
        for (int i = 0; i < lower.Length; i++)
        {
            char c = lower[i];
            if (c == '_' || c == '-' || c == ' ' || c == '/' || c == '\\' || c == '.' || c == '(' || c == ')' || c == '[' || c == ']')
                continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private bool ContainsLayerToken(string text, params string[] tokens)
    {
        if (string.IsNullOrEmpty(text) || tokens == null)
            return false;

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (string.IsNullOrEmpty(token))
                continue;

            if (text.Contains(token))
                return true;
        }

        return false;
    }

    private string BuildBoundRigHierarchyText(string boundRigKey)
    {
        if (string.IsNullOrEmpty(boundRigKey) || state == null)
            return string.Empty;

        SkyPrisonAnimationRigRow rig = state.FindRigRow(boundRigKey);
        if (rig == null)
            return string.Empty;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
        HashSet<string> visited = new HashSet<string>();

        for (int guard = 0; rig != null && guard < 12; guard++)
        {
            if (!string.IsNullOrEmpty(rig.key))
            {
                if (visited.Contains(rig.key))
                    break;
                visited.Add(rig.key);
            }

            sb.Append(' ');
            sb.Append(rig.key ?? string.Empty);
            sb.Append(' ');
            sb.Append(rig.name ?? string.Empty);
            sb.Append(' ');
            sb.Append(rig.semantic ?? string.Empty);
            sb.Append(' ');
            sb.Append(rig.sourceLayerPath ?? string.Empty);

            if (string.IsNullOrEmpty(rig.parentKey))
                break;

            rig = state.FindRigRow(rig.parentKey);
        }

        return sb.ToString();
    }



    private bool ShouldSuppressMeshDeformerPreviewEffects()
    {
        // Rig 编辑模式是 Setup / Rest Pose，不是动作预览。
        // 进入编辑模式后，骨架不会播放动作；曲面变形也必须同样回到未变形显示，
        // 否则用户会误以为基础绑定/PSB 图层本身已经被拉歪。
        return state != null && state.ShowRigEdit;
    }

    private SkyPrisonAnimationRigRow FindMeshDeformerForPsbRow(SkyPrisonAnimationRigRow psb)
    {
        if (psb == null || state == null)
            return null;

        if (!string.IsNullOrEmpty(psb.boundRigKey))
        {
            SkyPrisonAnimationRigRow byBoundRig = FindFirstMeshDeformerForTarget(psb.boundRigKey);
            if (byBoundRig != null)
                return byBoundRig;
        }

        if (!string.IsNullOrEmpty(psb.key))
            return FindFirstMeshDeformerForTarget(psb.key);

        return null;
    }

    private bool DrawSpriteWithMeshDeformer(Sprite sprite, PsbSpriteDrawState drawState, SkyPrisonAnimationRigRow deformer, float alpha, string blendMode, bool hasMask, Rect maskRect, Shader layerEffectShader, SkyPrisonAnimationRigRow sourceRow, bool hasViewportMask, ModelViewportMaskSpriteCommand viewportMaskCommand)
    {
        if (sprite == null || drawState == null || deformer == null || !deformer.isMeshDeformer)
            return false;

        int columns = Mathf.Clamp(deformer.meshDeformColumns, 2, 16);
        int rows = Mathf.Clamp(deformer.meshDeformRows, 2, 16);
        if (columns < 2 || rows < 2 || drawState.size.x <= 1f || drawState.size.y <= 1f)
            return false;

        EnsureMeshDeformerPreviewPointGrid(deformer, columns, rows);
        Vector2[,] points = BuildMeshDeformerPreviewPointsForDrawState(deformer, drawState, columns, rows);

        Texture2D texture = sprite.texture;
        if (texture == null)
            return false;

        Rect tr = sprite.textureRect;
        Rect uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);
        Color color = GetBlendPreviewColor(blendMode, alpha);

        // 这里正式改成“三角网格贴图”。
        // 之前那种把每个小块当旋转矩形贴片绘制的做法，本质还是条带/矩形近似，
        // 一旦图层细长、旋转较大或者格子被拉成梯形，就会出现你图里那种青色条带、块状错位。
        // 改成每个 cell = 两个纹理三角形之后，PSB 图层会按真正的三角网格进行仿射插值。
        // 正常模式必须保证 1.00 = 原图 1.00。
        // 之前为了压 RT 偏亮把这里乘了 0.5，会导致所有曲面图层看起来像正片叠底/加深，
        // 也会误导用户判断“正常 / 正片叠底 / 滤色”等合成方式。
        float textureBrightnessUi = Mathf.Clamp(deformer.meshDeformTextureBrightness <= 0f ? 1f : deformer.meshDeformTextureBrightness, 0.20f, 2.00f);

        // 新模型视口 RT 管线里，UI 亮度 1.0 必须对应原图 1.0。
        // 旧版这里乘 0.5 是为了压制临时 RT 贴回 IMGUI 时的过曝，
        // 但现在曲面图层已经直接进入统一模型视口，继续乘 0.5 会导致图层明显发灰发暗。
        float textureBrightness = textureBrightnessUi * MeshDeformViewportBrightnessMultiplier;
        DrawMeshDeformedTextureGUI(texture, uv, points, columns, rows, color, textureBrightness, blendMode, hasMask, maskRect, layerEffectShader, sourceRow, hasViewportMask, viewportMaskCommand);
        return true;
    }

    private Vector2[,] BuildMeshDeformerPreviewPointsForDrawState(SkyPrisonAnimationRigRow deformer, PsbSpriteDrawState drawState, int columns, int rows)
    {
        Vector2[,] points = new Vector2[columns, rows];
        if (deformer == null || drawState == null)
            return points;

        MeshDeformerScreenFrame frame = BuildMeshDeformerScreenFrame(drawState);
        if (!frame.valid)
            return points;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                SkyPrisonMeshDeformPoint p = ShouldSuppressMeshDeformerPreviewEffects() ? null : FindMeshDeformerPointForPreview(deformer, x, y, columns, rows);
                Vector2 offset = p != null ? p.offset : Vector2.zero;
                Vector2 basePoint = GetBaseMeshPointScreen(frame, columns, rows, x, y);
                points[x, y] = ApplyMeshLocalOffsetToScreen(frame, basePoint, offset);
            }
        }

        return points;
    }

    private static Material GetMeshDeformTextureMaterial()
    {
        if (MeshDeformTextureMaterial != null)
            return MeshDeformTextureMaterial;

        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Hidden/Internal-GUITexture");

        if (shader == null)
            return null;

        MeshDeformTextureMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return MeshDeformTextureMaterial;
    }

    private void DrawMeshDeformedTextureGUI(Texture2D texture, Rect uv, Vector2[,] points, int columns, int rows, Color color, float textureBrightness, string blendMode, bool hasMask, Rect maskRect, Shader layerEffectShader, SkyPrisonAnimationRigRow sourceRow, bool hasViewportMask, ModelViewportMaskSpriteCommand viewportMaskCommand)
    {
        if (texture == null || points == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        // 新视口管线：曲面变形不再先做“局部临时 RT → 再贴回 IMGUI”。
        // 那条旧路径虽然能显示，但它重新建立了一次局部 bounds 坐标，后续和模型视口 RT 混用时
        // 很容易把曲面层拖回旧的裁切/坐标问题里。
        // 普通曲面层直接把变形后的网格三角形提交到整张模型视口 RT，和普通 Sprite Mesh 共用同一个出口。
        if (modelViewportCollecting)
        {
            Color viewportMeshColor = new Color(textureBrightness, textureBrightness, textureBrightness, Mathf.Clamp01(color.a));
            EnqueueModelViewportDeformedMesh(texture, uv, points, columns, rows, viewportMeshColor, hasViewportMask, viewportMaskCommand, blendMode, layerEffectShader, sourceRow);
            return;
        }

        Material mat = GetMeshDeformTextureMaterial();
        if (mat == null)
            return;

        // 关键修正：不要再直接把三角形画到 EditorWindow / Screen 坐标。
        // 红框、绿点、原 PSB 图层都在 Preview 局部 GUI 坐标中，GL 直接上屏很容易因为
        // GUI.BeginGroup、EditorWindow 标题栏、高 DPI、工作台缩放而产生偏差。
        // 这里先把三角网格画进一个临时 RenderTexture，再用 GUI.DrawTexture 按 Preview 局部坐标贴回去。
        // 这样最终落点完全跟随红框/绿点所在的同一套工作台缩放和平移坐标。
        Rect bounds = GetMeshDeformerPointBounds(points, columns, rows, 2f);
        if (bounds.width < 1f || bounds.height < 1f)
            return;

        if (hasMask && maskRect.width > 0.5f && maskRect.height > 0.5f && !bounds.Overlaps(maskRect))
            return;

        float pixelsPerPoint = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
        int rtWidth = Mathf.Clamp(Mathf.CeilToInt(bounds.width * pixelsPerPoint), 8, 2048);
        int rtHeight = Mathf.Clamp(Mathf.CeilToInt(bounds.height * pixelsPerPoint), 8, 2048);

        // 曲面贴图要先画到临时 RT 再贴回 GUI。
        // 这里不能使用 sRGB RT + GL.sRGBWrite=true。GUI.DrawTexture 再贴回编辑器时还会走一次 GUI 色彩处理，
        // 结果就是曲面节点比原 PSB 图层明显发白。RT 内部统一保持 Linear 写入，最后交给 GUI 做一次显示转换。
        RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear;
        RenderTexture rt = RenderTexture.GetTemporary(rtWidth, rtHeight, 0, RenderTextureFormat.ARGB32, readWrite);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;

        RenderTexture oldActive = RenderTexture.active;
        bool oldSrgbWrite = GL.sRGBWrite;
        RenderTexture.active = rt;
        GL.sRGBWrite = false;
        GL.PushMatrix();
        GL.Clear(true, true, Color.clear);

        mat.mainTexture = texture;
        mat.SetPass(0);
        GL.LoadPixelMatrix(0f, rtWidth, rtHeight, 0f);
        GL.Begin(GL.TRIANGLES);
        Color meshColor = new Color(
            Mathf.Clamp01(color.r * textureBrightness),
            Mathf.Clamp01(color.g * textureBrightness),
            Mathf.Clamp01(color.b * textureBrightness),
            color.a);
        GL.Color(meshColor);

        float sx = rtWidth / Mathf.Max(0.0001f, bounds.width);
        float sy = rtHeight / Mathf.Max(0.0001f, bounds.height);

        for (int y = 0; y < rows - 1; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                Vector2 p00 = MeshPointToRenderTexturePoint(points[x, y], bounds, sx, sy);
                Vector2 p10 = MeshPointToRenderTexturePoint(points[x + 1, y], bounds, sx, sy);
                Vector2 p11 = MeshPointToRenderTexturePoint(points[x + 1, y + 1], bounds, sx, sy);
                Vector2 p01 = MeshPointToRenderTexturePoint(points[x, y + 1], bounds, sx, sy);

                Vector2 uv00 = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 0f, 0f);
                Vector2 uv10 = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 1f, 0f);
                Vector2 uv11 = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 1f, 1f);
                Vector2 uv01 = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 0f, 1f);

                EmitTexturedGuiTriangle(p00, uv00, p10, uv10, p11, uv11);
                EmitTexturedGuiTriangle(p00, uv00, p11, uv11, p01, uv01);
            }
        }

        GL.End();
        GL.PopMatrix();
        GL.sRGBWrite = oldSrgbWrite;
        RenderTexture.active = oldActive;

        Color oldGuiColor = GUI.color;
        GUI.color = Color.white;

        if (hasMask && maskRect.width > 0.5f && maskRect.height > 0.5f)
        {
            Rect oldPreviewClip = currentPreviewClipRect;
            Vector2 oldBlendGroupOffset = previewBlendGroupOffset;
            GUI.BeginGroup(maskRect);
            currentPreviewClipRect = new Rect(0f, 0f, maskRect.width, maskRect.height);
            previewBlendGroupOffset = oldBlendGroupOffset + maskRect.position;
            Rect localDrawRect = new Rect(
                bounds.x - maskRect.x,
                bounds.y - maskRect.y,
                bounds.width,
                bounds.height);
            DrawTextureWithPreviewBlend(localDrawRect, rt, new Rect(0f, 0f, 1f, 1f), Color.white, blendMode, layerEffectShader, sourceRow);
            previewBlendGroupOffset = oldBlendGroupOffset;
            currentPreviewClipRect = oldPreviewClip;
            GUI.EndGroup();
        }
        else
        {
            DrawTextureWithPreviewBlend(bounds, rt, new Rect(0f, 0f, 1f, 1f), Color.white, blendMode, layerEffectShader, sourceRow);
        }

        GUI.color = oldGuiColor;
        RenderTexture.ReleaseTemporary(rt);
    }

    private static Vector2 MeshPointToRenderTexturePoint(Vector2 p, Rect bounds, float sx, float sy)
    {
        return new Vector2((p.x - bounds.xMin) * sx, (p.y - bounds.yMin) * sy);
    }

    private static Rect GetMeshDeformerPointBounds(Vector2[,] points, int columns, int rows, float padding)
    {
        if (points == null || columns <= 0 || rows <= 0)
            return new Rect(0f, 0f, 0f, 0f);

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                Vector2 p = points[x, y];
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
        }

        if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
            return new Rect(0f, 0f, 0f, 0f);

        return Rect.MinMaxRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
    }

    private void DrawAffineTexturePatch(Texture2D texture, Vector2 p00, Vector2 p10, Vector2 p01, Vector2 uv00, Vector2 uv10, Vector2 uv01)
    {
        Vector2 right = p10 - p00;
        Vector2 down = p01 - p00;
        float width = right.magnitude;
        float height = down.magnitude;
        if (width < 0.1f || height < 0.1f)
            return;

        // 不使用自定义 shear GUI.matrix。Unity IMGUI 对任意仿射矩阵 + DrawTextureWithTexCoords 的裁切/批处理不稳定，
        // 会导致曲面贴图直接不显示。这里用 IMGUI 原生的旋转矩形贴片：
        // 1) 顶点仍然使用预览区局部坐标，所以会跟随 PreviewPan / PreviewZoom / BeginGroup；
        // 2) 每个小贴片按 right 方向旋转绘制，先保证绑定 PSB 图层真实可见并跟随控制网格；
        // 3) shear/Bezier 高精度采样后续再换成运行时 MeshRenderer 或专用预览 RT。
        float angle = Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
        Rect patchRect = new Rect(p00.x, p00.y, width, height);
        Rect patchUv = new Rect(
            uv00.x,
            uv00.y,
            uv10.x - uv00.x,
            uv01.y - uv00.y);

        Matrix4x4 oldMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, p00);
        GUI.DrawTextureWithTexCoords(patchRect, texture, patchUv, false);
        GUI.matrix = oldMatrix;
    }

    private Vector2 EvaluateMeshDeformerUv(Rect uv, int columns, int rows, int cellX, int cellY, float u, float v)
    {
        float tx = ((float)cellX + u) / Mathf.Max(1, columns - 1);
        float ty = ((float)cellY + v) / Mathf.Max(1, rows - 1);

        if (visualMirrorEnabled)
            tx = 1f - tx;

        // Unity 的 Sprite.textureRect / GL.TexCoord2 使用的是纹理 UV 坐标，Y 轴和 IMGUI 预览坐标相反。
        // 曲面控制点的 y=0 是预览框顶部；如果直接 uv.yMin -> uv.yMax，
        // 生成曲面后 PSB 贴图会变成上下颠倒。
        // 所以这里让预览顶部采样 uv.yMax，预览底部采样 uv.yMin。
        float flippedTy = 1f - ty;

        return new Vector2(
            Mathf.Lerp(uv.xMin, uv.xMax, tx),
            Mathf.Lerp(uv.yMin, uv.yMax, flippedTy));
    }

    private Vector2 EvaluateMeshDeformerCellPoint(Vector2[,] points, int cellX, int cellY, float u, float v)
    {
        Vector2 p00 = points[cellX, cellY];
        Vector2 p10 = points[cellX + 1, cellY];
        Vector2 p01 = points[cellX, cellY + 1];
        Vector2 p11 = points[cellX + 1, cellY + 1];

        // 第一版运行时变形先采用双线性网格面。控制器仍保留 Bezier 方向柄；
        // 方向柄会影响控制线显示和下一步高精度曲面，主控制点已经能真实拉动 PSB 图层。
        Vector2 top = Vector2.Lerp(p00, p10, u);
        Vector2 bottom = Vector2.Lerp(p01, p11, u);
        return Vector2.Lerp(top, bottom, v);
    }

    private static void EmitTexturedGuiTriangle(Vector2 p0, Vector2 uv0, Vector2 p1, Vector2 uv1, Vector2 p2, Vector2 uv2)
    {
        GL.TexCoord2(uv0.x, uv0.y);
        GL.Vertex3(p0.x, p0.y, 0f);
        GL.TexCoord2(uv1.x, uv1.y);
        GL.Vertex3(p1.x, p1.y, 0f);
        GL.TexCoord2(uv2.x, uv2.y);
        GL.Vertex3(p2.x, p2.y, 0f);
    }

    private static Rect TriangleBounds(Vector2 a, Vector2 b, Vector2 c)
    {
        float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private bool DrawSpriteWithInheritedMaskDeformer(Sprite sprite, PsbSpriteDrawState drawState, float alpha, string blendMode, bool hasMask, Rect maskRect, Shader layerEffectShader, SkyPrisonAnimationRigRow sourceRow, ModelViewportMaskSpriteCommand viewportMaskCommand)
    {
        if (sprite == null || sprite.texture == null || drawState == null || !viewportMaskCommand.inheritDeformer || viewportMaskCommand.deformerPoints == null)
            return false;

        int columns = Mathf.Clamp(viewportMaskCommand.deformerColumns, 2, 16);
        int rows = Mathf.Clamp(viewportMaskCommand.deformerRows, 2, 16);
        if (columns < 2 || rows < 2)
            return false;

        Texture2D texture = sprite.texture;
        Rect tr = sprite.textureRect;
        Rect uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);

        Vector2[,] points = new Vector2[columns, rows];
        Vector2 targetCenter = VisualPoint(drawState.center);
        float targetAngle = (visualMirrorEnabled ? -drawState.angle : drawState.angle) * Mathf.Deg2Rad;
        Vector2 targetRight = new Vector2(Mathf.Cos(targetAngle), Mathf.Sin(targetAngle));
        Vector2 targetDown = new Vector2(-Mathf.Sin(targetAngle), Mathf.Cos(targetAngle));
        float targetW = Mathf.Max(1f, drawState.size.x);
        float targetH = Mathf.Max(1f, drawState.size.y);

        for (int y = 0; y < rows; y++)
        {
            float ty = rows <= 1 ? 0f : (float)y / (rows - 1);
            float localY = Mathf.Lerp(-targetH * 0.5f, targetH * 0.5f, ty);
            for (int x = 0; x < columns; x++)
            {
                float tx = columns <= 1 ? 0f : (float)x / (columns - 1);
                float localX = Mathf.Lerp(-targetW * 0.5f, targetW * 0.5f, tx);
                Vector2 basePoint = targetCenter + targetRight * localX + targetDown * localY;
                points[x, y] = ApplyMaskInheritedDeformer(basePoint, viewportMaskCommand);
            }
        }

        Color color = GetBlendPreviewColor(blendMode, alpha);
        float textureBrightness = MeshDeformViewportBrightnessMultiplier;
        DrawMeshDeformedTextureGUI(texture, uv, points, columns, rows, color, textureBrightness, blendMode, hasMask, maskRect, layerEffectShader, sourceRow, true, viewportMaskCommand);
        return true;
    }

    private Vector2 ApplyMaskInheritedDeformer(Vector2 point, ModelViewportMaskSpriteCommand maskCommand)
    {
        if (!maskCommand.inheritDeformer || maskCommand.deformerPoints == null || maskCommand.deformerColumns < 2 || maskCommand.deformerRows < 2)
            return point;

        float w = Mathf.Max(1f, maskCommand.deformerBaseSize.x);
        float h = Mathf.Max(1f, maskCommand.deformerBaseSize.y);
        float angle = maskCommand.deformerBaseAngle * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 down = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
        Vector2 d = point - maskCommand.deformerBaseCenter;

        float u = Vector2.Dot(d, right) / w + 0.5f;
        float v = Vector2.Dot(d, down) / h + 0.5f;

        // 瞳孔这类子图层通常在眼白内部；为了避免边缘采样越界，先 clamp 到参照曲面区域。
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);

        return EvaluateMaskDeformerPoint(maskCommand.deformerPoints, maskCommand.deformerColumns, maskCommand.deformerRows, u, v);
    }

    private Vector2 EvaluateMaskDeformerPoint(Vector2[,] points, int columns, int rows, float u, float v)
    {
        if (points == null || columns < 2 || rows < 2)
            return Vector2.zero;

        float gx = Mathf.Clamp01(u) * (columns - 1);
        float gy = Mathf.Clamp01(v) * (rows - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, columns - 2);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, rows - 2);
        float tx = gx - x0;
        float ty = gy - y0;

        Vector2 a = Vector2.Lerp(points[x0, y0], points[x0 + 1, y0], tx);
        Vector2 b = Vector2.Lerp(points[x0, y0 + 1], points[x0 + 1, y0 + 1], tx);
        return Vector2.Lerp(a, b, ty);
    }

    private void DrawSpriteWithLayout(Sprite sprite, Vector2 center, Vector2 size, float angle, float alpha, string blendMode, bool hasMask, Rect maskRect, Shader layerEffectShader, SkyPrisonAnimationRigRow sourceRow, bool hasViewportMask, ModelViewportMaskSpriteCommand viewportMaskCommand)
    {
        Texture2D texture = sprite.texture;
        Rect tr = sprite.textureRect;
        Rect drawRect = GetSpriteDrawRect(center, size);
        Rect uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);

        Color oldColor = GUI.color;
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.color = Color.white;
        Color layerColor = GetBlendPreviewColor(blendMode, alpha);

        // 真视口路径：普通 PSB Sprite 先进入整张模型视口 RT。
        // 不在单图层局部 Rect 内裁切，也不使用矩形 Quad；最终绘制使用 sprite.vertices / triangles。
        if (modelViewportCollecting)
        {
            EnqueueModelViewportSprite(sprite, center, size, angle, layerColor, visualMirrorEnabled, hasViewportMask, viewportMaskCommand, blendMode, layerEffectShader, sourceRow);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            return;
        }

        if (hasMask && maskRect.width > 0.5f && maskRect.height > 0.5f)
        {
            Rect clippedMask = maskRect;
            Rect oldPreviewClip = currentPreviewClipRect;
            Vector2 oldBlendGroupOffset = previewBlendGroupOffset;
            GUI.BeginGroup(clippedMask);
            currentPreviewClipRect = new Rect(0f, 0f, clippedMask.width, clippedMask.height);
            previewBlendGroupOffset = oldBlendGroupOffset + clippedMask.position;
            Vector2 localCenter = VisualPoint(center) - new Vector2(clippedMask.x, clippedMask.y);
            Rect localRect = new Rect(drawRect.x - clippedMask.x, drawRect.y - clippedMask.y, drawRect.width, drawRect.height);
            if (visualMirrorEnabled)
                GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), localCenter);
            GUIUtility.RotateAroundPivot(visualMirrorEnabled ? -angle : angle, localCenter);
            DrawTextureWithPreviewBlend(localRect, texture, uv, layerColor, blendMode, layerEffectShader, sourceRow);
            GUI.matrix = oldMatrix;
            previewBlendGroupOffset = oldBlendGroupOffset;
            currentPreviewClipRect = oldPreviewClip;
            GUI.EndGroup();
        }
        else
        {
            Vector2 visualCenter = VisualPoint(center);
            // 只翻转这个 Sprite 的视觉结果；不翻转整个 GUI 上下文，避免文字/骨架/后续控件被镜像污染。
            if (visualMirrorEnabled)
                GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), visualCenter);
            GUIUtility.RotateAroundPivot(visualMirrorEnabled ? -angle : angle, visualCenter);
            DrawTextureWithPreviewBlend(drawRect, texture, uv, layerColor, blendMode, layerEffectShader, sourceRow);
            GUI.matrix = oldMatrix;
        }

        GUI.color = oldColor;
    }

    private void BeginModelViewportSpriteCollection()
    {
        modelViewportSpriteCommands.Clear();
        modelViewportDrawCommands.Clear();
        modelViewportCollecting = true;
    }

    private ModelViewportMaskSpriteCommand BuildModelViewportMaskSpriteCommand(Sprite sprite, Vector2 center, Vector2 size, float angle, Color color, bool mirrored)
    {
        return new ModelViewportMaskSpriteCommand
        {
            sprite = sprite,
            center = VisualPoint(center),
            size = size,
            angle = mirrored ? -angle : angle,
            color = color,
            mirrored = mirrored
        };
    }

    private bool IsModelViewportMaskCommandValid(ModelViewportMaskSpriteCommand mask)
    {
        if (mask.useMeshMask && mask.texture != null && mask.vertices != null && mask.uvs != null && mask.indices != null && mask.indices.Length > 0)
            return true;
        return mask.sprite != null;
    }

    private void BuildModelViewportMeshArraysFromGrid(
        Rect uv,
        Vector2[,] points,
        int columns,
        int rows,
        out Vector2[] vertices,
        out Vector2[] uvs,
        out int[] indices)
    {
        vertices = BuildModelViewportMeshVerticesFromGrid(points, columns, rows);
        uvs = GetCachedModelViewportMeshUvs(uv, columns, rows);
        indices = GetCachedModelViewportMeshIndices(columns, rows);
    }

    private Vector2[] BuildModelViewportMeshVerticesFromGrid(Vector2[,] points, int columns, int rows)
    {
        int cellCount = Mathf.Max(0, (columns - 1) * (rows - 1));
        Vector2[] vertices = new Vector2[cellCount * 4];
        int write = 0;

        for (int y = 0; y < rows - 1; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                vertices[write++] = points[x, y];
                vertices[write++] = points[x + 1, y];
                vertices[write++] = points[x + 1, y + 1];
                vertices[write++] = points[x, y + 1];
            }
        }

        return vertices;
    }

    private int[] GetCachedModelViewportMeshIndices(int columns, int rows)
    {
        string key = columns + "x" + rows;
        if (modelViewportMeshIndexCache.TryGetValue(key, out int[] cached) && cached != null)
            return cached;

        int cellCount = Mathf.Max(0, (columns - 1) * (rows - 1));
        int[] indices = new int[cellCount * 6];
        int write = 0;
        int vertex = 0;

        for (int i = 0; i < cellCount; i++)
        {
            indices[write++] = vertex + 0;
            indices[write++] = vertex + 1;
            indices[write++] = vertex + 2;
            indices[write++] = vertex + 0;
            indices[write++] = vertex + 2;
            indices[write++] = vertex + 3;
            vertex += 4;
        }

        modelViewportMeshIndexCache[key] = indices;
        return indices;
    }

    private Vector2[] GetCachedModelViewportMeshUvs(Rect uv, int columns, int rows)
    {
        string key = MakeModelViewportMeshUvCacheKey(uv, columns, rows);
        if (modelViewportMeshUvCache.TryGetValue(key, out Vector2[] cached) && cached != null)
            return cached;

        int cellCount = Mathf.Max(0, (columns - 1) * (rows - 1));
        Vector2[] uvs = new Vector2[cellCount * 4];
        int write = 0;

        for (int y = 0; y < rows - 1; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                uvs[write++] = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 0f, 0f);
                uvs[write++] = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 1f, 0f);
                uvs[write++] = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 1f, 1f);
                uvs[write++] = EvaluateMeshDeformerUv(uv, columns, rows, x, y, 0f, 1f);
            }
        }

        // 防止长期打开多个 PSB 包时缓存无限长。UV 只和 Sprite.textureRect 有关，64 组已经足够覆盖常见角色包。
        if (modelViewportMeshUvCache.Count > 64)
            modelViewportMeshUvCache.Clear();

        modelViewportMeshUvCache[key] = uvs;
        return uvs;
    }

    private static string MakeModelViewportMeshUvCacheKey(Rect uv, int columns, int rows)
    {
        const float scale = 1000000f;
        int x = Mathf.RoundToInt(uv.x * scale);
        int y = Mathf.RoundToInt(uv.y * scale);
        int w = Mathf.RoundToInt(uv.width * scale);
        int h = Mathf.RoundToInt(uv.height * scale);
        return columns + "x" + rows + ":" + x + ":" + y + ":" + w + ":" + h;
    }

    private void EnqueueModelViewportSprite(Sprite sprite, Vector2 center, Vector2 size, float angle, Color color, bool mirrored, bool hasMask, ModelViewportMaskSpriteCommand maskCommand, string blendMode, Shader layerEffectShader, SkyPrisonAnimationRigRow sourceRow)
    {
        if (sprite == null || sprite.texture == null)
            return;

        ModelViewportSpriteCommand command = new ModelViewportSpriteCommand
        {
            sprite = sprite,
            center = VisualPoint(center),
            size = size,
            angle = mirrored ? -angle : angle,
            color = color,
            mirrored = mirrored,
            hasMask = hasMask && IsModelViewportMaskCommandValid(maskCommand),
            mask = maskCommand,
            blendMode = string.IsNullOrWhiteSpace(blendMode) ? "正常" : blendMode,
            layerEffectShader = layerEffectShader,
            sourceRow = sourceRow
        };
        modelViewportSpriteCommands.Add(command);
        modelViewportDrawCommands.Add(new ModelViewportDrawCommand
        {
            isMesh = false,
            sprite = command
        });
    }

    private void EnqueueModelViewportDeformedMesh(Texture texture, Rect uv, Vector2[,] points, int columns, int rows, Color color, bool hasMask, ModelViewportMaskSpriteCommand maskCommand, string blendMode, Shader layerEffectShader, SkyPrisonAnimationRigRow sourceRow)
    {
        if (texture == null || points == null || columns < 2 || rows < 2)
            return;

        Vector2[] vertices = BuildModelViewportMeshVerticesFromGrid(points, columns, rows);
        int[] indices = GetCachedModelViewportMeshIndices(columns, rows);
        if (vertices == null || vertices.Length == 0 || indices == null || indices.Length == 0)
            return;

        ModelViewportMeshCommand command = new ModelViewportMeshCommand
        {
            texture = texture,
            vertices = vertices,
            uvs = GetCachedModelViewportMeshUvs(uv, columns, rows),
            indices = indices,
            color = color,
            hasMask = hasMask && IsModelViewportMaskCommandValid(maskCommand),
            mask = maskCommand,
            blendMode = string.IsNullOrWhiteSpace(blendMode) ? "正常" : blendMode,
            layerEffectShader = layerEffectShader,
            sourceRow = sourceRow
        };

        modelViewportDrawCommands.Add(new ModelViewportDrawCommand
        {
            isMesh = true,
            mesh = command
        });
    }

    private void EndModelViewportSpriteCollectionAndDraw(Rect localView)
    {
        modelViewportCollecting = false;

        if (Event.current == null || Event.current.type != EventType.Repaint)
        {
            modelViewportSpriteCommands.Clear();
            modelViewportDrawCommands.Clear();
            return;
        }

        if (modelViewportDrawCommands.Count == 0)
        {
            modelViewportSpriteCommands.Clear();
            return;
        }

        int width = Mathf.Max(1, Mathf.CeilToInt(localView.width));
        int height = Mathf.Max(1, Mathf.CeilToInt(localView.height));

        // 注意：不要在缩放 / 平移过程中释放并重建模型视口 RT。
        // 旧版为了处理残影，在 PreviewZoom / PreviewPan 变化时 RecreateModelViewportRTIfViewTransformChanged()，
        // 但 Shader 图层会在缩放拖动时频繁重建 RT，导致卡顿，并且某些图层效果在重建帧里只显示一部分。
        // 现在每帧都会强制 Clear modelViewportRT / layerRT / nextRT，残影不再需要靠重建 RT 解决。
        // Recreate 只留给尺寸变化，由 EnsureModelViewportRT / EnsureModelViewportMaskRT 处理。

        EnsureModelViewportRT(width, height);
        EnsureModelViewportMaskRT(width, height);
        currentModelViewportWidth = width;
        currentModelViewportHeight = height;

        RenderTexture oldActive = RenderTexture.active;
        bool oldSrgbWrite = GL.sRGBWrite;
        RenderTexture.active = modelViewportRT;
        // 真正的修正不是调亮/调暗，而是让 RT 写入和项目色彩空间一致。
        // Linear 项目写入 sRGB RT 时需要打开 sRGBWrite，否则中间调会发灰发暗；
        // 但材质不能再用 Internal-GUITexture，否则高光会被二次 GUI 化而发白。
        GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
        ClearRenderTexture(modelViewportRT, Color.clear);

        Material material = GetModelViewportSpriteMaterial();
        if (material != null)
        {
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, height, 0f);

            EnsureModelViewportBlendRTs(width, height);

            for (int i = 0; i < modelViewportDrawCommands.Count; i++)
            {
                DrawModelViewportCommandWithBlend(modelViewportDrawCommands[i], material, modelViewportLayerRT, modelViewportNextRT, width, height);
            }

            GL.PopMatrix();
        }

        GL.sRGBWrite = oldSrgbWrite;
        RenderTexture.active = oldActive;

        Color oldColor = GUI.color;
        Matrix4x4 oldGuiMatrix = GUI.matrix;
        GUI.color = new Color(ModelViewportOutputBrightness, ModelViewportOutputBrightness, ModelViewportOutputBrightness, 1f);

        // 真正的镜像：整张模型视口 RT 作为一个整体翻转。
        // 这样所有图层共享同一个镜像轴，不会再出现“头发按头发中心翻、眼睛按眼睛中心翻”的局部翻转错位。
        if (state != null && state.PreviewMirrored)
            GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), visualMirrorPivot);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), modelViewportRT, ScaleMode.StretchToFill, true);
        GUI.matrix = oldGuiMatrix;
        GUI.color = oldColor;

        modelViewportSpriteCommands.Clear();
        modelViewportDrawCommands.Clear();
    }


    private void DrawModelViewportCommandRaw(ModelViewportDrawCommand command, Material material)
    {
        if (command.isMesh)
        {
            if (command.mesh.hasMask)
                DrawModelViewportMaskedMeshCommand(command.mesh, material);
            else
                DrawModelViewportMeshCommand(command.mesh, material);
        }
        else
        {
            if (command.sprite.hasMask)
                DrawModelViewportMaskedSpriteCommand(command.sprite, material);
            else
                DrawModelViewportSpriteCommand(command.sprite, material);
        }
    }

    private string GetModelViewportCommandBlendMode(ModelViewportDrawCommand command)
    {
        string blend = command.isMesh ? command.mesh.blendMode : command.sprite.blendMode;
        return string.IsNullOrWhiteSpace(blend) ? "正常" : blend;
    }

    private Shader GetModelViewportCommandLayerShader(ModelViewportDrawCommand command)
    {
        return command.isMesh ? command.mesh.layerEffectShader : command.sprite.layerEffectShader;
    }

    private SkyPrisonAnimationRigRow GetModelViewportCommandSourceRow(ModelViewportDrawCommand command)
    {
        return command.isMesh ? command.mesh.sourceRow : command.sprite.sourceRow;
    }

    private Rect GetModelViewportCommandBounds(ModelViewportDrawCommand command)
    {
        return command.isMesh ? GetModelViewportMeshCommandBounds(command.mesh) : GetModelViewportSpriteCommandBounds(command.sprite);
    }

    private void DrawModelViewportCommandWithBlend(ModelViewportDrawCommand command, Material baseMaterial, RenderTexture layerRT, RenderTexture nextRT, int width, int height)
    {
        string blendMode = GetModelViewportCommandBlendMode(command);
        Shader layerEffectShader = GetModelViewportCommandLayerShader(command);

        bool needsLayerPass = layerEffectShader != null || GetPreviewBlendModeId(blendMode) != 0;
        if (!needsLayerPass)
        {
            RenderTexture.active = modelViewportRT;
            GL.LoadPixelMatrix(0f, width, height, 0f);
            DrawModelViewportCommandRaw(command, baseMaterial);
            return;
        }

        RenderTexture oldLayer = previewBlendLayerRT;
        RenderTexture oldNext = previewBlendNextRT;

        ClearRenderTexture(layerRT, Color.clear);
        ClearRenderTexture(nextRT, Color.clear);
        RenderTexture.active = layerRT;
        GL.LoadPixelMatrix(0f, width, height, 0f);
        DrawModelViewportCommandRaw(command, baseMaterial);

        RenderTexture originalAlphaRT = null;
        if (layerEffectShader != null)
        {
            originalAlphaRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Default);
            originalAlphaRT.filterMode = FilterMode.Bilinear;
            originalAlphaRT.wrapMode = TextureWrapMode.Clamp;
            ClearRenderTexture(originalAlphaRT, Color.clear);
            Graphics.Blit(layerRT, originalAlphaRT);
        }

        previewBlendLayerRT = layerRT;
        previewBlendNextRT = nextRT;
        ApplyPreviewLayerEffectToLayerRT(layerEffectShader, GetModelViewportCommandBounds(command), GetModelViewportCommandSourceRow(command));
        ApplyPreviewLayerAlphaMaskToLayerRT(originalAlphaRT);
        if (originalAlphaRT != null)
            RenderTexture.ReleaseTemporary(originalAlphaRT);
        layerRT = previewBlendLayerRT;
        nextRT = previewBlendNextRT;

        Material compositor = GetPreviewCompositorMaterial();
        if (compositor != null)
        {
            compositor.SetTexture("_BaseTex", modelViewportRT);
            compositor.SetTexture("_LayerTex", layerRT);
            compositor.SetInt("_Mode", GetPreviewBlendModeId(blendMode));
            ClearRenderTexture(nextRT, Color.clear);
            Graphics.Blit(null, nextRT, compositor, 0);
            ClearRenderTexture(modelViewportRT, Color.clear);
            Graphics.Blit(nextRT, modelViewportRT);
        }
        else
        {
            RenderTexture.active = modelViewportRT;
            GL.LoadPixelMatrix(0f, width, height, 0f);
            DrawModelViewportCommandRaw(command, baseMaterial);
        }

        previewBlendLayerRT = oldLayer;
        previewBlendNextRT = oldNext;
        RenderTexture.active = modelViewportRT;
    }

    private Rect GetModelViewportSpriteCommandBounds(ModelViewportSpriteCommand command)
    {
        Vector2[] vertices = command.sprite != null ? command.sprite.vertices : null;
        Bounds bounds = command.sprite != null ? command.sprite.bounds : new Bounds(Vector3.zero, Vector3.zero);
        if (vertices == null || vertices.Length == 0 || bounds.size.x <= 0.00001f || bounds.size.y <= 0.00001f)
            return new Rect(command.center.x, command.center.y, 0f, 0f);

        float rad = command.angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        Vector2 min = bounds.min;
        Vector2 size = bounds.size;
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 v = vertices[i];
            float nx = ((v.x - min.x) / size.x) - 0.5f;
            float ny = ((v.y - min.y) / size.y) - 0.5f;
            float localX = nx * command.size.x;
            float localY = -ny * command.size.y;
            if (command.mirrored)
                localX = -localX;
            float rx = localX * cos - localY * sin;
            float ry = localX * sin + localY * cos;
            Vector2 p = command.center + new Vector2(rx, ry);
            minX = Mathf.Min(minX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxX = Mathf.Max(maxX, p.x);
            maxY = Mathf.Max(maxY, p.y);
        }

        if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
            return new Rect(command.center.x, command.center.y, 0f, 0f);

        return Rect.MinMaxRect(minX - 2f, minY - 2f, maxX + 2f, maxY + 2f);
    }

    private Rect GetModelViewportMeshCommandBounds(ModelViewportMeshCommand command)
    {
        if (command.vertices == null || command.vertices.Length == 0)
            return Rect.zero;

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        for (int i = 0; i < command.vertices.Length; i++)
        {
            Vector2 p = command.vertices[i];
            minX = Mathf.Min(minX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxX = Mathf.Max(maxX, p.x);
            maxY = Mathf.Max(maxY, p.y);
        }

        if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
            return Rect.zero;

        return Rect.MinMaxRect(minX - 2f, minY - 2f, maxX + 2f, maxY + 2f);
    }


    private void RecreateModelViewportRTIfViewTransformChanged()
    {
        if (state == null)
            return;

        bool zoomChanged = !Mathf.Approximately(lastModelViewportRtZoom, state.PreviewZoom);
        bool panChanged = !IsFinite(lastModelViewportRtPan) || (lastModelViewportRtPan - state.PreviewPan).sqrMagnitude > 0.0001f;
        bool mirrorChanged = lastModelViewportRtMirrored != state.PreviewMirrored;

        if (zoomChanged || panChanged || mirrorChanged)
        {
            ReleaseModelViewportRT();
            ReleaseModelViewportMaskRT();
            lastModelViewportRtZoom = state.PreviewZoom;
            lastModelViewportRtPan = state.PreviewPan;
            lastModelViewportRtMirrored = state.PreviewMirrored;
        }
    }

    private void ReleaseModelViewportRT()
    {
        if (modelViewportRT == null)
            return;

        modelViewportRT.Release();
        UnityEngine.Object.DestroyImmediate(modelViewportRT);
        modelViewportRT = null;
    }

    private void ReleaseModelViewportMaskRT()
    {
        if (modelViewportMaskRT == null)
            return;

        modelViewportMaskRT.Release();
        UnityEngine.Object.DestroyImmediate(modelViewportMaskRT);
        modelViewportMaskRT = null;
    }

    private void ReleaseModelViewportBlendRTs()
    {
        if (modelViewportLayerRT != null)
        {
            modelViewportLayerRT.Release();
            UnityEngine.Object.DestroyImmediate(modelViewportLayerRT);
            modelViewportLayerRT = null;
        }

        if (modelViewportNextRT != null)
        {
            modelViewportNextRT.Release();
            UnityEngine.Object.DestroyImmediate(modelViewportNextRT);
            modelViewportNextRT = null;
        }
    }

    private void ClearRenderTexture(RenderTexture rt, Color clearColor)
    {
        if (rt == null)
            return;

        RenderTexture old = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, clearColor);
        RenderTexture.active = old;
    }

    private void EnsureModelViewportRT(int width, int height)
    {
        if (modelViewportRT != null && modelViewportRT.width == width && modelViewportRT.height == height)
            return;

        ReleaseModelViewportRT();

        RenderTextureReadWrite viewportReadWrite = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? RenderTextureReadWrite.sRGB
            : RenderTextureReadWrite.Default;

        modelViewportRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, viewportReadWrite)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        modelViewportRT.Create();
    }

    private void EnsureModelViewportMaskRT(int width, int height)
    {
        if (modelViewportMaskRT != null && modelViewportMaskRT.width == width && modelViewportMaskRT.height == height)
            return;

        ReleaseModelViewportMaskRT();

        RenderTextureReadWrite viewportReadWrite = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? RenderTextureReadWrite.sRGB
            : RenderTextureReadWrite.Default;

        modelViewportMaskRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, viewportReadWrite)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        modelViewportMaskRT.Create();
    }

    private void EnsureModelViewportBlendRTs(int width, int height)
    {
        if (modelViewportLayerRT != null && modelViewportNextRT != null &&
            modelViewportLayerRT.width == width && modelViewportLayerRT.height == height &&
            modelViewportNextRT.width == width && modelViewportNextRT.height == height)
            return;

        ReleaseModelViewportBlendRTs();

        RenderTextureReadWrite viewportReadWrite = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? RenderTextureReadWrite.sRGB
            : RenderTextureReadWrite.Default;

        modelViewportLayerRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, viewportReadWrite)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        modelViewportLayerRT.Create();

        modelViewportNextRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, viewportReadWrite)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        modelViewportNextRT.Create();
    }

    private static Material GetModelViewportSpriteMaterial()
    {
        if (ModelViewportSpriteMaterial != null)
            return ModelViewportSpriteMaterial;

        // 模型视口专用材质：优先使用 Premultiplied Alpha 预览 Shader。
        // 目的不是调亮度，而是修透明边缘：
        // 贴图边缘的半透明像素如果按普通 SrcAlpha 混合，透明区残色会被放大成黑/灰边。
        // Premultiply 后用 Blend One OneMinusSrcAlpha，可以让头发、手臂、腿部边缘更接近原始 PSB 观感。
        Shader shader = Shader.Find("Hidden/SkyPrison/EditorSpritePremultiply");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Hidden/Internal-GUITexture");

        if (shader == null)
            return null;

        ModelViewportSpriteMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        if (ModelViewportSpriteMaterial.HasProperty("_Color"))
            ModelViewportSpriteMaterial.SetColor("_Color", Color.white);

        return ModelViewportSpriteMaterial;
    }

    private static Material GetModelViewportMaskedSpriteMaterial()
    {
        if (ModelViewportMaskedSpriteMaterial != null)
            return ModelViewportMaskedSpriteMaterial;

        Shader shader = Shader.Find("Hidden/SkyPrison/EditorSpritePremultiplyMasked");
        if (shader == null)
            shader = Shader.Find("Hidden/SkyPrison/EditorSpritePremultiply");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");

        if (shader == null)
            return null;

        ModelViewportMaskedSpriteMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        return ModelViewportMaskedSpriteMaterial;
    }

    private void DrawModelViewportMaskedSpriteCommand(ModelViewportSpriteCommand command, Material baseMaterial)
    {
        if (modelViewportMaskRT == null || !IsModelViewportMaskCommandValid(command.mask))
        {
            DrawModelViewportSpriteCommand(command, baseMaterial);
            return;
        }

        RenderTexture oldActive = RenderTexture.active;
        RenderTexture.active = modelViewportMaskRT;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        DrawModelViewportMaskSpriteCommand(command.mask, baseMaterial);
        RenderTexture.active = oldActive;

        Material maskedMaterial = GetModelViewportMaskedSpriteMaterial();
        if (maskedMaterial == null)
        {
            DrawModelViewportSpriteCommand(command, baseMaterial);
            return;
        }

        maskedMaterial.SetTexture("_MaskTex", modelViewportMaskRT);
        DrawModelViewportSpriteCommand(command, maskedMaterial);
    }

    private void DrawModelViewportMaskedMeshCommand(ModelViewportMeshCommand command, Material baseMaterial)
    {
        if (modelViewportMaskRT == null || !IsModelViewportMaskCommandValid(command.mask))
        {
            DrawModelViewportMeshCommand(command, baseMaterial);
            return;
        }

        RenderTexture oldActive = RenderTexture.active;
        RenderTexture.active = modelViewportMaskRT;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        DrawModelViewportMaskSpriteCommand(command.mask, baseMaterial);
        RenderTexture.active = oldActive;

        Material maskedMaterial = GetModelViewportMaskedSpriteMaterial();
        if (maskedMaterial == null)
        {
            DrawModelViewportMeshCommand(command, baseMaterial);
            return;
        }

        maskedMaterial.SetTexture("_MaskTex", modelViewportMaskRT);
        DrawModelViewportMeshCommand(command, maskedMaterial);
    }

    private void DrawModelViewportMaskSpriteCommand(ModelViewportMaskSpriteCommand mask, Material material)
    {
        if (mask.useMeshMask && mask.texture != null && mask.vertices != null && mask.uvs != null && mask.indices != null && mask.indices.Length > 0)
        {
            ModelViewportMeshCommand meshCommand = new ModelViewportMeshCommand
            {
                texture = mask.texture,
                vertices = mask.vertices,
                uvs = mask.uvs,
                indices = mask.indices,
                color = Color.white,
                hasMask = false
            };
            DrawModelViewportMeshCommand(meshCommand, material);
            return;
        }

        if (mask.sprite == null || mask.sprite.texture == null)
            return;

        ModelViewportSpriteCommand command = new ModelViewportSpriteCommand
        {
            sprite = mask.sprite,
            center = mask.center,
            size = mask.size,
            angle = mask.angle,
            color = Color.white,
            mirrored = mask.mirrored,
            hasMask = false
        };

        DrawModelViewportSpriteCommand(command, material);
    }

    private void DrawModelViewportSpriteCommand(ModelViewportSpriteCommand command, Material material)
    {
        Sprite sprite = command.sprite;
        if (sprite == null || sprite.texture == null)
            return;

        Vector2[] vertices = sprite.vertices;
        ushort[] triangles = sprite.triangles;
        Vector2[] uvs = sprite.uv;
        Bounds bounds = sprite.bounds;

        if (vertices == null || triangles == null || uvs == null ||
            vertices.Length == 0 || triangles.Length < 3 || uvs.Length != vertices.Length ||
            bounds.size.x <= 0.00001f || bounds.size.y <= 0.00001f)
            return;

        material.mainTexture = sprite.texture;
        material.SetTexture("_MainTex", sprite.texture);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", command.color);

        material.SetPass(0);

        float rad = command.angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        Vector2 min = bounds.min;
        Vector2 size = bounds.size;

        float invViewportWidth = 1f / Mathf.Max(1f, currentModelViewportWidth);
        float invViewportHeight = 1f / Mathf.Max(1f, currentModelViewportHeight);

        GL.Begin(GL.TRIANGLES);
        GL.Color(command.color);

        for (int i = 0; i < triangles.Length; i++)
        {
            int index = triangles[i];
            if (index < 0 || index >= vertices.Length)
                continue;

            Vector2 v = vertices[index];
            float nx = ((v.x - min.x) / size.x) - 0.5f;
            float ny = ((v.y - min.y) / size.y) - 0.5f;

            float localX = nx * command.size.x;
            float localY = -ny * command.size.y;

            if (command.mirrored)
                localX = -localX;

            float rx = localX * cos - localY * sin;
            float ry = localX * sin + localY * cos;
            Vector2 p = command.center + new Vector2(rx, ry);

            Vector2 uv = uvs[index];
            GL.TexCoord2(uv.x, uv.y);
            GL.MultiTexCoord2(1, p.x * invViewportWidth, 1f - (p.y * invViewportHeight));
            GL.Vertex3(p.x, p.y, 0f);
        }

        GL.End();
    }


    private void DrawModelViewportMeshCommand(ModelViewportMeshCommand command, Material material)
    {
        if (command.texture == null || command.vertices == null || command.uvs == null || command.indices == null)
            return;

        if (command.vertices.Length == 0 || command.uvs.Length != command.vertices.Length || command.indices.Length < 3)
            return;

        material.mainTexture = command.texture;
        material.SetTexture("_MainTex", command.texture);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", command.color);

        material.SetPass(0);

        float invViewportWidth = 1f / Mathf.Max(1f, currentModelViewportWidth);
        float invViewportHeight = 1f / Mathf.Max(1f, currentModelViewportHeight);

        GL.Begin(GL.TRIANGLES);
        GL.Color(command.color);

        for (int i = 0; i < command.indices.Length; i++)
        {
            int index = command.indices[i];
            if (index < 0 || index >= command.vertices.Length)
                continue;

            Vector2 uv = command.uvs[index];
            Vector2 p = command.vertices[index];
            GL.TexCoord2(uv.x, uv.y);
            GL.MultiTexCoord2(1, p.x * invViewportWidth, 1f - (p.y * invViewportHeight));
            GL.Vertex3(p.x, p.y, 0f);
        }

        GL.End();
    }

    private Rect GetSpriteDrawRect(Vector2 center, Vector2 size)
    {
        float w = Mathf.Max(1f, size.x);
        float h = Mathf.Max(1f, size.y);
        Vector2 visualCenter = VisualPoint(center);
        return new Rect(visualCenter.x - w * 0.5f, visualCenter.y - h * 0.5f, w, h);
    }

    private bool HasAnyPreviewLayerThatNeedsCompositor(List<SkyPrisonAnimationRigRow> rows)
    {
        if (rows == null || rows.Count == 0)
            return false;

        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null)
                continue;

            // 只有非“正常”的合成方式才需要真正的 AccumRT + LayerRT 合成。
            // 注意：图层 Shader 效果器不再强制启用整张工作台 RT 合成器。
            // Shader 效果默认走当前 PSB 图层自己的 IMGUI 绘制空间，避免预览缩放时产生坐标偏移。
            string blendMode = GetEffectiveBlendMode(row);
            if (!string.IsNullOrWhiteSpace(blendMode) && blendMode != "正常")
                return true;
        }

        return false;
    }

    private string GetEffectiveBlendMode(SkyPrisonAnimationRigRow row)
    {
        if (row == null) return "正常";
        if (!string.IsNullOrEmpty(row.blendMode)) return row.blendMode;
        if (!string.IsNullOrEmpty(row.boundRigKey))
        {
            SkyPrisonAnimationRigRow rig = state.FindRigRow(row.boundRigKey);
            if (rig != null && !string.IsNullOrEmpty(rig.blendMode)) return rig.blendMode;
        }
        return "正常";
    }

    private Color GetBlendPreviewColor(string blendMode, float alpha)
    {
        // 正常情况下：这里仅负责图层本身的不透明度，模式区分交给 PreviewBlend Shader。
        // 但如果 Shader 没有被正确放进工程 / 编译失败，会走 DrawTextureWithPreviewBlend 的 fallback；
        // fallback 也必须给出可见差异，避免下拉菜单“看起来完全没反应”。
        return new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
    }

    private static Material GetPreviewBlendMaterial()
    {
        if (PreviewBlendMaterial != null)
            return PreviewBlendMaterial;

        Shader shader = Shader.Find("Hidden/SkyPrison/AnimationPreviewBlendV2");
        if (shader == null)
        {
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shader/Editor/SkyPrisonAnimationPreviewBlendV2.shader");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Editor/SkyPrisonAnimationPreviewBlendV2.shader");
        }

        if (shader == null)
            return null;

        PreviewBlendMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return PreviewBlendMaterial;
    }


    private void BeginPreviewBlendCompositor(Rect localView)
    {
        ReleasePreviewBlendCompositorRTs();

        int width = Mathf.Clamp(Mathf.CeilToInt(localView.width), 8, 4096);
        int height = Mathf.Clamp(Mathf.CeilToInt(localView.height), 8, 4096);
        previewBlendCanvasRect = new Rect(0f, 0f, width, height);
        previewBlendAccumRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        previewBlendLayerRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        previewBlendNextRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        previewBlendAccumRT.filterMode = FilterMode.Bilinear;
        previewBlendLayerRT.filterMode = FilterMode.Bilinear;
        previewBlendNextRT.filterMode = FilterMode.Bilinear;
        previewBlendAccumRT.wrapMode = TextureWrapMode.Clamp;
        previewBlendLayerRT.wrapMode = TextureWrapMode.Clamp;
        previewBlendNextRT.wrapMode = TextureWrapMode.Clamp;

        RenderTexture oldActive = RenderTexture.active;
        bool oldSrgbWrite = GL.sRGBWrite;
        GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
        RenderTexture.active = previewBlendAccumRT;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = previewBlendLayerRT;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = previewBlendNextRT;
        GL.Clear(true, true, Color.clear);
        GL.sRGBWrite = oldSrgbWrite;
        RenderTexture.active = oldActive;

        previewBlendGroupOffset = Vector2.zero;
        previewBlendCompositorActive = true;
    }

    private void EndPreviewBlendCompositor(Rect localView)
    {
        if (!previewBlendCompositorActive || previewBlendAccumRT == null)
        {
            previewBlendCompositorActive = false;
            ReleasePreviewBlendCompositorRTs();
            return;
        }

        bool oldActive = GUI.enabled;
        Color oldColor = GUI.color;
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.matrix = Matrix4x4.identity;
        GUI.DrawTexture(new Rect(0f, 0f, localView.width, localView.height), previewBlendAccumRT, ScaleMode.StretchToFill, true);
        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
        GUI.enabled = oldActive;

        previewBlendCompositorActive = false;
        previewBlendGroupOffset = Vector2.zero;
        ReleasePreviewBlendCompositorRTs();
    }

    private void ReleasePreviewBlendCompositorRTs()
    {
        if (previewBlendAccumRT != null)
        {
            RenderTexture.ReleaseTemporary(previewBlendAccumRT);
            previewBlendAccumRT = null;
        }
        if (previewBlendLayerRT != null)
        {
            RenderTexture.ReleaseTemporary(previewBlendLayerRT);
            previewBlendLayerRT = null;
        }
        if (previewBlendNextRT != null)
        {
            RenderTexture.ReleaseTemporary(previewBlendNextRT);
            previewBlendNextRT = null;
        }
        previewBlendCanvasRect = Rect.zero;
    }

    private static Material GetPreviewLayerCopyMaterial()
    {
        if (PreviewLayerCopyMaterial != null)
            return PreviewLayerCopyMaterial;

        Shader shader = Shader.Find("Hidden/SkyPrison/AnimationPreviewLayerCopy");
        if (shader == null)
        {
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shader/Editor/SkyPrisonAnimationPreviewLayerCopy.shader");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Editor/SkyPrisonAnimationPreviewLayerCopy.shader");
        }

        if (shader == null)
            return null;

        PreviewLayerCopyMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return PreviewLayerCopyMaterial;
    }

    private static Material GetPreviewCompositorMaterial()
    {
        if (PreviewCompositorMaterial != null)
            return PreviewCompositorMaterial;

        Shader shader = Shader.Find("Hidden/SkyPrison/AnimationPreviewCompositor");
        if (shader == null)
        {
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shader/Editor/SkyPrisonAnimationPreviewCompositor.shader");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Editor/SkyPrisonAnimationPreviewCompositor.shader");
        }

        if (shader == null)
            return null;

        PreviewCompositorMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return PreviewCompositorMaterial;
    }

    private static Material GetPreviewLayerEffectMaterial(Shader shader)
    {
        if (shader == null)
            return null;

        Material material;
        if (PreviewLayerEffectMaterials.TryGetValue(shader, out material) && material != null)
            return material;

        material = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        PreviewLayerEffectMaterials[shader] = material;
        return material;
    }

    private void ApplyPreviewLayerEffectToLayerRT(Shader layerEffectShader, Rect layerRtBounds, SkyPrisonAnimationRigRow row)
    {
        if (layerEffectShader == null || previewBlendLayerRT == null || previewBlendNextRT == null)
            return;

        Material effectMaterial = GetPreviewLayerEffectMaterial(layerEffectShader);
        if (effectMaterial == null || effectMaterial.passCount <= 0)
            return;

        float rtW = Mathf.Max(1, previewBlendLayerRT.width);
        float rtH = Mathf.Max(1, previewBlendLayerRT.height);
        Rect safeBounds = NormalizeLayerRtBounds(layerRtBounds, rtW, rtH);

        float t = (float)EditorApplication.timeSinceStartup;
        effectMaterial.SetFloat("_SkyPrisonTime", t);
        effectMaterial.SetFloat("_PreviewTime", t);
        effectMaterial.SetVector("_LayerTexelSize", new Vector4(
            1f / rtW,
            1f / rtH,
            rtW,
            rtH));
        // 图层 Shader 只应该以“当前图层局部范围”为效果坐标，不能用整个工作台 RT 的 UV。
        // 否则在缩放预览时，动态噪点 / Glitch 这种屏幕 UV 偏移会表现成 PSB 部件位置漂移。
        effectMaterial.SetVector("_SkyPrisonLayerRect", new Vector4(
            safeBounds.x / rtW,
            safeBounds.y / rtH,
            safeBounds.width / rtW,
            safeBounds.height / rtH));
        effectMaterial.SetVector("_SkyPrisonLayerRectPixels", new Vector4(
            safeBounds.x,
            safeBounds.y,
            safeBounds.width,
            safeBounds.height));
        ApplyLayerShaderParameterOverrides(effectMaterial, row);

        Graphics.Blit(previewBlendLayerRT, previewBlendNextRT, effectMaterial, 0);

        RenderTexture swap = previewBlendLayerRT;
        previewBlendLayerRT = previewBlendNextRT;
        previewBlendNextRT = swap;
    }

    private static Material GetPreviewLayerAlphaMaskMaterial()
    {
        if (PreviewLayerAlphaMaskMaterial != null)
            return PreviewLayerAlphaMaskMaterial;

        Shader shader = Shader.Find("Hidden/SkyPrison/AnimationPreviewLayerAlphaMask");
        if (shader == null)
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shader/Editor/SkyPrisonAnimationPreviewLayerAlphaMask.shader");
        if (shader == null)
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Editor/SkyPrisonAnimationPreviewLayerAlphaMask.shader");
        if (shader == null)
            return null;

        PreviewLayerAlphaMaskMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return PreviewLayerAlphaMaskMaterial;
    }

    private void ApplyPreviewLayerAlphaMaskToLayerRT(RenderTexture originalAlphaRT)
    {
        if (originalAlphaRT == null || previewBlendLayerRT == null || previewBlendNextRT == null)
            return;

        Material maskMaterial = GetPreviewLayerAlphaMaskMaterial();
        if (maskMaterial == null)
            return;

        maskMaterial.SetTexture("_AlphaTex", originalAlphaRT);
        if (maskMaterial.HasProperty("_AlphaThreshold"))
            maskMaterial.SetFloat("_AlphaThreshold", 0.015f);
        ClearRenderTexture(previewBlendNextRT, Color.clear);
        Graphics.Blit(previewBlendLayerRT, previewBlendNextRT, maskMaterial, 0);

        RenderTexture swap = previewBlendLayerRT;
        previewBlendLayerRT = previewBlendNextRT;
        previewBlendNextRT = swap;
    }


    private static void ApplyLayerShaderParameterOverrides(Material material, SkyPrisonAnimationRigRow row)
    {
        if (material == null || row == null || row.shaderParameters == null)
            return;

        for (int i = 0; i < row.shaderParameters.Count; i++)
        {
            SkyPrisonAnimationShaderPropertyOverride prop = row.shaderParameters[i];
            if (prop == null || string.IsNullOrEmpty(prop.propertyName) || !material.HasProperty(prop.propertyName))
                continue;

            switch (prop.propertyKind)
            {
                case SkyPrisonAnimationShaderPropertyKind.Float:
                case SkyPrisonAnimationShaderPropertyKind.Range:
                    material.SetFloat(prop.propertyName, prop.floatValue);
                    break;
                case SkyPrisonAnimationShaderPropertyKind.Color:
                    material.SetColor(prop.propertyName, prop.colorValue);
                    break;
                case SkyPrisonAnimationShaderPropertyKind.Vector:
                    material.SetVector(prop.propertyName, prop.vectorValue);
                    break;
                case SkyPrisonAnimationShaderPropertyKind.Texture:
                    if (prop.textureValue != null)
                        material.SetTexture(prop.propertyName, prop.textureValue);
                    break;
            }
        }
    }

    private static Rect NormalizeLayerRtBounds(Rect bounds, float rtW, float rtH)
    {
        // Any-scale stable shader coordinate policy.
        //
        // The previous versions tried to derive _SkyPrisonLayerRect from the current on-screen
        // bounds of a PSB layer. That is fragile: when zooming out the bounds becomes too small,
        // and when zooming in only part of the layer may be inside the viewport. Glitch/noise/scanline
        // shaders then receive a changing local coordinate window, so the effect gets cropped, squeezed,
        // or disappears at certain zoom levels.
        //
        // This version deliberately gives layer shaders the full model viewport as their working
        // coordinate domain. The shader result is still clipped back to the original layer alpha in
        // ApplyPreviewLayerAlphaMaskToLayerRT(), so effects will not leak into rectangular blocks.
        //
        // Tradeoff: screen-space style shaders such as glitch/scanline become viewport-stable instead
        // of layer-rect-stable. That is the correct safe default for editor preview because it keeps
        // the effect visible at every zoom level. Later we can add an explicit per-shader coordinate
        // mode for advanced cases: Viewport / LayerBounds / SpriteUV.
        return new Rect(0f, 0f, Mathf.Max(1f, rtW), Mathf.Max(1f, rtH));
    }

    private void CompositeTextureIntoPreviewRT(Rect rect, Texture texture, Rect uv, Color color, string blendMode, Matrix4x4 guiMatrix, Vector2 groupOffset, Shader layerEffectShader, SkyPrisonAnimationRigRow row)
    {
        if (previewBlendAccumRT == null || previewBlendLayerRT == null || previewBlendNextRT == null || texture == null)
            return;

        RenderTexture oldActive = RenderTexture.active;
        bool oldSrgbWrite = GL.sRGBWrite;
        GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;

        RenderTexture.active = previewBlendLayerRT;
        GL.Clear(true, true, Color.clear);
        Rect layerRtBounds = DrawTexturedQuadToActiveRT(texture, rect, uv, color, guiMatrix, groupOffset, previewBlendLayerRT.width, previewBlendLayerRT.height);

        RenderTexture originalAlphaRT = null;
        if (layerEffectShader != null)
        {
            originalAlphaRT = RenderTexture.GetTemporary(previewBlendLayerRT.width, previewBlendLayerRT.height, 0, RenderTextureFormat.ARGB32, QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Default);
            originalAlphaRT.filterMode = FilterMode.Bilinear;
            originalAlphaRT.wrapMode = TextureWrapMode.Clamp;
            ClearRenderTexture(originalAlphaRT, Color.clear);
            Graphics.Blit(previewBlendLayerRT, originalAlphaRT);
        }

        ApplyPreviewLayerEffectToLayerRT(layerEffectShader, layerRtBounds, row);
        ApplyPreviewLayerAlphaMaskToLayerRT(originalAlphaRT);
        if (originalAlphaRT != null)
            RenderTexture.ReleaseTemporary(originalAlphaRT);

        Material mat = GetPreviewCompositorMaterial();
        if (mat == null)
        {
            // 安全降级：没有合成器 Shader 时，至少保持正常 alpha-over，不让预览直接消失。
            Graphics.Blit(previewBlendAccumRT, previewBlendNextRT);
        }
        else
        {
            mat.SetTexture("_BaseTex", previewBlendAccumRT);
            mat.SetTexture("_LayerTex", previewBlendLayerRT);
            mat.SetInt("_Mode", GetPreviewBlendModeId(blendMode));
            Graphics.Blit(null, previewBlendNextRT, mat, 0);
        }

        RenderTexture swap = previewBlendAccumRT;
        previewBlendAccumRT = previewBlendNextRT;
        previewBlendNextRT = swap;

        GL.sRGBWrite = oldSrgbWrite;
        RenderTexture.active = oldActive;
    }

    private Rect DrawTexturedQuadToActiveRT(Texture texture, Rect rect, Rect uv, Color color, Matrix4x4 guiMatrix, Vector2 groupOffset, int rtWidth, int rtHeight)
    {
        Material mat = GetPreviewLayerCopyMaterial();
        if (mat == null)
            return Rect.zero;

        Vector2 p0 = TransformGuiPointForPreviewRT(guiMatrix, new Vector2(rect.xMin, rect.yMin), groupOffset);
        Vector2 p1 = TransformGuiPointForPreviewRT(guiMatrix, new Vector2(rect.xMax, rect.yMin), groupOffset);
        Vector2 p2 = TransformGuiPointForPreviewRT(guiMatrix, new Vector2(rect.xMax, rect.yMax), groupOffset);
        Vector2 p3 = TransformGuiPointForPreviewRT(guiMatrix, new Vector2(rect.xMin, rect.yMax), groupOffset);

        mat.SetTexture("_MainTex", texture);
        mat.SetColor("_Color", color);
        mat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, rtWidth, rtHeight, 0f);
        GL.Begin(GL.TRIANGLES);
        GL.Color(Color.white);

        // 注意：GUI.DrawTextureWithTexCoords 的 uv.yMin / uv.yMax 语义和这里写入 RenderTexture 的 GL 采样方向不同。
        // 之前直接把 GUI 的 UV 塞给 RT，会让 PSB 图层在合成器路径里出现上下颠倒。
        // 坐标位置仍然用 GUI 的 y-down 体系；只翻转贴图采样的 V 方向，避免影响骨骼点、曲面点和图层排序。
        Vector2 uvTopLeft = new Vector2(uv.xMin, uv.yMax);
        Vector2 uvTopRight = new Vector2(uv.xMax, uv.yMax);
        Vector2 uvBottomRight = new Vector2(uv.xMax, uv.yMin);
        Vector2 uvBottomLeft = new Vector2(uv.xMin, uv.yMin);

        EmitTexturedGuiTriangle(p0, uvTopLeft, p1, uvTopRight, p2, uvBottomRight);
        EmitTexturedGuiTriangle(p0, uvTopLeft, p2, uvBottomRight, p3, uvBottomLeft);

        GL.End();
        GL.PopMatrix();

        return GetQuadBounds(p0, p1, p2, p3);
    }

    private static Rect GetQuadBounds(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        float minX = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x));
        float minY = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y));
        float maxX = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x));
        float maxY = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y));
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static Vector2 TransformGuiPointForPreviewRT(Matrix4x4 guiMatrix, Vector2 point, Vector2 groupOffset)
    {
        Vector3 p = guiMatrix.MultiplyPoint3x4(new Vector3(point.x, point.y, 0f));
        return new Vector2(p.x + groupOffset.x, p.y + groupOffset.y);
    }

    private static int GetPreviewBlendModeId(string blendMode)
    {
        switch (blendMode)
        {
            case "变暗": return 1;
            case "正片叠底": return 2;
            case "颜色加深": return 3;
            case "线性加深": return 4;
            case "变亮": return 5;
            case "滤色": return 6;
            case "颜色减淡": return 7;
            case "叠加": return 8;
            case "柔光": return 9;
            case "强光": return 10;
            case "差值": return 11;
            case "排除": return 12;
            case "色相": return 13;
            case "饱和度": return 14;
            case "颜色": return 15;
            case "亮度": return 16;
            default: return 0;
        }
    }

    private void DrawTextureWithPreviewBlend(Rect rect, Texture texture, Rect uv, Color color, string blendMode, Shader layerEffectShader = null, SkyPrisonAnimationRigRow row = null)
    {
        if (texture == null || rect.width <= 0.5f || rect.height <= 0.5f)
            return;

        if (previewBlendCompositorActive && Event.current != null && Event.current.type == EventType.Repaint)
        {
            CompositeTextureIntoPreviewRT(rect, texture, uv, color, blendMode, GUI.matrix, previewBlendGroupOffset, layerEffectShader, row);
            return;
        }

        int pass = GetPreviewBlendPass(blendMode);

        // 正常合成 + 图层 Shader：不要走整张工作台 RT 合成器。
        // 直接在当前 IMGUI 图层绘制空间用 Shader 处理这张 Sprite，位置/缩放/旋转继续沿用原本稳定路径。
        if (pass == 0 && layerEffectShader != null)
        {
            if (DrawTextureWithLayerEffectImmediate(rect, texture, uv, color, layerEffectShader, row))
                return;
        }

        // “正常”必须完全回到 IMGUI 原生路径。
        // 这里不要提前把 rect 裁成工作台大小再重算 UV。
        // Unity 的 GUIClip / BeginGroup 会自然裁掉超出工作台的区域；
        // 如果我们先改 rect/uv，某些 PSD/PSB 图集与曲面 RT 在放大越界时会看起来像被重新压缩进可视区域。
        if (pass == 0)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            GUI.color = old;
            return;
        }

        // 非正常合成需要走 Graphics.DrawTexture + Material。
        // 这条路径不一定稳定尊重 IMGUI 的 GUIClip，所以这里只对 Shader 合成路径做手动裁切，
        // 并同步修正 UV，确保是“裁掉”，不是“缩进”。
        Rect clippedRect = rect;
        Rect clippedUv = uv;
        if (!ClipTextureRectToPreview(ref clippedRect, ref clippedUv))
            return;

        Material mat = GetPreviewBlendMaterial();
        if (mat == null)
        {
            // Shader 未加载时的安全降级：至少让合成方式在编辑器里有肉眼可见的变化。
            Color old = GUI.color;
            GUI.color = GetFallbackPreviewBlendColor(blendMode, color.a);
            GUI.DrawTextureWithTexCoords(clippedRect, texture, clippedUv, true);
            GUI.color = old;
            return;
        }

        mat.SetColor("_Color", color);
        Graphics.DrawTexture(clippedRect, texture, clippedUv, 0, 0, 0, 0, color, mat, Mathf.Clamp(pass, 0, mat.passCount - 1));
    }

    private bool DrawTextureWithLayerEffectImmediate(Rect rect, Texture texture, Rect uv, Color color, Shader layerEffectShader, SkyPrisonAnimationRigRow row)
    {
        if (layerEffectShader == null || texture == null || rect.width <= 0.5f || rect.height <= 0.5f)
            return false;

        Material mat = GetPreviewLayerEffectMaterial(layerEffectShader);
        if (mat == null || mat.passCount <= 0)
            return false;

        Rect drawRect = rect;
        Rect drawUv = uv;
        if (!ClipTextureRectToPreview(ref drawRect, ref drawUv))
            return true;

        float t = (float)EditorApplication.timeSinceStartup;
        float texW = Mathf.Max(1f, texture.width);
        float texH = Mathf.Max(1f, texture.height);

        mat.SetFloat("_SkyPrisonTime", t);
        mat.SetFloat("_PreviewTime", t);
        mat.SetColor("_Color", color);
        mat.SetVector("_LayerTexelSize", new Vector4(
            1f / texW,
            1f / texH,
            texW,
            texH));

        // 直接绘制模式下，Shader 收到的是 Sprite 在原纹理/图集中的 UV 区域。
        // 这样动态噪点 / Glitch 的局部坐标来自当前 PSB 图层自身，而不是整张工作台 RT。
        mat.SetVector("_SkyPrisonLayerRect", new Vector4(
            drawUv.x,
            drawUv.y,
            drawUv.width,
            drawUv.height));
        mat.SetVector("_SkyPrisonLayerRectPixels", new Vector4(
            drawRect.x,
            drawRect.y,
            Mathf.Max(1f, drawRect.width),
            Mathf.Max(1f, drawRect.height)));
        ApplyLayerShaderParameterOverrides(mat, row);

        // Tint / alpha 只通过 Shader 的 _Color 传入。
        // 这里传 Color.white，避免 Graphics.DrawTexture 的 GUI tint 和 _Color 双重相乘，
        // 否则挂效果器的图层会比正常绘制更暗或透明度异常。
        Graphics.DrawTexture(drawRect, texture, drawUv, 0, 0, 0, 0, Color.white, mat, 0);
        return true;
    }

    private bool ClipTextureRectToPreview(ref Rect rect, ref Rect uv)
    {
        Rect clip = currentPreviewClipRect.width > 1f && currentPreviewClipRect.height > 1f
            ? currentPreviewClipRect
            : new Rect(-100000f, -100000f, 200000f, 200000f);

        float xMin = Mathf.Max(rect.xMin, clip.xMin);
        float yMin = Mathf.Max(rect.yMin, clip.yMin);
        float xMax = Mathf.Min(rect.xMax, clip.xMax);
        float yMax = Mathf.Min(rect.yMax, clip.yMax);

        if (xMax <= xMin + 0.01f || yMax <= yMin + 0.01f)
            return false;

        if (Mathf.Abs(rect.width) > 0.0001f && Mathf.Abs(rect.height) > 0.0001f)
        {
            float left01 = (xMin - rect.xMin) / rect.width;
            float right01 = (xMax - rect.xMin) / rect.width;
            float top01 = (yMin - rect.yMin) / rect.height;
            float bottom01 = (yMax - rect.yMin) / rect.height;

            float uvXMin = Mathf.Lerp(uv.xMin, uv.xMax, left01);
            float uvXMax = Mathf.Lerp(uv.xMin, uv.xMax, right01);
            float uvYMin = Mathf.Lerp(uv.yMin, uv.yMax, top01);
            float uvYMax = Mathf.Lerp(uv.yMin, uv.yMax, bottom01);
            uv = Rect.MinMaxRect(uvXMin, uvYMin, uvXMax, uvYMax);
        }

        rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    private static Color GetFallbackPreviewBlendColor(string blendMode, float alpha)
    {
        alpha = Mathf.Clamp01(alpha);

        switch (blendMode)
        {
            case "变暗":
            case "正片叠底":
                return new Color(0.72f, 0.72f, 0.72f, alpha);
            case "颜色加深":
            case "线性加深":
                return new Color(0.56f, 0.56f, 0.56f, alpha);
            case "变亮":
            case "滤色":
                return new Color(1.22f, 1.22f, 1.22f, alpha);
            case "颜色减淡":
                return new Color(1.42f, 1.42f, 1.42f, alpha);
            case "叠加":
            case "柔光":
            case "强光":
                return new Color(1.12f, 1.04f, 0.92f, alpha);
            case "差值":
            case "排除":
                return new Color(0.90f, 0.78f, 1.18f, alpha);
            case "色相":
            case "饱和度":
            case "颜色":
                return new Color(1.10f, 0.92f, 1.10f, alpha);
            case "亮度":
                return new Color(1.18f, 1.18f, 1.18f, alpha);
            case "正常":
            default:
                return new Color(1f, 1f, 1f, alpha);
        }
    }

    private int GetPreviewBlendPass(string blendMode)
    {
        switch (blendMode)
        {
            case "变暗": return 1;
            case "正片叠底": return 2;
            case "颜色加深": return 3;
            case "线性加深": return 4;
            case "变亮": return 5;
            case "滤色": return 6;
            case "颜色减淡": return 7;
            case "叠加": return 8;
            case "柔光": return 9;
            case "强光": return 10;
            case "差值": return 11;
            case "排除": return 12;
            case "色相": return 13;
            case "饱和度": return 14;
            case "颜色": return 15;
            case "亮度": return 16;
            case "正常":
            default: return 0;
        }
    }

    private bool TryGetMaskPreviewRect(
        SkyPrisonAnimationRigRow row,
        Dictionary<SkyPrisonAnimationRigRow, PsbSpriteLayout> rowLayout,
        PsbPrefabLayout layout,
        Vector2 center,
        float fitScale,
        Dictionary<SkyPrisonAnimationRigRow, Rect> finalRects,
        out Rect maskRect)
    {
        maskRect = new Rect();
        if (row == null || string.IsNullOrEmpty(row.maskReferenceKey))
            return false;

        SkyPrisonAnimationRigRow maskRow = state.FindAnyStructureRow(row.maskReferenceKey);
        if (maskRow == null)
            return false;

        // 参照直接来自本次可绘制 PSB 时，使用它已经算好的最终矩形。
        foreach (KeyValuePair<SkyPrisonAnimationRigRow, Rect> kv in finalRects)
        {
            if (kv.Key == null) continue;
            if (kv.Key.key == maskRow.key || kv.Key.boundRigKey == maskRow.key || maskRow.boundRigKey == kv.Key.key)
            {
                maskRect = kv.Value;
                return true;
            }
        }

        // 参照来自 Rig 行时，尝试找到绑定到它的 PSB 行。
        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow psb = state.PsbRows[i];
            if (psb == null || psb.isFolder) continue;
            if (psb.boundRigKey != maskRow.key && psb.key != maskRow.key) continue;
            PsbSpriteLayout item = FindPsbSpriteLayout(layout, psb);
            if (item == null) continue;
            Vector2 c = PsbLocalToPreview(item.localCenter, layout.bounds, center, fitScale);
            maskRect = GetSpriteDrawRect(c, item.localSize * fitScale);
            return true;
        }

        return false;
    }

    private bool TryBuildModelViewportMaskCommand(
        SkyPrisonAnimationRigRow row,
        Dictionary<SkyPrisonAnimationRigRow, PsbSpriteLayout> rowLayout,
        Dictionary<SkyPrisonAnimationRigRow, PsbSpriteDrawState> drawStates,
        out ModelViewportMaskSpriteCommand maskCommand)
    {
        maskCommand = new ModelViewportMaskSpriteCommand();

        if (row == null || string.IsNullOrEmpty(row.maskReferenceKey) || rowLayout == null || drawStates == null)
            return false;

        SkyPrisonAnimationRigRow maskRow = state.FindAnyStructureRow(row.maskReferenceKey);
        if (maskRow == null)
            return false;

        foreach (KeyValuePair<SkyPrisonAnimationRigRow, PsbSpriteLayout> kv in rowLayout)
        {
            SkyPrisonAnimationRigRow candidate = kv.Key;
            PsbSpriteLayout layout = kv.Value;
            if (candidate == null || layout == null || layout.sprite == null)
                continue;

            bool matched = candidate.key == maskRow.key ||
                           candidate.boundRigKey == maskRow.key ||
                           maskRow.boundRigKey == candidate.key;
            if (!matched)
                continue;

            PsbSpriteDrawState stateForMask;
            if (!drawStates.TryGetValue(candidate, out stateForMask))
                continue;

            // 默认：参照图层作为普通 Alpha Mask。
            maskCommand = BuildModelViewportMaskSpriteCommand(
                layout.sprite,
                stateForMask.center,
                stateForMask.size,
                stateForMask.angle,
                Color.white,
                visualMirrorEnabled);

            // 新规则：如果参照蒙版本身挂了 MeshDeformer，maskRT 必须写入“变形后的参照蒙版”。
            // 同时，被参照的图层也可以继承这套曲面场，例如眼白压缩时，眼黑/瞳孔同步压缩。
            SkyPrisonAnimationRigRow maskDeformer = ShouldSuppressMeshDeformerPreviewEffects() ? null : FindMeshDeformerForPsbRow(candidate);
            if (maskDeformer != null && maskDeformer.isMeshDeformer && layout.sprite.texture != null)
            {
                int columns = Mathf.Clamp(maskDeformer.meshDeformColumns, 2, 16);
                int rows = Mathf.Clamp(maskDeformer.meshDeformRows, 2, 16);
                EnsureMeshDeformerPreviewPointGrid(maskDeformer, columns, rows);
                Vector2[,] points = BuildMeshDeformerPreviewPointsForDrawState(maskDeformer, stateForMask, columns, rows);

                Rect tr = layout.sprite.textureRect;
                Texture2D texture = layout.sprite.texture;
                Rect uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);

                Vector2[] vertices;
                Vector2[] uvs;
                int[] indices;
                BuildModelViewportMeshArraysFromGrid(uv, points, columns, rows, out vertices, out uvs, out indices);

                if (vertices != null && vertices.Length > 0 && indices != null && indices.Length > 0)
                {
                    maskCommand.useMeshMask = true;
                    maskCommand.texture = texture;
                    maskCommand.vertices = vertices;
                    maskCommand.uvs = uvs;
                    maskCommand.indices = indices;

                    // MaskOnly：参照图层可以自己曲面变形并写入 maskRT，
                    // 但被遮罩图层不继承参照曲面。
                    // 例如眼白被压扁时，瞳孔自身大小不变，只是被变形后的眼白 Alpha 裁掉。
                    maskCommand.inheritDeformer = false;
                    maskCommand.deformerPoints = null;
                    maskCommand.deformerColumns = 0;
                    maskCommand.deformerRows = 0;
                    maskCommand.deformerBaseCenter = Vector2.zero;
                    maskCommand.deformerBaseSize = Vector2.zero;
                    maskCommand.deformerBaseAngle = 0f;
                }
            }

            return true;
        }

        return false;
    }

    private void DrawActionPreviewPath(string actionKey, Vector2 center, float z)
    {
        // 不再使用 Handles.DrawAAPolyLine。Handles 在 IMGUI 缩放/裁剪下容易越界，
        // 严重时会触发 Repaint 重入卡死。这里改成逐段安全 GUI 线。
        Color color = ActionPathColor(actionKey);
        Vector2? last = null;
        for (int i = 0; i < 72; i++)
        {
            float sampleT = i / 71f;
            float samplePhase = sampleT * Mathf.PI * 2f;
            HumanPose p = EvaluateHumanPose(actionKey, center, z, sampleT, samplePhase);
            Vector2 current = VisualPoint(p.core);

            if (last.HasValue)
            {
                Vector2 a = last.Value;
                Vector2 b = current;
                Rect clip = currentPreviewClipRect.width > 1f && currentPreviewClipRect.height > 1f
                    ? currentPreviewClipRect
                    : new Rect(0f, 0f, 100000f, 100000f);
                if (ClipLineToRect(ref a, ref b, ExpandRect(clip, 1f)))
                    DrawSafeRotatedRectLine(a, b, 2f, color);
            }

            last = current;
        }
    }

    private Color ActionPathColor(string actionKey)
    {
        if (actionKey == "Hit" || actionKey == "Hurt") return new Color(1f, 0.35f, 0.25f, 0.85f);
        if (actionKey == "Attack_01") return new Color(1f, 0.78f, 0.20f, 0.85f);
        if (actionKey == "Jump") return new Color(0.35f, 0.80f, 1f, 0.85f);
        if (actionKey == "Death") return new Color(0.85f, 0.45f, 1f, 0.85f);
        return new Color(0.2f, 0.9f, 1f, 0.80f);
    }

    private void HandlePreviewInput(Rect view)
    {
        if (!state.CanEditCurrentActionTimeline()) return;
        Event e = Event.current;
        if (e == null || !view.Contains(e.mousePosition))
            return;

        if (GetRigEditButtonRect(view).Contains(e.mousePosition))
            return;

        // 底部预览开关栏、右下角缩放栏属于 UI 控件层。
        // 不能让 PSB 图层拾取/取消选择逻辑先吃掉它们的 MouseDown，
        // 否则“部位”等开关会看起来点不下去。
        if (GetPreviewToggleBarRect(view).Contains(e.mousePosition) ||
            GetPreviewZoomPanelRect(view).Contains(e.mousePosition))
            return;

        // 左键 PSB 图层拾取不在这里做。
        // 这里执行时 PSB 显示矩形还是上一帧缓存；缩放 / 平移后会导致命中区域比例跟不上画面。
        // 实际拾取放到 DrawBoundPsbSprites 刷新本帧矩形之后的 HandleCurrentFramePsbLayerSelection。

        if (!state.ShowRigEdit && state.CanEditCurrentActionTimeline() && state.IsMotionTimelineTrack(state.ActiveTimelineTrackKey))
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                state.PushStructureUndo();
                draggingMotionVisualOffset = true;
                draggingMotionStartMouse = e.mousePosition;
                draggingMotionStartOffset = state.EvaluateMotionVisualOffset();
                state.SelectTimelineTrack(SkyPrisonAnimationWorkbenchState.MotionTimelineTrackKey, false);
                state.PreviewPlaying = false;
                state.PreviewPanelHasKeyboardFocus = true;
                GUI.FocusControl(null);
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && draggingMotionVisualOffset)
            {
                Vector2 delta = e.mousePosition - draggingMotionStartMouse;
                if (state.PreviewMirrored)
                    delta.x = -delta.x;
                Vector2 offset = draggingMotionStartOffset + delta / Mathf.Max(0.0001f, state.PreviewZoom);
                state.InsertOrUpdateMotionKeyframe(state.TimelineCurrentFrame, offset);
                GUI.changed = true;
                e.Use();
                return;
            }
            if (e.type == EventType.MouseUp && draggingMotionVisualOffset)
            {
                draggingMotionVisualOffset = false;
                e.Use();
                return;
            }
        }

        if (e.type == EventType.MouseDrag && e.button == 2)
        {
            state.PreviewPan += e.delta;
            e.Use();
        }
        else if (e.type == EventType.ScrollWheel)
        {
            float oldZoom = state.PreviewZoom;
            float factor = e.delta.y > 0f ? 0.92f : 1.08f;
            state.PreviewZoom = Mathf.Clamp(state.PreviewZoom * factor, 0.1f, 5f);

            // 以鼠标所在位置为缩放中心，避免滚轮缩放时画面跳开。
            Vector2 localMouse = e.mousePosition - view.position;
            Vector2 canvasOrigin = GetPreviewCanvasOrigin(new Rect(0f, 0f, view.width, view.height));
            Vector2 before = (localMouse - canvasOrigin - state.PreviewPan) / Mathf.Max(0.0001f, oldZoom);
            Vector2 after = before * state.PreviewZoom;
            state.PreviewPan = localMouse - canvasOrigin - after;

            e.Use();
        }
    }

    private Rect GetPreviewToggleBarRect(Rect view)
    {
        const float margin = 8f;
        const float height = 24f;
        float width = Mathf.Min(620f, Mathf.Max(260f, view.width - margin * 2f));
        Rect bar = new Rect(view.x + margin, view.yMax - height - margin, width, height);

        if (bar.y < view.y + margin)
            bar.y = view.y + margin;

        return bar;
    }

    private void DrawPreviewToggles(Rect view)
    {
        // 这排按钮属于预览画布自身，必须锚定在 view 内部底边。
        // 不使用 GUILayout，避免被外部布局/滚动区域影响后又“浮”到画布中间。
        Rect bar = GetPreviewToggleBarRect(view);

        EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.24f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(bar, new Color(1f, 1f, 1f, 0.08f));

        const float gap = 2f;
        float buttonWidth = Mathf.Clamp((bar.width - gap * 6f) / 7f, 48f, 82f);
        Rect r0 = new Rect(bar.x, bar.y, buttonWidth, bar.height);
        Rect r1 = new Rect(r0.xMax + gap, bar.y, buttonWidth, bar.height);
        Rect r2 = new Rect(r1.xMax + gap, bar.y, buttonWidth, bar.height);
        Rect r3 = new Rect(r2.xMax + gap, bar.y, buttonWidth, bar.height);
        Rect r4 = new Rect(r3.xMax + gap, bar.y, buttonWidth, bar.height);
        Rect r5 = new Rect(r4.xMax + gap, bar.y, buttonWidth, bar.height);
        Rect r6 = new Rect(r5.xMax + gap, bar.y, buttonWidth, bar.height);

        EditorGUI.BeginChangeCheck();
        state.ShowVisualParts = GUI.Toggle(r0, state.ShowVisualParts, "部位", EditorStyles.toolbarButton);
        state.ShowRigLines = GUI.Toggle(r1, state.ShowRigLines, "骨架线", EditorStyles.toolbarButton);
        state.ShowFormulaPath = GUI.Toggle(r2, state.ShowFormulaPath, "轨迹", EditorStyles.toolbarButton);
        state.ShowHitbox = GUI.Toggle(r3, state.ShowHitbox, "判定框", EditorStyles.toolbarButton);
        state.ShowCenterOfGravityLine = GUI.Toggle(r4, state.ShowCenterOfGravityLine, "重心线", EditorStyles.toolbarButton);
        state.ShowOnionSkinPrevious = GUI.Toggle(r5, state.ShowOnionSkinPrevious, "上一关键帧", EditorStyles.toolbarButton);
        state.ShowPhysicsPreview = GUI.Toggle(r6, state.ShowPhysicsPreview, "物理", EditorStyles.toolbarButton);
        state.ShowPhysicsOscillatorDebug = false;
        if (EditorGUI.EndChangeCheck())
        {
            GUI.FocusControl(null);
            GUI.changed = true;
        }

    }

    private void DrawPhysicsOscillatorDebugOverlay(Rect localView)
    {
        if (physicsOscillatorDebugEntries == null || physicsOscillatorDebugEntries.Count == 0)
            return;

        string selectedPsb = state.LastSelectedPsbLayerKey ?? string.Empty;
        string selectedRig = state.LastSelectedRigKey ?? string.Empty;
        bool hasSelection = !string.IsNullOrEmpty(selectedPsb) || !string.IsNullOrEmpty(selectedRig);

        bool drewFocused = false;
        for (int i = 0; i < physicsOscillatorDebugEntries.Count; i++)
        {
            PhysicsOscillatorDebugDrawEntry entry = physicsOscillatorDebugEntries[i];
            if (entry == null || entry.preset == null)
                continue;

            bool focused = (!string.IsNullOrEmpty(selectedPsb) && entry.rowKey == selectedPsb) ||
                           (!string.IsNullOrEmpty(selectedRig) && entry.sourceKey == selectedRig);
            if (hasSelection && !focused)
                continue;

            DrawSinglePhysicsOscillatorDebug(entry, focused);
            drewFocused = true;
        }

        if (hasSelection && drewFocused)
            return;

        if (!hasSelection)
        {
            for (int i = 0; i < physicsOscillatorDebugEntries.Count; i++)
                DrawSinglePhysicsOscillatorDebug(physicsOscillatorDebugEntries[i], false);
        }
    }

    private void DrawSinglePhysicsOscillatorDebug(PhysicsOscillatorDebugDrawEntry entry, bool focused)
    {
        if (entry == null || entry.preset == null || entry.preset.oscillators == null)
            return;

        Vector2 dir = entry.direction.sqrMagnitude > 0.0001f ? entry.direction.normalized : Vector2.down;
        Vector2 p = entry.root;
        Color line = focused ? new Color(0.05f, 0.85f, 1f, 0.95f) : new Color(0.05f, 0.72f, 1f, 0.55f);
        Color point = focused ? new Color(1f, 0.95f, 0.25f, 1f) : new Color(0.60f, 0.90f, 1f, 0.80f);

        DrawPhysicsDebugPoint(p, 4.5f, point);

        float debugScale = Mathf.Max(4.5f, 8f * Mathf.Clamp(state.PreviewZoom, 0.35f, 2.5f));
        float accumulatedAngle = entry.physicsAngle;
        int count = Mathf.Min(entry.preset.oscillators.Count, Mathf.Max(1, entry.preset.oscillatorCount));
        for (int i = 0; i < count; i++)
        {
            SkyPrisonPhysicsOscillator osc = entry.preset.oscillators[i];
            if (osc == null)
                continue;

            float section = (i + 1f) / Mathf.Max(1f, count);
            accumulatedAngle += entry.physicsAngle * osc.swayEase * 0.18f;
            Vector2 sectionDir = RotateVector(dir, accumulatedAngle);
            float len = Mathf.Max(6f, osc.length * Mathf.Max(0.2f, entry.preset.globalScale) * debugScale);
            Vector2 lateral = entry.perpendicular * entry.offsetAmount * section * 0.35f;
            Vector2 next = p + sectionDir.normalized * len + lateral;

            Vector2 a = VisualPoint(p);
            Vector2 b = VisualPoint(next);
            Rect clip = currentPreviewClipRect.width > 1f && currentPreviewClipRect.height > 1f
                ? currentPreviewClipRect
                : new Rect(0f, 0f, 100000f, 100000f);
            if (ClipLineToRect(ref a, ref b, ExpandRect(clip, 1f)))
                DrawSafeRotatedRectLine(a, b, focused ? 2.2f : 1.6f, line);

            DrawPhysicsDebugPoint(next, focused ? 4.8f : 3.8f, point);
            p = next;
        }

        Vector2 label = VisualPoint(entry.root) + new Vector2(8f, -18f);
        GUI.Label(new Rect(label.x, label.y, 120f, 18f), focused ? "物理振子" : "物理", EditorStyles.miniLabel);
    }

    private void DrawPhysicsDebugPoint(Vector2 worldPoint, float radius, Color color)
    {
        Vector2 p = VisualPoint(worldPoint);
        Rect r = new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f);
        EditorGUI.DrawRect(r, color);
    }

    private void DrawCenterOfGravityLine(Rect localView)
    {
        if (lastPsbPreviewRects == null || lastPsbPreviewRects.Count == 0)
            return;

        float weightedX = 0f;
        float totalWeight = 0f;
        Rect bounds = new Rect(0f, 0f, 0f, 0f);
        bool hasBounds = false;

        foreach (KeyValuePair<string, Rect> pair in lastPsbPreviewRects)
        {
            Rect r = pair.Value;
            if (r.width <= 0.5f || r.height <= 0.5f)
                continue;

            float clippedXMin = Mathf.Clamp(r.xMin, localView.xMin, localView.xMax);
            float clippedXMax = Mathf.Clamp(r.xMax, localView.xMin, localView.xMax);
            float clippedYMin = Mathf.Clamp(r.yMin, localView.yMin, localView.yMax);
            float clippedYMax = Mathf.Clamp(r.yMax, localView.yMin, localView.yMax);
            float area = Mathf.Max(0f, clippedXMax - clippedXMin) * Mathf.Max(0f, clippedYMax - clippedYMin);
            if (area <= 1f)
                continue;

            float cx = (clippedXMin + clippedXMax) * 0.5f;
            weightedX += cx * area;
            totalWeight += area;

            Rect clipped = Rect.MinMaxRect(clippedXMin, clippedYMin, clippedXMax, clippedYMax);
            bounds = hasBounds ? UnionRect(bounds, clipped) : clipped;
            hasBounds = true;
        }

        if (!hasBounds || totalWeight <= 0.0001f)
            return;

        float gravityX = weightedX / totalWeight;

        // 重心线是观察辅助线，不应该被角色当前包围盒截断。
        // 这里直接延伸到当前预览画布上下边界；缩放 / 平移后仍然保持一条纵向参考线。
        float y0 = localView.yMin;
        float y1 = localView.yMax;

        Color line = new Color(1f, 0.10f, 0.08f, 0.90f);
        DrawVerticalDashedLine(gravityX, y0, y1, line, 1f, 8f, 5f);

        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = line }
        };
        GUI.Label(new Rect(gravityX - 32f, localView.yMin + 4f, 64f, 16f), "重心线", labelStyle);
    }

    private Rect UnionRect(Rect a, Rect b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin),
            Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax),
            Mathf.Max(a.yMax, b.yMax));
    }

    private void DrawVerticalDashedLine(float x, float yMin, float yMax, Color color, float width = 1f, float dashLength = 8f, float gapLength = 5f)
    {
        if (yMax <= yMin)
            return;

        float safeWidth = Mathf.Max(1f, width);
        float dash = Mathf.Max(1f, dashLength);
        float gap = Mathf.Max(0f, gapLength);
        float y = yMin;

        while (y < yMax)
        {
            float h = Mathf.Min(dash, yMax - y);
            EditorGUI.DrawRect(new Rect(x - safeWidth * 0.5f, y, safeWidth, h), color);
            y += dash + gap;
        }
    }

    private void DrawHorizontalDashedLine(float y, float xMin, float xMax, Color color, float width = 1f, float dashLength = 8f, float gapLength = 5f)
    {
        if (xMax <= xMin)
            return;

        float safeWidth = Mathf.Max(1f, width);
        float dash = Mathf.Max(1f, dashLength);
        float gap = Mathf.Max(0f, gapLength);
        float x = xMin;

        while (x < xMax)
        {
            float w = Mathf.Min(dash, xMax - x);
            EditorGUI.DrawRect(new Rect(x, y - safeWidth * 0.5f, w, safeWidth), color);
            x += dash + gap;
        }
    }


    private Rect GetPreviewZoomPanelRect(Rect view)
    {
        const float panelWidth = 238f;
        const float panelHeight = 28f;
        return new Rect(
            Mathf.Max(view.x + 8f, view.xMax - panelWidth - 10f),
            view.yMax - panelHeight - 10f,
            Mathf.Min(panelWidth, Mathf.Max(120f, view.width - 16f)),
            panelHeight
        );
    }

    private void DrawPreviewZoomControls(Rect view)
    {
        // 缩放控件固定在预览画布右下角，完整留在 view 内部，不再被右侧边缘裁掉。
        Rect panel = GetPreviewZoomPanelRect(view);

        EditorGUI.DrawRect(panel, new Color(0f, 0f, 0f, 0.30f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(panel, new Color(1f, 1f, 1f, 0.08f));

        float x = panel.x + 6f;
        float y = panel.y + 4f;

        if (IconButton(new Rect(x, y, 22f, 20f), 15, "回到角色中心 / 100%"))
        {
            state.ResetPreviewView();
            ResetPreviewCanvasOrigin(view);
        }
        x += 28f;

        bool oldMirror = state.PreviewMirrored;
        Color oldColor = GUI.color;
        if (state.PreviewMirrored)
            GUI.color = new Color(0.55f, 0.85f, 1f, 1f);
        if (IconButton(new Rect(x, y, 22f, 20f), 37, state.PreviewMirrored ? "关闭镜像翻转" : "镜像翻转"))
            state.PreviewMirrored = !state.PreviewMirrored;
        GUI.color = oldColor;
        x += 30f;

        if (GUI.Button(new Rect(x, y, 22f, 20f), "-", EditorStyles.miniButton))
            state.PreviewZoom = Mathf.Clamp(state.PreviewZoom - 0.1f, 0.1f, 5f);
        x += 28f;

        float sliderWidth = Mathf.Max(54f, panel.xMax - x - 86f);
        state.PreviewZoom = GUI.HorizontalSlider(new Rect(x, panel.y + 9f, sliderWidth, 12f), state.PreviewZoom, 0.1f, 5f);
        x += sliderWidth + 8f;

        GUI.Label(new Rect(x, panel.y + 5f, 48f, 18f), Mathf.RoundToInt(state.PreviewZoom * 100f) + "%", EditorStyles.miniLabel);
        x += 50f;

        if (GUI.Button(new Rect(x, y, 22f, 20f), "+", EditorStyles.miniButton))
            state.PreviewZoom = Mathf.Clamp(state.PreviewZoom + 0.1f, 0.1f, 5f);
    }



    private void HandlePreviewUndoShortcutRequest()
    {
        Event e = Event.current;
        if (e == null)
            return;

        bool ownsPreview = state.PreviewPanelHasKeyboardFocus || state.PreviewPanelRigDragging || !string.IsNullOrEmpty(draggingBoneSegmentKey);
        if (!ownsPreview)
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        if (e.type == EventType.ValidateCommand)
        {
            if (e.commandName == "Undo" || e.commandName == "Redo")
                e.Use();
            return;
        }

        if (e.type == EventType.ExecuteCommand)
        {
            if (e.commandName == "Undo" || e.commandName == "UndoRedoPerformed")
            {
                state.WorkbenchUndoShortcutRequested = true;
                e.Use();
                return;
            }
            if (e.commandName == "Redo")
            {
                state.WorkbenchRedoShortcutRequested = true;
                e.Use();
                return;
            }
        }

        if (e.type != EventType.KeyDown)
            return;

        bool ctrlOrCmd = e.control || e.command;
        if (!ctrlOrCmd)
            return;

        if (e.keyCode == KeyCode.Z)
        {
            if (e.shift) state.WorkbenchRedoShortcutRequested = true;
            else state.WorkbenchUndoShortcutRequested = true;
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Y)
        {
            state.WorkbenchRedoShortcutRequested = true;
            e.Use();
        }
    }


    private void DrawGroupSelectedPreview(Rect view)
    {
        EditorGUI.DrawRect(view, new Color(0.08f, 0.08f, 0.09f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawGrid(view, Mathf.Max(8f, 24f * state.PreviewZoom), new Color(1f, 1f, 1f, 0.035f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(view, new Color(1f, 1f, 1f, 0.06f));

        GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            normal = { textColor = new Color(1f, 0.86f, 0.58f, 1f) }
        };
        GUIStyle msg = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = new Color(0.78f, 0.78f, 0.80f, 1f) }
        };

        GUI.Label(new Rect(view.x + 20f, view.center.y - 32f, view.width - 40f, 24f), "当前选中动作组：「" + state.CurrentActionGroupDisplayName() + "」", title);
        GUI.Label(new Rect(view.x + 36f, view.center.y - 4f, view.width - 72f, 46f), "动作组不参与姿势预览，也不能拖动 Motion。请选择组内具体动作后再编辑预览、骨骼、曲面和关键帧。", msg);
    }
    private void CapturePreviewKeyboardFocus(Rect view)
    {
        Event e = Event.current;
        if (e == null)
            return;

        // 给预览窗口一个真正的 IMGUI KeyboardControl。
        // 否则 Ctrl+Z 会继续走 Unity 编辑器本体的 Undo，而不是动作工作台自己的 Rig Undo 栈。
        previewKeyboardControlId = GUIUtility.GetControlID("SkyPrisonAnimationPreviewPanel.KeyboardFocus".GetHashCode(), FocusType.Keyboard, view);

        bool inside = view.Contains(e.mousePosition);
        state.PreviewPanelMouseInside = inside;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (inside)
            {
                state.PreviewPanelHasKeyboardFocus = true;
                GUIUtility.keyboardControl = previewKeyboardControlId;
                GUI.FocusControl(null);
                EditorGUIUtility.editingTextField = false;
            }
            else if (!state.PreviewPanelRigDragging)
            {
                state.PreviewPanelHasKeyboardFocus = false;
                if (GUIUtility.keyboardControl == previewKeyboardControlId)
                    GUIUtility.keyboardControl = 0;
            }
        }
    }

    private void HandleRigUndoShortcuts()
    {
        Event e = Event.current;
        if (e == null)
            return;

        bool previewOwnsUndo = state.PreviewPanelHasKeyboardFocus || state.PreviewPanelRigDragging || !string.IsNullOrEmpty(draggingBoneSegmentKey);
        if (!previewOwnsUndo)
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        if (e.type == EventType.ValidateCommand)
        {
            if (e.commandName == "Undo" || e.commandName == "Redo")
                e.Use();
            return;
        }

        if (e.type == EventType.ExecuteCommand)
        {
            if (e.commandName == "Undo")
            {
                if (state.UndoRig()) GUI.changed = true;
                e.Use();
                return;
            }
            if (e.commandName == "Redo")
            {
                if (state.RedoRig()) GUI.changed = true;
                e.Use();
                return;
            }
        }

        if (e.type != EventType.KeyDown)
            return;

        bool modifier = e.control || e.command;
        if (!modifier)
            return;

        if (e.keyCode == KeyCode.Z)
        {
            bool ok = e.shift ? state.RedoRig() : state.UndoRig();
            if (ok) GUI.changed = true;
            e.Use();
        }
        else if (e.keyCode == KeyCode.Y)
        {
            if (state.RedoRig()) GUI.changed = true;
            e.Use();
        }
    }


    private Rect GetRigEditButtonRect(Rect view)
    {
        return new Rect(view.x + 10f, view.y + 10f, 42f, 42f);
    }

    private Rect GetRigEditLocalButtonRect()
    {
        return new Rect(10f, 10f, 42f, 42f);
    }

    private void DrawRigEditRoundButton(Rect view)
    {
        Rect rect = GetRigEditButtonRect(view);
        bool active = state.ShowRigEdit;
        Event e = Event.current;
        bool hover = e != null && rect.Contains(e.mousePosition);

        Color oldColor = GUI.color;
        GUI.color = active
            ? (hover ? new Color(1f, 0.82f, 0.32f, 1f) : new Color(0.96f, 0.72f, 0.22f, 0.98f))
            : (hover ? new Color(0.34f, 0.38f, 0.42f, 0.96f) : new Color(0.12f, 0.13f, 0.14f, 0.88f));
        GUI.DrawTexture(rect, GetCircleTexture(), ScaleMode.StretchToFill, true);
        GUI.color = oldColor;

        Handles.BeginGUI();
        Handles.color = active ? new Color(1f, 0.94f, 0.48f, 1f) : new Color(1f, 1f, 1f, 0.28f);
        Handles.DrawWireDisc(rect.center, Vector3.forward, rect.width * 0.5f - 1f);
        Handles.color = new Color(0f, 0f, 0f, 0.24f);
        Handles.DrawWireDisc(rect.center, Vector3.forward, rect.width * 0.5f - 3f);
        Handles.EndGUI();

        Texture2D icon = GetEditorIcon(46);
        Rect iconRect = new Rect(rect.center.x - 11f, rect.center.y - 11f, 22f, 22f);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            GUI.Label(rect, active ? "✓" : "+", GetCenteredMiniBoldStyle());

        GUI.Label(rect, new GUIContent(string.Empty, active ? "关闭骨架编辑" : "开启骨架编辑"));

        if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            state.ShowRigEdit = !state.ShowRigEdit;
            if (state.ShowRigEdit)
            {
                // 编辑模式是 Rig Setup，不是动画播放/关键帧模式。
                state.PreviewPlaying = false;

                // 编辑模式是工作台能力，不依赖 RigRows 是否为空。
                state.ShowRigLines = true;
                state.StructureTab = SkyPrisonAnimationStructureTab.Rig;
                state.SelectedRig = state.RigRows.Count > 0
                    ? Mathf.Clamp(state.SelectedRig, 0, state.RigRows.Count - 1)
                    : -1;
            }
            creatingCustomBoneLine = false;
            e.Use();
        }
    }

    private GUIStyle GetCenteredMiniBoldStyle()
    {
        return new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
    }

    private Vector2 GetRigSegmentMotionDelta(string headKey, Dictionary<string, Vector2> currentAnchors, Dictionary<string, Vector2> restAnchors)
    {
        if (string.IsNullOrEmpty(headKey) || currentAnchors == null || restAnchors == null)
            return Vector2.zero;

        Vector2 headDelta = Vector2.zero;
        if (currentAnchors.ContainsKey(headKey) && restAnchors.ContainsKey(headKey))
            headDelta = currentAnchors[headKey] - restAnchors[headKey];

        string tailKey = GetHumanoidV1TailKey(headKey);
        if (string.IsNullOrEmpty(tailKey) || !currentAnchors.ContainsKey(tailKey) || !restAnchors.ContainsKey(tailKey))
            return headDelta;

        Vector2 tailDelta = currentAnchors[tailKey] - restAnchors[tailKey];
        return Vector2.Lerp(headDelta, tailDelta, 0.55f);
    }


    private Dictionary<string, RigBoneSegment> BuildRigBoneSegments(Dictionary<string, Vector2> anchors, float zoom, bool includeSetupOffsets, bool includeRuntimeOffsets)
    {
        // Spine式父子链：Pelvis -> Spine -> Chest -> Neck -> Head。
        // 子骨骼 Root 默认来自父骨骼 Head；Root 偏移是子骨骼在父空间里的局部偏移，
        // 所以父骨骼旋转/移动后，子骨骼仍然会继承父级控制。
        Dictionary<string, RigBoneSegment> segments = new Dictionary<string, RigBoneSegment>();
        if (anchors == null)
            return segments;

        // RigRows 为空就是空骨架状态，不应该生成隐藏 Human fallback 骨段来抢鼠标事件。
        // Custom 模板也只使用用户手绘的 CustomBone。
        if (IsManualCustomRigMode() || state.RigRows == null || state.RigRows.Count == 0)
        {
            Dictionary<string, RigBoneSegment> restSegmentsForCustom = null;
            if (includeRuntimeOffsets)
                restSegmentsForCustom = BuildRigBoneSegments(anchors, zoom, includeSetupOffsets, false);

            AddCustomRigBoneSegments(segments, anchors, zoom, includeSetupOffsets, includeRuntimeOffsets, restSegmentsForCustom);
            return segments;
        }

        AddSpineBoneSegment(segments, anchors, "Pelvis", string.Empty, "Pelvis", "Spine", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddSpineBoneSegment(segments, anchors, "Spine", "Pelvis", "Spine", "Chest", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddSpineBoneSegment(segments, anchors, "Chest", "Spine", "Chest", "Neck", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddSpineBoneSegment(segments, anchors, "Neck", "Chest", "Neck", "Head", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddSpineBoneSegment(segments, anchors, "Head", "Neck", "Head", "HeadTop", zoom, includeSetupOffsets, includeRuntimeOffsets);

        // 手臂分支：Root 在 Chest 父空间里带局部偏移。
        // 因此拖 Chest.Head 时双臂跟随；拖 Shoulder/Elbow/Wrist 时不反推胸口。
        AddBranchBoneSegment(segments, anchors, "Shoulder_L", "Chest", "Shoulder_L", "Elbow_L", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Elbow_L", "Shoulder_L", "Elbow_L", "Wrist_L", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Wrist_L", "Elbow_L", "Wrist_L", "HandEnd_L", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Shoulder_R", "Chest", "Shoulder_R", "Elbow_R", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Elbow_R", "Shoulder_R", "Elbow_R", "Wrist_R", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Wrist_R", "Elbow_R", "Wrist_R", "HandEnd_R", zoom, includeSetupOffsets, includeRuntimeOffsets);

        // 腿部分支：Root 在 Pelvis 父空间里带局部偏移。
        AddBranchBoneSegment(segments, anchors, "Hip_L", "Pelvis", "Hip_L", "Knee_L", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Knee_L", "Hip_L", "Knee_L", "Ankle_L", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Ankle_L", "Knee_L", "Ankle_L", "Foot_L", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Hip_R", "Pelvis", "Hip_R", "Knee_R", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Knee_R", "Hip_R", "Knee_R", "Ankle_R", zoom, includeSetupOffsets, includeRuntimeOffsets);
        AddBranchBoneSegment(segments, anchors, "Ankle_R", "Knee_R", "Ankle_R", "Foot_R", zoom, includeSetupOffsets, includeRuntimeOffsets);

        Dictionary<string, RigBoneSegment> restSegmentsForCustomChildren = null;
        if (includeRuntimeOffsets)
            restSegmentsForCustomChildren = BuildRigBoneSegments(anchors, zoom, includeSetupOffsets, false);

        AddCustomRigBoneSegments(segments, anchors, zoom, includeSetupOffsets, includeRuntimeOffsets, restSegmentsForCustomChildren);

        return segments;
    }

    private void AddSpineBoneSegment(Dictionary<string, RigBoneSegment> segments, Dictionary<string, Vector2> anchors, string segmentKey, string parentSegmentKey, string rootKey, string headKey, float zoom, bool includeSetupOffsets, bool includeRuntimeOffsets)
    {
        if (!anchors.TryGetValue(rootKey, out Vector2 baseRoot) || !anchors.TryGetValue(headKey, out Vector2 baseHead))
            return;

        Vector2 rootOffset;
        Vector2 headOffset;
        GetBoneEndpointOffsets(segmentKey, zoom, includeSetupOffsets, includeRuntimeOffsets, out rootOffset, out headOffset);

        Vector2 root;
        Vector2 head;
        Vector2 baseVector = baseHead - baseRoot;
        float localAngle = 0f;
        bool hasLocalAngle = includeRuntimeOffsets && TryGetRigAngleForPoseSnapshot(segmentKey, out localAngle);
        Vector2 localVector = baseVector + headOffset;
        if (hasLocalAngle)
            localVector = RotateVector(localVector, localAngle);

        if (string.IsNullOrEmpty(parentSegmentKey) || !segments.TryGetValue(parentSegmentKey, out RigBoneSegment parent))
        {
            // 根骨骼：Pelvis.Root 是整条中轴链的世界根。
            root = baseRoot + rootOffset;
            head = root + localVector;
        }
        else
        {
            Vector2 parentBaseRoot;
            Vector2 parentBaseHead;
            float inheritedAngle = 0f;
            if (anchors.TryGetValue(parent.rootKey, out parentBaseRoot) && anchors.TryGetValue(parent.headKey, out parentBaseHead))
            {
                Vector2 parentBaseVector = parentBaseHead - parentBaseRoot;
                Vector2 parentCurrentVector = parent.head - parent.root;
                if (parentBaseVector.sqrMagnitude > 0.0001f && parentCurrentVector.sqrMagnitude > 0.0001f)
                    inheritedAngle = Vector2.SignedAngle(parentBaseVector, parentCurrentVector);
            }

            // 子骨骼 Root 默认贴在父骨骼 Head 上；rootOffset 让它可以在父空间里偏离，
            // 但这个偏移会被父级旋转带着走，不会断开父子关系。
            root = parent.head + RotateVector(rootOffset, inheritedAngle);
            head = root + RotateVector(localVector, inheritedAngle);
        }

        segments[segmentKey] = new RigBoneSegment
        {
            segmentKey = segmentKey,
            rootKey = rootKey,
            headKey = headKey,
            root = root,
            head = head
        };
    }


    private void AddBranchBoneSegment(Dictionary<string, RigBoneSegment> segments, Dictionary<string, Vector2> anchors, string segmentKey, string parentSegmentKey, string rootKey, string headKey, float zoom, bool includeSetupOffsets, bool includeRuntimeOffsets)
    {
        if (!anchors.TryGetValue(rootKey, out Vector2 baseRoot) || !anchors.TryGetValue(headKey, out Vector2 baseHead))
            return;

        Vector2 rootOffset;
        Vector2 headOffset;
        GetBoneEndpointOffsets(segmentKey, zoom, includeSetupOffsets, includeRuntimeOffsets, out rootOffset, out headOffset);

        Vector2 root;
        Vector2 head;
        Vector2 baseVector = baseHead - baseRoot;
        float localAngle = 0f;
        bool hasLocalAngle = includeRuntimeOffsets && TryGetRigAngleForPoseSnapshot(segmentKey, out localAngle);
        Vector2 localVector = baseVector + headOffset;
        if (hasLocalAngle)
            localVector = RotateVector(localVector, localAngle);

        if (string.IsNullOrEmpty(parentSegmentKey) || !segments.TryGetValue(parentSegmentKey, out RigBoneSegment parent))
        {
            root = baseRoot + rootOffset;
            head = root + localVector;
        }
        else
        {
            Vector2 parentBaseRoot;
            Vector2 parentBaseHead;
            float inheritedAngle = 0f;
            Vector2 parentLocalRootFromHead = Vector2.zero;

            if (anchors.TryGetValue(parent.rootKey, out parentBaseRoot) && anchors.TryGetValue(parent.headKey, out parentBaseHead))
            {
                Vector2 parentBaseVector = parentBaseHead - parentBaseRoot;
                Vector2 parentCurrentVector = parent.head - parent.root;
                if (parentBaseVector.sqrMagnitude > 0.0001f && parentCurrentVector.sqrMagnitude > 0.0001f)
                    inheritedAngle = Vector2.SignedAngle(parentBaseVector, parentCurrentVector);

                // 分支骨骼不能强行贴在父 Head 上；需要保留“肩宽/上臂起点”带来的局部偏移。
                parentLocalRootFromHead = baseRoot - parentBaseHead;
            }

            root = parent.head + RotateVector(parentLocalRootFromHead + rootOffset, inheritedAngle);
            head = root + RotateVector(localVector, inheritedAngle);
        }

        segments[segmentKey] = new RigBoneSegment
        {
            segmentKey = segmentKey,
            rootKey = rootKey,
            headKey = headKey,
            root = root,
            head = head
        };
    }

    private void GetBoneEndpointOffsets(string segmentKey, float zoom, bool includeSetupOffsets, bool includeRuntimeOffsets, out Vector2 rootOffset, out Vector2 headOffset)
    {
        rootOffset = Vector2.zero;
        headOffset = Vector2.zero;

        SkyPrisonAnimationRigRow row = state.FindRigRow(segmentKey);
        if (row == null)
            return;

        float z = Mathf.Max(0.0001f, zoom);
        Vector2 rootEndpointOffset = Vector2.zero;
        Vector2 headEndpointOffset = Vector2.zero;

        if (includeSetupOffsets)
        {
            Vector2 setupRoot = row.useManualBoneRootOffset ? row.manualBoneRootOffset * z : Vector2.zero;
            Vector2 setupHead;
            bool hasSetupHead = false;

            if (row.useManualBoneHeadOffset)
            {
                setupHead = row.manualBoneHeadOffset * z;
                hasSetupHead = true;
            }
            else if (row.useManualRigOffset)
            {
                setupHead = row.manualRigOffset * z;
                hasSetupHead = true;
            }
            else
            {
                setupHead = setupRoot;
            }

            rootEndpointOffset += setupRoot;
            headEndpointOffset += hasSetupHead ? setupHead : setupRoot;
        }

        if (includeRuntimeOffsets)
        {
            bool hasTimelineRoot;
            bool hasTimelineHead;
            Vector2 fallbackRuntimeRoot = (!drawingOnionSkinSnapshot && row.useRuntimeBoneRootOffset) ? row.runtimeBoneRootOffset : Vector2.zero;
            Vector2 fallbackRuntimeHead = (!drawingOnionSkinSnapshot && row.useRuntimeBoneHeadOffset)
                ? row.runtimeBoneHeadOffset
                : (!drawingOnionSkinSnapshot && row.useManualRigLayerOffset ? row.manualRigLayerOffset : fallbackRuntimeRoot);

            Vector2 runtimeRootSource = state.EvaluateTimelineRuntimeBoneRootOffset(row.key, fallbackRuntimeRoot, out hasTimelineRoot);
            Vector2 runtimeHeadSource = state.EvaluateTimelineRuntimeBoneHeadOffset(row.key, fallbackRuntimeHead, out hasTimelineHead);

            Vector2 runtimeRoot = ((!drawingOnionSkinSnapshot && row.useRuntimeBoneRootOffset) || hasTimelineRoot) ? runtimeRootSource * z : Vector2.zero;
            Vector2 runtimeHead;
            bool hasRuntimeHead = false;

            // RigAngle 是 Head 端旋转控制。只要当前白线帧处这个骨骼由 RigAngle 接管，
            // 同一目标上的旧 Rig / RuntimeOffset / manualRigLayerOffset 都不能再给 Head 端追加偏移。
            // 否则会出现“拖动时一边按角度转，一边又被旧 Head 偏移拉回去”的抖动/抽动。
            bool angleControlsHead = state != null && state.IsRigAngleDrivingAtCurrentFrame(row.key);
            if (angleControlsHead)
            {
                runtimeHead = runtimeRoot;
                hasRuntimeHead = true;
            }
            else if ((!drawingOnionSkinSnapshot && row.useRuntimeBoneHeadOffset) || hasTimelineHead)
            {
                runtimeHead = runtimeHeadSource * z;
                hasRuntimeHead = true;
            }
            else if (!drawingOnionSkinSnapshot && row.useManualRigLayerOffset)
            {
                Vector2 fallbackRuntimeOffset = row.manualRigLayerOffset;
                runtimeHead = state.EvaluateTimelineRuntimeOffset(row.key, fallbackRuntimeOffset) * z;
                hasRuntimeHead = true;
            }
            else
            {
                runtimeHead = runtimeRoot;
            }

            rootEndpointOffset += runtimeRoot;
            headEndpointOffset += hasRuntimeHead ? runtimeHead : runtimeRoot;
        }

        rootOffset = rootEndpointOffset;

        // 数据层存的是端点偏移；Spine链路计算时 Head 需要换成相对 Root 的局部修正。
        // Root 端拖动：root=head，换算后 HeadLocal=0，整条骨骼只平移。
        // Head 端拖动：root不变，HeadLocal保留，骨骼旋转/编辑长度。
        headOffset = headEndpointOffset - rootEndpointOffset;
    }

    private Vector2 GetSegmentBaseVector(RigBoneSegment segment, Dictionary<string, Vector2> anchors)
    {
        if (IsCustomRigSegment(segment.segmentKey))
        {
            SkyPrisonAnimationRigRow row = state.FindRigRow(segment.segmentKey);
            if (row != null && row.useManualBoneRootOffset && row.useManualBoneHeadOffset)
                return (row.manualBoneHeadOffset - row.manualBoneRootOffset) * Mathf.Max(0.0001f, state.PreviewZoom);
        }

        if (anchors == null)
            return segment.head - segment.root;

        Vector2 baseRoot;
        Vector2 baseHead;
        if (anchors.TryGetValue(segment.rootKey, out baseRoot) && anchors.TryGetValue(segment.headKey, out baseHead))
            return baseHead - baseRoot;

        return segment.head - segment.root;
    }

    private float GetSegmentInheritedAngle(string segmentKey, Dictionary<string, RigBoneSegment> segments, Dictionary<string, Vector2> anchors)
    {
        if (segments == null || anchors == null || string.IsNullOrEmpty(segmentKey))
            return 0f;

        string parentKey = GetRigSegmentParentKey(segmentKey);
        if (string.IsNullOrEmpty(parentKey))
            return 0f;

        RigBoneSegment parent;
        if (!segments.TryGetValue(parentKey, out parent))
            return 0f;

        Vector2 parentBaseRoot;
        Vector2 parentBaseHead;
        if (!anchors.TryGetValue(parent.rootKey, out parentBaseRoot) || !anchors.TryGetValue(parent.headKey, out parentBaseHead))
            return 0f;

        Vector2 baseVector = parentBaseHead - parentBaseRoot;
        Vector2 currentVector = parent.head - parent.root;
        if (baseVector.sqrMagnitude < 0.0001f || currentVector.sqrMagnitude < 0.0001f)
            return 0f;

        return Vector2.SignedAngle(baseVector, currentVector);
    }

    private string GetRigSegmentParentKey(string segmentKey)
    {
        switch (segmentKey)
        {
            case "Spine": return "Pelvis";
            case "Chest": return "Spine";
            case "Neck": return "Chest";
            case "Head": return "Neck";
            case "Shoulder_L": return "Chest";
            case "Elbow_L": return "Shoulder_L";
            case "Wrist_L": return "Elbow_L";
            case "Shoulder_R": return "Chest";
            case "Elbow_R": return "Shoulder_R";
            case "Wrist_R": return "Elbow_R";
            case "Hip_L": return "Pelvis";
            case "Knee_L": return "Hip_L";
            case "Ankle_L": return "Knee_L";
            case "Hip_R": return "Pelvis";
            case "Knee_R": return "Hip_R";
            case "Ankle_R": return "Knee_R";
            default: return string.Empty;
        }
    }

    private Vector2 GetSetupEndpointRelativeOffset(SkyPrisonAnimationRigRow row, float zoom)
    {
        if (row == null)
            return Vector2.zero;

        float z = Mathf.Max(0.0001f, zoom);
        Vector2 root = row.useManualBoneRootOffset ? row.manualBoneRootOffset * z : Vector2.zero;
        Vector2 head;

        if (row.useManualBoneHeadOffset)
            head = row.manualBoneHeadOffset * z;
        else if (row.useManualRigOffset)
            head = row.manualRigOffset * z;
        else
            head = root;

        return head - root;
    }

    private Vector2 RotateVector(Vector2 v, float degrees)
    {
        if (Mathf.Abs(degrees) < 0.0001f || v.sqrMagnitude < 0.000001f)
            return v;
        float rad = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad);
        float s = Mathf.Sin(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private bool TryGetSegmentPair(string segmentKey, Dictionary<string, RigBoneSegment> restSegments, Dictionary<string, RigBoneSegment> currentSegments, out RigBoneSegment rest, out RigBoneSegment current)
    {
        rest = new RigBoneSegment();
        current = new RigBoneSegment();
        if (string.IsNullOrEmpty(segmentKey) || restSegments == null || currentSegments == null)
            return false;
        return restSegments.TryGetValue(segmentKey, out rest) && currentSegments.TryGetValue(segmentKey, out current);
    }

    private string ResolveDriverSegmentKeyForBoundRig(string rigKey)
    {
        switch (rigKey)
        {
            // 中轴端点。
            case "HeadTop": return "Head";

            // 手臂末端。
            case "HandEnd_L": return "Wrist_L";
            case "HandEnd_R": return "Wrist_R";

            // 腿脚末端。Foot 是 Ankle 段的 Head，不是独立段。
            case "Foot_L": return "Ankle_L";
            case "Foot_R": return "Ankle_R";

            default:
                return rigKey;
        }
    }

    private Vector2 GetManualRigSegmentDisplayOffset(string headKey, float zoom)
    {
        Vector2 headOffset = GetManualRigDisplayOffset(headKey, zoom);
        string tailKey = GetHumanoidV1TailKey(headKey);
        if (string.IsNullOrEmpty(tailKey))
            return headOffset;

        Vector2 tailOffset = GetManualRigDisplayOffset(tailKey, zoom);
        return Vector2.Lerp(headOffset, tailOffset, 0.55f);
    }

    private string GetHumanoidV1TailKey(string headKey)
    {
        switch (headKey)
        {
            case "Pelvis": return "Spine";
            case "Spine": return "Chest";
            case "Chest": return "Neck";
            case "Neck": return "Head";
            case "Head": return "HeadTop";
            case "Shoulder_L": return "Elbow_L";
            case "Elbow_L": return "Wrist_L";
            case "Wrist_L": return "HandEnd_L";
            case "Shoulder_R": return "Elbow_R";
            case "Elbow_R": return "Wrist_R";
            case "Wrist_R": return "HandEnd_R";
            case "Hip_L": return "Knee_L";
            case "Knee_L": return "Ankle_L";
            case "Ankle_L": return "Foot_L";
            case "Hip_R": return "Knee_R";
            case "Knee_R": return "Ankle_R";
            case "Ankle_R": return "Foot_R";
            default: return string.Empty;
        }
    }

    private Vector2 GetManualRigDisplayOffset(string rigKey, float zoom)
    {
        // 非编辑模式拖拽使用 manualRigLayerOffset，表示“当前姿态偏移”。
        // 它只参与 current/display，不参与 rest，所以贴图可以跟随骨骼角度旋转。
        SkyPrisonAnimationRigRow row = state.FindRigRow(rigKey);
        if (row == null)
            return Vector2.zero;

        Vector2 fallback = (!drawingOnionSkinSnapshot && row.useManualRigLayerOffset) ? row.manualRigLayerOffset : Vector2.zero;
        Vector2 evaluated = state.EvaluateTimelineRuntimeOffset(rigKey, fallback);
        return evaluated * Mathf.Max(0.0001f, zoom);
    }

    private Dictionary<string, Vector2> ApplySetupRigOffsets(Dictionary<string, Vector2> anchors, float zoom)
    {
        if (anchors == null || state.RigRows == null)
            return anchors;

        Dictionary<string, Vector2> result = new Dictionary<string, Vector2>(anchors);
        float z = Mathf.Max(0.0001f, zoom);
        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || string.IsNullOrEmpty(row.key) || !row.useManualRigOffset)
                continue;
            if (!result.ContainsKey(row.key))
                continue;
            result[row.key] = result[row.key] + row.manualRigOffset * z;
        }
        return result;
    }

    private Dictionary<string, Vector2> ApplyRuntimeRigOffsets(Dictionary<string, Vector2> anchors, float zoom)
    {
        if (anchors == null || state.RigRows == null)
            return anchors;

        Dictionary<string, Vector2> result = new Dictionary<string, Vector2>(anchors);
        float z = Mathf.Max(0.0001f, zoom);
        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || string.IsNullOrEmpty(row.key))
                continue;
            Vector2 fallback = (!drawingOnionSkinSnapshot && row.useManualRigLayerOffset) ? row.manualRigLayerOffset : Vector2.zero;
            if ((!row.useManualRigLayerOffset || drawingOnionSkinSnapshot) && !state.HasTimelineKeyframes(row.key))
                continue;
            if (!result.ContainsKey(row.key))
                continue;
            result[row.key] = result[row.key] + state.EvaluateTimelineRuntimeOffset(row.key, fallback) * z;
        }
        return result;
    }

    private Dictionary<string, Vector2> ApplyRigOffsetsForContext(Dictionary<string, Vector2> anchors, float zoom, bool includeSetupOffsets, bool includeRuntimeOffsets)
    {
        Dictionary<string, Vector2> result = anchors;
        if (includeSetupOffsets)
            result = ApplySetupRigOffsets(result, zoom);
        if (includeRuntimeOffsets)
            result = ApplyRuntimeRigOffsets(result, zoom);
        return result;
    }

    private Dictionary<string, Vector2> ApplyDisplayRigOffsets(Dictionary<string, Vector2> anchors, float zoom)
    {
        return ApplyRigOffsetsForContext(anchors, zoom, true, !state.ShowRigEdit);
    }

    private void HandleRigDragEvents(Dictionary<string, Vector2> anchors, float zoom)
    {
        // 新规则：拖的是“骨骼线端点”，不是共享关节。
        // Root 端：平移整条骨骼线，角度/长度不变。
        // Head 端：Root 固定；非编辑模式锁长度只旋转，编辑模式允许同步改 Setup 长度。
        if (anchors == null)
            return;

        Event e = Event.current;
        if (e == null)
            return;

        Dictionary<string, RigBoneSegment> segments = BuildRigBoneSegments(anchors, zoom, true, !state.ShowRigEdit);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            bool hitRoot;
            string segmentKey = FindNearestBoneEndpoint(segments, e.mousePosition, Mathf.Clamp(12f * Mathf.Max(1f, zoom), 12f, 22f), out hitRoot);
            if (string.IsNullOrEmpty(segmentKey))
            {
                if (state.ShowRigEdit && !GetRigEditLocalButtonRect().Contains(e.mousePosition))
                {
                    creatingCustomBoneLine = true;
                    creatingCustomBoneRootLocal = e.mousePosition;
                    creatingCustomBoneHeadLocal = e.mousePosition;
                    state.PreviewPanelHasKeyboardFocus = true;
                    if (previewKeyboardControlId != 0)
                        GUIUtility.keyboardControl = previewKeyboardControlId;
                    e.Use();
                }
                return;
            }

            SkyPrisonAnimationRigRow row = state.FindRigRow(segmentKey);
            if (row == null || row.locked)
                return;

            // 轨道锁定：选中“手”的轨道时，预览区只能拖“手”的骨骼线。
            // 这样不会发生“手轨道插了关键帧，结果拖腿把手关键帧污染”的问题。
            if (!state.ShowRigEdit && state.TimelineTrackLockEnabled && !string.IsNullOrEmpty(state.ActiveTimelineTrackKey) && !state.CanEditAnimatedTarget(segmentKey))
                return;

            // 撤销必须在任何“锁定/创建当前帧关键帧”之前压栈。
            // 非编辑模式下拖 Root / Head 会写入 TimelineKeyframes、ManualPoseKeys、ManualBoneAngles，
            // 这些不在 RigUndo 里；如果这里只 PushRigUndo，Ctrl+Z 看起来就会“没反应”。
            // 编辑模式仍然只改 Setup RigRows，所以继续走 RigUndo，保持轻量。
            if (state.ShowRigEdit)
                state.PushRigUndo();
            else
                state.PushStructureUndo();

            // 编辑模式是 Setup / Rig 调整，不是动画时间线编辑。
            // 非编辑模式下按端点类型锁定不同关键帧：
            // Root = Rig 位移关键帧；Head = RigAngle 旋转关键帧。
            if (!state.ShowRigEdit)
                state.LockCurrentFrameKeyframeForRigTarget(segmentKey, true, !hitRoot);

            state.PreviewPanelHasKeyboardFocus = true;
            state.PreviewPanelRigDragging = true;
            if (previewKeyboardControlId != 0)
                GUIUtility.keyboardControl = previewKeyboardControlId;
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;

            RigBoneSegment seg = segments[segmentKey];
            draggingBoneSegmentKey = segmentKey;
            draggingBoneRootHandle = hitRoot;
            draggingManualRigStartMouse = e.mousePosition;
            draggingBoneStartRootWorld = seg.root;
            draggingBoneStartHeadWorld = seg.head;
            draggingBoneStartLength = Mathf.Max(0.0001f, Vector2.Distance(seg.root, seg.head));
            draggingBoneStartBaseVector = GetSegmentBaseVector(seg, anchors);
            draggingBoneStartInheritedAngle = GetSegmentInheritedAngle(segmentKey, segments, anchors);
            draggingBoneStartSetupRootOffset = row.useManualBoneRootOffset ? row.manualBoneRootOffset : Vector2.zero;
            draggingBoneStartSetupHeadOffset = row.useManualBoneHeadOffset
                ? row.manualBoneHeadOffset
                : (row.useManualRigOffset ? row.manualRigOffset : draggingBoneStartSetupRootOffset);
            draggingBoneStartRuntimeRootOffset = row.useRuntimeBoneRootOffset ? row.runtimeBoneRootOffset : Vector2.zero;
            draggingBoneStartRuntimeHeadOffset = row.useRuntimeBoneHeadOffset
                ? row.runtimeBoneHeadOffset
                : (row.useManualRigLayerOffset ? row.manualRigLayerOffset : draggingBoneStartRuntimeRootOffset);
            draggingRootShiftGuideVisible = false;
            draggingRootShiftGuideHasAxis = false;
            draggingRootShiftGuideHorizontal = false;
            draggingRootShiftGuideOrigin = VisualPoint(GetBoneEndpointHandlePoint(seg, true));

            // 非编辑模式下，当前帧的真实姿态可能来自“已选中的关键帧”，而不是 RigRow 本体。
            // 之前 Root 端平移时只从 RigRow 读取 startRoot/startHead；如果关键帧里已经有 Head 旋转，
            // Root 一移动就会把 Head 起点当成默认值重写，表现为“移动根部会重置骨骼线头的旋转角度”。
            // 这里改成：正在编辑当前帧关键帧时，拖拽起点必须从关键帧本身读取。
            if (!state.ShowRigEdit && state.IsSelectedTimelineKeyframeForRowAtCurrentFrame(row))
            {
                SkyPrisonAnimationTimelineKeyframe selectedKey = state.GetSelectedTimelineKeyframe();
                if (selectedKey != null && selectedKey.targetKey == row.key)
                {
                    draggingBoneStartRuntimeRootOffset = selectedKey.useRuntimeBoneRootOffset
                        ? selectedKey.runtimeBoneRootOffset
                        : Vector2.zero;

                    draggingBoneStartRuntimeHeadOffset = selectedKey.useRuntimeBoneHeadOffset
                        ? selectedKey.runtimeBoneHeadOffset
                        : (selectedKey.runtimeOffset != Vector2.zero ? selectedKey.runtimeOffset : draggingBoneStartRuntimeRootOffset);
                }
            }

            draggingManualRigKey = segmentKey;
            state.LastSelectedRigKey = segmentKey;
            for (int i = 0; i < state.RigRows.Count; i++)
            {
                if (state.RigRows[i] != null && state.RigRows[i].key == segmentKey)
                {
                    state.SelectedRig = i;
                    break;
                }
            }

            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && creatingCustomBoneLine)
        {
            creatingCustomBoneHeadLocal = e.mousePosition;
            GUI.changed = true;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && creatingCustomBoneLine)
        {
            creatingCustomBoneHeadLocal = e.mousePosition;
            CommitCustomRigBoneFromPreview(zoom);
            creatingCustomBoneLine = false;
            GUI.changed = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && !string.IsNullOrEmpty(draggingBoneSegmentKey))
        {
            SkyPrisonAnimationRigRow row = state.FindRigRow(draggingBoneSegmentKey);
            if (row == null || row.locked)
                return;
            if (!state.ShowRigEdit && state.TimelineTrackLockEnabled && !string.IsNullOrEmpty(state.ActiveTimelineTrackKey) && !state.CanEditAnimatedTarget(row.key))
                return;

            // 非编辑模式 + 拖 Head 端：这是“当前帧骨骼旋转”编辑，不是 Rest Pose 编辑。
            // 以前这里沿用端点 offset 写法，后来接入 RigAngle 后又要求先显式选中关键帧，
            // 导致原本的预览区拖动旋转失效。这里改为直接根据当前鼠标方向计算局部角度，
            // 同步写入左侧动作参数、当前帧姿势点和时间线 RigAngle 关键帧。
            if (!state.ShowRigEdit && !draggingBoneRootHandle)
            {
                Vector2 rigMouseForAngle = VisualToRigPoint(e.mousePosition);
                Vector2 dir = rigMouseForAngle - draggingBoneStartRootWorld;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = draggingBoneStartHeadWorld - draggingBoneStartRootWorld;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = draggingBoneStartBaseVector;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = Vector2.up;

                Vector2 lockedHeadWorld = draggingBoneStartRootWorld + dir.normalized * Mathf.Max(0.0001f, draggingBoneStartLength);
                Vector2 desiredVectorInParentSpace = RotateVector(lockedHeadWorld - draggingBoneStartRootWorld, -draggingBoneStartInheritedAngle);

                Vector2 baseVector = draggingBoneStartBaseVector;
                if (baseVector.sqrMagnitude < 0.0001f)
                    baseVector = draggingBoneStartHeadWorld - draggingBoneStartRootWorld;
                if (baseVector.sqrMagnitude < 0.0001f)
                    baseVector = Vector2.up;

                float angleDeg = Vector2.SignedAngle(baseVector, desiredVectorInParentSpace);
                angleDeg = Mathf.Clamp(angleDeg, -180f, 180f);

                state.SetManualBoneAngle(row.key, angleDeg);
                state.ApplyManualAngleLiveChange(row.key);
                state.SelectTimelineTrack(row.key, true);
                // 拖动本身就是实时输入源。不要立刻从时间线回读，
                // 否则白线停在非整数帧时会读到前后关键帧插值，把刚拖出的角度拉回去造成抖动。
                GUI.changed = true;
                e.Use();
                return;
            }

            if (!state.ShowRigEdit && draggingBoneRootHandle)
            {
                // Root 拖动必须能直接移动当前帧。
                // 如果同帧只有 RigAngle，不能把位移写进角度帧，也不能覆盖角度帧；
                // 这里创建/锁定一个并存的 Rig 位移关键帧。
                state.EnsureCurrentFrameRigOffsetKeyframeForRow(row);
            }

            bool keyframeOnlyEdit = !state.ShowRigEdit && (draggingBoneRootHandle || state.ShouldRedirectAnimatedEditToTimelineKeyframe(row));
            bool oldUseManualRigLayerOffset = row.useManualRigLayerOffset;
            Vector2 oldManualRigLayerOffset = row.manualRigLayerOffset;
            bool oldUseRuntimeBoneRootOffset = row.useRuntimeBoneRootOffset;
            Vector2 oldRuntimeBoneRootOffset = row.runtimeBoneRootOffset;
            bool oldUseRuntimeBoneHeadOffset = row.useRuntimeBoneHeadOffset;
            Vector2 oldRuntimeBoneHeadOffset = row.runtimeBoneHeadOffset;

            Vector2 rigMouse = VisualToRigPoint(e.mousePosition);
            Vector2 localDelta;

            if (draggingBoneRootHandle)
            {
                // Root 端：整条骨骼线平移，角度不变。
                Vector2 delta = e.mousePosition - draggingManualRigStartMouse;
                if (e.shift)
                {
                    draggingRootShiftGuideVisible = true;
                    draggingRootShiftGuideHasAxis = delta.sqrMagnitude >= 4f;
                    draggingRootShiftGuideHorizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);

                    if (draggingRootShiftGuideHasAxis)
                    {
                        if (draggingRootShiftGuideHorizontal)
                            delta.y = 0f;
                        else
                            delta.x = 0f;
                    }
                }
                else
                {
                    draggingRootShiftGuideVisible = false;
                    draggingRootShiftGuideHasAxis = false;
                }

                if (visualMirrorEnabled)
                    delta.x = -delta.x;
                // Root 偏移存的是父骨骼空间里的局部偏移。
                // 如果父级已经旋转，必须先把屏幕拖动量反旋回父空间，
                // 否则 Root 平移也会变成斜向拉伸。
                localDelta = RotateVector(delta, -draggingBoneStartInheritedAngle) / Mathf.Max(0.0001f, zoom);

                if (state.ShowRigEdit)
                {
                    row.useManualBoneRootOffset = true;
                    row.useManualBoneHeadOffset = true;
                    row.manualBoneRootOffset = draggingBoneStartSetupRootOffset + localDelta;
                    row.manualBoneHeadOffset = draggingBoneStartSetupHeadOffset + localDelta;

                    // Root 拖动写的是“两个端点同量平移”。
                    // BuildRigBoneSegments 会把 HeadEndpoint-RootEndpoint 换算成本地 Head 修正，
                    // 所以这里不会再造成双重叠加，也不会改变角度/长度。
                    row.useManualRigOffset = false;
                    row.manualRigOffset = Vector2.zero;
                }
                else
                {
                    row.useRuntimeBoneRootOffset = true;
                    row.useRuntimeBoneHeadOffset = true;
                    row.runtimeBoneRootOffset = draggingBoneStartRuntimeRootOffset + localDelta;
                    row.runtimeBoneHeadOffset = draggingBoneStartRuntimeHeadOffset + localDelta;
                    // 运行时 Root 拖动也是两个端点同量平移，只移动当前姿态，不写 Setup。
                    row.useManualRigLayerOffset = false;
                    row.manualRigLayerOffset = Vector2.zero;
                }
            }
            else
            {
                // Head 端：Root 固定。
                Vector2 newHeadWorld;
                if (state.ShowRigEdit)
                {
                    // 编辑模式允许重设 Setup 骨长。
                    Vector2 delta = e.mousePosition - draggingManualRigStartMouse;
                    if (visualMirrorEnabled)
                        delta.x = -delta.x;
                    newHeadWorld = draggingBoneStartHeadWorld + delta;
                }
                else
                {
                    // 非编辑模式锁住长度，只调整角度。
                    Vector2 dir = rigMouse - draggingBoneStartRootWorld;
                    if (dir.sqrMagnitude < 0.0001f)
                        dir = draggingBoneStartHeadWorld - draggingBoneStartRootWorld;
                    if (dir.sqrMagnitude < 0.0001f)
                        dir = Vector2.up;
                    newHeadWorld = draggingBoneStartRootWorld + dir.normalized * draggingBoneStartLength;
                }

                // Head 端保存的不是“屏幕世界 delta”，而是父空间里的端点偏移。
                // 手臂分支受 Chest / Shoulder / Elbow 父级旋转影响；如果直接把世界 delta 写进去，
                // 下一帧 BuildRigBoneSegments 会再旋转一次，表现就是非编辑模式锁长度失效、越拖越变长。
                Vector2 desiredVectorInParentSpace = RotateVector(newHeadWorld - draggingBoneStartRootWorld, -draggingBoneStartInheritedAngle);
                Vector2 desiredTotalHeadRelativeOffset = desiredVectorInParentSpace - draggingBoneStartBaseVector;

                if (state.ShowRigEdit)
                {
                    Vector2 setupRootEndpoint = row.useManualBoneRootOffset ? row.manualBoneRootOffset : Vector2.zero;
                    row.useManualBoneHeadOffset = true;
                    row.manualBoneHeadOffset = setupRootEndpoint + desiredTotalHeadRelativeOffset / Mathf.Max(0.0001f, zoom);
                    // Head 端编辑只改变本骨骼线 Head，不污染共享节点锚点。
                    row.useManualRigOffset = false;
                    row.manualRigOffset = Vector2.zero;
                }
                else
                {
                    Vector2 setupRelativeOffset = GetSetupEndpointRelativeOffset(row, zoom);
                    Vector2 runtimeRootEndpoint = row.useRuntimeBoneRootOffset ? row.runtimeBoneRootOffset : Vector2.zero;
                    Vector2 runtimeRelativeOffset;

                    if (IsCustomRigSegment(row.key))
                    {
                        // 自定义骨骼没有“模板 baseVector + setupOffset”两层结构。
                        // 它的 setup 本身就是整条骨骼向量，所以运行时只需要写入 desired - setup。
                        // 旧算法又减了一次 setup，非编辑旋转时就会越转越短/越转越长。
                        runtimeRelativeOffset = desiredTotalHeadRelativeOffset / Mathf.Max(0.0001f, zoom);
                    }
                    else
                    {
                        runtimeRelativeOffset = (desiredTotalHeadRelativeOffset - setupRelativeOffset) / Mathf.Max(0.0001f, zoom);
                    }

                    row.useRuntimeBoneHeadOffset = true;
                    row.runtimeBoneHeadOffset = runtimeRootEndpoint + runtimeRelativeOffset;
                    // 运行时也只写独立端点，不再写旧共享节点偏移，避免双重叠加。
                    row.useManualRigLayerOffset = false;
                    row.manualRigLayerOffset = Vector2.zero;
                }
            }

            if (keyframeOnlyEdit)
            {
                if (state.IsSelectedTimelineKeyframeForRowAtCurrentFrame(row))
                    state.UpdateSelectedTimelineKeyframeFromRow(row);

                // 单帧锁定时，拖拽写入关键帧本身；结构行默认姿态恢复，避免污染两帧之间的插值基准。
                // 如果当前帧没有通过右键显式创建/选中的关键帧，也不会自动创建。
                row.useManualRigLayerOffset = oldUseManualRigLayerOffset;
                row.manualRigLayerOffset = oldManualRigLayerOffset;
                row.useRuntimeBoneRootOffset = oldUseRuntimeBoneRootOffset;
                row.runtimeBoneRootOffset = oldRuntimeBoneRootOffset;
                row.useRuntimeBoneHeadOffset = oldUseRuntimeBoneHeadOffset;
                row.runtimeBoneHeadOffset = oldRuntimeBoneHeadOffset;
            }

            GUI.changed = true;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && !string.IsNullOrEmpty(draggingBoneSegmentKey))
        {
            draggingManualRigKey = string.Empty;
            draggingBoneSegmentKey = string.Empty;
            draggingBoneRootHandle = false;
            draggingBoneStartRootWorld = Vector2.zero;
            draggingBoneStartHeadWorld = Vector2.zero;
            draggingBoneStartLength = 0f;
            draggingBoneStartBaseVector = Vector2.zero;
            draggingBoneStartInheritedAngle = 0f;
            draggingBoneStartSetupRootOffset = Vector2.zero;
            draggingBoneStartSetupHeadOffset = Vector2.zero;
            draggingBoneStartRuntimeRootOffset = Vector2.zero;
            draggingBoneStartRuntimeHeadOffset = Vector2.zero;
            draggingRootShiftGuideVisible = false;
            draggingRootShiftGuideHasAxis = false;
            draggingRootShiftGuideHorizontal = false;
            draggingRootShiftGuideOrigin = Vector2.zero;
            state.PreviewPanelRigDragging = false;
            e.Use();
        }
    }

    private string FindNearestBoneEndpoint(Dictionary<string, RigBoneSegment> segments, Vector2 mouse, float maxDistance, out bool rootHandle)
    {
        rootHandle = false;
        string bestKey = string.Empty;
        float bestScore = maxDistance;
        if (segments == null)
            return bestKey;

        bool preferRoot = Event.current != null && Event.current.alt;
        const float tiePenalty = 0.75f;

        foreach (KeyValuePair<string, RigBoneSegment> kv in segments)
        {
            if (!IsRigSegmentVisible(kv.Value.rootKey, kv.Value.headKey))
                continue;
            SkyPrisonAnimationRigRow row = state.FindRigRow(kv.Key);
            if (row == null || row.locked)
                continue;

            Vector2 rootHandlePoint = GetBoneEndpointHandlePoint(kv.Value, true);
            Vector2 headHandlePoint = GetBoneEndpointHandlePoint(kv.Value, false);
            Vector2 vr = VisualPoint(rootHandlePoint);
            Vector2 vh = VisualPoint(headHandlePoint);
            float dr = Vector2.Distance(vr, mouse);
            float dh = Vector2.Distance(vh, mouse);

            // Root 端已经用独立颜色点显示，并且子骨骼 Root 会轻微侧偏，避免和父骨骼 Head 完全重叠。
            // 所以默认可以直接点尾部彩色点拖 Root；Alt 只作为共享点极近时的保险优先级。
            float rootScore = dr + (preferRoot ? 0f : tiePenalty * 0.25f);
            float headScore = dh + (preferRoot ? tiePenalty : 0f);

            if (rootScore <= bestScore && dr <= maxDistance)
            {
                bestScore = rootScore;
                bestKey = kv.Key;
                rootHandle = true;
            }

            if (headScore <= bestScore && dh <= maxDistance)
            {
                bestScore = headScore;
                bestKey = kv.Key;
                rootHandle = false;
            }
        }
        return bestKey;
    }

    private void CaptureRootMoveSetupStartOffsets()
    {
        draggingRootStartSetupOffsets.Clear();
        draggingRootStartSetupEnabled.Clear();

        if (!draggingRigRootMove || state.RigRows == null)
            return;

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow rig = state.RigRows[i];
            if (rig == null || string.IsNullOrEmpty(rig.key))
                continue;

            draggingRootStartSetupOffsets[rig.key] = rig.useManualRigOffset ? rig.manualRigOffset : Vector2.zero;
            if (rig.useManualRigOffset)
                draggingRootStartSetupEnabled.Add(rig.key);
        }
    }

    private void ApplyRootMoveSetupOffset(Vector2 localDelta)
    {
        if (state.RigRows == null)
            return;

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow rig = state.RigRows[i];
            if (rig == null || string.IsNullOrEmpty(rig.key) || rig.locked)
                continue;

            Vector2 start = Vector2.zero;
            draggingRootStartSetupOffsets.TryGetValue(rig.key, out start);
            rig.useManualRigOffset = true;
            rig.manualRigOffset = start + localDelta;
        }
    }

    private Vector2 VisualToRigPoint(Vector2 point)
    {
        if (!visualMirrorEnabled)
            return point;
        return new Vector2(visualMirrorPivot.x * 2f - point.x, point.y);
    }

    private string GetRigParentKeyForLengthLock(string key)
    {
        SkyPrisonAnimationRigRow row = state.FindRigRow(key);
        if (row != null && !string.IsNullOrEmpty(row.parentKey) && row.parentKey != key)
            return row.parentKey;

        switch (key)
        {
            case "Spine": return "Pelvis";
            case "Chest": return "Spine";
            case "Neck": return "Chest";
            case "Head": return "Neck";
            case "HeadTop": return "Head";
            case "Elbow_L": return "Shoulder_L";
            case "Wrist_L": return "Elbow_L";
            case "HandEnd_L": return "Wrist_L";
            case "Elbow_R": return "Shoulder_R";
            case "Wrist_R": return "Elbow_R";
            case "HandEnd_R": return "Wrist_R";
            case "Knee_L": return "Hip_L";
            case "Ankle_L": return "Knee_L";
            case "Foot_L": return "Ankle_L";
            case "Knee_R": return "Hip_R";
            case "Ankle_R": return "Knee_R";
            case "Foot_R": return "Ankle_R";
            default: return string.Empty;
        }
    }

    private string FindNearestEditableRigKey(Dictionary<string, Vector2> anchors, Vector2 mouse, float maxDistance)
    {
        string bestKey = string.Empty;
        float bestDistance = maxDistance;
        foreach (KeyValuePair<string, Vector2> kv in anchors)
        {
            if (!IsRigRowEffectivelyVisible(kv.Key))
                continue;
            if (kv.Key == "Pelvis" && !state.ShowRigEdit)
                continue;
            SkyPrisonAnimationRigRow row = state.FindRigRow(kv.Key);
            if (row == null || row.locked)
                continue;
            float d = Vector2.Distance(VisualPoint(kv.Value), mouse);
            if (d <= bestDistance)
            {
                bestDistance = d;
                bestKey = kv.Key;
            }
        }
        return bestKey;
    }
    private bool IconButton(Rect rect, int iconNumber, string tooltip)
    {
        Texture2D icon = GetEditorIcon(iconNumber);
        GUIContent content = icon != null ? new GUIContent(icon, tooltip) : new GUIContent(iconNumber.ToString(), tooltip);
        return GUI.Button(rect, content, EditorStyles.miniButton);
    }


    private Vector2 MirrorPoint(Vector2 point, float axisX)
    {
        point.x = axisX - (point.x - axisX);
        return point;
    }

    private void DrawEnterpriseRigOverlay(HumanPose pose, Vector2 center, Rect localView, float zoom)
    {
        // 纯绘制只在 Repaint 阶段执行。Layout/Used 阶段不做旋转矩阵/贴图绘制，
        // 避免 Unity IMGUI 在放大拖拽时重复计算并触发 Repaint 卡死。
        Event e = Event.current;
        if (e != null && e.type != EventType.Repaint)
            return;

        // 这里必须拿未偏移的校准锚点作为骨骼线基准，端点偏移只在 BuildRigBoneSegments 内部应用一次。
        // 否则 Root 端平移会因为旧共享偏移被重复叠加，导致骨段边长和角度一起漂。
        Dictionary<string, Vector2> a = BuildDisplayRigAnchorMap(pose, center, localView, zoom, false, false);
        Dictionary<string, RigBoneSegment> segments = BuildRigBoneSegments(a, zoom, true, !state.ShowRigEdit);

        Texture2D boneTex = GetEditorIcon(100);
        float mainWidth = Mathf.Clamp(8.5f * zoom, 4.5f, 15f);
        float jointRadius = Mathf.Clamp(2.6f * zoom, 1.7f, 4.2f);

        Color spine = new Color(0.62f, 0.95f, 0.25f, 0.88f);
        Color torso = new Color(0.20f, 0.85f, 1.00f, 0.72f);
        Color armL = new Color(1.00f, 0.42f, 0.76f, 0.88f);
        Color armR = new Color(1.00f, 0.62f, 0.24f, 0.88f);
        Color armJoint = new Color(0.20f, 0.90f, 1.00f, 0.86f);

        DrawRigSegment(boneTex, segments, "Pelvis", mainWidth, spine);
        DrawRigSegment(boneTex, segments, "Spine", mainWidth, spine);
        DrawRigSegment(boneTex, segments, "Chest", mainWidth, spine);
        DrawRigSegment(boneTex, segments, "Neck", mainWidth, spine);
        DrawRigSegment(boneTex, segments, "Head", mainWidth, spine);

        float armWidth = Mathf.Clamp(mainWidth * 0.72f, 3.5f, 10f);
        DrawRigSegment(boneTex, segments, "Shoulder_L", armWidth, armL);
        DrawRigSegment(boneTex, segments, "Elbow_L", armWidth, armL);
        DrawRigSegment(boneTex, segments, "Wrist_L", armWidth, armL);
        DrawRigSegment(boneTex, segments, "Shoulder_R", armWidth, armR);
        DrawRigSegment(boneTex, segments, "Elbow_R", armWidth, armR);
        DrawRigSegment(boneTex, segments, "Wrist_R", armWidth, armR);

        float legWidth = Mathf.Clamp(mainWidth * 0.78f, 3.8f, 10.5f);
        Color legL = new Color(0.42f, 0.72f, 1.00f, 0.88f);
        Color legR = new Color(1.00f, 0.80f, 0.28f, 0.88f);
        Color legJoint = new Color(0.30f, 0.92f, 1.00f, 0.86f);
        DrawRigSegment(boneTex, segments, "Hip_L", legWidth, legL);
        DrawRigSegment(boneTex, segments, "Knee_L", legWidth, legL);
        DrawRigSegment(boneTex, segments, "Ankle_L", legWidth, legL);
        DrawRigSegment(boneTex, segments, "Hip_R", legWidth, legR);
        DrawRigSegment(boneTex, segments, "Knee_R", legWidth, legR);
        DrawRigSegment(boneTex, segments, "Ankle_R", legWidth, legR);

        DrawRigSegmentEndpoint(segments, "Pelvis", true, jointRadius + 0.8f, torso, "Pelvis Root / skeleton root");
        DrawRigSegmentEndpoint(segments, "Pelvis", false, jointRadius + 0.8f, torso, "Pelvis Head / Spine joint");
        DrawRigSegmentEndpoint(segments, "Spine", true, jointRadius + 0.8f, torso, "Spine Root / local offset from Pelvis Head");
        DrawRigSegmentEndpoint(segments, "Spine", false, jointRadius, torso, "Spine Head / Chest joint");
        DrawRigSegmentEndpoint(segments, "Chest", true, jointRadius + 0.8f, torso, "Chest Root / local offset from Spine Head");
        DrawRigSegmentEndpoint(segments, "Chest", false, jointRadius, torso, "Chest Head / Neck joint");
        DrawRigSegmentEndpoint(segments, "Neck", true, jointRadius, spine, "Neck Root / local offset from Chest Head");
        DrawRigSegmentEndpoint(segments, "Neck", false, jointRadius + 1f, spine, "Neck Head / Head joint");
        DrawRigSegmentEndpoint(segments, "Head", true, jointRadius + 1f, spine, "Head Root / local offset from Neck Head");
        DrawRigSegmentEndpoint(segments, "Head", false, jointRadius, spine, "Head Head / HeadTop point");

        DrawArmChainEndpoints(segments, "L", jointRadius, armJoint);
        DrawArmChainEndpoints(segments, "R", jointRadius, armJoint);
        DrawLegChainEndpoints(segments, "L", jointRadius, legJoint);
        DrawLegChainEndpoints(segments, "R", jointRadius, legJoint);

        DrawCustomRigSegments(boneTex, segments, Mathf.Clamp(mainWidth * 0.80f, 3.8f, 10.5f), jointRadius);
        DrawCreatingCustomBonePreview(boneTex, Mathf.Clamp(mainWidth * 0.80f, 3.8f, 10.5f), jointRadius);

    }


    private bool IsManualCustomRigMode()
    {
        return state != null &&
               (state.ManualRigTemplateMode ||
                string.Equals(state.CurrentRigTemplateKey, "Custom", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCustomRigSegment(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        SkyPrisonAnimationRigRow row = state.FindRigRow(key);
        return IsCustomRigSegmentRow(row);
    }

    private bool IsCustomRigSegmentRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.isFolder)
            return false;

        if (row.semantic != "CustomBone")
            return false;

        // 兼容两套已经存在的数据：
        // 1) 手绘/拖拽后保存到 manualBoneRootOffset / manualBoneHeadOffset 的自定义骨骼线。
        // 2) 结构面板“新增自定义骨骼”保存到 customBoneRoot / customBoneHead 的自定义骨骼线。
        // 只判断第一套会导致挂到子节点后整条线不参与 segments 构建，最终看不见。
        return (row.useManualBoneRootOffset && row.useManualBoneHeadOffset) || row.useCustomBoneLine;
    }

    private bool IsBuiltInRigSegmentKey(string key)
    {
        switch (key)
        {
            case "Pelvis": case "Spine": case "Chest": case "Neck": case "Head":
            case "Shoulder_L": case "Elbow_L": case "Wrist_L": case "Shoulder_R": case "Elbow_R": case "Wrist_R":
            case "Hip_L": case "Knee_L": case "Ankle_L": case "Hip_R": case "Knee_R": case "Ankle_R":
                return true;
            default: return false;
        }
    }

    private void AddCustomRigBoneSegments(Dictionary<string, RigBoneSegment> segments, Dictionary<string, Vector2> anchors, float zoom, bool includeSetupOffsets, bool includeRuntimeOffsets, Dictionary<string, RigBoneSegment> restSegments)
    {
        if (state.RigRows == null || anchors == null) return;
        Vector2 canvasCenter = anchors.ContainsKey("__PreviewCenter") ? anchors["__PreviewCenter"] : Vector2.zero;
        float z = Mathf.Max(0.0001f, zoom);

        // 自定义骨骼不再只是“结构树里的子节点”。
        // parentKey 指向父骨骼时，自定义线会继承父线从 Setup 到 Current 的平移与旋转。
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;

            for (int i = 0; i < state.RigRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.RigRows[i];
                if (row == null || row.isFolder || string.IsNullOrEmpty(row.key) || IsBuiltInRigSegmentKey(row.key)) continue;
                if (segments.ContainsKey(row.key)) continue;
                if (!IsCustomRigSegmentRow(row)) continue;

                Vector2 setupRoot;
                Vector2 setupHead;
                GetCustomRigSetupEndpoints(row, canvasCenter, z, includeSetupOffsets, out setupRoot, out setupHead);

                Vector2 root = setupRoot;
                Vector2 head = setupHead;

                string parentSegmentKey = ResolveCustomRigParentSegmentKey(row, segments);
                if (!string.IsNullOrEmpty(parentSegmentKey))
                {
                    RigBoneSegment currentParent;
                    if (segments.TryGetValue(parentSegmentKey, out currentParent))
                    {
                        RigBoneSegment restParent = currentParent;
                        if (restSegments != null)
                            restSegments.TryGetValue(parentSegmentKey, out restParent);

                        root = TransformPointByParentSegment(setupRoot, restParent, currentParent);
                        head = TransformPointByParentSegment(setupHead, restParent, currentParent);
                    }
                }

                if (includeRuntimeOffsets)
                {
                    if (row.useRuntimeBoneRootOffset) root += row.runtimeBoneRootOffset * z;
                    if (row.useRuntimeBoneHeadOffset) head += row.runtimeBoneHeadOffset * z;
                }

                segments[row.key] = new RigBoneSegment { segmentKey = row.key, rootKey = row.key, headKey = row.key, root = root, head = head };
                changed = true;
            }

            if (!changed)
                break;
        }
    }

    private void GetCustomRigSetupEndpoints(SkyPrisonAnimationRigRow row, Vector2 canvasCenter, float zoom, bool includeSetupOffsets, out Vector2 setupRoot, out Vector2 setupHead)
    {
        setupRoot = canvasCenter;
        setupHead = canvasCenter;

        if (row == null)
            return;

        if (!includeSetupOffsets)
            return;

        if (row.useManualBoneRootOffset && row.useManualBoneHeadOffset)
        {
            setupRoot += row.manualBoneRootOffset * zoom;
            setupHead += row.manualBoneHeadOffset * zoom;
            return;
        }

        if (row.useCustomBoneLine)
        {
            setupRoot += row.customBoneRoot * zoom;
            setupHead += row.customBoneHead * zoom;
        }
    }

    private string ResolveCustomRigParentSegmentKey(SkyPrisonAnimationRigRow row, Dictionary<string, RigBoneSegment> segments)
    {
        if (row == null || segments == null)
            return string.Empty;

        string key = row.parentKey;
        HashSet<string> guard = new HashSet<string>();

        while (!string.IsNullOrEmpty(key) && key != row.key && guard.Add(key))
        {
            if (segments.ContainsKey(key))
                return key;

            string incomingSegment = GetIncomingRigSegmentKeyForEndpoint(key);
            if (!string.IsNullOrEmpty(incomingSegment) && segments.ContainsKey(incomingSegment))
                return incomingSegment;

            SkyPrisonAnimationRigRow parentRow = state != null ? state.FindRigRow(key) : null;
            if (parentRow == null)
                break;

            if (!string.IsNullOrEmpty(parentRow.boundRigKey) && segments.ContainsKey(parentRow.boundRigKey))
                return parentRow.boundRigKey;

            incomingSegment = GetIncomingRigSegmentKeyForEndpoint(parentRow.boundRigKey);
            if (!string.IsNullOrEmpty(incomingSegment) && segments.ContainsKey(incomingSegment))
                return incomingSegment;

            key = parentRow.parentKey;
        }

        return string.Empty;
    }

    private string GetIncomingRigSegmentKeyForEndpoint(string endpointKey)
    {
        switch (endpointKey)
        {
            case "Spine": return "Pelvis";
            case "Chest": return "Spine";
            case "Neck": return "Chest";
            case "Head": return "Neck";
            case "HeadTop": return "Head";

            case "Elbow_L": return "Shoulder_L";
            case "Wrist_L": return "Elbow_L";
            case "HandEnd_L": return "Wrist_L";
            case "Elbow_R": return "Shoulder_R";
            case "Wrist_R": return "Elbow_R";
            case "HandEnd_R": return "Wrist_R";

            case "Knee_L": return "Hip_L";
            case "Ankle_L": return "Knee_L";
            case "Foot_L": return "Ankle_L";
            case "Knee_R": return "Hip_R";
            case "Ankle_R": return "Knee_R";
            case "Foot_R": return "Ankle_R";
            default: return string.Empty;
        }
    }

    private Vector2 TransformPointByParentSegment(Vector2 setupPoint, RigBoneSegment restParent, RigBoneSegment currentParent)
    {
        Vector2 restVector = restParent.head - restParent.root;
        Vector2 currentVector = currentParent.head - currentParent.root;

        float inheritedAngle = 0f;
        if (restVector.sqrMagnitude > 0.0001f && currentVector.sqrMagnitude > 0.0001f)
            inheritedAngle = Vector2.SignedAngle(restVector, currentVector);

        return currentParent.root + RotateVector(setupPoint - restParent.root, inheritedAngle);
    }

    private void DrawCustomRigSegments(Texture2D boneTex, Dictionary<string, RigBoneSegment> segments, float width, float jointRadius)
    {
        if (segments == null || state.RigRows == null) return;

        Color baseLine = new Color(0.92f, 0.70f, 1.00f, 0.88f);
        Color baseRoot = new Color(0.18f, 0.88f, 1.00f, 0.96f);
        Color baseHead = new Color(0.74f, 1.00f, 0.22f, 0.96f);

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !segments.ContainsKey(row.key) || !IsCustomRigSegment(row.key)) continue;

            RigBoneSegment seg = segments[row.key];

            // 自定义骨骼线也必须遵守和默认骨骼线完全一致的动画模式聚焦规则。
            // 之前这里直接使用固定颜色绘制，绕过了 ApplyPreviewFocusColor，
            // 所以自定义节点被拖成父/子后，即使不是当前轨道，也不会被透明淡化。
            // 编辑模式下 IsPreviewFocusModeActive() 为 false，仍然会全部实色显示、全部可操作。
            bool focused = IsPreviewFocusSegment(seg);
            Color line = ApplyPreviewFocusColor(baseLine, focused);
            Color root = ApplyPreviewFocusColor(baseRoot, focused);
            Color head = ApplyPreviewFocusColor(baseHead, focused);

            DrawBoneIconSegment(boneTex, seg.root, seg.head, width, line);
            DrawBoneJoint(seg.root, jointRadius + 0.6f, root);
            DrawBoneJoint(seg.head, jointRadius + 0.6f, head);
        }
    }

    private void DrawCreatingCustomBonePreview(Texture2D boneTex, float width, float jointRadius)
    {
        if (!creatingCustomBoneLine) return;
        DrawBoneIconSegment(boneTex, creatingCustomBoneRootLocal, creatingCustomBoneHeadLocal, width, new Color(0.92f, 0.70f, 1f, 0.58f));
        DrawBoneJoint(creatingCustomBoneRootLocal, jointRadius + 0.6f, new Color(0.18f, 0.88f, 1f, 0.96f));
        DrawBoneJoint(creatingCustomBoneHeadLocal, jointRadius + 0.6f, new Color(0.74f, 1f, 0.22f, 0.96f));
    }

    private void CommitCustomRigBoneFromPreview(float zoom)
    {
        if (Vector2.Distance(creatingCustomBoneRootLocal, creatingCustomBoneHeadLocal) < 6f) return;
        state.PushRigUndo();
        string key = GenerateCustomRigKey();
        int index = state.RigRows != null ? state.RigRows.Count : 0;
        float z = Mathf.Max(0.0001f, zoom);
        Vector2 canvasCenter = Vector2.zero;
        if (currentPreviewClipRect.width > 1f || currentPreviewClipRect.height > 1f)
            canvasCenter = GetPreviewCanvasOrigin(currentPreviewClipRect) + state.PreviewPan;
        SkyPrisonAnimationRigRow row = new SkyPrisonAnimationRigRow
        {
            key = key,
            name = "自定义骨骼_" + (index + 1),
            semantic = "CustomBone",
            depth = 0,
            parentKey = "",
            isFolder = false,
            expanded = true,
            hasKey = true,
            mapped = true,
            previewIconNumber = 100,
            previewColor = new Color(0.92f, 0.70f, 1f, 1f),
            useManualBoneRootOffset = true,
            useManualBoneHeadOffset = true,
            manualBoneRootOffset = (creatingCustomBoneRootLocal - canvasCenter) / z,
            manualBoneHeadOffset = (creatingCustomBoneHeadLocal - canvasCenter) / z
        };
        state.RigRows.Add(row);
        state.SelectedRig = state.RigRows.Count - 1;
        state.LastSelectedRigKey = row.key;
        state.StructureTab = SkyPrisonAnimationStructureTab.Rig;
    }

    private string GenerateCustomRigKey()
    {
        int n = 1;
        while (state.FindRigRow("CustomBone_" + n) != null) n++;
        return "CustomBone_" + n;
    }

    private void HandleCurrentFramePsbLayerSelection(Rect view)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0)
            return;

        if (state.ShowRigEdit)
            return;

        if (!view.Contains(e.mousePosition))
            return;

        if (GetRigEditButtonRect(view).Contains(e.mousePosition))
            return;

        if (GetPreviewToggleBarRect(view).Contains(e.mousePosition) ||
            GetPreviewZoomPanelRect(view).Contains(e.mousePosition))
            return;

        Vector2 localMouse = e.mousePosition - view.position;

        if (TrySelectPsbLayerAtPreviewPoint(localMouse))
        {
            state.PreviewPanelHasKeyboardFocus = true;
            GUI.FocusControl(null);
            e.Use();
            return;
        }

        if (state.ShowVisualParts && state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer && state.HasSelectedPsbLayer())
        {
            state.ClearCurrentStructureSelection(true);
            state.PreviewPanelHasKeyboardFocus = true;
            GUI.FocusControl(null);
            GUI.changed = true;
            e.Use();
        }
    }

    private bool TrySelectPsbLayerAtPreviewPoint(Vector2 localMouse)
    {
        if (!state.ShowVisualParts)
            return false;

        if (lastPsbPreviewRects == null || lastPsbPreviewRects.Count == 0 || state.PsbRows == null)
            return false;

        string bestKey = string.Empty;
        float bestScore = float.MaxValue;

        // 使用“当前帧”的显示矩形拾取，并让热区跟 PreviewZoom 一起变化。
        // 这样缩放 / 平移后命中区和画面比例一致，不再出现看起来点中了但实际还在上一帧位置的问题。
        for (int i = lastPsbPreviewPickOrder.Count - 1; i >= 0; i--)
        {
            string key = lastPsbPreviewPickOrder[i];
            if (string.IsNullOrEmpty(key)) continue;

            Rect r;
            if (!lastPsbPreviewRects.TryGetValue(key, out r)) continue;
            if (r.width <= 0.01f || r.height <= 0.01f) continue;

            Rect hotRect = ExpandRect(r, GetPsbLayerPickPadding(r));
            if (!hotRect.Contains(localMouse)) continue;

            float area = Mathf.Max(1f, r.width * r.height);
            float centerDistance = Vector2.Distance(localMouse, r.center);

            // 仍然偏向小图层，但不要让极小热区在很远处误抢大图层。
            // i 是反向绘制顺序，越靠前越接近视觉上层。
            float drawOrderBias = (lastPsbPreviewPickOrder.Count - 1 - i) * 0.001f;
            float score = area * 0.0008f + centerDistance + drawOrderBias;

            if (string.IsNullOrEmpty(bestKey) || score < bestScore)
            {
                bestKey = key;
                bestScore = score;
            }
        }

        if (string.IsNullOrEmpty(bestKey))
            return false;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row != null && row.key == bestKey)
            {
                state.SelectedRig = i;
                state.LastSelectedPsbLayerKey = bestKey;
                state.StructureTab = SkyPrisonAnimationStructureTab.PsbLayer;

                // 点击 PSB 图像时，它仍然只是“图像选择对象”，不能把 PSB 行塞进时间轴。
                // 但是为了让用户知道这个图层由哪条骨骼线驱动，这里把预览聚焦 / 轨道锁定
                // 切到它绑定的 Rig 骨骼线。这样画面里亮的是可编辑骨骼线，而不是 PSB 轨道。
                if (!string.IsNullOrEmpty(row.boundRigKey) && state.FindRigRow(row.boundRigKey) != null)
                {
                    // 点击 PSB 图层：图层本身不进入时间轴，但自动锁定到它绑定 Rig 在当前白线帧的关键帧。
                    // 若当前帧没有关键帧，只锁轨道，不自动生成。
                    state.LockCurrentFrameKeyframeForRigTarget(row.boundRigKey, false, false, row.key);
                }

                GUI.changed = true;
                return true;
            }
        }
        return false;
    }

    private float GetPsbLayerPickPadding(Rect rect)
    {
        float shortest = Mathf.Min(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        float zoom = Mathf.Clamp(state.PreviewZoom, 0.1f, 5f);

        // 屏幕热区和画面缩放保持同步：
        // - 缩小时，细碎图层不会缩成几乎点不到；
        // - 放大时，热区也不会大到误选旁边图层。
        float basePadding;
        if (shortest < 14f) basePadding = 18f;
        else if (shortest < 28f) basePadding = 14f;
        else basePadding = 9f;

        float zoomCompensation = Mathf.Lerp(1.45f, 0.75f, Mathf.InverseLerp(0.35f, 2.5f, zoom));
        return Mathf.Clamp(basePadding * zoomCompensation, 6f, 28f);
    }

    private void DrawSelectedMeshDeformerGrid(Dictionary<SkyPrisonAnimationRigRow, Rect> finalRects, Dictionary<SkyPrisonAnimationRigRow, PsbSpriteDrawState> drawStates)
    {
        if (state == null || finalRects == null || finalRects.Count == 0)
            return;

        // Rig 编辑模式下只显示 Rest Pose / 骨架 Setup，不显示也不编辑曲面变形控制网格。
        // 曲面变形属于动作/关键帧侧的姿势编辑，避免和基础骨架编辑混在一起。
        if (ShouldSuppressMeshDeformerPreviewEffects())
        {
            ClearMeshDeformDraggingIfNeeded();
            return;
        }

        SkyPrisonAnimationRigRow deformer = GetActiveMeshDeformerForPreview();
        if (deformer == null || !deformer.isMeshDeformer)
        {
            ClearMeshDeformDraggingIfNeeded();
            return;
        }

        string targetKey = deformer.meshDeformTargetKey;
        if (string.IsNullOrEmpty(targetKey))
            return;

        int columns = Mathf.Clamp(deformer.meshDeformColumns, 2, 16);
        int rows = Mathf.Clamp(deformer.meshDeformRows, 2, 16);
        EnsureMeshDeformerPreviewPointGrid(deformer, columns, rows);
        EnsureMeshAnchorSelectionForDeformer(deformer);

        Color fillColor = new Color(0.00f, 0.55f, 0.12f, 0.035f);
        Color lineColor = new Color(0.00f, 0.78f, 0.10f, 0.95f);
        Color softLineColor = new Color(0.00f, 0.78f, 0.10f, 0.30f);
        Color borderColor = new Color(0.15f, 1.00f, 0.18f, 0.95f);

        // 主控制点 / 方向柄 / 热点三套颜色拆开：控制点偏蓝青，方向臂继续保持绿色，避免多选时误点。
        Color pointColor = new Color(0.10f, 0.72f, 1.00f, 1f);
        Color handleColor = new Color(0.20f, 1.00f, 0.32f, 0.96f);
        Color hotPointColor = new Color(1f, 0.92f, 0.18f, 1f);

        bool drewAny = false;

        BeginMeshDeformerPreviewPointCache(deformer, columns, rows);

        foreach (KeyValuePair<SkyPrisonAnimationRigRow, Rect> kv in finalRects)
        {
            SkyPrisonAnimationRigRow psb = kv.Key;
            if (!IsMeshDeformerPreviewAffectedPsb(psb, targetKey))
                continue;

            Rect r = kv.Value;
            if (r.width <= 2f || r.height <= 2f)
                continue;

            PsbSpriteDrawState psbDrawState = null;
            if (drawStates != null)
                drawStates.TryGetValue(psb, out psbDrawState);

            currentMeshDeformerScreenFrame = psbDrawState != null
                ? BuildMeshDeformerScreenFrame(psbDrawState)
                : BuildMeshDeformerScreenFrame(r);
            currentMeshDeformerScreenFrameValid = currentMeshDeformerScreenFrame.valid;

            drewAny = true;

            Vector2[,] points = BuildMeshDeformerPreviewPoints(deformer, r, columns, rows);

            // Shift + 方向键：微调当前曲面红框位置。
            // 这属于曲面关键帧编辑，和鼠标拖动红框一样，会写入当前帧 MeshDeformer Key。
            bool keyboardNudged = HandleMeshDeformerOuterFrameKeyboardNudge(deformer, r, columns, rows);
            if (keyboardNudged)
                points = BuildMeshDeformerPreviewPoints(deformer, r, columns, rows);

            bool outerFrameCaptured = false;
            if (!keyboardNudged && IsMeshOuterFrameVisibleFor(deformer))
                outerFrameCaptured = HandleMeshDeformerOuterFrameInput(deformer, r, points, columns, rows);
            if (!keyboardNudged && !outerFrameCaptured)
            {
                HandleMeshDeformerPointInput(deformer, r, points, columns, rows);
                AddMeshDeformerSurfaceMoveCursor(deformer, points, columns, rows);
            }
            else if (outerFrameCaptured)
            {
                points = BuildMeshDeformerPreviewPoints(deformer, r, columns, rows);
            }

            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, fillColor);

                Handles.BeginGUI();

            // 曲面边不再用直线硬连，而用 Bezier 方向柄控制。
            // 这样拖角点旁边的方向柄时，只改变边缘切线，中间主控制点仍能保持原本水平/垂直关系。
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns - 1; x++)
                {
                    Vector2 a = points[x, y];
                    Vector2 b = points[x + 1, y];
                    Vector2 c1 = GetMeshDeformerHandleScreenPoint(deformer, points, x, y, "right");
                    Vector2 c2 = GetMeshDeformerHandleScreenPoint(deformer, points, x + 1, y, "left");
                    Handles.DrawBezier(a, b, c1, c2, lineColor, null, 1.35f);
                }
            }

            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows - 1; y++)
                {
                    Vector2 a = points[x, y];
                    Vector2 b = points[x, y + 1];
                    Vector2 c1 = GetMeshDeformerHandleScreenPoint(deformer, points, x, y, "down");
                    Vector2 c2 = GetMeshDeformerHandleScreenPoint(deformer, points, x, y + 1, "up");
                    Handles.DrawBezier(a, b, c1, c2, lineColor, null, 1.35f);
                }
            }

            // 外框加粗，依然走 Bezier，避免边缘被方向柄拉弯后外框还显示成硬直线。
            for (int x = 0; x < columns - 1; x++)
            {
                Handles.DrawBezier(points[x, 0], points[x + 1, 0], GetMeshDeformerHandleScreenPoint(deformer, points, x, 0, "right"), GetMeshDeformerHandleScreenPoint(deformer, points, x + 1, 0, "left"), borderColor, null, 2.0f);
                Handles.DrawBezier(points[x, rows - 1], points[x + 1, rows - 1], GetMeshDeformerHandleScreenPoint(deformer, points, x, rows - 1, "right"), GetMeshDeformerHandleScreenPoint(deformer, points, x + 1, rows - 1, "left"), borderColor, null, 2.0f);
            }
            for (int y = 0; y < rows - 1; y++)
            {
                Handles.DrawBezier(points[0, y], points[0, y + 1], GetMeshDeformerHandleScreenPoint(deformer, points, 0, y, "down"), GetMeshDeformerHandleScreenPoint(deformer, points, 0, y + 1, "up"), borderColor, null, 2.0f);
                Handles.DrawBezier(points[columns - 1, y], points[columns - 1, y + 1], GetMeshDeformerHandleScreenPoint(deformer, points, columns - 1, y, "down"), GetMeshDeformerHandleScreenPoint(deformer, points, columns - 1, y + 1, "up"), borderColor, null, 2.0f);
            }

            if (IsMeshOuterFrameVisibleFor(deformer))
                DrawMeshDeformerOuterFrame(deformer, points, columns, rows);

            // 方向柄关系线：每个主控制点只和自己的方向柄用实线连接。
            // 方向柄之间不互相连线，避免误读成三角网或额外控制边。
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    Vector2 anchor = points[x, y];
                    DrawMeshDeformerHandleArm(deformer, points, x, y, "left", anchor);
                    DrawMeshDeformerHandleArm(deformer, points, x, y, "right", anchor);
                    DrawMeshDeformerHandleArm(deformer, points, x, y, "up", anchor);
                    DrawMeshDeformerHandleArm(deformer, points, x, y, "down", anchor);
                }
            }
            Handles.EndGUI();

            Event e = Event.current;
            Vector2 mouse = e != null ? e.mousePosition : Vector2.zero;

            // 先画方向柄小点，再画主点。方向柄点更小，主点更重。
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    DrawMeshDeformerHandleDot(deformer, points, x, y, "left", mouse, handleColor, hotPointColor);
                    DrawMeshDeformerHandleDot(deformer, points, x, y, "right", mouse, handleColor, hotPointColor);
                    DrawMeshDeformerHandleDot(deformer, points, x, y, "up", mouse, handleColor, hotPointColor);
                    DrawMeshDeformerHandleDot(deformer, points, x, y, "down", mouse, handleColor, hotPointColor);
                }
            }

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    Vector2 p = points[x, y];
                    float hitSize = 9f;
                    Rect hit = new Rect(p.x - hitSize * 0.5f, p.y - hitSize * 0.5f, hitSize, hitSize);
                    bool active = draggingMeshDeformerKey == deformer.key && draggingMeshHandleKind == "anchor" && draggingMeshPointX == x && draggingMeshPointY == y;
                    bool hover = hit.Contains(mouse);
                    bool selectedAnchor = IsMeshAnchorSelected(deformer, x, y);
                    Color c = selectedAnchor ? new Color(1f, 0.95f, 0.20f, 1f) : (active || hover ? hotPointColor : pointColor);

                    EditorGUI.DrawRect(new Rect(p.x - 4.5f, p.y - 4.5f, 9f, 9f), selectedAnchor ? new Color(1f, 0.05f, 0.03f, 0.85f) : new Color(0f, 0f, 0f, 0.55f));
                    EditorGUI.DrawRect(new Rect(p.x - 3.2f, p.y - 3.2f, 6.4f, 6.4f), c);
                }
            }
            }
        }

        EndMeshDeformerPreviewPointCache();
        currentMeshDeformerScreenFrameValid = false;

        // Shift 轴向锁定提示线和角标只在 Repaint 阶段绘制。
        if (Event.current == null || Event.current.type == EventType.Repaint)
        {
            DrawMeshDeformerShiftAxisGuide();
            if (drewAny)
                DrawMeshDeformerPreviewBadge(deformer, columns, rows);
        }

        HandleMeshDeformerGlobalBlankClick(deformer, drewAny);
    }

    private void DrawMeshDeformerShiftAxisGuide()
    {
        Event e = Event.current;
        if (e == null || !e.shift)
            return;

        if (!draggingMeshPointActive && !draggingMeshSurfaceActive && !draggingMeshOuterActive)
            return;

        Vector2 origin;
        if (draggingMeshPointActive)
            origin = draggingMeshStartMouse;
        else if (draggingMeshSurfaceActive)
            origin = draggingMeshSurfaceStartMouse;
        else
            origin = draggingMeshOuterStartMouse;

        Vector2 delta = e.mousePosition - origin;

        Rect localView = currentPreviewClipRect;
        if (localView.width <= 1f || localView.height <= 1f)
            localView = new Rect(0f, 0f, Screen.width, Screen.height);

        Color active = new Color(0.18f, 0.62f, 1f, 0.95f);
        Color inactive = new Color(0.18f, 0.62f, 1f, 0.30f);

        // 刚按下 Shift、尚未形成明显方向时，显示十字参考线；移动超过阈值后只保留主轴。
        if (delta.sqrMagnitude <= 16f)
        {
            DrawHorizontalDashedLine(origin.y, localView.xMin, localView.xMax, inactive, 1.5f, 9f, 5f);
            DrawVerticalDashedLine(origin.x, localView.yMin, localView.yMax, inactive, 1.5f, 9f, 5f);
            return;
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            DrawHorizontalDashedLine(origin.y, localView.xMin, localView.xMax, active, 2f, 10f, 5f);
        else
            DrawVerticalDashedLine(origin.x, localView.yMin, localView.yMax, active, 2f, 10f, 5f);
    }

    private void BeginMeshDeformerPreviewPointCache(SkyPrisonAnimationRigRow deformer, int columns, int rows)
    {
        meshPreviewPointCacheDeformerKey = deformer != null ? deformer.key : string.Empty;
        meshPreviewPointCacheColumns = Mathf.Clamp(columns, 2, 16);
        meshPreviewPointCacheRows = Mathf.Clamp(rows, 2, 16);

        // 正在拖拽时直接读 row.meshDeformPoints，不能读时间线插值缓存，否则拖动会被旧帧拉回。
        if (deformer == null || state == null || IsMeshDeformerLiveEditing(deformer))
        {
            meshPreviewPointCache = null;
            return;
        }

        if (drawingOnionSkinSnapshot)
        {
            if (!TryEvaluateTimelineMeshDeformPointsSnapshot(deformer, meshPreviewPointCacheColumns, meshPreviewPointCacheRows, out meshPreviewPointCache))
                meshPreviewPointCache = BuildDefaultMeshDeformerPointGrid(meshPreviewPointCacheColumns, meshPreviewPointCacheRows);
            return;
        }

        meshPreviewPointCache = state.EvaluateTimelineMeshDeformPoints(deformer, meshPreviewPointCacheColumns, meshPreviewPointCacheRows);
    }

    private void EndMeshDeformerPreviewPointCache()
    {
        meshPreviewPointCacheDeformerKey = string.Empty;
        meshPreviewPointCacheColumns = 0;
        meshPreviewPointCacheRows = 0;
        meshPreviewPointCache = null;
    }

    private bool IsMeshDeformerPreviewPointCacheValid(SkyPrisonAnimationRigRow deformer, int columns, int rows)
    {
        return deformer != null
            && !string.IsNullOrEmpty(meshPreviewPointCacheDeformerKey)
            && meshPreviewPointCacheDeformerKey == deformer.key
            && meshPreviewPointCacheColumns == Mathf.Clamp(columns, 2, 16)
            && meshPreviewPointCacheRows == Mathf.Clamp(rows, 2, 16);
    }

    private void ClearMeshDeformDraggingIfNeeded()
    {
        if (!draggingMeshPointActive && !draggingMeshSurfaceActive)
            return;

        draggingMeshDeformerKey = string.Empty;
        draggingMeshPointX = -1;
        draggingMeshPointY = -1;
        draggingMeshHandleKind = "anchor";
        draggingMeshPointActive = false;
        draggingMeshSurfaceActive = false;
        draggingMeshSelectedAnchorStartOffsets.Clear();
        draggingMeshSurfaceStartAnchorOffsets.Clear();
        meshDeformerLiveEditingDirty = false;
    }

    private void EnsureMeshDeformerPreviewPointGrid(SkyPrisonAnimationRigRow deformer, int columns, int rows)
    {
        if (deformer == null)
            return;

        deformer.meshDeformColumns = Mathf.Clamp(columns, 2, 16);
        deformer.meshDeformRows = Mathf.Clamp(rows, 2, 16);
        if (deformer.meshDeformPoints == null)
            deformer.meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();

        Dictionary<string, SkyPrisonMeshDeformPoint> old = new Dictionary<string, SkyPrisonMeshDeformPoint>();
        for (int i = 0; i < deformer.meshDeformPoints.Count; i++)
        {
            SkyPrisonMeshDeformPoint p = deformer.meshDeformPoints[i];
            if (p == null) continue;
            old[p.x + "_" + p.y] = p;
        }

        deformer.meshDeformPoints.Clear();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                string key = x + "_" + y;
                if (old.TryGetValue(key, out SkyPrisonMeshDeformPoint existing) && existing != null)
                {
                    existing.x = x;
                    existing.y = y;
                    deformer.meshDeformPoints.Add(existing);
                }
                else
                {
                    deformer.meshDeformPoints.Add(new SkyPrisonMeshDeformPoint { x = x, y = y, offset = Vector2.zero });
                }
            }
        }
    }

    private Vector2[,] BuildMeshDeformerPreviewPoints(SkyPrisonAnimationRigRow deformer, Rect rect, int columns, int rows)
    {
        Vector2[,] result = new Vector2[columns, rows];
        MeshDeformerScreenFrame frame = currentMeshDeformerScreenFrameValid
            ? currentMeshDeformerScreenFrame
            : BuildMeshDeformerScreenFrame(rect);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                SkyPrisonMeshDeformPoint p = FindMeshDeformerPointForPreview(deformer, x, y, columns, rows);
                Vector2 offset = p != null ? p.offset : Vector2.zero;
                Vector2 basePoint = GetBaseMeshPointScreen(frame, columns, rows, x, y);
                result[x, y] = ApplyMeshLocalOffsetToScreen(frame, basePoint, offset);
            }
        }
        return result;
    }

    private MeshDeformerScreenFrame BuildMeshDeformerScreenFrame(Rect rect)
    {
        float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
        return new MeshDeformerScreenFrame
        {
            valid = rect.width > 0.0001f && rect.height > 0.0001f,
            center = rect.center,
            right = Vector2.right,
            down = Vector2.down,
            width = Mathf.Max(1f, rect.width),
            height = Mathf.Max(1f, rect.height),
            zoom = zoom
        };
    }

    private MeshDeformerScreenFrame BuildMeshDeformerScreenFrame(PsbSpriteDrawState drawState)
    {
        float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
        if (drawState == null)
            return new MeshDeformerScreenFrame { valid = false, right = Vector2.right, down = Vector2.down, zoom = zoom };

        float angle = (visualMirrorEnabled ? -drawState.angle : drawState.angle) * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 down = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
        if (right.sqrMagnitude <= 0.0001f) right = Vector2.right;
        if (down.sqrMagnitude <= 0.0001f) down = Vector2.down;
        right.Normalize();
        down.Normalize();

        return new MeshDeformerScreenFrame
        {
            valid = drawState.size.x > 0.0001f && drawState.size.y > 0.0001f,
            center = VisualPoint(drawState.center),
            right = right,
            down = down,
            width = Mathf.Max(1f, drawState.size.x),
            height = Mathf.Max(1f, drawState.size.y),
            zoom = zoom
        };
    }

    private Vector2 GetBaseMeshPointScreen(MeshDeformerScreenFrame frame, int columns, int rows, int x, int y)
    {
        float tx = columns <= 1 ? 0f : (float)x / (columns - 1);
        float ty = rows <= 1 ? 0f : (float)y / (rows - 1);
        float localX = Mathf.Lerp(-frame.width * 0.5f, frame.width * 0.5f, tx);
        float localY = Mathf.Lerp(-frame.height * 0.5f, frame.height * 0.5f, ty);
        return frame.center + frame.right * localX + frame.down * localY;
    }

    private Vector2 ApplyMeshLocalOffsetToScreen(MeshDeformerScreenFrame frame, Vector2 basePoint, Vector2 localOffset)
    {
        return basePoint + frame.right * (localOffset.x * frame.zoom) + frame.down * (localOffset.y * frame.zoom);
    }

    private Vector2 MeshScreenVectorToLocalOffset(MeshDeformerScreenFrame frame, Vector2 screenVector)
    {
        float safeZoom = Mathf.Max(0.0001f, frame.zoom);
        return new Vector2(Vector2.Dot(screenVector, frame.right), Vector2.Dot(screenVector, frame.down)) / safeZoom;
    }

    private Vector2 MeshScreenPointToLocalOffset(MeshDeformerScreenFrame frame, Vector2 basePoint, Vector2 screenPoint)
    {
        return MeshScreenVectorToLocalOffset(frame, screenPoint - basePoint);
    }

    private SkyPrisonMeshDeformPoint FindMeshDeformerPoint(SkyPrisonAnimationRigRow deformer, int x, int y)
    {
        if (deformer == null || deformer.meshDeformPoints == null)
            return null;

        // EnsureMeshDeformerPreviewPointGrid 会按 y * columns + x 排列。
        // 先走 O(1) 命中，失败再 fallback 扫描，兼容旧数据或异常顺序。
        int columns = Mathf.Clamp(deformer.meshDeformColumns, 2, 16);
        int directIndex = y * columns + x;
        if (directIndex >= 0 && directIndex < deformer.meshDeformPoints.Count)
        {
            SkyPrisonMeshDeformPoint direct = deformer.meshDeformPoints[directIndex];
            if (direct != null && direct.x == x && direct.y == y)
                return direct;
        }

        for (int i = 0; i < deformer.meshDeformPoints.Count; i++)
        {
            SkyPrisonMeshDeformPoint p = deformer.meshDeformPoints[i];
            if (p != null && p.x == x && p.y == y)
                return p;
        }
        return null;
    }

    private bool IsMeshDeformerLiveEditing(SkyPrisonAnimationRigRow deformer)
    {
        if (deformer == null || drawingOnionSkinSnapshot)
            return false;

        return (draggingMeshPointActive || draggingMeshSurfaceActive || draggingMeshOuterActive) && draggingMeshDeformerKey == deformer.key;
    }

    private void MarkMeshDeformerLivePreviewChanged(bool force = false)
    {
        double now = EditorApplication.timeSinceStartup;
        if (!force && now - lastMeshLivePreviewRepaintTime < MeshLivePreviewRepaintInterval)
            return;

        lastMeshLivePreviewRepaintTime = now;
        GUI.changed = true;
    }

    private SkyPrisonMeshDeformPoint FindMeshDeformerPointForPreview(SkyPrisonAnimationRigRow deformer, int x, int y, int columns, int rows)
    {
        if (deformer == null)
            return null;

        // 正在拖拽时必须优先读取正在被鼠标修改的 row 数据。
        // 否则每次 Repaint 都会去时间线插值并克隆点位，拖多选点时会明显卡顿，
        // 甚至视觉上会被旧关键帧结果拉回。
        if (IsMeshDeformerLiveEditing(deformer))
            return FindMeshDeformerPoint(deformer, x, y);

        List<SkyPrisonMeshDeformPoint> evaluated = null;
        int safeColumns = Mathf.Clamp(columns, 2, 16);
        int safeRows = Mathf.Clamp(rows, 2, 16);

        if (IsMeshDeformerPreviewPointCacheValid(deformer, safeColumns, safeRows))
            evaluated = meshPreviewPointCache;
        else if (drawingOnionSkinSnapshot)
        {
            if (!TryEvaluateTimelineMeshDeformPointsSnapshot(deformer, safeColumns, safeRows, out evaluated))
                evaluated = BuildDefaultMeshDeformerPointGrid(safeColumns, safeRows);
        }
        else if (state != null)
            evaluated = state.EvaluateTimelineMeshDeformPoints(deformer, safeColumns, safeRows);

        SkyPrisonMeshDeformPoint p = FindMeshDeformerPointInList(evaluated, x, y, safeColumns);
        if (p != null)
            return p;

        if (drawingOnionSkinSnapshot)
            return new SkyPrisonMeshDeformPoint { x = x, y = y, offset = Vector2.zero };

        return FindMeshDeformerPoint(deformer, x, y);
    }

    private bool TryEvaluateTimelineMeshDeformPointsSnapshot(SkyPrisonAnimationRigRow deformer, int columns, int rows, out List<SkyPrisonMeshDeformPoint> points)
    {
        points = null;
        if (state == null || deformer == null || string.IsNullOrEmpty(deformer.key) || state.TimelineKeyframes == null)
            return false;

        string actionKey = state.CurrentActionKey();
        float frameFloat = state.TimelineCurrentFrameFloat;
        int frame = state.SnapFrame(state.TimelineCurrentFrame);

        SkyPrisonAnimationTimelineKeyframe exact = null;
        SkyPrisonAnimationTimelineKeyframe prev = null;
        SkyPrisonAnimationTimelineKeyframe next = null;

        for (int i = 0; i < state.TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
            if (k == null)
                continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(k.targetKey, deformer.key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!k.useMeshDeform || k.meshDeformPoints == null || k.meshDeformPoints.Count == 0)
                continue;

            int kFrame = state.SnapFrame(k.frame);
            if (kFrame == frame)
            {
                exact = k;
                break;
            }
            if (kFrame < frameFloat && (prev == null || kFrame > prev.frame))
                prev = k;
            if (kFrame > frameFloat && (next == null || kFrame < next.frame))
                next = k;
        }

        if (exact != null)
        {
            points = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(exact.meshDeformPoints);
            return true;
        }

        if (prev == null && next == null)
            return false;
        if (prev == null)
        {
            points = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(next.meshDeformPoints);
            return true;
        }
        if (next == null || prev.frame == next.frame)
        {
            points = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(prev.meshDeformPoints);
            return true;
        }

        float t = Mathf.InverseLerp(prev.frame, next.frame, frameFloat);
        t = t * t * (3f - 2f * t);
        points = LerpMeshDeformPointsForSnapshot(prev.meshDeformPoints, next.meshDeformPoints, columns, rows, t);
        return true;
    }

    private List<SkyPrisonMeshDeformPoint> LerpMeshDeformPointsForSnapshot(List<SkyPrisonMeshDeformPoint> a, List<SkyPrisonMeshDeformPoint> b, int columns, int rows, float t)
    {
        List<SkyPrisonMeshDeformPoint> result = new List<SkyPrisonMeshDeformPoint>();
        columns = Mathf.Clamp(columns, 2, 16);
        rows = Mathf.Clamp(rows, 2, 16);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                SkyPrisonMeshDeformPoint pa = FindMeshDeformerPointInList(a, x, y, columns);
                SkyPrisonMeshDeformPoint pb = FindMeshDeformerPointInList(b, x, y, columns);
                Vector2 ao = pa != null ? pa.offset : Vector2.zero;
                Vector2 bo = pb != null ? pb.offset : Vector2.zero;
                Vector2 al = pa != null ? pa.handleLeftOffset : Vector2.zero;
                Vector2 bl = pb != null ? pb.handleLeftOffset : Vector2.zero;
                Vector2 ar = pa != null ? pa.handleRightOffset : Vector2.zero;
                Vector2 br = pb != null ? pb.handleRightOffset : Vector2.zero;
                Vector2 au = pa != null ? pa.handleUpOffset : Vector2.zero;
                Vector2 bu = pb != null ? pb.handleUpOffset : Vector2.zero;
                Vector2 ad = pa != null ? pa.handleDownOffset : Vector2.zero;
                Vector2 bd = pb != null ? pb.handleDownOffset : Vector2.zero;

                result.Add(new SkyPrisonMeshDeformPoint
                {
                    x = x,
                    y = y,
                    offset = Vector2.LerpUnclamped(ao, bo, t),
                    handleLeftOffset = Vector2.LerpUnclamped(al, bl, t),
                    handleRightOffset = Vector2.LerpUnclamped(ar, br, t),
                    handleUpOffset = Vector2.LerpUnclamped(au, bu, t),
                    handleDownOffset = Vector2.LerpUnclamped(ad, bd, t),
                });
            }
        }
        return result;
    }

    private List<SkyPrisonMeshDeformPoint> BuildDefaultMeshDeformerPointGrid(int columns, int rows)
    {
        List<SkyPrisonMeshDeformPoint> result = new List<SkyPrisonMeshDeformPoint>();
        columns = Mathf.Clamp(columns, 2, 16);
        rows = Mathf.Clamp(rows, 2, 16);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
                result.Add(new SkyPrisonMeshDeformPoint { x = x, y = y, offset = Vector2.zero });
        }
        return result;
    }

    private SkyPrisonMeshDeformPoint FindMeshDeformerPointInList(List<SkyPrisonMeshDeformPoint> list, int x, int y, int columns)
    {
        if (list == null)
            return null;

        // CloneMeshDeformPoints / EnsureMeshDeformerPreviewPointGrid 通常保持 y * columns + x 顺序。
        // 高密网格下这里会被 Bezier 绘制反复调用，先 O(1) 再 fallback。
        int directIndex = y * Mathf.Max(2, columns) + x;
        if (directIndex >= 0 && directIndex < list.Count)
        {
            SkyPrisonMeshDeformPoint direct = list[directIndex];
            if (direct != null && direct.x == x && direct.y == y)
                return direct;
        }

        for (int i = 0; i < list.Count; i++)
        {
            SkyPrisonMeshDeformPoint p = list[i];
            if (p != null && p.x == x && p.y == y)
                return p;
        }

        return null;
    }

    private void SyncMeshDeformerRowToEvaluatedCurrentTime(SkyPrisonAnimationRigRow deformer)
    {
        if (deformer == null || state == null)
            return;

        int columns = Mathf.Clamp(deformer.meshDeformColumns, 2, 16);
        int rows = Mathf.Clamp(deformer.meshDeformRows, 2, 16);
        List<SkyPrisonMeshDeformPoint> evaluated = state.EvaluateTimelineMeshDeformPoints(deformer, columns, rows);
        if (evaluated == null || evaluated.Count == 0)
            return;

        deformer.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(evaluated);
        EnsureMeshDeformerPreviewPointGrid(deformer, columns, rows);
    }

    private void PrepareMeshDeformerEditAtCurrentFrame(SkyPrisonAnimationRigRow deformer)
    {
        if (deformer == null || state == null || !deformer.isMeshDeformer)
            return;

        SyncMeshDeformerRowToEvaluatedCurrentTime(deformer);
        state.PushStructureUndo();
        state.EnsureMeshDeformerProtectionKeyframesAroundCurrent(deformer);
        state.EnsureCurrentFrameMeshDeformerKeyframeForRow(deformer);
        meshOuterFrameHidden = false;
        meshOuterFrameHiddenDeformerKey = string.Empty;
    }

    private void SaveMeshDeformerKeyframeAtCurrentFrame(SkyPrisonAnimationRigRow deformer)
    {
        if (deformer == null || state == null || !deformer.isMeshDeformer)
            return;

        SkyPrisonAnimationTimelineKeyframe key = state.EnsureCurrentFrameMeshDeformerKeyframeForRow(deformer);
        if (key == null)
            return;

        key.targetKind = "MeshDeformer";
        key.useMeshDeform = true;
        key.meshDeformColumns = deformer.meshDeformColumns;
        key.meshDeformRows = deformer.meshDeformRows;
        key.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(deformer.meshDeformPoints);
    }


    private bool HandleMeshDeformerOuterFrameKeyboardNudge(SkyPrisonAnimationRigRow deformer, Rect rect, int columns, int rows)
    {
        Event e = Event.current;
        if (e == null || deformer == null || state == null || !deformer.isMeshDeformer)
            return false;

        if (e.type != EventType.KeyDown || !e.shift)
            return false;

        // 输入框正在编辑文字时，Shift + 方向键应该继续作为文本选择/光标操作，不能抢走。
        if (EditorGUIUtility.editingTextField)
            return false;

        if (draggingMeshPointActive || draggingMeshSurfaceActive || draggingMeshOuterActive)
            return false;

        Vector2 screenDelta = Vector2.zero;
        switch (e.keyCode)
        {
            case KeyCode.LeftArrow: screenDelta = new Vector2(-1f, 0f); break;
            case KeyCode.RightArrow: screenDelta = new Vector2(1f, 0f); break;
            case KeyCode.UpArrow: screenDelta = new Vector2(0f, -1f); break;
            case KeyCode.DownArrow: screenDelta = new Vector2(0f, 1f); break;
            default: return false;
        }

        // Shift + Ctrl/Cmd + 方向键：快速微调 10px。
        if (e.control || e.command)
            screenDelta *= 10f;

        PrepareMeshDeformerEditAtCurrentFrame(deformer);
        EnsureMeshDeformerPreviewPointGrid(deformer, columns, rows);

        Vector2[,] currentPoints = BuildMeshDeformerPreviewPoints(deformer, rect, columns, rows);
        TranslateMeshDeformerFrameByScreenDelta(deformer, rect, currentPoints, columns, rows, screenDelta);

        SaveMeshDeformerKeyframeAtCurrentFrame(deformer);
        meshDeformerLiveEditingDirty = false;
        draggingMeshDeformerKey = string.Empty;
        ShowMeshOuterFrame();
        MarkMeshDeformerLivePreviewChanged(true);
        GUI.changed = true;
        e.Use();
        return true;
    }

    private void TranslateMeshDeformerFrameByScreenDelta(SkyPrisonAnimationRigRow deformer, Rect rect, Vector2[,] currentPoints, int columns, int rows, Vector2 screenDelta)
    {
        if (deformer == null || currentPoints == null)
            return;

        float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
        float safeZoom = Mathf.Max(0.0001f, zoom);
        bool useSelection = HasMeshAnchorSelection(deformer);
        string[] kinds = { "left", "right", "up", "down" };

        Vector2[,] desiredAnchors = new Vector2[columns, rows];
        Dictionary<string, Vector2> desiredHandles = new Dictionary<string, Vector2>();

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                bool moved = !useSelection || IsMeshAnchorSelected(deformer, x, y);
                desiredAnchors[x, y] = currentPoints[x, y] + (moved ? screenDelta : Vector2.zero);

                if (!moved)
                    continue;

                for (int i = 0; i < kinds.Length; i++)
                {
                    string kind = kinds[i];
                    if (!IsMeshDeformerHandleValid(currentPoints, x, y, kind))
                        continue;
                    desiredHandles[MeshPointKey(x, y, kind)] = GetMeshDeformerHandleScreenPoint(deformer, currentPoints, x, y, kind) + screenDelta;
                }
            }
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (useSelection && !IsMeshAnchorSelected(deformer, x, y))
                    continue;

                SkyPrisonMeshDeformPoint p = FindMeshDeformerPoint(deformer, x, y);
                if (p == null)
                    continue;

                Vector2 basePoint = GetBaseMeshPointScreen(rect, columns, rows, x, y);
                p.offset = MeshScreenPointToLocalOffset(currentMeshDeformerScreenFrameValid ? currentMeshDeformerScreenFrame : BuildMeshDeformerScreenFrame(rect), basePoint, desiredAnchors[x, y]);
            }
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (useSelection && !IsMeshAnchorSelected(deformer, x, y))
                    continue;

                SkyPrisonMeshDeformPoint p = FindMeshDeformerPoint(deformer, x, y);
                if (p == null)
                    continue;

                for (int i = 0; i < kinds.Length; i++)
                {
                    string kind = kinds[i];
                    if (!IsMeshDeformerHandleValid(desiredAnchors, x, y, kind))
                        continue;

                    Vector2 desiredHandle;
                    if (!desiredHandles.TryGetValue(MeshPointKey(x, y, kind), out desiredHandle))
                        continue;

                    Vector2 anchor = desiredAnchors[x, y];
                    Vector2 neighbor = anchor;
                    switch (kind)
                    {
                        case "left": neighbor = desiredAnchors[x - 1, y]; break;
                        case "right": neighbor = desiredAnchors[x + 1, y]; break;
                        case "up": neighbor = desiredAnchors[x, y - 1]; break;
                        case "down": neighbor = desiredAnchors[x, y + 1]; break;
                    }

                    Vector2 defaultHandle = anchor + (neighbor - anchor) / 3f;
                    SetMeshDeformerHandleOffset(p, kind, MeshScreenVectorToLocalOffset(currentMeshDeformerScreenFrameValid ? currentMeshDeformerScreenFrame : BuildMeshDeformerScreenFrame(rect), desiredHandle - defaultHandle));
                }
            }
        }
    }

    private bool IsMeshOuterFrameVisibleFor(SkyPrisonAnimationRigRow deformer)
    {
        if (deformer == null)
            return false;
        return !(meshOuterFrameHidden && meshOuterFrameHiddenDeformerKey == deformer.key);
    }

    private void ShowMeshOuterFrame()
    {
        meshOuterFrameHidden = false;
        meshOuterFrameHiddenDeformerKey = string.Empty;
    }

    private void HideMeshOuterFrame(SkyPrisonAnimationRigRow deformer)
    {
        meshOuterFrameHidden = true;
        meshOuterFrameHiddenDeformerKey = deformer != null ? deformer.key : string.Empty;
    }

    private void ClearMeshAnchorSelection()
    {
        selectedMeshAnchorKeys.Clear();
        ClearMeshSelectionFrameAxes();
    }

    private bool IsMeshDeformerHandleValid(Vector2[,] points, int x, int y, string kind)
    {
        if (points == null)
            return false;
        int columns = points.GetLength(0);
        int rows = points.GetLength(1);
        switch (kind)
        {
            case "left": return x > 0;
            case "right": return x < columns - 1;
            case "up": return y > 0;
            case "down": return y < rows - 1;
            default: return false;
        }
    }

    private Vector2 GetMeshDeformerHandleScreenPoint(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int x, int y, string kind)
    {
        Vector2 anchor = points[x, y];
        if (!IsMeshDeformerHandleValid(points, x, y, kind))
            return anchor;

        Vector2 neighbor = anchor;
        switch (kind)
        {
            case "left": neighbor = points[x - 1, y]; break;
            case "right": neighbor = points[x + 1, y]; break;
            case "up": neighbor = points[x, y - 1]; break;
            case "down": neighbor = points[x, y + 1]; break;
        }

        int columns = points != null ? points.GetLength(0) : (deformer != null ? deformer.meshDeformColumns : 0);
        int rows = points != null ? points.GetLength(1) : (deformer != null ? deformer.meshDeformRows : 0);
        SkyPrisonMeshDeformPoint p = FindMeshDeformerPointForPreview(deformer, x, y, columns, rows);
        Vector2 offset = p != null ? GetMeshDeformerHandleOffset(p, kind) : Vector2.zero;
        MeshDeformerScreenFrame frame = currentMeshDeformerScreenFrameValid ? currentMeshDeformerScreenFrame : BuildMeshDeformerScreenFrame(GetMeshDeformerPointBounds(points, columns, rows, 0f));
        Vector2 defaultHandle = anchor + (neighbor - anchor) / 3f;
        return ApplyMeshLocalOffsetToScreen(frame, defaultHandle, offset);
    }

    private Vector2 GetMeshDeformerHandleOffset(SkyPrisonMeshDeformPoint p, string kind)
    {
        if (p == null)
            return Vector2.zero;
        switch (kind)
        {
            case "left": return p.handleLeftOffset;
            case "right": return p.handleRightOffset;
            case "up": return p.handleUpOffset;
            case "down": return p.handleDownOffset;
            default: return p.offset;
        }
    }

    private void SetMeshDeformerHandleOffset(SkyPrisonMeshDeformPoint p, string kind, Vector2 value)
    {
        if (p == null)
            return;
        switch (kind)
        {
            case "left": p.handleLeftOffset = value; break;
            case "right": p.handleRightOffset = value; break;
            case "up": p.handleUpOffset = value; break;
            case "down": p.handleDownOffset = value; break;
            default: p.offset = value; break;
        }
    }

    private struct MeshOuterFrame
    {
        public bool valid;
        public Vector2 tl;
        public Vector2 tr;
        public Vector2 br;
        public Vector2 bl;
        public Vector2 center;
        public Vector2 xAxis;
        public Vector2 yAxis;
        public Vector2 topCenter;
        public Vector2 bottomCenter;
        public Vector2 leftCenter;
        public Vector2 rightCenter;
        public Vector2 rotateHandle;
    }

    private void BuildMeshOuterFrameAxes(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, out Vector2 xAxis, out Vector2 yAxis)
    {
        xAxis = Vector2.right;
        yAxis = Vector2.down;

        if (points == null || columns <= 0 || rows <= 0)
            return;

        bool useSelection = HasMeshAnchorSelection(deformer);
        if (useSelection)
        {
            // 选区红框不能在普通拖点时根据点位分布重新 PCA 旋转。
            // 否则只拖一个点，红框就会自己歪掉。选区方向只在红色旋转点操作时更新。
            if (meshSelectionFrameAxesValid && deformer != null && meshSelectionFrameAxesDeformerKey == deformer.key)
            {
                xAxis = meshSelectionFrameXAxis.sqrMagnitude > 0.0001f ? meshSelectionFrameXAxis.normalized : Vector2.right;
                yAxis = meshSelectionFrameYAxis.sqrMagnitude > 0.0001f ? meshSelectionFrameYAxis.normalized : Vector2.down;
                return;
            }

            BuildFullMeshOuterFrameAxes(points, columns, rows, out xAxis, out yAxis);
            SetMeshSelectionFrameAxes(deformer, xAxis, yAxis);
            return;
        }

        BuildFullMeshOuterFrameAxes(points, columns, rows, out xAxis, out yAxis);
    }

    private void BuildFullMeshOuterFrameAxes(Vector2[,] points, int columns, int rows, out Vector2 xAxis, out Vector2 yAxis)
    {
        xAxis = Vector2.right;
        yAxis = Vector2.down;

        if (points == null || columns <= 0 || rows <= 0)
            return;

        Vector2 meshTL = points[0, 0];
        Vector2 meshTR = points[columns - 1, 0];
        Vector2 meshBR = points[columns - 1, rows - 1];
        Vector2 meshBL = points[0, rows - 1];

        xAxis = ((meshTR - meshTL) + (meshBR - meshBL)) * 0.5f;
        if (xAxis.sqrMagnitude <= 0.0001f)
            xAxis = Vector2.right;
        xAxis.Normalize();

        Vector2 yRaw = ((meshBL - meshTL) + (meshBR - meshTR)) * 0.5f;
        yAxis = new Vector2(-xAxis.y, xAxis.x);
        if (yRaw.sqrMagnitude > 0.0001f && Vector2.Dot(yAxis, yRaw) < 0f)
            yAxis = -yAxis;
    }

    private void SetMeshSelectionFrameAxes(SkyPrisonAnimationRigRow deformer, Vector2 xAxis, Vector2 yAxis)
    {
        if (deformer == null)
            return;

        if (xAxis.sqrMagnitude <= 0.0001f)
            xAxis = Vector2.right;
        if (yAxis.sqrMagnitude <= 0.0001f)
            yAxis = Vector2.down;

        meshSelectionFrameAxesDeformerKey = deformer.key;
        meshSelectionFrameAxesValid = true;
        meshSelectionFrameXAxis = xAxis.normalized;
        meshSelectionFrameYAxis = yAxis.normalized;
    }

    private void ClearMeshSelectionFrameAxes()
    {
        meshSelectionFrameAxesDeformerKey = string.Empty;
        meshSelectionFrameAxesValid = false;
        meshSelectionFrameXAxis = Vector2.right;
        meshSelectionFrameYAxis = Vector2.down;
    }

    private Vector2 RotateMeshVector(Vector2 v, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    private bool TryBuildMeshSelectionPrincipalAxes(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, out Vector2 xAxis, out Vector2 yAxis)
    {
        xAxis = Vector2.right;
        yAxis = Vector2.down;

        List<Vector2> samples = new List<Vector2>();
        string[] handleKinds = { "left", "right", "up", "down" };

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (!IsMeshAnchorSelected(deformer, x, y))
                    continue;

                samples.Add(points[x, y]);

                for (int i = 0; i < handleKinds.Length; i++)
                {
                    string kind = handleKinds[i];
                    if (IsMeshDeformerHandleValid(points, x, y, kind))
                        samples.Add(GetMeshDeformerHandleScreenPoint(deformer, points, x, y, kind));
                }
            }
        }

        if (samples.Count < 2)
            return false;

        Vector2 center = Vector2.zero;
        for (int i = 0; i < samples.Count; i++)
            center += samples[i];
        center /= samples.Count;

        float xx = 0f;
        float xy = 0f;
        float yy = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 d = samples[i] - center;
            xx += d.x * d.x;
            xy += d.x * d.y;
            yy += d.y * d.y;
        }

        if (xx + yy <= 0.0001f)
            return false;

        // 用选区点位的主方向来决定红框朝向。
        // 这样旋转选区以后，红色矩形会跟随选区旋转，而不是回弹成整张曲面的水平/垂直框。
        float angle = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
        Vector2 axisA = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        if (axisA.sqrMagnitude <= 0.0001f)
            return false;
        axisA.Normalize();

        Vector2 axisB = new Vector2(-axisA.y, axisA.x);

        float varA = 0f;
        float varB = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 d = samples[i] - center;
            float da = Vector2.Dot(d, axisA);
            float db = Vector2.Dot(d, axisB);
            varA += da * da;
            varB += db * db;
        }

        xAxis = varA >= varB ? axisA : axisB;
        if (Vector2.Dot(xAxis, Vector2.right) < 0f)
            xAxis = -xAxis;

        yAxis = new Vector2(-xAxis.y, xAxis.x);
        if (Vector2.Dot(yAxis, Vector2.down) < 0f)
            yAxis = -yAxis;

        return true;
    }

    private MeshOuterFrame BuildMeshDeformerOuterFrame(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, float padding = 12f)
    {
        MeshOuterFrame frame = new MeshOuterFrame();
        frame.valid = false;
        frame.xAxis = Vector2.right;
        frame.yAxis = Vector2.down;

        if (points == null || columns <= 0 || rows <= 0)
            return frame;

        Vector2 xAxis;
        Vector2 yAxis;
        BuildMeshOuterFrameAxes(deformer, points, columns, rows, out xAxis, out yAxis);

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        bool any = false;

        string[] handleKinds = { "left", "right", "up", "down" };
        bool useSelection = HasMeshAnchorSelection(deformer);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (useSelection && !IsMeshAnchorSelected(deformer, x, y))
                    continue;

                ExpandMeshOuterFrameBounds(points[x, y], xAxis, yAxis, ref minX, ref maxX, ref minY, ref maxY);
                any = true;

                if (deformer == null)
                    continue;

                for (int i = 0; i < handleKinds.Length; i++)
                {
                    string kind = handleKinds[i];
                    if (!IsMeshDeformerHandleValid(points, x, y, kind))
                        continue;

                    Vector2 hp = GetMeshDeformerHandleScreenPoint(deformer, points, x, y, kind);
                    ExpandMeshOuterFrameBounds(hp, xAxis, yAxis, ref minX, ref maxX, ref minY, ref maxY);
                }
            }
        }

        if (!any)
            return frame;

        minX -= padding;
        maxX += padding;
        minY -= padding;
        maxY += padding;

        frame.tl = xAxis * minX + yAxis * minY;
        frame.tr = xAxis * maxX + yAxis * minY;
        frame.br = xAxis * maxX + yAxis * maxY;
        frame.bl = xAxis * minX + yAxis * maxY;
        frame.center = (frame.tl + frame.tr + frame.br + frame.bl) * 0.25f;
        frame.xAxis = xAxis;
        frame.yAxis = yAxis;
        frame.topCenter = (frame.tl + frame.tr) * 0.5f;
        frame.bottomCenter = (frame.bl + frame.br) * 0.5f;
        frame.leftCenter = (frame.tl + frame.bl) * 0.5f;
        frame.rightCenter = (frame.tr + frame.br) * 0.5f;
        frame.rotateHandle = frame.topCenter - yAxis * 28f;
        frame.valid = true;
        return frame;
    }

    private void ExpandMeshOuterFrameBounds(Vector2 p, Vector2 xAxis, Vector2 yAxis, ref float minX, ref float maxX, ref float minY, ref float maxY)
    {
        float px = Vector2.Dot(p, xAxis);
        float py = Vector2.Dot(p, yAxis);
        minX = Mathf.Min(minX, px);
        maxX = Mathf.Max(maxX, px);
        minY = Mathf.Min(minY, py);
        maxY = Mathf.Max(maxY, py);
    }

    private Rect GetMeshDeformerOuterBounds(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, float padding = 12f)
    {
        MeshOuterFrame frame = BuildMeshDeformerOuterFrame(deformer, points, columns, rows, padding);
        if (!frame.valid)
            return Rect.zero;

        float minX = Mathf.Min(frame.tl.x, frame.tr.x, frame.br.x, frame.bl.x);
        float minY = Mathf.Min(frame.tl.y, frame.tr.y, frame.br.y, frame.bl.y);
        float maxX = Mathf.Max(frame.tl.x, frame.tr.x, frame.br.x, frame.bl.x);
        float maxY = Mathf.Max(frame.tl.y, frame.tr.y, frame.br.y, frame.bl.y);
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private void DrawMeshDeformerOuterFrame(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows)
    {
        MeshOuterFrame frame = BuildMeshDeformerOuterFrame(deformer, points, columns, rows, 12f);
        if (!frame.valid)
            return;

        Event e = Event.current;
        Vector2 mouse = e != null ? e.mousePosition : Vector2.zero;
        bool active = draggingMeshOuterActive && draggingMeshDeformerKey == deformer.key;

        Color old = Handles.color;
        Color red = active ? new Color(1f, 0.22f, 0.16f, 1f) : new Color(1f, 0.05f, 0.03f, 0.92f);
        Handles.color = red;
        Handles.DrawAAPolyLine(2.0f,
            new Vector3(frame.tl.x, frame.tl.y, 0f),
            new Vector3(frame.tr.x, frame.tr.y, 0f),
            new Vector3(frame.br.x, frame.br.y, 0f),
            new Vector3(frame.bl.x, frame.bl.y, 0f),
            new Vector3(frame.tl.x, frame.tl.y, 0f));

        Handles.DrawAAPolyLine(1.5f,
            new Vector3(frame.topCenter.x, frame.topCenter.y, 0f),
            new Vector3(frame.rotateHandle.x, frame.rotateHandle.y, 0f));
        Handles.color = old;

        AddCursorRectForMeshOuterEdge(frame.tl, frame.tr, MouseCursor.ResizeVertical);
        AddCursorRectForMeshOuterEdge(frame.tr, frame.br, MouseCursor.ResizeHorizontal);
        AddCursorRectForMeshOuterEdge(frame.br, frame.bl, MouseCursor.ResizeVertical);
        AddCursorRectForMeshOuterEdge(frame.bl, frame.tl, MouseCursor.ResizeHorizontal);
        AddCursorRectForMeshOuterHandle(frame.tl, MouseCursor.ResizeUpLeft);
        AddCursorRectForMeshOuterHandle(frame.tr, MouseCursor.ResizeUpRight);
        AddCursorRectForMeshOuterHandle(frame.br, MouseCursor.ResizeUpLeft);
        AddCursorRectForMeshOuterHandle(frame.bl, MouseCursor.ResizeUpRight);
        AddCursorRectForMeshOuterHandle(frame.topCenter, MouseCursor.ResizeVertical);
        AddCursorRectForMeshOuterHandle(frame.rightCenter, MouseCursor.ResizeHorizontal);
        AddCursorRectForMeshOuterHandle(frame.bottomCenter, MouseCursor.ResizeVertical);
        AddCursorRectForMeshOuterHandle(frame.leftCenter, MouseCursor.ResizeHorizontal);
        AddCursorRectForMeshOuterHandle(frame.rotateHandle, MouseCursor.RotateArrow);

        // PS / CSP 式变形框内部：显示完整十字移动箭头。
        // 注意只在真实四边形内部、且不靠近边/角/旋转柄时显示，避免和缩放/旋转抢焦点。
        AddCursorRectForMeshOuterInteriorMove(deformer, points, columns, rows, mouse);

        DrawMeshOuterHandleSquare(frame.tl, mouse, "scale_tl", red);
        DrawMeshOuterHandleSquare(frame.tr, mouse, "scale_tr", red);
        DrawMeshOuterHandleSquare(frame.br, mouse, "scale_br", red);
        DrawMeshOuterHandleSquare(frame.bl, mouse, "scale_bl", red);
        DrawMeshOuterHandleSquare(frame.topCenter, mouse, "scale_top", red);
        DrawMeshOuterHandleSquare(frame.rightCenter, mouse, "scale_right", red);
        DrawMeshOuterHandleSquare(frame.bottomCenter, mouse, "scale_bottom", red);
        DrawMeshOuterHandleSquare(frame.leftCenter, mouse, "scale_left", red);
        DrawMeshOuterRotateHandle(frame.rotateHandle, mouse, red);
    }

    private void DrawMeshOuterHandleSquare(Vector2 p, Vector2 mouse, string kind, Color normal)
    {
        bool active = draggingMeshOuterActive && draggingMeshOuterKind == kind;
        bool hover = Vector2.Distance(mouse, p) <= 9f;
        Color c = active || hover ? new Color(1f, 0.86f, 0.20f, 1f) : normal;
        EditorGUI.DrawRect(new Rect(p.x - 5.5f, p.y - 5.5f, 11f, 11f), new Color(0f, 0f, 0f, 0.55f));
        EditorGUI.DrawRect(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), c);
    }

    private void DrawMeshOuterRotateHandle(Vector2 p, Vector2 mouse, Color normal)
    {
        bool active = draggingMeshOuterActive && draggingMeshOuterKind == "rotate";
        bool hover = Vector2.Distance(mouse, p) <= 10f;
        Color c = active || hover ? new Color(1f, 0.86f, 0.20f, 1f) : normal;
        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = new Color(0f, 0f, 0f, 0.55f);
        Handles.DrawSolidDisc(p, Vector3.forward, 6.0f);
        Handles.color = c;
        Handles.DrawSolidDisc(p, Vector3.forward, 4.2f);
        Handles.color = old;
        Handles.EndGUI();
    }

    private void AddCursorRectForMeshOuterHandle(Vector2 p, MouseCursor cursor, float size = 16f)
    {
        EditorGUIUtility.AddCursorRect(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), cursor);
    }

    private void AddCursorRectForMeshOuterEdge(Vector2 a, Vector2 b, MouseCursor cursor, float pad = 5f)
    {
        float minX = Mathf.Min(a.x, b.x) - pad;
        float minY = Mathf.Min(a.y, b.y) - pad;
        float maxX = Mathf.Max(a.x, b.x) + pad;
        float maxY = Mathf.Max(a.y, b.y) + pad;
        EditorGUIUtility.AddCursorRect(Rect.MinMaxRect(minX, minY, maxX, maxY), cursor);
    }

    private void AddCursorRectForMeshOuterInteriorMove(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, Vector2 mouse)
    {
        if (draggingMeshPointActive || draggingMeshOuterActive)
            return;

        if (!IsInsideMeshOuterFrameInterior(deformer, points, columns, rows, mouse))
            return;

        if (ShouldMeshOuterFrameInputYieldToPointInput(deformer, points, columns, rows, mouse, false))
            return;

        // IMGUI 没有任意多边形光标区域，只能注册 Rect。
        // 这里用鼠标附近的小区域注册，命中判断仍然走上面的真实四边形检测。
        EditorGUIUtility.AddCursorRect(new Rect(mouse.x - 18f, mouse.y - 18f, 36f, 36f), MouseCursor.MoveArrow);
    }

    private bool IsInsideMeshOuterFrameInterior(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, Vector2 mouse)
    {
        MeshOuterFrame frame = BuildMeshDeformerOuterFrame(deformer, points, columns, rows, 12f);
        if (!frame.valid)
            return false;

        string edgeOrHandleKind;
        if (TryHitMeshOuterFrame(deformer, points, columns, rows, mouse, out edgeOrHandleKind))
            return false;

        return PointInTriangle(mouse, frame.tl, frame.tr, frame.br) ||
               PointInTriangle(mouse, frame.tl, frame.br, frame.bl);
    }

    private float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq <= 0.0001f)
            return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        Vector2 q = a + ab * t;
        return Vector2.Distance(p, q);
    }

    private bool ShouldMeshOuterFrameInputYieldToPointInput(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, Vector2 mouse, bool shiftMode)
    {
        int hitX;
        int hitY;
        string hitKind;

        // Shift 模式用于多选主控制点，方向柄不参与多选；保持原本的多选语义。
        if (!shiftMode && TryHitMeshDeformerHandle(deformer, points, columns, rows, mouse, out hitX, out hitY, out hitKind))
            return true;

        return TryHitMeshDeformerAnchor(points, columns, rows, mouse, out hitX, out hitY);
    }

    private bool TryHitMeshOuterFrame(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, Vector2 mouse, out string kind)
    {
        kind = string.Empty;
        MeshOuterFrame frame = BuildMeshDeformerOuterFrame(deformer, points, columns, rows, 12f);
        if (!frame.valid)
            return false;

        float best = float.MaxValue;
        TryHitMeshOuterFrameHandle(mouse, frame.rotateHandle, "rotate", 11f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.tl, "scale_tl", 10f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.tr, "scale_tr", 10f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.br, "scale_br", 10f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.bl, "scale_bl", 10f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.topCenter, "scale_top", 10f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.rightCenter, "scale_right", 10f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.bottomCenter, "scale_bottom", 10f, ref best, ref kind);
        TryHitMeshOuterFrameHandle(mouse, frame.leftCenter, "scale_left", 10f, ref best, ref kind);

        const float edgeThreshold = 6f;
        TryHitMeshOuterFrameEdge(mouse, frame.tl, frame.tr, "scale_top", edgeThreshold, ref best, ref kind);
        TryHitMeshOuterFrameEdge(mouse, frame.tr, frame.br, "scale_right", edgeThreshold, ref best, ref kind);
        TryHitMeshOuterFrameEdge(mouse, frame.br, frame.bl, "scale_bottom", edgeThreshold, ref best, ref kind);
        TryHitMeshOuterFrameEdge(mouse, frame.bl, frame.tl, "scale_left", edgeThreshold, ref best, ref kind);

        return !string.IsNullOrEmpty(kind);
    }

    private void TryHitMeshOuterFrameHandle(Vector2 mouse, Vector2 handle, string candidateKind, float radius, ref float best, ref string bestKind)
    {
        float d = Vector2.Distance(mouse, handle);
        if (d <= radius && d < best)
        {
            best = d;
            bestKind = candidateKind;
        }
    }

    private void TryHitMeshOuterFrameEdge(Vector2 mouse, Vector2 a, Vector2 b, string candidateKind, float radius, ref float best, ref string bestKind)
    {
        float d = DistancePointToSegment(mouse, a, b);
        if (d <= radius && d < best)
        {
            best = d;
            bestKind = candidateKind;
        }
    }

    private bool HandleMeshDeformerOuterFrameInput(SkyPrisonAnimationRigRow deformer, Rect rect, Vector2[,] points, int columns, int rows)
    {
        Event e = Event.current;
        if (e == null || deformer == null || points == null)
            return false;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            // 命中优先级：方向柄端点 / 主控制点 > 红色外框 > 曲面整体移动。
            // 之前外框输入先执行，鼠标落在红框内部时会直接进入整体移动，导致明明点到绿色方向柄端点，实际却拖动了整块曲面。
            // 这里先把方向柄和主点让给 HandleMeshDeformerPointInput 处理，保证“点到什么就拖什么”。
            if (ShouldMeshOuterFrameInputYieldToPointInput(deformer, points, columns, rows, e.mousePosition, e.shift))
                return false;

            string kind;
            if (TryHitMeshOuterFrame(deformer, points, columns, rows, e.mousePosition, out kind))
            {
                PrepareMeshDeformerEditAtCurrentFrame(deformer);
                points = BuildMeshDeformerPreviewPoints(deformer, rect, columns, rows);
                draggingMeshDeformerKey = deformer.key;
                draggingMeshOuterKind = kind;
                draggingMeshOuterActive = true;
                draggingMeshOuterStartMouse = e.mousePosition;
                MeshOuterFrame startFrame = BuildMeshDeformerOuterFrame(deformer, points, columns, rows, 12f);
                draggingMeshOuterStartBounds = GetMeshDeformerOuterBounds(deformer, points, columns, rows, 12f);
                draggingMeshOuterStartCenter = startFrame.valid ? startFrame.center : draggingMeshOuterStartBounds.center;
                draggingMeshOuterStartXAxis = startFrame.valid ? startFrame.xAxis : Vector2.right;
                draggingMeshOuterStartYAxis = startFrame.valid ? startFrame.yAxis : Vector2.down;
                draggingMeshOuterStartTL = startFrame.valid ? startFrame.tl : new Vector2(draggingMeshOuterStartBounds.xMin, draggingMeshOuterStartBounds.yMin);
                draggingMeshOuterStartTR = startFrame.valid ? startFrame.tr : new Vector2(draggingMeshOuterStartBounds.xMax, draggingMeshOuterStartBounds.yMin);
                draggingMeshOuterStartBR = startFrame.valid ? startFrame.br : new Vector2(draggingMeshOuterStartBounds.xMax, draggingMeshOuterStartBounds.yMax);
                draggingMeshOuterStartBL = startFrame.valid ? startFrame.bl : new Vector2(draggingMeshOuterStartBounds.xMin, draggingMeshOuterStartBounds.yMax);
                draggingMeshOuterStartVector = e.mousePosition - draggingMeshOuterStartCenter;
                CaptureMeshOuterStartPoints(deformer, points, columns, rows);
                meshDeformerLiveEditingDirty = false;
                state.PreviewPanelRigDragging = true;
                MarkMeshDeformerLivePreviewChanged(true);
                e.Use();
                return true;
            }

            // 变形框内部空白区域：移动整个红色变形框。
            // 这不是缩放，也不是拖单点，而是把所有主控制点 / 方向柄整体平移。
            if (!e.shift && !e.control && !e.command && IsInsideMeshOuterFrameInterior(deformer, points, columns, rows, e.mousePosition))
            {
                PrepareMeshDeformerEditAtCurrentFrame(deformer);
                points = BuildMeshDeformerPreviewPoints(deformer, rect, columns, rows);

                draggingMeshDeformerKey = deformer.key;
                draggingMeshOuterKind = "move";
                draggingMeshOuterActive = true;
                draggingMeshOuterStartMouse = e.mousePosition;

                MeshOuterFrame startFrame = BuildMeshDeformerOuterFrame(deformer, points, columns, rows, 12f);
                draggingMeshOuterStartBounds = GetMeshDeformerOuterBounds(deformer, points, columns, rows, 12f);
                draggingMeshOuterStartCenter = startFrame.valid ? startFrame.center : draggingMeshOuterStartBounds.center;
                draggingMeshOuterStartXAxis = startFrame.valid ? startFrame.xAxis : Vector2.right;
                draggingMeshOuterStartYAxis = startFrame.valid ? startFrame.yAxis : Vector2.down;
                draggingMeshOuterStartTL = startFrame.valid ? startFrame.tl : new Vector2(draggingMeshOuterStartBounds.xMin, draggingMeshOuterStartBounds.yMin);
                draggingMeshOuterStartTR = startFrame.valid ? startFrame.tr : new Vector2(draggingMeshOuterStartBounds.xMax, draggingMeshOuterStartBounds.yMin);
                draggingMeshOuterStartBR = startFrame.valid ? startFrame.br : new Vector2(draggingMeshOuterStartBounds.xMax, draggingMeshOuterStartBounds.yMax);
                draggingMeshOuterStartBL = startFrame.valid ? startFrame.bl : new Vector2(draggingMeshOuterStartBounds.xMin, draggingMeshOuterStartBounds.yMax);
                draggingMeshOuterStartVector = e.mousePosition - draggingMeshOuterStartCenter;

                CaptureMeshOuterStartPoints(deformer, points, columns, rows);
                meshDeformerLiveEditingDirty = false;
                state.PreviewPanelRigDragging = true;
                MarkMeshDeformerLivePreviewChanged(true);
                e.Use();
                return true;
            }
        }
        else if (e.type == EventType.MouseDrag && draggingMeshOuterActive && draggingMeshDeformerKey == deformer.key)
        {
            ApplyMeshOuterTransformDrag(deformer, rect, columns, rows, e.mousePosition, e.shift);
            meshDeformerLiveEditingDirty = true;
            MarkMeshDeformerLivePreviewChanged(false);
            e.Use();
            return true;
        }
        else if (e.type == EventType.MouseUp && draggingMeshOuterActive && draggingMeshDeformerKey == deformer.key)
        {
            if (meshDeformerLiveEditingDirty)
                SaveMeshDeformerKeyframeAtCurrentFrame(deformer);
            meshDeformerLiveEditingDirty = false;
            draggingMeshOuterActive = false;
            draggingMeshOuterKind = string.Empty;
            draggingMeshOuterStartAnchors.Clear();
            draggingMeshOuterStartHandles.Clear();
            state.PreviewPanelRigDragging = false;
            MarkMeshDeformerLivePreviewChanged(true);
            e.Use();
            return true;
        }

        return draggingMeshOuterActive && draggingMeshDeformerKey == deformer.key;
    }

    private void CaptureMeshOuterStartPoints(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows)
    {
        draggingMeshOuterStartAnchors.Clear();
        draggingMeshOuterStartHandles.Clear();
        string[] kinds = { "left", "right", "up", "down" };
        bool useSelection = HasMeshAnchorSelection(deformer);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (useSelection && !IsMeshAnchorSelected(deformer, x, y))
                    continue;

                draggingMeshOuterStartAnchors[MeshPointKey(x, y, "anchor")] = points[x, y];
                for (int i = 0; i < kinds.Length; i++)
                {
                    string kind = kinds[i];
                    if (IsMeshDeformerHandleValid(points, x, y, kind))
                        draggingMeshOuterStartHandles[MeshPointKey(x, y, kind)] = GetMeshDeformerHandleScreenPoint(deformer, points, x, y, kind);
                }
            }
        }
    }

    private void EnsureMeshAnchorSelectionForDeformer(SkyPrisonAnimationRigRow deformer)
    {
        string key = deformer != null ? deformer.key : string.Empty;
        if (selectedMeshAnchorDeformerKey == key)
            return;

        selectedMeshAnchorDeformerKey = key;
        selectedMeshAnchorKeys.Clear();
        ClearMeshSelectionFrameAxes();
        meshOuterFrameHidden = false;
        meshOuterFrameHiddenDeformerKey = string.Empty;
    }

    private string MeshAnchorSelectionKey(int x, int y)
    {
        return x.ToString() + "_" + y.ToString();
    }

    private bool TryParseMeshAnchorSelectionKey(string key, out int x, out int y)
    {
        x = -1;
        y = -1;
        if (string.IsNullOrEmpty(key))
            return false;

        string[] parts = key.Split('_');
        return parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y);
    }

    private void CaptureSelectedMeshAnchorStartOffsets(SkyPrisonAnimationRigRow deformer)
    {
        draggingMeshSelectedAnchorStartOffsets.Clear();
        if (deformer == null || !HasMeshAnchorSelection(deformer))
            return;

        foreach (string selectionKey in selectedMeshAnchorKeys)
        {
            int x;
            int y;
            if (!TryParseMeshAnchorSelectionKey(selectionKey, out x, out y))
                continue;

            SkyPrisonMeshDeformPoint selectedPoint = FindMeshDeformerPoint(deformer, x, y);
            if (selectedPoint != null)
                draggingMeshSelectedAnchorStartOffsets[selectionKey] = selectedPoint.offset;
        }
    }

    private bool HasMeshAnchorSelection(SkyPrisonAnimationRigRow deformer)
    {
        return deformer != null && selectedMeshAnchorDeformerKey == deformer.key && selectedMeshAnchorKeys.Count > 0;
    }

    private bool IsMeshAnchorSelected(SkyPrisonAnimationRigRow deformer, int x, int y)
    {
        return deformer != null && selectedMeshAnchorDeformerKey == deformer.key && selectedMeshAnchorKeys.Contains(MeshAnchorSelectionKey(x, y));
    }

    private void ToggleMeshAnchorSelection(SkyPrisonAnimationRigRow deformer, int x, int y)
    {
        if (deformer == null)
            return;

        EnsureMeshAnchorSelectionForDeformer(deformer);
        string key = MeshAnchorSelectionKey(x, y);
        if (selectedMeshAnchorKeys.Contains(key))
            selectedMeshAnchorKeys.Remove(key);
        else
            selectedMeshAnchorKeys.Add(key);

        ClearMeshSelectionFrameAxes();
        ShowMeshOuterFrame();
    }

    private string MeshPointKey(int x, int y, string kind)
    {
        return x.ToString() + "_" + y.ToString() + "_" + kind;
    }

    private Vector2 GetBaseMeshPointScreen(Rect rect, int columns, int rows, int x, int y)
    {
        MeshDeformerScreenFrame frame = currentMeshDeformerScreenFrameValid
            ? currentMeshDeformerScreenFrame
            : BuildMeshDeformerScreenFrame(rect);
        return GetBaseMeshPointScreen(frame, columns, rows, x, y);
    }

    private SkyPrisonAnimationRigRow FindRigRowByKeyLocal(string key)
    {
        if (state == null || string.IsNullOrEmpty(key))
            return null;

        if (state.RigRows != null)
        {
            for (int i = 0; i < state.RigRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.RigRows[i];
                if (row != null && row.key == key)
                    return row;
            }
        }

        if (state.PsbRows != null)
        {
            for (int i = 0; i < state.PsbRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.PsbRows[i];
                if (row != null && row.key == key)
                    return row;
            }
        }

        if (state.SocketRows != null)
        {
            for (int i = 0; i < state.SocketRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.SocketRows[i];
                if (row != null && row.key == key)
                    return row;
            }
        }

        return null;
    }

    private Vector2 TransformMeshOuterStartPoint(Vector2 p, Vector2 mouse, bool lockUniform)
    {
        Vector2 center = draggingMeshOuterStartCenter;
        if (draggingMeshOuterKind == "move")
        {
            Vector2 delta = mouse - draggingMeshOuterStartMouse;
            return p + delta;
        }

        if (draggingMeshOuterKind == "rotate")
        {
            Vector2 from = draggingMeshOuterStartVector;
            Vector2 to = mouse - center;
            if (from.sqrMagnitude <= 0.0001f || to.sqrMagnitude <= 0.0001f)
                return p;

            float angle = Vector2.SignedAngle(from, to);
            Vector2 v = p - center;
            float rad = angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return center + new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        Vector2 xAxis = draggingMeshOuterStartXAxis.sqrMagnitude > 0.0001f ? draggingMeshOuterStartXAxis.normalized : Vector2.right;
        Vector2 yAxis = draggingMeshOuterStartYAxis.sqrMagnitude > 0.0001f ? draggingMeshOuterStartYAxis.normalized : Vector2.down;
        Vector2 corner = Vector2.zero;
        switch (draggingMeshOuterKind)
        {
            case "scale_tl": corner = draggingMeshOuterStartTL; break;
            case "scale_tr": corner = draggingMeshOuterStartTR; break;
            case "scale_br": corner = draggingMeshOuterStartBR; break;
            case "scale_bl": corner = draggingMeshOuterStartBL; break;
            case "scale_top": corner = draggingMeshOuterStartTL * 0.5f + draggingMeshOuterStartTR * 0.5f; break;
            case "scale_right": corner = draggingMeshOuterStartTR * 0.5f + draggingMeshOuterStartBR * 0.5f; break;
            case "scale_bottom": corner = draggingMeshOuterStartBL * 0.5f + draggingMeshOuterStartBR * 0.5f; break;
            case "scale_left": corner = draggingMeshOuterStartTL * 0.5f + draggingMeshOuterStartBL * 0.5f; break;
            default: return p;
        }

        Vector2 startLocal = new Vector2(
            Vector2.Dot(corner - center, xAxis),
            Vector2.Dot(corner - center, yAxis));
        Vector2 nowLocal = new Vector2(
            Vector2.Dot(mouse - center, xAxis),
            Vector2.Dot(mouse - center, yAxis));

        bool scaleEdgeWithOppositeFixed = false;
        bool scaleCornerWithDiagonalFixed = false;
        if (state != null)
        {
            SkyPrisonAnimationRigRow activeDeformer = FindRigRowByKeyLocal(draggingMeshDeformerKey);
            string rule = activeDeformer != null && !string.IsNullOrEmpty(activeDeformer.meshDeformScaleRule)
                ? activeDeformer.meshDeformScaleRule
                : "中心对称伸缩";

            bool oldCombinedRule = string.Equals(rule, "固定对边/对角", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(rule, "KeepOppositeFixed", StringComparison.OrdinalIgnoreCase);
            bool fixedEdgeRule = string.Equals(rule, "固定对边", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(rule, "FixedOppositeEdge", StringComparison.OrdinalIgnoreCase);
            bool diagonalRule = string.Equals(rule, "对角固定", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(rule, "DiagonalFixed", StringComparison.OrdinalIgnoreCase);

            bool draggingEdge = draggingMeshOuterKind == "scale_top" || draggingMeshOuterKind == "scale_right" || draggingMeshOuterKind == "scale_bottom" || draggingMeshOuterKind == "scale_left";
            bool draggingCorner = draggingMeshOuterKind == "scale_tl" || draggingMeshOuterKind == "scale_tr" || draggingMeshOuterKind == "scale_br" || draggingMeshOuterKind == "scale_bl";

            scaleEdgeWithOppositeFixed = draggingEdge && (fixedEdgeRule || diagonalRule || oldCombinedRule);
            scaleCornerWithDiagonalFixed = draggingCorner && (diagonalRule || oldCombinedRule);
        }

        float sx = 1f;
        float sy = 1f;
        float fixedX = 0f;
        float fixedY = 0f;
        float minX = Vector2.Dot(draggingMeshOuterStartTL - center, xAxis);
        float maxX = Vector2.Dot(draggingMeshOuterStartBR - center, xAxis);
        float minY = Vector2.Dot(draggingMeshOuterStartTL - center, yAxis);
        float maxY = Vector2.Dot(draggingMeshOuterStartBR - center, yAxis);

        if (scaleEdgeWithOppositeFixed || scaleCornerWithDiagonalFixed)
        {
            switch (draggingMeshOuterKind)
            {
                case "scale_tl":
                    fixedX = maxX; fixedY = maxY;
                    sx = Mathf.Abs(minX - fixedX) > 0.001f ? (nowLocal.x - fixedX) / (minX - fixedX) : 1f;
                    sy = Mathf.Abs(minY - fixedY) > 0.001f ? (nowLocal.y - fixedY) / (minY - fixedY) : 1f;
                    break;
                case "scale_tr":
                    fixedX = minX; fixedY = maxY;
                    sx = Mathf.Abs(maxX - fixedX) > 0.001f ? (nowLocal.x - fixedX) / (maxX - fixedX) : 1f;
                    sy = Mathf.Abs(minY - fixedY) > 0.001f ? (nowLocal.y - fixedY) / (minY - fixedY) : 1f;
                    break;
                case "scale_br":
                    fixedX = minX; fixedY = minY;
                    sx = Mathf.Abs(maxX - fixedX) > 0.001f ? (nowLocal.x - fixedX) / (maxX - fixedX) : 1f;
                    sy = Mathf.Abs(maxY - fixedY) > 0.001f ? (nowLocal.y - fixedY) / (maxY - fixedY) : 1f;
                    break;
                case "scale_bl":
                    fixedX = maxX; fixedY = minY;
                    sx = Mathf.Abs(minX - fixedX) > 0.001f ? (nowLocal.x - fixedX) / (minX - fixedX) : 1f;
                    sy = Mathf.Abs(maxY - fixedY) > 0.001f ? (nowLocal.y - fixedY) / (maxY - fixedY) : 1f;
                    break;
                case "scale_top":
                    fixedY = maxY;
                    sx = 1f;
                    sy = Mathf.Abs(minY - fixedY) > 0.001f ? (nowLocal.y - fixedY) / (minY - fixedY) : 1f;
                    break;
                case "scale_right":
                    fixedX = minX;
                    sx = Mathf.Abs(maxX - fixedX) > 0.001f ? (nowLocal.x - fixedX) / (maxX - fixedX) : 1f;
                    sy = 1f;
                    break;
                case "scale_bottom":
                    fixedY = minY;
                    sx = 1f;
                    sy = Mathf.Abs(maxY - fixedY) > 0.001f ? (nowLocal.y - fixedY) / (maxY - fixedY) : 1f;
                    break;
                case "scale_left":
                    fixedX = maxX;
                    sx = Mathf.Abs(minX - fixedX) > 0.001f ? (nowLocal.x - fixedX) / (minX - fixedX) : 1f;
                    sy = 1f;
                    break;
            }
        }
        else
        {
            sx = Mathf.Abs(startLocal.x) > 0.001f ? nowLocal.x / startLocal.x : 1f;
            sy = Mathf.Abs(startLocal.y) > 0.001f ? nowLocal.y / startLocal.y : 1f;

            switch (draggingMeshOuterKind)
            {
                case "scale_top":
                case "scale_bottom":
                    sx = 1f;
                    break;
                case "scale_left":
                case "scale_right":
                    sy = 1f;
                    break;
            }
        }

        sx = Mathf.Clamp(sx, 0.05f, 20f);
        sy = Mathf.Clamp(sy, 0.05f, 20f);

        if (lockUniform)
        {
            float uniform = Mathf.Abs(sx) >= Mathf.Abs(sy) ? sx : sy;
            sx = uniform;
            sy = uniform;
        }

        Vector2 pLocal = new Vector2(
            Vector2.Dot(p - center, xAxis),
            Vector2.Dot(p - center, yAxis));

        Vector2 transformedLocal;
        if (scaleEdgeWithOppositeFixed || scaleCornerWithDiagonalFixed)
        {
            float outX = pLocal.x;
            float outY = pLocal.y;
            switch (draggingMeshOuterKind)
            {
                case "scale_tl":
                case "scale_tr":
                case "scale_br":
                case "scale_bl":
                    outX = fixedX + (pLocal.x - fixedX) * sx;
                    outY = fixedY + (pLocal.y - fixedY) * sy;
                    break;
                case "scale_top":
                case "scale_bottom":
                    outY = fixedY + (pLocal.y - fixedY) * sy;
                    break;
                case "scale_left":
                case "scale_right":
                    outX = fixedX + (pLocal.x - fixedX) * sx;
                    break;
            }
            transformedLocal = new Vector2(outX, outY);
        }
        else
        {
            transformedLocal = new Vector2(pLocal.x * sx, pLocal.y * sy);
        }

        return center + xAxis * transformedLocal.x + yAxis * transformedLocal.y;
    }

    private void ApplyMeshOuterTransformDrag(SkyPrisonAnimationRigRow deformer, Rect rect, int columns, int rows, Vector2 mouse, bool lockUniform)
    {
        if (deformer == null)
            return;

        float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
        Vector2[,] desiredAnchors = new Vector2[columns, rows];
        bool useSelection = HasMeshAnchorSelection(deformer);

        // 非选区点必须保留当前屏幕位置；否则选区缩放时，方向柄默认参考会被错误拉走。
        for (int yy = 0; yy < rows; yy++)
        {
            for (int xx = 0; xx < columns; xx++)
            {
                string baseKey = MeshPointKey(xx, yy, "anchor");
                Vector2 current;
                if (!draggingMeshOuterStartAnchors.TryGetValue(baseKey, out current))
                    current = GetCurrentMeshPointScreen(deformer, rect, columns, rows, xx, yy);
                desiredAnchors[xx, yy] = current;
            }
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (useSelection && !IsMeshAnchorSelected(deformer, x, y))
                    continue;

                string key = MeshPointKey(x, y, "anchor");
                Vector2 start;
                if (!draggingMeshOuterStartAnchors.TryGetValue(key, out start))
                    start = GetCurrentMeshPointScreen(deformer, rect, columns, rows, x, y);

                Vector2 desired = TransformMeshOuterStartPoint(start, mouse, lockUniform);
                desiredAnchors[x, y] = desired;

                SkyPrisonMeshDeformPoint p = FindMeshDeformerPoint(deformer, x, y);
                if (p != null)
                {
                    Vector2 basePoint = GetBaseMeshPointScreen(rect, columns, rows, x, y);
                    p.offset = MeshScreenPointToLocalOffset(currentMeshDeformerScreenFrameValid ? currentMeshDeformerScreenFrame : BuildMeshDeformerScreenFrame(rect), basePoint, desired);
                }
            }
        }

        string[] kinds = { "left", "right", "up", "down" };
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (useSelection && !IsMeshAnchorSelected(deformer, x, y))
                    continue;

                SkyPrisonMeshDeformPoint p = FindMeshDeformerPoint(deformer, x, y);
                if (p == null)
                    continue;

                for (int i = 0; i < kinds.Length; i++)
                {
                    string kind = kinds[i];
                    if (!IsMeshDeformerHandleValid(desiredAnchors, x, y, kind))
                        continue;

                    string key = MeshPointKey(x, y, kind);
                    Vector2 startHandle;
                    if (!draggingMeshOuterStartHandles.TryGetValue(key, out startHandle))
                        continue;

                    Vector2 desiredHandle = TransformMeshOuterStartPoint(startHandle, mouse, lockUniform);
                    Vector2 anchor = desiredAnchors[x, y];
                    Vector2 neighbor = anchor;
                    switch (kind)
                    {
                        case "left": neighbor = desiredAnchors[x - 1, y]; break;
                        case "right": neighbor = desiredAnchors[x + 1, y]; break;
                        case "up": neighbor = desiredAnchors[x, y - 1]; break;
                        case "down": neighbor = desiredAnchors[x, y + 1]; break;
                    }

                    Vector2 defaultHandle = anchor + (neighbor - anchor) / 3f;
                    SetMeshDeformerHandleOffset(p, kind, MeshScreenVectorToLocalOffset(currentMeshDeformerScreenFrameValid ? currentMeshDeformerScreenFrame : BuildMeshDeformerScreenFrame(rect), desiredHandle - defaultHandle));
                }
            }
        }

        if (useSelection)
        {
            Vector2 xAxis = draggingMeshOuterStartXAxis.sqrMagnitude > 0.0001f ? draggingMeshOuterStartXAxis.normalized : Vector2.right;
            Vector2 yAxis = draggingMeshOuterStartYAxis.sqrMagnitude > 0.0001f ? draggingMeshOuterStartYAxis.normalized : Vector2.down;

            if (draggingMeshOuterKind == "rotate")
            {
                Vector2 from = draggingMeshOuterStartVector;
                Vector2 to = mouse - draggingMeshOuterStartCenter;
                if (from.sqrMagnitude > 0.0001f && to.sqrMagnitude > 0.0001f)
                {
                    float angle = Vector2.SignedAngle(from, to);
                    xAxis = RotateMeshVector(xAxis, angle);
                    yAxis = RotateMeshVector(yAxis, angle);
                }
            }

            SetMeshSelectionFrameAxes(deformer, xAxis, yAxis);
        }
    }

    private Vector2 GetCurrentMeshPointScreen(SkyPrisonAnimationRigRow deformer, Rect rect, int columns, int rows, int x, int y)
    {
        Vector2 basePoint = GetBaseMeshPointScreen(rect, columns, rows, x, y);
        SkyPrisonMeshDeformPoint p = FindMeshDeformerPoint(deformer, x, y);
        Vector2 offset = p != null ? p.offset : Vector2.zero;
        MeshDeformerScreenFrame frame = currentMeshDeformerScreenFrameValid ? currentMeshDeformerScreenFrame : BuildMeshDeformerScreenFrame(rect);
        return ApplyMeshLocalOffsetToScreen(frame, basePoint, offset);
    }

    private void DrawMeshDeformerHandleArm(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int x, int y, string kind, Vector2 anchor)
    {
        if (!IsMeshDeformerHandleValid(points, x, y, kind))
            return;

        Vector2 handle = GetMeshDeformerHandleScreenPoint(deformer, points, x, y, kind);
        float distance = Vector2.Distance(anchor, handle);
        if (distance <= 2f)
            return;

        Event e = Event.current;
        Vector2 mouse = e != null ? e.mousePosition : Vector2.zero;
        bool active = draggingMeshDeformerKey == deformer.key && draggingMeshHandleKind == kind && draggingMeshPointX == x && draggingMeshPointY == y;
        bool hover = Vector2.Distance(mouse, handle) <= 8f || Vector2.Distance(mouse, anchor) <= 9f;

        Color oldColor = Handles.color;
        Handles.color = active || hover
            ? new Color(1f, 0.92f, 0.18f, 0.92f)
            : new Color(0.20f, 1.00f, 0.32f, 0.52f);

        Handles.DrawAAPolyLine(active || hover ? 1.75f : 1.25f, new Vector3(anchor.x, anchor.y, 0f), new Vector3(handle.x, handle.y, 0f));
        Handles.color = oldColor;
    }

    private void DrawDashedGuiLine(Vector2 from, Vector2 to, float dashLength, float gapLength, float width)
    {
        float length = Vector2.Distance(from, to);
        if (length <= 0.01f)
            return;

        Vector2 dir = (to - from) / length;
        float cursor = 0f;
        while (cursor < length)
        {
            float next = Mathf.Min(cursor + dashLength, length);
            Vector2 a = from + dir * cursor;
            Vector2 b = from + dir * next;
            Handles.DrawAAPolyLine(width, new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
            cursor += dashLength + gapLength;
        }
    }

    private void DrawMeshDeformerHandleDot(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int x, int y, string kind, Vector2 mouse, Color normalColor, Color hotColor)
    {
        if (!IsMeshDeformerHandleValid(points, x, y, kind))
            return;

        Vector2 p = GetMeshDeformerHandleScreenPoint(deformer, points, x, y, kind);
        Rect hit = new Rect(p.x - 4f, p.y - 4f, 8f, 8f);
        bool active = draggingMeshDeformerKey == deformer.key && draggingMeshHandleKind == kind && draggingMeshPointX == x && draggingMeshPointY == y;
        bool hover = hit.Contains(mouse);
        Color c = active || hover ? hotColor : normalColor;

        EditorGUI.DrawRect(new Rect(p.x - 3.7f, p.y - 3.7f, 7.4f, 7.4f), new Color(0f, 0f, 0f, 0.55f));
        EditorGUI.DrawRect(new Rect(p.x - 2.7f, p.y - 2.7f, 5.4f, 5.4f), c);
        EditorGUI.DrawRect(new Rect(p.x - 1.0f, p.y - 1.0f, 2.0f, 2.0f), new Color(0f, 0f, 0f, 0.35f));
    }

    private float GetMeshDeformerHandleHitRadius()
    {
        // 方向柄端点是精细编辑对象，命中半径要略大于视觉点。
        // 否则用户视觉上已经点到端点，却会被曲面整体移动抢走，手感非常反直觉。
        float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
        return Mathf.Clamp(11f * Mathf.Lerp(1.25f, 0.90f, Mathf.InverseLerp(0.35f, 2.5f, zoom)), 9f, 14f);
    }

    private bool TryHitMeshDeformerHandle(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows, Vector2 mouse, out int hitX, out int hitY, out string hitKind)
    {
        hitX = -1;
        hitY = -1;
        hitKind = "anchor";
        float bestDist = float.MaxValue;
        string[] kinds = { "left", "right", "up", "down" };

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                for (int i = 0; i < kinds.Length; i++)
                {
                    string kind = kinds[i];
                    if (!IsMeshDeformerHandleValid(points, x, y, kind))
                        continue;

                    Vector2 hp = GetMeshDeformerHandleScreenPoint(deformer, points, x, y, kind);
                    float d = Vector2.Distance(mouse, hp);
                    if (d <= GetMeshDeformerHandleHitRadius() && d < bestDist)
                    {
                        bestDist = d;
                        hitX = x;
                        hitY = y;
                        hitKind = kind;
                    }
                }
            }
        }

        return hitX >= 0 && hitY >= 0;
    }

    private bool TryHitMeshDeformerAnchor(Vector2[,] points, int columns, int rows, Vector2 mouse, out int hitX, out int hitY)
    {
        hitX = -1;
        hitY = -1;
        float bestDist = float.MaxValue;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                float d = Vector2.Distance(mouse, points[x, y]);
                if (d <= 8f && d < bestDist)
                {
                    bestDist = d;
                    hitX = x;
                    hitY = y;
                }
            }
        }

        return hitX >= 0 && hitY >= 0;
    }

    private void HandleMeshDeformerPointInput(SkyPrisonAnimationRigRow deformer, Rect rect, Vector2[,] points, int columns, int rows)
    {
        Event e = Event.current;
        if (e == null || deformer == null || points == null)
            return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int hitX;
            int hitY;
            string hitKind;
            Vector2 mouse = e.mousePosition;

            bool hit;
            if (e.shift)
            {
                // Shift + 左键只处理主控制点多选，不让方向柄抢命中。
                hitKind = "anchor";
                hit = TryHitMeshDeformerAnchor(points, columns, rows, mouse, out hitX, out hitY);
            }
            else
            {
                // 普通拖拽优先命中方向柄；方向柄只改切线，不移动主控制点。
                hit = TryHitMeshDeformerHandle(deformer, points, columns, rows, mouse, out hitX, out hitY, out hitKind);
                if (!hit)
                {
                    hitKind = "anchor";
                    hit = TryHitMeshDeformerAnchor(points, columns, rows, mouse, out hitX, out hitY);
                }
            }

            if (hit)
            {
                SkyPrisonMeshDeformPoint point = FindMeshDeformerPointForPreview(deformer, hitX, hitY, columns, rows);
                if (point == null)
                    return;

                // Shift + 左键：多选主控制点。方向柄不进入选区，避免选区框语义混乱。
                if (e.shift && hitKind == "anchor")
                {
                    ToggleMeshAnchorSelection(deformer, hitX, hitY);
                    GUI.changed = true;
                    e.Use();
                    return;
                }

                PrepareMeshDeformerEditAtCurrentFrame(deformer);
                point = FindMeshDeformerPoint(deformer, hitX, hitY);
                if (point == null)
                    return;

                ShowMeshOuterFrame();
                draggingMeshDeformerKey = deformer.key;
                draggingMeshPointX = hitX;
                draggingMeshPointY = hitY;
                draggingMeshHandleKind = hitKind;
                draggingMeshStartMouse = mouse;
                draggingMeshStartOffset = hitKind == "anchor" ? point.offset : GetMeshDeformerHandleOffset(point, hitKind);
                draggingMeshSelectedAnchorStartOffsets.Clear();
                if (hitKind == "anchor" && IsMeshAnchorSelected(deformer, hitX, hitY))
                    CaptureSelectedMeshAnchorStartOffsets(deformer);
                meshDeformerLiveEditingDirty = false;
                draggingMeshPointActive = true;
                state.PreviewPanelRigDragging = true;
                MarkMeshDeformerLivePreviewChanged(true);
                e.Use();
            }
            else if (!e.shift && !e.control && !e.command && TryHitMeshDeformerSurfaceMoveArea(points, columns, rows, mouse))
            {
                // 曲面内部空白区域：PS / CSP 式整体拖拽，不抢控制点、方向柄、边线。
                PrepareMeshDeformerEditAtCurrentFrame(deformer);
                CaptureMeshSurfaceStartAnchorOffsets(deformer, columns, rows);
                ShowMeshOuterFrame();
                draggingMeshDeformerKey = deformer.key;
                draggingMeshSurfaceStartMouse = mouse;
                draggingMeshSurfaceActive = true;
                draggingMeshPointActive = false;
                meshDeformerLiveEditingDirty = false;
                state.PreviewPanelRigDragging = true;
                MarkMeshDeformerLivePreviewChanged(true);
                e.Use();
            }
            else if (rect.Contains(mouse))
            {
                if (HasMeshAnchorSelection(deformer))
                {
                    ClearMeshAnchorSelection();
                    ShowMeshOuterFrame();
                }
                else
                {
                    HideMeshOuterFrame(deformer);
                }

                GUI.changed = true;
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDrag && draggingMeshPointActive && draggingMeshDeformerKey == deformer.key)
        {
            SkyPrisonMeshDeformPoint point = FindMeshDeformerPoint(deformer, draggingMeshPointX, draggingMeshPointY);
            if (point == null)
                return;

            float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
            Vector2 delta = (e.mousePosition - draggingMeshStartMouse) / Mathf.Max(0.0001f, zoom);

            // Shift：锁定主轴。拖方向柄时也能让切线保持纯水平/纯垂直。
            if (e.shift)
            {
                if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) delta.y = 0f;
                else delta.x = 0f;
            }

            if (draggingMeshHandleKind == "anchor" && draggingMeshSelectedAnchorStartOffsets.Count > 0)
            {
                foreach (KeyValuePair<string, Vector2> pair in draggingMeshSelectedAnchorStartOffsets)
                {
                    int sx;
                    int sy;
                    if (!TryParseMeshAnchorSelectionKey(pair.Key, out sx, out sy))
                        continue;

                    SkyPrisonMeshDeformPoint selectedPoint = FindMeshDeformerPoint(deformer, sx, sy);
                    if (selectedPoint != null)
                        selectedPoint.offset = pair.Value + delta;
                }
            }
            else
            {
                SetMeshDeformerHandleOffset(point, draggingMeshHandleKind, draggingMeshStartOffset + delta);
            }

            meshDeformerLiveEditingDirty = true;
            MarkMeshDeformerLivePreviewChanged(false);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingMeshSurfaceActive && draggingMeshDeformerKey == deformer.key)
        {
            float zoom = state != null ? Mathf.Clamp(state.PreviewZoom, 0.1f, 5f) : 1f;
            Vector2 delta = (e.mousePosition - draggingMeshSurfaceStartMouse) / Mathf.Max(0.0001f, zoom);

            if (e.shift)
            {
                if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) delta.y = 0f;
                else delta.x = 0f;
            }

            ApplyMeshSurfaceAnchorOffsetDelta(deformer, delta);
            meshDeformerLiveEditingDirty = true;
            MarkMeshDeformerLivePreviewChanged(false);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingMeshSurfaceActive && draggingMeshDeformerKey == deformer.key)
        {
            if (meshDeformerLiveEditingDirty)
                SaveMeshDeformerKeyframeAtCurrentFrame(deformer);
            meshDeformerLiveEditingDirty = false;
            ClearMeshDeformDraggingIfNeeded();
            state.PreviewPanelRigDragging = false;
            MarkMeshDeformerLivePreviewChanged(true);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingMeshPointActive && draggingMeshDeformerKey == deformer.key)
        {
            if (meshDeformerLiveEditingDirty)
                SaveMeshDeformerKeyframeAtCurrentFrame(deformer);
            meshDeformerLiveEditingDirty = false;
            ClearMeshDeformDraggingIfNeeded();
            state.PreviewPanelRigDragging = false;
            MarkMeshDeformerLivePreviewChanged(true);
            e.Use();
        }

        if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition))
        {
            int hitX;
            int hitY;
            string hitKind;
            bool hit = TryHitMeshDeformerHandle(deformer, points, columns, rows, e.mousePosition, out hitX, out hitY, out hitKind);
            if (!hit)
            {
                hitKind = "anchor";
                hit = TryHitMeshDeformerAnchor(points, columns, rows, e.mousePosition, out hitX, out hitY);
            }

            if (hit)
            {
                SkyPrisonMeshDeformPoint point = FindMeshDeformerPoint(deformer, hitX, hitY);
                if (point != null)
                {
                    PrepareMeshDeformerEditAtCurrentFrame(deformer);
                    point = FindMeshDeformerPoint(deformer, hitX, hitY);
                    if (point != null)
                    {
                        SetMeshDeformerHandleOffset(point, hitKind, Vector2.zero);
                        SaveMeshDeformerKeyframeAtCurrentFrame(deformer);
                    }
                    GUI.changed = true;
                    e.Use();
                }
            }
        }
    }


    private void AddMeshDeformerSurfaceMoveCursor(SkyPrisonAnimationRigRow deformer, Vector2[,] points, int columns, int rows)
    {
        Event e = Event.current;
        if (e == null || deformer == null || points == null)
            return;
        if (draggingMeshPointActive || draggingMeshOuterActive)
            return;

        Vector2 mouse = e.mousePosition;
        int hitX;
        int hitY;
        string hitKind;
        if (TryHitMeshDeformerHandle(deformer, points, columns, rows, mouse, out hitX, out hitY, out hitKind))
            return;
        if (TryHitMeshDeformerAnchor(points, columns, rows, mouse, out hitX, out hitY))
            return;
        if (!TryHitMeshDeformerSurfaceMoveArea(points, columns, rows, mouse))
            return;

        // IMGUI 的光标区域必须是 Rect；这里仅在鼠标实际位于曲面 cell 内时给鼠标附近注册十字移动光标。
        EditorGUIUtility.AddCursorRect(new Rect(mouse.x - 12f, mouse.y - 12f, 24f, 24f), MouseCursor.MoveArrow);
    }

    private bool TryHitMeshDeformerSurfaceMoveArea(Vector2[,] points, int columns, int rows, Vector2 mouse)
    {
        if (points == null || columns < 2 || rows < 2)
            return false;

        // 控制点 / 方向柄 / 网格边线都不触发整体移动，避免和点编辑、边编辑抢操作。
        if (IsNearAnyMeshDeformerGridLine(points, columns, rows, mouse, 6f))
            return false;

        for (int y = 0; y < rows - 1; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                Vector2 p00 = points[x, y];
                Vector2 p10 = points[x + 1, y];
                Vector2 p11 = points[x + 1, y + 1];
                Vector2 p01 = points[x, y + 1];

                if (PointInTriangle(mouse, p00, p10, p11) || PointInTriangle(mouse, p00, p11, p01))
                    return true;
            }
        }

        return false;
    }

    private bool IsNearAnyMeshDeformerGridLine(Vector2[,] points, int columns, int rows, Vector2 mouse, float threshold)
    {
        float sqr = threshold * threshold;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                if (DistancePointSegmentSqr(mouse, points[x, y], points[x + 1, y]) <= sqr)
                    return true;
            }
        }

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows - 1; y++)
            {
                if (DistancePointSegmentSqr(mouse, points[x, y], points[x, y + 1]) <= sqr)
                    return true;
            }
        }

        return false;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign2D(p, a, b);
        float d2 = Sign2D(p, b, c);
        float d3 = Sign2D(p, c, a);

        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    private static float Sign2D(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static float DistancePointSegmentSqr(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len = ab.sqrMagnitude;
        if (len <= 0.000001f)
            return (p - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
        Vector2 closest = a + ab * t;
        return (p - closest).sqrMagnitude;
    }

    private void CaptureMeshSurfaceStartAnchorOffsets(SkyPrisonAnimationRigRow deformer, int columns, int rows)
    {
        draggingMeshSurfaceStartAnchorOffsets.Clear();
        if (deformer == null)
            return;

        EnsureMeshDeformerPreviewPointGrid(deformer, columns, rows);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                SkyPrisonMeshDeformPoint p = FindMeshDeformerPoint(deformer, x, y);
                if (p != null)
                    draggingMeshSurfaceStartAnchorOffsets[MeshPointKey(x, y, "anchor")] = p.offset;
            }
        }
    }

    private void ApplyMeshSurfaceAnchorOffsetDelta(SkyPrisonAnimationRigRow deformer, Vector2 delta)
    {
        if (deformer == null || draggingMeshSurfaceStartAnchorOffsets.Count == 0)
            return;

        foreach (KeyValuePair<string, Vector2> pair in draggingMeshSurfaceStartAnchorOffsets)
        {
            int x;
            int y;
            if (!TryParseMeshAnchorSelectionKey(pair.Key, out x, out y))
                continue;

            SkyPrisonMeshDeformPoint p = FindMeshDeformerPoint(deformer, x, y);
            if (p != null)
                p.offset = pair.Value + delta;
        }
    }

    private SkyPrisonAnimationRigRow GetActiveMeshDeformerForPreview()
    {
        if (state == null)
            return null;

        SkyPrisonAnimationRigRow selected = state.GetSelectedRigRow();
        if (selected == null)
            return null;

        // 曲面变形只能在“生成出来的曲面变形节点”被选中时操作。
        // 原 PSB / Rig 节点即使下面有曲面子节点，也只显示普通节点，不进入曲面编辑状态。
        if (selected.isMeshDeformer)
            return selected;

        ClearMeshAnchorSelection();
        HideMeshOuterFrame(null);
        return null;
    }

    private SkyPrisonAnimationRigRow FindFirstMeshDeformerForTarget(string targetKey)
    {
        if (state == null || state.RigRows == null || string.IsNullOrEmpty(targetKey))
            return null;

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !row.isMeshDeformer)
                continue;

            if (row.meshDeformTargetKey == targetKey)
                return row;
        }

        return null;
    }

    private bool IsMeshDeformerPreviewAffectedPsb(SkyPrisonAnimationRigRow psb, string targetKey)
    {
        if (psb == null || string.IsNullOrEmpty(targetKey))
            return false;

        // finalRects 通常来自 PSB 行，所以优先看 PSB.boundRigKey。
        // 兼容少数旧缓存：如果图层 key 本身等于目标 key，也允许显示。
        return psb.boundRigKey == targetKey || psb.key == targetKey;
    }

    private void HandleMeshDeformerGlobalBlankClick(SkyPrisonAnimationRigRow deformer, bool drewAny)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0)
            return;
        if (!drewAny || deformer == null)
            return;
        if (e.shift || draggingMeshPointActive || draggingMeshOuterActive)
            return;
        if (!HasMeshAnchorSelection(deformer))
            return;

        // 多选点完成操作后，在预览工作台其它空白位置左键点击，恢复“全体控制”模式。
        // 这里不隐藏红框，只清空选区，让红框回到整张曲面的控制范围。
        ClearMeshAnchorSelection();
        ShowMeshOuterFrame();
        GUI.changed = true;
        e.Use();
    }

    private void DrawMeshDeformerPreviewBadge(SkyPrisonAnimationRigRow deformer, int columns, int rows)
    {
        if (deformer == null)
            return;

        string label = string.Format("曲面变形  {0}×{1}", columns, rows);
        Rect rect = new Rect(12f, 12f, 126f, 22f);
        EditorGUI.DrawRect(rect, new Color(0.05f, 0.08f, 0.10f, 0.72f));
        GUI.Label(rect, label, new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.86f, 0.96f, 1f, 1f) }
        });
    }

    private void DrawPsbLayerSelectionOverlay(Dictionary<SkyPrisonAnimationRigRow, Rect> finalRects)
    {
        hoveredPsbLayerKey = string.Empty;

        // “部位”关闭时，预览窗口不再显示选择框，也不做 hover 命中提示。
        // 这让开关语义变成真正的选择保护，而不是只隐藏一个视觉层。
        if (!state.ShowVisualParts)
            return;

        if (finalRects == null) return;
        Vector2 mouse = Event.current != null ? Event.current.mousePosition : new Vector2(-99999f, -99999f);

        foreach (KeyValuePair<SkyPrisonAnimationRigRow, Rect> kv in finalRects)
        {
            SkyPrisonAnimationRigRow row = kv.Key;
            if (row == null || string.IsNullOrEmpty(row.key)) continue;
            Rect visualRect = GetPreviewFinalVisualRect(kv.Value);
            Rect hotRect = ExpandRect(visualRect, GetPsbLayerPickPadding(visualRect));
            bool selected = row.key == state.LastSelectedPsbLayerKey;
            bool hover = hotRect.Contains(mouse);
            if (hover) hoveredPsbLayerKey = row.key;
            if (!selected && !hover) continue;

            // 选中框只负责“定位/选择提示”，不能参与图层颜色判断。
            // 之前这里用了 0.24 的青色填充，选中图层时会把矩形范围内的角色整体染暗，
            // 看起来像“正常合成方式也被加深”。现在改成极轻 hover 填充 + 选中仅描边。
            Color fill = selected ? new Color(0.18f, 0.82f, 1f, 0.025f) : new Color(1f, 1f, 1f, 0.055f);
            Color border = selected ? new Color(0.20f, 0.95f, 1f, 1f) : new Color(1f, 1f, 1f, 0.38f);
            if (fill.a > 0.001f)
                EditorGUI.DrawRect(visualRect, fill);
            SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(visualRect, border);

            if (selected)
            {
                Rect labelRect = new Rect(visualRect.x, Mathf.Max(0f, visualRect.y - 20f), Mathf.Min(220f, Mathf.Max(86f, visualRect.width)), 18f);
                EditorGUI.DrawRect(labelRect, new Color(0.02f, 0.12f, 0.16f, 0.86f));
                SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(labelRect, new Color(0.20f, 0.95f, 1f, 0.92f));
                GUI.Label(new Rect(labelRect.x + 5f, labelRect.y + 1f, labelRect.width - 10f, 16f), "已选中: " + row.name, EditorStyles.miniBoldLabel);
            }
        }
    }

    private void DrawEnterpriseRigNodes(HumanPose pose, Vector2 center, Rect localView, float zoom)
    {
        // 拖拽命中检测也必须基于未偏移锚点 + 独立端点偏移，保证看到哪条线就拖哪条线。
        Dictionary<string, Vector2> a = BuildDisplayRigAnchorMap(pose, center, localView, zoom, false, false);
        HandleRigDragEvents(a, zoom);
        DrawRootShiftAxisGuide(localView);
    }

    private void DrawRootShiftAxisGuide(Rect localView)
    {
        if (!draggingRootShiftGuideVisible || !draggingBoneRootHandle || string.IsNullOrEmpty(draggingBoneSegmentKey))
            return;

        Vector2 origin = draggingRootShiftGuideOrigin;
        if (origin == Vector2.zero)
            origin = draggingManualRigStartMouse;

        Color active = new Color(0.18f, 0.62f, 1f, 0.95f);
        Color inactive = new Color(0.18f, 0.62f, 1f, 0.30f);

        if (!draggingRootShiftGuideHasAxis)
        {
            DrawHorizontalDashedLine(origin.y, localView.xMin, localView.xMax, inactive, 1.5f, 9f, 5f);
            DrawVerticalDashedLine(origin.x, localView.yMin, localView.yMax, inactive, 1.5f, 9f, 5f);
            return;
        }

        if (draggingRootShiftGuideHorizontal)
            DrawHorizontalDashedLine(origin.y, localView.xMin, localView.xMax, active, 2f, 10f, 5f);
        else
            DrawVerticalDashedLine(origin.x, localView.yMin, localView.yMax, active, 2f, 10f, 5f);
    }

    private void DrawRigSegment(Texture2D boneTex, Dictionary<string, RigBoneSegment> segments, string segmentKey, float width, Color color)
    {
        if (segments == null || !segments.TryGetValue(segmentKey, out RigBoneSegment seg))
            return;
        if (!IsRigSegmentVisible(seg.rootKey, seg.headKey))
            return;

        color = ApplyPreviewFocusColor(color, IsPreviewFocusSegment(seg));
        DrawBoneIconSegment(boneTex, seg.root, seg.head, width, color);
    }

    private void DrawRigSegmentEndpoint(Dictionary<string, RigBoneSegment> segments, string segmentKey, bool root, float radius, Color color, string tooltip)
    {
        if (segments == null || !segments.TryGetValue(segmentKey, out RigBoneSegment seg))
            return;
        if (!IsRigSegmentVisible(seg.rootKey, seg.headKey))
            return;

        Vector2 actual = root ? seg.root : seg.head;
        Vector2 handlePoint = GetBoneEndpointHandlePoint(seg, root);
        Color handleColor = root
            ? new Color(0.18f, 0.88f, 1.00f, 0.96f)   // 尾部 / Root：青色点
            : new Color(0.74f, 1.00f, 0.22f, 0.96f);  // 头部 / Head：黄绿色点

        bool focused = IsPreviewFocusSegment(seg);
        color = ApplyPreviewFocusColor(color, focused);
        handleColor = ApplyPreviewFocusColor(handleColor, focused);

        if (root && Vector2.Distance(actual, handlePoint) > 0.1f)
            DrawEndpointGuideLine(actual, handlePoint, new Color(0.18f, 0.88f, 1.00f, 0.36f));

        DrawBoneJoint(handlePoint, radius + (root ? 0.6f : 0f), handleColor);

        Vector2 vp = VisualPoint(handlePoint);
        Rect hot = new Rect(vp.x - 9f, vp.y - 9f, 18f, 18f);
        GUI.Label(hot, new GUIContent(string.Empty, tooltip));
    }

    private void DrawArmChainEndpoints(Dictionary<string, RigBoneSegment> segments, string suffix, float jointRadius, Color color)
    {
        DrawRigSegmentEndpoint(segments, "Shoulder_" + suffix, true, jointRadius, color, "Shoulder_" + suffix + " Root / local offset from Chest");
        DrawRigSegmentEndpoint(segments, "Shoulder_" + suffix, false, jointRadius, color, "Shoulder_" + suffix + " Head / Elbow joint");
        DrawRigSegmentEndpoint(segments, "Elbow_" + suffix, true, jointRadius * 0.92f, color, "Elbow_" + suffix + " Root / local offset from Shoulder");
        DrawRigSegmentEndpoint(segments, "Elbow_" + suffix, false, jointRadius * 0.92f, color, "Elbow_" + suffix + " Head / Wrist joint");
        DrawRigSegmentEndpoint(segments, "Wrist_" + suffix, true, jointRadius * 0.86f, color, "Wrist_" + suffix + " Root / local offset from Elbow");
        DrawRigSegmentEndpoint(segments, "Wrist_" + suffix, false, jointRadius * 0.86f, color, "Wrist_" + suffix + " Head / HandEnd point");
    }

    private void DrawLegChainEndpoints(Dictionary<string, RigBoneSegment> segments, string suffix, float jointRadius, Color color)
    {
        DrawRigSegmentEndpoint(segments, "Hip_" + suffix, true, jointRadius, color, "Hip_" + suffix + " Root / local offset from Pelvis");
        DrawRigSegmentEndpoint(segments, "Hip_" + suffix, false, jointRadius, color, "Hip_" + suffix + " Head / Knee joint");
        DrawRigSegmentEndpoint(segments, "Knee_" + suffix, true, jointRadius * 0.92f, color, "Knee_" + suffix + " Root / local offset from Hip");
        DrawRigSegmentEndpoint(segments, "Knee_" + suffix, false, jointRadius * 0.92f, color, "Knee_" + suffix + " Head / Ankle joint");
        DrawRigSegmentEndpoint(segments, "Ankle_" + suffix, true, jointRadius * 0.86f, color, "Ankle_" + suffix + " Root / local offset from Knee");
        DrawRigSegmentEndpoint(segments, "Ankle_" + suffix, false, jointRadius * 0.86f, color, "Ankle_" + suffix + " Head / Foot tip point");
    }

    private Vector2 GetBoneEndpointHandlePoint(RigBoneSegment seg, bool root)
    {
        if (!root)
            return seg.head;

        // Pelvis Root 是整棵骨架的真实根，不需要侧偏；其他子骨骼 Root 常与父骨骼 Head 重合，
        // 因此只在显示/命中层做轻微侧偏，实际计算仍使用 seg.root。
        if (seg.segmentKey == "Pelvis")
            return seg.root;

        Vector2 dir = seg.head - seg.root;
        if (dir.sqrMagnitude < 0.0001f)
            return seg.root;

        dir.Normalize();
        Vector2 normal = new Vector2(-dir.y, dir.x);
        float side = (seg.segmentKey == "Chest" || seg.segmentKey == "Head") ? -1f : 1f;
        return seg.root + normal * side * 10f;
    }

    private void DrawEndpointGuideLine(Vector2 from, Vector2 to, Color color)
    {
        Vector2 a = VisualPoint(from);
        Vector2 b = VisualPoint(to);

        Rect clip = currentPreviewClipRect.width > 1f && currentPreviewClipRect.height > 1f
            ? currentPreviewClipRect
            : new Rect(0f, 0f, 100000f, 100000f);

        if (!ClipLineToRect(ref a, ref b, ExpandRect(clip, 1f)))
            return;

        DrawSafeRotatedRectLine(a, b, 1.5f, color);
    }

    private void DrawRigChain(Texture2D boneTex, Dictionary<string, Vector2> anchors, float width, Color color, params string[] keys)
    {
        if (keys == null || keys.Length < 2)
            return;

        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (!IsRigSegmentVisible(keys[i], keys[i + 1]))
                continue;

            if (!anchors.TryGetValue(keys[i], out Vector2 a) || !anchors.TryGetValue(keys[i + 1], out Vector2 b))
                continue;

            DrawBoneIconSegment(boneTex, a, b, width, color);
        }
    }

    private void DrawRigJoint(Dictionary<string, Vector2> anchors, string key, float radius, Color color, string tooltip)
    {
        if (!IsRigRowEffectivelyVisible(key))
            return;

        if (!anchors.TryGetValue(key, out Vector2 p))
            return;

        DrawBoneJoint(p, radius, color);

        Vector2 vp = VisualPoint(p);
        Rect hot = new Rect(vp.x - 8f, vp.y - 8f, 16f, 16f);
        GUI.Label(hot, new GUIContent(string.Empty, tooltip));
    }

    private void DrawCompactNode(Dictionary<string, Vector2> anchors, string key, float radius, Color color)
    {
        if (!IsRigRowEffectivelyVisible(key))
            return;

        if (!anchors.TryGetValue(key, out Vector2 p))
            return;

        DrawNode(p, radius, color, key);
    }


    private void DrawBoneIconSegment(Texture2D icon, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 originalA = VisualPoint(a);
        Vector2 originalB = VisualPoint(b);

        Rect clip = currentPreviewClipRect.width > 1f && currentPreviewClipRect.height > 1f
            ? currentPreviewClipRect
            : new Rect(0f, 0f, 100000f, 100000f);

        Vector2 clippedA = originalA;
        Vector2 clippedB = originalB;
        if (!ClipLineToRect(ref clippedA, ref clippedB, ExpandRect(clip, 1f)))
            return;

        float safeWidth = Mathf.Clamp(width, 4f, 18f);
        float visibleLength = (clippedB - clippedA).magnitude;
        if (visibleLength < 0.75f)
            return;

        // 关键安全规则：100号骨骼贴图只在整条骨骼线都位于预览区内时绘制。
        // 如果端点越界，旋转贴图矩形在 IMGUI 中无法可靠裁剪，会被画到错误位置甚至触发 Repaint 卡死。
        bool fullyInside = clip.Contains(originalA) && clip.Contains(originalB);
        // 旋转贴图在 IMGUI 中是风险点：只允许较短、完全在可视区内的骨骼段使用贴图。
        // 长段或越界段一律走纯色裁剪线，避免放大后 Repaint 卡死。
        float maxReasonableLength = Mathf.Min(520f, Mathf.Sqrt(clip.width * clip.width + clip.height * clip.height) * 0.90f);
        bool safeToDrawTexture = fullyInside && icon != null && visibleLength <= maxReasonableLength;

        DrawSafeRotatedRectLine(
            clippedA,
            clippedB,
            Mathf.Max(1f, safeWidth * 0.52f),
            new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * 0.20f))
        );

        if (safeToDrawTexture)
            DrawSafeRotatedTextureLine(icon, originalA, originalB, safeWidth, color);
        else
            DrawSafeRotatedRectLine(clippedA, clippedB, Mathf.Max(2f, safeWidth * 0.72f), color);
    }

    private void DrawSafeRotatedTextureLine(Texture2D texture, Vector2 a, Vector2 b, float width, Color color)
    {
        if (!IsFiniteVector(a) || !IsFiniteVector(b))
            return;

        Vector2 dir = b - a;
        float length = dir.magnitude;
        if (length < 0.75f || length > 20000f || texture == null)
            return;

        // SkyPrisonEditor_100.png 是竖向骨骼线素材：贴图长轴在 Y 方向。
        // 注意：这个函数只允许在整条线完全位于预览区内时调用。越界线段请走纯色裁剪线。
        Vector2 mid = (a + b) * 0.5f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

        Rect r = new Rect(mid.x - width * 0.5f, mid.y - length * 0.5f, width, length);

        // 即使端点在可视区内，旋转后的包围盒也可能越界。越界时不要画旋转贴图。
        if (currentPreviewClipRect.width > 1f && currentPreviewClipRect.height > 1f)
        {
            Rect safeClip = ExpandRect(currentPreviewClipRect, 2f);
            Rect bound = new Rect(
                Mathf.Min(a.x, b.x) - width,
                Mathf.Min(a.y, b.y) - width,
                Mathf.Abs(a.x - b.x) + width * 2f,
                Mathf.Abs(a.y - b.y) + width * 2f
            );

            if (!safeClip.Contains(new Vector2(bound.xMin, bound.yMin)) ||
                !safeClip.Contains(new Vector2(bound.xMax, bound.yMax)))
            {
                DrawSafeRotatedRectLine(a, b, Mathf.Max(2f, width * 0.72f), color);
                return;
            }
        }

        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;

        GUIUtility.RotateAroundPivot(angle, mid);
        GUI.color = color;
        GUI.DrawTexture(r, texture, ScaleMode.StretchToFill, true);

        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }

    private void DrawSafeRotatedRectLine(Vector2 a, Vector2 b, float width, Color color)
    {
        if (!IsFiniteVector(a) || !IsFiniteVector(b))
            return;

        Vector2 dir = b - a;
        float length = dir.magnitude;
        if (length < 0.75f || length > 20000f)
            return;

        Vector2 mid = (a + b) * 0.5f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Rect r = new Rect(mid.x - length * 0.5f, mid.y - width * 0.5f, length, width);

        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        GUIUtility.RotateAroundPivot(angle, mid);
        GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }

    private Rect ExpandRect(Rect rect, float pad)
    {
        return new Rect(rect.xMin - pad, rect.yMin - pad, rect.width + pad * 2f, rect.height + pad * 2f);
    }

    private int GetLineClipCode(Vector2 p, Rect rect)
    {
        int code = 0;
        if (p.x < rect.xMin) code |= 1;
        else if (p.x > rect.xMax) code |= 2;
        if (p.y > rect.yMax) code |= 4;
        else if (p.y < rect.yMin) code |= 8;
        return code;
    }

    private bool IsFiniteVector(Vector2 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.x) || float.IsInfinity(v.y));
    }

    private bool ClipLineToRect(ref Vector2 a, ref Vector2 b, Rect rect)
    {
        // Liang-Barsky 裁剪：没有 while(true)，不会因为端点超出预览区后进入死循环。
        // 旧版 Cohen-Sutherland 在 dx/dy 为负数时用 Mathf.Max 修正分母，会算出错误交点；
        // 骨骼线拖到屏幕外后可能反复重算，Unity 就卡在 Repaint。
        if (rect.width <= 0.1f || rect.height <= 0.1f)
            return false;
        if (!IsFiniteVector(a) || !IsFiniteVector(b))
            return false;

        float x0 = a.x;
        float y0 = a.y;
        float x1 = b.x;
        float y1 = b.y;
        float dx = x1 - x0;
        float dy = y1 - y0;

        float t0 = 0f;
        float t1 = 1f;

        if (!ClipTest(-dx, x0 - rect.xMin, ref t0, ref t1)) return false;
        if (!ClipTest( dx, rect.xMax - x0, ref t0, ref t1)) return false;
        if (!ClipTest(-dy, y0 - rect.yMin, ref t0, ref t1)) return false;
        if (!ClipTest( dy, rect.yMax - y0, ref t0, ref t1)) return false;

        Vector2 na = new Vector2(x0 + dx * t0, y0 + dy * t0);
        Vector2 nb = new Vector2(x0 + dx * t1, y0 + dy * t1);
        if (!IsFiniteVector(na) || !IsFiniteVector(nb))
            return false;

        a = na;
        b = nb;
        return true;
    }

    private bool ClipTest(float p, float q, ref float t0, ref float t1)
    {
        const float Epsilon = 0.000001f;

        if (Mathf.Abs(p) < Epsilon)
            return q >= 0f;

        float r = q / p;
        if (p < 0f)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }

        return true;
    }

    private void DrawBoneJoint(Vector2 center, float radius, Color color)
    {
        center = VisualPoint(center);
        radius = Mathf.Max(2.5f, radius);
        DrawGuiCircle(center, radius, color, new Color(1f, 1f, 1f, 0.72f));
    }

    private bool DrawIconToggleButton(Rect rect, bool value, int iconNumber, string label, string tooltip, Color iconTint)
    {
        Event e = Event.current;
        bool clicked = false;

        GUIContent empty = new GUIContent(string.Empty, tooltip);
        GUI.Toggle(rect, value, empty, EditorStyles.toolbarButton);

        if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            clicked = true;
            e.Use();
        }

        Texture2D icon = GetEditorIcon(iconNumber);
        float iconSize = Mathf.Min(16f, rect.height - 6f);
        Rect iconRect = new Rect(rect.x + 6f, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);

        Color oldColor = GUI.color;
        GUI.color = iconTint;
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            GUI.Label(iconRect, iconNumber.ToString(), EditorStyles.miniLabel);
        GUI.color = oldColor;

        Rect labelRect = new Rect(iconRect.xMax + 4f, rect.y + 3f, rect.width - iconSize - 12f, rect.height - 6f);
        GUI.Label(labelRect, label, EditorStyles.miniLabel);

        return clicked ? !value : value;
    }

    private static Sprite LoadSprite(string assetPath, string spriteName)
    {
        if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(spriteName))
            return null;

        string cacheKey = assetPath + "::" + spriteName;
        if (SpriteCache.TryGetValue(cacheKey, out Sprite cached))
            return cached;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Sprite sprite && sprite.name == spriteName)
            {
                SpriteCache[cacheKey] = sprite;
                return sprite;
            }
        }

        Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (direct != null && (direct.name == spriteName || string.IsNullOrEmpty(spriteName)))
        {
            SpriteCache[cacheKey] = direct;
            return direct;
        }

        SpriteCache[cacheKey] = null;
        return null;
    }

    private Texture2D GetCircleTexture()
    {
        if (CircleTextureCache != null)
            return CircleTextureCache;

        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 c = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float r = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01(r + 0.5f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply(false, true);
        CircleTextureCache = tex;
        return CircleTextureCache;
    }

    private void DrawGuiCircle(Vector2 center, float radius, Color fill, Color border)
    {
        if (radius <= 0.1f)
            return;

        Rect circleRect = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);

        if (currentPreviewClipRect.width > 1f && currentPreviewClipRect.height > 1f)
        {
            Rect expanded = ExpandRect(currentPreviewClipRect, radius + 2f);
            if (!expanded.Overlaps(circleRect))
                return;
        }

        Texture2D circle = GetCircleTexture();
        Color old = GUI.color;

        GUI.color = fill;
        GUI.DrawTexture(circleRect, circle, ScaleMode.StretchToFill, true);

        if (border.a > 0.001f && radius > 3f)
        {
            float borderRadius = Mathf.Max(1f, radius - 1.2f);
            Rect borderRect = new Rect(center.x - borderRadius, center.y - borderRadius, borderRadius * 2f, borderRadius * 2f);
            GUI.color = border;
            GUI.DrawTexture(borderRect, circle, ScaleMode.StretchToFill, true);

            float innerRadius = Mathf.Max(0.1f, borderRadius - 1.4f);
            Rect innerRect = new Rect(center.x - innerRadius, center.y - innerRadius, innerRadius * 2f, innerRadius * 2f);
            GUI.color = fill;
            GUI.DrawTexture(innerRect, circle, ScaleMode.StretchToFill, true);
        }

        GUI.color = old;
    }

    private static Texture2D GetEditorIcon(int iconNumber)
    {
        if (IconCache.TryGetValue(iconNumber, out Texture2D cached))
            return cached;

        string path = IconFolder + "SkyPrisonEditor_" + iconNumber + ".png";
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        IconCache[iconNumber] = tex;
        return tex;
    }

    private Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private void DrawNode(Vector2 center, float radius, Color color, string label)
    {
        center = VisualPoint(center);
        radius = Mathf.Max(3f, radius);
        DrawGuiCircle(center, radius, color, new Color(1f, 1f, 1f, 0.70f));
        GUI.Label(new Rect(center.x - 42f, center.y + radius + 2f, 84f, 18f), label, EditorStyles.centeredGreyMiniLabel);
    }
}
