#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 遮挡系统的伸缩性基准——回答「遮挡物数量涨上去时，CPU 时间是不是跟着线性涨」。
///
/// 这才是这次架构迁移真正的验收标准。只看 FPS 会被 4K 填充率之类的 GPU 侧因素
/// 掩盖掉 CPU 侧的胜负，反过来也一样。要看的是曲线形状：
///
///   旧路径（CPU 逐三角面）  预期 CPU 时间随遮挡物数量近似线性上涨
///   新路径（GPU 深度比较）  预期 CPU 时间基本持平，与数量无关
///
/// 做法：复制场景里最贵的那个遮挡物到玩家周围，按 N 档分别采样，两条路径各跑一遍，
/// 最后打印对比表。复制体在测完后全部销毁，不碰场景资产。
///
/// F7 开始。整轮大约 (档数 × 2 条路径 × (预热+采样)) 秒。
/// </summary>
public sealed class OcclusionScalingBenchmark : MonoBehaviour
{
    private const KeyCode StartKey = KeyCode.F7;
    private static readonly int[] Counts = { 0, 10, 50, 100 };
    private const float SettleSeconds = 1.5f;   // 让候选扫描和休眠优化稳定下来
    private const float SampleSeconds = 3f;

    private static readonly int UseSceneDepthId = Shader.PropertyToID("_SkyPrison_UseSceneDepthOcclusion");

    private sealed class Row
    {
        public bool sceneDepth;
        public int occluders;
        public double occluderMsPerFrame;
        public double frameMs;
        public int gcDelta;
    }

    private readonly List<GameObject> clones = new List<GameObject>();
    private readonly List<Row> rows = new List<Row>();
    private readonly List<Renderer> characterRenderers = new List<Renderer>();
    private MaterialPropertyBlock block;
    private bool running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("~OcclusionScalingBenchmark");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<OcclusionScalingBenchmark>();
    }

    private void Update()
    {
        if (!running && Input.GetKeyDown(StartKey))
            StartCoroutine(RunAll());
    }

    private IEnumerator RunAll()
    {
        running = true;
        rows.Clear();

        GameObject template = FindMostExpensiveOccluder();
        if (template == null)
        {
            Debug.LogWarning("[OcclusionBenchmark] 场景里找不到带 FrontOccluderTrigger 的装饰物，无法测试。");
            running = false;
            yield break;
        }

        Transform player = FindPlayer();
        if (player == null)
        {
            Debug.LogWarning("[OcclusionBenchmark] 找不到玩家，无法在其周围放置遮挡物。");
            running = false;
            yield break;
        }

        Debug.Log($"[OcclusionBenchmark] 开始。模板：{template.name}，档位：{string.Join("/", Counts)}");

        foreach (bool sceneDepth in new[] { false, true })
        {
            SetOcclusionPath(sceneDepth);

            foreach (int n in Counts)
            {
                SpawnClones(template, player, n);
                yield return new WaitForSecondsRealtime(SettleSeconds);
                yield return Measure(sceneDepth, n);
                DespawnClones();
            }
        }

        SetOcclusionPath(false);
        Report();
        running = false;
    }

    private IEnumerator Measure(bool sceneDepth, int n)
    {
        int gcBefore = System.GC.CollectionCount(0);
        float t0 = Time.realtimeSinceStartup;
        int frames = 0;

        OccluderSelfProfiler.BeginExternalSample();

        while (Time.realtimeSinceStartup - t0 < SampleSeconds)
        {
            frames++;
            yield return null;
        }

        OccluderSelfProfiler.EndExternalSample(out double occluderMs, out _);

        float elapsed = Time.realtimeSinceStartup - t0;
        rows.Add(new Row
        {
            sceneDepth = sceneDepth,
            occluders = n,
            occluderMsPerFrame = frames > 0 ? occluderMs / frames : 0,
            frameMs = frames > 0 ? elapsed * 1000.0 / frames : 0,
            gcDelta = System.GC.CollectionCount(0) - gcBefore,
        });
    }

    /// <summary>
    /// 挑累计开销最大的那个做模板——用最贵的样本才看得出曲线差异。
    /// 便宜的样本复制一百份也可能淹没在噪声里。
    /// </summary>
    private GameObject FindMostExpensiveOccluder()
    {
        var triggers = FindObjectsByType<SkyPrisonTerrainDecorationFrontOccluderTrigger>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        GameObject best = null;
        int bestRenderers = -1;

        foreach (var t in triggers)
        {
            if (t == null)
                continue;

            // 按特征找装饰物根节点，不能靠固定层数往上爬。
            //
            // 上一版写的是「无条件往上爬 4 层」——触发器挂在 RuleRoot/BackTrigger 上，
            // 往上两层才是装饰物根，爬 4 层直接爬到了 WorldRoot/BackgroundRoot，
            // 于是基准测试准备复制整个场景背景 100 份，把编辑器卡死。
            //
            // 装饰物根节点的可靠标志是身上挂着 TerrainDecorationRuntimeBinder。
            var binder = t.GetComponentInParent<TerrainDecorationRuntimeBinder>();
            if (binder == null)
                continue;

            Transform root = binder.transform;
            int rc = root.GetComponentsInChildren<Renderer>(true).Length;
            if (rc > bestRenderers)
            {
                bestRenderers = rc;
                best = root.gameObject;
            }
        }

        if (best != null)
            Debug.Log($"[OcclusionBenchmark] 模板：{best.name}（{bestRenderers} 个渲染体）");

        return best;
    }

    private static Transform FindPlayer()
    {
        var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (unit != null)
            return unit.transform;

        GameObject go = GameObject.FindGameObjectWithTag("Player");
        return go != null ? go.transform : null;
    }

    /// <summary>
    /// 围着玩家撒 n 个复制体。半径刻意放在遮挡判定会真正评估的范围内——
    /// 撒太远它们会进休眠优化，测出来的曲线是平的，但那是假的。
    /// </summary>
    private void SpawnClones(GameObject template, Transform player, int n)
    {
        DespawnClones();
        if (n <= 0)
            return;

        Vector3 center = player.position;
        for (int i = 0; i < n; i++)
        {
            float angle = i * Mathf.PI * 2f / Mathf.Max(1, n);
            float radius = 2f + (i % 5) * 1.2f;
            Vector3 pos = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            GameObject clone = Instantiate(template, pos, template.transform.rotation);
            clone.name = $"~BenchOccluder_{i}";
            clones.Add(clone);
        }
    }

    private void DespawnClones()
    {
        foreach (GameObject c in clones)
        {
            if (c != null)
                Destroy(c);
        }
        clones.Clear();
    }

    private void SetOcclusionPath(bool sceneDepth)
    {
        block ??= new MaterialPropertyBlock();
        characterRenderers.Clear();

        foreach (UnitOcclusionMaterialReceiver receiver in
                 FindObjectsByType<UnitOcclusionMaterialReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (receiver == null)
                continue;
            characterRenderers.AddRange(receiver.GetComponentsInChildren<Renderer>(true));
        }

        foreach (Renderer r in characterRenderers)
        {
            if (r == null)
                continue;
            r.GetPropertyBlock(block);
            block.SetFloat(UseSceneDepthId, sceneDepth ? 1f : 0f);
            r.SetPropertyBlock(block);
        }
    }

    private void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[OcclusionBenchmark] 结果——看曲线形状，不是绝对值：");
        sb.AppendLine("  判定路径          遮挡物   遮挡CPU/帧    整帧耗时     GC");

        foreach (Row r in rows)
        {
            sb.AppendLine(
                $"  {(r.sceneDepth ? "GPU 深度比较  " : "CPU 三角面射线")}  " +
                $"{r.occluders,5}   {r.occluderMsPerFrame,8:F3}ms  {r.frameMs,8:F2}ms  {r.gcDelta,4}");
        }

        sb.AppendLine();
        sb.AppendLine("判读标准：CPU 三角面那几行的「遮挡CPU/帧」应随数量近似线性上涨；");
        sb.AppendLine("GPU 深度那几行应基本持平。持平才说明迁移成功——");
        sb.AppendLine("如果两条都涨，说明开销不在判定本身，得回去重新定位。");

        Debug.Log(sb.ToString());
    }

    private void OnDestroy()
    {
        DespawnClones();
    }
}
#endif
