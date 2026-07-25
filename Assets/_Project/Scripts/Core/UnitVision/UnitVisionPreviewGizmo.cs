using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class UnitVisionPreviewGizmo : MonoBehaviour
{
    [SerializeField, HideInInspector] private string scriptVersion = "V2 - 2026-05-22 - runtime suppressed cached editor preview";
    public enum VisionPreviewOriginMode
    {
        AutoFromAnchorOrRenderer = 0,
        TransformPosition = 1,
        CustomTransform = 2,
    }

    public enum VisionPreviewDisplayMode
    {
        Standard = 0,
        Lightweight = 1,
        Debug = 2,
    }

    public enum VisionFacingMode
    {
        UseHorizontalLeftRight = 0,
        Use3DForward = 1,
    }

    [Header("Definition Source")]
    public UnitDefinition manualOverrideDefinition;
    [SerializeField] private UnitDefinition resolvedDefinition;
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;

    [Header("Preview Origin")]
    public VisionPreviewOriginMode originMode = VisionPreviewOriginMode.AutoFromAnchorOrRenderer;
    [Tooltip("推荐挂到胸口或上半身。")]
    public Transform visionOriginAnchor;
    public Transform customOriginTransform;
    public Vector3 visionOriginOffset = new Vector3(0f, 0.15f, 0f);
    [Range(0f, 1f)] public float autoBoundsHeightRatio = 0.68f;
    public float fallbackTransformHeight = 1.2f;

    [Header("Facing")]
    public VisionFacingMode facingMode = VisionFacingMode.UseHorizontalLeftRight;
    [Tooltip("手动指定真正负责 Spine 左右翻面的节点。为空时自动查找。")]
    public Transform facingSource;
    [Tooltip("自动查找朝向节点。建议开启。")]
    public bool autoResolveFacingSource = true;
    [Tooltip("手动强制反转横版朝向。")]
    public bool invertHorizontalFacing = false;
    [Tooltip("敌对单位默认自动反转横版朝向。")]
    public bool autoInvertForHostileIdentity = true;
    [Tooltip("完全取不到朝向时的默认值。")]
    public bool defaultFacingRight = true;

    [SerializeField] private Transform resolvedFacingSource;
    [SerializeField] private string resolvedFacingSourcePath = string.Empty;
    [SerializeField] private bool resolvedHorizontalFacingRight = true;
    [SerializeField] private bool resolvedInvertHorizontalFacing = false;

    [Header("Display Mode")]
    public VisionPreviewDisplayMode displayMode = VisionPreviewDisplayMode.Standard;

    [Header("Draw Options")]
    public bool drawWhenNotSelected = true;
    public bool drawOnlyWhenEnabledVision = true;
    public bool drawLabelWhenSelected = true;
    public bool drawMiniLabelWhenUnselected = false;
    public bool drawSolidArea = true;
    public bool drawWireOutline = true;
    public bool drawForwardLineWhenSelected = true;
    public bool drawOriginMarkerWhenSelected = true;
    public bool drawVerticalGuideWhenSelected = false;

    [Header("Visual Tuning")]
    [Range(0.01f, 0.6f)] public float playerSolidAlpha = 0.08f;
    [Range(0.01f, 0.6f)] public float hostileSolidAlpha = 0.14f;
    [Range(0.5f, 6.0f)] public float outlineThickness = 2.0f;
    [Range(0.02f, 0.5f)] public float originMarkerSize = 0.08f;
    [Range(0.1f, 2.0f)] public float labelHeightOffset = 0.32f;

    [Header("Gizmo Style")]
    public Color playerCircleColor = new Color(0.20f, 0.95f, 0.35f, 1f);
    public Color allyCircleColor = new Color(0.20f, 0.80f, 1.0f, 1f);
    public Color hostileSectorColor = new Color(1f, 0.55f, 0.15f, 1f);
    public Color neutralColor = new Color(0.90f, 0.90f, 0.90f, 1f);
    public Color fogImmuneTint = new Color(1f, 0.92f, 0.30f, 1f);
    public Color guideLineColor = new Color(1f, 1f, 1f, 0.85f);
    public Color originMarkerColor = new Color(1f, 0.95f, 0.15f, 1f);
    public Color textColor = Color.white;

    [Header("Shape Detail")]
    [Range(16, 256)] public int circleSegments = 64;
    [Range(12, 128)] public int sectorSegments = 40;

    [Header("Auto Resolve")]
    public bool autoRefreshBinding = true;

    [Header("Runtime / Performance Guard")]
    [Tooltip("正式运行时关闭这个编辑器视野预览。它只应该服务 Scene 视图调试，不应该进入 Play 热路径。")]
    public bool disableGizmoInPlayMode = true;
    [Tooltip("绘制时是否重新解析 UnitDefinition / FacingSource。默认关闭，避免 OnDrawGizmos 每帧扫描单位层级。需要刷新时用右键菜单或重新启用组件。")]
    public bool refreshBindingWhileDrawing = false;
    [Tooltip("允许绘制时刷新绑定时的最小间隔。只在 refreshBindingWhileDrawing=true 时生效。")]
    [Range(0.1f, 5f)] public float drawBindingRefreshInterval = 0.5f;

    [SerializeField, HideInInspector] private bool runtimeSuppressed = false;

    private double nextAllowedDrawBindingRefreshTime = 0.0;
    private Renderer[] cachedOriginRenderers;
    private bool cachedOriginRendererListDirty = true;

    public UnitDefinition ResolvedDefinition => resolvedDefinition;
    public UnitDefinitionRuntimeBinder RuntimeBinder => runtimeBinder;
    public bool HasResolvedDefinition => resolvedDefinition != null;
    public Transform ResolvedFacingSource => resolvedFacingSource;

    public bool IsCharacterVisionAvailable
    {
        get
        {
            if (resolvedDefinition == null) return false;
            if (resolvedDefinition.defineType != UnitDefineType.Character) return false;
            if (drawOnlyWhenEnabledVision && !resolvedDefinition.enableVision) return false;
            return true;
        }
    }

    public bool IsPlayerVision =>
        resolvedDefinition != null &&
        resolvedDefinition.defineType == UnitDefineType.Character &&
        resolvedDefinition.characterIdentity == CharacterIdentity.Player;

    public bool IsHostileIdentity
    {
        get
        {
            if (resolvedDefinition == null)
                return false;

            switch (resolvedDefinition.characterIdentity)
            {
                case CharacterIdentity.Enemy:
                case CharacterIdentity.Elite:
                case CharacterIdentity.Boss:
                case CharacterIdentity.NeutralHostile:
                    return true;
                default:
                    return false;
            }
        }
    }

    public bool IsFogImmune => resolvedDefinition != null && resolvedDefinition.ignoreFogOfWar;
    public float CurrentVisionRadius => resolvedDefinition != null ? resolvedDefinition.visionRadius : 0f;

    public float CurrentVisionAngle
    {
        get
        {
            if (resolvedDefinition == null) return 0f;
            if (IsPlayerVision) return 360f;
            return resolvedDefinition.visionShape == UnitVisionShape.Circle
                ? 360f
                : Mathf.Clamp(resolvedDefinition.visionAngle, 1f, 360f);
        }
    }

    public bool CurrentUseFacingDirection
    {
        get
        {
            if (resolvedDefinition == null) return false;
            if (IsPlayerVision) return false;
            return resolvedDefinition.useFacingDirection;
        }
    }

    public bool CurrentUseCircle
    {
        get
        {
            if (resolvedDefinition == null) return true;
            if (IsPlayerVision) return true;
            return resolvedDefinition.visionShape == UnitVisionShape.Circle;
        }
    }

    private void OnEnable()
    {
        runtimeSuppressed = Application.isPlaying && disableGizmoInPlayMode;
        cachedOriginRendererListDirty = true;

        if (runtimeSuppressed)
            return;

        RefreshBindingsOnce();
    }

    private void OnValidate()
    {
        cachedOriginRendererListDirty = true;

        if (Application.isPlaying && disableGizmoInPlayMode)
            return;

        RefreshBindingsOnce();
    }

    private void OnDrawGizmos()
    {
        if (!drawWhenNotSelected)
            return;

        if (ShouldSkipGizmoDrawing())
            return;

        DrawVisionGizmo(false);
    }

    private void OnDrawGizmosSelected()
    {
        if (ShouldSkipGizmoDrawing())
            return;

        DrawVisionGizmo(true);
    }

    private bool ShouldSkipGizmoDrawing()
    {
        runtimeSuppressed = Application.isPlaying && disableGizmoInPlayMode;
        return runtimeSuppressed;
    }

    private void RefreshBindingsOnce()
    {
        if (autoRefreshBinding)
            RefreshDefinitionBinding();

        RefreshFacingSourceBinding();
        cachedOriginRendererListDirty = true;
    }

    [ContextMenu("Refresh Vision Preview Binding")]
    public void RefreshVisionPreviewBindingFromContextMenu()
    {
        RefreshBindingsOnce();
    }

    public void RefreshDefinitionBinding()
    {
        runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();
        resolvedDefinition = manualOverrideDefinition;

        if (runtimeBinder != null && runtimeBinder.UnitDefinitionAsset != null)
            resolvedDefinition = runtimeBinder.UnitDefinitionAsset;
    }

    public void RefreshFacingSourceBinding()
    {
        resolvedFacingSource = ResolveFacingSourceInternal();
        resolvedFacingSourcePath = resolvedFacingSource != null ? BuildRelativePath(transform, resolvedFacingSource) : "(self)";
    }

    public Vector3 GetResolvedVisionOriginWorld()
    {
        switch (originMode)
        {
            case VisionPreviewOriginMode.TransformPosition:
                return transform.position + Vector3.up * fallbackTransformHeight + visionOriginOffset;

            case VisionPreviewOriginMode.CustomTransform:
                if (customOriginTransform != null)
                    return customOriginTransform.position + visionOriginOffset;
                return transform.position + Vector3.up * fallbackTransformHeight + visionOriginOffset;
        }

        if (visionOriginAnchor != null)
            return visionOriginAnchor.position + visionOriginOffset;

        Renderer[] renderers = GetCachedOriginRenderers();
        if (renderers != null && renderers.Length > 0)
        {
            bool hasBounds = false;
            Bounds bounds = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                return new Vector3(
                    bounds.center.x,
                    bounds.min.y + bounds.size.y * autoBoundsHeightRatio,
                    bounds.center.z) + visionOriginOffset;
            }
        }

        return transform.position + Vector3.up * fallbackTransformHeight + visionOriginOffset;
    }

    public Vector3 GetResolvedForward()
    {
        if (resolvedFacingSource == null && autoResolveFacingSource)
            RefreshFacingSourceBinding();

        if (facingMode == VisionFacingMode.UseHorizontalLeftRight)
        {
            bool facingRight = ResolveHorizontalFacingRight();
            resolvedHorizontalFacingRight = facingRight;
            return facingRight ? Vector3.right : Vector3.left;
        }

        Transform source = resolvedFacingSource != null ? resolvedFacingSource : transform;
        return source.forward.normalized;
    }

    private bool ResolveHorizontalFacingRight()
    {
        Transform source = resolvedFacingSource != null ? resolvedFacingSource : transform;

        bool facingRight;
        if (Mathf.Abs(source.localScale.x) > 0.0001f)
            facingRight = source.localScale.x > 0f;
        else if (Mathf.Abs(source.lossyScale.x) > 0.0001f)
            facingRight = source.lossyScale.x > 0f;
        else
            facingRight = defaultFacingRight;

        bool shouldInvert = invertHorizontalFacing || (autoInvertForHostileIdentity && IsHostileIdentity);
        resolvedInvertHorizontalFacing = shouldInvert;

        if (shouldInvert)
            facingRight = !facingRight;

        return facingRight;
    }

    private Transform ResolveFacingSourceInternal()
    {
        if (facingSource != null)
            return facingSource;

        if (!autoResolveFacingSource)
            return transform;

        string[] highPriorityKeywords =
        {
            "VisualRoot", "visualroot",
            "Spine GameObject", "SpineGameObject", "spinegameobject",
            "Spine", "spine",
            "Skeleton", "skeleton",
            "Model", "model",
            "Graphics", "graphics",
            "Render", "render"
        };

        Transform[] all = GetComponentsInChildren<Transform>(true);
        Transform bestByName = null;
        int bestNameScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == transform)
                continue;

            int score = ScoreTransformName(t.name, highPriorityKeywords);
            if (score > bestNameScore)
            {
                bestNameScore = score;
                bestByName = t;
            }
        }

        if (bestByName != null && bestNameScore > 0)
            return bestByName;

        Transform bestHeuristic = null;
        int bestHeuristicScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == transform)
                continue;

            int score = 0;

            if (t.GetComponent<Renderer>() != null)
                score += 30;

            if (t.childCount > 0)
                score += 5;

            int depthPenalty = GetDepthRelativeToRoot(transform, t);
            score -= depthPenalty * 2;

            string name = t.name ?? string.Empty;
            if (name.IndexOf("visual", StringComparison.OrdinalIgnoreCase) >= 0) score += 24;
            if (name.IndexOf("spine", StringComparison.OrdinalIgnoreCase) >= 0) score += 20;
            if (name.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0) score += 12;
            if (name.IndexOf("render", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;

            if (score > bestHeuristicScore)
            {
                bestHeuristicScore = score;
                bestHeuristic = t;
            }
        }

        if (bestHeuristic != null)
            return bestHeuristic;

        return transform;
    }

    private static int ScoreTransformName(string name, string[] keywords)
    {
        if (string.IsNullOrEmpty(name))
            return 0;

        int score = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (name.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                score += 100 - i;
        }

        return score;
    }

    private static int GetDepthRelativeToRoot(Transform root, Transform node)
    {
        int depth = 0;
        Transform current = node;
        while (current != null && current != root)
        {
            depth++;
            current = current.parent;
        }
        return depth;
    }

    private static string BuildRelativePath(Transform root, Transform node)
    {
        if (root == null || node == null)
            return string.Empty;

        if (node == root)
            return root.name;

        string path = node.name;
        Transform current = node.parent;
        int guard = 0;

        while (current != null && current != root && guard++ < 64)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }


    private void RefreshBindingsForDrawIfNeeded()
    {
        if (!refreshBindingWhileDrawing)
            return;

#if UNITY_EDITOR
        double now = EditorApplication.timeSinceStartup;
#else
        double now = Time.realtimeSinceStartupAsDouble;
#endif
        if (now < nextAllowedDrawBindingRefreshTime)
            return;

        nextAllowedDrawBindingRefreshTime = now + Mathf.Max(0.1f, drawBindingRefreshInterval);
        RefreshBindingsOnce();
    }

    private Renderer[] GetCachedOriginRenderers()
    {
        if (cachedOriginRendererListDirty || cachedOriginRenderers == null)
        {
            cachedOriginRenderers = GetComponentsInChildren<Renderer>(true);
            cachedOriginRendererListDirty = false;
        }

        return cachedOriginRenderers;
    }

    public Color GetResolvedMainColor()
    {
        if (resolvedDefinition == null)
            return neutralColor;

        Color baseColor;
        switch (resolvedDefinition.characterIdentity)
        {
            case CharacterIdentity.Player:
                baseColor = playerCircleColor;
                break;
            case CharacterIdentity.Enemy:
            case CharacterIdentity.Elite:
            case CharacterIdentity.Boss:
            case CharacterIdentity.NeutralHostile:
                baseColor = hostileSectorColor;
                break;
            case CharacterIdentity.Ally:
                baseColor = allyCircleColor;
                break;
            default:
                baseColor = neutralColor;
                break;
        }

        if (resolvedDefinition.ignoreFogOfWar)
            baseColor = Color.Lerp(baseColor, fogImmuneTint, 0.45f);

        return baseColor;
    }

    private void DrawVisionGizmo(bool selected)
    {
        RefreshBindingsForDrawIfNeeded();

        if (!IsCharacterVisionAvailable)
            return;

        Vector3 origin = GetResolvedVisionOriginWorld();
        Vector3 forward = GetResolvedForward();
        bool useCircle = CurrentUseCircle;
        float radius = Mathf.Max(0f, CurrentVisionRadius);
        float angle = CurrentVisionAngle;
        Color mainColor = GetResolvedMainColor();

#if UNITY_EDITOR
        if (drawSolidArea)
        {
            Color solid = mainColor;
            solid.a = GetSolidAlpha();
            Handles.color = solid;

            if (useCircle)
                Handles.DrawSolidDisc(origin, Vector3.up, radius);
            else
                DrawSolidSector(origin, forward, radius, angle, sectorSegments);
        }

        if (drawWireOutline)
        {
            Handles.color = mainColor;
            if (useCircle)
                DrawThickCircle(origin, radius, circleSegments, outlineThickness);
            else
                DrawThickSector(origin, forward, radius, angle, sectorSegments, outlineThickness);
        }

        if (selected && drawOriginMarkerWhenSelected)
            DrawOriginMarker(origin, mainColor);

        if (selected)
            DrawExtraGuides(origin, forward, useCircle, radius);

        if (selected && drawLabelWhenSelected)
            DrawLabel(origin, useCircle, angle, radius, mainColor, false);
        else if (!selected && drawMiniLabelWhenUnselected)
            DrawLabel(origin, useCircle, angle, radius, mainColor, true);
#endif
    }

    private float GetSolidAlpha()
    {
        switch (displayMode)
        {
            case VisionPreviewDisplayMode.Lightweight:
                return Mathf.Min(playerSolidAlpha, hostileSolidAlpha) * 0.45f;
            case VisionPreviewDisplayMode.Debug:
                return Mathf.Max(playerSolidAlpha, hostileSolidAlpha) * 1.5f;
            default:
                return CurrentUseCircle ? playerSolidAlpha : hostileSolidAlpha;
        }
    }

#if UNITY_EDITOR
    private void DrawSolidSector(Vector3 center, Vector3 forward, float radius, float angleDeg, int segments)
    {
        int seg = Mathf.Max(8, segments);
        float half = angleDeg * 0.5f;
        Vector3[] verts = new Vector3[seg + 2];
        verts[0] = center;

        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;
            float currentAngle = Mathf.Lerp(-half, half, t);
            Vector3 dir = Quaternion.AngleAxis(currentAngle, Vector3.up) * forward;
            verts[i + 1] = center + dir * radius;
        }

        Handles.DrawAAConvexPolygon(verts);
    }

    private void DrawThickCircle(Vector3 center, float radius, int segments, float thickness)
    {
        int seg = Mathf.Max(16, segments);
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= seg; i++)
        {
            float t = i / (float)seg;
            float rad = t * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
            Handles.DrawAAPolyLine(thickness, prev, next);
            prev = next;
        }
    }

    private void DrawThickSector(Vector3 center, Vector3 forward, float radius, float angleDeg, int segments, float thickness)
    {
        int seg = Mathf.Max(12, segments);
        float half = angleDeg * 0.5f;
        Quaternion leftRot = Quaternion.AngleAxis(-half, Vector3.up);
        Quaternion rightRot = Quaternion.AngleAxis(half, Vector3.up);

        Vector3 leftDir = leftRot * forward;
        Vector3 rightDir = rightRot * forward;

        Handles.DrawAAPolyLine(thickness, center, center + leftDir * radius);
        Handles.DrawAAPolyLine(thickness, center, center + rightDir * radius);

        Vector3 prev = center + leftDir * radius;
        for (int i = 1; i <= seg; i++)
        {
            float t = i / (float)seg;
            float currentAngle = Mathf.Lerp(-half, half, t);
            Vector3 dir = Quaternion.AngleAxis(currentAngle, Vector3.up) * forward;
            Vector3 next = center + dir * radius;
            Handles.DrawAAPolyLine(thickness, prev, next);
            prev = next;
        }
    }

    private void DrawOriginMarker(Vector3 origin, Color mainColor)
    {
        float size = displayMode == VisionPreviewDisplayMode.Debug ? originMarkerSize * 1.4f : originMarkerSize;

        Handles.color = originMarkerColor;
        Handles.SphereHandleCap(0, origin, Quaternion.identity, size, EventType.Repaint);

        float cross = size * 1.6f;
        Handles.color = Color.Lerp(mainColor, Color.white, 0.35f);
        Handles.DrawAAPolyLine(outlineThickness, origin + Vector3.left * cross, origin + Vector3.right * cross);
        Handles.DrawAAPolyLine(outlineThickness, origin + Vector3.forward * cross, origin + Vector3.back * cross);
    }

    private void DrawExtraGuides(Vector3 origin, Vector3 forward, bool useCircle, float radius)
    {
        if (drawVerticalGuideWhenSelected)
        {
            Handles.color = guideLineColor;
            Handles.DrawDottedLine(transform.position, origin, 4f);
        }

        if (drawForwardLineWhenSelected && !useCircle)
        {
            Handles.color = guideLineColor;
            Handles.DrawAAPolyLine(outlineThickness, origin, origin + forward * Mathf.Max(0.6f, radius * 0.45f));
        }
    }

    private void DrawLabel(Vector3 origin, bool useCircle, float angle, float radius, Color mainColor, bool mini)
    {
        if (resolvedDefinition == null)
            return;

        string shapeText = useCircle ? "圆形" : "扇区";
        string text;

        if (mini)
        {
            text = $"视野 {shapeText} {radius:0.#}";
        }
        else
        {
            string facingText = (!useCircle && CurrentUseFacingDirection) ? "｜受朝向" : string.Empty;
            string fogText = resolvedDefinition.ignoreFogOfWar ? "｜免疫迷雾" : string.Empty;
            string shareText = resolvedDefinition.shareVisionToFaction ? "｜共享阵营" : string.Empty;
            string facingNodeText = resolvedFacingSource != null ? $"\n朝向源: {resolvedFacingSourcePath}" : string.Empty;
            string facingResolveText = !useCircle
                ? $"\n左右朝向: {(resolvedHorizontalFacingRight ? "右" : "左")}｜反转: {(resolvedInvertHorizontalFacing ? "开" : "关")}"
                : string.Empty;

            text = useCircle
                ? $"{resolvedDefinition.name}\n{shapeText}｜半径 {radius:0.##}{shareText}{fogText}{facingNodeText}"
                : $"{resolvedDefinition.name}\n{shapeText}｜半径 {radius:0.##}｜角度 {angle:0.#}{facingText}{shareText}{fogText}{facingNodeText}{facingResolveText}";
        }

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = textColor;
        style.fontSize = mini ? 10 : 11;
        style.alignment = TextAnchor.MiddleCenter;

        Vector3 labelPos = origin + Vector3.up * labelHeightOffset;

        Handles.BeginGUI();
        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(labelPos);
        float width = mini ? 120f : 290f;
        float height = mini ? 20f : 66f;
        Rect rect = new Rect(guiPoint.x - width * 0.5f, guiPoint.y - height, width, height);

        Color old = GUI.color;
        GUI.color = new Color(mainColor.r, mainColor.g, mainColor.b, mini ? 0.10f : 0.14f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = old;

        GUI.Label(rect, text, style);
        Handles.EndGUI();
    }
#endif
}
