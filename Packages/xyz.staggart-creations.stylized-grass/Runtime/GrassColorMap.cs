// ⚠️ WARNING: UNAUTHORIZED USE OR DISTRIBUTION IS STRICTLY PROHIBITED
// • Copying, referencing, or reverse-engineering this source code for the creation of new Asset Store or derivative products,
//   or any other publicly distributed content is strictly forbidden and will result in legal action.
// • Studying this file for the purpose of reproducing its functionality in your own assets or tools is not permitted.
// • If you are viewing this file as a reference, please close it immediately to avoid unintentional design influence or potential EULA violations.
// • Uploading this file or any derivative of it to a public GitHub or similar repository will trigger an automated DMCA takedown request.
// • Studying to understand for personal, educational or integration purposes is allowed, studying to reproduce is not.

using UnityEngine;

namespace sc.stylizedgrass.runtime
{
    public class GrassColorMap : ScriptableObject
    {
        public Bounds bounds;
        public Vector4 uv;
        public Texture texture;
        public bool hasScalemap = false;
        
        [Tooltip("When enabled, a custom color map texture can be used")]
        public bool overrideTexture;
        public Texture2D customTex;

        public static GrassColorMap Active;

        private static readonly int _ColorMap = Shader.PropertyToID("_ColorMap"); 
        private static readonly int _ColorMapUV = Shader.PropertyToID("_ColorMapUV"); 
        private static readonly int _ColorMapParams = Shader.PropertyToID("_ColorMapParams");

        public static GrassColorMap CreateNew()
        {
            GrassColorMap newColorMap = ScriptableObject.CreateInstance<GrassColorMap>();
            
            #if UNITY_EDITOR
            string prefix =  UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;
            if (prefix == string.Empty) prefix = "Untitled";
            
            newColorMap.name = $"{prefix}_Colormap";
            #endif
            
            return newColorMap;
        }
        
        public void SetActive()
        {
            if (!texture || (overrideTexture && !customTex)) //Nothing rendered yet
            {
                return;
            }
            
            if ((overrideTexture && !customTex))
            {
                Debug.LogWarning("Tried to activate grass color map with null texture", this);
                return;
            }

            Shader.SetGlobalTexture(_ColorMap, overrideTexture ? customTex : texture);
            
            Shader.SetGlobalVector(_ColorMapUV, uv);
            Shader.SetGlobalVector(_ColorMapParams, new Vector4(1, hasScalemap ? 1 : 0, 0, 0));

            Active = this;
        }

        /// <summary>
        /// Disables sampling of a color map in the grass shader. This must be called when a color map was used, but the current game context no longer has one active
        /// </summary>
        public static void DisableGlobally()
        {
            Shader.SetGlobalTexture(_ColorMap, null);
            Shader.SetGlobalVector(_ColorMapUV, Vector4.zero);
            //Disables sampling of the color/scale map in the shader
            Shader.SetGlobalVector(_ColorMapParams, Vector4.zero);

            Active = null;
        }
    }
}