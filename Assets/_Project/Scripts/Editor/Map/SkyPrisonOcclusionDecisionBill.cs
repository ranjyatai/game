#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonOcclusionDecisionBill
{
    private const string Version = "V1 - 2026-05-22 - trigger decision state bill";

    [MenuItem("Tools/Sky Prison/Map/遮挡诊断/复制选中遮挡物判定账单")]
    private static void CopySelectedOccluderDecisionBill()
    {
        Transform selected = Selection.activeTransform;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Sky Prison", "请先在 Hierarchy 里选中一个地形装饰物、RuleRoot、BackTrigger 或 FrontOccluderRoot。", "OK");
            return;
        }

        var trigger = FindTrigger(selected);
        if (trigger == null)
        {
            EditorUtility.DisplayDialog("Sky Prison", "选中对象附近没有找到 SkyPrisonTerrainDecorationFrontOccluderTrigger。", "OK");
            return;
        }

        string report = BuildReport(trigger);
        EditorGUIUtility.systemCopyBuffer = report;
        Debug.Log(report);
    }

    private static MonoBehaviour FindTrigger(Transform t)
    {
        Transform cur = t;
        for (int i = 0; cur != null && i < 16; i++, cur = cur.parent)
        {
            var mb = cur.GetComponent("SkyPrisonTerrainDecorationFrontOccluderTrigger") as MonoBehaviour;
            if (mb != null)
                return mb;

            mb = cur.GetComponentInChildren(TypeByName("SkyPrisonTerrainDecorationFrontOccluderTrigger"), true) as MonoBehaviour;
            if (mb != null)
                return mb;
        }

        return null;
    }

    private static Type TypeByName(string typeName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(typeName);
            if (t != null)
                return t;
        }
        return null;
    }

    private static string BuildReport(MonoBehaviour trigger)
    {
        var sb = new StringBuilder(32768);
        Type type = trigger.GetType();

        sb.AppendLine("==== SKY PRISON OCCLUSION DECISION BILL ====");
        sb.AppendLine(Version);
        sb.AppendLine($"Time={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} frame={Time.frameCount} playMode={Application.isPlaying}");
        sb.AppendLine($"Trigger={PathOf(trigger.transform)}");
        sb.AppendLine($"TriggerEnabled={trigger.enabled} activeSelf={trigger.gameObject.activeSelf} activeHierarchy={trigger.gameObject.activeInHierarchy}");
        sb.AppendLine();

        AppendPublicAndSerializedSettings(sb, trigger, type);
        AppendResolvedRefs(sb, trigger, type);
        AppendDebugFields(sb, trigger, type);
        AppendCandidateStates(sb, trigger, type);
        AppendCharacterOcclusionPoints(sb);
        AppendSceneReceivers(sb);
        AppendOccluderGeometry(sb, trigger, type);

        sb.AppendLine();
        sb.AppendLine("==== QUICK READ ====");
        sb.AppendLine("1. debugLastRejectReason / debugBehindVolumeRejectReason 能直接说明 clear 的原因。");
        sb.AppendLine("2. candidateStates=0 说明候选扫描没把 Player 纳入。");
        sb.AppendLine("3. candidate occluding=false 且 debugClearCount 高，通常是 BehindVolume 拒绝或 Ray 没打到当前物体。");
        sb.AppendLine("4. frontOccluderRoot.active=false 且 receiver current clear，说明问题在 Trigger 判定层，不在 Receiver 材质层。");
        sb.AppendLine("5. 如果 logicalAnchor 使用 UnitPhysicsProbe/UnitBody 但站位在边缘外，可能需要微调 behindVolumeSideMargin / frontMargin 或采样规则。");

        return sb.ToString();
    }

    private static void AppendPublicAndSerializedSettings(StringBuilder sb, MonoBehaviour trigger, Type type)
    {
        sb.AppendLine("==== KEY SETTINGS ====");
        string[] names =
        {
            "scriptVersion",
            "sourceCamera",
            "targetLayers",
            "raycastLayers",
            "useCameraAreaSweep",
            "useSelfColliderRayDepth",
            "useSelfVisualMeshTriangleRayDepth",
            "maxSelfVisualMeshTrianglesPerRenderer",
            "activeWakeScan",
            "useVisualRootBoundsForWakeScan",
            "visualRootWakeExtraPadding",
            "wakeScanInterval",
            "scanAllTargetLayerCollidersForDebug",
            "preferUnitMeshVertices",
            "maxUnitMeshSamplesPerRenderer",
            "alwaysAddRendererBoundsFootprintSamples",
            "forceRendererBoundsAsVisualFootprint",
            "includeInactiveUnitRenderers",
            "useNameExclusion",
            "useUnitLogicalDepthPlane",
            "useVisualWidthWithUnifiedUnitDepth",
            "useVisualSampleHeightForUnifiedDepth",
            "preferLogicalBodyCollider",
            "fallbackToUnitRootPosition",
            "requireUnitBodyInsideCameraFacingBehindVolume",
            "behindVolumeSideMargin",
            "behindVolumeFrontMargin",
            "behindVolumeHeightPadding",
            "useBehindVolumeHeightCheck",
            "useOverheadHeightOcclusion",
            "useMotionSweptOcclusion",
            "motionSweepMinDistance",
            "useMotionSweepMidpoint",
            "releaseHysteresisFrames",
            "notifyOcclusionStateReceivers"
        };

        foreach (string n in names)
            sb.AppendLine($"{n}={FormatValue(GetField(type, trigger, n))}");
        sb.AppendLine();
    }

    private static void AppendResolvedRefs(StringBuilder sb, MonoBehaviour trigger, Type type)
    {
        sb.AppendLine("==== RESOLVED ROOTS ====");
        string[] names =
        {
            "frontOccluderRoot",
            "occluderContentRoot",
            "occluderColliderRoot",
            "sourceCamera"
        };

        foreach (string n in names)
            sb.AppendLine($"{n}={FormatValue(GetField(type, trigger, n))}");
        sb.AppendLine();
    }

    private static void AppendDebugFields(StringBuilder sb, MonoBehaviour trigger, Type type)
    {
        sb.AppendLine("==== TRIGGER DEBUG FIELDS ====");
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (FieldInfo f in fields)
        {
            if (!f.Name.StartsWith("debug", StringComparison.Ordinal))
                continue;

            object v = SafeGet(f, trigger);
            sb.AppendLine($"{f.Name}={FormatValue(v)}");
        }
        sb.AppendLine();
    }

    private static void AppendCandidateStates(StringBuilder sb, MonoBehaviour trigger, Type type)
    {
        sb.AppendLine("==== CANDIDATE STATES ====");
        object candidateObj = GetField(type, trigger, "candidateStates");
        if (candidateObj == null)
        {
            sb.AppendLine("candidateStates=<null/unavailable>");
            sb.AppendLine();
            return;
        }

        var dict = candidateObj as IDictionary;
        if (dict == null)
        {
            sb.AppendLine($"candidateStates type={candidateObj.GetType().FullName} not IDictionary");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"candidateCount={dict.Count}");

        int index = 0;
        foreach (DictionaryEntry entry in dict)
        {
            Transform key = entry.Key as Transform;
            object state = entry.Value;
            Type st = state != null ? state.GetType() : null;
            sb.AppendLine($"[{index}] unitRoot={PathOf(key)} stateType={(st != null ? st.Name : "<null>")}");
            if (state != null && st != null)
            {
                string[] stateFields =
                {
                    "unitRoot", "sourceCollider", "occluding", "releaseGraceRemaining",
                    "hasLastLogicalAnchor", "lastLogicalAnchor"
                };

                foreach (string n in stateFields)
                    sb.AppendLine($"    {n}={FormatValue(GetField(st, state, n))}");

                object receiversObj = GetField(st, state, "receivers");
                var list = receiversObj as IList;
                if (list != null)
                {
                    sb.AppendLine($"    receivers={list.Count}");
                    for (int i = 0; i < list.Count; i++)
                    {
                        var mb = list[i] as MonoBehaviour;
                        sb.AppendLine($"      [{i}] {PathOf(mb != null ? mb.transform : null)} ({(mb != null ? mb.GetType().Name : "<null>")}) enabled={(mb != null && mb.enabled)}");
                    }
                }
            }
            index++;
        }
        sb.AppendLine();
    }

    private static void AppendCharacterOcclusionPoints(StringBuilder sb)
    {
        sb.AppendLine("==== CHARACTER OCCLUSION POINTS ====");
        var points = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < points.Length; i++)
        {
            MonoBehaviour mb = points[i];
            if (mb == null || mb.GetType().Name != "CharacterOcclusionPoints")
                continue;

            Type t = mb.GetType();
            count++;
            sb.AppendLine($"[{count}] {PathOf(mb.transform)}");
            foreach (string n in new[] { "footAnchor", "bodyAnchor", "headAnchor", "weaponTipAnchor" })
            {
                Transform tr = GetField(t, mb, n) as Transform;
                sb.AppendLine($"    {n}={PathOf(tr)} pos={(tr != null ? tr.position.ToString("F3") : "<null>")}");
            }
        }
        if (count == 0)
            sb.AppendLine("<none>");
        sb.AppendLine();
    }

    private static void AppendSceneReceivers(StringBuilder sb)
    {
        sb.AppendLine("==== UNIT OCCLUSION RECEIVERS ====");
        var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int count = 0;
        foreach (var mb in all)
        {
            if (mb == null || mb.GetType().Name != "UnitOcclusionMaterialReceiver")
                continue;
            count++;
            Type t = mb.GetType();
            sb.AppendLine($"[{count}] {PathOf(mb.transform)} enabled={mb.enabled}");
            foreach (string n in new[] { "scriptVersion", "currentOccluded", "activeOccluderCount", "lastWarning", "lastApply" })
            {
                object v = GetField(t, mb, n);
                if (v != null)
                    sb.AppendLine($"    {n}={FormatValue(v)}");
            }
        }
        if (count == 0)
            sb.AppendLine("<none>");
        sb.AppendLine();
    }

    private static void AppendOccluderGeometry(StringBuilder sb, MonoBehaviour trigger, Type type)
    {
        sb.AppendLine("==== OCCLUDER GEOMETRY SNAPSHOT ====");
        var contentRoot = GetField(type, trigger, "occluderContentRoot") as Transform;
        var colliderRoot = GetField(type, trigger, "occluderColliderRoot") as Transform;
        var frontRootObj = GetField(type, trigger, "frontOccluderRoot") as GameObject;

        AppendRenderers(sb, "ContentRoot Renderers", contentRoot);
        AppendColliders(sb, "ColliderRoot Colliders", colliderRoot);
        AppendRenderers(sb, "FrontOccluderRoot Renderers", frontRootObj != null ? frontRootObj.transform : null);
        sb.AppendLine();
    }

    private static void AppendRenderers(StringBuilder sb, string title, Transform root)
    {
        sb.AppendLine($"-- {title}: {PathOf(root)}");
        if (root == null)
        {
            sb.AppendLine("   <null>");
            return;
        }

        var rs = root.GetComponentsInChildren<Renderer>(true);
        sb.AppendLine($"   count={rs.Length}");
        for (int i = 0; i < rs.Length && i < 16; i++)
        {
            Renderer r = rs[i];
            if (r == null) continue;
            sb.AppendLine($"   [{i}] {PathOf(r.transform)} type={r.GetType().Name} enabled={r.enabled} active={r.gameObject.activeInHierarchy} layer={LayerMask.LayerToName(r.gameObject.layer)} bounds={BoundsToString(r.bounds)}");
        }
    }

    private static void AppendColliders(StringBuilder sb, string title, Transform root)
    {
        sb.AppendLine($"-- {title}: {PathOf(root)}");
        if (root == null)
        {
            sb.AppendLine("   <null>");
            return;
        }

        var cs = root.GetComponentsInChildren<Collider>(true);
        sb.AppendLine($"   count={cs.Length}");
        for (int i = 0; i < cs.Length && i < 24; i++)
        {
            Collider c = cs[i];
            if (c == null) continue;
            sb.AppendLine($"   [{i}] {PathOf(c.transform)} type={c.GetType().Name} enabled={c.enabled} trigger={c.isTrigger} active={c.gameObject.activeInHierarchy} layer={LayerMask.LayerToName(c.gameObject.layer)} bounds={BoundsToString(c.bounds)}");
        }
    }

    private static object GetField(Type type, object target, string name)
    {
        if (type == null || target == null || string.IsNullOrEmpty(name))
            return null;

        FieldInfo f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
            return null;

        return SafeGet(f, target);
    }

    private static object SafeGet(FieldInfo f, object target)
    {
        try { return f.GetValue(target); }
        catch { return null; }
    }

    private static string FormatValue(object v)
    {
        if (v == null) return "<null>";

        if (v is Transform tr) return PathOf(tr);
        if (v is GameObject go) return $"{PathOf(go.transform)} activeSelf={go.activeSelf} activeHierarchy={go.activeInHierarchy}";
        if (v is Camera cam) return $"{PathOf(cam.transform)} enabled={cam.enabled} active={cam.gameObject.activeInHierarchy} pixel={cam.pixelWidth}x{cam.pixelHeight}";
        if (v is Collider col) return $"{PathOf(col.transform)} type={col.GetType().Name} enabled={col.enabled} trigger={col.isTrigger} bounds={BoundsToString(col.bounds)}";
        if (v is Renderer r) return $"{PathOf(r.transform)} type={r.GetType().Name} enabled={r.enabled} bounds={BoundsToString(r.bounds)}";
        if (v is Vector3 vec) return vec.ToString("F3");
        if (v is Vector2 vec2) return vec2.ToString("F3");
        if (v is Vector4 vec4) return vec4.ToString("F3");
        if (v is Bounds b) return BoundsToString(b);
        if (v is LayerMask lm) return LayerMaskToString(lm.value);
        if (v is UnityEngine.Object obj) return $"{obj.name} ({obj.GetType().Name})";

        return v.ToString();
    }

    private static string BoundsToString(Bounds b)
    {
        return $"center={b.center.ToString("F3")} size={b.size.ToString("F3")} min={b.min.ToString("F3")} max={b.max.ToString("F3")}";
    }

    private static string LayerMaskToString(int mask)
    {
        if (mask == ~0) return "Everything";
        if (mask == 0) return "Nothing";

        var sb = new StringBuilder();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) == 0) continue;
            if (sb.Length > 0) sb.Append("|");
            string name = LayerMask.LayerToName(i);
            sb.Append(string.IsNullOrEmpty(name) ? i.ToString() : $"{name}({i})");
        }
        return sb.ToString();
    }

    private static string PathOf(Transform t)
    {
        if (t == null) return "<null>";
        string s = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 64)
        {
            s = p.name + "/" + s;
            p = p.parent;
        }
        return s;
    }
}
#endif
