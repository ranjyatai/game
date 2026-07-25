using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class UnitSpawner : MonoBehaviour
{
    public enum SpawnAreaType
    {
        Point,
        BoxArea,
        SphereArea
    }

    public enum SpawnClockMode
    {
        Manual,
        OnStart,
        Interval
    }

    public enum SpawnIntervalUnit
    {
        Seconds,
        Minutes,
        Hours
    }

    [System.Serializable]
    public class UnitSpawnCandidate
    {
        [Header("启用")]
        public bool enabled = true;

        [Header("候选单位")]
        public UnitDefinition definition;

        [Min(0f)]
        public float weight = 1f;

        [TextArea(1, 3)]
        public string note;

        [Header("基础过滤")]
        public bool useFilter = false;
        public UnitDefineType requiredDefineType = UnitDefineType.Character;
        public CharacterIdentity requiredCharacterIdentity = CharacterIdentity.Enemy;

        [Header("条件脚本接口")]
        public UnitSpawnCondition condition;

        public bool Pass(UnitSpawner spawner, SpawnGroup group)
        {
            if (!enabled || definition == null)
                return false;

            if (useFilter)
            {
                if (definition.defineType != requiredDefineType)
                    return false;

                if (requiredDefineType == UnitDefineType.Character &&
                    definition.characterIdentity != requiredCharacterIdentity)
                    return false;
            }

            if (condition != null && !condition.CanSpawn(spawner, definition))
                return false;

            return true;
        }
    }

    [System.Serializable]
    public class SpawnGroup
    {
        [Header("基础信息")]
        public bool enabled = true;
        public string groupName = "新刷怪组";

        [TextArea(2, 5)]
        public string groupNote;

        [Header("阶段限制")]
        public bool useStageLimit = false;
        public int minStage = 0;
        public int maxStage = 9999;

        [Header("孵化时钟")]
        public SpawnClockMode spawnClockMode = SpawnClockMode.OnStart;
        public int spawnCountPerTick = 1;
        public SpawnIntervalUnit spawnIntervalUnit = SpawnIntervalUnit.Seconds;
        public float spawnIntervalValue = 5f;

        [Header("孵化检测")]
        public bool useSpawnCountLimit = false;
        public int maxAliveCountInCheckRange = 3;
        public BoxCollider aliveCheckBox;
        public SphereCollider aliveCheckSphere;
        public bool countOnlyBoundUnits = true;

        [Header("掉落配置")]
        public DropProfile dropProfile;
        public DropPoolMode overrideDropMode = DropPoolMode.IndependentRolls;

        [Header("条件脚本接口")]
        public UnitSpawnCondition groupCondition;

        [Header("候选单位池")]
        public List<UnitSpawnCandidate> candidates = new();

        [HideInInspector] public float runtimeSpawnTimer = 0f;
        [HideInInspector] public int runtimeAliveCount = 0;

        public bool IsStageValid(int stage)
        {
            if (!useStageLimit)
                return true;

            return stage >= minStage && stage <= maxStage;
        }

        public float GetIntervalSeconds()
        {
            switch (spawnIntervalUnit)
            {
                case SpawnIntervalUnit.Minutes:
                    return spawnIntervalValue * 60f;
                case SpawnIntervalUnit.Hours:
                    return spawnIntervalValue * 3600f;
                default:
                    return spawnIntervalValue;
            }
        }
    }

    [Header("基础信息")]
    [SerializeField] private string spawnerNote;

    [Header("生成区域")]
    [SerializeField] private SpawnAreaType spawnAreaType = SpawnAreaType.Point;
    [SerializeField] private BoxCollider boxArea;
    [SerializeField] private SphereCollider sphereArea;

    [Header("生成父级")]
    [SerializeField] private Transform spawnParent;
    [SerializeField] private bool useSpawnerTransform = true;
    [SerializeField] private Vector3 spawnPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 spawnEulerOffset = Vector3.zero;

    [Header("阶段控制")]
    [SerializeField] private int currentStage = 0;

    [Header("刷怪组")]
    [SerializeField] private List<SpawnGroup> spawnGroups = new();

    [Header("通用设置")]
    [SerializeField] private bool autoFindRangeOnAwake = true;
    [SerializeField] private bool applyDefinitionAfterSpawn = true;
    [SerializeField] private bool useGroupDropProfileOnSpawnedUnit = false;

    [Header("Gizmo 显示")]
    [SerializeField] private bool drawSpawnAreaGizmo = true;
    [SerializeField] private bool drawPointGizmo = true;
    [SerializeField] private Color spawnAreaColor = Color.green;
    [SerializeField] private Color pointColor = Color.cyan;
    [SerializeField] private Color aliveCheckColor = Color.yellow;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private List<GameObject> spawnedInstances = new();

    public string SpawnerNote => spawnerNote;
    public int CurrentStage => currentStage;
    public SpawnAreaType AreaType => spawnAreaType;
    public List<SpawnGroup> SpawnGroups => spawnGroups;

    private void Reset()
    {
        AutoSetup();
    }

    private void OnValidate()
    {
        AutoSetup();
    }

    private void Awake()
    {
        if (autoFindRangeOnAwake)
            AutoSetup();
    }

    private void Start()
    {
        for (int i = 0; i < spawnGroups.Count; i++)
        {
            SpawnGroup group = spawnGroups[i];
            if (group == null || !group.enabled)
                continue;

            if (!group.IsStageValid(currentStage))
                continue;

            if (group.spawnClockMode == SpawnClockMode.OnStart)
                SpawnMultipleFromGroup(i, group.spawnCountPerTick);
        }
    }

    private void Update()
    {
        for (int i = 0; i < spawnGroups.Count; i++)
        {
            SpawnGroup group = spawnGroups[i];
            if (group == null || !group.enabled)
                continue;

            group.runtimeAliveCount = CountAliveUnitsInGroupRange(group);

            if (!group.IsStageValid(currentStage))
                continue;

            if (group.spawnClockMode != SpawnClockMode.Interval)
                continue;

            group.runtimeSpawnTimer += Time.deltaTime;
            float intervalSeconds = Mathf.Max(0.01f, group.GetIntervalSeconds());

            if (group.runtimeSpawnTimer >= intervalSeconds)
            {
                group.runtimeSpawnTimer = 0f;
                SpawnMultipleFromGroup(i, Mathf.Max(1, group.spawnCountPerTick));
            }
        }
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (spawnParent == null)
            spawnParent = transform.parent;

        if (boxArea == null)
            boxArea = FindBoxRangeCollider();

        if (sphereArea == null)
            sphereArea = FindSphereRangeCollider();

        for (int i = 0; i < spawnGroups.Count; i++)
        {
            SpawnGroup group = spawnGroups[i];
            if (group == null)
                continue;

            if (group.aliveCheckBox == null)
                group.aliveCheckBox = boxArea;

            if (group.aliveCheckSphere == null)
                group.aliveCheckSphere = sphereArea;

            group.spawnCountPerTick = Mathf.Max(1, group.spawnCountPerTick);
            group.maxAliveCountInCheckRange = Mathf.Max(1, group.maxAliveCountInCheckRange);
            group.spawnIntervalValue = Mathf.Max(0.01f, group.spawnIntervalValue);
        }
    }

    [ContextMenu("Stage +1")]
    public void IncreaseStage()
    {
        currentStage++;
    }

    [ContextMenu("Stage -1")]
    public void DecreaseStage()
    {
        currentStage = Mathf.Max(0, currentStage - 1);
    }

    public void SetStage(int stage)
    {
        currentStage = Mathf.Max(0, stage);
    }

    [ContextMenu("Spawn One From First Valid Group")]
    public GameObject SpawnOne()
    {
        for (int i = 0; i < spawnGroups.Count; i++)
        {
            if (CanGroupSpawn(i))
                return SpawnOneFromGroup(i);
        }

        return null;
    }

    public GameObject SpawnOneFromGroup(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= spawnGroups.Count)
            return null;

        SpawnGroup group = spawnGroups[groupIndex];
        if (group == null || !group.enabled)
            return null;

        if (!group.IsStageValid(currentStage))
            return null;

        if (group.groupCondition != null && !group.groupCondition.CanSpawn(this, null))
            return null;

        if (!CanSpawnMoreByCountLimit(group))
            return null;

        UnitDefinition selected = SelectRandomDefinitionFromGroup(group);
        if (selected == null)
            return null;

        if (selected.prefab == null)
            return null;

        Vector3 spawnPosition = GetSpawnPosition();
        Quaternion spawnRotation = useSpawnerTransform
            ? transform.rotation * Quaternion.Euler(spawnEulerOffset)
            : Quaternion.Euler(spawnEulerOffset);

        Transform parent = spawnParent != null ? spawnParent : null;

        GameObject instance = Instantiate(selected.prefab, spawnPosition, spawnRotation, parent);
        instance.name = selected.prefab.name + "(Clone)";
        spawnedInstances.Add(instance);

        BindDefinition(instance, selected);

        if (useGroupDropProfileOnSpawnedUnit)
        {
            UnitDeathDropController dropController = instance.GetComponent<UnitDeathDropController>();
            if (dropController != null)
            {
                if (group.dropProfile != null)
                    dropController.SetDropProfile(group.dropProfile, group.overrideDropMode);
                else
                    dropController.ClearDropProfile();
            }
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[UnitSpawner] {name}: group='{group.groupName}' spawned '{selected.displayName}' at {spawnPosition}",
                this
            );
        }

        return instance;
    }

    public void SpawnMultipleFromGroup(int groupIndex, int count)
    {
        if (count <= 0)
            return;

        for (int i = 0; i < count; i++)
        {
            if (!CanGroupSpawn(groupIndex))
                break;

            SpawnOneFromGroup(groupIndex);
        }
    }

    [ContextMenu("Clear Spawned")]
    public void ClearSpawned()
    {
        for (int i = spawnedInstances.Count - 1; i >= 0; i--)
        {
            GameObject go = spawnedInstances[i];
            if (go == null)
                continue;

            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        spawnedInstances.Clear();
    }

    public void AddGroup()
    {
        spawnGroups.Add(new SpawnGroup());
    }

    public bool CanGroupSpawn(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= spawnGroups.Count)
            return false;

        SpawnGroup group = spawnGroups[groupIndex];
        if (group == null || !group.enabled)
            return false;

        if (!group.IsStageValid(currentStage))
            return false;

        if (group.groupCondition != null && !group.groupCondition.CanSpawn(this, null))
            return false;

        if (!CanSpawnMoreByCountLimit(group))
            return false;

        return SelectRandomDefinitionFromGroup(group) != null;
    }

    public bool CanSpawnMoreByCountLimit(SpawnGroup group)
    {
        if (group == null)
            return false;

        if (!group.useSpawnCountLimit)
            return true;

        int alive = CountAliveUnitsInGroupRange(group);
        group.runtimeAliveCount = alive;
        return alive < group.maxAliveCountInCheckRange;
    }

    public int CountAliveUnitsInGroupRange(SpawnGroup group)
    {
        if (group == null)
            return 0;

        HashSet<GameObject> units = new();

        if (group.aliveCheckBox != null)
        {
            Bounds b = group.aliveCheckBox.bounds;
            Collider[] hits = Physics.OverlapBox(
                b.center,
                b.extents,
                group.aliveCheckBox.transform.rotation
            );

            CollectUnitsFromHits(hits, units, group.countOnlyBoundUnits);
        }
        else if (group.aliveCheckSphere != null)
        {
            Vector3 center = group.aliveCheckSphere.transform.TransformPoint(group.aliveCheckSphere.center);
            float radius = group.aliveCheckSphere.radius * GetMaxAbsAxis(group.aliveCheckSphere.transform.lossyScale);

            Collider[] hits = Physics.OverlapSphere(center, radius);
            CollectUnitsFromHits(hits, units, group.countOnlyBoundUnits);
        }

        return units.Count;
    }

    private void CollectUnitsFromHits(Collider[] hits, HashSet<GameObject> units, bool onlyBoundUnits)
    {
        if (hits == null)
            return;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            UnitDefinitionRuntimeBinder binder = hit.GetComponentInParent<UnitDefinitionRuntimeBinder>();
            if (binder == null)
                continue;

            if (onlyBoundUnits && binder.UnitDefinitionAsset == null)
                continue;

            units.Add(binder.gameObject);
        }
    }

    public List<UnitDefinition> GetValidDefinitionsFromGroup(SpawnGroup group)
    {
        List<UnitDefinition> result = new();

        if (group == null)
            return result;

        for (int i = 0; i < group.candidates.Count; i++)
        {
            UnitSpawnCandidate c = group.candidates[i];
            if (c == null || !c.Pass(this, group) || c.definition == null)
                continue;

            result.Add(c.definition);
        }

        return result;
    }

    private UnitDefinition SelectRandomDefinitionFromGroup(SpawnGroup group)
    {
        if (group == null)
            return null;

        List<UnitSpawnCandidate> valid = new();
        float totalWeight = 0f;

        for (int i = 0; i < group.candidates.Count; i++)
        {
            UnitSpawnCandidate c = group.candidates[i];
            if (c == null)
                continue;

            if (!c.Pass(this, group))
                continue;

            if (c.weight <= 0f)
                continue;

            valid.Add(c);
            totalWeight += c.weight;
        }

        if (valid.Count == 0 || totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        float accum = 0f;

        for (int i = 0; i < valid.Count; i++)
        {
            accum += valid[i].weight;
            if (roll <= accum)
                return valid[i].definition;
        }

        return valid[valid.Count - 1].definition;
    }

    private Vector3 GetSpawnPosition()
    {
        switch (spawnAreaType)
        {
            case SpawnAreaType.Point:
                return GetPointSpawnPosition();
            case SpawnAreaType.BoxArea:
                return GetRandomPointInBox();
            case SpawnAreaType.SphereArea:
                return GetRandomPointInSphere();
            default:
                return GetPointSpawnPosition();
        }
    }

    private Vector3 GetPointSpawnPosition()
    {
        if (useSpawnerTransform)
            return transform.position + transform.TransformVector(spawnPositionOffset);

        return spawnPositionOffset;
    }

    private Vector3 GetRandomPointInBox()
    {
        if (boxArea == null)
            boxArea = FindBoxRangeCollider();

        if (boxArea == null)
            return GetPointSpawnPosition();

        Bounds b = boxArea.bounds;

        float x = Random.Range(b.min.x, b.max.x);
        float y = Random.Range(b.min.y, b.max.y);
        float z = Random.Range(b.min.z, b.max.z);

        return new Vector3(x, y, z) + spawnPositionOffset;
    }

    private Vector3 GetRandomPointInSphere()
    {
        if (sphereArea == null)
            sphereArea = FindSphereRangeCollider();

        if (sphereArea == null)
            return GetPointSpawnPosition();

        Vector3 center = sphereArea.transform.TransformPoint(sphereArea.center);
        float radius = sphereArea.radius * GetMaxAbsAxis(sphereArea.transform.lossyScale);

        Vector3 random = Random.insideUnitSphere * radius;
        return center + random + spawnPositionOffset;
    }

    private float GetMaxAbsAxis(Vector3 v)
    {
        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);
        float az = Mathf.Abs(v.z);
        return Mathf.Max(ax, Mathf.Max(ay, az));
    }

    private void BindDefinition(GameObject instance, UnitDefinition definition)
    {
        if (instance == null || definition == null)
            return;

        UnitDefinitionRuntimeBinder binder = instance.GetComponent<UnitDefinitionRuntimeBinder>();
        if (binder == null)
            binder = instance.GetComponentInChildren<UnitDefinitionRuntimeBinder>(true);

        if (binder != null)
        {
            binder.SetUnitDefinitionAsset(definition, applyDefinitionAfterSpawn);

            if (applyDefinitionAfterSpawn)
            {
                UnitDefinitionRuntimeApplier applier = binder.GetComponent<UnitDefinitionRuntimeApplier>();
                if (applier == null)
                    applier = binder.GetComponentInChildren<UnitDefinitionRuntimeApplier>(true);
                if (applier != null)
                    applier.ApplyDefinition(true);

                UnitOverheadHealthBridge bridge = binder.GetComponent<UnitOverheadHealthBridge>();
                if (bridge == null)
                    bridge = binder.GetComponentInChildren<UnitOverheadHealthBridge>(true);
                if (bridge != null)
                    bridge.RefreshNow();
            }
            return;
        }

        if (debugLogs)
        {
            Debug.LogWarning(
                $"[UnitSpawner] {name}: Spawned instance '{instance.name}' has no UnitDefinitionRuntimeBinder on root or children.",
                instance
            );
        }
    }

    private BoxCollider FindBoxRangeCollider()
    {
        Transform boxNode = transform.Find("BoxRange");
        if (boxNode != null)
        {
            BoxCollider col = boxNode.GetComponent<BoxCollider>();
            if (col != null)
                return col;
        }

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "BoxRange")
            {
                BoxCollider col = t.GetComponent<BoxCollider>();
                if (col != null)
                    return col;
            }
        }

        return null;
    }

    private SphereCollider FindSphereRangeCollider()
    {
        Transform sphereNode = transform.Find("SphereRange");
        if (sphereNode != null)
        {
            SphereCollider col = sphereNode.GetComponent<SphereCollider>();
            if (col != null)
                return col;
        }

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "SphereRange")
            {
                SphereCollider col = t.GetComponent<SphereCollider>();
                if (col != null)
                    return col;
            }
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (drawPointGizmo)
        {
            Gizmos.color = pointColor;
            Vector3 p = useSpawnerTransform
                ? transform.position + transform.TransformVector(spawnPositionOffset)
                : spawnPositionOffset;
            Gizmos.DrawSphere(p, 0.15f);
        }

        if (drawSpawnAreaGizmo)
        {
            Gizmos.color = spawnAreaColor;

            switch (spawnAreaType)
            {
                case SpawnAreaType.BoxArea:
                    if (boxArea == null) boxArea = FindBoxRangeCollider();
                    if (boxArea != null)
                    {
                        Bounds b = boxArea.bounds;
                        Gizmos.DrawWireCube(b.center + spawnPositionOffset, b.size);
                    }
                    break;

                case SpawnAreaType.SphereArea:
                    if (sphereArea == null) sphereArea = FindSphereRangeCollider();
                    if (sphereArea != null)
                    {
                        Vector3 center = sphereArea.transform.TransformPoint(sphereArea.center) + spawnPositionOffset;
                        float radius = sphereArea.radius * GetMaxAbsAxis(sphereArea.transform.lossyScale);
                        Gizmos.DrawWireSphere(center, radius);
                    }
                    break;
            }
        }

        Gizmos.color = aliveCheckColor;
        for (int i = 0; i < spawnGroups.Count; i++)
        {
            SpawnGroup group = spawnGroups[i];
            if (group == null || !group.useSpawnCountLimit)
                continue;

            if (group.aliveCheckBox != null)
            {
                Bounds b = group.aliveCheckBox.bounds;
                Gizmos.DrawWireCube(b.center, b.size);
            }
            else if (group.aliveCheckSphere != null)
            {
                Vector3 center = group.aliveCheckSphere.transform.TransformPoint(group.aliveCheckSphere.center);
                float radius = group.aliveCheckSphere.radius * GetMaxAbsAxis(group.aliveCheckSphere.transform.lossyScale);
                Gizmos.DrawWireSphere(center, radius);
            }
        }
    }
}
