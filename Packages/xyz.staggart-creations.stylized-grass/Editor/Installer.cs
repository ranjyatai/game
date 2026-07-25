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
using System.Collections.Generic;
using System.Linq;
using sc.stylizedgrass.runtime;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace sc.stylizedgrass.editor
{
    //Verifies any and all project settings and states
    public static class Installer
    {
        public const string OLD_SHADER_NAME = "Universal Render Pipeline/Nature/Stylized Grass";
        
        public class SetupItem
        {
            public MessageType state = MessageType.None;
            public string name;
            public string description;
            public string actionName;
            
            public Action action;

            public SetupItem(string name)
            {
                this.name = name;
            }
            
            public SetupItem(string name, MessageType state)
            {
                this.name = name;
                this.state = state;
            }
            

            public void ExecuteAction()
            {
                action.Invoke();
                action = null;

                if (state == MessageType.Error)
                {
                    m_errorCount--;
                }
                else if (state == MessageType.Warning)
                {
                    m_warningCount--;
                }

                state = MessageType.None;
                
                Installer.Initialize();
            }
        }
        
        public static List<SetupItem> SetupItems = new List<SetupItem>();

        public static int ErrorCount
        {
            get => m_errorCount;
        }
        private static int m_errorCount;
        public static int WarningCount
        {
            get => m_warningCount;
        }
        private static int m_warningCount;
        
        public static bool HasError => ErrorCount > 0;

        private static void AddItem(SetupItem item)
        {
            if (item.state == MessageType.Error) m_errorCount++;
            else if (item.state == MessageType.Warning) m_warningCount++;
            
            SetupItems.Add(item);
        }
        
        public static void Initialize()
        {
            SetupItems.Clear();
            m_errorCount = 0;
            m_warningCount = 0;

            AssetInfo.VersionChecking.CheckUnityVersion();
            SetupItem unityVersion = new SetupItem(AssetInfo.VersionChecking.GetUnityVersion());
            {
                unityVersion.state = MessageType.None;
                unityVersion.description = $"Likely compatible and supported Unity version";
                
                //Too old
                if (AssetInfo.VersionChecking.compatibleVersion == false || AssetInfo.VersionChecking.supportedPatchVersion == false)
                {
                    unityVersion.state = MessageType.Error;
                    unityVersion.description = $"This version of Unity is not compatible and is not subject to support. Update to at least <b>{AssetInfo.VersionChecking.MinRequiredUnityVersion}</b>. Errors and issues need to be resolved locally";
                }
                else
                {
                    //Too broken
                    if (AssetInfo.VersionChecking.unityVersionType != AssetInfo.VersionChecking.UnityVersionType.Release)
                    {
                        unityVersion.state = MessageType.Warning;
                        unityVersion.description = "Alpha/preview versions of Unity are not supported. Shader/script errors or warnings may occur depending on which weekly-version you are using." +
                                                   "\n\n" +
                                                   "After the release version becomes available, compatibility will be verified and an update may follow with the necessary fixes/changes.";
                    }

                    //Too new
                    if (AssetInfo.VersionChecking.supportedMajorVersion == false)
                    {
                        unityVersion.state = MessageType.Warning;
                        unityVersion.description = $"This version of Unity is not supported. An upgrade version of this asset that does support this may be available. See the documentation for up-to-date information.";
                    }
                }
            }
            AddItem(unityVersion);

            AssetInfo.VersionChecking.CheckForUpdate();
            SetupItem assetVersion = new SetupItem($"Asset version ({AssetInfo.INSTALLED_VERSION})");
            {
                //Testing
                //AssetInfo.VersionChecking.compatibleVersion = false;
                //AssetInfo.VersionChecking.alphaVersion = true;
                //AssetInfo.VersionChecking.UPDATE_AVAILABLE = false;
                
                if (AssetInfo.VersionChecking.UPDATE_AVAILABLE)
                {
                    assetVersion.state = MessageType.Info;
                    assetVersion.description = $"An updated version is available (v{AssetInfo.VersionChecking.LATEST_VERSION})" +
                        "\n\n" +
                        "Asset can be updated through the Package Manager. Please update any extensions as well!";

                    assetVersion.actionName = "Open Package Manager";
                    assetVersion.action = AssetInfo.OpenInPackageManager;
                }
                else
                {
                    assetVersion.state = MessageType.None;
                    assetVersion.description = "Installed version is the latest";
                }
            }
            AddItem(assetVersion);

            SetupItem graphicsAPI = new SetupItem($"Graphics API ({PlayerSettings.GetGraphicsAPIs(EditorUserBuildSettings.activeBuildTarget)[0].ToString()})");
            {
                graphicsAPI.state = MessageType.None;
                graphicsAPI.description = $"Compatible";
            }
            AddItem(graphicsAPI);
            
            SetupItem colorSpace = new SetupItem($"Color space ({PlayerSettings.colorSpace.ToString()})");
            {
                if (PlayerSettings.colorSpace == ColorSpace.Linear)
                {
                    colorSpace.state = MessageType.None;
                    colorSpace.description = $"Linear";
                }
                else
                {
                    colorSpace.state = MessageType.Warning;
                    colorSpace.description = $"All content is authored for the Linear color space, colors will not look as advertised.";
                }
            }
            AddItem(colorSpace);
            
            SetupItem shaderCompiler = new SetupItem("Shader Compiler");
            {
                Shader defaultShader = GrassShaderImporter.GetDefaultShader();
                var shaderCompiled = defaultShader != null;
                shaderCompiler.state = shaderCompiled ? MessageType.Error : MessageType.None;
                
                //Shader object created
                if (shaderCompiled)
                {
                    shaderCompiler.state = MessageType.None;
                    shaderCompiler.description = "Shader compiled without any errors";

                    ShaderMessage[] shaderErrors = GrassShaderImporter.GetErrorMessages(defaultShader);

                    //Compiled with errors
                    if (shaderErrors != null && shaderErrors.Length > 0)
                    {
                        shaderCompiler.state = MessageType.Error;
                        shaderCompiler.description = "Grass shader has one or more critical errors:\n";

                        for (int i = 0; i < shaderErrors.Length; i++)
                        {
                            shaderCompiler.description += "\n• " + $"{shaderErrors[i].message} (line:{shaderErrors[i].line})";
                        }

                        shaderCompiler.description += "\n\nThese messages may provide a clue as to what went wrong";
                        
                        shaderCompiler.actionName = "Try recompiling";
                        shaderCompiler.action = () =>
                        {
                            string defaultShaderPath = AssetDatabase.GUIDToAssetPath(GrassShaderImporter.DEFAULT_SHADER_GUID);
                            AssetDatabase.ImportAsset(defaultShaderPath);
                            
                            Initialize();
                        };
                    }
                    //Success
                    else
                    {
                        GrassShaderImporter importer = GrassShaderImporter.GetForShader(defaultShader);
                        bool requiresRecompile = importer.RequiresRecompilation(out var recompileMessage);

                        if (requiresRecompile)
                        {
                            shaderCompiler.state = MessageType.Warning;
                            shaderCompiler.description = "Grass shader needs to be recompiled:" +
                                                         "\n" +
                                                         recompileMessage +
                                                         "\n";

                            shaderCompiler.actionName = "Recompile";
                            shaderCompiler.action = () =>
                            {
                                importer.Reimport();
                                
                                Initialize();
                            };
                        }

                        //Shader is valid, but also check if any water meshes have a pink shader
                        {
                            Shader legacyShader = Shader.Find(OLD_SHADER_NAME);
                            List<Material> legacyGrassMaterials = new List<Material>();
                            
                            //Find all materials in the project using the `legacyShader` and add them to the legacyGrassMaterials list
                            string[] materialGuids = AssetDatabase.FindAssets("t:Material");
                            foreach (string guid in materialGuids)
                            {
                                string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                                    
                                if (material != null && material.shader == legacyShader)
                                {
                                    legacyGrassMaterials.Add(material);
                                }
                            }
                            
                            //Remove duplicates
                            legacyGrassMaterials = legacyGrassMaterials.Distinct().ToList();

                            if (legacyGrassMaterials.Count > 0)
                            {
                                SetupItem legacyMaterialError = new SetupItem("Grass materials using old shader");
                                legacyMaterialError.state = MessageType.Error;
                                legacyMaterialError.description = "One or more materials in the project are using the old (v1) shader.";

                                for (int i = 0; i < legacyGrassMaterials.Count; i++)
                                {
                                    legacyMaterialError.description += $"\n• {legacyGrassMaterials[i].name}";
                                }

                                legacyMaterialError.description += "\n\nThese need to switch over to the new shader.";

                                legacyMaterialError.actionName = "Fix";
                                legacyMaterialError.action = () =>
                                {
                                    for (int i = 0; i < legacyGrassMaterials.Count; i++)
                                    {
                                        legacyGrassMaterials[i].shader = defaultShader;
                                        
                                        EditorUtility.SetDirty(legacyGrassMaterials[i]);
                                    }
                                    
                                    AssetDatabase.SaveAssets();
                                    
                                    Initialize();
                                };

                                AddItem(legacyMaterialError);
                            }
                        }
                    }
                }
                else
                {
                    shaderCompiler.state = MessageType.Error;
                    shaderCompiler.description = "Shader failed to compile. Please ensure that there are no unresolved scripting compilation errors in the project." +
                                                 "\n\n" +
                                                 "After resolving them, re-import this asset";
                }
                
                AddItem(shaderCompiler);
            }
            
            SetupItem renderGraph = new SetupItem("Render Graph", PipelineUtilities.RenderGraphEnabled() ? MessageType.None : MessageType.Error);
            {
                if (renderGraph.state == MessageType.Error)
                {
                    renderGraph.description = "Disabled (Backwards Compatibility mode). The render feature (and its functionality) will be unavailable!";
                    renderGraph.actionName = "Enable";
                    renderGraph.action = () =>
                    {
                        PipelineUtilities.SetRenderGraphCompatibilityMode(false);
                    };
                }
                else
                {
                    renderGraph.state = MessageType.None;
                    renderGraph.description = "Render Graph is enabled";
                }

                AddItem(renderGraph);
            }

            SetupItem renderFeature = new SetupItem("Render Feature");
            {
                PipelineUtilities.RenderFeatureMissing<GrassRenderFeature>(out ScriptableRendererData[] renderers);
                    
                if (renderers.Length > 0)
                {
                    renderFeature.state = MessageType.Error;
                    renderFeature.description = "The Stylized Grass render feature hasn't been added to these renderers:";

                    for (int i = 0; i < renderers.Length; i++)
                    {
                        renderFeature.description += $"\n• {renderers[i].name}";
                    }

                    renderFeature.description +=
                        "\n\nThe following functionality will be unavailable if they are active:\n" +
                        "\n• Bending" +
                        "\n• Fading";

                    renderFeature.description += "\n\nFor some use cases this is intentional and this warning can be ignored";
                    
                    renderFeature.actionName = "Add to renderers";
                    renderFeature.action = () =>
                    {
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            PipelineUtilities.AddRenderFeature<GrassRenderFeature>(renderers[i], "Stylized Grass");
                        }
                    };
                }
                else
                {
                    renderFeature.state = MessageType.None;
                    renderFeature.description = "Render feature has been set up on all renderers";

                    SetupItem bendingFeature = new SetupItem("Bending");
                    {
                        GrassRenderFeature currentRenderFeature = GrassRenderFeature.GetDefault();

                        if (currentRenderFeature.settings.enableBending)
                        {
                            bendingFeature.state = MessageType.None;
                            bendingFeature.description = "Enabled on the default renderer";
                        }
                        else
                        {
                            bendingFeature.state = MessageType.Warning;
                            bendingFeature.description =
                                "Disabled on the default renderer. This makes grass bending unavailable";

                            bendingFeature.actionName = "Enable";
                            bendingFeature.action = () =>
                            {
                                currentRenderFeature = GrassRenderFeature.GetDefault();
                                currentRenderFeature.settings.enableBending = true;
                                
                                EditorUtility.SetDirty(currentRenderFeature);
                            };
                        }
                        
                        AddItem(bendingFeature);
                    }
                }

                AddItem(renderFeature);
            }

            if (SceneView.lastActiveSceneView)
            {
                SetupItem sceneViewAnimations = new SetupItem("Scene view animations");
                
                if (SceneView.lastActiveSceneView.sceneViewState.alwaysRefreshEnabled == false)
                {
                    sceneViewAnimations.state = MessageType.Warning;
                    sceneViewAnimations.description = "The \"Always Refresh\" option is disabled in the scene view. Water surface animations will appear to be jumpy";

                    sceneViewAnimations.actionName = "Enable";
                    sceneViewAnimations.action = () =>
                    {
                        SceneView.lastActiveSceneView.sceneViewState.alwaysRefresh = true;
                    };
                    AddItem(sceneViewAnimations);
                }
            }

            /*
            //Test formatting
            SetupItem error = new SetupItem("Error");
            {
                error.state = MessageType.Error;
                error.description = "An error has occured";

                error.actionName = "Fix";
                error.action = () =>
                {

                };
                AddItem(error);
            }

            SetupItem warnings = new SetupItem("Warning");
            {
                warnings.state = MessageType.Warning;
                warnings.description = "Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industry's standard dummy text ever since the 1500s, when an unknown printer took a galley of type and scrambled it to make a type specimen book.";

                warnings.actionName = "Enable";
                warnings.action = () =>
                {

                };
                AddItem(warnings);
            }
            */

            //Sort to display errors first, then warnings.
            SetupItems = SetupItems.OrderBy(o=> (o.state == MessageType.Info || o.state
                 == MessageType.None)).ToList();
        }

        public static void SetupRenderFeature()
        {
            PipelineUtilities.AddRenderFeature<GrassRenderFeature>(null, "Stylized Grass");
        }

        public static void OpenWindowIfNeeded()
        {
            if (HasError && Application.isBatchMode == false && BuildPipeline.isBuildingPlayer == false)
            {
                HelpWindow.ShowWindow(true);
            }
        }

        public static void InstallPackage(string id)
        {
            SearchRequest request = Client.Search(id, false);

            while (request.Status == StatusCode.InProgress)
            {
                /* Waiting... */
            }

            if (request.IsCompleted)
            {
                if (request.Result == null)
                {
                    Debug.LogError($"Searching for package {id} failed");
                    return;
                }

                PackageInfo packageInfo = request.Result[0];
                string packageName = packageInfo.name;
                
                //Installed in project?
                bool installed = packageInfo.resolvedPath != string.Empty;

                if (installed)
                {
                    Debug.Log($"{packageName} is already installed");
                    return;
                }
                
                Debug.Log($"Installation of package \"{packageName}\" will start in a moment...");
                AddRequest addRequest = Client.Add(packageInfo.name + "@" + packageInfo.versions.latestCompatible);
                
            }
        }
    }
}