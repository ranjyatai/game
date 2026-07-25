using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonTerrainDecorationPlacementRouteAudit
{
    private static readonly string[] Keywords =
    {
        "PlaceSelectedDefinition",
        "地图对象放置工具",
        "SkyPrisonTerrainDecorationPlacementPage",
        "SkyPrisonMapObjectPlacementToolWindow",
        "InstantiateConnectedRuntimeTemplate",
        "SkyPrisonTerrainDecorationRuntimeTemplateUtility",
        "CorrectBackTriggerToStartNearRuleCenter",
        "ApplyDefinition();",
        "new GameObject(",
        "[TD_PLACE_ACTIVE]",
        "[NO_PF]"
    };

    [MenuItem("Tools/Sky Prison/Map/地形装饰物/诊断/扫描实际放置入口")]
    public static void Run()
    {
        string assetsRoot = Application.dataPath.Replace('\\', '/');
        string projectRoot = Directory.GetParent(assetsRoot).FullName.Replace('\\', '/');
        string[] files = Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories);

        StringBuilder report = new StringBuilder(64 * 1024);
        report.AppendLine("Sky Prison Terrain Decoration Placement Route Audit");
        report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("Project: " + projectRoot);
        report.AppendLine();
        report.AppendLine("Purpose:");
        report.AppendLine("- Find which C# files still contain PF_TD runtime-template placement, old BackTrigger correction, or actual placement entry points.");
        report.AppendLine("- This script only scans. It does not modify project files.");
        report.AppendLine();

        int hitFileCount = 0;
        foreach (string file in files)
        {
            string normalized = file.Replace('\\', '/');
            string rel = normalized.StartsWith(projectRoot, StringComparison.Ordinal)
                ? normalized.Substring(projectRoot.Length + 1)
                : normalized;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file, Encoding.UTF8);
            }
            catch
            {
                try { lines = File.ReadAllLines(file); }
                catch { continue; }
            }

            List<string> hits = new List<string>();
            int score = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                for (int k = 0; k < Keywords.Length; k++)
                {
                    string kw = Keywords[k];
                    if (line.IndexOf(kw, StringComparison.Ordinal) >= 0)
                    {
                        int weight = 1;
                        if (kw == "InstantiateConnectedRuntimeTemplate" || kw == "SkyPrisonTerrainDecorationRuntimeTemplateUtility") weight = 20;
                        if (kw == "CorrectBackTriggerToStartNearRuleCenter") weight = 20;
                        if (kw == "PlaceSelectedDefinition") weight = 12;
                        if (kw == "ApplyDefinition();") weight = 8;
                        if (kw == "new GameObject(") weight = 6;
                        if (kw == "[TD_PLACE_ACTIVE]" || kw == "[NO_PF]") weight = 15;

                        score += weight;
                        hits.Add("  L" + (i + 1).ToString() + " [" + kw + "] " + line.Trim());
                    }
                }
            }

            if (hits.Count == 0)
                continue;

            hitFileCount++;
            report.AppendLine("============================================================");
            report.AppendLine("Score " + score.ToString("D4") + "  " + rel);
            foreach (string hit in hits)
                report.AppendLine(hit);
            report.AppendLine();
        }

        report.AppendLine("============================================================");
        report.AppendLine("Hit files: " + hitFileCount);
        report.AppendLine();
        report.AppendLine("How to read:");
        report.AppendLine("- If a file contains InstantiateConnectedRuntimeTemplate or RuntimeTemplateUtility, it is still using PF_TD runtime-template placement.");
        report.AppendLine("- If a file contains CorrectBackTriggerToStartNearRuleCenter, it can overwrite the new projected occlusion correction.");
        report.AppendLine("- The actual placement entry is likely the highest-score file containing PlaceSelectedDefinition or the window/menu title.");

        string logDir = Path.Combine(assetsRoot, "_Project/EditorLogs").Replace('\\', '/');
        Directory.CreateDirectory(logDir);
        string outPath = Path.Combine(logDir, "TerrainDecoration_PlacementRouteAudit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt").Replace('\\', '/');
        File.WriteAllText(outPath, report.ToString(), Encoding.UTF8);

        AssetDatabase.Refresh();
        Debug.Log("[TerrainDecoration Placement Route Audit] 完成。命中文件数: " + hitFileCount + "。报告: " + outPath);
        EditorUtility.RevealInFinder(outPath);
    }
}
