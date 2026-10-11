using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "Black Flame Works" 片头的离线渲染：逐帧把 Shaders/IntroBlackFlame 画进 RT，存成 PNG。
/// 草稿 = 一张联系表（几个关键时刻），终版 = PNG 序列（再用剪辑软件 / ffmpeg 合成视频）。
/// </summary>
public static class SkyPrisonBlackFlameIntroRenderer
{
    const string Dir = "Assets/_Project/Art/Intro/BlackFlame";
    const float Duration = SkyPrisonBlackFlameTimeline.Duration;       // 实际片长（秒）；着色器内部时钟是 14s，用 Warp 压缩

    static Material MakeMat()
    {
        var path = Dir + "/BlackFlameWorks_Mask.png";
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null && (ti.sRGBTexture || ti.mipmapEnabled || ti.maxTextureSize < 4096 || ti.textureCompression != TextureImporterCompression.Uncompressed)) { ti.sRGBTexture = false; ti.mipmapEnabled = false; ti.maxTextureSize = 4096; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var m = new Material(Shader.Find("SkyPrison/Intro/BlackFlame"));
        m.SetTexture("_Mask", mask); m.SetFloat("_Duration", 14f);
        return m;
    }

    // 实际时间 → 着色器时间：前摇 0.5 秒（从火种开始，对应着色器 0.5–7s；火焰在约 0.035s 窜出小火，匀速长到大火（约 0.43s），大火只留约 0.08s 即爆炸），爆炸之后加快 2.2 倍（LOGO 不拖沓）
    static float Warp(float t) { return SkyPrisonBlackFlameTimeline.Warp(t); }

    static Texture2D RenderFrame(Material m, float t, int w, int h)
    {
        m.SetFloat("_T", Warp(t)); m.SetFloat("_Aspect", w / (float)h);
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(null, rt, m);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false, true);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    [MenuItem("天空囚笼/片头/渲染黑焰草稿（联系表）", false, 900)]
    public static void ContactSheet()
    {
        var m = MakeMat(); float[] ts = { 0.75f, 0.80f, 0.85f, 0.92f, 1.05f, 1.4f };
        const int W = 640, H = 360; var sheet = new Texture2D(W * 3, H * 2, TextureFormat.RGB24, false, true);
        for (int i = 0; i < ts.Length; i++)
        {
            var f = RenderFrame(m, ts[i], W, H);
            sheet.SetPixels((i % 3) * W, (1 - i / 3) * H, W, H, f.GetPixels()); Object.DestroyImmediate(f);
        }
        sheet.Apply(); File.WriteAllBytes(Dir + "/Draft_ContactSheet.png", sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet); Object.DestroyImmediate(m); AssetDatabase.Refresh();
        Debug.Log("[BlackFlame] 草稿已输出：" + Dir + "/Draft_ContactSheet.png");
    }

    [MenuItem("天空囚笼/片头/渲染黑焰片头（PNG 序列 4K 60fps）", false, 901)]
    public static void Sequence()
    {
        var m = MakeMat(); const int W = 3840, H = 2160, Fps = 60; int n = Mathf.RoundToInt(Duration * Fps);
        string outDir = "Library/BlackFlameSeq"; Directory.CreateDirectory(outDir);
        for (int i = 0; i < n; i++)
        {
            if (EditorUtility.DisplayCancelableProgressBar("渲染黑焰片头", i + " / " + n, i / (float)n)) break;
            var f = RenderFrame(m, i / (float)Fps, W, H); File.WriteAllBytes(outDir + "/frame_" + i.ToString("D4") + ".png", f.EncodeToPNG()); Object.DestroyImmediate(f);
        }
        EditorUtility.ClearProgressBar(); Object.DestroyImmediate(m);
        Debug.Log("[BlackFlame] PNG 序列已输出：" + Path.GetFullPath(outDir));
    }

    [MenuItem("天空囚笼/片头/渲染黑焰预览序列（低清 15fps）", false, 902)]
    public static void PreviewSequence()
    {
        var m = MakeMat(); const int W = 640, H = 360, Fps = 30; int n = Mathf.RoundToInt(Duration * Fps);
        string outDir = "Library/BlackFlamePreview"; if (Directory.Exists(outDir)) Directory.Delete(outDir, true); Directory.CreateDirectory(outDir);
        for (int i = 0; i < n; i++)
        {
            var f = RenderFrame(m, i / (float)Fps, W, H); File.WriteAllBytes(outDir + "/frame_" + i.ToString("D4") + ".png", f.EncodeToPNG()); Object.DestroyImmediate(f);
        }
        Object.DestroyImmediate(m);
        Debug.Log("[BlackFlame] 预览序列已输出：" + Path.GetFullPath(outDir) + "（" + n + " 帧）");
    }
}
