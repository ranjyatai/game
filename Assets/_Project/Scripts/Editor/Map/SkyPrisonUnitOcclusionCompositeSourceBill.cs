#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SkyPrisonUnitOcclusionCompositeSourceBill
{
    private const string Version = "V1 - 2026-05-22 - unit occlusion composite material source bill";

    private static readonly string[] TextureProps =
    {
        "_OcclusionTex",
        "_SkyPrison_OcclusionTex",
        "_SkyPrison_OcclusionMask",
        "_MaskTex",
        "_MainTex"
    };

    private static readonly string[] FloatProps =
    {
        "_OcclusionStrength",
        "_SkyPrison_OcclusionStrength",
        "_OccludedAlpha",
        "_VisibleAlpha",
        "_OutlineWidth",
        "_Cutoff",
        "_StencilRef",
        "_StencilComp"
    };

    private static readonly string[] ColorProps =
    {
        "_Color",
        "_Tint",
        "_OutlineColor",
        "_OcclusionColor",
        "_SkyPrison_OcclusionColor"
    };

    [MenuItem("Tools/Sky Prison/Map/遮挡诊断/复制选中单位遮挡材质账单", false, 3601)]
    public static void CopySelectedUnitBill()
    {
        Transform root = Selection.activeTransform;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Sky Prison", "请先在 Hierarchy 里选中 Player / Enemy / VisualRoot / SpineRoot / OutlineProxyRoot 相关节点。", "OK");
            return;
        }

        string report = BuildReport(root);
        EditorGUIUtility.systemCopyBuffer = report;
        UnityEngine.Debug.Log(report);
        EditorUtility.DisplayDialog("Sky Prison", "单位遮挡材质账单已复制到剪贴板。", "OK");
    }

    private static string BuildReport(Transform selected)
    {
        Transform unitRoot = ResolveUsefulRoot(selected);
        var sb = new StringBuilder(64 * 1024);

        sb.AppendLine("==== SKY PRISON UNIT OCCLUSION COMPOSITE SOURCE BILL ====");
        sb.AppendLine(Version);
        sb.AppendLine($"Time={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} frame={Time.frameCount} playMode={EditorApplication.isPlaying}");
        sb.AppendLine($"Selected={GetPath(selected)}");
        sb.AppendLine($"ResolvedRoot={GetPath(unitRoot)}");
        sb.AppendLine();

        AppendCameraAndRTSummary(sb);
        AppendRendererSummary(sb, unitRoot);
        AppendDiagnosisGuide(sb);

        return sb.ToString();
    }

    private static Transform ResolveUsefulRoot(Transform t)
    {
        if (t == null) return null;

        Transform cur = t;
        Transform best = t;
        while (cur != null)
        {
            string n = cur.name;
            if (n == "Player" || n.StartsWith("PF_Unit_", StringComparison.OrdinalIgnoreCase) || n.Contains("Enemy") || n.Contains("PlayerRuntime"))
                best = cur;

            if (n == "UnitRoot" || n == "WorldRoot" || n == "System")
                break;

            cur = cur.parent;
        }

        return best != null ? best : t;
    }

    private static void AppendCameraAndRTSummary(StringBuilder sb)
    {
        sb.AppendLine("==== OCCLUSION / OUTLINE CAMERAS ====");
        Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
        Array.Sort(cameras, (a, b) => string.Compare(GetPath(a.transform), GetPath(b.transform), StringComparison.Ordinal));

        foreach (Camera cam in cameras)
        {
            if (cam == null) continue;
            string path = GetPath(cam.transform);
            if (!path.Contains("Occlusion", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("Outline", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("Main Camera", StringComparison.OrdinalIgnoreCase))
                continue;

            RenderTexture rt = cam.targetTexture;
            sb.AppendLine($"{path} enabled={cam.enabled} active={cam.gameObject.activeInHierarchy} depth={cam.depth} culling={LayerMaskToNames(cam.cullingMask)} target={DescribeRT(rt)} pixelRect={cam.pixelRect}");
        }
        sb.AppendLine();
    }

    private static void AppendRendererSummary(StringBuilder sb, Transform root)
    {
        sb.AppendLine("==== UNIT RENDERERS ====");
        if (root == null)
        {
            sb.AppendLine("<null root>");
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Array.Sort(renderers, (a, b) => string.Compare(GetPath(a.transform), GetPath(b.transform), StringComparison.Ordinal));

        int liveCount = 0;
        int occlusionShaderCount = 0;
        int skeletonShaderCount = 0;
        int outlineCount = 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            bool live = r.enabled && r.gameObject.activeInHierarchy;
            if (live) liveCount++;

            string path = GetPath(r.transform);
            string group = ClassifyRenderer(path);
            if (group.Contains("Outline")) outlineCount++;

            sb.AppendLine($"[{i:00}] {group} {path}");
            sb.AppendLine($"     type={r.GetType().Name} enabled={r.enabled} activeSelf={r.gameObject.activeSelf} activeHierarchy={r.gameObject.activeInHierarchy} live={live}");
            sb.AppendLine($"     layer={LayerMask.LayerToName(r.gameObject.layer)}({r.gameObject.layer}) sorting={GetSorting(r)} shadow={r.shadowCastingMode} receiveShadows={r.receiveShadows}");
            sb.AppendLine($"     bounds=center={Fmt(r.bounds.center)} size={Fmt(r.bounds.size)} min={Fmt(r.bounds.min)} max={Fmt(r.bounds.max)}");
            sb.AppendLine($"     lossyScale={Fmt(r.transform.lossyScale)} localScale={Fmt(r.transform.localScale)}");

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            Material[] mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                sb.AppendLine("     mats=<none>");
            }
            else
            {
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                    {
                        sb.AppendLine($"     mat[{m}]=<null>");
                        continue;
                    }

                    string shaderName = mat.shader != null ? mat.shader.name : "<null shader>";
                    if (shaderName.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0 || mat.name.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0)
                        occlusionShaderCount++;
                    if (shaderName.IndexOf("Spine/Skeleton", StringComparison.OrdinalIgnoreCase) >= 0)
                        skeletonShaderCount++;

                    sb.AppendLine($"     mat[{m}] {mat.name} / {shaderName} queue={mat.renderQueue} keywords={string.Join(",", mat.shaderKeywords ?? Array.Empty<string>())}");
                    AppendMaterialProperties(sb, mat, mpb, "        ");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("==== UNIT RENDERER COUNTS ====");
        sb.AppendLine($"totalRenderers={renderers.Length} live={liveCount} occlusionRelatedMaterials={occlusionShaderCount} skeletonMaterials={skeletonShaderCount} outlineNamedRenderers={outlineCount}");
        sb.AppendLine();
    }

    private static void AppendMaterialProperties(StringBuilder sb, Material mat, MaterialPropertyBlock mpb, string indent)
    {
        foreach (string p in TextureProps)
        {
            if (!mat.HasProperty(p)) continue;
            Texture sharedTex = mat.GetTexture(p);
            Texture blockTex = null;
            try { blockTex = mpb.GetTexture(p); } catch { /* ignored */ }
            sb.AppendLine($"{indent}tex {p}: material={DescribeTex(sharedTex)} mpb={DescribeTex(blockTex)}");
        }

        foreach (string p in FloatProps)
        {
            if (!mat.HasProperty(p)) continue;
            float v = mat.GetFloat(p);
            float bv = 0f;
            bool hasBlock = false;
            try { bv = mpb.GetFloat(p); hasBlock = Math.Abs(bv) > 0.00001f; } catch { /* ignored */ }
            sb.AppendLine($"{indent}float {p}: material={v:0.###} mpb={(hasBlock ? bv.ToString("0.###") : "<none/0>")}");
        }

        foreach (string p in ColorProps)
        {
            if (!mat.HasProperty(p)) continue;
            Color c = mat.GetColor(p);
            Color bc = default;
            bool hasBlock = false;
            try { bc = mpb.GetColor(p); hasBlock = bc.maxColorComponent > 0.00001f || bc.a > 0.00001f; } catch { /* ignored */ }
            sb.AppendLine($"{indent}color {p}: material={Fmt(c)} mpb={(hasBlock ? Fmt(bc) : "<none/0>")}");
        }
    }

    private static void AppendDiagnosisGuide(StringBuilder sb)
    {
        sb.AppendLine("==== DIAGNOSIS GUIDE ====");
        sb.AppendLine("1. Body renderer and Outline/OccludedOutline renderer should sample the same effective occlusion mask texture if they are expected to be clipped together.");
        sb.AppendLine("2. If body uses an OcclusionComposite material but outline still uses plain Spine/Skeleton or Outline-only material, outline will leak beyond the cut.");
        sb.AppendLine("3. If _OcclusionTex is only present in material but MPB is <none>, confirm whether the texture is globally bound by Shader.SetGlobalTexture.");
        sb.AppendLine("4. If OccludedOutline proxy is live while normal outline proxy is also live, verify Gate state and currentOccluded logic.");
        sb.AppendLine("5. If renderers are aligned but clipped shape is wrong, inspect shader alpha / stencil / screen UV path rather than proxy transforms.");
    }

    private static string ClassifyRenderer(string path)
    {
        if (path.IndexOf("OutlineProxy", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (path.IndexOf("Occluded", StringComparison.OrdinalIgnoreCase) >= 0) return "OCCLUDED_OUTLINE_PROXY";
            return "OUTLINE_PROXY";
        }
        if (path.IndexOf("SpineRoot", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Spine GameObject", StringComparison.OrdinalIgnoreCase) >= 0)
            return "BODY_SPINE";
        if (path.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
            return "WEAPON";
        return "OTHER";
    }

    private static string GetSorting(Renderer r)
    {
        if (r == null) return "<null>";
        return $"layer={r.sortingLayerName}({r.sortingLayerID}) order={r.sortingOrder}";
    }

    private static string DescribeRT(RenderTexture rt)
    {
        if (rt == null) return "null";
        return $"{rt.name} {rt.width}x{rt.height} fmt={rt.graphicsFormat}";
    }

    private static string DescribeTex(Texture tex)
    {
        if (tex == null) return "<null>";
        return $"{tex.name} {tex.width}x{tex.height} type={tex.GetType().Name}";
    }

    private static string LayerMaskToNames(int mask)
    {
        if (mask == 0) return "<none>(0)";
        var names = new List<string>();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) == 0) continue;
            string n = LayerMask.LayerToName(i);
            names.Add(string.IsNullOrEmpty(n) ? i.ToString() : $"{n}({i})");
        }
        return string.Join("|", names);
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "<null>";
        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }

    private static string Fmt(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
    private static string Fmt(Color c) => $"RGBA({c.r:0.###},{c.g:0.###},{c.b:0.###},{c.a:0.###})";
}
#endif
