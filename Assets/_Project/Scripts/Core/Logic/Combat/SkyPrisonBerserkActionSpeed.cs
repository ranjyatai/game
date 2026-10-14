using Spine.Unity;
using UnityEngine;

/// <summary>
/// 狂暴的「行动速度↑」部分：状态系统只能改属性(攻击/防御/移速)，攻速字段 atkSpeed 目前没有接进出招流程，
/// 所以行动速度用「Spine 整体播放速度」实现——怪物的出招判定、收招都挂在 Spine 事件 / 动画播完上，
/// 动画整体变快，出招节奏就一起变快，判定帧不会和画面错开。
/// 由颂赞者施加狂暴时自动挂到目标身上；状态结束就把播放速度还原并移除自己。
/// （代码驱动的怪物——织面者的冲刺前摇、咏唱者的蓄势——是自己的计时器，不受这里影响，只吃到攻击/防御/移速。）
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonBerserkActionSpeed : MonoBehaviour
{
    public const string StatusKey = "status_berserk";

    private UnitStatusController _status;
    private SkeletonAnimation _skeleton;
    private float _multiplier = 1.25f;
    private float _baseTimeScale = 1f;
    private bool _applied;

    public static void EnsureOn(GameObject unitRoot, float multiplier)
    {
        if (unitRoot == null) return;
        var c = unitRoot.GetComponent<SkyPrisonBerserkActionSpeed>();
        if (c == null) c = unitRoot.AddComponent<SkyPrisonBerserkActionSpeed>();
        c._multiplier = Mathf.Max(1f, multiplier);
    }

    private void Update()
    {
        if (_status == null) _status = GetComponentInChildren<UnitStatusController>(true);
        if (_skeleton == null) _skeleton = GetComponentInChildren<SkeletonAnimation>(true);
        bool on = _status != null && _status.HasStatus(StatusKey);

        if (on && _skeleton != null)
        {
            if (!_applied) { _baseTimeScale = _skeleton.timeScale > 0f ? _skeleton.timeScale : 1f; _applied = true; }
            _skeleton.timeScale = _baseTimeScale * _multiplier;
        }
        else if (!on)
        {
            Restore();
            Destroy(this);
        }
    }

    private void Restore()
    {
        if (_applied && _skeleton != null) _skeleton.timeScale = _baseTimeScale;
        _applied = false;
    }

    private void OnDestroy() { Restore(); }
}
