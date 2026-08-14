using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 找到真正的根因：Spine的SkeletonRenderer.LateUpdate()带[DefaultExecutionOrder(1)]，
// 这个类之前没有显式execution order(默认0)，比Spine的网格更新先跑——每帧读到的
// 渲染网格localBounds是上一帧、甚至偶尔是完全没生成过的空网格(实测抓到过
// lb.center直接是(0,0,0)的帧)，导致算出来的高度不稳定，名字位置跟着乱跳。
// 显式设一个比Spine(1)大得多的执行顺序，保证这个脚本的LateUpdate永远排在Spine
// 的网格更新完成之后执行，读到的才是这一帧真正定稿的网格数据。
[DefaultExecutionOrder(100)]
[ExecuteAlways]
public class UnitOverheadUIView : MonoBehaviour
{
    public UnitDefinition unitDefinition;
    public Component runtimeBinder;
    public bool autoResolveUnitDefinition = true;
    public bool autoEnsureStructure = true;
    public bool applyOnStart = true;
    public bool applyOnValidate = true;

    [Header("Layout Safety")]
    [Tooltip("开启后，运行时只刷新血量/状态图标/贴图，不改 UI 编辑器里已经摆好的 RectTransform 坐标。") ]
    public bool preserveAuthoredLayout = false;

    public Transform overheadAnchor;
    public RectTransform runtimeUiRoot;
    public RectTransform nameRoot;
    public TextMeshProUGUI nameText;
    public RectTransform slotRoot01;
    public RectTransform slotView01;
    public RectTransform bgLayerRoot;
    public RectTransform statusRoot;
    public RectTransform statusAreaRoot;
    public RectTransform statusContentRoot;
    public RectTransform damageNumberRoot;
    public RectTransform damageNumberContentRoot;
    public RectTransform damageRefMaskRoot;
    public RawImage damageReferenceImage;
    public RectTransform fillMaskRoot;
    public RawImage fillImage;
    public TextMeshProUGUI bracketLeftText;
    public TextMeshProUGUI bracketRightText;

    public bool debugLogs = false;

    [Header("Billboard")]
    public bool faceCameraEveryFrame = true;
    public Camera targetCamera;

    private readonly List<RawImage> runtimeBgImages = new List<RawImage>();
    private readonly List<UnitStatusIconView> runtimeStatusIcons = new List<UnitStatusIconView>();
    private readonly List<DamageNumberView> runtimeDamageNumbers = new List<DamageNumberView>();
    private readonly List<StatusViewData> cachedStatuses = new List<StatusViewData>();
    private readonly List<StatusViewData> visibleStatusBuffer = new List<StatusViewData>();
    private readonly List<RectTransform> activeStatusRectBuffer = new List<RectTransform>();

    [Header("Status Icon Runtime Safety")]
    [Tooltip("状态图标池上限。防止异常状态列表或重复刷新把 UnitStatusIconView 无限堆起来。") ]
    [SerializeField] private int maxRuntimeStatusIconPool = 24;
    [Tooltip("运行时刷新状态时，清理旧版本遗留/未登记的状态图标子节点。") ]
    [SerializeField] private bool compactStrayStatusIconsOnRefresh = true;

    private OverheadBarStyleAsset appliedStyle;
    private StatusDisplayDefinition cachedStatusDisplayDefinition;

    private float targetPercent = 1f;
    private float currentPercent = 1f;
    private float damageReferencePercent = 1f;
    private float damageReferenceHoldTimer = 0f;
    private float currentDisplayAlpha = 1f;
    private float targetDisplayAlpha = 1f;
    private CanvasGroup slotCanvasGroup;

    // 名字淡入淡出——NPC对话系统("靠近淡入、远离淡出")用，跟血条那套
    // current/targetDisplayAlpha 是完全对称的一份独立淡出状态，互不干扰
    // (名字和血条各自的显隐节奏本来就不一样，不能共用同一组alpha)。
    private CanvasGroup nameCanvasGroup;
    private float nameCurrentAlpha = 0f;
    private float nameTargetAlpha = 0f;
    private bool  nameProximityFadeMode = false;
    private const float NameFadeSpeed = 6f;

    // 视距淡出——整个头顶UI(名字+血条+括号+状态图标)离玩家太远整体一起淡出，
    // 跟上面"名字单独淡入淡出"是两回事(那个只管名字、只在NPC对话系统里手动开)，
    // 这个管的是整块UI、只看跟玩家的距离，自己在LateUpdate里算，不用外部驱动。
    private CanvasGroup rangeCanvasGroup;
    private float rangeCurrentAlpha = 1f;

    private const string RuntimeRootName = "UnitOverheadUIRuntimeRoot";
    private const int OverheadUiLayer = 19;

    private static Shader alwaysOnTopUiShader;
    private static Material alwaysOnTopUiMaterial;

    /// <summary>血条这些Graphic默认材质会正常参与3D深度测试，站在墙/集装箱后面
    /// 就被挡住了——跟角色描边遮挡穿透用同一个思路，换成ZTest Always的专用材质，
    /// 不管前面有没有不透明物体都画在最上层。</summary>
    /// <summary>血条两端的"[ ]"装饰括号——固定贴在SlotView_01左右外侧边缘，
    /// 不参与血量填充遮罩(不是fillMaskRoot/damageRefMaskRoot的子物体)，纯装饰。</summary>
    private static void ConfigureBracketRect(TextMeshProUGUI text, TextAlignmentOptions alignment, Vector2 anchor, Vector2 anchoredOffset)
    {
        if (text == null)
            return;

        text.alignment = alignment;
        text.fontSize = 11f;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(alignment == TextAlignmentOptions.Right ? 1f : 0f, 0.5f);
        rect.anchoredPosition = anchoredOffset;
        rect.sizeDelta = new Vector2(16f, 24f);
    }

    /// <summary>
    /// 给 Image / RawImage 这类 Graphic 套上头顶 UI 的「永远画在最上层」材质。
    /// 注意不能拿 TMP 的 materialForRendering 代替——那是字体材质，采样的是字体图集，
    /// 套到 Image 上着色器根本不读 sprite，只会填出一个实心方块。
    /// 文字要用的是下面那个 ApplyAlwaysOnTopFontMaterial。
    /// </summary>
    public static void ApplyAlwaysOnTopMaterial(Graphic graphic)
    {
        if (graphic == null)
            return;

        if (alwaysOnTopUiMaterial == null)
        {
            if (alwaysOnTopUiShader == null)
                alwaysOnTopUiShader = Shader.Find("SkyPrison/UI/AlwaysOnTop");

            if (alwaysOnTopUiShader == null)
                return;

            alwaysOnTopUiMaterial = new Material(alwaysOnTopUiShader) { name = "M_UnitOverheadUI_AlwaysOnTop" };
        }

        graphic.material = alwaysOnTopUiMaterial;
    }


    private bool isInitializing;

    private void Awake() { SafeInitialize(); }
    private void Start() { SafeInitialize(); }

    /// <summary>在Inspector里右键这个组件 -> "Debug Dump State"，Console会打印一份
    /// 当前实际运行状态——名字/血条到底卡在哪一步(没绑定UnitDefinition？Canvas没启用？
    /// 相机没找到？alpha是0？角色身份不是Ally？)不用再靠猜。</summary>
    [ContextMenu("Debug Dump State")]
    public void DebugDumpState()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        Vector3? screenPoint = null;
        if (cam != null && overheadAnchor != null)
            screenPoint = cam.WorldToScreenPoint(overheadAnchor.position);

        Transform spineRootDbg = transform.Find("VisualRoot/SpineRoot");
        Transform searchRootDbg = spineRootDbg != null ? spineRootDbg : transform;
        Renderer[] renderersDbg = searchRootDbg.GetComponentsInChildren<Renderer>(true);
        int nonProxyRendererCount = 0;
        foreach (var r in renderersDbg)
            if (r != null && !r.transform.name.Contains("OutlineProxy")) nonProxyRendererCount++;

        string report =
            "==== UnitOverheadUIView Debug Dump ====\n" +
            $"GameObject: {gameObject.name} (active={gameObject.activeInHierarchy})\n" +
            $"unitDefinition: {(unitDefinition != null ? unitDefinition.name : "null")}" +
                (unitDefinition != null ? $" | characterIdentity={unitDefinition.characterIdentity} | autoOverheadNameVisibility={unitDefinition.autoOverheadNameVisibility} | manualShowOverheadName={unitDefinition.manualShowOverheadName} | overheadHpBarStyle={(unitDefinition.overheadHpBarStyle != null ? unitDefinition.overheadHpBarStyle.name : "null")}" : "") + "\n" +
            $"[高度诊断] VisualRoot/SpineRoot找到={spineRootDbg != null} | searchRoot={searchRootDbg.name} | 找到的Renderer数(排除描边代理)={nonProxyRendererCount} | enabled={enabled} gameObject.activeInHierarchy={gameObject.activeInHierarchy} faceCameraEveryFrame={faceCameraEveryFrame}\n" +
            $"overheadAnchor: {(overheadAnchor != null ? $"local={overheadAnchor.localPosition:F3} world={overheadAnchor.position:F3} parent={(overheadAnchor.parent != null ? overheadAnchor.parent.name : "null")} localScale={overheadAnchor.localScale:F3}" : "null")}\n" +
            $"runtimeUiRoot: {(runtimeUiRoot != null ? $"active={runtimeUiRoot.gameObject.activeSelf} position={runtimeUiRoot.position:F1} localScale={runtimeUiRoot.localScale:F3}" : "null")}\n" +
            $"Canvas: {(runtimeUiRoot != null && runtimeUiRoot.GetComponent<Canvas>() != null ? $"renderMode={runtimeUiRoot.GetComponent<Canvas>().renderMode} enabled={runtimeUiRoot.GetComponent<Canvas>().enabled} sortingOrder={runtimeUiRoot.GetComponent<Canvas>().sortingOrder}" : "missing")}\n" +
            $"camera used: {(cam != null ? cam.name : "null (Camera.main 没找到摄像机！)")} | screenPoint={(screenPoint.HasValue ? screenPoint.Value.ToString("F1") : "n/a")} (z<0代表在相机背后，会被隐藏)\n" +
            $"nameRoot: {(nameRoot != null ? $"active={nameRoot.gameObject.activeSelf} anchoredPosition={nameRoot.anchoredPosition:F1} sizeDelta={nameRoot.sizeDelta:F1} localScale={nameRoot.localScale:F3} worldPos={nameRoot.position:F2}" : "null")} | nameText: {(nameText != null ? $"\"{nameText.text}\" fontSize={nameText.fontSize} enableAutoSizing={nameText.enableAutoSizing} color={nameText.color} font={(nameText.font != null ? nameText.font.name : "null")}" : "null")}\n" +
            $"preserveAuthoredLayout={preserveAuthoredLayout} autoEnsureStructure={autoEnsureStructure} applyOnStart={applyOnStart}\n" +
            $"appliedStyle nameFontSize(live asset)={(appliedStyle != null ? appliedStyle.nameFontSize.ToString() : "n/a")} nameOffset(live asset)={(appliedStyle != null ? appliedStyle.nameOffset.ToString() : "n/a")} barSize(live asset)={(appliedStyle != null ? appliedStyle.barSize.ToString() : "n/a")} hpBarOffset(live asset)={(appliedStyle != null ? appliedStyle.hpBarOffset.ToString() : "n/a")}\n" +
            $"nameProximityFadeMode={nameProximityFadeMode} nameCurrentAlpha={nameCurrentAlpha:F2} nameTargetAlpha={nameTargetAlpha:F2}\n" +
            $"slotRoot01: {(slotRoot01 != null ? $"active={slotRoot01.gameObject.activeSelf} anchoredPosition={slotRoot01.anchoredPosition:F2}" : "null")} | slotView01.sizeDelta={(slotView01 != null ? slotView01.sizeDelta.ToString("F2") : "null")} | slotCanvasGroup.alpha={(slotCanvasGroup != null ? slotCanvasGroup.alpha.ToString("F2") : "null")}\n" +
            $"currentDisplayAlpha={currentDisplayAlpha:F2} targetDisplayAlpha={targetDisplayAlpha:F2} (血条本身的显隐——满血且hideWhenFull=true时会故意隐藏)\n" +
            $"currentPercent={currentPercent:F3} targetPercent={targetPercent:F3} (血量填充比例——如果扣血了这两个数没变，说明SetHp根本没被调用)\n" +
            $"fillMaskRoot.sizeDelta={(fillMaskRoot != null ? fillMaskRoot.sizeDelta.ToString("F1") : "null")} slotView01.sizeDelta={(slotView01 != null ? slotView01.sizeDelta.ToString("F1") : "null")}\n" +
            $"appliedStyle: {(appliedStyle != null ? appliedStyle.name : "null")}\n" +
            $"fillImage: {(fillImage != null ? $"texture={(fillImage.texture != null ? fillImage.texture.name : "null")} color={fillImage.color}" : "null")}";

        Debug.Log(report, this);
    }

    private void OnEnable()
    {
        OverheadBarStyleAsset.OnStyleChanged += HandleStyleChanged;
        // 见HandleBeforeCameraRenders的注释——朝向摄像机这一步必须挂在渲染管线的
        // 逐相机回调上，不能留在LateUpdate里。注意：Camera.onPreCull是内置渲染管线
        // 的回调，在URP/SRP下永远不会被触发(已用日志实测确认)，必须用
        // RenderPipelineManager.beginCameraRendering。
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += HandleBeforeCameraRenders;
    }

    private void OnDisable()
    {
        OverheadBarStyleAsset.OnStyleChanged -= HandleStyleChanged;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= HandleBeforeCameraRenders;
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (!applyOnValidate || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                SafeInitialize();
        };
#endif
    }

    private void Update()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;

        float fillSpeed = appliedStyle != null ? Mathf.Max(0.01f, appliedStyle.fillLerpSpeed) : 3f;
        currentPercent = Mathf.Lerp(currentPercent, targetPercent, 1f - Mathf.Exp(-fillSpeed * dt));
        if (Mathf.Abs(currentPercent - targetPercent) < 0.0005f)
            currentPercent = targetPercent;

        if (damageReferenceHoldTimer > 0f)
        {
            damageReferenceHoldTimer -= dt;
        }
        else
        {
            float refSpeed = appliedStyle != null ? Mathf.Max(0.01f, appliedStyle.damageReferenceFadeSpeed) : 2.5f;
            damageReferencePercent = Mathf.MoveTowards(damageReferencePercent, currentPercent, refSpeed * dt);
        }

        float fadeSpeed = appliedStyle != null ? Mathf.Max(0.01f, appliedStyle.fadeSpeed) : 8f;
        currentDisplayAlpha = Mathf.Lerp(currentDisplayAlpha, targetDisplayAlpha, 1f - Mathf.Exp(-fadeSpeed * dt));
        if (Mathf.Abs(currentDisplayAlpha - targetDisplayAlpha) < 0.0005f)
            currentDisplayAlpha = targetDisplayAlpha;

        if (nameProximityFadeMode)
            UpdateNameFade(dt);

        RefreshBarVisuals();
    }

    private void LateUpdate()
    {
        // 高度计算跟"要不要转向摄像机"是两件不相关的事——高度只依赖角色自己的渲染
        // 网格，每帧持续测量没问题("只测一次就锁死"试过，已经证明有更严重的漏洞：
        // 角色刚生成时Spine骨骼还没摆好姿势就被锁死，永久卡在脚底)。朝向摄像机
        // 这一步反而不能留在这里——见HandleBeforeCameraRenders的注释，那部分挂在
        // RenderPipelineManager.beginCameraRendering上，LateUpdate读到的摄像机
        // Transform在Cinemachine接管的场景里永远是上一帧的旧值。
        UpdateOverheadAnchorHeightFromRendererBounds();

        UpdateRangeFade();
    }

    /// <summary>整个头顶UI离玩家太远就整体淡出——跟名字的"靠近淡入/远离淡出"是
    /// 两套独立的alpha，互不干扰。appliedStyle.visibilityFadeStartDistance&lt;=0
    /// 表示没启用，永远保持完全不透明。</summary>
    private void UpdateRangeFade()
    {
        if (runtimeUiRoot == null)
            return;

        float startDist = appliedStyle != null ? appliedStyle.visibilityFadeStartDistance : 0f;

        if (!Application.isPlaying || startDist <= 0f)
        {
            // 编辑器里(没在Play)或者这个功能没启用——不参与淡出判定，直接保证满
            // alpha，避免上次Play模式算出来的半透明值卡住不还原。
            if (rangeCanvasGroup != null) rangeCanvasGroup.alpha = 1f;
            return;
        }

        GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        float targetAlpha = 1f;
        if (playerGo != null)
        {
            float dist = Vector3.Distance(playerGo.transform.position, transform.position);
            float fadeRange = Mathf.Max(0.01f, appliedStyle.visibilityFadeRange);
            targetAlpha = 1f - Mathf.Clamp01((dist - startDist) / fadeRange);
        }

        float speed = appliedStyle != null ? appliedStyle.fadeSpeed : 8f;
        rangeCurrentAlpha = Mathf.Lerp(rangeCurrentAlpha, targetAlpha, 1f - Mathf.Exp(-speed * Time.deltaTime));
        if (Mathf.Abs(rangeCurrentAlpha - targetAlpha) < 0.0005f)
            rangeCurrentAlpha = targetAlpha;

        if (rangeCanvasGroup == null) rangeCanvasGroup = EnsureCanvasGroup(runtimeUiRoot);
        rangeCanvasGroup.alpha = rangeCurrentAlpha;
        bool visible = rangeCurrentAlpha > 0.001f;
        rangeCanvasGroup.blocksRaycasts = visible;
        rangeCanvasGroup.interactable = visible;
    }

    /// <summary>用真实渲染出来的Renderer.localBounds量角色实际有多高，把OverheadAnchor
    /// 摆到模型顶部正上方——比读Spine骨架文件里声明的Width/Height可靠得多(那个是
    /// 美术填的参考值，不一定跟实际渲染大小对得上，之前就因为漏乘scale把UI摆到
    /// 天上)。眼见为实：直接量渲染出来的东西，缩放/骨架大小改了也自动跟着对。
    /// 只量 SpineRoot 底下的渲染器，排除 OutlineProxy_*——那些是描边代理，位置/
    /// 缩放不一定跟主体一致，混进来算包围盒会得到错误的高度(参照之前"幽灵分身"
    /// 那次描边代理跑飞的教训)。
    ///
    /// 之前这里算的是完整的世界坐标(X/Y/Z都现测)，再InverseTransformPoint转回局部——
    /// 兜了一圈想让锚点自动对齐"倾斜/翻转/2.5D假Z排序"这些复杂情况，结果反而引入
    /// 了新的抖动来源，玩家一走动名字就跟着相对角色飘，排查了很久也没能完全定位
    /// 具体是哪一步在抖。用户一句话点破：锚点本来就该是"挂在角色身上、局部偏移量
    /// 固定不变的一个普通子物体"，跟角色一起走、一起转、一起缩放，不需要每帧现测
    /// 世界坐标再转换。现在只测一个标量——"模型最高点比角色根节点高出多少"，
    /// 直接写进localPosition.y，X/Z永远钉在0(=贴着根节点自己的位置，根节点在哪
    /// 名字就在正上方哪，根节点自己的Transform层级天然处理好跟随/旋转，不用这里
    /// 操心)。</summary>
    private void UpdateOverheadAnchorHeightFromRendererBounds()
    {
        if (overheadAnchor == null)
            return;

        Transform spineRoot = transform.Find("VisualRoot/SpineRoot");
        Transform searchRoot = spineRoot != null ? spineRoot : transform;

        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        // localBounds是渲染器自己局部空间下的包围盒，不受当前朝向/倾斜角度影响——
        // 蹲下/变形这类真的会改变localBounds本身的姿势变化依然会正确反映出来。
        // 每个渲染器只取"局部空间最高点，换算成世界Y"，再用transform.InverseTransformPoint
        // 转回角色根节点自己的局部空间取Y分量——只要这一个标量，X/Z完全不用管，
        // 从根源上排除了"包围盒中心X/Z被倾斜或排序假Z污染"这整类问题。
        //
        // 真正的抖动根因(实测日志确认过)：Spine的SkeletonRenderer执行顺序是1，这个
        // 类之前没设execution order(默认0)，比Spine的网格更新先跑，读到的localBounds
        // 有时候是上一帧的、甚至偶尔是完全没生成过的空网格((0,0,0))——已经在class
        // 上加了[DefaultExecutionOrder(100)]从根源解决，不需要靠"锁死不重测"这种
        // 绕开症状的办法。
        float maxLocalTop = float.NegativeInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || r.transform.name.Contains("OutlineProxy"))
                continue;

            Bounds lb = r.localBounds;
            float worldTopY = r.transform.position.y + (lb.center.y + lb.extents.y) * r.transform.lossyScale.y;
            float localTop = transform.InverseTransformPoint(new Vector3(0f, worldTopY, 0f)).y
                            - transform.InverseTransformPoint(Vector3.zero).y;
            if (localTop > maxLocalTop)
                maxLocalTop = localTop;
        }

        if (float.IsNegativeInfinity(maxLocalTop) || maxLocalTop <= 0.001f)
            return;

        // 贴着模型最高点摆会正好卡在发际线附近，看起来像"太低"——加一点头顶留白
        // (按模型高度的比例算，不同大小的角色留白也按比例跟着变)。这里同时把原本
        // 靠nameOffset(在跟着摄像机旋转的Canvas局部空间里叠加的二级偏移)才能做到的
        // "名字再往上挪一点"也一并折算进来——nameOffset现在清零，不再让名字的最终
        // 位置依赖两套独立坐标系统叠加(稳定的世界锚点 + 每帧跟着摄像机旋转的Canvas
        // 内偏移)，全部并成这一个已验证稳定的计算。
        // 0.32 是当初配合"世界空间竖直偏移"调出来的系数——现在偏移轴换成了
        // VisualRoot的本地上轴（45度倾斜方向），同样的系数在这个方向上投影出来的
        // 视觉高度比原来更远，才会显得"离头顶太远"。先调小到接近发际线的量，
        // 具体数值进Play目视确认后可能还要微调。
        float headroom = maxLocalTop * 0.04f;
        float height = maxLocalTop + headroom;

        // 2026-08-14：改回 localPosition，用户明确指出这条line应该沿billboard自己
        // 的"上"轴走，不是世界竖直方向——这正是最初"把人物和HUD一起billboard"这个
        // 思路本身：HUD要摆在角色billboard自己的那条线上，而这条线只有在父节点是
        // VisualRoot（会转向相机）时，本地Y轴才等于"billboard的上"。改成世界
        // Vector3.up是把这条线又拽回世界竖直方向，表现正是用户说的"各自45度"——
        // 角色的billboard轴和HUD的位置轴不再是同一条线，两者视觉上各转各的。
        //
        // "太高了"这个反馈针对的是height这个标量本身，不是它该沿哪个轴走——
        // 量级问题应该调headroom/maxLocalTop这个数值，不该把轴改回世界空间。
        overheadAnchor.localPosition = new Vector3(0f, height, 0f);
    }

    /// <summary>找到真正的根因：这个场景的Main Camera挂了CinemachineBrain，
    /// 它的实际位置/旋转是在Camera.OnPreCull()里更新的——这个回调比场景里所有
    /// 脚本的Update/LateUpdate都晚(是渲染管线在真正剔除/渲染这台摄像机之前才
    /// 触发的，不受DefaultExecutionOrder约束)。之前这一步放在LateUpdate里，
    /// 读到的runtimeUiRoot要对齐的摄像机朝向永远是"上一帧Cinemachine还没更新前"
    /// 的旧值，跟这一帧真正渲染出来的画面差一帧——玩家持续移动时，就表现成
    /// 名字/头顶UI跟着移动方向持续滞后飘移(脚下的地面阴影系统UnitGroundShadowFollowerTerrain
    /// 也在LateUpdate里读摄像机，同一个坑，只是这个类不归这次改)。改成挂在
    /// RenderPipelineManager.beginCameraRendering上，保证在Cinemachine真正定位完
    /// 摄像机之后才读取朝向，这才是这一帧最终会被渲染出来的那个朝向。
    /// (Camera.onPreCull是内置管线的回调，URP下永不触发，用了等于没写。)</summary>
    private void HandleBeforeCameraRenders(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera renderingCam)
    {
        if (!faceCameraEveryFrame || runtimeUiRoot == null)
            return;

        Camera cam = ResolveOverheadCamera();
        if (cam == null || renderingCam != cam)
            return;

        // 2026-08-14：之前这里每帧手动把 runtimeUiRoot.rotation 设成 cam.transform.rotation，
        // 用来让HUD面向相机。现在 overheadAnchor 已经挂到 VisualRoot 下面（见
        // EnsureStructure），朝向直接继承父节点——VisualRoot 转到哪，这里就跟着到哪，
        // 完全不需要在这个单独的、时机还和角色本体不一样的回调里再算一遍。
        //
        // 只有找不到 VisualRoot（anchorSharesVisualRootRotation=false，走了退化到
        // 逻辑根节点的兜底路径）才需要这段手动朝向——那种情况下 overheadAnchor 没有
        // 可继承的旋转来源，之前的行为（好歹自己转向相机）比完全不转要好。
        if (!anchorSharesVisualRootRotation)
            runtimeUiRoot.rotation = cam.transform.rotation;

        // 诊断：之前两次都以为改对了，用户实测还是45+45。与其继续猜前提成不成立，
        // 直接把关键状态打出来——一次性判断到底是"没找到VisualRoot走了兜底"，
        // 还是"找到了但VisualRoot本身没有被CameraFacingBillboard转到位"，
        // 还是"两个旋转其实一致，问题出在别的地方（比如runtimeUiRoot下面某个
        // 子节点又带了自己的旋转)"。每5秒打一次，够定位又不刷屏。
        if (Time.frameCount % 300 == 0)
        {
            Transform visualRoot = transform.Find("VisualRoot");
            var billboard = visualRoot != null ? visualRoot.GetComponent<CameraFacingBillboard>() : null;
            Debug.Log($"[OverheadBillboardDiag] {name} " +
                      $"anchorSharesVisualRootRotation={anchorSharesVisualRootRotation} " +
                      $"VisualRoot找到={visualRoot != null} " +
                      $"VisualRoot上有CameraFacingBillboard={billboard != null}" +
                      (billboard != null ? $"(enabled={billboard.enabled})" : "") +
                      $"\n  VisualRoot.rotation.eulerAngles={(visualRoot != null ? visualRoot.rotation.eulerAngles.ToString("F1") : "n/a")}" +
                      $"\n  overheadAnchor.rotation.eulerAngles={(overheadAnchor != null ? overheadAnchor.rotation.eulerAngles.ToString("F1") : "n/a")}" +
                      $"\n  overheadAnchor.localRotation.eulerAngles={(overheadAnchor != null ? overheadAnchor.localRotation.eulerAngles.ToString("F1") : "n/a")}" +
                      $"\n  runtimeUiRoot.rotation.eulerAngles={runtimeUiRoot.rotation.eulerAngles:F1}" +
                      $"\n  runtimeUiRoot.localRotation.eulerAngles={runtimeUiRoot.localRotation.eulerAngles:F1}" +
                      $"\n  cam.transform.rotation.eulerAngles={cam.transform.rotation.eulerAngles:F1}", this);

            // runtimeUiRoot.localRotation 的值跟 cam.transform.rotation 完全相等——
            // 这不是我写的那段代码干的（anchorSharesVisualRootRotation=True 时那段
            // 代码根本不会执行），说明另有一个脚本直接把相机的世界旋转当成本地旋转
            // 写了进去。列出这个物体上挂的所有组件，把元凶揪出来。
            var comps = runtimeUiRoot.GetComponents<Component>();
            var compNames = new System.Text.StringBuilder("[OverheadBillboardDiag] runtimeUiRoot上的组件：");
            foreach (var c in comps)
                compNames.Append(c != null ? c.GetType().Name : "NULL").Append(", ");
            Debug.Log(compNames.ToString(), this);
        }
    }

    // EnsureStructure 里记录：overheadAnchor 是否成功挂到了 VisualRoot 下面共享朝向，
    // 还是退化到了逻辑根节点（没有 VisualRoot 时的兜底）。
    private bool anchorSharesVisualRootRotation;

    // 场景里明确存在一个专属的"OverheadUICamera"(Overlay类型，只渲染头顶UI所在的
    // 层，叠加合成在主摄像机画面上面)——之前这里一直用Camera.main，间接依赖
    // "MainCamera"标签解析到的那个摄像机对象。这个项目摄像机结构比较复杂
    // (Main Camera/GamePlayCamera/CinemachineCamera/OverheadUICamera同时存在)，
    // 名字跟着玩家移动飘的问题查到这一步还没能定位到具体是哪一层的差异，与其
    // 继续猜Camera.main解析到的到底是不是渲染这层UI的那个摄像机，不如直接明确
    // 指定用这个专属摄像机，彻底排除"用错摄像机"这一整类可能性。缓存住，不用
    // 每帧GameObject.Find。
    private static Camera _cachedOverheadUICamera;

    private Camera ResolveOverheadCamera()
    {
        if (targetCamera != null)
            return targetCamera;

        if (_cachedOverheadUICamera == null)
        {
            GameObject go = GameObject.Find("OverheadUICamera");
            if (go != null) _cachedOverheadUICamera = go.GetComponent<Camera>();
        }

        return _cachedOverheadUICamera != null ? _cachedOverheadUICamera : Camera.main;
    }

    private void HandleStyleChanged(OverheadBarStyleAsset changedStyle)
    {
        if (changedStyle == null)
            return;

        OverheadBarStyleAsset currentStyle = unitDefinition != null
            ? unitDefinition.overheadHpBarStyle
            : appliedStyle;

        if (currentStyle == null || currentStyle != changedStyle)
            return;

        ApplyStyle(changedStyle);

        if (cachedStatuses.Count > 0 || cachedStatusDisplayDefinition != null)
            RefreshStatuses(cachedStatuses, cachedStatusDisplayDefinition);
    }

    private void SafeInitialize()
    {
        if (isInitializing)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(gameObject))
            return;
#endif

        isInitializing = true;
        try
        {
            if (autoEnsureStructure)
                EnsureStructure();

            if (autoResolveUnitDefinition)
                TryAutoResolveUnitDefinition();

            if (applyOnStart)
            {
                ApplyStyleFromDefinition();
                ApplyNameVisibilityFromDefinition();
            }
        }
        finally
        {
            isInitializing = false;
        }
    }


    [Header("Damage Number Anchor")]
    [SerializeField] private Transform damageNumberAnchor;
    [SerializeField] private Vector3 damageNumberAnchorOffset = new Vector3(0f, 0.15f, 0f);
    public void SetName(string value) { SetDisplayName(value); }

    public void SetNameVisible(bool visible)
    {
        if (nameRoot != null)
            nameRoot.gameObject.SetActive(visible);
    }

    /// <summary>
    /// 切到"靠近淡入/远离淡出"模式——一旦调过一次，ApplyNameVisibilityFromDefinition()
    /// 和 UnitOverheadHealthBridge.ForceOverheadVisibility() 里原本按
    /// autoOverheadNameVisibility/manualShowOverheadName 直接 SetActive 的逻辑就不再
    /// 插手名字显隐了(死亡隐藏除外)，改由 SetNameFadeTarget() 驱动。
    /// </summary>
    public void SetNameProximityFadeMode(bool enabled)
    {
        nameProximityFadeMode = enabled;
    }

    /// <summary>UnitOverheadHealthBridge.ForceOverheadVisibility() 用来判断要不要
    /// 让开，别跟 SetNameFadeTarget() 抢着改 nameRoot 的激活状态。</summary>
    public bool IsNameProximityFadeMode => nameProximityFadeMode;

    /// <summary>NPCDialogueInteractable按玩家距离调用——true=淡入，false=淡出。</summary>
    public void SetNameFadeTarget(bool visible)
    {
        nameProximityFadeMode = true;
        nameTargetAlpha = visible ? 1f : 0f;
        if (visible && nameRoot != null)
            nameRoot.gameObject.SetActive(true); // 淡入前先激活，淡出完全归零后才在Update里关掉
    }

    private void UpdateNameFade(float dt)
    {
        if (nameRoot == null) return;
        if (nameCanvasGroup == null) nameCanvasGroup = EnsureCanvasGroup(nameRoot);

        nameCurrentAlpha = Mathf.Lerp(nameCurrentAlpha, nameTargetAlpha, 1f - Mathf.Exp(-NameFadeSpeed * dt));
        if (Mathf.Abs(nameCurrentAlpha - nameTargetAlpha) < 0.002f)
            nameCurrentAlpha = nameTargetAlpha;

        nameCanvasGroup.alpha = nameCurrentAlpha;

        if (nameTargetAlpha <= 0f && nameCurrentAlpha <= 0f)
            nameRoot.gameObject.SetActive(false);
    }

    public void SetDisplayName(string value)
    {
        EnsureNameHierarchyCorrect();
        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(value) ? "NPC Name" : value;
    }

    public void ApplyStyleFromDefinition()
    {
        OverheadBarStyleAsset style = unitDefinition != null ? unitDefinition.overheadHpBarStyle : null;
        ApplyStyle(style);
    }

    public void ApplyStyle(OverheadBarStyleAsset style)
    {
        appliedStyle = style;

        if (autoEnsureStructure)
            EnsureStructure();

        // preserveAuthoredLayout以前会把血条/名字的位置和尺寸拆成两条独立代码路径
        // (一条用于"手搓预制体"、一条用于"自动生成的壳")，每次改一条另一条就没人管，
        // 这才是这几轮"这里改好了那里又坏了"的根本原因。现在统一成一条路径，位置/
        // 尺寸/颜色永远只从样式资产读一份逻辑，不再区分是不是手搓预制体。
        ApplyUnitDefinitionTransformOverrides();
        ApplyNameStyle(style);
        ApplyBarStyle(style);
        ApplyStatusAreaStyle(style);
        ApplyNameVisibilityFromDefinition();
        // 2026-08-14：这是"45+45=90"真正的元凶——之前只查了每帧跑的
        // HandleBeforeCameraRenders，漏了这条"应用样式"时跑的一次性路径。
        //
        // 这里直接把 runtimeUiRoot 的世界旋转设成当前相机旋转，保证样式刚应用/
        // 编辑器没进Play模式时也能立刻摆正。问题是它执行时机早于
        // CameraFacingBillboard第一次转动VisualRoot——那一刻父节点世界旋转还是0，
        // 这行代码算出的本地旋转就被烘焙成了"45度"(=相机自己的倾角)。之后
        // VisualRoot才被转到45度，这份烘焙值没人再更新（因为anchorSharesVisualRootRotation
        // 为真时，每帧那条路径被跳过），于是45(烘焙的本地值)+45(父节点后来转到的)=90。
        //
        // 现在挂到VisualRoot下面共享朝向时，这条一次性路径也要跳过——
        // runtimeUiRoot的本地旋转应该保持EnsureSingleRuntimeRoot里设的identity，
        // 完全靠继承，不需要（也不能）在这里再手动摆一次。
        if (!anchorSharesVisualRootRotation && faceCameraEveryFrame && runtimeUiRoot != null)
        {
            Camera camForStyle = ResolveOverheadCamera();
            if (camForStyle != null)
                runtimeUiRoot.rotation = camForStyle.transform.rotation;
        }
        UpdateDisplayAlphaTarget();

        if (!Application.isPlaying)
            currentDisplayAlpha = targetDisplayAlpha;

        RefreshBarVisuals();
        DebugDumpOffsets("ApplyStyle");
    }

    public void SetHp(float current, float max)
    {
        float normalized = max > 0.0001f ? Mathf.Clamp01(current / max) : 0f;
        float oldTarget = targetPercent;
        targetPercent = normalized;

        if (targetPercent < oldTarget)
        {
            damageReferencePercent = Mathf.Max(damageReferencePercent, oldTarget);
            damageReferenceHoldTimer = appliedStyle != null
                ? Mathf.Max(0f, appliedStyle.damageReferenceHoldTime)
                : 0.35f;
        }

        UpdateDisplayAlphaTarget();

        if (!Application.isPlaying)
        {
            currentPercent = targetPercent;
            damageReferencePercent = Mathf.Max(damageReferencePercent, currentPercent);
            currentDisplayAlpha = targetDisplayAlpha;
            RefreshBarVisuals();
        }
    }

    public void EnsureStructure()
    {
        // 任务标记单独一个组件，只读这个类的 unitDefinition / nameRoot / nameText。
        // 在这里补挂而不是要求预制体里手加：头顶 UI 本来就是按单位动态搭的。
        if (GetComponent<UnitOverheadQuestMarker>() == null)
            gameObject.AddComponent<UnitOverheadQuestMarker>();

        // 2026-08-14：挂到 VisualRoot 下面，不再挂在逻辑根节点(transform)下面。
        //
        // 角色本体的 billboard 朝向由 CameraFacingBillboard 只旋转 VisualRoot 完成
        // （45度贴合相机视线那套），逻辑根节点(transform)本身不转——之前 overheadAnchor
        // 挂在 transform 下，跟角色朝向完全是两套独立的东西：角色转、锚点不转，
        // 于是头顶UI需要自己单独算一套朝向/位置去追角色，两边各自为政，任何一点
        // 时机差（LateUpdate vs beginCameraRendering）或计算方式差异（世界空间偏移
        // vs 相机空间偏移）都会表现成"HUD跟角色不同步"——之前两次修复(反投影/相机up轴)
        // 想解决的都是这个"追"的过程，但只要还是两套独立系统，就永远追不干净。
        //
        // 挂到 VisualRoot 下面之后，overheadAnchor 的朝向直接继承父节点的旋转——
        // 角色朝哪转，锚点跟着转到哪，不需要写任何同步代码，两者不可能不一致，
        // 因为它们现在共用同一次旋转计算，不是两次独立计算出来的两个结果。
        Transform visualRootForAnchor = transform.Find("VisualRoot");
        anchorSharesVisualRootRotation = visualRootForAnchor != null;
        overheadAnchor = EnsureTransformChild(
            visualRootForAnchor != null ? visualRootForAnchor : transform,
            "OverheadAnchor");

        // EnsureTransformChild是按名字找、找到了就直接复用——如果这个预制体里
        // 早就存在一个OverheadAnchor（老系统年代，挂在不转的父节点下时，可能被
        // 手动摆过角度去补偿看起来"不够斜"），复用时会带着那份旧旋转一起过来。
        // 现在父节点(VisualRoot)自己就有45度的billboard旋转，这份历史遗留的本地
        // 旋转会跟父节点的旋转叠加——45+45=90度，表现正好是"翻倍了"。
        // 挂到VisualRoot下之后，朝向完全交给父节点继承，本地旋转必须清零，
        // 不管这个anchor是刚建的还是找到的旧对象。
        if (anchorSharesVisualRootRotation)
            overheadAnchor.localRotation = Quaternion.identity;

        // 挂载点从 transform 迁到 VisualRoot 之后，已经存过的场景/预制体里
        // 可能还留着挂在 transform 下的旧 OverheadAnchor——EnsureTransformChild
        // 只会在新父节点下找不到就新建，老的那份不会自动清掉，会变成孤儿一直
        // 留在层级里。这里顺手清掉，只在它确实不是刚才拿到手的那个时才删。
        if (visualRootForAnchor != null)
        {
            Transform staleAnchor = transform.Find("OverheadAnchor");
            if (staleAnchor != null && staleAnchor != overheadAnchor)
                SafeDestroy(staleAnchor.gameObject);
        }

        // 改回挂在单位身上的World Space Canvas(跟着单位在地图里到处跑)，不再是
        // 屏幕投影——之前的ScreenSpaceOverlay在Canvas层面有个死结：Canvas自己的
        // RectTransform每帧都被Unity强制拉伸铺满整个屏幕，不管代码怎么改子物体的
        // position都没用，所有单位的UI因此全部叠在屏幕中心。World Space没有这个
        // 问题，Canvas本身就是一个普通的3D物件，跟着OverheadAnchor走就行。
        //
        // 清掉屏幕投影阶段遗留的画布壳节点，避免僵尸重影。
        Transform legacyScreenCanvas = overheadAnchor.Find("UnitOverheadUIScreenCanvas");
        if (legacyScreenCanvas != null)
            SafeDestroy(legacyScreenCanvas.gameObject);

        runtimeUiRoot = EnsureSingleRuntimeRoot(overheadAnchor);
        EnsureCanvasComponents(runtimeUiRoot);
        EnsureNameHierarchyCorrect();

        slotRoot01 = EnsureRectChild(runtimeUiRoot, "SlotRoot_01");
        slotCanvasGroup = EnsureCanvasGroup(slotRoot01);
        slotView01 = EnsureRectChild(slotRoot01, "SlotView_01");
        bgLayerRoot = EnsureRectChild(slotView01, "BgLayerRoot");
        damageRefMaskRoot = EnsureMaskRoot(slotView01, "DamageRefMaskRoot");
        damageReferenceImage = EnsureRawImageChild(damageRefMaskRoot, "DamageRef");
        fillMaskRoot = EnsureMaskRoot(slotView01, "FillMaskRoot");
        fillImage = EnsureRawImageChild(fillMaskRoot, "Fill");

        // 血条要能穿透墙体/集装箱这些不透明3D物体显示在最上层，不能用默认UI
        // 材质(会正常参与深度测试、被挡住)——换成ZTest Always的专用材质。
        ApplyAlwaysOnTopMaterial(damageReferenceImage);
        ApplyAlwaysOnTopMaterial(fillImage);

        // 血条两端加"[ ]"装饰括号，贴着血条左右边缘，不参与血量填充遮罩。
        bracketLeftText = EnsureTMPChild(slotView01, "BracketLeft", "[");
        bracketRightText = EnsureTMPChild(slotView01, "BracketRight", "]");
        ConfigureBracketRect(bracketLeftText, TextAlignmentOptions.Right, new Vector2(0f, 0.5f), new Vector2(-2f, 0f));
        ConfigureBracketRect(bracketRightText, TextAlignmentOptions.Left, new Vector2(1f, 0.5f), new Vector2(2f, 0f));
        ApplyAlwaysOnTopFontMaterial(bracketLeftText);
        ApplyAlwaysOnTopFontMaterial(bracketRightText);

        statusRoot = EnsureRectChild(runtimeUiRoot, "StatusRoot");
        statusAreaRoot = EnsureRectChild(statusRoot, "StatusAreaRoot");
        statusContentRoot = EnsureRectChild(statusAreaRoot, "StatusContentRoot");

        damageNumberRoot = EnsureRectChild(runtimeUiRoot, "DamageNumberRoot");
        damageNumberContentRoot = EnsureRectChild(damageNumberRoot, "DamageNumberContentRoot");

        // ConfigureDefaultAnchors()只应该在这个节点刚建出来、什么值都还没有的时候
        // 摆一次占位默认值——但EnsureStructure()在一次ApplyStyle()流程里会被调用
        // 不止一次(比如ApplyStatusAreaStyle内部又调了一次)，如果每次都重摆，
        // 会把ApplyBarStyle()刚从样式资产设进去的真实血条位置/尺寸冲掉，表现为
        // "样式资产的数值明明是对的，但实际血条还是停在150x14这种旧的硬编码
        // 默认值上"——这正是这次的bug。改成只在第一次(节点刚创建时)摆一次。
        if (!defaultAnchorsInitialized)
        {
            ConfigureDefaultAnchors();
            defaultAnchorsInitialized = true;
        }

        ApplyUnitDefinitionTransformOverrides();

        SetLayerRecursively(runtimeUiRoot.gameObject, OverheadUiLayer);
    }

    private bool defaultAnchorsInitialized;

    private RectTransform EnsureSingleRuntimeRoot(Transform parent)
    {
        List<Transform> matches = new List<Transform>();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == RuntimeRootName)
                matches.Add(child);
        }

        RectTransform keep = null;
        for (int i = 0; i < matches.Count; i++)
        {
            RectTransform rect = matches[i] as RectTransform;
            if (keep == null && rect != null)
            {
                keep = rect;
                continue;
            }

            SafeDestroy(matches[i].gameObject);
        }

        if (keep != null)
        {
            // 复用已存在节点时原样返回，从不touch它的旋转——如果这个节点带着
            // 历史遗留的本地旋转(比如老层级下手动摆过的45度)，会一直原样保留下去，
            // 现在挂到VisualRoot下面(父节点自己会动态转向相机)，这份残留旋转就是
            // 多余的、错误的。这个节点自己的本地旋转必须是0，朝向完全交给
            // HandleBeforeCameraRenders每帧动态设置的世界旋转决定。
            keep.localRotation = Quaternion.identity;
            return keep;
        }

        GameObject go = new GameObject(RuntimeRootName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform freshRoot = go.GetComponent<RectTransform>();
        // SetParent(parent, false)已经会给新节点0本地旋转，这里显式写一遍，
        // 不依赖隐式默认值——避免以后有人在这附近加代码时误以为需要自己处理旋转。
        freshRoot.localRotation = Quaternion.identity;

        // 只在这里、只对"刚创建出来、以前从来不存在"的节点给一个初始缩放——
        // 血条/字号这些数值(150、24这种)是照着"缩小过的Canvas"这个假设配的，
        // 全新单位(比如通用壳)的Canvas默认缩放是1，不缩小的话数值会在世界空间
        // 里被放得极大(24号字在scale=1下能有小两米高)。老的手搓预制体已经带了
        // 自己的缩放(不知道具体是多少，但明显是缩小过的，不然当年也用不了这批
        // 数值)，这里绝不touch已存在的节点，只在真正"从0开始"时给一次初始值，
        // 之后永远不再覆盖。
        freshRoot.localScale = new Vector3(0.02f, 0.02f, 0.02f);
        return freshRoot;
    }

    private void ApplyNameVisibilityFromDefinition()
    {
        if (nameProximityFadeMode) return; // 交给 SetNameFadeTarget()，这里不再插手

        if (unitDefinition == null)
        {
            SetNameVisible(false);
            return;
        }

        bool visible = unitDefinition.autoOverheadNameVisibility
            ? ShouldShowNameAutomatically(unitDefinition)
            : unitDefinition.manualShowOverheadName;

        SetNameVisible(visible);
    }

    private bool ShouldShowNameAutomatically(UnitDefinition definition)
    {
        return definition != null && definition.characterIdentity == CharacterIdentity.Ally;
    }

    private void EnsureNameHierarchyCorrect()
    {
        if (runtimeUiRoot == null)
            return;

        TextMeshProUGUI strayRootText = runtimeUiRoot.GetComponent<TextMeshProUGUI>();
        if (strayRootText != null)
            SafeDestroyComponent(strayRootText);

        nameRoot = EnsureRectChild(runtimeUiRoot, "NameRoot");

        for (int i = runtimeUiRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = runtimeUiRoot.GetChild(i);
            if (child == null || child == nameRoot)
                continue;

            if (child.name == "NameText")
                SafeDestroy(child.gameObject);
        }

        TextMeshProUGUI wrongTextOnRoot = nameRoot.GetComponent<TextMeshProUGUI>();
        if (wrongTextOnRoot != null)
            SafeDestroyComponent(wrongTextOnRoot);

        nameText = EnsureTMPChild(nameRoot, "NameText", "NPC Name");
        nameRoot.gameObject.SetActive(false);
    }

    private void ApplyNameStyle(OverheadBarStyleAsset style)
    {
        EnsureNameHierarchyCorrect();
        if (nameText == null || nameRoot == null)
            return;

        if (style != null)
        {
            if (style.nameFontAsset != null)
                nameText.font = style.nameFontAsset;

            nameText.fontSize = Mathf.Max(8, style.nameFontSize) * 1.2f;
            nameText.color = style.nameColor;
            // SetRectCenter内部会把Y取负(历史上是给"配置值越大越往下"这种约定用的)，
            // 名字这里要的是"配置值越大越往上"，先取负抵消掉，不然调大nameOffset.y
            // 实际效果是名字一直往下掉，跟直觉完全反着来——这正是之前"怎么调都不往
            // 上"的真正原因。
            SetRectCenter(nameRoot, new Vector2(style.nameOffset.x, -style.nameOffset.y));
            nameRoot.sizeDelta = new Vector2(240f, 24f);

            // 细黑描边——名字浅色时贴在浅色背景(墙面/天空)上容易糊成一片，
            // 描边是最省事的可读性修法，不用额外做贴图。
            nameText.outlineWidth = 0.2f;
            nameText.outlineColor = Color.black;
        }
        else
        {
            nameText.fontSize = 13f;
            nameText.color = Color.white;
            SetRectCenter(nameRoot, new Vector2(0f, 16f));
            nameRoot.sizeDelta = new Vector2(240f, 24f);
        }

        nameText.alignment = TextAlignmentOptions.Center;
        nameText.rectTransform.anchorMin = Vector2.zero;
        nameText.rectTransform.anchorMax = Vector2.one;
        nameText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        nameText.rectTransform.anchoredPosition = Vector2.zero;
        nameText.rectTransform.sizeDelta = Vector2.zero;

        ApplyAlwaysOnTopFontMaterial(nameText);
    }

    /// <summary>名字文字也要能穿透墙体显示——TMP自带的"Distance Field Overlay"
    /// 变体就是ZTest Always版本，直接从当前字体的默认材质复制一份、换成Overlay
    /// shader即可，贴图/参数都跟原字体保持一致，不用自己再配一份字体材质。</summary>
    private void ApplyAlwaysOnTopFontMaterial(TextMeshProUGUI text)
    {
        if (text == null || text.font == null || text.font.material == null)
            return;

        Shader overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (overlayShader == null)
            return;

        if (text.fontMaterial == null || text.fontMaterial.shader != overlayShader)
        {
            Material overlayMaterial = new Material(text.font.material) { shader = overlayShader };
            text.fontMaterial = overlayMaterial;
        }
    }

    private void ApplyBarStyle(OverheadBarStyleAsset style)
    {
        if (slotRoot01 == null || slotView01 == null || bgLayerRoot == null ||
            damageRefMaskRoot == null || fillMaskRoot == null ||
            damageReferenceImage == null || fillImage == null)
            return;

        UnitOverheadLayoutUtility.LayoutResult layout = UnitOverheadLayoutUtility.BuildLayout(style);

        slotRoot01.anchorMin = new Vector2(0.5f, 0.5f);
        slotRoot01.anchorMax = new Vector2(0.5f, 0.5f);
        slotRoot01.pivot = new Vector2(0.5f, 0.5f);
        slotRoot01.anchoredPosition = layout.barRootPosition;

        SetRectCenter(slotView01, Vector2.zero);
        slotView01.sizeDelta = layout.barSize;
        slotView01.localRotation = Quaternion.Euler(0f, 0f, style != null ? style.rotationZ : 0f);

        ConfigureMaskRoot(damageRefMaskRoot, layout.barSize);
        ConfigureMaskRoot(fillMaskRoot, layout.barSize);
        ConfigureBarImage(damageReferenceImage.rectTransform, layout.barSize);
        ConfigureBarImage(fillImage.rectTransform, layout.barSize);

        if (style != null)
        {
            fillImage.texture = style.fillTexture;
            fillImage.color = style.fillColor;
            damageReferenceImage.texture = style.damageReferenceTexture;
            damageReferenceImage.color = style.damageReferenceColor;
            RebuildBgLayers(style);
        }
        else
        {
            fillImage.texture = null;
            fillImage.color = Color.white;
            damageReferenceImage.texture = null;
            damageReferenceImage.color = new Color(0.42f, 0.12f, 0.12f, 1f);
            ClearBgLayers();
        }

        if (debugLogs)
            Log($"ApplyBarStyle | barRoot={layout.barRootPosition} | barSize={layout.barSize}");
    }

    private void ApplyBarVisualStyleOnly(OverheadBarStyleAsset style)
    {
        if (damageReferenceImage == null || fillImage == null)
            return;

        appliedStyle = style;

        // ApplyNameStyle()不管preserveAuthoredLayout是不是true都会无条件重新摆
        // 名字的位置，但血条的位置以前只在ApplyBarStyle()里设置——手搓的专属预制体
        // (preserveAuthoredLayout=true，比如这次这个运行时生成的敌人)走的是这个
        // "只改颜色贴图"的方法，血条位置一直没人管，还停在很久以前(好几轮结构调整
        // 之前)authored的旧坐标上，跟名字的位置对不上。位置跟着样式资产走，不属于
        // "要保留的手工排版"，两边都应该跟着同一个offset摆，不用分preserve不preserve。
        // 血条的粗细/尺寸(slotView01.sizeDelta + 两个遮罩根 + 两张RawImage的Rect)
        // 之前也是只在ApplyBarStyle()里设置的——跟位置是同一个漏洞，"保留手工排版"
        // 只应该保留手工可能想留的东西(比如整体旋转这种细节调整)，粗细这种直接
        // 由样式资产决定的数值不应该被落下不管，不然调了样式资产里的barSize
        // 这条腿完全没反应，看起来就是"改了跟没改一样"。
        if (slotRoot01 != null && slotView01 != null && damageRefMaskRoot != null && fillMaskRoot != null)
        {
            UnitOverheadLayoutUtility.LayoutResult layout = UnitOverheadLayoutUtility.BuildLayout(style);
            slotRoot01.anchorMin = new Vector2(0.5f, 0.5f);
            slotRoot01.anchorMax = new Vector2(0.5f, 0.5f);
            slotRoot01.pivot = new Vector2(0.5f, 0.5f);
            slotRoot01.anchoredPosition = layout.barRootPosition;

            SetRectCenter(slotView01, Vector2.zero);
            slotView01.sizeDelta = layout.barSize;

            ConfigureMaskRoot(damageRefMaskRoot, layout.barSize);
            ConfigureMaskRoot(fillMaskRoot, layout.barSize);
            ConfigureBarImage(damageReferenceImage.rectTransform, layout.barSize);
            ConfigureBarImage(fillImage.rectTransform, layout.barSize);
        }

        if (style != null)
        {
            fillImage.texture = style.fillTexture;
            fillImage.color = style.fillColor;
            damageReferenceImage.texture = style.damageReferenceTexture;
            damageReferenceImage.color = style.damageReferenceColor;
            RebuildBgLayers(style);
        }
        else
        {
            fillImage.texture = null;
            fillImage.color = Color.white;
            damageReferenceImage.texture = null;
            damageReferenceImage.color = new Color(0.42f, 0.12f, 0.12f, 1f);
            ClearBgLayers();
        }
    }

    private void ApplyStatusAreaVisibilityOnly(OverheadBarStyleAsset style)
    {
        if (statusRoot == null)
            return;

        bool enable = style == null || style.enableStatusArea;
        statusRoot.gameObject.SetActive(enable);
        if (!enable)
            ClearStatusIconViews();
    }

    public void ApplyStatusAreaStyle(OverheadBarStyleAsset style)
    {
        if (autoEnsureStructure)
            EnsureStructure();

        if (statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
            return;

        bool enable = style == null || style.enableStatusArea;
        statusRoot.gameObject.SetActive(enable);
        if (!enable)
            return;

        UnitOverheadLayoutUtility.LayoutResult layout = UnitOverheadLayoutUtility.BuildLayout(style);

        statusRoot.anchorMin = new Vector2(0.5f, 0.5f);
        statusRoot.anchorMax = new Vector2(0.5f, 0.5f);
        statusRoot.pivot = new Vector2(0.5f, 0.5f);
        statusRoot.anchoredPosition = layout.statusRootPosition;

        statusAreaRoot.anchorMin = new Vector2(0.5f, 0.5f);
        statusAreaRoot.anchorMax = new Vector2(0.5f, 0.5f);
        statusAreaRoot.pivot = new Vector2(0.5f, 0.5f);
        statusAreaRoot.anchoredPosition = layout.statusAreaPosition;
        statusAreaRoot.sizeDelta = layout.statusAreaSize;

        statusContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
        statusContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
        statusContentRoot.pivot = new Vector2(0.5f, 0.5f);
        statusContentRoot.anchoredPosition = layout.statusContentPosition;
        statusContentRoot.sizeDelta = layout.statusContentSize;

        if (debugLogs)
        {
            Log($"ApplyStatusAreaStyle | statusRoot={layout.statusRootPosition} | statusArea={layout.statusAreaPosition} | statusAreaSize={layout.statusAreaSize}");
        }
    }

    public void RefreshStatuses(IReadOnlyList<StatusViewData> statuses, StatusDisplayDefinition displayDefinition)
    {
        cachedStatuses.Clear();
        if (statuses != null)
        {
            for (int i = 0; i < statuses.Count; i++)
                cachedStatuses.Add(statuses[i]);
        }
        cachedStatusDisplayDefinition = displayDefinition;

        // 重要：状态刷新只刷新“状态图标”，不重套头顶 UI 坐标。
        // 之前每次状态同步都 EnsureStructure + ApplyStatusAreaStyle，会把血条/状态区域反复拉回默认布局，造成上下抖动和血条坐标下移。
        if (statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
        {
            if (autoEnsureStructure)
                EnsureStructure();

            if (statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
                return;
        }

        bool enableStatusArea = appliedStyle == null || appliedStyle.enableStatusArea;
        statusRoot.gameObject.SetActive(enableStatusArea);
        if (!enableStatusArea)
        {
            ClearStatusIconViews();
            return;
        }

        FillVisibleStatusesBuffer(statuses, visibleStatusBuffer);
        if (maxRuntimeStatusIconPool > 0 && visibleStatusBuffer.Count > maxRuntimeStatusIconPool)
            visibleStatusBuffer.RemoveRange(maxRuntimeStatusIconPool, visibleStatusBuffer.Count - maxRuntimeStatusIconPool);

        int visibleCount = visibleStatusBuffer.Count;
        EnsureStatusIconPool(visibleCount);

        if (compactStrayStatusIconsOnRefresh)
            CompactStatusIconChildren();

        // 先把池里旧图标清干净，但不动 statusRoot/statusAreaRoot/slotRoot01 的坐标。
        for (int i = 0; i < runtimeStatusIcons.Count; i++)
        {
            UnitStatusIconView view = runtimeStatusIcons[i];
            if (view == null)
                continue;

            view.ResetView();
            view.gameObject.SetActive(false);
        }

        if (visibleCount <= 0)
            return;

        Vector2 iconSize = UnitOverheadLayoutUtility.ResolveStatusIconSize(appliedStyle);
        Vector2 spacing = UnitOverheadLayoutUtility.ResolveStatusSpacing(appliedStyle);
        TextAnchor alignment = UnitOverheadLayoutUtility.ResolveStatusAlignment(appliedStyle);
        int maxLines = UnitOverheadLayoutUtility.ResolveStatusMaxLines(displayDefinition);
        StatusDisplayDirection direction = UnitOverheadLayoutUtility.ResolveStatusDirection(displayDefinition);

        activeStatusRectBuffer.Clear();

        for (int i = 0; i < visibleStatusBuffer.Count; i++)
        {
            UnitStatusIconView view = runtimeStatusIcons[i];
            if (view == null)
                continue;

            view.gameObject.SetActive(true);
            view.EnsureStructure();
            view.Apply(visibleStatusBuffer[i], direction);

            activeStatusRectBuffer.Add(view.root);
        }

        UnitOverheadLayoutUtility.LayoutStatusIcons(
            activeStatusRectBuffer,
            statusAreaRoot.sizeDelta,
            iconSize,
            spacing,
            maxLines,
            direction,
            alignment);

        AlignStatusContentToBarLeft(iconSize);

        if (debugLogs)
        {
            Log($"RefreshStatuses | visible={visibleStatusBuffer.Count} | pool={runtimeStatusIcons.Count} | iconSize={iconSize} | spacing={spacing} | alignment={alignment} | maxLines={maxLines} | direction={direction}");
        }
    }

    // 把状态图标整体偏移，使第一个图标的左端对齐血条的左端。
    // 只改 statusContentRoot.anchoredPosition.x，不碰 statusAreaRoot 的布局坐标。
    private void AlignStatusContentToBarLeft(Vector2 iconSize)
    {
        if (slotRoot01 == null || statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
            return;

        if (activeStatusRectBuffer.Count == 0)
            return;

        float barHalfW = appliedStyle != null ? appliedStyle.barSize.x * 0.5f : 75f;

        // 血条左端在 runtimeUiRoot 空间的 X
        float barLeftX = slotRoot01.anchoredPosition.x - barHalfW;

        // statusAreaRoot 中心在 runtimeUiRoot 空间的 X
        float statusAreaCenterX = statusRoot.anchoredPosition.x + statusAreaRoot.anchoredPosition.x;

        // LayoutStatusIcons 完成后，第一个图标中心在 statusContentRoot 空间的 X
        float firstIconCenterX = activeStatusRectBuffer[0].anchoredPosition.x;

        // 直接计算 statusContentRoot.anchoredPosition.x 的目标值，
        // 使"statusAreaCenter + contentOffset + iconCenter - halfIcon == barLeftX"成立。
        // 注意：不依赖 contentPos 的当前值，直接设为结果，避免累加误差。
        float targetContentX = barLeftX - statusAreaCenterX - firstIconCenterX + iconSize.x * 0.5f;

        Vector2 contentPos = statusContentRoot.anchoredPosition;
        contentPos.x = targetContentX;
        statusContentRoot.anchoredPosition = contentPos;
    }

    public void ClearStatuses()
    {
        cachedStatuses.Clear();
        cachedStatusDisplayDefinition = null;
        ClearStatusIconViews();
    }

    private void ClearStatusIconViews()
    {
        for (int i = 0; i < runtimeStatusIcons.Count; i++)
        {
            UnitStatusIconView view = runtimeStatusIcons[i];
            if (view == null)
                continue;

            view.ResetView();
            view.gameObject.SetActive(false);
        }

        // 保险：旧版本/回档前生成的图标可能没有登记进 runtimeStatusIcons，直接扫容器，但仍然只清图标，不碰布局根坐标。
        if (statusContentRoot == null)
            return;

        for (int i = 0; i < statusContentRoot.childCount; i++)
        {
            Transform child = statusContentRoot.GetChild(i);
            if (child == null)
                continue;

            UnitStatusIconView view = child.GetComponent<UnitStatusIconView>();
            if (view != null)
                view.ResetView();

            child.gameObject.SetActive(false);
        }
    }


    public void ShowDamageNumber(float amount, Color color, bool isHeal, bool isPositiveCrit = false, bool isNegativeCrit = false)
    {
        if (amount <= 0f)
        {
            Debug.LogWarning($"[DmgNum] amount<=0，跳过 amount={amount}", this);
            return;
        }

        if (autoEnsureStructure)
            EnsureStructure();

        EnsureDamageNumberRootLayout();
        DamageNumberView view = GetOrCreateDamageNumberView();
        if (view == null)
        {
            Debug.LogWarning($"[DmgNum] GetOrCreateDamageNumberView 返回 null，damageNumberContentRoot={damageNumberContentRoot}", this);
            return;
        }

        ApplyDamageNumberStyle(view);

        Vector2 bodyLocalPos = GetRandomBodyDamageNumberPosition();
        bool preferSoft = false;
        view.Play(amount, color, isHeal, bodyLocalPos, preferSoft, isPositiveCrit, isNegativeCrit);
        view.transform.SetAsLastSibling();
    }

    private void EnsureDamageNumberRootLayout()
    {
        if (damageNumberRoot == null || damageNumberContentRoot == null)
            return;

        damageNumberRoot.anchorMin = new Vector2(0.5f, 0.5f);
        damageNumberRoot.anchorMax = new Vector2(0.5f, 0.5f);
        damageNumberRoot.pivot = new Vector2(0.5f, 0.5f);
        damageNumberRoot.anchoredPosition = Vector2.zero;
        damageNumberRoot.sizeDelta = new Vector2(220f, 220f);

        damageNumberContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
        damageNumberContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
        damageNumberContentRoot.pivot = new Vector2(0.5f, 0.5f);
        damageNumberContentRoot.anchoredPosition = Vector2.zero;
        damageNumberContentRoot.sizeDelta = damageNumberRoot.sizeDelta;
    }

    private Vector2 GetRandomBodyDamageNumberPosition()
    {
        if (damageNumberContentRoot == null)
            return Vector2.zero;

        Canvas canvas = damageNumberContentRoot.GetComponentInParent<Canvas>();
        Camera cam = null;

        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        if (cam == null)
            cam = Camera.main;

        // 1. 优先取专用锚点（推荐挂在胸口/上半身）
        Vector3 baseWorld;
        if (damageNumberAnchor != null)
        {
            baseWorld = damageNumberAnchor.position + damageNumberAnchorOffset;
        }
        else
        {
            // 2. 没锚点时，退回到所有 Renderer 的包围盒中上部
            baseWorld = transform.position;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                // 取更靠近胸口的位置，而不是正中心或脚底
                baseWorld = new Vector3(
                    bounds.center.x,
                    bounds.min.y + bounds.size.y * 0.72f,
                    bounds.center.z);
            }
            else
            {
                // 最后兜底，也直接抬高到上半身
                baseWorld = transform.position + Vector3.up * 1.35f;
            }
        }

        // 3. 围绕基准点做“偏上半身”的随机角度 + 多档距离分布
        float angleDeg;
        float anglePick = UnityEngine.Random.value;

        // 主要分布：左上 / 正上 / 右上
        if (anglePick < 0.30f)
        {
            angleDeg = UnityEngine.Random.Range(120f, 155f);   // 左上
        }
        else if (anglePick < 0.70f)
        {
            angleDeg = UnityEngine.Random.Range(72f, 108f);    // 正上附近
        }
        else
        {
            angleDeg = UnityEngine.Random.Range(25f, 60f);     // 右上
        }

        // 少量扩展到两侧偏上，增强分散感，但不往脚下掉
        if (UnityEngine.Random.value < 0.22f)
        {
            angleDeg = UnityEngine.Random.value < 0.5f
                ? UnityEngine.Random.Range(155f, 192f)         // 左侧偏上
                : UnityEngine.Random.Range(-12f, 25f);         // 右侧偏上
        }

        float angle = angleDeg * Mathf.Deg2Rad;

        // 距离做成三档，让分布更散，不全挤在一圈
        float radiusPick = UnityEngine.Random.value;
        float radius;
        if (radiusPick < 0.38f)
        {
            radius = UnityEngine.Random.Range(0.10f, 0.16f);   // 近圈
        }
        else if (radiusPick < 0.80f)
        {
            radius = UnityEngine.Random.Range(0.16f, 0.26f);   // 中圈
        }
        else
        {
            radius = UnityEngine.Random.Range(0.26f, 0.36f);   // 外圈
        }

        // 椭圆分布：横向略宽，纵向略窄，更贴近身体范围
        float offsetX = Mathf.Cos(angle) * radius * 1.35f;
        float offsetY = Mathf.Sin(angle) * radius * 1.00f;

        Vector3 worldPoint =
            baseWorld +
            transform.right * offsetX +
            Vector3.up * offsetY;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPoint);

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            damageNumberContentRoot,
            screenPoint,
            canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam,
            out localPoint);

        localPoint.x += UnityEngine.Random.Range(-8f, 8f);
        localPoint.y += UnityEngine.Random.Range(-5f, 5f);

        return localPoint;
    }

    private DamageNumberView GetOrCreateDamageNumberView()
    {
        if (damageNumberContentRoot == null)
            return null;

        for (int i = 0; i < runtimeDamageNumbers.Count; i++)
        {
            DamageNumberView item = runtimeDamageNumbers[i];
            if (item != null && !item.IsPlaying)
                return item;
        }

        GameObject go = new GameObject($"DamageNumber_{runtimeDamageNumbers.Count}", typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup), typeof(DamageNumberView));
        go.transform.SetParent(damageNumberContentRoot, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(128f, 32f);

        DamageNumberView view = go.GetComponent<DamageNumberView>();
        view.EnsureStructure();
        ApplyDamageNumberStyle(view);
        go.layer = runtimeUiRoot != null ? runtimeUiRoot.gameObject.layer : go.layer;
        runtimeDamageNumbers.Add(view);
        return view;
    }

    private void ApplyDamageNumberStyle(DamageNumberView view)
    {
        if (view == null)
            return;

        if (appliedStyle != null && appliedStyle.damageNumberFontAsset != null)
            view.ApplyFontAsset(appliedStyle.damageNumberFontAsset);
    }

    private void RebuildBgLayers(OverheadBarStyleAsset style)
    {
        runtimeBgImages.Clear();

        if (bgLayerRoot == null || style == null)
            return;

        HashSet<string> usedNames = new HashSet<string>();

        for (int i = 0; i < style.backgroundLayers.Count; i++)
        {
            OverheadBarBackgroundLayer layer = style.backgroundLayers[i];
            if (layer == null)
                continue;

            string layerName = string.IsNullOrWhiteSpace(layer.layerName) ? $"BgLayer_{i}" : layer.layerName;
            string uniqueName = layerName;
            int suffix = 1;
            while (!usedNames.Add(uniqueName))
            {
                uniqueName = $"{layerName}_{suffix}";
                suffix++;
            }

            Transform existing = bgLayerRoot.Find(uniqueName);
            RectTransform rt;
            RawImage image;

            if (existing != null)
            {
                rt = existing as RectTransform;
                image = existing.GetComponent<RawImage>();

                if (rt == null)
                    rt = existing.gameObject.GetComponent<RectTransform>() ?? existing.gameObject.AddComponent<RectTransform>();

                if (image == null)
                    image = existing.gameObject.AddComponent<RawImage>();
            }
            else
            {
                GameObject go = new GameObject(uniqueName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.transform.SetParent(bgLayerRoot, false);
                rt = go.GetComponent<RectTransform>();
                image = go.GetComponent<RawImage>();
            }

            Vector2 size = layer.sizeOverride == Vector2.zero ? appliedStyle.barSize : layer.sizeOverride;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(layer.offset.x, -layer.offset.y);
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, layer.rotationZ);
            rt.localScale = Vector3.one;

            image.texture = layer.texture;
            image.color = layer.useTint ? layer.tint : Color.white;
            image.raycastTarget = false;

            runtimeBgImages.Add(image);
            rt.SetSiblingIndex(i);
        }

        for (int i = bgLayerRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = bgLayerRoot.GetChild(i);
            if (!usedNames.Contains(child.name))
                SafeDestroy(child.gameObject);
        }

        bgLayerRoot.SetAsFirstSibling();
    }

    private void ClearBgLayers()
    {
        runtimeBgImages.Clear();

        if (bgLayerRoot == null)
            return;

        for (int i = bgLayerRoot.childCount - 1; i >= 0; i--)
            SafeDestroy(bgLayerRoot.GetChild(i).gameObject);
    }

    private void RefreshBarVisuals()
    {
        if (slotView01 == null || fillMaskRoot == null || damageRefMaskRoot == null ||
            fillImage == null || damageReferenceImage == null)
            return;

        Vector2 total = slotView01.sizeDelta;
        float left = appliedStyle != null ? appliedStyle.paddingLeft : 0f;
        float right = appliedStyle != null ? appliedStyle.paddingRight : 0f;
        float top = appliedStyle != null ? appliedStyle.paddingTop : 0f;
        float bottom = appliedStyle != null ? appliedStyle.paddingBottom : 0f;

        float innerWidth = Mathf.Max(0f, total.x - left - right);
        float innerHeight = Mathf.Max(0f, total.y - top - bottom);
        float verticalOffset = (bottom - top) * 0.5f;

        damageRefMaskRoot.anchoredPosition = new Vector2(left, verticalOffset);
        fillMaskRoot.anchoredPosition = new Vector2(left, verticalOffset);

        damageRefMaskRoot.sizeDelta = new Vector2(innerWidth * Mathf.Clamp01(damageReferencePercent), innerHeight);
        fillMaskRoot.sizeDelta = new Vector2(innerWidth * Mathf.Clamp01(currentPercent), innerHeight);

        ConfigureBarImage(damageReferenceImage.rectTransform, new Vector2(innerWidth, innerHeight));
        ConfigureBarImage(fillImage.rectTransform, new Vector2(innerWidth, innerHeight));

        if (slotCanvasGroup != null)
            slotCanvasGroup.alpha = currentDisplayAlpha;
        else if (slotRoot01 != null)
            slotRoot01.gameObject.SetActive(currentDisplayAlpha > 0.001f);
    }

    private void UpdateDisplayAlphaTarget()
    {
        bool hideWhenFull = appliedStyle != null && appliedStyle.hideWhenFull;
        float threshold = appliedStyle != null ? appliedStyle.fullHpThreshold : 0.999f;
        targetDisplayAlpha = (!hideWhenFull || targetPercent < threshold) ? 1f : 0f;
    }

    private void ConfigureDefaultAnchors()
    {
        if (runtimeUiRoot != null)
        {
            runtimeUiRoot.anchorMin = new Vector2(0.5f, 0.5f);
            runtimeUiRoot.anchorMax = new Vector2(0.5f, 0.5f);
            runtimeUiRoot.pivot = new Vector2(0.5f, 0.5f);
            runtimeUiRoot.anchoredPosition = Vector2.zero;
            runtimeUiRoot.sizeDelta = new Vector2(256f, 128f);
        }

        if (nameRoot != null)
            SetRectCenter(nameRoot, new Vector2(0f, 16f));

        if (slotRoot01 != null)
            SetRectCenter(slotRoot01, Vector2.zero);

        if (slotView01 != null)
        {
            SetRectCenter(slotView01, Vector2.zero);
            slotView01.sizeDelta = new Vector2(150f, 14f);
        }

        if (bgLayerRoot != null)
            SetRectCenter(bgLayerRoot, Vector2.zero);

        if (statusRoot != null)
            SetRectCenter(statusRoot, Vector2.zero);

        if (statusAreaRoot != null)
        {
            statusAreaRoot.anchorMin = new Vector2(0.5f, 0.5f);
            statusAreaRoot.anchorMax = new Vector2(0.5f, 0.5f);
            statusAreaRoot.pivot = new Vector2(0.5f, 0.5f);
            statusAreaRoot.anchoredPosition = Vector2.zero;
            statusAreaRoot.sizeDelta = new Vector2(120f, 28f);
        }

        if (statusContentRoot != null)
        {
            statusContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
            statusContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
            statusContentRoot.pivot = new Vector2(0.5f, 0.5f);
            statusContentRoot.anchoredPosition = Vector2.zero;
            statusContentRoot.sizeDelta = new Vector2(120f, 28f);
        }

        if (damageNumberRoot != null)
        {
            damageNumberRoot.anchorMin = new Vector2(0.5f, 0.5f);
            damageNumberRoot.anchorMax = new Vector2(0.5f, 0.5f);
            damageNumberRoot.pivot = new Vector2(0.5f, 0.5f);
            damageNumberRoot.anchoredPosition = Vector2.zero;
            damageNumberRoot.sizeDelta = new Vector2(180f, 80f);
        }

        if (damageNumberContentRoot != null)
        {
            damageNumberContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
            damageNumberContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
            damageNumberContentRoot.pivot = new Vector2(0.5f, 0.5f);
            damageNumberContentRoot.anchoredPosition = Vector2.zero;
            damageNumberContentRoot.sizeDelta = new Vector2(180f, 80f);
        }
    }

    private void ApplyUnitDefinitionTransformOverrides()
    {
        if (runtimeUiRoot == null)
            return;

        Vector3 localPosition = Vector3.zero;

        float offsetX = 0f;
        float offsetY = 0f;
        bool hasOffsetX = TryGetFloatFromUnitDefinition(new[] { "overheadUiOffsetX", "unitUiOffsetX" }, out offsetX);
        bool hasOffsetY = TryGetFloatFromUnitDefinition(new[] { "overheadUiOffsetY", "unitUiOffsetY" }, out offsetY);

        if (hasOffsetX)
            localPosition.x += offsetX;

        if (hasOffsetY)
            localPosition.y += offsetY;

        runtimeUiRoot.localPosition = localPosition;
        runtimeUiRoot.localEulerAngles = Vector3.zero;
        // 缩放不再统一强制——不同预制体原本authored的runtimeUiRoot缩放不一定一样
        // (手搓的老预制体可能本来就带了一个很小的缩放)，强行统一成同一个值对
        // 其中一批单位来说不是"归零"而是"改成错的"，这正是这次巨大白块的成因。
        // 缩放交给EnsureSingleRuntimeRoot创建节点时的默认值(新节点=1，已存在的
        // 节点=沿用原来就有的值)，不在这里覆盖。

        if (debugLogs)
        {
            string anchorInfo = overheadAnchor != null ? overheadAnchor.localPosition.ToString("F3") : "null";
            Log($"ApplyUnitDefinitionTransformOverrides | overheadAnchor.localPosition={anchorInfo} | defOffsetX={(hasOffsetX ? offsetX.ToString() : "none")} | defOffsetY={(hasOffsetY ? offsetY.ToString() : "none")} | runtimeUiRoot.localPosition={runtimeUiRoot.localPosition}");
        }
    }

    private bool TryGetFloatFromUnitDefinition(string[] names, out float value)
    {
        value = 0f;
        if (unitDefinition == null || names == null)
            return false;

        Type type = unitDefinition.GetType();
        for (int i = 0; i < names.Length; i++)
        {
            FieldInfo field = type.GetField(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                value = (float)field.GetValue(unitDefinition);
                return true;
            }

            PropertyInfo property = type.GetProperty(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.PropertyType == typeof(float))
            {
                value = (float)property.GetValue(unitDefinition, null);
                return true;
            }
        }

        return false;
    }

    private int CountVisibleStatuses(IReadOnlyList<StatusViewData> statuses)
    {
        if (statuses == null)
            return 0;

        int count = 0;
        for (int i = 0; i < statuses.Count; i++)
        {
            if (IsRenderableStatus(statuses[i]))
                count++;
        }

        return count;
    }

    private void FillVisibleStatusesBuffer(IReadOnlyList<StatusViewData> statuses, List<StatusViewData> result)
    {
        result.Clear();
        if (statuses == null)
            return;

        int limit = maxRuntimeStatusIconPool > 0 ? maxRuntimeStatusIconPool : int.MaxValue;
        for (int i = 0; i < statuses.Count; i++)
        {
            if (!IsRenderableStatus(statuses[i]))
                continue;

            result.Add(statuses[i]);
            if (result.Count >= limit)
                break;
        }
    }

    private bool IsRenderableStatus(StatusViewData data)
    {
        if (!data.visible)
            return false;

        if (data.icon == null)
            return false;

        bool usesDurationMask = !data.hideDurationMask && data.durationFillMode != StatusDurationFillMode.None;
        if (usesDurationMask && data.remainingNormalized <= 0.001f)
            return false;

        return true;
    }

    private void EnsureStatusIconPool(int count)
    {
        if (statusContentRoot == null)
            return;

        int hardCap = Mathf.Max(1, maxRuntimeStatusIconPool);
        int target = Mathf.Clamp(Mathf.Max(1, count), 1, hardCap);

        for (int i = runtimeStatusIcons.Count - 1; i >= 0; i--)
        {
            if (runtimeStatusIcons[i] == null)
                runtimeStatusIcons.RemoveAt(i);
        }

        // Reuse existing children first. This is important after hot reloads / prefab revisions where icons already exist
        // under StatusContentRoot but the non-serialized runtime list has been rebuilt empty.
        for (int i = 0; i < statusContentRoot.childCount && runtimeStatusIcons.Count < target; i++)
        {
            UnitStatusIconView existing = statusContentRoot.GetChild(i).GetComponent<UnitStatusIconView>();
            if (existing != null && !runtimeStatusIcons.Contains(existing))
            {
                existing.EnsureStructure();
                runtimeStatusIcons.Add(existing);
            }
        }

        while (runtimeStatusIcons.Count < target)
        {
            GameObject go = new GameObject($"StatusIcon_{runtimeStatusIcons.Count}", typeof(RectTransform), typeof(UnitStatusIconView));
            go.transform.SetParent(statusContentRoot, false);

            UnitStatusIconView view = go.GetComponent<UnitStatusIconView>();
            view.EnsureStructure();

            runtimeStatusIcons.Add(view);
        }
    }

    private void CompactStatusIconChildren()
    {
        if (statusContentRoot == null)
            return;

        int hardCap = Mathf.Max(1, maxRuntimeStatusIconPool);
        int kept = 0;

        for (int i = statusContentRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = statusContentRoot.GetChild(i);
            if (child == null)
                continue;

            UnitStatusIconView view = child.GetComponent<UnitStatusIconView>();
            if (view == null)
                continue;

            kept++;
            if (kept > hardCap)
            {
                runtimeStatusIcons.Remove(view);
                SafeDestroy(child.gameObject);
            }
        }
    }

    private void EnsureCanvasComponents(RectTransform root)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
            canvas = root.gameObject.AddComponent<Canvas>();

        // 改回 WorldSpace——挂在单位身上、跟着单位在地图里到处跑，不再是屏幕投影。
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 150;

        if (root.GetComponent<CanvasScaler>() == null)
            root.gameObject.AddComponent<CanvasScaler>();

        if (root.GetComponent<GraphicRaycaster>() == null)
            root.gameObject.AddComponent<GraphicRaycaster>();
    }

    private CanvasGroup EnsureCanvasGroup(RectTransform target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
            group = target.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private Transform EnsureTransformChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private RectTransform EnsureRectChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        RectTransform rect = child as RectTransform;
        if (rect != null)
            return rect;

        string safeName = child != null ? (childName + "_RuntimeUI") : childName;
        Transform runtimeChild = parent.Find(safeName);
        RectTransform runtimeRect = runtimeChild as RectTransform;
        if (runtimeRect != null)
            return runtimeRect;

        GameObject go = new GameObject(safeName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private RectTransform EnsureMaskRoot(Transform parent, string childName)
    {
        RectTransform rect = EnsureRectChild(parent, childName);
        RectMask2D mask = rect.GetComponent<RectMask2D>();
        if (mask == null)
            mask = rect.gameObject.AddComponent<RectMask2D>();
        return rect;
    }

    private TextMeshProUGUI EnsureTMPChild(Transform parent, string childName, string defaultText)
    {
        RectTransform rect = EnsureRectChild(parent, childName);
        TextMeshProUGUI tmp = rect.GetComponent<TextMeshProUGUI>();
        bool isNew = tmp == null;
        if (isNew)
            tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();

        tmp.text = string.IsNullOrWhiteSpace(tmp.text) ? defaultText : tmp.text;
        tmp.raycastTarget = false;
        tmp.alignment = TextAlignmentOptions.Center;

        // fontSize/color只在刚创建这个组件的时候给一个占位默认值——这个方法会被
        // EnsureStructure()反复调用(比如ApplyStatusAreaStyle内部又调用一次
        // EnsureStructure())，如果每次都无条件覆盖，会把ApplyNameStyle()刚从样式
        // 资产设进去的真实字号/颜色冲掉，表现为"字号时大时小、跟改的数值对不上"。
        if (isNew)
        {
            tmp.fontSize = 13f;
            tmp.color = Color.white;
        }

        return tmp;
    }

    private RawImage EnsureRawImageChild(Transform parent, string childName)
    {
        RectTransform rect = EnsureRectChild(parent, childName);
        RawImage image = rect.GetComponent<RawImage>();
        if (image == null)
            image = rect.gameObject.AddComponent<RawImage>();

        image.raycastTarget = false;
        return image;
    }

    private void ConfigureMaskRoot(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private void ConfigureBarImage(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private void SetRectCenter(RectTransform rect, Vector2 anchored)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(anchored.x, -anchored.y);
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null)
            return;

        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    private void TryAutoResolveUnitDefinition()
    {
        if (unitDefinition != null)
            return;

        if (runtimeBinder == null)
            runtimeBinder = GetComponent("UnitDefinitionRuntimeBinder") ?? GetComponentInParent(typeof(MonoBehaviour), true);

        if (runtimeBinder != null)
        {
            if (TryExtractUnitDefinition(runtimeBinder, out UnitDefinition explicitDef))
            {
                unitDefinition = explicitDef;
                Log("Resolved UnitDefinition from runtimeBinder.");
                return;
            }
        }

        MonoBehaviour[] behaviours = GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            if (TryExtractUnitDefinition(behaviour, out UnitDefinition value))
            {
                unitDefinition = value;
                Log("Auto resolved UnitDefinition from component: " + behaviour.GetType().Name);
                return;
            }
        }
    }

    private bool TryExtractUnitDefinition(object source, out UnitDefinition value)
    {
        value = null;
        if (source == null)
            return false;

        Type type = source.GetType();
        string[] preferredNames = { "unitDefinition", "definition", "currentDefinition", "runtimeDefinition", "resolvedDefinition", "UnitDefinition" };

        for (int i = 0; i < preferredNames.Length; i++)
        {
            FieldInfo field = type.GetField(preferredNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && typeof(UnitDefinition).IsAssignableFrom(field.FieldType))
            {
                value = field.GetValue(source) as UnitDefinition;
                if (value != null)
                    return true;
            }

            PropertyInfo property = type.GetProperty(preferredNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && typeof(UnitDefinition).IsAssignableFrom(property.PropertyType))
            {
                value = property.GetValue(source, null) as UnitDefinition;
                if (value != null)
                    return true;
            }
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (typeof(UnitDefinition).IsAssignableFrom(field.FieldType))
            {
                value = field.GetValue(source) as UnitDefinition;
                if (value != null)
                    return true;
            }
        }

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (!property.CanRead)
                continue;

            if (typeof(UnitDefinition).IsAssignableFrom(property.PropertyType))
            {
                try
                {
                    value = property.GetValue(source, null) as UnitDefinition;
                    if (value != null)
                        return true;
                }
                catch { }
            }
        }

        return false;
    }

    private void DebugDumpOffsets(string stage)
    {
        if (!debugLogs)
            return;

        Vector3 anchorLocal = overheadAnchor != null ? overheadAnchor.localPosition : Vector3.zero;
        Vector3 runtimeLocal = runtimeUiRoot != null ? runtimeUiRoot.localPosition : Vector3.zero;
        Vector2 barOffset = appliedStyle != null ? appliedStyle.hpBarOffset : Vector2.zero;
        Vector2 slotPos = slotRoot01 != null ? slotRoot01.anchoredPosition : Vector2.zero;
        Vector2 statusPos = statusAreaRoot != null ? statusAreaRoot.anchoredPosition : Vector2.zero;

        Log($"{stage} | anchorLocal={anchorLocal} | runtimeLocal={runtimeLocal} | hpBarOffset={barOffset} | slotRoot01={slotPos} | statusAreaRoot={statusPos}");
    }

    private void SafeDestroy(GameObject go)
    {
        if (go == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (go != null)
                    DestroyImmediate(go);
            };
            return;
        }
#endif
        Destroy(go);
    }

    private void SafeDestroyComponent(Component comp)
    {
        if (comp == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (comp != null)
                    DestroyImmediate(comp);
            };
            return;
        }
#endif
        Destroy(comp);
    }

    private void Log(string message)
    {
        if (!debugLogs)
            return;

        Debug.Log("[UnitOverheadUIView] " + message, this);
    }
}