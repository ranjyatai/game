using System.Collections.Generic;
using BloodEffectsPack;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 血液特效管理器。运行时自动创建，无需手动挂载。
/// 从 Resources/BloodVFXSettings 读取预制体列表。
/// </summary>
public class BloodVFXManager : MonoBehaviour
{
    public static BloodVFXManager Instance { get; private set; }

    // ── 地面血迹持久化 ────────────────────────────────────────────────────────
    private readonly List<GameObject> _persistentDecals = new List<GameObject>();
    private const int MaxDecals = 60; // 超过时移除最旧的

    // 血迹贴花的排序值必须是固定常量，不能跟着角色/掉落物的 sortingOrder 动态换算——
    // 那样等于没脱钩，角色一动排序值跟着变，又会被牵连出"角色前后关系影响血迹"的问题。
    // 贴在地上的血迹理应无条件排在所有角色/掉落物（它们的 SortingGroup 数值不会比这个
    // 还低）后面，直接给一个够低的固定值，永远垫底。
    // 注意：sortingOrder 底层是 16 位有符号整数（范围 -32768~32767），之前写的
    // -100000 远超这个下限，赋值时静默溢出绕成了 +31072（正好排到最前面，跟预期完全
    //相反）——同类坑之前在 Canvas.sortingOrder 上也踩过一次，这次是 Renderer/
    // SortingGroup 版本，必须卡在合法范围内，不能拍脑袋写任意大的数。
    //
    // 之前这里直接卡在16位下限-32768("永远垫底")，但地图装饰物贴花(马路线等，见
    // SkyPrisonMapObjectPlacementToolWindow.SafeGroundDecalSortingOrder)也是同一套
    // sortingOrder机制、同样想垫底，两边都想抢最底那个值就没法分先后——而且地面
    // 装饰物那边之前的值本身也溢出了(-400000绕成+58752再解释成有符号数变成-6784)，
    // 意外地比血迹这个-32768还大，血迹反而被装饰物盖住。血迹应该"贴在装饰物贴花
    // 上面"（视觉上是先铺路面再溅血），不用垫到绝对最底——留出比装饰物新值
    // (-32767)高一点的余量即可，两边都在16位范围内，顺序明确。
    private const int DecalSortingOrder = -32760;

    // ── 和谐模式 ──────────────────────────────────────────────────────────────
    private static bool _harmonyMode = false;
    private static readonly Color HarmonyColor = new Color(0.05f, 0.05f, 0.05f, 1f);
    private static readonly Color NormalDecalColor = new Color(0.15f, 0.004f, 0.004f);

    /// <summary>和谐模式：开启后所有血液颜色变为黑色。
    /// 切换时必须连带把地面上已经生成的血迹一起改色——不能只对之后新生成的生效，
    /// 不然会出现"开关切了，但地上原有那些血迹颜色没变"这种一半红一半黑的怪状态。</summary>
    public static bool HarmonyMode
    {
        get => _harmonyMode;
        set
        {
            if (_harmonyMode == value) return;
            _harmonyMode = value;
            Instance?.ReapplyHarmonyModeToExistingDecals();
        }
    }

    private void ReapplyHarmonyModeToExistingDecals()
    {
        Color resolvedColor = _harmonyMode ? HarmonyColor : NormalDecalColor;
        float colorIntensity = _harmonyMode ? 1f : 0.55f;

        foreach (var decalGo in _persistentDecals)
        {
            if (decalGo == null) continue;
            var modifier = decalGo.GetComponent<BloodModifier_URP>()
                        ?? decalGo.GetComponentInChildren<BloodModifier_URP>(true);
            if (modifier == null) continue;

            var s = modifier.settings;
            s.color          = resolvedColor;
            s.colorIntensity = colorIntensity;
            modifier.ApplyToMaterialInstancesInRuntime();
        }
    }

    // ── 自动初始化 ────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[BloodVFXManager]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<BloodVFXManager>();
        Instance.LoadSettings();
    }

    private BloodVFXSettings _settings;

    // ── 喷溅特效对象池 ────────────────────────────────────────────────────────
    // 之前每次命中都 Instantiate() 一个全新的喷溅特效物体、4秒后 Destroy()——
    // 战斗密集时这一来一回的建/销毁是明确的GC分配来源（性能排查中确认过命中瞬间
    // GC次数飙升跟这里对得上）。改成对象池：按预制体分组复用，不再新建/销毁物体，
    // 只是隐藏/显示 + 重置粒子状态，视觉表现完全不变。
    private readonly Dictionary<GameObject, Queue<GameObject>> _splashPool = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, GameObject> _splashInstancePrefab = new Dictionary<GameObject, GameObject>();

    private GameObject AcquireSplash(GameObject prefab)
    {
        if (_splashPool.TryGetValue(prefab, out var queue) && queue.Count > 0)
        {
            GameObject pooled = queue.Dequeue();
            if (pooled != null)
            {
                pooled.SetActive(true);
                return pooled;
            }
        }

        GameObject inst = Instantiate(prefab);
        // 挂在常驻的 Manager 节点下（DontDestroyOnLoad），池子里的实例才能跨场景存活，
        // 不然切场景时会被当成场景物体一起销毁，池子里剩一堆悬空引用，等于白建。
        inst.transform.SetParent(transform, false);
        _splashInstancePrefab[inst] = prefab;
        return inst;
    }

    private void ReleaseSplash(GameObject go)
    {
        if (go == null) return;
        if (!_splashInstancePrefab.TryGetValue(go, out GameObject prefab))
        {
            Destroy(go); // 不认识的实例（理论上不会发生），保底销毁而不是内存泄漏
            return;
        }

        go.SetActive(false);
        if (!_splashPool.TryGetValue(prefab, out var queue))
        {
            queue = new Queue<GameObject>();
            _splashPool[prefab] = queue;
        }
        queue.Enqueue(go);
    }

    private System.Collections.IEnumerator ReleaseSplashAfterDelay(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        ReleaseSplash(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadSettings();
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnSceneUnloaded(Scene scene)
    {
        foreach (var go in _persistentDecals)
            if (go != null) Destroy(go);
        _persistentDecals.Clear();
    }

    private void LoadSettings()
    {
        if (_settings != null) return;
        _settings = Resources.Load<BloodVFXSettings>("BloodVFXSettings");
        if (_settings == null)
            Debug.LogWarning("[BloodVFXManager] 未找到 Resources/BloodVFXSettings.asset，血液特效不会播放。");
    }

    // ── 对外接口 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 在受击位置生成血液飞溅。
    /// </summary>
    /// <param name="hitPosition">受击世界坐标</param>
    /// <param name="attackerPosition">攻击者世界坐标（用于确定喷溅方向）</param>
    /// <param name="unitBloodColor">单位定义的血液颜色</param>
    /// <param name="enable">该单位是否启用血液特效</param>
    public void SpawnSplash(GameObject targetUnit, Vector3 attackerPosition,
                            Color unitBloodColor, UnitBloodVFXType bloodType = UnitBloodVFXType.Normal)
    {
        if (bloodType == UnitBloodVFXType.None) return;
        if (_settings == null) return;

        // 选择特效预制体池：特殊类型优先使用专用池，为空时回退到通用飞溅
        GameObject[] pool = bloodType switch
        {
            UnitBloodVFXType.MetalSpark   => (_settings.metalSparkPrefabs  != null && _settings.metalSparkPrefabs.Length  > 0) ? _settings.metalSparkPrefabs  : _settings.splashPrefabs,
            UnitBloodVFXType.EnergyBurst  => (_settings.energyBurstPrefabs != null && _settings.energyBurstPrefabs.Length > 0) ? _settings.energyBurstPrefabs : _settings.splashPrefabs,
            _                             => _settings.splashPrefabs,
        };

        if (pool == null || pool.Length == 0) return;

        GameObject prefab = pool[Random.Range(0, pool.Length)];
        if (prefab == null) return;

        // 用 Renderer 包围盒求角色视觉中心（约身体 60% 高处）
        Vector3 spawnPos = ResolveHitPosition(targetUnit);

        // 朝攻击者反向，同时向上倾斜以适配等距俯视摄像机
        Vector3 dir = spawnPos - attackerPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
        // 上倾约 55°：让血柱朝摄像机方向散开，在俯视下有宽度
        Vector3 sprayDir = (dir.normalized + Vector3.up * 1.4f).normalized;
        Quaternion rot = Quaternion.LookRotation(sprayDir, Vector3.up);

        GameObject go = AcquireSplash(prefab);
        go.transform.SetPositionAndRotation(spawnPos, rot);
        go.transform.localScale = Vector3.one * 2.2f;

        // 复用出来的实例可能还带着上一次没播完的粒子——必须先清空再重新播放，
        // 否则会出现"新特效叠加着旧特效残留粒子"的观感问题。
        var reusedParticleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in reusedParticleSystems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 覆盖为压暗的血色（和谐模式→黑色；DarkBlood→单位自定义色；Normal→深暗血红）
        {
            var modifier = go.GetComponent<BloodModifier_URP>()
                        ?? go.GetComponentInChildren<BloodModifier_URP>(true);
            if (modifier != null)
            {
                Color resolvedColor = _harmonyMode       ? HarmonyColor
                                    : bloodType == UnitBloodVFXType.DarkBlood ? unitBloodColor
                                    : new Color(0.20f, 0.005f, 0.005f);
                var s = modifier.settings;
                s.color                 = resolvedColor;
                s.colorIntensity        = _harmonyMode ? 1f : 0.65f;
                s.smoothness            = 0f;
                s.albedoPower           = 1.6f;
                s.ambientColorIntensity = 0.4f;
                modifier.ApplyToMaterialInstancesInRuntime();
            }
        }

        // 设为 World3D 层，并覆盖排序使血液渲染在角色之上
        int world3DLayer = LayerMask.NameToLayer("World3D");
        int unitSortOrder = GetUnitSortingOrder(targetUnit);
        var psRenderers = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (var r in psRenderers)
        {
            if (world3DLayer >= 0) r.gameObject.layer = world3DLayer;
            r.sortingLayerName = "Default";
            r.sortingOrder     = unitSortOrder + 5;
        }
        if (world3DLayer >= 0)
            SetLayerRecursive(go, world3DLayer);

        // 清空阶段(Stop)之后要重新播放，否则复用出来的实例会一直停在空状态不喷。
        foreach (var ps in reusedParticleSystems)
        {
            ps.Play();
        }

        // 之前是兜底 Destroy(go, 4f)；现在改成还回对象池，不再真的销毁物体。
        StartCoroutine(ReleaseSplashAfterDelay(go, 4f));

        // 地面血迹贴花（持久）
        if (_settings.decalPrefabs != null && _settings.decalPrefabs.Length > 0)
        {
            GameObject decalPrefab = _settings.decalPrefabs[Random.Range(0, _settings.decalPrefabs.Length)];
            if (decalPrefab != null)
            {
                // 血迹落点不能死钉在角色正下方——真实情况下血是往"被打飞的方向"喷溅出去
                // 散落一片，不是垂直滴在原地。以 dir（攻击者→受击者的水平方向，跟
                // 上面血浆喷溅用的是同一个方向）为主方向，叠加随机偏转角度+随机距离，
                // 让落点在攻击方向附近扇形散开，每次命中位置都不一样。
                float scatterAngle    = Random.Range(-60f, 60f);
                Vector3 scatterDir    = Quaternion.Euler(0f, scatterAngle, 0f) * dir.normalized;
                float scatterDistance = Random.Range(0.3f, 1.4f);
                Vector3 decalXZ       = new Vector3(spawnPos.x, 0f, spawnPos.z) + scatterDir * scatterDistance;
                Vector3 decalPos      = new Vector3(decalXZ.x, targetUnit.transform.position.y + 0.02f, decalXZ.z);
                // 内置 Quad 网格默认是"立牌"朝向（法线朝前），躺平贴地要绕X轴转90°，
                // 再叠加一个随机Y轴自转让每次血迹朝向不一样——只转Y轴不会让它躺平，
                // 之前换成不走DecalProjector的Quad版本时漏了这一步，导致血迹立在地上
                // 不贴地。
                Quaternion decalRot = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
                GameObject decalGo = Instantiate(decalPrefab, decalPos, decalRot);

                // 这套素材固定形状只有4种，命中密集时几滩贴花挨在一起很容易被看出
                // 是同一张图——加随机缩放+随机镜像翻转，同一张图换个大小/照镜子
                // 看着就不容易撞脸了，比加更多形状素材便宜很多。
                const float DecalBaseSizeMultiplier = 3f;
                float scale = Random.Range(0.9f, 1.7f) * DecalBaseSizeMultiplier;
                float mirrorX = Random.value < 0.5f ? -1f : 1f;
                decalGo.transform.localScale = new Vector3(scale * mirrorX, scale, scale);

                // 物理层（影响阴影/碰撞），不影响 Decal 渲染层
                int physicsLayer = LayerMask.NameToLayer("World3D");
                if (physicsLayer >= 0) SetLayerRecursive(decalGo, physicsLayer);

                // 接入项目统一的 SortingGroup 排序体系，用固定常量（不跟着角色/掉落物的
                // sortingOrder动态换算），确保不管角色怎么前后移动，血迹永远垫在最底下。
                var decalSortingGroup = decalGo.AddComponent<SortingGroup>();
                decalSortingGroup.sortingOrder = DecalSortingOrder;
                var decalMeshRenderer = decalGo.GetComponent<MeshRenderer>();
                if (decalMeshRenderer != null)
                {
                    decalMeshRenderer.sortingLayerName = "Default";
                    decalMeshRenderer.sortingOrder     = DecalSortingOrder;
                }

                // 颜色（BloodModifier 会创建材质实例再修改）
                var decalModifier = decalGo.GetComponent<BloodEffectsPack.BloodModifier_URP>()
                                 ?? decalGo.GetComponentInChildren<BloodEffectsPack.BloodModifier_URP>(true);
                if (decalModifier != null)
                {
                    var s = decalModifier.settings;
                    s.color                 = _harmonyMode ? HarmonyColor : NormalDecalColor;
                    s.colorIntensity        = _harmonyMode ? 1f : 0.55f;
                    s.smoothness            = 0f;
                    s.albedoPower           = 1.8f;
                    s.ambientColorIntensity = 0.3f;
                    decalModifier.ApplyToMaterialInstancesInRuntime();
                }
                // renderingLayerMask 保持预制体默认值 1u，不要改（URP Decal 渲染层≠物理层）

                // 持久列表
                if (_persistentDecals.Count >= MaxDecals)
                {
                    var oldest = _persistentDecals[0];
                    _persistentDecals.RemoveAt(0);
                    if (oldest != null) Destroy(oldest);
                }
                _persistentDecals.Add(decalGo);
            }
        }
    }

    private static int GetUnitSortingOrder(GameObject unit)
    {
        if (unit == null) return 100;
        // 与 UnitSpriteSortingController 算法一致：Z * 100
        return Mathf.RoundToInt(unit.transform.position.z * 100f);
    }

    private static Vector3 ResolveHitPosition(GameObject unit)
    {
        if (unit == null) return Vector3.zero;

        Bounds bounds = default;
        bool hasBounds = false;
        foreach (var r in unit.GetComponentsInChildren<Renderer>(true))
        {
            // 跳过地面阴影（通常名字含 shadow）
            if (r.gameObject.name.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (hasBounds)
        {
            // 取包围盒垂直 60% 处（偏上腰部位置）
            float y = bounds.min.y + bounds.size.y * 0.6f;
            return new Vector3(bounds.center.x, y, bounds.center.z);
        }

        return unit.transform.position + Vector3.up * 1.0f;
    }

private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static void ApplyColor(GameObject go, Color color)
    {
        // BloodEffectsPack 用 BloodModifier 或直接改 ParticleSystem.startColor
        var modifiers = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in modifiers)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }
    }
}
