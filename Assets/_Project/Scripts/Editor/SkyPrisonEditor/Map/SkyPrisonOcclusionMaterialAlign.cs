using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 让遮挡合成材质和普通材质的外观参数保持一致——切换材质时角色裸露部分的色调不能变。
///
/// 角色被遮挡时，UnitOcclusionMaterialReceiver 会把整个 Renderer 的材质从
/// Axia_base_Material（Spine-Skeleton）换成 M_Player_OcclusionComposite
/// （SpineOcclusionComposite）。两个着色器里环境色调那套的计算是逐行相同的，
/// 但材质各存各的值，一旦不同步，换材质那一瞬间色调就会跳。
///
/// 只同步「两个着色器都声明、且语义相同」的外观属性。明确不碰：
/// - Hologram / HiddenOutline：只画在 hidden 分支，和裸露部分无关，是刻意的效果
/// - _MaskThreshold：两个着色器里含义不同（合成用于遮挡遮罩），复制会破坏遮挡
/// - _OccludedBrightness / _OccludedSaturation：没有任何着色器读，是死值
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonOcclusionMaterialAlign
{
    private const string LogPrefix = "[SkyPrison OcclusionAlign]";
    private const string VersionKey = "SkyPrison.OcclusionMaterialAlign.Version";
    private const int Version = 3;

    private const string NormalMaterialPath =
        "Assets/_Project/Art/Spine/Axia/Axia_base_Material.mat";

    /// <summary>两个着色器共有、语义相同的外观参数。</summary>
    private static readonly string[] SharedFloats =
    {
        "_SkyPrison_EnvTintStrength",
        "_SkyPrison_EnvDarken",
        "_SkyPrison_EnvSaturation",
        "_SkyPrison_EnvContrast",
        "_SkyPrison_EnvExposure",
        "_SkyPrison_EnvShadowTintStrength",
        "_SkyPrison_ShadowMaskStrength",
        "_SkyPrison_AlphaCleanupCutoff",
        "_SkyPrison_AlphaCleanupFeather",
        "_SkyPrison_AlphaCleanupPower",
        "_SkyPrison_OcclusionAlpha",
        "_StraightAlphaInput",
    };

    private static readonly string[] SharedColors = { "_SkyPrison_EnvTint" };
    private static readonly string[] SharedTextures = { "_SkyPrison_ShadowMask" };

    /// <summary>被我上一版误关掉的全息填充，恢复原值。它只作用于被遮住的像素。</summary>
    private static readonly (string name, float value)[] HologramRestore =
    {
        ("_SkyPrison_UseHologramFill", 1f),
        ("_SkyPrison_HologramAlpha", 0.75f),
        ("_SkyPrison_HologramSilhouetteAlpha", 0f),
        ("_SkyPrison_HologramGridBright", 1.4f),
    };

    static SkyPrisonOcclusionMaterialAlign()
    {
        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version)
            return;

        EditorPrefs.SetInt(VersionKey, Version);
        Run();
    }

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/遮挡材质外观对齐普通材质")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Run;
            return;
        }

        var normal = AssetDatabase.LoadAssetAtPath<Material>(NormalMaterialPath);
        if (normal == null)
        {
            Debug.LogWarning($"{LogPrefix} 找不到普通材质：{NormalMaterialPath}，无法对齐。");
            return;
        }

        var report = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null)
                continue;
            if (!mat.shader.name.Contains("SpineOcclusionComposite"))
                continue;

            var lines = new List<string>();

            foreach (var (name, value) in HologramRestore)
                if (SetFloat(mat, name, value, lines))
                    { }

            foreach (string p in SharedFloats)
            {
                // 普通材质没序列化的属性 → 它在用着色器默认值。合成着色器的同名属性
                // 默认值是一样的（两边都从 Spine-Skeleton 抄过来的），所以读 normal
                // 的运行时值就是正确目标，不需要区分"存了没存"。
                if (normal.HasProperty(p) && mat.HasProperty(p))
                    SetFloat(mat, p, normal.GetFloat(p), lines);
            }

            foreach (string p in SharedColors)
                if (normal.HasProperty(p) && mat.HasProperty(p))
                    SetColor(mat, p, normal.GetColor(p), lines);

            foreach (string p in SharedTextures)
                if (normal.HasProperty(p) && mat.HasProperty(p))
                    SetTexture(mat, p, normal.GetTexture(p), lines);

            if (lines.Count == 0)
                continue;

            EditorUtility.SetDirty(mat);
            report.Add($"  {path}\n    " + string.Join("\n    ", lines));
        }

        AssetDatabase.SaveAssets();

        if (report.Count == 0)
        {
            Debug.Log($"{LogPrefix} 已经对齐，没有需要改的。");
            return;
        }

        Debug.Log(
            $"{LogPrefix} 已对齐 {report.Count} 个遮挡合成材质的外观参数：\n" +
            string.Join("\n", report));
    }

    private static bool SetFloat(Material mat, string name, float value, List<string> lines)
    {
        if (!mat.HasProperty(name))
            return false;
        float old = mat.GetFloat(name);
        if (Mathf.Approximately(old, value))
            return false;

        Undo.RecordObject(mat, "Align Occlusion Material");
        mat.SetFloat(name, value);
        lines.Add($"{name}: {old} -> {value}");
        return true;
    }

    private static void SetColor(Material mat, string name, Color value, List<string> lines)
    {
        Color old = mat.GetColor(name);
        if (old == value)
            return;

        Undo.RecordObject(mat, "Align Occlusion Material");
        mat.SetColor(name, value);
        lines.Add($"{name}: {old} -> {value}");
    }

    private static void SetTexture(Material mat, string name, Texture value, List<string> lines)
    {
        if (mat.GetTexture(name) == value)
            return;

        Undo.RecordObject(mat, "Align Occlusion Material");
        mat.SetTexture(name, value);
        lines.Add($"{name}: -> {(value != null ? value.name : "null")}");
    }
}
