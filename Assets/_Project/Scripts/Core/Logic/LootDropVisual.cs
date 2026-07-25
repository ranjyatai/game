using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 掉落物世界表现：悬浮、旋转、底部发光、描边图层、按品级换发光色。
/// 模型直接 Instantiate library 里配置的 GameObject，保留原始材质/Shader。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LootDropWorldObject))]
public sealed class LootDropVisual : MonoBehaviour
{
    [Header("模型")]
    [SerializeField] private float   modelScale          = 104f;  // 80 × 1.3
    [SerializeField] private Vector3 modelRotationOffset = new Vector3(-90f, 0f, 0f);  // Blender Z-up → Unity Y-up
    [SerializeField] private float   modelHeightOffset   = 0.732f;

    [Header("旋转 / 悬浮")]
    [SerializeField] private float rotateSpeed     = 60f;
    [SerializeField] private float hoverAmplitude  = 0.06f;
    [SerializeField] private float hoverSpeed      = 1.4f;
    [SerializeField] private float hoverBaseHeight = 0.16f;

    [Header("发光")]
    [SerializeField] private float glowRange           = 4.0f;   // 扩大范围让光散开
    [SerializeField] private float glowIntensityNormal = 0.08f;  // 低强度避免 HDR 过曝发白
    [SerializeField] private float glowIntensitySelect = 0.18f;

    // ── 运行时状态 ────────────────────────────────────────────────────────
    private Transform           _modelRoot;
    private Light               _glowLight;
    private LootDropBeaconVFX   _beacon;
    private SortingGroup        _sortingGroup;
    private Renderer[]          _modelRenderers = System.Array.Empty<Renderer>();
    private MaterialPropertyBlock _mpb;
    private static readonly int   _holoRootZId      = Shader.PropertyToID("_HoloRootZ");
    private static readonly int   _holoColorId           = Shader.PropertyToID("_Color");
    private static readonly int   _occludeOutlineColorId = Shader.PropertyToID("_OccludeOutlineColor");
    private static readonly int   _objWorldCenterId      = Shader.PropertyToID("_ObjectWorldCenter");
    private static readonly int   _objWorldRadiusId      = Shader.PropertyToID("_ObjectWorldRadius");
    private static readonly int   _vfxTargetColorId      = Shader.PropertyToID("_TargetColor");
    private static readonly int   _useRainbowId          = Shader.PropertyToID("_UseRainbow");
    private float               _hoverPhase;
    private float               _groundY;
    private bool                _selected;
    private int                 _itemLevel = 1;
    private bool                _visualApplied;
    private LootDropWorldObject _loot;

    // ── 遮挡描边（链路说明见下方 SetupOcclusionReceiver 一节的注释）──────────
    private UnitOcclusionMaterialReceiver _occlusionReceiver;
    private GameObject _hiddenOutlineRing; // 名字沿用历史命名，现在是全息填充体
    private static Material _ringMat;      // 全息填充材质（LootDropHiddenHologramFill）

    // ── Feature 访问接口（独立 Feature 路径已废弃，保留列表供调试）──────────
    public static readonly List<LootDropVisual> ActiveDrops = new List<LootDropVisual>();
    public Renderer[] ModelRenderers => _modelRenderers;


    private void Awake()
    {
        _mpb  = new MaterialPropertyBlock();
        _loot = GetComponent<LootDropWorldObject>();

        // 穿透触发碰撞体
        if (GetComponent<Collider>() == null)
        {
            var col       = gameObject.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.height    = 0.6f;
            col.radius    = 0.22f;
            col.center    = new Vector3(0f, 0.3f, 0f);
        }

        // 创建旋转/悬浮节点（模型会 Instantiate 到这里）
        var mg = new GameObject("ModelRoot");
        mg.transform.SetParent(transform, false);
        _modelRoot = mg.transform;

        _groundY    = transform.position.y;
        _hoverPhase = Random.Range(0f, Mathf.PI * 2f);

        // SortingGroup 强制让 MeshRenderer 进入 2D 排序路径（受 sortingOrder 控制）
        _sortingGroup = gameObject.AddComponent<SortingGroup>();

        BuildGlowLight();
        BuildBeaconVFX();
        SetupOcclusionReceiver();
    }

    private void OnEnable()
    {
        if (!ActiveDrops.Contains(this)) ActiveDrops.Add(this);
    }

    private void OnDisable()
    {
        ActiveDrops.Remove(this);
    }

    private void Start()
    {
        ApplyItemVisual();
        ApplyBeaconSortingLayer();
        Debug.Log($"[LootDropVisual] Start 完成，模型数={_modelRenderers.Length}，位置={transform.position}", this);
    }

    private void ApplyBeaconSortingLayer()
    {
        if (_beaconVFXInstance == null) return;
        int layer = LayerMask.NameToLayer("World3D");
        if (layer < 0) layer = 7; // 直接用 ID 兜底
        SetLayerRecursive(_beaconVFXInstance, layer);
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }

    // ── 公开 API ──────────────────────────────────────────────────────────

    public void ApplyItemVisual()
    {
        if (_loot == null || _loot.Item == null) return;

        var lib = LootDropModelLibrary.Instance;
        if (lib == null)
        {
            Debug.LogError("[LootDropVisual] LootDropModelLibrary 未找到。", this);
            return;
        }

        ItemDefinition item = _loot.Item;
        _itemLevel = Mathf.Clamp(item.itemLevel, 1, 9);
        int sortOrder = Mathf.RoundToInt(-transform.position.z * 100f) - 1;

        // 取配置的模型 prefab + 独立缩放 + 旋转偏移
        GameObject prefab;
        float      entryScale;
        Vector3    entryRotation;
        Mesh       entryMesh;
        if (item.IsEquipmentItem)
        {
            prefab = lib.GetModelForEquipment(item.equipment.slot, out entryScale, out entryRotation, out entryMesh);
        }
        else if (item.category == ItemCategory.Material)
        {
            prefab = lib.GetModelForMaterial(item.materialSubCategory, out entryScale, out entryRotation, out entryMesh);
        }
        else
        {
            prefab = lib.GetModelForGeneral(item.category, out entryScale, out entryRotation, out entryMesh);
        }

        if (prefab != null || entryMesh != null)
        {
            // 清除旧模型（重复调用时）
            for (int i = _modelRoot.childCount - 1; i >= 0; i--)
                Destroy(_modelRoot.GetChild(i).gameObject);

            // prefab 优先；无 prefab 时用 Mesh asset 动态构建 GameObject
            GameObject inst;
            if (prefab != null)
            {
                inst = Instantiate(prefab, _modelRoot);
            }
            else
            {
                inst = new GameObject("MeshModel");
                inst.transform.SetParent(_modelRoot, false);
                inst.AddComponent<MeshFilter>().sharedMesh = entryMesh;
                inst.AddComponent<MeshRenderer>();
            }
            inst.transform.localPosition = new Vector3(0f, modelHeightOffset, 0f);
            // entryRotation 为绝对旋转；全为零时回退到全局 modelRotationOffset（Blender 轴修正）
            bool hasEntryRotation = entryRotation != Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(hasEntryRotation ? entryRotation : modelRotationOffset);
            float sizeMultiplier = (lib.modelSizeMultiplier > 0f ? lib.modelSizeMultiplier : 1f)
                                 * (entryScale > 0f ? entryScale : 1f);
            inst.transform.localScale    = Vector3.one * (modelScale * sizeMultiplier);

            // 层 + 材质 + 排序
            // 偏移 -30：与角色同 Z 时排在角色后面；角色明显在后时（Z差>0.3m）掉落物正确浮现
            Material holoMat  = lib.hologramMaterial;
            if (_sortingGroup != null) _sortingGroup.sortingOrder = sortOrder;
            _modelRenderers = inst.GetComponentsInChildren<Renderer>(true);

            // 计算世界空间包围盒，传给全息 shader 做自适应 rim
            if (_modelRenderers.Length > 0)
            {
                Bounds b = _modelRenderers[0].bounds;
                foreach (var mr in _modelRenderers) b.Encapsulate(mr.bounds);
                float radius = Mathf.Max(b.extents.x, b.extents.z);
                _mpb.SetVector(_objWorldCenterId, b.center);
                _mpb.SetFloat(_objWorldRadiusId, radius);
            }
            foreach (Renderer r in _modelRenderers)
            {
                r.gameObject.layer = 7;
                r.sortingOrder     = sortOrder;
                if (holoMat != null)
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = holoMat;
                    r.sharedMaterials = mats;
                }
            }

#if UNITY_EDITOR
            // 调试：打印 bounds 确认大小和位置
            var renderers = inst.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                Debug.Log($"[LootDropVisual] 模型 '{prefab.name}' bounds center={b.center} size={b.size}", this);
            }
            else
            {
                Debug.LogWarning($"[LootDropVisual] '{prefab.name}' 没有 Renderer 组件！", this);
            }
#endif
            ConfigureOcclusionReceiver(inst);
            RebuildHiddenOutlineRing(inst);
        }
        else
        {
            Debug.LogWarning($"[LootDropVisual] '{item.displayName}' (cat={item.category}) 没有配置模型，请在掉落物设置中指定。", this);
        }

        // 开启并设置发光
        if (_glowLight != null)
        {
            _glowLight.gameObject.SetActive(true);
            if (_itemLevel != 9)
                ApplyGlowByLevel(_itemLevel, _selected);
        }

        // beacon 比模型低一步，确保模型始终渲染在光柱前面
        int beaconSortOrder = sortOrder - 1;

        if (_itemLevel == 9)
        {
            // ── LV9：全息 Shader 用位置彩虹，VFX 用梯度彩虹 ──────────────
            _mpb.SetFloat(_useRainbowId, 1f);
            _mpb.SetColor(_occludeOutlineColorId, new Color(1f, 1f, 1f, 0.75f)); // LV9 描边白色

            if (_beaconVFXInstance != null)
            {
                LootDropVFXTinter.ApplyRainbow(_beaconVFXInstance);
                _beaconRenderers = _beaconVFXInstance.GetComponentsInChildren<Renderer>(true);
            }

            if (_beacon != null)
                _beacon.SetupRainbow(beaconSortOrder);
        }
        else
        {
            // ── 普通品级：单色 ────────────────────────────────────────────
            _mpb.SetFloat(_useRainbowId, 0f);
            Color beaconColor = LootDropModelLibrary.GetLevelColor(_itemLevel);
            _mpb.SetColor(_holoColorId, beaconColor);
            // 遮挡描边颜色与品级色一致，alpha 略降
            _mpb.SetColor(_occludeOutlineColorId, new Color(1f, 1f, 1f, 0.85f));

            if (_beaconVFXInstance != null)
            {
                LootDropVFXTinter.Apply(_beaconVFXInstance, beaconColor);
                _beaconRenderers = _beaconVFXInstance.GetComponentsInChildren<Renderer>(true);
            }

            if (_beacon != null)
                _beacon.Setup(beaconColor, beaconSortOrder);
        }

        // 外部 VFX Prefab 的渲染器默认 sortingOrder=0，统一压到模型后面
        foreach (var r in _beaconRenderers)
            r.sortingOrder = beaconSortOrder;

        _visualApplied = true;
    }

    // 缓存玩家 Spine MeshRenderer（sortingOrder 从它读取）
    private static Renderer _cachedCharRenderer;

    private int CalcSortOrder()
    {
        if (_cachedCharRenderer == null)
        {
            var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (unit != null)
            {
                // 优先找 SkeletonAnimation（Spine 主体渲染器）
                var skelAnim = unit.GetComponentInChildren<Spine.Unity.SkeletonAnimation>(true);
                if (skelAnim != null)
                    _cachedCharRenderer = skelAnim.GetComponent<Renderer>();

                // 回退：层级内所有 MeshRenderer 中 sortingOrder 最大的（主体，不是阴影）
                if (_cachedCharRenderer == null)
                {
                    int maxOrder = int.MinValue;
                    foreach (var r in unit.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (r.sortingOrder > maxOrder)
                        {
                            maxOrder = r.sortingOrder;
                            _cachedCharRenderer = r;
                        }
                    }
                }
            }
        }

        // 角色静态 sortingOrder = 0；掉落物根据角色 Z 动态切换：
        //   角色在前（charZ ≤ holoZ + buffer）→ 掉落物排在角色后面（-1）
        //   角色在后（charZ > holoZ + buffer）→ 掉落物排在角色前面（+1）
        const float kBuffer = 0.08f;  // 防止边界闪烁
        int charBaseSort = _cachedCharRenderer != null ? _cachedCharRenderer.sortingOrder : 0;
        float holoZ = transform.position.z;

        float charZ = 0f;
        if (_cachedCharRenderer != null)
        {
            var charUnit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            charZ = charUnit != null ? charUnit.transform.position.z
                                     : _cachedCharRenderer.transform.position.z;
        }

        return charZ > holoZ + kBuffer ? charBaseSort + 1   // 角色在后 → 掉落物在前
                                       : charBaseSort - 1;  // 角色在前 → 掉落物在后
    }

    private void OnDestroy() => _cachedCharRenderer = null;

    public void SetSelected(bool selected)
    {
        if (_selected == selected) return;
        _selected = selected;
        if (_itemLevel != 9 && _glowLight != null)
            ApplyGlowByLevel(_itemLevel, _selected);
    }

    // ── Update ───────────────────────────────────────────────────────────
    private void Update()
    {
        // 旋转
        _modelRoot.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);

        // 悬浮
        float y = _groundY + hoverBaseHeight
                  + Mathf.Sin(Time.time * hoverSpeed + _hoverPhase) * hoverAmplitude;
        Vector3 pos = transform.position;
        pos.y = y;
        transform.position = pos;

        // 每帧动态跟随最近角色的 sortingOrder，确保掉落物始终在角色后面
        int order = CalcSortOrder();
        if (_sortingGroup != null && _sortingGroup.sortingOrder != order)
        {
            _sortingGroup.sortingOrder = order;
            foreach (var r in _modelRenderers)  r.sortingOrder = order;
            foreach (var r in _beaconRenderers) r.sortingOrder = order - 1;
        }

        // 每帧把掉落物根节点 Z 写入材质，供全息 Shader 做前后判断
        if (_modelRenderers.Length > 0 || _beaconRenderers.Length > 0)
        {
            _mpb.SetFloat(_holoRootZId, transform.position.z);
            foreach (var r in _modelRenderers)
                r.SetPropertyBlock(_mpb);
            foreach (var r in _beaconRenderers)
                r.SetPropertyBlock(_mpb);
        }

        // 被遮挡时显示白色描边环（判定来源跟角色描边完全同一套：遮挡物触发器 → receiver）
        if (_hiddenOutlineRing != null && _occlusionReceiver != null)
        {
            bool occluded = _occlusionReceiver.CurrentOccluded;
            if (_hiddenOutlineRing.activeSelf != occluded)
                _hiddenOutlineRing.SetActive(occluded);
        }

        // LV9：只让发光灯慢速彩虹（视觉点缀），模型/VFX 已由各自系统处理同帧七彩
        if (_visualApplied && _itemLevel == 9 && _glowLight != null)
        {
            _glowLight.color     = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.12f, 1f), 0.45f, 1f);
            _glowLight.intensity = _selected ? glowIntensitySelect : glowIntensityNormal;
        }

    }

    // ── 内部 ─────────────────────────────────────────────────────────────

    private GameObject _beaconVFXInstance;
    private Renderer[] _beaconRenderers = System.Array.Empty<Renderer>();

    // ── 遮挡表现：全息点阵填充（2026-07-19 定版）───────────────────────────
    // 链路：集装箱等遮挡物的 SkyPrisonTerrainDecorationFrontOccluderTrigger 探测到
    // OcclusionDetectionProbe（UnitBody层）→ 调 receiver.SetOccludedBy → CurrentOccluded
    // → Update 里开关全息填充体（世界空间网格+扫描横带，见 LootDropHiddenHologramFill.
    // shader，跟角色 SpineOcclusionComposite 里的全息填充同一套算法）。判定来源跟角色
    // 完全同源。填充式表现最初是从"描边"改过来的——多个单位贴近交叉时，逐网格描边
    // 只能画在自己网格覆盖到的范围内，会被对方实体盖住出现缺口，填充不需要找边缘，
    // 天生没有这个问题。曾经尝试过全屏 RawImage 描边方案，两轮排查后已彻底放弃
    // （详情见项目记忆 project-occlusion-outline-architecture）。

    private void SetupOcclusionReceiver()
    {
        _occlusionReceiver = gameObject.AddComponent<UnitOcclusionMaterialReceiver>();

        // 集装箱触发器的 targetLayers 在场景里被显式收窄成只扫"UnitBody"层——掉落物的
        // 拾取碰撞体在别的图层永远进不了候选名单。不能直接改拾取碰撞体的图层（拾取逻辑
        // 可能依赖），加一个专门被遮挡探测扫到的独立触发碰撞体。
        var occlusionProbeGo = new GameObject("OcclusionDetectionProbe");
        occlusionProbeGo.transform.SetParent(transform, false);
        int unitBodyLayer = LayerMask.NameToLayer("UnitBody");
        occlusionProbeGo.layer = unitBodyLayer >= 0 ? unitBodyLayer : gameObject.layer;
        var probeCol = occlusionProbeGo.AddComponent<CapsuleCollider>();
        probeCol.isTrigger = true;
        probeCol.height    = 0.6f;
        probeCol.radius    = 0.22f;
        probeCol.center    = new Vector3(0f, 0.3f, 0f);
    }

    private void ConfigureOcclusionReceiver(GameObject modelInstance)
    {
        // 防御性配置：receiver 的本职是被遮挡时切换材质组（角色靠这个换成Spine描边材质）。
        // 掉落物不用材质切换，但如果完全不配置，receiver 的 autoFind 兜底可能自己找渲染体
        // 并创建 Spine 专用的运行时合成材质，把全息材质换掉。把"正常"和"遮挡"两组配成
        // 同一份让切换永远是空操作，receiver 只剩"提供 CurrentOccluded 状态"这一个职责。
        if (_occlusionReceiver == null || modelInstance == null) return;

        Renderer primary = modelInstance.GetComponentInChildren<Renderer>(true);
        if (primary != null)
        {
            Material[] currentMats = primary.sharedMaterials;
            _occlusionReceiver.ConfigureRendererAndMaterials(primary, null, currentMats, currentMats, false);
        }
    }

    /// <summary>
    /// 被遮挡时显示的全息点阵填充。跟角色同一套判定来源（集装箱等的
    /// FrontOccluderTrigger → UnitOcclusionMaterialReceiver.CurrentOccluded）、
    /// 同一套视觉语言（世界空间网格 + 周期性扫描横带，跟角色 SpineOcclusionComposite
    /// 里的全息填充是同一套算法照搬到 LootDropHiddenHologramFill.shader）。
    /// 填充不需要找轮廓边缘，只复制一份实体网格叠加渲染即可，不需要之前描边环那套
    /// 模板+外扩壳的双渲染体技巧。平时整个渲染体隐藏，Update里按遮挡状态开关。
    /// </summary>
    private void RebuildHiddenOutlineRing(GameObject modelInstance)
    {
        if (_hiddenOutlineRing != null) Destroy(_hiddenOutlineRing);
        if (modelInstance == null) return;

        if (_ringMat == null)
        {
            var fillShader = Shader.Find("SkyPrison/LootDrop/HiddenHologramFill");
            if (fillShader == null)
            {
                Debug.LogError("[LootDropVisual] 全息填充 shader 缺失（shader编译失败或文件未导入）", this);
                return;
            }
            _ringMat = new Material(fillShader) { name = "M_LootDropHiddenHologramFill", hideFlags = HideFlags.DontSave };
            // 显式写死，不依赖 shader Properties 声明的默认值——这个材质是 static 缓存，
            // 一旦某次 Play 会话里创建过就不会再重新创建，如果只靠 shader 默认值，改了
            // shader 默认值也不会追溯影响已经创建好的这份实例。
            _ringMat.SetFloat("_SilhouetteAlpha", 0f);
        }

        var fillRoot = new GameObject("HiddenHologramFillRoot");
        fillRoot.transform.SetParent(_modelRoot, false); // 跟随 hover/旋转
        CopyMeshes(modelInstance.transform, fillRoot.transform, _ringMat);

        // 主相机 cullingMask 只有 World3D(7)+Character2D(8)，填充体必须放在 World3D 才会被画
        SetLayerRecursive(fillRoot, 7);
        fillRoot.SetActive(false);
        _hiddenOutlineRing = fillRoot;
    }

    private static void CopyMeshes(Transform src, Transform dstParent, Material mat)
    {
        var go = new GameObject(src.name);
        go.transform.SetParent(dstParent, false);
        go.transform.localPosition = src.localPosition;
        go.transform.localRotation = src.localRotation;
        go.transform.localScale    = src.localScale;

        var mf = src.GetComponent<MeshFilter>();
        var mr = src.GetComponent<MeshRenderer>();
        if (mf != null && mr != null && mf.sharedMesh != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
        foreach (Transform child in src)
            CopyMeshes(child, go.transform, mat);
    }

    private void BuildBeaconVFX()
    {
        var lib = LootDropModelLibrary.Instance;
        if (lib == null) return;

        if (lib.beaconVFXPrefab != null)
        {
            // ── 商店 Prefab 路径 ──────────────────────────────────────────
            _beaconVFXInstance = Object.Instantiate(lib.beaconVFXPrefab, transform);
            _beaconVFXInstance.transform.localPosition = Vector3.zero;
            if (lib.beaconVFXScale != Vector3.zero)
                _beaconVFXInstance.transform.localScale = lib.beaconVFXScale;

            // 排序层在 Start() 里统一修复，确保粒子子对象全部初始化后再设
            // 染色在 ApplyItemVisual 调用时执行
        }
        else
        {
            // ── 自搓粒子 Fallback ─────────────────────────────────────────
            var go = new GameObject("BeaconVFX");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _beacon = go.AddComponent<LootDropBeaconVFX>();
            if (lib.beaconMaterial != null)
            {
                _beacon.beamMaterial     = lib.beaconMaterial;
                _beacon.particleMaterial = lib.beaconMaterial;
            }
        }
    }

    private void BuildGlowLight()
    {
        var go = new GameObject("GlowLight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.SetActive(false);   // 等 ApplyItemVisual 成功才开

        _glowLight           = go.AddComponent<Light>();
        _glowLight.type      = LightType.Point;
        _glowLight.shadows   = LightShadows.None;
        _glowLight.range     = glowRange;
        _glowLight.intensity = glowIntensityNormal;
    }

    private void ApplyGlowByLevel(int lv, bool selected)
    {
        if (_glowLight == null) return;
        Color baseColor      = LootDropModelLibrary.GetLevelColor(lv);
        _glowLight.color     = selected ? Color.Lerp(baseColor, Color.white, 0.2f) : baseColor;
        _glowLight.intensity = selected ? glowIntensitySelect : glowIntensityNormal;
    }
}
