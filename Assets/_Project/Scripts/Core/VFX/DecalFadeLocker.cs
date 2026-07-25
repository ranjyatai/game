using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 挂在 DecalProjector 所在的 GameObject 上，每帧强制 fadeFactor = 1，
/// 防止预制体内任何动画/脚本将血迹淡出。
/// </summary>
[DisallowMultipleComponent]
public class DecalFadeLocker : MonoBehaviour
{
    private DecalProjector _projector;

    private void Awake()
    {
        _projector = GetComponent<DecalProjector>()
                  ?? GetComponentInChildren<DecalProjector>(true);
    }

    private void LateUpdate()
    {
        if (_projector != null)
            _projector.fadeFactor = 1f;
    }
}
