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

using sc.stylizedgrass.runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if URP
using UnityEngine.Rendering.Universal;
#endif

namespace sc.stylizedgrass.editor
{
    public class StylizedGrassEditor : Editor
    {
        [MenuItem("GameObject/Effects/Grass Bender")]
        public static void CreateGrassBender()
        {
            GrassBender gb = new GameObject().AddComponent<GrassBender>();
            gb.gameObject.name = "Grass Bender";

            Selection.activeGameObject = gb.gameObject;
            EditorApplication.ExecuteMenuItem("GameObject/Move To View");
        }
        
        [MenuItem("GameObject/Effects/Grass Wind Controller")]
        public static void CreateWindController()
        {
            WindController gb = new GameObject().AddComponent<WindController>();
            gb.gameObject.name = "Grass Wind Controller";

            Selection.activeGameObject = gb.gameObject;
        }
        
        [MenuItem("Window/Stylized Grass/Open demo scene", false, 1004)]
        public static void OpenDemoScene()
        {
            string path = AssetDatabase.GUIDToAssetPath("8e1f93bc9f41c9040a419138bf1796d4");
            
            EditorSceneManager.OpenScene(path);
        }
        
        [MenuItem("Window/Stylized Grass/Setup Render Feature", false, 1003)]
        public static void SetupRenderFeature()
        {
            GrassRenderFeature renderFeature = GrassRenderFeature.GetDefault();
            
            if(!renderFeature)
            {
                Installer.SetupRenderFeature();
            }
            else
            {
                ScriptableRendererData renderer = PipelineUtilities.GetDefaultRenderer();
                Selection.activeObject = renderer;
            }
        }

        #region Context menus
        public static void AddGrassBender(GameObject gameObject)
        {
            if (!gameObject.GetComponent<GrassBender>())
            {
                GrassBender bender = gameObject.AddComponent<GrassBender>();
                bender.OnEnable();
            }
        }
        
        [MenuItem("CONTEXT/MeshFilter/Convert to grass bender")]
        public static void ConvertMeshToBender(MenuCommand cmd)
        {
            MeshFilter mf = (MeshFilter)cmd.context;
            AddGrassBender(mf.gameObject);
        }

        [MenuItem("CONTEXT/TrailRenderer/Convert to grass bender")]
        public static void ConvertTrailToBender(MenuCommand cmd)
        {
            TrailRenderer t = (TrailRenderer)cmd.context;
            AddGrassBender(t.gameObject);
        }

        [MenuItem("CONTEXT/ParticleSystem/Convert to grass bender")]
        public static void ConvertParticleToBender(MenuCommand cmd)
        {
            ParticleSystem ps = (ParticleSystem)cmd.context;
            AddGrassBender(ps.gameObject);
        }
        
        [MenuItem("CONTEXT/LineRenderer/Convert to grass bender")]
        public static void ConvertLineToBender(MenuCommand cmd)
        {
            LineRenderer line = (LineRenderer)cmd.context;
            AddGrassBender(line.gameObject);
        }
        
        [MenuItem("CONTEXT/MeshFilter/Bake Height Vertex Color")]
        public static void BakeHeightIntoMesh(MenuCommand cmd)
        {
            MeshFilter mf = (MeshFilter)cmd.context;

            if (mf.sharedMesh)
            {
                mf.sharedMesh = MeshBaker.BakeHeight(mf.sharedMesh);
            }
        }
        
        [MenuItem("CONTEXT/LODGroup/Bake Height Vertex Color")]
        public static void BakeHeightIntoLODS(MenuCommand cmd)
        {
            LODGroup lodGroup = (LODGroup)cmd.context;

            LOD[] lods = lodGroup.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                LOD lod = lods[i];

                for (int j = 0; j < lod.renderers.Length; j++)
                {
                    MeshFilter mf = lod.renderers[j].GetComponent<MeshFilter>();

                    if (mf && mf.sharedMesh)
                    {
                        mf.sharedMesh = MeshBaker.BakeHeight(mf.sharedMesh);
                    }
                }
            }
        }


        #endregion
    }
}