using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SimpleDirectionalOccluder : MonoBehaviour
{
    [Header("Candidate Trigger")]
    [SerializeField] private Collider candidateTrigger;

    [Header("Sort Anchor")]
    [SerializeField] private Transform sortAnchor;
    [SerializeField] private string sortAnchorName = "SortAnchor";

    [Header("Occlusion Volumes")]
    [SerializeField] private BoxCollider[] occlusionVolumes;

    [Header("Unit-Level Authorization")]
    [Tooltip("V2: This exact FrontOccluderRoot is registered to each UnitOcclusionMaterialReceiver. This prevents other objects in the same channel from leaking into the receiver mask.")]
    [SerializeField] private GameObject frontOccluderRoot;
    [SerializeField] private string frontOccluderRootName = "FrontOccluderRoot";
    [SerializeField] private bool autoResolveFrontOccluderRoot = true;
    [SerializeField] private string lastAuthorizedFrontOccluderRootPath = "";

    [Header("Candidate Filter")]
    [SerializeField] private string candidateLayerName = "Character2D";
    [SerializeField] private bool requireCharacterLayer = true;

    [Header("Unit Shape Detection")]
    [SerializeField] private bool useUnitColliders = true;
    [SerializeField] private bool includeInactiveColliders = false;

    [Header("Anchor Fallback")]
    [SerializeField] private bool useAnchorFallback = true;
    [SerializeField] private bool useFootAnchor = true;
    [SerializeField] private bool useBodyAnchor = true;
    [SerializeField] private bool useWeaponTipAnchor = true;

    [Header("Front / Back Judgment")]
    [SerializeField] private float frontExemptOffset = 0.02f;
    [SerializeField] private bool useBodyAnchorForFrontCheck = true;
    [SerializeField] private bool useWeaponTipAnchorForFrontCheck = false;

    [Header("Overlap Tuning")]
    [SerializeField] private float overlapPadding = 0.01f;

    [Header("Y Occlusion Check")]
    [SerializeField] private bool useYCheck = true;
    [SerializeField] private float overlapPaddingY = 0.01f;

    [Header("Occluder Fade")]
    [SerializeField] private OccluderVisualFader visualFader;
    [SerializeField] private bool fadeForPlayer = true;
    [SerializeField] private bool fadeForEnemy = false;
    [SerializeField] private bool fadeForAlly = false;
    [SerializeField] private bool fadeForItem = false;

    [Header("Debug")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugDraw = true;

    [Header("Runtime Debug")]
    [SerializeField] private int candidateCount = 0;
    [SerializeField] private int occludedCount = 0;
    [SerializeField] private int fadeOccludedCount = 0;

    [Header("Prototype Proxy Switch")]
    [SerializeField] private bool switchAutoFrontOccluderShapeClone = true;
    [SerializeField] private string autoFrontOccluderCloneName = "__AutoFrontOccluderShapeClone";
    [SerializeField] private bool autoFrontCloneActive = false;
    [SerializeField] private int autoFrontSwitchCandidateCount = 0;

    private int candidateLayer = -1;

    [NonSerialized] private readonly HashSet<CharacterOcclusionPoints> candidates = new HashSet<CharacterOcclusionPoints>();
    [NonSerialized] private readonly Dictionary<CharacterOcclusionPoints, bool> currentStates = new Dictionary<CharacterOcclusionPoints, bool>();

    private void Awake()
    {
        if (autoFindOnAwake)
            AutoResolveReferences();

        if (candidateTrigger == null)
            candidateTrigger = GetComponent<Collider>();

        if (candidateTrigger != null)
            candidateTrigger.isTrigger = true;

        ResolveFrontOccluderRootIfNeeded();
        candidateLayer = LayerMask.NameToLayer(candidateLayerName);
        SetAutoFrontOccluderCloneActive(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 不在 OnValidate 里自动扫描整棵层级。
        // 旧版会在编辑器序列化/反序列化期间反复改写组件引用，
        // 容易触发 Unity 的 excessive object composition 警告。
        if (candidateTrigger == null)
            candidateTrigger = GetComponent<Collider>();

        if (candidateTrigger != null)
            candidateTrigger.isTrigger = true;

        ResolveFrontOccluderRootIfNeeded();
        candidateLayer = LayerMask.NameToLayer(candidateLayerName);
    }
#endif

    private void Update()
    {
        List<CharacterOcclusionPoints> invalid = null;
        int runtimeOccluded = 0;
        int runtimeFadeOccluded = 0;

        foreach (var unit in candidates)
        {
            if (unit == null)
            {
                invalid ??= new List<CharacterOcclusionPoints>();
                invalid.Add(unit);
                continue;
            }

            bool isOccluded = EvaluateUnit(unit);

            if (currentStates.TryGetValue(unit, out bool oldState))
            {
                if (oldState != isOccluded)
                {
                    currentStates[unit] = isOccluded;
                    NotifyUnit(unit, isOccluded);

                    if (debugLogs)
                        Debug.Log($"[SimpleDirectionalOccluder] {unit.name} state changed -> {isOccluded}", this);
                }
            }
            else
            {
                currentStates[unit] = isOccluded;
                NotifyUnit(unit, isOccluded);

                if (debugLogs)
                    Debug.Log($"[SimpleDirectionalOccluder] {unit.name} init state -> {isOccluded}", this);
            }

            if (isOccluded)
            {
                runtimeOccluded++;

                if (visualFader != null && ShouldAffectOccluderFade(unit))
                    runtimeFadeOccluded++;
            }
        }

        if (invalid != null)
        {
            foreach (var dead in invalid)
            {
                candidates.Remove(dead);
                currentStates.Remove(dead);
            }
        }

        candidateCount = candidates.Count;
        occludedCount = runtimeOccluded;
        fadeOccludedCount = runtimeFadeOccluded;

        if (visualFader != null)
            visualFader.SetOccluded(runtimeFadeOccluded > 0);

        // 原型开关：只跟随当前 BackTrigger 的进入/离开。
        // 注意：这里不能绑定 EvaluateUnit / occlusionVolumes / SortAnchor / FrontExempt / YCheck 的遮挡结果。
        // 你的目标是“进入 BackTrigger 就打开 __AutoFrontOccluderShapeClone”，
        // 所以只看当前 trigger 内有没有候选单位。
        autoFrontSwitchCandidateCount = candidates.Count;
        SetAutoFrontOccluderCloneActive(candidates.Count > 0);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        CharacterOcclusionPoints unit = FindCharacterPointsFromCollider(other);
        if (unit == null)
            return;

        if (!PassLayerFilter(unit.gameObject))
            return;

        if (candidates.Add(unit) && debugLogs)
            Debug.Log($"[SimpleDirectionalOccluder] Candidate Enter -> {unit.name}", this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null)
            return;

        CharacterOcclusionPoints unit = FindCharacterPointsFromCollider(other);
        if (unit == null)
            return;

        if (candidates.Remove(unit))
        {
            currentStates.Remove(unit);
            NotifyUnit(unit, false);

            autoFrontSwitchCandidateCount = candidates.Count;

            if (candidates.Count == 0)
                SetAutoFrontOccluderCloneActive(false);

            if (debugLogs)
                Debug.Log($"[SimpleDirectionalOccluder] Candidate Exit -> {unit.name}", this);
        }
    }

    private void OnDisable()
    {
        foreach (var pair in currentStates)
        {
            if (pair.Key != null)
                NotifyUnit(pair.Key, false);
        }

        if (visualFader != null)
            visualFader.SetOccluded(false);

        SetAutoFrontOccluderCloneActive(false);

        candidates.Clear();
        currentStates.Clear();
        candidateCount = 0;
        occludedCount = 0;
        fadeOccludedCount = 0;
        autoFrontSwitchCandidateCount = 0;
    }

    private bool EvaluateUnit(CharacterOcclusionPoints unit)
    {
        if (unit == null || sortAnchor == null)
            return false;

        // 1) 前方豁免：只要关键点明显在柱子前方，就绝不遮挡
        if (IsClearlyInFront(unit))
        {
            if (debugLogs)
                Debug.Log($"[SimpleDirectionalOccluder] {unit.name} FRONT EXEMPT", this);

            return false;
        }

        if (occlusionVolumes == null || occlusionVolumes.Length == 0)
            return false;

        // 2) 先做 Collider 检测
        bool colliderHit = false;

        if (useUnitColliders)
        {
            Collider[] unitCols = unit.GetComponentsInChildren<Collider>(includeInactiveColliders);

            for (int i = 0; i < unitCols.Length; i++)
            {
                Collider col = unitCols[i];
                if (col == null || !col.enabled || col.isTrigger)
                    continue;

                for (int j = 0; j < occlusionVolumes.Length; j++)
                {
                    BoxCollider box = occlusionVolumes[j];
                    if (box == null || !box.enabled)
                        continue;

                    if (UseVolumeOverlap(col.bounds, box.bounds))
                    {
                        colliderHit = true;

                        if (debugLogs)
                            Debug.Log($"[SimpleDirectionalOccluder] {unit.name} OCCLUDED by collider overlap -> {box.name}", this);

                        return true;
                    }
                }
            }
        }

        // 3) Collider 没命中时，继续做 Anchor 补判
        if (useAnchorFallback && !colliderHit)
        {
            if (CheckAnchorsAgainstVolumes(unit))
                return true;
        }

        return false;
    }

    private bool CheckAnchorsAgainstVolumes(CharacterOcclusionPoints unit)
    {
        Vector3? footPos = (useFootAnchor && unit.footAnchor != null) ? unit.footAnchor.position : (Vector3?)null;
        Vector3? bodyPos = (useBodyAnchor && unit.bodyAnchor != null) ? unit.bodyAnchor.position : (Vector3?)null;
        Vector3? weaponPos = (useWeaponTipAnchor && unit.weaponTipAnchor != null) ? unit.weaponTipAnchor.position : (Vector3?)null;

        for (int j = 0; j < occlusionVolumes.Length; j++)
        {
            BoxCollider box = occlusionVolumes[j];
            if (box == null || !box.enabled)
                continue;

            bool footHit = footPos.HasValue && IsPointInsideOcclusionBox(box, footPos.Value);
            bool bodyHit = bodyPos.HasValue && IsPointInsideOcclusionBox(box, bodyPos.Value);
            bool weaponHit = weaponPos.HasValue && IsPointInsideOcclusionBox(box, weaponPos.Value);

            if (footHit || bodyHit || weaponHit)
            {
                if (debugLogs)
                {
                    string hitName = footHit ? "Foot" : bodyHit ? "Body" : "Weapon";
                    Debug.Log($"[SimpleDirectionalOccluder] {unit.name} OCCLUDED by anchor fallback ({hitName}) -> {box.name}", this);
                }

                return true;
            }
        }

        return false;
    }

    private bool IsClearlyInFront(CharacterOcclusionPoints unit)
    {
        float anchorZ = sortAnchor.position.z;

        if (unit.footAnchor != null && unit.footAnchor.position.z < anchorZ - frontExemptOffset)
            return true;

        if (useBodyAnchorForFrontCheck && unit.bodyAnchor != null && unit.bodyAnchor.position.z < anchorZ - frontExemptOffset)
            return true;

        if (useWeaponTipAnchorForFrontCheck && unit.weaponTipAnchor != null && unit.weaponTipAnchor.position.z < anchorZ - frontExemptOffset)
            return true;

        return false;
    }

    private bool ShouldAffectOccluderFade(CharacterOcclusionPoints unit)
    {
        if (unit == null)
            return false;

        UnitOutlineState outlineState = unit.GetComponentInParent<UnitOutlineState>(true);
        if (outlineState == null)
            return fadeForPlayer;

        switch (outlineState.CurrentState)
        {
            case OutlineState.Player:
                return fadeForPlayer;
            case OutlineState.Enemy:
                return fadeForEnemy;
            case OutlineState.Ally:
                return fadeForAlly;
            case OutlineState.Item:
                return fadeForItem;
            default:
                return false;
        }
    }

    private bool UseVolumeOverlap(Bounds a, Bounds b)
    {
        if (useYCheck)
            return OverlapXYZ(a, b, overlapPadding, overlapPaddingY);

        return OverlapXZ(a, b, overlapPadding);
    }

    private bool IsPointInsideOcclusionBox(BoxCollider box, Vector3 worldPoint)
    {
        if (useYCheck)
            return IsPointInsideBoxColliderXYZ(box, worldPoint, overlapPadding, overlapPaddingY);

        return IsPointInsideBoxColliderXZ(box, worldPoint, overlapPadding);
    }

    private bool OverlapXZ(Bounds a, Bounds b, float padding)
    {
        float aMinX = a.min.x - padding;
        float aMaxX = a.max.x + padding;
        float aMinZ = a.min.z - padding;
        float aMaxZ = a.max.z + padding;

        float bMinX = b.min.x - padding;
        float bMaxX = b.max.x + padding;
        float bMinZ = b.min.z - padding;
        float bMaxZ = b.max.z + padding;

        bool overlapX = aMaxX >= bMinX && bMaxX >= aMinX;
        bool overlapZ = aMaxZ >= bMinZ && bMaxZ >= aMinZ;

        return overlapX && overlapZ;
    }

    private bool OverlapXYZ(Bounds a, Bounds b, float paddingXZ, float paddingY)
    {
        float aMinX = a.min.x - paddingXZ;
        float aMaxX = a.max.x + paddingXZ;
        float aMinY = a.min.y - paddingY;
        float aMaxY = a.max.y + paddingY;
        float aMinZ = a.min.z - paddingXZ;
        float aMaxZ = a.max.z + paddingXZ;

        float bMinX = b.min.x - paddingXZ;
        float bMaxX = b.max.x + paddingXZ;
        float bMinY = b.min.y - paddingY;
        float bMaxY = b.max.y + paddingY;
        float bMinZ = b.min.z - paddingXZ;
        float bMaxZ = b.max.z + paddingXZ;

        bool overlapX = aMaxX >= bMinX && bMaxX >= aMinX;
        bool overlapY = aMaxY >= bMinY && bMaxY >= aMinY;
        bool overlapZ = aMaxZ >= bMinZ && bMaxZ >= aMinZ;

        return overlapX && overlapY && overlapZ;
    }

    private bool IsPointInsideBoxColliderXZ(BoxCollider box, Vector3 worldPoint, float epsilon)
    {
        Vector3 localPoint = box.transform.InverseTransformPoint(worldPoint) - box.center;
        Vector3 halfSize = box.size * 0.5f;

        return
            Mathf.Abs(localPoint.x) <= halfSize.x + epsilon &&
            Mathf.Abs(localPoint.z) <= halfSize.z + epsilon;
    }

    private bool IsPointInsideBoxColliderXYZ(BoxCollider box, Vector3 worldPoint, float epsilonXZ, float epsilonY)
    {
        Vector3 localPoint = box.transform.InverseTransformPoint(worldPoint) - box.center;
        Vector3 halfSize = box.size * 0.5f;

        return
            Mathf.Abs(localPoint.x) <= halfSize.x + epsilonXZ &&
            Mathf.Abs(localPoint.y) <= halfSize.y + epsilonY &&
            Mathf.Abs(localPoint.z) <= halfSize.z + epsilonXZ;
    }

    private void SetAutoFrontOccluderCloneActive(bool active)
    {
        if (!switchAutoFrontOccluderShapeClone)
            return;

        Transform clone = FindAutoFrontOccluderClone();
        if (clone == null)
        {
            autoFrontCloneActive = false;

            if (debugLogs)
                Debug.LogWarning($"[SimpleDirectionalOccluder] Auto front clone not found: {autoFrontOccluderCloneName}", this);

            return;
        }

        autoFrontCloneActive = active;

        if (clone.gameObject.activeSelf != active)
        {
            clone.gameObject.SetActive(active);

            if (debugLogs)
                Debug.Log($"[SimpleDirectionalOccluder] Set {clone.name}.activeSelf = {active}", this);
        }
    }

    private Transform FindAutoFrontOccluderClone()
    {
        // 不再依赖 FrontOccluderRoot 字段、Winner 字段、Raw Contact 字段。
        // 只在“本 SimpleDirectionalOccluder 所属的地形装饰物实例”里找这个节点。
        Transform searchRoot = ResolveOccluderSearchRoot();
        if (searchRoot == null)
            return null;

        Transform exact = FindInSelfOrChildren(searchRoot, autoFrontOccluderCloneName);
        if (exact != null)
            return exact;

        return null;
    }

    // 玩家跑进一片新区域时，附近好几个装饰物会在同一帧里第一次触发这个通知，之前每次都
    // 新分配一个数组，是"跑图瞬间GC猛增"的一个来源。复用这个缓冲区，改用 List 版本的
    // NonAlloc 重载。
    private readonly List<MonoBehaviour> scratchUnitBehaviours = new List<MonoBehaviour>(32);

    private void NotifyUnit(CharacterOcclusionPoints unit, bool isOccluded)
    {
        if (unit == null)
            return;

        GameObject authorizedRoot = ResolveFrontOccluderRootIfNeeded();
        lastAuthorizedFrontOccluderRootPath = authorizedRoot != null ? GetTransformPath(authorizedRoot.transform) : "NULL";

        unit.GetComponentsInChildren(true, scratchUnitBehaviours);
        for (int i = 0; i < scratchUnitBehaviours.Count; i++)
        {
            if (scratchUnitBehaviours[i] is UnitOcclusionMaterialReceiver unitReceiver)
            {
                unitReceiver.SetOccludedByRoot(this, authorizedRoot, isOccluded);
                continue;
            }

            if (scratchUnitBehaviours[i] is IOcclusionStateReceiver receiver)
                receiver.SetOccludedBy(this, isOccluded);
        }
    }

    private GameObject ResolveFrontOccluderRootIfNeeded()
    {
        if (frontOccluderRoot != null && frontOccluderRoot.activeInHierarchy)
            return frontOccluderRoot;

        if (!autoResolveFrontOccluderRoot)
            return frontOccluderRoot;

        Transform searchRoot = ResolveOccluderSearchRoot();
        Transform root = FindInSelfOrChildren(searchRoot, frontOccluderRootName);
        if (root == null)
        {
            // Some legacy prefabs place the visible proxy directly under StencilWriterRoot.
            // Keep this only as a fallback; the standard authorized root is FrontOccluderRoot.
            root = FindInSelfOrChildren(searchRoot, "StencilWriterRoot");
        }

        frontOccluderRoot = root != null ? root.gameObject : null;
        return frontOccluderRoot;
    }

    private static string GetTransformPath(Transform tr)
    {
        if (tr == null)
            return "NULL";

        string path = tr.name;
        Transform cur = tr.parent;
        int guard = 0;
        while (cur != null && guard++ < 64)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }

        return path;
    }

    private CharacterOcclusionPoints FindCharacterPointsFromCollider(Collider col)
    {
        if (col == null)
            return null;

        CharacterOcclusionPoints points = col.GetComponent<CharacterOcclusionPoints>();
        if (points != null)
            return points;

        points = col.GetComponentInParent<CharacterOcclusionPoints>(true);
        if (points != null)
            return points;

        return col.GetComponentInChildren<CharacterOcclusionPoints>(true);
    }

    private bool PassLayerFilter(GameObject go)
    {
        if (!requireCharacterLayer)
            return true;

        if (go == null)
            return false;

        if (candidateLayer < 0)
            return true;

        return go.layer == candidateLayer;
    }


    public void ConfigureForTerrainDecoration(Collider trigger, Transform anchor, BoxCollider[] volumes, OccluderVisualFader fader)
    {
        if (trigger != null)
        {
            candidateTrigger = trigger;
            candidateTrigger.isTrigger = true;
        }

        if (anchor != null)
            sortAnchor = anchor;

        occlusionVolumes = volumes ?? Array.Empty<BoxCollider>();
        visualFader = fader;

        ResolveFrontOccluderRootIfNeeded();
        candidateLayer = LayerMask.NameToLayer(candidateLayerName);
    }

    [ContextMenu("Auto Resolve References")]
    public void AutoResolveReferences()
    {
        if (candidateTrigger == null)
            candidateTrigger = GetComponent<Collider>();

        if (candidateTrigger != null)
            candidateTrigger.isTrigger = true;

        Transform searchRoot = ResolveOccluderSearchRoot();

        if (sortAnchor == null)
            sortAnchor = FindInSelfOrChildren(searchRoot, sortAnchorName);

        if (sortAnchor == null)
            sortAnchor = FindInSelfOrChildren(searchRoot, "PlaneOrigin");

        if (visualFader == null)
            visualFader = GetComponentInParent<OccluderVisualFader>(true);

        if (occlusionVolumes == null || occlusionVolumes.Length == 0)
        {
            List<BoxCollider> found = new List<BoxCollider>();

            CollectBoxesFromNamedRoot(searchRoot, "FrontOccluderRoot", found);
            CollectBoxesFromNamedRoot(searchRoot, "OutlineMaskProxyRoot", found);
            CollectBoxesFromNamedRoot(searchRoot, "StencilWriterRoot", found);

            // 兼容旧样板：旧版本把 FrontOccluderProxy_Box 直接放在 StencilWriterRoot 或根下。
            AddBoxIfExists(searchRoot, "FrontOccluderProxy_Box", found);
            AddBoxIfExists(searchRoot, "OutlineMaskProxy_T_Box", found);
            AddBoxIfExists(searchRoot, "SubOutlineMaskProxy_01", found);

            BoxCollider triggerBox = candidateTrigger as BoxCollider;
            found.RemoveAll(x => x == null || x == triggerBox);
            occlusionVolumes = found.ToArray();
        }

        ResolveFrontOccluderRootIfNeeded();
        candidateLayer = LayerMask.NameToLayer(candidateLayerName);
    }

    private Transform ResolveOccluderSearchRoot()
    {
        TerrainDecorationRuntimeBinder decorationBinder = GetComponentInParent<TerrainDecorationRuntimeBinder>(true);
        if (decorationBinder != null)
            return decorationBinder.transform;

        return transform.parent != null ? transform.parent : transform;
    }

    private void CollectBoxesFromNamedRoot(Transform searchRoot, string rootName, List<BoxCollider> result)
    {
        Transform root = FindInSelfOrChildren(searchRoot, rootName);
        if (root == null)
            return;

        BoxCollider[] boxes = root.GetComponentsInChildren<BoxCollider>(true);
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i] != null && !result.Contains(boxes[i]))
                result.Add(boxes[i]);
        }
    }

    private void AddBoxIfExists(Transform searchRoot, string objectName, List<BoxCollider> result)
    {
        Transform tr = FindInSelfOrChildren(searchRoot, objectName);
        if (tr == null)
            return;

        BoxCollider box = tr.GetComponent<BoxCollider>();
        if (box != null && !result.Contains(box))
            result.Add(box);
    }

    private Transform FindInSelfOrChildren(Transform start, string targetName)
    {
        if (start == null)
            return null;

        foreach (Transform t in start.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == targetName)
                return t;
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugDraw)
            return;

        if (sortAnchor != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(sortAnchor.position, 0.06f);
        }

        if (occlusionVolumes != null)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < occlusionVolumes.Length; i++)
            {
                BoxCollider box = occlusionVolumes[i];
                if (box == null)
                    continue;

                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = box.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = old;
            }
        }

        if (candidates != null)
        {
            foreach (var unit in candidates)
            {
                if (unit == null)
                    continue;

                Vector3 markerPos = unit.bodyAnchor != null
                    ? unit.bodyAnchor.position
                    : (unit.footAnchor != null ? unit.footAnchor.position : unit.transform.position);

                bool isOccluded = currentStates.TryGetValue(unit, out bool state) && state;
                Gizmos.color = isOccluded ? Color.red : Color.green;
                Gizmos.DrawSphere(markerPos, 0.04f);
            }
        }
    }
}
