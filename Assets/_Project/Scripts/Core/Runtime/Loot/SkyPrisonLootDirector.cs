using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 进图时按「地图编辑器的资源箱配置」从手摆的候选点位里随机抽点、生成箱子。
/// 每种箱子独立抽选（A 的名额只在 A 的点位里抽，不占 B 的）；同种被抽中的点位之间保持最小间距。
/// 场景里没有任何 SkyPrisonLootPoint、或注册表里没有这张地图的配置时什么都不做。
/// 生成方式和 UnitSpawner 同一条路：Instantiate(def.prefab) → 绑定单位定义 → ApplyDefinition。
/// </summary>
public sealed class SkyPrisonLootDirector : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        TryStart(SceneManager.GetActiveScene());   // 第一个场景：AfterSceneLoad 时 sceneLoaded 已经过了
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode mode) { TryStart(s); }

    static void TryStart(Scene s)
    {
        if (!s.IsValid()) return;
        var reg = MapLootRegistry.LoadOrNull();
        var entry = reg != null ? reg.FindByScene(s.name) : null;
        if (entry == null || entry.boxes == null || entry.boxes.Count == 0) return;
        var go = new GameObject("[LootDirector]"); SceneManager.MoveGameObjectToScene(go, s);
        go.AddComponent<SkyPrisonLootDirector>().Run(s, entry);
    }

    void Run(Scene scene, MapLootRegistry.Entry entry) { StartCoroutine(Co(scene, entry)); }

    IEnumerator Co(Scene scene, MapLootRegistry.Entry entry)
    {
        yield return null;   // 等场景里所有 Awake / Start 跑完，再收集点位
        var points = new List<SkyPrisonLootPoint>();
        foreach (var root in scene.GetRootGameObjects()) points.AddRange(root.GetComponentsInChildren<SkyPrisonLootPoint>(false));

        Transform parent = new GameObject("LootBoxes").transform; SceneManager.MoveGameObjectToScene(parent.gameObject, scene);
        bool night = SkyPrisonRaidRunContext.Night;      // 出发时选的是昼还是夜（选关界面写入）
        int totalSpawned = 0; var log = night ? "[夜] " : "[昼] ";
        foreach (var cfg in entry.boxes)
        {
            int want = cfg == null ? 0 : cfg.CountFor(night);
            if (cfg == null || cfg.box == null || cfg.box.prefab == null || want <= 0) continue;
            var pool = points.FindAll(p => p != null && p.box == cfg.box);
            var picked = Pick(pool, want, cfg.minSpacing);
            foreach (var p in picked) { Spawn(cfg.box, p.transform, parent); totalSpawned++; }
            log += $"{cfg.box.name}: {picked.Count}/{want}（候选 {pool.Count}）  ";
        }
        Debug.Log($"[LootDirector] {scene.name}：生成资源箱 {totalSpawned} 个。{log}");
    }

    /// <summary>
    /// 按权重随机抽：每次从剩余候选里抽一个，抽中后把它周围 minSpacing 米内的候选先「挡住」，直到抽满或没有候选。
    /// 如果按这个间距抽不满（点位摆得比间距要求更密），就把间距逐步收小（×0.75，直到 0），再从被挡住的点位里补，
    /// 所以间距是「尽量保持」，不会因为点位摆得密就少出箱子。
    /// </summary>
    public static List<SkyPrisonLootPoint> Pick(List<SkyPrisonLootPoint> pool, int count, float minSpacing)
    {
        var result = new List<SkyPrisonLootPoint>();
        float spacing = Mathf.Max(0f, minSpacing);
        while (result.Count < count)
        {
            float sq = spacing * spacing;
            // 这一轮的候选：没被抽过、并且离已抽中的点都不近于当前间距
            var cand = new List<SkyPrisonLootPoint>();
            foreach (var p in pool)
            {
                if (p == null || result.Contains(p)) continue;
                bool ok = true;
                if (sq > 0f) foreach (var q in result) { Vector3 d = p.transform.position - q.transform.position; d.y = 0f; if (d.sqrMagnitude < sq) { ok = false; break; } }
                if (ok) cand.Add(p);
            }
            while (result.Count < count && cand.Count > 0)
            {
                float sum = 0f; foreach (var p in cand) sum += Mathf.Max(0f, p.weight);
                int idx;
                if (sum <= 0f) idx = Random.Range(0, cand.Count);
                else { float r = Random.value * sum; idx = 0; for (int i2 = 0; i2 < cand.Count; i2++) { r -= Mathf.Max(0f, cand[i2].weight); if (r <= 0f) { idx = i2; break; } } }
                var chosen = cand[idx]; result.Add(chosen); cand.RemoveAt(idx);
                if (sq > 0f)
                {
                    Vector3 c = chosen.transform.position;
                    cand.RemoveAll(p => { Vector3 d = p.transform.position - c; d.y = 0f; return d.sqrMagnitude < sq; });
                }
            }
            if (result.Count >= count || spacing <= 0f) break;
            spacing = spacing > 1f ? spacing * 0.75f : 0f;   // 抽不满：放宽间距再补，最后一轮不限制
        }
        return result;
    }

    static void Spawn(UnitDefinition def, Transform at, Transform parent)
    {
        GameObject instance = Instantiate(def.prefab, at.position, at.rotation, parent);
        instance.name = def.prefab.name + "(Loot)";
        var binder = instance.GetComponent<UnitDefinitionRuntimeBinder>() ?? instance.GetComponentInChildren<UnitDefinitionRuntimeBinder>(true);
        if (binder == null) return;
        binder.SetUnitDefinitionAsset(def, true);
        var applier = binder.GetComponent<UnitDefinitionRuntimeApplier>() ?? binder.GetComponentInChildren<UnitDefinitionRuntimeApplier>(true);
        if (applier != null) applier.ApplyDefinition(true);
        var bridge = binder.GetComponent<UnitOverheadHealthBridge>() ?? binder.GetComponentInChildren<UnitOverheadHealthBridge>(true);
        if (bridge != null) bridge.RefreshNow();
    }
}
