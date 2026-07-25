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
using UnityEngine.Rendering;
 
namespace sc.stylizedgrass.runtime
{
    [ExecuteInEditMode]
    [AddComponentMenu("Stylized Grass/Stylized Grass Wind Controller")]
    public class WindController : MonoBehaviour
    {
        public static WindController Instance;
        
        [Tooltip("When enabled the grass Ambient and Gust strength values are multiplied by the WindZone's Main value")]
        public WindZone windZone;
        
        [Tooltip("Acts as a multiplier for the WindZone's \"Main\" parameter. Use this to account for any discrepancies with other wind-enabled shaders.")]
        public float windAmbientMultiplier = 1f;
        [Tooltip("Acts as a multiplier for the WindZone's \"Turbulence\" parameter. Use this to account for any discrepancies with other wind-enabled shaders.")]
        public float windGustMultiplier = 1f;
        
        private static readonly int _GlobalWindParams = Shader.PropertyToID("_GlobalWindParams");
        private static readonly int _GlobalWindDirection = Shader.PropertyToID("_GlobalWindDirection");
        private static readonly int _GlobalWindOffset = Shader.PropertyToID("_GlobalWindOffset");

        private void Reset()
        {
            windZone = GetComponent<WindZone>();
        }

        public void OnEnable()
        {
            Instance = this;
            
            #if UNITY_EDITOR && URP
            if (Application.isPlaying == false)
            {
                if (!PipelineUtilities.RenderFeatureAdded<GrassRenderFeature>())
                {
                    Debug.LogError("The \"Grass Render Feature\" hasn't been added to the render pipeline. Check the inspector for setup instructions", this);
                    UnityEditor.EditorGUIUtility.PingObject(this);
                }
            }
            #endif

            #if URP
            RenderPipelineManager.beginContextRendering += OnBeginRender;
            #endif
        }

        #if URP
        private void OnBeginRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            UpdateWind();
        }
        #endif

        public void OnDisable()
        {
            Instance = null;
            
            #if URP
            RenderPipelineManager.beginContextRendering -= OnBeginRender;
            #endif

            //Disable usage of Wind Zone parameters
            Shader.SetGlobalVector(_GlobalWindParams, Vector4.zero);
        }
     
        public static void SetWindZone(WindZone windZone)
        {
            if (!Instance)
            {
                Debug.LogWarning("Tried to set Stylized Grass wind zone, but no Wind Controller instance is present");
                return;
            }
 
            Instance.windZone = windZone;
        }
 
        private double lastFrameTime;
        private double timeOffset;
        private Vector3 windDirection;
        private Vector3 gustOffset;
        private static Vector4 globalWindParams;

        private float m_windMain;
        private float m_windTurbulence;
        
        private void UpdateWind()
        {
            if (windZone && windZone.gameObject.activeInHierarchy)
            {
                //This is used as a boolean in the wind shader function, to indicate that the values passed on here should be used.
                globalWindParams.w = 1;
                
                double deltaTime = Time.time - lastFrameTime;
                lastFrameTime = Time.time;

                //Apply normalization multipliers
                m_windMain = windZone.windMain * windAmbientMultiplier;
                m_windTurbulence = windZone.windTurbulence * windGustMultiplier;
                
                timeOffset += deltaTime * (double)m_windMain * Time.timeScale;

                globalWindParams.x = (float)timeOffset;
                //Not allowing negative values, as this reverses the direction
                globalWindParams.y = Mathf.Max(0, m_windMain);
                globalWindParams.z = Mathf.Max(0, m_windTurbulence);

                Shader.SetGlobalVector(_GlobalWindParams, globalWindParams);
                
                windDirection = windZone.transform.rotation * Vector3.forward;
                Shader.SetGlobalVector(_GlobalWindDirection, windDirection);
                
                gustOffset += windDirection * (m_windTurbulence * (float)deltaTime * Time.timeScale); 
                Shader.SetGlobalVector(_GlobalWindOffset, gustOffset);
            }
            else
            {
                //When the .W component is 0, the shader uses material parameters to control wind
                Shader.SetGlobalVector(_GlobalWindParams, Vector4.zero);
            }
        }

        private void OnDrawGizmosSelected()
        {
            #if URP
            if (windZone && windZone.gameObject.activeInHierarchy)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(windZone.transform.position, windZone.transform.position + (windDirection * 5f));
            }
            #endif
        }
    }
}