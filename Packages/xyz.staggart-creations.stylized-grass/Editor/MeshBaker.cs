using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
#if FBX_EXPORTER
using UnityEditor.Formats.Fbx.Exporter;
#endif

namespace sc.stylizedgrass.editor
{
    public static class MeshBaker
    {
        /// <summary>
        /// Converts all meshes in the prefabs to use a height gradient. Then exports them to a new FBX file and updates the prefab to use the new meshes.
        /// </summary>
        /// <param name="prefabs"></param>
        /// <param name="targetFolder"></param>
        public static void ConvertPrefabs(GameObject[] prefabs, string targetFolder)
        {
#if FBX_EXPORTER
            foreach (var prefab in prefabs)
            {
                GameObject target = prefab;
               
                //Load the prefab source
                GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(target);
                string sourcePath = AssetDatabase.GetAssetPath(target);
                
                //Start editing the source prefab
                string prefabSourcePath = string.IsNullOrEmpty(sourcePath) ? AssetDatabase.GetAssetPath(prefabSource) : sourcePath;
                PrefabStage prefabStage = PrefabStageUtility.OpenPrefab(prefabSourcePath, null, PrefabStage.Mode.InIsolation);
                
                //Get the instance in the prefab stage, not the asset
                GameObject prefabRoot = prefabStage.prefabContentsRoot;
                target = prefabRoot;
                
                MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
                Mesh[] meshes = new Mesh[meshFilters.Length];
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    Mesh mesh = meshFilters[i].sharedMesh;
                    if (mesh)
                    {
                        //Create updated mesh
                        meshes[i] = BakeHeight(mesh);
                        
                        //Assign it to the prefab
                        meshFilters[i].mesh = meshes[i];
                    }
                    else
                    {
                        StageUtility.GoToMainStage();
                        throw new Exception($"MeshFilter {meshFilters[i].name} in {target.name} is missing a mesh");
                    }
                }
                
                string filePath = $"{targetFolder}/{target.name}_Converted.fbx";
                ModelExporter.ExportObject(filePath, target);
                
                //Import
                AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceSynchronousImport);
                
                //Loading meshes and assigning them to MeshFilters
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(filePath);
                Mesh[] newMeshes = subAssets.OfType<Mesh>().ToArray();
                
                //Debug.Log($"Converted {target.name} to {filePath}");
                
                //Now assign the FBX meshes back to the source prefab
                meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
                
                for (int i = 0; i < meshFilters.Length; i++) //Skip the first sub-asset, as it is the parent
                {
                    meshFilters[i].sharedMesh = newMeshes[i];
                }
                
                //Save prefab changes
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabStage.assetPath);
                StageUtility.GoToMainStage();
            }
            
            AssetDatabase.SaveAssets();
#endif
        }
        
        public static Mesh BakeHeight(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;

            bool hasVertexColors = colors.Length > 0;
            if(hasVertexColors == false) colors = new Color[vertices.Length];
            
            float height = mesh.bounds.size.y;
            
            int vertexCount = vertices.Length;
            for (int v = 0; v < vertexCount; v++)
            {
                float yGradient = (vertices[v].y / height);
                
                //Retain any baked in ambient occlusion
                colors[v] = new Color(hasVertexColors ? colors[v].r : yGradient, yGradient, yGradient, 1);
            }
            
            Mesh newMesh = new Mesh();
            EditorUtility.CopySerialized(mesh, newMesh);
            
            newMesh.SetColors(colors, 0, vertexCount, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            newMesh.name += "(GrassShader)";

            return newMesh;
        }
        
        public class Window : EditorWindow
        {
            private bool acknowledged;
            
            private string destinationFolder = "Assets/Models/Grass/";
            private readonly List<GameObject> prefabList = new List<GameObject>();
            private Vector2 scrollPosition = Vector2.zero;

            private GameObject[] selection = Array.Empty<GameObject>();
            
            [MenuItem("Window/Stylized Grass/Prefab Converter", false, 2000)]
            public static void ShowWindow()
            {
                Window window = GetWindow<Window>("Prefab Converter");
                window.minSize = new Vector2(300, 600);
                window.Show();
            }

            private void OnEnable()
            {
                Selection.selectionChanged += UpdateSelection;
                UpdateSelection();
            }

            private void OnGUI()
            {
                EditorGUILayout.Space(10f);
                
                UI.DrawHeader();
                
                EditorGUILayout.Space(10f);

                // Check for FBX Exporter package
                if (!HasFBXExporter())
                {
                    UI.DrawNotification(
                        "The FBX Exporter package is required but not installed. To use this functionality install it through the Package Manager.",
                        MessageType.Error
                    );
                    EditorGUILayout.Space(10f);

                    if (GUILayout.Button("Install", GUILayout.Height(30f)))
                    {
                        Installer.InstallPackage("com.unity.formats.fbx");
                    }
                    return;
                }

                if (acknowledged == false)
                {
                    UI.DrawNotification("This utility will:" +
                                        "\n• Bake a height gradient into all meshes in the selected prefabs (vertex color RGB)." +
                                        "\n• Export the meshes to a new FBX file" +
                                        "\n• Update the prefab to use the new meshes." +
                                        "\n\n" +
                                        "This baking process is required for correct shading, wind animations and bending behaviour.", MessageType.Info);
                    
                    UI.DrawNotification("Use of a version control system is recommended to keep track of changes made to the prefabs, and possibly revert them", MessageType.Warning);
                    
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("I understand"))
                        {
                            acknowledged = true;
                        }
                    }
                    
                    EditorGUILayout.Space(10f);
                }
                else
                {
                    //Destination Folder Section
                    UI.DrawH2("Export Settings");

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Destination Folder:", EditorStyles.label, GUILayout.Width(130f));
                        destinationFolder = EditorGUILayout.TextField(destinationFolder, GUILayout.ExpandWidth(true));

                        if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                        {
                            BrowseForFolder();
                        }
                    }

                    EditorGUILayout.Space(10f);

                    //Prefab List Section
                    UI.DrawH2("Target Prefabs");

                    using (new EditorGUI.DisabledGroupScope(destinationFolder == string.Empty))
                    {
                        EditorGUILayout.Space(5f);

                        //Prefab list with scroll view
                        using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPosition,
                                   EditorStyles.textArea, GUILayout.Height(200f)))
                        {
                            scrollPosition = scrollView.scrollPosition;

                            if (prefabList.Count == 0)
                            {
                                EditorGUILayout.HelpBox("No prefabs added. Click 'Add Selection' to add prefabs from the project.", MessageType.Info);
                            }
                            else
                            {
                                for (int i = prefabList.Count - 1; i >= 0; i--)
                                {
                                    using (new EditorGUILayout.HorizontalScope(EditorStyles.textField))
                                    {
                                        EditorGUILayout.ObjectField(prefabList[i], typeof(GameObject), false);

                                        if (GUILayout.Button("×", GUILayout.Width(30f)))
                                        {
                                            prefabList.RemoveAt(i);
                                        }
                                    }
                                }
                            }
                        }

                        EditorGUILayout.Space(10f);

                        using (new EditorGUI.DisabledGroupScope(selection.Length == 0))
                        {
                            //Add Selection Button
                            if (GUILayout.Button($"Add Selection ({selection.Length})", GUILayout.Height(30f)))
                            {
                                AddSelectedPrefabs();
                            }
                        }

                        if (selection.Length == 0 && Selection.objects.Length > 0)
                        {
                            EditorGUILayout.HelpBox("Only Prefab objects are accepted", MessageType.Info);
                        }

                        EditorGUILayout.Space(20f);

                        //Convert Button
                        using (new EditorGUI.DisabledScope(prefabList.Count == 0))
                        {
                            if (GUILayout.Button("Convert", GUILayout.Height(35f)))
                            {
                                MeshBaker.ConvertPrefabs(prefabList.ToArray(), destinationFolder);

                                EditorUtility.DisplayDialog("Success", $"Converted {prefabList.Count} prefab(s)", "OK");

                                prefabList.Clear();
                            }
                        }
                    }

                    EditorGUILayout.Space(10f);
                }
                
                UI.DrawFooter();
            }

            private static bool HasFBXExporter()
            {
                #if FBX_EXPORTER
                return true;
                #else
                return false;
                #endif
            }
            
            private void BrowseForFolder()
            {
                string selected = EditorUtility.OpenFolderPanel("Select Export Folder", destinationFolder, "");
                
                if (!string.IsNullOrEmpty(selected))
                {
                    //Convert absolute path to relative path within Assets
                    if (selected.StartsWith(Application.dataPath))
                    {
                        destinationFolder = "Assets" + selected.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        destinationFolder = selected;
                    }
                    
                    this.Repaint();
                }
            }

            private void UpdateSelection()
            {
                List<GameObject> m_selection = new List<GameObject>();
                foreach (var obj in Selection.objects)
                {
                    if (obj is GameObject == false) continue;

                    GameObject gameObject = obj as GameObject;

                    PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(gameObject);
                    bool isPrefab = prefabType == PrefabAssetType.Regular && prefabType != PrefabAssetType.Model;
                    GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(gameObject);
                    isPrefab |= prefabSource != null;

                    if (isPrefab && selection.Contains(gameObject) == false)
                    {
                        m_selection.Add(gameObject);
                    }
                }
                
                selection = m_selection.ToArray();

                this.Repaint();
            }
            
            private void AddSelectedPrefabs()
            {
                foreach (var obj in selection)
                {
                    if (!prefabList.Contains(obj))
                    {
                        prefabList.Add(obj);
                    }
                }

                if (Selection.objects.Length == 0)
                {
                    EditorUtility.DisplayDialog("No Selection", "Please select one or more prefabs in the project window.", "OK");
                }
            }
        }
    }
}