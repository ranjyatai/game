using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 奔跑/跳跃扬尘特效管理器。运行时自动创建，无需手动挂载。
/// 从 Resources/FootstepVFXSettings 读取预制体列表。跟 BloodVFXManager 同一套
/// 对象池思路：按预制体分组复用，不重复 Instantiate/Destroy。
/// </summary>
public class FootstepVFXManager : MonoBehaviour
{
    public static FootstepVFXManager Instance { get; private set; }

    private FootstepVFXSettings _settings;

    private readonly Dictionary<GameObject, Queue<GameObject>> _pool = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, GameObject> _instancePrefab = new Dictionary<GameObject, GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[FootstepVFXManager]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<FootstepVFXManager>();
        Instance.LoadSettings();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (_settings != null) return;
        _settings = Resources.Load<FootstepVFXSettings>("FootstepVFXSettings");
        if (_settings == null)
            Debug.LogWarning("[FootstepVFXManager] 未找到 Resources/FootstepVFXSettings.asset，扬尘特效不会播放。");
    }

    private GameObject Acquire(GameObject prefab)
    {
        if (_pool.TryGetValue(prefab, out var queue) && queue.Count > 0)
        {
            GameObject pooled = queue.Dequeue();
            if (pooled != null)
            {
                pooled.SetActive(true);
                return pooled;
            }
        }

        GameObject inst = Instantiate(prefab);
        inst.transform.SetParent(transform, false);
        _instancePrefab[inst] = prefab;
        return inst;
    }

    private void Release(GameObject go)
    {
        if (go == null) return;
        if (!_instancePrefab.TryGetValue(go, out GameObject prefab))
        {
            Destroy(go);
            return;
        }

        go.SetActive(false);
        if (!_pool.TryGetValue(prefab, out var queue))
        {
            queue = new Queue<GameObject>();
            _pool[prefab] = queue;
        }
        queue.Enqueue(go);
    }

    private IEnumerator ReleaseAfterDelay(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        Release(go);
    }

    // 配置资产没建/数组没拖预制体时，之前是静默直接返回，排查起来跟没触发一样，
    // 完全看不出区别。只警告一次（不是每次脚步都刷），够定位问题又不刷屏。
    private bool _warnedMissingSettings;
    private bool _warnedEmptyPool;

    /// <summary>在指定位置生成一次性扬尘特效。unitSortingOrder 用 Z*100 那套跟角色一致的排序惯例，
    /// 扬尘落在角色脚下，排序比角色本体略靠后，不会盖住角色。</summary>
    public void SpawnDust(GameObject[] pool, Vector3 position, Quaternion rotation, float scale, int unitSortingOrder, float lifetime = 3f)
    {
        if (_settings == null)
        {
            if (!_warnedMissingSettings)
            {
                _warnedMissingSettings = true;
                Debug.LogWarning("[FootstepVFXManager] Resources/FootstepVFXSettings.asset 不存在，扬尘特效永远不会生成。请在 Assets/Resources 下 Create → SkyPrison → FootstepVFXSettings 并拖入预制体。");
            }
            return;
        }

        if (pool == null || pool.Length == 0)
        {
            if (!_warnedEmptyPool)
            {
                _warnedEmptyPool = true;
                Debug.LogWarning("[FootstepVFXManager] FootstepVFXSettings 里对应的预制体数组是空的（Run/Jump/Land Dust Prefabs 至少有一个没拖预制体），扬尘不会生成。");
            }
            return;
        }

        GameObject prefab = pool[Random.Range(0, pool.Length)];
        if (prefab == null) return;

        GameObject go = Acquire(prefab);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        var particleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in particleSystems)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        int world3DLayer = LayerMask.NameToLayer("World3D");
        var renderers = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (var r in renderers)
        {
            if (world3DLayer >= 0) r.gameObject.layer = world3DLayer;
            r.sortingLayerName = "Default";
            r.sortingOrder = unitSortingOrder - 1;
        }
        if (world3DLayer >= 0)
            SetLayerRecursive(go, world3DLayer);

        foreach (var ps in particleSystems)
            ps.Play();

        StartCoroutine(ReleaseAfterDelay(go, lifetime));
    }

    public GameObject[] RunDustPrefabs => _settings != null ? _settings.runDustPrefabs : null;
    public GameObject[] JumpDustPrefabs => _settings != null ? _settings.jumpDustPrefabs : null;
    public GameObject[] LandDustPrefabs => _settings != null ? _settings.landDustPrefabs : null;
    public GameObject[] DodgeDustPrefabs => _settings != null ? _settings.dodgeDustPrefabs : null;
    public GameObject[] ChargeDustPrefabs => _settings != null ? _settings.chargeDustPrefabs : null;
    public GameObject[] UnarmedAttackDustPrefabs => _settings != null ? _settings.unarmedAttackDustPrefabs : null;

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
