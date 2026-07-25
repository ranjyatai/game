using System;
using sc.stylizedgrass.runtime;
using UnityEditor;
using UnityEngine;

namespace sc.stylizedgrass.editor
{
    [CustomEditor(typeof(GrassRenderFeature))]
    public class RenderFeatureInspector : Editor
    {
        private GrassRenderFeature renderFeature;
        
        private SerializedProperty enableBending;
        private SerializedProperty debugBending;
        private SerializedProperty bendingRenderRange;
        private SerializedProperty forwardPerspectiveCorrection;
        private SerializedProperty fadeAtMaxRenderingRange;
        private SerializedProperty ditheringNoise;
        private SerializedProperty animateDithering;
        private SerializedProperty ignoreSceneView;
        private SerializedProperty ignoreOverlayCamera;
        
        private void OnEnable()
        {
            renderFeature = (GrassRenderFeature)target;
            
            SerializedProperty settings = serializedObject.FindProperty("settings");
            
            enableBending = settings.FindPropertyRelative("enableBending");
            debugBending = settings.FindPropertyRelative("debugBending");
            bendingRenderRange = settings.FindPropertyRelative("bendingRenderRange");
            
            forwardPerspectiveCorrection = settings.FindPropertyRelative("forwardPerspectiveCorrection");
            
            fadeAtMaxRenderingRange = settings.FindPropertyRelative("fadeAtMaxRenderingRange");
            ditheringNoise = settings.FindPropertyRelative("ditheringNoise");
            animateDithering = settings.FindPropertyRelative("animateDithering");
            
            ignoreSceneView = settings.FindPropertyRelative("ignoreSceneView");
            ignoreOverlayCamera = settings.FindPropertyRelative("ignoreOverlayCamera");
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space();
            
            UI.DrawHeader();
            
            UI.DrawNotification(renderFeature.bendingShader == null, "A shader is not assigned to the render feature. Do not add render features in Play mode!", "Attempt fix", () =>
            {
                renderFeature.VerifyReferences();
            }, MessageType.Error);
            
            EditorGUILayout.LabelField("Bending", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableBending, new GUIContent("Enabled", enableBending.tooltip));
            if (enableBending.boolValue)
            {
                EditorGUILayout.PropertyField(bendingRenderRange, new GUIContent("Render Range", bendingRenderRange.tooltip));
                
                EditorGUILayout.HelpBox($"Active benders: {GrassBender.Instances.Count}" + (GrassBender.Instances.Count == 0 ? " (Rendering skipped)" : ""), MessageType.None, false);
                
                EditorGUILayout.PropertyField(debugBending, new GUIContent("Debug", debugBending.tooltip));
            }
            
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(forwardPerspectiveCorrection);
            
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Fading", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fadeAtMaxRenderingRange, new GUIContent("Use terrain detail distance", fadeAtMaxRenderingRange.tooltip));

            if (fadeAtMaxRenderingRange.boolValue)
            {
                Terrain mainTerrain = Terrain.activeTerrain;

                if (mainTerrain)
                {
                    EditorGUILayout.HelpBox($"Using the detail distance ({mainTerrain.detailObjectDistance}) from the terrain \"{mainTerrain.name}\".", MessageType.None, false);
                }
                else
                {
                    EditorGUILayout.HelpBox("No terrain is active, feature won't be used at the moment.", MessageType.None, false);
                }
                
                EditorGUILayout.Separator();
            }
            EditorGUILayout.PropertyField(ditheringNoise);
            if (ditheringNoise.objectReferenceValue)
            {
                Texture2D noiseTex = (Texture2D)ditheringNoise.objectReferenceValue;
                
                Rect rect = EditorGUILayout.GetControlRect();
                rect.x = EditorGUIUtility.labelWidth + 17f;
                rect.width = 64;
                rect.height = 64;
                EditorGUI.DrawPreviewTexture(rect, noiseTex);
                
                EditorGUILayout.Space(rect.height + 10f);
            }
            EditorGUILayout.PropertyField(animateDithering);
            
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(ignoreSceneView);
            EditorGUILayout.PropertyField(ignoreOverlayCamera);
            
            UI.DrawFooter();
        }
    }
}