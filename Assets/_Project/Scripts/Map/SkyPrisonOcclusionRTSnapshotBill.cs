#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sky Prison debug utility.
/// Captures the actual occlusion RenderTextures that are bound to cameras/materials/MPBs.
/// This does not modify scene objects.
/// </summary>
public static class SkyPrisonOcclusionRTSnapshotBill
{
    private const string Version = "V1 - 2026-05-22 - occlusion RT snapshot + binding bill";
    private const string MenuPath = "Tools/Sky Prison/Map/遮挡诊断/保存遮挡RT快照并复制账单";

    [MenuItem(MenuPath)]
    public static void SaveSnapshotsAndCopyBill()
    {
        var selected = Selection.activeTransform;
        var sb = new StringBuilder(16384);
        sb.AppendLine("==== SKY PRISON OCCLUSION RT SNAPSHOT BILL ====");
        sb.AppendLine(Version);
        sb.AppendLine($"Time={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} frame={Time.frameCount} playMode={Application.isPlaying}");
        sb.AppendLine($"Selected={(selected ? GetPath(selected) : "<none>")}");
        sb.AppendLine();

        var outputDir = Path.Combine(Application.dataPath, "_Project/Debug/OcclusionRTSnapshots");
        Directory.CreateDirectory(outputDir);
        sb.AppendLine($"OutputDir={outputDir}");
        sb.AppendLine();

        var textures = new Dictionary<string, RenderTexture>();

        sb.AppendLine("==== CAMERA TARGET TEXTURES ====");
        foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (!cam) continue;
            var go = cam.gameObject;
            var rt = cam.targetTexture;
            sb.AppendLine($"Camera {GetPath(cam.transform)} enabled={cam.enabled} active={go.activeInHierarchy} culling={LayerMaskToNames(cam.cullingMask)} target={(rt ? FormatRT(rt) : "null")}");
            if (rt && IsOcclusionLike(rt.name))
                AddTexture(textures, "camera." + rt.name, rt);
        }
        sb.AppendLine();

        sb.AppendLine("==== SELECTED UNIT / OBJECT RENDERER TEXTURE BINDINGS ====");
        if (selected)
        {
            var renderers = selected.GetComponentsInChildren<Renderer>(true);
            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                sb.AppendLine($"[{i:00}] {GetPath(r.transform)} type={r.GetType().Name} enabled={r.enabled} active={r.gameObject.activeInHierarchy} layer={LayerMask.LayerToName(r.gameObject.layer)} bounds={FormatBounds(r.bounds)}");

                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (!mat) { sb.AppendLine($"   mat[{m}]=<null>"); continue; }
                    sb.AppendLine($"   mat[{m}] {mat.name} / {(mat.shader ? mat.shader.name : "<no shader>")}");
                    TryAppendTexture(sb, textures, $"renderer[{i}].mat[{m}]._OcclusionTex", mat, null, "_OcclusionTex");
                    TryAppendTexture(sb, textures, $"renderer[{i}].mat[{m}]._MainTex", mat, null, "_MainTex");
                    TryAppendTexture(sb, textures, $"renderer[{i}].mpb._OcclusionTex", null, mpb, "_OcclusionTex");
                    TryAppendTexture(sb, textures, $"renderer[{i}].mpb._MainTex", null, mpb, "_MainTex");
                }
            }
        }
        else
        {
            sb.AppendLine("<no selection>");
        }
        sb.AppendLine();

        sb.AppendLine("==== ALL LIVE OCCLUSION-LIKE RENDER TEXTURES ====");
        foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
        {
            if (!rt || !IsOcclusionLike(rt.name)) continue;
            sb.AppendLine(FormatRT(rt));
            AddTexture(textures, "live." + rt.name, rt);
        }
        sb.AppendLine();

        sb.AppendLine("==== SAVED SNAPSHOTS ====");
        int saved = 0;
        foreach (var kv in textures)
        {
            var rt = kv.Value;
            if (!rt) continue;
            string safeName = MakeSafeFileName(kv.Key + "__" + rt.name);
            string path = Path.Combine(outputDir, safeName + ".png");
            try
            {
                SaveRenderTexturePng(rt, path);
                sb.AppendLine($"saved {kv.Key} => {path}");
                saved++;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"FAILED {kv.Key} {FormatRT(rt)} error={ex.GetType().Name}: {ex.Message}");
            }
        }
        if (saved == 0)
            sb.AppendLine("<none>");

        sb.AppendLine();
        sb.AppendLine("==== READ THIS ====");
        sb.AppendLine("1. If RT_OcclusionMask_Runtime is correct but RT_OcclusionRaw_Player_Runtime is shifted, the RawMask copy/channel phase is wrong.");
        sb.AppendLine("2. If both RTs look correct but the body still clips wrong, inspect SpineOcclusionComposite screen UV sampling.");
        sb.AppendLine("3. If MPB _OcclusionTex differs from material _OcclusionTex, MPB is the effective binding for that renderer.");
        sb.AppendLine("4. This tool only reads and saves snapshots; it does not change scene state.");

        var text = sb.ToString();
        EditorGUIUtility.systemCopyBuffer = text;
        Debug.Log(text);
        AssetDatabase.Refresh();
    }

    private static void TryAppendTexture(StringBuilder sb, Dictionary<string, RenderTexture> textures, string label, Material mat, MaterialPropertyBlock mpb, string prop)
    {
        Texture tex = null;
        if (mat != null && mat.HasProperty(prop)) tex = mat.GetTexture(prop);
        if (mpb != null) tex = mpb.GetTexture(prop);
        if (!tex)
        {
            sb.AppendLine($"      tex {prop}: <null>");
            return;
        }
        sb.AppendLine($"      tex {prop}: {tex.name} {tex.width}x{tex.height} type={tex.GetType().Name}");
        if (tex is RenderTexture rt && IsOcclusionLike(rt.name))
            AddTexture(textures, label + "." + rt.name, rt);
    }

    private static void AddTexture(Dictionary<string, RenderTexture> dict, string key, RenderTexture rt)
    {
        if (!rt) return;
        if (dict.ContainsValue(rt)) return;
        dict[key] = rt;
    }

    private static bool IsOcclusionLike(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Raw", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Mask", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void SaveRenderTexturePng(RenderTexture rt, string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        tex.Apply(false, false);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        RenderTexture.active = previous;
    }

    private static string FormatRT(RenderTexture rt)
    {
        if (!rt) return "<null>";
        return $"{rt.name} {rt.width}x{rt.height} fmt={rt.graphicsFormat} depth={rt.depth} created={rt.IsCreated()}";
    }

    private static string FormatBounds(Bounds b)
    {
        return $"center={F3(b.center)} size={F3(b.size)}";
    }

    private static string F3(Vector3 v)
    {
        return $"({v.x:F3},{v.y:F3},{v.z:F3})";
    }

    private static string GetPath(Transform t)
    {
        if (!t) return "<null>";
        var stack = new Stack<string>();
        while (t)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack.ToArray());
    }

    private static string LayerMaskToNames(int mask)
    {
        if (mask == 0) return "Nothing";
        if (mask == -1) return "Everything";
        var parts = new List<string>();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) == 0) continue;
            var name = LayerMask.LayerToName(i);
            parts.Add(string.IsNullOrEmpty(name) ? i.ToString() : name + "(" + i + ")");
        }
        return string.Join("|", parts);
    }

    private static string MakeSafeFileName(string input)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');
        return input.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }
}
#endif
