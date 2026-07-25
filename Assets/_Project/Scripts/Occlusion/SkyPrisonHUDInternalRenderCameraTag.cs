using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Marker for HUD module internal render cameras.
    /// 2.5D occlusion / outline renderer features must skip cameras with this tag.
    /// This component has no runtime behavior; it only provides an explicit compatibility boundary.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonHUDInternalRenderCameraTag : MonoBehaviour
    {
    }
}
