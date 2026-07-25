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
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class GrassMaskingSphere : MonoBehaviour
    {
        [Min(0.1f)]
        public float radius = 0.5f;
        public Vector3 offset;
        
        private Vector4 vector;
        private readonly int _PlayerSphereID = Shader.PropertyToID("_PlayerSphere");
        
        public void Update()
        {
            if(enabled) UpdateProperties();
        }

        private void UpdateProperties()
        {
            vector = transform.position + offset;

            //With a value higher than 0, processing also occurs in the shader
            vector.w = radius * transform.lossyScale.magnitude;
            
            Shader.SetGlobalVector(_PlayerSphereID, vector);
        }

        private void OnDisable()
        {
            Shader.SetGlobalVector(_PlayerSphereID, Vector4.zero);
        }

        private void OnDrawGizmosSelected()
        {
            if (!enabled) return;
            
            UpdateProperties();

            Gizmos.DrawWireSphere(vector, vector.w);
        }
    }
}