using UnityEngine;

public class CameraFollowProxy : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float fixedY = 0f;
    [SerializeField] private float xScale = 1f;
    [SerializeField] private float zScale = 1f;

    // 之前这里是原样 1:1 跟随 player.position，没有任何死区——玩家站定不动时，
    // 碰撞体的物理残留速度/待机动画根位移哪怕只有零点几个单位的抖动，都会被
    // 原样传给镜头（再经 xScale/zScale 放大），镜头世界坐标因此一直在微抖，
    // 肉眼看不出来，但下游"镜头是否在动"的判断（比如背包窗口色收差快照的
    // 运动检测）会因此一直判定"在动"，表现为窗口持续闪烁——实测日志证实了
    // 这一点：站定时 DetectMotion 测到的相机位移 Δ 从 0.01 到 2+ 不等。
    // 加一个小死区，玩家真实位移小于这个量时镜头直接沿用上一帧位置，把待机抖动
    // 过滤掉；真正移动时不受影响，因为一旦超过死区就立刻跟上，没有延迟感。
    private const float Deadzone = 0.02f;
    private Vector3 _lastPlayerPos;
    private bool _initialized;

    [Header("跟随平滑")]
    [Tooltip("镜头跟随目标位置的平滑时间——0=瞬间贴上去(跟之前一样)，越大越有延迟/柔和" +
        "跟随感。建议先从0.1~0.2秒试。")]
    [SerializeField] private float followSmoothTime = 0.15f;

    // SmoothDamp跟上面的死区是两回事，但要小心同一个坑：SmoothDamp是指数衰减逼近，
    // 理论上永远到不了精确的目标点，尾巴上会有肉眼看不见但数值上非零的残留位移——
    // 跟这个脚本顶部注释里"背包窗口色收差运动检测把这类残留误判成'镜头在动'导致
    // 持续闪烁"是同一个问题，不做处理的话加平滑跟随就是重新踩一遍这个坑。目标点
    // 已经停止变化(死区在过滤玩家侧的抖动)且镜头已经逼近到很近时，直接摁到精确值，
    // 把这条尾巴掐掉——跟UnitMovementController.currentVelocity处理同一类问题的
    // 手法一致。
    private Vector3 _smoothedPos;
    private Vector3 _smoothVelocity;
    private bool _smoothInitialized;

    private void LateUpdate()
    {
        if (player == null) return;

        Vector3 p = player.position;

        if (!_initialized || (p - _lastPlayerPos).sqrMagnitude > Deadzone * Deadzone)
        {
            _lastPlayerPos = p;
            _initialized = true;
        }

        Vector3 targetPos = new Vector3(
            _lastPlayerPos.x * xScale,
            fixedY,
            _lastPlayerPos.y * zScale
        );

        if (!_smoothInitialized)
        {
            _smoothedPos = targetPos;
            _smoothVelocity = Vector3.zero;
            _smoothInitialized = true;
        }
        else if (followSmoothTime > 0.0001f)
        {
            _smoothedPos = Vector3.SmoothDamp(_smoothedPos, targetPos, ref _smoothVelocity, followSmoothTime);

            if ((_smoothedPos - targetPos).sqrMagnitude < 0.0001f && _smoothVelocity.sqrMagnitude < 0.0004f)
            {
                _smoothedPos = targetPos;
                _smoothVelocity = Vector3.zero;
            }
        }
        else
        {
            _smoothedPos = targetPos;
            _smoothVelocity = Vector3.zero;
        }

        transform.position = _smoothedPos + SkyPrisonScreenShake.CurrentOffset;
    }
}