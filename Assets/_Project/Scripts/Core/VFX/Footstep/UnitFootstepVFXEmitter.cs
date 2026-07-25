using UnityEngine;

/// <summary>
/// 奔跑/跳跃扬尘触发器。挂在单位根节点，监听 UnitFootstepAudioEmitter 已有的
/// FootstepNoiseEmitted 事件（跟音效同一个触发源，天然跟脚步/起跳/落地节奏对齐，
/// 不用再单独订阅 Spine 事件或轮询跳跃状态）。
/// 只在"奔跑"这种运动状态下的脚步才出扬尘（走路/潜行不出），起跳/落地不管当时是
/// 走路还是奔跑都会出。
/// 由 UnitDefinitionRuntimeApplier 自动添加。
/// </summary>
[DisallowMultipleComponent]
public class UnitFootstepVFXEmitter : MonoBehaviour
{
    [SerializeField] private UnitFootstepAudioEmitter footstepEmitter;

    [Header("参数")]
    [Tooltip("左右脚扬尘的横向偏移（世界单位），做出脚印左右交替的观感，不是死钉在角色正中心。")]
    [SerializeField] private float footOffsetX = 0.15f;

    [Tooltip("扬尘特效的基础缩放。素材是按真实人体比例做的，chibi角色大概率需要调，先用1测，不对再调。")]
    [SerializeField] private float dustScale = 1f;

    [Tooltip("奔跑扬尘相对基础缩放的额外倍率（默认调大一点，普通脚步扬尘偏小不容易注意到）。")]
    [SerializeField] private float runDustScaleMultiplier = 1.8f;

    [Tooltip("跳跃/落地扬尘相对基础缩放的额外倍率（起跳/落地通常比普通脚步动静更大）。")]
    [SerializeField] private float jumpDustScaleMultiplier = 1.4f;

    [Tooltip("扬尘特效存在时长（秒），播完自动回收。")]
    [SerializeField] private float dustLifetime = 3f;

    private bool _subscribed;

    private void Awake()
    {
        AutoSetup();
    }

    private void OnEnable()
    {
        AutoSetup();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (footstepEmitter == null)
            footstepEmitter = GetComponent<UnitFootstepAudioEmitter>()
                           ?? GetComponentInParent<UnitFootstepAudioEmitter>()
                           ?? GetComponentInChildren<UnitFootstepAudioEmitter>(true);
    }

    public void Configure(UnitFootstepAudioEmitter emitter)
    {
        Unsubscribe();
        footstepEmitter = emitter;
        Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed || footstepEmitter == null)
            return;

        footstepEmitter.FootstepNoiseEmitted += HandleFootstepNoise;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        if (footstepEmitter != null)
            footstepEmitter.FootstepNoiseEmitted -= HandleFootstepNoise;
        _subscribed = false;
    }

    private void HandleFootstepNoise(UnitFootstepAudioEmitter.FootstepNoiseEvent evt)
    {
        if (FootstepVFXManager.Instance == null)
            return;

        switch (evt.kind)
        {
            case UnitFootstepAudioEmitter.FootstepEventKind.Left:
                if (evt.motion == UnitFootstepAudioEmitter.FootstepMotionKind.Run)
                    SpawnRunDust(evt.position, -footOffsetX);
                break;

            case UnitFootstepAudioEmitter.FootstepEventKind.Right:
                if (evt.motion == UnitFootstepAudioEmitter.FootstepMotionKind.Run)
                    SpawnRunDust(evt.position, footOffsetX);
                break;

            case UnitFootstepAudioEmitter.FootstepEventKind.JumpUp:
                SpawnBurstDust(FootstepVFXManager.Instance.JumpDustPrefabs, evt.position);
                break;

            case UnitFootstepAudioEmitter.FootstepEventKind.Land:
                SpawnBurstDust(FootstepVFXManager.Instance.LandDustPrefabs, evt.position);
                break;
        }
    }

    private void SpawnRunDust(Vector3 sourcePosition, float lateralOffset)
    {
        Vector3 spawnPos = ResolveGroundPosition(sourcePosition) + new Vector3(lateralOffset, 0f, 0f);
        FootstepVFXManager.Instance.SpawnDust(
            FootstepVFXManager.Instance.RunDustPrefabs,
            spawnPos,
            Quaternion.identity,
            dustScale * runDustScaleMultiplier,
            GetUnitSortingOrder(),
            dustLifetime);
    }

    private void SpawnBurstDust(GameObject[] pool, Vector3 sourcePosition)
    {
        Vector3 spawnPos = ResolveGroundPosition(sourcePosition);
        FootstepVFXManager.Instance.SpawnDust(
            pool,
            spawnPos,
            Quaternion.identity,
            dustScale * jumpDustScaleMultiplier,
            GetUnitSortingOrder(),
            dustLifetime);
    }

    // 脚步声播放点不一定贴地（可能挂在角色身上某个固定高度），扬尘要贴地才对——
    // 用这个单位根节点自己的世界Y当地面高度，X/Z仍然用事件带过来的声源位置
    // （更贴近实际脚步落点，尤其是移动速度快时跟音效的位置差不会太明显）。
    private Vector3 ResolveGroundPosition(Vector3 sourcePosition)
    {
        return new Vector3(sourcePosition.x, transform.position.y, sourcePosition.z);
    }

    private int GetUnitSortingOrder()
    {
        // 跟 BloodVFXManager.GetUnitSortingOrder 同一套惯例：Z * 100。
        return Mathf.RoundToInt(transform.position.z * 100f);
    }
}
