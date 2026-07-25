using UnityEngine;

public class CameraFacingBillboard : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool autoFindMainCamera = true;

    private void Awake()
    {
        TryResolveCamera();
    }

    private void OnEnable()
    {
        TryResolveCamera();
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            if (!autoFindMainCamera)
                return;

            TryResolveCamera();
            if (targetCamera == null)
                return;
        }

        Vector3 camForward = targetCamera.transform.forward;

        // 让角色平面朝向镜头
        transform.forward = camForward;
    }

    private void TryResolveCamera()
    {
        if (targetCamera != null)
            return;

        targetCamera = Camera.main;
    }

    public void SetTargetCamera(Camera cam)
    {
        targetCamera = cam;
    }

    public void ClearTargetCamera()
    {
        targetCamera = null;
    }
}