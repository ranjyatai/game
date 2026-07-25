using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局单例，每帧（分批）按距离控制敌人 LOD 睡眠。
/// 自动创建，无需手动放到场景里。
/// </summary>
public class UnitLODManager : MonoBehaviour
{
    [Tooltip("超出此距离的敌人进入睡眠（停止 AI / Spine 更新）")]
    public static float SleepRadius = 40f;

    [Tooltip("每帧最多处理的单位数量，分批避免同帧全量遍历")]
    public static int BatchPerFrame = 10;

    private static UnitLODManager _instance;

    private static readonly List<UnitLODSleepController> _units = new List<UnitLODSleepController>();
    private int _cursor;

    public static void Register(UnitLODSleepController unit)
    {
        if (!_units.Contains(unit)) _units.Add(unit);
        EnsureInstance();
    }

    public static void Unregister(UnitLODSleepController unit)
    {
        _units.Remove(unit);
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        var go = new GameObject("[UnitLODManager]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<UnitLODManager>();
    }

    private void Update()
    {
        if (_units.Count == 0) return;

        Vector3 playerPos = GetPlayerPos();
        float r2 = SleepRadius * SleepRadius;

        int count = Mathf.Min(BatchPerFrame, _units.Count);
        for (int i = 0; i < count; i++)
        {
            _cursor = _cursor % _units.Count;
            UnitLODSleepController unit = _units[_cursor];
            _cursor++;

            if (unit == null) { _units.RemoveAt(_cursor - 1); _cursor--; continue; }

            float dist2 = (unit.transform.position - playerPos).sqrMagnitude;
            unit.SetSleep(dist2 > r2);
        }
    }

    private static Vector3 GetPlayerPos()
    {
        if (SkyPrisonPlayerAuthority.Instance?.CurrentPlayerGameObject != null)
            return SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject.transform.position;
        return Vector3.zero;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
