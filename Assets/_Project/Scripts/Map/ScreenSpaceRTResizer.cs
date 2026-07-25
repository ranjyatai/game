using UnityEngine;

public class ScreenSpaceRTResizer : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private Camera[] targetCameras;

    [Header("RenderTextures")]
    [SerializeField] private RenderTexture[] targetRTs;

    [Header("Options")]
    [SerializeField] private bool useScreenSize = true;
    [SerializeField] private int overrideWidth = 1920;
    [SerializeField] private int overrideHeight = 1080;
    [SerializeField] private bool logChanges = false;

    private int lastWidth = -1;
    private int lastHeight = -1;

    private void Start()
    {
        ResizeIfNeeded(force: true);
    }

    private void Update()
    {
        ResizeIfNeeded(force: false);
    }

    private void ResizeIfNeeded(bool force)
    {
        int width = useScreenSize ? Screen.width : overrideWidth;
        int height = useScreenSize ? Screen.height : overrideHeight;

        if (!force && width == lastWidth && height == lastHeight)
            return;

        lastWidth = width;
        lastHeight = height;

        for (int i = 0; i < targetRTs.Length; i++)
        {
            var rt = targetRTs[i];
            if (rt == null) continue;

            if (rt.width == width && rt.height == height)
                continue;

            rt.Release();
            rt.width = width;
            rt.height = height;
            rt.Create();

            if (logChanges)
                Debug.Log($"[ScreenSpaceRTResizer] Resized RT: {rt.name} -> {width}x{height}", this);
        }

        for (int i = 0; i < targetCameras.Length; i++)
        {
            var cam = targetCameras[i];
            if (cam == null) continue;

            // 重新指定一次，确保相机继续输出到已重建的 RT
            for (int j = 0; j < targetRTs.Length; j++)
            {
                if (cam.targetTexture == targetRTs[j])
                {
                    cam.targetTexture = null;
                    cam.targetTexture = targetRTs[j];
                    break;
                }
            }
        }
    }
}