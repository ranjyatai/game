// Stylized Grass Shader © Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//
// ⚠️ WARNING: UNAUTHORIZED USE OR DISTRIBUTION IS STRICTLY PROHIBITED
// • Copying, referencing, or reverse-engineering this source code for the creation of new Asset Store or derivative products,
//   or any other publicly distributed content is strictly forbidden and will result in legal action.
// • Studying this file for the purpose of reproducing its functionality in your own assets or tools is not permitted.
// • If you are viewing this file as a reference, please close it immediately to avoid unintentional design influence or potential EULA violations.
// • Uploading this file or any derivative of it to a public GitHub or similar repository will trigger an automated DMCA takedown request.
// • Studying to understand for personal, educational or integration purposes is allowed, studying to reproduce is not.

using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace sc.stylizedgrass.editor
{
    public static class RendererIntegration
    {
        public enum Assets
        {
            [InspectorName("None")]
            None,
            [InspectorName("Vegetation Studio (Beyond)")]
            VegetationStudio,
            [InspectorName("GPU Instancer Pro")]
            GPUInstancer,
            [InspectorName("Nature Renderer (Unity 6)")]
            NatureRenderer,
            [InspectorName("Foliage Renderer")]
            FoliageRenderer
        }

        [Serializable]
        public class Integration
        {
            public string name;
            public Assets asset;
            public Texture2D thumbnail;
            public int id;
            public string libraryGUID;
            public string instancing_options;
            public bool includeWithPragmas;
            public bool installed;

            public Integration(string name, Assets asset, int id, string guid, bool includeWithPragmas, string instancingOptions)
            {
                this.name = name;
                this.asset = asset;
                this.id = id;
                this.libraryGUID = guid;
                this.includeWithPragmas = includeWithPragmas;
                this.instancing_options = instancingOptions;

                this.installed = IsLibraryPresent(this);
            }
        }

        private static Integration[] _Integrations;
        public static Integration[] Integrations
        {
            get
            {
                if (_Integrations == null) _Integrations = GetAvailableIntegrations();
                return _Integrations;
            }
        }

        private static Integration[] GetAvailableIntegrations()
        {
            Integration[] integrationsArray = new[]
            {
                new Integration("Default Unity", Assets.None, 0, "", false,string.Empty),
                new Integration("Vegetation Studio (Beyond)", Assets.VegetationStudio, 0, "a9324aff8d6fb7746847dbf6108e0382", false, "assumeuniformscaling renderinglayer procedural:setupVSPro"),
                new Integration("Nature Renderer 6", Assets.NatureRenderer, 285950,"ca4c4574fc8ceab448f85800842a6cee", false, "procedural:SetupNatureRenderer"),
                new Integration("GPU Instancer Pro", Assets.GPUInstancer, 290293, "01c16f02afaf429591046b0d8007c478", true, "procedural:setupGPUI"),
                new Integration("Foliage Renderer", Assets.FoliageRenderer, 307081, "7f684950130464f4c86c65052b7c92c8", false, "procedural:setupFoliageRenderer forwardadd"),
            };

            for (int i = 0; i < integrationsArray.Length; i++)
            {
                integrationsArray[i].installed = IsLibraryPresent(integrationsArray[i]);
            }

            return integrationsArray;
        }

        public static Integration GetIntegration(Assets asset)
        {
            for (int i = 0; i < Integrations.Length; i++)
            {
                if (Integrations[i].asset == asset) return Integrations[i];
            }

            return null;
        }

        public static bool IsLibraryPresent(Integration integration)
        {
            if (integration.asset == Assets.None) return true;

            string path = AssetDatabase.GUIDToAssetPath(integration.libraryGUID);
            
            if(path == string.Empty) return false;

            return AssetDatabase.LoadAssetAtPath(path, typeof(Object));
        }

        public static Integration GetFirstInstalled()
        {
            for (int i = 0; i < Integrations.Length; i++)
            {
                //Always installed anyway
                if (Integrations[i].asset == Assets.None) continue;
                
                if (IsLibraryPresent(Integrations[i]))
                {
                    return Integrations[i];
                }
            }

            //No third-party assets installed, default to Unity
            return GetIntegration(Assets.None);
        }
    }
}