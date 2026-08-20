using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;

/// <summary>
/// 闪避 / 突刺(蓄力攻击释放冲刺)期间生成半透明角色残影。
///
/// 残影是 Spine 网格在生成瞬间的一次"快照"——不是持续跟着骨骼动，是当帧的姿势
/// 定格下来，独立淡出。用 Spine/Skeleton Fill 这个内置 Shader（纯色描边填充用的
/// 那个）而不是角色本体的遮挡合成材质：残影不需要参与遮挡判定/环境光，只要一个
/// 干净的、按 Alpha 淡出的纯色剪影，混入本体材质的复杂 Shader 反而容易出问题。
///
/// 挂在角色根对象上，由 UnitDefinitionRuntimeApplier 自动添加（跟
/// UnitDodgeVFXBridge 同一个套路：轮询 UnitMovementController 的状态，不需要
/// 闪避/突刺代码反过来关心"残影"这件事）。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonAfterimageEmitter : MonoBehaviour
{
    [SerializeField] private UnitMovementController movementController;
    [SerializeField] private SkeletonAnimation skeletonAnimation;
    [SerializeField] private MeshFilter skeletonMeshFilter;

    [Tooltip("闪避/突刺期间生成几个残影。")]
    [SerializeField] private int ghostCount = 4;

    [Tooltip("用来把 ghostCount 个残影摊开的预估动作时长（秒）——不是精确读闪避/突刺" +
             "的真实时长（两者时长来源不同、还可能被动画片段动态延长），只是给\"多久生成一个\"" +
             "定个节奏。动作提前结束不影响已生成的残影继续淡出；动作比预估的长，" +
             "残影会在 ghostCount 个生成完之后停下，不会一直刷。")]
    [SerializeField] private float estimatedActionDuration = 0.35f;

    [Tooltip("单个残影从生成到完全消失的时间（秒）。")]
    [SerializeField] private float ghostFadeDuration = 0.28f;

    [Tooltip("残影刚生成时的透明度（0~1）。之前的 Shader 用法 bug 修好之前调的 0.16" +
             "实测太淡看不见——这个 Shader 真正生效之后，同样的数值视觉效果比预期弱，" +
             "调回一个看得清但依然偏暗的值，需要的话继续调。")]
    [Range(0f, 1f)]
    [SerializeField] private float ghostStartAlpha = 0.38f;

    [Tooltip("残影染色。暗淡但不能淡到跟透明度叠加后完全看不见。")]
    [SerializeField] private Color ghostTint = new Color(0.32f, 0.4f, 0.5f, 1f);

    private static Shader _fillShaderCache;
    private const string FillShaderName = "Spine/Skeleton Fill";

    private bool _wasActive;
    private float _actionStartTime;
    private float _emitInterval;
    private int _emittedThisAction;

    private readonly List<Ghost> _pool = new List<Ghost>();

    private sealed class Ghost
    {
        public GameObject go;
        public MeshFilter filter;
        public MeshRenderer meshRenderer;
        public Mesh mesh;
        public Material[] materials;
        public Color32[] baseVertexColors; // 生成瞬间的原始顶点色，淡出时在这份基础上缩放 alpha
        public Color32[] scratchVertexColors; // 每帧复用的缩放结果缓冲，不重新分配
        public float spawnTime = -999f;
        public bool active;
    }

    private void Awake() => AutoResolve();
    private void OnEnable() => AutoResolve();

    private void AutoResolve()
    {
        if (movementController == null)
            movementController = GetComponent<UnitMovementController>()
                              ?? GetComponentInParent<UnitMovementController>();

        if (skeletonAnimation == null)
            skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

        if (skeletonMeshFilter == null && skeletonAnimation != null)
            skeletonMeshFilter = skeletonAnimation.GetComponent<MeshFilter>();
    }

    public void Configure(UnitMovementController controller, SkeletonAnimation skeleton)
    {
        movementController = controller;
        skeletonAnimation = skeleton;
        skeletonMeshFilter = skeleton != null ? skeleton.GetComponent<MeshFilter>() : null;
    }

    private void Update()
    {
        if (movementController == null || skeletonAnimation == null)
            return;

        bool active = movementController.IsDodging || movementController.IsChargeDashing;

        if (active && !_wasActive)
        {
            _actionStartTime = Time.time;
            _emittedThisAction = 0;
            _emitInterval = Mathf.Max(0.02f, estimatedActionDuration) / Mathf.Max(1, ghostCount);
            SpawnGhost();
            _emittedThisAction = 1;
        }
        else if (active)
        {
            int shouldHaveEmitted = Mathf.Min(
                ghostCount,
                1 + Mathf.FloorToInt((Time.time - _actionStartTime) / _emitInterval));

            while (_emittedThisAction < shouldHaveEmitted)
            {
                SpawnGhost();
                _emittedThisAction++;
            }
        }

        _wasActive = active;

        UpdateGhostFades();
    }

    private void SpawnGhost()
    {
        if (skeletonMeshFilter == null)
            return;

        // SkeletonRenderer 每帧把生成好的网格塞进自己 MeshFilter.sharedMesh——直接读
        // 这个就是"当前帧最终渲染用的网格"，不需要 Spine 额外提供什么"最后一帧网格"
        // 的 API（这个版本也确实没有）。下面立刻把数据拷进残影自己的 Mesh 实例，
        // 不持有这个引用——它下一帧会被 SkeletonRenderer 自己的双缓冲换成别的对象。
        Mesh sourceMesh = skeletonMeshFilter.sharedMesh;
        if (sourceMesh == null || sourceMesh.vertexCount == 0)
            return;

        Ghost ghost = GetPooledGhost();

        ghost.mesh.Clear();
        ghost.mesh.vertices = sourceMesh.vertices;
        ghost.mesh.uv = sourceMesh.uv;
        ghost.mesh.subMeshCount = sourceMesh.subMeshCount;
        for (int i = 0; i < sourceMesh.subMeshCount; i++)
            ghost.mesh.SetTriangles(sourceMesh.GetTriangles(i), i);

        // Shader 的最终 alpha = 贴图自身 alpha × 顶点色 alpha，跟材质的 _FillColor.a
        // 完全无关（读过 Spine-Skeleton-Fill.shader 源码确认的）——淡出只能靠缩放
        // 顶点色的 alpha 通道来做，缓存一份"这一次生成时的原始顶点色"作为 100% 强度
        // 基准，后面每帧在这份基准上乘当前淡出比例，而不是在上一帧已经缩放过的结果
        // 上再缩一次（否则会指数级衰减，速度对不上 ghostFadeDuration）。
        Color32[] sourceColors = sourceMesh.colors32;
        if (ghost.baseVertexColors == null || ghost.baseVertexColors.Length != sourceColors.Length)
        {
            ghost.baseVertexColors = new Color32[sourceColors.Length];
            ghost.scratchVertexColors = new Color32[sourceColors.Length];
        }
        System.Array.Copy(sourceColors, ghost.baseVertexColors, sourceColors.Length);

        EnsureMaterials(ghost, sourceMesh.subMeshCount);

        ghost.go.transform.SetPositionAndRotation(skeletonAnimation.transform.position, skeletonAnimation.transform.rotation);
        ghost.go.transform.localScale = skeletonAnimation.transform.lossyScale;

        // 跟 UnitDodgeVFXBridge 的扬尘同一套排序惯例：这个项目按 Z 深度算 2.5D 精灵排序，
        // 不是固定值。残影定格在某个世界位置后，排序也要按那个位置重算，不能沿用
        // "生成残影物体那一刻角色当前的排序"——角色后续还会继续移动，两者会脱节。
        ghost.meshRenderer.sortingOrder = Mathf.RoundToInt(ghost.go.transform.position.z * 100f);

        ghost.spawnTime = Time.time;
        ghost.active = true;
        ghost.go.SetActive(true);
        ApplyGhostAlpha(ghost, ghostStartAlpha);
    }

    private void EnsureMaterials(Ghost ghost, int subMeshCount)
    {
        if (ghost.materials != null && ghost.materials.Length == subMeshCount)
            return;

        if (_fillShaderCache == null)
            _fillShaderCache = Shader.Find(FillShaderName);
        if (_fillShaderCache == null)
        {
            Debug.LogWarning($"[SkyPrisonAfterimageEmitter] 找不到 Shader「{FillShaderName}」，残影不会显示。");
            return;
        }

        Renderer sourceRenderer = skeletonAnimation.GetComponent<Renderer>();
        Material[] sourceMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : null;

        var materials = new Material[subMeshCount];
        for (int i = 0; i < subMeshCount; i++)
        {
            var mat = new Material(_fillShaderCache) { hideFlags = HideFlags.DontSave };
            Texture sourceTex = sourceMaterials != null && i < sourceMaterials.Length && sourceMaterials[i] != null
                ? sourceMaterials[i].mainTexture
                : null;
            if (sourceTex != null)
                mat.SetTexture("_MainTex", sourceTex);

            // _FillPhase 默认是 0——那个状态下 shader 显示的是角色贴图原色，_FillColor
            // 完全不生效。必须显式拨到 1 才会真的用 _FillColor.rgb 画纯色剪影。
            mat.SetFloat("_FillPhase", 1f);
            mat.SetColor("_FillColor", ghostTint);
            materials[i] = mat;
        }

        ghost.materials = materials;
        ghost.meshRenderer.sharedMaterials = materials;
    }

    private void UpdateGhostFades()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            Ghost ghost = _pool[i];
            if (!ghost.active)
                continue;

            float t = (Time.time - ghost.spawnTime) / Mathf.Max(0.01f, ghostFadeDuration);
            if (t >= 1f)
            {
                ghost.active = false;
                ghost.go.SetActive(false);
                continue;
            }

            ApplyGhostAlpha(ghost, ghostStartAlpha * (1f - t));
        }
    }

    private void ApplyGhostAlpha(Ghost ghost, float alpha)
    {
        if (ghost.baseVertexColors == null || ghost.mesh == null)
            return;

        // 最终 alpha 由顶点色的 alpha 通道决定（见 SpawnGhost 里的说明），所以淡出
        // 要靠重写顶点色数组，不是改材质属性。在"生成瞬间的原始顶点色"上按当前
        // 淡出比例缩放，不在上一次已经缩放过的结果上再缩一次。
        Color32[] baseColors = ghost.baseVertexColors;
        Color32[] scaled = ghost.scratchVertexColors;
        byte alphaByte = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
        for (int i = 0; i < baseColors.Length; i++)
        {
            Color32 c = baseColors[i];
            scaled[i] = new Color32(c.r, c.g, c.b, (byte)((c.a * alphaByte) / 255));
        }
        ghost.mesh.colors32 = scaled;
    }

    private Ghost GetPooledGhost()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].active)
                return _pool[i];
        }

        GameObject go = new GameObject("Afterimage") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(null, false); // 世界空间独立存在，不跟着角色骨骼继续动
        MeshFilter filter = go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.gameObject.layer = skeletonAnimation != null ? skeletonAnimation.gameObject.layer : gameObject.layer;

        Mesh mesh = new Mesh { name = "AfterimageMesh", hideFlags = HideFlags.DontSave };
        filter.sharedMesh = mesh;

        var ghost = new Ghost
        {
            go = go,
            filter = filter,
            meshRenderer = renderer,
            mesh = mesh,
            active = false,
        };
        go.SetActive(false);
        _pool.Add(ghost);
        return ghost;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            Ghost ghost = _pool[i];
            if (ghost.go != null) Destroy(ghost.go);
            if (ghost.mesh != null) Destroy(ghost.mesh);
            if (ghost.materials != null)
                foreach (Material m in ghost.materials)
                    if (m != null) Destroy(m);
        }
        _pool.Clear();
    }
}
