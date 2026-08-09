#if !BOXOPHOBIC_DEVELOPMENT

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Globalization;
using UnityEngine.Rendering;
using Boxophobic.Utility;

namespace AtmosphericHeightFog
{
    [InitializeOnLoad]
    public class HeightFogInstaller
    {
        static HeightFogInstaller()
        {
            EditorApplication.update += OnInit;
        }

        static void OnInit()
        {
            EditorApplication.update -= OnInit;

            var installer = AssetDatabase.GUIDToAssetPath("d85b7a5a3a1a0994fb2d8fe27364cf75");

            if (!File.Exists(installer))
	        {
                return;
	        }

            var symbol = "ATMOSPHERIC_HEIGHT_FOG";
            var version = AssetDatabase.GUIDToAssetPath("41b457a34c9fb7f45a332c79a90945b5");

            var assetFolder = version.Replace("/Core/Editor/Version.asset", "");
            var userFolder = BoxoUtils.GetUserFolder();

            var assetVersion = SettingsUtils.LoadSettingsData(version, "00");

            var projectData = GetProjectData();

            if (projectData.pipeline != "Standard")
            {
                var pipelinePackagePath = assetFolder + "/Core/Pipelines/" + projectData.pipeline + " " + projectData.package + ".unitypackage";

                if (File.Exists(pipelinePackagePath))
                {
                    AssetDatabase.ImportPackage(pipelinePackagePath, false);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    Debug.Log("<b>[Atmospheric Height Fog]</b> " + projectData.pipeline + " Render Pipeline " + projectData.package + " support is imported for Atmospheric Height Fog!");
                }
                else
                {
                    Debug.Log("<b>[Atmospheric Height Fog]</b> " + projectData.pipeline + " Render Pipeline " + projectData.package + " package cannot be found! Make sure to re-import the asset and keep the .unitypackage files!");
                }
            }
            else
            {
                Debug.Log("<b>[Atmospheric Height Fog]</b> " + projectData.pipeline + " Render Pipeline support is imported for Atmospheric Height Fog!");
            }

            SettingsUtils.SaveSettingsData(userFolder + "/User/Atmospheric Height Fog/Pipeline.asset", projectData.pipeline);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BoxoUtils.SetDefineSymbol(symbol);

            AssetDatabase.DeleteAsset(installer);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static BoxoUtils.ProjectData GetProjectData()
        {
            var projectData = new BoxoUtils.ProjectData();

            string pipeline = "Standard";

            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                if (GraphicsSettings.defaultRenderPipeline.GetType().ToString().Contains("Universal"))
                {
                    pipeline = "Universal";
                }

                if (GraphicsSettings.defaultRenderPipeline.GetType().ToString().Contains("HD"))
                {
                    pipeline = "High Definition";
                }
            }

            if (QualitySettings.renderPipeline != null)
            {
                if (QualitySettings.renderPipeline.GetType().ToString().Contains("Universal"))
                {
                    pipeline = "Universal";
                }

                if (QualitySettings.renderPipeline.GetType().ToString().Contains("HD"))
                {
                    pipeline = "High Definition";
                }
            }

            projectData.pipeline = pipeline;

            var version = Application.unityVersion;

            if (version.Contains("a") || version.Contains("b"))
            {
                projectData.isAlphaOrBetaRelease = true;
            }

            version = version.Replace("f", "x").Replace("a", "x").Replace("b", "x");

            if (pipeline != "Standard")
            {
                var versionSplit = version.Split(".");

                var version0 = int.Parse(versionSplit[0], CultureInfo.InvariantCulture);
                var version1 = int.Parse(versionSplit[1], CultureInfo.InvariantCulture);
                var version2Split = versionSplit[2].Split("x");
                var version2 = int.Parse(version2Split[0], CultureInfo.InvariantCulture);

                projectData.package = "NONE";

                if (version0 == 2022)
                {
                    if (version1 == 3)
                    {
                        projectData.package = "2022.3+";
                    }
                }

                if (version0 >= 6000)
                {
                    projectData.package = "6000.0+";
                }
            }

            return projectData;
        }
    }
}

#endif


