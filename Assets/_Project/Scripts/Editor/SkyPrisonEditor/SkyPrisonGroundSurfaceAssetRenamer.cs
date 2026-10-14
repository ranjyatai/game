using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把地表材质定义的资产文件名整理成 GSM_中文名。
///
/// 新建定义时文件名是自动生成的（GSM_new_ground_surface_1_copy_copy 这种），
/// 而 Inspector 里只显示 displayName，两者对不上。在 Project 窗口里找一个材质
/// 得逐个点开看，复制粘贴出来的 _copy 链更是完全看不出谁是谁。
///
/// 只改文件名。surfaceId 不动——它是查找键（SkyPrisonGroundAudioSurfaceResolver
/// 用它兜底匹配 TerrainLayer 名字，UnitFootstepAudioEmitter 用它匹配旧版音声包
/// 绑定），改了会断现有绑定。displayName 也不动，那是你自己填的。
///
/// 走 AssetDatabase.RenameAsset，GUID 和所有引用都保留。
/// </summary>
public static class SkyPrisonGroundSurfaceAssetRenamer
{
    private const string Prefix = "GSM_";

    [MenuItem("天空囚笼/地面/按中文名整理地表材质文件名", false, 146)]
    public static void RenameByDisplayName()
    {
        string[] guids = AssetDatabase.FindAssets("t:GroundSurfaceMaterialDefinition");
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("地表材质", "没有找到任何地表材质定义。", "确定");
            return;
        }

        // 先算出每个资产的目标名，重名的加序号。用已占用集合而不是"第二个才加序号"，
        // 避免把一个本来就叫 GSM_马路 的资产挤掉。
        var planned = new List<(string path, string current, string target)>();
        var taken = new HashSet<string>();

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var def = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
            if (def == null) continue;

            string current = System.IO.Path.GetFileNameWithoutExtension(path);
            string baseName = Prefix + Sanitize(def.displayName);

            // displayName 为空时保留原名，不要生成一堆 GSM_ 空名
            if (baseName == Prefix)
            {
                taken.Add(current);
                continue;
            }

            string target = baseName;
            int n = 2;
            while (taken.Contains(target))
                target = baseName + "_" + n++;

            taken.Add(target);
            if (target != current)
                planned.Add((path, current, target));
        }

        if (planned.Count == 0)
        {
            EditorUtility.DisplayDialog("地表材质", $"检查了 {guids.Length} 个定义，文件名都已经是规范的，无需改动。", "确定");
            return;
        }

        var preview = new StringBuilder();
        for (int i = 0; i < planned.Count && i < 12; i++)
            preview.AppendLine($"{planned[i].current}  →  {planned[i].target}");
        if (planned.Count > 12)
            preview.AppendLine($"…… 以及另外 {planned.Count - 12} 个");

        if (!EditorUtility.DisplayDialog("地表材质：整理文件名",
                $"将重命名 {planned.Count} 个资产（共 {guids.Length} 个定义）：\n\n{preview}\n" +
                "只改文件名，surfaceId / displayName / GUID / 引用全部保留。",
                "执行", "取消"))
            return;

        int ok = 0;
        var failures = new List<string>();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < planned.Count; i++)
            {
                string error = AssetDatabase.RenameAsset(planned[i].path, planned[i].target);
                if (string.IsNullOrEmpty(error))
                    ok++;
                else
                    failures.Add($"{planned[i].current} → {planned[i].target}：{error}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        var log = new StringBuilder($"[地表材质] 重命名完成：成功 {ok} / {planned.Count}。");
        for (int i = 0; i < failures.Count; i++)
            log.Append("\n  失败：").Append(failures[i]);

        if (failures.Count > 0)
            Debug.LogWarning(log.ToString());
        else
            Debug.Log(log.ToString());
    }

    /// <summary>去掉 Windows 文件名非法字符，并压掉首尾空白。</summary>
    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw.Trim())
        {
            if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' ||
                c == '"' || c == '<' || c == '>' || c == '|')
                sb.Append('-');
            else
                sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
