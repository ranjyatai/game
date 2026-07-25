using Spine.Unity;
using UnityEngine;

/// <summary>
/// 挂在每个敌人单位根对象上，由 UnitLODManager 统一驱动。
/// 超出激活半径时冻结 AI、Spine 更新；进入半径时恢复。
/// </summary>
[DisallowMultipleComponent]
public class UnitLODSleepController : MonoBehaviour
{
    private AIBehaviorPackageRuntimeController _ai;
    private SkeletonAnimation                 _spine;
    private UnitMovementController            _movement;
    private bool                              _sleeping;
    private bool                              _initialized;

    private void Awake()
    {
        _ai       = GetComponentInChildren<AIBehaviorPackageRuntimeController>(true);
        _spine    = GetComponentInChildren<SkeletonAnimation>(true);
        _movement = GetComponentInChildren<UnitMovementController>(true);
        _initialized = true;
    }

    private void OnEnable()
    {
        UnitLODManager.Register(this);
    }

    private void OnDisable()
    {
        UnitLODManager.Unregister(this);
        // 对象禁用时确保不留睡眠状态，避免再次启用时状态错误
        if (_sleeping) ApplySleep(false);
    }

    public void SetSleep(bool sleep)
    {
        if (!_initialized || _sleeping == sleep) return;
        _sleeping = sleep;
        ApplySleep(sleep);
    }

    private void ApplySleep(bool sleep)
    {
        if (_ai       != null) _ai.enabled       = !sleep;
        if (_movement != null) _movement.enabled  = !sleep;
        if (_spine    != null) _spine.enabled     = !sleep;
    }
}
