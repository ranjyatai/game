using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 把地图场景实际的主光源（Main_DirectionalLight，地图环境结构里固定命名的那个）
/// 亮度/颜色喂给Spine角色shader已有的"环境色调接收接口"（_SkyPrison_EnvTint/
/// _SkyPrison_EnvExposure/_SkyPrison_EnvDarken——Spine-Skeleton.shader里本来就有，
/// 一直没人接），让角色明暗跟着地图场景的光照氛围走，避免"背景很暗、角色却是
/// 死白的"这种割裂感。风格化控制、非物理模拟，跟之前废弃掉的法线贴图实时光照
/// 是两套不同的东西。
/// 通过MaterialPropertyBlock喂参数，不创建材质实例、不影响SRP Batching。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SkyPrisonCharacterEnvironmentLightReceiver : MonoBehaviour
{
    // 地图环境结构生成出来的那盏灯实际叫 MapDirectionalLight，而这里原本只找
    // Main_DirectionalLight——名字对不上，sceneMainLight 永远是 null，Apply() 又是
    // "找不到就直接 return"，于是整个组件静默失效，角色永远不受环境影响。
    // 两个名字都认，再退回场景里任意一盏平行光兜底。
    private static readonly string[] MainLightNames = { "MapDirectionalLight", "Main_DirectionalLight" };

    private static readonly int EnvTintId = Shader.PropertyToID("_SkyPrison_EnvTint");
    private static readonly int EnvTintStrengthId = Shader.PropertyToID("_SkyPrison_EnvTintStrength");
    private static readonly int EnvDarkenId = Shader.PropertyToID("_SkyPrison_EnvDarken");
    private static readonly int EnvExposureId = Shader.PropertyToID("_SkyPrison_EnvExposure");

    // 逐像素灯光：着色器按数组里的灯算每个像素的受光（见 Spine-Skeleton.shader / SpineOcclusionComposite.shader）。
    private const int MaxShaderLamps = 4;
    private static readonly int LampCountId = Shader.PropertyToID("_SkyPrison_EnvLampCount");
    private static readonly int LampPosId = Shader.PropertyToID("_SkyPrison_EnvLampPos");
    private static readonly int LampDirId = Shader.PropertyToID("_SkyPrison_EnvLampDir");
    private static readonly int LampParamId = Shader.PropertyToID("_SkyPrison_EnvLampParam");
    private static readonly int LampDarkenGainId = Shader.PropertyToID("_SkyPrison_EnvLampDarkenGain");

    private readonly Vector4[] _lampPos = new Vector4[MaxShaderLamps];
    private readonly Vector4[] _lampDir = new Vector4[MaxShaderLamps];
    private readonly Vector4[] _lampParam = new Vector4[MaxShaderLamps];
    private readonly Vector4[] _lampColor = new Vector4[MaxShaderLamps];
    private static readonly int LampColorId = Shader.PropertyToID("_SkyPrison_EnvLampColor");
    private readonly List<Light> _nearLamps = new List<Light>(8);

    private static readonly int ShadowColorId = Shader.PropertyToID("_Color");
    private SpriteRenderer _shadowRenderer;
    private MaterialPropertyBlock _shadowBlock;
    private float _shadowBaseAlpha = -1f;
    private float _shadowSearchTimer;

    // 轮廓影子：UpdateLampShadow 里算好参数，beginCameraRendering 里用 DrawMesh 画。
    private static readonly int SilGroundYId = Shader.PropertyToID("_SkyPrison_LampShadowGroundY");
    private static readonly int SilShearId = Shader.PropertyToID("_SkyPrison_LampShadowShear");
    // 投影灯的参数：影子逐像素按「这盏灯照到这块地面多少」压暗，光圈外自然消失。
    private static readonly int SilLightId = Shader.PropertyToID("_SkyPrison_LampShadowLight"); // xyz = 位置, w = range
    private static readonly int SilSpotId = Shader.PropertyToID("_SkyPrison_LampShadowSpot");   // xyz = 朝向, w = 1 聚光 / 0 点光
    private static readonly int SilConeId = Shader.PropertyToID("_SkyPrison_LampShadowCone");   // x = cos(半角), y = cos(内半角)
    private Vector4 _silLight, _silSpot, _silCone;
    private static readonly int SilMainTexId = Shader.PropertyToID("_MainTex");
    private const string SilhouetteShaderName = "Hidden/SkyPrison/LampSilhouetteShadow";
    private Material _silMaterial;
    private MaterialPropertyBlock _silBlock;
    private MeshRenderer _silSource;
    private bool _silActive;
    private Vector4 _silShear;
    private float _silGroundY;
    private int _silLayer;

    private SpriteRenderer _lampShadowSr;
    private MaterialPropertyBlock _lampShadowBlock;

    // 深度贴花版轮廓影子（见 Hidden/SkyPrison/LampShadowDepthDecal）：先把扶正后的角色轮廓画进一张小 RT，
    // 再画一个盒体贴花，每个像素读深度还原世界坐标、沿投影方向回推到角色平面去采样 RT。
    // 影子因此能落在屏幕上看到的任何表面（斜坡、高台、箱子、墙）。
    private const string DecalShaderName = "Hidden/SkyPrison/LampShadowDepthDecal";
    private static readonly int DecalMaskTexId = Shader.PropertyToID("_SkyPrison_LampMaskTex");
    private static readonly int DecalFootId = Shader.PropertyToID("_SkyPrison_LampDecalFoot");
    private static readonly int DecalRightId = Shader.PropertyToID("_SkyPrison_LampDecalRight");
    private static readonly int DecalDvId = Shader.PropertyToID("_SkyPrison_LampDecalDv");
    // 轮廓贴图覆盖的范围不能写死：Spine 网格在「扶正坐标」里的高度由骨骼缩放决定，和碰撞胶囊的高度没有关系
    // （这个角色大约是胶囊的两倍高）。写死 2.3 米时，贴图只盖到腰，头和上半身被整个裁掉。
    // 每帧由网格包围盒量出真实范围，只增不减（动画时包围盒会变，贴图映射跟着抖会让影子闪）。
    private const float DecalMinHalfWidth = 0.5f;
    private const float DecalMinHeight = 1.5f;
    private const float DecalExtentMargin = 1.1f;
    // 脚底贴地：精灵最低点高出根节点多少就扣掉多少（上限 0.3），再加这点接触余量，让灯影起点压在脚下、被角色自己盖住。
    private const float DecalMaxSoleOffset = 0.3f;
    private const float DecalContactShiftMeters = 0.27f;  // 影子朝灯方向压进脚底的水平距离
    private const float DecalContactBiasMin = 0.15f;
    private const float DecalContactBiasMax = 0.5f;
    private const int DecalMaskWidth = 256;
    private const int DecalMaskHeight = 512;
    private float _decalHalfWidth;
    private float _decalMaskHeightM;
    private Material _decalMaterial;
    private int _decalMaskPass = -1;
    private RenderTexture _decalMaskRt;
    private CommandBuffer _decalMaskCmd;
    private MaterialPropertyBlock _decalBlock;
    private Mesh _decalCube;
    private bool _useDecal;
    private Vector4 _decalFoot, _decalRight, _decalDv, _decalClip = new Vector4(0f, 1f, 1e4f, 0f);
    private readonly RaycastHit[] _decalClipHits = new RaycastHit[8];
    private static readonly int DecalClipId = Shader.PropertyToID("_SkyPrison_LampDecalClip");
    private Matrix4x4 _decalMatrix;

    /// <summary>这一帧主相机要画的轮廓影子，供 HologramCoverageFeature 先画一遍去重计数图
    /// （部件重叠处乘法混合会被乘两次、更暗）。在 beginCameraRendering 里登记，
    /// URP 之后才调用 AddRenderPasses，所以 Feature 读到的是这一帧的。</summary>
    public struct SilhouetteDraw
    {
        public Mesh mesh;
        public Matrix4x4 matrix;
        public int submesh;
        public Material material;
        public MaterialPropertyBlock block;
    }

    private static readonly List<SilhouetteDraw> s_SilhouetteDraws = new List<SilhouetteDraw>();
    private static int s_SilhouetteDrawFrame = -1;
    private readonly List<MaterialPropertyBlock> _silBlocksPerSubmesh = new List<MaterialPropertyBlock>();

    /// <summary>本帧登记的轮廓影子；不是本帧登记的返回空。</summary>
    public static IReadOnlyList<SilhouetteDraw> CurrentSilhouetteDraws =>
        s_SilhouetteDrawFrame == Time.frameCount ? (IReadOnlyList<SilhouetteDraw>)s_SilhouetteDraws : System.Array.Empty<SilhouetteDraw>();

    [Header("场景主光源")]
    [Tooltip("留空则自动按名字查找场景里的 Main_DirectionalLight")]
    public Light sceneMainLight;

    [Header("响应参数")]
    [Tooltip("场景主光强度达到这个值时，角色按满亮度显示，不再继续变暗")]
    public float referenceLightIntensity = 1f;
    [Tooltip("完全没光时，角色最多压暗到这个程度。0.55 是给地面白天场景留的旧默认值，" +
        "在地下会显得角色明显浮在画面上；地下建议 0.85~0.9。不要设成 1——全黑会让" +
        "玩家在漆黑处彻底找不到自己的角色，那是可读性缺陷不是氛围。")]
    [Range(0f, 1f)] public float maxDarken = 0.92f;
    [Tooltip("场景光颜色带入角色身上的强度，不用调太高，避免整个角色变色")]
    [Range(0f, 1f)] public float tintStrength = 0.3f;

    [Tooltip("是否响应场景里的点光/聚光。地下场景必须开——那里所有光都来自灯具，" +
        "只看主平行光的话角色站在灯下和站在漆黑角落一样暗。")]
    public bool respondToLamps = true;

    [Tooltip("灯光按像素计算：把附近最多 4 盏灯的位置/范围/锥角传给角色着色器，每个像素按自己的世界坐标" +
        "算受光——被光照到的身体部位亮、没照到的部位暗。关掉则退回整个角色统一一个亮度。")]
    public bool perPixelLamps = true;

    [Header("脚底投影")]
    [Tooltip("灯光越强，脚底投影越明显：投影强度 = 材质原强度 × (1 + 这个值 × 脚底处的灯光亮度 0~1)。" +
        "0 = 不随灯光变化。只调强度，不动投影的位置和形状，不会和单位定义里锁定的投影变换冲突。")]
    [Range(0f, 3f)] public float shadowLampBoost = 1.2f;

    [Tooltip("灯光投影：在灯的反方向额外画一条被拉长的影子（脚下原来那块小椭圆保留）。灯越亮影子越深，" +
        "灯在正上方时影子缩回脚下。只在 Play 里创建，不会存进场景。")]
    public bool lampShadow = true;
    [Tooltip("角色高度（米），用来按灯的高度和水平距离估算影子长度。")]
    public float lampShadowCharacterHeight = 1.7f;
    [Tooltip("影子最长多少米。")]
    public float lampShadowMaxLength = 3.5f;
    [Tooltip("灯影长度的风格化放大倍数。灯挂得很高（十几米）时按真实几何算出来的影子只有 0.4～1 米，压成脚边一条细尾巴、认不出是人。\n乘上这个倍数再夹到 [最短, 最长]；1 = 完全按几何。")]
    [Range(1f, 5f)] public float lampShadowLengthScale = 2.5f;
    [Tooltip("灯影最短长度（米）。灯离脚太近、影子被压到很短时也保证能看出人形。")]
    [Range(0.2f, 3f)] public float lampShadowMinLength = 1.2f;
    [Tooltip("影子宽度（米，地面上垂直于影子方向的宽度）。")]
    public float lampShadowWidth = 0.9f;
    [Tooltip("影子用角色自己的轮廓（把角色网格压到地面上沿背离灯的方向倾斜拉长）。关掉则用一块椭圆软影子。" +
        "轮廓版是平行倾斜，不会出现真实点光源阴影那种「离灯近的头被放大」的反透视。")]
    public bool lampShadowSilhouette = true;
    [Tooltip("轮廓影子读相机深度图，落在屏幕上看到的任何表面：斜坡、高台、箱子、墙面都能接住影子。\n" +
        "关掉则退回旧版：影子压在脚底高度的一张水平面上，并且会被「前景物体模板」拒绝——" +
        "站在高台、斜坡这类装饰物自己的表面上时，影子会整个消失。")]
    public bool lampShadowDepthProjection = true;

    [Tooltip("采样点相对角色根节点的高度。取身体中段而不是脚底——脚底容易被地面" +
        "灯光衰减的边缘扫到，角色走两步就忽明忽暗。")]
    public float lampSampleHeight = 1.2f;

    [Tooltip("逐像素受光：地面被一盏灯照到这个亮度（URP 物理衰减：强度/距离²）时，角色按满亮度显示。\n" +
             "和灯光投影着色器里的 SKYPRISON_LAMP_SHADOW_FULL_BRIGHTNESS 保持一致，角色亮不亮和地面亮不亮才对得上。")]
    public float lampFullBrightness = 0.25f;

    [Header("作用目标")]
    [Tooltip("勾选时忽略下面手动拖的列表，每秒自动重新收集自己子物体下的所有Renderer" +
        "（包括换装/运行时才生成的Spine渲染物体）。不勾选则只用手动拖的列表，自己管理。")]
    public bool autoFindRenderers = true;
    [Tooltip("Auto Find Renderers关闭时才生效——要跟随场景光照的角色渲染器")]
    public Renderer[] targetRenderers;

    private MaterialPropertyBlock _block;
    private float _rendererRefreshTimer;

    private void OnEnable()
    {
        _block = new MaterialPropertyBlock();
        ResolveMainLight();
        RefreshTargetRenderers();
    }

    private void LateUpdate()
    {
        // Spine角色的实际渲染物体可能是换装/初始化流程运行时才创建的子物体，
        // 挂点这个组件的时候未必已经存在——持续每秒重新收集一次，保证换装/
        // 重新生成之后也能自动跟上，不用手动重新拖引用。
        _rendererRefreshTimer -= Time.unscaledDeltaTime;
        if (_rendererRefreshTimer <= 0f)
        {
            _rendererRefreshTimer = 1f;
            RefreshTargetRenderers();
        }

        Apply();
    }

    private void RefreshTargetRenderers()
    {
        if (!autoFindRenderers)
            return;

        targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ResolveMainLight()
    {
        if (sceneMainLight != null) return;

        foreach (string n in MainLightNames)
        {
            GameObject found = GameObject.Find(n);
            if (found == null) continue;
            sceneMainLight = found.GetComponent<Light>();
            if (sceneMainLight != null) return;
        }

        // 名字都对不上就退回场景里任意一盏平行光。找不到也没关系——地下场景本来
        // 就不该有平行光，那时候亮度完全由环境光和灯具决定。
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l != null && l.type == LightType.Directional) { sceneMainLight = l; return; }
        }
    }

    private void Apply()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        Vector3 samplePos = transform.position + Vector3.up * lampSampleHeight;
        Color lightColor;
        float lightLevel = EvaluateLightLevel(samplePos, out lightColor);

        float darken = Mathf.Lerp(maxDarken, 0f, lightLevel);

        // 逐像素模式：darken 只含环境光+主平行光（见 EvaluateLightLevel），灯的部分交给着色器按像素减。
        // 着色器里：darken_像素 = darken - 灯光亮度_像素 × 增益；增益 = maxDarken / 参考强度，
        // 这样「灯下满亮度」和原来整体模式的结果一致，只是变成每个像素各算各的。
        UpdateShadowStrength();
        UpdateLampShadow();

        int lampCount = perPixelLamps ? CollectShaderLamps(samplePos) : 0;
        // 灯的强度已经按 lampFullBrightness 归一（level = 1 即照满），增益就是 maxDarken。
        float darkenGain = maxDarken;

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_block);
            _block.SetColor(EnvTintId, lightColor);
            _block.SetFloat(EnvTintStrengthId, tintStrength);
            _block.SetFloat(EnvDarkenId, darken);
            _block.SetFloat(EnvExposureId, 0f);
            _block.SetFloat(LampCountId, lampCount);
            _block.SetFloat(LampDarkenGainId, darkenGain);
            if (lampCount > 0)
            {
                _block.SetVectorArray(LampPosId, _lampPos);
                _block.SetVectorArray(LampDirId, _lampDir);
                _block.SetVectorArray(LampParamId, _lampParam);
                _block.SetVectorArray(LampColorId, _lampColor);
            }
            r.SetPropertyBlock(_block);
        }
    }

    private void OnDisable()
    {
        if (_lampShadowSr != null)
            _lampShadowSr.enabled = false;
        _silActive = false;
        _useDecal = false;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        ReleaseDecal();
    }

    /// <summary>找角色的主 Spine 网格渲染器（走遮挡合成着色器的那个）。轮廓影子用它的网格和图集。</summary>
    private MeshRenderer FindSilhouetteSource()
    {
        if (_silSource != null)
            return _silSource;

        if (targetRenderers == null)
            return null;

        foreach (Renderer r in targetRenderers)
        {
            MeshRenderer mr = r as MeshRenderer;
            if (mr == null || mr.sharedMaterial == null) continue;
            if (mr.sharedMaterial.shader.name.Contains("SpineOcclusionComposite") && mr.GetComponent<MeshFilter>() != null)
            {
                _silSource = mr;
                return _silSource;
            }
        }
        return null;
    }

    private bool PrepareSilhouetteMaterial()
    {
        if (_silBlock == null)
            _silBlock = new MaterialPropertyBlock();

        if (_silMaterial != null)
            return true;

        Shader s = Shader.Find(SilhouetteShaderName);
        if (s == null)
            return false;

        _silMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    /// <summary>深度贴花版需要的资源：着色器材质、轮廓 RT、命令缓冲、盒体网格。缺任何一样就退回旧版。</summary>
    private bool PrepareDecal()
    {
        if (_decalMaterial == null)
        {
            Shader s = Shader.Find(DecalShaderName);
            if (s == null || !s.isSupported)
                return false;
            _decalMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            _decalMaskPass = _decalMaterial.FindPass("Mask");
            if (_decalMaskPass < 0)
                return false;
        }

        if (_decalMaskRt == null)
        {
            RenderTextureFormat fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                ? RenderTextureFormat.R8
                : RenderTextureFormat.ARGB32;
            _decalMaskRt = new RenderTexture(DecalMaskWidth, DecalMaskHeight, 0, fmt)
            {
                name = "RT_LampShadowMask",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            _decalMaskRt.Create();
        }

        if (_decalMaskCmd == null)
            _decalMaskCmd = new CommandBuffer { name = "LampShadowMask" };
        if (_decalBlock == null)
            _decalBlock = new MaterialPropertyBlock();
        if (_decalCube == null)
            _decalCube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        return _decalCube != null;
    }

    /// <summary>算贴花的投影参数和包围盒。K = 高度 1 米的点落地时的水平位移；g = 背离灯的水平单位方向。</summary>
    private void UpdateDecalParams(Vector3 g, float k, float strength)
    {
        MeshRenderer src = FindSilhouetteSource();
        Vector3 root = src.transform.position;

        // 角色纸片的「右」轴：取网格自己的 X 轴压平到水平面（和着色器里扶正用的右轴一致）。
        Vector3 right = src.transform.right;
        right.y = 0f;
        right = right.sqrMagnitude < 1e-4f ? Vector3.right : right.normalized;

        MeasureSilhouetteExtent(src, root, right, out float extentH, out float extentU, out float extentMinH);
        _decalMaskHeightM = Mathf.Max(_decalMaskHeightM, Mathf.Max(DecalMinHeight, extentH * DecalExtentMargin));
        _decalHalfWidth = Mathf.Max(_decalHalfWidth, Mathf.Max(DecalMinHalfWidth, extentU * DecalExtentMargin));
        float maskHeight = _decalMaskHeightM;
        float halfW = _decalHalfWidth;

        _decalFoot = new Vector4(root.x, root.y, root.z, maskHeight);
        // 接触余量按「水平位移」定而不是按高度定：高度偏移 w 会让影子沿灯光方向平移 w * |Dv.xz|，
        // 灯越斜平移越大，固定的 w 在斜灯下会让影子越过脚底、在直上方的灯下又留缝。
        float kxz = new Vector2(g.x * k, g.z * k).magnitude;
        // 压入方向是「朝灯」。灯在角色后面（影子朝镜头伸）时这一截被身体盖住；灯在角色前面（镜头一侧，
        // 影子朝远处伸）时压入的那一截会跑到脚前方，变成和脚分离的黑块——所以灯在前面时几乎不压入。
        float shiftScale = 1f;
        Camera shiftCam = Camera.main;
        if (shiftCam != null)
        {
            Vector3 camFwd = shiftCam.transform.forward;
            camFwd.y = 0f;
            camFwd = camFwd.sqrMagnitude < 1e-4f ? Vector3.forward : camFwd.normalized;
            float away = Mathf.Max(0f, g.x * camFwd.x + g.z * camFwd.z); // 1 = 影子朝远处（灯在镜头一侧）
            shiftScale = Mathf.Lerp(1f, 0.1f, away);
        }
        float contactBias = Mathf.Clamp(DecalContactShiftMeters * shiftScale / Mathf.Max(kxz, 0.05f),
            DecalContactBiasMin * shiftScale, DecalContactBiasMax);
        float soleOffset = Mathf.Clamp(extentMinH, 0f, DecalMaxSoleOffset) + contactBias;
        _decalRight = new Vector4(right.x, right.z, halfW, soleOffset);
        _decalDv = new Vector4(g.x * k, -1f, g.z * k, strength);

        // 墙挡光：从脚沿影子方向水平打两根射线（膝盖、胸口高度），第一面竖直的高大障碍的位置就是影子的截止距离。
        // 没有它，影子会像幽灵一样穿过墙，墙另一侧的地面本该是照不到的。
        Vector3 gd = new Vector3(g.x, 0f, g.z);
        gd = gd.sqrMagnitude < 1e-4f ? Vector3.forward : gd.normalized;
        float clipDist = 1e4f;
        for (int ri = 0; ri < 2; ri++)
        {
            Vector3 ro = root + Vector3.up * (ri == 0 ? 0.4f : 1.4f);
            int hn = Physics.RaycastNonAlloc(ro, gd, _decalClipHits, 14f, ~0, QueryTriggerInteraction.Ignore);
            for (int hi = 0; hi < hn; hi++)
            {
                RaycastHit h = _decalClipHits[hi];
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.normal.y > 0.5f) continue;                   // 斜坡、台阶侧边不挡光，只认竖直面
                if (h.collider.bounds.size.y < 2f) continue;       // 矮的东西挡不住灯
                if (h.distance < clipDist) clipDist = h.distance;
            }
        }
        _decalClip = new Vector4(gd.x, gd.z, clipDist + 0.05f, 0f);

        // 包围盒（沿影子方向对齐）：高度从脚下 3 米到头顶，沿方向最远到 K*(H+3)。
        // 表面点离纸片越远，对应的纸片高度越高，所以向下看得越深、影子能伸得越远。
        const float depthBelow = 3f;
        float along = Mathf.Min(k * (maskHeight + depthBelow) + halfW + 0.3f, 14f);
        Vector3 dir = new Vector3(g.x, 0f, g.z);
        dir = dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;
        Vector3 center = new Vector3(root.x, root.y, root.z) + dir * ((along - halfW - 0.3f) * 0.5f);
        center.y = root.y + (maskHeight + 0.5f - depthBelow) * 0.5f;
        Vector3 size = new Vector3(2f * (halfW + 0.4f), maskHeight + 0.5f + depthBelow, along + halfW + 0.3f);
        _decalMatrix = Matrix4x4.TRS(center, Quaternion.LookRotation(dir, Vector3.up), size);
    }

    /// <summary>量角色网格在「扶正坐标」里的范围：离脚最高多少米（h）、左右最宽多少米（u）。
    /// 坐标定义和 Mask Pass 顶点着色器完全一致：以根节点为原点，h 沿网格自己的上轴、u 沿水平的右轴。</summary>
    private void MeasureSilhouetteExtent(MeshRenderer src, Vector3 root, Vector3 right, out float maxH, out float maxU, out float minH)
    {
        maxH = 0f;
        maxU = 0f;
        minH = 0f;
        MeshFilter mf = src.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null)
            return;

        Bounds b = mesh.bounds;
        Matrix4x4 m = src.localToWorldMatrix;
        Vector3 upW = m.MultiplyVector(Vector3.up).normalized;
        minH = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            Vector3 d = m.MultiplyPoint3x4(corner) - root;
            float h = Vector3.Dot(d, upW);
            float u = Mathf.Abs(Vector3.Dot(d, right));
            if (h > maxH) maxH = h;
            if (h < minH) minH = h;
            if (u > maxU) maxU = u;
        }
        if (minH == float.MaxValue) minH = 0f;
    }

    /// <summary>深度贴花版的绘制：先画轮廓 RT，再画盒体贴花。</summary>
    private void DrawDepthDecal(ScriptableRenderContext context, Camera cam, MeshRenderer src, Mesh mesh)
    {
        _decalMaskCmd.Clear();
        _decalMaskCmd.SetRenderTarget(_decalMaskRt);
        _decalMaskCmd.ClearRenderTarget(false, true, Color.clear);

        Material[] mats = src.sharedMaterials;
        int sub = Mathf.Min(mesh.subMeshCount, mats.Length);
        Matrix4x4 m = src.localToWorldMatrix;
        for (int i = 0; i < sub; i++)
        {
            Texture atlas = mats[i] != null ? mats[i].mainTexture : null;
            if (atlas == null) continue;

            // 每个子网格一份属性块：命令缓冲是延后执行的，共用一份会被最后一个子网格覆盖。
            while (_silBlocksPerSubmesh.Count <= i)
                _silBlocksPerSubmesh.Add(new MaterialPropertyBlock());
            MaterialPropertyBlock block = _silBlocksPerSubmesh[i];
            block.SetTexture(SilMainTexId, atlas);
            block.SetVector(DecalFootId, _decalFoot);
            block.SetVector(DecalRightId, _decalRight);
            _decalMaskCmd.DrawMesh(mesh, m, _decalMaterial, i, _decalMaskPass, block);
        }
        context.ExecuteCommandBuffer(_decalMaskCmd);

        _decalBlock.Clear();
        _decalBlock.SetTexture(DecalMaskTexId, _decalMaskRt);
        _decalBlock.SetVector(DecalFootId, _decalFoot);
        _decalBlock.SetVector(DecalRightId, _decalRight);
        _decalBlock.SetVector(DecalDvId, _decalDv);
        _decalBlock.SetVector(DecalClipId, _decalClip);
        _decalBlock.SetVector(SilLightId, _silLight);
        _decalBlock.SetVector(SilSpotId, _silSpot);
        _decalBlock.SetVector(SilConeId, _silCone);
        Graphics.DrawMesh(_decalCube, _decalMatrix, _decalMaterial, _silLayer, cam, 0, _decalBlock,
            UnityEngine.Rendering.ShadowCastingMode.Off, false);
    }

    private void ReleaseDecal()
    {
        if (_decalMaskCmd != null) { _decalMaskCmd.Release(); _decalMaskCmd = null; }
        if (_decalMaskRt != null) { _decalMaskRt.Release(); DestroyImmediate(_decalMaskRt); _decalMaskRt = null; }
        if (_decalMaterial != null) { DestroyImmediate(_decalMaterial); _decalMaterial = null; }
        _decalHalfWidth = 0f;
        _decalMaskHeightM = 0f;
    }

    // 在相机渲染开始时提交 DrawMesh：此时 Spine 这一帧的网格已经更新完（LateUpdate 之后）。
    // 只给 MainCamera 画，Overlay 相机（GamePlayCamera / OverheadUICamera）不画，避免重复。
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (!_silActive || cam == null || !cam.CompareTag("MainCamera") || cam.cameraType != CameraType.Game)
            return;

        MeshRenderer src = FindSilhouetteSource();
        if (src == null || !src.enabled || !PrepareSilhouetteMaterial())
            return;

        Mesh mesh = src.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null)
            return;

        // 深度贴花版不登记 s_SilhouetteDraws：RT 里已经是部件并集，不需要 HologramCoverageFeature 的去重计数图。
        if (_useDecal && _decalMaterial != null && _decalMaskRt != null)
        {
            DrawDepthDecal(context, cam, src, mesh);
            return;
        }

        if (s_SilhouetteDrawFrame != Time.frameCount)
        {
            s_SilhouetteDraws.Clear();
            s_SilhouetteDrawFrame = Time.frameCount;
        }

        Material[] mats = src.sharedMaterials;
        int sub = Mathf.Min(mesh.subMeshCount, mats.Length);
        for (int i = 0; i < sub; i++)
        {
            Texture atlas = mats[i] != null ? mats[i].mainTexture : null;
            if (atlas == null) continue;

            // 每个子网格一份属性块：计数 Pass 在之后的渲染阶段才执行，共用一份会被最后一个子网格覆盖。
            while (_silBlocksPerSubmesh.Count <= i)
                _silBlocksPerSubmesh.Add(new MaterialPropertyBlock());
            MaterialPropertyBlock block = _silBlocksPerSubmesh[i];
            block.SetTexture(SilMainTexId, atlas);
            block.SetFloat(SilGroundYId, _silGroundY);
            block.SetVector(SilShearId, _silShear);
            block.SetVector(SilLightId, _silLight);
            block.SetVector(SilSpotId, _silSpot);
            block.SetVector(SilConeId, _silCone);

            Matrix4x4 m = src.localToWorldMatrix;
            Graphics.DrawMesh(mesh, m, _silMaterial, _silLayer, cam, i, block);
            s_SilhouetteDraws.Add(new SilhouetteDraw { mesh = mesh, matrix = m, submesh = i, material = _silMaterial, block = block });
        }
    }

    /// <summary>灯光投影：找对脚下光照贡献最大的那盏灯，在它的反方向画一条被拉长的影子。
    /// 复用脚下那块 Shadow_Projector 的精灵和材质（同一套乘法混合 + 遮挡物模板保护），
    /// 所以不会盖到前景遮挡物上，也永远画在角色下面。
    ///
    /// 那块投影是朝向屏幕的精灵（竖着、y 方向被压扁来模拟地面透视），不是躺在地上的，
    /// 所以影子的朝向和长度要换算成屏幕空间：地面方向 → 相机右/前方向投影，前向分量再乘俯角的正弦。
    /// 影子的 Transform 由这里直接写（单位定义锁定的是原来那块，不是这一块）。</summary>
    private void UpdateLampShadow()
    {
        if (!Application.isPlaying || !lampShadow || !respondToLamps)
        {
            if (_lampShadowSr != null) _lampShadowSr.enabled = false;
            return;
        }

        if (_shadowRenderer == null)
            return; // 原投影还没找到（UpdateShadowStrength 每秒会重试）

        Camera cam = Camera.main;
        if (cam == null)
            return;

        if (_lampShadowSr == null)
        {
            var go = new GameObject("LampShadow");
            go.layer = _shadowRenderer.gameObject.layer;
            go.transform.SetParent(_shadowRenderer.transform.parent, false);
            _lampShadowSr = go.AddComponent<SpriteRenderer>();
            _lampShadowSr.sprite = _shadowRenderer.sprite;
            _lampShadowSr.sharedMaterial = _shadowRenderer.sharedMaterial;
            _lampShadowSr.color = _shadowRenderer.color;
            _lampShadowSr.sortingLayerID = _shadowRenderer.sortingLayerID;
            _lampShadowSr.sortingOrder = _shadowRenderer.sortingOrder;
        }
        if (_lampShadowBlock == null)
            _lampShadowBlock = new MaterialPropertyBlock();

        Transform proj = _shadowRenderer.transform;
        Vector3 feet = proj.position;
        // 轮廓影子的脚必须是角色网格自己的根节点：NPC 外壳的 Shadow_Projector 子物体带本地偏移
        // （实测琪亚拉偏了 1.5 米），拿它当脚会让选灯、灯光方向 g 和剪切系数 K 全部算在一个不是脚的点上，
        // 影子起点和 AXIA（投影物体恰好在根节点）对不上。
        if (lampShadowSilhouette)
        {
            MeshRenderer rootSrc = FindSilhouetteSource();
            if (rootSrc != null) feet = rootSrc.transform.position;
        }

        // 找对脚下贡献最大的灯。
        Light best = null;
        float bestLevel = 0f;
        EnsureLampCache();
        for (int i = 0; i < _lampCache.Length; i++)
        {
            Light l = _lampCache[i];
            if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
            if (l.type != LightType.Point && l.type != LightType.Spot) continue;
            float lv = LampContribution(l, feet);
            if (lv > bestLevel) { bestLevel = lv; best = l; }
        }

        Vector3 delta = best != null ? feet - best.transform.position : Vector3.zero;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float dist = flat.magnitude;
        float height = best != null ? best.transform.position.y - feet.y : 0f;
        // 灯几乎在正上方：影子就在脚下，原来那块已经够了。
        float fade = Mathf.InverseLerp(0.25f, 0.9f, dist);
        float strength = Mathf.Clamp01(bestLevel) * fade;

        if (best == null || strength < 0.02f || height < 0.3f || _lampShadowSr.sprite == null)
        {
            _lampShadowSr.enabled = false;
            _silActive = false;
            return;
        }

        Vector3 g = flat / dist;

        // 轮廓版：把参数交给 beginCameraRendering 里的 DrawMesh，椭圆软影子隐藏。
        if (lampShadowSilhouette && FindSilhouetteSource() != null && PrepareSilhouetteMaterial())
        {
            float len = Mathf.Clamp(
                lampShadowCharacterHeight * dist / Mathf.Max(height - lampShadowCharacterHeight * 0.5f, 0.6f) * lampShadowLengthScale,
                lampShadowMinLength, lampShadowMaxLength);
            // 以前短影子（< 0.6 米）改用屏幕空间的软椭圆：那时网格是 45° 倾斜着压到地面的，
            // 短影子会皱成一团碎片。着色器里把角色扶正之后，短影子只是等比压短的人形，
            // 椭圆反而会和脚分开、像一块多余的残留影子——所以现在所有长度都用轮廓（2026-10-03）。
            float silWeight = 1f;
            _silShear = new Vector4(g.x, g.z, len / Mathf.Max(lampShadowCharacterHeight, 0.1f), strength * silWeight);
            _silGroundY = feet.y;
            // 着色器按 URP 的衰减逐像素算地面被这盏灯照多亮，影子伸出光圈的部分跟着地面一起暗下去。
            _silLight = new Vector4(best.transform.position.x, best.transform.position.y, best.transform.position.z, best.range);
            Vector3 fwd = best.transform.forward;
            _silSpot = new Vector4(fwd.x, fwd.y, fwd.z, best.type == LightType.Spot ? 1f : 0f);
            float lum = best.color.r * 0.299f + best.color.g * 0.587f + best.color.b * 0.114f;
            _silCone = new Vector4(
                Mathf.Cos(best.spotAngle * 0.5f * Mathf.Deg2Rad),
                Mathf.Cos(Mathf.Min(best.innerSpotAngle, best.spotAngle - 0.1f) * 0.5f * Mathf.Deg2Rad),
                best.intensity * lum, 0f);
            _silLayer = _shadowRenderer.gameObject.layer;

            // 深度贴花版：沿用上面的灯参数和剪切系数 K，只是把「压到一张水平面」换成「落在深度图看到的表面」。
            _useDecal = lampShadowDepthProjection && PrepareDecal();
            if (_useDecal)
                UpdateDecalParams(g, _silShear.z, strength * silWeight);

            _silActive = silWeight > 0.01f;
            if (_silActive)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            }

            if (silWeight >= 0.99f)
            {
                _lampShadowSr.enabled = false;
                return;
            }

            // 轮廓还没完全接管：椭圆影子按剩余比例继续画（下面走原来的椭圆流程）。
            strength *= 1f - silWeight;
        }
        else
        {
            _silActive = false;
        }
        // 地面上影子的长度：身高 × 水平距离 / (灯高 − 半个身高)，近似点光源下直立物体的影长。
        float length = Mathf.Clamp(
            lampShadowCharacterHeight * dist / Mathf.Max(height - lampShadowCharacterHeight * 0.5f, 0.6f) * lampShadowLengthScale,
            lampShadowMinLength, lampShadowMaxLength);

        // 地面方向 → 屏幕方向（俯角越大，纵向压得越扁）。
        Vector3 camRight = cam.transform.right; camRight.y = 0f; camRight.Normalize();
        Vector3 camFwd = cam.transform.forward; camFwd.y = 0f; camFwd.Normalize();
        float pitch = Mathf.Abs(Mathf.Sin(Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f))));
        Vector3 p = new Vector3(-g.z, 0f, g.x);

        Vector2 sAlong = new Vector2(Vector3.Dot(g, camRight), Vector3.Dot(g, camFwd) * pitch);
        Vector2 sAcross = new Vector2(Vector3.Dot(p, camRight), Vector3.Dot(p, camFwd) * pitch);
        float angle = Mathf.Atan2(sAlong.y, sAlong.x) * Mathf.Rad2Deg;

        Bounds sb = _lampShadowSr.sprite.bounds;
        // 椭圆精灵的软边会吃掉一部分可见范围（可见核心约占精灵尺寸的 70%），按 1/0.7 放大，
        // 否则影子看起来比算出来的小，而且近端飘在脚前面一截。
        const float SoftEdgeCompensation = 1f / 0.7f;
        // 影子宽度：等宽，不随离灯远近放大（避免「头顶那端变大」的反透视）。
        // 只用屏幕压扁系数的下限 0.75 保底，影子沿镜头纵深方向时也不会被压成一条线。
        float worldLen = Mathf.Max(0.3f, length * sAlong.magnitude) * SoftEdgeCompensation;
        float worldWid = Mathf.Max(0.2f, lampShadowWidth * Mathf.Max(sAcross.magnitude, 0.75f)) * SoftEdgeCompensation;

        Transform t2 = _lampShadowSr.transform;
        // 中心推到影子长度的一半：可见部分的近端正好接在脚底。
        t2.position = feet + g * (length * 0.5f);
        t2.rotation = proj.rotation * Quaternion.Euler(0f, 0f, angle);
        Vector3 ps = t2.parent != null ? t2.parent.lossyScale : Vector3.one;
        t2.localScale = new Vector3(
            worldLen / Mathf.Max(sb.size.x, 0.0001f) / Mathf.Max(ps.x, 0.0001f),
            worldWid / Mathf.Max(sb.size.y, 0.0001f) / Mathf.Max(ps.y, 0.0001f),
            1f);

        // 越靠后（离脚越远）越淡由精灵自带的软边提供；整体强度跟灯光亮度走。
        Color c = _lampShadowSr.sharedMaterial.GetColor(ShadowColorId);
        c.a = strength;
        _lampShadowSr.GetPropertyBlock(_lampShadowBlock);
        _lampShadowBlock.SetColor(ShadowColorId, c);
        _lampShadowSr.SetPropertyBlock(_lampShadowBlock);
        _lampShadowSr.enabled = true;
    }

    /// <summary>单盏灯在某位置的亮度贡献（强度×颜色亮度×衰减），公式和 EvaluateLampLevelAt 里一致。</summary>
    private static float LampContribution(Light l, Vector3 worldPos)
    {
        float dist = Vector3.Distance(worldPos, l.transform.position);
        if (dist >= l.range) return 0f;

        float atten = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, dist / l.range));
        if (l.type == LightType.Spot)
        {
            Vector3 toPoint = (worldPos - l.transform.position).normalized;
            float cosAngle = Vector3.Dot(l.transform.forward, toPoint);
            float cosCutoff = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
            if (cosAngle < cosCutoff) return 0f;
            float cosInner = Mathf.Cos(l.spotAngle * 0.5f * 0.7f * Mathf.Deg2Rad);
            atten *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cosCutoff, cosInner, cosAngle));
        }
        float lum = l.color.r * 0.299f + l.color.g * 0.587f + l.color.b * 0.114f;
        return l.intensity * lum * atten;
    }

    /// <summary>脚底投影随灯光变强：灯越亮、离脚越近，投影越明显。只改 _Color.a（MaterialPropertyBlock），
    /// 不碰 Shadow_Projector 的 Transform——那个由 UnitDefinitionRuntimeApplier 锁定，改了会被覆盖。</summary>
    private void UpdateShadowStrength()
    {
        if (shadowLampBoost <= 0f || !respondToLamps)
            return;

        if (_shadowRenderer == null)
        {
            _shadowSearchTimer -= Time.unscaledDeltaTime;
            if (_shadowSearchTimer > 0f)
                return;
            _shadowSearchTimer = 1f;

            foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.gameObject.name != "Shadow_Projector") continue;
                _shadowRenderer = sr;
                break;
            }
            if (_shadowRenderer == null || _shadowRenderer.sharedMaterial == null || !_shadowRenderer.sharedMaterial.HasProperty(ShadowColorId))
            {
                _shadowRenderer = null;
                return;
            }
            _shadowBaseAlpha = _shadowRenderer.sharedMaterial.GetColor(ShadowColorId).a;
            _shadowBlock = new MaterialPropertyBlock();
        }

        // 脚本重新编译后，Unity 会保留 _shadowRenderer 这类对象引用，但 MaterialPropertyBlock 没法保留，要补建。
        if (_shadowBlock == null)
            _shadowBlock = new MaterialPropertyBlock();

        // 取脚底处的灯光亮度：投影落在地面上，看的是脚下被照亮多少，不是身体中段。
        float lampLevel = Mathf.Clamp01(EvaluateLampLevelAt(transform.position));
        // 不会比没灯时更淡，也不超过 1。注意：当前投影材质的原强度就是 1，这个加成实际没有余量；
        // 投影在亮的地面上更明显，靠的是乘法混合本身（地面越亮，被乘暗的差别越大）。
        float strength = Mathf.Clamp01(_shadowBaseAlpha * (1f + shadowLampBoost * lampLevel));

        Color c = _shadowRenderer.sharedMaterial.GetColor(ShadowColorId);
        c.a = strength;
        _shadowRenderer.GetPropertyBlock(_shadowBlock);
        _shadowBlock.SetColor(ShadowColorId, c);
        _shadowRenderer.SetPropertyBlock(_shadowBlock);
    }

    /// <summary>只算点光/聚光在某个位置的亮度（不含环境光和主平行光）。公式和 EvaluateLightLevel 里一致。</summary>
    private static float EvaluateLampLevelAt(Vector3 worldPos)
    {
        EnsureLampCache();
        float sum = 0f;
        for (int i = 0; i < _lampCache.Length; i++)
        {
            Light l = _lampCache[i];
            if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
            if (l.type != LightType.Point && l.type != LightType.Spot) continue;

            float dist = Vector3.Distance(worldPos, l.transform.position);
            if (dist >= l.range) continue;

            float atten = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, dist / l.range));
            if (l.type == LightType.Spot)
            {
                Vector3 toPoint = (worldPos - l.transform.position).normalized;
                float cosAngle = Vector3.Dot(l.transform.forward, toPoint);
                float cosCutoff = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                if (cosAngle < cosCutoff) continue;
                float cosInner = Mathf.Cos(l.spotAngle * 0.5f * 0.7f * Mathf.Deg2Rad);
                atten *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cosCutoff, cosInner, cosAngle));
            }
            float lum = l.color.r * 0.299f + l.color.g * 0.587f + l.color.b * 0.114f;
            sum += l.intensity * lum * atten;
        }
        return sum;
    }

    /// <summary>挑离角色最近的几盏点光/聚光，打包成着色器数组。返回实际盏数。
    /// 打包格式和 SkyPrisonLampLevelAt（着色器）一一对应：
    ///   Pos   = (世界位置, 范围)
    ///   Dir   = (朝向, cos 外角)，点光源的 cos 外角填 -2 表示没有锥角
    ///   Param = (强度×颜色亮度, cos 内角)</summary>
    private int CollectShaderLamps(Vector3 center)
    {
        EnsureLampCache();
        _nearLamps.Clear();
        for (int i = 0; i < _lampCache.Length; i++)
        {
            Light l = _lampCache[i];
            if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
            if (l.type != LightType.Point && l.type != LightType.Spot) continue;
            // 角色整个身体在灯范围外就没必要传：留 2 米余量，覆盖身高和移动。
            if (Vector3.Distance(center, l.transform.position) > l.range + 2f) continue;
            _nearLamps.Add(l);
        }

        _nearLamps.Sort((a, b) =>
            (a.transform.position - center).sqrMagnitude.CompareTo((b.transform.position - center).sqrMagnitude));

        int count = Mathf.Min(MaxShaderLamps, _nearLamps.Count);
        for (int i = 0; i < count; i++)
        {
            Light l = _nearLamps[i];
            Vector3 p = l.transform.position;
            Vector3 f = l.transform.forward;
            float lum = l.color.r * 0.299f + l.color.g * 0.587f + l.color.b * 0.114f;
            _lampPos[i] = new Vector4(p.x, p.y, p.z, l.range);
            if (l.type == LightType.Spot)
            {
                // 内外角和 URP 一致（innerSpotAngle），着色器用 URP 的物理衰减，光圈边界和地面对齐。
                float cosOuter = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(Mathf.Min(l.innerSpotAngle, l.spotAngle - 0.1f) * 0.5f * Mathf.Deg2Rad);
                _lampDir[i] = new Vector4(f.x, f.y, f.z, cosOuter);
                _lampParam[i] = new Vector4(l.intensity * lum / Mathf.Max(lampFullBrightness, 0.0001f), cosInner, 0f, 0f);
            }
            else
            {
                _lampDir[i] = new Vector4(0f, -1f, 0f, -2f);
                _lampParam[i] = new Vector4(l.intensity * lum / Mathf.Max(lampFullBrightness, 0.0001f), 1f, 0f, 0f);
            }
            // 灯色相（按亮度归一），着色器逐像素按受光比例往这个颜色染。
            _lampColor[i] = lum > 0.0001f ? new Vector4(l.color.r / lum, l.color.g / lum, l.color.b / lum, 1f) : Vector4.one;
        }
        return count;
    }

    /// <summary>这个位置有多亮（0~1）、什么颜色。
    ///
    /// 原来只看主平行光的强度。那套逻辑只能表达"白天/夜晚"，表达不了地下——
    /// 地下的主平行光恒等于 0，所有光都来自摆在场景里的灯具，只看平行光的话，
    /// 角色不管站在灯下还是站在漆黑角落，压暗程度完全一样。
    /// 现在三项累加：环境光 + 主平行光 + 附近点光/聚光的距离衰减。</summary>
    private float EvaluateLightLevel(Vector3 worldPos, out Color lightColor)
    {
        Color accum = RenderSettings.ambientLight;

        if (sceneMainLight != null && sceneMainLight.isActiveAndEnabled)
            accum += sceneMainLight.color * sceneMainLight.intensity;

        // 环境光 + 主平行光 = 整个角色共用的基础亮度；灯光部分逐像素模式下交给着色器，这里不再计入亮度，
        // 只计入色相（见下面 lightColor）。
        Color baseAccum = accum;

        if (respondToLamps)
        {
            EnsureLampCache();
            for (int i = 0; i < _lampCache.Length; i++)
            {
                Light l = _lampCache[i];
                if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;

                float dist = Vector3.Distance(worldPos, l.transform.position);
                if (dist >= l.range) continue;

                // 不用物理的平方反比：那样灯正下方极亮、走开两步就全黑，过渡太硬，
                // 跟手绘精灵图的明暗语言对不上。
                // 也不用 (1 - d/r)²：那条曲线在 87% 范围处只剩 1.7%，而可见光束（VLB）一直亮到
                // 范围终点，玩家看到角色明明站在光里，算出来却几乎没光，角色不被照亮。
                // 范围前 55% 保持满亮度，后 45% 平滑落到 0，亮度边界和肉眼看到的光体积对得上。
                float atten = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, dist / l.range));

                if (l.type == LightType.Spot)
                {
                    Vector3 toPoint = (worldPos - l.transform.position).normalized;
                    float cosAngle = Vector3.Dot(l.transform.forward, toPoint);
                    float cosCutoff = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                    if (cosAngle < cosCutoff) continue;
                    // 光锥边缘只在最外侧约 30% 角度内渐变，锥内大部分区域保持满亮度。
                    float cosInner = Mathf.Cos(l.spotAngle * 0.5f * 0.7f * Mathf.Deg2Rad);
                    atten *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cosCutoff, cosInner, cosAngle));
                }

                accum += l.color * (l.intensity * atten);
            }
        }

        float luminance = accum.r * 0.299f + accum.g * 0.587f + accum.b * 0.114f;
        Color levelSource = perPixelLamps ? baseAccum : accum;
        float levelLuminance = levelSource.r * 0.299f + levelSource.g * 0.587f + levelSource.b * 0.114f;
        float level = referenceLightIntensity > 0.0001f
            ? Mathf.Clamp01(levelLuminance / referenceLightIntensity)
            : 1f;

        // 颜色只取色相，不带亮度——亮度已经由 darken 表达，两边都带会翻倍。
        // 逐像素模式下这里只给环境光+主光的色相，灯色由着色器逐像素混入（腰部一个采样点在
        // 光锥边缘会比腿先出锥，造成「腿被照亮却没有灯色」）。
        float hueLum = levelSource.r * 0.299f + levelSource.g * 0.587f + levelSource.b * 0.114f;
        lightColor = hueLum > 0.0001f
            ? new Color(levelSource.r / hueLum, levelSource.g / hueLum, levelSource.b / hueLum, 1f)
            : Color.white;

        return level;
    }

    // 每帧全场找灯开销不可接受。地下场景的灯基本是静态摆放的，1 秒刷新一次足够。
    private static Light[] _lampCache = System.Array.Empty<Light>();
    private static float _lampCacheTime = -999f;

    private static void EnsureLampCache()
    {
        if (Time.unscaledTime - _lampCacheTime < 1f && _lampCache.Length > 0) return;
        _lampCacheTime = Time.unscaledTime;
        _lampCache = FindObjectsByType<Light>(FindObjectsSortMode.None);
    }
}
