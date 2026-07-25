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
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace sc.stylizedgrass.runtime
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "StylizedGrass", "sc.stylizedgrass.runtime", "GrassBendingFeature")]
    public class GrassRenderFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Returns the render feature instance on the default renderer. Null if not found.
        /// </summary>
        /// <returns></returns>
        public static GrassRenderFeature GetDefault()
        {
            return (GrassRenderFeature)PipelineUtilities.GetRenderFeature<GrassRenderFeature>();
        }
        
        [Serializable]
        public class Settings
        {
            public bool enableBending = true;
            public bool debugBending = false;
            [Min(10f)] public float bendingRenderRange = 50f;
            
            [Tooltip("Use the camera's forward direction for the perspective correction feature." +
                     "\n\nIf disabled, grass is bent away from the camera's position instead")]
            public bool forwardPerspectiveCorrection = true;
            [Tooltip("Fade the grass at the maximum rendering range of the terrains \"Detail Distance\". Prevents vegetation from popping at the threshold.")]
            public bool fadeAtMaxRenderingRange;
            
            [Tooltip("Texture used for the Distance/Angle fading shading feature.")]
            public Texture2D ditheringNoise;

            [Tooltip("For the Distance/Angle fading feature, animate the dithering pattern when Temporal Anti-Aliasing (TAA) is enabled. This tends to cause TAA to smooth out the noise, emulating true transparency." +
                     "\n\nHas no effect in the scene view.")]
            public bool animateDithering = true;
            
            [Tooltip("Do not execute this render feature for the scene-view camera. Helps to inspect the world while everything is rendering from the main camera's perspective")]
            public bool ignoreSceneView;
            [Tooltip("Do not execute this render feature for overlay camera's. Doing so has no practical effect (but does incur a minor performance hit), unless grass is actually rendered on one.")]
            public bool ignoreOverlayCamera = true;
        }

        [SerializeField] [HideInInspector]
        //Reference it, so that it's included in a build
        public Shader bendingShader;

        public Settings settings = new Settings();
        
        private void Reset()
        {
            VerifyReferences();
        }

        public void VerifyReferences()
        {
            bendingShader = Shader.Find(GrassBender.BEND_SHADER_NAME);
            
#if UNITY_EDITOR
            settings.ditheringNoise = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath("81200413a40918d4d8702e94db29911c"));
#endif
        }
        
        private SetupConstants constantsSetupPass;
        private BendVectorPass bendingVectorPass;
        private BendVectorPass.DebugPass debugPass;

        void OnEnable()
        {
            #if UNITY_6000_0_OR_NEWER && !UNITY_6000_3_OR_NEWER && UNITY_EDITOR
            if (PipelineUtilities.RenderGraphEnabled() == false)
            {
                Debug.LogError($"[{this.name}] Render Graph is disabled but required. Disable \"Compatibility Mode\" in your project's Graphics Settings.");
            }
            #endif
        }
        
        public override void Create()
        {
            constantsSetupPass = new SetupConstants(ref settings)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingShadows
            };

            if (settings.enableBending && GrassBender.Instances.Count > 0)
            {
                bendingVectorPass = new BendVectorPass(ref settings)
                {
                    //Still needs to be executing here, unable to properly restore the view/projection matrix.
                    renderPassEvent = RenderPassEvent.BeforeRendering
                };
                
                if (settings.debugBending)
                {
                    debugPass = new BendVectorPass.DebugPass()
                    {
                        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
                    };
                }
            }
            else
            {
                BendVectorPass.DisableBendingShaderPath();
            }
        }

        private void OnDisable()
        {
            bendingVectorPass?.Dispose();
            constantsSetupPass?.Dispose();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var currentCam = renderingData.cameraData.camera;
            
            //Skip for any special use camera's (except scene view camera)
            if (currentCam.cameraType != CameraType.SceneView && (currentCam.cameraType == CameraType.Reflection || currentCam.cameraType == CameraType.Preview || currentCam.hideFlags != HideFlags.None)) return;

            //Skip overlay cameras
            if (settings.ignoreOverlayCamera && renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            
            #if UNITY_EDITOR
            if (settings.ignoreSceneView && currentCam.cameraType == CameraType.SceneView) return;
            #endif

            renderer.EnqueuePass(constantsSetupPass);
            if (settings.enableBending && GrassBender.Instances.Count > 0 && bendingVectorPass != null)
            {
                renderer.EnqueuePass(bendingVectorPass);

                if (settings.debugBending) renderer.EnqueuePass(debugPass);
            }
        }

        [Obsolete("As of version 2.0.0+ this no longer exists. It it kept around to avoid a compile error when upgrading to Version 2. Delete any scripts that use this.")]
        public class RenderBendVectors
        {
            public static int CurrentResolution;
            public static void DrawOrthographicViewGizmo() {}
        }
    }
}