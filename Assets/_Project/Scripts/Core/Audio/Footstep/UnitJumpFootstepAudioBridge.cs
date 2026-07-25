using UnityEngine;

/// <summary>
/// 轮询 UnitMovementController 跳跃状态，在起跳和落地瞬间通过 UnitFootstepAudioEmitter
/// 触发 jumpstep_up / jumpstep_down 音轨播放。
/// 挂在角色根对象上，由 UnitDefinitionRuntimeApplier 自动添加。
/// </summary>
[DisallowMultipleComponent]
public class UnitJumpFootstepAudioBridge : MonoBehaviour
{
    [SerializeField] private UnitMovementController movementController;
    [SerializeField] private UnitFootstepAudioEmitter footstepEmitter;

    private UnitMovementController.JumpRuntimeState _lastState = UnitMovementController.JumpRuntimeState.None;

    private void Awake()
    {
        AutoResolve();
    }

    private void OnEnable()
    {
        AutoResolve();
    }

    private void AutoResolve()
    {
        if (movementController == null)
            movementController = GetComponent<UnitMovementController>()
                              ?? GetComponentInParent<UnitMovementController>();

        if (footstepEmitter == null)
            footstepEmitter = GetComponent<UnitFootstepAudioEmitter>()
                           ?? GetComponentInParent<UnitFootstepAudioEmitter>()
                           ?? GetComponentInChildren<UnitFootstepAudioEmitter>(true);
    }

    private void Update()
    {
        if (movementController == null || footstepEmitter == null)
            return;

        UnitMovementController.JumpRuntimeState current = movementController.CurrentJumpRuntimeState;

        if (current != _lastState)
        {
            // 起跳：进入 Start 阶段
            if (current == UnitMovementController.JumpRuntimeState.Start)
                footstepEmitter.PlayFootstep(UnitFootstepAudioEmitter.FootstepEventKind.JumpUp);

            // 落地：进入 Land 阶段
            if (current == UnitMovementController.JumpRuntimeState.Land)
                footstepEmitter.PlayFootstep(UnitFootstepAudioEmitter.FootstepEventKind.Land);

            _lastState = current;
        }
    }

    public void Configure(UnitMovementController controller, UnitFootstepAudioEmitter emitter)
    {
        movementController = controller;
        footstepEmitter    = emitter;
    }
}
