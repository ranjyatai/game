using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Spline geometry drawing tool.
/// Creates GroundSpline Mesh objects. It does not paint TerrainLayer and does not write GroundOverlay textures.
/// The selected GroundSurfaceMaterialDefinition.splineTexture is the only default color source.
/// </summary>
public class SkyPrisonGroundOverlaySplineGeometryTool : EditorWindow
{
    private GroundSurfaceMaterialDefinition surfaceMaterial;
    private string surfaceMaterialPath = "";
    private Texture2D currentSplineTexture;

    private Terrain targetTerrain;
    private SkyPrisonGroundSplineLine activeLine;
    private readonly List<Vector3> pathPoints = new List<Vector3>();

    private float lineWidth = 0.55f;
    private float opacity = 1f;
    private bool followTerrain = true;
    private float yOffset = 0.035f;

    private bool clampToMapBounds = true;
    private bool rejectOutsideMapBounds = false;
    private float mapBoundsClipPadding = 0f;

    private bool smoothPath = false;
    private int smoothSubdivisions = 8;

    private bool dashed = false;
    private float dashLength = 2f;
    private float dashGap = 1f;
    private float dashPhase = 0f;

    private bool shiftClickCreatesSegment = true;
    private bool clearAfterBake = true;
    private bool sceneMode = false;

    private Vector2 scroll;

    private const int MaxPathUndo = 32;
    private readonly List<List<Vector3>> pathUndo = new List<List<Vector3>>();

    [MenuItem("Tools/Sky Prison/Ground/Spline 几何路径绘制器")]
    public static void Open()
    {
        var window = GetWindow<SkyPrisonGroundOverlaySplineGeometryTool>("Spline 几何路径绘制");
        window.RefreshTarget();
        window.Show();
    }

    public static void OpenForSurfaceMaterial(GroundSurfaceMaterialDefinition material, float width, float alpha)
    {
        var window = GetWindow<SkyPrisonGroundOverlaySplineGeometryTool>("Spline 几何路径绘制");
        window.SetSurfaceMaterialFromPlacement(material, width, alpha, true, true);
        window.RefreshTarget();
        window.Show();
        window.Focus();
    }

    // Compatibility for older calls that still pass an ignored overlay-layer value.
    public static void OpenForSurfaceMaterial(GroundSurfaceMaterialDefinition material, object ignoredLayer, float width, float alpha)
    {
        OpenForSurfaceMaterial(material, width, alpha);
    }

    public static void SyncFromPlacementFocus(GroundSurfaceMaterialDefinition material, float width, float alpha)
    {
        var windows = Resources.FindObjectsOfTypeAll<SkyPrisonGroundOverlaySplineGeometryTool>();
        if (windows == null || windows.Length == 0)
            return;

        foreach (var window in windows)
        {
            if (window == null)
                continue;

            // Do not repaint on hover. Only repaint if the focused asset really changed.
            bool changed = window.SetSurfaceMaterialFromPlacement(material, width, alpha, false, true);
            if (changed)
                window.Repaint();
        }
    }

    private bool SetSurfaceMaterialFromPlacement(
        GroundSurfaceMaterialDefinition material,
        float fallbackWidth,
        float fallbackAlpha,
        bool focusOpen,
        bool clearActiveLineWhenAssetChanges)
    {
        GroundSurfaceMaterialDefinition reloaded = ReloadSurfaceMaterialAsset(material);
        string newPath = reloaded != null ? AssetDatabase.GetAssetPath(reloaded) : "";
        Texture2D newTexture = reloaded != null ? reloaded.splineTexture : null;

        bool changed = !string.Equals(surfaceMaterialPath, newPath, System.StringComparison.Ordinal)
                       || currentSplineTexture != newTexture;

        surfaceMaterial = reloaded;
        surfaceMaterialPath = newPath;
        currentSplineTexture = newTexture;

        ApplySurfaceMaterialDefaults(fallbackWidth, fallbackAlpha);

        if (changed && clearActiveLineWhenAssetChanges)
        {
            // Switching from 白线 to 黄线 must not keep editing an old white-line object.
            activeLine = null;
        }

        if (focusOpen && changed)
            SceneView.RepaintAll();

        return changed;
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        RefreshTarget();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        sceneMode = false;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("样条图案几何路径绘制器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("生成 GroundSpline Mesh 对象；不写 TerrainLayer，也不写 GroundOverlay 大贴图。样条颜色默认完全来自 splineTexture 原图。", MessageType.Info);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("数据", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        GroundSurfaceMaterialDefinition picked = (GroundSurfaceMaterialDefinition)EditorGUILayout.ObjectField("样条素材", surfaceMaterial, typeof(GroundSurfaceMaterialDefinition), false);
        if (EditorGUI.EndChangeCheck())
        {
            SetSurfaceMaterialFromPlacement(picked, lineWidth, opacity, false, true);
            GUI.changed = true;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("当前素材路径", string.IsNullOrEmpty(surfaceMaterialPath) ? "-" : surfaceMaterialPath);
            EditorGUILayout.ObjectField("当前样条贴图", currentSplineTexture, typeof(Texture2D), false);
            EditorGUILayout.Toggle("启用蒙版参照", surfaceMaterial != null && surfaceMaterial.splineMaskEnabled);
            EditorGUILayout.ObjectField("蒙版参照贴图", surfaceMaterial != null ? surfaceMaterial.splineMaskTexture : null, typeof(Texture2D), false);
        }

        DrawSplineMaskReferencePreview();

        targetTerrain = (Terrain)EditorGUILayout.ObjectField("GroundTerrain", targetTerrain, typeof(Terrain), true);
        activeLine = (SkyPrisonGroundSplineLine)EditorGUILayout.ObjectField("当前线对象", activeLine, typeof(SkyPrisonGroundSplineLine), true);

        if (GUILayout.Button("刷新 / 查找目标"))
            RefreshTarget(true);

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("参数", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        lineWidth = Mathf.Max(0.01f, EditorGUILayout.FloatField("线宽 / m", lineWidth));
        opacity = EditorGUILayout.Slider("不透明度", opacity, 0f, 1f);
        followTerrain = EditorGUILayout.Toggle("跟随 Terrain 高度", followTerrain);
        yOffset = EditorGUILayout.FloatField("贴地高度偏移", yOffset);

        clampToMapBounds = EditorGUILayout.Toggle("生成时裁切到 MapBounds", clampToMapBounds);
        using (new EditorGUI.DisabledScope(!clampToMapBounds))
        {
            rejectOutsideMapBounds = false;
            EditorGUILayout.LabelField("越界输入", "允许，最终 Mesh 会被边界干净裁切");
            mapBoundsClipPadding = EditorGUILayout.FloatField("裁切边界偏移 / m", mapBoundsClipPadding);
        }

        smoothPath = EditorGUILayout.Toggle("曲线 / 平滑路径", smoothPath);
        using (new EditorGUI.DisabledScope(!smoothPath))
            smoothSubdivisions = Mathf.Clamp(EditorGUILayout.IntField("曲线细分", smoothSubdivisions), 2, 32);

        dashed = EditorGUILayout.Toggle("虚线", dashed);
        using (new EditorGUI.DisabledScope(!dashed))
        {
            dashLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("实线段长度 / m", dashLength));
            dashGap = Mathf.Max(0f, EditorGUILayout.FloatField("空白间隔 / m", dashGap));
            dashPhase = Mathf.Max(0f, EditorGUILayout.FloatField("虚线相位偏移 / m", dashPhase));
        }

        using (new EditorGUI.DisabledScope(smoothPath))
            shiftClickCreatesSegment = EditorGUILayout.Toggle("Shift 点击立即生成直线段", shiftClickCreatesSegment && !smoothPath);
        if (smoothPath)
            shiftClickCreatesSegment = false;

        clearAfterBake = EditorGUILayout.Toggle("烘焙后清空路径点", clearAfterBake);

        if (EditorGUI.EndChangeCheck())
        {
            SyncActiveLineShapeAndVisual(false);
            SceneView.RepaintAll();
        }

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);

        if (GUILayout.Button(sceneMode ? "退出几何绘制模式" : "进入几何绘制模式", GUILayout.Height(28f)))
        {
            sceneMode = !sceneMode;
            SceneView.RepaintAll();
        }

        EditorGUILayout.LabelField("路径点数：" + pathPoints.Count);

        using (new EditorGUI.DisabledScope(pathPoints.Count < 2))
        {
            if (GUILayout.Button("生成 / 更新整条路径"))
                BakeCurrentPath(false);
        }

        using (new EditorGUI.DisabledScope(pathPoints.Count == 0))
        {
            if (GUILayout.Button("删除最后一点"))
                RemoveLastPoint();
            if (GUILayout.Button("清空路径点"))
                ClearPathPoints();
        }

        using (new EditorGUI.DisabledScope(pathUndo.Count == 0))
        {
            if (GUILayout.Button("撤销路径点 Ctrl+Z / Cmd+Z"))
                UndoPathPointEdit();
        }

        using (new EditorGUI.DisabledScope(activeLine == null || surfaceMaterial == null))
        {
            if (GUILayout.Button("将当前素材设置同步到当前线对象"))
                SyncActiveLineShapeAndVisual(true);
        }

        GUILayout.Space(8f);
        EditorGUILayout.HelpBox("Scene 操作：左键添加路径点；Shift+左键可直接生成直线段（曲线模式关闭时）；Enter 生成整条路径；Delete 删除最后一点；Ctrl/Cmd+Z 撤销路径点。", MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!sceneMode)
            return;

        Event e = Event.current;
        if (e == null)
            return;

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12, 12, 320, 86), GUI.skin.box);
        GUILayout.Label("Spline 几何绘制模式", EditorStyles.boldLabel);
        GUILayout.Label(surfaceMaterial != null ? "素材：" + GetDisplayName(surfaceMaterial) : "素材：未指定");
        GUILayout.Label(currentSplineTexture != null ? "贴图：" + currentSplineTexture.name : "贴图：None");
        GUILayout.Label("路径点：" + pathPoints.Count);
        GUILayout.EndArea();
        Handles.EndGUI();

        DrawScenePreview();

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(controlId);

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                sceneMode = false;
                e.Use();
                sceneView.Repaint();
                return;
            }

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                BakeCurrentPath(false);
                e.Use();
                sceneView.Repaint();
                return;
            }

            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                RemoveLastPoint();
                e.Use();
                sceneView.Repaint();
                return;
            }

            if ((e.control || e.command) && e.keyCode == KeyCode.Z)
            {
                UndoPathPointEdit();
                e.Use();
                sceneView.Repaint();
                return;
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (TryGetGroundPoint(e.mousePosition, out Vector3 point))
            {
                if (e.shift && shiftClickCreatesSegment && pathPoints.Count > 0 && !smoothPath)
                {
                    PushPathUndo();
                    Vector3 last = pathPoints[pathPoints.Count - 1];
                    BakeExplicitPoints(new List<Vector3> { last, point }, true);
                    pathPoints.Clear();
                    pathPoints.Add(point);
                }
                else
                {
                    PushPathUndo();
                    pathPoints.Add(point);
                }

                e.Use();
                Repaint();
                sceneView.Repaint();
            }
        }
    }

    private void DrawScenePreview()
    {
        if (pathPoints.Count == 0)
            return;

        Handles.color = Color.yellow;
        for (int i = 0; i < pathPoints.Count; i++)
        {
            Handles.SphereHandleCap(0, pathPoints[i], Quaternion.identity, Mathf.Max(0.08f, lineWidth * 0.25f), EventType.Repaint);
            if (i > 0)
                Handles.DrawLine(pathPoints[i - 1], pathPoints[i]);
        }

        if (smoothPath && pathPoints.Count >= 3)
        {
            List<Vector3> smoothed = BuildCatmullRom(pathPoints, smoothSubdivisions);
            Handles.color = Color.cyan;
            for (int i = 1; i < smoothed.Count; i++)
                Handles.DrawLine(smoothed[i - 1], smoothed[i]);
        }
    }

    private void BakeCurrentPath(bool keepPoints)
    {
        if (pathPoints.Count < 2)
            return;

        BakeExplicitPoints(BuildOutputPoints(), keepPoints);
        if (clearAfterBake && !keepPoints)
            pathPoints.Clear();

        Repaint();
        SceneView.RepaintAll();
    }

    private void BakeExplicitPoints(List<Vector3> pointsToBake, bool keepExistingActiveLine)
    {
        if (pointsToBake == null || pointsToBake.Count < 2)
            return;

        surfaceMaterial = ReloadSurfaceMaterialAsset(surfaceMaterial);
        currentSplineTexture = surfaceMaterial != null ? surfaceMaterial.splineTexture : currentSplineTexture;
        ApplySurfaceMaterialDefaults(lineWidth, opacity);

        SkyPrisonGroundSplineLine line = keepExistingActiveLine ? activeLine : null;
        if (line == null || (surfaceMaterial != null && line.sourceSurfaceMaterial != null && line.sourceSurfaceMaterial != surfaceMaterial))
            line = CreateLineObject();
        if (line == null)
            return;

        Undo.RegisterCompleteObjectUndo(line.gameObject, "Generate Ground Spline Line");
        Undo.RegisterCompleteObjectUndo(line, "Generate Ground Spline Line");

        line.sourceSurfaceMaterial = surfaceMaterial;
        line.syncVisualFromSourceMaterial = true;
        line.syncShapeFromSourceMaterial = true;
        line.surfaceId = surfaceMaterial != null ? surfaceMaterial.surfaceId : "";
        line.displayName = surfaceMaterial != null ? GetDisplayName(surfaceMaterial) : "GroundSpline";
        line.width = Mathf.Max(0.01f, lineWidth);
        line.opacity = Mathf.Clamp01(opacity);
        line.followTerrain = followTerrain;
        line.terrainYOffset = yOffset;
        line.clipToMapBounds = clampToMapBounds;
        line.mapBoundsClipPadding = mapBoundsClipPadding;
        line.dashed = dashed;
        line.dashLength = Mathf.Max(0.01f, dashLength);
        line.dashGap = Mathf.Max(0f, dashGap);
        line.dashPhase = Mathf.Max(0f, dashPhase);
        line.targetTerrain = targetTerrain;
        line.lineTexture = currentSplineTexture;
        line.lineTint = Color.white;
        line.textureWorldLength = surfaceMaterial != null ? Mathf.Max(0.01f, surfaceMaterial.splineSegmentWorldLength) : 1f;
        ApplySplineMaskReferenceToLine(line, surfaceMaterial);
        line.lineMaterial = null; // Force a clean material path; old white/yellow material state must not leak.
        line.SetPoints(pointsToBake);

        EditorUtility.SetDirty(line);
        EditorUtility.SetDirty(line.gameObject);
        activeLine = line;
        Selection.activeGameObject = line.gameObject;
    }

    private void SyncActiveLineShapeAndVisual(bool forceRebuild)
    {
        if (activeLine == null || surfaceMaterial == null)
            return;

        surfaceMaterial = ReloadSurfaceMaterialAsset(surfaceMaterial);
        currentSplineTexture = surfaceMaterial != null ? surfaceMaterial.splineTexture : currentSplineTexture;
        ApplySurfaceMaterialDefaults(lineWidth, opacity);

        Undo.RegisterCompleteObjectUndo(activeLine.gameObject, "Sync Ground Spline Material");
        Undo.RegisterCompleteObjectUndo(activeLine, "Sync Ground Spline Material");

        activeLine.sourceSurfaceMaterial = surfaceMaterial;
        activeLine.syncVisualFromSourceMaterial = true;
        activeLine.syncShapeFromSourceMaterial = true;
        activeLine.lineTexture = currentSplineTexture;
        activeLine.lineTint = Color.white;
        activeLine.lineMaterial = null;
        activeLine.width = Mathf.Max(0.01f, lineWidth);
        activeLine.opacity = Mathf.Clamp01(opacity);
        activeLine.followTerrain = followTerrain;
        activeLine.terrainYOffset = yOffset;
        activeLine.clipToMapBounds = clampToMapBounds;
        activeLine.mapBoundsClipPadding = mapBoundsClipPadding;
        activeLine.dashed = dashed;
        activeLine.dashLength = dashLength;
        activeLine.dashGap = dashGap;
        activeLine.dashPhase = dashPhase;
        activeLine.targetTerrain = targetTerrain;
        ApplySplineMaskReferenceToLine(activeLine, surfaceMaterial);

        if (forceRebuild)
            activeLine.Rebuild();

        EditorUtility.SetDirty(activeLine);
        EditorUtility.SetDirty(activeLine.gameObject);
        SceneView.RepaintAll();
    }

    private List<Vector3> BuildOutputPoints()
    {
        if (!smoothPath || pathPoints.Count < 3)
            return new List<Vector3>(pathPoints);
        return BuildCatmullRom(pathPoints, smoothSubdivisions);
    }

    private static List<Vector3> BuildCatmullRom(IList<Vector3> source, int subdivisions)
    {
        List<Vector3> result = new List<Vector3>();
        if (source == null || source.Count == 0)
            return result;
        if (source.Count < 3)
        {
            result.AddRange(source);
            return result;
        }

        int sub = Mathf.Clamp(subdivisions, 2, 32);
        for (int i = 0; i < source.Count - 1; i++)
        {
            Vector3 p0 = source[Mathf.Max(i - 1, 0)];
            Vector3 p1 = source[i];
            Vector3 p2 = source[i + 1];
            Vector3 p3 = source[Mathf.Min(i + 2, source.Count - 1)];
            for (int s = 0; s < sub; s++)
            {
                float t = s / (float)sub;
                result.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        result.Add(source[source.Count - 1]);
        return result;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private SkyPrisonGroundSplineLine CreateLineObject()
    {
        GameObject root = EnsureGroundSplinesRoot();
        string baseName = surfaceMaterial != null ? GetDisplayName(surfaceMaterial) : "GroundSpline";
        GameObject go = new GameObject(ObjectNames.NicifyVariableName(baseName));
        go.transform.SetParent(root.transform, false);
        int worldLayer = LayerMask.NameToLayer("World3D");
        if (worldLayer >= 0)
            go.layer = worldLayer;
        var line = Undo.AddComponent<SkyPrisonGroundSplineLine>(go);
        Undo.RegisterCreatedObjectUndo(go, "Create Ground Spline Line");
        return line;
    }

    private GameObject EnsureGroundSplinesRoot()
    {
        GameObject groundRoot = GameObject.Find("WorldRoot/GroundRoot");
        if (groundRoot == null)
        {
            GameObject worldRoot = GameObject.Find("WorldRoot") ?? new GameObject("WorldRoot");
            groundRoot = new GameObject("GroundRoot");
            Undo.RegisterCreatedObjectUndo(groundRoot, "Create GroundRoot");
            groundRoot.transform.SetParent(worldRoot.transform, false);
        }

        Transform child = groundRoot.transform.Find("GroundSplines");
        if (child != null)
            return child.gameObject;

        GameObject root = new GameObject("GroundSplines");
        Undo.RegisterCreatedObjectUndo(root, "Create GroundSplines Root");
        root.transform.SetParent(groundRoot.transform, false);
        int worldLayer = LayerMask.NameToLayer("World3D");
        if (worldLayer >= 0)
            root.layer = worldLayer;
        return root;
    }

    private bool TryGetGroundPoint(Vector2 mousePosition, out Vector3 world)
    {
        world = default;
        RefreshTarget();

        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

        if (targetTerrain != null)
        {
            TerrainCollider terrainCollider = targetTerrain.GetComponent<TerrainCollider>();
            if (terrainCollider != null && terrainCollider.Raycast(ray, out RaycastHit terrainHit, 10000f))
            {
                world = terrainHit.point;
                return ApplyMapBoundsRestriction(ref world);
            }
        }

        if (Physics.Raycast(ray, out RaycastHit hit, 10000f))
        {
            world = hit.point;
            return ApplyMapBoundsRestriction(ref world);
        }

        if (targetTerrain != null)
        {
            Plane plane = new Plane(Vector3.up, targetTerrain.transform.position);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 p = ray.GetPoint(enter);
                float y = targetTerrain.transform.position.y + targetTerrain.SampleHeight(p);
                world = new Vector3(p.x, y, p.z);
                return ApplyMapBoundsRestriction(ref world);
            }
        }

        return false;
    }

    private bool ApplyMapBoundsRestriction(ref Vector3 world)
    {
        // 这里故意不再阻止 / 夹住鼠标输入点。
        // 样条线应该允许用户从地图内画到地图外，最后由 SkyPrisonGroundSplineLine 在 Mesh 生成层
        // 按 MapBounds 对“带宽后的实际多边形”做干净裁切。
        // 如果在输入层 clamp，斜线会被提前拉回边界，端角和路径手感都会很怪。
        if (targetTerrain != null)
            world.y = targetTerrain.transform.position.y + targetTerrain.SampleHeight(world) + yOffset;
        return true;
    }

    private bool TryResolveMapClipBounds(out Bounds bounds)
    {
        // 先用 Terrain 范围作为可靠兜底。MapBounds 空节点不能用 scale=1 裁剪，
        // 否则点击点会被夹到一个 1m 小盒子里，生成结果自然看不见。
        Bounds terrainBounds = default;
        bool hasTerrainBounds = false;

        if (targetTerrain == null)
            targetTerrain = Terrain.activeTerrain;

        if (targetTerrain != null && targetTerrain.terrainData != null)
        {
            Vector3 pos = targetTerrain.transform.position;
            Vector3 size = targetTerrain.terrainData.size;
            terrainBounds = new Bounds(pos + size * 0.5f, size);
            hasTerrainBounds = true;
        }

        // 最高优先级：使用真正的 SkyPrisonMapBounds 组件。
        // 它代表地图有效内边界；物理墙和 Terrain 范围都可能包含外侧灰区。
        if (TryGetSkyPrisonMapBoundsComponentBounds(hasTerrainBounds ? terrainBounds : (Bounds?)null, out Bounds mapComponentBounds)
            && IsUsableClipBounds(mapComponentBounds, hasTerrainBounds ? terrainBounds : (Bounds?)null))
        {
            bounds = mapComponentBounds;
            return true;
        }

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
        if (candidate.size.x < 2f || candidate.size.z < 2f)
            return false;

        if (!terrainBounds.HasValue)
            return true;

        Bounds terrain = terrainBounds.Value;
        bool intersectsXZ = candidate.max.x >= terrain.min.x
            && candidate.min.x <= terrain.max.x
            && candidate.max.z >= terrain.min.z
            && candidate.min.z <= terrain.max.z;

        return intersectsXZ;
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
            // 普通空节点 scale=1 绝不能当作 MapBounds。
            if (Mathf.Abs(scale.x) >= 2f && Mathf.Abs(scale.z) >= 2f)
            {
                bounds = new Bounds(root.transform.position, new Vector3(Mathf.Abs(scale.x), 1f, Mathf.Abs(scale.z)));
                has = true;
            }
        }

        return has && bounds.size.x >= 2f && bounds.size.z >= 2f;
    }

    private void PushPathUndo()
    {
        pathUndo.Add(new List<Vector3>(pathPoints));
        while (pathUndo.Count > MaxPathUndo)
            pathUndo.RemoveAt(0);
    }

    private void UndoPathPointEdit()
    {
        if (pathUndo.Count == 0)
            return;
        int last = pathUndo.Count - 1;
        pathPoints.Clear();
        pathPoints.AddRange(pathUndo[last]);
        pathUndo.RemoveAt(last);
        Repaint();
        SceneView.RepaintAll();
    }

    private void RemoveLastPoint()
    {
        if (pathPoints.Count == 0)
            return;
        PushPathUndo();
        pathPoints.RemoveAt(pathPoints.Count - 1);
        Repaint();
        SceneView.RepaintAll();
    }

    private void ClearPathPoints()
    {
        if (pathPoints.Count == 0)
            return;
        PushPathUndo();
        pathPoints.Clear();
        Repaint();
        SceneView.RepaintAll();
    }

    private void RefreshTarget(bool force = false)
    {
        if (force)
            targetTerrain = null;

        if (targetTerrain == null)
        {
            GameObject go = GameObject.Find("WorldRoot/GroundRoot/GroundTerrain");
            if (go != null)
                targetTerrain = go.GetComponent<Terrain>();
            if (targetTerrain == null)
                targetTerrain = Terrain.activeTerrain;
        }
    }

    private void DrawSplineMaskReferencePreview()
    {
        if (surfaceMaterial == null)
            return;

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("蒙版参照（来自样条素材定义）", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Toggle("启用蒙版", surfaceMaterial.splineMaskEnabled);
            EditorGUILayout.ObjectField("蒙版纹理", surfaceMaterial.splineMaskTexture, typeof(Texture2D), false);
            EditorGUILayout.Slider("蒙版强度", surfaceMaterial.splineMaskStrength, 0f, 1f);
            EditorGUILayout.Slider("蒙版阈值", surfaceMaterial.splineMaskThreshold, 0f, 1f);
            EditorGUILayout.Slider("蒙版软边", surfaceMaterial.splineMaskSoftness, 0.001f, 0.5f);
            EditorGUILayout.FloatField("蒙版世界尺寸", surfaceMaterial.splineMaskWorldSize);
            EditorGUILayout.Toggle("反转蒙版", surfaceMaterial.splineMaskInvert);
            EditorGUILayout.Vector2Field("蒙版偏移", surfaceMaterial.splineMaskOffset);
        }

        EditorGUILayout.HelpBox("这里是生成前参照，只读取样条素材定义里的蒙版设置。若要修改默认蒙版，请回到地表材质定义页。", MessageType.None);
    }

    private static void ApplySplineMaskReferenceToLine(SkyPrisonGroundSplineLine line, GroundSurfaceMaterialDefinition material)
    {
        if (line == null)
            return;

        bool enabled = material != null && material.splineMaskEnabled;
        Texture2D texture = enabled && material != null ? material.splineMaskTexture : null;
        float strength = material != null ? Mathf.Clamp01(material.splineMaskStrength) : 0f;
        float threshold = material != null ? Mathf.Clamp01(material.splineMaskThreshold) : 0.45f;
        float softness = material != null ? Mathf.Clamp(material.splineMaskSoftness, 0.001f, 0.5f) : 0.08f;
        float worldSize = material != null ? Mathf.Max(0.01f, material.splineMaskWorldSize) : 3f;
        bool invert = material != null && material.splineMaskInvert;
        Vector2 offset = material != null ? material.splineMaskOffset : Vector2.zero;

        // Compile-safe bridge: the renderer component may already have these fields in newer versions.
        // If the current SkyPrisonGroundSplineLine does not yet define them, this does nothing instead of breaking compilation.
        SerializedObject so = new SerializedObject(line);

        SetSerializedBool(so, "splineMaskEnabled", enabled);
        SetSerializedObject(so, "splineMaskTexture", texture);
        SetSerializedFloat(so, "splineMaskStrength", strength);
        SetSerializedFloat(so, "splineMaskThreshold", threshold);
        SetSerializedFloat(so, "splineMaskSoftness", softness);
        SetSerializedFloat(so, "splineMaskWorldSize", worldSize);
        SetSerializedBool(so, "splineMaskInvert", invert);
        SetSerializedVector2(so, "splineMaskOffset", offset);

        // Compatibility aliases for possible renderer-side naming.
        SetSerializedBool(so, "damageMaskEnabled", enabled);
        SetSerializedObject(so, "damageMaskTexture", texture);
        SetSerializedFloat(so, "damageMaskStrength", strength);
        SetSerializedFloat(so, "damageMaskThreshold", threshold);
        SetSerializedFloat(so, "damageMaskSoftness", softness);
        SetSerializedFloat(so, "damageMaskWorldSize", worldSize);
        SetSerializedBool(so, "damageMaskInvert", invert);
        SetSerializedVector2(so, "damageMaskOffset", offset);

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedBool(SerializedObject so, string propertyName, bool value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.Boolean)
            prop.boolValue = value;
    }

    private static void SetSerializedFloat(SerializedObject so, string propertyName, float value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.Float)
            prop.floatValue = value;
    }

    private static void SetSerializedObject(SerializedObject so, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
            prop.objectReferenceValue = value;
    }

    private static void SetSerializedVector2(SerializedObject so, string propertyName, Vector2 value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.Vector2)
            prop.vector2Value = value;
    }

    private void ApplySurfaceMaterialDefaults(float fallbackWidth, float fallbackAlpha)
    {
        if (surfaceMaterial == null)
        {
            lineWidth = Mathf.Max(0.01f, fallbackWidth);
            opacity = Mathf.Clamp01(fallbackAlpha);
            currentSplineTexture = null;
            return;
        }

        float resolvedWidth = surfaceMaterial.EffectiveFixedSplineWorldWidth;
        if (resolvedWidth <= 0.001f)
            resolvedWidth = surfaceMaterial.splineWorldWidth;
        if (resolvedWidth <= 0.001f)
            resolvedWidth = fallbackWidth;

        lineWidth = Mathf.Max(0.01f, resolvedWidth);
        opacity = Mathf.Clamp01(surfaceMaterial.splineOpacity);
        currentSplineTexture = surfaceMaterial.splineTexture;
    }

    private static GroundSurfaceMaterialDefinition ReloadSurfaceMaterialAsset(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return null;
        string path = AssetDatabase.GetAssetPath(material);
        if (string.IsNullOrEmpty(path))
            return material;
        GroundSurfaceMaterialDefinition reloaded = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
        return reloaded != null ? reloaded : material;
    }

    private static string GetDisplayName(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return "GroundSpline";
        if (!string.IsNullOrWhiteSpace(material.displayName))
            return material.displayName;
        if (!string.IsNullOrWhiteSpace(material.name))
            return material.name;
        return "GroundSpline";
    }
}
