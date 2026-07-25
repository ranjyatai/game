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

using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEditor.AssetImporters;

namespace sc.stylizedgrass.editor
{
    [CustomEditor(typeof(GrassShaderImporter))]
    [CanEditMultipleObjects]
    public class WaterShaderImporterEditor : ScriptedImporterEditor
    {
        private GrassShaderImporter importer;

        private SerializedProperty template;

        private SerializedProperty settings;

        private SerializedProperty shaderName;

        private SerializedProperty autoIntegration;
        private SerializedProperty rendererIntegration;

        private SerializedProperty customIncludeDirectives;
        private SerializedProperty additionalPasses;
        
        private SerializedProperty configurationState;
        
        private RendererIntegration.Integration firstIntegration;
        private bool curvedWorldInstalled;

        private bool showDependencies;

        private ShaderData shaderData;
        
        public override void OnEnable()
        {
            base.OnEnable();
            
            firstIntegration = RendererIntegration.GetFirstInstalled();
            //curvedWorldInstalled = StylizedWaterEditor.CurvedWorldInstalled(out var _);

            importer = (GrassShaderImporter)target;

            template = serializedObject.FindProperty("template");

            settings = serializedObject.FindProperty("settings");
            //settings.isExpanded = true;

            shaderName = settings.FindPropertyRelative("shaderName");
            
            autoIntegration = settings.FindPropertyRelative("autoIntegration");
            rendererIntegration = settings.FindPropertyRelative("rendererIntegration");

            customIncludeDirectives = settings.FindPropertyRelative("customIncludeDirectives");
            additionalPasses = settings.FindPropertyRelative("additionalPasses");
            
            configurationState = serializedObject.FindProperty("configurationState");

            Shader shader = importer.GetShader();
            if (shader != null)
            {
                shaderData = ShaderUtil.GetShaderData(shader);
            }
        }

        public override bool HasPreviewGUI()
        {
            //Hide the useless sphere preview :)
            return false;
        }

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            Color defaultColor = GUI.contentColor;

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(importer.assetPath);
            /*
            UI.DrawHeader();

            if (shader == null)
            {
                UI.DrawNotification("Shader failed to compile, try to manually recompile it now.", MessageType.Error);
            }

            UI.DrawNotification(EditorSettings.asyncShaderCompilation == false, "Asynchronous shader compilation is disabled in the Editor settings." +
                                                                                "\n\n" +
                                                                                "This will very likely cause the editor to crash when trying to compile this shader (D3D11 Swapchain error).", "Enable", () =>
            {
                EditorSettings.asyncShaderCompilation = true;
            }, MessageType.Error);
            */
            
            if (GUILayout.Button(new GUIContent("  Recompile", EditorGUIUtility.IconContent("RotateTool").image), GUILayout.MinHeight(30f)))
            {
                importer.Reimport();
                return;
            }

            GUILayout.Space(-2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(shader == null))
                {
                    if (GUILayout.Button(new GUIContent("  Show Generated Code", EditorGUIUtility.IconContent("align_horizontally_left_active").image), EditorStyles.miniButtonLeft, GUILayout.Height(28f)))
                    {
                        OpenGeneratedCode();
                    }
                    if (GUILayout.Button(new GUIContent("Clear cache", "Unity's shader compiler will cache the compiled shader, and internally use that." +
                                                                       "\n\nThis may result in seemingly false-positive shader errors. Such as in the case of importing the shader, before the URP shader libraries are." +
                                                                       "\n\nClearing the cache gives the compiler a kick, and makes the shader properly represent the current state of the project/dependencies."), EditorStyles.miniButtonRight, GUILayout.Height(28f)))
                    {
                        importer.ClearCache();
                    }
                }
            }

            EditorGUILayout.Space();

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(template);

            if (template.objectReferenceValue == null) EditorGUILayout.HelpBox("• Template is assumed to be in the contents of the file itself", MessageType.None);
            //EditorGUILayout.LabelField(importer.GetTemplatePath(), EditorStyles.miniLabel);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(shaderName);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Integrations", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(autoIntegration, new GUIContent("Automatic detection", autoIntegration.tooltip));
            if (autoIntegration.boolValue)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("Renderer", GUILayout.MaxWidth(EditorGUIUtility.labelWidth));
                    EditorGUI.indentLevel--;

                    using (new EditorGUILayout.HorizontalScope(EditorStyles.textField))
                    {
                        GUI.contentColor = Color.green;
                        EditorGUILayout.LabelField(firstIntegration.name);

                        GUI.contentColor = defaultColor;
                    }
                }

                if (curvedWorldInstalled)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField("Curved World 2020", GUILayout.MaxWidth(EditorGUIUtility.labelWidth));
                        EditorGUI.indentLevel--;

                        using (new EditorGUILayout.HorizontalScope(EditorStyles.textField))
                        {
                            GUI.contentColor = Color.green;
                            EditorGUILayout.LabelField("Installed");
                            GUI.contentColor = defaultColor;
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.PropertyField(rendererIntegration);
            }
            if (curvedWorldInstalled) EditorGUILayout.HelpBox("Curved World integration must be manually activated through minor code changes, see documentation.", MessageType.Info);
            
            EditorGUILayout.Space();

            /*
            EditorGUILayout.LabelField("Functionality support", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(lightCookies);
            EditorGUILayout.PropertyField(additionalLightCaustics);
            EditorGUILayout.PropertyField(additionalLightTranslucency);
            EditorGUILayout.PropertyField(singleCausticsLayers);
            

            EditorGUILayout.Space();
            */

            EditorGUILayout.PropertyField(customIncludeDirectives);
            if (customIncludeDirectives.isExpanded)
            {
                EditorGUILayout.HelpBox("These are defined in a HLSLINCLUDE block and apply to all passes" +
                                        "\nMay be used to insert custom code.", MessageType.Info);
            }
            EditorGUILayout.PropertyField(additionalPasses);
            if (additionalPasses.isExpanded)
            {
                EditorGUILayout.LabelField("Compiled passes:", EditorStyles.miniBoldLabel);
                if (shaderData != null)
                {
                    ShaderData.Subshader subShader = shaderData.GetSubshader(0);
                    int passCount = subShader.PassCount;
                    for (int i = 0; i < passCount; i++)
                    {
                        EditorGUILayout.LabelField($"{i}: {subShader.GetPass(i).Name}", EditorStyles.miniLabel);
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                //Force the parameter to a matching value.
                //This way, if the "auto-integration" option is used, the .meta file will be changed when using the shader in a package, spanning different projects.
                //When switching a different project, the file will be seen as changed and will be re-imported, in turn applying the project-specific integration.
                if (autoIntegration.boolValue)
                {
                    rendererIntegration.intValue = (int)firstIntegration.asset;
                }

                serializedObject.ApplyModifiedProperties();
            }

            this.ApplyRevertGUI();

            showDependencies = EditorGUILayout.BeginFoldoutHeaderGroup(showDependencies, $"Dependencies ({importer.dependencies.Count})");

            if (showDependencies)
            {
                this.Repaint();

                using (new EditorGUILayout.VerticalScope(EditorStyles.textArea))
                {
                    foreach (string dependency in importer.dependencies)
                    {
                        var rect = EditorGUILayout.BeginHorizontal(EditorStyles.miniLabel);

                        if (rect.Contains(Event.current.mousePosition))
                        {
                            EditorGUIUtility.AddCursorRect(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 27, 27), MouseCursor.Link);
                            EditorGUI.DrawRect(rect, Color.gray * (EditorGUIUtility.isProSkin ? 0.66f : 0.20f));
                        }

                        if (GUILayout.Button(dependency == string.Empty ? new GUIContent(" (Missing)", EditorGUIUtility.IconContent("console.warnicon.sml").image) : new GUIContent(" " + dependency, EditorGUIUtility.IconContent("TextAsset Icon").image),
                                EditorStyles.miniLabel, GUILayout.Height(20f)))
                        {
                            if (dependency != string.Empty)
                            {
                                TextAsset file = AssetDatabase.LoadAssetAtPath<TextAsset>(dependency);

                                EditorGUIUtility.PingObject(file);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.HelpBox("Should any of these files be modified/moved/deleted, this shader will also re-import", MessageType.Info);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            //EditorGUILayout.PropertyField(configurationState);
            
            /*
            UI.DrawFooter();

            if (shader)
            {
                UI.DrawNotification(ShaderUtil.ShaderHasError(shader), "Errors may be false-positives due to caching", "Clear cache", () => importer.ClearCache(true), MessageType.Warning);
            }
            */
        }

        void OpenGeneratedCode()
        {
            importer = (GrassShaderImporter)target;

            string tempDir = $"{Application.dataPath.Replace("Assets", string.Empty)}Temp";
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string filePath = $"{tempDir}/{importer.settings.shaderName}(Generated Code).shader";

            string templatePath = importer.GetTemplatePath();
            string[] lines = File.ReadAllLines(templatePath);

            string code = TemplateParser.CreateShaderCode(importer.GetTemplatePath(), ref lines, importer);
            File.WriteAllText(filePath, code);

            if (!File.Exists(filePath))
            {
                Debug.LogError(string.Format("Path {0} doesn't exists", filePath));
                return;
            }

            string externalScriptEditor = ScriptEditorUtility.GetExternalScriptEditor();
            if (externalScriptEditor != "internal")
            {
                InternalEditorUtility.OpenFileAtLineExternal(filePath, 0);
            }
            else
            {
                Application.OpenURL("file://" + filePath);
            }
        }
    }
}