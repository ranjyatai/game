#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sky Prison occlusion mask source bill.
/// Editor-only diagnostic tool. It does not modify scene objects.
/// Use it to verify which renderers under a selected terrain decoration can actually be rendered by the occlusion mask cameras.
/// </summary>
public static class SkyPrisonOcclusionMaskSourceBill
{
    private const string Version = "V1 - 2026-05-22 - occlusion mask source renderer bill";

    [MenuItem("Tools/Sky Prison/Map/遮挡诊断/复制选中物体遮挡Mask来源账单", priority = 3101)]
    public static void CopySelectedBill()
    {
        Transform root = ResolveSelectedDecorationRoot();
        if (root == null)
        {
            EditorUtility.DisplayDialog(
                "Sky Prison Occlusion Mask Source Bill",
                "请先选中地图物体实例，或它下面的 VisualRoot / RuleRoot / FrontOccluderRoot / Proxy 子节点。",
                "OK");
            return;
        }

        string report = BuildBill(root);
        EditorGUIUtility.systemCopyBuffer = report;
        Debug.Log(report);
        EditorUtility.DisplayDialog(
            "Sky Prison Occlusion Mask Source Bill",
            "已复制遮挡 Mask 来源账单到剪贴板，也已输出到 Console。",
            "OK");
    }

    [MenuItem("Tools/Sky Prison/Map/遮挡诊断/复制选中物体遮挡Mask来源账单", true)]
    public static bool ValidateCopySelectedBill()
    {
        return Selection.activeTransform != null;
    }

    private static Transform ResolveSelectedDecorationRoot()
    {
        Transform t = Selection.activeTransform;
        if (t == null)
            return null;

        Transform cur = t;
        while (cur != null)
        {
            if (cur.Find("VisualRoot") != null && cur.Find("RuleRoot") != null)
                return cur;
            cur = cur.parent;
        }

        cur = t;
        while (cur != null)
        {
            if (cur.name == "VisualRoot" || cur.name == "RuleRoot" || cur.name == "FrontOccluderRoot" || cur.name.StartsWith("FrontOccluderProxy", StringComparison.Ordinal))
            {
                Transform p = cur.parent;
                while (p != null)
                {
                    if (p.Find("VisualRoot") != null && p.Find("RuleRoot") != null)
                        return p;
                    p = p.parent;
                }
            }
            cur = cur.parent;
        }

        return null;
    }

    private static string BuildBill(Transform root)
    {
        var sb = new StringBuilder(64 * 1024);
        Transform visualRoot = root.Find("VisualRoot");
        Transform ruleRoot = root.Find("RuleRoot");
        Transform frontRoot = root.Find("RuleRoot/FrontOccluderRoot");
        Transform modelProxy = frontRoot != null ? frontRoot.Find("FrontOccluderProxy_Model") : null;
        Transform boxProxy = frontRoot != null ? frontRoot.Find("FrontOccluderProxy_Box") : null;

        Camera[] allCameras = Resources.FindObjectsOfTypeAll<Camera>()
            .Where(c => c != null && IsSceneObject(c.gameObject))
            .OrderBy(c => GetPath(c.transform))
            .ToArray();

        Camera[] maskCameras = allCameras
            .Where(c => c.name.IndexOf("OcclusionMask", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (c.targetTexture != null && c.targetTexture.name.IndexOf("OcclusionMask", StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();

        sb.AppendLine("==== SKY PRISON OCCLUSION MASK SOURCE BILL ====");
        sb.AppendLine(Version);
        sb.AppendLine("Time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " frame=" + Time.frameCount + " playMode=" + EditorApplication.isPlaying);
        sb.AppendLine("TargetRoot=" + GetPath(root));
        sb.AppendLine("VisualRoot=" + PathOrNull(visualRoot));
        sb.AppendLine("RuleRoot=" + PathOrNull(ruleRoot));
        sb.AppendLine("FrontOccluderRoot=" + PathOrNull(frontRoot));
        sb.AppendLine("FrontOccluderProxy_Model=" + PathOrNull(modelProxy));
        sb.AppendLine("FrontOccluderProxy_Box=" + PathOrNull(boxProxy));
        sb.AppendLine();

        sb.AppendLine("==== OCCLUSION MASK CAMERAS ====");
        if (maskCameras.Length == 0)
        {
            sb.AppendLine("<none found by name/targetTexture containing OcclusionMask>");
        }
        else
        {
            foreach (Camera c in maskCameras)
                AppendCamera(sb, c);
        }
        sb.AppendLine();

        sb.AppendLine("==== VISUAL ROOT RENDERERS ====");
        Renderer[] visualRenderers = GetRenderers(visualRoot);
        AppendRendererList(sb, visualRenderers, maskCameras, "VISUAL");
        sb.AppendLine();

        sb.AppendLine("==== FRONT OCCLUDER ROOT RENDERERS ====");
        Renderer[] frontRenderers = GetRenderers(frontRoot);
        AppendRendererList(sb, frontRenderers, maskCameras, "FRONT");
        sb.AppendLine();

        sb.AppendLine("==== FRONT PROXY GROUP COUNTS ====");
        AppendGroupCount(sb, "Model", GetRenderers(modelProxy));
        AppendGroupCount(sb, "Box", GetRenderers(boxProxy));
        AppendGroupCount(sb, "Box/__AutoFrontOccluderShapeClone", GetRenderers(boxProxy != null ? boxProxy.Find("__AutoFrontOccluderShapeClone") : null));
        sb.AppendLine();

        sb.AppendLine("==== ACTIVE MASK-RENDERABLE FRONT RENDERERS ====");
        int activeMaskRenderableCount = 0;
        foreach (Renderer r in frontRenderers)
        {
            bool renderableByAnyMask = maskCameras.Any(c => CameraCanRender(c, r));
            if (!IsRendererLive(r) || !renderableByAnyMask)
                continue;

            activeMaskRenderableCount++;
            sb.AppendLine($"[{activeMaskRenderableCount:00}] {GetPath(r.transform)}");
            sb.AppendLine($"     group={GetProxyGroup(frontRoot, r.transform)} layer={LayerMask.LayerToName(r.gameObject.layer)}({r.gameObject.layer}) enabled={r.enabled} active={r.gameObject.activeInHierarchy}");
            sb.AppendLine($"     mats={FormatMaterials(r)}");
            sb.AppendLine($"     bounds={FormatBounds(r.bounds)}");
        }
        if (activeMaskRenderableCount == 0)
            sb.AppendLine("<none>");
        sb.AppendLine();

        sb.AppendLine("==== SUSPICIOUS CONDITIONS ====");
        AppendSuspicionReport(sb, frontRoot, visualRoot, frontRenderers, visualRenderers, maskCameras);
        sb.AppendLine();

        sb.AppendLine("==== SHORT DIAGNOSIS GUIDE ====");
        sb.AppendLine("1. If ACTIVE MASK-RENDERABLE FRONT RENDERERS contains Box/Clone while Model also exists, the mask may be a mixed source.");
        sb.AppendLine("2. If Model renderers are aligned but not renderable by mask cameras, layer/cullingMask/material is wrong.");
        sb.AppendLine("3. If visual and proxy bounds are aligned but the image still looks offset, inspect the actual shader/material alpha/mask pass or the debug overlay being drawn.");
        sb.AppendLine("4. If an unexpected renderer outside FrontOccluderProxy_Model is active and mask-renderable, disable it before changing camera/UI code.");

        return sb.ToString();
    }

    private static void AppendSuspicionReport(StringBuilder sb, Transform frontRoot, Transform visualRoot, Renderer[] frontRenderers, Renderer[] visualRenderers, Camera[] maskCameras)
    {
        if (frontRoot == null)
        {
            sb.AppendLine("ERROR: FrontOccluderRoot missing.");
            return;
        }

        Renderer[] modelRenderers = GetRenderers(frontRoot.Find("FrontOccluderProxy_Model"));
        Renderer[] boxRenderers = GetRenderers(frontRoot.Find("FrontOccluderProxy_Box"));
        Renderer[] cloneRenderers = GetRenderers(frontRoot.Find("FrontOccluderProxy_Box/__AutoFrontOccluderShapeClone"));

        int liveModel = modelRenderers.Count(IsRendererLive);
        int liveBox = boxRenderers.Count(IsRendererLive);
        int liveClone = cloneRenderers.Count(IsRendererLive);
        int liveMaskModel = modelRenderers.Count(r => IsRendererLive(r) && maskCameras.Any(c => CameraCanRender(c, r)));
        int liveMaskBox = boxRenderers.Count(r => IsRendererLive(r) && maskCameras.Any(c => CameraCanRender(c, r)));
        int liveMaskClone = cloneRenderers.Count(r => IsRendererLive(r) && maskCameras.Any(c => CameraCanRender(c, r)));

        sb.AppendLine($"liveModel={liveModel} liveBox={liveBox} liveClone={liveClone}");
        sb.AppendLine($"maskRenderableModel={liveMaskModel} maskRenderableBox={liveMaskBox} maskRenderableClone={liveMaskClone}");

        if (liveMaskModel > 0 && (liveMaskBox > 0 || liveMaskClone > 0))
            sb.AppendLine("WARNING: Model proxy and Box/Clone proxy are both active and renderable by mask cameras. This can create a mixed/offset mask.");

        if (liveModel == 0 && (liveBox > 0 || liveClone > 0))
            sb.AppendLine("WARNING: Model proxy is not live, mask may be coming from Box/Clone only.");

        if (liveModel > 0 && liveMaskModel == 0)
            sb.AppendLine("WARNING: Model proxy is live but not renderable by any detected OcclusionMask camera. Check layer/cullingMask.");

        if (visualRenderers.Length != modelRenderers.Length)
            sb.AppendLine($"WARNING: Visual renderer count ({visualRenderers.Length}) != Model proxy renderer count ({modelRenderers.Length}). Shape may be incomplete.");

        foreach (Renderer r in frontRenderers)
        {
            if (r == null) continue;
            if (!IsRendererLive(r)) continue;
            if (!maskCameras.Any(c => CameraCanRender(c, r))) continue;
            string group = GetProxyGroup(frontRoot, r.transform);
            if (group != "Model" && group != "ModelChild")
                sb.AppendLine("WARNING: Non-model renderer is active and mask-renderable: " + GetPath(r.transform) + " group=" + group);
        }
    }

    private static void AppendGroupCount(StringBuilder sb, string label, Renderer[] renderers)
    {
        int live = renderers.Count(IsRendererLive);
        sb.AppendLine($"{label}: renderers={renderers.Length} live={live}");
    }

    private static void AppendRendererList(StringBuilder sb, Renderer[] renderers, Camera[] maskCameras, string label)
    {
        if (renderers == null || renderers.Length == 0)
        {
            sb.AppendLine("<none>");
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            bool live = IsRendererLive(r);
            string cams = FormatRenderableCameras(r, maskCameras);
            sb.AppendLine($"[{i:00}] {label} {GetPath(r.transform)}");
            sb.AppendLine($"     type={r.GetType().Name} enabled={r.enabled} activeSelf={r.gameObject.activeSelf} activeHierarchy={r.gameObject.activeInHierarchy} live={live}");
            sb.AppendLine($"     layer={LayerMask.LayerToName(r.gameObject.layer)}({r.gameObject.layer}) shadow={r.shadowCastingMode} receiveShadows={r.receiveShadows}");
            sb.AppendLine($"     maskRenderableBy={cams}");
            sb.AppendLine($"     mats={FormatMaterials(r)}");
            sb.AppendLine($"     bounds={FormatBounds(r.bounds)}");
            sb.AppendLine($"     lossyScale={FormatV3(r.transform.lossyScale)} localScale={FormatV3(r.transform.localScale)}");
        }
    }

    private static void AppendCamera(StringBuilder sb, Camera c)
    {
        string rt = c.targetTexture == null ? "null" : c.targetTexture.name + " " + c.targetTexture.width + "x" + c.targetTexture.height;
        sb.AppendLine($"{GetPath(c.transform)} enabled={c.enabled} active={c.gameObject.activeInHierarchy} depth={c.depth} target={rt}");
        sb.AppendLine($"     cullingMask={FormatMask(c.cullingMask)} clearFlags={c.clearFlags} background={c.backgroundColor}");
    }

    private static Renderer[] GetRenderers(Transform root)
    {
        if (root == null)
            return Array.Empty<Renderer>();
        return root.GetComponentsInChildren<Renderer>(true)
            .Where(r => r != null)
            .OrderBy(r => GetPath(r.transform))
            .ToArray();
    }

    private static bool IsRendererLive(Renderer r)
    {
        return r != null && r.enabled && r.gameObject.activeInHierarchy;
    }

    private static bool CameraCanRender(Camera c, Renderer r)
    {
        if (c == null || r == null)
            return false;
        if (!c.enabled || !c.gameObject.activeInHierarchy)
            return false;
        int layerBit = 1 << r.gameObject.layer;
        return (c.cullingMask & layerBit) != 0;
    }

    private static string FormatRenderableCameras(Renderer r, Camera[] cameras)
    {
        if (cameras == null || cameras.Length == 0)
            return "<no mask cameras>";
        var names = new List<string>();
        foreach (Camera c in cameras)
        {
            if (CameraCanRender(c, r))
                names.Add(c.name);
        }
        return names.Count == 0 ? "<none>" : string.Join(", ", names);
    }

    private static string FormatMaterials(Renderer r)
    {
        if (r == null) return "<null renderer>";
        Material[] mats = r.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return "<none>";
        var parts = new List<string>();
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (m == null)
                parts.Add($"[{i}] null");
            else
                parts.Add($"[{i}] {m.name} / {(m.shader != null ? m.shader.name : "<null shader>")}");
        }
        return string.Join(" | ", parts);
    }

    private static string FormatMask(int mask)
    {
        var parts = new List<string>();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) == 0) continue;
            string name = LayerMask.LayerToName(i);
            parts.Add(string.IsNullOrEmpty(name) ? i.ToString() : name + "(" + i + ")");
        }
        return parts.Count == 0 ? "<none>" : string.Join(", ", parts);
    }

    private static string FormatBounds(Bounds b)
    {
        return $"center={FormatV3(b.center)} size={FormatV3(b.size)} min={FormatV3(b.min)} max={FormatV3(b.max)}";
    }

    private static string FormatV3(Vector3 v)
    {
        return $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
    }

    private static string PathOrNull(Transform t)
    {
        return t == null ? "<null>" : GetPath(t);
    }

    private static string GetProxyGroup(Transform frontRoot, Transform t)
    {
        if (frontRoot == null || t == null)
            return "<unknown>";

        Transform model = frontRoot.Find("FrontOccluderProxy_Model");
        Transform box = frontRoot.Find("FrontOccluderProxy_Box");
        Transform clone = frontRoot.Find("FrontOccluderProxy_Box/__AutoFrontOccluderShapeClone");

        if (model != null && t == model) return "Model";
        if (model != null && IsChildOf(t, model)) return "ModelChild";
        if (clone != null && (t == clone || IsChildOf(t, clone))) return "BoxAutoClone";
        if (box != null && (t == box || IsChildOf(t, box))) return "Box";
        return "OtherUnderFrontRoot";
    }

    private static bool IsChildOf(Transform child, Transform parent)
    {
        Transform cur = child;
        while (cur != null)
        {
            if (cur == parent) return true;
            cur = cur.parent;
        }
        return false;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "<null>";
        var stack = new Stack<string>();
        Transform cur = t;
        while (cur != null)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }
        return string.Join("/", stack.ToArray());
    }

    private static bool IsSceneObject(GameObject go)
    {
        if (go == null) return false;
        if (EditorUtility.IsPersistent(go)) return false;
        return go.scene.IsValid();
    }
}
#endif
