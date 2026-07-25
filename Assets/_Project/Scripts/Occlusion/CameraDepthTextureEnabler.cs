using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ensures the camera asks URP for a depth texture when possible.
/// This uses reflection so the script will not hard-fail if URP internals differ.
/// For a permanent project setting, also enable Depth Texture on the URP asset / Camera.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraDepthTextureEnabler : MonoBehaviour
{
    [SerializeField] private bool enableOnAwake = true;
    [SerializeField] private bool logResult = false;

    private void Awake()
    {
        if (enableOnAwake)
            EnableDepthTexture();
    }

    [ContextMenu("Enable Depth Texture")]
    public void EnableDepthTexture()
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null)
            return;

        cam.depthTextureMode |= DepthTextureMode.Depth;

        bool urpSet = TrySetUniversalAdditionalCameraData(cam);

        if (logResult)
        {
            Debug.Log($"[CameraDepthTextureEnabler] DepthTextureMode.Depth set on {cam.name}. URP additional camera data set={urpSet}", this);
        }
    }

    private bool TrySetUniversalAdditionalCameraData(Camera cam)
    {
        try
        {
            Type extensionType = Type.GetType("UnityEngine.Rendering.Universal.CameraExtensions, Unity.RenderPipelines.Universal.Runtime");
            if (extensionType == null)
                return false;

            MethodInfo getDataMethod = extensionType.GetMethod("GetUniversalAdditionalCameraData", BindingFlags.Public | BindingFlags.Static);
            if (getDataMethod == null)
                return false;

            object data = getDataMethod.Invoke(null, new object[] { cam });
            if (data == null)
                return false;

            Type dataType = data.GetType();

            PropertyInfo requiresDepthTextureProperty = dataType.GetProperty("requiresDepthTexture", BindingFlags.Public | BindingFlags.Instance);
            if (requiresDepthTextureProperty != null && requiresDepthTextureProperty.CanWrite)
            {
                requiresDepthTextureProperty.SetValue(data, true);
                return true;
            }

            FieldInfo requiresDepthTextureField = dataType.GetField("requiresDepthTexture", BindingFlags.Public | BindingFlags.Instance);
            if (requiresDepthTextureField != null)
            {
                requiresDepthTextureField.SetValue(data, true);
                return true;
            }
        }
        catch (Exception ex)
        {
            if (logResult)
                Debug.LogWarning($"[CameraDepthTextureEnabler] Could not set URP additional camera depth texture flag: {ex.Message}", this);
        }

        return false;
    }
}
