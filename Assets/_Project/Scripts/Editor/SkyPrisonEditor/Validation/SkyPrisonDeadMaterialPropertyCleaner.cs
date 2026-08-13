using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 清理材质上「着色器已经不声明」的残留属性。
///
/// 着色器迭代时从 Properties 块里删掉一个属性，材质里那条序列化数据不会自动消失。
/// 它对运行时零影响，但在排查时极具误导性——2026-08-13 我按
/// _OccludedBrightness / _OccludedSaturation 追了很久，最后才发现没有任何着色器读它们；
/// 同一天还被 _DebugMode、_SkyPrison_LightTint 带偏过。首次扫描发现 56 处。
///
/// ================== 安全前提 ==================
/// 这个操作会删除资产数据，有一个必须防住的风险：
/// 着色器编译失败或丢失时，GetPropertyCount() 会返回 0 或极小值，
/// 那时候「着色器不声明的属性」＝全部属性，会把材质清空。
/// 所以每个材质动手前都要先确认着色器有效，任何可疑迹象一律跳过。
///
/// 另外只碰 Assets/_Project 下、且属于项目自有前缀的属性——
/// 第三方资产的历史残留不归我们管，动它们只会带来无法预期的风险。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonDeadMaterialPropertyCleaner
{
    private const string LogPrefix = "[SkyPrison 死属性清理]";
    private const string DryRunVersionKey = "SkyPrison.DeadMaterialProperty.DryRunVersion";
    private const int DryRunVersion = 1;

    static SkyPrisonDeadMaterialPropertyCleaner()
    {
        EditorApplication.delayCall += AutoDryRunOnce;
    }

    /// <summary>
    /// 只自动跑 dry run，永远不自动删除。
    ///
    /// 检测是只读的、可以自动化；删除依赖着色器的运行时状态，编译失败时判据会失效
    /// （见 IsShaderTrustworthy），必须有人在场。用静默破坏去解决静默失败是本末倒置。
    /// </summary>
    private static void AutoDryRunOnce()
    {
        if (EditorPrefs.GetInt(DryRunVersionKey, 0) >= DryRunVersion)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += AutoDryRunOnce;
            return;
        }

        EditorPrefs.SetInt(DryRunVersionKey, DryRunVersion);
        Execute(dryRun: true);
    }
    private static readonly string[] OwnPrefixes = { "_SkyPrison", "_SP_", "_Occluded" };

    /// <summary>属性总数低于这个值就认为着色器状态可疑，不做清理。</summary>
    private const int MinPlausiblePropertyCount = 4;

    [MenuItem("Tools/Sky Prison/校验/清理材质死属性", priority = 2)]
    public static void Run() => Execute(dryRun: false);

    [MenuItem("Tools/Sky Prison/校验/清理材质死属性（只列出不删）", priority = 3)]
    public static void DryRun() => Execute(dryRun: true);

    private static void Execute(bool dryRun)
    {
        var removed = new List<string>();
        var skipped = new List<string>();
        int materialsTouched = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                continue;

            if (!IsShaderTrustworthy(mat, path, skipped))
                continue;

            var declared = new HashSet<string>();
            int count = mat.shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
                declared.Add(mat.shader.GetPropertyName(i));

            var so = new SerializedObject(mat);
            int before = removed.Count;

            foreach (string arrayName in new[]
                     { "m_SavedProperties.m_Floats", "m_SavedProperties.m_Colors", "m_SavedProperties.m_TexEnvs" })
            {
                SerializedProperty arr = so.FindProperty(arrayName);
                if (arr == null || !arr.isArray)
                    continue;

                // 倒序删除：正序会让后面的下标整体前移，删到一半就开始删错条目。
                for (int i = arr.arraySize - 1; i >= 0; i--)
                {
                    string name = arr.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("first")?.stringValue;

                    if (string.IsNullOrEmpty(name) || declared.Contains(name))
                        continue;
                    if (!OwnPrefixes.Any(name.StartsWith))
                        continue;

                    removed.Add($"{System.IO.Path.GetFileName(path)} → {name}");
                    if (!dryRun)
                        arr.DeleteArrayElementAtIndex(i);
                }
            }

            if (removed.Count == before)
                continue;

            materialsTouched++;
            if (!dryRun)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mat);
            }
        }

        if (!dryRun && materialsTouched > 0)
            AssetDatabase.SaveAssets();

        Report(removed, skipped, materialsTouched, dryRun);
    }

    /// <summary>
    /// 着色器可信才动手。任何一条不满足都跳过——宁可漏清，不可误删。
    /// </summary>
    private static bool IsShaderTrustworthy(Material mat, string path, List<string> skipped)
    {
        if (mat.shader == null)
        {
            skipped.Add($"{System.IO.Path.GetFileName(path)}（着色器丢失）");
            return false;
        }

        // 着色器编译失败时 Unity 会把它替换成内部错误着色器，属性表是空的。
        if (mat.shader.name.Contains("InternalErrorShader") || mat.shader.name.Contains("Hidden/InternalError"))
        {
            skipped.Add($"{System.IO.Path.GetFileName(path)}（着色器编译失败）");
            return false;
        }

        int count = mat.shader.GetPropertyCount();
        if (count < MinPlausiblePropertyCount)
        {
            skipped.Add($"{System.IO.Path.GetFileName(path)}（着色器只报告 {count} 个属性，状态可疑）");
            return false;
        }

        return true;
    }

    private static void Report(List<string> removed, List<string> skipped, int materialsTouched, bool dryRun)
    {
        var sb = new StringBuilder();

        if (removed.Count == 0)
        {
            sb.AppendLine($"{LogPrefix} 没有发现死属性。");
        }
        else
        {
            sb.AppendLine(
                $"{LogPrefix} {(dryRun ? "发现" : "已清理")} {removed.Count} 处死属性，" +
                $"涉及 {materialsTouched} 个材质：");
            foreach (string r in removed.Take(30))
                sb.AppendLine($"  {r}");
            if (removed.Count > 30)
                sb.AppendLine($"  …… 其余 {removed.Count - 30} 处");
        }

        if (skipped.Count > 0)
        {
            sb.AppendLine($"跳过 {skipped.Count} 个材质（着色器状态不可信，宁可漏清不误删）：");
            foreach (string s in skipped.Take(10))
                sb.AppendLine($"  {s}");
        }

        if (dryRun && removed.Count > 0)
            sb.AppendLine("这是只列不删。确认无误后跑「清理材质死属性」。");

        Debug.Log(sb.ToString());
    }
}
