using UnityEngine;

/// <summary>
/// 统一地面查询入口。
/// 移动、跳跃、摆放、脚步声、AI 听觉后面都应该从这里查询“脚下是什么”。
/// 优先级：模型 GroundSurfaceMarker > BaseGroundBlock Shape/Surface 数据。
/// </summary>
public class GroundQueryService : MonoBehaviour
{
    public static GroundQueryService Active { get; private set; }

    [Header("Query")]
    [SerializeField] private LayerMask groundRaycastLayers = ~0;
    [SerializeField] private float rayStartHeight = 4f;
    [SerializeField] private float rayDistance = 12f;
    [SerializeField] private bool queryModelSurfaceMarkerFirst = true;
    [SerializeField] private bool autoRefreshBlocksWhenEmpty = true;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;
    [SerializeField] private string lastQueryDebug = "-";

    private BaseGroundBlock[] cachedGroundBlocks;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    private void OnEnable()
    {
        Active = this;
        RefreshGroundBlocks();
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    public void RefreshGroundBlocks()
    {
        cachedGroundBlocks = FindObjectsOfType<BaseGroundBlock>();
    }

    public static GroundQueryService FindOrCreateInScene()
    {
        GroundQueryService existing = Active != null ? Active : FindObjectOfType<GroundQueryService>();
        if (existing != null)
            return existing;

        GameObject root = GameObject.Find("WorldRoot");
        Transform parent = root != null ? root.transform : null;
        GameObject obj = new GameObject("GroundQueryService");
        if (parent != null)
            obj.transform.SetParent(parent, false);

        return obj.AddComponent<GroundQueryService>();
    }

    public bool TryQueryGround(Vector3 worldPosition, out GroundQueryResult result)
    {
        result = GroundQueryResult.None(worldPosition);

        if (queryModelSurfaceMarkerFirst && TryQueryModelSurface(worldPosition, out result))
        {
            lastQueryDebug = result.ToDebugString();
            return true;
        }

        if (TryQueryBaseGroundBlock(worldPosition, out result))
        {
            lastQueryDebug = result.ToDebugString();
            return true;
        }

        lastQueryDebug = "No ground source";
        return false;
    }

    private bool TryQueryModelSurface(Vector3 worldPosition, out GroundQueryResult result)
    {
        result = GroundQueryResult.None(worldPosition);

        Vector3 origin = worldPosition + Vector3.up * Mathf.Max(0.01f, rayStartHeight);
        float distance = Mathf.Max(0.01f, rayStartHeight + rayDistance);
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            hitBuffer,
            distance,
            groundRaycastLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        float bestDistance = float.PositiveInfinity;
        RaycastHit bestHit = default;
        GroundSurfaceMarker bestMarker = null;
        BaseGroundBlock bestBaseBlock = null;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hitBuffer[i];
            hitBuffer[i] = default;

            Collider col = hit.collider;
            if (col == null || col.isTrigger)
                continue;

            if (Vector3.Dot(hit.normal, Vector3.up) <= 0.15f)
                continue;

            GroundSurfaceMarker marker = col.GetComponentInParent<GroundSurfaceMarker>();
            BaseGroundBlock block = col.GetComponentInParent<BaseGroundBlock>();

            // BaseGroundBlock 的真实地面形状必须走 ShapeMask 采样，不能只因为打到矩形 Collider 就认为有地面。
            if (marker == null && block == null)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestHit = hit;
            bestMarker = marker;
            bestBaseBlock = block;
        }

        if (bestMarker == null && bestBaseBlock == null)
            return false;

        if (bestBaseBlock != null)
        {
            // 命中 BaseGroundBlock 矩形碰撞后，仍以 ShapeMask / SurfaceMaterialMap 为准。
            return BuildResultFromBaseGroundBlock(bestBaseBlock, worldPosition, out result, bestHit.collider);
        }

        float groundY = bestMarker.overrideGroundHeight
            ? bestMarker.transform.position.y + bestMarker.groundHeightOffset
            : bestHit.point.y + bestMarker.groundHeightOffset;

        result = new GroundQueryResult
        {
            queryPosition = worldPosition,
            hasGround = true,
            groundY = groundY,
            surfaceType = bestMarker.surfaceType,
            surfaceMaterial = null,
            isFallDeathArea = false,
            hitCollider = bestHit.collider,
            groundBlock = null,
            surfaceMarker = bestMarker,
            sourceName = bestMarker.name
        };

        if (drawDebug)
            Debug.DrawLine(worldPosition + Vector3.up * 0.2f, new Vector3(worldPosition.x, groundY, worldPosition.z), Color.cyan, 0.05f);

        return true;
    }

    private bool TryQueryBaseGroundBlock(Vector3 worldPosition, out GroundQueryResult result)
    {
        result = GroundQueryResult.None(worldPosition);

        if ((cachedGroundBlocks == null || cachedGroundBlocks.Length == 0) && autoRefreshBlocksWhenEmpty)
            RefreshGroundBlocks();

        if (cachedGroundBlocks == null || cachedGroundBlocks.Length == 0)
            return false;

        BaseGroundBlock best = null;
        float bestYDistance = float.PositiveInfinity;

        for (int i = 0; i < cachedGroundBlocks.Length; i++)
        {
            BaseGroundBlock block = cachedGroundBlocks[i];
            if (block == null || !block.isActiveAndEnabled)
                continue;

            if (!block.TryWorldToUV(worldPosition, out _))
                continue;

            float yDistance = Mathf.Abs(block.GetGroundWorldYAtWorld(worldPosition) - worldPosition.y);
            if (yDistance < bestYDistance)
            {
                bestYDistance = yDistance;
                best = block;
            }
        }

        if (best == null)
            return false;

        return BuildResultFromBaseGroundBlock(best, worldPosition, out result, null);
    }

    private bool BuildResultFromBaseGroundBlock(BaseGroundBlock block, Vector3 worldPosition, out GroundQueryResult result, Collider hitCollider)
    {
        result = GroundQueryResult.None(worldPosition);
        if (block == null)
            return false;

        bool hasGround = block.HasGroundAtWorld(worldPosition);
        GroundSurfaceMaterialDefinition material = hasGround ? block.GetSurfaceMaterialAtWorld(worldPosition) : null;

        result = new GroundQueryResult
        {
            queryPosition = worldPosition,
            hasGround = hasGround,
            groundY = block.GetGroundWorldYAtWorld(worldPosition),
            surfaceType = hasGround ? block.GetSurfaceTypeAtWorld(worldPosition) : GroundSurfaceType.Default,
            surfaceMaterial = material,
            isFallDeathArea = block.IsFallDeathAreaAtWorld(worldPosition),
            hitCollider = hitCollider,
            groundBlock = block,
            surfaceMarker = block.GetComponent<GroundSurfaceMarker>(),
            sourceName = block.name
        };

        if (drawDebug)
        {
            Color color = hasGround ? Color.green : Color.red;
            Debug.DrawLine(worldPosition + Vector3.up * 0.2f, new Vector3(worldPosition.x, result.groundY, worldPosition.z), color, 0.05f);
        }

        return true;
    }
}

[System.Serializable]
public struct GroundQueryResult
{
    public Vector3 queryPosition;
    public bool hasGround;
    public float groundY;
    public GroundSurfaceType surfaceType;
    public GroundSurfaceMaterialDefinition surfaceMaterial;
    public bool isFallDeathArea;
    public Collider hitCollider;
    public BaseGroundBlock groundBlock;
    public GroundSurfaceMarker surfaceMarker;
    public string sourceName;

    public static GroundQueryResult None(Vector3 queryPosition)
    {
        return new GroundQueryResult
        {
            queryPosition = queryPosition,
            hasGround = false,
            groundY = queryPosition.y,
            surfaceType = GroundSurfaceType.Default,
            surfaceMaterial = null,
            isFallDeathArea = true,
            hitCollider = null,
            groundBlock = null,
            surfaceMarker = null,
            sourceName = "None"
        };
    }

    public string ToDebugString()
    {
        string materialName = surfaceMaterial != null ? surfaceMaterial.displayName : "-";
        return $"hasGround={hasGround}, y={groundY:0.###}, type={surfaceType}, material={materialName}, source={sourceName}";
    }
}
