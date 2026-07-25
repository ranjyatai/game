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

using UnityEditor.AssetImporters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace sc.stylizedgrass.editor
{
    [ScriptedImporter(TemplateParser.SHADER_GENERATOR_VERSION_MAJOR + TemplateParser.SHADER_GENERATOR_MINOR + TemplateParser.SHADER_GENERATOR_PATCH, TARGET_FILE_EXTENSION, 0)]
    public class GrassShaderImporter : ScriptedImporter
    {
        private const string TARGET_FILE_EXTENSION = "grassshader";
        private const string ICON_NAME = "grass-shader-icon";
        internal const string DEFAULT_SHADER_GUID = "f6c3aa64fdb1460d928f40a04c112292";
        
        [Tooltip("Rather than storing the template in this file, it can be sourced from an external text file" +
                 "\nUse this if you intent to duplicate this asset, and need only minor modifications to its import settings")]
        [SerializeField] public LazyLoadReference<Object> template;

        [Space]

        public GrassShaderSettings settings = new GrassShaderSettings();

        /// <summary>
        /// File paths of any file this shader depends on. This list will be populated with any "#include" paths present in the template
        /// Registering these as dependencies is required to trigger the shader to recompile when these files are changed
        /// </summary>
        //[NonSerialized] //Want to keep these serialized. Will differ per-project, which also causes the file to appear as changed for every project when updating the asset (this triggers a re-import)
        public List<string> dependencies = new List<string>();

        [Serializable]
        //Keep track of what was being compiled in
        //Used to detect discrepencies between the project state, and the compiled shader
        public class ConfigurationState
        {
            public RendererIntegration.Assets rendererIntegration;

            public void Reset()
            {
                rendererIntegration = RendererIntegration.Assets.None;
            }
        }
        public ConfigurationState configurationState = new ConfigurationState();
        
        public string GetTemplatePath()
        {
            return template.isSet ? AssetDatabase.GetAssetPath(template.asset) : assetPath;
        }

        private void OnValidate()
        {
            if(settings.shaderName == string.Empty) settings.shaderName = $"{Application.productName} ({DateTime.Now.Ticks})";
        }

        public override void OnImportAsset(AssetImportContext context)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(context.assetPath);
            //if (shader != null) ShaderUtil.ClearShaderMessages(shader);

            string templatePath = GetTemplatePath();

            if (templatePath == string.Empty)
            {
                Debug.LogError("Failed to import grass shader, template file path is null. It possibly hasn't been imported first?", shader);
                return;
            }
            
            #if SGS_DEV
            Stopwatch sw = new Stopwatch();
            sw.Start();
            #endif
            
            string[] lines = File.ReadAllLines(templatePath);

            if (lines.Length == 0)
            {
                Debug.LogError("Failed to generated grass shader. Template or file content is empty (or wasn't yet imported)...");
                return;
            }

            dependencies.Clear();

            configurationState.Reset();
            
            string shaderLab = TemplateParser.CreateShaderCode(context.assetPath, ref lines, this);
            
            Shader shaderAsset = ShaderUtil.CreateShaderAsset(shaderLab, true);
            
            int passCount = shaderAsset.passCount;

            ShaderInfo shaderInfo = ShaderUtil.GetShaderInfo(shaderAsset);
            ShaderData shaderData = ShaderUtil.GetShaderData(shaderAsset);
            
            //Unity will always create 3 base passes: Unnamed, DepthNormalsOnly & DepthOnly
            if (shaderInfo.hasErrors && shaderData.GetSubshader(0).GetPass(0).Name.Contains("Unnamed"))
            {
                Debug.LogError($"Failed to compile grass shader at {context.assetPath}. It contains no passes. " +
                               $"This may happen if the shader file was imported while one or more script compile errors were present, or moving the Stylized Water 3 folder, or the meta-file was deleted. Resulting in all configurations getting wiped. To resolve this, re-import the file from the Package Manager.");
                return;
            }
            ShaderUtil.RegisterShader(shaderAsset);
            
            Texture2D thumbnail = Resources.Load<Texture2D>(ICON_NAME);
            if(!thumbnail) thumbnail = EditorGUIUtility.IconContent("ShaderImporter Icon").image as Texture2D;
            
            context.AddObjectToAsset("MainAsset", shaderAsset, thumbnail);
            context.SetMainObject(shaderAsset);
            
            //Set up dependency, so that changes to the template triggers shaders to regenerate
            if (template.isSet && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(template, out var guid, out long _))
            {
                //Note: this strictly only works when adding the file path!
                //context.DependsOnArtifact(guid);
                
                dependencies.Insert(0, AssetDatabase.GUIDToAssetPath(guid));
            }

            //Dependencies are populated during the template parsing phase.
            foreach (string dependency in dependencies)
            {
                context.DependsOnSourceAsset(dependency);
            }
            
            #if SGS_DEV
            sw.Stop();
            //Debug.Log($"Imported \"{Path.GetFileNameWithoutExtension(assetPath)}\" grass shader in {sw.Elapsed.Milliseconds}ms. With {dependencies.Count} dependencies.", shader);
            #endif
        }
        
        public bool RequiresRecompilation(out string message)
        {
            bool isValid = configurationState.rendererIntegration == GetRendererIntegration().asset;

            message = string.Empty;
            
            if (isValid == false)
            {
                message += $"\nFog integration does not match.\nInstalled: {configurationState.rendererIntegration} - Detected in project: {GetRendererIntegration().name}";
            }
            
            return !isValid;
        }
        
        public static Shader GetDefaultShader()
        {
            string defaultShaderPath = AssetDatabase.GUIDToAssetPath(DEFAULT_SHADER_GUID);
            Shader defaultShader = AssetDatabase.LoadAssetAtPath<Shader>(defaultShaderPath);

            return defaultShader;
        }
        
        public static ShaderMessage[] GetErrorMessages(Shader shader)
        {
            ShaderMessage[] messages = null;

            int n = ShaderUtil.GetShaderMessageCount(shader);

            if (n < 1) return messages;
            
            messages = ShaderUtil.GetShaderMessages(shader);
            
            //Filter for errors
            messages = messages.Where(x => x.severity == ShaderCompilerMessageSeverity.Error).ToArray();

            return messages;
        }

        public void Reimport()
        {
            this.SaveAndReimport();
        }

        public void ClearCache(bool recompile = false)
        {
            var objs = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            
            foreach (var obj in objs)
            {
                if (obj is Shader)
                {
                    ShaderUtil.ClearShaderMessages((Shader)obj);
                    ShaderUtil.ClearCachedData((Shader)obj);
                    
                    if(recompile) AssetDatabase.ImportAsset(assetPath);
                    
                    #if SGS_DEV
                    Debug.Log($"Cleared cache for {obj.name}");
                    #endif
                }
            }
        }
        public void RegisterDependency(string dependencyAssetPath)
        {
            if (dependencyAssetPath.StartsWith("Packages/") == false)
            {
                string guid = AssetDatabase.AssetPathToGUID(dependencyAssetPath);

                if (guid == string.Empty)
                {
                    //Also throws an error for things like '#include_library "SurfaceModifiers/SurfaceModifiers.hlsl"', which are wrapped in an #ifdef. That's a false positive
                    //Debug.LogException(new Exception($"Tried to import \"{this.assetPath}\" with an missing dependency, supposedly at path: {dependencyAssetPath}."));
                    return;
                }
            }

            //Tessellation variant pass may run, causing the same dependencies to be registered twice, hence check first
            if(dependencies.Contains(dependencyAssetPath) == false) dependencies.Add(dependencyAssetPath);
        }
        
        //Handles correct behaviour when double-clicking a .watershader asset. Should open in the IDE
        [UnityEditor.Callbacks.OnOpenAsset]
#if UNITY_6000_3_OR_NEWER
        public static bool OnOpenAsset(EntityId instanceID, int line)
#else
        public static bool OnOpenAsset(int instanceID, int line)
#endif
        {
            #if UNITY_6000_3_OR_NEWER
            Object target = EditorUtility.EntityIdToObject(instanceID);
            EntityId id = (EntityId)instanceID;
            #else
            Object target = EditorUtility.InstanceIDToObject(instanceID);
            int id = instanceID;
            #endif

            if (target is Shader)
            {
                var path = AssetDatabase.GetAssetPath(id);
                
                if (Path.GetExtension(path) != "." + TARGET_FILE_EXTENSION) return false;

                string externalScriptEditor = ScriptEditorUtility.GetExternalScriptEditor();
                
                if (externalScriptEditor != "internal" && externalScriptEditor != string.Empty)
                {
                    InternalEditorUtility.OpenFileAtLineExternal(path, 0);
                }
                else
                {
                    Application.OpenURL("file://" + path);
                }
                
                return true;
            }
            
            return false;
        }

        public static GrassShaderImporter GetForShader(Shader shader)
        {
            return AssetImporter.GetAtPath(AssetDatabase.GetAssetOrScenePath(shader)) as GrassShaderImporter;
        }

        public Shader GetShader()
        {
            return AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
        }

        [Serializable]
        public class Directive
        {
            public enum Type
            {
                [InspectorName("(no prefix)")]
                custom,
                [InspectorName("#include")]
                include,
                [InspectorName("#pragma")]
                pragma,
                [InspectorName("#include_with_pragmas")]
                include_with_pragmas,
                [InspectorName("#define")]
                define
            }
            public bool enabled = true;
            public Type type;
            public string value;

            public Directive(Type _type, string _value)
            {
                this.type = _type;
                this.value = _value;
            }
        }

        public static string[] FindAllAssets()
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(Application.dataPath);
            
            FileInfo[] fileInfos = directoryInfo.GetFiles("*." + TARGET_FILE_EXTENSION, SearchOption.AllDirectories);
            
            #if SGS_DEV
            //Debug.Log($"{fileInfos.Length} .{TARGET_FILE_EXTENSION} assets found");
            #endif

            string[] filePaths = new string[fileInfos.Length];

            for (int i = 0; i < filePaths.Length; i++)
            {
                filePaths[i] = fileInfos[i].FullName.Replace(@"\", "/").Replace(Application.dataPath, "Assets");
            }

            return filePaths;
        }
        
        #if SGS_DEV
        [MenuItem("SGS/Reimport grass shaders")]
        #endif
        public static void ReimportAll()
        {
            string[] filePaths = FindAllAssets();
            foreach (var filePath in filePaths)
            {
                #if SGS_DEV
                //Debug.Log($"Reimporting: {filePath}");
                #endif
                AssetDatabase.ImportAsset(filePath);
            }
        }

        public RendererIntegration.Integration GetRendererIntegration()
        {
            return settings != null && settings.autoIntegration ? RendererIntegration.GetFirstInstalled() : RendererIntegration.GetIntegration(settings.rendererIntegration);
        }

        [Serializable]
        public class GrassShaderSettings
        {
            [Tooltip("How it will appear in the selection menu")]
            public string shaderName;

            [Tooltip("Before compiling the shader, check whichever asset is present in the project and activate its integration")]
            public bool autoIntegration = true;
            public RendererIntegration.Assets rendererIntegration = RendererIntegration.Assets.None;
            
            public List<Directive> customIncludeDirectives = new List<Directive>();
            [Tooltip("Pass blocks that are to be added to the shader template")]
            public Object[] additionalPasses = new Object[0];
        }
    }
}