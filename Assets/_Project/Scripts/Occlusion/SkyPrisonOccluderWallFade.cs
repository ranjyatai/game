using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 大遮挡物（墙、柱子）挡住玩家时，整面墙按到玩家的距离渐变成半透明。
///
/// 为什么不读 SkyPrisonTerrainDecorationFrontOccluderTrigger：它的判定循环已经交给 GPU
/// （SkyPrisonOcclusionMode.UseGpuDepthOcclusion：角色着色器逐像素比较深度，被挡的像素画成全息点阵），
/// 没有"这面墙正挡着玩家"的 CPU 状态可读；而旧循环的开销随遮挡物数量线性增长（实测 100 个 7.8 秒/帧），不能捡回来。
///
/// 这里的做法是常数开销：每帧从主相机向玩家身上的几个点发几根射线，打到的、够高的装饰物实例就是
/// "正在挡玩家的墙"。射线数和场景里有多少面墙完全无关。被挡住的墙目标渐隐量 = 距离系数（离玩家越近越透），
/// 再随时间平滑过渡；通过 MaterialPropertyBlock 写 _OccluderFadeAmount，白模着色器据此整面抖动裁切。
///
/// 只对高度 ≥ MinOccluderHeight 的实例生效（"大遮挡半透明、小遮挡只留轮廓"的分级），
/// 且材质必须有 _OccluderFadeAmount 属性（目前是 SkyPrison/Graybox）。甲板、箱子这类矮的不受影响。
/// </summary>
public sealed class SkyPrisonOccluderWallFade : MonoBehaviour
{
    private static readonly int FadeId = Shader.PropertyToID("_OccluderFadeAmount");
    private static readonly int SplitId = Shader.PropertyToID("_FadeSplit");
    private static readonly int PlayerId = Shader.PropertyToID("_SkyPrison_WallFadePlayer");

    private const float MinOccluderHeight = 3f;   // 低于这个高度的实例不参与
    private const float MinWallLength = 4f;        // 平面上最长边小于这个值的是柱子，不参与渐隐
    private const float FullFadeDistance = 6f;     // 玩家离墙 ≤ 这个距离：目标最透
    private const float NoFadeDistance = 14f;      // 玩家离墙 ≥ 这个距离：目标不透
    private const float FadeSpeed = 3.5f;          // 渐隐量每秒变化多少（0→1 约 0.3 秒）
    private const int MaxHits = 32;
    private const float SwapAmount = 0.25f;        // 渐隐量到这个值才换成真半透明材质、并且不再算角色的遮挡物

    /// <summary>正处于"真半透明"状态的墙的渲染器：落地深度图不再把它们当遮挡物，角色在它后面正常渲染而不是全息点阵。</summary>
    private static readonly HashSet<Renderer> FadingRenderers = new HashSet<Renderer>();
    public static bool IsFading(Renderer r) { return FadingRenderers.Count > 0 && FadingRenderers.Contains(r); }

    private sealed class State
    {
        public Renderer[] renderers;
        public Material[][] origMats;
        public bool swapped;
        public bool split;   // 所有渲染器都是单子网格：原材质 + 半透明克隆叠画（克隆画在最后一个子网格上）
        public Bounds bounds;
        public bool eligible;
        public float amount;
        public bool applied;
    }

    private readonly Dictionary<int, State> states = new Dictionary<int, State>();
    private readonly Dictionary<int, TerrainDecorationRuntimeBinder> colliderOwner = new Dictionary<int, TerrainDecorationRuntimeBinder>();
    private readonly HashSet<int> occludedNow = new HashSet<int>();
    private readonly RaycastHit[] hits = new RaycastHit[MaxHits];
    private MaterialPropertyBlock block;
    private Transform playerTransform;
    private int lastUpdateFrame = -1;

    /// <summary>调试/验证用：当前正在渐隐（渐隐量 > 0）的实例名和渐隐量。</summary>
    public static readonly List<string> DebugActive = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // HideFlags.DontSave 的物体会跨 Play 会话留在编辑器里：每进一次 Play 就多一个实例，
        // 多个实例各自维护渐隐状态、各自换材质，互相覆盖（实测累计到 24 个），墙就会莫名闪烁/消失。
        // 所以启动时先清掉所有旧实例，只留这一个。
        foreach (var old in Resources.FindObjectsOfTypeAll<SkyPrisonOccluderWallFade>())
        {
            if (old != null)
                Destroy(old.gameObject);
        }

        var go = new GameObject("~SkyPrisonOccluderWallFade");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave;   // 不用 HideAndDontSave，原因见 SkyPrisonPlayerPositionBroadcaster
        go.AddComponent<SkyPrisonOccluderWallFade>();
    }

    private void OnEnable()
    {
        block = new MaterialPropertyBlock();
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        foreach (var kv in states)
            Apply(kv.Value, 0f);
        states.Clear();
        FadingRenderers.Clear();
        foreach (var kv in fadeClones)
            if (kv.Value != null) Destroy(kv.Value);
        fadeClones.Clear();
        colliderOwner.Clear();
    }

    private void HandleBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        // 只在主相机、每帧一次（叠加相机不重复算）。放在 beginCameraRendering 而不是 LateUpdate：
        // 相机位置要用 Cinemachine 这一帧定位完之后的，原因同 SkyPrisonPlayerPositionBroadcaster。
        if (cam == null || cam.cameraType != CameraType.Game || !cam.CompareTag("MainCamera"))
            return;
        if (lastUpdateFrame == Time.frameCount)
            return;
        lastUpdateFrame = Time.frameCount;

        if (playerTransform == null)
        {
            var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (unit != null) playerTransform = unit.transform;
        }

        occludedNow.Clear();
        if (playerTransform != null)
        {
            Vector3 pp = playerTransform.position;
            Vector3 cf = cam.transform.forward;
            cf.y = 0f;
            cf = cf.sqrMagnitude < 1e-4f ? Vector3.forward : cf.normalized;
            Shader.SetGlobalVector(PlayerId, new Vector4(pp.x, pp.z, cf.x, cf.z));
            CollectOccluders(cam);
        }
        UpdateStates(playerTransform != null ? playerTransform.position : Vector3.zero);
    }

    private void CollectOccluders(Camera cam)
    {
        Vector3 camPos = cam.transform.position;
        Vector3 right = cam.transform.right;
        Vector3 p = playerTransform.position;
        // 玩家身上取 5 个点：脚、身、头，加身体左右各一个（角色精灵比碰撞胶囊大一圈，只看逻辑点会漏掉上半身被挡的情形）。
        Vector3[] samples =
        {
            p + Vector3.up * 0.3f,
            p + Vector3.up * 1.2f,
            p + Vector3.up * 2.4f,
            p + Vector3.up * 1.2f + right * 0.6f,
            p + Vector3.up * 1.2f - right * 0.6f,
        };

        // 每面墙累计被几根射线打中：身体正中的三根（脚/身/头）各算 1，左右两根只算 0.5，
        // 总分 ≥ 1 才算"挡住了玩家"。只有左右两根擦到墙边（玩家贴着侧墙站）不触发，
        // 但墙真的压到身体正中、或左右两根同时被挡，都会触发。
        scores.Clear();
        for (int s = 0; s < samples.Length; s++)
        {
            Vector3 to = samples[s] - camPos;
            float dist = to.magnitude;
            if (dist < 0.2f) continue;
            float weight = s < 3 ? 1f : 0.5f;
            int n = Physics.RaycastNonAlloc(camPos, to / dist, hits, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                // 每个装饰物实例都带一个 OccluderProbe_Box（整个物体的包围盒）。弧墙的包围盒比墙大得多
                // （15.7×12×15.7 米的方盒里只有一道细弧），射线打到包围盒就会误判成"墙挡住了玩家"，
                // 于是墙明明在旁边、没挡着人也跟着渐隐。只认真实的碰撞网格。
                if (hits[i].collider.name == "OccluderProbe_Box")
                    continue;
                var owner = ResolveOwner(hits[i].collider);
                if (owner == null) continue;
                var st = GetState(owner);
                if (!st.eligible) continue;
                int id = owner.GetInstanceID();
                scores.TryGetValue(id, out float prev);
                scores[id] = prev + weight;
            }
        }
        foreach (var kv in scores)
            if (kv.Value >= 1f)
                occludedNow.Add(kv.Key);
    }

    private readonly Dictionary<int, float> scores = new Dictionary<int, float>();

    private TerrainDecorationRuntimeBinder ResolveOwner(Collider c)
    {
        if (c == null) return null;
        int id = c.GetInstanceID();
        if (!colliderOwner.TryGetValue(id, out var owner))
        {
            owner = c.GetComponentInParent<TerrainDecorationRuntimeBinder>();
            colliderOwner[id] = owner;
        }
        return owner;
    }

    private State GetState(TerrainDecorationRuntimeBinder binder)
    {
        int key = binder.GetInstanceID();
        if (states.TryGetValue(key, out var st))
            return st;

        st = new State();
        Transform visualRoot = binder.transform.Find("VisualRoot");
        var list = new List<Renderer>();
        if (visualRoot != null)
        {
            foreach (var r in visualRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(FadeId))
                    list.Add(r);
            }
        }
        st.renderers = list.ToArray();
        st.split = true;
        st.origMats = new Material[st.renderers.Length][];
        for (int i = 0; i < st.renderers.Length; i++)
        {
            st.origMats[i] = st.renderers[i].sharedMaterials;
            var mf = st.renderers[i].GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount != 1 || st.origMats[i].Length != 1)
                st.split = false;
        }
        if (st.renderers.Length > 0)
        {
            st.bounds = st.renderers[0].bounds;
            for (int i = 1; i < st.renderers.Length; i++) st.bounds.Encapsulate(st.renderers[i].bounds);
        }
        // 柱子（平面尺寸很小的高物体）不渐隐：走地形装饰物原来的遮挡逻辑，角色在柱子后面显示全息点阵。
        // 渐隐只给墙——墙有一个方向很长。
        float planMax = Mathf.Max(st.bounds.size.x, st.bounds.size.z);
        st.eligible = st.renderers.Length > 0 && st.bounds.size.y >= MinOccluderHeight && planMax >= MinWallLength;
        // 脚本热重载时，墙上可能还挂着上一个实例留下的半透明克隆：此时拿不到原材质，这面墙本次不参与（重新进 Play 即恢复）。
        for (int i = 0; i < st.origMats.Length && st.eligible; i++)
            foreach (var m in st.origMats[i])
                if (m != null && m.name.Contains("(WallFade)")) { st.eligible = false; break; }
        states[key] = st;
        return st;
    }

    private void UpdateStates(Vector3 playerPos)
    {
        float dt = Time.deltaTime;
        DebugActive.Clear();
        foreach (var kv in states)
        {
            var st = kv.Value;
            if (!st.eligible) continue;

            float target = 0f;
            if (occludedNow.Contains(kv.Key))
            {
                Vector3 q = new Vector3(playerPos.x, st.bounds.center.y, playerPos.z);
                Vector3 cp = st.bounds.ClosestPoint(q);
                float d = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(cp.x, cp.z));
                target = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullFadeDistance, NoFadeDistance, d));
            }

            float next = Mathf.MoveTowards(st.amount, target, FadeSpeed * dt);
            if (Mathf.Approximately(next, st.amount) && (st.amount > 0.001f) == st.applied)
            {
                if (st.amount > 0.001f) DebugActive.Add(kv.Key + ":" + st.amount.ToString("0.00"));
                continue;
            }
            st.amount = next;
            Apply(st, st.amount);
            if (st.amount > 0.001f) DebugActive.Add(kv.Key + ":" + st.amount.ToString("0.00"));
        }
    }

    private void Apply(State st, float amount)
    {
        if (st.renderers == null) return;
        bool on = amount > 0.001f;
        // 渐隐中换成真半透明克隆（不写深度、队列在角色之前）：不进深度图，角色就不会被判成"被挡"而画成全息点阵。
        // 渐隐量回到 0 就换回原材质，平时墙完全是原来的不透明材质。
        // 迟滞：渐隐量到 0.25 才换（此前保持抖动裁切 + 角色全息），回落到 0.02 才换回来，避免在阈值附近来回切。
        bool swap = st.swapped ? amount > 0.02f : amount >= SwapAmount;
        if (swap != st.swapped)
        {
            for (int i = 0; i < st.renderers.Length; i++)
            {
                var r = st.renderers[i];
                if (r == null) continue;
                if (!swap) r.sharedMaterials = st.origMats[i];
                else if (st.split) r.sharedMaterials = new[] { st.origMats[i][0], GetFadeMaterials(st.origMats[i])[0] };
                else r.sharedMaterials = GetFadeMaterials(st.origMats[i]);
                if (swap) FadingRenderers.Add(r); else FadingRenderers.Remove(r);
            }
            st.swapped = swap;
        }
        for (int i = 0; i < st.renderers.Length; i++)
        {
            var r = st.renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetFloat(FadeId, on ? amount : 0f);
            block.SetFloat(SplitId, (st.swapped && st.split) ? 1f : 0f);
            r.SetPropertyBlock(block);
        }
        st.applied = on;
    }

    private readonly Dictionary<Material, Material> fadeClones = new Dictionary<Material, Material>();

    private Material[] GetFadeMaterials(Material[] orig)
    {
        var result = new Material[orig.Length];
        for (int i = 0; i < orig.Length; i++)
        {
            var m = orig[i];
            if (m == null || !m.HasProperty(FadeId)) { result[i] = m; continue; }
            if (!fadeClones.TryGetValue(m, out var clone) || clone == null)
            {
                clone = new Material(m) { name = m.name + " (WallFade)", hideFlags = HideFlags.HideAndDontSave };
                clone.SetFloat("_FadeTransparent", 1f);
                clone.SetFloat("_FadeSrcBlend", (float)BlendMode.SrcAlpha);
                clone.SetFloat("_FadeDstBlend", (float)BlendMode.OneMinusSrcAlpha);
                clone.SetFloat("_FadeZWrite", 0f);
                clone.SetOverrideTag("RenderType", "Transparent");
                clone.renderQueue = (int)RenderQueue.Transparent;   // 3000，在角色（3040）之前画
                fadeClones[m] = clone;
            }
            result[i] = clone;
        }
        return result;
    }
}
