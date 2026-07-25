using UnityEngine;

public static class SkyPrisonMapPhysicalBounds
{
    public const string GeneratedRootName = "__MapPhysicalBounds";
    public const string WallPrefix = "MapBoundary_";

    private const string BoundaryLayerName = "StaticObstacle";
    private const string PushableLayerName = "PushableProp";
    private const string UnitBodyLayerName = "UnitBody";
    private const string CharacterLayerName = "Character2D";

    public static void Rebuild(GameObject root, MapDefinition map)
    {
        if (root == null || map == null)
            return;

        ClearGeneratedWalls(root);

        if (!map.enablePhysicalMapBounds)
            return;

        int boundaryLayer = ResolveLayer(BoundaryLayerName, root.layer);
        root.layer = boundaryLayer;
        root.isStatic = true;

        SkyPrisonMapPhysicalBoundsRuntimeGuard guard = root.GetComponent<SkyPrisonMapPhysicalBoundsRuntimeGuard>();
        if (guard == null)
            guard = root.AddComponent<SkyPrisonMapPhysicalBoundsRuntimeGuard>();
        guard.boundaryLayerName = BoundaryLayerName;
        guard.pushableLayerName = PushableLayerName;
        guard.unitBodyLayerName = UnitBodyLayerName;
        guard.characterLayerName = CharacterLayerName;
        guard.ApplyNow();

        Vector3 center = map.mapBoundsCenter;
        Vector3 size = map.mapBoundsSize;

        float safeX = Mathf.Max(0.1f, Mathf.Abs(size.x));
        float safeZ = Mathf.Max(0.1f, Mathf.Abs(size.z));
        float wallThickness = Mathf.Max(0.1f, map.mapBoundsWallThickness);
        float wallHeight = Mathf.Max(0.1f, map.mapBoundsWallHeight);

        float xMin = center.x - safeX * 0.5f;
        float xMax = center.x + safeX * 0.5f;
        float zMin = center.z - safeZ * 0.5f;
        float zMax = center.z + safeZ * 0.5f;

        // mapBoundsCenter.y 在当前地图体系里是地表 / GroundBlock 高度。
        // 边界墙的底部贴住这条地平线，向上延伸，不参与修正地面高度。
        float wallY = center.y + wallHeight * 0.5f;

        CreateWall(
            root,
            "West",
            new Vector3(xMin - wallThickness * 0.5f, wallY, center.z),
            new Vector3(wallThickness, wallHeight, safeZ + wallThickness * 2f),
            boundaryLayer);

        CreateWall(
            root,
            "East",
            new Vector3(xMax + wallThickness * 0.5f, wallY, center.z),
            new Vector3(wallThickness, wallHeight, safeZ + wallThickness * 2f),
            boundaryLayer);

        CreateWall(
            root,
            "South",
            new Vector3(center.x, wallY, zMin - wallThickness * 0.5f),
            new Vector3(safeX + wallThickness * 2f, wallHeight, wallThickness),
            boundaryLayer);

        CreateWall(
            root,
            "North",
            new Vector3(center.x, wallY, zMax + wallThickness * 0.5f),
            new Vector3(safeX + wallThickness * 2f, wallHeight, wallThickness),
            boundaryLayer);

        if (map.mapBoundsUseCeiling)
        {
            float ceilingThickness = Mathf.Max(0.1f, map.mapBoundsCeilingThickness);
            float ceilingY = center.y + wallHeight + ceilingThickness * 0.5f;
            CreateWall(
                root,
                "Ceiling",
                new Vector3(center.x, ceilingY, center.z),
                new Vector3(safeX + wallThickness * 2f, ceilingThickness, safeZ + wallThickness * 2f),
                boundaryLayer);
        }
    }

    public static void ClearGeneratedWalls(GameObject root)
    {
        if (root == null)
            return;

        Transform transform = root.transform;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !child.name.StartsWith(WallPrefix))
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(child.gameObject);
            else
#endif
                Object.Destroy(child.gameObject);
        }
    }

    private static void CreateWall(GameObject root, string suffix, Vector3 worldCenter, Vector3 worldSize, int layer)
    {
        GameObject wall = new GameObject(WallPrefix + suffix);
        wall.transform.SetParent(root.transform, false);
        wall.transform.position = worldCenter;
        wall.transform.rotation = Quaternion.identity;
        wall.transform.localScale = Vector3.one;
        wall.layer = layer;
        wall.isStatic = true;

        BoxCollider collider = wall.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = worldSize;
        collider.isTrigger = false;
    }

    private static int ResolveLayer(string layerName, int fallback)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : fallback;
    }
}

[DisallowMultipleComponent]
public sealed class SkyPrisonMapPhysicalBoundsRuntimeGuard : MonoBehaviour
{
    public string boundaryLayerName = "StaticObstacle";
    public string pushableLayerName = "PushableProp";
    public string unitBodyLayerName = "UnitBody";
    public string characterLayerName = "Character2D";

    private void Reset()
    {
        ApplyNow();
    }

    private void Awake()
    {
        ApplyNow();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            ApplyNow();
    }
#endif

    public void ApplyNow()
    {
        int boundaryLayer = LayerMask.NameToLayer(boundaryLayerName);
        if (boundaryLayer < 0)
            return;

        gameObject.layer = boundaryLayer;
        ApplyLayerRecursive(transform, boundaryLayer);

        EnableCollision(boundaryLayer, pushableLayerName);
        EnableCollision(boundaryLayer, unitBodyLayerName);
        EnableCollision(boundaryLayer, characterLayerName);
    }

    private static void ApplyLayerRecursive(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            ApplyLayerRecursive(root.GetChild(i), layer);
    }

    private static void EnableCollision(int boundaryLayer, string otherLayerName)
    {
        int otherLayer = LayerMask.NameToLayer(otherLayerName);
        if (otherLayer < 0)
            return;

        Physics.IgnoreLayerCollision(boundaryLayer, otherLayer, false);
    }
}
