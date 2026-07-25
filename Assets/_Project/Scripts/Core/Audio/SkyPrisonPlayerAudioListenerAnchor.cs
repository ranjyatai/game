using UnityEngine;

/// <summary>
/// Sky Prison 2.5D runtime audio listener anchor.
///
/// Camera is allowed to stay at a high/oblique 2.5D position for rendering.
/// AudioListener should instead follow the player root so 3D AudioSource attenuation
/// is evaluated from the player's hearing position, not from the camera position.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonPlayerAudioListenerAnchor : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform targetPlayerRoot;
    [SerializeField] private bool autoFindPlayerRoot = true;

    [Header("Follow")]
    [SerializeField] private float listenerHeightOffset = 1.5f;
    [SerializeField] private bool followX = true;
    [SerializeField] private bool followY = true;
    [SerializeField] private bool followZ = true;

    [Header("Listener Safety")]
    [SerializeField] private bool disableOtherAudioListeners = true;
    [SerializeField] private float duplicateListenerCheckInterval = 1.0f;

    private AudioListener ownListener;
    private float nextDuplicateCheckTime;

    public Transform TargetPlayerRoot => targetPlayerRoot;
    public float ListenerHeightOffset => listenerHeightOffset;

    public void Configure(Transform playerRoot, float heightOffset, bool disableOthers)
    {
        targetPlayerRoot = playerRoot;
        listenerHeightOffset = heightOffset;
        disableOtherAudioListeners = disableOthers;
        EnsureOwnListener();
        ApplyFollowImmediate();
        DisableOtherListenersIfNeeded();
    }

    private void Awake()
    {
        EnsureOwnListener();
        ResolveTargetIfNeeded();
        ApplyFollowImmediate();
        DisableOtherListenersIfNeeded();
    }

    private void OnEnable()
    {
        EnsureOwnListener();
        ResolveTargetIfNeeded();
        ApplyFollowImmediate();
        DisableOtherListenersIfNeeded();
    }

    private void LateUpdate()
    {
        ResolveTargetIfNeeded();
        ApplyFollowImmediate();

        if (disableOtherAudioListeners && Time.unscaledTime >= nextDuplicateCheckTime)
        {
            nextDuplicateCheckTime = Time.unscaledTime + Mathf.Max(0.2f, duplicateListenerCheckInterval);
            DisableOtherListenersIfNeeded();
        }
    }

    private void EnsureOwnListener()
    {
        if (ownListener == null)
            ownListener = GetComponent<AudioListener>();

        if (ownListener == null)
            ownListener = gameObject.AddComponent<AudioListener>();

        if (!ownListener.enabled)
            ownListener.enabled = true;
    }

    private void ResolveTargetIfNeeded()
    {
        if (!autoFindPlayerRoot || targetPlayerRoot != null)
            return;

        targetPlayerRoot = FindBestPlayerRoot();
    }

    private void ApplyFollowImmediate()
    {
        if (targetPlayerRoot == null)
            return;

        Vector3 current = transform.position;
        Vector3 target = targetPlayerRoot.position;
        target.y += listenerHeightOffset;

        transform.position = new Vector3(
            followX ? target.x : current.x,
            followY ? target.y : current.y,
            followZ ? target.z : current.z);
    }

    private void DisableOtherListenersIfNeeded()
    {
        if (!disableOtherAudioListeners)
            return;

        EnsureOwnListener();

        AudioListener[] listeners = FindObjectsOfType<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || listener == ownListener)
                continue;

            // 之前这里只是 listener.enabled = false——组件还留着，只是禁用。这本该
            // 没问题，但项目里反复出现"实际组件从头到尾只有2个，Unity引擎自己那条
            // 监听器数量警告却一路往上滚到几十上百"的情况，怎么查都只有这2个真实
            // 组件，唯一还没试过的可能：Unity引擎内部的监听器注册表，对"disable"
            // 这个状态的处理不像禁用其它组件那样干净利落，反复禁用/重新启用同一个
            // 组件可能没有真正从引擎那份内部登记里退出过。直接销毁这个组件而不是
            // 只关掉它，才是真正意义上的"退出监听器登记"，不留任何暧昧状态。
            Destroy(listener);
        }

        if (ownListener != null && !ownListener.enabled)
            ownListener.enabled = true;
    }

    private static Transform FindBestPlayerRoot()
    {
        GameObject tagged = null;
        try
        {
            tagged = GameObject.FindGameObjectWithTag("Player");
        }
        catch
        {
            tagged = null;
        }

        if (tagged != null)
            return tagged.transform;

        GameObject exact = GameObject.Find("Player");
        if (exact != null)
            return exact.transform;

        Transform[] transforms = FindObjectsOfType<Transform>(true);
        Transform best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null)
                continue;

            int score = ScorePlayerCandidate(t);
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static int ScorePlayerCandidate(Transform t)
    {
        string path = GetHierarchyPath(t);
        string name = t.name;
        int score = 0;

        if (name == "Player") score += 1000;
        if (name.Contains("Player")) score += 200;
        if (path.Contains("PlayerRoot")) score += 300;
        if (path.Contains("PlayerRuntime")) score += 300;
        if (t.GetComponent("UnitMovementController") != null) score += 120;
        if (t.GetComponent("UnitDefinitionRuntimeBinder") != null) score += 120;
        if (t.GetComponentInChildren<Spine.Unity.SkeletonAnimation>(true) != null) score += 40;

        if (path.Contains("EnemyRoot") || path.Contains("EnemyRuntime")) score -= 1000;
        if (name.Contains("Enemy")) score -= 1000;
        if (name.Contains("Camera")) score -= 500;
        if (name.Contains("AudioListenerRoot")) score -= 500;

        return score;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
