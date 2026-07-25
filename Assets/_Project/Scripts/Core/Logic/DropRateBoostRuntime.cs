using UnityEngine;

public class DropRateBoostRuntime : MonoBehaviour
{
    public static DropRateBoostRuntime Instance { get; private set; }

    private float _multiplier = 1f;
    private float _remainingSeconds = 0f;

    public static float CurrentMultiplier => Instance != null ? Instance._multiplier : 1f;
    public static bool IsActive => Instance != null && Instance._remainingSeconds > 0f;
    public static float RemainingSeconds => Instance != null ? Instance._remainingSeconds : 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_remainingSeconds > 0f)
        {
            _remainingSeconds -= Time.deltaTime;
            if (_remainingSeconds <= 0f)
            {
                _remainingSeconds = 0f;
                _multiplier = 1f;
            }
        }
    }

    /// <summary>
    /// 激活掉落率加成。若当前已有加成，叠加剩余时间并取较大倍率。
    /// </summary>
    public static void Activate(float multiplier, float duration)
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("[DropRateBoostRuntime]");
            go.AddComponent<DropRateBoostRuntime>();
        }

        if (multiplier > Instance._multiplier)
            Instance._multiplier = multiplier;

        Instance._remainingSeconds += duration;
    }
}
