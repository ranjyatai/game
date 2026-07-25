using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_2023_1_OR_NEWER
using UnityEngine.SceneManagement;
#endif

/// <summary>
/// V1 - 2026-05-20 - Outline Camera No Display Quarantine
///
/// Goal:
/// Prevent any legacy OutlineCamera_* camera from rendering directly to Display.
///
/// Why:
/// An outline camera with Target Texture = None + black clear color behaves like a normal camera
/// and can cover the whole Game view with black. This guard quarantines such cameras before render.
///
/// Safe scope:
/// - Does not touch gameplay cameras.
/// - Does not touch cameras whose name does not start with "OutlineCamera_".
/// - Does not change occlusion trigger / BackTrigger logic.
/// - Does not rewrite overlay materials.
/// </summary>
[DefaultExecutionOrder(-32000)]
public sealed class SkyPrisonOutlineCameraDisplayGuard : MonoBehaviour
{
    private const string VersionText = "V1 - 2026-05-20 - Outline Camera No Display Quarantine";

    private static SkyPrisonOutlineCameraDisplayGuard instance;
    private static RenderTexture quarantineTexture;

    [Header("Version")]
    [SerializeField] private string scriptVersion = VersionText;
    [SerializeField] private int compileTouchVersion = 2026052014;

    [Header("Guard")]
    [SerializeField] private bool guardEnabled = true;
    [SerializeField] private bool assignQuarantineTexture = true;
    [SerializeField] private bool disableCameraWhenQuarantined = true;
    [SerializeField] private bool forceTransparentClear = true;
    [SerializeField] private bool runEveryLateUpdate = true;

    [Header("Name Filter")]
    [SerializeField] private string requiredNamePrefix = "OutlineCamera_";
    [SerializeField] private bool includeInactiveCameras = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private int debugScannedCameraCount = 0;
    [SerializeField] private int debugProtectedCameraCount = 0;
    [SerializeField] private string debugProtectedCameraPaths = string.Empty;
    [SerializeField] private string debugLastRenderCameraName = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreateBeforeSceneLoad()
    {
        EnsureInstance();
    }

    public static SkyPrisonOutlineCameraDisplayGuard EnsureInstance()
    {
        if (instance != null)
            return instance;

#if UNITY_2023_1_OR_NEWER
        SkyPrisonOutlineCameraDisplayGuard found = UnityEngine.Object.FindFirstObjectByType<SkyPrisonOutlineCameraDisplayGuard>(FindObjectsInactive.Include);
#else
        SkyPrisonOutlineCameraDisplayGuard found = UnityEngine.Object.FindObjectOfType<SkyPrisonOutlineCameraDisplayGuard>(true);
#endif
        if (found != null)
        {
            instance = found;
            return instance;
        }

        GameObject go = new GameObject("__SkyPrisonOutlineCameraDisplayGuard_Runtime");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SkyPrisonOutlineCameraDisplayGuard>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        ForceVersionStamp();
        EnsureQuarantineTexture();
        ProtectNow("Awake");
    }

    private void OnEnable()
    {
        ForceVersionStamp();
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        ProtectNow("OnEnable");
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private void LateUpdate()
    {
        if (runEveryLateUpdate)
            ProtectNow("LateUpdate");
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (!guardEnabled || cam == null)
            return;

        debugLastRenderCameraName = cam.name;

        if (!IsTargetOutlineCamera(cam))
            return;

        if (cam.targetTexture == null)
        {
            QuarantineCamera(cam, "beginCameraRendering");
        }
        else if (forceTransparentClear)
        {
            ForceTransparentCameraClear(cam);
        }
    }

    public void ProtectNow(string reason = "manual")
    {
        debugScannedCameraCount = 0;
        debugProtectedCameraCount = 0;
        debugProtectedCameraPaths = string.Empty;

        if (!guardEnabled)
            return;

        EnsureQuarantineTexture();

#if UNITY_2023_1_OR_NEWER
        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
            includeInactiveCameras ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#else
        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>(includeInactiveCameras);
#endif

        StringBuilder sb = null;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null)
                continue;

            if (!IsTargetOutlineCamera(cam))
                continue;

            debugScannedCameraCount++;

            if (forceTransparentClear)
                ForceTransparentCameraClear(cam);

            if (cam.targetTexture == null)
            {
                QuarantineCamera(cam, reason);
                debugProtectedCameraCount++;

                if (sb == null)
                    sb = new StringBuilder(512);
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(GetPath(cam.transform));
            }
        }

        if (sb != null)
            debugProtectedCameraPaths = sb.ToString();
    }

    private void QuarantineCamera(Camera cam, string reason)
    {
        if (cam == null)
            return;

        EnsureQuarantineTexture();

        if (forceTransparentClear)
            ForceTransparentCameraClear(cam);

        if (assignQuarantineTexture && quarantineTexture != null)
            cam.targetTexture = quarantineTexture;

        if (disableCameraWhenQuarantined)
            cam.enabled = false;

        if (debugLogs)
        {
            Debug.Log($"[SkyPrisonOutlineCameraDisplayGuard] Quarantined {GetPath(cam.transform)} reason={reason}", cam);
        }
    }

    private void ForceTransparentCameraClear(Camera cam)
    {
        if (cam == null)
            return;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
    }

    private bool IsTargetOutlineCamera(Camera cam)
    {
        if (cam == null)
            return false;

        string n = cam.name;
        if (string.IsNullOrEmpty(n))
            return false;

        if (!string.IsNullOrEmpty(requiredNamePrefix) && n.StartsWith(requiredNamePrefix, StringComparison.Ordinal))
            return true;

        return false;
    }

    private static void EnsureQuarantineTexture()
    {
        if (quarantineTexture != null)
            return;

        quarantineTexture = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32)
        {
            name = "RT_OutlineCamera_DisplayQuarantine_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        quarantineTexture.Create();
    }

    private void ForceVersionStamp()
    {
        scriptVersion = VersionText;
        compileTouchVersion = 2026052014;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return string.Empty;

        StringBuilder sb = new StringBuilder(t.name);
        Transform p = t.parent;
        while (p != null)
        {
            sb.Insert(0, p.name + "/");
            p = p.parent;
        }
        return sb.ToString();
    }
}
