using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ground spline mesh line. Used for road lines / pipes / cables / rail guides.
/// This does NOT write into TerrainLayer or overlay textures. It generates a normal MeshRenderer object.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SkyPrisonGroundSplineLine : MonoBehaviour
{
    [Serializable]
    public class Point
    {
        public Vector3 worldPosition;
    }

    [Header("Data")]
    public string surfaceId = "";
    public string displayName = "Ground Spline";
    public GroundSurfaceMaterialDefinition sourceSurfaceMaterial;
    public bool syncVisualFromSourceMaterial = true;
    public bool syncShapeFromSourceMaterial = true;
    public List<Point> points = new List<Point>();

    [Header("Shape")]
    [Min(0.01f)] public float width = 0.55f;
    [Range(0f, 1f)] public float opacity = 1f;
    public bool closed = false;
    public bool followTerrain = true;
    public float terrainYOffset = 0.035f;

    [Header("Dash")]
    public bool dashed = false;
    [Min(0.01f)] public float dashLength = 2f;
    [Min(0f)] public float dashGap = 1f;
    [Min(0f)] public float dashPhase = 0f;

    [Header("Visual")]
    public Texture2D lineTexture;
    public Material lineMaterial;
    public Color lineTint = Color.white;
    [Min(0.01f)] public float textureWorldLength = 1f;
    [Tooltip("把贴图透明边界裁掉，让素材的实际可见线宽对应 width。马路线贴图常有 2048x1024 画布留白，必须开启。")]
    public bool fitWidthToTextureAlpha = true;
    [Range(0.001f, 0.5f)] public float alphaBoundsThreshold = 0.01f;

    [Header("Spline Mask / Damage")]
    [Tooltip("样条图案蒙版 / 破损图层。白色保留，黑色擦除，灰色过渡。")]
    public bool splineMaskEnabled = false;
    public Texture2D splineMaskTexture;
    [Range(0f, 1f)] public float splineMaskStrength = 1f;
    [Range(0f, 1f)] public float splineMaskThreshold = 0.45f;
    [Range(0.001f, 0.5f)] public float splineMaskSoftness = 0.08f;
    [Min(0.01f)] public float splineMaskWorldSize = 3f;
    public bool splineMaskInvert = false;
    public Vector2 splineMaskOffset = Vector2.zero;

    [SerializeField, HideInInspector] private float textureVMin = 0f;
    [SerializeField, HideInInspector] private float textureVMax = 1f;
    [SerializeField, HideInInspector] private bool hasCachedTextureAlphaBounds = false;
    [SerializeField, HideInInspector] private Texture2D cachedAlphaBoundsTexture;

    [Header("Target")]
    public Terrain targetTerrain;

    [Header("Map Bounds Clip")]
    [Tooltip("限制样条 Mesh 不超出 MapBounds。没有 MapBounds 时会退回 Terrain 范围。")]
    public bool clipToMapBounds = true;
    [Tooltip("边界内缩/外扩。0 表示严格贴 MapBounds；负值会稍微内缩。单位：米。")]
    public float mapBoundsClipPadding = 0f;

    [Header("Debug")]
    public bool rebuildEveryEditorUpdate = false;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh generatedMesh;
    private MaterialPropertyBlock materialPropertyBlock;


    private void Awake()
    {
        EnsureComponents();
        EnsureMaterial();
        RefreshTextureVerticalAlphaBounds();

        // Player build does not call editor-only OnEnable/OnValidate.
        // Refresh the mesh/material once so serialized scene lines do not depend on editor callbacks.
        if (points != null && points.Count >= 2)
            Rebuild();
        else
            ApplyVisualProperties();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        EnsureComponents();
        Rebuild();
    }

    private void OnValidate()
    {
        EnsureComponents();
        Rebuild();
    }
#endif

    public void Rebuild()
    {
        EnsureComponents();
        ApplySourceMaterialSettings(syncShapeFromSourceMaterial);
        EnsureMaterial();
        RefreshTextureVerticalAlphaBounds();

        if (points == null || points.Count < 2)
        {
            ClearMesh();
            return;
        }

        if (targetTerrain == null)
            targetTerrain = FindDefaultTerrain();

        List<Vector3> source = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
            source.Add(ProjectToGround(points[i].worldPosition));

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector2> maskUvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        // 重要：这里不再“阻止画出去”，也不再把中心线夹回边界。
        // GroundSpline 是普通 Mesh，正确做法是在最终生成网格时把带状多边形按 MapBounds 干净裁切。
        // 这样用户可以从地图内拖到地图外，最终只显示边界内那一截，端面直接被边界切平。
        Bounds clipBounds = default;
        bool hasClipBounds = false;
        if (clipToMapBounds)
            hasClipBounds = TryResolveMapClipBounds(out clipBounds);

        float carriedDistance = 0f;
        int segmentCount = closed ? source.Count : source.Count - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 a = source[i];
            Vector3 b = source[(i + 1) % source.Count];

            float len = Vector3.Distance(a, b);
            if (len <= 0.0001f)
                continue;

            if (dashed)
                AppendDashedSegment(a, b, len, ref carriedDistance, vertices, uvs, maskUvs, triangles, hasClipBounds, clipBounds);
            else
            {
                AppendQuadSegment(a, b, carriedDistance, carriedDistance + len, vertices, uvs, maskUvs, triangles, hasClipBounds, clipBounds);
                carriedDistance += len;
            }
        }

        if (generatedMesh == null)
        {
            generatedMesh = new Mesh();
            generatedMesh.name = "MESH_" + gameObject.name;
        }
        else
        {
            generatedMesh.Clear();
        }

        generatedMesh.SetVertices(vertices);
        generatedMesh.SetUVs(0, uvs);
        generatedMesh.SetUVs(1, maskUvs);
        generatedMesh.SetTriangles(triangles, 0);
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();
        meshFilter.sharedMesh = generatedMesh;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.EditorUtility.SetDirty(gameObject);
#endif
    }

    public void SetPoints(IEnumerable<Vector3> worldPoints)
    {
        points.Clear();
        if (worldPoints != null)
        {
            foreach (var p in worldPoints)
                points.Add(new Point { worldPosition = p });
        }
        Rebuild();
    }

    private void AppendDashedSegment(
        Vector3 a,
        Vector3 b,
        float len,
        ref float carriedDistance,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> maskUvs,
        List<int> triangles,
        bool hasClipBounds,
        Bounds clipBounds)
    {
        float period = Mathf.Max(0.01f, dashLength + dashGap);
        Vector3 dir = (b - a) / len;
        float local = 0f;

        while (local < len)
        {
            float global = carriedDistance + local + dashPhase;
            float inPeriod = Mathf.Repeat(global, period);
            bool inDash = inPeriod < dashLength;

            float nextBoundary = inDash ? (dashLength - inPeriod) : (period - inPeriod);
            float step = Mathf.Min(len - local, Mathf.Max(0.001f, nextBoundary));

            if (inDash && step > 0.001f)
            {
                Vector3 da = a + dir * local;
                Vector3 db = a + dir * (local + step);
                AppendQuadSegment(da, db, carriedDistance + local, carriedDistance + local + step, vertices, uvs, maskUvs, triangles, hasClipBounds, clipBounds);
            }

            local += step;
        }

        carriedDistance += len;
    }

    private void AppendQuadSegment(
        Vector3 a,
        Vector3 b,
        float distanceA,
        float distanceB,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> maskUvs,
        List<int> triangles,
        bool hasClipBounds,
        Bounds clipBounds)
    {
        Vector3 dir = b - a;
        dir.y = 0f;
        float len = dir.magnitude;
        if (len <= 0.0001f)
            return;
        dir /= len;

        Vector3 right = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);

        SplineClipVertex v0 = new SplineClipVertex(a - right, new Vector2(distanceA / Mathf.Max(0.01f, textureWorldLength), textureVMin), new Vector2(distanceA, 0f));
        SplineClipVertex v1 = new SplineClipVertex(a + right, new Vector2(distanceA / Mathf.Max(0.01f, textureWorldLength), textureVMax), new Vector2(distanceA, width));
        SplineClipVertex v2 = new SplineClipVertex(b + right, new Vector2(distanceB / Mathf.Max(0.01f, textureWorldLength), textureVMax), new Vector2(distanceB, width));
        SplineClipVertex v3 = new SplineClipVertex(b - right, new Vector2(distanceB / Mathf.Max(0.01f, textureWorldLength), textureVMin), new Vector2(distanceB, 0f));

        List<SplineClipVertex> polygon = new List<SplineClipVertex>(4) { v0, v1, v2, v3 };

        if (hasClipBounds)
        {
            polygon = ClipPolygonToXZBounds(polygon, clipBounds, mapBoundsClipPadding);
            if (polygon == null || polygon.Count < 3)
                return;
        }

        int start = vertices.Count;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 p = polygon[i].position;
            // 裁切产生的新端点也要重新贴地。这样 Terrain 有坡度时，切口不会漂在空中。
            p = ProjectToGround(p);
            vertices.Add(transform.InverseTransformPoint(p));
            uvs.Add(polygon[i].uv);
            maskUvs.Add(polygon[i].maskUv);
        }

        // Sutherland-Hodgman 裁切后仍是凸多边形，扇形三角化即可。
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            triangles.Add(start);
            triangles.Add(start + i);
            triangles.Add(start + i + 1);
        }
    }

    private struct SplineClipVertex
    {
        public Vector3 position;
        public Vector2 uv;
        public Vector2 maskUv;

        public SplineClipVertex(Vector3 position, Vector2 uv, Vector2 maskUv)
        {
            this.position = position;
            this.uv = uv;
            this.maskUv = maskUv;
        }
    }

    private static List<SplineClipVertex> ClipPolygonToXZBounds(List<SplineClipVertex> input, Bounds bounds, float padding)
    {
        if (input == null || input.Count == 0)
            return input;

        float minX = bounds.min.x + padding;
        float maxX = bounds.max.x - padding;
        float minZ = bounds.min.z + padding;
        float maxZ = bounds.max.z - padding;
        if (maxX <= minX || maxZ <= minZ)
            return input;

        List<SplineClipVertex> output = input;
        output = ClipPolygon(output, v => v.position.x >= minX, (a, b) => IntersectAtX(a, b, minX));
        output = ClipPolygon(output, v => v.position.x <= maxX, (a, b) => IntersectAtX(a, b, maxX));
        output = ClipPolygon(output, v => v.position.z >= minZ, (a, b) => IntersectAtZ(a, b, minZ));
        output = ClipPolygon(output, v => v.position.z <= maxZ, (a, b) => IntersectAtZ(a, b, maxZ));
        return output;
    }

    private static List<SplineClipVertex> ClipPolygon(
        List<SplineClipVertex> input,
        Func<SplineClipVertex, bool> inside,
        Func<SplineClipVertex, SplineClipVertex, SplineClipVertex> intersect)
    {
        List<SplineClipVertex> output = new List<SplineClipVertex>();
        if (input == null || input.Count == 0)
            return output;

        SplineClipVertex previous = input[input.Count - 1];
        bool previousInside = inside(previous);

        for (int i = 0; i < input.Count; i++)
        {
            SplineClipVertex current = input[i];
            bool currentInside = inside(current);

            if (currentInside)
            {
                if (!previousInside)
                    output.Add(intersect(previous, current));
                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(intersect(previous, current));
            }

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }

    private static SplineClipVertex IntersectAtX(SplineClipVertex a, SplineClipVertex b, float x)
    {
        float denom = b.position.x - a.position.x;
        float t = Mathf.Abs(denom) < 0.000001f ? 0f : Mathf.Clamp01((x - a.position.x) / denom);
        return LerpClipVertex(a, b, t);
    }

    private static SplineClipVertex IntersectAtZ(SplineClipVertex a, SplineClipVertex b, float z)
    {
        float denom = b.position.z - a.position.z;
        float t = Mathf.Abs(denom) < 0.000001f ? 0f : Mathf.Clamp01((z - a.position.z) / denom);
        return LerpClipVertex(a, b, t);
    }

    private static SplineClipVertex LerpClipVertex(SplineClipVertex a, SplineClipVertex b, float t)
    {
        return new SplineClipVertex(
            Vector3.Lerp(a.position, b.position, t),
            Vector2.Lerp(a.uv, b.uv, t),
            Vector2.Lerp(a.maskUv, b.maskUv, t));
    }

    private void RefreshTextureVerticalAlphaBounds()
    {
        if (!fitWidthToTextureAlpha || lineTexture == null)
        {
            textureVMin = 0f;
            textureVMax = 1f;
            hasCachedTextureAlphaBounds = false;
            cachedAlphaBoundsTexture = null;
            return;
        }

        // Critical: do NOT reset to full 0..1 before we know the texture is readable.
        // Imported textures can be readable in edit generation but not readable after entering Play / domain reload.
        // If we reset here and GetPixels32 throws, the visible road line becomes thin again because the full 2048x1024 canvas is used.
        if (hasCachedTextureAlphaBounds
            && cachedAlphaBoundsTexture == lineTexture
            && textureVMax > textureVMin + 0.0001f)
        {
            return;
        }

        float previousMin = textureVMin;
        float previousMax = textureVMax;
        bool previousValid = hasCachedTextureAlphaBounds
            && cachedAlphaBoundsTexture == lineTexture
            && previousMax > previousMin + 0.0001f;

        try
        {
            int widthPx = lineTexture.width;
            int heightPx = lineTexture.height;
            if (widthPx <= 0 || heightPx <= 0)
                return;

            Color32[] pixels = lineTexture.GetPixels32();
            if (pixels == null || pixels.Length == 0)
                return;

            byte threshold = (byte)Mathf.Clamp(Mathf.RoundToInt(alphaBoundsThreshold * 255f), 1, 254);
            int minY = heightPx;
            int maxY = -1;

            for (int y = 0; y < heightPx; y++)
            {
                int row = y * widthPx;
                bool rowHasAlpha = false;
                for (int x = 0; x < widthPx; x++)
                {
                    if (pixels[row + x].a >= threshold)
                    {
                        rowHasAlpha = true;
                        break;
                    }
                }

                if (rowHasAlpha)
                {
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxY < minY)
                return;

            // Expand by one pixel so bilinear filtering does not clip the edge.
            minY = Mathf.Max(0, minY - 1);
            maxY = Mathf.Min(heightPx - 1, maxY + 1);

            float nextMin = Mathf.Clamp01((float)minY / Mathf.Max(1, heightPx - 1));
            float nextMax = Mathf.Clamp01((float)maxY / Mathf.Max(1, heightPx - 1));

            if (nextMax <= nextMin + 0.0001f)
            {
                textureVMin = 0f;
                textureVMax = 1f;
                hasCachedTextureAlphaBounds = false;
                cachedAlphaBoundsTexture = lineTexture;
                return;
            }

            textureVMin = nextMin;
            textureVMax = nextMax;
            cachedAlphaBoundsTexture = lineTexture;
            hasCachedTextureAlphaBounds = true;
        }
        catch (Exception)
        {
            // If the source texture is not readable after entering Play / reload, keep the cached alpha crop.
            // Falling back to full 0..1 is exactly what made the 0.55m road line turn into a hairline.
            if (previousValid)
            {
                textureVMin = previousMin;
                textureVMax = previousMax;
                hasCachedTextureAlphaBounds = true;
                cachedAlphaBoundsTexture = lineTexture;
                return;
            }

            // Last-resort fallback for the project's standard RoadLine 2048x1024 atlas: center band.
            // This is only used when no cached data exists and the texture is unreadable.
            textureVMin = 0.45f;
            textureVMax = 0.55f;
            cachedAlphaBoundsTexture = lineTexture;
            hasCachedTextureAlphaBounds = true;
        }
    }

    public void ForceRecalculateTextureAlphaBounds()
    {
        hasCachedTextureAlphaBounds = false;
        cachedAlphaBoundsTexture = null;
        RefreshTextureVerticalAlphaBounds();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.EditorUtility.SetDirty(gameObject);
#endif
    }


    private bool ClipSegmentToMapBounds(ref Vector3 a, ref Vector3 b)
    {
        if (!clipToMapBounds)
            return true;

        if (!TryResolveMapClipBounds(out Bounds bounds))
            return true;

        // 裁剪的是中心线，但实际 Mesh 会向两侧扩出 width/2。
        // 因此必须先把可绘制范围向内收半个线宽，否则中心线虽然在边界内，四个角仍会伸出地图。
        float safeInset = Mathf.Max(0f, width * 0.5f) + mapBoundsClipPadding;
        float minX = bounds.min.x + safeInset;
        float maxX = bounds.max.x - safeInset;
        float minZ = bounds.min.z + safeInset;
        float maxZ = bounds.max.z - safeInset;
        if (maxX <= minX || maxZ <= minZ)
            return true;

        float t0 = 0f;
        float t1 = 1f;
        float dx = b.x - a.x;
        float dz = b.z - a.z;

        if (!ClipTest(-dx, a.x - minX, ref t0, ref t1)) return false;
        if (!ClipTest( dx, maxX - a.x, ref t0, ref t1)) return false;
        if (!ClipTest(-dz, a.z - minZ, ref t0, ref t1)) return false;
        if (!ClipTest( dz, maxZ - a.z, ref t0, ref t1)) return false;

        Vector3 originalA = a;
        Vector3 delta = b - a;
        if (t1 < 1f)
            b = originalA + delta * t1;
        if (t0 > 0f)
            a = originalA + delta * t0;

        return true;
    }

    private static bool ClipTest(float p, float q, ref float t0, ref float t1)
    {
        if (Mathf.Abs(p) < 0.000001f)
            return q >= 0f;

        float r = q / p;
        if (p < 0f)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }
        return true;
    }

    private bool TryResolveMapClipBounds(out Bounds bounds)
    {
        // 先拿 Terrain 范围作为兜底。GroundSpline 是贴在 GroundTerrain 上的，
        // MapBounds 节点如果没有真实 Collider/Renderer，不能用 transform scale=1 的小盒子把线整段裁没。
        Bounds terrainBounds = default;
        bool hasTerrainBounds = false;

        if (targetTerrain == null)
            targetTerrain = FindDefaultTerrain();

        if (targetTerrain != null && targetTerrain.terrainData != null)
        {
            Vector3 pos = targetTerrain.transform.position;
            Vector3 size = targetTerrain.terrainData.size;
            terrainBounds = new Bounds(pos + size * 0.5f, size);
            hasTerrainBounds = true;
        }

        // 最高优先级：使用真正的 SkyPrisonMapBounds 组件。
        // 物理边界是一圈有厚度的墙，Terrain 范围又可能包含灰色边界区；
        // 只有 MapBounds 组件的 center / size 才是地图有效内边界。
        if (TryGetSkyPrisonMapBoundsComponentBounds(hasTerrainBounds ? terrainBounds : (Bounds?)null, out Bounds mapComponentBounds)
            && IsUsableClipBounds(mapComponentBounds, hasTerrainBounds ? terrainBounds : (Bounds?)null))
        {
            bounds = mapComponentBounds;
            return true;
        }

        // 其次尝试有实际范围的 MapBoundary / MapBounds。
        // 但只有当它尺寸足够大且与 Terrain 范围合理重叠时才采用。
        string[] candidatePaths =
        {
            // 物理边界优先。你的项目里真实边界通常在 System/MapBoundary/_MapPhysicalBounds。
            // 之前没搜到它时会退回 GroundTerrain 范围，所以视觉上会继续伸到地图外灰区。
            "System/MapBoundary/_MapPhysicalBounds",
            "WorldRoot/MapBoundary/_MapPhysicalBounds",
            "MapBoundary/_MapPhysicalBounds",
            "WorldLogic/MapBoundary/_MapPhysicalBounds",

            "System/MapBoundary",
            "WorldRoot/MapBoundary",
            "MapBoundary",
            "WorldLogic/MapBoundary",

            "System/MapBounds",
            "WorldRoot/MapBounds",
            "MapBounds",
            "WorldLogic/MapBounds"
        };

        for (int i = 0; i < candidatePaths.Length; i++)
        {
            GameObject candidate = GameObject.Find(candidatePaths[i]);
            if (candidate == null)
                continue;

            // _MapPhysicalBounds 通常是一圈带厚度的 BoxCollider 墙。
            // 直接 Encapsulate 全部 collider 会得到“外边界”，线条就会被允许伸进灰色墙厚区域。
            // 这里优先从墙体 collider 推导“内边界”：左墙取 max.x，右墙取 min.x，下墙取 max.z，上墙取 min.z。
            if (candidate.name.Contains("_MapPhysicalBounds")
                && TryGetInnerBoundsFromPhysicalWalls(candidate, hasTerrainBounds ? terrainBounds : (Bounds?)null, out Bounds innerBounds)
                && IsUsableClipBounds(innerBounds, hasTerrainBounds ? terrainBounds : (Bounds?)null))
            {
                bounds = innerBounds;
                return true;
            }

            if (TryGetObjectBounds(candidate, out Bounds candidateBounds)
                && IsUsableClipBounds(candidateBounds, hasTerrainBounds ? terrainBounds : (Bounds?)null))
            {
                bounds = candidateBounds;
                return true;
            }
        }

        if (hasTerrainBounds)
        {
            bounds = terrainBounds;
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetSkyPrisonMapBoundsComponentBounds(Bounds? terrainBounds, out Bounds bounds)
    {
        bounds = default;

        // 不直接引用 SkyPrisonMapBounds 类型，避免这个核心运行时代码被编辑器/程序集拆分卡住。
        // 通过反射读取它的 public center / size 字段或属性。
        MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (behaviours == null)
            return false;

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null)
                continue;

            Type type = mb.GetType();
            if (type == null || type.Name != "SkyPrisonMapBounds")
                continue;

            if (!TryReadVector3Member(type, mb, "center", out Vector3 center))
                center = mb.transform.position;

            if (!TryReadVector3Member(type, mb, "size", out Vector3 size))
                size = mb.transform.lossyScale;

            size = new Vector3(Mathf.Abs(size.x), Mathf.Max(0.01f, Mathf.Abs(size.y)), Mathf.Abs(size.z));
            Bounds candidate = new Bounds(center, size);
            if (IsUsableClipBounds(candidate, terrainBounds))
            {
                bounds = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadVector3Member(Type type, object instance, string memberName, out Vector3 value)
    {
        value = default;
        if (type == null || instance == null)
            return false;

        System.Reflection.FieldInfo field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(Vector3))
        {
            value = (Vector3)field.GetValue(instance);
            return true;
        }

        System.Reflection.PropertyInfo property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(Vector3) && property.GetIndexParameters().Length == 0)
        {
            value = (Vector3)property.GetValue(instance, null);
            return true;
        }

        return false;
    }

    private static bool TryGetInnerBoundsFromPhysicalWalls(GameObject root, Bounds? terrainBounds, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
            return false;

        Bounds outer = default;
        bool hasOuter = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null)
                continue;

            Bounds b = c.bounds;
            if (b.size.x < 0.01f || b.size.z < 0.01f)
                continue;

            if (!hasOuter)
            {
                outer = b;
                hasOuter = true;
            }
            else outer.Encapsulate(b);
        }

        if (!hasOuter)
            return false;

        Vector3 center = terrainBounds.HasValue ? terrainBounds.Value.center : outer.center;

        float leftInner = float.NegativeInfinity;
        float rightInner = float.PositiveInfinity;
        float bottomInner = float.NegativeInfinity;
        float topInner = float.PositiveInfinity;

        int verticalWalls = 0;
        int horizontalWalls = 0;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null)
                continue;

            Bounds b = c.bounds;
            if (b.size.x < 0.01f || b.size.z < 0.01f)
                continue;

            // 竖墙：Z 方向长、X 方向薄。
            if (b.size.z > b.size.x * 1.5f)
            {
                verticalWalls++;
                if (b.center.x < center.x)
                    leftInner = Mathf.Max(leftInner, b.max.x);
                else
                    rightInner = Mathf.Min(rightInner, b.min.x);
            }
            // 横墙：X 方向长、Z 方向薄。
            else if (b.size.x > b.size.z * 1.5f)
            {
                horizontalWalls++;
                if (b.center.z < center.z)
                    bottomInner = Mathf.Max(bottomInner, b.max.z);
                else
                    topInner = Mathf.Min(topInner, b.min.z);
            }
        }

        // 没有识别出完整四面墙时，不要乱猜内边界。
        if (verticalWalls < 2 || horizontalWalls < 2
            || float.IsNegativeInfinity(leftInner) || float.IsPositiveInfinity(rightInner)
            || float.IsNegativeInfinity(bottomInner) || float.IsPositiveInfinity(topInner))
            return false;

        if (rightInner <= leftInner || topInner <= bottomInner)
            return false;

        Vector3 size = new Vector3(rightInner - leftInner, outer.size.y, topInner - bottomInner);
        Vector3 boundsCenter = new Vector3((leftInner + rightInner) * 0.5f, outer.center.y, (bottomInner + topInner) * 0.5f);
        bounds = new Bounds(boundsCenter, size);
        return bounds.size.x >= 2f && bounds.size.z >= 2f;
    }

    private static bool IsUsableClipBounds(Bounds candidate, Bounds? terrainBounds)
    {
        // 防止空节点 / 普通 Transform scale=1 被当成地图边界，导致所有线被裁掉。
        if (candidate.size.x < 2f || candidate.size.z < 2f)
            return false;

        if (!terrainBounds.HasValue)
            return true;

        Bounds terrain = terrainBounds.Value;
        bool intersectsXZ = candidate.max.x >= terrain.min.x
            && candidate.min.x <= terrain.max.x
            && candidate.max.z >= terrain.min.z
            && candidate.min.z <= terrain.max.z;

        if (!intersectsXZ)
            return false;

        return true;
    }

    private static bool TryGetObjectBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        bool has = false;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;
            if (!has)
            {
                bounds = colliders[i].bounds;
                has = true;
            }
            else bounds.Encapsulate(colliders[i].bounds);
        }

        if (!has)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                if (!has)
                {
                    bounds = renderers[i].bounds;
                    has = true;
                }
                else bounds.Encapsulate(renderers[i].bounds);
            }
        }

        if (!has)
        {
            Vector3 scale = root.transform.lossyScale;
            // 只有明显像地图边界的大尺寸节点才允许用 Transform scale 兜底。
            // 普通空节点 scale=1 绝不能当作 MapBounds，否则会把整条线裁没。
            if (Mathf.Abs(scale.x) >= 2f && Mathf.Abs(scale.z) >= 2f)
            {
                bounds = new Bounds(root.transform.position, new Vector3(Mathf.Abs(scale.x), 1f, Mathf.Abs(scale.z)));
                has = true;
            }
        }

        return has && bounds.size.x >= 2f && bounds.size.z >= 2f;
    }

    private Vector3 ProjectToGround(Vector3 world)
    {
        if (!followTerrain || targetTerrain == null || targetTerrain.terrainData == null)
            return world;

        Vector3 terrainPos = targetTerrain.transform.position;
        TerrainData data = targetTerrain.terrainData;
        Vector3 local = world - terrainPos;
        if (local.x < -0.01f || local.z < -0.01f || local.x > data.size.x + 0.01f || local.z > data.size.z + 0.01f)
            return world;

        float y = terrainPos.y + targetTerrain.SampleHeight(world) + terrainYOffset;
        return new Vector3(world.x, y, world.z);
    }

    private Terrain FindDefaultTerrain()
    {
        GameObject groundTerrain = GameObject.Find("WorldRoot/GroundRoot/GroundTerrain");
        if (groundTerrain != null)
        {
            Terrain t = groundTerrain.GetComponent<Terrain>();
            if (t != null)
                return t;
        }
        return Terrain.activeTerrain;
    }

    private void EnsureComponents()
    {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

        if (lineMaterial != null)
            meshRenderer.sharedMaterial = lineMaterial;

        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();
    }

    private void EnsureMaterial()
    {
        Shader splineShader = Shader.Find("SkyPrison/GroundSplineLine_MaskedUnlit_BeforeUnit");

        if (lineMaterial == null)
        {
            Shader shader = splineShader;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Standard");
            lineMaterial = new Material(shader);
            lineMaterial.name = "MAT_" + gameObject.name;
            lineMaterial.hideFlags = HideFlags.None;
        }
        else if (splineShader != null && lineMaterial.shader != splineShader)
        {
            // GroundSpline / RoadLine needs its dedicated shader so mask settings can work.
            // Keep the material asset, only switch the shader used by this line material.
            lineMaterial.shader = splineShader;
        }

        if (lineMaterial != null)
            lineMaterial.hideFlags = HideFlags.None;

        ConfigureMaterialForTransparentTexture(lineMaterial);
        ApplyVisualProperties();
    }


    private static void ConfigureMaterialForTransparentTexture(Material mat)
    {
        if (mat == null)
            return;

        // RoadLine / spline textures must use their own alpha channel.
        // If URP Unlit stays Opaque, Unity renders the whole ribbon mesh as a white rectangle
        // and only the center texture line appears colored. Force the material into transparent mode.
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);          // URP: Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);              // Alpha blend
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 50;

        // Build safety:
        // Do NOT use the non-build-save flag here. GroundSplineLine stores its line material
        // on MeshRenderer.sharedMaterial / lineMaterial. Marking it as not saved in builds
        // can make Player builds drop the material and render the line as magenta.
        mat.hideFlags = HideFlags.None;
    }

    public void ApplySourceMaterialSettings(bool includeShape)
    {
        if (!syncVisualFromSourceMaterial || sourceSurfaceMaterial == null)
            return;

        if (!string.IsNullOrWhiteSpace(sourceSurfaceMaterial.surfaceId))
            surfaceId = sourceSurfaceMaterial.surfaceId;

        string sourceName = string.IsNullOrWhiteSpace(sourceSurfaceMaterial.displayName)
            ? sourceSurfaceMaterial.name
            : sourceSurfaceMaterial.displayName;
        if (!string.IsNullOrWhiteSpace(sourceName))
            displayName = sourceName;

        // The texture is the source of truth for white/yellow/etc. Do not fake color by tinting by default.
        lineTexture = sourceSurfaceMaterial.splineTexture;
        textureWorldLength = Mathf.Max(0.01f, sourceSurfaceMaterial.splineSegmentWorldLength);
        lineTint = ResolveOptionalSplineTint(sourceSurfaceMaterial);

        if (includeShape)
        {
            float resolvedWidth = sourceSurfaceMaterial.EffectiveFixedSplineWorldWidth;
            if (resolvedWidth <= 0.001f)
                resolvedWidth = sourceSurfaceMaterial.splineWorldWidth;
            if (resolvedWidth > 0.001f)
                width = resolvedWidth;

            opacity = Mathf.Clamp01(sourceSurfaceMaterial.splineOpacity);
        }

        splineMaskEnabled = sourceSurfaceMaterial.splineMaskEnabled;
        splineMaskTexture = sourceSurfaceMaterial.splineMaskEnabled ? sourceSurfaceMaterial.splineMaskTexture : null;
        splineMaskStrength = Mathf.Clamp01(sourceSurfaceMaterial.splineMaskStrength);
        splineMaskThreshold = Mathf.Clamp01(sourceSurfaceMaterial.splineMaskThreshold);
        splineMaskSoftness = Mathf.Clamp(sourceSurfaceMaterial.splineMaskSoftness, 0.001f, 0.5f);
        splineMaskWorldSize = Mathf.Max(0.01f, sourceSurfaceMaterial.splineMaskWorldSize);
        splineMaskInvert = sourceSurfaceMaterial.splineMaskInvert;
        splineMaskOffset = sourceSurfaceMaterial.splineMaskOffset;
    }

    private void ApplyVisualProperties()
    {
        if (meshRenderer == null || lineMaterial == null)
            return;

        meshRenderer.sharedMaterial = lineMaterial;

        // Keep the shared material neutral. Per-line color/texture is applied through MPB,
        // so reopening the scene cannot leave a previous yellow/white line cached on the material asset.
        SetMaterialColorAndTexture(lineMaterial, Color.white, lineTexture);
        SetMaterialSplineMaskDefaults(lineMaterial);

        Color c = lineTint;
        if (c.a <= 0.0001f && c.r <= 0.0001f && c.g <= 0.0001f && c.b <= 0.0001f)
            c = Color.white;
        c.a *= opacity;

        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();

        // Clear first: old generated objects may still carry a yellow texture/tint in MPB.
        // A stale property block has higher priority than the shared material.
        materialPropertyBlock.Clear();
        SetPropertyBlockColorAndTexture(materialPropertyBlock, c, lineTexture);
        SetPropertyBlockSplineMask(materialPropertyBlock);
        meshRenderer.SetPropertyBlock(materialPropertyBlock);
    }

    private static Color ResolveOptionalSplineTint(GroundSurfaceMaterialDefinition materialDef)
    {
        // GroundSpline uses the spline texture as the source of truth.
        // Do NOT apply GroundSurface color-composition here. That color section is for
        // Terrain/base-surface preview, not for repainting road-line textures.
        return Color.white;
    }

    private static void SetMaterialColorAndTexture(Material mat, Color color, Texture2D texture)
    {
        if (mat == null)
            return;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseColorFactor")) mat.SetColor("_BaseColorFactor", color);

        if (texture == null)
            return;

        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
        if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", texture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
    }

    private static void SetPropertyBlockColorAndTexture(MaterialPropertyBlock block, Color color, Texture2D texture)
    {
        if (block == null)
            return;

        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        block.SetColor("_BaseColorFactor", color);

        if (texture == null)
            return;

        block.SetTexture("_BaseMap", texture);
        block.SetTexture("_BaseColorMap", texture);
        block.SetTexture("_MainTex", texture);
    }

    private static void SetMaterialSplineMaskDefaults(Material mat)
    {
        if (mat == null)
            return;

        if (mat.HasProperty("_SplineMaskEnabled")) mat.SetFloat("_SplineMaskEnabled", 0f);
        if (mat.HasProperty("_SplineMaskStrength")) mat.SetFloat("_SplineMaskStrength", 0f);
        if (mat.HasProperty("_SplineMaskThreshold")) mat.SetFloat("_SplineMaskThreshold", 0.45f);
        if (mat.HasProperty("_SplineMaskSoftness")) mat.SetFloat("_SplineMaskSoftness", 0.08f);
        if (mat.HasProperty("_SplineMaskWorldSize")) mat.SetFloat("_SplineMaskWorldSize", 3f);
        if (mat.HasProperty("_SplineMaskInvert")) mat.SetFloat("_SplineMaskInvert", 0f);
        if (mat.HasProperty("_SplineMaskOffset")) mat.SetVector("_SplineMaskOffset", Vector4.zero);
    }

    private void SetPropertyBlockSplineMask(MaterialPropertyBlock block)
    {
        if (block == null)
            return;

        bool enabled = splineMaskEnabled && splineMaskTexture != null && splineMaskStrength > 0.0001f;
        block.SetFloat("_SplineMaskEnabled", enabled ? 1f : 0f);
        block.SetFloat("_SplineMaskStrength", enabled ? Mathf.Clamp01(splineMaskStrength) : 0f);
        block.SetFloat("_SplineMaskThreshold", Mathf.Clamp01(splineMaskThreshold));
        block.SetFloat("_SplineMaskSoftness", Mathf.Clamp(splineMaskSoftness, 0.001f, 0.5f));
        block.SetFloat("_SplineMaskWorldSize", Mathf.Max(0.01f, splineMaskWorldSize));
        block.SetFloat("_SplineMaskInvert", splineMaskInvert ? 1f : 0f);
        block.SetVector("_SplineMaskOffset", new Vector4(splineMaskOffset.x, splineMaskOffset.y, 0f, 0f));

        if (splineMaskTexture != null)
        {
            splineMaskTexture.wrapMode = TextureWrapMode.Repeat;
            splineMaskTexture.filterMode = FilterMode.Bilinear;
            block.SetTexture("_SplineMaskTex", splineMaskTexture);
        }
    }

    private void ClearMesh()
    {
        if (meshFilter != null)
            meshFilter.sharedMesh = null;
    }
}
