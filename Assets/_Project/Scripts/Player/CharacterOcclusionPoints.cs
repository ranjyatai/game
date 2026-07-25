using UnityEngine;

public class CharacterOcclusionPoints : MonoBehaviour
{
    [Header("Anchor References")]
    public Transform footAnchor;
    public Transform bodyAnchor;
    public Transform headAnchor;
    public Transform weaponTipAnchor;

    [Header("Auto Find")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private bool autoFindOnValidate = true;

    private void Awake()
    {
        if (autoFindOnAwake)
            AutoFindAnchors();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoFindOnValidate)
            AutoFindAnchors();
    }
#endif

    [ContextMenu("Auto Find Anchors")]
    public void AutoFindAnchors()
    {
        Transform anchorRoot = FindDeepChild(transform, "AnchorRoot");

        if (anchorRoot != null)
        {
            if (footAnchor == null)
                footAnchor = FindDeepChild(anchorRoot, "FootAnchor");

            if (bodyAnchor == null)
                bodyAnchor = FindDeepChild(anchorRoot, "BodyAnchor");

            if (headAnchor == null)
                headAnchor = FindDeepChild(anchorRoot, "HeadAnchor");

            if (weaponTipAnchor == null)
            {
                weaponTipAnchor = FindDeepChild(anchorRoot, "WeaponTip Anchor");
                if (weaponTipAnchor == null)
                    weaponTipAnchor = FindDeepChild(anchorRoot, "WeaponTipAnchor");
            }
        }

        // 兜底：找不到脚锚点时，至少退回自己
        if (footAnchor == null)
            footAnchor = transform;
    }

    private Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null)
            return null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName)
                return t;
        }

        return null;
    }
}