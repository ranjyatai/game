using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public enum SkyPrisonAnimationStructureTab { Rig, PsbLayer, Socket }
public enum SkyPrisonAnimationFormulaType { Sine, AbsSine, Infinity, Shake }
public enum SkyPrisonMotionArcEasePreset { Linear, Smooth, Soft, Elastic }

[Serializable]
public class SkyPrisonAnimationActionGroupRow
{
    public string key = "Group";
    public string name = "动作组";
    public bool expanded = true;
}

[Serializable]
public class SkyPrisonAnimationActionRow
{
    public string key, name, type, status;
    public bool loop;
    public float duration;
    // 新版动作列表支持动作组。旧文件没有这个字段时会保持空，运行时由 EnsureActionGroups() 自动迁移，不影响读取。
    public string groupKey = "";
}

[Serializable]
public class SkyPrisonAnimationMotionKeyframe
{
    public string actionKey = "";
    public int frame = 0;
    public Vector2 visualOffset = Vector2.zero;

    public SkyPrisonAnimationMotionKeyframe Clone()
    {
        return new SkyPrisonAnimationMotionKeyframe
        {
            actionKey = actionKey,
            frame = frame,
            visualOffset = visualOffset
        };
    }
}

[Serializable]
public class SkyPrisonAnimationLayerOrderKeyframe
{
    public string actionKey = "";
    public string layerKey = "";
    public float time = 0f;
    public float orderWeight = 0f;
}

[Serializable]
public class SkyPrisonAnimationTimelineKeyframe
{
    public string actionKey = "";
    public string targetKey = "";
    public string targetName = "";
    public string targetKind = "Rig";
    // PSB 图层权重可以由绑定的 Rig 关键帧单独覆盖。
    // targetKey 仍然是 Rig 轨道 Key；layerWeightTargetKey 记录真正被覆盖的 PSB 图层 Key。
    public string layerWeightTargetKey = "";
    public int frame = 0;

    // 当前第一版关键帧记录“姿态偏移 + 图层显示参数”。
    // Rig 行主要吃 runtimeOffset；PSB 行主要吃 opacity / layerWeight。
    public Vector2 runtimeOffset = Vector2.zero;
    public bool useRuntimeBoneRootOffset = false;
    public Vector2 runtimeBoneRootOffset = Vector2.zero;
    public bool useRuntimeBoneHeadOffset = false;
    public Vector2 runtimeBoneHeadOffset = Vector2.zero;
    public float opacity = 1f;
    public float layerWeight = 0f;
    public float manualLayerWeightOffset = 0f;

    // 曲面变形关键帧。每个关键帧保存当前网格规格与所有主控制点/方向柄偏移。
    // 这样曲面变形可以像骨骼关键帧一样在时间线上插值过渡。
    public bool useMeshDeform = false;
    public int meshDeformColumns = 0;
    public int meshDeformRows = 0;
    public List<SkyPrisonMeshDeformPoint> meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();

    public SkyPrisonAnimationTimelineKeyframe Clone()
    {
        return new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = actionKey,
            targetKey = targetKey,
            targetName = targetName,
            targetKind = targetKind,
            layerWeightTargetKey = layerWeightTargetKey,
            frame = frame,
            runtimeOffset = runtimeOffset,
            useRuntimeBoneRootOffset = useRuntimeBoneRootOffset,
            runtimeBoneRootOffset = runtimeBoneRootOffset,
            useRuntimeBoneHeadOffset = useRuntimeBoneHeadOffset,
            runtimeBoneHeadOffset = runtimeBoneHeadOffset,
            opacity = opacity,
            layerWeight = layerWeight,
            manualLayerWeightOffset = manualLayerWeightOffset,
            useMeshDeform = useMeshDeform,
            meshDeformColumns = meshDeformColumns,
            meshDeformRows = meshDeformRows,
            meshDeformPoints = CloneMeshDeformPoints(meshDeformPoints)
        };
    }

    public static List<SkyPrisonMeshDeformPoint> CloneMeshDeformPoints(List<SkyPrisonMeshDeformPoint> source)
    {
        List<SkyPrisonMeshDeformPoint> result = new List<SkyPrisonMeshDeformPoint>();
        if (source == null)
            return result;

        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null)
                result.Add(source[i].Clone());
        }
        return result;
    }
}


[Serializable]
public class SkyPrisonManualPoseAngle
{
    public string rigKey = "";
    public float angle = 0f;
}

[Serializable]
public class SkyPrisonManualPoseKey
{
    public int frame = 0;
    public string label = "姿势点";
    public List<SkyPrisonManualPoseAngle> angles = new List<SkyPrisonManualPoseAngle>();

    public float GetAngle(string rigKey)
    {
        if (string.IsNullOrEmpty(rigKey) || angles == null) return 0f;
        for (int i = 0; i < angles.Count; i++)
        {
            SkyPrisonManualPoseAngle a = angles[i];
            if (a != null && a.rigKey == rigKey)
                return Mathf.Clamp(a.angle, -180f, 180f);
        }
        return 0f;
    }
}

[Serializable]
public class SkyPrisonPhysicsOscillator
{
    public float length = 7f;
    public float swayEase = 0.8f;
    public float reactionSpeed = 0.9f;
    public float returnSpeed = 1.5f;
    public float damping = 0.35f;
    public float weight = 1f;

    public SkyPrisonPhysicsOscillator Clone()
    {
        return new SkyPrisonPhysicsOscillator { length = length, swayEase = swayEase, reactionSpeed = reactionSpeed, returnSpeed = returnSpeed, damping = damping, weight = weight };
    }
}

[Serializable]
public class SkyPrisonPhysicsPreset
{
    public string presetKey = "hair_side_3";
    public string displayName = "侧发_3节";
    public int oscillatorCount = 3;
    public float globalScale = 1f;
    public float gravityAngle = -90f;
    public float gravityStrength = 1f;
    public float windInfluence = 0f;
    public float velocityInfluence = 1f;
    public float defaultBlend = 0.5f;
    public List<SkyPrisonPhysicsOscillator> oscillators = new List<SkyPrisonPhysicsOscillator>();

    public SkyPrisonPhysicsPreset Clone()
    {
        SkyPrisonPhysicsPreset c = new SkyPrisonPhysicsPreset
        {
            presetKey = presetKey,
            displayName = displayName,
            oscillatorCount = oscillatorCount,
            globalScale = globalScale,
            gravityAngle = gravityAngle,
            gravityStrength = gravityStrength,
            windInfluence = windInfluence,
            velocityInfluence = velocityInfluence,
            defaultBlend = defaultBlend,
            oscillators = new List<SkyPrisonPhysicsOscillator>()
        };
        if (oscillators != null)
            for (int i = 0; i < oscillators.Count; i++)
                if (oscillators[i] != null) c.oscillators.Add(oscillators[i].Clone());
        c.EnsureOscillatorCount();
        return c;
    }

    public void EnsureOscillatorCount()
    {
        oscillatorCount = Mathf.Clamp(oscillatorCount, 1, 12);
        if (oscillators == null) oscillators = new List<SkyPrisonPhysicsOscillator>();
        while (oscillators.Count < oscillatorCount) oscillators.Add(new SkyPrisonPhysicsOscillator());
        while (oscillators.Count > oscillatorCount) oscillators.RemoveAt(oscillators.Count - 1);
    }
}

[Serializable]
public class SkyPrisonPhysicsOscillatorStatus
{
    public string rowKey = "";
    public string sourceKey = "";
    public string presetName = "";
    public bool active = false;
    public float inputAngle = 0f;
    public float outputAngle = 0f;
    public float offsetAmount = 0f;
    public readonly List<Vector2> points = new List<Vector2>();
}

[Serializable]
public class SkyPrisonMeshDeformPoint
{
    public int x = 0;
    public int y = 0;

    // 主控制点偏移：移动这个点会改变网格交点本身。
    public Vector2 offset = Vector2.zero;

    // Bezier 方向柄偏移：不移动网格交点，只改变相邻边的切线方向。
    // 默认方向柄位置由相邻点间距自动计算，这里只保存用户额外拖动的偏移量。
    public Vector2 handleLeftOffset = Vector2.zero;
    public Vector2 handleRightOffset = Vector2.zero;
    public Vector2 handleUpOffset = Vector2.zero;
    public Vector2 handleDownOffset = Vector2.zero;

    public SkyPrisonMeshDeformPoint Clone()
    {
        return new SkyPrisonMeshDeformPoint
        {
            x = x,
            y = y,
            offset = offset,
            handleLeftOffset = handleLeftOffset,
            handleRightOffset = handleRightOffset,
            handleUpOffset = handleUpOffset,
            handleDownOffset = handleDownOffset,
        };
    }
}


public enum SkyPrisonAnimationShaderPropertyKind
{
    Float = 0,
    Range = 1,
    Color = 2,
    Vector = 3,
    Texture = 4,
}

[Serializable]
public class SkyPrisonAnimationShaderPropertyOverride
{
    public string propertyName = string.Empty;
    public string displayName = string.Empty;
    public SkyPrisonAnimationShaderPropertyKind propertyKind = SkyPrisonAnimationShaderPropertyKind.Float;
    public float floatValue = 0f;
    public Color colorValue = Color.white;
    public Vector4 vectorValue = Vector4.zero;
    public Texture textureValue;
    public bool enabled = true;

    public SkyPrisonAnimationShaderPropertyOverride Clone()
    {
        return new SkyPrisonAnimationShaderPropertyOverride
        {
            propertyName = propertyName,
            displayName = displayName,
            propertyKind = propertyKind,
            floatValue = floatValue,
            colorValue = colorValue,
            vectorValue = vectorValue,
            textureValue = textureValue,
            enabled = enabled,
        };
    }
}

[Serializable]
public class SkyPrisonAnimationRigRow {
    public string key, name, semantic; public int depth; public bool visible = true, locked, hasKey, mapped = true;
    public string parentKey = ""; public bool isFolder, expanded = true;
    public float opacity = 1f; public string blendMode = "正常", maskReferenceKey = "";
    public Shader renderShader; public string shaderKey = "";
    public string shaderParameterShaderKey = "";
    public bool shaderParametersExpanded = true;
    public List<SkyPrisonAnimationShaderPropertyOverride> shaderParameters = new List<SkyPrisonAnimationShaderPropertyOverride>();
    public bool useGameDyeRegion, isDyeRegion; public string dyeRegionKey = "无"; public Color dyePreviewColor = Color.white;
    public string visualSlotKey = "Body", slotKey = "Body"; public bool hideBaseBodyPart, hideBody; public string boundEquipmentKey = "", equipmentSourceKey = "";
    public Color previewColor = new Color(.75f,.78f,.82f,1f); public int previewIconNumber = 42;
    public string sourceAssetPath = "", sourceSpriteName = "", sourceLayerPath = "";
    public bool fromAppearanceSlot = false;
    public string appearanceSlotKey = "", appearanceLayerKey = "";
    public string boundRigKey = "", boundRigName = "", bindMode = "未绑定"; public float bindConfidence = 0f;
    public bool useManualRigOffset = false; public Vector2 manualRigOffset = Vector2.zero;
    public bool useManualRigLayerOffset = false; public Vector2 manualRigLayerOffset = Vector2.zero;
    // 每条骨骼线独立保存自己的根/头端点偏移。segmentKey 使用骨骼线 Key，例如 Spine 表示 Pelvis->Spine。
    public bool useManualBoneRootOffset = false; public Vector2 manualBoneRootOffset = Vector2.zero;
    public bool useManualBoneHeadOffset = false; public Vector2 manualBoneHeadOffset = Vector2.zero;
    public bool useRuntimeBoneRootOffset = false; public Vector2 runtimeBoneRootOffset = Vector2.zero;
    public bool useRuntimeBoneHeadOffset = false; public Vector2 runtimeBoneHeadOffset = Vector2.zero;

    // 节点物理参数：这里只保存“这个节点是否参与物理求值”和物理影响强度。
    // 真正的运行时/预览物理解算后续统一读取这两个字段，不在绘制层写隐藏逻辑。
    public bool usePhysicsInfluence = false;
    public float physicsInfluenceStrength = 0.35f;
    public string physicsPresetKey = "";
    public float physicsLocalDelayMultiplier = 1f;
    public float physicsLocalSwingMultiplier = 1f;

    // 自定义模板手绘骨骼线：RigRows 中的一行就是一条骨骼线。
    // 坐标保存为预览画布局部坐标，不吃屏幕缩放；显示时用 canvasOrigin + point * zoom 还原。
    public bool useCustomBoneLine = false;
    public Vector2 customBoneRoot = Vector2.zero;
    public Vector2 customBoneHead = new Vector2(0f, -48f);

    // 曲面变形节点：它本身不替代原节点，只作为原节点的子控制器。
    // 第一版先保存目标节点 + N×M 网格 + 控制点偏移；渲染层可按 targetKey 找到它来绘制/编辑网格。
    public bool isMeshDeformer = false;
    public string meshDeformTargetKey = "";
    public int meshDeformColumns = 3;
    public int meshDeformRows = 3;
    public List<SkyPrisonMeshDeformPoint> meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();
    // 红框伸缩规则：中心对称伸缩=以中心对称缩放；固定对边/对角=保持对边/对角固定。
    public string meshDeformScaleRule = "中心对称伸缩";
    // 曲面贴图亮度补偿。用于抵消 RT / GUI 色彩空间差异导致的发白，1 为不补偿。
    public float meshDeformTextureBrightness = 1f;

    public bool usePsbLayerWeight = true; public float psbLayerWeight = 0f, manualLayerWeightOffset = 0f;

    public SkyPrisonAnimationRigRow Clone()
    {
        SkyPrisonAnimationRigRow c = (SkyPrisonAnimationRigRow)MemberwiseClone();
        c.meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();
        if (meshDeformPoints != null)
        {
            for (int i = 0; i < meshDeformPoints.Count; i++)
            {
                if (meshDeformPoints[i] != null)
                    c.meshDeformPoints.Add(meshDeformPoints[i].Clone());
            }
        }
        return c;
    }
}

[Serializable]
public class SkyPrisonAppearanceDyeChannel
{
    public string channelKey = "main";
    public string scopeKey = "";
    public string displayName = "主色";
    public string maskChannel = "R";
    public bool enabled = true;
    public Color previewColor = Color.white;

    public SkyPrisonAppearanceDyeChannel Clone()
    {
        return new SkyPrisonAppearanceDyeChannel
        {
            channelKey = channelKey,
            scopeKey = scopeKey,
            displayName = displayName,
            maskChannel = maskChannel,
            enabled = enabled,
            previewColor = previewColor
        };
    }
}

[Serializable]
public class SkyPrisonAppearancePsbLayerNode
{
    public string key = "";
    public string parentKey = "";
    public string name = "";
    public string sourceLayerPath = "";
    public string sourceSpriteName = "";
    public string sourceAssetPath = "";
    public bool isFolder = false;
    public bool expanded = true;
    public bool visible = true;
    public int depth = 0;

    // 解析语义：绑定区域 / 外貌槽位 / 衣物类型 / 段位 / 左右 / 排序。
    public string bodyRegion = "";       // body / arm_L / arm_R / leg_L / leg_R ...
    public string slotKey = "";          // top / bottom / sock / shoe / weapon ...
    public string partType = "";         // jacket / skirt / sock / shoe ...
    public string side = "";             // L / R / Center
    public string segment = "";          // upper / lower / foot / hand ...
    public string sortLayer = "Normal";  // Normal / BehindBody / FrontBody / Mask
    public string bindMode = "未绑定";    // HardBind / TwoPointBind / SurfaceBind / Mask
    public string bindTargetKey = "";
    public string bindTargetName = "";
    public string bindStartKey = "";
    public string bindEndKey = "";
    public float bindConfidence = 0f;
    public bool autoRecognized = false;

    // 染色遮罩：xxx_dyeMask 不显示、不独立绑定，只作为 xxx 的附属纹理，和原图共用曲面变形。
    public bool isDyeMask = false;
    public string dyeMaskForLayerKey = "";
    public string dyeMaskLayerKey = "";
    public bool hasDyeMask = false;
    public bool dyeChannelR = false;
    public bool dyeChannelG = false;
    public bool dyeChannelB = false;
    public bool dyeChannelA = false;

    public List<SkyPrisonAppearancePsbLayerNode> children = new List<SkyPrisonAppearancePsbLayerNode>();

    public SkyPrisonAppearancePsbLayerNode Clone()
    {
        SkyPrisonAppearancePsbLayerNode c = (SkyPrisonAppearancePsbLayerNode)MemberwiseClone();
        c.children = new List<SkyPrisonAppearancePsbLayerNode>();
        if (children != null)
        {
            for (int i = 0; i < children.Count; i++)
                if (children[i] != null) c.children.Add(children[i].Clone());
        }
        return c;
    }
}

[Serializable]
public class SkyPrisonAnimationAssemblySlot
{
    public string slotKey, displayName, assetKey, boundPartKey, visualSlotKey;
    public bool visible = true;
    public string dyeSetKey = "无";

    public string appearancePackageKey = "";
    public string appearanceSourceAssetPath = "";
    public string selectedAppearanceLayerKey = "";
    public List<SkyPrisonAppearancePsbLayerNode> appearanceLayers = new List<SkyPrisonAppearancePsbLayerNode>();
    public List<SkyPrisonAppearanceDyeChannel> dyeChannels = new List<SkyPrisonAppearanceDyeChannel>();

    public SkyPrisonAnimationAssemblySlot Clone()
    {
        SkyPrisonAnimationAssemblySlot c = new SkyPrisonAnimationAssemblySlot
        {
            slotKey = slotKey,
            displayName = displayName,
            assetKey = assetKey,
            boundPartKey = boundPartKey,
            visualSlotKey = visualSlotKey,
            visible = visible,
            dyeSetKey = dyeSetKey,
            appearancePackageKey = appearancePackageKey,
            appearanceSourceAssetPath = appearanceSourceAssetPath,
            selectedAppearanceLayerKey = selectedAppearanceLayerKey,
            appearanceLayers = new List<SkyPrisonAppearancePsbLayerNode>(),
            dyeChannels = new List<SkyPrisonAppearanceDyeChannel>()
        };
        if (appearanceLayers != null)
            for (int i = 0; i < appearanceLayers.Count; i++)
                if (appearanceLayers[i] != null) c.appearanceLayers.Add(appearanceLayers[i].Clone());
        if (dyeChannels != null)
            for (int i = 0; i < dyeChannels.Count; i++)
                if (dyeChannels[i] != null) c.dyeChannels.Add(dyeChannels[i].Clone());
        return c;
    }
}

public sealed class SkyPrisonAnimationWorkbenchState {
    public const string FootstepTimelineTrackKey = "__SkyPrisonFootstepTrack";
    public const string FootstepTimelineTrackLabel = "脚步声";
    public const string FootstepTimelineTargetKind = "Footstep";
    public const string MotionTimelineTrackKey = "__SkyPrisonMotionTrack";
    public const string MotionTimelineTrackLabel = "Motion";

    public const float HeaderHeight=34f, DefaultPreviewHeight=310f, DefaultInspectorWidth=300f, DefaultTimelineHeight=210f, DefaultFormulaHeight=220f, RowHeight=22f, SplitterSize=6f, FoldBarSize=24f, MinLeftActionHeight=95f, MaxLeftActionHeight=680f, MinInspectorWidth=180f, MinPreviewWidth=260f, MinPreviewHeight=150f, MinTimelineHeight=92f, MinFormulaHeight=88f;
    public readonly List<SkyPrisonAnimationActionGroupRow> ActionGroups=new List<SkyPrisonAnimationActionGroupRow>();
    public readonly List<SkyPrisonAnimationActionRow> Actions=new List<SkyPrisonAnimationActionRow>();
    public readonly List<SkyPrisonAnimationRigRow> RigRows=new List<SkyPrisonAnimationRigRow>(), PsbRows=new List<SkyPrisonAnimationRigRow>(), SocketRows=new List<SkyPrisonAnimationRigRow>();
    public readonly List<SkyPrisonPhysicsPreset> PhysicsPresets = new List<SkyPrisonPhysicsPreset>();
    // 预览面板每帧写入的只读物理状态，Inspector 用它画“振子状态”窗口。
    // 注意：这里不是关键帧数据，也不参与保存动作。
    public readonly List<SkyPrisonPhysicsOscillatorStatus> PhysicsOscillatorStatuses = new List<SkyPrisonPhysicsOscillatorStatus>();
    public int SelectedPhysicsPresetIndex = -1;
    public bool PhysicsPresetEditorExpanded = true;
    public readonly List<SkyPrisonAnimationAssemblySlot> AssemblySlots=new List<SkyPrisonAnimationAssemblySlot>();
    public readonly List<SkyPrisonAnimationLayerOrderKeyframe> LayerOrderKeyframes=new List<SkyPrisonAnimationLayerOrderKeyframe>();
    public readonly List<SkyPrisonAnimationTimelineKeyframe> TimelineKeyframes=new List<SkyPrisonAnimationTimelineKeyframe>();
    public readonly List<SkyPrisonAnimationMotionKeyframe> MotionKeyframes=new List<SkyPrisonAnimationMotionKeyframe>();
    public int SelectedTimelineKeyframeIndex=-1;
    public int SelectedMotionKeyframeIndex=-1;
    public int SelectedActionGroup=0;
    public bool ActionGroupSelectionActive=false;
    public string ActiveTimelineTrackKey = "";
    public bool TimelineTrackLockEnabled = true;
    public bool ShowAllTimelineTracks = false;
    public SkyPrisonAnimationTimelineKeyframe TimelineKeyframeClipboard=null;
    public SkyPrisonAnimationMotionKeyframe MotionKeyframeClipboard=null;
    public readonly List<SkyPrisonAnimationTimelineKeyframe> TimelineFrameClipboard=new List<SkyPrisonAnimationTimelineKeyframe>();
    public readonly List<SkyPrisonAnimationMotionKeyframe> MotionFrameClipboard=new List<SkyPrisonAnimationMotionKeyframe>();
    public SkyPrisonAnimationStructureTab StructureTab=SkyPrisonAnimationStructureTab.Rig; public SkyPrisonAnimationFormulaType FormulaType=SkyPrisonAnimationFormulaType.Sine;
    public int SelectedAction=0, SelectedRig=0, LastClickedRig=-1, LastSelectedRigIndex=-1; public readonly HashSet<int> SelectedRigRows=new HashSet<int>(), SelectedRigIndices=new HashSet<int>();
    public string LastSelectedRigKey="", LastSelectedPsbLayerKey="";
    public string Search="", StructureSearch=""; public string SourcePsdAssetPath="";
    public string CurrentRigTemplateKey="Human"; public bool ManualRigTemplateMode=false;
    public bool IsCustomPurePsbMode { get { return ManualRigTemplateMode || string.Equals(CurrentRigTemplateKey, "Custom", System.StringComparison.OrdinalIgnoreCase); } }
    public bool PreviewPlaying, ShowFormulaPath=true, ShowHitbox=true, ShowRigLines=true, ShowVisualParts=true, ShowCenterOfGravityLine=true, ShowOnionSkinPrevious=false, ShowPhysicsPreview=true, ShowPhysicsOscillatorDebug=true, ShowParts=true, PreviewMirrored, ShowRigEdit=false;
    // 预览窗口键盘焦点：Rig 可视化窗口获得焦点后，Ctrl+Z / Ctrl+Y 只走 Rig Undo，避免打到 Unity 本体 Undo。
    public bool PreviewPanelHasKeyboardFocus=false, PreviewPanelMouseInside=false, PreviewPanelRigDragging=false;
    // 预览窗口内捕获到 Ctrl+Z / Ctrl+Y 时，不直接走 Unity Undo，而是交给 Page 层统一路由，和顶部按钮保持同一逻辑。
    public bool WorkbenchUndoShortcutRequested=false, WorkbenchRedoShortcutRequested=false;
    public float CurrentTime, TimelineDurationSeconds=1.2f, TimelineDuration=1.2f; public int TimelineFrameRate=30; public float PlaybackSpeedPercent=100f; public int _PlaybackSpeedPercentLegacy=100; public float TimelineDensityZoom=1f, TimelineDensity=1f, TimelineZoom=1f;
    public Vector2 PreviewPan=Vector2.zero; public float PreviewZoom=1f;
    public float FormulaAmplitude=.035f, FormulaFrequency=1.2f, FormulaPhase, FormulaOffset, InfinityAmplitudeX=.08f, InfinityAmplitudeY=.04f; public double LastTime;

    // 动作模板手动角度模式：把当前扫描/构建出的身体骨架节点全部暴露为 -180~180 度参数。
    // 这些值只作为“生成关键帧前的参数面板状态”，真正动画仍然写入 TimelineKeyframes。
    public readonly Dictionary<string, float> ManualBoneAngles = new Dictionary<string, float>();
    // 当前编辑帧的实时角度覆盖。只有参数面板或预览区拖动正在编辑的骨骼会进入这里；换帧同步时清空，避免默认 0 覆盖时间线插值。
    public readonly HashSet<string> LiveManualBoneAngleKeys = new HashSet<string>();
    public bool ManualAngleReplaceExisting = true;
    public bool ManualAngleWriteAllSampleFrames = false;
    public bool StructureAngleEditMode = false;
    public int ManualAngleSampleSegments = 8;
    public Vector2 ManualAngleParameterScroll = Vector2.zero;

    // 弧线补间工具：第一版只生成当前锁定/选中 RigAngle 轨道的中间 Key。
    // 自动化只负责“落 Key”，不会在预览里偷偷套隐藏公式。
    public bool MotionArcToolExpanded = false;
    public int MotionArcTweenCount = 5;
    public SkyPrisonMotionArcEasePreset MotionArcEasePreset = SkyPrisonMotionArcEasePreset.Soft;
    public float MotionArcEaseAmount = 0.60f;
    public bool MotionArcOverwriteInnerKeys = true;
    public Vector2 ManualPoseListScroll = Vector2.zero;
    public readonly List<SkyPrisonManualPoseKey> ManualPoseKeys = new List<SkyPrisonManualPoseKey>();
    public int SelectedManualPoseKeyIndex = -1;
    // 动作参数缓存必须绑定当前文件/当前 Rig。换 PSB 或重建骨架后如果不清，会把上一个角色的角度和姿势点带过来。
    private string manualAngleRigSignature = string.Empty;
    private bool manualAngleRigSignatureInitialized = false;

    // 动作参数页必须跟随当前时间线白线帧刷新。
    // 否则切到新时间点时会把上一帧姿势当成新的 0 点。
    private int lastManualAngleSyncedFrame = int.MinValue;
    private string lastManualAngleSyncedActionKey = string.Empty;
    private bool suppressManualAngleFrameSync = false;

    public Vector2 InspectorScroll, FormulaScroll, ActionListScroll, StructureScroll, TimelineScroll, TimelineHorizontalScroll, TimelineVerticalScroll, AssemblyScroll;
    public bool ActionListCollapsed, StructurePanelCollapsed, PreviewPanelCollapsed, InspectorPanelCollapsed, TimelinePanelCollapsed, FormulaPanelCollapsed, LeftWorkbenchCollapsed, UpperPanelCollapsed, AssemblyPanelCollapsed, SelectedInspectorCollapsed;
    public float LeftActionListHeight=235f, InspectorWidth=DefaultInspectorWidth, RightPreviewHeight=360f, RightTimelineHeight=DefaultTimelineHeight, RightFormulaHeight=DefaultFormulaHeight, LastRightContentWidth, LastRightContentHeight;
    public bool DraggingLeftActionStructureSplitter, DraggingPreviewInspectorSplitter, DraggingPreviewTimelineSplitter, DraggingTimelineFormulaSplitter;
    public int AssemblyPreviewMode=1, SelectedAssemblySlot=0; public int CurrentAssemblySlotIndex { get { return SelectedAssemblySlot; } set { SelectedAssemblySlot=value; } }
    public float InspectorAssemblyPanelHeight = 330f;
    public readonly string[] AssemblyPreviewModes={"裸体基础","当前装备","对比"};
    public readonly string[] BlendModeOptions={"正常","变暗","正片叠底","颜色加深","线性加深","变亮","滤色","颜色减淡","叠加","柔光","强光","差值","排除","色相","饱和度","颜色","亮度"};
    public readonly string[] DyeRegionOptions={"无","皮肤","头发","眼睛","装备","装备主色","装备副色","装备细节","裤子","袜子","鞋子","武器","特效","自定义"};
    public float LayerWeightBatchStep = 100000f;
    private const int StructureUndoLimit = 1000;

    // 工作台级撤销快照：不只保存当前页签列表。
    // 右侧检查器参数、装配模拟、PSB/Rig/Socket 父子结构、临时压层关键帧都走这一套，
    // 否则 Ctrl+Z 会出现“左边能撤，右边参数撤不了”或者“Rig 页签优先走 RigUndo 导致父子变化撤不掉”。
    private sealed class StructureUndoSnapshot
    {
        public SkyPrisonAnimationStructureTab tab;
        public int selectedRig;
        public int lastClickedRig;
        public int lastSelectedRigIndex;
        public int selectedAssemblySlot;
        public int selectedAction;
        public List<SkyPrisonAnimationActionGroupRow> actionGroups;
        public List<SkyPrisonAnimationActionRow> actions;
        public string search;
        public string lastSelectedRigKey;
        public string lastSelectedPsbLayerKey;
        public List<int> selectedRigRows;
        public List<int> selectedRigIndices;
        public List<SkyPrisonAnimationRigRow> rigRows;
        public List<SkyPrisonAnimationRigRow> psbRows;
        public List<SkyPrisonAnimationRigRow> socketRows;
        public List<SkyPrisonPhysicsPreset> physicsPresets;
        public int selectedPhysicsPresetIndex;
        public bool physicsPresetEditorExpanded;
        public List<SkyPrisonAnimationAssemblySlot> assemblySlots;
        public List<SkyPrisonAnimationLayerOrderKeyframe> layerOrderKeyframes;
        public List<SkyPrisonAnimationTimelineKeyframe> timelineKeyframes;
        public List<SkyPrisonAnimationMotionKeyframe> motionKeyframes;
        public int selectedTimelineKeyframeIndex;
        public int selectedMotionKeyframeIndex;
        public int selectedActionGroup;
        public bool actionGroupSelectionActive;
        public string activeTimelineTrackKey;
        public bool timelineTrackLockEnabled;
        public bool showAllTimelineTracks;
        public Dictionary<string, float> manualBoneAngles;
        public List<SkyPrisonManualPoseKey> manualPoseKeys;
        public int selectedManualPoseKeyIndex;
        public int manualAngleSampleSegments;
        public bool manualAngleReplaceExisting;
        public bool manualAngleWriteAllSampleFrames;
        public bool motionArcToolExpanded;
        public int motionArcTweenCount;
        public SkyPrisonMotionArcEasePreset motionArcEasePreset;
        public float motionArcEaseAmount;
        public bool motionArcOverwriteInnerKeys;
        public int lastManualAngleSyncedFrame;
        public string lastManualAngleSyncedActionKey;

        public StructureUndoSnapshot(SkyPrisonAnimationWorkbenchState state)
        {
            tab = state.StructureTab;
            selectedRig = state.SelectedRig;
            lastClickedRig = state.LastClickedRig;
            lastSelectedRigIndex = state.LastSelectedRigIndex;
            selectedAssemblySlot = state.SelectedAssemblySlot;
            selectedAction = state.SelectedAction;
            actionGroups = CloneActionGroups(state.ActionGroups);
            actions = CloneActions(state.Actions);
            search = state.Search;
            lastSelectedRigKey = state.LastSelectedRigKey;
            lastSelectedPsbLayerKey = state.LastSelectedPsbLayerKey;
            selectedRigRows = new List<int>(state.SelectedRigRows);
            selectedRigIndices = new List<int>(state.SelectedRigIndices);
            rigRows = CloneRows(state.RigRows);
            psbRows = CloneRows(state.PsbRows);
            socketRows = CloneRows(state.SocketRows);
            physicsPresets = ClonePhysicsPresets(state.PhysicsPresets);
            selectedPhysicsPresetIndex = state.SelectedPhysicsPresetIndex;
            physicsPresetEditorExpanded = state.PhysicsPresetEditorExpanded;
            assemblySlots = CloneAssemblySlots(state.AssemblySlots);
            layerOrderKeyframes = CloneLayerOrderKeyframes(state.LayerOrderKeyframes);
            timelineKeyframes = CloneTimelineKeyframes(state.TimelineKeyframes);
            motionKeyframes = CloneMotionKeyframes(state.MotionKeyframes);
            selectedTimelineKeyframeIndex = state.SelectedTimelineKeyframeIndex;
            selectedMotionKeyframeIndex = state.SelectedMotionKeyframeIndex;
            selectedActionGroup = state.SelectedActionGroup;
            actionGroupSelectionActive = state.ActionGroupSelectionActive;
            activeTimelineTrackKey = state.ActiveTimelineTrackKey;
            timelineTrackLockEnabled = state.TimelineTrackLockEnabled;
            showAllTimelineTracks = state.ShowAllTimelineTracks;
            manualBoneAngles = CloneManualBoneAngles(state.ManualBoneAngles);
            manualPoseKeys = CloneManualPoseKeys(state.ManualPoseKeys);
            selectedManualPoseKeyIndex = state.SelectedManualPoseKeyIndex;
            manualAngleSampleSegments = state.ManualAngleSampleSegments;
            manualAngleReplaceExisting = state.ManualAngleReplaceExisting;
            manualAngleWriteAllSampleFrames = state.ManualAngleWriteAllSampleFrames;
            motionArcToolExpanded = state.MotionArcToolExpanded;
            motionArcTweenCount = state.MotionArcTweenCount;
            motionArcEasePreset = state.MotionArcEasePreset;
            motionArcEaseAmount = state.MotionArcEaseAmount;
            motionArcOverwriteInnerKeys = state.MotionArcOverwriteInnerKeys;
            lastManualAngleSyncedFrame = state.lastManualAngleSyncedFrame;
            lastManualAngleSyncedActionKey = state.lastManualAngleSyncedActionKey;
        }

        public void RestoreTo(SkyPrisonAnimationWorkbenchState state)
        {
            state.StructureTab = tab;
            ReplaceActionGroups(state.ActionGroups, actionGroups);
            ReplaceActions(state.Actions, actions);
            state.SelectedAction = Mathf.Clamp(selectedAction, 0, Mathf.Max(0, state.Actions.Count - 1));
            state.Search = search ?? string.Empty;
            ReplaceRows(state.RigRows, rigRows);
            ReplaceRows(state.PsbRows, psbRows);
            ReplaceRows(state.SocketRows, socketRows);
            ReplacePhysicsPresets(state.PhysicsPresets, physicsPresets);
            state.SelectedPhysicsPresetIndex = Mathf.Clamp(selectedPhysicsPresetIndex, -1, Mathf.Max(-1, state.PhysicsPresets.Count - 1));
            state.PhysicsPresetEditorExpanded = physicsPresetEditorExpanded;
            ReplaceAssemblySlots(state.AssemblySlots, assemblySlots);
            ReplaceLayerOrderKeyframes(state.LayerOrderKeyframes, layerOrderKeyframes);
            ReplaceTimelineKeyframes(state.TimelineKeyframes, timelineKeyframes);
            ReplaceMotionKeyframes(state.MotionKeyframes, motionKeyframes);
            state.SelectedTimelineKeyframeIndex = Mathf.Clamp(selectedTimelineKeyframeIndex, -1, Mathf.Max(-1, state.TimelineKeyframes.Count - 1));
            state.SelectedMotionKeyframeIndex = Mathf.Clamp(selectedMotionKeyframeIndex, -1, Mathf.Max(-1, state.MotionKeyframes.Count - 1));
            state.SelectedActionGroup = Mathf.Clamp(selectedActionGroup, 0, Mathf.Max(0, state.ActionGroups.Count - 1));
            state.ActiveTimelineTrackKey = activeTimelineTrackKey ?? string.Empty;
            state.TimelineTrackLockEnabled = timelineTrackLockEnabled;
            state.ShowAllTimelineTracks = showAllTimelineTracks;
            state.ManualBoneAngles.Clear();
            if (manualBoneAngles != null)
            {
                foreach (KeyValuePair<string, float> kv in manualBoneAngles)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                        state.ManualBoneAngles[kv.Key] = Mathf.Clamp(kv.Value, -180f, 180f);
                }
            }
            ReplaceManualPoseKeys(state.ManualPoseKeys, manualPoseKeys);
            state.SelectedManualPoseKeyIndex = Mathf.Clamp(selectedManualPoseKeyIndex, -1, Mathf.Max(-1, state.ManualPoseKeys.Count - 1));
            state.ManualAngleSampleSegments = Mathf.Clamp(manualAngleSampleSegments, 1, 32);
            state.ManualAngleReplaceExisting = manualAngleReplaceExisting;
            state.ManualAngleWriteAllSampleFrames = manualAngleWriteAllSampleFrames;
            state.MotionArcToolExpanded = motionArcToolExpanded;
            state.MotionArcTweenCount = Mathf.Clamp(motionArcTweenCount, 1, 60);
            state.MotionArcEasePreset = motionArcEasePreset;
            state.MotionArcEaseAmount = Mathf.Clamp01(motionArcEaseAmount);
            state.MotionArcOverwriteInnerKeys = motionArcOverwriteInnerKeys;
            state.lastManualAngleSyncedFrame = int.MinValue;
            state.lastManualAngleSyncedActionKey = string.Empty;
            state.suppressManualAngleFrameSync = false;
            state.ApplyManualAnglePreviewToAll();

            state.SelectedRig = Mathf.Clamp(selectedRig, -1, Mathf.Max(-1, state.GetCurrentRows().Count - 1));
            state.LastClickedRig = lastClickedRig;
            state.LastSelectedRigIndex = lastSelectedRigIndex;
            state.SelectedAssemblySlot = Mathf.Clamp(selectedAssemblySlot, 0, Mathf.Max(0, state.AssemblySlots.Count - 1));
            state.LastSelectedRigKey = lastSelectedRigKey ?? string.Empty;
            state.LastSelectedPsbLayerKey = lastSelectedPsbLayerKey ?? string.Empty;
            state.SelectedRigRows.Clear();
            if (selectedRigRows != null)
            {
                int count = state.GetCurrentRows().Count;
                for (int i = 0; i < selectedRigRows.Count; i++)
                    if (selectedRigRows[i] >= 0 && selectedRigRows[i] < count)
                        state.SelectedRigRows.Add(selectedRigRows[i]);
            }
            state.SelectedRigIndices.Clear();
            if (selectedRigIndices != null)
            {
                int count = state.GetCurrentRows().Count;
                for (int i = 0; i < selectedRigIndices.Count; i++)
                    if (selectedRigIndices[i] >= 0 && selectedRigIndices[i] < count)
                        state.SelectedRigIndices.Add(selectedRigIndices[i]);
            }
        }
    }

    readonly Stack<StructureUndoSnapshot> undoStack=new Stack<StructureUndoSnapshot>(), redoStack=new Stack<StructureUndoSnapshot>();
    readonly Stack<List<SkyPrisonAnimationRigRow>> rigUndoStack=new Stack<List<SkyPrisonAnimationRigRow>>(), rigRedoStack=new Stack<List<SkyPrisonAnimationRigRow>>();

    public void EnforceCustomPurePsbMode(bool preservePsbRows)
    {
        CurrentRigTemplateKey = "Custom";
        ManualRigTemplateMode = true;

        if (!preservePsbRows)
            PsbRows.Clear();

        // Custom / 空 Rig 不是“只读 PSB 模式”。
        // 不清 RigRows，不关闭 ShowRigEdit，不强制跳回 PsbLayer。
        if (StructureTab == SkyPrisonAnimationStructureTab.Rig)
            SelectedRig = RigRows.Count > 0 ? Mathf.Clamp(SelectedRig, 0, RigRows.Count - 1) : -1;
        else if (StructureTab == SkyPrisonAnimationStructureTab.PsbLayer)
            SelectedRig = PsbRows.Count > 0 ? Mathf.Clamp(SelectedRig, 0, PsbRows.Count - 1) : -1;
        else
            SelectedRig = SocketRows.Count > 0 ? Mathf.Clamp(SelectedRig, 0, SocketRows.Count - 1) : -1;

        if (PsbRows != null)
        {
            for (int i = 0; i < PsbRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = PsbRows[i];
                if (row == null) continue;
                row.boundRigKey = string.Empty;
                row.boundRigName = string.Empty;
                row.bindMode = "未绑定";
                row.bindConfidence = 0f;
                row.mapped = false;
                if (!row.isFolder)
                    row.semantic = "PSD Layer";
            }
        }
    }

    public int TimelineTotalFrames { get { return Mathf.Max(1, Mathf.RoundToInt(TimelineDurationSeconds*Mathf.Max(1,TimelineFrameRate))); } }
    public int TimelineCurrentFrame { get { return Mathf.Clamp(Mathf.RoundToInt(CurrentTime*Mathf.Max(1,TimelineFrameRate)),0,TimelineTotalFrames); } }
    public float TimelineCurrentFrameFloat { get { return Mathf.Clamp(CurrentTime*Mathf.Max(1,TimelineFrameRate),0f,TimelineTotalFrames); } }
    public string CurrentTimeCode { get { return FormatCurrentTime(); } }
    public string FormatCurrentTime()
    {
        float s = Mathf.Max(0f, CurrentTime);
        int minutes = Mathf.FloorToInt(s / 60f);
        float seconds = s - minutes * 60f;

        // 这里显示真实时间，不显示“秒:帧”。
        // 旧版 1.2s@60fps 会显示成 00:01:12，容易被看成 1分12秒或 1.12秒。
        // 现在统一显示为真实秒数：1.20s；右侧仍然单独显示帧号 00072。
        if (minutes <= 0)
            return seconds.ToString("0.00") + "s";

        return string.Format("{0:00}:{1:00.00}s", minutes, seconds);
    }
    public void SetCurrentFrame(int frame)
    {
        CurrentTime = Mathf.Clamp(frame, 0, TimelineTotalFrames) / Mathf.Max(1f, TimelineFrameRate);
        SyncManualAnglesFromCurrentFrame(false);
    }
    public void SyncCurrentActionDurationFromTimeline(){ TimelineDurationSeconds=Mathf.Max(.01f,TimelineDurationSeconds); TimelineDuration=TimelineDurationSeconds; CurrentTime=Mathf.Clamp(CurrentTime,0f,TimelineDurationSeconds); }
    public void ResetTimelineDensity(){ TimelineDensityZoom=TimelineDensity=TimelineZoom=1f; }
    public void ResetPreviewView(){ PreviewPan=Vector2.zero; PreviewZoom=1f; }
    public void ResetWorkbenchLayout(){ LeftActionListHeight=235f; InspectorWidth=DefaultInspectorWidth; RightPreviewHeight=360f; RightTimelineHeight=DefaultTimelineHeight; RightFormulaHeight=DefaultFormulaHeight; ActionListCollapsed=StructurePanelCollapsed=PreviewPanelCollapsed=InspectorPanelCollapsed=TimelinePanelCollapsed=FormulaPanelCollapsed=LeftWorkbenchCollapsed=UpperPanelCollapsed=AssemblyPanelCollapsed=SelectedInspectorCollapsed=false; ShowRigEdit=false; ResetPreviewView(); ResetTimelineDensity(); }
    public SkyPrisonAnimationActionRow CurrentAction(){ if(Actions.Count==0){ if(IsCustomPurePsbMode) Actions.Add(new SkyPrisonAnimationActionRow{key="Idle",name="待机",type="自定义",status="手动",loop=true,duration=1.2f,groupKey="Base"}); else BuildMockData(); } EnsureActionGroups(); SelectedAction=Mathf.Clamp(SelectedAction,0,Actions.Count-1); return Actions[SelectedAction]; }

    public SkyPrisonAnimationActionGroupRow CurrentActionGroup()
    {
        EnsureActionGroups();
        if (ActionGroups.Count == 0)
            return null;
        SelectedActionGroup = Mathf.Clamp(SelectedActionGroup, 0, ActionGroups.Count - 1);
        return ActionGroups[SelectedActionGroup];
    }

    public bool IsActionGroupSelected()
    {
        return ActionGroupSelectionActive && CurrentActionGroup() != null;
    }

    public bool CanEditCurrentActionTimeline()
    {
        return !IsActionGroupSelected() && Actions != null && Actions.Count > 0;
    }

    public string CurrentActionGroupDisplayName()
    {
        SkyPrisonAnimationActionGroupRow g = CurrentActionGroup();
        if (g == null) return "动作组";
        return string.IsNullOrWhiteSpace(g.name) ? (string.IsNullOrWhiteSpace(g.key) ? "动作组" : g.key) : g.name;
    }

    public void SelectActionAndRefresh(int index)
    {
        if (Actions == null || Actions.Count == 0)
            CurrentAction();

        if (Actions == null || Actions.Count == 0)
            return;

        int newIndex = Mathf.Clamp(index, 0, Actions.Count - 1);
        ActionGroupSelectionActive = false;
        if (SelectedAction == newIndex)
        {
            SyncManualAnglesFromCurrentFrame(true);
            RepairMeshDeformerCachesForCurrentAction();
            return;
        }

        SelectedAction = newIndex;
        SkyPrisonAnimationActionRow row = CurrentAction();
        TimelineDurationSeconds = Mathf.Max(0.01f, row != null ? row.duration : TimelineDurationSeconds);
        TimelineDuration = TimelineDurationSeconds;
        CurrentTime = 0f;
        SelectedTimelineKeyframeIndex = -1;
        SelectedManualPoseKeyIndex = -1;
        lastManualAngleSyncedFrame = int.MinValue;
        lastManualAngleSyncedActionKey = string.Empty;
        SyncManualAnglesFromCurrentFrame(true);
        RepairMeshDeformerCachesForCurrentAction();
    }

    public List<SkyPrisonAnimationRigRow> GetCurrentRows(){ return StructureTab==SkyPrisonAnimationStructureTab.PsbLayer?PsbRows:(StructureTab==SkyPrisonAnimationStructureTab.Socket?SocketRows:RigRows); }
    public SkyPrisonAnimationRigRow GetSelectedRigRow(){ var rows=GetCurrentRows(); if(rows.Count==0)return new SkyPrisonAnimationRigRow{key="-",name="-",semantic="-"}; SelectedRig=Mathf.Clamp(SelectedRig,0,rows.Count-1); return rows[SelectedRig]; }
    public SkyPrisonAnimationAssemblySlot CurrentAssemblySlot(){ if(IsCustomPurePsbMode){ AssemblySlots.Clear(); return new SkyPrisonAnimationAssemblySlot{slotKey="Custom",displayName="自定义",assetKey="",boundPartKey="",visualSlotKey=""}; } BuildMockAssemblyData(); if(AssemblySlots.Count==0)return new SkyPrisonAnimationAssemblySlot{slotKey="Custom",displayName="自定义",assetKey="",boundPartKey="",visualSlotKey=""}; SelectedAssemblySlot=Mathf.Clamp(SelectedAssemblySlot,0,AssemblySlots.Count-1); return AssemblySlots[SelectedAssemblySlot]; }
    public bool HasSelectedPsbLayer()
    {
        if (PsbRows == null || PsbRows.Count == 0)
            return false;

        if (!string.IsNullOrEmpty(LastSelectedPsbLayerKey))
        {
            for (int i = 0; i < PsbRows.Count; i++)
            {
                if (PsbRows[i] != null && PsbRows[i].key == LastSelectedPsbLayerKey)
                    return true;
            }
        }

        return StructureTab == SkyPrisonAnimationStructureTab.PsbLayer && SelectedRig >= 0 && SelectedRig < PsbRows.Count;
    }

    public void ClearCurrentStructureSelection(bool clearRememberedSelection)
    {
        SelectedRig = -1;
        LastClickedRig = -1;
        LastSelectedRigIndex = -1;
        SelectedRigRows.Clear();
        SelectedRigIndices.Clear();

        if (!clearRememberedSelection)
            return;

        if (StructureTab == SkyPrisonAnimationStructureTab.PsbLayer)
            LastSelectedPsbLayerKey = string.Empty;
        else if (StructureTab == SkyPrisonAnimationStructureTab.Rig)
            LastSelectedRigKey = string.Empty;
        else
        {
            LastSelectedRigKey = string.Empty;
            LastSelectedPsbLayerKey = string.Empty;
        }
    }

    public void BindSelectedAssemblyToSelectedRig(){ BindSelectedAssemblyToSelectedNode(); }
    public void BindSelectedAssemblyToSelectedNode(){ var slot=CurrentAssemblySlot(); var row=GetSelectedRigRow(); slot.boundPartKey=row.key; if(string.IsNullOrEmpty(slot.visualSlotKey))slot.visualSlotKey=GuessVisualSlotFromAssemblySlot(slot.slotKey); row.boundEquipmentKey=slot.assetKey; row.equipmentSourceKey=slot.slotKey; row.visualSlotKey=row.slotKey=slot.visualSlotKey; }
    public void ClearSelectedAssemblyBinding(){ var slot=CurrentAssemblySlot(); slot.boundPartKey=""; slot.visualSlotKey=GuessVisualSlotFromAssemblySlot(slot.slotKey); }
    public string GuessVisualSlotFromAssemblySlot(string k){ switch(k){case"BaseBody":return"Body";case"Head":return"Head";case"Hair":return"Hair";case"Top":return"Outfit";case"Hand":return"Hand";case"Pants":return"Pants";case"Socks":return"Socks";case"Shoes":return"Shoes";case"Accessory":return"Accessory";case"Weapon":return"Weapon";default:return"Outfit";} }

    public void EnsureActionGroups()
    {
        if (ActionGroups.Count == 0)
        {
            AddActionGroupInternal("Base", "基础");
            AddActionGroupInternal("Move", "移动");
            AddActionGroupInternal("Jump", "跳跃");
            AddActionGroupInternal("Attack", "攻击");
            AddActionGroupInternal("Hit", "受击");
            AddActionGroupInternal("Other", "其他");
        }
        for (int i = 0; i < Actions.Count; i++)
        {
            SkyPrisonAnimationActionRow a = Actions[i];
            if (a == null) continue;
            if (string.IsNullOrEmpty(a.groupKey) || FindActionGroupIndex(a.groupKey) < 0)
                a.groupKey = GuessActionGroupKey(a);
        }
        SelectedActionGroup = Mathf.Clamp(SelectedActionGroup, 0, Mathf.Max(0, ActionGroups.Count - 1));
    }

    private void AddActionGroupInternal(string key, string name)
    {
        ActionGroups.Add(new SkyPrisonAnimationActionGroupRow { key = GenerateUniqueActionGroupKey(key), name = name, expanded = true });
    }

    public void AddActionGroup()
    {
        PushStructureUndo();
        EnsureActionGroups();
        ActionGroups.Add(new SkyPrisonAnimationActionGroupRow { key = GenerateUniqueActionGroupKey("Group"), name = "新动作组", expanded = true });
        SelectedActionGroup = ActionGroups.Count - 1;
        ActionGroupSelectionActive = true;
    }

    public void DeleteSelectedActionGroup()
    {
        EnsureActionGroups();
        if (ActionGroups.Count <= 1 || SelectedActionGroup < 0 || SelectedActionGroup >= ActionGroups.Count) return;
        PushStructureUndo();
        string removedKey = ActionGroups[SelectedActionGroup].key;
        ActionGroups.RemoveAt(SelectedActionGroup);
        string fallback = ActionGroups.Count > 0 ? ActionGroups[Mathf.Clamp(SelectedActionGroup - 1, 0, ActionGroups.Count - 1)].key : "";
        for (int i = 0; i < Actions.Count; i++)
            if (Actions[i] != null && string.Equals(Actions[i].groupKey, removedKey, StringComparison.OrdinalIgnoreCase))
                Actions[i].groupKey = fallback;
        SelectedActionGroup = Mathf.Clamp(SelectedActionGroup - 1, 0, Mathf.Max(0, ActionGroups.Count - 1));
        ActionGroupSelectionActive = true;
    }

    public int FindActionGroupIndex(string key)
    {
        if (string.IsNullOrEmpty(key)) return -1;
        for (int i = 0; i < ActionGroups.Count; i++)
            if (ActionGroups[i] != null && string.Equals(ActionGroups[i].key, key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public string GuessActionGroupKey(SkyPrisonAnimationActionRow a)
    {
        string k = ((a != null ? a.key : "") ?? "").ToLowerInvariant();
        string n = ((a != null ? a.name : "") ?? "").ToLowerInvariant();
        string s2 = k + " " + n;
        if (s2.Contains("jump") || s2.Contains("跳")) return FindActionGroupIndex("Jump") >= 0 ? "Jump" : ActionGroups[0].key;
        if (s2.Contains("attack") || s2.Contains("攻")) return FindActionGroupIndex("Attack") >= 0 ? "Attack" : ActionGroups[0].key;
        if (s2.Contains("hit") || s2.Contains("hurt") || s2.Contains("受击")) return FindActionGroupIndex("Hit") >= 0 ? "Hit" : ActionGroups[0].key;
        if (s2.Contains("move") || s2.Contains("run") || s2.Contains("walk") || s2.Contains("sneak") || s2.Contains("移动") || s2.Contains("奔跑") || s2.Contains("潜行")) return FindActionGroupIndex("Move") >= 0 ? "Move" : ActionGroups[0].key;
        if (s2.Contains("idle") || s2.Contains("待机") || s2.Contains("death") || s2.Contains("wink")) return FindActionGroupIndex("Base") >= 0 ? "Base" : ActionGroups[0].key;
        return ActionGroups.Count > 0 ? ActionGroups[0].key : "";
    }

    public string GenerateUniqueActionGroupKey(string baseKey)
    {
        baseKey = SanitizeActionKey(baseKey);
        if (string.IsNullOrEmpty(baseKey)) baseKey = "Group";
        bool used = FindActionGroupIndex(baseKey) >= 0;
        if (!used) return baseKey;
        int suffix = 1;
        while (FindActionGroupIndex(baseKey + "_" + suffix) >= 0) suffix++;
        return baseKey + "_" + suffix;
    }

    public void AddAction(){ PushStructureUndo(); EnsureActionGroups(); string g=ActionGroups.Count>0?ActionGroups[Mathf.Clamp(SelectedActionGroup,0,ActionGroups.Count-1)].key:""; Actions.Add(new SkyPrisonAnimationActionRow{key=GenerateUniqueActionKey("NewAction"),name="新动作",type="关键帧",status="占位",loop=false,duration=TimelineDurationSeconds,groupKey=g}); SelectedAction=Actions.Count-1; ActionGroupSelectionActive=false; }
    public void DuplicateAction()
    {
        EnsureActionGroups();

        // 动作组只是分类容器，不是 ActionClip。
        // 组选中时不允许复制，避免把上一次选中的动作误复制出来。
        if (IsActionGroupSelected())
            return;

        SkyPrisonAnimationActionRow source = CurrentAction();
        if (source == null)
            return;

        string sourceKey = source.key ?? string.Empty;
        string newKey = GenerateUniqueActionKey((string.IsNullOrEmpty(sourceKey) ? "Action" : sourceKey) + "_Copy");

        PushStructureUndo();

        SkyPrisonAnimationActionRow copiedAction = new SkyPrisonAnimationActionRow
        {
            key = newKey,
            name = string.IsNullOrEmpty(source.name) ? (newKey + " 副本") : (source.name + " 副本"),
            type = source.type,
            status = source.status,
            loop = source.loop,
            duration = source.duration,
            groupKey = source.groupKey
        };

        Actions.Add(copiedAction);

        // 复制动作时必须复制该 ActionKey 下的全部动作数据。
        // 这里只替换 actionKey，不改 targetKey / targetKind / frame / 曲面点 / Motion 偏移等内容，
        // 这样复制出来的动作和原动作在时间线、Motion 轨道、图层顺序上完全一致，但互不引用。
        if (!string.IsNullOrEmpty(sourceKey))
        {
            int timelineInsertStart = TimelineKeyframes.Count;
            for (int i = 0; i < timelineInsertStart; i++)
            {
                SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
                if (k == null || !string.Equals(k.actionKey, sourceKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                SkyPrisonAnimationTimelineKeyframe cloned = k.Clone();
                cloned.actionKey = newKey;
                TimelineKeyframes.Add(cloned);
            }

            int layerInsertStart = LayerOrderKeyframes.Count;
            for (int i = 0; i < layerInsertStart; i++)
            {
                SkyPrisonAnimationLayerOrderKeyframe k = LayerOrderKeyframes[i];
                if (k == null || !string.Equals(k.actionKey, sourceKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                LayerOrderKeyframes.Add(new SkyPrisonAnimationLayerOrderKeyframe
                {
                    actionKey = newKey,
                    layerKey = k.layerKey,
                    time = k.time,
                    orderWeight = k.orderWeight
                });
            }

            int motionInsertStart = MotionKeyframes.Count;
            for (int i = 0; i < motionInsertStart; i++)
            {
                SkyPrisonAnimationMotionKeyframe k = MotionKeyframes[i];
                if (k == null || !string.Equals(k.actionKey, sourceKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                SkyPrisonAnimationMotionKeyframe cloned = k.Clone();
                cloned.actionKey = newKey;
                MotionKeyframes.Add(cloned);
            }
        }

        SortTimelineKeyframes();
        SortMotionKeyframes();

        SelectedAction = Actions.Count - 1;
        SelectedTimelineKeyframeIndex = -1;
        SelectedMotionKeyframeIndex = -1;
        ActionGroupSelectionActive = false;
        CurrentTime = 0f;
    }
    public void DeleteAction(){ DeleteActionAt(SelectedAction); }

    public void DeleteActionAt(int index)
    {
        if (Actions == null || Actions.Count <= 1) return;
        if (index < 0 || index >= Actions.Count) return;

        SkyPrisonAnimationActionRow row = Actions[index];
        string actionKey = row != null ? (row.key ?? string.Empty) : string.Empty;

        PushStructureUndo();
        Actions.RemoveAt(index);

        if (!string.IsNullOrEmpty(actionKey))
        {
            TimelineKeyframes.RemoveAll(k => k != null && string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase));
            LayerOrderKeyframes.RemoveAll(k => k != null && string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase));
            MotionKeyframes.RemoveAll(k => k != null && string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedAction == index)
            SelectedAction = Mathf.Clamp(index, 0, Actions.Count - 1);
        else if (SelectedAction > index)
            SelectedAction--;

        SelectedAction = Mathf.Clamp(SelectedAction, 0, Actions.Count - 1);
        SelectedTimelineKeyframeIndex = -1;
        lastManualAngleSyncedActionKey = string.Empty;
        SyncManualAnglesFromCurrentFrame(true);
    }

    public void RenameActionName(int index, string newName)
    {
        if (index < 0 || index >= Actions.Count) return;
        newName = string.IsNullOrWhiteSpace(newName) ? "未命名动作" : newName.Trim();
        if (Actions[index] != null && Actions[index].name == newName) return;
        PushStructureUndo();
        Actions[index].name = newName;
    }

    public void RenameActionKey(int index, string newKey)
    {
        if (index < 0 || index >= Actions.Count) return;
        SkyPrisonAnimationActionRow row = Actions[index];
        if (row == null) return;

        newKey = SanitizeActionKey(newKey);
        if (string.IsNullOrEmpty(newKey)) newKey = "Action";
        newKey = GenerateUniqueActionKey(newKey, index);
        if (row.key == newKey) return;

        string oldKey = row.key;
        PushStructureUndo();
        row.key = newKey;
        RetargetActionKey(oldKey, newKey);
        lastManualAngleSyncedActionKey = string.Empty;
        SyncManualAnglesFromCurrentFrame(true);
    }

    public void MoveAction(int fromIndex, int toIndex)
    {
        if (Actions == null || Actions.Count <= 1) return;
        if (fromIndex < 0 || fromIndex >= Actions.Count) return;
        toIndex = Mathf.Clamp(toIndex, 0, Actions.Count - 1);
        if (fromIndex == toIndex) return;

        PushStructureUndo();
        SkyPrisonAnimationActionRow moving = Actions[fromIndex];
        Actions.RemoveAt(fromIndex);
        Actions.Insert(toIndex, moving);

        if (SelectedAction == fromIndex)
            SelectedAction = toIndex;
        else if (fromIndex < SelectedAction && toIndex >= SelectedAction)
            SelectedAction--;
        else if (fromIndex > SelectedAction && toIndex <= SelectedAction)
            SelectedAction++;

        SelectedAction = Mathf.Clamp(SelectedAction, 0, Actions.Count - 1);
    }

    public string GenerateUniqueActionKey(string baseKey, int ignoreIndex = -1)
    {
        baseKey = SanitizeActionKey(baseKey);
        if (string.IsNullOrEmpty(baseKey)) baseKey = "Action";

        bool used = false;
        for (int i = 0; i < Actions.Count; i++)
        {
            if (i == ignoreIndex) continue;
            SkyPrisonAnimationActionRow row = Actions[i];
            if (row != null && row.key == baseKey) { used = true; break; }
        }
        if (!used) return baseKey;

        int suffix = 1;
        while (true)
        {
            string candidate = baseKey + "_" + suffix;
            used = false;
            for (int i = 0; i < Actions.Count; i++)
            {
                if (i == ignoreIndex) continue;
                SkyPrisonAnimationActionRow row = Actions[i];
                if (row != null && row.key == candidate) { used = true; break; }
            }
            if (!used) return candidate;
            suffix++;
        }
    }

    public static string SanitizeActionKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        raw = raw.Trim();
        System.Text.StringBuilder sb = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
                sb.Append(c);
            else if (c == '-' || c == ' ' || c == '/')
                sb.Append('_');
        }
        return sb.ToString();
    }

    private void RetargetActionKey(string oldKey, string newKey)
    {
        if (string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey) || oldKey == newKey) return;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
            if (TimelineKeyframes[i] != null && TimelineKeyframes[i].actionKey == oldKey)
                TimelineKeyframes[i].actionKey = newKey;

        for (int i = 0; i < LayerOrderKeyframes.Count; i++)
            if (LayerOrderKeyframes[i] != null && LayerOrderKeyframes[i].actionKey == oldKey)
                LayerOrderKeyframes[i].actionKey = newKey;

        for (int i = 0; i < MotionKeyframes.Count; i++)
            if (MotionKeyframes[i] != null && MotionKeyframes[i].actionKey == oldKey)
                MotionKeyframes[i].actionKey = newKey;
    }

    public bool PassSearch(string a,string b,string c,string d)
    {
        // 动作列表搜索只能使用动作列表自己的 Search。
        // 之前这里在 StructureSearch 非空时会拿“结构搜索”过滤动作列表，
        // 导致别名/Key 保存后看起来像被搜索吞掉或没有保存。
        string s = Search;
        if (string.IsNullOrWhiteSpace(s)) return true;
        s = s.Trim().ToLowerInvariant();
        return SafeContains(a,s)||SafeContains(b,s)||SafeContains(c,s)||SafeContains(d,s);
    }
    public static bool SafeContains(string text,string keyword){ return !string.IsNullOrEmpty(text)&&text.ToLower().Contains(keyword); }
    public void AutoBindPsbLayersToRig(){ if(IsCustomPurePsbMode){ ClearPsbLayerBindings(); return; } BuildMockDataIfNeeded(); EnsureCoreRigNodes(); ClearRigPsbLinksOnly(); for(int i=0;i<PsbRows.Count;i++) AutoBindSinglePsbLayer(PsbRows[i]); RefreshRigLinksFromPsbBindings(); }
    public void ClearPsbLayerBindings(){ for(int i=0;i<PsbRows.Count;i++){ PsbRows[i].boundRigKey=""; PsbRows[i].boundRigName=""; PsbRows[i].bindMode="未绑定"; PsbRows[i].bindConfidence=0f; PsbRows[i].mapped=false; } ClearRigPsbLinksOnly(); }
    public void RememberSelectedStructureRow(SkyPrisonAnimationRigRow row){ if(row==null)return; if(StructureTab==SkyPrisonAnimationStructureTab.Rig)LastSelectedRigKey=row.key; else if(StructureTab==SkyPrisonAnimationStructureTab.PsbLayer&&!row.isFolder)LastSelectedPsbLayerKey=row.key; }
    public SkyPrisonAnimationRigRow FindPsbRow(string key){ if(string.IsNullOrEmpty(key))return null; for(int i=0;i<PsbRows.Count;i++) if(PsbRows[i].key==key) return PsbRows[i]; return null; }

    public SkyPrisonAnimationRigRow FindAnyStructureRow(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        SkyPrisonAnimationRigRow row = FindPsbRow(key);
        if (row != null) return row;
        row = FindRigRow(key);
        if (row != null) return row;
        for (int i = 0; i < SocketRows.Count; i++) if (SocketRows[i] != null && SocketRows[i].key == key) return SocketRows[i];
        return null;
    }

    public string[] GetMaskReferenceOptionKeys(SkyPrisonAnimationRigRow row)
    {
        List<string> keys = new List<string>();
        keys.Add("");
        AddMaskReferenceKeys(keys, PsbRows, row);
        AddMaskReferenceKeys(keys, RigRows, row);
        return keys.ToArray();
    }

    public string[] GetMaskReferenceOptionLabels(SkyPrisonAnimationRigRow row)
    {
        List<string> labels = new List<string>();
        labels.Add("无");
        AddMaskReferenceLabels(labels, PsbRows, row, "PSB");
        AddMaskReferenceLabels(labels, RigRows, row, "Rig");
        return labels.ToArray();
    }

    private void AddMaskReferenceKeys(List<string> keys, List<SkyPrisonAnimationRigRow> rows, SkyPrisonAnimationRigRow self)
    {
        if (rows == null) return;
        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow r = rows[i];
            if (r == null || r.isFolder || r == self || string.IsNullOrEmpty(r.key)) continue;
            if (!keys.Contains(r.key)) keys.Add(r.key);
        }
    }

    private void AddMaskReferenceLabels(List<string> labels, List<SkyPrisonAnimationRigRow> rows, SkyPrisonAnimationRigRow self, string prefix)
    {
        if (rows == null) return;
        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow r = rows[i];
            if (r == null || r.isFolder || r == self || string.IsNullOrEmpty(r.key)) continue;
            string name = string.IsNullOrEmpty(r.name) ? r.key : r.name;
            labels.Add(prefix + " / " + name + "  [" + r.key + "]");
        }
    }

    public int GetMaskReferenceIndex(SkyPrisonAnimationRigRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.maskReferenceKey)) return 0;
        string[] keys = GetMaskReferenceOptionKeys(row);
        for (int i = 0; i < keys.Length; i++) if (keys[i] == row.maskReferenceKey) return i;
        return 0;
    }

    public void SetMaskReferenceByIndex(SkyPrisonAnimationRigRow row, int index)
    {
        if (row == null) return;
        string[] keys = GetMaskReferenceOptionKeys(row);
        index = Mathf.Clamp(index, 0, Mathf.Max(0, keys.Length - 1));
        row.maskReferenceKey = keys.Length == 0 ? string.Empty : keys[index];
    }

    public bool AutoBindMaskReferenceForRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.isFolder) return false;
        SkyPrisonAnimationRigRow mask = GuessMaskReferenceForRow(row);
        if (mask == null) return false;
        row.maskReferenceKey = mask.key;
        return true;
    }

    public int AutoBindCommonMaskReferences()
    {
        int count = 0;
        List<SkyPrisonAnimationRigRow> rows = GetCurrentRows();
        for (int i = 0; i < rows.Count; i++)
            if (AutoBindMaskReferenceForRow(rows[i])) count++;
        return count;
    }

    private SkyPrisonAnimationRigRow GuessMaskReferenceForRow(SkyPrisonAnimationRigRow row)
    {
        string n = NormalizeLayerBindName((row.name ?? "") + " " + (row.semantic ?? "") + " " + (row.sourceSpriteName ?? "") + " " + (row.sourceLayerPath ?? ""));
        bool left = n.Contains("left") || n.Contains("_l") || n.Contains(" l ") || n.Contains("左");
        bool right = n.Contains("right") || n.Contains("_r") || n.Contains(" r ") || n.Contains("右");

        bool isEyeInner = n.Contains("pupil") || n.Contains("iris") || n.Contains("黒目") || n.Contains("黑眼") || n.Contains("眼黑") || n.Contains("瞳") || n.Contains("虹彩") || n.Contains("eye_black") || n.Contains("eyeinner");
        if (isEyeInner)
        {
            SkyPrisonAnimationRigRow eyeWhite = FindBestMaskCandidate(row, left, right,
                new string[] { "eye_white", "white_eye", "sclera", "白目", "眼白", "eye base", "eye_base", "眼_白" });
            if (eyeWhite != null) return eyeWhite;
        }

        bool isEyeHighlight = n.Contains("highlight") || n.Contains("hi_light") || n.Contains("catchlight") || n.Contains("眼光") || n.Contains("高光");
        if (isEyeHighlight)
        {
            SkyPrisonAnimationRigRow eyeBase = FindBestMaskCandidate(row, left, right,
                new string[] { "eye_white", "white_eye", "sclera", "白目", "眼白", "eye_base", "pupil", "iris", "瞳", "虹彩" });
            if (eyeBase != null) return eyeBase;
        }

        return null;
    }

    private SkyPrisonAnimationRigRow FindBestMaskCandidate(SkyPrisonAnimationRigRow row, bool left, bool right, string[] keywords)
    {
        SkyPrisonAnimationRigRow best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow c = PsbRows[i];
            if (c == null || c.isFolder || c == row || string.IsNullOrEmpty(c.key)) continue;
            string n = NormalizeLayerBindName((c.name ?? "") + " " + (c.semantic ?? "") + " " + (c.sourceSpriteName ?? "") + " " + (c.sourceLayerPath ?? ""));
            int score = 0;
            for (int k = 0; k < keywords.Length; k++) if (n.Contains(keywords[k].ToLowerInvariant())) score += 10;
            if (score <= 0) continue;
            if (left && (n.Contains("left") || n.Contains("_l") || n.Contains(" l ") || n.Contains("左"))) score += 4;
            if (right && (n.Contains("right") || n.Contains("_r") || n.Contains(" r ") || n.Contains("右"))) score += 4;
            if (left && (n.Contains("right") || n.Contains("_r") || n.Contains(" r ") || n.Contains("右"))) score -= 3;
            if (right && (n.Contains("left") || n.Contains("_l") || n.Contains(" l ") || n.Contains("左"))) score -= 3;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }
    public SkyPrisonAnimationRigRow FindCurrentSelectedRigForBinding(){ SkyPrisonAnimationRigRow rig=FindRigRow(LastSelectedRigKey); if(rig!=null)return rig; if(StructureTab==SkyPrisonAnimationStructureTab.Rig&&SelectedRig>=0&&SelectedRig<RigRows.Count)return RigRows[SelectedRig]; return null; }
    public SkyPrisonAnimationRigRow FindCurrentSelectedPsbForBinding(){ SkyPrisonAnimationRigRow psb=FindPsbRow(LastSelectedPsbLayerKey); if(psb!=null)return psb; if(StructureTab==SkyPrisonAnimationStructureTab.PsbLayer&&SelectedRig>=0&&SelectedRig<PsbRows.Count&&!PsbRows[SelectedRig].isFolder)return PsbRows[SelectedRig]; return null; }
    public bool BindRememberedPsbToRememberedRig(){ SkyPrisonAnimationRigRow rig=FindCurrentSelectedRigForBinding(); SkyPrisonAnimationRigRow psb=FindCurrentSelectedPsbForBinding(); return BindPsbLayerToRig(psb,rig,true); }
    public bool BindPsbLayerToRig(SkyPrisonAnimationRigRow psb,SkyPrisonAnimationRigRow rig,bool markManual){ if(psb==null||rig==null||psb.isFolder||rig.isFolder)return false; psb.boundRigKey=rig.key; psb.boundRigName=rig.name; psb.bindMode=markManual?"手动":"自动"; psb.bindConfidence=markManual?1f:psb.bindConfidence; psb.mapped=true; CopyPsbSourceToRig(psb,rig); rig.mapped=true; LastSelectedRigKey=rig.key; LastSelectedPsbLayerKey=psb.key; return true; }
    public void UnbindRigFromPsb(SkyPrisonAnimationRigRow rig){ if(rig==null)return; for(int i=0;i<PsbRows.Count;i++){ if(PsbRows[i].boundRigKey==rig.key){ PsbRows[i].boundRigKey=""; PsbRows[i].boundRigName=""; PsbRows[i].bindMode="未绑定"; PsbRows[i].bindConfidence=0f; PsbRows[i].mapped=false; }} ClearRigPsbSource(rig); }
    public void RefreshRigLinksFromPsbBindings(){ ClearRigPsbLinksOnly(); for(int i=0;i<PsbRows.Count;i++){ SkyPrisonAnimationRigRow psb=PsbRows[i]; if(psb==null||psb.isFolder||string.IsNullOrEmpty(psb.boundRigKey))continue; SkyPrisonAnimationRigRow rig=FindRigRow(psb.boundRigKey); if(rig!=null&&string.IsNullOrEmpty(rig.sourceAssetPath))CopyPsbSourceToRig(psb,rig); }}
    private void ClearRigPsbLinksOnly(){ for(int i=0;i<RigRows.Count;i++)ClearRigPsbSource(RigRows[i]); }
    private void ClearRigPsbSource(SkyPrisonAnimationRigRow rig){ if(rig==null)return; rig.sourceAssetPath=""; rig.sourceSpriteName=""; rig.sourceLayerPath=""; rig.boundRigKey=""; rig.boundRigName=""; }
    private void CopyPsbSourceToRig(SkyPrisonAnimationRigRow psb,SkyPrisonAnimationRigRow rig){ if(psb==null||rig==null)return; rig.sourceAssetPath=psb.sourceAssetPath; rig.sourceSpriteName=psb.sourceSpriteName; rig.sourceLayerPath=string.IsNullOrEmpty(psb.sourceLayerPath)?psb.name:psb.sourceLayerPath; rig.boundRigKey=psb.key; rig.boundRigName=psb.name; rig.previewColor=psb.previewColor; rig.psbLayerWeight=psb.psbLayerWeight; rig.usePsbLayerWeight=psb.usePsbLayerWeight; }

    public float GetEffectiveLayerOrderWeight(SkyPrisonAnimationRigRow row)
    {
        if (row == null) return 0f;
        float baseWeight = row.usePsbLayerWeight ? row.psbLayerWeight : 0f;
        return EvaluateLayerOrderKeyframeWeight(row.key, CurrentAction().key, CurrentTime, baseWeight + row.manualLayerWeightOffset);
    }

    public float EvaluateLayerOrderKeyframeWeight(string layerKey, string actionKey, float time, float fallback)
    {
        SkyPrisonAnimationLayerOrderKeyframe prev = null;
        SkyPrisonAnimationLayerOrderKeyframe next = null;

        for (int i = 0; i < LayerOrderKeyframes.Count; i++)
        {
            SkyPrisonAnimationLayerOrderKeyframe k = LayerOrderKeyframes[i];
            if (k == null || k.layerKey != layerKey || k.actionKey != actionKey)
                continue;

            if (k.time <= time && (prev == null || k.time > prev.time))
                prev = k;

            if (k.time >= time && (next == null || k.time < next.time))
                next = k;
        }

        if (prev == null && next == null) return fallback;
        if (prev == null) return next.orderWeight;
        if (next == null) return prev.orderWeight;
        if (Mathf.Abs(next.time - prev.time) < 0.0001f) return next.orderWeight;

        float t = Mathf.InverseLerp(prev.time, next.time, time);
        return Mathf.Lerp(prev.orderWeight, next.orderWeight, t);
    }

    public void SetLayerOrderKeyframe(SkyPrisonAnimationRigRow row, float time, float orderWeight)
    {
        if (row == null) return;

        string actionKey = CurrentAction().key;
        time = Mathf.Clamp(time, 0f, Mathf.Max(0.01f, TimelineDurationSeconds));

        for (int i = 0; i < LayerOrderKeyframes.Count; i++)
        {
            SkyPrisonAnimationLayerOrderKeyframe k = LayerOrderKeyframes[i];
            if (k != null && k.layerKey == row.key && k.actionKey == actionKey && Mathf.Abs(k.time - time) < 0.001f)
            {
                k.orderWeight = orderWeight;
                return;
            }
        }

        LayerOrderKeyframes.Add(new SkyPrisonAnimationLayerOrderKeyframe
        {
            actionKey = actionKey,
            layerKey = row.key,
            time = time,
            orderWeight = orderWeight
        });
    }

    public void ClearLayerOrderKeyframes(SkyPrisonAnimationRigRow row)
    {
        if (row == null) return;
        string actionKey = CurrentAction().key;
        LayerOrderKeyframes.RemoveAll(k => k != null && k.layerKey == row.key && k.actionKey == actionKey);
    }

    public List<SkyPrisonAnimationRigRow> GetLayerWeightTargetRows(SkyPrisonAnimationRigRow row)
    {
        List<SkyPrisonAnimationRigRow> result = new List<SkyPrisonAnimationRigRow>();
        if (row == null)
            return result;

        HashSet<string> used = new HashSet<string>();
        System.Action<SkyPrisonAnimationRigRow> add = r =>
        {
            if (r == null || string.IsNullOrEmpty(r.key) || r.isFolder)
                return;
            if (used.Add(r.key))
                result.Add(r);
        };

        if (PsbRows.Contains(row))
        {
            add(row);
            return result;
        }

        for (int i = 0; i < PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow psb = PsbRows[i];
            if (psb != null && !psb.isFolder && psb.boundRigKey == row.key)
                add(psb);
        }

        if (result.Count == 0)
            add(row);

        return result;
    }

    public void SetLayerOrderKeyframeForTargets(List<SkyPrisonAnimationRigRow> targets, float delta)
    {
        if (targets == null || targets.Count == 0)
            return;

        PushStructureUndo();
        for (int i = 0; i < targets.Count; i++)
        {
            SkyPrisonAnimationRigRow r = targets[i];
            if (r == null) continue;
            float current = GetEffectiveLayerOrderWeight(r);
            SetLayerOrderKeyframe(r, CurrentTime, current + delta);
        }
        GUI.changed = true;
    }

    public void SetLayerOrderKeyframeForTargets(List<SkyPrisonAnimationRigRow> targets, float time, float orderWeight)
    {
        if (targets == null || targets.Count == 0)
            return;

        PushStructureUndo();
        for (int i = 0; i < targets.Count; i++)
            SetLayerOrderKeyframe(targets[i], time, orderWeight);
        GUI.changed = true;
    }

    public void SetLayerOrderKeyframeForTargetRows(List<SkyPrisonAnimationRigRow> targets, float delta)
    {
        SetLayerOrderKeyframeForTargets(targets, delta);
    }

    public void SetLayerOrderKeyframeForTargetRows(List<SkyPrisonAnimationRigRow> targets, float time, float orderWeight)
    {
        SetLayerOrderKeyframeForTargets(targets, time, orderWeight);
    }

    public void ClearLayerOrderKeyframesForTargets(List<SkyPrisonAnimationRigRow> targets)
    {
        if (targets == null || targets.Count == 0)
            return;

        PushStructureUndo();
        for (int i = 0; i < targets.Count; i++)
            ClearLayerOrderKeyframes(targets[i]);
        GUI.changed = true;
    }

    public void ClearLayerOrderKeyframesForTargetRows(List<SkyPrisonAnimationRigRow> targets)
    {
        ClearLayerOrderKeyframesForTargets(targets);
    }


    public List<SkyPrisonAnimationRigRow> GetLayerWeightTargetRows()
    {
        return GetLayerWeightTargetRows(GetSelectedRigRow());
    }

    public void SetLayerOrderKeyframeForTargets(IEnumerable<SkyPrisonAnimationRigRow> targets, float delta)
    {
        if (targets == null) return;
        SetLayerOrderKeyframeForTargets(new List<SkyPrisonAnimationRigRow>(targets), delta);
    }

    public void SetLayerOrderKeyframeForTargets(List<SkyPrisonAnimationRigRow> targets, float delta, bool relative)
    {
        if (relative)
        {
            SetLayerOrderKeyframeForTargets(targets, delta);
            return;
        }
        SetLayerOrderKeyframeForTargets(targets, CurrentTime, delta);
    }

    public void SetLayerOrderKeyframeForTargets(List<SkyPrisonAnimationRigRow> targets, float time, float value, bool relative)
    {
        if (relative)
        {
            if (targets == null || targets.Count == 0) return;
            PushStructureUndo();
            for (int i = 0; i < targets.Count; i++)
            {
                SkyPrisonAnimationRigRow r = targets[i];
                if (r == null) continue;
                float current = GetEffectiveLayerOrderWeight(r);
                SetLayerOrderKeyframe(r, time, current + value);
            }
            GUI.changed = true;
            return;
        }
        SetLayerOrderKeyframeForTargets(targets, time, value);
    }

    public void ClearLayerOrderKeyframesForTargets(IEnumerable<SkyPrisonAnimationRigRow> targets)
    {
        if (targets == null) return;
        ClearLayerOrderKeyframesForTargets(new List<SkyPrisonAnimationRigRow>(targets));
    }
    public string CurrentActionKey(){ SkyPrisonAnimationActionRow a=CurrentAction(); return a!=null ? (a.key ?? string.Empty) : string.Empty; }
    public int SnapFrame(int frame){ return Mathf.Clamp(frame, 0, TimelineTotalFrames); }
    public float FrameToSeconds(int frame){ return SnapFrame(frame) / Mathf.Max(1f, TimelineFrameRate); }
    public int SecondsToTimelineFrame(float seconds){ return SnapFrame(Mathf.RoundToInt(seconds * Mathf.Max(1, TimelineFrameRate))); }

    public SkyPrisonAnimationRigRow GetTimelineTargetRow()
    {
        // 时间线轨道只接 Rig 骨骼节点。
        // PSB 图层只是图像选择 / 绑定 / 检查器编辑对象，不能因为选中 PSB 就自动进入时间轴。
        List<SkyPrisonAnimationRigRow> selectedRows = GetTimelineTargetRows();
        if (selectedRows != null && selectedRows.Count > 0)
            return selectedRows[0];

        SkyPrisonAnimationRigRow rig = FindRigRow(LastSelectedRigKey);
        if (rig != null && !rig.isFolder) return rig;
        return null;
    }


    public SkyPrisonAnimationRigRow GetActiveTimelineTrackRow()
    {
        if (IsFootstepTimelineTrack(ActiveTimelineTrackKey))
            return null;

        if (!string.IsNullOrEmpty(ActiveTimelineTrackKey))
        {
            SkyPrisonAnimationRigRow row = FindAnyStructureRow(ActiveTimelineTrackKey);
            if (row != null && !row.isFolder)
                return row;
        }
        SkyPrisonAnimationTimelineKeyframe k = GetSelectedTimelineKeyframe();
        if (k != null && !string.IsNullOrEmpty(k.targetKey))
        {
            SkyPrisonAnimationRigRow row = FindAnyStructureRow(k.targetKey);
            if (row != null && !row.isFolder)
            {
                ActiveTimelineTrackKey = row.key;
                return row;
            }
        }
        return GetTimelineTargetRow();
    }

    public bool SelectTimelineTrack(string targetKey, bool syncStructureSelection)
    {
        if (string.IsNullOrEmpty(targetKey)) return false;

        // 脚步声是时间线固定功能轨，不对应 Rig / PSB 结构节点。
        // 它不能被删除，也不能因为左侧结构选择而丢失。
        if (IsFootstepTimelineTrack(targetKey))
        {
            ActiveTimelineTrackKey = FootstepTimelineTrackKey;
            return true;
        }
        if (IsMotionTimelineTrack(targetKey))
        {
            ActiveTimelineTrackKey = MotionTimelineTrackKey;
            return true;
        }

        // 时间线轨道只允许锁 Rig。PSB 图层被选中时不创建 / 不切换轨道。
        SkyPrisonAnimationRigRow row = FindRigRow(targetKey);
        if (row == null || row.isFolder) return false;

        ActiveTimelineTrackKey = row.key;
        if (syncStructureSelection) SelectStructureRowByKey(row.key);
        return true;
    }

    public bool IsFootstepTimelineTrack(string targetKey)
    {
        return string.Equals(targetKey, FootstepTimelineTrackKey, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsMotionTimelineTrack(string targetKey)
    {
        return string.Equals(targetKey, MotionTimelineTrackKey, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsFootstepTimelineKeyframe(SkyPrisonAnimationTimelineKeyframe key)
    {
        if (key == null) return false;
        return IsFootstepTimelineTrack(key.targetKey)
            || string.Equals(key.targetKind, FootstepTimelineTargetKind, StringComparison.OrdinalIgnoreCase);
    }

    public SkyPrisonAnimationTimelineKeyframe InsertOrUpdateFootstepMarker(int frame)
    {
        string actionKey = CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey)) return null;

        int snappedFrame = SnapFrame(frame);
        SkyPrisonAnimationTimelineKeyframe marker = new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = actionKey,
            targetKey = FootstepTimelineTrackKey,
            targetName = FootstepTimelineTrackLabel,
            targetKind = FootstepTimelineTargetKind,
            layerWeightTargetKey = string.Empty,
            frame = snappedFrame,
            runtimeOffset = Vector2.zero,
            useRuntimeBoneRootOffset = false,
            runtimeBoneRootOffset = Vector2.zero,
            useRuntimeBoneHeadOffset = false,
            runtimeBoneHeadOffset = Vector2.zero,
            opacity = 1f,
            layerWeight = 0f,
            manualLayerWeightOffset = 0f,
            useMeshDeform = false,
            meshDeformColumns = 0,
            meshDeformRows = 0,
            meshDeformPoints = new List<SkyPrisonMeshDeformPoint>()
        };

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k != null
                && string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)
                && IsFootstepTimelineTrack(k.targetKey)
                && SnapFrame(k.frame) == snappedFrame)
            {
                TimelineKeyframes[i] = marker;
                SelectedTimelineKeyframeIndex = i;
                ActiveTimelineTrackKey = FootstepTimelineTrackKey;
                CurrentTime = FrameToSeconds(snappedFrame);
                return marker;
            }
        }

        TimelineKeyframes.Add(marker);
        SortTimelineKeyframes();
        SelectedTimelineKeyframeIndex = FindTimelineKeyframeIndexByVisibleSlot(marker);
        ActiveTimelineTrackKey = FootstepTimelineTrackKey;
        CurrentTime = FrameToSeconds(snappedFrame);
        return marker;
    }


    public SkyPrisonAnimationMotionKeyframe InsertOrUpdateMotionKeyframe(int frame, Vector2 visualOffset)
    {
        string actionKey = CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey)) return null;
        int snappedFrame = SnapFrame(frame);
        for (int i = 0; i < MotionKeyframes.Count; i++)
        {
            SkyPrisonAnimationMotionKeyframe k = MotionKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != snappedFrame) continue;
            k.visualOffset = visualOffset;
            SelectedMotionKeyframeIndex = i;
            ActiveTimelineTrackKey = MotionTimelineTrackKey;
            CurrentTime = FrameToSeconds(snappedFrame);
            return k;
        }
        SkyPrisonAnimationMotionKeyframe created = new SkyPrisonAnimationMotionKeyframe { actionKey = actionKey, frame = snappedFrame, visualOffset = visualOffset };
        MotionKeyframes.Add(created);
        SortMotionKeyframes();
        SelectedMotionKeyframeIndex = MotionKeyframes.IndexOf(created);
        ActiveTimelineTrackKey = MotionTimelineTrackKey;
        CurrentTime = FrameToSeconds(snappedFrame);
        return created;
    }

    public void SortMotionKeyframes()
    {
        MotionKeyframes.Sort(delegate(SkyPrisonAnimationMotionKeyframe a, SkyPrisonAnimationMotionKeyframe b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int c = string.Compare(a.actionKey, b.actionKey, StringComparison.Ordinal);
            if (c != 0) return c;
            return a.frame.CompareTo(b.frame);
        });
    }

    public Vector2 EvaluateMotionVisualOffset()
    {
        return EvaluateMotionVisualOffset(CurrentActionKey(), TimelineCurrentFrameFloat);
    }

    public Vector2 EvaluateMotionVisualOffset(string actionKey, float frameFloat)
    {
        if (string.IsNullOrEmpty(actionKey)) return Vector2.zero;
        SkyPrisonAnimationMotionKeyframe prev = null, next = null;
        for (int i = 0; i < MotionKeyframes.Count; i++)
        {
            SkyPrisonAnimationMotionKeyframe k = MotionKeyframes[i];
            if (k == null || !string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (k.frame <= frameFloat && (prev == null || k.frame > prev.frame)) prev = k;
            if (k.frame >= frameFloat && (next == null || k.frame < next.frame)) next = k;
        }
        if (prev == null && next == null) return Vector2.zero;
        if (prev == null) return next.visualOffset;
        if (next == null) return prev.visualOffset;
        if (prev == next || prev.frame == next.frame) return prev.visualOffset;
        float t = Mathf.InverseLerp(prev.frame, next.frame, frameFloat);
        t = SmoothTimelineInterpolation(prev.frame, next.frame, frameFloat);
        return Vector2.Lerp(prev.visualOffset, next.visualOffset, t);
    }

    public int FindMotionKeyframeIndex(string actionKey, int frame)
    {
        int snapped = SnapFrame(frame);
        for (int i = 0; i < MotionKeyframes.Count; i++)
        {
            SkyPrisonAnimationMotionKeyframe k = MotionKeyframes[i];
            if (k != null && string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase) && SnapFrame(k.frame) == snapped)
                return i;
        }
        return -1;
    }

    public bool IsTimelineTrackLockedTo(string targetKey)
    {
        return TimelineTrackLockEnabled && !string.IsNullOrEmpty(ActiveTimelineTrackKey) && ActiveTimelineTrackKey == targetKey;
    }

    public bool CanEditAnimatedTarget(string targetKey)
    {
        if (!TimelineTrackLockEnabled || string.IsNullOrEmpty(ActiveTimelineTrackKey)) return true;
        return ActiveTimelineTrackKey == targetKey;
    }

    public bool ShouldRedirectAnimatedEditToTimelineKeyframe(SkyPrisonAnimationRigRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.key)) return false;
        if (!CanEditAnimatedTarget(row.key)) return false;
        return TimelineTrackLockEnabled && !string.IsNullOrEmpty(ActiveTimelineTrackKey) && ActiveTimelineTrackKey == row.key;
    }

    public SkyPrisonAnimationTimelineKeyframe EnsureCurrentFrameKeyframeForRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return null;
        SelectTimelineTrack(row.key, false);
        int frame = SnapFrame(TimelineCurrentFrame);
        CurrentTime = FrameToSeconds(frame);
        SelectTimelineKeyframe(row.key, frame);
        SkyPrisonAnimationTimelineKeyframe selected = GetSelectedTimelineKeyframe();
        if (selected != null && selected.actionKey == CurrentActionKey() && selected.targetKey == row.key && SnapFrame(selected.frame) == frame)
            return selected;
        return InsertOrUpdateTimelineKeyframe(row, frame);
    }

    public SkyPrisonAnimationTimelineKeyframe EnsureCurrentFrameRigOffsetKeyframeForRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return null;

        SelectTimelineTrack(row.key, false);

        string actionKey = CurrentActionKey();
        int frame = SnapFrame(TimelineCurrentFrame);

        // Root 端移动和 Head 端旋转一样，必须先给其它姿势点补保护 Key。
        // 否则第一次在中间帧移动骨骼根部时，这个位移会向前后整段插值扩散，表现为“一动就动前后”。
        EnsureRigRootOffsetProtectionKeyframes(actionKey, row, frame);

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, row.key, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != frame) continue;
            if (string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;

            k.targetKind = "Rig";
            k.layerWeightTargetKey = row.key;
            SelectedTimelineKeyframeIndex = i;
            CurrentTime = FrameToSeconds(frame);
            return k;
        }

        SkyPrisonAnimationTimelineKeyframe created = CaptureTimelineKeyframe(row, frame);
        if (created == null) return null;
        created.targetKind = "Rig";
        created.layerWeightTargetKey = row.key;
        TimelineKeyframes.Add(created);
        SortTimelineKeyframes();
        SelectedTimelineKeyframeIndex = TimelineKeyframes.IndexOf(created);
        CurrentTime = FrameToSeconds(frame);
        return created;
    }

    public SkyPrisonAnimationTimelineKeyframe InsertOrUpdateTimelineKeyframeForActiveTrack()
    {
        if (IsFootstepTimelineTrack(ActiveTimelineTrackKey))
            return InsertOrUpdateFootstepMarker(TimelineCurrentFrame);

        SkyPrisonAnimationRigRow row = GetActiveTimelineTrackRow();
        if (row == null) return null;
        return InsertOrUpdateTimelineKeyframe(row, TimelineCurrentFrame);
    }

    private void EnsureRigRootOffsetProtectionKeyframes(string actionKey, SkyPrisonAnimationRigRow row, int currentFrame)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return;
        if (string.IsNullOrEmpty(actionKey)) return;

        int snappedCurrent = SnapFrame(currentFrame);
        List<int> guardFrames = CollectManualAngleProtectionFrames(actionKey, snappedCurrent);
        if (guardFrames == null || guardFrames.Count == 0) return;

        // 先采样、后写入。不能边写边采样，否则前一个保护 Key 会影响后一个保护 Key 的结果。
        Dictionary<int, Vector2> sampledRoots = new Dictionary<int, Vector2>();
        for (int i = 0; i < guardFrames.Count; i++)
        {
            int frame = SnapFrame(guardFrames[i]);
            if (frame == snappedCurrent) continue;

            Vector2 rootOffset;
            if (!TryEvaluateTimelineRuntimeBoneRootOffsetAtFrame(actionKey, row.key, frame, out rootOffset))
                rootOffset = Vector2.zero;

            sampledRoots[frame] = rootOffset;
        }

        foreach (KeyValuePair<int, Vector2> kv in sampledRoots)
            AddOrUpdateRigRootOffsetProtectionKeyframe(actionKey, row, kv.Key, kv.Value);

        SortTimelineKeyframes();
    }

    private void AddOrUpdateRigRootOffsetProtectionKeyframe(string actionKey, SkyPrisonAnimationRigRow row, int frame, Vector2 rootOffset)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return;
        if (string.IsNullOrEmpty(actionKey)) return;

        int snappedFrame = SnapFrame(frame);
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, row.key, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != snappedFrame) continue;
            if (string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;

            k.targetKind = "Rig";
            k.targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name;
            k.layerWeightTargetKey = row.key;

            // 保护 Root 位移时只写“整条骨骼同量平移”。
            // Head 也写同一个值，是为了保证这个保护 Key 不改变骨骼长度和角度。
            k.useRuntimeBoneRootOffset = true;
            k.runtimeBoneRootOffset = rootOffset;
            k.useRuntimeBoneHeadOffset = true;
            k.runtimeBoneHeadOffset = rootOffset;
            return;
        }

        TimelineKeyframes.Add(new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = actionKey,
            targetKey = row.key,
            targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name,
            targetKind = row.isMeshDeformer ? "MeshDeformer" : "Rig",
            layerWeightTargetKey = row.key,
            frame = snappedFrame,
            runtimeOffset = Vector2.zero,
            useRuntimeBoneRootOffset = true,
            runtimeBoneRootOffset = rootOffset,
            useRuntimeBoneHeadOffset = true,
            runtimeBoneHeadOffset = rootOffset,
            opacity = Mathf.Clamp01(row.opacity),
            layerWeight = row.psbLayerWeight,
            manualLayerWeightOffset = row.manualLayerWeightOffset
        });
    }

    private bool TryEvaluateTimelineRuntimeBoneRootOffsetAtFrame(string actionKey, string targetKey, int frame, out Vector2 rootOffset)
    {
        rootOffset = Vector2.zero;
        if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(targetKey)) return false;

        int snappedFrame = SnapFrame(frame);
        SkyPrisonAnimationTimelineKeyframe prev = null;
        SkyPrisonAnimationTimelineKeyframe next = null;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;

            int keyFrame = SnapFrame(k.frame);
            if (keyFrame <= snappedFrame && (prev == null || keyFrame > SnapFrame(prev.frame))) prev = k;
            if (keyFrame >= snappedFrame && (next == null || keyFrame < SnapFrame(next.frame))) next = k;
        }

        bool prevHas = prev != null && prev.useRuntimeBoneRootOffset;
        bool nextHas = next != null && next.useRuntimeBoneRootOffset;
        if (!prevHas && !nextHas)
            return false;

        if (!prevHas)
        {
            rootOffset = next.runtimeBoneRootOffset;
            return true;
        }

        if (!nextHas || SnapFrame(next.frame) == SnapFrame(prev.frame))
        {
            rootOffset = prev.runtimeBoneRootOffset;
            return true;
        }

        float t = SmoothTimelineInterpolation(SnapFrame(prev.frame), SnapFrame(next.frame), snappedFrame);
        rootOffset = Vector2.LerpUnclamped(prev.runtimeBoneRootOffset, next.runtimeBoneRootOffset, t);
        return true;
    }

    public void SelectStructureRowByKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        // 轨道锁只反选 Rig 节点，不跳到 PSB 图层页，避免“选 PSB -> 进时间轴”的错觉。
        for (int i = 0; i < RigRows.Count; i++)
        {
            if (RigRows[i] != null && RigRows[i].key == key)
            {
                StructureTab = SkyPrisonAnimationStructureTab.Rig;
                SelectedRig = i;
                SelectedRigRows.Clear();
                SelectedRigIndices.Clear();
                SelectedRigRows.Add(i);
                SelectedRigIndices.Add(i);
                RememberSelectedStructureRow(RigRows[i]);
                return;
            }
        }
    }

    public List<SkyPrisonAnimationRigRow> GetTimelineTargetRows()
    {
        List<SkyPrisonAnimationRigRow> result = new List<SkyPrisonAnimationRigRow>();
        HashSet<string> usedKeys = new HashSet<string>();

        // 时间线只跟随 Rig 页节点。
        // 当前在 PSB 图层页选择图层时，不把 PSB 行塞进轨道。
        if (StructureTab != SkyPrisonAnimationStructureTab.Rig)
            return result;

        List<SkyPrisonAnimationRigRow> rows = RigRows;

        System.Action<int> addByIndex = delegate(int index)
        {
            if (index < 0 || index >= rows.Count)
                return;

            SkyPrisonAnimationRigRow row = rows[index];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
                return;

            if (usedKeys.Add(row.key))
                result.Add(row);
        };

        if (SelectedRigRows != null && SelectedRigRows.Count > 0)
        {
            List<int> indices = new List<int>(SelectedRigRows);
            indices.Sort();
            for (int i = 0; i < indices.Count; i++)
                addByIndex(indices[i]);
        }

        if (SelectedRigIndices != null && SelectedRigIndices.Count > 0)
        {
            List<int> indices = new List<int>(SelectedRigIndices);
            indices.Sort();
            for (int i = 0; i < indices.Count; i++)
                addByIndex(indices[i]);
        }

        addByIndex(SelectedRig);

        return result;
    }


    public void SyncActiveTimelineTrackToCurrentSelection(bool clearStaleKeyframeSelection)
    {
        // 显示全部轨道关闭时：左侧选哪里，时间线就刷新哪里。
        // 显示全部轨道开启时：不要再被左侧单选强制刷新，只校验当前活动轨道是否仍存在。
        List<string> visibleKeys = GetTimelineTrackKeysForCurrentAction();

        bool activeStillVisible = false;
        if (!string.IsNullOrEmpty(ActiveTimelineTrackKey))
        {
            for (int i = 0; i < visibleKeys.Count; i++)
            {
                if (visibleKeys[i] == ActiveTimelineTrackKey)
                {
                    activeStillVisible = true;
                    break;
                }
            }
        }

        if (!activeStillVisible)
        {
            ActiveTimelineTrackKey = visibleKeys.Count > 0 ? visibleKeys[0] : string.Empty;
            if (clearStaleKeyframeSelection)
                SelectedTimelineKeyframeIndex = -1;
        }

        SkyPrisonAnimationTimelineKeyframe selected = GetSelectedTimelineKeyframe();
        if (clearStaleKeyframeSelection
            && selected != null
            && !string.IsNullOrEmpty(ActiveTimelineTrackKey)
            && selected.targetKey != ActiveTimelineTrackKey)
        {
            SelectedTimelineKeyframeIndex = -1;
        }
    }

    public string ResolveActivePreviewFocusRigKey()
    {
        if (string.IsNullOrEmpty(ActiveTimelineTrackKey) || IsFootstepTimelineTrack(ActiveTimelineTrackKey))
            return string.Empty;

        SkyPrisonAnimationRigRow row = FindAnyStructureRow(ActiveTimelineTrackKey);
        if (row == null)
            return ActiveTimelineTrackKey;

        if (!string.IsNullOrEmpty(row.boundRigKey))
            return row.boundRigKey;

        return row.key;
    }

    public SkyPrisonAnimationTimelineKeyframe CaptureTimelineKeyframe(SkyPrisonAnimationRigRow row, int frame)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return null;
        return new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = CurrentActionKey(),
            targetKey = row.key,
            targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name,
            targetKind = "Rig",
            layerWeightTargetKey = row.key,
            frame = SnapFrame(frame),
            runtimeOffset = row.useManualRigLayerOffset ? row.manualRigLayerOffset : Vector2.zero,
            useRuntimeBoneRootOffset = row.useRuntimeBoneRootOffset,
            runtimeBoneRootOffset = row.runtimeBoneRootOffset,
            useRuntimeBoneHeadOffset = row.useRuntimeBoneHeadOffset,
            runtimeBoneHeadOffset = row.runtimeBoneHeadOffset,
            opacity = Mathf.Clamp01(row.opacity),
            layerWeight = row.psbLayerWeight,
            manualLayerWeightOffset = row.manualLayerWeightOffset,
            useMeshDeform = row.isMeshDeformer,
            meshDeformColumns = row.isMeshDeformer ? row.meshDeformColumns : 0,
            meshDeformRows = row.isMeshDeformer ? row.meshDeformRows : 0,
            meshDeformPoints = row.isMeshDeformer ? SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(row.meshDeformPoints) : new List<SkyPrisonMeshDeformPoint>()
        };
    }

    public SkyPrisonAnimationTimelineKeyframe InsertOrUpdateTimelineKeyframeForSelectedRow()
    {
        if (TimelineTrackLockEnabled && !string.IsNullOrEmpty(ActiveTimelineTrackKey))
            return InsertOrUpdateTimelineKeyframeForActiveTrack();

        List<SkyPrisonAnimationRigRow> rows = GetTimelineTargetRows();
        if (rows != null && rows.Count > 0)
            return InsertOrUpdateTimelineKeyframe(rows[0], TimelineCurrentFrame);

        SkyPrisonAnimationRigRow row = GetTimelineTargetRow();
        return InsertOrUpdateTimelineKeyframe(row, TimelineCurrentFrame);
    }

    public List<SkyPrisonAnimationTimelineKeyframe> InsertOrUpdateTimelineKeyframesForSelectedRows()
    {
        List<SkyPrisonAnimationTimelineKeyframe> created = new List<SkyPrisonAnimationTimelineKeyframe>();
        List<SkyPrisonAnimationRigRow> rows = GetTimelineTargetRows();
        if (rows == null || rows.Count == 0)
        {
            SkyPrisonAnimationTimelineKeyframe single = InsertOrUpdateTimelineKeyframeForSelectedRow();
            if (single != null)
                created.Add(single);
            return created;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = InsertOrUpdateTimelineKeyframe(rows[i], TimelineCurrentFrame);
            if (k != null)
                created.Add(k);
        }

        return created;
    }

    public SkyPrisonAnimationTimelineKeyframe InsertOrUpdateTimelineKeyframe(SkyPrisonAnimationRigRow row, int frame)
    {
        if (row == null || FindRigRow(row.key) == null) return null;
        SkyPrisonAnimationTimelineKeyframe key = CaptureTimelineKeyframe(row, frame);
        if (key == null) return null;
        ActiveTimelineTrackKey = row.key;
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k != null && k.actionKey == key.actionKey && k.targetKey == key.targetKey && k.frame == key.frame)
            {
                TimelineKeyframes[i] = key;
                SelectedTimelineKeyframeIndex = i;
                return key;
            }
        }
        TimelineKeyframes.Add(key);
        SortTimelineKeyframes();
        SelectedTimelineKeyframeIndex = TimelineKeyframes.IndexOf(key);
        return key;
    }

    public void SortTimelineKeyframes()
    {
        TimelineKeyframes.Sort(delegate(SkyPrisonAnimationTimelineKeyframe a, SkyPrisonAnimationTimelineKeyframe b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int c = string.Compare(a.actionKey, b.actionKey, StringComparison.Ordinal);
            if (c != 0) return c;
            c = string.Compare(a.targetKey, b.targetKey, StringComparison.Ordinal);
            if (c != 0) return c;
            return a.frame.CompareTo(b.frame);
        });
    }

    public bool DeleteSelectedTimelineKeyframe()
    {
        if (SelectedTimelineKeyframeIndex < 0 || SelectedTimelineKeyframeIndex >= TimelineKeyframes.Count) return false;

        SkyPrisonAnimationTimelineKeyframe removed = TimelineKeyframes[SelectedTimelineKeyframeIndex];
        string removedActionKey = removed != null ? removed.actionKey : CurrentActionKey();
        int removedFrame = removed != null ? SnapFrame(removed.frame) : SnapFrame(TimelineCurrentFrame);
        bool removedManualAngle = removed != null && string.Equals(removed.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase);

        List<string> removedMeshDeformerTargets = new List<string>();
        CollectMeshDeformerTargetFromRemovedKey(removed, removedMeshDeformerTargets);

        TimelineKeyframes.RemoveAt(SelectedTimelineKeyframeIndex);
        SelectedTimelineKeyframeIndex = Mathf.Clamp(SelectedTimelineKeyframeIndex, -1, Mathf.Max(-1, TimelineKeyframes.Count - 1));

        if (removedManualAngle)
            RebuildManualPoseKeyFromExactRigAngleKeys(removedActionKey, removedFrame);

        if (removedMeshDeformerTargets.Count > 0)
            RefreshMeshDeformerRowsAfterTimelineKeyRemoval(removedActionKey, removedMeshDeformerTargets);

        if (string.Equals(removedActionKey, CurrentActionKey(), StringComparison.OrdinalIgnoreCase)
            && removedFrame == SnapFrame(TimelineCurrentFrame))
        {
            lastManualAngleSyncedFrame = int.MinValue;
            lastManualAngleSyncedActionKey = string.Empty;
            SyncManualAnglesFromCurrentFrame(true);
        }

        return true;
    }

    public bool SelectTimelineKeyframe(string targetKey, int frame)
    {
        string actionKey = CurrentActionKey();
        int snappedFrame = SnapFrame(frame);
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k != null
                && string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)
                && SnapFrame(k.frame) == snappedFrame)
            {
                SelectedTimelineKeyframeIndex = i;
                ActiveTimelineTrackKey = targetKey;
                CurrentTime = FrameToSeconds(snappedFrame);
                SyncManualAnglesFromCurrentFrame(true);
                return true;
            }
        }
        return false;
    }

    public bool LockCurrentFrameKeyframeForRigTarget(string targetKey, bool syncStructureSelection, bool preferRigAngle)
    {
        return LockCurrentFrameKeyframeForRigTarget(targetKey, syncStructureSelection, preferRigAngle, string.Empty);
    }

    public bool LockCurrentFrameKeyframeForRigTarget(string targetKey, bool syncStructureSelection, bool preferRigAngle, string preferredLayerWeightTargetKey)
    {
        if (string.IsNullOrEmpty(targetKey))
            return false;

        if (!SelectTimelineTrack(targetKey, syncStructureSelection))
            return false;

        int frame = SnapFrame(TimelineCurrentFrame);

        // 预览区拖拽骨骼线时，白线必须先吸附到“当前编辑帧”。
        // 否则 CurrentTime 还停在两个整数帧之间时，下一次重绘会从上一关键帧/下一关键帧插值回读，
        // 看起来就像刚拖出的骨骼线被刷新回了上一帧。
        CurrentTime = FrameToSeconds(frame);

        int index = FindCurrentFrameTimelineKeyframeIndex(targetKey, frame, preferRigAngle, preferredLayerWeightTargetKey);
        if (index >= 0)
        {
            SelectedTimelineKeyframeIndex = index;
            CurrentTime = FrameToSeconds(frame);
            SyncManualAnglesFromCurrentFrame(true);
            return true;
        }

        // 没有当前白线帧的关键帧时，只锁轨道，不偷偷生成关键帧。
        // 如果旧选择不是当前对象 / 当前帧，则清掉旧选择，避免后续参数写到别的帧。
        SkyPrisonAnimationTimelineKeyframe selected = GetSelectedTimelineKeyframe();
        if (selected != null
            && (!string.Equals(selected.actionKey, CurrentActionKey(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(selected.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)
                || SnapFrame(selected.frame) != frame))
        {
            SelectedTimelineKeyframeIndex = -1;
        }

        SyncManualAnglesFromCurrentFrame(true);
        return false;
    }

    private int FindCurrentFrameTimelineKeyframeIndex(string targetKey, int frame, bool preferRigAngle, string preferredLayerWeightTargetKey)
    {
        string actionKey = CurrentActionKey();
        int snappedFrame = SnapFrame(frame);
        int first = -1;
        int preferredRigAngle = -1;
        int preferredLayer = -1;
        int preferredNonAngle = -1;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != snappedFrame) continue;

            if (first < 0) first = i;

            bool isRigAngle = string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase);
            if (isRigAngle && preferredRigAngle < 0) preferredRigAngle = i;
            if (!isRigAngle && preferredNonAngle < 0) preferredNonAngle = i;

            if (!string.IsNullOrEmpty(preferredLayerWeightTargetKey)
                && string.Equals(k.layerWeightTargetKey, preferredLayerWeightTargetKey, StringComparison.OrdinalIgnoreCase))
            {
                preferredLayer = i;
            }
        }

        if (!string.IsNullOrEmpty(preferredLayerWeightTargetKey) && preferredLayer >= 0)
            return preferredLayer;

        if (preferRigAngle && preferredRigAngle >= 0)
            return preferredRigAngle;

        if (!preferRigAngle && preferredNonAngle >= 0)
            return preferredNonAngle;

        if (preferredRigAngle >= 0)
            return preferredRigAngle;

        return first;
    }

    public bool CopySelectedTimelineKeyframe()
    {
        // Motion 轨道是独立关键帧类型，不能塞进普通 TimelineKeyframeClipboard。
        if (IsMotionTimelineTrack(ActiveTimelineTrackKey))
        {
            int mi = SelectedMotionKeyframeIndex;
            if (mi < 0 || mi >= MotionKeyframes.Count)
                mi = FindMotionKeyframeIndex(CurrentActionKey(), TimelineCurrentFrame);

            if (mi < 0 || mi >= MotionKeyframes.Count)
                return false;

            SelectedMotionKeyframeIndex = mi;
            MotionKeyframeClipboard = MotionKeyframes[mi] != null ? MotionKeyframes[mi].Clone() : null;
            TimelineKeyframeClipboard = null;
            return MotionKeyframeClipboard != null;
        }

        if (!IsSelectedTimelineKeyframeValid())
        {
            int fallbackIndex = FindActiveTrackCurrentFrameKeyframeIndex();
            if (fallbackIndex >= 0)
                SelectedTimelineKeyframeIndex = fallbackIndex;
        }

        if (!IsSelectedTimelineKeyframeValid()) return false;
        SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[SelectedTimelineKeyframeIndex];
        TimelineKeyframeClipboard = k != null ? k.Clone() : null;
        MotionKeyframeClipboard = null;
        return TimelineKeyframeClipboard != null;
    }

    public bool CopyActiveTrackCurrentFrameKeyframe()
    {
        if (IsMotionTimelineTrack(ActiveTimelineTrackKey))
            return CopySelectedTimelineKeyframe();

        int index = FindActiveTrackCurrentFrameKeyframeIndex();
        if (index < 0) return false;

        SelectedTimelineKeyframeIndex = index;
        SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[index];
        TimelineKeyframeClipboard = k != null ? k.Clone() : null;
        MotionKeyframeClipboard = null;
        return TimelineKeyframeClipboard != null;
    }

    public bool DeleteActiveTrackCurrentFrameKeyframe()
    {
        int index = FindActiveTrackCurrentFrameKeyframeIndex();
        if (index < 0) return false;

        SelectedTimelineKeyframeIndex = index;
        return DeleteSelectedTimelineKeyframe();
    }

    public bool CutActiveTrackCurrentFrameKeyframe()
    {
        if (!CopyActiveTrackCurrentFrameKeyframe()) return false;
        return DeleteSelectedTimelineKeyframe();
    }

    public bool CutSelectedOrActiveTimelineKeyframe()
    {
        if (IsMotionTimelineTrack(ActiveTimelineTrackKey))
        {
            int mi = SelectedMotionKeyframeIndex;
            if (mi < 0 || mi >= MotionKeyframes.Count)
                mi = FindMotionKeyframeIndex(CurrentActionKey(), TimelineCurrentFrame);

            if (mi < 0 || mi >= MotionKeyframes.Count)
                return false;

            MotionKeyframeClipboard = MotionKeyframes[mi] != null ? MotionKeyframes[mi].Clone() : null;
            TimelineKeyframeClipboard = null;
            if (MotionKeyframeClipboard == null) return false;
            MotionKeyframes.RemoveAt(mi);
            SelectedMotionKeyframeIndex = -1;
            return true;
        }

        if (!IsSelectedTimelineKeyframeValid())
        {
            int fallbackIndex = FindActiveTrackCurrentFrameKeyframeIndex();
            if (fallbackIndex >= 0)
                SelectedTimelineKeyframeIndex = fallbackIndex;
        }

        if (!IsSelectedTimelineKeyframeValid()) return false;

        SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[SelectedTimelineKeyframeIndex];
        TimelineKeyframeClipboard = k != null ? k.Clone() : null;
        MotionKeyframeClipboard = null;
        if (TimelineKeyframeClipboard == null) return false;
        return DeleteSelectedTimelineKeyframe();
    }

    public bool DeleteSelectedOrActiveTimelineKeyframe()
    {
        if (IsMotionTimelineTrack(ActiveTimelineTrackKey))
        {
            int mi = SelectedMotionKeyframeIndex;
            if (mi < 0 || mi >= MotionKeyframes.Count) mi = FindMotionKeyframeIndex(CurrentActionKey(), TimelineCurrentFrame);
            if (mi >= 0 && mi < MotionKeyframes.Count) { MotionKeyframes.RemoveAt(mi); SelectedMotionKeyframeIndex = -1; return true; }
        }
        if (!IsSelectedTimelineKeyframeValid())
        {
            int fallbackIndex = FindActiveTrackCurrentFrameKeyframeIndex();
            if (fallbackIndex >= 0)
                SelectedTimelineKeyframeIndex = fallbackIndex;
        }

        return DeleteSelectedTimelineKeyframe();
    }

    public bool HasSelectedOrActiveTimelineKeyframe()
    {
        if (IsMotionTimelineTrack(ActiveTimelineTrackKey) && (SelectedMotionKeyframeIndex >= 0 && SelectedMotionKeyframeIndex < MotionKeyframes.Count || FindMotionKeyframeIndex(CurrentActionKey(), TimelineCurrentFrame) >= 0)) return true;
        return IsSelectedTimelineKeyframeValid() || FindActiveTrackCurrentFrameKeyframeIndex() >= 0;
    }

    public bool HasTimelineKeyframeClipboard()
    {
        return TimelineKeyframeClipboard != null || MotionKeyframeClipboard != null;
    }

    public bool IsSelectedTimelineKeyframeValid()
    {
        return SelectedTimelineKeyframeIndex >= 0 && SelectedTimelineKeyframeIndex < TimelineKeyframes.Count && TimelineKeyframes[SelectedTimelineKeyframeIndex] != null;
    }

    public int FindActiveTrackCurrentFrameKeyframeIndex()
    {
        string actionKey = CurrentActionKey();
        string targetKey = ActiveTimelineTrackKey;
        int frame = SnapFrame(TimelineCurrentFrame);

        if (string.IsNullOrEmpty(targetKey))
        {
            SkyPrisonAnimationTimelineKeyframe selected = GetSelectedTimelineKeyframe();
            if (selected != null)
                targetKey = selected.targetKey;
        }

        if (string.IsNullOrEmpty(targetKey))
            return -1;

        int firstMatch = -1;
        int rigAngleMatch = -1;
        int rigMatch = -1;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != frame) continue;

            if (firstMatch < 0) firstMatch = i;
            if (string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) rigAngleMatch = i;
            else if (string.Equals(k.targetKind, "Rig", StringComparison.OrdinalIgnoreCase)) rigMatch = i;
        }

        if (rigAngleMatch >= 0) return rigAngleMatch;
        if (rigMatch >= 0) return rigMatch;
        return firstMatch;
    }

    public bool PasteTimelineKeyframeAtCurrentFrame()
    {
        // 当前锁定/选中的轨道是 Motion 时，Ctrl+V / 右键粘贴应粘贴 Motion Key。
        // 如果剪贴板里只有 Motion Key，也允许直接粘到当前帧，避免复制后点时间尺移动播放头导致轨道焦点丢失。
        if ((IsMotionTimelineTrack(ActiveTimelineTrackKey) || TimelineKeyframeClipboard == null) && MotionKeyframeClipboard != null)
        {
            SkyPrisonAnimationMotionKeyframe pastedMotion = MotionKeyframeClipboard.Clone();
            pastedMotion.actionKey = CurrentActionKey();
            pastedMotion.frame = SnapFrame(TimelineCurrentFrame);

            int oldIndex = FindMotionKeyframeIndex(pastedMotion.actionKey, pastedMotion.frame);
            if (oldIndex >= 0 && oldIndex < MotionKeyframes.Count)
                MotionKeyframes.RemoveAt(oldIndex);

            MotionKeyframes.Add(pastedMotion);
            SortMotionKeyframes();
            SelectedTimelineKeyframeIndex = -1;
            SelectedMotionKeyframeIndex = FindMotionKeyframeIndex(pastedMotion.actionKey, pastedMotion.frame);
            ActiveTimelineTrackKey = MotionTimelineTrackKey;
            return true;
        }

        if (TimelineKeyframeClipboard == null) return false;

        SkyPrisonAnimationTimelineKeyframe pasted = TimelineKeyframeClipboard.Clone();
        pasted.actionKey = CurrentActionKey();
        pasted.frame = SnapFrame(TimelineCurrentFrame);

        // 锁轨道时，单Key粘贴的语义是“粘到当前锁定轨道当前帧”，
        // 而不是永远粘回剪贴板原来的轨道。这样右肘、右腕等单帧Key可以直接顶替当前锁定轨道上的旧Key。
        if (TimelineTrackLockEnabled && !string.IsNullOrEmpty(ActiveTimelineTrackKey))
        {
            if (IsFootstepTimelineTrack(ActiveTimelineTrackKey))
            {
                pasted.targetKey = FootstepTimelineTrackKey;
                pasted.targetName = FootstepTimelineTrackLabel;
                pasted.targetKind = FootstepTimelineTargetKind;
                pasted.layerWeightTargetKey = string.Empty;
                pasted.runtimeOffset = Vector2.zero;
                pasted.useRuntimeBoneRootOffset = false;
                pasted.runtimeBoneRootOffset = Vector2.zero;
                pasted.useRuntimeBoneHeadOffset = false;
                pasted.runtimeBoneHeadOffset = Vector2.zero;
                pasted.opacity = 1f;
                pasted.layerWeight = 0f;
                pasted.manualLayerWeightOffset = 0f;
                pasted.useMeshDeform = false;
                pasted.meshDeformColumns = 0;
                pasted.meshDeformRows = 0;
                pasted.meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();
            }
            else
            {
                SkyPrisonAnimationRigRow activeRow = FindRigRow(ActiveTimelineTrackKey);
                if (activeRow != null && !activeRow.isFolder)
                {
                pasted.targetKey = activeRow.key;
                pasted.targetName = string.IsNullOrEmpty(activeRow.name) ? activeRow.key : activeRow.name;
                if (string.IsNullOrEmpty(pasted.layerWeightTargetKey) || string.Equals(pasted.layerWeightTargetKey, TimelineKeyframeClipboard.targetKey, StringComparison.OrdinalIgnoreCase))
                    pasted.layerWeightTargetKey = activeRow.key;
                }
            }
        }

        // 单Key粘贴必须是“当前轨道当前帧直接顶替旧Key”。
        // 旧版本可能在同一轨道同一帧留下 Rig / RigAngle / LayerWeight 等不同 targetKind 的重叠Key；
        // 时间线 UI 只显示一个方块，所以这里按可见槽位 action + targetKey + frame 统一清掉旧数据，再放入新数据。
        RemoveTimelineKeyframesInVisibleSlot(pasted);
        TimelineKeyframes.Add(pasted);

        SortTimelineKeyframes();
        SelectedTimelineKeyframeIndex = FindTimelineKeyframeIndexByVisibleSlot(pasted);
        ActiveTimelineTrackKey = pasted.targetKey;
        SyncManualAnglesFromCurrentFrame(true);
        return true;
    }

    private bool IsSameTimelineKeyIdentity(SkyPrisonAnimationTimelineKeyframe a, SkyPrisonAnimationTimelineKeyframe b)
    {
        if (a == null || b == null) return false;
        return string.Equals(a.actionKey, b.actionKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.targetKey, b.targetKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.targetKind, b.targetKind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.layerWeightTargetKey, b.layerWeightTargetKey, StringComparison.OrdinalIgnoreCase)
            && SnapFrame(a.frame) == SnapFrame(b.frame);
    }

    private bool IsSameTimelineVisibleSlot(SkyPrisonAnimationTimelineKeyframe a, SkyPrisonAnimationTimelineKeyframe b)
    {
        if (a == null || b == null) return false;
        return string.Equals(a.actionKey, b.actionKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.targetKey, b.targetKey, StringComparison.OrdinalIgnoreCase)
            && SnapFrame(a.frame) == SnapFrame(b.frame);
    }

    private int RemoveTimelineKeyframesInVisibleSlot(SkyPrisonAnimationTimelineKeyframe slot)
    {
        if (slot == null) return 0;

        int removed = 0;
        for (int i = TimelineKeyframes.Count - 1; i >= 0; i--)
        {
            if (IsSameTimelineVisibleSlot(TimelineKeyframes[i], slot))
            {
                TimelineKeyframes.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    private int FindTimelineKeyframeIndexByVisibleSlot(SkyPrisonAnimationTimelineKeyframe needle)
    {
        if (needle == null) return -1;
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            if (IsSameTimelineVisibleSlot(TimelineKeyframes[i], needle))
                return i;
        }
        return -1;
    }

    public int CountCurrentFrameKeyframes()
    {
        string actionKey = CurrentActionKey();
        int frame = SnapFrame(TimelineCurrentFrame);
        int count = 0;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != frame) continue;
            count++;
        }

        if (FindMotionKeyframeIndex(actionKey, frame) >= 0) count++;
        return count;
    }

    public bool CopyCurrentFrameKeyframes()
    {
        TimelineFrameClipboard.Clear();
        MotionFrameClipboard.Clear();

        string actionKey = CurrentActionKey();
        int frame = SnapFrame(TimelineCurrentFrame);

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != frame) continue;
            TimelineFrameClipboard.Add(k.Clone());
        }

        int motionIndex = FindMotionKeyframeIndex(actionKey, frame);
        if (motionIndex >= 0 && motionIndex < MotionKeyframes.Count && MotionKeyframes[motionIndex] != null)
            MotionFrameClipboard.Add(MotionKeyframes[motionIndex].Clone());

        return TimelineFrameClipboard.Count > 0 || MotionFrameClipboard.Count > 0;
    }

    public bool PasteCurrentFrameKeyframes()
    {
        if (TimelineFrameClipboard.Count <= 0 && MotionFrameClipboard.Count <= 0)
            return false;

        string actionKey = CurrentActionKey();
        int frame = SnapFrame(TimelineCurrentFrame);
        SkyPrisonAnimationTimelineKeyframe lastPasted = null;
        SkyPrisonAnimationMotionKeyframe lastMotionPasted = null;

        List<SkyPrisonAnimationTimelineKeyframe> pastedKeys = new List<SkyPrisonAnimationTimelineKeyframe>();
        for (int i = 0; i < TimelineFrameClipboard.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe incoming = TimelineFrameClipboard[i];
            if (incoming == null) continue;

            SkyPrisonAnimationTimelineKeyframe pasted = incoming.Clone();
            pasted.actionKey = actionKey;
            pasted.frame = frame;
            pastedKeys.Add(pasted);
            lastPasted = pasted;
        }

        if (pastedKeys.Count <= 0)
            return false;

        // 整帧粘贴：先按每个被粘贴轨道的可见槽位清掉目标帧旧数据，再一次性写入复制来的数据。
        // 这样同一帧复制来的多种Key可以保留，但目标帧旧Key不会和新Key叠加。
        for (int i = TimelineKeyframes.Count - 1; i >= 0; i--)
        {
            SkyPrisonAnimationTimelineKeyframe oldKey = TimelineKeyframes[i];
            if (oldKey == null) continue;

            bool shouldRemove = false;
            for (int j = 0; j < pastedKeys.Count; j++)
            {
                if (IsSameTimelineVisibleSlot(oldKey, pastedKeys[j]))
                {
                    shouldRemove = true;
                    break;
                }
            }

            if (shouldRemove)
                TimelineKeyframes.RemoveAt(i);
        }

        for (int i = 0; i < pastedKeys.Count; i++)
            TimelineKeyframes.Add(pastedKeys[i]);

        if (MotionFrameClipboard.Count > 0)
        {
            int existingMotion = FindMotionKeyframeIndex(actionKey, frame);
            if (existingMotion >= 0 && existingMotion < MotionKeyframes.Count)
                MotionKeyframes.RemoveAt(existingMotion);

            // 当前只有一条 Motion 轨道，整帧粘贴时只需要保留一个 Motion Key。
            for (int i = 0; i < MotionFrameClipboard.Count; i++)
            {
                SkyPrisonAnimationMotionKeyframe incomingMotion = MotionFrameClipboard[i];
                if (incomingMotion == null) continue;
                SkyPrisonAnimationMotionKeyframe pastedMotion = incomingMotion.Clone();
                pastedMotion.actionKey = actionKey;
                pastedMotion.frame = frame;
                MotionKeyframes.Add(pastedMotion);
                lastMotionPasted = pastedMotion;
                break;
            }
            SortMotionKeyframes();
        }

        SortTimelineKeyframes();
        SelectedTimelineKeyframeIndex = FindTimelineKeyframeIndexByVisibleSlot(lastPasted);
        if (lastMotionPasted != null)
        {
            SelectedTimelineKeyframeIndex = -1;
            SelectedMotionKeyframeIndex = FindMotionKeyframeIndex(actionKey, frame);
            ActiveTimelineTrackKey = MotionTimelineTrackKey;
        }
        else if (lastPasted != null)
        {
            ActiveTimelineTrackKey = lastPasted.targetKey;
        }

        SyncManualAnglesFromCurrentFrame(true);
        return true;
    }

    private bool IsMeshDeformerTimelineKeyframe(SkyPrisonAnimationTimelineKeyframe key)
    {
        if (key == null)
            return false;

        if (string.Equals(key.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase))
            return true;

        return key.useMeshDeform && key.meshDeformPoints != null && key.meshDeformPoints.Count > 0;
    }

    private void CollectMeshDeformerTargetFromRemovedKey(SkyPrisonAnimationTimelineKeyframe key, List<string> targets)
    {
        if (!IsMeshDeformerTimelineKeyframe(key) || targets == null || string.IsNullOrEmpty(key.targetKey))
            return;

        for (int i = 0; i < targets.Count; i++)
        {
            if (string.Equals(targets[i], key.targetKey, StringComparison.OrdinalIgnoreCase))
                return;
        }

        targets.Add(key.targetKey);
    }

    private bool HasMeshDeformerTimelineKeyframesForAction(string actionKey, string targetKey)
    {
        if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(targetKey))
            return false;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe key = TimelineKeyframes[i];
            if (key == null)
                continue;
            if (!string.Equals(key.actionKey, actionKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(key.targetKey, targetKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsMeshDeformerTimelineKeyframe(key))
                return true;
        }

        return false;
    }

    private void RefreshMeshDeformerRowsAfterTimelineKeyRemoval(string actionKey, List<string> removedMeshDeformerTargets)
    {
        if (removedMeshDeformerTargets == null || removedMeshDeformerTargets.Count == 0)
            return;

        string currentActionKey = CurrentActionKey();

        for (int i = 0; i < removedMeshDeformerTargets.Count; i++)
        {
            string targetKey = removedMeshDeformerTargets[i];
            if (string.IsNullOrEmpty(targetKey))
                continue;

            SkyPrisonAnimationRigRow row = FindRigRow(targetKey);
            if (row == null || !row.isMeshDeformer)
                continue;

            int columns = Mathf.Clamp(row.meshDeformColumns, 2, 16);
            int rows = Mathf.Clamp(row.meshDeformRows, 2, 16);

            // 如果这个动作里该曲面已经没有任何 MeshDeformer Key，说明它应该回到默认规整矩形。
            // 否则 row.meshDeformPoints 会残留最后一次编辑的闭眼/压缩状态，造成“关键帧删完但模型还闭着”。
            if (!HasMeshDeformerTimelineKeyframesForAction(actionKey, targetKey))
            {
                ResetMeshDeformerPointGridToRect(row);
                continue;
            }

            // 仍然存在其它曲面 Key 时，把编辑缓存同步成删除后的当前时间线结果，
            // 避免刚删掉当前帧 Key 后，右侧/预览还短暂显示被删掉的旧 row 缓存。
            if (string.Equals(actionKey, currentActionKey, StringComparison.OrdinalIgnoreCase))
            {
                List<SkyPrisonMeshDeformPoint> evaluated = EvaluateTimelineMeshDeformPoints(row, columns, rows);
                if (evaluated != null && evaluated.Count > 0)
                {
                    row.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(evaluated);
                }
                else
                {
                    ResetMeshDeformerPointGridToRect(row);
                }
            }
        }
    }

    public int DeleteCurrentFrameKeyframes()
    {
        string actionKey = CurrentActionKey();
        int frame = SnapFrame(TimelineCurrentFrame);
        int deleted = 0;
        List<string> removedMeshDeformerTargets = new List<string>();

        for (int i = TimelineKeyframes.Count - 1; i >= 0; i--)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != frame) continue;

            CollectMeshDeformerTargetFromRemovedKey(k, removedMeshDeformerTargets);
            TimelineKeyframes.RemoveAt(i);
            deleted++;
        }

        int motionIndex = FindMotionKeyframeIndex(actionKey, frame);
        if (motionIndex >= 0 && motionIndex < MotionKeyframes.Count)
        {
            MotionKeyframes.RemoveAt(motionIndex);
            SelectedMotionKeyframeIndex = -1;
            deleted++;
        }

        if (deleted > 0)
        {
            SelectedTimelineKeyframeIndex = -1;
            RemoveManualPoseKeyAtFrame(frame);

            if (removedMeshDeformerTargets.Count > 0)
                RefreshMeshDeformerRowsAfterTimelineKeyRemoval(actionKey, removedMeshDeformerTargets);

            lastManualAngleSyncedFrame = int.MinValue;
            lastManualAngleSyncedActionKey = string.Empty;
            SyncManualAnglesFromCurrentFrame(true);
        }

        return deleted;
    }

    public bool HasTimelineKeyframesForAction(string actionKey)
    {
        if (string.IsNullOrEmpty(actionKey)) return false;
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k != null && k.actionKey == actionKey)
                return true;
        }
        return false;
    }

    public bool ShouldUseTimelineDrivenBasePose(string actionKey)
    {
        // 只要模板动作已经被“入轨”为关键帧，就不再叠加 PreviewPanel 里的旧硬编码公式。
        // 这样 Idle / Move / Run 都会由时间线参数驱动，避免模板双重叠加。
        return HasTimelineKeyframesForAction(actionKey);
    }

    public bool MaterializeIdleBreathingTemplateToTimeline(bool replaceExisting)
    {
        // 已停用：旧版呼吸待机会写入错误的硬编码关键帧。
        // 保留方法签名只为兼容旧调用，实际不再生成任何动作数据。
        return false;
    }

    private SkyPrisonAnimationActionRow FindActionByKey(string key)
    {
        if (Actions == null) return null;
        for (int i = 0; i < Actions.Count; i++)
        {
            SkyPrisonAnimationActionRow a = Actions[i];
            if (a != null && string.Equals(a.key, key, StringComparison.OrdinalIgnoreCase)) return a;
        }
        return null;
    }


    public bool MaterializeWalkTemplateToTimeline(bool replaceExisting)
    {
        // 已停用：旧版行走模板是写死动作，会污染时间线。
        return false;
    }

    public bool MaterializeRunTemplateToTimeline(bool replaceExisting)
    {
        // 已停用：旧版奔跑模板是写死动作，会污染时间线。
        return false;
    }

    private int AddHumanoidLocomotionFrame(string actionKey, int frame, float normalized, bool isRun)
    {
        // 注意：Walk / Run 模板绝对不能在 PreviewPanel 里继续当隐藏公式跑。
        // 这里一次性把模板采样结果写成 TimelineKeyframes。
        // 每个关键帧保存三层显式参数：
        // 1. runtimeOffset：该节点自身的位移，用于 PSB / Rig 锚点跟随。
        // 2. runtimeBoneRootOffset：骨骼线根端点位移。
        // 3. runtimeBoneHeadOffset：骨骼线头端点位移。
        // 这样腿、膝、脚踝、手肘的“角度/弯曲”会真实进入时间线，而不是预览硬编码。
        Dictionary<string, Vector2> pose = BuildHumanoidLocomotionPoseMap(normalized, isRun);

        int count = 0;
        int snappedFrame = SnapFrame(frame);
        for (int i = 0; i < RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = RigRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;

            Vector2 selfOffset = GetPoseOffset(pose, row.key);
            string childKey = GetHumanoidDefaultChildKey(row.key);
            bool hasChild = !string.IsNullOrEmpty(childKey) && pose.ContainsKey(childKey);
            Vector2 childOffset = hasChild ? GetPoseOffset(pose, childKey) : selfOffset;

            TimelineKeyframes.Add(new SkyPrisonAnimationTimelineKeyframe
            {
                actionKey = actionKey,
                targetKey = row.key,
                targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name,
                targetKind = "Rig",
                layerWeightTargetKey = row.key,
                frame = snappedFrame,
                runtimeOffset = selfOffset,
                useRuntimeBoneRootOffset = true,
                runtimeBoneRootOffset = selfOffset,
                useRuntimeBoneHeadOffset = hasChild,
                runtimeBoneHeadOffset = childOffset,
                opacity = Mathf.Clamp01(row.opacity),
                layerWeight = row.psbLayerWeight,
                manualLayerWeightOffset = row.manualLayerWeightOffset
            });
            count++;
        }
        return count;
    }

    private Dictionary<string, Vector2> BuildHumanoidLocomotionPoseMap(float normalized, bool isRun)
    {
        Dictionary<string, Vector2> pose = new Dictionary<string, Vector2>();

        // 标准人形骨架全部显式采样。即使当前 RigRows 中暂时没有某些节点，
        // 它们也可以作为父子骨骼线的 head offset 参考，保证角度不是靠隐藏公式补出来。
        string[] keys =
        {
            "Root", "Pelvis", "Spine", "Chest", "Neck", "Head", "HeadTop",
            "Shoulder_L", "Elbow_L", "Wrist_L", "HandEnd_L",
            "Shoulder_R", "Elbow_R", "Wrist_R", "HandEnd_R",
            "Hip_L", "Knee_L", "Ankle_L", "Foot_L",
            "Hip_R", "Knee_R", "Ankle_R", "Foot_R",
            "Body", "Core"
        };

        for (int i = 0; i < keys.Length; i++)
            pose[keys[i]] = EvaluateHumanoidLocomotionTemplateOffset(keys[i], normalized, isRun);

        // 自定义或命名不完全一致的 Rig，也要把采样结果落到自己的轨道上，
        // 否则时间线看起来像只有标准骨架在动。
        for (int i = 0; i < RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = RigRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;
            if (!pose.ContainsKey(row.key))
                pose[row.key] = EvaluateHumanoidLocomotionTemplateOffset(row.key, normalized, isRun);
        }

        return pose;
    }

    private Vector2 GetPoseOffset(Dictionary<string, Vector2> pose, string key)
    {
        if (pose == null || string.IsNullOrEmpty(key)) return Vector2.zero;
        Vector2 v;
        return pose.TryGetValue(key, out v) ? v : Vector2.zero;
    }

    private string GetHumanoidDefaultChildKey(string key)
    {
        switch (key)
        {
            case "Root": return "Pelvis";
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

    private int AddFootContactFrame(string actionKey, int frame, float leftContact, float rightContact)
    {
        TimelineKeyframes.Add(new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = actionKey,
            targetKey = "FootContact_L",
            targetName = "左脚接地",
            targetKind = "Contact",
            layerWeightTargetKey = "FootContact_L",
            frame = SnapFrame(frame),
            runtimeOffset = new Vector2(Mathf.Clamp01(leftContact), 0f),
            opacity = 1f,
            layerWeight = Mathf.Clamp01(leftContact),
            manualLayerWeightOffset = 0f
        });
        TimelineKeyframes.Add(new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = actionKey,
            targetKey = "FootContact_R",
            targetName = "右脚接地",
            targetKind = "Contact",
            layerWeightTargetKey = "FootContact_R",
            frame = SnapFrame(frame),
            runtimeOffset = new Vector2(Mathf.Clamp01(rightContact), 0f),
            opacity = 1f,
            layerWeight = Mathf.Clamp01(rightContact),
            manualLayerWeightOffset = 0f
        });
        return 2;
    }

    private Vector2 EvaluateHumanoidLocomotionTemplateOffset(string key, float normalized, bool isRun)
    {
        if (string.IsNullOrEmpty(key)) return Vector2.zero;

        // 模板原则：
        // 1. Root X 不前进，地图位移交给运行时移动系统。
        // 2. Walk 始终至少一脚接地；Run 允许双脚离地。
        // 3. 关节不走直线，脚、膝、肘、腕都走弧线，靠时间线关键帧真实可见。
        float t = Mathf.Repeat(normalized, 1f);
        float phase = t * Mathf.PI * 2f;
        float sin = Mathf.Sin(phase);
        float cos = Mathf.Cos(phase);
        float doubleStep = Mathf.Sin(phase * 2f);
        float doubleStepAbs = Mathf.Abs(doubleStep);

        float stride = isRun ? 68f : 62f;
        float footLift = isRun ? 50f : 28f;
        float kneeLift = isRun ? 68f : 38f;
        float hipBob = isRun ? 15.0f : 7.0f;
        float hipSway = isRun ? 8.0f : 5.0f;
        float chestCounter = isRun ? 16.0f : 10.0f;
        float armSwing = isRun ? 74f : 42f;
        float armLift = isRun ? 38f : 20f;
        float leanX = isRun ? 14.0f : 5.0f;

        // 跑步的腾空相：不是简单把身体抬高，而是在蹬地后有短暂悬浮。
        float air01 = isRun ? Mathf.Max(0f, Mathf.Sin(phase * 2f - Mathf.PI * 0.18f)) : 0f;

        Vector2 leftHip, leftKnee, leftAnkle, leftFoot;
        Vector2 rightHip, rightKnee, rightAnkle, rightFoot;
        EvaluateLegArc(t, false, isRun, stride, footLift, kneeLift, hipBob, out leftHip, out leftKnee, out leftAnkle, out leftFoot);
        EvaluateLegArc(Mathf.Repeat(t + 0.5f, 1f), true, isRun, stride, footLift, kneeLift, hipBob, out rightHip, out rightKnee, out rightAnkle, out rightFoot);

        // 身体不是上下平移，而是髋部和胸腔反相，形成“走路扭转”。
        Vector2 pelvis = new Vector2(hipSway * sin * (isRun ? 0.55f : 0.85f), -doubleStepAbs * hipBob - air01 * (isRun ? 7.0f : 0f));
        Vector2 spine = pelvis + new Vector2(leanX * 0.35f - sin * chestCounter * 0.20f, doubleStepAbs * (isRun ? 1.8f : 0.9f));
        Vector2 chest = pelvis + new Vector2(leanX - sin * chestCounter, doubleStepAbs * (isRun ? 3.2f : 1.8f));
        Vector2 neck = chest + new Vector2(-sin * chestCounter * 0.22f, -doubleStepAbs * (isRun ? 1.5f : 0.8f));
        Vector2 head = chest + new Vector2(-sin * chestCounter * 0.35f, -doubleStepAbs * (isRun ? 2.0f : 1.0f));

        // 手臂反摆：左腿前摆时右臂前摆，肘和腕形成滞后弧线。
        float armPhase = sin;
        Vector2 shoulderL = chest + new Vector2(-armPhase * 3.0f, doubleStepAbs * 0.8f);
        Vector2 shoulderR = chest + new Vector2(armPhase * 3.0f, doubleStepAbs * 0.8f);
        Vector2 elbowL = shoulderL + new Vector2(-armPhase * armSwing * 0.58f, -Mathf.Abs(armPhase) * armLift * 0.35f + Mathf.Max(0f, armPhase) * armLift * 0.18f);
        Vector2 wristL = shoulderL + new Vector2(-armPhase * armSwing, -Mathf.Abs(armPhase) * armLift * 0.52f + Mathf.Max(0f, armPhase) * armLift * 0.25f);
        Vector2 elbowR = shoulderR + new Vector2(armPhase * armSwing * 0.58f, -Mathf.Abs(armPhase) * armLift * 0.35f + Mathf.Max(0f, -armPhase) * armLift * 0.18f);
        Vector2 wristR = shoulderR + new Vector2(armPhase * armSwing, -Mathf.Abs(armPhase) * armLift * 0.52f + Mathf.Max(0f, -armPhase) * armLift * 0.25f);

        switch (key)
        {
            case "Root": return new Vector2(0f, isRun ? -air01 * 4.0f : 0f);
            case "Pelvis": return pelvis;
            case "Spine": return spine;
            case "Chest": return chest;
            case "Neck": return neck;
            case "Head":
            case "HeadTop": return head;

            case "Shoulder_L": return shoulderL;
            case "Shoulder_R": return shoulderR;
            case "Elbow_L": return elbowL;
            case "Wrist_L":
            case "HandEnd_L": return wristL;
            case "Elbow_R": return elbowR;
            case "Wrist_R":
            case "HandEnd_R": return wristR;

            case "Hip_L": return pelvis + leftHip;
            case "Knee_L": return pelvis + leftKnee;
            case "Ankle_L": return pelvis + leftAnkle;
            case "Foot_L": return pelvis + leftFoot;

            case "Hip_R": return pelvis + rightHip;
            case "Knee_R": return pelvis + rightKnee;
            case "Ankle_R": return pelvis + rightAnkle;
            case "Foot_R": return pelvis + rightFoot;

            case "Body": return chest;
            case "Core": return spine;
        }

        string lower = key.ToLowerInvariant();
        if (lower.Contains("foot")) return pelvis + leftFoot;
        if (lower.Contains("ankle")) return pelvis + leftAnkle;
        if (lower.Contains("knee")) return pelvis + leftKnee;
        if (lower.Contains("hip") || lower.Contains("pelvis")) return pelvis;
        if (lower.Contains("head")) return head;
        if (lower.Contains("chest") || lower.Contains("body")) return chest;
        if (lower.Contains("spine") || lower.Contains("core")) return spine;
        if (lower.Contains("wrist") || lower.Contains("hand")) return wristL;
        if (lower.Contains("elbow")) return elbowL;
        if (lower.Contains("arm")) return elbowL;
        return Vector2.zero;
    }

    private void EvaluateLegArc(
        float legPhase,
        bool mirroredLeg,
        bool isRun,
        float stride,
        float footLift,
        float kneeLift,
        float hipBob,
        out Vector2 hip,
        out Vector2 knee,
        out Vector2 ankle,
        out Vector2 foot)
    {
        legPhase = Mathf.Repeat(legPhase, 1f);

        // Walk: 55% 支撑、45% 摆腿。Run: 支撑更短，腾空更明确。
        float stanceEnd = isRun ? 0.32f : 0.58f;
        bool swing = legPhase >= stanceEnd;
        float stance01 = stanceEnd <= 0.0001f ? 0f : Mathf.Clamp01(legPhase / stanceEnd);
        float swing01 = Mathf.Clamp01((legPhase - stanceEnd) / Mathf.Max(0.0001f, 1f - stanceEnd));
        float supportEase = Smooth01(stance01);
        float swingEase = Smooth01(swing01);
        float arc = Mathf.Sin(swing01 * Mathf.PI);

        // 支撑脚相对身体从前方压到后方；摆腿沿弧线从后方扫回前方。
        // 默认角色朝左，所以“前方”是负 X，“后方”是正 X。
        // 旧版把 front/back 写反，会出现看起来往后挪、迈步不交叉的问题。
        float front = -stride * (isRun ? 0.62f : 0.72f);
        float back = stride * (isRun ? 0.54f : 0.58f);
        float x = swing ? Mathf.Lerp(back, front, swingEase) : Mathf.Lerp(front, back, supportEase);

        // 屏幕坐标中负 Y 代表向上。摆腿抬脚，支撑腿轻微压缩。
        float yFoot = swing ? -arc * footLift : supportEase * (isRun ? 6.0f : 3.5f);
        float yAnkle = swing ? -arc * footLift * 0.85f : supportEase * (isRun ? 4.5f : 2.2f);

        // 膝盖不能在线性中点上，否则腿是直杆；给膝盖一个额外弯曲弧线。
        float kneeBend = swing ? (0.42f + arc * 1.15f) : (0.24f + Mathf.Sin(stance01 * Mathf.PI) * 0.36f);
        float kneeX = Mathf.Lerp(0f, x, 0.55f) + (mirroredLeg ? 1f : -1f) * (isRun ? 8.0f : 4.5f) * Mathf.Sin(legPhase * Mathf.PI * 2f);
        float kneeY = Mathf.Lerp(0f, yAnkle, 0.52f) - kneeBend * kneeLift;

        // 髋关节不再反向抵消步幅，而是轻微跟随当前腿的前后相位。
        // 这样大腿根会真正带出前后交叉，而不是只有脚踝在局部挪动。
        float hipX = x * (isRun ? 0.12f : 0.10f);
        float hipY = -Mathf.Abs(Mathf.Sin(legPhase * Mathf.PI * 2f)) * hipBob * (isRun ? 0.28f : 0.20f);

        hip = new Vector2(hipX, hipY);
        knee = new Vector2(kneeX, kneeY);
        ankle = new Vector2(x * 0.88f, yAnkle);

        // 脚尖不是踝关节复制：落地前脚尖略抬，后蹬时脚尖压地。
        float toeRoll = swing ? -Mathf.Sin(swing01 * Mathf.PI) * (isRun ? 10.0f : 6.5f) : Mathf.Sin(stance01 * Mathf.PI) * (isRun ? 7.0f : 4.0f);
        foot = new Vector2(x, yFoot + toeRoll);
    }

    private float Smooth01(float v)
    {
        v = Mathf.Clamp01(v);
        return v * v * (3f - 2f * v);
    }


    private string BuildManualAngleRigSignature()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (SourcePsdAssetPath == null ? 0 : SourcePsdAssetPath.GetHashCode());
            hash = hash * 31 + (CurrentRigTemplateKey == null ? 0 : CurrentRigTemplateKey.GetHashCode());
            hash = hash * 31 + (ManualRigTemplateMode ? 1 : 0);
            hash = hash * 31 + (RigRows == null ? 0 : RigRows.Count);

            if (RigRows != null)
            {
                for (int i = 0; i < RigRows.Count; i++)
                {
                    SkyPrisonAnimationRigRow row = RigRows[i];
                    if (row == null)
                    {
                        hash = hash * 31;
                        continue;
                    }

                    hash = hash * 31 + (row.key == null ? 0 : row.key.GetHashCode());
                    hash = hash * 31 + (row.name == null ? 0 : row.name.GetHashCode());
                    hash = hash * 31 + (row.parentKey == null ? 0 : row.parentKey.GetHashCode());
                    hash = hash * 31 + (row.semantic == null ? 0 : row.semantic.GetHashCode());
                    hash = hash * 31 + (row.isFolder ? 1 : 0);
                }
            }

            return hash.ToString();
        }
    }

    public void InvalidateManualAngleRigSignature()
    {
        manualAngleRigSignature = string.Empty;
        manualAngleRigSignatureInitialized = false;
    }

    public void ClearMotionPoseEditorState(bool clearRuntimePreview)
    {
        ManualBoneAngles.Clear();
        ManualPoseKeys.Clear();
        SelectedManualPoseKeyIndex = -1;
        ManualAngleParameterScroll = Vector2.zero;
        ManualPoseListScroll = Vector2.zero;
        StructureAngleEditMode = false;
        lastManualAngleSyncedFrame = int.MinValue;
        lastManualAngleSyncedActionKey = string.Empty;
        suppressManualAngleFrameSync = false;

        if (clearRuntimePreview && RigRows != null)
        {
            for (int i = 0; i < RigRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = RigRows[i];
                if (row == null) continue;
                row.useRuntimeBoneRootOffset = false;
                row.runtimeBoneRootOffset = Vector2.zero;
                row.useRuntimeBoneHeadOffset = false;
                row.runtimeBoneHeadOffset = Vector2.zero;
            }
        }
    }

    public void EnsureMotionPoseEditorStateMatchesCurrentRig()
    {
        string signature = BuildManualAngleRigSignature();
        if (!manualAngleRigSignatureInitialized)
        {
            manualAngleRigSignature = signature;
            manualAngleRigSignatureInitialized = true;
            return;
        }

        if (!string.Equals(manualAngleRigSignature, signature, StringComparison.Ordinal))
        {
            ClearMotionPoseEditorState(true);
            manualAngleRigSignature = signature;
            manualAngleRigSignatureInitialized = true;
        }
    }

    public List<SkyPrisonAnimationRigRow> GetManualAngleTargetRows()
    {
        EnsureMotionPoseEditorStateMatchesCurrentRig();

        List<SkyPrisonAnimationRigRow> rows = new List<SkyPrisonAnimationRigRow>();
        if (RigRows == null) return rows;

        for (int i = 0; i < RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = RigRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
                continue;

            rows.Add(row);
            if (!ManualBoneAngles.ContainsKey(row.key))
                ManualBoneAngles[row.key] = 0f;
        }

        // 清理已经不存在的节点参数，避免换骨架后旧 key 残留。
        List<string> stale = null;
        foreach (string key in ManualBoneAngles.Keys)
        {
            if (FindRigRow(key) == null)
            {
                if (stale == null) stale = new List<string>();
                stale.Add(key);
            }
        }
        if (stale != null)
        {
            for (int i = 0; i < stale.Count; i++)
                ManualBoneAngles.Remove(stale[i]);
        }

        return rows;
    }

    public float GetManualBoneAngle(string rigKey)
    {
        if (string.IsNullOrEmpty(rigKey)) return 0f;
        float value;
        if (!ManualBoneAngles.TryGetValue(rigKey, out value))
        {
            value = 0f;
            ManualBoneAngles[rigKey] = value;
        }
        return Mathf.Clamp(value, -180f, 180f);
    }

    public void SetManualBoneAngle(string rigKey, float angle)
    {
        if (string.IsNullOrEmpty(rigKey)) return;
        ManualBoneAngles[rigKey] = Mathf.Clamp(angle, -180f, 180f);
        LiveManualBoneAngleKeys.Add(rigKey);
    }

    public void ApplyManualAnglePreviewToRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
            return;

        ApplyManualAnglePreviewToRow(row, GetManualBoneAngle(row.key));
    }

    public void ApplyManualAnglePreviewToRow(SkyPrisonAnimationRigRow row, float angleDeg)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
            return;

        // 动作参数里的角度是“局部旋转参数”，不是端点偏移。
        // 这里绝对不能再把角度换算成 runtimeBoneHeadOffset 写回 row，
        // 否则 PreviewPanel 读取时间线 RigAngle 后又叠加一次端点偏移，骨骼长度会被拉长。
        row.useRuntimeBoneRootOffset = false;
        row.runtimeBoneRootOffset = Vector2.zero;
        row.useRuntimeBoneHeadOffset = false;
        row.runtimeBoneHeadOffset = Vector2.zero;
    }

    public void ApplyManualAnglePreviewToAll()
    {
        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        for (int i = 0; i < rows.Count; i++)
            ApplyManualAnglePreviewToRow(rows[i]);
    }

    public void ApplyManualAngleLiveChange(string rigKey)
    {
        if (string.IsNullOrEmpty(rigKey)) return;

        SkyPrisonAnimationRigRow row = FindRigRow(rigKey);
        if (row == null || row.isFolder) return;

        float angle = GetManualBoneAngle(rigKey);
        ApplyManualAnglePreviewToRow(row, angle);

        int frame = SnapFrame(TimelineCurrentFrame);

        // Head 端旋转是离散关键帧编辑。这里也要把 CurrentTime 吸附到该帧，
        // 不允许后续预览用 sub-frame 插值把实时拖拽结果拉回上一关键帧。
        CurrentTime = FrameToSeconds(frame);

        int poseIndex = FindManualPoseKeyIndex(frame);
        SkyPrisonManualPoseKey pose = CaptureCurrentManualPoseKey(frame, string.Format("姿势点 {0}", frame));
        if (poseIndex >= 0)
        {
            ManualPoseKeys[poseIndex] = pose;
            SelectedManualPoseKeyIndex = poseIndex;
        }
        else
        {
            ManualPoseKeys.Add(pose);
            SortManualPoseKeys();
            SelectedManualPoseKeyIndex = FindManualPoseKeyIndex(frame);
        }

        string actionKey = CurrentActionKey();
        if (!string.IsNullOrEmpty(actionKey))
        {
            // 先给其它姿势点补“保护 Key”，再写当前帧。
            // 这样某个骨骼第一次在当前帧被编辑时，不会因为缺少前后关键帧而把整段动画都拉弯。
            EnsureManualAngleProtectionKeyframes(actionKey, row, frame);
            AddManualAngleKeyframe(actionKey, row, frame, angle);
            SortTimelineKeyframes();

            // 可视窗口拖动 Head 端时，左侧动作参数、当前时间线轨道、白线帧必须指向同一个 RigAngle。
            // 同一帧如果还残留旧 Rig 端点关键帧，不能让它再次叠加到 RigAngle 上，否则会出现
            // “看起来转过去、下一帧又抽回去 / 怎么拖都转不过来”的冲突。
            SelectTimelineTrack(row.key, false);
            SelectedTimelineKeyframeIndex = FindTimelineKeyframeIndexByKind(actionKey, row.key, frame, "RigAngle");
            if (SelectedTimelineKeyframeIndex < 0)
                SelectedTimelineKeyframeIndex = FindFirstTimelineKeyframeIndex(actionKey, row.key, frame);

            ShowAllTimelineTracks = true;
            CurrentAction().type = "全身姿势关键帧";
            CurrentAction().status = "可编辑";
        }

        ManualBoneAngles[rigKey] = Mathf.Clamp(angle, -180f, 180f);
        LiveManualBoneAngleKeys.Add(rigKey);
        lastManualAngleSyncedFrame = frame;
        lastManualAngleSyncedActionKey = actionKey ?? string.Empty;
    }

    public void ClearManualAnglePreviewFromAll()
    {
        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null) continue;
            row.useRuntimeBoneHeadOffset = false;
            row.runtimeBoneHeadOffset = Vector2.zero;
        }
    }

    public void ResetManualBoneAngles()
    {
        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        string actionKey = CurrentActionKey();
        int frame = SnapFrame(TimelineCurrentFrame);

        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null || string.IsNullOrEmpty(row.key))
                continue;

            ManualBoneAngles[row.key] = 0f;
            ApplyManualAnglePreviewToRow(row, 0f);

            if (!string.IsNullOrEmpty(actionKey))
                AddManualAngleKeyframe(actionKey, row, frame, 0f);
        }

        int poseIndex = FindManualPoseKeyIndex(frame);
        SkyPrisonManualPoseKey pose = CaptureCurrentManualPoseKey(frame, string.Format("姿势点 {0}", frame));
        if (poseIndex >= 0)
        {
            ManualPoseKeys[poseIndex] = pose;
            SelectedManualPoseKeyIndex = poseIndex;
        }
        else
        {
            ManualPoseKeys.Add(pose);
            SortManualPoseKeys();
            SelectedManualPoseKeyIndex = FindManualPoseKeyIndex(frame);
        }

        SortTimelineKeyframes();
        lastManualAngleSyncedFrame = frame;
        lastManualAngleSyncedActionKey = actionKey;
    }

    public int[] GetManualAngleSampleFrames()
    {
        int total = TimelineTotalFrames;
        int segments = Mathf.Clamp(ManualAngleSampleSegments, 1, 60);
        int[] frames = new int[segments + 1];
        for (int i = 0; i <= segments; i++)
            frames[i] = SnapFrame(Mathf.RoundToInt(total * (i / (float)segments)));
        return frames;
    }

    public string FormatManualAngleSampleFrames()
    {
        int[] frames = GetManualAngleSampleFrames();
        if (frames == null || frames.Length == 0) return "-";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < frames.Length; i++)
        {
            if (i > 0) sb.Append(" / ");
            sb.Append(frames[i]);
        }
        return sb.ToString();
    }

    public SkyPrisonManualPoseKey CaptureCurrentManualPoseKey(int frame, string label)
    {
        SkyPrisonManualPoseKey pose = new SkyPrisonManualPoseKey();
        pose.frame = SnapFrame(frame);
        pose.label = string.IsNullOrWhiteSpace(label) ? string.Format("姿势点 {0}", pose.frame) : label;

        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;
            pose.angles.Add(new SkyPrisonManualPoseAngle
            {
                rigKey = row.key,
                angle = GetManualBoneAngle(row.key)
            });
        }
        return pose;
    }

    public void SaveCurrentManualPoseKey()
    {
        int frame = SnapFrame(TimelineCurrentFrame);
        SkyPrisonManualPoseKey pose = CaptureCurrentManualPoseKey(frame, string.Format("姿势点 {0}", frame));

        int existing = FindManualPoseKeyIndex(frame);
        if (existing >= 0)
        {
            ManualPoseKeys[existing] = pose;
            SelectedManualPoseKeyIndex = existing;
        }
        else
        {
            ManualPoseKeys.Add(pose);
            SortManualPoseKeys();
            SelectedManualPoseKeyIndex = FindManualPoseKeyIndex(frame);
        }
    }

    public void UpdateSelectedManualPoseKey()
    {
        if (SelectedManualPoseKeyIndex < 0 || SelectedManualPoseKeyIndex >= ManualPoseKeys.Count)
        {
            SaveCurrentManualPoseKey();
            return;
        }

        SkyPrisonManualPoseKey selected = ManualPoseKeys[SelectedManualPoseKeyIndex];
        int frame = selected != null ? selected.frame : SnapFrame(TimelineCurrentFrame);
        string label = selected != null ? selected.label : string.Format("姿势点 {0}", frame);
        ManualPoseKeys[SelectedManualPoseKeyIndex] = CaptureCurrentManualPoseKey(frame, label);
        SortManualPoseKeys();
        SelectedManualPoseKeyIndex = FindManualPoseKeyIndex(frame);
    }

    public void DeleteSelectedManualPoseKey()
    {
        if (SelectedManualPoseKeyIndex < 0 || SelectedManualPoseKeyIndex >= ManualPoseKeys.Count) return;

        SkyPrisonManualPoseKey removed = ManualPoseKeys[SelectedManualPoseKeyIndex];
        int frame = removed != null ? SnapFrame(removed.frame) : SnapFrame(TimelineCurrentFrame);
        string actionKey = CurrentActionKey();

        ManualPoseKeys.RemoveAt(SelectedManualPoseKeyIndex);
        SelectedManualPoseKeyIndex = Mathf.Clamp(SelectedManualPoseKeyIndex, -1, ManualPoseKeys.Count - 1);

        RemoveExactRigAngleKeyframesAtFrame(actionKey, frame);

        if (frame == SnapFrame(TimelineCurrentFrame))
        {
            lastManualAngleSyncedFrame = int.MinValue;
            lastManualAngleSyncedActionKey = string.Empty;
            SyncManualAnglesFromCurrentFrame(true);
        }
    }

    private void RemoveManualPoseKeyAtFrame(int frame)
    {
        int snapped = SnapFrame(frame);
        for (int i = ManualPoseKeys.Count - 1; i >= 0; i--)
        {
            SkyPrisonManualPoseKey pose = ManualPoseKeys[i];
            if (pose != null && SnapFrame(pose.frame) == snapped)
                ManualPoseKeys.RemoveAt(i);
        }

        SelectedManualPoseKeyIndex = Mathf.Clamp(SelectedManualPoseKeyIndex, -1, ManualPoseKeys.Count - 1);
    }

    private int RemoveExactRigAngleKeyframesAtFrame(string actionKey, int frame)
    {
        if (string.IsNullOrEmpty(actionKey)) return 0;

        int snapped = SnapFrame(frame);
        int removed = 0;
        for (int i = TimelineKeyframes.Count - 1; i >= 0; i--)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != snapped) continue;
            if (!string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;

            TimelineKeyframes.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
        {
            SortTimelineKeyframes();
            SelectedTimelineKeyframeIndex = -1;
        }

        return removed;
    }

    private bool RebuildManualPoseKeyFromExactRigAngleKeys(string actionKey, int frame)
    {
        int snapped = SnapFrame(frame);
        RemoveManualPoseKeyAtFrame(snapped);

        if (string.IsNullOrEmpty(actionKey))
            return false;

        bool hasAnyAngleKey = false;
        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        SkyPrisonManualPoseKey pose = new SkyPrisonManualPoseKey();
        pose.frame = snapped;
        pose.label = string.Format("姿势点 {0}", snapped);

        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;

            float angle;
            bool hasKey = TryGetExactRigAngleKey(actionKey, row.key, snapped, out angle);
            if (hasKey)
                hasAnyAngleKey = true;
            else
                angle = 0f;

            pose.angles.Add(new SkyPrisonManualPoseAngle
            {
                rigKey = row.key,
                angle = Mathf.Clamp(angle, -180f, 180f)
            });
        }

        if (!hasAnyAngleKey)
            return false;

        ManualPoseKeys.Add(pose);
        SortManualPoseKeys();
        SelectedManualPoseKeyIndex = FindManualPoseKeyIndex(snapped);
        return true;
    }

    public void ClearManualPoseKeys()
    {
        ManualPoseKeys.Clear();
        SelectedManualPoseKeyIndex = -1;
    }

    public void LoadManualPoseKeyToParameters(int index)
    {
        if (index < 0 || index >= ManualPoseKeys.Count) return;
        SkyPrisonManualPoseKey pose = ManualPoseKeys[index];
        if (pose == null) return;

        suppressManualAngleFrameSync = true;
        try
        {
            SelectedManualPoseKeyIndex = index;
            SetCurrentFrame(pose.frame);

            List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
            for (int i = 0; i < rows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.key)) continue;

                float angle = pose.GetAngle(row.key);
                SetManualBoneAngle(row.key, angle);
                ApplyManualAnglePreviewToRow(row, angle);
            }
        }
        finally
        {
            suppressManualAngleFrameSync = false;
            lastManualAngleSyncedFrame = SnapFrame(TimelineCurrentFrame);
            lastManualAngleSyncedActionKey = CurrentActionKey();
        }
    }

    public void SyncManualAnglesFromCurrentFrame(bool force)
    {
        if (suppressManualAngleFrameSync)
            return;

        int frame = SnapFrame(TimelineCurrentFrame);
        string actionKey = CurrentActionKey();

        if (!force && lastManualAngleSyncedFrame == frame && string.Equals(lastManualAngleSyncedActionKey, actionKey, StringComparison.Ordinal))
            return;

        // 时间线同步是“读取当前帧结果”，不是实时编辑输入。
        // 清掉 live override，避免上一帧手动输入继续压过时间线插值。
        LiveManualBoneAngleKeys.Clear();

        lastManualAngleSyncedFrame = frame;
        lastManualAngleSyncedActionKey = actionKey ?? string.Empty;

        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        if (rows == null)
            return;

        int poseIndex = FindManualPoseKeyIndex(frame);
        if (poseIndex >= 0)
        {
            SelectedManualPoseKeyIndex = poseIndex;
            SkyPrisonManualPoseKey pose = ManualPoseKeys[poseIndex];

            for (int i = 0; i < rows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.key)) continue;

                float angle = pose != null ? pose.GetAngle(row.key) : 0f;
                ManualBoneAngles[row.key] = Mathf.Clamp(angle, -180f, 180f);
                ApplyManualAnglePreviewToRow(row, angle);
            }
            return;
        }

        SelectedManualPoseKeyIndex = -1;

        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null || string.IsNullOrEmpty(row.key)) continue;

            float angle;
            bool hasExactKey = TryGetExactRigAngleKey(actionKey, row.key, frame, out angle);

            // 左侧动作参数必须显示“当前白线真正预览到的角度”。
            // 没有精确 RigAngle 时，读取前后 RigAngle 的插值；只有完全没有角度轨道时才回到 Rest Pose。
            if (!hasExactKey && !TryEvaluateTimelineManualBoneAngle(row.key, out angle))
                angle = 0f;

            ManualBoneAngles[row.key] = Mathf.Clamp(angle, -180f, 180f);
            ApplyManualAnglePreviewToRow(row, angle);
        }
    }

    private bool TryGetExactRigAngleKey(string actionKey, string rigKey, int frame, out float angle)
    {
        angle = 0f;

        if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(rigKey))
            return false;

        int snappedFrame = SnapFrame(frame);
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;

            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(k.targetKey, rigKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (SnapFrame(k.frame) != snappedFrame)
                continue;
            if (!string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase))
                continue;

            angle = Mathf.Clamp(k.runtimeOffset.x, -180f, 180f);
            return true;
        }

        return false;
    }

    public int FindManualPoseKeyIndex(int frame)
    {
        int snapped = SnapFrame(frame);
        for (int i = 0; i < ManualPoseKeys.Count; i++)
        {
            SkyPrisonManualPoseKey pose = ManualPoseKeys[i];
            if (pose != null && pose.frame == snapped)
                return i;
        }
        return -1;
    }

    public void SortManualPoseKeys()
    {
        ManualPoseKeys.Sort((a, b) =>
        {
            int af = a != null ? a.frame : 0;
            int bf = b != null ? b.frame : 0;
            return af.CompareTo(bf);
        });
    }

    public string GetMotionArcTargetLabel()
    {
        SkyPrisonAnimationRigRow row = GetActiveTimelineTrackRow();
        if (row == null)
            return "未锁定轨道";
        string name = string.IsNullOrEmpty(row.name) ? row.key : row.name;
        return name + " [" + row.key + "]";
    }

    public string GetMotionArcRangeLabel()
    {
        SkyPrisonAnimationRigRow row = GetActiveTimelineTrackRow();
        if (row == null)
            return "请先锁定一条 Rig 轨道";

        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindMotionArcRigAngleBounds(CurrentActionKey(), row.key, TimelineCurrentFrame, out prev, out next))
            return "当前帧前后需要各有一个动作参数 Key";

        return string.Format("区间：{0}帧 → {1}帧", prev.frame, next.frame);
    }

    public bool CanGenerateMotionArcKeys()
    {
        SkyPrisonAnimationRigRow row = GetActiveTimelineTrackRow();
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return false;
        SkyPrisonAnimationTimelineKeyframe prev, next;
        return TryFindMotionArcRigAngleBounds(CurrentActionKey(), row.key, TimelineCurrentFrame, out prev, out next);
    }

    public int GenerateMotionArcKeysForActiveTrack()
    {
        SkyPrisonAnimationRigRow row = GetActiveTimelineTrackRow();
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return 0;

        string actionKey = CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey)) return 0;

        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindMotionArcRigAngleBounds(actionKey, row.key, TimelineCurrentFrame, out prev, out next)) return 0;
        if (prev == null || next == null || next.frame <= prev.frame) return 0;

        int tweenCount = Mathf.Clamp(MotionArcTweenCount, 1, 60);
        object snapshot = CaptureStructureUndoSnapshot();

        if (MotionArcOverwriteInnerKeys)
        {
            for (int i = TimelineKeyframes.Count - 1; i >= 0; i--)
            {
                SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
                if (k == null) continue;
                if (!string.Equals(k.actionKey, actionKey, StringComparison.Ordinal)) continue;
                if (!string.Equals(k.targetKey, row.key, StringComparison.Ordinal)) continue;
                if (!string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.frame > prev.frame && k.frame < next.frame)
                    TimelineKeyframes.RemoveAt(i);
            }
        }

        int created = 0;
        HashSet<int> usedFrames = new HashSet<int>();
        usedFrames.Add(prev.frame);
        usedFrames.Add(next.frame);

        for (int i = 1; i <= tweenCount; i++)
        {
            float rawT = i / (float)(tweenCount + 1);
            int frame = SnapFrame(Mathf.RoundToInt(Mathf.Lerp(prev.frame, next.frame, rawT)));
            if (frame <= prev.frame || frame >= next.frame) continue;
            if (!usedFrames.Add(frame)) continue;

            float t = Mathf.InverseLerp(prev.frame, next.frame, frame);
            float eased = EvaluateMotionArcEase(t);
            float angle = Mathf.LerpAngle(prev.runtimeOffset.x, next.runtimeOffset.x, eased);
            created += AddManualAngleKeyframe(actionKey, row, frame, angle);
        }

        if (created <= 0) return 0;

        SortTimelineKeyframes();
        ShowAllTimelineTracks = true;
        SelectTimelineTrack(row.key, true);
        SelectedTimelineKeyframeIndex = FindFirstTimelineKeyframeIndex(actionKey, row.key, TimelineCurrentFrame);
        CurrentAction().type = "弧线补间关键帧";
        CurrentAction().status = "可编辑";
        lastManualAngleSyncedFrame = int.MinValue;
        lastManualAngleSyncedActionKey = string.Empty;
        SyncManualAnglesFromCurrentFrame(true);
        PushCapturedStructureUndo(snapshot);
        return created;
    }

    private float EvaluateMotionArcEase(float t)
    {
        t = Mathf.Clamp01(t);
        float raw;
        switch (MotionArcEasePreset)
        {
            case SkyPrisonMotionArcEasePreset.Linear:
                return t;
            case SkyPrisonMotionArcEasePreset.Smooth:
                raw = t * t * (3f - 2f * t);
                break;
            case SkyPrisonMotionArcEasePreset.Elastic:
                raw = EvaluateMotionArcElasticEase(t);
                break;
            case SkyPrisonMotionArcEasePreset.Soft:
            default:
                raw = 0.5f - Mathf.Cos(t * Mathf.PI) * 0.5f;
                break;
        }
        return Mathf.Clamp01(Mathf.Lerp(t, raw, Mathf.Clamp01(MotionArcEaseAmount)));
    }

    private float EvaluateMotionArcElasticEase(float t)
    {
        // 简化版“有弹性”：两端仍然准确落点，中段略带力量感，第一版不做真正过冲，避免生成难以理解的角度。
        float sine = 0.5f - Mathf.Cos(t * Mathf.PI) * 0.5f;
        float kick = Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI * 2f) * 0.10f;
        return Mathf.Clamp01(sine + kick);
    }

    private bool TryFindMotionArcRigAngleBounds(string actionKey, string targetKey, int currentFrame, out SkyPrisonAnimationTimelineKeyframe prev, out SkyPrisonAnimationTimelineKeyframe next)
    {
        prev = null;
        next = null;
        if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(targetKey)) return false;
        int frame = SnapFrame(currentFrame);

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.Ordinal)) continue;
            if (!string.Equals(k.targetKey, targetKey, StringComparison.Ordinal)) continue;
            if (!string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;

            if (k.frame < frame)
            {
                if (prev == null || k.frame > prev.frame) prev = k;
            }
            else if (k.frame > frame)
            {
                if (next == null || k.frame < next.frame) next = k;
            }
        }

        return prev != null && next != null && next.frame > prev.frame;
    }

    public bool GenerateManualPoseKeysToCurrentAction()
    {
        if (ManualPoseKeys.Count == 0) return false;
        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        if (rows == null || rows.Count == 0) return false;

        string actionKey = CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey)) return false;

        SortManualPoseKeys();
        object snapshot = CaptureStructureUndoSnapshot();

        HashSet<string> targetKeys = new HashSet<string>();
        for (int i = 0; i < rows.Count; i++)
            if (rows[i] != null && !string.IsNullOrEmpty(rows[i].key))
                targetKeys.Add(rows[i].key);

        HashSet<int> targetFrames = new HashSet<int>();
        for (int i = 0; i < ManualPoseKeys.Count; i++)
            if (ManualPoseKeys[i] != null)
                targetFrames.Add(SnapFrame(ManualPoseKeys[i].frame));

        if (ManualAngleReplaceExisting)
        {
            for (int i = TimelineKeyframes.Count - 1; i >= 0; i--)
            {
                SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
                if (k == null) continue;
                if (k.actionKey == actionKey && targetKeys.Contains(k.targetKey) && targetFrames.Contains(k.frame))
                    TimelineKeyframes.RemoveAt(i);
            }
        }

        int created = 0;
        for (int p = 0; p < ManualPoseKeys.Count; p++)
        {
            SkyPrisonManualPoseKey pose = ManualPoseKeys[p];
            if (pose == null) continue;
            int frame = SnapFrame(pose.frame);
            for (int i = 0; i < rows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = rows[i];
                if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;
                created += AddManualAngleKeyframe(actionKey, row, frame, pose.GetAngle(row.key));
            }
        }

        if (created > 0)
        {
            SortTimelineKeyframes();
            ShowAllTimelineTracks = true;
            if (rows.Count > 0 && rows[0] != null)
                SelectTimelineTrack(rows[0].key, true);
            SelectedTimelineKeyframeIndex = FindFirstTimelineKeyframeIndex(actionKey, ActiveTimelineTrackKey, ManualPoseKeys[0].frame);
            PushCapturedStructureUndo(snapshot);
            CurrentAction().type = "全身姿势关键帧";
            CurrentAction().status = "可编辑";
            ApplyManualAnglePreviewToAll();
            return true;
        }
        return false;
    }

    public bool GenerateManualAnglesToCurrentAction(bool allSampleFrames)
    {
        List<SkyPrisonAnimationRigRow> rows = GetManualAngleTargetRows();
        if (rows == null || rows.Count == 0) return false;

        string actionKey = CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey)) return false;

        int[] frames = allSampleFrames ? GetManualAngleSampleFrames() : new int[] { TimelineCurrentFrame };
        if (frames == null || frames.Length == 0) return false;

        object snapshot = CaptureStructureUndoSnapshot();

        if (ManualAngleReplaceExisting)
        {
            HashSet<string> targetKeys = new HashSet<string>();
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && !string.IsNullOrEmpty(rows[i].key))
                    targetKeys.Add(rows[i].key);

            HashSet<int> targetFrames = new HashSet<int>();
            for (int i = 0; i < frames.Length; i++)
                targetFrames.Add(SnapFrame(frames[i]));

            for (int i = TimelineKeyframes.Count - 1; i >= 0; i--)
            {
                SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
                if (k == null) continue;
                if (k.actionKey == actionKey && targetKeys.Contains(k.targetKey) && targetFrames.Contains(k.frame))
                    TimelineKeyframes.RemoveAt(i);
            }
        }

        int created = 0;
        for (int f = 0; f < frames.Length; f++)
        {
            int frame = SnapFrame(frames[f]);
            for (int i = 0; i < rows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = rows[i];
                if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;
                created += AddManualAngleKeyframe(actionKey, row, frame, GetManualBoneAngle(row.key));
            }
        }

        if (created > 0)
        {
            ApplyManualAnglePreviewToAll();
            SortTimelineKeyframes();
            ShowAllTimelineTracks = true;
            if (rows.Count > 0 && rows[0] != null)
                SelectTimelineTrack(rows[0].key, true);
            SelectedTimelineKeyframeIndex = FindFirstTimelineKeyframeIndex(actionKey, ActiveTimelineTrackKey, SnapFrame(frames[0]));
            PushCapturedStructureUndo(snapshot);
            CurrentAction().type = "手动角度关键帧";
            CurrentAction().status = "可编辑";
            return true;
        }

        return false;
    }

    private void EnsureManualAngleProtectionKeyframes(string actionKey, SkyPrisonAnimationRigRow row, int currentFrame)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return;
        if (string.IsNullOrEmpty(actionKey)) return;

        int snappedCurrent = SnapFrame(currentFrame);
        List<int> guardFrames = CollectManualAngleProtectionFrames(actionKey, snappedCurrent);
        if (guardFrames == null || guardFrames.Count == 0) return;

        // 先采样、后写入。否则前一个保护 Key 会影响后一个保护 Key 的采样结果。
        Dictionary<int, float> sampledAngles = new Dictionary<int, float>();
        for (int i = 0; i < guardFrames.Count; i++)
        {
            int frame = SnapFrame(guardFrames[i]);
            if (frame == snappedCurrent) continue;

            float existing;
            if (TryGetExactRigAngleKey(actionKey, row.key, frame, out existing))
                continue;

            float angle;
            if (!TryEvaluateTimelineManualBoneAngleAtFrame(actionKey, row.key, frame, out angle))
                angle = 0f;

            sampledAngles[frame] = Mathf.Clamp(angle, -180f, 180f);
        }

        foreach (KeyValuePair<int, float> kv in sampledAngles)
            AddManualAngleKeyframe(actionKey, row, kv.Key, kv.Value);
    }

    private List<int> CollectManualAngleProtectionFrames(string actionKey, int currentFrame)
    {
        HashSet<int> frames = new HashSet<int>();
        int snappedCurrent = SnapFrame(currentFrame);
        int total = TimelineTotalFrames;

        // 边界必须保护：否则第一次在中间帧弯膝盖时，会从 0 帧一路影响到动作末尾。
        frames.Add(0);
        frames.Add(total);

        // 手动姿势点是“用户认为存在的时间点”。某根骨骼没有编辑过，也要在这些点补 0/插值保护。
        for (int i = 0; i < ManualPoseKeys.Count; i++)
        {
            SkyPrisonManualPoseKey pose = ManualPoseKeys[i];
            if (pose == null) continue;
            frames.Add(SnapFrame(pose.frame));
        }

        // 当前动作里其它轨道已有的关键帧，也视为姿势时间点。
        // 例如 Walk 的 0/9/18/27/36... 其它骨骼已有 Key，膝盖第一次编辑时也要跟着补保护 Key。
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            frames.Add(SnapFrame(k.frame));
        }

        // 如果当前动作几乎没有姿势点，至少在当前帧两侧放保护，避免一个孤立 RigAngle 控制整段。
        bool hasPrev = false;
        bool hasNext = false;
        foreach (int f in frames)
        {
            if (f < snappedCurrent) hasPrev = true;
            if (f > snappedCurrent) hasNext = true;
        }

        if (!hasPrev && snappedCurrent > 0)
            frames.Add(SnapFrame(snappedCurrent - 1));
        if (!hasNext && snappedCurrent < total)
            frames.Add(SnapFrame(snappedCurrent + 1));

        frames.Remove(snappedCurrent);

        List<int> result = new List<int>(frames);
        result.Sort();
        return result;
    }

    private bool TryEvaluateTimelineManualBoneAngleAtFrame(string actionKey, string targetKey, int frame, out float angleDeg)
    {
        angleDeg = 0f;
        if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(targetKey)) return false;

        int snappedFrame = SnapFrame(frame);
        SkyPrisonAnimationTimelineKeyframe prev = null;
        SkyPrisonAnimationTimelineKeyframe next = null;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;

            int keyFrame = SnapFrame(k.frame);
            if (keyFrame <= snappedFrame && (prev == null || keyFrame > SnapFrame(prev.frame))) prev = k;
            if (keyFrame >= snappedFrame && (next == null || keyFrame < SnapFrame(next.frame))) next = k;
        }

        if (prev == null && next == null)
            return false;

        if (prev == null)
        {
            angleDeg = Mathf.Clamp(next.runtimeOffset.x, -180f, 180f);
            return true;
        }

        if (next == null || SnapFrame(next.frame) == SnapFrame(prev.frame))
        {
            angleDeg = Mathf.Clamp(prev.runtimeOffset.x, -180f, 180f);
            return true;
        }

        float t = SmoothTimelineInterpolation(SnapFrame(prev.frame), SnapFrame(next.frame), snappedFrame);
        angleDeg = Mathf.LerpAngle(prev.runtimeOffset.x, next.runtimeOffset.x, t);
        angleDeg = Mathf.Clamp(angleDeg, -180f, 180f);
        return true;
    }

    private int AddManualAngleKeyframe(string actionKey, SkyPrisonAnimationRigRow row, int frame, float angleDeg)
    {
        if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) return 0;

        int snappedFrame = SnapFrame(frame);
        NormalizeSameFrameRigKeyframesForRigAngle(actionKey, row.key, snappedFrame);

        SkyPrisonAnimationTimelineKeyframe key = new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = actionKey,
            targetKey = row.key,
            targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name,
            targetKind = "RigAngle",
            layerWeightTargetKey = row.key,
            frame = SnapFrame(frame),
            // runtimeOffset.x 保存原始角度。PreviewPanel 会优先用这个角度按当前骨骼 Rest Vector 做保长旋转，
            // 而不是盲目叠加一个已经换算好的端点偏移。
            runtimeOffset = new Vector2(Mathf.Clamp(angleDeg, -180f, 180f), 0f),
            useRuntimeBoneRootOffset = false,
            runtimeBoneRootOffset = Vector2.zero,
            // RigAngle 关键帧只保存角度。端点偏移由 PreviewPanel 用当前 Rest Vector 保长计算。
            useRuntimeBoneHeadOffset = false,
            runtimeBoneHeadOffset = Vector2.zero,
            opacity = Mathf.Clamp01(row.opacity),
            layerWeight = row.psbLayerWeight,
            manualLayerWeightOffset = row.manualLayerWeightOffset
        };

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe old = TimelineKeyframes[i];
            if (old != null && old.actionKey == key.actionKey && old.targetKey == key.targetKey && old.frame == key.frame && string.Equals(old.targetKind, key.targetKind, StringComparison.OrdinalIgnoreCase))
            {
                TimelineKeyframes[i] = key;
                return 1;
            }
        }

        TimelineKeyframes.Add(key);
        return 1;
    }

    private void NormalizeSameFrameRigKeyframesForRigAngle(string actionKey, string targetKey, int frame)
    {
        if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(targetKey))
            return;

        int snappedFrame = SnapFrame(frame);
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != snappedFrame) continue;
            if (string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase)) continue;

            // RigAngle 负责本骨骼 Head 端旋转。旧版 Rig / RuntimeOffset 如果继续作为 Head 偏移叠加，
            // 会和角度系统互相拉扯，表现为拖拽抽动、角度被重置、或者视觉上旋转但数据没有真正落下。
            // 这里只清掉“Head 专用偏移”，保留 Root 平移与图层权重信息。
            k.runtimeOffset = Vector2.zero;

            if (k.useRuntimeBoneRootOffset)
            {
                k.useRuntimeBoneHeadOffset = true;
                k.runtimeBoneHeadOffset = k.runtimeBoneRootOffset;
            }
            else
            {
                k.useRuntimeBoneHeadOffset = false;
                k.runtimeBoneHeadOffset = Vector2.zero;
            }
        }
    }

    public Vector2 ComputeManualAngleHeadOffset(SkyPrisonAnimationRigRow row, float angleDeg)
    {
        if (row == null) return Vector2.zero;

        // 这里只保留一个“兼容旧预览/旧关键帧”的换算。
        // 真正的保长旋转由 PreviewPanel 按当前骨骼实际 Rest Vector 计算：
        //     newHead = newRoot + Rotate(restVector, angle)
        // 因为 Models 层拿不到 PreviewPanel 的标准骨架锚点，不能用 row.customBoneHead 的默认值假装真实长度。
        Vector2 rest = ResolveManualAngleFallbackRestVector(row);
        if (rest.sqrMagnitude < 0.0001f)
            rest = new Vector2(0f, -48f);

        Vector2 rotated = RotateVectorPreserveLength(rest, Mathf.Clamp(angleDeg, -180f, 180f));
        return rotated - rest;
    }

    private Vector2 ResolveManualAngleFallbackRestVector(SkyPrisonAnimationRigRow row)
    {
        if (row == null) return Vector2.zero;

        if (row.useManualBoneRootOffset && row.useManualBoneHeadOffset)
            return row.manualBoneHeadOffset - row.manualBoneRootOffset;

        if (row.useCustomBoneLine)
            return row.customBoneHead - row.customBoneRoot;

        Vector2 rest = row.customBoneHead - row.customBoneRoot;
        if (rest.sqrMagnitude > 0.0001f && rest != new Vector2(0f, -48f))
            return rest;

        // 标准人形骨架在 Models 层没有 Preview 锚点，只给一个兼容 fallback。
        // PreviewPanel 会用真实 baseVector 重新计算，所以这里不会作为最终旋转依据。
        return new Vector2(0f, -48f);
    }

    public static Vector2 RotateVectorPreserveLength(Vector2 vector, float angleDeg)
    {
        if (vector.sqrMagnitude < 0.0001f) return vector;
        float rad = angleDeg * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad);
        float s = Mathf.Sin(rad);
        return new Vector2(vector.x * c - vector.y * s, vector.x * s + vector.y * c);
    }

    public bool TryGetManualPreviewAngle(string targetKey, out float angleDeg)
    {
        angleDeg = 0f;
        if (string.IsNullOrEmpty(targetKey)) return false;

        float value;
        if (!ManualBoneAngles.TryGetValue(targetKey, out value))
            return false;

        angleDeg = Mathf.Clamp(value, -180f, 180f);
        return true;
    }

    public bool TryEvaluateTimelineManualBoneAngle(string targetKey, out float angleDeg)
    {
        angleDeg = 0f;
        if (string.IsNullOrEmpty(targetKey)) return false;

        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindTimelineKeyframePairByKind(targetKey, TimelineCurrentFrameFloat, "RigAngle", out prev, out next))
            return false;

        if (prev == null && next == null)
            return false;

        if (prev == null)
        {
            angleDeg = Mathf.Clamp(next.runtimeOffset.x, -180f, 180f);
            return true;
        }

        if (next == null || next.frame == prev.frame)
        {
            angleDeg = Mathf.Clamp(prev.runtimeOffset.x, -180f, 180f);
            return true;
        }

        float t = SmoothTimelineInterpolation(prev.frame, next.frame, TimelineCurrentFrameFloat);
        angleDeg = Mathf.LerpAngle(prev.runtimeOffset.x, next.runtimeOffset.x, t);
        angleDeg = Mathf.Clamp(angleDeg, -180f, 180f);
        return true;
    }

    public bool TryGetEffectiveManualBoneAngle(string targetKey, out float angleDeg)
    {
        angleDeg = 0f;
        if (string.IsNullOrEmpty(targetKey)) return false;

        // 编辑中以“正在输入的角度”为唯一实时源。
        // 左侧参数拖动和预览区 Head 拖动都会先写 SetManualBoneAngle，
        // 所以它们在预览层看到的是同一套值，不再一边读时间线插值、一边读手动参数。
        if (!PreviewPlaying && LiveManualBoneAngleKeys.Contains(targetKey) && TryGetManualPreviewAngle(targetKey, out angleDeg))
            return true;

        if (TryEvaluateTimelineManualBoneAngle(targetKey, out angleDeg))
            return true;

        return false;
    }

    public bool IsRigAngleDrivingAtCurrentFrame(string targetKey)
    {
        // PreviewPanel 用这个判断当前 Head 端是否应完全交给 RigAngle。
        // 这里只看时间线 RigAngle，不看 ManualBoneAngles 默认 0 值；
        // 避免没有角度关键帧的骨骼被误判，导致普通 Root/Head 偏移失效。
        float angle;
        return TryEvaluateTimelineManualBoneAngle(targetKey, out angle);
    }


    private int FindTimelineKeyframeIndexByKind(string actionKey, string targetKey, int frame, string targetKind)
    {
        int snappedFrame = SnapFrame(frame);
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null) continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (SnapFrame(k.frame) != snappedFrame) continue;
            if (!string.Equals(k.targetKind, targetKind, StringComparison.OrdinalIgnoreCase)) continue;
            return i;
        }
        return -1;
    }

    private int FindFirstTimelineKeyframeIndex(string actionKey, string targetKey, int frame)
    {
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k != null && k.actionKey == actionKey && k.targetKey == targetKey && k.frame == frame) return i;
        }
        return -1;
    }

    private int AddIdleBreathingFrame(int frame, float phase)
    {
        int count = 0;
        for (int i = 0; i < RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = RigRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;
            TimelineKeyframes.Add(new SkyPrisonAnimationTimelineKeyframe
            {
                actionKey = "Idle",
                targetKey = row.key,
                targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name,
                targetKind = "Rig",
                layerWeightTargetKey = row.key,
                frame = SnapFrame(frame),
                runtimeOffset = EvaluateIdleBreathingTemplateOffset(row.key, phase),
                opacity = Mathf.Clamp01(row.opacity),
                layerWeight = row.psbLayerWeight,
                manualLayerWeightOffset = row.manualLayerWeightOffset
            });
            count++;
        }
        return count;
    }

    private Vector2 EvaluateIdleBreathingTemplateOffset(string key, float phase)
    {
        if (string.IsNullOrEmpty(key)) return Vector2.zero;

        // 呼吸待机的基准：下半身/根节点锚定，不让整个人跳起来。
        // 只让胸腔、颈部、头部、肩臂产生轻微纵向起伏。
        // Unity GUI 坐标里 y 负方向是向上，因此 inhale 为负值。
        float inhale = Mathf.Sin(phase);
        float chestLift = -inhale * 1.6f;
        float spineLift = -inhale * 0.7f;
        float neckLift = -inhale * 1.35f;
        float headLift = -inhale * 1.15f;
        float shoulderLift = -inhale * 1.05f;
        float armFollow = -inhale * 0.45f;
        float handFollow = -inhale * 0.25f;

        switch (key)
        {
            // 锚点层：绝对不参与呼吸上下位移，避免“整个人跳起来”。
            case "Root":
            case "Pelvis":
            case "Hip_L":
            case "Hip_R":
            case "Knee_L":
            case "Knee_R":
            case "Ankle_L":
            case "Ankle_R":
            case "Foot_L":
            case "Foot_R":
                return Vector2.zero;

            // 上半身：越靠胸腔越明显，但幅度保持小。
            case "Spine": return new Vector2(0f, spineLift);
            case "Chest": return new Vector2(0f, chestLift);
            case "Neck": return new Vector2(0f, neckLift);
            case "Head": return new Vector2(0f, headLift);
            case "HeadTop": return new Vector2(0f, headLift * 1.05f);

            // 肩和手臂只是被胸腔轻轻带动，不做摆臂。
            case "Shoulder_L":
            case "Shoulder_R":
                return new Vector2(0f, shoulderLift);
            case "Elbow_L":
            case "Elbow_R":
                return new Vector2(0f, armFollow);
            case "Wrist_L":
            case "Wrist_R":
                return new Vector2(0f, handFollow);
            case "HandEnd_L":
            case "HandEnd_R":
                return new Vector2(0f, handFollow * 0.8f);

            case "Body": return new Vector2(0f, chestLift);
            case "Core": return new Vector2(0f, spineLift);
        }

        string lower = key.ToLowerInvariant();
        if (lower.Contains("foot") || lower.Contains("ankle") || lower.Contains("knee") || lower.Contains("hip") || lower.Contains("pelvis") || lower.Contains("root"))
            return Vector2.zero;
        if (lower.Contains("head")) return new Vector2(0f, headLift);
        if (lower.Contains("neck")) return new Vector2(0f, neckLift);
        if (lower.Contains("chest") || lower.Contains("body")) return new Vector2(0f, chestLift);
        if (lower.Contains("spine") || lower.Contains("core")) return new Vector2(0f, spineLift);
        if (lower.Contains("shoulder")) return new Vector2(0f, shoulderLift);
        if (lower.Contains("arm") || lower.Contains("hand") || lower.Contains("wrist") || lower.Contains("elbow")) return new Vector2(0f, armFollow);
        return Vector2.zero;
    }
    public bool HasTimelineKeyframes(string targetKey)
    {
        if (string.IsNullOrEmpty(targetKey)) return false;
        string actionKey = CurrentActionKey();
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k != null && k.actionKey == actionKey && k.targetKey == targetKey) return true;
        }
        return false;
    }

    private void EnsureFootstepTimelineTrackVisible(List<string> keys, HashSet<string> used)
    {
        if (keys == null || used == null) return;
        if (used.Add(FootstepTimelineTrackKey))
            keys.Add(FootstepTimelineTrackKey);
    }

    public List<string> GetTimelineTrackKeysForCurrentAction()
    {
        List<string> keys = new List<string>();
        HashSet<string> used = new HashSet<string>();
        string actionKey = CurrentActionKey();
        if (used.Add(MotionTimelineTrackKey)) keys.Add(MotionTimelineTrackKey);

        // 模板入轨后的第一优先级：先显示“这个动作实际拥有关键帧的轨道”。
        // 之前 ShowAll 先铺满所有 Rig，关键帧轨道会被埋在很长的列表里，看起来像没有生成。
        // 现在 Move / Run / Idle 入轨后，带关键帧的轨道会顶到上面，用户能直接看到菱形关键帧。
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null
                || k.actionKey != actionKey
                || string.IsNullOrEmpty(k.targetKey)
                || used.Contains(k.targetKey))
                continue;

            keys.Add(k.targetKey);
            used.Add(k.targetKey);
        }

        if (ShowAllTimelineTracks)
        {
            // 显示全部轨道：关键帧轨道优先，其后补齐 Rig 骨骼节点。
            List<SkyPrisonAnimationRigRow> rows = RigRows;
            for (int i = 0; i < rows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = rows[i];
                if (row != null && !row.isFolder && !string.IsNullOrEmpty(row.key) && used.Add(row.key))
                    keys.Add(row.key);
            }
            EnsureFootstepTimelineTrackVisible(keys, used);
            return keys;
        }

        // 默认模式：时间线跟随左侧 Rig 结构选择；但如果当前动作已经有模板关键帧，
        // 上面已经把这些轨道加入 keys，这里只负责补充当前选中/锁定轨道。
        List<SkyPrisonAnimationRigRow> selectedRows = GetTimelineTargetRows();
        for (int i = 0; i < selectedRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = selectedRows[i];
            if (row != null && !string.IsNullOrEmpty(row.key) && used.Add(row.key))
                keys.Add(row.key);
        }

        if (!string.IsNullOrEmpty(ActiveTimelineTrackKey))
        {
            SkyPrisonAnimationRigRow activeRig = FindRigRow(ActiveTimelineTrackKey);
            if (activeRig != null && !activeRig.isFolder && used.Add(activeRig.key))
                keys.Add(activeRig.key);
        }

        SkyPrisonAnimationTimelineKeyframe selectedKey = GetSelectedTimelineKeyframe();
        if (selectedKey != null
            && selectedKey.actionKey == CurrentActionKey()
            && !string.IsNullOrEmpty(selectedKey.targetKey)
            && used.Add(selectedKey.targetKey))
        {
            keys.Add(selectedKey.targetKey);
        }

        EnsureFootstepTimelineTrackVisible(keys, used);
        return keys;
    }

    public string GetTimelineTrackLabel(string targetKey)
    {
        if (IsMotionTimelineTrack(targetKey)) return MotionTimelineTrackLabel;
        if (IsFootstepTimelineTrack(targetKey)) return FootstepTimelineTrackLabel;

        SkyPrisonAnimationRigRow row = FindAnyStructureRow(targetKey);
        if (row != null) return string.IsNullOrEmpty(row.name) ? row.key : row.name;
        string actionKey = CurrentActionKey();
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k != null && k.actionKey == actionKey && k.targetKey == targetKey)
                return string.IsNullOrEmpty(k.targetName) ? targetKey : k.targetName;
        }
        return targetKey;
    }

    public SkyPrisonAnimationTimelineKeyframe GetSelectedTimelineKeyframe()
    {
        if (SelectedTimelineKeyframeIndex < 0 || SelectedTimelineKeyframeIndex >= TimelineKeyframes.Count)
            return null;
        return TimelineKeyframes[SelectedTimelineKeyframeIndex];
    }

    public bool IsSelectedTimelineKeyframeForRowAtCurrentFrame(SkyPrisonAnimationRigRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.key))
            return false;
        SkyPrisonAnimationTimelineKeyframe k = GetSelectedTimelineKeyframe();
        return k != null
            && k.actionKey == CurrentActionKey()
            && k.targetKey == row.key
            && k.frame == TimelineCurrentFrame;
    }

    public bool IsSelectedTimelineKeyframeForLayerWeightRowAtCurrentFrame(SkyPrisonAnimationRigRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.key))
            return false;

        SkyPrisonAnimationTimelineKeyframe k = GetSelectedTimelineKeyframe();
        if (k == null || k.actionKey != CurrentActionKey() || k.frame != TimelineCurrentFrame)
            return false;

        if (k.targetKey == row.key)
            return true;

        // PSB 图层本身不进入时间轴；如果它绑定到了当前 Rig 轨道，允许把权重写进该 Rig 关键帧。
        if (StructureTab == SkyPrisonAnimationStructureTab.PsbLayer
            && !string.IsNullOrEmpty(row.boundRigKey)
            && k.targetKey == row.boundRigKey)
            return true;

        return false;
    }

    public bool TryGetSelectedTimelineLayerWeightsForRow(SkyPrisonAnimationRigRow row, out float layerWeight, out float manualOffset)
    {
        layerWeight = 0f;
        manualOffset = 0f;
        SkyPrisonAnimationTimelineKeyframe k = GetSelectedTimelineKeyframe();
        if (row == null || k == null || k.actionKey != CurrentActionKey() || k.frame != TimelineCurrentFrame)
            return false;

        bool sameRig = k.targetKey == row.key;
        bool boundPsb = StructureTab == SkyPrisonAnimationStructureTab.PsbLayer
            && !string.IsNullOrEmpty(row.boundRigKey)
            && k.targetKey == row.boundRigKey;

        if (!sameRig && !boundPsb)
            return false;

        if (boundPsb && !string.IsNullOrEmpty(k.layerWeightTargetKey) && k.layerWeightTargetKey != row.key)
            return false;

        layerWeight = k.layerWeight;
        manualOffset = k.manualLayerWeightOffset;
        return true;
    }

    public bool CanEditInspectorSelectedRowUnderTrackLock(SkyPrisonAnimationRigRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.key)) return true;
        if (CanEditAnimatedTarget(row.key)) return true;

        // PSB 行不生成轨道，但允许在选中绑定 Rig 关键帧时编辑它的权重覆盖值。
        if (StructureTab == SkyPrisonAnimationStructureTab.PsbLayer
            && !string.IsNullOrEmpty(row.boundRigKey)
            && row.boundRigKey == ActiveTimelineTrackKey
            && IsSelectedTimelineKeyframeForLayerWeightRowAtCurrentFrame(row))
            return true;

        return false;
    }

    public bool UpdateSelectedTimelineKeyframeFromRow(SkyPrisonAnimationRigRow row)
    {
        SkyPrisonAnimationTimelineKeyframe k = GetSelectedTimelineKeyframe();
        if (k == null || row == null)
            return false;

        if (k.actionKey != CurrentActionKey() || k.frame != TimelineCurrentFrame)
            return false;

        bool isBoundPsbLayerWeightEdit = StructureTab == SkyPrisonAnimationStructureTab.PsbLayer
            && !string.IsNullOrEmpty(row.boundRigKey)
            && k.targetKey == row.boundRigKey;

        if (!isBoundPsbLayerWeightEdit && k.targetKey != row.key)
            return false;

        if (isBoundPsbLayerWeightEdit)
        {
            // PSB 图层本身不作为轨道存在；这里只把该 PSB 图层的压层参数写入当前 Rig 关键帧。
            k.layerWeightTargetKey = row.key;
            k.layerWeight = row.psbLayerWeight;
            k.manualLayerWeightOffset = row.manualLayerWeightOffset;
            return true;
        }

        if (string.Equals(k.targetKind, "RigAngle", StringComparison.OrdinalIgnoreCase))
        {
            // 角度关键帧被选中时，Inspector / 预览区同步不能把它降级回旧 Rig 偏移关键帧。
            // 否则同一帧会同时出现 Rig 与 RigAngle 两套控制，Head 端拖拽就会抽动。
            k.targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name;
            k.layerWeightTargetKey = row.key;
            k.runtimeOffset = new Vector2(Mathf.Clamp(GetManualBoneAngle(row.key), -180f, 180f), 0f);
            k.useRuntimeBoneRootOffset = false;
            k.runtimeBoneRootOffset = Vector2.zero;
            k.useRuntimeBoneHeadOffset = false;
            k.runtimeBoneHeadOffset = Vector2.zero;
            k.opacity = Mathf.Clamp01(row.opacity);
            k.layerWeight = row.psbLayerWeight;
            k.manualLayerWeightOffset = row.manualLayerWeightOffset;
            return true;
        }

        if (row.isMeshDeformer || string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase))
        {
            // 曲面关键帧被 Inspector 操作后，不能被通用 Rig 同步逻辑降级成 Rig 关键帧。
            // “复原当前帧曲面”之前看起来失灵，就是因为按钮写入了 MeshDeformer Key，
            // 随后的 Inspector EndChangeCheck 又把当前选中 Key 改回 targetKind=Rig，
            // 预览层找不到当前帧 MeshDeformer Key，只好继续显示前后插值结果。
            k.targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name;
            k.targetKind = "MeshDeformer";
            k.layerWeightTargetKey = row.key;
            k.runtimeOffset = Vector2.zero;
            k.useRuntimeBoneRootOffset = false;
            k.runtimeBoneRootOffset = Vector2.zero;
            k.useRuntimeBoneHeadOffset = false;
            k.runtimeBoneHeadOffset = Vector2.zero;
            k.opacity = 1f;
            k.layerWeight = 0f;
            k.manualLayerWeightOffset = 0f;
            k.useMeshDeform = true;
            k.meshDeformColumns = Mathf.Clamp(row.meshDeformColumns, 2, 16);
            k.meshDeformRows = Mathf.Clamp(row.meshDeformRows, 2, 16);
            k.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(row.meshDeformPoints);
            return true;
        }

        k.targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name;
        k.targetKind = "Rig";
        k.layerWeightTargetKey = row.key;
        k.runtimeOffset = row.useManualRigLayerOffset ? row.manualRigLayerOffset : Vector2.zero;
        k.useRuntimeBoneRootOffset = row.useRuntimeBoneRootOffset;
        k.runtimeBoneRootOffset = row.runtimeBoneRootOffset;
        k.useRuntimeBoneHeadOffset = row.useRuntimeBoneHeadOffset;
        k.runtimeBoneHeadOffset = row.runtimeBoneHeadOffset;
        k.opacity = Mathf.Clamp01(row.opacity);
        k.layerWeight = row.psbLayerWeight;
        k.manualLayerWeightOffset = row.manualLayerWeightOffset;
        k.useMeshDeform = row.isMeshDeformer;
        k.meshDeformColumns = row.isMeshDeformer ? row.meshDeformColumns : 0;
        k.meshDeformRows = row.isMeshDeformer ? row.meshDeformRows : 0;
        k.meshDeformPoints = row.isMeshDeformer ? SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(row.meshDeformPoints) : new List<SkyPrisonMeshDeformPoint>();
        return true;
    }


    public List<SkyPrisonMeshDeformPoint> EvaluateTimelineMeshDeformPoints(SkyPrisonAnimationRigRow deformer, int columns, int rows)
    {
        List<SkyPrisonMeshDeformPoint> fallback = deformer != null
            ? SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(deformer.meshDeformPoints)
            : new List<SkyPrisonMeshDeformPoint>();

        if (deformer == null || string.IsNullOrEmpty(deformer.key))
            return fallback;

        string actionKey = CurrentActionKey();
        int displayFrame = SnapFrame(TimelineCurrentFrame);

        // 曲面编辑器的“当前帧”语义是离散帧。
        // 复原当前帧以后，如果 CurrentTime 还有极小的小数误差，不能再拿前后帧插值结果覆盖视觉点位，
        // 否则右侧按钮已经写入规整矩形，但预览绿色点看起来仍然像没复原。
        int exactIndex = FindTimelineKeyframeIndexByKind(actionKey, deformer.key, displayFrame, "MeshDeformer");
        if (exactIndex >= 0 && exactIndex < TimelineKeyframes.Count)
        {
            SkyPrisonAnimationTimelineKeyframe exact = TimelineKeyframes[exactIndex];
            if (exact != null && exact.useMeshDeform && exact.meshDeformPoints != null && exact.meshDeformPoints.Count > 0)
                return SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(exact.meshDeformPoints);
        }

        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindTimelineKeyframePairByKind(deformer.key, TimelineCurrentFrameFloat, "MeshDeformer", out prev, out next))
        {
            if (!TryFindTimelineKeyframePair(deformer.key, TimelineCurrentFrameFloat, out prev, out next))
                return fallback;
        }

        bool prevHas = prev != null && prev.useMeshDeform && prev.meshDeformPoints != null && prev.meshDeformPoints.Count > 0;
        bool nextHas = next != null && next.useMeshDeform && next.meshDeformPoints != null && next.meshDeformPoints.Count > 0;

        if (!prevHas && !nextHas)
            return fallback;
        if (!prevHas)
            return SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(next.meshDeformPoints);
        if (!nextHas || prev.frame == next.frame)
            return SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(prev.meshDeformPoints);

        float t = SmoothTimelineInterpolation(prev.frame, next.frame, TimelineCurrentFrameFloat);
        return LerpMeshDeformPoints(prev.meshDeformPoints, next.meshDeformPoints, columns, rows, t);
    }

    private List<SkyPrisonMeshDeformPoint> LerpMeshDeformPoints(List<SkyPrisonMeshDeformPoint> a, List<SkyPrisonMeshDeformPoint> b, int columns, int rows, float t)
    {
        List<SkyPrisonMeshDeformPoint> result = new List<SkyPrisonMeshDeformPoint>();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                SkyPrisonMeshDeformPoint pa = FindMeshDeformPointInList(a, x, y);
                SkyPrisonMeshDeformPoint pb = FindMeshDeformPointInList(b, x, y);
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

    private SkyPrisonMeshDeformPoint FindMeshDeformPointInList(List<SkyPrisonMeshDeformPoint> list, int x, int y)
    {
        if (list == null)
            return null;
        for (int i = 0; i < list.Count; i++)
        {
            SkyPrisonMeshDeformPoint p = list[i];
            if (p != null && p.x == x && p.y == y)
                return p;
        }
        return null;
    }


    private bool HasMeshDeformerKeyframeAt(string actionKey, string targetKey, int frame)
    {
        if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(targetKey))
            return false;

        int snap = SnapFrame(frame);
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null)
                continue;
            if (k.actionKey != actionKey || k.targetKey != targetKey || k.frame != snap)
                continue;
            if (string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private SkyPrisonAnimationTimelineKeyframe CreateMeshDeformerProtectionKeyframe(SkyPrisonAnimationRigRow row, int frame, List<SkyPrisonMeshDeformPoint> protectedPoints)
    {
        if (row == null || !row.isMeshDeformer)
            return null;

        int snap = SnapFrame(frame);
        string actionKey = CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey) || HasMeshDeformerKeyframeAt(actionKey, row.key, snap))
            return null;

        SkyPrisonAnimationTimelineKeyframe key = new SkyPrisonAnimationTimelineKeyframe
        {
            actionKey = actionKey,
            targetKey = row.key,
            targetName = string.IsNullOrEmpty(row.name) ? row.key : row.name,
            targetKind = "MeshDeformer",
            frame = snap,
            runtimeOffset = Vector2.zero,
            useRuntimeBoneRootOffset = false,
            runtimeBoneRootOffset = Vector2.zero,
            useRuntimeBoneHeadOffset = false,
            runtimeBoneHeadOffset = Vector2.zero,
            opacity = 1f,
            layerWeight = 0f,
            manualLayerWeightOffset = 0f,
            useMeshDeform = true,
            meshDeformColumns = row.meshDeformColumns,
            meshDeformRows = row.meshDeformRows,
            meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(protectedPoints)
        };

        TimelineKeyframes.Add(key);
        return key;
    }

    public void EnsureMeshDeformerProtectionKeyframesAroundCurrent(SkyPrisonAnimationRigRow row)
    {
        if (row == null || !row.isMeshDeformer)
            return;

        string actionKey = CurrentActionKey();
        if (string.IsNullOrEmpty(actionKey))
            return;

        int current = SnapFrame(TimelineCurrentFrame);

        // 只有当前帧原本没有曲面关键帧时，才自动插入保护帧。
        // 这样新增一个局部变形时，不会让前后整段都被插值污染；
        // 但如果用户本来就在编辑已有曲面关键帧，则不破坏他手动设计好的过渡段。
        if (HasMeshDeformerKeyframeAt(actionKey, row.key, current))
            return;

        List<int> guardFrames = CollectMeshDeformerVerticalProtectionFrames(actionKey, row.key, current);
        if (guardFrames == null || guardFrames.Count == 0)
            return;

        List<SkyPrisonMeshDeformPoint> protectedPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(row.meshDeformPoints);

        int oldSelected = SelectedTimelineKeyframeIndex;
        string oldActiveTrack = ActiveTimelineTrackKey;

        for (int i = 0; i < guardFrames.Count; i++)
        {
            int frame = SnapFrame(guardFrames[i]);
            if (frame == current)
                continue;
            CreateMeshDeformerProtectionKeyframe(row, frame, protectedPoints);
        }

        SortTimelineKeyframes();
        ActiveTimelineTrackKey = oldActiveTrack;
        SelectedTimelineKeyframeIndex = Mathf.Clamp(oldSelected, -1, TimelineKeyframes.Count - 1);
    }

    private List<int> CollectMeshDeformerVerticalProtectionFrames(string actionKey, string targetKey, int currentFrame)
    {
        HashSet<int> candidates = new HashSet<int>();
        int current = SnapFrame(currentFrame);
        int total = TimelineTotalFrames;

        // 曲面保护帧的目标不是“当前帧左右各补一个”，而是只在当前曲面轨道缺少边界时补。
        // 如果这个曲面轨道在当前帧之前已经有任意 MeshDeformer Key，说明左侧已经有边界，
        // 不需要再在最近的纵向时间点额外制造保护帧；右侧同理。
        bool hasMeshKeyBefore = false;
        bool hasMeshKeyAfter = false;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null)
                continue;
            if (!string.Equals(k.actionKey, actionKey, StringComparison.OrdinalIgnoreCase))
                continue;

            int f = SnapFrame(k.frame);

            if (string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.targetKey, targetKey, StringComparison.OrdinalIgnoreCase))
            {
                if (f < current)
                    hasMeshKeyBefore = true;
                else if (f > current)
                    hasMeshKeyAfter = true;
            }

            // 纵向候选仍然来自整条动作的已有编辑列，但是否使用它，要看当前曲面轨道两侧是否缺 Key。
            candidates.Add(f);
        }

        candidates.Add(0);
        candidates.Add(total);

        for (int i = 0; i < ManualPoseKeys.Count; i++)
        {
            SkyPrisonManualPoseKey pose = ManualPoseKeys[i];
            if (pose == null)
                continue;
            candidates.Add(SnapFrame(pose.frame));
        }

        int before = -1;
        int after = -1;
        foreach (int f in candidates)
        {
            if (f < current && (before < 0 || f > before))
                before = f;
            if (f > current && (after < 0 || f < after))
                after = f;
        }

        // 只有当前曲面轨道左侧完全没有 Key 时，才补左保护帧。
        // 例如左侧已经有上上个曲面 Key，就说明它已经能限制插值范围，不再补新的左保护帧。
        if (hasMeshKeyBefore)
            before = -1;
        if (hasMeshKeyAfter)
            after = -1;

        // 如果当前动作几乎没有任何纵向编辑时间点，才退回到邻近帧保护。
        // 但同样要遵守“该侧已经有曲面 Key 就不补”的原则。
        if (before < 0 && !hasMeshKeyBefore && current > 0 && candidates.Count <= 2)
            before = SnapFrame(current - 1);
        if (after < 0 && !hasMeshKeyAfter && current < total && candidates.Count <= 2)
            after = SnapFrame(current + 1);

        List<int> result = new List<int>();
        if (before >= 0 && before != current && !HasMeshDeformerKeyframeAt(actionKey, targetKey, before))
            result.Add(before);
        if (after >= 0 && after != current && after != before && !HasMeshDeformerKeyframeAt(actionKey, targetKey, after))
            result.Add(after);

        result.Sort();
        return result;
    }

    public SkyPrisonAnimationTimelineKeyframe EnsureCurrentFrameMeshDeformerKeyframeForRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null || !row.isMeshDeformer)
            return null;

        SkyPrisonAnimationTimelineKeyframe key = InsertOrUpdateTimelineKeyframe(row, TimelineCurrentFrame);
        if (key != null)
        {
            key.targetKind = "MeshDeformer";
            key.useMeshDeform = true;
            key.meshDeformColumns = row.meshDeformColumns;
            key.meshDeformRows = row.meshDeformRows;
            key.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(row.meshDeformPoints);
            ActiveTimelineTrackKey = row.key;
            SelectedTimelineKeyframeIndex = TimelineKeyframes.IndexOf(key);
            SortTimelineKeyframes();
            SelectedTimelineKeyframeIndex = FindTimelineKeyframeIndexByKind(CurrentActionKey(), row.key, TimelineCurrentFrame, "MeshDeformer");
        }
        return key;
    }

    public void ResetCurrentFrameMeshDeformerToRect(SkyPrisonAnimationRigRow row)
    {
        if (row == null || !row.isMeshDeformer)
            return;

        PushStructureUndo();

        int frame = SnapFrame(TimelineCurrentFrame);
        string actionKey = CurrentActionKey();

        // 复原按钮是离散帧操作。先把播放头吸附到这个帧，避免预览层用 CurrentTime 的小数部分继续显示插值结果。
        SetCurrentFrame(frame);

        // 先把曲面节点自身恢复为规整矩形。
        ResetMeshDeformerPointGridToRect(row);

        // 再强制覆盖“当前帧”的曲面关键帧。
        // 这里不要依赖当前插值结果，否则在前后已有曲面帧时，复原按钮看起来像没生效。
        SkyPrisonAnimationTimelineKeyframe key = InsertOrUpdateTimelineKeyframe(row, frame);
        if (key != null)
        {
            key.actionKey = actionKey;
            key.targetKey = row.key;
            key.targetKind = "MeshDeformer";
            key.frame = frame;
            key.useMeshDeform = true;
            key.meshDeformColumns = row.meshDeformColumns;
            key.meshDeformRows = row.meshDeformRows;
            key.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(row.meshDeformPoints);

            ActiveTimelineTrackKey = row.key;
            SortTimelineKeyframes();
            SelectedTimelineKeyframeIndex = FindTimelineKeyframeIndexByKind(actionKey, row.key, frame, "MeshDeformer");
        }
    }

    public void ResetAllMeshDeformerKeyframesToRect(SkyPrisonAnimationRigRow row)
    {
        if (row == null || !row.isMeshDeformer)
            return;

        PushStructureUndo();
        ResetMeshDeformerPointGridToRect(row);

        string actionKey = CurrentActionKey();
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe key = TimelineKeyframes[i];
            if (key == null)
                continue;
            if (!string.Equals(key.actionKey, actionKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(key.targetKey, row.key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(key.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase))
                continue;

            key.useMeshDeform = true;
            key.meshDeformColumns = row.meshDeformColumns;
            key.meshDeformRows = row.meshDeformRows;
            key.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(row.meshDeformPoints);
        }
    }

    private void ResetMeshDeformerPointGridToRect(SkyPrisonAnimationRigRow row)
    {
        if (row == null || !row.isMeshDeformer)
            return;

        int columns = Mathf.Clamp(row.meshDeformColumns, 2, 16);
        int rows = Mathf.Clamp(row.meshDeformRows, 2, 16);
        row.meshDeformColumns = columns;
        row.meshDeformRows = rows;

        if (row.meshDeformPoints == null)
            row.meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();
        else
            row.meshDeformPoints.Clear();

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                row.meshDeformPoints.Add(new SkyPrisonMeshDeformPoint
                {
                    x = x,
                    y = y,
                    offset = Vector2.zero,
                    handleLeftOffset = Vector2.zero,
                    handleRightOffset = Vector2.zero,
                    handleUpOffset = Vector2.zero,
                    handleDownOffset = Vector2.zero,
                });
            }
        }
    }



    public int RepairMeshDeformerCachesForCurrentAction()
    {
        string actionKey = CurrentActionKey();
        int repaired = 0;
        repaired += RepairMeshDeformerCachesForCurrentActionInRows(RigRows, actionKey);
        repaired += RepairMeshDeformerCachesForCurrentActionInRows(PsbRows, actionKey);
        repaired += RepairMeshDeformerCachesForCurrentActionInRows(SocketRows, actionKey);
        return repaired;
    }

    private int RepairMeshDeformerCachesForCurrentActionInRows(List<SkyPrisonAnimationRigRow> rows, string actionKey)
    {
        if (rows == null || rows.Count == 0)
            return 0;

        int repaired = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null || !row.isMeshDeformer)
                continue;

            int columns = Mathf.Clamp(row.meshDeformColumns, 2, 16);
            int rowCount = Mathf.Clamp(row.meshDeformRows, 2, 16);
            row.meshDeformColumns = columns;
            row.meshDeformRows = rowCount;

            List<SkyPrisonMeshDeformPoint> repairedPoints = null;
            if (HasMeshDeformerTimelineKeyframesForAction(actionKey, row.key))
            {
                repairedPoints = EvaluateTimelineMeshDeformPoints(row, columns, rowCount);
                if (repairedPoints == null || repairedPoints.Count == 0)
                    repairedPoints = BuildDefaultMeshDeformerPointGrid(columns, rowCount);
            }
            else
            {
                repairedPoints = BuildDefaultMeshDeformerPointGrid(columns, rowCount);
            }

            if (!AreMeshDeformPointListsEquivalent(row.meshDeformPoints, repairedPoints, columns, rowCount))
            {
                row.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(repairedPoints);
                repaired++;
            }
        }
        return repaired;
    }

    private List<SkyPrisonMeshDeformPoint> BuildDefaultMeshDeformerPointGrid(int columns, int rows)
    {
        columns = Mathf.Clamp(columns, 2, 16);
        rows = Mathf.Clamp(rows, 2, 16);
        List<SkyPrisonMeshDeformPoint> points = new List<SkyPrisonMeshDeformPoint>();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                points.Add(new SkyPrisonMeshDeformPoint
                {
                    x = x,
                    y = y,
                    offset = Vector2.zero,
                    handleLeftOffset = Vector2.zero,
                    handleRightOffset = Vector2.zero,
                    handleUpOffset = Vector2.zero,
                    handleDownOffset = Vector2.zero,
                });
            }
        }
        return points;
    }

    private bool AreMeshDeformPointListsEquivalent(List<SkyPrisonMeshDeformPoint> a, List<SkyPrisonMeshDeformPoint> b, int columns, int rows)
    {
        if (a == null || b == null)
            return a == b;

        const float eps = 0.001f;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                SkyPrisonMeshDeformPoint pa = FindMeshDeformPointInList(a, x, y);
                SkyPrisonMeshDeformPoint pb = FindMeshDeformPointInList(b, x, y);
                if (pa == null || pb == null)
                    return false;
                if ((pa.offset - pb.offset).sqrMagnitude > eps)
                    return false;
                if ((pa.handleLeftOffset - pb.handleLeftOffset).sqrMagnitude > eps)
                    return false;
                if ((pa.handleRightOffset - pb.handleRightOffset).sqrMagnitude > eps)
                    return false;
                if ((pa.handleUpOffset - pb.handleUpOffset).sqrMagnitude > eps)
                    return false;
                if ((pa.handleDownOffset - pb.handleDownOffset).sqrMagnitude > eps)
                    return false;
            }
        }
        return true;
    }

    public Vector2 EvaluateTimelineRuntimeBoneRootOffset(string targetKey, Vector2 fallback, out bool hasValue)
    {
        hasValue = false;
        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindTimelineKeyframePairExcludingKind(targetKey, TimelineCurrentFrameFloat, "RigAngle", out prev, out next)) return fallback;
        bool prevHas = prev != null && prev.useRuntimeBoneRootOffset;
        bool nextHas = next != null && next.useRuntimeBoneRootOffset;
        if (!prevHas && !nextHas) return fallback;
        hasValue = true;
        if (!prevHas) return next.runtimeBoneRootOffset;
        if (!nextHas) return prev.runtimeBoneRootOffset;
        if (next.frame == prev.frame) return next.runtimeBoneRootOffset;
        float t = SmoothTimelineInterpolation(prev.frame, next.frame, TimelineCurrentFrameFloat);
        return Vector2.LerpUnclamped(prev.runtimeBoneRootOffset, next.runtimeBoneRootOffset, t);
    }

    public Vector2 EvaluateTimelineRuntimeBoneHeadOffset(string targetKey, Vector2 fallback, out bool hasValue)
    {
        hasValue = false;
        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindTimelineKeyframePairExcludingKind(targetKey, TimelineCurrentFrameFloat, "RigAngle", out prev, out next)) return fallback;
        bool prevHas = prev != null && prev.useRuntimeBoneHeadOffset;
        bool nextHas = next != null && next.useRuntimeBoneHeadOffset;
        if (!prevHas && !nextHas) return fallback;
        hasValue = true;
        if (!prevHas) return next.runtimeBoneHeadOffset;
        if (!nextHas) return prev.runtimeBoneHeadOffset;
        if (next.frame == prev.frame) return next.runtimeBoneHeadOffset;
        float t = SmoothTimelineInterpolation(prev.frame, next.frame, TimelineCurrentFrameFloat);
        return Vector2.LerpUnclamped(prev.runtimeBoneHeadOffset, next.runtimeBoneHeadOffset, t);
    }

    public Vector2 EvaluateTimelineRuntimeOffset(string targetKey, Vector2 fallback)
    {
        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindTimelineKeyframePairExcludingKind(targetKey, TimelineCurrentFrameFloat, "RigAngle", out prev, out next)) return fallback;
        if (prev == null) return next.runtimeOffset;
        if (next == null) return prev.runtimeOffset;
        if (next.frame == prev.frame) return next.runtimeOffset;
        float t = SmoothTimelineInterpolation(prev.frame, next.frame, TimelineCurrentFrameFloat);
        return Vector2.LerpUnclamped(prev.runtimeOffset, next.runtimeOffset, t);
    }

    public float EvaluateTimelineOpacity(string targetKey, float fallback)
    {
        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindTimelineKeyframePair(targetKey, TimelineCurrentFrameFloat, out prev, out next)) return fallback;
        if (prev == null) return next.opacity;
        if (next == null) return prev.opacity;
        if (next.frame == prev.frame) return next.opacity;
        float t = SmoothTimelineInterpolation(prev.frame, next.frame, TimelineCurrentFrameFloat);
        return Mathf.LerpUnclamped(prev.opacity, next.opacity, t);
    }

    public float EvaluateTimelineLayerWeightForPsb(string psbLayerKey, string boundRigKey, float fallback)
    {
        SkyPrisonAnimationTimelineKeyframe prev = null;
        SkyPrisonAnimationTimelineKeyframe next = null;
        string actionKey = CurrentActionKey();
        float frame = TimelineCurrentFrameFloat;

        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null || k.actionKey != actionKey)
                continue;

            bool matchLayer = !string.IsNullOrEmpty(psbLayerKey) && k.layerWeightTargetKey == psbLayerKey;
            bool legacyMatch = string.IsNullOrEmpty(k.layerWeightTargetKey)
                && ((!string.IsNullOrEmpty(psbLayerKey) && k.targetKey == psbLayerKey)
                    || (!string.IsNullOrEmpty(boundRigKey) && k.targetKey == boundRigKey));

            if (!matchLayer && !legacyMatch)
                continue;

            if (k.frame <= frame && (prev == null || k.frame > prev.frame)) prev = k;
            if (k.frame >= frame && (next == null || k.frame < next.frame)) next = k;
        }

        if (prev == null && next == null) return fallback;
        float a = prev != null ? prev.layerWeight + prev.manualLayerWeightOffset : fallback;
        float b = next != null ? next.layerWeight + next.manualLayerWeightOffset : fallback;
        if (prev == null) return b;
        if (next == null) return a;
        if (next.frame == prev.frame) return b;
        float t = SmoothTimelineInterpolation(prev.frame, next.frame, frame);
        return Mathf.LerpUnclamped(a, b, t);
    }

    public float EvaluateTimelineLayerWeight(string targetKey, float fallback)
    {
        SkyPrisonAnimationTimelineKeyframe prev, next;
        if (!TryFindTimelineKeyframePair(targetKey, TimelineCurrentFrameFloat, out prev, out next)) return fallback;
        float a = prev != null ? prev.layerWeight + prev.manualLayerWeightOffset : fallback;
        float b = next != null ? next.layerWeight + next.manualLayerWeightOffset : fallback;
        if (prev == null) return b;
        if (next == null) return a;
        if (next.frame == prev.frame) return b;
        float t = SmoothTimelineInterpolation(prev.frame, next.frame, TimelineCurrentFrameFloat);
        return Mathf.LerpUnclamped(a, b, t);
    }

    private float SmoothTimelineInterpolation(int prevFrame, int nextFrame, float frame)
    {
        if (nextFrame == prevFrame)
            return 1f;

        float t = Mathf.Clamp01(Mathf.InverseLerp(prevFrame, nextFrame, frame));

        // Smootherstep: 比 SmoothStep 更柔和，首尾速度更自然，避免关键帧之间一跳一停的僵硬感。
        // 公式：t^3 * (t * (t * 6 - 15) + 10)
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private bool TryFindTimelineKeyframePairByKind(string targetKey, float frame, string requiredKind, out SkyPrisonAnimationTimelineKeyframe prev, out SkyPrisonAnimationTimelineKeyframe next)
    {
        prev = null;
        next = null;

        if (string.IsNullOrEmpty(targetKey) || string.IsNullOrEmpty(requiredKind))
            return false;

        string actionKey = CurrentActionKey();
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null || k.actionKey != actionKey || k.targetKey != targetKey) continue;
            if (!string.Equals(k.targetKind, requiredKind, StringComparison.OrdinalIgnoreCase)) continue;

            if (k.frame <= frame && (prev == null || k.frame > prev.frame)) prev = k;
            if (k.frame >= frame && (next == null || k.frame < next.frame)) next = k;
        }

        return prev != null || next != null;
    }

    private bool TryFindTimelineKeyframePairExcludingKind(string targetKey, float frame, string excludedKind, out SkyPrisonAnimationTimelineKeyframe prev, out SkyPrisonAnimationTimelineKeyframe next)
    {
        prev = null;
        next = null;
        if (string.IsNullOrEmpty(targetKey)) return false;
        string actionKey = CurrentActionKey();
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null || k.actionKey != actionKey || k.targetKey != targetKey) continue;
            if (!string.IsNullOrEmpty(excludedKind) && string.Equals(k.targetKind, excludedKind, StringComparison.OrdinalIgnoreCase)) continue;
            if (k.frame <= frame && (prev == null || k.frame > prev.frame)) prev = k;
            if (k.frame >= frame && (next == null || k.frame < next.frame)) next = k;
        }
        return prev != null || next != null;
    }

    private bool TryFindTimelineKeyframePair(string targetKey, float frame, out SkyPrisonAnimationTimelineKeyframe prev, out SkyPrisonAnimationTimelineKeyframe next)
    {
        prev = null;
        next = null;
        if (string.IsNullOrEmpty(targetKey)) return false;
        string actionKey = CurrentActionKey();
        for (int i = 0; i < TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = TimelineKeyframes[i];
            if (k == null || k.actionKey != actionKey || k.targetKey != targetKey) continue;
            if (k.frame <= frame && (prev == null || k.frame > prev.frame)) prev = k;
            if (k.frame >= frame && (next == null || k.frame < next.frame)) next = k;
        }
        return prev != null || next != null;
    }

    public void AutoBindSinglePsbLayer(SkyPrisonAnimationRigRow layer)
    {
        if(layer==null||layer.isFolder)return;
        if(IsCustomPurePsbMode)
        {
            layer.boundRigKey="";
            layer.boundRigName="";
            layer.bindMode="未绑定";
            layer.bindConfidence=0f;
            layer.mapped=false;
            return;
        }
        BuildMockDataIfNeeded();

        string lookup=(layer.name??"")+" "+(layer.sourceSpriteName??"")+" "+(layer.sourceLayerPath??"")+" "+(layer.semantic??"");

        // 头发图层不能再粗暴合并到 Head。
        // 它们会自动挂到 Head 下方的无骨骼 Hair 分组 / 发束节点：
        // Head -> HairRoot -> HairFrontRoot / HairSide_LRoot / ... -> 具体图层节点。
        string rigKey;
        if(!TryResolveHairProxyRigKeyForPsbLayer(layer, lookup, out rigKey))
            rigKey=GuessCoreSpineRigKeyForPsbLayerName(lookup,layer.semantic);

        SkyPrisonAnimationRigRow rig=FindRigRow(rigKey);

        // 自动绑定只绑定到明确的驱动节点。饰品等不确定对象保持未绑定，避免坐标系污染。
        if(rig==null || string.IsNullOrEmpty(rigKey))
        {
            layer.boundRigKey="";
            layer.boundRigName="";
            layer.bindMode="未绑定";
            layer.bindConfidence=0f;
            layer.mapped=false;
            return;
        }

        layer.boundRigKey=rig.key;
        layer.boundRigName=rig.name;
        layer.bindMode=rig.key.StartsWith("Hair", StringComparison.OrdinalIgnoreCase)?"头发节点自动":"中轴自动";
        layer.bindConfidence=CalculateCoreSpineAutoBindConfidence(lookup, rig.key);
        layer.mapped=true;
        CopyPsbSourceToRig(layer,rig);
    }

    private float CalculateCoreSpineAutoBindConfidence(string lookup,string rigKey)
    {
        string n=NormalizeLayerBindName(lookup??"");
        if(string.IsNullOrEmpty(rigKey))return 0f;
        if(rigKey.StartsWith("Hair", StringComparison.OrdinalIgnoreCase))return 0.96f;
        if(rigKey=="Head" && ContainsAny(n,"head","face","eye","mouth","nose","brow","头","顔","脸","眼","口","鼻","眉"))return 0.94f;
        if(rigKey=="Neck" && ContainsAny(n,"neck","首","脖"))return 0.92f;
        // 人体模板 V1：Pelvis 是骨盆控制点；Spine 行承担“下半身/腹腰”；Chest 行承担“上半身/胸腔”。
        if(rigKey=="Chest" && ContainsAny(n,"torso_upper","upper_torso","body_upper","upperbody","spine_upper","chest","bust","collar","shoulderbase","上半身","躯干上","身体上","胸口","胸腔","锁骨","肩颈"))return 0.91f;
        if(rigKey=="Spine" && ContainsAny(n,"torso_lower","lower_torso","body_lower","lowerbody","spine_lower","abdomen","belly","waist","下半身","躯干下","身体下","腹","腰"))return 0.91f;
        if(rigKey=="Pelvis" && ContainsAny(n,"pelvis","hip","骨盆","盆","胯"))return 0.91f;
        if(rigKey=="Shoulder_L" || rigKey=="Shoulder_R" || rigKey=="Elbow_L" || rigKey=="Elbow_R" || rigKey=="Wrist_L" || rigKey=="Wrist_R")return 0.88f;
        if(rigKey=="Hip_L" || rigKey=="Hip_R" || rigKey=="Knee_L" || rigKey=="Knee_R" || rigKey=="Ankle_L" || rigKey=="Ankle_R" || rigKey=="Foot_L" || rigKey=="Foot_R")return 0.88f;
        return 0.76f;
    }


    private bool TryResolveHairProxyRigKeyForPsbLayer(SkyPrisonAnimationRigRow layer, string lookup, out string rigKey)
    {
        rigKey = "";
        if (layer == null || layer.isFolder) return false;

        string n = NormalizeLayerBindName((lookup ?? "") + " " + (layer.name ?? "") + " " + (layer.sourceSpriteName ?? "") + " " + (layer.sourceLayerPath ?? ""));
        if (!IsHairLayerName(n)) return false;

        EnsureHairProxyRigHierarchy();

        string groupKey = ResolveHairProxyGroupKey(n);
        string rawName = !string.IsNullOrWhiteSpace(layer.sourceSpriteName) ? layer.sourceSpriteName : layer.name;
        if (string.IsNullOrWhiteSpace(rawName)) rawName = layer.key;

        string leafKey = BuildHairProxyLeafKey(rawName, layer.key, groupKey);
        string displayName = string.IsNullOrWhiteSpace(layer.name) ? rawName : layer.name;
        AddRigProxyIfMissing(leafKey, displayName, ResolveHairProxySemantic(groupKey), GetRigDepth(groupKey) + 1, groupKey, GuessHairProxyIcon(groupKey));

        SkyPrisonAnimationRigRow leaf = FindRigRow(leafKey);
        if (leaf != null)
        {
            leaf.visible = layer.visible;
            leaf.opacity = layer.opacity;
            leaf.previewColor = layer.previewColor;
            leaf.usePsbLayerWeight = layer.usePsbLayerWeight;
            leaf.psbLayerWeight = layer.psbLayerWeight;
            leaf.visualSlotKey = "Hair";
            leaf.slotKey = "Hair";
            leaf.mapped = true;
            string guessedPhysicsPresetKey = GuessPhysicsPresetKeyForRow(leaf);
            if (!string.IsNullOrWhiteSpace(guessedPhysicsPresetKey) && string.IsNullOrWhiteSpace(leaf.physicsPresetKey))
            {
                leaf.usePhysicsInfluence = true;
                leaf.physicsPresetKey = guessedPhysicsPresetKey;
                SkyPrisonPhysicsPreset guessedPhysicsPreset = FindPhysicsPreset(guessedPhysicsPresetKey);
                leaf.physicsInfluenceStrength = guessedPhysicsPreset != null ? Mathf.Max(leaf.physicsInfluenceStrength, guessedPhysicsPreset.defaultBlend) : Mathf.Max(leaf.physicsInfluenceStrength, 0.35f);
            }
        }

        rigKey = leafKey;
        return true;
    }

    private bool IsHairLayerName(string normalizedName)
    {
        string n = normalizedName ?? "";
        return ContainsAny(n,
            "hair", "bang", "fringe", "braid", "ponytail", "sidelock", "side_hair", "hair_side", "front_hair", "hair_front", "back_hair", "hair_back",
            "髪", "发", "頭髪", "头发", "刘海", "前髪", "前发", "後髪", "后髪", "后发", "侧发", "側髪", "横髪", "辫", "辮", "马尾", "馬尾");
    }

    private void EnsureHairProxyRigHierarchy()
    {
        EnsureCoreRigNodes();
        AddRigProxyIfMissing("HairRoot", "头发", "Hair", 6, "Head", 42);
        AddRigProxyIfMissing("HairFrontRoot", "刘海组", "HairFront", 7, "HairRoot", 42);
        AddRigProxyIfMissing("HairSide_LRoot", "左侧发组", "HairSide_L", 7, "HairRoot", 42);
        AddRigProxyIfMissing("HairSide_RRoot", "右侧发组", "HairSide_R", 7, "HairRoot", 42);
        AddRigProxyIfMissing("HairBackRoot", "后发组", "HairBack", 7, "HairRoot", 42);
        AddRigProxyIfMissing("BraidRoot", "辫子组", "Braid", 7, "HairRoot", 42);
        AddRigProxyIfMissing("HairOtherRoot", "其它头发组", "Hair", 7, "HairRoot", 42);
    }

    private string ResolveHairProxyGroupKey(string normalizedName)
    {
        string n = normalizedName ?? "";
        if (ContainsAny(n, "braid", "pony", "ponytail", "tail_hair", "辫", "辮", "马尾", "馬尾")) return "BraidRoot";
        if (ContainsAny(n, "front_hair", "hair_front", "bang", "fringe", "刘海", "前髪", "前发")) return "HairFrontRoot";
        if (ContainsAny(n, "back_hair", "hair_back", "後髪", "后髪", "后发")) return "HairBackRoot";
        if (ContainsAny(n, "side_hair", "hair_side", "sidelock", "侧发", "側髪", "横髪"))
        {
            string side = GuessSideSuffix(n);
            if (side == "_R") return "HairSide_RRoot";
            return "HairSide_LRoot";
        }
        string guessedSide = GuessSideSuffix(n);
        if (guessedSide == "_L") return "HairSide_LRoot";
        if (guessedSide == "_R") return "HairSide_RRoot";
        return "HairOtherRoot";
    }

    private string ResolveHairProxySemantic(string groupKey)
    {
        switch (groupKey)
        {
            case "HairFrontRoot": return "HairFront";
            case "HairSide_LRoot": return "HairSide_L";
            case "HairSide_RRoot": return "HairSide_R";
            case "HairBackRoot": return "HairBack";
            case "BraidRoot": return "Braid";
            default: return "Hair";
        }
    }

    private int GuessHairProxyIcon(string groupKey)
    {
        return 42;
    }

    private string BuildHairProxyLeafKey(string rawName, string layerKey, string groupKey)
    {
        // 使用 PSB 行 key 作为叶节点 key 的主要来源。PSB 行 key 在导入时已经防重，
        // 并且不会被 ClearRigPsbLinksOnly 清掉；这样反复“刷新绑定预览”不会生成 Hair_xxx_1 / _2 脏节点。
        string safeLayerKey = SanitizeKeyToken(layerKey);
        if (!string.IsNullOrWhiteSpace(safeLayerKey))
            return "Hair_" + safeLayerKey;

        string safeName = SanitizeKeyToken(rawName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Layer";
        string baseKey = "Hair_" + safeName;
        if (FindRigRow(baseKey) == null) return baseKey;

        int suffix = 1;
        while (FindRigRow(baseKey + "_" + suffix) != null) suffix++;
        return baseKey + "_" + suffix;
    }

    private string SanitizeKeyToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
            }
            else if (c == '_' || c == '-' || c == ' ' || c == '/' || c == '\\')
            {
                if (sb.Length == 0 || sb[sb.Length - 1] != '_') sb.Append('_');
            }
        }
        string s = sb.ToString().Trim('_');
        if (string.IsNullOrEmpty(s)) return "Layer";
        return s;
    }

    private int GetRigDepth(string key)
    {
        SkyPrisonAnimationRigRow row = FindRigRow(key);
        return row != null ? row.depth : 0;
    }

    private string GuessCoreSpineRigKeyForPsbLayerName(string layerName,string semantic)
    {
        string n=NormalizeLayerBindName((string.IsNullOrEmpty(layerName)?semantic:layerName) + " " + (semantic ?? ""));

        // 默认绑定表 Key：Pelvis -> Spine(下半身/腹腰) -> Chest(上半身/胸腔) -> Neck -> Head。
        // 先让明确命名的上下半身优先于泛称 body/skin/base。
        if(ContainsAny(n,"torso_upper","upper_torso","body_upper","upperbody","spine_upper","上半身","躯干上","身体上"))return "Chest";
        if(ContainsAny(n,"chest","bust","collar","shoulderbase","胸口","胸腔","锁骨","肩颈"))return "Chest";
        if(ContainsAny(n,"torso_lower","lower_torso","body_lower","lowerbody","spine_lower","waist","abdomen","belly","腰","腹","下半身","躯干下","身体下"))return "Spine";
        if(ContainsAny(n,"pelvis","hip","骨盆","盆","胯"))return "Pelvis";

        // 头发图层由 TryResolveHairProxyRigKeyForPsbLayer 创建 Head 下的无骨骼发束节点。
        // 这里不再返回 Head，避免 hair_front / hair_side_L / braid 等细分层被合并。
        if(ContainsAny(n,"hair","bang","fringe","braid","ponytail","髪","发","刘海","辫","马尾","横髪","前髪","後髪"))return "";

        // 手臂第一阶段：允许从 Chest/肩颈处分出左右手臂链。
        // 上臂绑定 Shoulder_*，下臂/前腕绑定 Elbow_*，手/手腕绑定 Wrist_*。
        string side = GuessSideSuffix(n);
        if(!string.IsNullOrEmpty(side))
        {
            if(ContainsAny(n,"upper_arm","arm_upper","upperarm","arm_l_upper","arm_r_upper","上臂","大臂","肩臂"))return "Shoulder"+side;
            if(ContainsAny(n,"lower_arm","arm_lower","forearm","fore_arm","elbow","arm_l_lower","arm_r_lower","前腕","下臂","小臂","肘"))return "Elbow"+side;
            if(ContainsAny(n,"wrist","hand","palm","finger","fist","arm_l_hand","arm_r_hand","手腕","手掌","手指","手","拳"))return "Wrist"+side;
            if(ContainsAny(n,"arm","腕","臂","肩"))return "Shoulder"+side;

            // 腿部第三阶段：大腿绑定 Hip_*，小腿/膝盖绑定 Knee_*，脚踝绑定 Ankle_*，脚/脚尖绑定 Foot_*。
            if(ContainsAny(n,"upper_leg","leg_upper","thigh","leg_l_upper","leg_r_upper","大腿","上腿","股"))return "Hip"+side;
            if(ContainsAny(n,"lower_leg","leg_lower","shin","calf","knee","leg_l_lower","leg_r_lower","小腿","下腿","膝"))return "Knee"+side;
            if(ContainsAny(n,"ankle","heel","脚踝","踝","跟"))return "Ankle"+side;
            if(ContainsAny(n,"foot","feet","toe","shoe","sock","leg_l_foot","leg_r_foot","脚尖","脚","足","靴","鞋","袜"))return "Foot"+side;
            if(ContainsAny(n,"leg","腿"))return "Hip"+side;
        }

        // 明确排除：饰品先不参与，避免一开始就把坐标系带乱。手臂/腿部已在上面单独处理。
        if(ContainsAny(n,
            "ribbon","bow","accessory","ornament","clip",
            "饰品","头饰","发卡","リボン"))
            return "";

        if(ContainsAny(n,"neck","首","脖"))return "Neck";
        if(ContainsAny(n,"head","face","eye","pupil","iris","sclera","mouth","nose","brow","cheek","头","顔","脸","眼","瞳","虹彩","眼白","口","嘴","鼻","眉","頬"))return "Head";

        if(ContainsAny(n,"torso_upper","upper_torso","body_upper","upperbody","spine_upper","上半身","躯干上","身体上"))return "Chest";
        if(ContainsAny(n,"chest","bust","collar","shoulderbase","胸口","胸腔","锁骨","肩颈"))return "Chest";
        if(ContainsAny(n,"torso_lower","lower_torso","body_lower","lowerbody","spine_lower","waist","abdomen","belly","腰","腹","下半身","躯干下","身体下"))return "Spine";
        if(ContainsAny(n,"pelvis","hip","骨盆","盆","胯"))return "Pelvis";

        // 太泛的 BODY / skin / basebody 不再硬绑到 Chest。
        // 单张身体图没有明确上下半身时，先挂在 Spine（下半身/躯干基座），避免污染 Pelvis 骨盆控制点。
        if(ContainsAny(n,"torso","body","basebody","skin","nude","躯干","身体","裸身","素体"))return "Spine";

        return "";
    }
    public SkyPrisonAnimationRigRow FindRigRow(string key){ if(string.IsNullOrEmpty(key))return null; for(int i=0;i<RigRows.Count;i++) if(RigRows[i].key==key) return RigRows[i]; return null; }
    public void EnsureCoreRigNodes()
    {
        if (ManualRigTemplateMode) return;
        if (RigRows.Count == 0) return;
        EnsureHumanoidRigTemplateV1();
    }

    public void RebuildHumanoidRigTemplateV1()
    {
        RigRows.Clear();
        AddHumanoidRigV1Rows();
        NormalizeHumanoidV1BodyRows();
        SelectedRig = Mathf.Clamp(SelectedRig, 0, Mathf.Max(0, RigRows.Count - 1));
    }

    private void EnsureHumanoidRigTemplateV1()
    {
        if (RigRows.Count == 0)
        {
            AddHumanoidRigV1Rows();
            NormalizeHumanoidV1BodyRows();
            return;
        }

        bool hasCoreSpineTemplate = FindRigRow("Root") != null
            && FindRigRow("Pelvis") != null
            && FindRigRow("Spine") != null
            && FindRigRow("Chest") != null
            && FindRigRow("Neck") != null
            && FindRigRow("Head") != null
            && FindRigRow("HeadTop") != null
            && FindRigRow("Shoulder_L") != null
            && FindRigRow("Elbow_L") != null
            && FindRigRow("Wrist_L") != null
            && FindRigRow("HandEnd_L") != null
            && FindRigRow("Shoulder_R") != null
            && FindRigRow("Elbow_R") != null
            && FindRigRow("Wrist_R") != null
            && FindRigRow("HandEnd_R") != null
            && FindRigRow("Hip_L") != null
            && FindRigRow("Knee_L") != null
            && FindRigRow("Ankle_L") != null
            && FindRigRow("Foot_L") != null
            && FindRigRow("Hip_R") != null
            && FindRigRow("Knee_R") != null
            && FindRigRow("Ankle_R") != null
            && FindRigRow("Foot_R") != null;

        // 第三阶段允许手臂和腿部分支存在；只有旧模板骨骼才触发回退重建。
        bool hasNonCoreBodyBones = FindRigRow("UpperArm_L") != null
            || FindRigRow("LowerArm_L") != null
            || FindRigRow("UpperLeg_L") != null
            || FindRigRow("LowerLeg_L") != null
            || FindRigRow("Toe_L") != null
            || FindRigRow("Torso") != null
            || FindRigRow("Face") != null
            || FindRigRow("HairFront") != null
            || FindRigRow("HairBack") != null
            || FindRigRow("HairSide_L") != null;

        if (!hasCoreSpineTemplate || hasNonCoreBodyBones)
        {
            List<SkyPrisonAnimationRigRow> oldRows = CloneRows(RigRows);
            RigRows.Clear();
            AddHumanoidRigV1Rows();
            NormalizeHumanoidV1BodyRows();
            RestoreManualOffsetsFromDeprecatedRows(oldRows);
        }
        else
        {
            NormalizeHumanoidV1BodyRows();
        }
    }

    private void AddHumanoidRigV1Rows()
    {
        // 人体模板中轴链：Root -> Pelvis -> Spine(下半身/腹腰) -> Chest(上半身/胸腔) -> Neck -> Head。
        // 注意：Spine 这一行显示为“下半身”，对应 PSB 的 torso_lower；Chest 对应 torso_upper。
        AddRigIfMissing("Root", "角色总控", "Root", 0, "", false, 42);
        AddRigIfMissing("Pelvis", "骨盆", "Pelvis", 1, "Root", false, 42);
        AddRigIfMissing("Spine", "下半身", "Spine", 2, "Pelvis", false, 42);
        AddRigIfMissing("Chest", "胸腔/肩颈", "Chest", 3, "Spine", false, 42);
        AddRigIfMissing("Shoulder_L", "左肩/左上臂根", "Shoulder_L", 4, "Chest", false, 42);
        AddRigIfMissing("Elbow_L", "左肘", "Elbow_L", 5, "Shoulder_L", false, 42);
        AddRigIfMissing("Wrist_L", "左腕", "Wrist_L", 6, "Elbow_L", false, 42);
        AddRigIfMissing("HandEnd_L", "左手端点", "HandEnd_L", 7, "Wrist_L", false, 42);
        AddRigIfMissing("Shoulder_R", "右肩/右上臂根", "Shoulder_R", 4, "Chest", false, 42);
        AddRigIfMissing("Elbow_R", "右肘", "Elbow_R", 5, "Shoulder_R", false, 42);
        AddRigIfMissing("Wrist_R", "右腕", "Wrist_R", 6, "Elbow_R", false, 42);
        AddRigIfMissing("HandEnd_R", "右手端点", "HandEnd_R", 7, "Wrist_R", false, 42);
        AddRigIfMissing("Hip_L", "左髋/左大腿根", "Hip_L", 2, "Pelvis", false, 42);
        AddRigIfMissing("Knee_L", "左膝", "Knee_L", 3, "Hip_L", false, 42);
        AddRigIfMissing("Ankle_L", "左脚踝", "Ankle_L", 4, "Knee_L", false, 42);
        AddRigIfMissing("Foot_L", "左脚尖", "Foot_L", 5, "Ankle_L", false, 42);
        AddRigIfMissing("Hip_R", "右髋/右大腿根", "Hip_R", 2, "Pelvis", false, 42);
        AddRigIfMissing("Knee_R", "右膝", "Knee_R", 3, "Hip_R", false, 42);
        AddRigIfMissing("Ankle_R", "右脚踝", "Ankle_R", 4, "Knee_R", false, 42);
        AddRigIfMissing("Foot_R", "右脚尖", "Foot_R", 5, "Ankle_R", false, 42);
        AddRigIfMissing("Neck", "脖子", "Neck", 4, "Chest", false, 42);
        AddRigIfMissing("Head", "头", "Head", 5, "Neck", false, 42);
        AddRigIfMissing("HeadTop", "头顶", "HeadTop", 6, "Head", false, 42);
    }

    private void NormalizeHumanoidV1BodyRows()
    {
        // 兼容旧缓存 / 已打开工程：不改 key，只修正人体模板的语义显示和父子链。
        SkyPrisonAnimationRigRow root = FindRigRow("Root");
        if (root != null)
        {
            root.name = "角色总控";
            root.semantic = "Root";
            root.parentKey = "";
            root.depth = 0;
        }

        SkyPrisonAnimationRigRow pelvis = FindRigRow("Pelvis");
        if (pelvis != null)
        {
            pelvis.name = "骨盆";
            pelvis.semantic = "Pelvis";
            pelvis.parentKey = "Root";
            pelvis.depth = 1;
        }

        SkyPrisonAnimationRigRow spine = FindRigRow("Spine");
        if (spine != null)
        {
            spine.name = "下半身";
            spine.semantic = "Spine";
            spine.parentKey = "Pelvis";
            spine.depth = 2;
        }

        SkyPrisonAnimationRigRow chest = FindRigRow("Chest");
        if (chest != null)
        {
            chest.name = "胸腔/肩颈";
            chest.semantic = "Chest";
            chest.parentKey = "Spine";
            chest.depth = 3;
        }
    }

    private void RestoreManualOffsetsFromDeprecatedRows(List<SkyPrisonAnimationRigRow> oldRows)
    {
        if (oldRows == null) return;
        for (int i = 0; i < oldRows.Count; i++)
        {
            SkyPrisonAnimationRigRow old = oldRows[i];
            if (old == null || string.IsNullOrEmpty(old.key)) continue;
            string newKey = ConvertDeprecatedRigKeyToHumanoidV1(old.key);
            SkyPrisonAnimationRigRow row = FindRigRow(newKey);
            if (row == null) continue;
            row.useManualRigOffset = old.useManualRigOffset;
            row.manualRigOffset = old.manualRigOffset;
            row.useManualRigLayerOffset = old.useManualRigLayerOffset;
            row.manualRigLayerOffset = old.manualRigLayerOffset;
            row.useManualBoneRootOffset = old.useManualBoneRootOffset;
            row.manualBoneRootOffset = old.manualBoneRootOffset;
            row.useManualBoneHeadOffset = old.useManualBoneHeadOffset;
            row.manualBoneHeadOffset = old.manualBoneHeadOffset;
            row.useRuntimeBoneRootOffset = old.useRuntimeBoneRootOffset;
            row.runtimeBoneRootOffset = old.runtimeBoneRootOffset;
            row.useRuntimeBoneHeadOffset = old.useRuntimeBoneHeadOffset;
            row.runtimeBoneHeadOffset = old.runtimeBoneHeadOffset;
        }
    }

    private string ConvertDeprecatedRigKeyToHumanoidV1(string key)
    {
        switch (key)
        {
            // 旧中轴链里 Chest 承担的是“上半身主骨骼”。
            // 新链补了 Spine 后，这份旧的上半身控制应迁移到 Spine。
            case "Chest": return "Spine";
            case "Torso": return "Spine";
            case "Face": return "Head";
            case "UpperArm_L": return "Shoulder_L";
            case "UpperArm_R": return "Shoulder_R";
            case "LowerArm_L": return "Elbow_L";
            case "LowerArm_R": return "Elbow_R";
            case "Hand_L": return "Wrist_L";
            case "Hand_R": return "Wrist_R";
            case "UpperLeg_L": return "Hip_L";
            case "UpperLeg_R": return "Hip_R";
            case "LowerLeg_L": return "Knee_L";
            case "LowerLeg_R": return "Knee_R";
            case "Toe_L": return "Foot_L";
            case "Toe_R": return "Foot_R";
            case "HairFront":
            case "HairBack":
            case "HairSide_L":
            case "HairSide_R":
            case "Braid_L":
            case "Braid_R":
            case "Eye_L":
            case "Eye_R":
            case "Brow_L":
            case "Brow_R":
            case "Accessory":
            case "Accessory_L":
            case "Accessory_R":
                return "Head";
            default: return key;
        }
    }

    private void AddRigIfMissing(string key,string name,string semantic,int depth,string parent,bool folder,int icon)
    {
        if(FindRigRow(key)!=null)return;
        RigRows.Add(new SkyPrisonAnimationRigRow{key=key,name=name,semantic=semantic,depth=depth,parentKey=parent,isFolder=folder,previewIconNumber=icon,expanded=true,hasKey=true});
    }

    private void AddRigProxyIfMissing(string key,string name,string semantic,int depth,string parent,int icon)
    {
        if(string.IsNullOrWhiteSpace(key) || FindRigRow(key)!=null)return;
        RigRows.Add(new SkyPrisonAnimationRigRow
        {
            key=key,
            name=name,
            semantic=semantic,
            depth=depth,
            parentKey=parent,
            isFolder=false,
            expanded=true,
            hasKey=false,
            mapped=true,
            previewIconNumber=icon,
            visualSlotKey="Hair",
            slotKey="Hair",
            useCustomBoneLine=false,
            useManualBoneRootOffset=false,
            useManualBoneHeadOffset=false
        });
    }
    private void BuildMockDataIfNeeded()
{ if(IsCustomPurePsbMode) return; if(RigRows.Count==0) BuildMockData(); EnsureCoreRigNodes(); }
    private string GuessRigKeyForPsbLayerName(string layerName,string semantic)
    {
        // 第二阶段允许中轴链 + 手臂分支自动绑定。
        // 腿、鞋袜、饰品仍然先返回空，避免把未验证分支带乱。
        return GuessCoreSpineRigKeyForPsbLayerName(layerName, semantic);
    }

    private bool ContainsAny(string text,params string[] tokens)
    {
        if(string.IsNullOrEmpty(text)||tokens==null)return false;
        for(int i=0;i<tokens.Length;i++)
        {
            string t=tokens[i];
            if(!string.IsNullOrEmpty(t)&&text.Contains(t))return true;
        }
        return false;
    }

    private bool IsStrictHandName(string n)
    {
        if(ContainsAny(n,"hand","palm","finger","fist","手掌","手指","掌","拳"))return true;
        if(!n.Contains("手"))return false;
        // “手臂 / 手腕 / 上臂 / 下臂”不是 Hand。
        if(ContainsAny(n,"手臂","手腕","上臂","下臂","前腕","腕","臂"))return false;
        return true;
    }

    private string GuessSideSuffix(string normalizedName)
    {
        if(string.IsNullOrEmpty(normalizedName))return "";
        bool left=HasLeftMarker(normalizedName);
        bool right=HasRightMarker(normalizedName);
        if(left&&!right)return "_L";
        if(right&&!left)return "_R";
        return "";
    }

    private bool HasLeftMarker(string n)
    {
        return n.Contains("左")||n.Contains(" left")||n.Contains("_left")||n.Contains("-left")||n.Contains(".left")||
               HasSideMarker(n,'l')||ContainsAny(n,"_l_","_l "," l_"," l ","(l)","[l]","左手","左腕","左脚","左足","左膝","左肩");
    }

    private bool HasRightMarker(string n)
    {
        return n.Contains("右")||n.Contains(" right")||n.Contains("_right")||n.Contains("-right")||n.Contains(".right")||
               HasSideMarker(n,'r')||ContainsAny(n,"_r_","_r "," r_"," r ","(r)","[r]","右手","右腕","右脚","右足","右膝","右肩");
    }

    private bool HasSideMarker(string normalizedName,char side)
    {
        for(int i=0;i<normalizedName.Length;i++)
        {
            if(normalizedName[i]!=side)continue;
            char prev=i>0?normalizedName[i-1]:' ';
            char next=i+1<normalizedName.Length?normalizedName[i+1]:' ';
            bool prevBoundary=prev==' '||prev=='_'||prev=='-'||prev=='.'||prev=='/'||prev=='('||prev=='['||prev=='（';
            bool nextBoundary=next==' '||next=='_'||next=='-'||next=='.'||next=='/'||next==')'||next==']'||next=='）'||char.IsDigit(next);
            if(prevBoundary&&nextBoundary)return true;

            // 兼容 PSB 常见命名：脚L / 手R / armL / legR / foot_l_01。
            bool prevIsCjk=prev>='\u4e00'&&prev<='\u9fff';
            bool nextIsBoundary=next==' '||next=='_'||next=='-'||next=='.'||next=='/'||char.IsDigit(next)||next==')'||next==']'||next=='）';
            if(prevIsCjk&&nextIsBoundary)return true;
        }
        return false;
    }

    private string NormalizeLayerBindName(string s){ if(string.IsNullOrEmpty(s))return ""; s=s.ToLowerInvariant(); s=s.Replace('-','_').Replace('.','_').Replace('/','_').Replace('\\','_'); return " "+s+" "; }
    private bool HasToken(string normalizedName,string token){ return normalizedName.Contains("_"+token+"_")||normalizedName.Contains(" "+token+"_")||normalizedName.Contains("_"+token+" "); }
    public int GetSemanticIndex(string semantic){ if(string.IsNullOrEmpty(semantic))return 0; string[] keys={"Root","Pelvis","Spine","Chest","Neck","Head","HeadTop","Shoulder","Elbow","Wrist","HandEnd","Hip","Knee","Ankle","Foot","Claw","Tail","Socket","Accessory"}; for(int i=0;i<keys.Length;i++)if(semantic.Contains(keys[i]))return i; return 0; }
    public Vector2 EvaluateInfinity(float phase){ float p=phase*FormulaFrequency+FormulaPhase; return new Vector2(Mathf.Sin(p)*InfinityAmplitudeX,Mathf.Sin(p*2f)*InfinityAmplitudeY); }
    public float EvaluateFormula(float time){ float p=time*FormulaFrequency*Mathf.PI*2f+FormulaPhase; if(FormulaType==SkyPrisonAnimationFormulaType.Sine)return FormulaOffset+Mathf.Sin(p)*FormulaAmplitude; if(FormulaType==SkyPrisonAnimationFormulaType.AbsSine)return FormulaOffset+Mathf.Abs(Mathf.Sin(p))*FormulaAmplitude; if(FormulaType==SkyPrisonAnimationFormulaType.Shake)return FormulaOffset+Mathf.Sin(p*7f)*FormulaAmplitude; return FormulaOffset; }
    public void PushStructureUndo(){ undoStack.Push(new StructureUndoSnapshot(this)); TrimStructureUndoStack(undoStack); redoStack.Clear(); }

    // 给 IMGUI 检查器使用：先截旧状态，控件真的变更后再把旧状态压入撤销栈。
    // 返回 object 是为了不把内部快照类型暴露给其它面板。
    public object CaptureStructureUndoSnapshot(){ return new StructureUndoSnapshot(this); }
    public void PushCapturedStructureUndo(object snapshot)
    {
        StructureUndoSnapshot s = snapshot as StructureUndoSnapshot;
        if (s == null) return;
        undoStack.Push(s);
        TrimStructureUndoStack(undoStack);
        redoStack.Clear();
    }

    public bool UndoStructure(){ if(undoStack.Count==0)return false; redoStack.Push(new StructureUndoSnapshot(this)); TrimStructureUndoStack(redoStack); StructureUndoSnapshot snapshot=undoStack.Pop(); snapshot.RestoreTo(this); return true; }
    public bool RedoStructure(){ if(redoStack.Count==0)return false; undoStack.Push(new StructureUndoSnapshot(this)); TrimStructureUndoStack(undoStack); StructureUndoSnapshot snapshot=redoStack.Pop(); snapshot.RestoreTo(this); return true; }
    public void ClearStructureUndo(){ undoStack.Clear(); redoStack.Clear(); }
    public void ClearRigUndo(){ rigUndoStack.Clear(); rigRedoStack.Clear(); }
    public void PushRigUndo(){ rigUndoStack.Push(CloneRows(RigRows)); TrimUndoStack(rigUndoStack); rigRedoStack.Clear(); }
    public bool UndoRig(){ if(rigUndoStack.Count==0)return false; rigRedoStack.Push(CloneRows(RigRows)); TrimUndoStack(rigRedoStack); ReplaceRows(RigRows,rigUndoStack.Pop()); SelectedRig=Mathf.Clamp(SelectedRig,0,Mathf.Max(0,RigRows.Count-1)); return true; }
    public bool RedoRig(){ if(rigRedoStack.Count==0)return false; rigUndoStack.Push(CloneRows(RigRows)); TrimUndoStack(rigUndoStack); ReplaceRows(RigRows,rigRedoStack.Pop()); SelectedRig=Mathf.Clamp(SelectedRig,0,Mathf.Max(0,RigRows.Count-1)); return true; }
    static List<SkyPrisonAnimationActionGroupRow> CloneActionGroups(List<SkyPrisonAnimationActionGroupRow> src)
    {
        List<SkyPrisonAnimationActionGroupRow> list = new List<SkyPrisonAnimationActionGroupRow>();
        if (src == null) return list;
        for (int i = 0; i < src.Count; i++)
        {
            SkyPrisonAnimationActionGroupRow g = src[i];
            if (g == null) continue;
            list.Add(new SkyPrisonAnimationActionGroupRow { key = g.key, name = g.name, expanded = g.expanded });
        }
        return list;
    }

    static void ReplaceActionGroups(List<SkyPrisonAnimationActionGroupRow> dst, List<SkyPrisonAnimationActionGroupRow> src)
    {
        dst.Clear();
        if (src == null) return;
        List<SkyPrisonAnimationActionGroupRow> cloned = CloneActionGroups(src);
        for (int i = 0; i < cloned.Count; i++) dst.Add(cloned[i]);
    }

    static List<SkyPrisonAnimationActionRow> CloneActions(List<SkyPrisonAnimationActionRow> src)
    {
        List<SkyPrisonAnimationActionRow> list = new List<SkyPrisonAnimationActionRow>();
        if (src == null) return list;
        for (int i = 0; i < src.Count; i++)
        {
            SkyPrisonAnimationActionRow r = src[i];
            if (r == null) continue;
            list.Add(new SkyPrisonAnimationActionRow
            {
                key = r.key,
                name = r.name,
                type = r.type,
                status = r.status,
                loop = r.loop,
                duration = r.duration,
                groupKey = r.groupKey
            });
        }
        return list;
    }

    static void ReplaceActions(List<SkyPrisonAnimationActionRow> dst, List<SkyPrisonAnimationActionRow> src)
    {
        dst.Clear();
        if (src == null) return;
        List<SkyPrisonAnimationActionRow> cloned = CloneActions(src);
        for (int i = 0; i < cloned.Count; i++)
            dst.Add(cloned[i]);
    }

    static List<SkyPrisonPhysicsPreset> ClonePhysicsPresets(List<SkyPrisonPhysicsPreset> presets)
    {
        List<SkyPrisonPhysicsPreset> r = new List<SkyPrisonPhysicsPreset>();
        if (presets == null) return r;
        for (int i = 0; i < presets.Count; i++)
            if (presets[i] != null) r.Add(presets[i].Clone());
        return r;
    }

    static void ReplacePhysicsPresets(List<SkyPrisonPhysicsPreset> target, List<SkyPrisonPhysicsPreset> src)
    {
        target.Clear();
        if (src == null) return;
        for (int i = 0; i < src.Count; i++)
            if (src[i] != null) target.Add(src[i].Clone());
    }

    static List<SkyPrisonAnimationRigRow> CloneRows(List<SkyPrisonAnimationRigRow> rows){ var r=new List<SkyPrisonAnimationRigRow>(); if(rows==null)return r; for(int i=0;i<rows.Count;i++) if(rows[i]!=null) r.Add(rows[i].Clone()); return r; }
    static void ReplaceRows(List<SkyPrisonAnimationRigRow> target,List<SkyPrisonAnimationRigRow> src){ target.Clear(); if(src==null)return; for(int i=0;i<src.Count;i++) if(src[i]!=null) target.Add(src[i].Clone()); }
    static List<SkyPrisonAnimationAssemblySlot> CloneAssemblySlots(List<SkyPrisonAnimationAssemblySlot> slots){ var r=new List<SkyPrisonAnimationAssemblySlot>(); if(slots==null)return r; for(int i=0;i<slots.Count;i++) if(slots[i]!=null) r.Add(slots[i].Clone()); return r; }
    static void ReplaceAssemblySlots(List<SkyPrisonAnimationAssemblySlot> target,List<SkyPrisonAnimationAssemblySlot> src){ target.Clear(); if(src==null)return; for(int i=0;i<src.Count;i++) if(src[i]!=null) target.Add(src[i].Clone()); }
    static List<SkyPrisonAnimationLayerOrderKeyframe> CloneLayerOrderKeyframes(List<SkyPrisonAnimationLayerOrderKeyframe> src){ var r=new List<SkyPrisonAnimationLayerOrderKeyframe>(); if(src==null)return r; for(int i=0;i<src.Count;i++){ var k=src[i]; if(k==null)continue; r.Add(new SkyPrisonAnimationLayerOrderKeyframe{actionKey=k.actionKey,layerKey=k.layerKey,time=k.time,orderWeight=k.orderWeight}); } return r; }
    static void ReplaceLayerOrderKeyframes(List<SkyPrisonAnimationLayerOrderKeyframe> target,List<SkyPrisonAnimationLayerOrderKeyframe> src){ target.Clear(); if(src==null)return; for(int i=0;i<src.Count;i++){ var k=src[i]; if(k==null)continue; target.Add(new SkyPrisonAnimationLayerOrderKeyframe{actionKey=k.actionKey,layerKey=k.layerKey,time=k.time,orderWeight=k.orderWeight}); } }
    static List<SkyPrisonAnimationTimelineKeyframe> CloneTimelineKeyframes(List<SkyPrisonAnimationTimelineKeyframe> src){ var r=new List<SkyPrisonAnimationTimelineKeyframe>(); if(src==null)return r; for(int i=0;i<src.Count;i++){ var k=src[i]; if(k==null)continue; r.Add(k.Clone()); } return r; }
    static void ReplaceTimelineKeyframes(List<SkyPrisonAnimationTimelineKeyframe> target,List<SkyPrisonAnimationTimelineKeyframe> src){ target.Clear(); if(src==null)return; for(int i=0;i<src.Count;i++){ var k=src[i]; if(k==null)continue; target.Add(k.Clone()); } }
    static List<SkyPrisonAnimationMotionKeyframe> CloneMotionKeyframes(List<SkyPrisonAnimationMotionKeyframe> src){ var r=new List<SkyPrisonAnimationMotionKeyframe>(); if(src==null)return r; for(int i=0;i<src.Count;i++){ var k=src[i]; if(k==null)continue; r.Add(k.Clone()); } return r; }
    static void ReplaceMotionKeyframes(List<SkyPrisonAnimationMotionKeyframe> target,List<SkyPrisonAnimationMotionKeyframe> src){ target.Clear(); if(src==null)return; for(int i=0;i<src.Count;i++){ var k=src[i]; if(k==null)continue; target.Add(k.Clone()); } }
    static Dictionary<string,float> CloneManualBoneAngles(Dictionary<string,float> src){ var r=new Dictionary<string,float>(); if(src==null)return r; foreach(KeyValuePair<string,float> kv in src){ if(!string.IsNullOrEmpty(kv.Key)) r[kv.Key]=Mathf.Clamp(kv.Value,-180f,180f); } return r; }
    static List<SkyPrisonManualPoseKey> CloneManualPoseKeys(List<SkyPrisonManualPoseKey> src){ var r=new List<SkyPrisonManualPoseKey>(); if(src==null)return r; for(int i=0;i<src.Count;i++){ var p=src[i]; if(p==null)continue; var np=new SkyPrisonManualPoseKey{frame=p.frame,label=p.label}; if(p.angles!=null){ for(int j=0;j<p.angles.Count;j++){ var a=p.angles[j]; if(a==null)continue; np.angles.Add(new SkyPrisonManualPoseAngle{rigKey=a.rigKey,angle=a.angle}); } } r.Add(np); } return r; }
    static void ReplaceManualPoseKeys(List<SkyPrisonManualPoseKey> target,List<SkyPrisonManualPoseKey> src){ target.Clear(); if(src==null)return; List<SkyPrisonManualPoseKey> cloned=CloneManualPoseKeys(src); for(int i=0;i<cloned.Count;i++) target.Add(cloned[i]); }
    static void TrimStructureUndoStack(Stack<StructureUndoSnapshot> stack)
    {
        if(stack==null||stack.Count<=StructureUndoLimit)return;
        StructureUndoSnapshot[] arr=stack.ToArray();
        stack.Clear();
        int keep=Mathf.Min(StructureUndoLimit,arr.Length);
        for(int i=keep-1;i>=0;i--)
            stack.Push(arr[i]);
    }
    static void TrimUndoStack(Stack<List<SkyPrisonAnimationRigRow>> stack)
    {
        if(stack==null||stack.Count<=StructureUndoLimit)return;
        List<SkyPrisonAnimationRigRow>[] arr=stack.ToArray();
        stack.Clear();
        int keep=Mathf.Min(StructureUndoLimit,arr.Length);
        for(int i=keep-1;i>=0;i--)
            stack.Push(arr[i]);
    }
    public void EnsureDefaultPhysicsPresets()
    {
        if (PhysicsPresets.Count > 0)
        {
            for (int i = 0; i < PhysicsPresets.Count; i++)
                if (PhysicsPresets[i] != null) PhysicsPresets[i].EnsureOscillatorCount();
            return;
        }
        AddDefaultPhysicsPreset("hair_front_2", "刘海_短_2节", 2, 0.55f, 0.18f, 0.9f, 1.8f, 0.42f, 0.65f);
        AddDefaultPhysicsPreset("hair_side_3", "侧发_中_3节", 3, 0.80f, 0.35f, 0.9f, 1.5f, 0.35f, 0.75f);
        AddDefaultPhysicsPreset("hair_back_4", "后发_柔_4节", 4, 0.90f, 0.45f, 0.75f, 1.25f, 0.30f, 0.85f);
        AddDefaultPhysicsPreset("braid_5", "辫子_重_5节", 5, 1.00f, 0.65f, 0.65f, 1.05f, 0.42f, 1.15f);
        AddDefaultPhysicsPreset("ribbon_6", "飘带_轻_6节", 6, 1.10f, 0.75f, 1.05f, 1.35f, 0.22f, 0.55f);
    }

    private void AddDefaultPhysicsPreset(string key, string name, int count, float blend, float sway, float reaction, float returnSpeed, float damping, float weight)
    {
        SkyPrisonPhysicsPreset preset = new SkyPrisonPhysicsPreset
        {
            presetKey = key,
            displayName = name,
            oscillatorCount = Mathf.Clamp(count, 1, 12),
            globalScale = 1f,
            gravityAngle = -90f,
            gravityStrength = 1f,
            windInfluence = 0f,
            velocityInfluence = 1f,
            defaultBlend = Mathf.Clamp01(blend),
            oscillators = new List<SkyPrisonPhysicsOscillator>()
        };
        for (int i = 0; i < preset.oscillatorCount; i++)
        {
            preset.oscillators.Add(new SkyPrisonPhysicsOscillator
            {
                length = 7f,
                swayEase = Mathf.Clamp01(sway),
                reactionSpeed = Mathf.Max(0f, reaction),
                returnSpeed = Mathf.Max(0f, returnSpeed),
                damping = Mathf.Clamp01(damping),
                weight = Mathf.Max(0f, weight)
            });
        }
        PhysicsPresets.Add(preset);
    }

    public SkyPrisonPhysicsPreset FindPhysicsPreset(string key)
    {
        EnsureDefaultPhysicsPresets();
        if (string.IsNullOrWhiteSpace(key)) return null;
        for (int i = 0; i < PhysicsPresets.Count; i++)
            if (PhysicsPresets[i] != null && PhysicsPresets[i].presetKey == key) return PhysicsPresets[i];
        return null;
    }

    public string[] GetPhysicsPresetLabels()
    {
        EnsureDefaultPhysicsPresets();
        string[] labels = new string[PhysicsPresets.Count + 1];
        labels[0] = "无 / 不指定";
        for (int i = 0; i < PhysicsPresets.Count; i++)
        {
            SkyPrisonPhysicsPreset p = PhysicsPresets[i];
            labels[i + 1] = p == null ? "<空预设>" : (p.displayName + "  [" + p.presetKey + "]");
        }
        return labels;
    }

    public int GetPhysicsPresetIndex(string key)
    {
        EnsureDefaultPhysicsPresets();
        if (string.IsNullOrWhiteSpace(key)) return 0;
        for (int i = 0; i < PhysicsPresets.Count; i++)
            if (PhysicsPresets[i] != null && PhysicsPresets[i].presetKey == key) return i + 1;
        return 0;
    }

    public string GetPhysicsPresetKeyByIndex(int index)
    {
        EnsureDefaultPhysicsPresets();
        if (index <= 0) return string.Empty;
        int presetIndex = Mathf.Clamp(index - 1, 0, PhysicsPresets.Count - 1);
        SkyPrisonPhysicsPreset p = PhysicsPresets[presetIndex];
        return p == null ? string.Empty : p.presetKey;
    }

    public SkyPrisonPhysicsPreset CreatePhysicsPreset(string displayName, int oscillatorCount)
    {
        EnsureDefaultPhysicsPresets();
        SkyPrisonPhysicsPreset preset = new SkyPrisonPhysicsPreset
        {
            presetKey = GenerateUniquePhysicsPresetKey(BuildSafePhysicsPresetKey(displayName)),
            displayName = string.IsNullOrWhiteSpace(displayName) ? "新物理预设" : displayName,
            oscillatorCount = Mathf.Clamp(oscillatorCount, 1, 12),
            globalScale = 1f,
            gravityAngle = -90f,
            gravityStrength = 1f,
            windInfluence = 0f,
            velocityInfluence = 1f,
            defaultBlend = 0.5f,
            oscillators = new List<SkyPrisonPhysicsOscillator>()
        };
        preset.EnsureOscillatorCount();
        PhysicsPresets.Add(preset);
        SelectedPhysicsPresetIndex = PhysicsPresets.Count - 1;
        return preset;
    }

    public SkyPrisonPhysicsPreset DuplicatePhysicsPreset(SkyPrisonPhysicsPreset source)
    {
        if (source == null) return CreatePhysicsPreset("新物理预设", 3);
        SkyPrisonPhysicsPreset preset = source.Clone();
        preset.displayName = string.IsNullOrWhiteSpace(source.displayName) ? "物理预设副本" : source.displayName + " 副本";
        preset.presetKey = GenerateUniquePhysicsPresetKey(BuildSafePhysicsPresetKey(source.presetKey + "_copy"));
        PhysicsPresets.Add(preset);
        SelectedPhysicsPresetIndex = PhysicsPresets.Count - 1;
        return preset;
    }

    public void DeletePhysicsPreset(SkyPrisonPhysicsPreset preset)
    {
        if (preset == null) return;
        string key = preset.presetKey;
        PhysicsPresets.Remove(preset);
        for (int i = 0; i < RigRows.Count; i++) if (RigRows[i] != null && RigRows[i].physicsPresetKey == key) RigRows[i].physicsPresetKey = string.Empty;
        for (int i = 0; i < PsbRows.Count; i++) if (PsbRows[i] != null && PsbRows[i].physicsPresetKey == key) PsbRows[i].physicsPresetKey = string.Empty;
        for (int i = 0; i < SocketRows.Count; i++) if (SocketRows[i] != null && SocketRows[i].physicsPresetKey == key) SocketRows[i].physicsPresetKey = string.Empty;
        SelectedPhysicsPresetIndex = Mathf.Clamp(SelectedPhysicsPresetIndex, -1, Mathf.Max(-1, PhysicsPresets.Count - 1));
    }

    public string GuessPhysicsPresetKeyForRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null) return string.Empty;
        string n = NormalizeLayerBindName((row.key ?? "") + " " + (row.name ?? "") + " " + (row.semantic ?? "") + " " + (row.sourceLayerPath ?? ""));
        if (ContainsAny(n, "braid", "ponytail", "tail_hair", "辫", "辮", "马尾", "馬尾")) return "braid_5";
        if (ContainsAny(n, "ribbon", "scarf", "belt", "飘带", "缎带", "丝带")) return "ribbon_6";
        if (ContainsAny(n, "front_hair", "hair_front", "bang", "fringe", "刘海", "前髪", "前发")) return "hair_front_2";
        if (ContainsAny(n, "back_hair", "hair_back", "後髪", "后髪", "后发")) return "hair_back_4";
        if (ContainsAny(n, "side_hair", "hair_side", "sidelock", "侧发", "側髪", "横髪")) return "hair_side_3";
        if (ContainsAny(n, "hair", "髪", "发", "头发")) return "hair_side_3";
        return string.Empty;
    }

    private string GenerateUniquePhysicsPresetKey(string baseKey)
    {
        if (string.IsNullOrWhiteSpace(baseKey)) baseKey = "physics_preset";
        string key = baseKey;
        int suffix = 1;
        while (FindPhysicsPresetWithoutEnsuring(key) != null) key = baseKey + "_" + suffix++;
        return key;
    }

    private SkyPrisonPhysicsPreset FindPhysicsPresetWithoutEnsuring(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        for (int i = 0; i < PhysicsPresets.Count; i++)
            if (PhysicsPresets[i] != null && PhysicsPresets[i].presetKey == key) return PhysicsPresets[i];
        return null;
    }

    private string BuildSafePhysicsPresetKey(string raw)
    {
        string text = string.IsNullOrWhiteSpace(raw) ? "physics_preset" : raw.Trim().ToLowerInvariant();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            else if (c == '_' || c == '-' || char.IsWhiteSpace(c)) sb.Append('_');
        }
        string key = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(key) ? "physics_preset" : key;
    }


    public int CountAppearanceLayers(List<SkyPrisonAppearancePsbLayerNode> nodes)
    {
        if (nodes == null) return 0;
        int count = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            SkyPrisonAppearancePsbLayerNode n = nodes[i];
            if (n == null) continue;
            if (!n.isFolder && !n.isDyeMask) count++;
            count += CountAppearanceLayers(n.children);
        }
        return count;
    }

    public SkyPrisonAppearancePsbLayerNode GetSelectedAppearanceLayerInCurrentSlot()
    {
        SkyPrisonAnimationAssemblySlot slot = CurrentAssemblySlot();
        if (slot == null || string.IsNullOrEmpty(slot.selectedAppearanceLayerKey)) return null;
        return FindAppearanceLayerNode(slot.appearanceLayers, slot.selectedAppearanceLayerKey);
    }

    public SkyPrisonAppearancePsbLayerNode FindAppearanceLayerNode(List<SkyPrisonAppearancePsbLayerNode> nodes, string key)
    {
        if (nodes == null || string.IsNullOrEmpty(key)) return null;
        for (int i = 0; i < nodes.Count; i++)
        {
            SkyPrisonAppearancePsbLayerNode n = nodes[i];
            if (n == null) continue;
            if (n.key == key) return n;
            SkyPrisonAppearancePsbLayerNode c = FindAppearanceLayerNode(n.children, key);
            if (c != null) return c;
        }
        return null;
    }

    public void ClearSelectedAssemblyAppearancePsb()
    {
        SkyPrisonAnimationAssemblySlot slot = CurrentAssemblySlot();
        if (slot == null) return;

        string slotKey = string.IsNullOrWhiteSpace(slot.slotKey) ? slot.visualSlotKey : slot.slotKey;

        // 清空槽位时必须同时移除它同步进 PSB 预览列表的行。
        // 否则 UI 树看起来清空了，预览里旧衣服还会继续显示，下一次置入又像叠了一层。
        if (PsbRows != null && !string.IsNullOrWhiteSpace(slotKey))
        {
            for (int i = PsbRows.Count - 1; i >= 0; i--)
            {
                SkyPrisonAnimationRigRow row = PsbRows[i];
                if (row != null && row.fromAppearanceSlot && string.Equals(row.appearanceSlotKey, slotKey, StringComparison.OrdinalIgnoreCase))
                    PsbRows.RemoveAt(i);
            }
        }

        slot.appearancePackageKey = "";
        slot.appearanceSourceAssetPath = "";
        slot.selectedAppearanceLayerKey = "";
        if (slot.appearanceLayers == null) slot.appearanceLayers = new List<SkyPrisonAppearancePsbLayerNode>();
        slot.appearanceLayers.Clear();
        if (slot.dyeChannels == null) slot.dyeChannels = new List<SkyPrisonAppearanceDyeChannel>();
        slot.dyeChannels.Clear();
    }

    public void BindSelectedAppearanceLayerToSelectedNode()
    {
        SkyPrisonAppearancePsbLayerNode node = GetSelectedAppearanceLayerInCurrentSlot();
        SkyPrisonAnimationRigRow row = GetSelectedRigRow();
        if (node == null || row == null || node.isFolder || node.isDyeMask) return;
        node.bindTargetKey = row.key;
        node.bindTargetName = row.name;
        node.bindStartKey = row.key;
        node.bindEndKey = "";
        node.bindMode = "HardBind";
        node.bindConfidence = 1f;
        autoRecognizedAppearanceLayerManualTouch(node);
    }

    private void autoRecognizedAppearanceLayerManualTouch(SkyPrisonAppearancePsbLayerNode node)
    {
        if (node == null) return;
        node.autoRecognized = true;
        LastSelectedRigKey = node.bindTargetKey;
    }

    public bool ImportAppearancePsbAssetIntoSelectedSlot(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return false;

        // 允许两种来源：
        // 1) Unity Project 面板里的 Assets/... 资源
        // 2) 系统文件夹里的外部 PSD/PSB。外部文件会先复制到 AppearanceImports，再交给 AssetDatabase 读取。
        string unityPath = NormalizeOrImportAppearanceAssetPath(assetPath);
        if (string.IsNullOrWhiteSpace(unityPath)) return false;

        BuildMockAssemblyData();
        SkyPrisonAnimationAssemblySlot slot = CurrentAssemblySlot();
        if (slot == null) return false;

        slot.appearanceSourceAssetPath = unityPath;
        slot.appearancePackageKey = BuildAppearancePackageKeyFromPath(unityPath);
        slot.assetKey = string.IsNullOrWhiteSpace(slot.assetKey) || slot.assetKey.EndsWith("_None", StringComparison.OrdinalIgnoreCase) ? slot.appearancePackageKey : slot.assetKey;
        slot.appearanceLayers = BuildAppearancePsbLayerTree(unityPath);
        slot.selectedAppearanceLayerKey = "";
        EnsureDefaultDyeChannels(slot);
        AnalyzeAppearanceLayerRules(slot);
        SyncAppearanceSlotPreviewRows(slot);
        return slot.appearanceLayers != null && slot.appearanceLayers.Count > 0;
    }

    public string NormalizeUnityAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string p = path.Replace('\\', '/');
        int index = p.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
        if (index >= 0) return p.Substring(index + 1);
        if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return p;
        return "";
    }

    private string NormalizeOrImportAppearanceAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        string normalized = path.Replace('\\', '/');
        string unityPath = NormalizeUnityAssetPath(normalized);
        if (!string.IsNullOrWhiteSpace(unityPath))
            return unityPath;

        // OpenFilePanel / 系统文件拖拽会给出 C:/... 这种外部路径。
        // AssetDatabase 不能直接读取外部文件，所以这里自动复制进项目。
        if (!System.IO.File.Exists(normalized))
            return "";

        string lower = normalized.ToLowerInvariant();
        if (!lower.EndsWith(".psb") && !lower.EndsWith(".psd"))
            return "";

        const string importFolder = "Assets/_Project/Data/AnimationWorkbench/AppearanceImports";
        EnsureAssetFolder(importFolder);

        string fileName = System.IO.Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "AppearanceImport.psb";

        // 同名服装 PSB 再次置入时覆盖原资源，不生成 xxx 1/2/3。
        string targetPath = importFolder + "/" + fileName;
        string fullTargetPath = System.IO.Path.GetFullPath(targetPath);
        string fullTargetDir = System.IO.Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrWhiteSpace(fullTargetDir) && !System.IO.Directory.Exists(fullTargetDir))
            System.IO.Directory.CreateDirectory(fullTargetDir);

        try
        {
            System.IO.File.Copy(normalized, fullTargetPath, true);
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh();
            Debug.Log("[SkyPrisonAnimation] 已将外部衣物 PSD/PSB 复制到项目内：" + targetPath);
            return targetPath;
        }
        catch (Exception ex)
        {
            Debug.LogError("[SkyPrisonAnimation] 复制外部衣物 PSD/PSB 失败：" + normalized + "\n" + ex.Message);
            return "";
        }
    }

    private void EnsureAssetFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        string[] parts = folderPath.Split('/');
        if (parts.Length == 0) return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private string BuildAppearancePackageKeyFromPath(string unityPath)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(unityPath);
        if (string.IsNullOrWhiteSpace(file)) file = "appearance_part";
        return file.Replace(" ", "_");
    }

    private List<SkyPrisonAppearancePsbLayerNode> BuildAppearancePsbLayerTree(string assetPath)
    {
        List<SkyPrisonAppearancePsbLayerNode> result = new List<SkyPrisonAppearancePsbLayerNode>();
        int counter = 0;

        UnityEngine.GameObject go = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(assetPath);
        if (go != null)
        {
            // 有些 PSD Importer 版本会把图层作为 Root 的子物体；有些资源可能 Root 自身就是有效层。
            // 先读子节点，读不到时再把 Root 自身作为一个节点兜底。
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SkyPrisonAppearancePsbLayerNode node = BuildAppearanceNodeFromTransform(go.transform.GetChild(i), assetPath, "", 0, ref counter);
                if (node != null) result.Add(node);
            }

            if (result.Count == 0)
            {
                SkyPrisonAppearancePsbLayerNode root = BuildAppearanceNodeFromTransform(go.transform, assetPath, "", 0, ref counter);
                if (root != null) result.Add(root);
            }

            if (result.Count > 0) return result;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        int spriteCounter = 0;
        for (int i = 0; i < assets.Length; i++)
        {
            Sprite sp = assets[i] as Sprite;
            if (sp == null) continue;
            string name = sp.name;
            if (string.IsNullOrWhiteSpace(name)) name = "Layer_" + spriteCounter;
            string path = name.Replace('\\', '/');
            string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            AddAppearanceFlatPath(result, parts, sp.name, assetPath, ref spriteCounter);
        }

        if (result.Count > 0)
            return result;

        // 最后兜底：如果资源只被 Unity 识别成单张 Texture2D，也至少生成一个可见根层，
        // 避免拖入后 UI 看起来像完全没读取。之后用户仍可检查 Importer 设置。
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (tex != null)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            result.Add(new SkyPrisonAppearancePsbLayerNode
            {
                key = "ap_" + counter++,
                parentKey = "",
                name = string.IsNullOrWhiteSpace(name) ? tex.name : name,
                sourceLayerPath = string.IsNullOrWhiteSpace(name) ? tex.name : name,
                sourceSpriteName = tex.name,
                sourceAssetPath = assetPath,
                isFolder = false,
                depth = 0,
                expanded = true,
                visible = true
            });
        }

        return result;
    }

    private SkyPrisonAppearancePsbLayerNode BuildAppearanceNodeFromTransform(Transform tr, string assetPath, string parentPath, int depth, ref int counter)
    {
        if (tr == null) return null;
        SpriteRenderer sr = tr.GetComponent<SpriteRenderer>();
        bool isFolder = sr == null && tr.childCount > 0;
        string layerPath = string.IsNullOrEmpty(parentPath) ? tr.name : parentPath + "/" + tr.name;
        SkyPrisonAppearancePsbLayerNode node = new SkyPrisonAppearancePsbLayerNode
        {
            key = "ap_" + counter++,
            name = tr.name,
            sourceLayerPath = layerPath,
            sourceSpriteName = sr != null && sr.sprite != null ? sr.sprite.name : "",
            sourceAssetPath = assetPath,
            isFolder = isFolder,
            depth = depth,
            expanded = true,
            visible = true
        };
        for (int i = 0; i < tr.childCount; i++)
        {
            SkyPrisonAppearancePsbLayerNode child = BuildAppearanceNodeFromTransform(tr.GetChild(i), assetPath, layerPath, depth + 1, ref counter);
            if (child != null)
            {
                child.parentKey = node.key;
                node.children.Add(child);
            }
        }
        if (sr == null && node.children.Count == 0) node.isFolder = true;
        return node;
    }

    private void AddAppearanceFlatPath(List<SkyPrisonAppearancePsbLayerNode> roots, string[] parts, string spriteName, string assetPath, ref int counter)
    {
        if (parts == null || parts.Length == 0) return;
        List<SkyPrisonAppearancePsbLayerNode> list = roots;
        string parentKey = "";
        string running = "";
        for (int i = 0; i < parts.Length; i++)
        {
            bool leaf = i == parts.Length - 1;
            string part = parts[i];
            running = string.IsNullOrEmpty(running) ? part : running + "/" + part;
            SkyPrisonAppearancePsbLayerNode found = null;
            for (int j = 0; j < list.Count; j++) if (list[j].name == part) { found = list[j]; break; }
            if (found == null)
            {
                found = new SkyPrisonAppearancePsbLayerNode
                {
                    key = "ap_" + counter++,
                    parentKey = parentKey,
                    name = part,
                    sourceLayerPath = running,
                    sourceSpriteName = leaf ? spriteName : "",
                    sourceAssetPath = assetPath,
                    isFolder = !leaf,
                    depth = i,
                    expanded = true,
                    visible = true
                };
                list.Add(found);
            }
            if (leaf)
            {
                found.isFolder = false;
                found.sourceSpriteName = spriteName;
                found.sourceLayerPath = running;
            }
            parentKey = found.key;
            list = found.children;
        }
    }

    public void EnsureDefaultDyeChannels(SkyPrisonAnimationAssemblySlot slot)
    {
        if (slot == null) return;
        if (slot.dyeChannels == null) slot.dyeChannels = new List<SkyPrisonAppearanceDyeChannel>();
        if (slot.dyeChannels.Count > 0) return;
        slot.dyeChannels.Add(new SkyPrisonAppearanceDyeChannel { channelKey = "main", displayName = "主色", maskChannel = "R", enabled = true, previewColor = new Color(0.9f, 0.2f, 0.18f, 1f) });
        slot.dyeChannels.Add(new SkyPrisonAppearanceDyeChannel { channelKey = "sub", displayName = "副色", maskChannel = "G", enabled = true, previewColor = new Color(0.2f, 0.9f, 0.2f, 1f) });
        slot.dyeChannels.Add(new SkyPrisonAppearanceDyeChannel { channelKey = "accent", displayName = "强调色", maskChannel = "B", enabled = true, previewColor = new Color(0.15f, 0.35f, 1f, 1f) });
    }

    public void AnalyzeAppearanceLayerRules(SkyPrisonAnimationAssemblySlot slot)
    {
        if (slot == null || slot.appearanceLayers == null) return;
        List<SkyPrisonAppearancePsbLayerNode> all = new List<SkyPrisonAppearancePsbLayerNode>();
        CollectAppearanceNodes(slot.appearanceLayers, all);
        for (int i = 0; i < all.Count; i++) AnalyzeSingleAppearanceNode(all[i], slot);
        PairAppearanceDyeMasks(all);
        UpdateDyeChannelEnableFlagsFromMasks(slot, all);
    }
    public void SyncAllAppearanceSlotPreviewRows()
    {
        if (AssemblySlots == null) return;
        for (int i = 0; i < AssemblySlots.Count; i++)
            SyncAppearanceSlotPreviewRows(AssemblySlots[i]);
    }

    public void SyncAppearanceSlotPreviewRows(SkyPrisonAnimationAssemblySlot slot)
    {
        if (slot == null || PsbRows == null) return;

        string slotKey = string.IsNullOrWhiteSpace(slot.slotKey) ? slot.visualSlotKey : slot.slotKey;
        if (string.IsNullOrWhiteSpace(slotKey)) slotKey = "Appearance";

        // 同一槽位重新置入时，先移除旧的预览行，避免图层名后面出现 1/2/3。
        for (int i = PsbRows.Count - 1; i >= 0; i--)
        {
            SkyPrisonAnimationRigRow r = PsbRows[i];
            if (r != null && r.fromAppearanceSlot && string.Equals(r.appearanceSlotKey, slotKey, StringComparison.OrdinalIgnoreCase))
                PsbRows.RemoveAt(i);
        }

        if (!slot.visible || slot.appearanceLayers == null || slot.appearanceLayers.Count == 0)
            return;

        List<SkyPrisonAppearancePsbLayerNode> all = new List<SkyPrisonAppearancePsbLayerNode>();
        CollectAppearanceNodes(slot.appearanceLayers, all);
        int orderIndex = 0;
        for (int i = 0; i < all.Count; i++)
        {
            SkyPrisonAppearancePsbLayerNode n = all[i];
            if (n == null || n.isFolder || n.isDyeMask || !n.visible)
                continue;

            string rowKey = BuildAppearancePreviewRowKey(slotKey, n);
            SkyPrisonAnimationRigRow row = new SkyPrisonAnimationRigRow
            {
                key = rowKey,
                name = string.IsNullOrWhiteSpace(n.name) ? n.sourceSpriteName : n.name,
                semantic = GuessAppearancePreviewSemantic(n),
                depth = Mathf.Max(0, n.depth),
                parentKey = "",
                visible = true,
                mapped = true,
                hasKey = false,
                fromAppearanceSlot = true,
                appearanceSlotKey = slotKey,
                appearanceLayerKey = n.key,
                slotKey = slotKey,
                visualSlotKey = string.IsNullOrWhiteSpace(slot.visualSlotKey) ? slotKey : slot.visualSlotKey,
                sourceAssetPath = n.sourceAssetPath,
                // 有些 PSD Importer 版本里 Transform 名就是 Sprite 名；sourceSpriteName 为空时必须兜底，
                // 否则预览层存在但实际贴图找不到，看起来就像“柄部没被读取”。
                sourceSpriteName = string.IsNullOrWhiteSpace(n.sourceSpriteName) ? n.name : n.sourceSpriteName,
                sourceLayerPath = n.sourceLayerPath,
                boundRigKey = n.bindTargetKey,
                boundRigName = n.bindTargetName,
                bindMode = n.bindMode,
                bindConfidence = n.bindConfidence,
                usePsbLayerWeight = true,
                psbLayerWeight = BuildAppearancePreviewLayerWeight(slotKey, n, orderIndex),
                manualLayerWeightOffset = 0f,
                previewColor = GuessAppearancePreviewColor(n)
            };

            PsbRows.Add(row);
            orderIndex++;
        }
    }

    private string BuildAppearancePreviewRowKey(string slotKey, SkyPrisonAppearancePsbLayerNode node)
    {
        string raw = "appearance_" + slotKey + "_" + (node != null ? (string.IsNullOrWhiteSpace(node.sourceLayerPath) ? node.name : node.sourceLayerPath) : "layer");
        string safe = SafeAppearancePreviewKey(raw);
        return string.IsNullOrWhiteSpace(safe) ? "appearance_layer" : safe;
    }

    private string SafeAppearancePreviewKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        char[] cs = raw.Trim().ToCharArray();
        for (int i = 0; i < cs.Length; i++)
        {
            char c = cs[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) cs[i] = '_';
        }
        return new string(cs).Trim('_');
    }

    private string GuessAppearancePreviewSemantic(SkyPrisonAppearancePsbLayerNode node)
    {
        if (node == null) return "Outfit";
        string slot = (node.slotKey ?? "").ToLowerInvariant();
        string region = (node.bodyRegion ?? "").ToLowerInvariant();
        if (slot == "weapon" || region == "weapon") return "Weapon";
        if (slot == "hair") return "Hair";
        if (slot == "accessory") return "HeadAccessory";
        if (slot == "shoe" || slot == "sock") return "Foot";
        if (slot == "bottom") return "Hip";
        if (region.Contains("arm_l")) return "Wrist_L";
        if (region.Contains("arm_r")) return "Wrist_R";
        if (region.Contains("head")) return "Head";
        if (slot == "top") return "Outfit";
        return string.IsNullOrWhiteSpace(node.bindTargetKey) ? "Outfit" : node.bindTargetKey;
    }

    private float BuildAppearancePreviewLayerWeight(string slotKey, SkyPrisonAppearancePsbLayerNode node, int index)
    {
        // 这里只给同一语义层内部提供稳定权重；真正跨 PSB 的大前后关系由 PreviewPanel.GetPsbDrawOrder 兜底。
        // 但权重本身也按同一套“后 -> 前”排序，方便用户在 PSB 图层页看起来不乱。
        string path = ((node != null ? node.sourceLayerPath : "") + " " + (node != null ? node.name : "") + " " + (node != null ? node.sourceSpriteName : "")).ToLowerInvariant();
        string slot = ((node != null ? node.slotKey : "") + " " + (slotKey ?? "")).ToLowerInvariant();
        string region = (node != null ? node.bodyRegion : "").ToLowerInvariant();
        string sort = (node != null ? node.sortLayer : "").ToLowerInvariant();

        bool back = sort.Contains("behind") || path.Contains("_behind") || path.Contains("/behind/") || path.Contains("_back") || path.Contains("/back/") || path.Contains(" back ") || path.Contains("后") || path.Contains("後");
        bool front = sort.Contains("front") || path.Contains("_front") || path.Contains("/front/") || path.Contains(" front ") || path.Contains("前置") || path.Contains("前片");
        bool weapon = slot.Contains("weapon") || region.Contains("weapon") || path.Contains("weapon") || path.Contains("sword") || path.Contains("blade") || path.Contains("spade") || path.Contains("hilt") || path.Contains("handle") || path.Contains("shaft") || path.Contains("武器") || path.Contains("剣") || path.Contains("剑") || path.Contains("刀") || path.Contains("柄");
        bool handle = weapon && (path.Contains("handle") || path.Contains("grip") || path.Contains("hilt") || path.Contains("shaft") || path.Contains("柄") || back);
        bool weaponHead = weapon && !handle;
        bool headAcc = slot.Contains("accessory") || path.Contains("head_accessory") || path.Contains("hair_accessory") || path.Contains("头饰") || path.Contains("頭飾") || path.Contains("发饰") || path.Contains("髪飾") || path.Contains("帽");
        bool collar = path.Contains("collar") || path.Contains("neck") || path.Contains("领") || path.Contains("領") || path.Contains("襟");
        bool leftHand = region == "arm_l" || path.Contains("arm_l") || path.Contains("hand_l") || path.Contains("sleeve_l") || path.Contains("glove_l") || path.Contains("left");
        bool rightHand = region == "arm_r" || path.Contains("arm_r") || path.Contains("hand_r") || path.Contains("sleeve_r") || path.Contains("glove_r") || path.Contains("right");

        float baseWeight;
        if (path.Contains("hair_back") || path.Contains("back_hair") || path.Contains("braid") || path.Contains("ponytail") || path.Contains("后发") || path.Contains("後髪") || path.Contains("辫") || path.Contains("辮")) baseWeight = 0f;
        else if (back && !weapon) baseWeight = 8f;
        else if (slot.Contains("sock") || path.Contains("sock") || path.Contains("袜") || path.Contains("靴下")) baseWeight = 20f;
        else if (slot.Contains("shoe") || path.Contains("shoe") || path.Contains("boot") || path.Contains("鞋") || path.Contains("靴")) baseWeight = 24f;
        else if (slot.Contains("bottom") || path.Contains("bottom") || path.Contains("skirt") || path.Contains("pants") || path.Contains("下装") || path.Contains("下裝") || path.Contains("裙") || path.Contains("裤") || path.Contains("褲")) baseWeight = 30f;
        else if (rightHand) baseWeight = 42f;
        else if (slot.Contains("top") || path.Contains("jacket") || path.Contains("coat") || path.Contains("shirt") || path.Contains("上衣") || path.Contains("上半身")) baseWeight = 52f;
        // 武器必须优先于 leftHand 判定。
        // 之前 HeavySpade_front 的路径里带 hand_L，导致它被当成左手层 W=62，结果武器头被手压住。
        // back/柄部放在手附近但低于主手；front/刃头放到最高，保证武器头永远不会被手套压住。
        else if (weapon && handle) baseWeight = 60f;
        else if (leftHand) baseWeight = 62f;
        else if (collar) baseWeight = 72f;
        else if (slot.Contains("head") || path.Contains("head") || path.Contains("face") || path.Contains("头") || path.Contains("頭")) baseWeight = 82f;
        else if (headAcc) baseWeight = 92f;
        else if (weaponHead) baseWeight = 118f;
        else if (weapon) baseWeight = 118f;
        else baseWeight = 54f;

        if (front && !weapon) baseWeight += 3f;
        if (back && baseWeight > 8f && !weapon) baseWeight -= 3f;
        return baseWeight * 1000f + index;
    }

    private Color GuessAppearancePreviewColor(SkyPrisonAppearancePsbLayerNode node)
    {
        string slot = node != null ? (node.slotKey ?? "").ToLowerInvariant() : "";
        if (slot == "weapon") return new Color(0.95f, 0.76f, 0.30f, 1f);
        if (slot == "shoe" || slot == "sock") return new Color(0.72f, 0.42f, 0.92f, 1f);
        if (slot == "top" || slot == "bottom") return new Color(0.35f, 0.65f, 0.95f, 1f);
        if (slot == "accessory" || slot == "hair") return new Color(0.95f, 0.60f, 0.35f, 1f);
        return new Color(0.72f, 0.74f, 0.78f, 1f);
    }


    private void CollectAppearanceNodes(List<SkyPrisonAppearancePsbLayerNode> nodes, List<SkyPrisonAppearancePsbLayerNode> list)
    {
        if (nodes == null || list == null) return;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] == null) continue;
            list.Add(nodes[i]);
            CollectAppearanceNodes(nodes[i].children, list);
        }
    }

    private void AnalyzeSingleAppearanceNode(SkyPrisonAppearancePsbLayerNode node, SkyPrisonAnimationAssemblySlot slot)
    {
        if (node == null) return;
        string path = NormalizeLayerBindName((node.sourceLayerPath ?? "") + " " + (node.name ?? ""));
        node.bodyRegion = GuessAppearanceBodyRegion(path);
        node.slotKey = GuessAppearanceSlotKey(path, slot != null ? slot.slotKey : "");
        node.partType = GuessAppearancePartType(path, node.slotKey);
        node.side = GuessAppearanceSide(path);
        node.segment = GuessAppearanceSegment(path);
        node.sortLayer = GuessAppearanceSortLayer(path);
        node.isDyeMask = IsAppearanceDyeMaskName(path);
        if (node.isDyeMask)
        {
            node.bindMode = "Mask";
            node.sortLayer = "Mask";
            node.visible = false;
            node.autoRecognized = true;
            node.dyeChannelR = true;
            node.dyeChannelG = true;
            node.dyeChannelB = true;
            return;
        }
        if (node.isFolder)
        {
            node.bindMode = "Folder";
            return;
        }
        GuessAppearanceBinding(node, path);
        node.autoRecognized = true;
    }

    private string GuessAppearanceBodyRegion(string n)
    {
        // 武器路径里可能包含 hand_L / mask 这类 PSD 分组名，必须先判武器，不能先落到 arm_L。
        if (ContainsAny(n, "weapon", "spade", "sword", "blade", "hilt", "handle", "shaft", "武器", "剣", "剑", "刀", "柄")) return "weapon";
        if (ContainsAny(n, "arm_l", "left_arm", "sleeve_l", "jacket_l", "glove_l", "hand_l")) return "arm_L";
        if (ContainsAny(n, "arm_r", "right_arm", "sleeve_r", "jacket_r", "glove_r", "hand_r")) return "arm_R";
        if (ContainsAny(n, "leg_l", "left_leg", "sock_l", "shoe_l", "pants_l", "foot_l")) return "leg_L";
        if (ContainsAny(n, "leg_r", "right_leg", "sock_r", "shoe_r", "pants_r", "foot_r")) return "leg_R";
        if (ContainsAny(n, "head", "hair", "face")) return "head";
        return "body";
    }

    private string GuessAppearanceSlotKey(string n, string fallback)
    {
        // 武器优先。否则 weapon/xxx/hand_L/mask/HeavySpade_front 会被 hand/glove/top 等分组名污染。
        if (ContainsAny(n, "weapon", "spade", "sword", "blade", "hilt", "handle", "shaft", "武器", "剣", "剑", "刀", "柄")) return "weapon";
        if (ContainsAny(n, "/top/", " top ", "jacket", "shirt", "coat", "suit", "armor", "sleeve")) return "top";
        if (ContainsAny(n, "/bottom/", " bottom ", "skirt", "pants", "shorts")) return "bottom";
        if (ContainsAny(n, "/sock/", "sock", "socks", "stocking", "tights")) return "sock";
        if (ContainsAny(n, "/shoe/", "shoe", "boot", "heel")) return "shoe";
        if (ContainsAny(n, "/glove/", "glove")) return "glove";
        if (ContainsAny(n, "hair")) return "hair";
        if (ContainsAny(n, "accessory", "acc", "ribbon", "badge")) return "accessory";
        return string.IsNullOrWhiteSpace(fallback) ? "part" : fallback.ToLowerInvariant();
    }

    private string GuessAppearancePartType(string n, string slot)
    {
        if (ContainsAny(n, "weapon", "spade", "sword", "blade", "hilt", "handle", "shaft", "武器", "剣", "剑", "刀", "柄")) return "weapon";
        if (ContainsAny(n, "jacket")) return "jacket";
        if (ContainsAny(n, "skirt")) return "skirt";
        if (ContainsAny(n, "pants")) return "pants";
        if (ContainsAny(n, "sock", "stocking", "tights")) return "sock";
        if (ContainsAny(n, "shoe", "boot", "heel")) return "shoe";
        if (ContainsAny(n, "sleeve")) return "sleeve";
        if (ContainsAny(n, "glove")) return "glove";
        if (ContainsAny(n, "hair")) return "hair";
        return slot;
    }

    private string GuessAppearanceSide(string n)
    {
        if (ContainsAny(n, "_l_", "_l/", "/l_", "_left", "left_", "arm_l", "leg_l", "shoe_l", "sock_l")) return "L";
        if (ContainsAny(n, "_r_", "_r/", "/r_", "_right", "right_", "arm_r", "leg_r", "shoe_r", "sock_r")) return "R";
        return "Center";
    }

    private string GuessAppearanceSegment(string n)
    {
        if (ContainsAny(n, "upper", "上段", "上部")) return "upper";
        if (ContainsAny(n, "lower", "下段", "下部")) return "lower";
        if (ContainsAny(n, "foot", "toe", "脚", "足")) return "foot";
        if (ContainsAny(n, "hand", "wrist", "手", "腕")) return "hand";
        if (ContainsAny(n, "collar", "neck", "领", "襟")) return "collar";
        if (ContainsAny(n, "hem", "skirt", "裙", "摆")) return "hem";
        return "";
    }

    private string GuessAppearanceSortLayer(string n)
    {
        if (ContainsAny(n, "_behind", "_back", "behind", " back ")) return "BehindBody";
        if (ContainsAny(n, "_front", "front")) return "FrontBody";
        return "Normal";
    }

    private bool IsAppearanceDyeMaskName(string n)
    {
        return ContainsAny(n, "dyemask", "dye_mask", "dye-mask", "_mask") && ContainsAny(n, "mask");
    }

    private void GuessAppearanceBinding(SkyPrisonAppearancePsbLayerNode node, string n)
    {
        string sideSuffix = node.side == "L" ? "_L" : (node.side == "R" ? "_R" : "");
        if (node.slotKey == "weapon" || node.partType == "weapon")
        {
            node.bindMode = "HardBind"; node.bindTargetKey = "Weapon"; node.bindTargetName = "Weapon Socket"; node.bindConfidence = 0.72f; return;
        }
        if (node.slotKey == "shoe" || node.partType == "shoe")
        {
            node.bindMode = "HardBind"; node.bindTargetKey = string.IsNullOrEmpty(sideSuffix) ? "Foot_R" : "Foot" + sideSuffix; node.bindTargetName = node.bindTargetKey; node.bindConfidence = 0.88f; return;
        }
        if (node.bodyRegion == "arm_L" || node.bodyRegion == "arm_R")
        {
            if (node.segment == "upper") { node.bindMode = "TwoPointBind"; node.bindStartKey = "Shoulder" + sideSuffix; node.bindEndKey = "Elbow" + sideSuffix; node.bindTargetKey = node.bindStartKey; node.bindTargetName = node.bindStartKey + " → " + node.bindEndKey; node.bindConfidence = 0.9f; return; }
            if (node.segment == "lower") { node.bindMode = "TwoPointBind"; node.bindStartKey = "Elbow" + sideSuffix; node.bindEndKey = "Wrist" + sideSuffix; node.bindTargetKey = node.bindStartKey; node.bindTargetName = node.bindStartKey + " → " + node.bindEndKey; node.bindConfidence = 0.9f; return; }
            if (node.segment == "hand") { node.bindMode = "HardBind"; node.bindTargetKey = "Wrist" + sideSuffix; node.bindTargetName = node.bindTargetKey; node.bindConfidence = 0.86f; return; }
        }
        if (node.bodyRegion == "leg_L" || node.bodyRegion == "leg_R")
        {
            if (node.segment == "upper") { node.bindMode = "TwoPointBind"; node.bindStartKey = "Hip" + sideSuffix; node.bindEndKey = "Knee" + sideSuffix; node.bindTargetKey = node.bindStartKey; node.bindTargetName = node.bindStartKey + " → " + node.bindEndKey; node.bindConfidence = 0.88f; return; }
            if (node.segment == "lower") { node.bindMode = "TwoPointBind"; node.bindStartKey = "Knee" + sideSuffix; node.bindEndKey = "Ankle" + sideSuffix; node.bindTargetKey = node.bindStartKey; node.bindTargetName = node.bindStartKey + " → " + node.bindEndKey; node.bindConfidence = 0.88f; return; }
            if (node.segment == "foot") { node.bindMode = node.slotKey == "shoe" ? "HardBind" : "TwoPointBind"; node.bindStartKey = "Ankle" + sideSuffix; node.bindEndKey = "Foot" + sideSuffix; node.bindTargetKey = node.bindStartKey; node.bindTargetName = node.bindStartKey + " → " + node.bindEndKey; node.bindConfidence = 0.84f; return; }
        }
        if (node.slotKey == "bottom" || node.partType == "skirt")
        {
            node.bindMode = "SurfaceBind"; node.bindStartKey = "Spine"; node.bindEndKey = "Pelvis"; node.bindTargetKey = "Spine"; node.bindTargetName = "腰/骨盆曲面"; node.bindConfidence = 0.82f; return;
        }
        if (node.slotKey == "top" || node.partType == "jacket")
        {
            node.bindMode = "SurfaceBind"; node.bindStartKey = "Chest"; node.bindEndKey = "Spine"; node.bindTargetKey = "Chest"; node.bindTargetName = "胸腔/腰曲面"; node.bindConfidence = 0.84f; return;
        }
        node.bindMode = "HardBind"; node.bindTargetKey = "Root"; node.bindTargetName = "Root"; node.bindConfidence = 0.35f;
    }

    private void PairAppearanceDyeMasks(List<SkyPrisonAppearancePsbLayerNode> all)
    {
        if (all == null) return;
        Dictionary<string, SkyPrisonAppearancePsbLayerNode> byPath = new Dictionary<string, SkyPrisonAppearancePsbLayerNode>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < all.Count; i++)
        {
            SkyPrisonAppearancePsbLayerNode n = all[i];
            if (n == null || n.isFolder || n.isDyeMask) continue;
            string path = NormalizeAppearanceMaskPairPath(n.sourceLayerPath);
            if (!byPath.ContainsKey(path)) byPath.Add(path, n);
            if (!byPath.ContainsKey(n.name)) byPath.Add(n.name, n);
        }
        for (int i = 0; i < all.Count; i++)
        {
            SkyPrisonAppearancePsbLayerNode m = all[i];
            if (m == null || !m.isDyeMask) continue;
            string basePath = NormalizeAppearanceMaskPairPath(RemoveDyeMaskSuffix(m.sourceLayerPath));
            SkyPrisonAppearancePsbLayerNode target = null;
            if (!byPath.TryGetValue(basePath, out target))
                byPath.TryGetValue(RemoveDyeMaskSuffix(m.name), out target);
            if (target != null)
            {
                m.dyeMaskForLayerKey = target.key;
                target.dyeMaskLayerKey = m.key;
                target.hasDyeMask = true;
                target.dyeChannelR = m.dyeChannelR;
                target.dyeChannelG = m.dyeChannelG;
                target.dyeChannelB = m.dyeChannelB;
                target.dyeChannelA = m.dyeChannelA;
            }
        }
    }

    private string NormalizeAppearanceMaskPairPath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        string s = p.Replace('\\', '/');
        s = s.Replace("/mask/", "/");
        s = s.Replace("/MASK/", "/");
        return s.Trim('/');
    }

    private string RemoveDyeMaskSuffix(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        string r = s;
        string[] suffixes = { "_dyeMask", "_dyemask", "_dye_mask", "@dyeMask", "@dyemask" };
        for (int i = 0; i < suffixes.Length; i++)
        {
            int idx = r.IndexOf(suffixes[i], StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) { r = r.Substring(0, idx); break; }
        }
        return r;
    }


    private bool TryScanDyeMaskChannels(SkyPrisonAppearancePsbLayerNode node)
    {
        if (node == null || string.IsNullOrWhiteSpace(node.sourceAssetPath)) return false;
        try
        {
            Sprite sp = null;
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(node.sourceAssetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                Sprite candidate = assets[i] as Sprite;
                if (candidate == null) continue;
                if (candidate.name == node.sourceSpriteName || candidate.name == node.name)
                {
                    sp = candidate;
                    break;
                }
            }
            if (sp == null || sp.texture == null) return false;
            Rect r = sp.textureRect;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(r.x), 0, sp.texture.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(r.y), 0, sp.texture.height - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(r.xMax), x0 + 1, sp.texture.width);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(r.yMax), y0 + 1, sp.texture.height);
            int stepX = Mathf.Max(1, (x1 - x0) / 64);
            int stepY = Mathf.Max(1, (y1 - y0) / 64);
            bool hasAny = false;
            node.dyeChannelR = node.dyeChannelG = node.dyeChannelB = node.dyeChannelA = false;
            for (int y = y0; y < y1; y += stepY)
            {
                for (int x = x0; x < x1; x += stepX)
                {
                    Color c = sp.texture.GetPixel(x, y);
                    if (c.a <= 0.02f) continue;
                    hasAny = true;
                    if (c.r > 0.20f && c.r >= c.g && c.r >= c.b) node.dyeChannelR = true;
                    if (c.g > 0.20f && c.g >= c.r && c.g >= c.b) node.dyeChannelG = true;
                    if (c.b > 0.20f && c.b >= c.r && c.b >= c.g) node.dyeChannelB = true;
                    if (c.a > 0.02f && c.a < 0.98f) node.dyeChannelA = true;
                }
            }
            return hasAny;
        }
        catch
        {
            // PSD/PSB 导入贴图不可读时保持安全默认：R/G/B 都可用，用户可在 UI 里关闭未使用通道。
            return false;
        }
    }

    private void UpdateDyeChannelEnableFlagsFromMasks(SkyPrisonAnimationAssemblySlot slot, List<SkyPrisonAppearancePsbLayerNode> all)
    {
        if (slot == null) return;
        if (slot.dyeChannels == null) slot.dyeChannels = new List<SkyPrisonAppearanceDyeChannel>();

        Dictionary<string, SkyPrisonAppearanceDyeChannel> channels = new Dictionary<string, SkyPrisonAppearanceDyeChannel>(StringComparer.OrdinalIgnoreCase);
        if (all != null)
        {
            Dictionary<string, SkyPrisonAppearancePsbLayerNode> byKey = new Dictionary<string, SkyPrisonAppearancePsbLayerNode>();
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && !string.IsNullOrEmpty(all[i].key) && !byKey.ContainsKey(all[i].key)) byKey.Add(all[i].key, all[i]);

            for (int i = 0; i < all.Count; i++)
            {
                SkyPrisonAppearancePsbLayerNode mask = all[i];
                if (mask == null || !mask.isDyeMask || string.IsNullOrEmpty(mask.dyeMaskForLayerKey)) continue;
                SkyPrisonAppearancePsbLayerNode target = null;
                byKey.TryGetValue(mask.dyeMaskForLayerKey, out target);
                string scope = target != null && !string.IsNullOrWhiteSpace(target.slotKey) ? target.slotKey : "part";
                if (mask.dyeChannelR) AddAppearanceDyeChannel(channels, scope, "main", "R");
                if (mask.dyeChannelG) AddAppearanceDyeChannel(channels, scope, "sub", "G");
                if (mask.dyeChannelB) AddAppearanceDyeChannel(channels, scope, "accent", "B");
            }
        }

        slot.dyeChannels.Clear();
        if (channels.Count == 0)
        {
            EnsureDefaultDyeChannels(slot);
            for (int i = 0; i < slot.dyeChannels.Count; i++) slot.dyeChannels[i].enabled = false;
            return;
        }

        List<SkyPrisonAppearanceDyeChannel> sorted = new List<SkyPrisonAppearanceDyeChannel>(channels.Values);
        sorted.Sort((a, b) => string.Compare(a.channelKey, b.channelKey, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < sorted.Count; i++) slot.dyeChannels.Add(sorted[i]);
    }

    private void AddAppearanceDyeChannel(Dictionary<string, SkyPrisonAppearanceDyeChannel> channels, string scope, string key, string maskChannel)
    {
        if (channels == null) return;
        if (string.IsNullOrWhiteSpace(scope)) scope = "part";
        string fullKey = scope + "." + key;
        if (channels.ContainsKey(fullKey)) return;
        channels.Add(fullKey, new SkyPrisonAppearanceDyeChannel
        {
            channelKey = fullKey,
            scopeKey = scope,
            displayName = BuildAppearanceDyeChannelDisplayName(scope, key),
            maskChannel = maskChannel,
            enabled = true,
            previewColor = maskChannel == "R" ? new Color(0.9f, 0.2f, 0.18f, 1f) : (maskChannel == "G" ? new Color(0.2f, 0.9f, 0.2f, 1f) : new Color(0.15f, 0.35f, 1f, 1f))
        });
    }

    private string BuildAppearanceDyeChannelDisplayName(string scope, string key)
    {
        string scopeName;
        switch ((scope ?? "").ToLowerInvariant())
        {
            case "top": scopeName = "上装"; break;
            case "bottom": scopeName = "下装"; break;
            case "sock": scopeName = "袜子"; break;
            case "shoe": scopeName = "鞋子"; break;
            case "glove": scopeName = "手套"; break;
            case "weapon": scopeName = "武器"; break;
            case "hair": scopeName = "头发"; break;
            default: scopeName = "部件"; break;
        }
        string keyName;
        switch ((key ?? "").ToLowerInvariant())
        {
            case "main": keyName = "主色"; break;
            case "sub": keyName = "副色"; break;
            case "accent": keyName = "强调色"; break;
            default: keyName = key; break;
        }
        return scopeName + keyName;
    }

    public void BuildMockAssemblyData(){ if(IsCustomPurePsbMode){ AssemblySlots.Clear(); return; } if(AssemblySlots.Count>0)return; string[,] s={{"BaseBody","基础身体","Axia_BaseBody","Root","Body"},{"Head","头部","Head_Default","Head","Head"},{"Hair","发型","Hair_Default","Head","Hair"},{"Top","上衣","Outfit_None","Chest","Outfit"},{"Hand","手部","Hand_None","Wrist_L / Wrist_R","Hand"},{"Pants","裤子","Pants_None","Spine / Hip_L / Hip_R","Pants"},{"Socks","袜子","Socks_None","Ankle_L / Ankle_R","Socks"},{"Shoes","鞋子","Shoes_None","Foot_L / Foot_R","Shoes"},{"Accessory","饰品","Accessory_None","Head","Accessory"},{"Weapon","武器","Weapon_None","Wrist_R","Weapon"}}; for(int i=0;i<s.GetLength(0);i++)AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot{slotKey=s[i,0],displayName=s[i,1],assetKey=s[i,2],boundPartKey=s[i,3],visualSlotKey=s[i,4]}); }
    public void BuildMockData()
    {
        ManualRigTemplateMode=false;
        CurrentRigTemplateKey="Human";
        EnsureDefaultPhysicsPresets();
        Actions.Clear();
        ActionGroups.Clear();
        ActionGroups.Add(new SkyPrisonAnimationActionGroupRow{key="Base",name="基础",expanded=true});
        ActionGroups.Add(new SkyPrisonAnimationActionGroupRow{key="Move",name="移动",expanded=true});
        ActionGroups.Add(new SkyPrisonAnimationActionGroupRow{key="Jump",name="跳跃",expanded=true});
        ActionGroups.Add(new SkyPrisonAnimationActionGroupRow{key="Attack",name="攻击",expanded=true});
        ActionGroups.Add(new SkyPrisonAnimationActionGroupRow{key="Hit",name="受击",expanded=true});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Idle",name="待机",type="关键帧",status="空轨",loop=true,duration=1.2f,groupKey="Base"});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Move",name="移动",type="关键帧",status="空轨",loop=true,duration=1.2f,groupKey="Move"});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Run",name="奔跑",type="关键帧",status="空轨",loop=true,duration=.8f,groupKey="Move"});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Jump_Start",name="跳跃_起跳",type="关键帧",status="空轨",loop=false,duration=.12f,groupKey="Jump"});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Jump_Rise",name="跳跃_上升",type="关键帧",status="空轨",loop=true,duration=.24f,groupKey="Jump"});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Jump_Fall",name="跳跃_下落",type="关键帧",status="空轨",loop=true,duration=.24f,groupKey="Jump"});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Jump_Land",name="跳跃_落地",type="关键帧",status="空轨",loop=false,duration=.12f,groupKey="Jump"});
        Actions.Add(new SkyPrisonAnimationActionRow{key="Attack_01",name="普通攻击",type="关键帧",status="空轨",loop=false,duration=1.0f,groupKey="Attack"});

        RigRows.Clear();
        AddHumanoidRigV1Rows();

        if(PsbRows.Count==0)
            PsbRows.AddRange(CloneRows(RigRows));

        SocketRows.Clear();
        SocketRows.Add(new SkyPrisonAnimationRigRow { key = "FootSocket_L", name = "左脚步点", semantic = "Footstep / Left", depth = 0, hasKey = true });
        SocketRows.Add(new SkyPrisonAnimationRigRow { key = "FootSocket_R", name = "右脚步点", semantic = "Footstep / Right", depth = 0, hasKey = true });
        SocketRows.Add(new SkyPrisonAnimationRigRow { key = "HitboxAnchor", name = "攻击判定锚点", semantic = "Hitbox", depth = 0, hasKey = true });

        BuildMockAssemblyData();
    }
    void AddRig(string key,string name,string semantic,int depth,string parent,bool folder,int icon){ RigRows.Add(new SkyPrisonAnimationRigRow{key=key,name=name,semantic=semantic,depth=depth,parentKey=parent,isFolder=folder,previewIconNumber=icon,expanded=true,hasKey=true}); }
}

public static class SkyPrisonAnimationWorkbenchStyle {
    public static readonly Color Bg=new Color(.12f,.12f,.13f,1f), PanelBg=new Color(.16f,.16f,.17f,1f), PanelDeepBg=new Color(.10f,.10f,.11f,1f), LineColor=new Color(1f,1f,1f,.10f), AccentBlue=new Color(.35f,.68f,1f,1f), AccentGreen=new Color(.45f,.90f,.60f,1f), AccentYellow=new Color(1f,.78f,.32f,1f), AccentPurple=new Color(.86f,.48f,1f,1f), SelectedBg=new Color(.25f,.38f,.52f,.85f);
    static readonly Dictionary<int,Texture2D> IconCache=new Dictionary<int,Texture2D>();
    public static Texture2D LoadEditorIcon(int n){ if(IconCache.TryGetValue(n,out var c))return c; var t=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Icon/Editor/SkyPrisonEditor_"+n+".png"); IconCache[n]=t; return t; }
    public static void DrawGrid(Rect r,float cell,Color color){ Handles.BeginGUI(); Handles.color=color; for(float x=r.x;x<=r.xMax;x+=cell)Handles.DrawLine(new Vector3(x,r.y),new Vector3(x,r.yMax)); for(float y=r.y;y<=r.yMax;y+=cell)Handles.DrawLine(new Vector3(r.x,y),new Vector3(r.xMax,y)); Handles.EndGUI(); }
    public static void DrawRectBorder(Rect r,Color c){ EditorGUI.DrawRect(new Rect(r.x,r.y,r.width,1f),c); EditorGUI.DrawRect(new Rect(r.x,r.yMax-1f,r.width,1f),c); EditorGUI.DrawRect(new Rect(r.x,r.y,1f,r.height),c); EditorGUI.DrawRect(new Rect(r.xMax-1f,r.y,1f,r.height),c); }
    public static void DrawLine(Vector2 a,Vector2 b,Color c,float w){ Handles.BeginGUI(); Handles.color=c; Handles.DrawAAPolyLine(w,new Vector3(a.x,a.y,0),new Vector3(b.x,b.y,0)); Handles.EndGUI(); }
}
