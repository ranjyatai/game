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
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace sc.stylizedgrass.editor
{
    public static class TemplateParser
    {
        public const int SHADER_GENERATOR_VERSION_MAJOR = 1;
        public const int SHADER_GENERATOR_MINOR = 0;
        public const int SHADER_GENERATOR_PATCH = 0;
        
        //Converts relative include paths such as (../../Libraries/File.hlsl) to an absolute path
        //Supports the source file being part of a package
        public static string RelativeToAbsoluteIncludePath(string filePath, string relativePath)
        {
            string fileDir = Path.GetDirectoryName(filePath);

            //Count how many folders should be traversed up
            int levels = relativePath.Split(new[]
            {
                ".."
            }, StringSplitOptions.None).Length - 1;

            string traveledPath = fileDir;
            if (levels > 0)
            {
                for (int i = 0; i < levels; i++)
                {
                    //Remove the number of needed sub-directories needed to reach the destination
                    int strimStart = traveledPath.LastIndexOf(Path.DirectorySeparatorChar);
                    traveledPath = traveledPath.Remove(strimStart);
                }
            }

            //The directory without the "up" navigators
            string relativeFolder = relativePath.Replace("../", string.Empty);

            //Concatenate them together
            string absolutePath = traveledPath + "/" + relativeFolder;

            //Convert back- to forward slashes
            absolutePath = absolutePath.Replace("\\", "/");

            return absolutePath;
        }

        //Pre-process the template to inject additional template contents into it
        private static void ModifyTemplate(ref string[] lines, GrassShaderImporter importer)
        {
            StringBuilder templateBuilder = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                //Inject additional passes into template
                if (line.Contains("%passes%"))
                {
                    List<Object> passes = new List<Object>(importer.settings.additionalPasses);
                    
                    int passCount = passes.Count;
                    for (int j = 0; j < passCount; j++)
                    {
                        if (passes[j] != null)
                        {
                            string filePath = AssetDatabase.GetAssetPath(passes[j]);

                            importer.RegisterDependency(filePath);
                            
                            string[] passContexts = File.ReadAllLines(filePath);

                            for (int k = 0; k < passContexts.Length; k++)
                            {
                                templateBuilder.AppendLine(passContexts[k]);
                            }
                        }
                    }
                    
                    continue;
                }
                
                templateBuilder.AppendLine(lines[i]);
            }
            lines = templateBuilder.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        }
        
        public static string CreateShaderCode(string templatePath, ref string[] lines, GrassShaderImporter importer)
        {
            if (importer == null)
            {
                throw new Exception("Failed to compile shader from template code. The importer is invalid, this should not even be possible. Whatever you did, undo it...");
            }
            
            RendererIntegration.Integration rendererIntegration = importer.GetRendererIntegration();

            AssetInfo.VersionChecking.CheckUnityVersion();
            
            //Shader name
            string shaderName = $"Stylized Grass/{importer.settings.shaderName}";

            string shaderPath = importer.assetPath;
            
            ModifyTemplate(ref lines, importer);
            
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                //Ignore blank lines and comments for analysis
                if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("//"))
                {
                    sb.AppendLine(lines[i]);
                    continue;
                }

                //First non-space character
                int indent = System.Text.RegularExpressions.Regex.Match(lines[i], "[^-\\s]").Index;

                string whitespace = lines[i].Replace(lines[i].Substring(indent), "");

                //AppendLine using previous line's white spacing
                void AddLine(string source)
                {
                    sb.AppendLine(source.Insert(0, whitespace));
                }

                //Remove whitespaces
                string line = lines[i].Remove(0, indent);

                bool Matches(string source) { return string.CompareOrdinal(source, line) == 0; }

                if (Matches("%asset_version%"))
                {
                    AddLine($"//Asset version {AssetInfo.INSTALLED_VERSION}");
                    continue;
                }

                if (Matches("%compiler_version%"))
                {
                    AddLine($"//Shader generator version: {new Version(SHADER_GENERATOR_VERSION_MAJOR, SHADER_GENERATOR_MINOR, SHADER_GENERATOR_PATCH)}");
                    continue;
                }
                
                if (Matches("%unity_version%"))
                {
                    AddLine($"//Unity version: {AssetInfo.VersionChecking.GetUnityVersion()}");
                    continue;
                }

                if (Matches("%shader_name%"))
                {
                    AddLine($"Shader \"{shaderName}\"");
                    continue;
                }

                if (Matches("%tags%"))
                {
                    if (rendererIntegration.asset == RendererIntegration.Assets.NatureRenderer)
                    {
                        AddLine("\"NatureRendererInstancing\" = \"True\"");
                    }
                    
                    continue;
                }

                if (Matches("%custom_directives%"))
                {
                    foreach (GrassShaderImporter.Directive directive in importer.settings.customIncludeDirectives)
                    {
                        if(directive.enabled == false) continue;
                        
                        string directivePrefix = string.Empty;

                        switch (directive.type)
                        {
                            case GrassShaderImporter.Directive.Type.define:
                                directivePrefix = "#define ";
                                break;
                            case GrassShaderImporter.Directive.Type.include:
                                directivePrefix = "#include ";
                                break;
                            case GrassShaderImporter.Directive.Type.include_with_pragmas:
                                directivePrefix = "#include_with_pragmas ";
                                break;
                            case GrassShaderImporter.Directive.Type.pragma:
                                directivePrefix = "#pragma ";
                                break;
                        }

                        if (directive.value != string.Empty) AddLine($"{directivePrefix}{directive.value}");
                    }
                    continue;
                }

                if (Matches("%pragma_target%"))
                {
                    AddLine("#pragma target 3.5");

                    continue;
                }

                if (Matches("%pragma_renderers%"))
                {
                    //AddLine("#pragma exclude_renderers gles");
                    AddLine(line);
                    
                    continue;
                }
                
                if (line.StartsWith("#include "))
                {
                    string includePath = line.Replace("#include ", string.Empty);
                    //Remove parenthesis
                    includePath = includePath.Replace("\"", string.Empty);

                    importer.RegisterDependency(includePath);
                }

                if (Matches("%instancing_options%"))
                {
                    if (rendererIntegration.instancing_options != string.Empty)
                    {
                        AddLine($"#pragma instancing_options {rendererIntegration.instancing_options}");
                    }

                    continue;
                }

                if (Matches("%define_renderer_integration%"))
                {
                    if(rendererIntegration.asset != RendererIntegration.Assets.None)
                        AddLine($"#define {rendererIntegration.asset.ToString()}");
                    
                    continue;
                }
                
                /* include FogLibrary */
                if (Matches("%include_renderer_integration_library%"))
                {
                    //Default until otherwise valid
                    line = string.Empty;

                    //Mark the asset integration as being compiled in
                    importer.configurationState.rendererIntegration = rendererIntegration.asset;
                    
                    if (rendererIntegration.asset != RendererIntegration.Assets.None)
                    {
                        string includePath = AssetDatabase.GUIDToAssetPath(rendererIntegration.libraryGUID);

                        importer.RegisterDependency(includePath);

                        //Not found error
                        if (includePath == string.Empty)
                        {
                            if (EditorUtility.DisplayDialog(AssetInfo.ASSET_NAME,
                                rendererIntegration.name + " fog shader library could not be found with the GUID \"" + rendererIntegration.libraryGUID + "\".\n\n" +
                                "This means it was changed by the author (rare!), you deleted the \".meta\" file at some point, or the asset simply isn't installed.", "Ok"))
                            {

                            }
                        }
                        else
                        {
                            var pragma = rendererIntegration.includeWithPragmas ? "include_with_pragmas" : "include";
                            line = $"#{pragma} \"{includePath}\"";

                            AddLine(line);
                            continue;
                        }
                    }
                }
                
                //Shaders created using "ShaderUtil.CreateShaderAsset" don't exist in a literal sense. Hence any relative file paths are invalid
                //Convert them to absolute file paths
                //Bonus: moving a shader file (or its folder) triggers it to re-import, thus always keeping the file path up-to-date
                if (line.StartsWith("#include_library") || line.StartsWith("#include_library_with_pragmas"))
                {
                    bool pragmas = line.StartsWith("#include_library_with_pragmas");
                    string relativePath = line.Replace(pragmas ? "#include_library_with_pragmas " : "#include_library ", string.Empty);
                    //Remove parenthesis
                    relativePath = relativePath.Replace("\"", string.Empty);

                    string includePath = RelativeToAbsoluteIncludePath(shaderPath, relativePath);

                    line = $"#{(pragmas ? "include_with_pragmas" : "include")} \"{includePath}\"";

                    importer.RegisterDependency(includePath);

                    AddLine(line);
                    continue;
                }

                //Insert whitespace back in
                line = line.Insert(0, whitespace);

                //Nothing special, keep whatever line this is
                sb.AppendLine(line);
            }

            //Convert to separate lines again
            lines = sb.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            //Convert to single string, respecting line breaks and spacing.
            return String.Join(Environment.NewLine, lines);
        }
    }
}