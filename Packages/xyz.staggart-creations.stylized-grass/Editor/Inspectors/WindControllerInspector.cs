using System;
using sc.stylizedgrass.runtime;
using UnityEditor;
using UnityEngine;

namespace sc.stylizedgrass.editor
{
    [CustomEditor(typeof(WindController))]
    public class WindControllerInspector : Editor
    {
        WindController script;
        SerializedProperty windZone;

        private SerializedProperty windAmbientMultiplier;
        private SerializedProperty windGustMultiplier;
        
        private bool renderFeaturePresent;
        private void OnEnable()
        {
            script = (WindController)target;
            
            renderFeaturePresent = PipelineUtilities.RenderFeatureAdded<GrassRenderFeature>();
            
            windZone = serializedObject.FindProperty("windZone");
            windAmbientMultiplier = serializedObject.FindProperty("windAmbientMultiplier");
            windGustMultiplier = serializedObject.FindProperty("windGustMultiplier");
        }

        public override void OnInspectorGUI()
        {
            UI.DrawHeader();
            
            if (!renderFeaturePresent)
            {
                EditorGUILayout.HelpBox("The Grass Render Feature hasn't been added\nto the current renderer", MessageType.Error);

                GUILayout.Space(-32);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Add", GUILayout.Width(60)))
                    {
                        AddRenderFeature();
                    }
                    GUILayout.Space(8);
                }
                GUILayout.Space(11);
            }
            
            UI.DrawRenderGraphError();

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(windZone);

                if (!windZone.objectReferenceValue)
                {
                    if (GUILayout.Button("Add", GUILayout.MaxWidth(75f)))
                    {
                        GameObject targetObject = script.gameObject;
                        WindZone windZoneComponent = targetObject.AddComponent<WindZone>();
                        
                        windZone.objectReferenceValue = windZoneComponent;
                        
                        EditorUtility.SetDirty(targetObject);
                    }
                }
                else
                {
                    if(GUILayout.Button("Edit", GUILayout.MaxWidth(65f)))
                    {
                        EditorGUIUtility.PingObject(windZone.objectReferenceValue);
                        Selection.activeObject = windZone.objectReferenceValue;
                    }
                }
            }
            
            if (windZone.objectReferenceValue)
            {
                EditorGUILayout.Separator();
                
                EditorGUILayout.PropertyField(windAmbientMultiplier);
                EditorGUILayout.PropertyField(windGustMultiplier);
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }

            UI.DrawFooter();
        }
        
        private void AddRenderFeature()
        {
            Installer.SetupRenderFeature();
            renderFeaturePresent = true;
        }
    }
}
